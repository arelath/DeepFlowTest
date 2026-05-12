namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using System.Diagnostics;
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

	public static AppDriver Launch(string executablePath, AppDriverLaunchOptions? options = null)
	{
		var effectiveOptions = options ?? new AppDriverLaunchOptions();
		return new AppDriver(backend.Launch(executablePath, effectiveOptions), effectiveOptions);
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

	public ScreenshotCommandResponse Screenshot(string format = "png") =>
		Send<ScreenshotCommandResponse>(new ScreenshotCommandRequest { Format = format });

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
}
