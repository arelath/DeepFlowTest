namespace DeepFlowTest.Mcp.Tools;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using DeepFlowTest.Cli;
using DeepFlowTest.Contracts;
using DeepFlowTest.Mcp.Configuration;
using DeepFlowTest.Mcp.Contracts;
using DeepFlowTest.Mcp.Hosting;
using DeepFlowTest.Mcp.Resources;
using DeepFlowTest.Interop;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using DeepFlowMcpOptions = DeepFlowTest.Mcp.Configuration.McpServerOptions;

[McpServerToolType]
internal static class AgentTools
{
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

	[McpServerTool(Name = "deepflow_open_context", UseStructuredContent = true, OutputSchemaType = typeof(McpContextResult), ReadOnly = false, OpenWorld = false), Description("Attach to or launch one desktop target and return the explicit context handle required by every other agent tool.")]
	public static CallToolResult OpenContext(
		McpToolRunner runner,
		McpSessionHost host,
		[Description("Typed attach or launch target.")] McpOpenContextTarget target,
		[Description("Attach timeout in milliseconds.")] int? timeoutMs = null)
	{
		var response = target switch
		{
			McpAttachContextTarget attach => runner.Run(() => host.AttachContext(
				new McpTargetSelector
				{
					ProcessId = attach.ProcessId,
					ProcessName = attach.ProcessName,
					WindowTitle = attach.WindowTitle,
				},
				timeoutMs), new { target, timeoutMs }),
			McpLaunchContextTarget launch => runner.Run(() => host.LaunchContext(new McpLaunchOptions
			{
				FileName = launch.FileName,
				Arguments = launch.Arguments,
				WorkingDirectory = launch.WorkingDirectory,
				AttachTimeoutMs = timeoutMs,
				TerminateOnDetach = launch.TerminateOnClose,
			}), new { target, timeoutMs }),
			_ => McpToolResponse.Fail(CliErrorCodes.InvalidArguments, "Unsupported context target."),
		};

		return McpCallToolResults.FromLegacy(response, static data => ToContextResult((McpTargetStatus)data!));
	}

	[McpServerTool(Name = "deepflow_observe", UseStructuredContent = true, OutputSchemaType = typeof(McpObservationResult), ReadOnly = true, OpenWorld = false), Description("Return a compact semantic UI snapshot for an explicit context, with revision and stable target identifiers.")]
	public static CallToolResult Observe(
		McpToolRunner runner,
		McpSessionHost host,
		McpSnapshotCache cache,
		DeepFlowResourceStore resources,
		IOptions<DeepFlowMcpOptions> options,
		string contextId,
		McpObservationFormat format = McpObservationFormat.Condensed,
		IReadOnlyList<string>? properties = null,
		int? limit = null,
		bool includeHidden = false,
		bool refresh = false)
	{
		var response = runner.Run(() =>
		{
			var session = host.RequireContext(contextId);
			var propertyNames = PropertiesOrDefaults(properties, options.Value.DefaultProperties);
			if (format == McpObservationFormat.Condensed)
				propertyNames = McpSemanticRecordingFormatter.MergeSemanticProperties(propertyNames);

			var snapshot = cache.GetOrRefresh(session, propertyNames, Math.Max(options.Value.TreeLimit, limit ?? 0), includeHidden: true, refresh: refresh);
			if (format == McpObservationFormat.Condensed)
			{
				var condensed = McpSemanticRecordingFormatter.FormatSnapshot(snapshot);
				var resource = resources.StoreContextSnapshot(contextId, snapshot.SequenceNumber, snapshot);
				return new McpObservationResult
				{
					ContextId = contextId,
					Revision = snapshot.SequenceNumber,
					NodeCount = snapshot.NodeCount,
					Format = McpSemanticRecordingFormatter.FormatName,
					Text = condensed.Text,
					ResourceUri = resource.Uri,
				};
			}

			var shaped = new TreeSnapshotService().Shape(snapshot, new TreeSnapshotOptions
			{
				Shape = TreeShape.Flat,
				Limit = limit ?? options.Value.TreeLimit,
				IncludeHidden = includeHidden,
				IncludePath = true,
				IncludeTypeNames = true,
				Properties = propertyNames,
				UseShortIds = true,
			});
			var jsonResource = resources.StoreContextSnapshot(contextId, snapshot.SequenceNumber, shaped);
			return new McpObservationResult
			{
				ContextId = contextId,
				Revision = snapshot.SequenceNumber,
				NodeCount = shaped.Nodes.Count,
				Format = "json",
				Nodes = shaped.Nodes,
				ResourceUri = jsonResource.Uri,
			};
		}, new { contextId, format, properties, limit, includeHidden, refresh });

		return McpCallToolResults.FromLegacy(response, static data => (McpObservationResult)data!, result =>
		[
			new TextContentBlock { Text = result.Text ?? $"Observed {result.NodeCount} nodes at revision {result.Revision}." },
			new ResourceLinkBlock { Uri = result.ResourceUri, Name = $"DeepFlow snapshot {result.Revision}", MimeType = "application/json" },
		], contextId, LatestRevision(host, contextId));
	}

	[McpServerTool(Name = "deepflow_find", UseStructuredContent = true, OutputSchemaType = typeof(McpFindResult), ReadOnly = true, OpenWorld = false), Description("Find UI elements using a nested typed selector and return server-managed stable handles. Does not silently choose among ambiguous elements.")]
	public static CallToolResult Find(
		McpToolRunner runner,
		McpSessionHost host,
		McpSnapshotCache cache,
		McpElementHandleRegistry handles,
		IOptions<DeepFlowMcpOptions> options,
		string contextId,
		McpAgentSelector target,
		IReadOnlyList<string>? properties = null,
		int limit = 50,
		bool refresh = false)
	{
		var response = runner.Run(() =>
		{
			var session = host.RequireContext(contextId);
			if (limit <= 0)
				throw new CliException(CliErrorCodes.InvalidArguments, "limit must be greater than zero.");

			var propertyNames = PropertiesOrDefaults(properties, options.Value.DefaultProperties);
			var snapshot = cache.GetOrRefresh(session, propertyNames, Math.Max(options.Value.TreeLimit, limit), refresh: refresh);
			var selector = target.ToCliSelector();
			if (!string.IsNullOrWhiteSpace(target.Handle))
			{
				var resolved = handles.Resolve(contextId, target.Handle!, snapshot);
				var node = snapshot.Nodes.First(node => string.Equals(node.TargetId, resolved.TargetId, StringComparison.Ordinal));
				var shaped = new TreeSnapshotService().ShapeOne(node, snapshot, new TreeSnapshotOptions
				{
					IncludePath = true,
					IncludeTypeNames = true,
					Properties = propertyNames,
					UseShortIds = true,
				});
				return new McpFindResult
				{
					ContextId = contextId,
					Revision = snapshot.SequenceNumber,
					MatchCount = 1,
					Matches = [ToElementMatch(resolved.Entry, shaped)],
				};
			}

			var found = new FindSnapshotService().Find(snapshot, new FindSnapshotOptions
			{
				TypeName = selector.TypeName,
				TypeContains = selector.TypeContains,
				Name = selector.Name,
				AutomationId = selector.AutomationId,
				Text = selector.Text,
				PropertyEquals = selector.PropertyEquals,
				PropertyContains = selector.PropertyContains,
				PropertyRegex = selector.PropertyRegex,
				Visible = selector.Visible,
				Enabled = selector.Enabled,
				CaseSensitive = selector.CaseSensitive,
				Limit = limit,
				IncludePath = true,
				IncludeProperties = true,
				Properties = propertyNames,
				UseShortIds = true,
			});
			if (found.MatchCount == 0 && target is McpSemanticSelector { Fallback: not null } semantic)
			{
				selector = semantic.Fallback.ToCliSelector();
				found = FindMatches(snapshot, selector, limit);
			}
			var matches = found.Matches.Select(match => ToElementMatch(
				handles.Register(contextId, match.Node.TargetId, StableSelector(selector, match.Node), match.Node, snapshot.SequenceNumber),
				match.Node)).ToArray();
			return new McpFindResult
			{
				ContextId = contextId,
				Revision = snapshot.SequenceNumber,
				MatchCount = found.MatchCount,
				Matches = matches,
			};
		}, new { contextId, target, properties, limit, refresh });

		return McpCallToolResults.FromLegacy(response, static data => (McpFindResult)data!, contextId: contextId, revision: LatestRevision(host, contextId));
	}

	[McpServerTool(Name = "deepflow_act", UseStructuredContent = true, OutputSchemaType = typeof(McpActionResult), ReadOnly = false, Destructive = true, OpenWorld = false), Description("Resolve, act, optionally verify, and observe in one call. Supports stable handles and automatic selector repair after UI revisions.")]
	public static CallToolResult Act(
		McpToolRunner runner,
		McpSessionHost host,
		McpSnapshotCache cache,
		McpElementHandleRegistry handles,
		IOptions<DeepFlowMcpOptions> options,
		string contextId,
		McpAgentSelector target,
		McpAgentAction action,
		McpActionExpectation? expect = null,
		McpObserveMode observe = McpObserveMode.Delta)
	{
		var response = runner.Run(() =>
		{
			var contextPolicy = host.GetContextPolicy(contextId);
			if (!contextPolicy.AllowActions)
				throw new CliException(CliErrorCodes.ActionDenied, $"Action '{ActionKind(action)}' requires allowActions policy.");

			var session = host.RequireContext(contextId);
			var properties = McpSemanticRecordingFormatter.MergeSemanticProperties(options.Value.DefaultProperties);
			var before = cache.GetOrRefresh(session, properties, options.Value.TreeLimit, includeHidden: true, refresh: true);
			var resolution = ResolveTarget(contextId, target, before, handles);
			try
			{
				ExecuteAction(session.AppSession, action, resolution.TargetId, contextId, before, handles, options.Value, contextPolicy);
			}
			catch (CliException ex) when (ex.ErrorCode == CliErrorCodes.StaleTarget && !string.IsNullOrWhiteSpace(target.Handle))
			{
				cache.Invalidate(session.SessionId);
				var repairSnapshot = cache.GetOrRefresh(session, properties, options.Value.TreeLimit, includeHidden: true, refresh: true);
				resolution = ResolveTarget(contextId, target, repairSnapshot, handles);
				ExecuteAction(session.AppSession, action, resolution.TargetId, contextId, repairSnapshot, handles, options.Value, contextPolicy);
			}

			cache.Invalidate(session.SessionId);
			var after = cache.GetOrRefresh(session, properties, options.Value.TreeLimit, includeHidden: true, refresh: true);
			if (expect is not null && !string.IsNullOrWhiteSpace(resolution.Handle))
			{
				var verifiedTarget = handles.Resolve(contextId, resolution.Handle!, after);
				resolution = new ActionResolution(
					verifiedTarget.TargetId,
					verifiedTarget.Entry.Handle,
					verifiedTarget.Strategy,
					verifiedTarget.Confidence,
					verifiedTarget.Entry.OriginalRevision,
					verifiedTarget.CurrentRevision);
			}
			var verification = expect is null ? null : Verify(session, cache, options.Value, resolution.TargetId, expect);
			return new McpActionResult
			{
				ContextId = contextId,
				Action = ActionKind(action),
				RevisionBefore = before.SequenceNumber,
				RevisionAfter = after.SequenceNumber,
				Resolved = new McpResolvedElement
				{
					Handle = resolution.Handle,
					TargetId = resolution.TargetId,
					Strategy = resolution.Strategy,
					Confidence = resolution.Confidence,
					OriginalRevision = resolution.OriginalRevision,
					CurrentRevision = resolution.CurrentRevision,
				},
				Verification = verification,
				Observation = CreateObservation(observe, before, after, resolution.TargetId),
			};
		}, new { contextId, target, action, expect, observe });

		return McpCallToolResults.FromLegacy(response, static data => (McpActionResult)data!, result =>
			[new TextContentBlock { Text = result.Observation ?? $"{result.Action} completed at revision {result.RevisionAfter}." }], contextId, LatestRevision(host, contextId));
	}

	[McpServerTool(Name = "deepflow_wait", UseStructuredContent = true, OutputSchemaType = typeof(McpWaitResult), ReadOnly = true, OpenWorld = false), Description("Wait for element, count, property, visibility, UI stability, target responsiveness, or window-title conditions.")]
	public static CallToolResult Wait(
		McpToolRunner runner,
		McpSessionHost host,
		McpSnapshotCache cache,
		McpElementHandleRegistry handles,
		IOptions<DeepFlowMcpOptions> options,
		string contextId,
		McpAgentSelector? target = null,
		McpWaitCondition condition = McpWaitCondition.Exists,
		int count = 1,
		McpPropertyMatch? property = null,
		int? timeoutMs = null,
		int intervalMs = TimeoutDefaults.CliWaitIntervalMs,
		int stabilityMs = 500,
		string? initialWindowTitle = null)
	{
		var response = runner.Run(() =>
		{
			var session = host.RequireContext(contextId);
			if (intervalMs <= 0)
				throw new CliException(CliErrorCodes.InvalidArguments, "intervalMs must be greater than zero.");
			if (count < 0)
				throw new CliException(CliErrorCodes.InvalidArguments, "count cannot be negative.");
			if (stabilityMs <= 0)
				throw new CliException(CliErrorCodes.InvalidArguments, "stabilityMs must be greater than zero.");
			if (target is null && condition is not (McpWaitCondition.Stable or McpWaitCondition.Responsive or McpWaitCondition.WindowTitleChanged))
				throw new CliException(CliErrorCodes.InvalidArguments, "target is required for this wait condition.");

			var timeout = Math.Max(1, timeoutMs ?? options.Value.DefaultTimeoutMs);
			var stopwatch = Stopwatch.StartNew();
			var propertyNames = PropertiesOrDefaults(property is null ? null : [property.Name], options.Value.DefaultProperties);
			var baselineTitle = initialWindowTitle ?? session.GetMainWindowTitle();
			string? previousFingerprint = null;
			long stableSinceMs = 0;
			while (stopwatch.ElapsedMilliseconds <= timeout)
			{
				if (condition == McpWaitCondition.Responsive)
				{
					try
					{
						_ = session.AppSession.Send<object>(new PingCommandRequest(Math.Min(intervalMs, timeout)), Math.Min(intervalMs, timeout));
						return new McpWaitResult
						{
							ContextId = contextId,
							Condition = condition,
							Satisfied = true,
							ElapsedMs = stopwatch.ElapsedMilliseconds,
							Revision = cache.GetLatestRevision(session.SessionId) ?? 0,
						};
					}
					catch (CliException) when (stopwatch.ElapsedMilliseconds < timeout)
					{
						Thread.Sleep(Math.Min(intervalMs, Math.Max(1, timeout - (int)stopwatch.ElapsedMilliseconds)));
						continue;
					}
				}

				if (condition == McpWaitCondition.WindowTitleChanged)
				{
					var currentTitle = session.GetMainWindowTitle();
					if (!string.Equals(currentTitle, baselineTitle, StringComparison.Ordinal))
					{
						return new McpWaitResult
						{
							ContextId = contextId,
							Condition = condition,
							Satisfied = true,
							ElapsedMs = stopwatch.ElapsedMilliseconds,
							Revision = cache.GetLatestRevision(session.SessionId) ?? 0,
							WindowTitle = currentTitle,
						};
					}

					Thread.Sleep(Math.Min(intervalMs, Math.Max(1, timeout - (int)stopwatch.ElapsedMilliseconds)));
					continue;
				}

				var snapshot = cache.GetOrRefresh(session, propertyNames, options.Value.TreeLimit, refresh: true);
				if (condition == McpWaitCondition.Stable)
				{
					var fingerprint = SnapshotFingerprint(snapshot);
					if (string.Equals(fingerprint, previousFingerprint, StringComparison.Ordinal))
					{
						if (stopwatch.ElapsedMilliseconds - stableSinceMs >= stabilityMs)
						{
							return new McpWaitResult
							{
								ContextId = contextId,
								Condition = condition,
								Satisfied = true,
								ElapsedMs = stopwatch.ElapsedMilliseconds,
								Revision = snapshot.SequenceNumber,
								MatchCount = snapshot.NodeCount,
							};
						}
					}
					else
					{
						previousFingerprint = fingerprint;
						stableSinceMs = stopwatch.ElapsedMilliseconds;
					}

					Thread.Sleep(Math.Min(intervalMs, Math.Max(1, timeout - (int)stopwatch.ElapsedMilliseconds)));
					continue;
				}

				var found = FindTargetMatches(contextId, target!, snapshot, handles, options.Value.TreeLimit);
				if (ConditionSatisfied(condition, found, count, property))
				{
					var selector = target!.ToCliSelector();
					var matches = found.Matches.Select(match => ToElementMatch(
						handles.Register(contextId, match.Node.TargetId, StableSelector(selector, match.Node), match.Node, snapshot.SequenceNumber),
						match.Node)).ToArray();
					return new McpWaitResult
					{
						ContextId = contextId,
						Condition = condition,
						Satisfied = true,
						ElapsedMs = stopwatch.ElapsedMilliseconds,
						Revision = snapshot.SequenceNumber,
						MatchCount = found.MatchCount,
						Matches = matches,
					};
				}

				Thread.Sleep(Math.Min(intervalMs, Math.Max(1, timeout - (int)stopwatch.ElapsedMilliseconds)));
			}

			throw new CliException(CliErrorCodes.CommandTimeout, $"Wait for {condition} timed out after {timeout} ms.");
		}, new { contextId, target, condition, count, property, timeoutMs, intervalMs, stabilityMs, initialWindowTitle });

		return McpCallToolResults.FromLegacy(response, static data => (McpWaitResult)data!, contextId: contextId, revision: LatestRevision(host, contextId));
	}

	[McpServerTool(Name = "deepflow_capture", UseStructuredContent = true, OutputSchemaType = typeof(McpCaptureResult), ReadOnly = true, OpenWorld = false), Description("Capture a native screenshot for an explicit context and return image content plus compact metadata.")]
	public static CallToolResult Capture(
		McpToolRunner runner,
		McpSessionHost host,
		McpSnapshotCache cache,
		McpElementHandleRegistry handles,
		DeepFlowResourceStore resources,
		IOptions<DeepFlowMcpOptions> options,
		string contextId,
		McpAgentSelector? target = null,
		McpImageFormat format = McpImageFormat.Png)
	{
		var validation = runner.Run(() =>
		{
			var session = host.RequireContext(contextId);
			if (target is null || (string.IsNullOrWhiteSpace(target.Handle) && target.ToCliSelector().IsEmpty))
				return null;

			var snapshot = cache.GetOrRefresh(session, options.Value.DefaultProperties, options.Value.TreeLimit, refresh: false);
			return ResolveTarget(contextId, target, snapshot, handles).TargetId;
		}, new { contextId });
		if (!validation.Success)
			return McpCallToolResults.Error(validation, contextId, LatestRevision(host, contextId));

		var resolvedTargetId = validation.Data as string;
		var capture = runner.Run(() =>
		{
			var session = host.RequireContext(contextId);
			var response = host.Send<ScreenshotCommandResponse>(contextId, new ScreenshotCommandRequest
			{
				Format = format switch
				{
					McpImageFormat.Jpeg => ImageFormat.Jpeg,
					McpImageFormat.Bmp => ImageFormat.Bmp,
					_ => ImageFormat.Png,
				},
				TargetId = resolvedTargetId,
				TimeoutMs = options.Value.DefaultTimeoutMs,
			}, options.Value.DefaultTimeoutMs);
			var screenshot = new ScreenshotFileService().Process(response, new ScreenshotFileOptions { IncludeBase64 = true });
			return new ScreenshotCaptureData(screenshot, resources.StoreContextScreenshot(contextId, cache.GetLatestRevision(session.SessionId), screenshot));
		}, new { contextId, target, format });
		return McpCallToolResults.FromLegacy(capture, data =>
		{
			var screenshot = (ScreenshotCaptureData)data!;
			return new McpCaptureResult
			{
				ContextId = contextId,
				MimeType = "image/" + screenshot.Screenshot.Format,
				Width = screenshot.Screenshot.Width,
				Height = screenshot.Screenshot.Height,
				Revision = LatestRevision(host, contextId) ?? 0,
				TargetId = screenshot.Screenshot.TargetId,
				ResourceUri = screenshot.Resource.Uri,
			};
		}, result =>
		{
			var screenshot = (ScreenshotCaptureData)capture.Data!;
			return
			[
				ImageContentBlock.FromBytes(
					Convert.FromBase64String(screenshot.Screenshot.BytesBase64 ?? string.Empty),
					result.MimeType),
				new ResourceLinkBlock
				{
					Uri = result.ResourceUri,
					Name = "DeepFlow screenshot",
					MimeType = "application/json",
				},
			];
		}, contextId, LatestRevision(host, contextId));
	}

	[McpServerTool(Name = "deepflow_diagnose", UseStructuredContent = true, OutputSchemaType = typeof(McpDiagnosisResult), ReadOnly = true, OpenWorld = false), Description("Check target health and binding failures, then return a concise diagnosis and likely recovery.")]
	public static CallToolResult Diagnose(
		McpToolRunner runner,
		McpSessionHost host,
		IOptions<DeepFlowMcpOptions> options,
		DeepFlowResourceStore resources,
		string contextId)
	{
		var response = runner.Run(() =>
		{
			host.RequireContext(contextId);
			var status = host.GetContextStatus(contextId);
			var failures = host.Send<BindingFailureBatchDto>(contextId,
				new GetBindingFailuresCommandRequest(null, 100, options.Value.DefaultTimeoutMs),
				options.Value.DefaultTimeoutMs);
			var count = failures.Failures.Count;
			var resource = resources.StoreContextDiagnostic(contextId, "bindings", new { status, failures });
			return new McpDiagnosisResult
			{
				ContextId = contextId,
				IsAlive = status.IsAlive,
				BindingFailureCount = count,
				Summary = status.IsAlive
					? count == 0 ? "Target is responsive; no binding failures were reported." : $"Target is responsive; {count} binding failure(s) were reported."
					: "Target is no longer responsive.",
				SuggestedRecovery = !status.IsAlive ? "Open a new context for a live target." : count > 0 ? "Inspect the binding failure resource for source and path details." : null,
				Revision = status.Revision ?? 0,
				ResourceUri = resource.Uri,
			};
		}, new { contextId });

		return McpCallToolResults.FromLegacy(response, static data => (McpDiagnosisResult)data!, result =>
		[
			new TextContentBlock { Text = result.Summary },
			new ResourceLinkBlock { Uri = result.ResourceUri, Name = "DeepFlow binding diagnostics", MimeType = "application/json" },
		], contextId, LatestRevision(host, contextId));
	}

	[McpServerTool(Name = "deepflow_close_context", UseStructuredContent = true, OutputSchemaType = typeof(McpCloseContextResult), ReadOnly = false, OpenWorld = false), Description("Close an explicit context, stop its streams, and detach from its target.")]
	public static CallToolResult CloseContext(
		McpToolRunner runner,
		McpSessionHost host,
		McpElementHandleRegistry handles,
		string contextId)
	{
		var response = runner.Run(() =>
		{
			host.RequireContext(contextId);
			host.CloseContext(contextId);
			handles.RemoveContext(contextId);
			return new McpCloseContextResult { ContextId = contextId, Closed = true };
		}, new { contextId });
		return McpCallToolResults.FromLegacy(response, static data => (McpCloseContextResult)data!);
	}

	private static McpContextResult ToContextResult(McpTargetStatus status) =>
		new()
		{
			ContextId = status.ContextId ?? string.Empty,
			ProcessId = status.ProcessId,
			ProcessName = status.ProcessName,
			WindowTitle = status.MainWindowTitle,
			IsAlive = status.IsAlive,
		};

	private static long? LatestRevision(McpSessionHost host, string contextId) =>
		host.TryGetContextStatus(contextId, out var status) ? status.Revision : null;

	private static IReadOnlyList<string> PropertiesOrDefaults(IReadOnlyList<string>? properties, IReadOnlyList<string> defaults)
	{
		if (properties is null || properties.Count == 0)
			return defaults;

		return defaults.Concat(properties).Where(static property => !string.IsNullOrWhiteSpace(property)).Distinct(StringComparer.Ordinal).ToArray();
	}

	private static ElementSelector StableSelector(ElementSelector requested, TreeNodeData node)
	{
		if (TryText(node, KnownProperties.AutomationId, out var automationId))
			return new ElementSelector { AutomationId = automationId, TypeName = node.TypeName, Visible = requested.Visible, Enabled = requested.Enabled };
		if (TryText(node, KnownProperties.AutomationName, out var automationName))
			return new ElementSelector { Name = automationName, TypeName = node.TypeName, Visible = requested.Visible, Enabled = requested.Enabled };
		if (TryText(node, KnownProperties.Name, out var name))
			return new ElementSelector { Name = name, TypeName = node.TypeName, Visible = requested.Visible, Enabled = requested.Enabled };

		return new ElementSelector
		{
			TypeName = requested.TypeName ?? node.TypeName,
			TypeContains = requested.TypeContains,
			Name = requested.Name,
			AutomationId = requested.AutomationId,
			Text = requested.Text,
			PropertyEquals = requested.PropertyEquals,
			PropertyContains = requested.PropertyContains,
			PropertyRegex = requested.PropertyRegex,
			Visible = requested.Visible,
			Enabled = requested.Enabled,
			CaseSensitive = requested.CaseSensitive,
		};
	}

	private static McpElementMatch ToElementMatch(HandleEntry entry, TreeNodeData node) =>
		new()
		{
			Handle = entry.Handle,
			TargetId = node.TargetId,
			Type = node.TypeName,
			AutomationId = Value(node, KnownProperties.AutomationId),
			Name = Value(node, KnownProperties.AutomationName) ?? Value(node, KnownProperties.Name),
			Text = KnownProperties.TextualIdentityPropertyNames.Select(property => Value(node, property)).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)),
			Path = node.Path,
			Properties = node.Properties,
		};

	private static string? Value(TreeNodeData node, string property) =>
		node.Properties.TryGetValue(property, out var value) ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;

	private static bool TryText(TreeNodeData node, string property, out string value)
	{
		value = Value(node, property) ?? string.Empty;
		return value.Length > 0;
	}

	private static ActionResolution ResolveTarget(
		string contextId,
		McpAgentSelector target,
		VisualTreeSnapshot snapshot,
		McpElementHandleRegistry handles)
	{
		if (!string.IsNullOrWhiteSpace(target.Handle))
		{
			var resolved = handles.Resolve(contextId, target.Handle!, snapshot);
			return new ActionResolution(resolved.TargetId, resolved.Entry.Handle, resolved.Strategy, resolved.Confidence, resolved.Entry.OriginalRevision, resolved.CurrentRevision);
		}

		var selector = target.ToCliSelector();
		var usedFallback = false;
		ElementResolution resolution;
		try
		{
			resolution = new ElementResolver().Resolve(snapshot, selector);
		}
		catch (CliException ex) when (ex.ErrorCode == CliErrorCodes.NoMatch && target is McpSemanticSelector { Fallback: not null } semantic)
		{
			selector = semantic.Fallback.ToCliSelector();
			usedFallback = true;
			try
			{
				resolution = new ElementResolver().Resolve(snapshot, selector);
			}
			catch (CliException fallbackError) when (fallbackError.ErrorCode == CliErrorCodes.AmbiguousTarget)
			{
				throw CreateAmbiguousElementError(contextId, selector, snapshot, handles);
			}
		}
		catch (CliException ex) when (ex.ErrorCode == CliErrorCodes.AmbiguousTarget)
		{
			throw CreateAmbiguousElementError(contextId, selector, snapshot, handles);
		}

		var entry = handles.Register(contextId, resolution.TargetId, StableSelector(selector, resolution.Summary), resolution.Summary, snapshot.SequenceNumber);
		var strategy = usedFallback ? "fallback_selector" : "selector";
		return new ActionResolution(resolution.TargetId, entry.Handle, strategy, 1.0, snapshot.SequenceNumber, snapshot.SequenceNumber);
	}

	private static CliException CreateAmbiguousElementError(
		string contextId,
		ElementSelector selector,
		VisualTreeSnapshot snapshot,
		McpElementHandleRegistry handles)
	{
		var found = FindMatches(snapshot, selector, 1_000);
		var candidates = found.Matches.Take(20).Select(match =>
		{
			var entry = handles.Register(contextId, match.Node.TargetId, StableSelector(selector, match.Node), match.Node, snapshot.SequenceNumber);
			return new McpAmbiguousElementCandidate
			{
				Handle = entry.Handle,
				TargetId = match.Node.TargetId,
				Type = match.Node.TypeName,
				AutomationId = Value(match.Node, KnownProperties.AutomationId),
				Name = Value(match.Node, KnownProperties.AutomationName) ?? Value(match.Node, KnownProperties.Name),
				Text = KnownProperties.TextualIdentityPropertyNames.Select(property => Value(match.Node, property)).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)),
				Path = match.Node.Path,
			};
		}).ToArray();
		return new CliException(
			CliErrorCodes.AmbiguousTarget,
			$"{found.MatchCount} elements matched the selector. Use a candidate handle, target.index, or a more specific selector.",
			new McpAmbiguousElementDetails { MatchCount = found.MatchCount, Candidates = candidates });
	}

	private static void ExecuteAction(
		ICliAppSession session,
		McpAgentAction action,
		string targetId,
		string contextId,
		VisualTreeSnapshot snapshot,
		McpElementHandleRegistry handles,
		DeepFlowMcpOptions options,
		McpPolicyOptions policy)
	{
		if (action is McpClickAction { Count: <= 0 })
			throw new CliException(CliErrorCodes.InvalidArguments, "action.count must be greater than zero.");

		var common = new CliCommonOptions
		{
			TimeoutMs = options.DefaultTimeoutMs,
			UseShortIds = true,
			AllowActions = true,
			AllowArbitraryInvoke = policy.AllowArbitraryInvoke,
			After = "none",
		};
		var defaults = new CliDefaults { TreeLimit = options.TreeLimit, PropertyNames = [.. options.DefaultProperties] };
		var support = new ActionCommandSupport();
		if (action is McpDragAction drag)
		{
			var destination = ResolveTarget(contextId, drag.Destination, snapshot, handles);
			support.ExecuteTwoTarget(
				"drag",
				session,
				common,
				defaults,
				new ElementSelector { TargetId = targetId },
				new ElementSelector { TargetId = destination.TargetId },
				(source, destinationTarget) => new DragAndDropCommandRequest
				{
					TargetId = source,
					DestinationTargetId = destinationTarget,
					DurationMs = drag.DurationMs,
					TimeoutMs = options.DefaultTimeoutMs,
					UseInjectedEvents = true,
				});
			return;
		}

		support.Execute(
			ActionKind(action).ToString().ToLowerInvariant(),
			session,
			common,
			defaults,
			new ElementSelector { TargetId = targetId },
			resolvedTargetId => CreateCommand(action, resolvedTargetId ?? targetId, options),
			requireElementTarget: true);
	}

	private static IpcCommand CreateCommand(McpAgentAction action, string targetId, DeepFlowMcpOptions options) =>
		action switch
		{
			McpClickAction click => new ClickCommandRequest
			{
				TargetId = targetId,
				MouseButton = click.Button switch
				{
					McpMouseButton.Right => MouseButtonKind.Right,
					McpMouseButton.Middle => MouseButtonKind.Middle,
					_ => MouseButtonKind.Left,
				},
				ClickCount = click.Count,
			},
			McpTypeAction type => new TypeTextCommandRequest { TargetId = targetId, Text = Require(type.Text, "action.text"), ClearFirst = type.ClearFirst },
			McpKeyAction key => new KeyPressCommandRequest { TargetId = targetId, Keys = Require(key.Keys, "action.keys"), EnsureForeground = true },
			McpSetAction set => new SetPropertyCommandRequest
			{
				TargetId = targetId,
				PropertyName = Require(set.Property.Name, "action.property.name"),
				PropertyValue = McpValueConversion.ToProtocolValue(set.Property.Value),
			},
			McpFocusAction => new FocusCommandRequest { TargetId = targetId },
			McpInvokeAction invoke when KnownOperations.Contains(invoke.Operation) => new KnownOperationCommandRequest { TargetId = targetId, Operation = invoke.Operation },
			McpInvokeAction invoke => throw new CliException(CliErrorCodes.InvalidArguments, $"Known operation '{invoke.Operation}' is not allow-listed."),
			_ => throw new CliException(CliErrorCodes.InvalidArguments, $"Unsupported action type '{action.GetType().Name}'."),
		};

	private static McpActionKind ActionKind(McpAgentAction action) =>
		action switch
		{
			McpClickAction => McpActionKind.Click,
			McpTypeAction => McpActionKind.Type,
			McpKeyAction => McpActionKind.Key,
			McpSetAction => McpActionKind.Set,
			McpFocusAction => McpActionKind.Focus,
			McpInvokeAction => McpActionKind.Invoke,
			McpDragAction => McpActionKind.Drag,
			_ => throw new CliException(CliErrorCodes.InvalidArguments, $"Unsupported action type '{action.GetType().Name}'."),
		};

	private static string Require(string? value, string name) =>
		string.IsNullOrWhiteSpace(value) ? throw new CliException(CliErrorCodes.InvalidArguments, $"{name} is required.") : value;

	private static McpVerificationResult Verify(
		McpSession session,
		McpSnapshotCache cache,
		DeepFlowMcpOptions options,
		string targetId,
		McpActionExpectation expectation)
	{
		var stopwatch = Stopwatch.StartNew();
		var timeout = Math.Max(1, expectation.TimeoutMs);
		while (stopwatch.ElapsedMilliseconds <= timeout)
		{
			var snapshot = cache.GetOrRefresh(session, [expectation.PropertyEquals.Name], options.TreeLimit, refresh: true);
			var node = snapshot.Nodes.FirstOrDefault(node => string.Equals(node.TargetId, targetId, StringComparison.Ordinal));
			if (node is not null && node.Properties.TryGetValue(expectation.PropertyEquals.Name, out var actual)
				&& string.Equals(Convert.ToString(actual, CultureInfo.InvariantCulture), expectation.PropertyEquals.TextValue, StringComparison.OrdinalIgnoreCase))
			{
				return new McpVerificationResult { Passed = true, ElapsedMs = stopwatch.ElapsedMilliseconds };
			}

			Thread.Sleep(Math.Min(100, Math.Max(1, timeout - (int)stopwatch.ElapsedMilliseconds)));
		}

		throw new CliException(CliErrorCodes.CommandTimeout, $"Action completed, but verification of property '{expectation.PropertyEquals.Name}' timed out after {timeout} ms.");
	}

	private static string? CreateObservation(McpObserveMode mode, VisualTreeSnapshot before, VisualTreeSnapshot after, string targetId) =>
		mode switch
		{
			McpObserveMode.None => null,
			McpObserveMode.Delta => McpSemanticRecordingFormatter.FormatDelta(before, after).Text,
			McpObserveMode.Tree => McpSemanticRecordingFormatter.FormatSnapshot(after).Text,
			McpObserveMode.Target => McpSemanticRecordingFormatter.FormatSnapshot(VisualTreeSnapshot.Create(after.SequenceNumber, after.Nodes.Where(node => node.TargetId == targetId), after.RequestedPropertyNames)).Text,
			_ => null,
		};

	private static FindResultData FindMatches(VisualTreeSnapshot snapshot, ElementSelector selector, int limit) =>
		new FindSnapshotService().Find(snapshot, new FindSnapshotOptions
		{
			TypeName = selector.TypeName,
			TypeContains = selector.TypeContains,
			Name = selector.Name,
			AutomationId = selector.AutomationId,
			Text = selector.Text,
			PropertyEquals = selector.PropertyEquals,
			PropertyContains = selector.PropertyContains,
			PropertyRegex = selector.PropertyRegex,
			Visible = selector.Visible,
			Enabled = selector.Enabled,
			CaseSensitive = selector.CaseSensitive,
			Limit = limit,
			IncludePath = true,
			IncludeProperties = true,
			Properties = snapshot.RequestedPropertyNames,
			UseShortIds = true,
		});

	private static FindResultData FindTargetMatches(
		string contextId,
		McpAgentSelector target,
		VisualTreeSnapshot snapshot,
		McpElementHandleRegistry handles,
		int limit)
	{
		if (!string.IsNullOrWhiteSpace(target.Handle))
		{
			try
			{
				var resolved = handles.Resolve(contextId, target.Handle!, snapshot);
				var node = snapshot.Nodes.First(node => string.Equals(node.TargetId, resolved.TargetId, StringComparison.Ordinal));
				var shaped = new TreeSnapshotService().ShapeOne(node, snapshot, new TreeSnapshotOptions
				{
					IncludePath = true,
					IncludeTypeNames = true,
					Properties = snapshot.RequestedPropertyNames,
					UseShortIds = true,
				});
				return new FindResultData { MatchCount = 1, MaxMatches = 1, Matches = [new FindMatchData { Node = shaped }] };
			}
			catch (CliException ex) when (ex.ErrorCode is CliErrorCodes.NoMatch or CliErrorCodes.TargetNotFound)
			{
				return new FindResultData { MatchCount = 0, MaxMatches = 1 };
			}
		}

		var found = FindMatches(snapshot, target.ToCliSelector(), limit);
		if (found.MatchCount == 0 && target is McpSemanticSelector { Fallback: not null } semantic)
			return FindMatches(snapshot, semantic.Fallback.ToCliSelector(), limit);
		return found;
	}

	private static string SnapshotFingerprint(VisualTreeSnapshot snapshot)
	{
		var builder = new StringBuilder();
		foreach (var node in snapshot.Nodes.OrderBy(static node => node.TargetId, StringComparer.Ordinal))
		{
			builder.Append(node.TargetId).Append('|').Append(node.TypeName).Append('|').Append(node.ParentId).Append(';');
			foreach (var property in node.Properties.OrderBy(static property => property.Key, StringComparer.Ordinal))
				builder.Append(property.Key).Append('=').Append(Convert.ToString(property.Value, CultureInfo.InvariantCulture)).Append(';');
		}

		return builder.ToString();
	}

	private static bool ConditionSatisfied(McpWaitCondition condition, FindResultData result, int count, McpPropertyMatch? property)
	{
		return condition switch
		{
			McpWaitCondition.Exists => result.MatchCount > 0,
			McpWaitCondition.Absent => result.MatchCount == 0,
			McpWaitCondition.ExactCount => result.MatchCount == count,
			McpWaitCondition.MinimumCount => result.MatchCount >= count,
			McpWaitCondition.PropertyEquals => AnyProperty(result, RequireProperty(property), equal: true),
			McpWaitCondition.PropertyDiffers => result.MatchCount > 0 && AnyProperty(result, RequireProperty(property), equal: false),
			McpWaitCondition.Enabled => AnyBoolean(result, KnownProperties.IsEnabled, expected: true),
			McpWaitCondition.Disabled => AnyBoolean(result, KnownProperties.IsEnabled, expected: false),
			McpWaitCondition.Visible => AnyBoolean(result, KnownProperties.IsVisible, expected: true),
			McpWaitCondition.Hidden => result.MatchCount > 0 && AnyBoolean(result, KnownProperties.IsVisible, expected: false),
			_ => false,
		};
	}

	private static McpPropertyMatch RequireProperty(McpPropertyMatch? property) =>
		property ?? throw new CliException(CliErrorCodes.InvalidArguments, "property is required for this wait condition.");

	private static bool AnyProperty(FindResultData result, McpPropertyMatch property, bool equal) =>
		result.Matches.Any(match =>
		{
			var matches = match.Node.Properties.TryGetValue(property.Name, out var value)
				&& string.Equals(Convert.ToString(value, CultureInfo.InvariantCulture), property.TextValue, StringComparison.OrdinalIgnoreCase);
			return equal ? matches : !matches;
		});

	private static bool AnyBoolean(FindResultData result, string property, bool expected) =>
		result.Matches.Any(match => match.Node.Properties.TryGetValue(property, out var value) && Convert.ToBoolean(value, CultureInfo.InvariantCulture) == expected);

	private sealed record ActionResolution(string TargetId, string? Handle, string Strategy, double Confidence, long OriginalRevision, long CurrentRevision);
}
