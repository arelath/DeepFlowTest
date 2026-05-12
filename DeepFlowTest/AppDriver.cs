namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using System.Linq;
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

	public Element GetElement(ElementSelector selector) =>
		GetElements(selector, maxMatches: 1).SingleOrDefault()
		?? throw new AppDriverException(AppDriverErrorCodes.TargetNotFound, $"No element matched selector '{selector}'.");

	public IReadOnlyList<Element> GetElements(ElementSelector selector, int maxMatches = 100)
	{
		_ = selector ?? throw new ArgumentNullException(nameof(selector));
		var response = Send<FindElementCommandResponse>(new FindElementCommandRequest
		{
			Selector = selector.ToDto(),
			PropNames = selector.RequestedPropertyNames,
			MaxMatches = maxMatches,
		});

		return response.Matches
			.Select(match => Element.FromMatch(this, match, selector))
			.ToArray();
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

		return GetElement(element.Selector);
	}

	public void Dispose()
	{
		if (disposed)
			return;

		disposed = true;
		Connection.Dispose();
	}
}
