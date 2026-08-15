namespace DeepFlowTest.Mcp.Tools;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using DeepFlowTest.Automation;
using DeepFlowTest.Contracts;
using DeepFlowTest.Mcp.Configuration;
using DeepFlowTest.Mcp.Contracts;
using DeepFlowTest.Mcp.Hosting;
using DeepFlowTest.Mcp.Resources;
using DeepFlowTest.Utility.WpfUtility.Tree;
using DeepFlowTest.Interop;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using DeepFlowMcpOptions = DeepFlowTest.Mcp.Configuration.McpServerOptions;

[McpServerToolType]
internal static class AgentTools
{
	private const string PropertyExtractionErrorMarker = "DeepFlowTest.Utility.WpfUtility.Tree.PropertyExtractionError";

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
			_ => McpToolResponse.Fail(AutomationErrorCodes.InvalidArguments, "Unsupported context target."),
		};

		return McpCallToolResults.FromLegacy(response, static data => ToContextResult((McpTargetStatus)data!), result =>
			[new TextContentBlock { Text = $"Opened context {result.ContextId} for {result.ProcessName ?? "target"}." }]);
	}

	[McpServerTool(Name = "deepflow_observe", UseStructuredContent = true, OutputSchemaType = typeof(McpObservationResult), ReadOnly = true, OpenWorld = false), Description("Return a compact semantic UI snapshot for an explicit context, with revision and stable target identifiers.")]
	public static CallToolResult Observe(
		McpToolRunner runner,
		McpSessionHost host,
		McpSnapshotCache cache,
		McpElementHandleRegistry handles,
		DeepFlowResourceStore resources,
		IOptions<DeepFlowMcpOptions> options,
		[Description("Context handle returned by deepflow_open_context.")] string contextId,
		[Description("Condensed is the token-efficient default. JSON returns explicit nodes and should use a small limit.")] McpObservationFormat format = McpObservationFormat.Condensed,
		[Description("Only additional UI properties needed for this observation; each property is repeated per node.")] IReadOnlyList<string>? properties = null,
		[Description("Maximum returned nodes. Keep this small for JSON observations.")] int? limit = null,
		[Description("Include hidden elements. This can substantially enlarge the observation.")] bool includeHidden = false,
		[Description("Also return structured element records. Off by default because compact text already describes the same UI.")] bool includeElements = false,
		[Description("Bypass the snapshot cache and read the target now.")] bool refresh = false)
	{
		var response = runner.Run(() =>
		{
			var session = host.RequireContext(contextId);
			var propertyNames = PropertiesOrDefaults(properties, options.Value.DefaultProperties);
			if (format == McpObservationFormat.Condensed)
				propertyNames = McpSemanticRecordingFormatter.MergeSemanticProperties(propertyNames);

			var snapshot = cache.GetOrRefresh(session, propertyNames, Math.Max(options.Value.TreeLimit, limit ?? 0), includeHidden: true, refresh: refresh);
			var shaped = new TreeSnapshotService().Shape(snapshot, new TreeSnapshotOptions
			{
				Shape = TreeShape.Flat,
				Limit = limit ?? options.Value.TreeLimit,
				IncludeHidden = includeHidden,
				IncludePath = format == McpObservationFormat.Condensed && includeElements,
				IncludeTypeNames = true,
				Properties = propertyNames,
				UseShortIds = true,
			});
			SanitizeObservationNodes(shaped.Nodes);
			var elements = includeElements
				? CreateObservationElements(contextId, snapshot, shaped.Nodes, handles)
				: [];
			if (format == McpObservationFormat.Condensed)
			{
				var includedIds = shaped.Nodes.Select(static node => node.TargetId).ToHashSet(StringComparer.Ordinal);
				var filtered = VisualTreeSnapshot.Create(
					snapshot.SequenceNumber,
					snapshot.Nodes.Where(node => includedIds.Contains(node.TargetId)),
					snapshot.RequestedPropertyNames);
				var condensed = McpSemanticRecordingFormatter.FormatSnapshot(filtered);
				var resource = resources.StoreContextSnapshot(contextId, snapshot.SequenceNumber, snapshot);
				return new McpObservationResult
				{
					ContextId = contextId,
					Revision = snapshot.SequenceNumber,
					NodeCount = shaped.Nodes.Count,
					Format = McpSemanticRecordingFormatter.FormatName,
					Text = condensed.Text,
					Elements = elements,
					ResourceUri = resource.Uri,
				};
			}

			var jsonResource = resources.StoreContextSnapshot(contextId, snapshot.SequenceNumber, shaped);
			return new McpObservationResult
			{
				ContextId = contextId,
				Revision = snapshot.SequenceNumber,
				NodeCount = shaped.Nodes.Count,
				Format = "json",
				Nodes = shaped.Nodes,
				Elements = elements,
				ResourceUri = jsonResource.Uri,
			};
		}, new { contextId, format, properties, limit, includeHidden, includeElements, refresh });

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
		[Description("Context handle returned by deepflow_open_context.")] string contextId,
		[Description("Handle, current target ID, or semantic selector to find.")] McpAgentSelector target,
		[Description("Additional UI properties to return with each match.")] IReadOnlyList<string>? properties = null,
		[Description("Maximum number of returned matches.")] int limit = 50,
		[Description("Bypass the snapshot cache and read the target now.")] bool refresh = false)
	{
		var response = runner.Run(() =>
		{
			var session = host.RequireContext(contextId);
			if (limit <= 0)
				throw new AutomationException(AutomationErrorCodes.InvalidArguments, "limit must be greater than zero.");

			var propertyNames = PropertiesOrDefaults(properties, options.Value.DefaultProperties);
			var snapshot = cache.GetOrRefresh(session, propertyNames, Math.Max(options.Value.TreeLimit, limit), refresh: refresh);
			var selector = target.ToAutomationSelector();
			if (!string.IsNullOrWhiteSpace(target.Handle))
			{
				var resolved = handles.Resolve(contextId, target.Handle!, snapshot);
				var node = snapshot.Nodes.FirstOrDefault(node => string.Equals(node.TargetId, resolved.TargetId, StringComparison.Ordinal))
					?? throw new AutomationException(AutomationErrorCodes.TargetNotFound, $"Resolved element handle '{target.Handle}' was not present in the current snapshot.");
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
				selector = semantic.Fallback.ToAutomationSelector();
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

		return McpCallToolResults.FromLegacy(response, static data => (McpFindResult)data!, result =>
			[new TextContentBlock { Text = $"Found {result.MatchCount} match(es) at revision {result.Revision}." }], contextId, LatestRevision(host, contextId));
	}

	[McpServerTool(Name = "deepflow_act", UseStructuredContent = true, OutputSchemaType = typeof(McpActionResult), ReadOnly = false, Destructive = true, OpenWorld = false), Description("Resolve, act, optionally verify, and observe in one call. Supports stable handles and automatic selector repair after UI revisions.")]
	public static CallToolResult Act(
		McpToolRunner runner,
		McpSessionHost host,
		McpSnapshotCache cache,
		McpElementHandleRegistry handles,
		IOptions<DeepFlowMcpOptions> options,
		[Description("Context handle returned by deepflow_open_context.")] string contextId,
		[Description("Handle, current target ID, or semantic selector for the action target.")] McpAgentSelector target,
		[Description("Discriminated click, wheel, type, key, set, focus, invoke, or drag action.")] McpAgentAction action,
		[Description("Optional property expectation verified after the action.")] McpActionExpectation? expect = null,
		[Description("Observation returned after the action; delta is the compact default.")] McpObserveMode observe = McpObserveMode.Delta)
	{
		var response = runner.Run(() =>
		{
			var contextPolicy = host.GetContextPolicy(contextId);
			var typedAction = ToAutomationAction(action);
			var pipeline = new AutomationActionPipeline();
			var descriptor = pipeline.Prepare(
				typedAction,
				new AutomationActionPipelineHooks
				{
					DemandPolicy = actionDescriptor =>
					{
						if (!contextPolicy.AllowActions)
							throw new AutomationException(AutomationErrorCodes.ActionDenied, $"Action '{actionDescriptor.Name}' requires allowActions policy.");
					},
				});
			var session = host.RequireContext(contextId);
			var properties = McpSemanticRecordingFormatter.MergeSemanticProperties(options.Value.DefaultProperties);
			var before = cache.GetOrRefresh(session, properties, options.Value.TreeLimit, includeHidden: true, refresh: true);
			var resolution = ResolveTarget(contextId, target, before, handles);
			var destination = action is McpDragAction drag ? ResolveTarget(contextId, drag.Destination, before, handles) : null;
			VisualTreeSnapshot? after = null;
			McpVerificationResult? verification = null;
			McpActionDelta? structuredDelta = null;
			IReadOnlyList<McpElementMatch> observedElements = [];
			var executionOptions = new AutomationExecutionOptions(
				options.Value.DefaultTimeoutMs,
				options.Value.TreeLimit,
				[.. options.Value.DefaultProperties],
				ObservationMode.None,
				UseShortIds: true);
			_ = pipeline.ExecutePrepared(
				session.AppSession,
				executionOptions,
				new AutomationActionRequest(
					typedAction,
					new ElementSelector { TargetId = resolution.TargetId },
					destination is null ? null : new ElementSelector { TargetId = destination.TargetId }),
				descriptor,
				new AutomationActionPipelineHooks
				{
					InvalidateCache = () => cache.Invalidate(session.SessionId),
					RepairStaleTarget = !string.IsNullOrWhiteSpace(target.Handle)
						? (_, _) =>
						{
							var repairSnapshot = cache.GetOrRefresh(session, properties, options.Value.TreeLimit, includeHidden: true, refresh: true);
							resolution = ResolveTarget(contextId, target, repairSnapshot, handles);
							destination = action is McpDragAction retryDrag
								? ResolveTarget(contextId, retryDrag.Destination, repairSnapshot, handles)
								: null;
							return new AutomationActionRetry(
								new ElementSelector { TargetId = resolution.TargetId },
								destination is null ? null : new ElementSelector { TargetId = destination.TargetId });
						}
						: null,
					Verify = _ =>
					{
						after = cache.GetOrRefresh(session, properties, options.Value.TreeLimit, includeHidden: true, refresh: true);
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
						verification = expect is null ? null : Verify(session, cache, options.Value, resolution.TargetId, expect);
					},
					Observe = _ =>
					{
						after ??= cache.GetOrRefresh(session, properties, options.Value.TreeLimit, includeHidden: true, refresh: true);
						structuredDelta = observe == McpObserveMode.Delta ? CreateActionDelta(contextId, before, after, handles) : null;
						observedElements = observe == McpObserveMode.Target
							? CreateActionElements(contextId, after, handles, node => node.TargetId == resolution.TargetId)
							: [];
					},
				});
			after ??= cache.GetOrRefresh(session, properties, options.Value.TreeLimit, includeHidden: true, refresh: true);
			return new McpActionResult
			{
				ContextId = contextId,
				Action = ActionKind(typedAction),
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
				Delta = structuredDelta,
				Elements = observedElements,
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
		[Description("Context handle returned by deepflow_open_context.")] string contextId,
		[Description("Element target; omit only for stable, responsive, or window-title-changed waits.")] McpAgentSelector? target = null,
		[Description("State that must become true before the wait succeeds.")] McpWaitCondition condition = McpWaitCondition.Exists,
		[Description("Expected count for exact-count or lower bound for minimum-count.")] int count = 1,
		[Description("Typed property comparison for property-equals or property-differs.")] McpPropertyMatch? property = null,
		[Description("Maximum wait duration in milliseconds.")] int? timeoutMs = null,
		[Description("Polling interval in milliseconds.")] int intervalMs = TimeoutDefaults.CliWaitIntervalMs,
		[Description("Required unchanged duration for a stable wait.")] int stabilityMs = 500,
		[Description("Optional explicit baseline for a window-title-changed wait; defaults to the current title.")] string? initialWindowTitle = null)
	{
		var response = runner.Run(() =>
		{
			var session = host.RequireContext(contextId);
			if (intervalMs <= 0)
				throw new AutomationException(AutomationErrorCodes.InvalidArguments, "intervalMs must be greater than zero.");
			if (count < 0)
				throw new AutomationException(AutomationErrorCodes.InvalidArguments, "count cannot be negative.");
			if (stabilityMs <= 0)
				throw new AutomationException(AutomationErrorCodes.InvalidArguments, "stabilityMs must be greater than zero.");
			if (target is null && condition is not (McpWaitCondition.Stable or McpWaitCondition.Responsive or McpWaitCondition.WindowTitleChanged))
				throw new AutomationException(AutomationErrorCodes.InvalidArguments, "target is required for this wait condition.");

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
					catch (AutomationException) when (stopwatch.ElapsedMilliseconds < timeout)
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
					var selector = target!.ToAutomationSelector();
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

			throw new AutomationException(AutomationErrorCodes.CommandTimeout, $"Wait for {condition} timed out after {timeout} ms.");
		}, new { contextId, target, condition, count, property, timeoutMs, intervalMs, stabilityMs, initialWindowTitle });

		return McpCallToolResults.FromLegacy(response, static data => (McpWaitResult)data!, result =>
			[new TextContentBlock { Text = $"Wait {result.Condition}: satisfied after {result.ElapsedMs} ms." }], contextId, LatestRevision(host, contextId));
	}

	[McpServerTool(Name = "deepflow_capture", UseStructuredContent = true, OutputSchemaType = typeof(McpCaptureResult), ReadOnly = true, OpenWorld = false), Description("Capture a native screenshot for an explicit context and return image content plus compact metadata.")]
	public static CallToolResult Capture(
		McpToolRunner runner,
		McpSessionHost host,
		McpSnapshotCache cache,
		McpElementHandleRegistry handles,
		DeepFlowResourceStore resources,
		IOptions<DeepFlowMcpOptions> options,
		[Description("Context handle returned by deepflow_open_context.")] string contextId,
		[Description("Optional element to capture; omit to capture the target window.")] McpAgentSelector? target = null,
		[Description("Screenshot image encoding.")] McpImageFormat format = McpImageFormat.Png)
	{
		var capture = runner.Run(() =>
		{
			var session = host.RequireContext(contextId);
			string? resolvedTargetId = null;
			if (target is not null && (!string.IsNullOrWhiteSpace(target.Handle) || !target.ToAutomationSelector().IsEmpty))
			{
				var snapshot = cache.GetOrRefresh(session, options.Value.DefaultProperties, options.Value.TreeLimit, refresh: false);
				resolvedTargetId = ResolveTarget(contextId, target, snapshot, handles).TargetId;
			}
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
		[Description("Context handle returned by deepflow_open_context.")] string contextId)
	{
		var response = runner.Run(() =>
		{
			_ = host.RequireContext(contextId);
			var status = host.GetContextStatus(contextId);
			BindingFailureBatchDto failures = new();
			string? targetErrorCode = null;
			string? targetErrorMessage = null;
			string? diagnosticErrorCode = null;
			string? diagnosticErrorMessage = null;
			var responsive = false;
			try
			{
				_ = host.Send<PingCommandResponse>(contextId, new PingCommandRequest(options.Value.DefaultTimeoutMs), options.Value.DefaultTimeoutMs);
				responsive = true;
			}
			catch (AutomationException ex)
			{
				targetErrorCode = ex.ErrorCode;
				targetErrorMessage = ex.Message;
			}
			catch (NamedPipeSessionException ex)
			{
				targetErrorCode = ProtocolErrorMapper.Map(ex.ErrorCode);
				targetErrorMessage = ex.Message;
			}
			catch (ProtocolException ex)
			{
				targetErrorCode = ProtocolErrorMapper.Map(ex.ErrorCode);
				targetErrorMessage = ex.Message;
			}
			if (responsive)
			{
				try
				{
					failures = host.Send<BindingFailureBatchDto>(contextId,
						new GetBindingFailuresCommandRequest(null, 100, options.Value.DefaultTimeoutMs),
						options.Value.DefaultTimeoutMs);
				}
				catch (AutomationException ex)
				{
					diagnosticErrorCode = ex.ErrorCode;
					diagnosticErrorMessage = ex.Message;
				}
				catch (NamedPipeSessionException ex)
				{
					diagnosticErrorCode = ProtocolErrorMapper.Map(ex.ErrorCode);
					diagnosticErrorMessage = ex.Message;
				}
				catch (ProtocolException ex)
				{
					diagnosticErrorCode = ProtocolErrorMapper.Map(ex.ErrorCode);
					diagnosticErrorMessage = ex.Message;
				}
			}
			var count = failures.Failures.Count;
			var logs = resources.SnapshotLogs(contextId).Select(static entry => new McpDiagnosticLogEntry
			{
				ContextId = entry.ContextId,
				Sequence = entry.Sequence,
				TimestampUtc = entry.TimestampUtc,
				Level = entry.Level,
				Code = entry.Code,
				Message = entry.Message,
			}).ToArray();
			var resource = resources.StoreContextDiagnostic(contextId, "bindings", new { status, responsive, targetErrorCode, targetErrorMessage, diagnosticErrorCode, diagnosticErrorMessage, failures, logs });
			return new McpDiagnosisResult
			{
				ContextId = contextId,
				IsAlive = status.IsAlive,
				IsResponsive = responsive,
				BindingFailureCount = count,
				RecentLogCount = logs.Length,
				RecentLogs = logs,
				TargetErrorCode = targetErrorCode,
				DiagnosticErrorCode = diagnosticErrorCode,
				Summary = !status.IsAlive
					? "Target process has exited."
					: !responsive
						? $"Target process is alive but did not respond: {targetErrorMessage}"
						: diagnosticErrorCode is not null
							? $"Target is responsive, but binding diagnostics failed: {diagnosticErrorMessage}"
							: count == 0 ? "Target is responsive; no binding failures were reported." : $"Target is responsive; {count} binding failure(s) were reported.",
				SuggestedRecovery = !status.IsAlive ? "Open a new context for a live target." : !responsive ? "Retry once, then open a new context if the pipe remains unresponsive." : diagnosticErrorCode is not null ? "Retry diagnostics; reopen the context if the failure repeats." : count > 0 ? "Inspect the binding failure resource for source and path details." : null,
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
		[Description("Context handle returned by deepflow_open_context.")] string contextId)
	{
		var response = runner.Run(() =>
		{
			host.CloseContext(contextId);
			return new McpCloseContextResult { ContextId = contextId, Closed = true };
		}, new { contextId });
		return McpCallToolResults.FromLegacy(response, static data => (McpCloseContextResult)data!, result =>
			[new TextContentBlock { Text = $"Closed context {result.ContextId}." }]);
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
			Properties = node.Properties
				.Where(static property => !IsRedundantMatchProperty(property.Key))
				.Where(static property => !IsPropertyExtractionError(property.Value))
				.Where(static property => property.Value is not string text || text.Length > 0)
				.ToDictionary(static property => property.Key, static property => property.Value, StringComparer.Ordinal),
		};

	private static bool IsRedundantMatchProperty(string propertyName) =>
		propertyName is KnownProperties.Name
			or KnownProperties.AutomationName
			or KnownProperties.AutomationNameAlias
			or KnownProperties.AutomationId
			or KnownProperties.AutomationIdAlias;

	private static string? Value(TreeNodeData node, string property) =>
		node.Properties.TryGetValue(property, out var value) && !IsPropertyExtractionError(value)
			? Convert.ToString(value, CultureInfo.InvariantCulture)
			: null;

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

		var selector = target.ToAutomationSelector();
		var usedFallback = false;
		ElementResolution resolution;
		try
		{
			resolution = new ElementResolver().Resolve(snapshot, selector);
		}
		catch (AutomationException ex) when (ex.ErrorCode == AutomationErrorCodes.NoMatch && target is McpSemanticSelector { Fallback: not null } semantic)
		{
			selector = semantic.Fallback.ToAutomationSelector();
			usedFallback = true;
			try
			{
				resolution = new ElementResolver().Resolve(snapshot, selector);
			}
			catch (AutomationException fallbackError) when (fallbackError.ErrorCode == AutomationErrorCodes.AmbiguousTarget)
			{
				throw CreateAmbiguousElementError(contextId, selector, snapshot, handles);
			}
		}
		catch (AutomationException ex) when (ex.ErrorCode == AutomationErrorCodes.AmbiguousTarget)
		{
			throw CreateAmbiguousElementError(contextId, selector, snapshot, handles);
		}

		var entry = handles.Register(contextId, resolution.TargetId, StableSelector(selector, resolution.Summary), resolution.Summary, snapshot.SequenceNumber);
		var strategy = usedFallback ? "fallback_selector" : "selector";
		return new ActionResolution(resolution.TargetId, entry.Handle, strategy, 1.0, snapshot.SequenceNumber, snapshot.SequenceNumber);
	}

	private static AutomationException CreateAmbiguousElementError(
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
		return new AutomationException(
			AutomationErrorCodes.AmbiguousTarget,
			$"{found.MatchCount} elements matched the selector. Use a candidate handle, target.index, or a more specific selector.",
			new McpAmbiguousElementDetails { MatchCount = found.MatchCount, Candidates = candidates });
	}

	private static AutomationAction ToAutomationAction(McpAgentAction action) =>
		action switch
		{
			McpClickAction click => new ClickAction(
				click.Button switch
				{
					McpMouseButton.Right => MouseButtonKind.Right,
					McpMouseButton.Middle => MouseButtonKind.Middle,
					_ => MouseButtonKind.Left,
				},
				click.Count),
			McpMouseWheelAction wheel => new MouseWheelAction(wheel.Delta),
			McpTypeAction type => new TypeTextAction(Require(type.Text, "action.text"), type.ClearFirst),
			McpKeyAction key => new KeyPressAction(Require(key.Keys, "action.keys"), TimeoutDefaults.KeyboardDelayMs, EnsureForeground: true),
			McpSetAction set => new SetPropertyAction(set.Property.Name, McpValueConversion.ToProtocolValue(set.Property.Value)),
			McpFocusAction => new FocusAction(),
			McpInvokeAction invoke => new KnownOperationAction(invoke.Operation),
			McpDragAction drag => new DragAction(DurationMs: drag.DurationMs, UseInjectedEvents: true),
			_ => throw new AutomationException(AutomationErrorCodes.InvalidArguments, $"Unsupported action type '{action.GetType().Name}'."),
		};

	private static McpActionKind ActionKind(AutomationAction action) =>
		AutomationActionRegistry.Describe(action).Name switch
		{
			"click" => McpActionKind.Click,
			"wheel" => McpActionKind.Wheel,
			"type" => McpActionKind.Type,
			"key" => McpActionKind.Key,
			"set" => McpActionKind.Set,
			"focus" => McpActionKind.Focus,
			"invoke" => McpActionKind.Invoke,
			"drag" => McpActionKind.Drag,
			var name => throw new AutomationException(AutomationErrorCodes.InvalidArguments, $"Unsupported action '{name}'."),
		};

	private static string Require(string? value, string name) =>
		string.IsNullOrWhiteSpace(value) ? throw new AutomationException(AutomationErrorCodes.InvalidArguments, $"{name} is required.") : value;

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

		throw new AutomationException(AutomationErrorCodes.CommandTimeout, $"Action completed, but verification of property '{expectation.PropertyEquals.Name}' timed out after {timeout} ms.");
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

	private static IReadOnlyList<McpElementMatch> CreateObservationElements(
		string contextId,
		VisualTreeSnapshot snapshot,
		IReadOnlyList<TreeNodeData> nodes,
		McpElementHandleRegistry handles)
	{
		var sourceById = snapshot.Nodes.ToDictionary(static node => node.TargetId, StringComparer.Ordinal);
		return nodes
			.Where(node => sourceById.TryGetValue(node.TargetId, out var source) && IsAgentRelevant(source))
			.Select(node =>
			{
				var selector = StableSelector(new ElementSelector(), node);
				return ToElementMatch(handles.Register(contextId, node.TargetId, selector, node, snapshot.SequenceNumber), node);
			})
			.ToArray();
	}

	private static IReadOnlyList<McpElementMatch> CreateActionElements(
		string contextId,
		VisualTreeSnapshot snapshot,
		McpElementHandleRegistry handles,
		Func<VisualTreeNodeDto, bool> predicate)
	{
		var tree = new TreeSnapshotService();
		return snapshot.Nodes.Where(predicate).Where(IsAgentRelevant).Select(node =>
		{
			var shaped = tree.ShapeOne(node, snapshot, new TreeSnapshotOptions
			{
				IncludePath = true,
				IncludeTypeNames = true,
				Properties = snapshot.RequestedPropertyNames,
				UseShortIds = true,
			});
			var selector = StableSelector(new ElementSelector(), shaped);
			return ToElementMatch(handles.Register(contextId, node.TargetId, selector, shaped, snapshot.SequenceNumber), shaped);
		}).ToArray();
	}

	private static McpActionDelta CreateActionDelta(
		string contextId,
		VisualTreeSnapshot before,
		VisualTreeSnapshot after,
		McpElementHandleRegistry handles)
	{
		var delta = VisualTreeSnapshotDelta.Create(before, after);
		var addedIds = delta.Added.Select(static node => node.TargetId).ToHashSet(StringComparer.Ordinal);
		var changedIds = delta.Changed.Select(static node => node.TargetId).ToHashSet(StringComparer.Ordinal);
		return new McpActionDelta
		{
			HasChanges = delta.HasChanges,
			Added = CreateActionElements(contextId, after, handles, node => addedIds.Contains(node.TargetId)),
			Changed = CreateActionElements(contextId, after, handles, node => changedIds.Contains(node.TargetId)),
			Removed = delta.RemovedTargetIds.Select(targetId => new McpRemovedElement
			{
				Handle = handles.TryGetHandle(contextId, targetId),
				TargetId = targetId,
			}).ToArray(),
		};
	}

	private static bool IsAgentRelevant(VisualTreeNodeDto node) =>
		HasText(node, KnownProperties.AutomationId)
		|| HasText(node, KnownProperties.AutomationName)
		|| HasText(node, KnownProperties.Name)
		|| KnownProperties.TextualIdentityPropertyNames.Any(property => HasText(node, property));

	private static bool HasText(VisualTreeNodeDto node, string property) =>
		node.Properties.TryGetValue(property, out var value)
		&& !IsPropertyExtractionError(value)
		&& !string.IsNullOrWhiteSpace(Convert.ToString(value, CultureInfo.InvariantCulture));

	private static bool IsPropertyExtractionError(object? value) =>
		value is PropertyExtractionError
		|| string.Equals(Convert.ToString(value, CultureInfo.InvariantCulture), PropertyExtractionErrorMarker, StringComparison.Ordinal);

	private static void SanitizeObservationNodes(IEnumerable<TreeNodeData> nodes)
	{
		foreach (var node in nodes)
		{
			node.Properties = node.Properties
				.Where(static property => !IsPropertyExtractionError(property.Value))
				.Where(static property => property.Value is not string text || text.Length > 0)
				.ToDictionary(static property => property.Key, static property => property.Value, StringComparer.Ordinal);
		}
	}

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
				var node = snapshot.Nodes.FirstOrDefault(node => string.Equals(node.TargetId, resolved.TargetId, StringComparison.Ordinal));
				if (node is null)
					return new FindResultData { MatchCount = 0, MaxMatches = 1 };
				var shaped = new TreeSnapshotService().ShapeOne(node, snapshot, new TreeSnapshotOptions
				{
					IncludePath = true,
					IncludeTypeNames = true,
					Properties = snapshot.RequestedPropertyNames,
					UseShortIds = true,
				});
				return new FindResultData { MatchCount = 1, MaxMatches = 1, Matches = [new FindMatchData { Node = shaped }] };
			}
			catch (AutomationException ex) when (ex.ErrorCode is AutomationErrorCodes.NoMatch or AutomationErrorCodes.TargetNotFound)
			{
				return new FindResultData { MatchCount = 0, MaxMatches = 1 };
			}
		}

		var found = FindMatches(snapshot, target.ToAutomationSelector(), limit);
		if (found.MatchCount == 0 && target is McpSemanticSelector { Fallback: not null } semantic)
			return FindMatches(snapshot, semantic.Fallback.ToAutomationSelector(), limit);
		return found;
	}

	private static string SnapshotFingerprint(VisualTreeSnapshot snapshot)
	{
		var semantic = McpSemanticRecordingFormatter.FormatSnapshot(snapshot).Text;
		return string.Join('\n', semantic
			.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
			.Where(static line => !line.StartsWith("dft-condensed/", StringComparison.Ordinal)
				&& !line.StartsWith("@1 snapshot ", StringComparison.Ordinal))
			.Select(static line => System.Text.RegularExpressions.Regex.Replace(line, @" \[[0-9a-f]+\]", string.Empty)));
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
		property ?? throw new AutomationException(AutomationErrorCodes.InvalidArguments, "property is required for this wait condition.");

	private static bool AnyProperty(FindResultData result, McpPropertyMatch property, bool equal) =>
		result.Matches.Any(match =>
		{
			var matches = match.Node.Properties.TryGetValue(property.Name, out var value)
				&& string.Equals(Convert.ToString(value, CultureInfo.InvariantCulture), property.TextValue, StringComparison.OrdinalIgnoreCase);
			return equal ? matches : !matches;
		});

	private static bool AnyBoolean(FindResultData result, string property, bool expected) =>
		result.Matches.Any(match => match.Node.Properties.TryGetValue(property, out var value)
			&& bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var actual)
			&& actual == expected);

	private sealed record ActionResolution(string TargetId, string? Handle, string Strategy, double Confidence, long OriginalRevision, long CurrentRevision);
}
