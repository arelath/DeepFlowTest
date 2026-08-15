namespace DeepFlowTest.Cli;

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

public static class Program
{
	public static int Main(string[] args)
	{
		return Run(args);
	}

	public static int Run(string[] args) => Run(args, services: null, stdout: null, stderr: null);

	public static int Run(string[] args, CliServices? services, TextWriter? stdout, TextWriter? stderr)
	{
		_ = args ?? throw new ArgumentNullException(nameof(args));
		services ??= new CliServices();
		stdout ??= Console.Out;
		stderr ??= Console.Error;
		var stopwatch = Stopwatch.StartNew();
		var commandPath = CliRootCommand.GetCommandPath(args);
		var commandName = string.IsNullOrWhiteSpace(commandPath) ? "help" : commandPath;
		var executionContext = new CliCommandExecutionContext(args, services, stdout, stderr, stopwatch);

		if (CliRootCommand.IsHelpRequest(args))
		{
			stdout.WriteLine(CliRootCommand.HelpText);
			return 0;
		}

		var root = CreateRootCommand(CreateCommandActions(executionContext));
		var parseResult = root.Parse(args, new ParserConfiguration());
		if (parseResult.Errors.Count != 0)
		{
			var message = string.Join(Environment.NewLine, parseResult.Errors.Select(static error => error.Message));
			var options = CreateOutputOptions(args, new CliDefaults());
			CliOutput.Write(CliResponseFactory.Error(commandName, AutomationErrorCodes.InvalidArguments, message, stopwatch), options, stdout);
			return ExitCodeMapper.Map(AutomationErrorCodes.InvalidArguments);
		}

		CliDefaults defaults;
		try
		{
			defaults = IsConfigReset(args) ? new CliDefaults() : services.DefaultsStore.Load();
		}
		catch (AutomationException ex)
		{
			var options = CreateOutputOptions(args, new CliDefaults());
			CliOutput.Write(CliResponseFactory.Error(commandName, ex.ErrorCode, ex.Message, stopwatch, ex.Details), options, stdout);
			return ExitCodeMapper.Map(ex.ErrorCode);
		}

		var commonOptions = CreateOutputOptions(args, defaults);
		try
		{
			executionContext.Configure(defaults, commonOptions);
			return parseResult.Invoke(new InvocationConfiguration
			{
				EnableDefaultExceptionHandler = false,
				Output = stdout,
				Error = stderr,
			});
		}
		catch (AutomationException ex)
		{
			CliOutput.Write(CliResponseFactory.Error(commandName, ex.ErrorCode, ex.Message, stopwatch, ex.Details), commonOptions, stdout);
			return ExitCodeMapper.Map(ex.ErrorCode);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			CliOutput.Write(CliResponseFactory.Error(commandName, AutomationErrorCodes.UnexpectedError, ex.Message, stopwatch), commonOptions, stdout);
			return ExitCodeMapper.Map(AutomationErrorCodes.UnexpectedError);
		}
	}

	public static RootCommand CreateRootCommand()
	{
		return CliRootCommand.Create();
	}

	internal static RootCommand CreateRootCommand(CliCommandActions actions)
	{
		return CliRootCommand.Create(actions);
	}

	public static bool IsHelpRequest(string[] args)
	{
		return CliRootCommand.IsHelpRequest(args);
	}

	private static CliCommandActions CreateCommandActions(CliCommandExecutionContext context) =>
		new()
		{
			Root = () => context.Execute(
				"help",
				targetBound: false,
				() => throw new AutomationException(AutomationErrorCodes.NotImplemented, "Command '' is not implemented.")),
			Config = () => context.NotImplemented("config"),
			ConfigGet = () => context.Execute("config get", targetBound: false, () =>
				context.Services.DefaultsStore.Get(GetConfigPositional(context.Args, 0)) ?? new object()),
			ConfigSet = () => context.Execute("config set", targetBound: false, () =>
			{
				var setKey = GetRequiredConfigPositional(context.Args, 0, "key");
				var setValue = GetRequiredConfigPositional(context.Args, 1, "value");
				context.Services.DefaultsStore.Set(setKey, setValue, HasOption(context.Args, "--json"));
				return new
				{
					key = setKey,
					value = context.Services.DefaultsStore.Get(setKey),
				};
			}),
			ConfigClear = () => context.Execute("config clear", targetBound: false, () =>
			{
				var clearKey = GetRequiredConfigPositional(context.Args, 0, "key");
				context.Services.DefaultsStore.Clear(clearKey);
				return new
				{
					key = clearKey,
					value = context.Services.DefaultsStore.Get(clearKey),
				};
			}),
			ConfigReset = () => context.Execute("config reset", targetBound: false, () =>
			{
				if (!HasOption(context.Args, "--yes"))
					throw new AutomationException(AutomationErrorCodes.InvalidArguments, "`config reset` requires --yes.");
				context.Services.DefaultsStore.Reset();
				return new { reset = true };
			}),
			Processes = () => context.Execute("processes", targetBound: false, () =>
				GetProcesses(context.Services, HasOption(context.Args, "--candidates-only") && !HasOption(context.Args, "--show-all"))),
			Ping = () => context.Execute("ping", targetBound: true, () =>
				SendProtocolCommand<PingCommandResponse>(
					context.Services,
					context.CommonOptions,
					new PingCommandRequest { TimeoutMs = context.CommonOptions.TimeoutMs },
					context.Stderr)),
			Pipe = () => context.NotImplemented("pipe"),
			PipeStatus = () => context.Execute("pipe status", targetBound: true, () =>
				SendProtocolCommand<PipeStatusCommandResponse>(
					context.Services,
					context.CommonOptions,
					new PipeStatusCommandRequest { TimeoutMs = context.CommonOptions.TimeoutMs },
					context.Stderr)),
			Tree = () => context.Execute("tree", targetBound: true, () =>
				ExecuteTree(context.Args, context.Services, context.Defaults, context.CommonOptions)),
			Find = () => context.Execute("find", targetBound: true, () =>
				ExecuteFind(context.Args, context.Services, context.Defaults, context.CommonOptions)),
			Node = () => context.Execute("node", targetBound: true, () =>
				ExecuteNode(context.Args, context.Services, context.Defaults, context.CommonOptions)),
			Props = () => context.Execute("props", targetBound: true, () =>
				ExecuteProps(context.Args, context.Services, context.Defaults, context.CommonOptions)),
			Selectors = () => context.Execute("selectors", targetBound: true, () =>
				ExecuteSelectors(context.Args, context.Services, context.Defaults, context.CommonOptions)),
			Screenshot = () => context.Execute("screenshot", targetBound: true, () =>
				ExecuteScreenshot(context.Args, context.Services, context.Defaults, context.CommonOptions)),
			Wait = () => context.Execute("wait", targetBound: true, () =>
				ExecuteWait(context.Args, context.Services, context.Defaults, context.CommonOptions)),
			Stream = () => context.NotImplemented("stream"),
			StreamVisualTree = () => context.Execute("stream visual-tree", targetBound: true, () =>
				ExecuteStream(context.Args, context.Services, context.Defaults, context.CommonOptions, ProtocolConstants.StreamKinds.VisualTree)),
			StreamVisualTreeDelta = () => context.Execute("stream visual-tree-delta", targetBound: true, () =>
				ExecuteStream(context.Args, context.Services, context.Defaults, context.CommonOptions, ProtocolConstants.StreamKinds.VisualTreeDelta)),
			StreamScreenshot = () => context.Execute("stream screenshot", targetBound: true, () =>
				ExecuteStream(context.Args, context.Services, context.Defaults, context.CommonOptions, ProtocolConstants.StreamKinds.Screenshot)),
			StreamEventLog = () => context.Execute("stream event-log", targetBound: true, () =>
				ExecuteStream(context.Args, context.Services, context.Defaults, context.CommonOptions, ProtocolConstants.StreamKinds.EventLog)),
			StreamBindingFailures = () => context.Execute("stream binding-failures", targetBound: true, () =>
				ExecuteStream(context.Args, context.Services, context.Defaults, context.CommonOptions, ProtocolConstants.StreamKinds.BindingFailures)),
			StreamSemanticRecording = () => context.Execute("stream semantic-recording", targetBound: true, () =>
				ExecuteStream(context.Args, context.Services, context.Defaults, context.CommonOptions, ProtocolConstants.StreamKinds.SemanticRecording)),
			Record = () => context.NotImplemented("record"),
			RecordSemantic = () => context.Execute("record semantic", targetBound: true, () =>
				ExecuteRecordSemantic(context.Args, context.Services, context.Defaults, context.CommonOptions)),
			Click = () => context.Execute("click", targetBound: true, () =>
				ExecuteClick(context.Args, context.Services, context.Defaults, context.CommonOptions)),
			Wheel = () => context.Execute("wheel", targetBound: true, () =>
				ExecuteMouseWheel(context.Args, context.Services, context.Defaults, context.CommonOptions)),
			Drag = () => context.Execute("drag", targetBound: true, () =>
				ExecuteDrag(context.Args, context.Services, context.Defaults, context.CommonOptions)),
			Focus = () => context.Execute("focus", targetBound: true, () =>
				ExecuteFocus(context.Args, context.Services, context.Defaults, context.CommonOptions)),
			Type = () => context.Execute("type", targetBound: true, () =>
				ExecuteType(context.Args, context.Services, context.Defaults, context.CommonOptions)),
			Key = () => context.Execute("key", targetBound: true, () =>
				ExecuteKey(context.Args, context.Services, context.Defaults, context.CommonOptions)),
			Set = () => context.Execute("set", targetBound: true, () =>
				ExecuteSet(context.Args, context.Services, context.Defaults, context.CommonOptions)),
			Raise = () => context.Execute("raise", targetBound: true, () =>
				ExecuteRaise(context.Args, context.Services, context.Defaults, context.CommonOptions)),
			Invoke = () => context.Execute("invoke", targetBound: true, () =>
				ExecuteInvoke(context.Args, context.Services, context.Defaults, context.CommonOptions)),
			Version = () => context.Execute("version", targetBound: false, () =>
				new ProductVersionData { ProductName = DeepFlowTest.ProductInfo.Name }),
		};

	private static ProtocolCommandData<TResponse> SendProtocolCommand<TResponse>(
		CliServices services,
		CliCommonOptions commonOptions,
		IpcCommand command,
		TextWriter stderr)
	{
		var target = services.TargetResolver.Resolve(commonOptions.ToTargetSelector());
		using var session = services.AppSessionService.Open(target, commonOptions.ToAttachOptions());
		if (commonOptions.Debug)
			stderr.WriteLine($"Connected to {target.ProcessName} ({target.ProcessId}) through protocol {session.Hello.ProtocolVersion}.");

		return new ProtocolCommandData<TResponse>
		{
			Target = target,
			Hello = session.Hello,
			Response = session.Send<TResponse>(command, commonOptions.TimeoutMs),
		};
	}

	private static TreeSnapshotData ExecuteTree(string[] args, CliServices services, CliDefaults defaults, CliCommonOptions commonOptions)
	{
		var includeHidden = CliArgumentReader.HasOption(args, "--include-hidden") || defaults.Commands.Tree.IncludeHidden;
		var propertySelection = GetTreePropertySelection(args, defaults, includeHidden);
		var properties = propertySelection.RequestProperties;
		var typeNames = GetTreeTypeNames(args, defaults);

		var limit = CliArgumentReader.GetInt(args, "--limit", defaults.Commands.Tree.Limit);
		using var session = OpenSession(services, commonOptions);
		var snapshot = ReadSnapshot(session, commonOptions, properties, limit);
		var options = new TreeSnapshotOptions
		{
			Shape = GetTreeShapeOption(args, defaults.Commands.Tree.Shape),
			RootTargetId = CliArgumentReader.GetOption(args, "--root", "--target-id") ?? defaults.Commands.Tree.Root,
			MaxDepth = CliArgumentReader.GetInt(args, "--max-depth", defaults.Commands.Tree.MaxDepth),
			Limit = limit,
			IncludeHidden = includeHidden,
			IncludeTypeNames = typeNames.Count != 0,
			TypeNames = typeNames,
			IncludePath = CliArgumentReader.HasOption(args, "--include-path") || defaults.Commands.Tree.IncludePath,
			UseShortIds = commonOptions.UseShortIds,
			Properties = propertySelection.OutputProperties,
			SuppressProperties = propertySelection.SuppressProperties,
		};
		return new TreeSnapshotService().Shape(snapshot, options);
	}

	private static FindResultData ExecuteFind(string[] args, CliServices services, CliDefaults defaults, CliCommonOptions commonOptions)
	{
		var outputProperties = GetRequestedProperties(args, defaults);
		using var session = OpenSession(services, commonOptions);
		var options = CreateFindOptions(args, defaults, commonOptions, outputProperties);
		var requestProperties = GetFindRequestProperties(options, outputProperties);
		var snapshot = ReadSnapshot(session, commonOptions, requestProperties, Math.Max(defaults.TreeLimit, options.Limit));
		var result = new FindSnapshotService().Find(snapshot, options);
		if (result.MatchCount == 0 && CliArgumentReader.HasOption(args, "--require-match"))
			throw new AutomationException(AutomationErrorCodes.NoMatch, "No matching nodes were found.");

		return result;
	}

	private static NodeResultData ExecuteNode(string[] args, CliServices services, CliDefaults defaults, CliCommonOptions commonOptions)
	{
		using var session = OpenSession(services, commonOptions);
		var properties = GetRequestedProperties(args, defaults);
		var snapshot = ReadSnapshot(session, commonOptions, properties, defaults.TreeLimit);
		return new NodeSnapshotService().GetNode(snapshot, CreateNodeOptions(args, commonOptions, properties));
	}

	private static PropsResultData ExecuteProps(string[] args, CliServices services, CliDefaults defaults, CliCommonOptions commonOptions)
	{
		using var session = OpenSession(services, commonOptions);
		var properties = GetRequestedProperties(args, defaults);
		var snapshot = ReadSnapshot(session, commonOptions, properties, defaults.TreeLimit);
		return new NodeSnapshotService().GetProps(snapshot, CreateNodeOptions(args, commonOptions, properties));
	}

	private static SelectorSuggestionData ExecuteSelectors(string[] args, CliServices services, CliDefaults defaults, CliCommonOptions commonOptions)
	{
		using var session = OpenSession(services, commonOptions);
		var properties = GetRequestedProperties(args, defaults);
		var snapshot = ReadSnapshot(session, commonOptions, properties, defaults.TreeLimit);
		var targetId = GetTargetIdArgument(args);
		var fullId = new TargetIdService().Resolve(targetId, snapshot);
		var node = snapshot.Nodes.First(node => node.TargetId == fullId);
		return new SelectorSuggestionService(new TargetIdService(), snapshot).Suggest(node, commonOptions.UseShortIds);
	}

	private static ScreenshotResultData ExecuteScreenshot(string[] args, CliServices services, CliDefaults defaults, CliCommonOptions commonOptions)
	{
		using var session = OpenSession(services, commonOptions);
		var format = GetImageFormatOption(args, defaults.Commands.Screenshot.ImageFormat);
		var targetId = CliArgumentReader.GetOption(args, "--target", "--target-id") ?? defaults.Commands.Screenshot.TargetId;
		var selector = ElementSelectorParser.FromArgs(args);
		if (string.IsNullOrWhiteSpace(targetId) && !selector.IsEmpty)
			targetId = new ElementResolver().Resolve(ReadSnapshot(session, commonOptions, defaults.PropertyNames, defaults.TreeLimit), selector).TargetId;
		else if (!string.IsNullOrWhiteSpace(targetId))
		{
			var snapshot = ReadSnapshot(session, commonOptions, defaults.PropertyNames, defaults.TreeLimit);
			targetId = new TargetIdService().Resolve(targetId!, snapshot);
		}

		var response = session.Send<ScreenshotCommandResponse>(
			new ScreenshotCommandRequest
			{
				Format = format,
				TargetId = targetId,
				TimeoutMs = commonOptions.TimeoutMs,
			},
			commonOptions.TimeoutMs);
		return new ScreenshotFileService().Process(response, new ScreenshotFileOptions
		{
			OutputPath = CliArgumentReader.GetOption(args, "--output", "--out") ?? defaults.Commands.Screenshot.OutputPath,
			IncludeBase64 = CliArgumentReader.HasOption(args, "--base64") || defaults.Commands.Screenshot.Base64,
		});
	}

	private static FindResultData ExecuteWait(string[] args, CliServices services, CliDefaults defaults, CliCommonOptions commonOptions)
	{
		var properties = GetRequestedProperties(args, defaults);
		using var session = OpenSession(services, commonOptions);
		var options = CreateFindOptions(args, defaults, commonOptions, properties);
		if (CliArgumentReader.HasOption(args, "--require-visible"))
			options.Visible = true;
		if (CliArgumentReader.HasOption(args, "--require-enabled"))
			options.Enabled = true;

		var interval = CliArgumentReader.GetInt(args, "--interval-ms", defaults.Commands.Wait.IntervalMs);
		var requiredMatches = CliArgumentReader.GetInt(args, "--match-count", defaults.Commands.Wait.MatchCount);
		using var cancellation = CreateConsoleCancellationSource();
		return new WaitExecutor().Execute(
			() => ReadSnapshot(session, commonOptions, properties, Math.Max(defaults.TreeLimit, options.Limit)),
			options,
			new WaitExecutionOptions(commonOptions.TimeoutMs, interval, requiredMatches),
			cancellation.Token);
	}

	private static CliResponseSequence ExecuteStream(
		string[] args,
		CliServices services,
		CliDefaults defaults,
		CliCommonOptions commonOptions,
		string streamKind)
	{
		var interval = CliArgumentReader.GetInt(args, "--interval-ms", defaults.StreamIntervalMs);
		if (interval < TimeoutDefaults.StreamMinimumIntervalMs)
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, $"Stream interval must be at least {TimeoutDefaults.StreamMinimumIntervalMs} ms.");

		var duration = CliArgumentReader.GetInt(args, "--duration-ms", defaults.StreamDurationMs);
		if (duration < 0)
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "Stream duration must be zero or greater.");

		var format = streamKind == ProtocolConstants.StreamKinds.Screenshot
			? GetImageFormatOption(args, defaults.Commands.Stream.ImageFormat)
			: defaults.Commands.Stream.ImageFormat;
		var properties = CliArgumentReader.GetStringList(args, "--props", defaults.Commands.Stream.Props);
		using var session = OpenSession(services, commonOptions);
		var request = new StartSendingCommandRequest
		{
			StreamKind = streamKind,
			IntervalMs = interval,
			PropNames = properties,
			TargetId = CliArgumentReader.GetOption(args, "--target", "--target-id"),
			Format = format,
			TimeoutMs = commonOptions.TimeoutMs,
			SemanticRecording = streamKind == ProtocolConstants.StreamKinds.SemanticRecording
				? CreateSemanticRecordingOptions(args)
				: null,
		};
		using var stream = session.StartStream(request, commonOptions.TimeoutMs);
		using var cancellation = CreateConsoleCancellationSource();
		List<CliResponseEnvelope> envelopes =
		[
			CliResponseFactory.Success($"stream {streamKind} start", stream.Start, Stopwatch.StartNew()),
		];

		var stopwatch = Stopwatch.StartNew();
		try
		{
			while (duration == 0 || stopwatch.ElapsedMilliseconds <= duration)
			{
				cancellation.Token.ThrowIfCancellationRequested();
				var remaining = duration == 0 ? commonOptions.TimeoutMs : duration - (int)stopwatch.ElapsedMilliseconds;
				var readTimeout = duration == 0
					? commonOptions.TimeoutMs
					: Math.Max(interval, Math.Min(commonOptions.TimeoutMs, remaining + interval));
				var frame = stream.ReadFrame(readTimeout, cancellation.Token);
				if (frame is null)
					break;

				envelopes.Add(CliResponseFactory.Success($"stream {streamKind} frame", frame, Stopwatch.StartNew()));
			}
		}
		catch (OperationCanceledException)
		{
		}

		var stop = session.Send<StopSendingCommandResponse>(
			new StopSendingCommandRequest
			{
				SubscriptionId = stream.Start.SubscriptionId,
				TimeoutMs = Math.Min(commonOptions.TimeoutMs, TimeoutDefaults.StreamStopTimeoutMs),
			},
			Math.Min(commonOptions.TimeoutMs, TimeoutDefaults.StreamStopTimeoutMs));
		envelopes.Add(CliResponseFactory.Success($"stream {streamKind} stop", stop, Stopwatch.StartNew()));

		return new CliResponseSequence(envelopes);
	}

	private static SemanticRecordingFileData ExecuteRecordSemantic(
		string[] args,
		CliServices services,
		CliDefaults defaults,
		CliCommonOptions commonOptions)
	{
		var outputPath = CliArgumentReader.GetOption(args, "--output", "--out");
		if (string.IsNullOrWhiteSpace(outputPath))
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "record semantic requires --out.");

		var interval = CliArgumentReader.GetInt(args, "--interval-ms", defaults.StreamIntervalMs);
		if (interval < TimeoutDefaults.StreamMinimumIntervalMs)
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, $"Stream interval must be at least {TimeoutDefaults.StreamMinimumIntervalMs} ms.");

		var duration = CliArgumentReader.GetInt(args, "--duration-ms", defaults.StreamDurationMs);
		if (duration < 0)
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "Recording duration must be zero or greater.");

		var fullOutputPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputPath));
		var directory = Path.GetDirectoryName(fullOutputPath);
		if (!string.IsNullOrEmpty(directory))
			Directory.CreateDirectory(directory);

		var outputFormat = GetSemanticRecordingOutputFormat(args);
		var properties = CliArgumentReader.GetStringList(args, "--props", defaults.Commands.Stream.Props);
		using var session = OpenSession(services, commonOptions);
		IAutomationStreamSession? stream = null;
		var droppedActions = 0;
		StopSendingCommandResponse? stop = null;
		using var cancellation = CreateConsoleCancellationSource();
		using var writer = new StreamWriter(new FileStream(fullOutputPath, FileMode.Create, FileAccess.Write, FileShare.Read));
		using var recordingWriter = SemanticRecordingFrameWriter.Create(writer, outputFormat);

		try
		{
			stream = session.StartStream(new StartSendingCommandRequest
			{
				StreamKind = ProtocolConstants.StreamKinds.SemanticRecording,
				IntervalMs = interval,
				PropNames = properties,
				TargetId = CliArgumentReader.GetOption(args, "--target", "--target-id"),
				TimeoutMs = commonOptions.TimeoutMs,
				SemanticRecording = CreateSemanticRecordingOptions(args),
			}, commonOptions.TimeoutMs);

			var stopwatch = Stopwatch.StartNew();
			while (duration == 0 || stopwatch.ElapsedMilliseconds <= duration)
			{
				cancellation.Token.ThrowIfCancellationRequested();
				var remaining = duration == 0 ? commonOptions.TimeoutMs : duration - (int)stopwatch.ElapsedMilliseconds;
				var readTimeout = duration == 0
					? commonOptions.TimeoutMs
					: Math.Max(interval, Math.Min(commonOptions.TimeoutMs, remaining + interval));
				var frame = stream.ReadFrame(readTimeout, cancellation.Token);
				if (frame is null)
					break;
				if (frame.Error is not null)
					throw new AutomationException(frame.Error.Code, frame.Error.Message);
				if (frame.Data is null)
					continue;

				var batch = MessagePacker.ConvertTo<SemanticRecordingBatch>(frame.Data);
				droppedActions += Math.Max(0, batch.DroppedActionCount);
				recordingWriter.WriteDroppedActionCount(batch.DroppedActionCount);
				foreach (var recordingFrame in batch.Frames ?? [])
					recordingWriter.WriteFrame(recordingFrame);
			}
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			if (stream is not null)
			{
				stop = session.Send<StopSendingCommandResponse>(
					new StopSendingCommandRequest
					{
						SubscriptionId = stream.Start.SubscriptionId,
						TimeoutMs = Math.Min(commonOptions.TimeoutMs, TimeoutDefaults.StreamStopTimeoutMs),
					},
					Math.Min(commonOptions.TimeoutMs, TimeoutDefaults.StreamStopTimeoutMs));
				stream.Dispose();
			}
		}

		return new SemanticRecordingFileData
		{
			OutputPath = fullOutputPath,
			RecordingFormat = FormatSemanticRecordingOutputFormat(outputFormat),
			FramesWritten = recordingWriter.FramesWritten,
			DroppedActionCount = droppedActions,
			Stop = stop,
		};
	}

	private static SemanticRecordingOptionsDto CreateSemanticRecordingOptions(IReadOnlyList<string> args) =>
		new()
		{
			TextIdleMs = CliArgumentReader.GetInt(args, "--text-idle-ms", 400),
			MaxQueuedActions = CliArgumentReader.GetInt(args, "--max-queued-actions", 1000),
			MaxBatchFrames = CliArgumentReader.GetInt(args, "--max-batch-frames", 100),
			MaxNodeCount = CliArgumentReader.GetInt(args, "--limit", VisualTreeDefaults.DefaultMaxNodeCount),
		};

	private static SemanticRecordingOutputFormat GetSemanticRecordingOutputFormat(IReadOnlyList<string> args)
	{
		var rawValue = CliArgumentReader.GetOption(args, "--recording-format");
		if (string.IsNullOrWhiteSpace(rawValue))
			return SemanticRecordingOutputFormat.CondensedAgent;

		return rawValue.Trim().ToLowerInvariant() switch
		{
			"condensed-agent" or "agent" or "text" => SemanticRecordingOutputFormat.CondensedAgent,
			"condensed-diagnostic" or "diagnostic" => SemanticRecordingOutputFormat.CondensedDiagnostic,
			"compact-json" or "json" => SemanticRecordingOutputFormat.CompactJson,
			"raw-json" => SemanticRecordingOutputFormat.RawJson,
			_ => throw new AutomationException(AutomationErrorCodes.InvalidArguments, "Recording format must be condensed-agent, condensed-diagnostic, compact-json, or raw-json."),
		};
	}

	private static string FormatSemanticRecordingOutputFormat(SemanticRecordingOutputFormat outputFormat) =>
		outputFormat switch
		{
			SemanticRecordingOutputFormat.CondensedAgent => "condensed-agent",
			SemanticRecordingOutputFormat.CondensedDiagnostic => "condensed-diagnostic",
			SemanticRecordingOutputFormat.CompactJson => "compact-json",
			SemanticRecordingOutputFormat.RawJson => "raw-json",
			_ => outputFormat.ToString(),
		};

	private static IAutomationSession OpenSession(CliServices services, CliCommonOptions commonOptions)
	{
		var target = services.TargetResolver.Resolve(commonOptions.ToTargetSelector());
		return services.AppSessionService.Open(target, commonOptions.ToAttachOptions());
	}

	private static VisualTreeSnapshot ReadSnapshot(IAutomationSession session, CliCommonOptions commonOptions, IReadOnlyList<string> properties, int limit)
	{
		var response = session.Send<object>(
			new GetVisualTreeCommandRequest
			{
				PropNames = properties,
				AsSnapshot = true,
				IncludeHidden = true,
				MaxNodeCount = limit,
				TimeoutMs = commonOptions.TimeoutMs,
			},
			commonOptions.TimeoutMs);
		return new VisualTreeResponseReader().Read(response, properties);
	}

	private static IReadOnlyList<string> GetRequestedProperties(string[] args, CliDefaults defaults) =>
		CliArgumentReader.GetStringList(args, "--props", defaults.PropertyNames);

	private static IReadOnlyList<string> GetFindRequestProperties(
		FindSnapshotOptions options,
		IEnumerable<string> outputProperties)
	{
		var properties = outputProperties.ToList();
		void Add(string? property)
		{
			if (!string.IsNullOrWhiteSpace(property) && !properties.Contains(property, StringComparer.Ordinal))
				properties.Add(property);
		}

		if (!string.IsNullOrWhiteSpace(options.Name))
		{
			Add(KnownProperties.Name);
			Add(KnownProperties.AutomationName);
		}
		if (!string.IsNullOrWhiteSpace(options.AutomationId))
		{
			Add(KnownProperties.AutomationId);
			Add(KnownProperties.Id);
		}
		if (!string.IsNullOrWhiteSpace(options.Text))
		{
			foreach (var property in KnownProperties.TextualIdentityPropertyNames)
				Add(property);
		}
		Add(options.PropertyEquals?.Key);
		Add(options.PropertyContains?.Key);
		Add(options.PropertyRegex?.Key);
		if (options.Visible.HasValue)
			Add(KnownProperties.IsVisible);
		if (options.Enabled.HasValue)
			Add(KnownProperties.IsEnabled);

		return properties;
	}

	private static TreePropertySelection GetTreePropertySelection(string[] args, CliDefaults defaults, bool includeHidden)
	{
		var rawProperties = CliArgumentReader.GetOption(args, "--props");
		IReadOnlyList<string> outputProperties = rawProperties is null
			? defaults.Commands.Tree.Props.ToArray()
			: string.Equals(rawProperties, "default", StringComparison.OrdinalIgnoreCase)
				? CliDefaults.CreateDefaultPropertyList()
				: string.Equals(rawProperties, "none", StringComparison.OrdinalIgnoreCase)
					? []
					: CliArgumentReader.SplitCsv(rawProperties);

		var requestProperties = outputProperties.ToList();
		if (!includeHidden && !requestProperties.Contains(KnownProperties.IsVisible, StringComparer.Ordinal))
			requestProperties.Add(KnownProperties.IsVisible);

		return new TreePropertySelection(
			requestProperties.Distinct(StringComparer.Ordinal).ToArray(),
			outputProperties,
			string.Equals(rawProperties, "none", StringComparison.OrdinalIgnoreCase));
	}

	private static IReadOnlyList<string> GetTreeTypeNames(string[] args, CliDefaults defaults)
	{
		var rawTypeNames = CliArgumentReader.GetOption(args, "--type-names");
		if (rawTypeNames is not null)
			return CliArgumentReader.SplitCsv(rawTypeNames);

		return defaults.Commands.Tree.TypeNames is { } configuredTypeNames
			? configuredTypeNames
			: [];
	}

	private static TreeShape GetTreeShapeOption(string[] args, TreeShape defaultValue)
	{
		var value = CliArgumentReader.GetOption(args, "--shape");
		return value is null ? defaultValue : CliValueParser.ParseTreeShape(value);
	}

	private static ImageFormat GetImageFormatOption(string[] args, ImageFormat defaultValue)
	{
		var value = CliArgumentReader.GetOption(args, "--image-format");
		return value is null ? defaultValue : CliValueParser.ParseImageFormat(value);
	}

	private static CliClickButton GetClickButtonOption(string[] args, MouseButtonKind defaultValue)
	{
		var value = CliArgumentReader.GetOption(args, "--button");
		return value is null
			? CliValueParser.ParseClickButton(ProtocolValueMapper.FormatMouseButton(defaultValue))
			: CliValueParser.ParseClickButton(value);
	}

	private static FindSnapshotOptions CreateFindOptions(
		string[] args,
		CliDefaults defaults,
		CliCommonOptions commonOptions,
		IReadOnlyList<string> properties)
	{
		var include = GetFindIncludeSections(args, defaults);
		return new FindSnapshotOptions
		{
			TypeName = CliArgumentReader.GetOption(args, "--type") ?? defaults.Commands.Find.Type,
			TypeContains = CliArgumentReader.GetOption(args, "--type-contains") ?? defaults.Commands.Find.TypeContains,
			Name = CliArgumentReader.GetOption(args, "--name") ?? defaults.Commands.Find.Name,
			AutomationId = CliArgumentReader.GetOption(args, "--automation-id") ?? defaults.Commands.Find.AutomationId,
			Text = CliArgumentReader.GetOption(args, "--text") ?? defaults.Commands.Find.Text,
			PropertyEquals = CliArgumentReader.GetKeyValue(args, "--property", "--prop") ?? ParseDefaultKeyValue(defaults.Commands.Find.PropertyEquals),
			PropertyContains = CliArgumentReader.GetKeyValue(args, "--property-contains", "--contains") ?? ParseDefaultKeyValue(defaults.Commands.Find.PropertyContains),
			PropertyRegex = CliArgumentReader.GetKeyValue(args, "--property-regex", "--regex") ?? ParseDefaultKeyValue(defaults.Commands.Find.PropertyRegex),
			Visible = CliArgumentReader.HasOption(args, "--visible") || defaults.Commands.Find.Visible ? true : null,
			Enabled = CliArgumentReader.HasOption(args, "--enabled") || defaults.Commands.Find.Enabled ? true : null,
			CaseSensitive = CliArgumentReader.HasOption(args, "--case-sensitive") || defaults.Commands.Find.CaseSensitive,
			Limit = CliArgumentReader.GetInt(args, "--limit", defaults.FindLimit),
			IncludePath = CliArgumentReader.HasOption(args, "--include-path") || include.Contains("path"),
			IncludeProperties = CliArgumentReader.HasOption(args, "--include-properties") || include.Contains("properties"),
			IncludeChildren = CliArgumentReader.HasOption(args, "--include-children") || include.Contains("children"),
			IncludeAncestors = CliArgumentReader.HasOption(args, "--include-ancestors") || include.Contains("ancestors"),
			UseShortIds = commonOptions.UseShortIds,
			Properties = properties,
		};
	}

	private static HashSet<string> GetFindIncludeSections(string[] args, CliDefaults defaults)
	{
		var sections = new HashSet<string>(defaults.Commands.Find.Include, StringComparer.OrdinalIgnoreCase);
		var include = CliArgumentReader.GetOption(args, "--include");
		if (include is not null)
		{
			foreach (var section in CliArgumentReader.SplitCsv(include))
			{
				if (section is not ("path" or "properties" or "children" or "ancestors"))
					throw new AutomationException(AutomationErrorCodes.InvalidArguments, $"Unsupported find include section '{section}'.");
				sections.Add(section);
			}
		}

		return sections;
	}

	private static KeyValuePair<string, string>? ParseDefaultKeyValue(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return null;

		var separator = value.IndexOf('=');
		if (separator <= 0)
			throw new AutomationException(AutomationErrorCodes.InvalidConfig, $"Default selector value '{value}' must use name=value.");

		return new KeyValuePair<string, string>(value[..separator], value[(separator + 1)..]);
	}

	private static NodeSnapshotOptions CreateNodeOptions(string[] args, CliCommonOptions commonOptions, IReadOnlyList<string> properties)
	{
		return new NodeSnapshotOptions
		{
			TargetId = GetTargetIdArgument(args),
			IncludeAncestors = CliArgumentReader.HasOption(args, "--include-ancestors", "--ancestors"),
			IncludeChildren = CliArgumentReader.HasOption(args, "--include-children", "--children"),
			IncludeSubtree = CliArgumentReader.HasOption(args, "--include-subtree", "--subtree"),
			SubtreeDepth = CliArgumentReader.GetInt(args, "--subtree-depth", -1),
			IncludePath = CliArgumentReader.HasOption(args, "--include-path"),
			UseShortIds = commonOptions.UseShortIds,
			Properties = properties,
		};
	}

	private static string GetTargetIdArgument(string[] args)
	{
		var targetId = CliArgumentReader.GetOption(args, "--target", "--target-id");
		if (string.IsNullOrWhiteSpace(targetId))
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "A target ID is required.");

		return targetId!;
	}

	private static AutomationExecutionOptions CreateAutomationOptions(CliCommonOptions commonOptions, CliDefaults defaults) =>
		new(
			commonOptions.TimeoutMs,
			defaults.TreeLimit,
			defaults.PropertyNames,
			commonOptions.After switch
			{
				"none" => ObservationMode.None,
				"tree" => ObservationMode.Tree,
				_ => ObservationMode.Target,
			},
			commonOptions.UseShortIds)
		{
			TreeShape = defaults.Commands.Tree.Shape,
		};

	private static ActionExecutionResult ExecuteClick(string[] args, CliServices services, CliDefaults defaults, CliCommonOptions commonOptions)
	{
		new ActionGate().Demand("click", commonOptions);
		var button = GetClickButtonOption(args, defaults.Commands.Click.Button);
		var count = CliArgumentReader.GetInt(args, "--count", 1);
		var isDouble = CliArgumentReader.HasOption(args, "--double") || defaults.Commands.Click.Double;
		if (count <= 0)
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "Click count must be greater than zero.");

		using var session = OpenSession(services, commonOptions);
		return new ActionExecutor().Execute(
			"click",
			session,
			CreateAutomationOptions(commonOptions, defaults),
			ElementSelectorParser.FromArgs(args),
			targetId =>
			{
				if (isDouble || button == CliClickButton.Double)
				{
					return new KnownRoutedEventCommandRequest
					{
						TargetId = targetId ?? string.Empty,
						EventName = "MouseDoubleClick",
					};
				}

				return new ClickCommandRequest
				{
					TargetId = targetId ?? string.Empty,
					MouseButton = CliValueParser.ToMouseButton(button),
					ClickCount = count,
				};
			},
			requireElementTarget: true);
	}

	private static ActionExecutionResult ExecuteMouseWheel(string[] args, CliServices services, CliDefaults defaults, CliCommonOptions commonOptions)
	{
		new ActionGate().Demand("wheel", commonOptions);
		var delta = CliArgumentReader.GetInt(args, "--delta", defaults.Commands.Wheel.Delta);
		if (delta == 0)
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "Mouse wheel delta must not be zero.");

		using var session = OpenSession(services, commonOptions);
		return new ActionExecutor().Execute(
			"wheel",
			session,
			CreateAutomationOptions(commonOptions, defaults),
			ElementSelectorParser.FromArgs(args),
			targetId => new MouseWheelCommandRequest
			{
				TargetId = targetId ?? string.Empty,
				Delta = delta,
			},
			requireElementTarget: true);
	}

	private static TwoTargetActionExecutionResult ExecuteDrag(string[] args, CliServices services, CliDefaults defaults, CliCommonOptions commonOptions)
	{
		new ActionGate().Demand("drag", commonOptions);
		var sourceSelector = ElementSelectorParser.FromArgs(args);
		var destinationSelector = ElementSelectorParser.FromArgs(args, "to");
		if (sourceSelector.IsEmpty)
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "The drag command requires a source target or selector.");
		if (destinationSelector.IsEmpty)
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "The drag command requires a destination target or selector.");

		var dragDefaults = defaults.Commands.Drag;
		var durationMs = CliArgumentReader.GetInt(args, "--duration-ms", dragDefaults.DurationMs);
		var holdMs = CliArgumentReader.GetInt(args, "--hold-ms", dragDefaults.HoldMs);
		var stepIntervalMs = CliArgumentReader.GetInt(args, "--step-interval-ms", dragDefaults.StepIntervalMs);
		var postDropWaitMs = CliArgumentReader.GetInt(args, "--post-drop-wait-ms", dragDefaults.PostDropWaitMs);
		var sourceAnchorX = CliArgumentReader.GetDouble(args, "--source-anchor-x", dragDefaults.SourceAnchorX);
		var sourceAnchorY = CliArgumentReader.GetDouble(args, "--source-anchor-y", dragDefaults.SourceAnchorY);
		var destinationAnchorX = CliArgumentReader.GetDouble(args, "--destination-anchor-x", dragDefaults.DestinationAnchorX);
		var destinationAnchorY = CliArgumentReader.GetDouble(args, "--destination-anchor-y", dragDefaults.DestinationAnchorY);
		var useInjectedEvents = CliArgumentReader.GetBool(args, "--injected-events", dragDefaults.UseInjectedEvents);
		var foreground = CliArgumentReader.GetBool(args, "--foreground", dragDefaults.Foreground);
		var validateSameProcess = CliArgumentReader.GetBool(args, "--validate-same-process", dragDefaults.ValidateSameProcess);

		using var session = OpenSession(services, commonOptions);
		return new ActionExecutor().ExecuteTwoTarget(
			"drag",
			session,
			CreateAutomationOptions(commonOptions, defaults),
			sourceSelector,
			destinationSelector,
			(sourceTargetId, destinationTargetId) => new DragAndDropCommandRequest
			{
				TargetId = sourceTargetId,
				DestinationTargetId = destinationTargetId,
				DurationMs = durationMs,
				HoldMs = holdMs,
				StepIntervalMs = stepIntervalMs,
				PostDropWaitMs = postDropWaitMs,
				SourceAnchorX = sourceAnchorX,
				SourceAnchorY = sourceAnchorY,
				DestinationAnchorX = destinationAnchorX,
				DestinationAnchorY = destinationAnchorY,
				UseInjectedEvents = useInjectedEvents,
				EnsureForeground = foreground,
				ValidateSameProcess = validateSameProcess,
				TimeoutMs = commonOptions.TimeoutMs,
			});
	}

	private static ActionExecutionResult ExecuteFocus(string[] args, CliServices services, CliDefaults defaults, CliCommonOptions commonOptions)
	{
		new ActionGate().Demand("focus", commonOptions);
		using var session = OpenSession(services, commonOptions);
		return new ActionExecutor().Execute(
			"focus",
			session,
			CreateAutomationOptions(commonOptions, defaults),
			ElementSelectorParser.FromArgs(args),
			targetId => new FocusCommandRequest { TargetId = targetId ?? string.Empty },
			requireElementTarget: true,
			afterProperties: new[] { KnownProperties.IsFocused, KnownProperties.IsKeyboardFocused, KnownProperties.IsKeyboardFocusWithin });
	}

	private static ActionExecutionResult ExecuteType(string[] args, CliServices services, CliDefaults defaults, CliCommonOptions commonOptions)
	{
		new ActionGate().Demand("type", commonOptions);
		var text = CliArgumentReader.GetOption(args, "--value", "--text") ?? defaults.Commands.Type.Text;
		if (text is null)
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "The type command requires --value or --text.");

		var selector = ElementSelectorParser.FromArgs(args);
		selector.Text = CliArgumentReader.GetOption(args, "--selector-text");

		using var session = OpenSession(services, commonOptions);
		return new ActionExecutor().Execute(
			"type",
			session,
			CreateAutomationOptions(commonOptions, defaults),
			selector,
			targetId => new TypeTextCommandRequest
			{
				Text = text,
				TargetId = targetId,
				ClearFirst = CliArgumentReader.HasOption(args, "--clear-first") || defaults.Commands.Type.ClearFirst,
			},
			requireElementTarget: false,
			afterProperties: new[] { KnownProperties.Text, KnownProperties.Content });
	}

	private static ActionExecutionResult ExecuteKey(string[] args, CliServices services, CliDefaults defaults, CliCommonOptions commonOptions)
	{
		new ActionGate().Demand("key", commonOptions);
		var keys = CliArgumentReader.GetOption(args, "--keys") ?? defaults.Commands.Key.Keys;
		if (string.IsNullOrWhiteSpace(keys))
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "The key command requires --keys.");
		ValidateKeys(keys!);

		var selector = ElementSelectorParser.FromArgs(args);

		using var session = OpenSession(services, commonOptions);
		return new ActionExecutor().Execute(
			"key",
			session,
			CreateAutomationOptions(commonOptions, defaults),
			selector,
			targetId => new KeyPressCommandRequest
			{
				Keys = keys,
				TargetId = targetId,
				DelayMs = CliArgumentReader.GetInt(args, "--delay-ms", defaults.KeyDelayMs),
				EnsureForeground = CliArgumentReader.GetBool(args, "--foreground", defaults.Commands.Key.Foreground),
			},
			requireElementTarget: false,
			afterProperties: new[] { KnownProperties.Text, KnownProperties.Content, KnownProperties.IsKeyboardFocused, KnownProperties.IsKeyboardFocusWithin });
	}

	private static ActionExecutionResult ExecuteSet(string[] args, CliServices services, CliDefaults defaults, CliCommonOptions commonOptions)
	{
		new ActionGate().Demand("set", commonOptions);
		var property = CliArgumentReader.GetOption(args, "--property") ?? defaults.Commands.Set.Property;
		if (string.IsNullOrWhiteSpace(property))
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "The set command requires --property.");

		var rawValue = CliArgumentReader.GetOption(args, "--value") ?? defaults.Commands.Set.Value;
		if (rawValue is null)
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "The set command requires --value.");

		using var session = OpenSession(services, commonOptions);
		return new ActionExecutor().Execute(
			"set",
			session,
			CreateAutomationOptions(commonOptions, defaults),
			ElementSelectorParser.FromArgs(args),
			targetId => new SetPropertyCommandRequest
			{
				TargetId = targetId ?? string.Empty,
				PropertyName = property!,
				PropertyValue = ParseJsonScalar(rawValue),
			},
			requireElementTarget: true,
			afterProperties: new[] { property! });
	}

	private static ActionExecutionResult ExecuteRaise(string[] args, CliServices services, CliDefaults defaults, CliCommonOptions commonOptions)
	{
		new ActionGate().Demand("raise", commonOptions);
		var eventName = CliArgumentReader.GetOption(args, "--event") ?? defaults.Commands.Raise.Event;
		if (string.IsNullOrWhiteSpace(eventName))
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "The raise command requires --event.");
		if (!KnownRoutedEvents.Contains(eventName!))
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, $"Routed event '{eventName}' is not allow-listed.");

		using var session = OpenSession(services, commonOptions);
		return new ActionExecutor().Execute(
			"raise",
			session,
			CreateAutomationOptions(commonOptions, defaults),
			ElementSelectorParser.FromArgs(args),
			targetId => new KnownRoutedEventCommandRequest
			{
				TargetId = targetId ?? string.Empty,
				EventName = eventName!,
			},
			requireElementTarget: true);
	}

	private static ActionExecutionResult ExecuteInvoke(string[] args, CliServices services, CliDefaults defaults, CliCommonOptions commonOptions)
	{
		var code = CliArgumentReader.GetOption(args, "--code") ?? defaults.Commands.Invoke.Code;
		var operation = CliArgumentReader.GetOption(args, "--operation") ?? defaults.Commands.Invoke.Operation;
		var arbitrary = !string.IsNullOrWhiteSpace(code);
		new ActionGate().Demand("invoke", commonOptions, arbitraryInvoke: arbitrary);
		if (string.IsNullOrWhiteSpace(operation) && string.IsNullOrWhiteSpace(code))
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "The invoke command requires --operation or --code.");
		if (!string.IsNullOrWhiteSpace(operation) && !string.IsNullOrWhiteSpace(code))
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, "The invoke command accepts either --operation or --code, not both.");

		if (!string.IsNullOrWhiteSpace(operation) && !KnownOperations.Contains(operation!))
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, $"Known operation '{operation}' is not allow-listed.");

		object? parsedCode = null;
		if (!string.IsNullOrWhiteSpace(code))
			parsedCode = ParseJsonScalarStrict(code!);

		using var session = OpenSession(services, commonOptions);
		return new ActionExecutor().Execute(
			"invoke",
			session,
			CreateAutomationOptions(commonOptions, defaults),
			ElementSelectorParser.FromArgs(args),
			targetId => !string.IsNullOrWhiteSpace(operation)
				? new KnownOperationCommandRequest { TargetId = targetId ?? string.Empty, Operation = operation! }
				: new InvokeCommandRequest { TargetId = targetId ?? string.Empty, Code = parsedCode, AllowUnsafeCode = commonOptions.AllowArbitraryInvoke },
			requireElementTarget: true);
	}

	private static object? ParseJsonScalar(string value)
	{
		try
		{
			using var document = JsonDocument.Parse(value);
			return ParseJsonScalarFromElement(document.RootElement);
		}
		catch (JsonException)
		{
			return value;
		}
	}

	private static object? ParseJsonScalarStrict(string value)
	{
		try
		{
			using var document = JsonDocument.Parse(value);
			return ParseJsonScalarFromElement(document.RootElement);
		}
		catch (JsonException ex)
		{
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, $"Invalid JSON payload: {ex.Message}");
		}
	}

	private static object? ParseJsonScalarFromElement(JsonElement root)
	{
		return root.ValueKind switch
		{
			JsonValueKind.String => root.GetString(),
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.Number when root.TryGetInt64(out var longValue) => longValue,
			JsonValueKind.Number => root.GetDouble(),
			JsonValueKind.Null => null,
			_ => throw new AutomationException(AutomationErrorCodes.InvalidArguments, "Only JSON scalar values are supported."),
		};
	}

	private static void ValidateKeys(string keys)
	{
		var known = new[]
		{
			"Enter", "Return", "Tab", "Escape", "Esc", "Space", "Backspace", "Delete", "Del",
			"Insert", "Ins", "Home", "End", "PageUp", "PageDown", "Up", "Down", "Left", "Right",
			"Ctrl", "Control", "Shift", "Alt", "A", "C", "V", "X", "Y", "Z",
			"F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
		};
		foreach (var token in keys.Split(new[] { '+', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			if (token.Length == 1 && char.IsLetterOrDigit(token[0]))
				continue;
			if (!known.Contains(token, StringComparer.OrdinalIgnoreCase))
				throw new AutomationException(AutomationErrorCodes.InvalidArguments, $"Unknown key name '{token}'.");
		}
	}

	private static readonly HashSet<string> KnownRoutedEvents = new(StringComparer.Ordinal)
	{
		"Click",
		"MouseDoubleClick",
		"Checked",
		"Unchecked",
		"Expanded",
		"Collapsed",
	};

	private static readonly HashSet<string> KnownOperations = new(StringComparer.Ordinal)
	{
		"Focus",
		"AcceptDialog",
		"CancelDialog",
		"BringIntoView",
		"Select",
		"Expand",
		"Collapse",
		"Check",
		"Uncheck",
	};

	private static ProcessListData GetProcesses(CliServices services, bool candidatesOnly)
	{
		var result = services.ProcessSnapshotSource.GetSnapshots();
		return ProcessListData.FromSnapshotResult(
			result,
			candidatesOnly,
			excludeExited: candidatesOnly,
			sortByProcessName: true);
	}

	private static CliCommonOptions CreateOutputOptions(string[] args, CliDefaults defaults)
	{
		try
		{
			return CliCommonOptions.Parse(args, defaults);
		}
		catch (AutomationException)
		{
			return new CliCommonOptions
			{
				TimeoutMs = defaults.TimeoutMs,
				Format = defaults.OutputFormat,
				HideEmpty = defaults.HideEmpty,
				UseShortIds = defaults.UseShortIds,
				After = defaults.AfterSnapshot,
			};
		}
	}

	private static bool IsConfigReset(string[] args) =>
		args.Length >= 2
		&& string.Equals(args[0], "config", StringComparison.Ordinal)
		&& string.Equals(args[1], "reset", StringComparison.Ordinal);

	private static bool HasOption(string[] args, string option) =>
		args.Any(arg => string.Equals(arg, option, StringComparison.Ordinal) || arg.StartsWith(option + "=", StringComparison.Ordinal));

	private static string? GetConfigPositional(string[] args, int index)
	{
		var positionals = GetConfigPositionals(args);
		return index >= 0 && index < positionals.Count ? positionals[index] : null;
	}

	private static string GetRequiredConfigPositional(string[] args, int index, string name)
	{
		var value = GetConfigPositional(args, index);
		if (string.IsNullOrWhiteSpace(value))
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, $"Missing required argument '{name}'.");

		return value!;
	}

	private static IReadOnlyList<string> GetConfigPositionals(string[] args)
	{
		List<string> result = [];
		for (var i = 2; i < args.Length; i++)
		{
			var arg = args[i];
			if (arg.StartsWith("--", StringComparison.Ordinal))
			{
				var optionName = arg.Split('=', 2)[0];
				if (OptionHasSeparateValue(optionName) && !arg.Contains('=', StringComparison.Ordinal) && i + 1 < args.Length)
					i++;
				continue;
			}

			result.Add(arg);
		}

		return result;
	}

	private static bool OptionHasSeparateValue(string optionName) =>
		optionName is "--format" or "--after";

	private static string? GetOptionalPositional(string[] args, int index)
	{
		if (args.Length <= index || args[index].StartsWith("-", StringComparison.Ordinal))
			return null;

		return args[index];
	}

	private static string GetRequiredPositional(string[] args, int index, string name)
	{
		var value = GetOptionalPositional(args, index);
		if (string.IsNullOrWhiteSpace(value))
			throw new AutomationException(AutomationErrorCodes.InvalidArguments, $"Missing required argument '{name}'.");

		return value;
	}

	private static ConsoleCancellationSource CreateConsoleCancellationSource() => new();
}

public sealed class ProtocolCommandData<TResponse>
{
	public TargetInfo Target { get; set; } = new();

	public HelloCommandResponse Hello { get; set; } = new();

	public TResponse? Response { get; set; }
}

public sealed class StreamCommandResult
{
	public StartSendingCommandResponse Start { get; set; } = new();

	public IReadOnlyList<StreamMessage> Frames { get; set; } = [];

	public StopSendingCommandResponse Stop { get; set; } = new();
}

public sealed class CliResponseSequence
{
	public CliResponseSequence(IReadOnlyList<CliResponseEnvelope> envelopes)
	{
		Envelopes = envelopes ?? throw new ArgumentNullException(nameof(envelopes));
	}

	public IReadOnlyList<CliResponseEnvelope> Envelopes { get; }
}

public sealed class SemanticRecordingFileData
{
	public string OutputPath { get; set; } = string.Empty;

	public string RecordingFormat { get; set; } = "condensed-agent";

	public long FramesWritten { get; set; }

	public int DroppedActionCount { get; set; }

	public StopSendingCommandResponse? Stop { get; set; }
}

internal sealed record TreePropertySelection(IReadOnlyList<string> RequestProperties, IReadOnlyList<string> OutputProperties, bool SuppressProperties);

internal sealed class ConsoleCancellationSource : IDisposable
{
	private readonly CancellationTokenSource source = new();
	private readonly ConsoleCancelEventHandler handler;

	public ConsoleCancellationSource()
	{
		handler = (_, args) =>
		{
			args.Cancel = true;
			source.Cancel();
		};
		try
		{
			Console.CancelKeyPress += handler;
		}
		catch (InvalidOperationException)
		{
		}
	}

	public CancellationToken Token => source.Token;

	public void Dispose()
	{
		try
		{
			Console.CancelKeyPress -= handler;
		}
		catch (InvalidOperationException)
		{
		}

		source.Dispose();
	}
}
