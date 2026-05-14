namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Utility.WpfUtility.Tree;

public sealed class AppDriver : IDisposable
{
	private static readonly object RecordingSync = new();
	private const int ClientSideMatcherMaxNodeCount = 50_000;
	private const int SelectorDescriptionMaxLength = 700;
	private const int ElementSummaryValueMaxLength = 120;
	private const int ElementPathMaxLength = 1_200;
	private const int NoMatchDiagnosticMaxNodeCount = 200;
	private const int NoMatchDiagnosticMaxElements = 25;
	private static IDisposable? activeRecording;
	private static readonly IReadOnlyList<string> AmbiguousElementSummaryPropertyNames =
	[
		"AutomationProperties.AutomationId",
		"AutomationId",
		"Name",
		"AutomationProperties.Name",
		"Header",
		"Text",
		"Content",
		"IsVisible",
		"IsEnabled",
	];

	private static readonly IReadOnlyList<string> ElementPathIdentityPropertyNames =
	[
		"AutomationProperties.AutomationId",
		"AutomationId",
		"Name",
		"AutomationProperties.Name",
		"Header",
		"Text",
		"Content",
	];

	private static readonly IReadOnlyList<string> ClientSideMatcherFallbackPropertyNames = new[]
	{
		"ActualHeight",
		"ActualWidth",
		"AllowDrop",
		"AutomationProperties.Id",
		"AutomationProperties.Name",
		"AutomationProperties.AutomationId",
		"AutomationId",
		"Background",
		"BorderBrush",
		"BorderThickness",
		"BoundarySize",
		"Child",
		"ClassName",
		"Command",
		"Content",
		"CornerRadius",
		"Cursor",
		"DesiredSize",
		"FlowDirection",
		"Focusable",
		"FontFamily",
		"FontSize",
		"FontWeight",
		"Foreground",
		"HasContent",
		"Header",
		"Height",
		"HorizontalAlignment",
		"InputGestureText",
		"IsChecked",
		"IsEnabled",
		"IsExpanded",
		"IsKeyboardFocused",
		"IsMouseCaptured",
		"IsMouseDirectlyOver",
		"IsMouseOver",
		"IsOpen",
		"IsVisible",
		"KeyboardNavigation.ControlTabNavigation",
		"KeyboardNavigation.DirectionalNavigation",
		"KeyboardNavigation.TabNavigation",
		"Language",
		"Left",
		"Margin",
		"MaxHeight",
		"MaxWidth",
		"MinHeight",
		"MinWidth",
		"Name",
		"Opacity",
		"Orientation",
		"Padding",
		"Panel.ZIndex",
		"RenderSize",
		"ScrollViewer.CanContentScroll",
		"ScrollViewer.HorizontalScrollBarVisibility",
		"ScrollViewer.PanningMode",
		"ScrollViewer.VerticalScrollBarVisibility",
		"TabIndex",
		"Text",
		"TextElement.Background",
		"TextElement.FontFamily",
		"TextElement.FontSize",
		"TextElement.FontWeight",
		"TextElement.Foreground",
		"TextTrimming",
		"Title",
		"ToolTip",
		"Top",
		"Uid",
		"VerticalAlignment",
		"Visibility",
		"Width",
		"WindowState",
	};
	private readonly object elementCacheSync = new();
	private readonly Dictionary<string, List<WeakReference<Element>>> elementCache = new(StringComparer.Ordinal);
	private readonly Keyboard keyboard;
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
		keyboard = new Keyboard(this);
	}

	public string ProductName => ProductInfo.Name;

	public AppConnection Connection { get; }

	public Process Process =>
		Connection.TargetProcess is TargetProcess targetProcess
			? targetProcess.Process
			: System.Diagnostics.Process.GetProcessById(Connection.TargetProcess.Id);

	public AppDriverOptions Options { get; }

	public IAppDriverCommandSession Session { get; }

	public Keyboard Keyboard => keyboard;

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
			() => FindElements(selector, matcherCode: null, matcherHash: null, maxMatches: 2),
			selector.ToString());
	}

	public Element GetElement(Expression<Func<VisualTreeNodeDto, bool>> matcher) =>
		GetElements(matcher, maxMatches: 1).SingleOrDefault()
		?? throw new AppDriverException(AppDriverErrorCodes.TargetNotFound, $"No element matched expression '{ExpressionPayloadSerializer.FormatDiagnosticText(matcher)}'.");

	public Element GetElement(Expression<Func<Element, bool?>> matcher, int timeoutMs = 30_000) =>
		GetElement(matcher, timeoutMs, propNames: null);

	public Element GetElement(Expression<Func<Element, bool?>> matcher, int timeoutMs, IReadOnlyList<string>? propNames)
	{
		_ = matcher ?? throw new ArgumentNullException(nameof(matcher));
		var predicate = matcher.Compile();
		ElementRepairInfo? repairInfo = null;
		repairInfo = CreateElementMatcherRepairInfo(matcher, predicate, () => repairInfo);
		return PollForElement(
			() => FindElements(matcher, predicate, repairInfo, maxMatches: 2, propNames: propNames),
			ExpressionPayloadSerializer.FormatDiagnosticText(matcher),
			TimeoutFromMilliseconds(timeoutMs));
	}

	public TElement GetElement<TElement>(Expression<Func<Element, bool?>> matcher, int timeoutMs = 30_000)
		where TElement : Element =>
		WrapElement<TElement>(GetElement(matcher, timeoutMs));

	public TElement GetElement<TElement>(Expression<Func<Element, bool?>> matcher, int timeoutMs, IReadOnlyList<string>? propNames)
		where TElement : Element =>
		WrapElement<TElement>(GetElement(matcher, timeoutMs, propNames));

	public Element GetElement(
		Expression<Func<Element, bool?>> rootMatcher,
		Expression<Func<Element, bool?>> matcher,
		int timeoutMs = 30_000) =>
		GetElement(rootMatcher, matcher, timeoutMs, propNames: null);

	public Element GetElement(
		Expression<Func<Element, bool?>> rootMatcher,
		Expression<Func<Element, bool?>> matcher,
		int timeoutMs,
		IReadOnlyList<string>? propNames)
	{
		_ = rootMatcher ?? throw new ArgumentNullException(nameof(rootMatcher));
		_ = matcher ?? throw new ArgumentNullException(nameof(matcher));
		EnsureServerSideMatcher(rootMatcher, nameof(rootMatcher));
		EnsureServerSideMatcher(matcher, nameof(matcher));
		var rootPredicate = rootMatcher.Compile();
		var predicate = matcher.Compile();
		ElementRepairInfo? repairInfo = null;
		repairInfo = CreateRootedElementMatcherRepairInfo(rootMatcher, rootPredicate, matcher, predicate, () => repairInfo, propNames);
		return PollForElement(
			() => FindElements(rootMatcher, matcher, repairInfo, maxMatches: 2, propNames: propNames),
			$"{ExpressionPayloadSerializer.FormatDiagnosticText(matcher)} under root matcher '{ExpressionPayloadSerializer.FormatDiagnosticText(rootMatcher)}'",
			TimeoutFromMilliseconds(timeoutMs));
	}

	public Element GetElement(Element root, Expression<Func<Element, bool?>> matcher, int timeoutMs = 30_000) =>
		GetElement(root, matcher, timeoutMs, propNames: null);

	public Element GetElement(Element root, Expression<Func<Element, bool?>> matcher, int timeoutMs, IReadOnlyList<string>? propNames)
	{
		_ = root ?? throw new ArgumentNullException(nameof(root));
		_ = matcher ?? throw new ArgumentNullException(nameof(matcher));
		var predicate = matcher.Compile();
		ElementRepairInfo? repairInfo = null;
		repairInfo = CreateElementMatcherRepairInfo(matcher, predicate, () => repairInfo);
		var diagnosticPropertyNames = GetPropNamesForMatcher(matcher, propNames);
		return PollForElement(
			() => FindElements(
				matcher,
				predicate,
				repairInfo,
				maxMatches: 2,
				propNames: propNames,
				rootTargetId: root.TargetId,
				includeRoot: false,
				maxNodeCount: ClientSideMatcherMaxNodeCount),
			$"{ExpressionPayloadSerializer.FormatDiagnosticText(matcher)} under '{root.TargetId}'",
			TimeoutFromMilliseconds(timeoutMs),
			() => BuildRootNoMatchDiagnostic(root.TargetId, diagnosticPropertyNames));
	}

	public IReadOnlyList<Element> GetElements(ElementSelector selector, int maxMatches = 100)
	{
		_ = selector ?? throw new ArgumentNullException(nameof(selector));
		return FindElements(selector, matcherCode: null, matcherHash: null, maxMatches: maxMatches);
	}

	public IReadOnlyList<Element> GetElements(Expression<Func<VisualTreeNodeDto, bool>> matcher, int maxMatches = 100)
	{
		_ = matcher ?? throw new ArgumentNullException(nameof(matcher));
		var predicate = matcher.Compile();
		var payload = ExpressionPayloadSerializer.Serialize(matcher);
		var repairInfo = new ElementRepairInfo(
			ExpressionPayloadSerializer.FormatDiagnosticText(matcher),
			payload.ExpressionHash,
			[],
			(node, _) => predicate(node));
		return FindElements(null, payload, payload.ExpressionHash, maxMatches, repairInfo);
	}

	public IReadOnlyList<Element> GetElements(Expression<Func<Element, bool?>> matcher, int timeoutMs = 30_000) =>
		GetElements(matcher, timeoutMs, propNames: null);

	public IReadOnlyList<Element> GetElements(Expression<Func<Element, bool?>> matcher, int timeoutMs, IReadOnlyList<string>? propNames)
	{
		_ = matcher ?? throw new ArgumentNullException(nameof(matcher));
		var predicate = matcher.Compile();
		ElementRepairInfo? repairInfo = null;
		repairInfo = CreateElementMatcherRepairInfo(matcher, predicate, () => repairInfo);
		var timeout = TimeoutFromMilliseconds(timeoutMs);
		var stopwatch = Stopwatch.StartNew();
		var attempt = 0;
		while (true)
		{
			SleepBeforePoll(attempt++, stopwatch, timeout);

			var matches = FindElements(matcher, predicate, repairInfo, maxMatches: 0, propNames: propNames);
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

	public IReadOnlyList<TElement> GetElements<TElement>(Expression<Func<Element, bool?>> matcher, int timeoutMs, IReadOnlyList<string>? propNames)
		where TElement : Element =>
		GetElements(matcher, timeoutMs, propNames)
			.Select(WrapElement<TElement>)
			.ToArray();

	public IReadOnlyList<Element> GetElements(
		Expression<Func<Element, bool?>> rootMatcher,
		Expression<Func<Element, bool?>> matcher,
		int timeoutMs = 30_000) =>
		GetElements(rootMatcher, matcher, timeoutMs, propNames: null);

	public IReadOnlyList<Element> GetElements(
		Expression<Func<Element, bool?>> rootMatcher,
		Expression<Func<Element, bool?>> matcher,
		int timeoutMs,
		IReadOnlyList<string>? propNames)
	{
		_ = rootMatcher ?? throw new ArgumentNullException(nameof(rootMatcher));
		_ = matcher ?? throw new ArgumentNullException(nameof(matcher));
		EnsureServerSideMatcher(rootMatcher, nameof(rootMatcher));
		EnsureServerSideMatcher(matcher, nameof(matcher));
		var rootPredicate = rootMatcher.Compile();
		var predicate = matcher.Compile();
		ElementRepairInfo? repairInfo = null;
		repairInfo = CreateRootedElementMatcherRepairInfo(rootMatcher, rootPredicate, matcher, predicate, () => repairInfo, propNames);
		var timeout = TimeoutFromMilliseconds(timeoutMs);
		var stopwatch = Stopwatch.StartNew();
		var attempt = 0;
		while (true)
		{
			SleepBeforePoll(attempt++, stopwatch, timeout);

			var matches = FindElements(rootMatcher, matcher, repairInfo, maxMatches: 0, propNames: propNames);
			if (matches.Count != 0)
				return matches;
			if (stopwatch.Elapsed >= timeout)
				break;
		}

		return [];
	}

	public IReadOnlyList<Element> GetElements(Element root, Expression<Func<Element, bool?>> matcher, int timeoutMs = 30_000) =>
		GetElements(root, matcher, timeoutMs, propNames: null);

	public IReadOnlyList<Element> GetElements(Element root, Expression<Func<Element, bool?>> matcher, int timeoutMs, IReadOnlyList<string>? propNames)
	{
		_ = root ?? throw new ArgumentNullException(nameof(root));
		_ = matcher ?? throw new ArgumentNullException(nameof(matcher));
		var predicate = matcher.Compile();
		ElementRepairInfo? repairInfo = null;
		repairInfo = CreateElementMatcherRepairInfo(matcher, predicate, () => repairInfo);
		var timeout = TimeoutFromMilliseconds(timeoutMs);
		var stopwatch = Stopwatch.StartNew();
		var attempt = 0;
		while (true)
		{
			SleepBeforePoll(attempt++, stopwatch, timeout);

			var matches = FindElements(
				matcher,
				predicate,
				repairInfo,
				maxMatches: 0,
				propNames: propNames,
				rootTargetId: root.TargetId,
				includeRoot: false,
				maxNodeCount: ClientSideMatcherMaxNodeCount);
			if (matches.Count != 0)
				return matches;
			if (stopwatch.Elapsed >= timeout)
				break;
		}

		return [];
	}

	private IReadOnlyList<Element> FindElements(
		Expression<Func<Element, bool?>> rootMatcher,
		Expression<Func<Element, bool?>> matcher,
		ElementRepairInfo repairInfo,
		int maxMatches,
		IReadOnlyList<string>? propNames = null)
	{
		EnsureServerSideMatcher(rootMatcher, nameof(rootMatcher));
		EnsureServerSideMatcher(matcher, nameof(matcher));
		var rootPayload = ExpressionPayloadSerializer.Serialize(rootMatcher);
		var payload = ExpressionPayloadSerializer.Serialize(matcher);
		var requestedPropertyNames = GetPropNamesForRootedMatcher(rootMatcher, matcher, propNames);
		return FindElements(
			null,
			new Eval(payload.ExpressionJson),
			payload.ExpressionHash,
			maxMatches,
			repairInfo,
			requestedPropertyNames,
			rootTargetId: null,
			includeRoot: false,
			maxDepth: null,
			maxNodeCount: ClientSideMatcherMaxNodeCount,
			rootMatcherCode: new Eval(rootPayload.ExpressionJson),
			rootMatcherHash: rootPayload.ExpressionHash);
	}

	private IReadOnlyList<Element> FindElements(
		Expression<Func<Element, bool?>> matcher,
		Func<Element, bool?> predicate,
		ElementRepairInfo repairInfo,
		int maxMatches,
		IReadOnlyList<string>? propNames = null,
		string? rootTargetId = null,
		bool includeRoot = true,
		int? maxDepth = null,
		int? maxNodeCount = null,
		object? rootMatcherCode = null,
		string? rootMatcherHash = null)
	{
		_ = matcher ?? throw new ArgumentNullException(nameof(matcher));
		if (RequiresClientSideElementPredicate(matcher))
			return FindElementsOnClient(predicate, maxMatches, repairInfo, propNames, rootTargetId, includeRoot);

		var payload = ExpressionPayloadSerializer.Serialize(matcher);
		var requestedPropertyNames = GetPropNamesForMatcher(matcher, propNames);
		return FindElements(
			null,
			new Eval(payload.ExpressionJson),
			payload.ExpressionHash,
			maxMatches,
			repairInfo,
			requestedPropertyNames,
			rootTargetId,
			includeRoot,
			maxDepth,
			maxNodeCount,
			rootMatcherCode,
			rootMatcherHash);
	}

	public TElement GetElement<TElement>(Expression<Func<TElement, bool?>> matcher)
		where TElement : Element =>
		GetElements<TElement>(matcher, maxMatches: 1).SingleOrDefault()
		?? throw new AppDriverException(AppDriverErrorCodes.TargetNotFound, $"No element matched expression '{ExpressionPayloadSerializer.FormatDiagnosticText(matcher)}'.");

	public IReadOnlyList<TElement> GetElements<TElement>(Expression<Func<TElement, bool?>> matcher, int maxMatches = 100)
		where TElement : Element
	{
		_ = matcher ?? throw new ArgumentNullException(nameof(matcher));
		var predicate = matcher.Compile();
		ElementRepairInfo? repairInfo = null;
		repairInfo = CreateTypedElementMatcherRepairInfo(matcher, predicate, () => repairInfo);
		if (RequiresClientSideElementPredicate(matcher))
			return FindTypedElementsOnClient(predicate, maxMatches, repairInfo);

		var payload = ExpressionPayloadSerializer.Serialize(matcher);
		var requestedPropertyNames = GetPropNamesForMatcher(matcher, propNames: null);
		return FindElements(null, new Eval(payload.ExpressionJson), payload.ExpressionHash, maxMatches, repairInfo, requestedPropertyNames)
			.Select(WrapElement<TElement>)
			.ToArray();
	}

	private IReadOnlyList<Element> FindElementsOnClient(
		Func<Element, bool?> predicate,
		int maxMatches,
		ElementRepairInfo repairInfo,
		IReadOnlyList<string>? propNames,
		string? rootTargetId = null,
		bool includeRoot = true)
	{
		var snapshot = GetVisualTree(
			rootTargetId: rootTargetId,
			propNames: GetClientSideMatcherPropNames(propNames),
			maxNodeCount: ClientSideMatcherMaxNodeCount);
		var limit = maxMatches <= 0 ? int.MaxValue : maxMatches;
		return snapshot.Nodes
			.Where(node => includeRoot || string.IsNullOrWhiteSpace(rootTargetId) || !string.Equals(node.TargetId, rootTargetId, StringComparison.Ordinal))
			.Select(node => Element.FromNode(this, node, snapshot, repairInfo))
			.Where(element => predicate(element) == true)
			.Take(limit)
			.ToArray();
	}

	private IReadOnlyList<TElement> FindTypedElementsOnClient<TElement>(
		Func<TElement, bool?> predicate,
		int maxMatches,
		ElementRepairInfo repairInfo)
		where TElement : Element
	{
		var snapshot = GetVisualTree(
			rootTargetId: null,
			propNames: GetClientSideMatcherPropNames(propNames: null),
			maxNodeCount: ClientSideMatcherMaxNodeCount);
		var limit = maxMatches <= 0 ? int.MaxValue : maxMatches;
		return snapshot.Nodes
			.Select(node => WrapElement<TElement>(Element.FromNode(this, node, snapshot, repairInfo)))
			.Where(element => predicate(element) == true)
			.Take(limit)
			.ToArray();
	}

	private IReadOnlyList<Element> FindElements(
		ElementSelector? selector,
		object? matcherCode,
		string? matcherHash,
		int maxMatches,
		ElementRepairInfo? repairInfo = null,
		IReadOnlyList<string>? propNames = null,
		string? rootTargetId = null,
		bool includeRoot = true,
		int? maxDepth = null,
		int? maxNodeCount = null,
		object? rootMatcherCode = null,
		string? rootMatcherHash = null)
	{
		var requestedPropertyNames = MergePropertyNames(selector?.RequestedPropertyNames, propNames);
		var response = Send<FindElementCommandResponse>(new FindElementCommandRequest
		{
			Selector = selector?.ToDto(),
			RootTargetId = rootTargetId,
			IncludeRoot = includeRoot,
			MaxDepth = maxDepth,
			MaxNodeCount = maxNodeCount,
			PropNames = requestedPropertyNames,
			MatcherCode = matcherCode,
			MatcherHash = matcherHash,
			RootMatcherCode = rootMatcherCode,
			RootMatcherHash = rootMatcherHash,
			MaxMatches = maxMatches,
		});

		return response.Matches
			.Select(match => Element.FromMatch(this, match, selector, repairInfo))
			.ToArray();
	}

	private Element PollForElement(Func<IReadOnlyList<Element>> find, string selectorDescription)
		=> PollForElement(find, selectorDescription, Options.Timeout);

	private Element PollForElement(
		Func<IReadOnlyList<Element>> find,
		string selectorDescription,
		TimeSpan timeout,
		Func<string?>? noMatchDiagnostic = null)
	{
		var stopwatch = Stopwatch.StartNew();
		var attempt = 0;
		while (true)
		{
			SleepBeforePoll(attempt++, stopwatch, timeout);

			var matches = find();
			if (matches.Count == 1)
				return matches[0];
			if (matches.Count > 1)
				throw new AppDriverException(AppDriverErrorCodes.AmbiguousTarget, BuildAmbiguousElementMessage(selectorDescription, matches));
			if (stopwatch.Elapsed >= timeout)
				break;
		}

		throw new AppDriverException(AppDriverErrorCodes.TargetNotFound, BuildNoMatchElementMessage(selectorDescription, noMatchDiagnostic));
	}

	private static string BuildNoMatchElementMessage(string selectorDescription, Func<string?>? diagnosticProvider)
	{
		var builder = new StringBuilder();
		builder.Append("No element matched selector.");
		builder.AppendLine();
		builder.Append("Selector: ");
		builder.AppendLine(Truncate(NormalizeWhitespace(selectorDescription), SelectorDescriptionMaxLength));
		var diagnostic = TryGetNoMatchDiagnostic(diagnosticProvider);
		if (!string.IsNullOrWhiteSpace(diagnostic))
		{
			builder.AppendLine(diagnostic);
		}

		return builder.ToString().TrimEnd();
	}

	private string BuildRootNoMatchDiagnostic(string rootTargetId, IReadOnlyList<string> propNames)
	{
		var snapshot = GetVisualTree(rootTargetId, propNames, maxNodeCount: NoMatchDiagnosticMaxNodeCount);
		var elements = snapshot.Nodes
			.Take(NoMatchDiagnosticMaxElements)
			.Select(node => Element.FromNode(this, node, snapshot, register: false))
			.ToArray();

		var builder = new StringBuilder();
		builder.AppendLine($"Elements currently under '{rootTargetId}' ({snapshot.NodeCount} captured):");
		if (elements.Length == 0)
		{
			builder.Append("  <none>");
			return builder.ToString();
		}

		for (var i = 0; i < elements.Length; i++)
		{
			builder.Append("  ");
			builder.Append(i + 1);
			builder.Append(". ");
			builder.AppendLine(FormatElementSummary(elements[i]));
		}

		if (snapshot.NodeCount > elements.Length)
		{
			builder.Append("  ... ");
			builder.Append(snapshot.NodeCount - elements.Length);
			builder.AppendLine(" more element(s) omitted.");
		}

		if (snapshot.IsTruncated && !string.IsNullOrWhiteSpace(snapshot.TruncationReason))
		{
			builder.Append("  Snapshot truncated: ");
			builder.AppendLine(snapshot.TruncationReason);
		}

		return builder.ToString().TrimEnd();
	}

	private static string? TryGetNoMatchDiagnostic(Func<string?>? diagnosticProvider)
	{
		if (diagnosticProvider is null)
			return null;

		try
		{
			return diagnosticProvider();
		}
		catch (Exception ex)
		{
			return $"Diagnostic lookup failed: {ex.GetType().Name}: {ex.Message}";
		}
	}

	private static string BuildAmbiguousElementMessage(string selectorDescription, IReadOnlyList<Element> matches)
	{
		var builder = new StringBuilder();
		builder.Append("More than one element matched selector.");
		builder.AppendLine();
		builder.Append("Selector: ");
		builder.AppendLine(Truncate(NormalizeWhitespace(selectorDescription), SelectorDescriptionMaxLength));
		builder.AppendLine("Matched elements:");
		for (var i = 0; i < matches.Count; i++)
		{
			builder.Append("  ");
			builder.Append(i + 1);
			builder.Append(". ");
			builder.AppendLine(FormatElementSummary(matches[i]));
		}

		builder.Append("Make the selector more specific, or call GetElements when multiple matches are expected.");
		return builder.ToString();
	}

	private static string FormatElementSummary(Element element)
	{
		var parts = new List<string>
		{
			$"TargetId={FormatDiagnosticValue(element.TargetId)}",
			$"TypeName={FormatDiagnosticValue(element.TypeName)}",
		};

		if (!string.IsNullOrWhiteSpace(element.FrameworkTypeName)
			&& !string.Equals(element.FrameworkTypeName, element.TypeName, StringComparison.Ordinal))
		{
			parts.Add($"FrameworkTypeName={FormatDiagnosticValue(element.FrameworkTypeName)}");
		}

		foreach (var propertyName in AmbiguousElementSummaryPropertyNames)
		{
			if (!element.Properties.TryGetValue(propertyName, out var value) || value is null or PropertyExtractionError)
				continue;

			var valueText = NormalizeWhitespace(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
			if (valueText.Length == 0)
				continue;

			parts.Add($"{propertyName}={FormatDiagnosticValue(value)}");
		}

		var path = FormatElementPath(element);
		if (path.Length != 0)
			parts.Add($"Path={path}");

		return string.Join(", ", parts);
	}

	private static string FormatElementPath(Element element)
	{
		var path = GetElementPath(element);
		if (path.Count <= 1)
			return string.Empty;

		return Truncate(string.Join(" > ", path.Select(FormatPathSegment)), ElementPathMaxLength);
	}

	private static IReadOnlyList<ElementPathSegmentResponse> GetElementPath(Element element)
	{
		if (element.DiagnosticPath.Count != 0)
			return element.DiagnosticPath;

		var snapshot = element.CurrentSnapshot;
		if (snapshot is null)
			return [];

		var byId = new Dictionary<string, VisualTreeNodeDto>(StringComparer.Ordinal);
		foreach (var candidate in snapshot.Nodes)
		{
			if (!byId.TryGetValue(candidate.TargetId, out var existing) || existing.ParentId is null)
				byId[candidate.TargetId] = candidate;
		}

		var path = new List<ElementPathSegmentResponse>();
		var seenTargetIds = new HashSet<string>(StringComparer.Ordinal);
		var current = element.SnapshotNode;
		while (true)
		{
			if (!seenTargetIds.Add(current.TargetId))
				break;

			path.Add(ToPathSegment(current));
			if (string.IsNullOrWhiteSpace(current.ParentId))
				break;
			if (!byId.TryGetValue(current.ParentId!, out current))
				break;
		}

		path.Reverse();
		return path;
	}

	private static ElementPathSegmentResponse ToPathSegment(VisualTreeNodeDto node) =>
		new()
		{
			TargetId = node.TargetId,
			TypeName = node.TypeName,
			FrameworkTypeName = node.FrameworkTypeName,
			Properties = node.Properties,
		};

	private static string FormatPathSegment(ElementPathSegmentResponse segment)
	{
		var identityParts = ElementPathIdentityPropertyNames
			.Select(propertyName => TryFormatPathProperty(segment, propertyName))
			.Where(static value => value is not null)
			.Cast<string>()
			.Take(3)
			.ToArray();
		return identityParts.Length == 0
			? segment.TypeName
			: $"{segment.TypeName}[{string.Join(", ", identityParts)}]";
	}

	private static string? TryFormatPathProperty(ElementPathSegmentResponse segment, string propertyName)
	{
		if (!segment.Properties.TryGetValue(propertyName, out var value) || value is null or PropertyExtractionError)
			return null;

		var valueText = NormalizeWhitespace(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
		if (valueText.Length == 0)
			return null;

		return $"{ShortenPropertyName(propertyName)}={FormatDiagnosticValue(value)}";
	}

	private static string ShortenPropertyName(string propertyName) =>
		propertyName.StartsWith("AutomationProperties.", StringComparison.Ordinal)
			? propertyName.Substring("AutomationProperties.".Length)
			: propertyName;

	private static string FormatDiagnosticValue(object? value)
	{
		if (value is null)
			return "<null>";

		var text = Truncate(NormalizeWhitespace(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty), ElementSummaryValueMaxLength);
		return value is string
			? $"\"{text.Replace("\"", "\\\"")}\""
			: text;
	}

	private static string NormalizeWhitespace(string value)
	{
		if (string.IsNullOrEmpty(value))
			return string.Empty;

		var builder = new StringBuilder(value.Length);
		var previousWasWhitespace = false;
		foreach (var character in value)
		{
			if (char.IsWhiteSpace(character))
			{
				if (!previousWasWhitespace)
					builder.Append(' ');
				previousWasWhitespace = true;
				continue;
			}

			builder.Append(character);
			previousWasWhitespace = false;
		}

		return builder.ToString().Trim();
	}

	private static string Truncate(string value, int maxLength)
	{
		if (value.Length <= maxLength)
			return value;

		const string Suffix = "...";
		return maxLength <= Suffix.Length
			? value.Substring(0, maxLength)
			: value.Substring(0, maxLength - Suffix.Length) + Suffix;
	}

	private void SleepBeforePoll(int attempt, Stopwatch stopwatch, TimeSpan timeout)
	{
		if (attempt == 0)
			return;

		var remainingMs = (int)Math.Ceiling((timeout - stopwatch.Elapsed).TotalMilliseconds);
		if (remainingMs <= 0)
			return;

		Thread.Sleep(Math.Min(GetElementPollDelayMs(attempt), remainingMs));
	}

	private int GetElementPollDelayMs(int attempt)
	{
		var backoff = Options.ElementPollBackoffMs ?? [];
		var index = attempt - 1;
		if (index >= 0 && index < backoff.Length)
			return Math.Max(0, backoff[index]);

		return 1000;
	}

	public VisualTreeSnapshot GetVisualTree(string? rootTargetId = null) =>
		GetVisualTree(rootTargetId, propNames: null);

	public VisualTreeSnapshot GetVisualTree(string? rootTargetId, IReadOnlyList<string>? propNames) =>
		GetVisualTree(rootTargetId, propNames, maxNodeCount: null);

	public VisualTreeSnapshot GetVisualTree(string? rootTargetId, IReadOnlyList<string>? propNames, int? maxNodeCount)
	{
		var snapshot = Send<VisualTreeSnapshot>(new GetVisualTreeCommandRequest
		{
			AsSnapshot = true,
			RootTargetId = rootTargetId,
			PropNames = propNames,
			MaxNodeCount = maxNodeCount,
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
		DecodeScreenshot(WaitForStableScreenshot(() => CaptureScreenshot(format.ToProtocolString()), nameof(Screenshot)));

	public void Screenshot(string fileOutputPath)
	{
		_ = fileOutputPath ?? throw new ArgumentNullException(nameof(fileOutputPath));
		var bytes = Screenshot(GetImageFormatFromPath(fileOutputPath));
		var directory = Path.GetDirectoryName(Path.GetFullPath(fileOutputPath));
		if (!string.IsNullOrEmpty(directory))
			Directory.CreateDirectory(directory);
		File.WriteAllBytes(fileOutputPath, bytes);
	}

	public static IDisposable Record(string fileOutputPath, string? windowTitle = null)
	{
		_ = fileOutputPath ?? throw new ArgumentNullException(nameof(fileOutputPath));
		lock (RecordingSync)
		{
			activeRecording?.Dispose();
			activeRecording = null;

			fileOutputPath = Environment.ExpandEnvironmentVariables(fileOutputPath);
			fileOutputPath = Path.GetFullPath(fileOutputPath);
			var directory = Path.GetDirectoryName(fileOutputPath);
			if (!string.IsNullOrEmpty(directory))
				Directory.CreateDirectory(directory);
			if (File.Exists(fileOutputPath))
				File.Delete(fileOutputPath);

			var ffmpegPath = ResolveFfmpegPath();
			var fullScreen = string.IsNullOrEmpty(windowTitle) || Process.GetProcesses().Count(process => string.Equals(process.MainWindowTitle, windowTitle, StringComparison.Ordinal)) > 1;
			var arguments = fullScreen
				? $"-y -f gdigrab -framerate 24 -i desktop \"{fileOutputPath}\" -c:v vp8"
				: $"-y -f gdigrab -framerate 24 -i title=\"{EscapeFfmpegArgument(windowTitle!)}\" \"{fileOutputPath}\" -c:v vp8";

			var recorder = RecordingProcessFactory(new ProcessStartInfo
			{
				FileName = ffmpegPath,
				Arguments = arguments,
				CreateNoWindow = true,
				UseShellExecute = false,
				RedirectStandardError = true,
				RedirectStandardOutput = true,
				RedirectStandardInput = true,
			});
			try
			{
				recorder.RegisterForParentClose();
			}
			catch
			{
				recorder.Dispose();
				throw;
			}

			activeRecording = new RecordingScope(recorder, () =>
			{
				lock (RecordingSync)
				{
					activeRecording = null;
				}
			});
			return activeRecording;
		}
	}

	internal static Func<ProcessStartInfo, IRecordingProcess> RecordingProcessFactory { get; set; } = ProcessRecordingProcess.Start;

	internal static string? RecordingFfmpegPathOverride { get; set; }

	internal void RefreshAfterPhysicalInput()
	{
		GetVisualTree();
	}

	internal static ScreenshotCommandResponse WaitForStableScreenshot(Func<ScreenshotCommandResponse> capture, string caller)
	{
		_ = capture ?? throw new ArgumentNullException(nameof(capture));
		var stopwatch = Stopwatch.StartNew();
		ScreenshotCommandResponse? previous = null;
		ScreenshotCommandResponse? current = null;

		while (stopwatch.ElapsedMilliseconds < 5_000)
		{
			current = capture();
			ThrowIfScreenshotFailed(current, caller);
			if (previous is not null && string.Equals(previous.BytesBase64, current.BytesBase64, StringComparison.Ordinal))
				return current;

			previous = current;
			Thread.Sleep(500);
		}

		current ??= capture();
		ThrowIfScreenshotFailed(current, caller);
		return current;
	}

	internal Element Repair(Element element)
	{
		_ = element ?? throw new ArgumentNullException(nameof(element));

		if (element.Selector is not null)
		{
			var matches = FindElements(element.Selector, matcherCode: null, matcherHash: null, maxMatches: 100);
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

	private static void ThrowIfScreenshotFailed(ScreenshotCommandResponse response, string caller)
	{
		if (response.Status == ProtocolConstants.Statuses.PendingResult)
			throw new TimeoutException($"{caller} timeout.");
		if (response.Success == false)
			throw new AppDriverException(response.ErrorCode ?? ProtocolConstants.ErrorCodes.ProtocolError, response.Error ?? $"{caller} failed.");
	}

	private static string ResolveFfmpegPath()
	{
		if (!string.IsNullOrWhiteSpace(RecordingFfmpegPathOverride))
			return RecordingFfmpegPathOverride!;

		var baseDirectory = AppContext.BaseDirectory;
		var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? baseDirectory;
		var candidates = new[]
		{
			Path.Combine(baseDirectory, "DeepFlowTestResources", "ffmpeg.exe"),
			Path.Combine(assemblyDirectory, "DeepFlowTestResources", "ffmpeg.exe"),
			Path.Combine(baseDirectory, "contentFiles", "any", "any", "DeepFlowTestResources", "ffmpeg.exe"),
			Path.Combine(assemblyDirectory, "contentFiles", "any", "any", "DeepFlowTestResources", "ffmpeg.exe"),
		};

		var path = candidates.FirstOrDefault(File.Exists);
		if (path is not null)
			return path;

		throw new FileNotFoundException("FFmpeg was not found. Expected ffmpeg.exe under DeepFlowTestResources next to the DeepFlowTest assembly.", candidates[0]);
	}

	private static string EscapeFfmpegArgument(string value) =>
		value.Replace("\"", "\\\"");

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

	private static IReadOnlyList<string> GetPropNamesForMatcher(LambdaExpression matcherExpression, IReadOnlyList<string>? propNames)
	{
		var collectedPropertyNames = ElementPropertyAccessCollector.Collect(matcherExpression);
		return MergePropertyNames(VisualTreePropertyExtractor.DefaultPropertyNames, propNames, collectedPropertyNames)
			?? VisualTreePropertyExtractor.DefaultPropertyNames.ToArray();
	}

	private static IReadOnlyList<string> GetPropNamesForRootedMatcher(
		LambdaExpression rootMatcherExpression,
		LambdaExpression matcherExpression,
		IReadOnlyList<string>? propNames)
	{
		var collectedRootPropertyNames = ElementPropertyAccessCollector.Collect(rootMatcherExpression);
		var collectedPropertyNames = ElementPropertyAccessCollector.Collect(matcherExpression);
		return MergePropertyNames(VisualTreePropertyExtractor.DefaultPropertyNames, propNames, collectedRootPropertyNames, collectedPropertyNames)
			?? VisualTreePropertyExtractor.DefaultPropertyNames.ToArray();
	}

	private static IReadOnlyList<string> GetClientSideMatcherPropNames(IReadOnlyList<string>? propNames) =>
		MergePropertyNames(VisualTreePropertyExtractor.DefaultPropertyNames, ClientSideMatcherFallbackPropertyNames, propNames)
		?? ClientSideMatcherFallbackPropertyNames.ToArray();

	private static IReadOnlyList<string>? MergePropertyNames(params IEnumerable<string>?[] sources)
	{
		HashSet<string>? merged = null;
		foreach (var source in sources)
		{
			if (source is null)
				continue;

			foreach (var name in source.Where(static item => !string.IsNullOrWhiteSpace(item)))
			{
				merged ??= new HashSet<string>(StringComparer.Ordinal);
				merged.Add(name);
			}
		}

		return merged?.ToArray();
	}

	private ElementRepairInfo CreateElementMatcherRepairInfo(
		Expression<Func<Element, bool?>> matcher,
		Func<Element, bool?> predicate,
		Func<ElementRepairInfo?> repairInfoAccessor)
	{
		var description = ExpressionPayloadSerializer.FormatDiagnosticText(matcher);
		var propertyNames = RequiresClientSideElementPredicate(matcher)
			? GetClientSideMatcherPropNames(propNames: null)
			: ElementPropertyAccessCollector.Collect(matcher).ToArray();
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
		var description = ExpressionPayloadSerializer.FormatDiagnosticText(matcher);
		var propertyNames = RequiresClientSideElementPredicate(matcher)
			? GetClientSideMatcherPropNames(propNames: null)
			: ElementPropertyAccessCollector.Collect(matcher).ToArray();
		return new ElementRepairInfo(
			description,
			StableHash(description),
			propertyNames,
			(node, snapshot) => predicate(WrapElement<TElement>(Element.FromNode(this, node, snapshot, repairInfoAccessor(), register: false))) == true);
	}

	private ElementRepairInfo CreateRootedElementMatcherRepairInfo(
		Expression<Func<Element, bool?>> rootMatcher,
		Func<Element, bool?> rootPredicate,
		Expression<Func<Element, bool?>> matcher,
		Func<Element, bool?> predicate,
		Func<ElementRepairInfo?> repairInfoAccessor,
		IReadOnlyList<string>? propNames)
	{
		var description = $"{ExpressionPayloadSerializer.FormatDiagnosticText(matcher)} under root matcher '{ExpressionPayloadSerializer.FormatDiagnosticText(rootMatcher)}'";
		var propertyNames = GetPropNamesForRootedMatcher(rootMatcher, matcher, propNames);
		return new ElementRepairInfo(
			description,
			StableHash(description),
			propertyNames,
			(node, snapshot) =>
			{
				var element = Element.FromNode(this, node, snapshot, repairInfoAccessor(), register: false);
				if (predicate(element) != true)
					return false;

				return HasMatchingAncestor(node, snapshot, rootPredicate, repairInfoAccessor);
			});
	}

	private bool HasMatchingAncestor(
		VisualTreeNodeDto node,
		VisualTreeSnapshot snapshot,
		Func<Element, bool?> rootPredicate,
		Func<ElementRepairInfo?> repairInfoAccessor)
	{
		var byId = new Dictionary<string, VisualTreeNodeDto>(StringComparer.Ordinal);
		foreach (var candidate in snapshot.Nodes)
		{
			if (!byId.TryGetValue(candidate.TargetId, out var existing) || existing.ParentId is null)
				byId[candidate.TargetId] = candidate;
		}

		var parentId = node.ParentId;
		while (!string.IsNullOrWhiteSpace(parentId))
		{
			if (!byId.TryGetValue(parentId!, out var parent))
				return false;

			var parentElement = Element.FromNode(this, parent, snapshot, repairInfoAccessor(), register: false);
			if (rootPredicate(parentElement) == true)
				return true;

			parentId = parent.ParentId;
		}

		return false;
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

	private static bool RequiresClientSideElementPredicate(LambdaExpression matcher)
	{
		var detector = new OpaqueDelegateInvocationDetector();
		detector.Visit(matcher);
		return detector.RequiresClientSideEvaluation;
	}

	private static void EnsureServerSideMatcher(LambdaExpression matcher, string argumentName)
	{
		if (RequiresClientSideElementPredicate(matcher))
			throw new InvalidOperationException($"Expression '{argumentName}' uses client-only helpers or delegates and cannot be used in a server-side root-scoped find.");
	}

	private sealed class OpaqueDelegateInvocationDetector : ExpressionVisitor
	{
		public bool RequiresClientSideEvaluation { get; private set; }

		protected override Expression VisitInvocation(InvocationExpression node)
		{
			if (IsOpaqueDelegate(node.Expression))
				RequiresClientSideEvaluation = true;

			return base.VisitInvocation(node);
		}

		protected override Expression VisitMethodCall(MethodCallExpression node)
		{
			if (node.Method.Name == nameof(Action.Invoke) && IsOpaqueDelegate(node.Object))
				RequiresClientSideEvaluation = true;
			else if (UsesElementValue(node) && IsClientOnlyMethod(node.Method))
				RequiresClientSideEvaluation = true;

			return base.VisitMethodCall(node);
		}

		private static bool IsOpaqueDelegate(Expression? expression) =>
			expression is not null
			&& expression is not LambdaExpression
			&& typeof(Delegate).IsAssignableFrom(expression.Type);

		private static bool UsesElementValue(MethodCallExpression expression) =>
			IsElementType(expression.Object?.Type)
			|| expression.Arguments.Any(static argument => IsElementType(argument.Type));

		private static bool IsElementType(Type? type) =>
			type is not null && typeof(Element).IsAssignableFrom(type);

		private static bool IsClientOnlyMethod(MethodInfo method)
		{
			var declaringType = method.DeclaringType;
			if (declaringType is null)
				return false;

			if (declaringType.Assembly == typeof(AppDriver).Assembly)
				return false;

			var namespaceName = declaringType.Namespace;
			return namespaceName is null || !namespaceName.StartsWith("System", StringComparison.Ordinal);
		}
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
