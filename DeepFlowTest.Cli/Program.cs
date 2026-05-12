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

		if (CliRootCommand.IsHelpRequest(args))
		{
			stdout.WriteLine(CliRootCommand.HelpText);
			return 0;
		}

		var root = CreateRootCommand();
		var parseResult = root.Parse(args, new ParserConfiguration());
		if (parseResult.Errors.Count != 0)
		{
			var message = string.Join(Environment.NewLine, parseResult.Errors.Select(static error => error.Message));
			var options = CreateOutputOptions(args, new CliDefaults());
			CliOutput.Write(CliResponseFactory.Error(commandName, CliErrorCodes.InvalidArguments, message, stopwatch), options, stdout);
			return ExitCodeMapper.Map(CliErrorCodes.InvalidArguments);
		}

		CliDefaults defaults;
		try
		{
			defaults = IsConfigReset(args) ? new CliDefaults() : services.DefaultsStore.Load();
		}
		catch (CliException ex)
		{
			var options = CreateOutputOptions(args, new CliDefaults());
			CliOutput.Write(CliResponseFactory.Error(commandName, ex.ErrorCode, ex.Message, stopwatch, ex.Details), options, stdout);
			return ExitCodeMapper.Map(ex.ErrorCode);
		}

		var commonOptions = CreateOutputOptions(args, defaults);
		try
		{
			commonOptions.ValidateEnums();
			if (CliRootCommand.IsTargetBound(commandPath))
				commonOptions.ValidateTargetSelectorRequired();

			var data = Execute(commandPath, args, services, defaults, commonOptions, stderr);
			CliOutput.Write(CliResponseFactory.Success(commandName, data, stopwatch), commonOptions, stdout);
			return 0;
		}
		catch (CliException ex)
		{
			CliOutput.Write(CliResponseFactory.Error(commandName, ex.ErrorCode, ex.Message, stopwatch, ex.Details), commonOptions, stdout);
			return ExitCodeMapper.Map(ex.ErrorCode);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			CliOutput.Write(CliResponseFactory.Error(commandName, CliErrorCodes.UnexpectedError, ex.Message, stopwatch), commonOptions, stdout);
			return ExitCodeMapper.Map(CliErrorCodes.UnexpectedError);
		}
	}

	public static RootCommand CreateRootCommand()
	{
		return CliRootCommand.Create();
	}

	public static bool IsHelpRequest(string[] args)
	{
		return CliRootCommand.IsHelpRequest(args);
	}

	private static object Execute(
		string commandPath,
		string[] args,
		CliServices services,
		CliDefaults defaults,
		CliCommonOptions commonOptions,
		TextWriter stderr)
	{
		switch (commandPath)
		{
			case "version":
				return new ProductVersionData { ProductName = DeepFlowTest.ProductInfo.Name };
			case "config get":
				return services.DefaultsStore.Get(GetOptionalPositional(args, 2));
			case "config set":
				services.DefaultsStore.Set(GetRequiredPositional(args, 2, "key"), GetRequiredPositional(args, 3, "value"));
				return new
				{
					key = GetRequiredPositional(args, 2, "key"),
					value = services.DefaultsStore.Get(GetRequiredPositional(args, 2, "key")),
				};
			case "config clear":
				services.DefaultsStore.Clear(GetRequiredPositional(args, 2, "key"));
				return new
				{
					key = GetRequiredPositional(args, 2, "key"),
					value = services.DefaultsStore.Get(GetRequiredPositional(args, 2, "key")),
				};
			case "config reset":
				services.DefaultsStore.Reset();
				return new { reset = true };
			case "processes":
				return GetProcesses(services, HasOption(args, "--candidates-only"));
			case "ping":
				return SendProtocolCommand<PingCommandResponse>(
					services,
					commonOptions,
					new PingCommandRequest { TimeoutMs = commonOptions.TimeoutMs },
					stderr);
			case "pipe status":
				return SendProtocolCommand<PipeStatusCommandResponse>(
					services,
					commonOptions,
					new PipeStatusCommandRequest { TimeoutMs = commonOptions.TimeoutMs },
					stderr);
			case "tree":
				return ExecuteTree(args, services, defaults, commonOptions);
			case "find":
				return ExecuteFind(args, services, defaults, commonOptions);
			case "node":
				return ExecuteNode(args, services, defaults, commonOptions);
			case "props":
				return ExecuteProps(args, services, defaults, commonOptions);
			case "selectors":
				return ExecuteSelectors(args, services, defaults, commonOptions);
			case "screenshot":
				return ExecuteScreenshot(args, services, defaults, commonOptions);
			case "wait":
				return ExecuteWait(args, services, defaults, commonOptions);
			case "stream visual-tree":
				return ExecuteStream(args, services, defaults, commonOptions, ProtocolConstants.StreamKinds.VisualTree);
			case "stream visual-tree-delta":
				return ExecuteStream(args, services, defaults, commonOptions, ProtocolConstants.StreamKinds.VisualTreeDelta);
			case "stream screenshot":
				return ExecuteStream(args, services, defaults, commonOptions, ProtocolConstants.StreamKinds.Screenshot);
			case "stream event-log":
				return ExecuteStream(args, services, defaults, commonOptions, ProtocolConstants.StreamKinds.EventLog);
			case "click":
				return ExecuteClick(args, services, defaults, commonOptions);
			case "focus":
				return ExecuteFocus(args, services, defaults, commonOptions);
			case "type":
				return ExecuteType(args, services, defaults, commonOptions);
			case "key":
				return ExecuteKey(args, services, defaults, commonOptions);
			case "set":
				return ExecuteSet(args, services, defaults, commonOptions);
			case "raise":
				return ExecuteRaise(args, services, defaults, commonOptions);
			case "invoke":
				return ExecuteInvoke(args, services, defaults, commonOptions);
			default:
				if (CliRootCommand.IsTargetBound(commandPath))
					throw new CliException(CliErrorCodes.NotImplemented, $"Command '{commandPath}' is registered but its handler is not implemented yet.");

				throw new CliException(CliErrorCodes.NotImplemented, $"Command '{commandPath}' is not implemented.");
		}
	}

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
		var properties = GetRequestedProperties(args, defaults);
		if (!CliArgumentReader.HasOption(args, "--include-hidden") && !properties.Contains("IsVisible", StringComparer.Ordinal))
			properties = properties.Concat(new[] { "IsVisible" }).ToArray();

		using var session = OpenSession(services, commonOptions);
		var snapshot = ReadSnapshot(session, commonOptions, properties, defaults.TreeLimit);
		var options = new TreeSnapshotOptions
		{
			Shape = CliArgumentReader.GetOption(args, "--shape") ?? defaults.TreeShape,
			RootTargetId = CliArgumentReader.GetOption(args, "--root", "--target-id"),
			MaxDepth = CliArgumentReader.GetInt(args, "--max-depth", defaults.TreeMaxDepth),
			Limit = CliArgumentReader.GetInt(args, "--limit", defaults.TreeLimit),
			IncludeHidden = CliArgumentReader.HasOption(args, "--include-hidden"),
			IncludeTypeNames = CliArgumentReader.HasOption(args, "--type-names"),
			IncludePath = CliArgumentReader.HasOption(args, "--include-path"),
			UseShortIds = commonOptions.UseShortIds,
			Properties = properties,
		};
		return new TreeSnapshotService().Shape(snapshot, options);
	}

	private static FindResultData ExecuteFind(string[] args, CliServices services, CliDefaults defaults, CliCommonOptions commonOptions)
	{
		var properties = GetRequestedProperties(args, defaults);
		using var session = OpenSession(services, commonOptions);
		var snapshot = ReadSnapshot(session, commonOptions, properties, defaults.TreeLimit);
		var options = CreateFindOptions(args, defaults, commonOptions, properties);
		var result = new FindSnapshotService().Find(snapshot, options);
		if (result.MatchCount == 0 && CliArgumentReader.HasOption(args, "--require-match"))
			throw new CliException(CliErrorCodes.NoMatch, "No matching nodes were found.");

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
		var fullId = new CliTargetIdService().Resolve(targetId, snapshot);
		var node = snapshot.Nodes.First(node => node.TargetId == fullId);
		return new SelectorSuggestionService(new CliTargetIdService(), snapshot).Suggest(node, commonOptions.UseShortIds);
	}

	private static ScreenshotResultData ExecuteScreenshot(string[] args, CliServices services, CliDefaults defaults, CliCommonOptions commonOptions)
	{
		using var session = OpenSession(services, commonOptions);
		var format = ScreenshotFileService.NormalizeFormat(CliArgumentReader.GetOption(args, "--image-format") ?? defaults.ScreenshotFormat);
		var targetId = CliArgumentReader.GetOption(args, "--target", "--target-id");
		if (!string.IsNullOrWhiteSpace(targetId))
		{
			var snapshot = ReadSnapshot(session, commonOptions, defaults.PropertyNames, defaults.TreeLimit);
			targetId = new CliTargetIdService().Resolve(targetId!, snapshot);
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
			OutputPath = CliArgumentReader.GetOption(args, "--output"),
			IncludeBase64 = CliArgumentReader.HasOption(args, "--base64"),
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

		var interval = CliArgumentReader.GetInt(args, "--interval-ms", defaults.WaitIntervalMs);
		var requiredMatches = CliArgumentReader.GetInt(args, "--match-count", defaults.WaitMatchCount);
		var stopwatch = Stopwatch.StartNew();
		while (stopwatch.ElapsedMilliseconds <= commonOptions.TimeoutMs)
		{
			var snapshot = ReadSnapshot(session, commonOptions, properties, defaults.TreeLimit);
			var result = new FindSnapshotService().Find(snapshot, options);
			if (result.MatchCount >= requiredMatches)
				return result;

			var remaining = commonOptions.TimeoutMs - (int)stopwatch.ElapsedMilliseconds;
			if (remaining <= 0)
				break;

			Thread.Sleep(Math.Min(interval, remaining));
		}

		throw new CliException(CliErrorCodes.CommandTimeout, $"Wait timed out after {commonOptions.TimeoutMs} ms.");
	}

	private static StreamCommandResult ExecuteStream(
		string[] args,
		CliServices services,
		CliDefaults defaults,
		CliCommonOptions commonOptions,
		string streamKind)
	{
		var interval = CliArgumentReader.GetInt(args, "--interval-ms", defaults.StreamIntervalMs);
		if (interval < 50)
			throw new CliException(CliErrorCodes.InvalidArguments, "Stream interval must be at least 50 ms.");

		var duration = CliArgumentReader.GetInt(args, "--duration-ms", interval);
		var format = streamKind == ProtocolConstants.StreamKinds.Screenshot
			? ScreenshotFileService.NormalizeFormat(CliArgumentReader.GetOption(args, "--image-format") ?? defaults.ScreenshotFormat)
			: defaults.ScreenshotFormat;
		var properties = GetRequestedProperties(args, defaults);
		using var session = OpenSession(services, commonOptions);
		var start = session.Send<StartSendingCommandResponse>(
			new StartSendingCommandRequest
			{
				StreamKind = streamKind,
				IntervalMs = interval,
				PropNames = properties,
				TargetId = CliArgumentReader.GetOption(args, "--target", "--target-id"),
				Format = format,
				TimeoutMs = commonOptions.TimeoutMs,
			},
			commonOptions.TimeoutMs);

		var frameCount = Math.Max(1, Math.Min(3, (duration + interval - 1) / interval));
		var frames = new List<StreamMessage>();
		VisualTreeSnapshot? previous = null;
		for (var i = 0; i < frameCount; i++)
		{
			var sequence = i + 1;
			frames.Add(new StreamMessage
			{
				SubscriptionId = start.SubscriptionId,
				StreamKind = streamKind,
				SequenceNumber = sequence,
				Data = CreateStreamFrameData(session, commonOptions, defaults, streamKind, properties, format, ref previous),
			});
		}

		var stop = session.Send<StopSendingCommandResponse>(
			new StopSendingCommandRequest
			{
				SubscriptionId = start.SubscriptionId,
				TimeoutMs = Math.Min(commonOptions.TimeoutMs, 2000),
			},
			Math.Min(commonOptions.TimeoutMs, 2000));

		return new StreamCommandResult
		{
			Start = start,
			Frames = frames,
			Stop = stop,
		};
	}

	private static object CreateStreamFrameData(
		ICliAppSession session,
		CliCommonOptions commonOptions,
		CliDefaults defaults,
		string streamKind,
		IReadOnlyList<string> properties,
		string format,
		ref VisualTreeSnapshot? previous)
	{
		switch (streamKind)
		{
			case ProtocolConstants.StreamKinds.VisualTree:
				return ReadSnapshot(session, commonOptions, properties, defaults.TreeLimit);
			case ProtocolConstants.StreamKinds.VisualTreeDelta:
				var current = ReadSnapshot(session, commonOptions, properties, defaults.TreeLimit);
				if (previous is null)
				{
					previous = current;
					return new { isFullSnapshot = true, snapshot = current };
				}

				var delta = VisualTreeSnapshotDelta.Create(previous, current);
				previous = current;
				return new { isFullSnapshot = false, delta };
			case ProtocolConstants.StreamKinds.Screenshot:
				return session.Send<ScreenshotCommandResponse>(
					new ScreenshotCommandRequest
					{
						Format = format,
						TimeoutMs = commonOptions.TimeoutMs,
					},
					commonOptions.TimeoutMs);
			case ProtocolConstants.StreamKinds.EventLog:
				return new
				{
					status = "heartbeat",
					processId = session.Hello.ProcessId,
					rootCount = session.Hello.IsReusable ? 1 : 0,
				};
			default:
				throw new CliException(CliErrorCodes.InvalidArguments, $"Unsupported stream kind '{streamKind}'.");
		}
	}

	private static ICliAppSession OpenSession(CliServices services, CliCommonOptions commonOptions)
	{
		var target = services.TargetResolver.Resolve(commonOptions.ToTargetSelector());
		return services.AppSessionService.Open(target, commonOptions.ToAttachOptions());
	}

	private static VisualTreeSnapshot ReadSnapshot(ICliAppSession session, CliCommonOptions commonOptions, IReadOnlyList<string> properties, int limit)
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

	private static FindSnapshotOptions CreateFindOptions(
		string[] args,
		CliDefaults defaults,
		CliCommonOptions commonOptions,
		IReadOnlyList<string> properties)
	{
		return new FindSnapshotOptions
		{
			TypeName = CliArgumentReader.GetOption(args, "--type"),
			TypeContains = CliArgumentReader.GetOption(args, "--type-contains"),
			Name = CliArgumentReader.GetOption(args, "--name"),
			AutomationId = CliArgumentReader.GetOption(args, "--automation-id"),
			Text = CliArgumentReader.GetOption(args, "--text"),
			PropertyEquals = CliArgumentReader.GetKeyValue(args, "--property"),
			PropertyContains = CliArgumentReader.GetKeyValue(args, "--property-contains"),
			PropertyRegex = CliArgumentReader.GetKeyValue(args, "--property-regex"),
			Visible = CliArgumentReader.HasOption(args, "--visible") ? true : null,
			Enabled = CliArgumentReader.HasOption(args, "--enabled") ? true : null,
			CaseSensitive = CliArgumentReader.HasOption(args, "--case-sensitive"),
			Limit = CliArgumentReader.GetInt(args, "--limit", defaults.FindLimit),
			IncludePath = CliArgumentReader.HasOption(args, "--include-path"),
			IncludeProperties = CliArgumentReader.HasOption(args, "--include-properties"),
			IncludeChildren = CliArgumentReader.HasOption(args, "--include-children"),
			IncludeAncestors = CliArgumentReader.HasOption(args, "--include-ancestors"),
			UseShortIds = commonOptions.UseShortIds,
			Properties = properties,
		};
	}

	private static NodeSnapshotOptions CreateNodeOptions(string[] args, CliCommonOptions commonOptions, IReadOnlyList<string> properties)
	{
		return new NodeSnapshotOptions
		{
			TargetId = GetTargetIdArgument(args),
			IncludeAncestors = CliArgumentReader.HasOption(args, "--include-ancestors"),
			IncludeChildren = CliArgumentReader.HasOption(args, "--include-children"),
			IncludeSubtree = CliArgumentReader.HasOption(args, "--include-subtree"),
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
			throw new CliException(CliErrorCodes.InvalidArguments, "A target ID is required.");

		return targetId!;
	}

	private static ActionCommandResult ExecuteClick(string[] args, CliServices services, CliDefaults defaults, CliCommonOptions commonOptions)
	{
		new ActionGate().Demand("click", commonOptions);
		var button = (CliArgumentReader.GetOption(args, "--button") ?? "left").ToLowerInvariant();
		var count = CliArgumentReader.GetInt(args, "--count", 1);
		using var session = OpenSession(services, commonOptions);
		return new ActionCommandSupport().Execute(
			"click",
			session,
			commonOptions,
			defaults,
			ElementSelector.FromArgs(args),
			targetId =>
			{
				if (button == "double" || count >= 2)
				{
					return new KnownRoutedEventCommandRequest
					{
						TargetId = targetId ?? string.Empty,
						EventName = "MouseDoubleClick",
					};
				}

				if (button is not ("left" or "right"))
					throw new CliException(CliErrorCodes.InvalidArguments, $"Unsupported click button '{button}'.");

				return new ClickCommandRequest
				{
					TargetId = targetId ?? string.Empty,
					MouseButton = button,
					ClickCount = count,
				};
			},
			requireElementTarget: true);
	}

	private static ActionCommandResult ExecuteFocus(string[] args, CliServices services, CliDefaults defaults, CliCommonOptions commonOptions)
	{
		new ActionGate().Demand("focus", commonOptions);
		using var session = OpenSession(services, commonOptions);
		return new ActionCommandSupport().Execute(
			"focus",
			session,
			commonOptions,
			defaults,
			ElementSelector.FromArgs(args),
			targetId => new FocusCommandRequest { TargetId = targetId ?? string.Empty },
			requireElementTarget: true);
	}

	private static ActionCommandResult ExecuteType(string[] args, CliServices services, CliDefaults defaults, CliCommonOptions commonOptions)
	{
		new ActionGate().Demand("type", commonOptions);
		var text = CliArgumentReader.GetOption(args, "--text");
		if (text is null)
			throw new CliException(CliErrorCodes.InvalidArguments, "The type command requires --text.");

		var selector = ElementSelector.FromArgs(args);
		selector.Text = null;
		using var session = OpenSession(services, commonOptions);
		return new ActionCommandSupport().Execute(
			"type",
			session,
			commonOptions,
			defaults,
			selector,
			targetId => new TypeTextCommandRequest
			{
				Text = text,
				TargetId = targetId,
				ClearFirst = CliArgumentReader.HasOption(args, "--clear-first"),
			},
			requireElementTarget: !selector.IsEmpty);
	}

	private static ActionCommandResult ExecuteKey(string[] args, CliServices services, CliDefaults defaults, CliCommonOptions commonOptions)
	{
		new ActionGate().Demand("key", commonOptions);
		var keys = CliArgumentReader.GetOption(args, "--keys");
		if (string.IsNullOrWhiteSpace(keys))
			throw new CliException(CliErrorCodes.InvalidArguments, "The key command requires --keys.");
		ValidateKeys(keys!);

		var selector = ElementSelector.FromArgs(args);
		using var session = OpenSession(services, commonOptions);
		return new ActionCommandSupport().Execute(
			"key",
			session,
			commonOptions,
			defaults,
			selector,
			targetId => new KeyPressCommandRequest
			{
				Keys = keys,
				TargetId = targetId,
				DelayMs = CliArgumentReader.GetInt(args, "--delay-ms", defaults.KeyDelayMs),
			},
			requireElementTarget: !selector.IsEmpty);
	}

	private static ActionCommandResult ExecuteSet(string[] args, CliServices services, CliDefaults defaults, CliCommonOptions commonOptions)
	{
		new ActionGate().Demand("set", commonOptions);
		var property = CliArgumentReader.GetOption(args, "--property");
		if (string.IsNullOrWhiteSpace(property))
			throw new CliException(CliErrorCodes.InvalidArguments, "The set command requires --property.");

		var rawValue = CliArgumentReader.GetOption(args, "--value");
		if (rawValue is null)
			throw new CliException(CliErrorCodes.InvalidArguments, "The set command requires --value.");

		using var session = OpenSession(services, commonOptions);
		return new ActionCommandSupport().Execute(
			"set",
			session,
			commonOptions,
			defaults,
			ElementSelector.FromArgs(args),
			targetId => new SetPropertyCommandRequest
			{
				TargetId = targetId ?? string.Empty,
				PropertyName = property!,
				PropertyValue = ParseJsonScalar(rawValue),
			},
			requireElementTarget: true);
	}

	private static ActionCommandResult ExecuteRaise(string[] args, CliServices services, CliDefaults defaults, CliCommonOptions commonOptions)
	{
		new ActionGate().Demand("raise", commonOptions);
		var eventName = CliArgumentReader.GetOption(args, "--event");
		if (string.IsNullOrWhiteSpace(eventName))
			throw new CliException(CliErrorCodes.InvalidArguments, "The raise command requires --event.");
		if (!KnownRoutedEvents.Contains(eventName!))
			throw new CliException(CliErrorCodes.InvalidArguments, $"Routed event '{eventName}' is not allow-listed.");

		using var session = OpenSession(services, commonOptions);
		return new ActionCommandSupport().Execute(
			"raise",
			session,
			commonOptions,
			defaults,
			ElementSelector.FromArgs(args),
			targetId => new KnownRoutedEventCommandRequest
			{
				TargetId = targetId ?? string.Empty,
				EventName = eventName!,
			},
			requireElementTarget: true);
	}

	private static ActionCommandResult ExecuteInvoke(string[] args, CliServices services, CliDefaults defaults, CliCommonOptions commonOptions)
	{
		var code = CliArgumentReader.GetOption(args, "--code");
		var operation = CliArgumentReader.GetOption(args, "--operation");
		var arbitrary = !string.IsNullOrWhiteSpace(code);
		new ActionGate().Demand("invoke", commonOptions, arbitraryInvoke: arbitrary);
		if (string.IsNullOrWhiteSpace(operation) && string.IsNullOrWhiteSpace(code))
			throw new CliException(CliErrorCodes.InvalidArguments, "The invoke command requires --operation or --code.");

		if (!string.IsNullOrWhiteSpace(operation) && !KnownOperations.Contains(operation!))
			throw new CliException(CliErrorCodes.InvalidArguments, $"Known operation '{operation}' is not allow-listed.");

		object? parsedCode = null;
		if (!string.IsNullOrWhiteSpace(code))
			parsedCode = ParseJsonScalarStrict(code!);

		using var session = OpenSession(services, commonOptions);
		return new ActionCommandSupport().Execute(
			"invoke",
			session,
			commonOptions,
			defaults,
			ElementSelector.FromArgs(args),
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
			throw new CliException(CliErrorCodes.InvalidArguments, $"Invalid JSON payload: {ex.Message}");
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
			_ => throw new CliException(CliErrorCodes.InvalidArguments, "Only JSON scalar values are supported."),
		};
	}

	private static void ValidateKeys(string keys)
	{
		var known = new[] { "Enter", "Tab", "Escape", "Esc", "Space", "Backspace", "Delete", "Ctrl", "Control", "Shift", "Alt", "A", "C", "V", "X", "Z" };
		foreach (var token in keys.Split(new[] { '+', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			if (token.Length == 1 && char.IsLetterOrDigit(token[0]))
				continue;
			if (!known.Contains(token, StringComparer.OrdinalIgnoreCase))
				throw new CliException(CliErrorCodes.InvalidArguments, $"Unknown key name '{token}'.");
		}
	}

	private static readonly HashSet<string> KnownRoutedEvents = new(StringComparer.Ordinal)
	{
		"Click",
		"MouseDoubleClick",
		"MouseDown",
		"MouseUp",
		"KeyDown",
		"KeyUp",
		"GotFocus",
		"LostFocus",
		"SelectionChanged",
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
		var processes = result.Processes
			.Where(process => !candidatesOnly || process.IsLikelyWpfCandidate)
			.OrderBy(static process => process.ProcessName, StringComparer.OrdinalIgnoreCase)
			.ThenBy(static process => process.ProcessId)
			.ToArray();
		return new ProcessListData
		{
			Processes = processes,
			Warnings = result.Warnings,
		};
	}

	private static CliCommonOptions CreateOutputOptions(string[] args, CliDefaults defaults)
	{
		try
		{
			return CliCommonOptions.Parse(args, defaults);
		}
		catch (CliException)
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
			throw new CliException(CliErrorCodes.InvalidArguments, $"Missing required argument '{name}'.");

		return value;
	}
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

	public IReadOnlyList<StreamMessage> Frames { get; set; } = Array.Empty<StreamMessage>();

	public StopSendingCommandResponse Stop { get; set; } = new();
}
