namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

public sealed class AppDriver : IDisposable
{
	private static IAppDriverBackend backend = new DefaultAppDriverBackend();
	private static Func<AppConnection, AppDriverOptions, IAppDriverCommandSession> sessionFactory =
		(connection, options) => new NamedPipeAppDriverCommandSession(connection, options);
	private bool disposed;

	private AppDriver(AppConnection connection, AppDriverOptions options, IAppDriverCommandSession? session = null)
	{
		Connection = connection ?? throw new ArgumentNullException(nameof(connection));
		Options = options ?? throw new ArgumentNullException(nameof(options));
		Session = session ?? sessionFactory(connection, options);
	}

	public string ProductName => ProductInfo.Name;

	public AppConnection Connection { get; }

	public AppDriverOptions Options { get; }

	public IAppDriverCommandSession Session { get; }

	public Keyboard Keyboard => new(this);

	public static AppDriver Launch(string executablePath) =>
		Launch(executablePath, new AppDriverLaunchOptions());

	public static AppDriver Launch(string executablePath, string? args) =>
		Launch(executablePath, new AppDriverLaunchOptions { Arguments = args });

	public static AppDriver Launch(ProcessStartInfo processStartInfo)
	{
		_ = processStartInfo ?? throw new ArgumentNullException(nameof(processStartInfo));
		return Launch(
			processStartInfo.FileName,
			new AppDriverLaunchOptions
			{
				Arguments = processStartInfo.Arguments,
				WorkingDirectory = string.IsNullOrWhiteSpace(processStartInfo.WorkingDirectory) ? null : processStartInfo.WorkingDirectory,
			});
	}

	public static AppDriver Launch(string executablePath, AppDriverLaunchOptions options)
	{
		_ = options ?? throw new ArgumentNullException(nameof(options));
		return new AppDriver(backend.Launch(executablePath, options), options);
	}

	public static AppDriver AttachTo(int processId, AppDriverAttachOptions? options = null)
	{
		var effectiveOptions = options ?? new AppDriverAttachOptions();
		return new AppDriver(backend.AttachTo(processId, effectiveOptions), effectiveOptions);
	}

	public static AppDriver AttachTo(string processName, AppDriverAttachOptions? options = null)
	{
		var effectiveOptions = options ?? new AppDriverAttachOptions();
		return new AppDriver(backend.AttachTo(processName, effectiveOptions), effectiveOptions);
	}

	public static void ConfigureBackendForTests(IAppDriverBackend testBackend)
	{
		backend = testBackend ?? throw new ArgumentNullException(nameof(testBackend));
	}

	public static void ResetBackendForTests()
	{
		backend = new DefaultAppDriverBackend();
		sessionFactory = (connection, options) => new NamedPipeAppDriverCommandSession(connection, options);
	}

	public static void ConfigureSessionFactoryForTests(Func<AppConnection, AppDriverOptions, IAppDriverCommandSession> testSessionFactory)
	{
		sessionFactory = testSessionFactory ?? throw new ArgumentNullException(nameof(testSessionFactory));
	}

	public static AppDriver CreateForTests(AppConnection connection, IAppDriverCommandSession session, AppDriverOptions? options = null) =>
		new(connection, options ?? new AppDriverOptions(), session);

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

	public Element GetElement(Expression<Func<Element, bool?>> matcher) =>
		GetElements(matcher, maxMatches: 1).SingleOrDefault()
		?? throw new AppDriverException(AppDriverErrorCodes.TargetNotFound, $"No element matched expression '{matcher}'.");

	public TElement GetElement<TElement>(Expression<Func<TElement, bool?>> matcher)
		where TElement : Element =>
		GetElements<TElement>(matcher, maxMatches: 1).SingleOrDefault()
		?? throw new AppDriverException(AppDriverErrorCodes.TargetNotFound, $"No element matched expression '{matcher}'.");

	public IReadOnlyList<Element> GetElements(ElementSelector selector, int maxMatches = 100)
	{
		_ = selector ?? throw new ArgumentNullException(nameof(selector));
		return FindElements(selector, matcherPayload: null, maxMatches);
	}

	public IReadOnlyList<Element> GetElements(Expression<Func<VisualTreeNodeDto, bool>> matcher, int maxMatches = 100)
	{
		_ = matcher ?? throw new ArgumentNullException(nameof(matcher));
		var payload = ExpressionPayloadSerializer.Serialize(matcher);
		return FindElements(null, payload, maxMatches);
	}

	public IReadOnlyList<Element> GetElements(Expression<Func<Element, bool?>> matcher, int maxMatches = 100)
	{
		_ = matcher ?? throw new ArgumentNullException(nameof(matcher));
		var predicate = matcher.Compile();
		var snapshot = GetVisualTree();
		return snapshot.Nodes
			.Select(node => Element.FromNode(this, node, snapshot))
			.Where(element => predicate(element) == true)
			.Take(Math.Max(0, maxMatches))
			.ToArray();
	}

	public IReadOnlyList<TElement> GetElements<TElement>(Expression<Func<TElement, bool?>> matcher, int maxMatches = 100)
		where TElement : Element
	{
		_ = matcher ?? throw new ArgumentNullException(nameof(matcher));
		var predicate = matcher.Compile();
		var snapshot = GetVisualTree();
		return snapshot.Nodes
			.Select(node => WrapElement<TElement>(Element.FromNode(this, node, snapshot)))
			.Where(element => predicate(element) == true)
			.Take(Math.Max(0, maxMatches))
			.ToArray();
	}

	private IReadOnlyList<Element> FindElements(ElementSelector? selector, ExpressionMatcherPayload? matcherPayload, int maxMatches)
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
			.Select(match => Element.FromMatch(this, match, selector))
			.ToArray();
	}

	private Element PollForElement(Func<IReadOnlyList<Element>> find, string selectorDescription)
	{
		var stopwatch = Stopwatch.StartNew();
		var delays = new[] { 0 }.Concat(Options.ElementPollBackoffMs ?? Array.Empty<int>());
		foreach (var delay in delays)
		{
			if (delay > 0)
				Thread.Sleep(delay);

			var matches = find();
			if (matches.Count == 1)
				return matches[0];
			if (matches.Count > 1)
				throw new AppDriverException(AppDriverErrorCodes.AmbiguousTarget, $"More than one element matched selector '{selectorDescription}'.");
			if (stopwatch.Elapsed >= Options.Timeout)
				break;
		}

		throw new AppDriverException(AppDriverErrorCodes.TargetNotFound, $"No element matched selector '{selectorDescription}'.");
	}

	public VisualTreeSnapshot GetVisualTree(string? rootTargetId = null)
	{
		return Send<VisualTreeSnapshot>(new GetVisualTreeCommandRequest
		{
			AsSnapshot = true,
			RootTargetId = rootTargetId,
		});
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

	internal Element Repair(Element element)
	{
		if (element.Selector is null)
			throw new AppDriverException(ProtocolConstants.ErrorCodes.StaleTarget, $"Element '{element.TargetId}' is stale and cannot be repaired without a selector.");

		var matches = FindElements(element.Selector, matcherPayload: null, maxMatches: 2);
		return matches.Count switch
		{
			1 => matches[0],
			0 => throw new AppDriverException(AppDriverErrorCodes.TargetNotFound, $"Element '{element.TargetId}' is stale and no replacement matched selector '{element.Selector}'."),
			_ => throw new AppDriverException(AppDriverErrorCodes.AmbiguousTarget, $"Element '{element.TargetId}' is stale and selector '{element.Selector}' matched multiple replacements."),
		};
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
			new[] { typeof(Element) },
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
}
