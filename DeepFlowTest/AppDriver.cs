namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

public sealed class AppDriver : IDisposable
{
	private readonly object elementCacheSync = new();
	private readonly Dictionary<string, List<WeakReference<Element>>> elementCache = new(StringComparer.Ordinal);
	private bool disposed;

	private AppDriver(
		AppConnection connection,
		AppDriverOptions options,
		Func<AppConnection, AppDriverOptions, IAppDriverCommandSession>? sessionFactory = null,
		IAppDriverCommandSession? session = null)
	{
		Connection = connection ?? throw new ArgumentNullException(nameof(connection));
		Options = options ?? throw new ArgumentNullException(nameof(options));
		Session = session ?? (sessionFactory ?? ((candidateConnection, candidateOptions) => new NamedPipeAppDriverCommandSession(candidateConnection, candidateOptions)))(connection, options);
	}

	public string ProductName => ProductInfo.Name;

	public AppConnection Connection { get; }

	public AppDriverOptions Options { get; }

	public IAppDriverCommandSession Session { get; }

	public Keyboard Keyboard => new(this);

	public static AppDriver Launch(string executablePath) =>
		new AppDriverFactory().Launch(executablePath);

	public static AppDriver Launch(string executablePath, string? args) =>
		new AppDriverFactory().Launch(executablePath, args);

	public static AppDriver Launch(ProcessStartInfo processStartInfo)
		=> new AppDriverFactory().Launch(processStartInfo);

	public static AppDriver Launch(string executablePath, AppDriverLaunchOptions options)
	{
		return new AppDriverFactory().Launch(executablePath, options);
	}

	public static AppDriver AttachTo(int processId, AppDriverAttachOptions? options = null)
	{
		return new AppDriverFactory().AttachTo(processId, options);
	}

	public static AppDriver AttachTo(string processName, AppDriverAttachOptions? options = null)
	{
		return new AppDriverFactory().AttachTo(processName, options);
	}

	public static AppDriver CreateForTests(AppConnection connection, IAppDriverCommandSession session, AppDriverOptions? options = null) =>
		new(connection, options ?? new AppDriverOptions(), session: session);

	internal static AppDriver FromConnection(
		AppConnection connection,
		AppDriverOptions options,
		Func<AppConnection, AppDriverOptions, IAppDriverCommandSession> sessionFactory) =>
		new(connection, options, sessionFactory);

	public Element GetElement(ElementSelector selector)
	{
		_ = selector ?? throw new ArgumentNullException(nameof(selector));
		return PollForElement(
			() => FindElements(selector, matcherPayload: null, maxMatches: 2),
			selector.ToString());
	}

	public Element GetElement(Expression<Func<VisualTreeNodeDto, bool>> matcher) =>
		GetElements(matcher, maxMatches: 1).SingleOrDefault()
		?? throw new AppDriverException(AppDriverErrorCodes.TargetNotFound, $"No element matched expression '{matcher}'.");

	public Element GetElement(Expression<Func<Element, bool?>> matcher, int timeoutMs = 30_000) =>
		PollForElement(
			() => FindElements(matcher, maxMatches: 2),
			matcher?.ToString() ?? string.Empty,
			TimeoutFromMilliseconds(timeoutMs));

	public TElement GetElement<TElement>(Expression<Func<Element, bool?>> matcher, int timeoutMs = 30_000)
		where TElement : Element =>
		WrapElement<TElement>(GetElement(matcher, timeoutMs));

	public IReadOnlyList<Element> GetElements(ElementSelector selector, int maxMatches = 100)
	{
		_ = selector ?? throw new ArgumentNullException(nameof(selector));
		return FindElements(selector, matcherPayload: null, maxMatches);
	}

	public IReadOnlyList<Element> GetElements(Expression<Func<VisualTreeNodeDto, bool>> matcher, int maxMatches = 100)
	{
		_ = matcher ?? throw new ArgumentNullException(nameof(matcher));
		var payload = ExpressionPayloadSerializer.Serialize(matcher);
		var predicate = matcher.Compile();
		var repairInfo = new ElementRepairInfo(
			matcher.ToString(),
			payload.ExpressionHash,
			[],
			(node, _) => predicate(node));
		return FindElements(null, payload, maxMatches, repairInfo);
	}

	public IReadOnlyList<Element> GetElements(Expression<Func<Element, bool?>> matcher, int timeoutMs = 30_000)
	{
		_ = matcher ?? throw new ArgumentNullException(nameof(matcher));
		var timeout = TimeoutFromMilliseconds(timeoutMs);
		var stopwatch = Stopwatch.StartNew();
		var delays = new[] { 0 }.Concat(Options.ElementPollBackoffMs ?? []);
		foreach (var delay in delays)
		{
			if (delay > 0)
				Thread.Sleep(delay);

			var matches = FindElements(matcher, maxMatches: 0);
			if (matches.Count != 0)
				return matches;
			if (stopwatch.Elapsed >= timeout)
				break;
		}

		return [];
	}

	public IReadOnlyList<TElement> GetElements<TElement>(Expression<Func<Element, bool?>> matcher, int timeoutMs = 30_000)
		where TElement : Element =>
		GetElements(matcher, timeoutMs)
			.Select(WrapElement<TElement>)
			.ToArray();

	private IReadOnlyList<Element> FindElements(Expression<Func<Element, bool?>> matcher, int maxMatches)
	{
		_ = matcher ?? throw new ArgumentNullException(nameof(matcher));
		var predicate = matcher.Compile();
		ElementRepairInfo? repairInfo = null;
		repairInfo = CreateElementMatcherRepairInfo(matcher, predicate, () => repairInfo);
		var snapshot = GetVisualTree();
		var limit = maxMatches <= 0 ? int.MaxValue : maxMatches;
		return snapshot.Nodes
			.Select(node => Element.FromNode(this, node, snapshot, repairInfo))
			.Where(element => predicate(element) == true)
			.Take(limit)
			.ToArray();
	}

	public TElement GetElement<TElement>(Expression<Func<TElement, bool?>> matcher)
		where TElement : Element =>
		GetElements<TElement>(matcher, maxMatches: 1).SingleOrDefault()
		?? throw new AppDriverException(AppDriverErrorCodes.TargetNotFound, $"No element matched expression '{matcher}'.");

	public IReadOnlyList<TElement> GetElements<TElement>(Expression<Func<TElement, bool?>> matcher, int maxMatches = 100)
		where TElement : Element
	{
		_ = matcher ?? throw new ArgumentNullException(nameof(matcher));
		var predicate = matcher.Compile();
		ElementRepairInfo? repairInfo = null;
		repairInfo = CreateTypedElementMatcherRepairInfo(matcher, predicate, () => repairInfo);
		var snapshot = GetVisualTree();
		return snapshot.Nodes
			.Select(node => WrapElement<TElement>(Element.FromNode(this, node, snapshot, repairInfo)))
			.Where(element => predicate(element) == true)
			.Take(Math.Max(0, maxMatches))
			.ToArray();
	}

	private IReadOnlyList<Element> FindElements(
		ElementSelector? selector,
		ExpressionMatcherPayload? matcherPayload,
		int maxMatches,
		ElementRepairInfo? repairInfo = null)
	{
		var response = Send<FindElementCommandResponse>(new FindElementCommandRequest
		{
			Selector = selector?.ToDto(),
			PropNames = selector?.RequestedPropertyNames,
			MatcherCode = matcherPayload,
			MatcherHash = matcherPayload?.ExpressionHash,
			MaxMatches = maxMatches,
		});

		return response.Matches
			.Select(match => Element.FromMatch(this, match, selector, repairInfo))
			.ToArray();
	}

	private Element PollForElement(Func<IReadOnlyList<Element>> find, string selectorDescription)
		=> PollForElement(find, selectorDescription, Options.Timeout);

	private Element PollForElement(Func<IReadOnlyList<Element>> find, string selectorDescription, TimeSpan timeout)
	{
		var stopwatch = Stopwatch.StartNew();
		var delays = new[] { 0 }.Concat(Options.ElementPollBackoffMs ?? []);
		foreach (var delay in delays)
		{
			if (delay > 0)
				Thread.Sleep(delay);

			var matches = find();
			if (matches.Count == 1)
				return matches[0];
			if (matches.Count > 1)
				throw new AppDriverException(AppDriverErrorCodes.AmbiguousTarget, $"More than one element matched selector '{selectorDescription}'.");
			if (stopwatch.Elapsed >= timeout)
				break;
		}

		throw new AppDriverException(AppDriverErrorCodes.TargetNotFound, $"No element matched selector '{selectorDescription}'.");
	}

	public VisualTreeSnapshot GetVisualTree(string? rootTargetId = null)
	{
		var snapshot = Send<VisualTreeSnapshot>(new GetVisualTreeCommandRequest
		{
			AsSnapshot = true,
			RootTargetId = rootTargetId,
		});
		RefreshCachedElements(snapshot);
		return snapshot;
	}

	public IReadOnlyList<Element> GetRootElements()
	{
		var snapshot = GetVisualTree();
		var byId = snapshot.Nodes.ToDictionary(static node => node.TargetId, StringComparer.Ordinal);
		return snapshot.RootIds
			.Where(byId.ContainsKey)
			.Select(rootId => Element.FromNode(this, byId[rootId], snapshot))
			.ToArray();
	}

	public TResponse Send<TResponse>(IpcCommand command) => Session.Send<TResponse>(command);

	public ScreenshotCommandResponse CaptureScreenshot(string format = "png") =>
		Send<ScreenshotCommandResponse>(new ScreenshotCommandRequest { Format = format });

	public byte[] Screenshot(ImageFormat format = ImageFormat.Jpeg) =>
		DecodeScreenshot(CaptureScreenshot(format.ToProtocolString()));

	public void Screenshot(string fileOutputPath)
	{
		_ = fileOutputPath ?? throw new ArgumentNullException(nameof(fileOutputPath));
		var bytes = Screenshot(GetImageFormatFromPath(fileOutputPath));
		var directory = Path.GetDirectoryName(Path.GetFullPath(fileOutputPath));
		if (!string.IsNullOrEmpty(directory))
			Directory.CreateDirectory(directory);
		File.WriteAllBytes(fileOutputPath, bytes);
	}

	public IDisposable Record(string fileOutputPath, string? windowTitle = null)
	{
		_ = fileOutputPath ?? throw new ArgumentNullException(nameof(fileOutputPath));
		throw new NotSupportedException("Recording requires an FFmpeg packaging decision for DeepFlowTest. Use screenshot streaming until recording resources are packaged.");
	}

	internal Element Repair(Element element)
	{
		_ = element ?? throw new ArgumentNullException(nameof(element));

		if (element.Selector is not null)
		{
			var matches = FindElements(element.Selector, matcherPayload: null, maxMatches: 100);
			if (matches.Count == 1)
				return matches[0];

			if (matches.Count > 1 && TryChooseBestRepairMatch(element, matches, out var bestMatch))
				return bestMatch;

			if (matches.Count > 1)
				throw new AppDriverException(AppDriverErrorCodes.AmbiguousTarget, $"Element '{element.TargetId}' is stale and selector '{element.Selector}' matched multiple replacements.");
		}

		if (element.RepairInfo is { HasMatcher: true } repairInfo)
		{
			var snapshot = GetVisualTreeForRepair(repairInfo);
			var matches = snapshot.Nodes
				.Where(node => repairInfo.Matches(node, snapshot))
				.Select(node => Element.FromNode(this, node, snapshot, repairInfo))
				.ToArray();

			if (matches.Length == 1)
				return matches[0];

			if (matches.Length > 1 && TryChooseBestRepairMatch(element, matches, out var bestMatch))
				return bestMatch;

			if (matches.Length > 1)
				throw new AppDriverException(AppDriverErrorCodes.AmbiguousTarget, $"Element '{element.TargetId}' is stale and matcher '{repairInfo.Description}' matched multiple replacements.");
		}

		throw new AppDriverException(AppDriverErrorCodes.TargetNotFound, $"Element '{element.TargetId}' is stale and no replacement matched its selector or matcher.");
	}

	internal void RegisterElement(Element element)
	{
		_ = element ?? throw new ArgumentNullException(nameof(element));
		lock (elementCacheSync)
		{
			AddElementRegistration(element.TargetId, element);
		}
	}

	internal void MoveElementRegistration(Element element, string oldTargetId, string newTargetId)
	{
		_ = element ?? throw new ArgumentNullException(nameof(element));
		lock (elementCacheSync)
		{
			RemoveElementRegistration(oldTargetId, element);
			AddElementRegistration(newTargetId, element);
		}
	}

	public void Dispose()
	{
		if (disposed)
			return;

		disposed = true;
		Connection.Dispose();
	}

	private static TElement WrapElement<TElement>(Element element)
		where TElement : Element
	{
		if (element is TElement typed)
			return typed;

		var constructor = typeof(TElement).GetConstructor(
			System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
			binder: null,
			[typeof(Element)],
			modifiers: null);
		if (constructor is not null)
			return (TElement)constructor.Invoke(new object[] { element });

		throw new AppDriverException(AppDriverErrorCodes.UnsupportedTarget, $"Element type '{typeof(TElement).FullName}' must expose a constructor that accepts Element.");
	}

	private static byte[] DecodeScreenshot(ScreenshotCommandResponse response) =>
		Convert.FromBase64String(response.BytesBase64 ?? string.Empty);

	private static ImageFormat GetImageFormatFromPath(string path)
	{
		return Path.GetExtension(path).ToLowerInvariant() switch
		{
			".bmp" => ImageFormat.Bmp,
			".gif" => ImageFormat.Gif,
			".jpg" or ".jpeg" => ImageFormat.Jpeg,
			_ => ImageFormat.Png,
		};
	}

	private static TimeSpan TimeoutFromMilliseconds(int timeoutMs) =>
		TimeSpan.FromMilliseconds(Math.Max(1, timeoutMs));

	private ElementRepairInfo CreateElementMatcherRepairInfo(
		Expression<Func<Element, bool?>> matcher,
		Func<Element, bool?> predicate,
		Func<ElementRepairInfo?> repairInfoAccessor)
	{
		var description = matcher.ToString();
		var propertyNames = ElementPropertyAccessCollector.Collect(matcher).ToArray();
		return new ElementRepairInfo(
			description,
			StableHash(description),
			propertyNames,
			(node, snapshot) => predicate(Element.FromNode(this, node, snapshot, repairInfoAccessor(), register: false)) == true);
	}

	private ElementRepairInfo CreateTypedElementMatcherRepairInfo<TElement>(
		Expression<Func<TElement, bool?>> matcher,
		Func<TElement, bool?> predicate,
		Func<ElementRepairInfo?> repairInfoAccessor)
		where TElement : Element
	{
		var description = matcher.ToString();
		var propertyNames = ElementPropertyAccessCollector.Collect(matcher).ToArray();
		return new ElementRepairInfo(
			description,
			StableHash(description),
			propertyNames,
			(node, snapshot) => predicate(WrapElement<TElement>(Element.FromNode(this, node, snapshot, repairInfoAccessor(), register: false))) == true);
	}

	private VisualTreeSnapshot GetVisualTreeForRepair(ElementRepairInfo repairInfo)
	{
		var snapshot = Send<VisualTreeSnapshot>(new GetVisualTreeCommandRequest
		{
			AsSnapshot = true,
			PropNames = repairInfo.RequestedPropertyNames.Count == 0
				? null
				: repairInfo.RequestedPropertyNames.ToArray(),
		});
		RefreshCachedElements(snapshot);
		return snapshot;
	}

	private void RefreshCachedElements(VisualTreeSnapshot snapshot)
	{
		lock (elementCacheSync)
		{
			foreach (var node in snapshot.Nodes)
			{
				if (!elementCache.TryGetValue(node.TargetId, out var references))
					continue;

				for (var index = references.Count - 1; index >= 0; index--)
				{
					if (references[index].TryGetTarget(out var element))
						element.RefreshFromCache(node, snapshot);
					else
						references.RemoveAt(index);
				}
			}
		}
	}

	private void AddElementRegistration(string targetId, Element element)
	{
		if (!elementCache.TryGetValue(targetId, out var references))
		{
			references = [];
			elementCache[targetId] = references;
		}

		for (var index = references.Count - 1; index >= 0; index--)
		{
			if (!references[index].TryGetTarget(out var live))
			{
				references.RemoveAt(index);
				continue;
			}

			if (ReferenceEquals(live, element))
				return;
		}

		references.Add(new WeakReference<Element>(element));
	}

	private void RemoveElementRegistration(string targetId, Element element)
	{
		if (!elementCache.TryGetValue(targetId, out var references))
			return;

		for (var index = references.Count - 1; index >= 0; index--)
		{
			if (!references[index].TryGetTarget(out var live) || ReferenceEquals(live, element))
				references.RemoveAt(index);
		}

		if (references.Count == 0)
			elementCache.Remove(targetId);
	}

	private static bool TryChooseBestRepairMatch(Element staleElement, IReadOnlyCollection<Element> matches, out Element bestMatch)
	{
		var ranked = matches
			.Select(match => (Element: match, Score: ScoreRepairMatch(staleElement, match)))
			.Where(match => match.Score > 0)
			.OrderByDescending(match => match.Score)
			.ToArray();

		if (ranked.Length == 0 || (ranked.Length > 1 && ranked[0].Score == ranked[1].Score))
		{
			bestMatch = null!;
			return false;
		}

		bestMatch = ranked[0].Element;
		return true;
	}

	private static int ScoreRepairMatch(Element staleElement, Element candidate)
	{
		var score = 0;
		if (string.Equals(staleElement.TypeName, candidate.TypeName, StringComparison.Ordinal))
			score += 10;

		if (PropertyEquals(staleElement, candidate, "AutomationProperties.AutomationId")
			|| PropertyEquals(staleElement, candidate, "AutomationId"))
		{
			score += 100;
		}

		if (PropertyEquals(staleElement, candidate, "AutomationProperties.Name"))
			score += 100;
		if (PropertyEquals(staleElement, candidate, "Name"))
			score += 50;
		if (PropertyEquals(staleElement, candidate, "Title"))
			score += 5;
		if (PropertyEquals(staleElement, candidate, "ActualWidth"))
			score += 50;
		if (PropertyEquals(staleElement, candidate, "ActualHeight"))
			score += 50;

		if (staleElement.RepairInfo is { HasMatcher: true } repairInfo
			&& repairInfo.Matches(candidate.SnapshotNode, candidate.CurrentSnapshot ?? VisualTreeSnapshot.Create(0, [candidate.SnapshotNode])))
		{
			score += 1;
		}

		return score;
	}

	private static bool PropertyEquals(Element left, Element right, string propertyName)
	{
		if (!left.Properties.TryGetValue(propertyName, out var leftValue) || IsEmpty(leftValue))
			return false;
		if (!right.Properties.TryGetValue(propertyName, out var rightValue) || IsEmpty(rightValue))
			return false;
		return Equals(leftValue, rightValue)
			|| string.Equals(Convert.ToString(leftValue), Convert.ToString(rightValue), StringComparison.Ordinal);
	}

	private static bool IsEmpty(object? value) =>
		value is null || (value is string text && text.Length == 0);

	private static string StableHash(string text)
	{
		using var sha = SHA256.Create();
		var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
		return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
	}

	private sealed class ElementPropertyAccessCollector : ExpressionVisitor
	{
		private readonly HashSet<string> propertyNames = new(StringComparer.Ordinal);

		public static IReadOnlyCollection<string> Collect(LambdaExpression expression)
		{
			var collector = new ElementPropertyAccessCollector();
			collector.Visit(expression);
			return collector.propertyNames;
		}

		protected override Expression VisitIndex(IndexExpression node)
		{
			if (IsElementExpression(node.Object) && node.Arguments.Count == 1 && TryGetString(node.Arguments[0], out var propertyName))
				propertyNames.Add(propertyName);

			return base.VisitIndex(node);
		}

		protected override Expression VisitMethodCall(MethodCallExpression node)
		{
			if (IsElementExpression(node.Object)
				&& node.Arguments.Count == 1
				&& (node.Method.Name == "get_Item" || node.Method.Name == nameof(Element.HasProperty))
				&& TryGetString(node.Arguments[0], out var propertyName))
			{
				propertyNames.Add(propertyName);
			}

			return base.VisitMethodCall(node);
		}

		private static bool IsElementExpression(Expression? expression) =>
			expression is not null && typeof(Element).IsAssignableFrom(expression.Type);

		private static bool TryGetString(Expression expression, out string value)
		{
			while (expression is UnaryExpression { NodeType: ExpressionType.Convert } convert)
				expression = convert.Operand;

			if (expression is ConstantExpression { Value: string constant })
			{
				value = constant;
				return true;
			}

			value = string.Empty;
			return false;
		}
	}
}
