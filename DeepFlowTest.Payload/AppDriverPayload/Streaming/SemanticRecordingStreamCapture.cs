namespace DeepFlowTest.AppDriverPayload.Streaming;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using DeepFlowTest.AppDriverPayload.Diagnostics;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Utility;
using DeepFlowTest.Utility.WpfUtility.Tree;
using Forms = System.Windows.Forms;

internal sealed class SemanticRecordingStreamCapture : IDisposable
{
	private const int SchemaVersion = 1;
	private readonly TreeService treeService;
	private readonly StartSendingCommandRequest request;
	private readonly SemanticRecordingOptionsDto options;
	private readonly string recordingId = Guid.NewGuid().ToString("N");
	private readonly ConcurrentQueue<SemanticRecordingFrame> queuedFrames = new();
	private readonly object textGate = new();
	private readonly IReadOnlyList<string>? requestedProperties;
	private readonly int maxQueuedActions;
	private readonly int maxBatchFrames;
	private readonly int textIdleMs;
	private int queuedFrameCount;
	private int droppedActionCount;
	private long nextFrameSequence;
	private VisualTreeSnapshot? previousSnapshot;
	private bool emittedInitialFrame;
	private bool disposed;
	private WpfSemanticInputListener? wpfListener;
	private WinFormsSemanticInputListener? winFormsListener;
	private RecordedTarget? textTarget;
	private string? textTargetId;
	private StringBuilder? textBuffer;
	private DateTimeOffset lastTextUtc;

	private SemanticRecordingStreamCapture(StartSendingCommandRequest request, TreeService treeService)
	{
		this.request = request ?? throw new ArgumentNullException(nameof(request));
		this.treeService = treeService ?? throw new ArgumentNullException(nameof(treeService));
		options = request.SemanticRecording ?? new SemanticRecordingOptionsDto();
		requestedProperties = request.PropNames;
		maxQueuedActions = Math.Max(1, options.MaxQueuedActions);
		maxBatchFrames = Math.Max(1, options.MaxBatchFrames);
		textIdleMs = Math.Max(0, options.TextIdleMs);
	}

	public static (SemanticRecordingStreamCapture? Capture, StandardIpcResponse? Error) TryStart(
		StartSendingCommandRequest request,
		TreeService treeService)
	{
		var capture = new SemanticRecordingStreamCapture(request, treeService);
		try
		{
			var runResult = RunOnTargetUiThread(capture.StartOnUiThread);
			if (runResult == UiThreadRunResult.Finished && capture.HasListener)
				return (capture, null);

			capture.Dispose();
			return (null, StandardIpcResponse.FromError(
				"No supported UI input thread is available for semantic recording.",
				ProtocolConstants.ErrorCodes.UnsupportedTarget,
				PayloadLog.CurrentCorrelationId));
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			capture.Dispose();
			return (null, StandardIpcResponse.FromError(
				$"Semantic recording could not start: {ex.Message}",
				ProtocolConstants.ErrorCodes.UnsupportedTarget,
				PayloadLog.CurrentCorrelationId));
		}
	}

	private bool HasListener => wpfListener is not null || winFormsListener is not null;

	public object Capture(long batchSequenceNumber)
	{
		if (!disposed)
			RunOnTargetUiThread(() => FlushIdleText(DateTimeOffset.UtcNow, force: false));

		var frames = new List<SemanticRecordingFrame>();
		var currentSnapshot = treeService.CaptureSnapshot(new TreeSnapshotOptions
		{
			RequestedPropertyNames = requestedProperties,
			RootTargetId = request.TargetId,
			IncludeHidden = true,
			MaxNodeCount = Math.Max(1, options.MaxNodeCount),
		});

		if (!emittedInitialFrame)
		{
			emittedInitialFrame = true;
			EnqueueFrame(new SemanticRecordingFrame
			{
				FrameKind = "recording-started",
				Metadata = new Dictionary<string, object?>
				{
					["processId"] = currentSnapshot.ProcessId,
					["rootTargetId"] = request.TargetId,
					["intervalMs"] = request.IntervalMs,
					["textIdleMs"] = textIdleMs,
					["maxQueuedActions"] = maxQueuedActions,
					["maxBatchFrames"] = maxBatchFrames,
				},
			});
			if (options.IncludeInitialSnapshot)
			{
				EnqueueFrame(new SemanticRecordingFrame
				{
					FrameKind = "snapshot",
					Snapshot = currentSnapshot,
				});
			}
		}
		else if (previousSnapshot is not null)
		{
			var delta = VisualTreeSnapshotDelta.Create(previousSnapshot, currentSnapshot);
			if (delta.HasChanges)
			{
				EnqueueFrame(new SemanticRecordingFrame
				{
					FrameKind = "delta",
					Delta = delta,
				});
			}
		}

		previousSnapshot = currentSnapshot;
		while (frames.Count < maxBatchFrames && queuedFrames.TryDequeue(out var frame))
		{
			Interlocked.Decrement(ref queuedFrameCount);
			frames.Add(frame);
		}

		frames.Sort(static (left, right) =>
		{
			var timestampComparison = left.TimestampUtc.CompareTo(right.TimestampUtc);
			return timestampComparison != 0
				? timestampComparison
				: left.SequenceNumber.CompareTo(right.SequenceNumber);
		});

		return new SemanticRecordingBatch
		{
			SchemaVersion = SchemaVersion,
			RecordingId = recordingId,
			BatchSequenceNumber = batchSequenceNumber,
			GeneratedUtc = DateTimeOffset.UtcNow,
			DroppedActionCount = Interlocked.Exchange(ref droppedActionCount, 0),
			Frames = frames,
		};
	}

	public void Dispose()
	{
		if (disposed)
			return;

		disposed = true;
		RunOnTargetUiThread(() =>
		{
			lock (textGate)
				FlushTextBufferLocked(DateTimeOffset.UtcNow);

			wpfListener?.Dispose();
			wpfListener = null;
			winFormsListener?.Dispose();
			winFormsListener = null;
		});
	}

	private void StartOnUiThread()
	{
		if (CanInstallWpfListener())
		{
			wpfListener = new WpfSemanticInputListener(this);
			wpfListener.Start();
		}

		if (Forms.Application.MessageLoop)
		{
			winFormsListener = new WinFormsSemanticInputListener(this);
			winFormsListener.Start();
		}
	}

	private void RecordMouseAction(object? rawTarget, object? rawSource, string actionKind, string mouseButton, int clickCount)
	{
		if (disposed || AppHooks.IsSyntheticInputActive || rawTarget is null)
			return;

		var target = treeService.DescribeTargetForRecording(rawTarget, rawSource, requestedProperties);
		lock (textGate)
			FlushTextBufferLocked(DateTimeOffset.UtcNow);

		EnqueueFrame(new SemanticRecordingFrame
		{
			FrameKind = "action",
			Action = new RecordedInputAction
			{
				ActionKind = actionKind,
				Target = target,
				MouseButton = mouseButton,
				ClickCount = clickCount,
			},
		});
	}

	private void RecordMouseWheelAction(object? rawTarget, object? rawSource, int delta)
	{
		if (disposed || AppHooks.IsSyntheticInputActive || rawTarget is null || delta == 0)
			return;

		var target = treeService.DescribeTargetForRecording(rawTarget, rawSource, requestedProperties);
		lock (textGate)
			FlushTextBufferLocked(DateTimeOffset.UtcNow);

		EnqueueFrame(new SemanticRecordingFrame
		{
			FrameKind = "action",
			Action = new RecordedInputAction
			{
				ActionKind = "wheel",
				Target = target,
				WheelDelta = delta,
			},
		});
	}

	private void RecordText(object? rawTarget, object? rawSource, string text)
	{
		if (disposed || AppHooks.IsSyntheticInputActive || rawTarget is null || string.IsNullOrEmpty(text))
			return;

		var target = treeService.DescribeTargetForRecording(rawTarget, rawSource, requestedProperties);
		lock (textGate)
		{
			var now = DateTimeOffset.UtcNow;
			if (!string.Equals(textTargetId, target.TargetId, StringComparison.Ordinal))
				FlushTextBufferLocked(now);

			textTarget = target;
			textTargetId = target.TargetId;
			textBuffer ??= new StringBuilder();
			textBuffer.Append(text);
			lastTextUtc = now;
		}
	}

	private void RecordKey(object? rawTarget, object? rawSource, string keys)
	{
		if (disposed || AppHooks.IsSyntheticInputActive || rawTarget is null || string.IsNullOrWhiteSpace(keys))
			return;

		var target = treeService.DescribeTargetForRecording(rawTarget, rawSource, requestedProperties);
		lock (textGate)
			FlushTextBufferLocked(DateTimeOffset.UtcNow);

		EnqueueFrame(new SemanticRecordingFrame
		{
			FrameKind = "action",
			Action = new RecordedInputAction
			{
				ActionKind = "key",
				Target = target,
				Keys = keys,
			},
		});
	}

	private void FlushIdleText(DateTimeOffset now, bool force)
	{
		lock (textGate)
		{
			if (force || textBuffer is not null && textBuffer.Length != 0 && (now - lastTextUtc).TotalMilliseconds >= textIdleMs)
				FlushTextBufferLocked(now);
		}
	}

	private void FlushTextBufferLocked(DateTimeOffset timestamp)
	{
		if (textTarget is null || textBuffer is null || textBuffer.Length == 0)
			return;

		EnqueueFrame(new SemanticRecordingFrame
		{
			FrameKind = "action",
			TimestampUtc = timestamp,
			Action = new RecordedInputAction
			{
				ActionKind = "type",
				Target = textTarget,
				Text = textBuffer.ToString(),
			},
		});
		textTarget = null;
		textTargetId = null;
		textBuffer.Clear();
	}

	private void EnqueueFrame(SemanticRecordingFrame frame)
	{
		if (Interlocked.Increment(ref queuedFrameCount) > maxQueuedActions)
		{
			Interlocked.Decrement(ref queuedFrameCount);
			Interlocked.Increment(ref droppedActionCount);
			return;
		}

		frame.SchemaVersion = SchemaVersion;
		frame.RecordingId = recordingId;
		if (frame.SequenceNumber <= 0)
			frame.SequenceNumber = Interlocked.Increment(ref nextFrameSequence);
		if (frame.TimestampUtc == default)
			frame.TimestampUtc = DateTimeOffset.UtcNow;
		queuedFrames.Enqueue(frame);
	}

	private object? NormalizeWpfTarget(object? rawTarget)
	{
		if (rawTarget is not DependencyObject dependencyObject)
			return rawTarget;

		var current = dependencyObject;
		while (current is not null)
		{
			if (treeService.IsUsefulRecordingTarget(current))
				return current;

			current = GetWpfParent(current);
		}

		return rawTarget;
	}

	private static DependencyObject? GetWpfParent(DependencyObject target)
	{
		try
		{
			if (target is Visual or Visual3D)
				return VisualTreeHelper.GetParent(target) ?? LogicalTreeHelper.GetParent(target);
			return LogicalTreeHelper.GetParent(target);
		}
		catch (InvalidOperationException)
		{
			return null;
		}
	}

	private static string GetWpfMouseButton(MouseButton button) =>
		button switch
		{
			MouseButton.Right => "right",
			MouseButton.Middle => "middle",
			_ => "left",
		};

	private static string GetMouseActionKind(string button, int clickCount) =>
		button switch
		{
			"right" => "right-click",
			_ when clickCount > 1 => "double-click",
			_ => "click",
		};

	private static bool ShouldRecordWpfKey(KeyEventArgs args)
	{
		var key = args.Key == Key.System ? args.SystemKey : args.Key;
		if (key is Key.None or Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt)
			return false;
		if (Keyboard.Modifiers != ModifierKeys.None)
			return true;
		return key is Key.Enter
			or Key.Return
			or Key.Escape
			or Key.Tab
			or Key.Up
			or Key.Down
			or Key.Left
			or Key.Right
			or Key.Home
			or Key.End
			or Key.PageUp
			or Key.PageDown
			or Key.Insert
			or Key.Delete
			or Key.Back
			or Key.Space
			or >= Key.F1 and <= Key.F24;
	}

	private static string FormatWpfKey(KeyEventArgs args)
	{
		var key = args.Key == Key.System ? args.SystemKey : args.Key;
		var modifiers = Keyboard.Modifiers;
		return modifiers == ModifierKeys.None ? key.ToString() : $"{modifiers}+{key}";
	}

	private static bool CanInstallWpfListener()
	{
		try
		{
			if (Application.Current?.Dispatcher.CheckAccess() == true)
				return true;
		}
		catch (InvalidOperationException)
		{
		}

		try
		{
			foreach (PresentationSource? source in PresentationSource.CurrentSources)
			{
				if (source?.RootVisual is not null && source.Dispatcher.CheckAccess())
					return true;
			}
		}
		catch (InvalidOperationException)
		{
		}

		return false;
	}

	private static UiThreadRunResult RunOnTargetUiThread(Action action)
	{
		var dispatcher = FindWpfRootDispatcher();
		if (dispatcher is not null)
		{
			ThreadUtility.RunOnDispatcher(dispatcher, action);
			return UiThreadRunResult.Finished;
		}

		var control = FindWinFormsControl();
		if (control is not null)
		{
			if (control.InvokeRequired)
				control.Invoke(action);
			else
				action();
			return UiThreadRunResult.Finished;
		}

		return UiThreadRunResult.Unable;
	}

	private static Dispatcher? FindWpfRootDispatcher()
	{
		try
		{
			foreach (PresentationSource? source in PresentationSource.CurrentSources)
				if (source?.RootVisual is not null && source.Dispatcher is not null)
					return source.Dispatcher;
		}
		catch (InvalidOperationException)
		{
		}

		try
		{
			return Application.Current?.Dispatcher;
		}
		catch (InvalidOperationException)
		{
			return null;
		}
	}

	private static Forms.Control? FindWinFormsControl()
	{
		try
		{
			return Forms.Application.OpenForms
				.Cast<Forms.Form?>()
				.FirstOrDefault(static form => form is not null && !form.IsDisposed && form.IsHandleCreated);
		}
		catch (InvalidOperationException)
		{
			return null;
		}
	}

	private sealed class WpfSemanticInputListener
	{
		private readonly SemanticRecordingStreamCapture owner;
		private object? lastMouseSource;
		private MouseButton lastMouseButton;
		private int lastMouseClickCount;
		private DateTimeOffset lastMouseUtc;

		public WpfSemanticInputListener(SemanticRecordingStreamCapture owner)
		{
			this.owner = owner;
		}

		public void Start()
		{
			InputManager.Current.PostProcessInput += OnPostProcessInput;
		}

		public void Dispose()
		{
			InputManager.Current.PostProcessInput -= OnPostProcessInput;
		}

		private void OnPostProcessInput(object sender, ProcessInputEventArgs e)
		{
			if (e.StagingItem.Input is MouseWheelEventArgs wheelArgs && IsWpfMouseWheelEvent(wheelArgs.RoutedEvent))
			{
				owner.RecordMouseWheelAction(owner.NormalizeWpfTarget(wheelArgs.OriginalSource), wheelArgs.OriginalSource, wheelArgs.Delta);
				return;
			}

			if (e.StagingItem.Input is MouseButtonEventArgs mouseArgs)
			{
				var shouldRecord = ShouldRecordMouse(mouseArgs);
				if (!shouldRecord)
					return;

				var rawTarget = owner.NormalizeWpfTarget(mouseArgs.OriginalSource);
				var button = GetWpfMouseButton(mouseArgs.ChangedButton);
				var clickCount = Math.Max(1, mouseArgs.ClickCount);
				owner.RecordMouseAction(rawTarget, mouseArgs.OriginalSource, GetMouseActionKind(button, clickCount), button, clickCount);
				return;
			}

			if (e.StagingItem.Input is TextCompositionEventArgs textArgs &&
				textArgs.RoutedEvent == TextCompositionManager.TextInputEvent)
			{
				owner.RecordText(owner.NormalizeWpfTarget(textArgs.OriginalSource), textArgs.OriginalSource, textArgs.Text);
				return;
			}

			if (e.StagingItem.Input is KeyEventArgs keyArgs &&
				keyArgs.RoutedEvent == Keyboard.PreviewKeyDownEvent &&
				ShouldRecordWpfKey(keyArgs))
			{
				owner.RecordKey(owner.NormalizeWpfTarget(keyArgs.OriginalSource), keyArgs.OriginalSource, FormatWpfKey(keyArgs));
			}
		}

		private bool ShouldRecordMouse(MouseButtonEventArgs mouseArgs)
		{
			if (!IsWpfMouseUpEvent(mouseArgs.RoutedEvent))
				return false;

			var now = DateTimeOffset.UtcNow;
			var clickCount = Math.Max(1, mouseArgs.ClickCount);
			if (ReferenceEquals(lastMouseSource, mouseArgs.OriginalSource)
				&& lastMouseButton == mouseArgs.ChangedButton
				&& lastMouseClickCount == clickCount
				&& (now - lastMouseUtc).TotalMilliseconds < 50)
			{
				return false;
			}

			lastMouseSource = mouseArgs.OriginalSource;
			lastMouseButton = mouseArgs.ChangedButton;
			lastMouseClickCount = clickCount;
			lastMouseUtc = now;
			return true;
		}

		private static bool IsWpfMouseUpEvent(RoutedEvent routedEvent) =>
			routedEvent == Mouse.MouseUpEvent
			|| routedEvent == UIElement.MouseUpEvent
			|| routedEvent == ContentElement.MouseUpEvent
			|| string.Equals(routedEvent.Name, "MouseUp", StringComparison.Ordinal)
			|| string.Equals(routedEvent.Name, "PreviewMouseUp", StringComparison.Ordinal)
			|| routedEvent.Name.EndsWith("ButtonUp", StringComparison.Ordinal);

		private static bool IsWpfMouseWheelEvent(RoutedEvent routedEvent) =>
			routedEvent == Mouse.MouseWheelEvent
			|| routedEvent == UIElement.MouseWheelEvent
			|| routedEvent == ContentElement.MouseWheelEvent
			|| string.Equals(routedEvent.Name, "MouseWheel", StringComparison.Ordinal);
	}

	private sealed class WinFormsSemanticInputListener : Forms.IMessageFilter
	{
		private const int WM_KEYDOWN = 0x0100;
		private const int WM_SYSKEYDOWN = 0x0104;
		private const int WM_CHAR = 0x0102;
		private const int WM_LBUTTONUP = 0x0202;
		private const int WM_LBUTTONDBLCLK = 0x0203;
		private const int WM_RBUTTONUP = 0x0205;
		private const int WM_MOUSEWHEEL = 0x020A;
		private readonly SemanticRecordingStreamCapture owner;
		private bool suppressNextLeftUp;

		public WinFormsSemanticInputListener(SemanticRecordingStreamCapture owner)
		{
			this.owner = owner;
		}

		public void Start()
		{
			Forms.Application.AddMessageFilter(this);
		}

		public void Dispose()
		{
			Forms.Application.RemoveMessageFilter(this);
		}

		public bool PreFilterMessage(ref Forms.Message m)
		{
			var control = Forms.Control.FromHandle(m.HWnd);
			if (control is null)
				return false;

			switch (m.Msg)
			{
				case WM_LBUTTONDBLCLK:
					suppressNextLeftUp = true;
					owner.RecordMouseAction(control, control, "double-click", "left", 2);
					break;
				case WM_LBUTTONUP:
					if (suppressNextLeftUp)
					{
						suppressNextLeftUp = false;
						break;
					}

					owner.RecordMouseAction(control, control, "click", "left", 1);
					break;
				case WM_RBUTTONUP:
					owner.RecordMouseAction(control, control, "right-click", "right", 1);
					break;
				case WM_MOUSEWHEEL:
					var delta = unchecked((short)((m.WParam.ToInt64() >> 16) & 0xffff));
					owner.RecordMouseWheelAction(control, control, delta);
					break;
				case WM_CHAR:
					var character = (char)m.WParam.ToInt32();
					if (!char.IsControl(character))
						owner.RecordText(control, control, character.ToString());
					break;
				case WM_KEYDOWN:
				case WM_SYSKEYDOWN:
					if (ShouldRecordWinFormsKey((Forms.Keys)m.WParam.ToInt32()))
						owner.RecordKey(control, control, FormatWinFormsKey((Forms.Keys)m.WParam.ToInt32()));
					break;
			}

			return false;
		}

		private static bool ShouldRecordWinFormsKey(Forms.Keys key)
		{
			var keyCode = key & Forms.Keys.KeyCode;
			if (keyCode is Forms.Keys.ShiftKey or Forms.Keys.ControlKey or Forms.Keys.Menu)
				return false;
			if (Forms.Control.ModifierKeys != Forms.Keys.None)
				return true;
			return keyCode is Forms.Keys.Enter
				or Forms.Keys.Escape
				or Forms.Keys.Tab
				or Forms.Keys.Up
				or Forms.Keys.Down
				or Forms.Keys.Left
				or Forms.Keys.Right
				or Forms.Keys.Home
				or Forms.Keys.End
				or Forms.Keys.PageUp
				or Forms.Keys.PageDown
				or Forms.Keys.Insert
				or Forms.Keys.Delete
				or Forms.Keys.Back
				or Forms.Keys.Space
				or >= Forms.Keys.F1 and <= Forms.Keys.F24;
		}

		private static string FormatWinFormsKey(Forms.Keys key)
		{
			var keyCode = key & Forms.Keys.KeyCode;
			var modifiers = Forms.Control.ModifierKeys;
			return modifiers == Forms.Keys.None ? keyCode.ToString() : $"{modifiers}+{keyCode}";
		}
	}
}
