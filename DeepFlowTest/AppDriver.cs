namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Runtime.ExceptionServices;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

public sealed class AppDriver : IDisposable
{
	private readonly DriverCommandClient commandClient;
	private readonly ElementRegistry elementRegistry;
	private readonly ElementFactory elementFactory;
	private readonly ElementMatcherPlanner matcherPlanner;
	private readonly ElementWaiter elementWaiter;
	private readonly VisualTreeClient visualTreeClient;
	private readonly ElementFinder elementFinder;
	private readonly ElementRepairService elementRepairService;
	private readonly ElementQueryService queryService;
	private readonly MediaCaptureService mediaCaptureService;
	private readonly ElementCommandExecutor elementCommandExecutor;
	private readonly Keyboard keyboard;
	private readonly BindingFailureMonitor bindingFailureMonitor;
	private IDisposable? automaticBindingFailureCapture;
	private SemanticRecordingSession? automaticSemanticRecording;
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

		bindingFailureMonitor = new BindingFailureMonitor(Session, Options);
		commandClient = new DriverCommandClient(Session, () =>
		{
			if (Options.FailOnBindingFailures)
				bindingFailureMonitor.CheckpointAndThrowIfNeeded();
		});
		elementRegistry = new ElementRegistry();
		elementFactory = new ElementFactory(this);
		matcherPlanner = new ElementMatcherPlanner(elementFactory);
		elementWaiter = new ElementWaiter(Options);
		visualTreeClient = new VisualTreeClient(commandClient, elementRegistry, elementFactory);
		elementFinder = new ElementFinder(commandClient, visualTreeClient, elementFactory, matcherPlanner);
		elementRepairService = new ElementRepairService(elementFinder, visualTreeClient, elementFactory);
		queryService = new ElementQueryService(elementFinder, matcherPlanner, elementWaiter, visualTreeClient, elementFactory);
		mediaCaptureService = new MediaCaptureService(commandClient);
		elementCommandExecutor = new ElementCommandExecutor(commandClient, elementRepairService);
		keyboard = new Keyboard(this);
		try
		{
			if (Options.FailOnBindingFailures)
				automaticBindingFailureCapture = StartBindingFailureCapture();
			var autoSemanticRecordingOutputPath = Options.AutoSemanticRecordingOutputPath;
			if (autoSemanticRecordingOutputPath is not null && !string.IsNullOrWhiteSpace(autoSemanticRecordingOutputPath))
				automaticSemanticRecording = StartSemanticRecording(autoSemanticRecordingOutputPath, Options.AutoSemanticRecordingOptions);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			DisposeQuietly(automaticSemanticRecording);
			DisposeQuietly(automaticBindingFailureCapture);
			DisposeQuietly(Connection);
			throw;
		}
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

	public event EventHandler<BindingFailureEventArgs>? BindingFailureReceived
	{
		add => bindingFailureMonitor.FailureReceived += value;
		remove => bindingFailureMonitor.FailureReceived -= value;
	}

	internal ElementCommandExecutor ElementCommandExecutor => elementCommandExecutor;

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

	public Element GetElement(ElementSelector selector) =>
		queryService.GetElement(selector);

	public Element GetElement(Expression<Func<VisualTreeNodeDto, bool>> matcher) =>
		queryService.GetElement(matcher);

	public Element GetElement(Expression<Func<Element, bool?>> matcher, int timeoutMs = TimeoutDefaults.ElementQueryTimeoutMs) =>
		GetElement(matcher, timeoutMs, propNames: null);

	public Element GetElement(Expression<Func<Element, bool?>> matcher, int timeoutMs, IReadOnlyList<string>? propNames) =>
		queryService.GetElement(matcher, timeoutMs, propNames);

	public TElement GetElement<TElement>(Expression<Func<Element, bool?>> matcher, int timeoutMs = TimeoutDefaults.ElementQueryTimeoutMs)
		where TElement : Element =>
		queryService.GetElement<TElement>(matcher, timeoutMs, propNames: null);

	public TElement GetElement<TElement>(Expression<Func<Element, bool?>> matcher, int timeoutMs, IReadOnlyList<string>? propNames)
		where TElement : Element =>
		queryService.GetElement<TElement>(matcher, timeoutMs, propNames);

	public Element GetElement(
		Expression<Func<Element, bool?>> rootMatcher,
		Expression<Func<Element, bool?>> matcher,
		int timeoutMs = TimeoutDefaults.ElementQueryTimeoutMs) =>
		GetElement(rootMatcher, matcher, timeoutMs, propNames: null);

	public Element GetElement(
		Expression<Func<Element, bool?>> rootMatcher,
		Expression<Func<Element, bool?>> matcher,
		int timeoutMs,
		IReadOnlyList<string>? propNames) =>
		queryService.GetElement(rootMatcher, matcher, timeoutMs, propNames);

	public Element GetElement(Element root, Expression<Func<Element, bool?>> matcher, int timeoutMs = TimeoutDefaults.ElementQueryTimeoutMs) =>
		GetElement(root, matcher, timeoutMs, propNames: null);

	public Element GetElement(Element root, Expression<Func<Element, bool?>> matcher, int timeoutMs, IReadOnlyList<string>? propNames) =>
		queryService.GetElement(root, matcher, timeoutMs, propNames);

	public IReadOnlyList<Element> GetElements(ElementSelector selector, int maxMatches = 100) =>
		queryService.GetElements(selector, maxMatches);

	public IReadOnlyList<Element> GetElements(Expression<Func<VisualTreeNodeDto, bool>> matcher, int maxMatches = 100) =>
		queryService.GetElements(matcher, maxMatches);

	public IReadOnlyList<Element> GetElements(Expression<Func<Element, bool?>> matcher, int timeoutMs = TimeoutDefaults.ElementQueryTimeoutMs) =>
		GetElements(matcher, timeoutMs, propNames: null);

	public IReadOnlyList<Element> GetElements(Expression<Func<Element, bool?>> matcher, int timeoutMs, IReadOnlyList<string>? propNames) =>
		queryService.GetElements(matcher, timeoutMs, propNames);

	public IReadOnlyList<TElement> GetElements<TElement>(Expression<Func<Element, bool?>> matcher, int timeoutMs = TimeoutDefaults.ElementQueryTimeoutMs)
		where TElement : Element =>
		queryService.GetElements<TElement>(matcher, timeoutMs, propNames: null);

	public IReadOnlyList<TElement> GetElements<TElement>(Expression<Func<Element, bool?>> matcher, int timeoutMs, IReadOnlyList<string>? propNames)
		where TElement : Element =>
		queryService.GetElements<TElement>(matcher, timeoutMs, propNames);

	public IReadOnlyList<Element> GetElements(
		Expression<Func<Element, bool?>> rootMatcher,
		Expression<Func<Element, bool?>> matcher,
		int timeoutMs = TimeoutDefaults.ElementQueryTimeoutMs) =>
		GetElements(rootMatcher, matcher, timeoutMs, propNames: null);

	public IReadOnlyList<Element> GetElements(
		Expression<Func<Element, bool?>> rootMatcher,
		Expression<Func<Element, bool?>> matcher,
		int timeoutMs,
		IReadOnlyList<string>? propNames) =>
		queryService.GetElements(rootMatcher, matcher, timeoutMs, propNames);

	public IReadOnlyList<Element> GetElements(Element root, Expression<Func<Element, bool?>> matcher, int timeoutMs = TimeoutDefaults.ElementQueryTimeoutMs) =>
		GetElements(root, matcher, timeoutMs, propNames: null);

	public IReadOnlyList<Element> GetElements(Element root, Expression<Func<Element, bool?>> matcher, int timeoutMs, IReadOnlyList<string>? propNames) =>
		queryService.GetElements(root, matcher, timeoutMs, propNames);

	public TElement GetElement<TElement>(Expression<Func<TElement, bool?>> matcher)
		where TElement : Element =>
		queryService.GetElement(matcher);

	public IReadOnlyList<TElement> GetElements<TElement>(Expression<Func<TElement, bool?>> matcher, int maxMatches = 100)
		where TElement : Element =>
		queryService.GetElements(matcher, maxMatches);

	public VisualTreeSnapshot GetVisualTree(string? rootTargetId = null) =>
		visualTreeClient.GetVisualTree(rootTargetId);

	public VisualTreeSnapshot GetVisualTree(string? rootTargetId, IReadOnlyList<string>? propNames) =>
		visualTreeClient.GetVisualTree(rootTargetId, propNames);

	public VisualTreeSnapshot GetVisualTree(string? rootTargetId, IReadOnlyList<string>? propNames, int? maxNodeCount) =>
		visualTreeClient.GetVisualTree(rootTargetId, propNames, maxNodeCount);

	public IReadOnlyList<Element> GetRootElements() =>
		visualTreeClient.GetRootElements();

	public TResponse Send<TResponse>(IpcCommand command) =>
		commandClient.Send<TResponse>(command);

	public IDisposable StartBindingFailureCapture(BindingFailureOptions? options = null) =>
		bindingFailureMonitor.Start(options ?? Options.BindingFailures);

	public IReadOnlyList<BindingFailureDto> GetObservedBindingFailures() =>
		bindingFailureMonitor.GetObservedFailures();

	public void ClearObservedBindingFailures() =>
		bindingFailureMonitor.Clear();

	public void AssertNoBindingFailures(bool clear = true) =>
		bindingFailureMonitor.AssertNoFailures(clear);

	public ScreenshotCommandResponse CaptureScreenshot(string format = "png") =>
		mediaCaptureService.CaptureScreenshot(format);

	public byte[] Screenshot(ImageFormat format = ImageFormat.Jpeg) =>
		mediaCaptureService.Screenshot(format);

	public void Screenshot(string fileOutputPath) =>
		mediaCaptureService.SaveScreenshot(fileOutputPath);

	public static IDisposable Record(string fileOutputPath, string? windowTitle = null) =>
		MediaCaptureService.Record(fileOutputPath, windowTitle);

	public SemanticRecordingSession StartSemanticRecording(string outputFilePath, SemanticRecordingOptions? options = null) =>
		SemanticRecordingSession.Start(
			Session,
			outputFilePath,
			options ?? new SemanticRecordingOptions(),
			(int)Math.Max(1, Options.Timeout.TotalMilliseconds));

	internal static Func<ProcessStartInfo, IRecordingProcess> RecordingProcessFactory
	{
		get => MediaCaptureService.RecordingProcessFactory;
		set => MediaCaptureService.RecordingProcessFactory = value;
	}

	internal static string? RecordingFfmpegPathOverride
	{
		get => MediaCaptureService.RecordingFfmpegPathOverride;
		set => MediaCaptureService.RecordingFfmpegPathOverride = value;
	}

	internal void RefreshAfterPhysicalInput()
	{
		visualTreeClient.GetVisualTree();
	}

	internal static ScreenshotCommandResponse WaitForStableScreenshot(Func<ScreenshotCommandResponse> capture, string caller) =>
		MediaCaptureService.WaitForStableScreenshot(capture, caller);

	internal Element Repair(Element element) =>
		elementRepairService.Repair(element);

	internal void RegisterElement(Element element) =>
		elementRegistry.Register(element);

	internal void MoveElementRegistration(Element element, string oldTargetId, string newTargetId) =>
		elementRegistry.Move(element, oldTargetId, newTargetId);

	private static void DisposeQuietly(IDisposable? disposable)
	{
		try
		{
			disposable?.Dispose();
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
		}
	}

	public void Dispose()
	{
		if (disposed)
			return;

		disposed = true;
		Exception? pendingException = null;
		try
		{
			automaticSemanticRecording?.Dispose();
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			pendingException = ex;
		}

		try
		{
			if (Options.FailOnBindingFailures && Options.BindingFailures.AssertOnDispose)
				bindingFailureMonitor.AssertNoFailures(clear: true);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			pendingException ??= ex;
		}
		finally
		{
			automaticSemanticRecording = null;
			automaticBindingFailureCapture = null;
			bindingFailureMonitor.Dispose();
			Connection.Dispose();
		}

		if (pendingException is not null)
			ExceptionDispatchInfo.Capture(pendingException).Throw();
	}
}
