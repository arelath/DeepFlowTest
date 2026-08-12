namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq.Expressions;
using System.Runtime.ExceptionServices;
using DeepFlowTest.Assert.TestFrameworks;
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
	private readonly AppDriverDiagnosticsCollector diagnostics = new();
	private IDisposable? automaticBindingFailureCapture;
	private AutomaticDiagnosticsSession? automaticDiagnostics;
	private bool disposed;

	private AppDriver(
		AppConnection connection,
		AppDriverOptions options,
		Func<AppConnection, AppDriverOptions, IUnsafeAppDriverCommandSession>? sessionFactory = null,
		IUnsafeAppDriverCommandSession? session = null)
	{
		Connection = connection ?? throw new ArgumentNullException(nameof(connection));
		Options = options ?? throw new ArgumentNullException(nameof(options));
		Options.Validate();
		Session = session ?? (sessionFactory ?? ((candidateConnection, candidateOptions) => new NamedPipeAppDriverCommandSession(candidateConnection, candidateOptions)))(connection, options);

		bindingFailureMonitor = new BindingFailureMonitor(Session, Options);
		commandClient = new DriverCommandClient(Session, () =>
		{
			if (Options.FailOnBindingFailures)
				bindingFailureMonitor.CheckpointAndThrowIfNeeded();
		}, MarkDiagnosticsFailure);
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
		TestFrameworkProvider.AssertionFailure += OnAssertionFailure;
		try
		{
			if (Session is NamedPipeAppDriverCommandSession namedPipeSession)
				namedPipeSession.NegotiateControlConnection();
			ConfigurePayloadDiagnostics();
			if (Options.FailOnBindingFailures)
				automaticBindingFailureCapture = StartBindingFailureCapture();
			if (Connection.InjectorState != AppConnectionInjectorState.InjectionSkipped || Connection.ReusesPipe)
				StartAutomaticDiagnosticsSafely();
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			MarkDiagnosticsFailure(ex);
			automaticDiagnostics?.Complete();
			DisposeQuietly(automaticBindingFailureCapture);
			TestFrameworkProvider.AssertionFailure -= OnAssertionFailure;
			DisposeQuietly(Session as IDisposable);
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

	public string? AutomaticSemanticRecordingOutputPath { get; private set; }

	public string? AutomaticDiagnosticsArtifactDirectory => automaticDiagnostics?.ArtifactDirectory;

	public string? AutomaticDiagnosticsManifestPath => automaticDiagnostics?.ManifestPath;

	public IReadOnlyList<AppDriverDiagnostic> Diagnostics => diagnostics.Snapshot();

	internal IUnsafeAppDriverCommandSession Session { get; }

	public IUnsafeAppDriverCommandSession UnsafeCommands => Session;

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

	public static AppDriver CreateForTests(AppConnection connection, IUnsafeAppDriverCommandSession session, AppDriverOptions? options = null) =>
		new(connection, options ?? CreateTestOptions(), session: session);

	internal static AppDriver FromConnection(
		AppConnection connection,
		AppDriverOptions options,
		Func<AppConnection, AppDriverOptions, IUnsafeAppDriverCommandSession> sessionFactory) =>
		new(connection, options, sessionFactory);

	public Element GetElement(ElementSelector selector) =>
		queryService.GetElement(selector);

	public Element GetElement(Expression<Func<VisualTreeNodeDto, bool>> matcher) =>
		queryService.GetElement(matcher);

	public Element GetElement(Expression<Func<Element, bool?>> matcher, TimeSpan? timeout = null) =>
		queryService.GetElement(matcher, EffectiveElementQueryTimeout(timeout), propNames: null);

	public Element GetElement(Expression<Func<Element, bool?>> matcher, TimeSpan timeout, IReadOnlyList<string>? propNames) =>
		queryService.GetElement(matcher, DurationUtility.ToMilliseconds(timeout, nameof(timeout)), propNames);

	public TElement GetElement<TElement>(Expression<Func<Element, bool?>> matcher, TimeSpan? timeout = null)
		where TElement : Element =>
		queryService.GetElement<TElement>(matcher, EffectiveElementQueryTimeout(timeout), propNames: null);

	public TElement GetElement<TElement>(Expression<Func<Element, bool?>> matcher, TimeSpan timeout, IReadOnlyList<string>? propNames)
		where TElement : Element =>
		queryService.GetElement<TElement>(matcher, DurationUtility.ToMilliseconds(timeout, nameof(timeout)), propNames);

	public Element GetElement(
		Expression<Func<Element, bool?>> rootMatcher,
		Expression<Func<Element, bool?>> matcher,
		TimeSpan? timeout = null) =>
		queryService.GetElement(rootMatcher, matcher, EffectiveElementQueryTimeout(timeout), propNames: null);

	public Element GetElement(
		Expression<Func<Element, bool?>> rootMatcher,
		Expression<Func<Element, bool?>> matcher,
		TimeSpan timeout,
		IReadOnlyList<string>? propNames) =>
		queryService.GetElement(rootMatcher, matcher, DurationUtility.ToMilliseconds(timeout, nameof(timeout)), propNames);

	public Element GetElement(Element root, Expression<Func<Element, bool?>> matcher, TimeSpan? timeout = null) =>
		queryService.GetElement(root, matcher, EffectiveElementQueryTimeout(timeout), propNames: null);

	public Element GetElement(Element root, Expression<Func<Element, bool?>> matcher, TimeSpan timeout, IReadOnlyList<string>? propNames) =>
		queryService.GetElement(root, matcher, DurationUtility.ToMilliseconds(timeout, nameof(timeout)), propNames);

	public IReadOnlyList<Element> GetElements(ElementSelector selector, int maxMatches = 100) =>
		queryService.GetElements(selector, maxMatches);

	public IReadOnlyList<Element> GetElements(Expression<Func<VisualTreeNodeDto, bool>> matcher, int maxMatches = 100) =>
		queryService.GetElements(matcher, maxMatches);

	public IReadOnlyList<Element> GetElements(Expression<Func<Element, bool?>> matcher, TimeSpan? timeout = null) =>
		queryService.GetElements(matcher, EffectiveElementQueryTimeout(timeout), propNames: null);

	public IReadOnlyList<Element> GetElements(Expression<Func<Element, bool?>> matcher, TimeSpan timeout, IReadOnlyList<string>? propNames) =>
		queryService.GetElements(matcher, DurationUtility.ToMilliseconds(timeout, nameof(timeout)), propNames);

	public IReadOnlyList<TElement> GetElements<TElement>(Expression<Func<Element, bool?>> matcher, TimeSpan? timeout = null)
		where TElement : Element =>
		queryService.GetElements<TElement>(matcher, EffectiveElementQueryTimeout(timeout), propNames: null);

	public IReadOnlyList<TElement> GetElements<TElement>(Expression<Func<Element, bool?>> matcher, TimeSpan timeout, IReadOnlyList<string>? propNames)
		where TElement : Element =>
		queryService.GetElements<TElement>(matcher, DurationUtility.ToMilliseconds(timeout, nameof(timeout)), propNames);

	public IReadOnlyList<Element> GetElements(
		Expression<Func<Element, bool?>> rootMatcher,
		Expression<Func<Element, bool?>> matcher,
		TimeSpan? timeout = null) =>
		queryService.GetElements(rootMatcher, matcher, EffectiveElementQueryTimeout(timeout), propNames: null);

	public IReadOnlyList<Element> GetElements(
		Expression<Func<Element, bool?>> rootMatcher,
		Expression<Func<Element, bool?>> matcher,
		TimeSpan timeout,
		IReadOnlyList<string>? propNames) =>
		queryService.GetElements(rootMatcher, matcher, DurationUtility.ToMilliseconds(timeout, nameof(timeout)), propNames);

	public IReadOnlyList<Element> GetElements(Element root, Expression<Func<Element, bool?>> matcher, TimeSpan? timeout = null) =>
		queryService.GetElements(root, matcher, EffectiveElementQueryTimeout(timeout), propNames: null);

	public IReadOnlyList<Element> GetElements(Element root, Expression<Func<Element, bool?>> matcher, TimeSpan timeout, IReadOnlyList<string>? propNames) =>
		queryService.GetElements(root, matcher, DurationUtility.ToMilliseconds(timeout, nameof(timeout)), propNames);

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

	internal TResponse SendCommand<TResponse>(IpcCommand command) => commandClient.Send<TResponse>(command);

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
			DurationUtility.ToMilliseconds(Options.Timeout, nameof(Options.Timeout)));

	public void MarkDiagnosticsFailure(Exception? failure = null) =>
		automaticDiagnostics?.MarkFailure(failure);

	private void ConfigurePayloadDiagnostics()
	{
		if (Options.VirtualPointer.IsDefault)
			return;

		var response = Session.Send<StandardIpcResponse>(new ConfigureDiagnosticsCommandRequest
		{
			TimeoutMs = DurationUtility.ToMilliseconds(Options.Timeout, nameof(Options.Timeout)),
			VirtualPointer = new VirtualPointerOptionsDto
			{
				Enabled = Options.VirtualPointer.Enabled,
				ShowClickRipples = Options.VirtualPointer.ShowClickRipples,
				ShowDragTrail = Options.VirtualPointer.ShowDragTrail,
				HideDelayMs = DurationUtility.ToMilliseconds(Options.VirtualPointer.HideDelay, nameof(Options.VirtualPointer.HideDelay), allowZero: true),
				IncludeInScreenshots = Options.VirtualPointer.IncludeInScreenshots,
			},
		});
		DriverCommandClient.ThrowIfStandardFailure(response, "Configure diagnostics failed.");
	}

	private (AutomaticDiagnosticsOptions Options, string? TracePath) ResolveAutomaticDiagnosticsConfiguration()
	{
		if (!Options.AutoSemanticRecordingEnabled && string.IsNullOrWhiteSpace(Options.AutoSemanticRecordingOutputPath))
			return (Options.AutomaticDiagnostics, null);

		var automatic = Options.AutomaticDiagnostics;
		var recording = Options.AutoSemanticRecordingOptions;
		return (new AutomaticDiagnosticsOptions
		{
			Mode = AutomaticDiagnosticsMode.Always,
			OutputDirectory = automatic.OutputDirectory,
			MaximumArtifactSizeBytes = Math.Max(automatic.MaximumArtifactSizeBytes, recording.MaximumArtifactSizeBytes),
			FailureBufferSizeBytes = automatic.FailureBufferSizeBytes,
			RetentionPolicy = DiagnosticsRetentionPolicy.KeepAll,
			MaximumArtifactAge = automatic.MaximumArtifactAge,
			MaximumRetainedSessions = automatic.MaximumRetainedSessions,
			CaptureFinalScreenshotOnFailure = automatic.CaptureFinalScreenshotOnFailure,
			CaptureFinalTreeOnFailure = automatic.CaptureFinalTreeOnFailure,
			IncludeProcessLogs = automatic.IncludeProcessLogs,
			Recording = recording,
			ArtifactSink = automatic.ArtifactSink,
		}, string.IsNullOrWhiteSpace(Options.AutoSemanticRecordingOutputPath) ? null : Options.AutoSemanticRecordingOutputPath);
	}

	private void StartAutomaticDiagnosticsSafely()
	{
		try
		{
			var configuration = ResolveAutomaticDiagnosticsConfiguration();
			automaticDiagnostics = AutomaticDiagnosticsSession.Create(this, configuration.Options, diagnostics, configuration.TracePath);
			AutomaticSemanticRecordingOutputPath = automaticDiagnostics.TracePath;
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			var diagnostic = new AppDriverDiagnostic
			{
				Severity = AppDriverDiagnosticSeverity.Warning,
				Code = "automatic-diagnostics-start-failed",
				Message = "Automatic diagnostics could not be initialized; driver construction will continue.",
				Exception = ex,
			};
			diagnostics.Add(diagnostic);
			Trace.TraceWarning($"DeepFlowTest: {diagnostic.Message} {ex.Message}");
			try
			{
				Options.AutomaticDiagnostics.ArtifactSink?.Log(diagnostic);
			}
			catch (Exception sinkException) when (sinkException is not OutOfMemoryException && sinkException is not StackOverflowException)
			{
			}
		}
	}

	private static int EffectiveElementQueryTimeout(TimeSpan? timeout) =>
		DurationUtility.ToMilliseconds(
			timeout ?? TimeSpan.FromMilliseconds(TimeoutDefaults.ElementQueryTimeoutMs),
			nameof(timeout));

	private static AppDriverOptions CreateTestOptions() =>
		new()
		{
			AutoSemanticRecordingEnabled = false,
			AutomaticDiagnostics = new AutomaticDiagnosticsOptions { Mode = AutomaticDiagnosticsMode.Off },
		};

	private void OnAssertionFailure(string message) =>
		MarkDiagnosticsFailure(new InvalidOperationException(message));

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
			if (Options.FailOnBindingFailures && Options.BindingFailures.AssertOnDispose)
				bindingFailureMonitor.AssertNoFailures(clear: true);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			MarkDiagnosticsFailure(ex);
			pendingException ??= ex;
		}
		finally
		{
			automaticDiagnostics?.Complete();
			AutomaticSemanticRecordingOutputPath = automaticDiagnostics?.TracePath;
			automaticBindingFailureCapture = null;
			TestFrameworkProvider.AssertionFailure -= OnAssertionFailure;
			bindingFailureMonitor.Dispose();
			DisposeQuietly(Session as IDisposable);
			Connection.Dispose();
		}

		if (pendingException is not null)
			ExceptionDispatchInfo.Capture(pendingException).Throw();
	}
}
