namespace DeepFlowTest.Utility.WpfUtility.Tree;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Shared;
using DeepFlowTest.Utility;
using Forms = System.Windows.Forms;

public sealed partial class TreeService
{
	private readonly TargetIdService targetIds;
	private readonly VisualTreePropertyExtractor propertyExtractor;
	private readonly Func<IReadOnlyList<object>>? rootProvider;
	private long nextSequenceNumber;

	public TreeService(
		TargetIdService? targetIds = null,
		VisualTreePropertyExtractor? propertyExtractor = null,
		Func<IReadOnlyList<object>>? rootProvider = null)
	{
		this.targetIds = targetIds ?? new TargetIdService();
		this.propertyExtractor = propertyExtractor ?? new VisualTreePropertyExtractor();
		this.rootProvider = rootProvider;
	}

	public VisualTreeSnapshot CaptureSnapshot(TreeSnapshotOptions? options = null)
	{
		options ??= new TreeSnapshotOptions();
		var dispatcher = ThreadUtility.FindWpfDispatcher();
		if (dispatcher is not null && !dispatcher.CheckAccess())
		{
			VisualTreeSnapshot? snapshot = null;
			dispatcher.Invoke(() => snapshot = CaptureSnapshotCore(options));
			return snapshot!;
		}

		return CaptureSnapshotCore(options);
	}

	public TargetIdResolution ResolveTarget(string targetId) => targetIds.Resolve(targetId);

	private VisualTreeSnapshot CaptureSnapshotCore(TreeSnapshotOptions options)
	{
		var roots = ResolveRoots(options);
		var nodes = new List<VisualTreeNodeDto>();
		var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
		var requestedProperties = options.RequestedPropertyNames ?? VisualTreePropertyExtractor.DefaultPropertyNames;
		var isTruncated = false;
		string? truncationReason = null;

		foreach (var root in roots)
		{
			if (nodes.Count >= options.MaxNodeCount)
			{
				isTruncated = true;
				truncationReason = $"Snapshot node limit {options.MaxNodeCount} was reached.";
				break;
			}

			AddNode(root, parent: null, depth: 0, siblingIndex: nodes.Count, nodes, visited, requestedProperties, options, ref isTruncated, ref truncationReason);
		}

		return VisualTreeSnapshot.Create(
			++nextSequenceNumber,
			nodes,
			requestedProperties,
			DetermineFrameworkFamily(nodes),
			isTruncated,
			truncationReason);
	}

	private IReadOnlyList<object> ResolveRoots(TreeSnapshotOptions options)
	{
		var rootTargetId = options.RootTargetId;
		if (!string.IsNullOrWhiteSpace(rootTargetId))
		{
			var resolution = targetIds.Resolve(rootTargetId!);
			if (resolution.Status == TargetIdResolutionStatus.Found)
				return new[] { resolution.Target! };

			var errorCode = resolution.Status == TargetIdResolutionStatus.Stale
				? ProtocolConstants.ErrorCodes.StaleTarget
				: ProtocolConstants.ErrorCodes.UnsupportedTarget;
			throw new TreeSnapshotException($"Root target '{rootTargetId}' resolved as {resolution.Status}.", errorCode);
		}

		return DiscoverRoots();
	}

	private IReadOnlyList<object> DiscoverRoots()
	{
		if (rootProvider is not null)
			return rootProvider();

		var roots = new List<object>();
		var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);

		void AddRoot(object? root)
		{
			if (root is null || ShouldExclude(root) || !seen.Add(root))
				return;

			roots.Add(root);
		}

		try
		{
			if (Application.Current is { } application && application.Dispatcher.CheckAccess())
			{
				AddRoot(application);
				AddRoot(new SystemResourceRoot());
				foreach (Window? window in application.Windows)
					AddRoot(window);
			}
		}
		catch (InvalidOperationException)
		{
		}

		try
		{
			foreach (PresentationSource? source in PresentationSource.CurrentSources)
			{
				if (source?.Dispatcher?.CheckAccess() == true)
					AddRoot(source.RootVisual);
			}
		}
		catch (InvalidOperationException)
		{
		}

		try
		{
			foreach (var hwnd in EnumerateProcessTopLevelWindows())
			{
				var source = HwndSource.FromHwnd(hwnd);
				if (source?.Dispatcher.CheckAccess() == true)
					AddRoot(source.RootVisual);
			}
		}
		catch (InvalidOperationException)
		{
		}

		try
		{
			foreach (Forms.Form? form in Forms.Application.OpenForms)
				AddRoot(form);
		}
		catch (InvalidOperationException)
		{
		}

		return roots;
	}

	private VisualTreeNodeDto? AddNode(
		object target,
		VisualTreeNodeDto? parent,
		int depth,
		int siblingIndex,
		List<VisualTreeNodeDto> nodes,
		HashSet<object> visited,
		IEnumerable<string> requestedProperties,
		TreeSnapshotOptions options,
		ref bool isTruncated,
		ref string? truncationReason)
	{
		if (ShouldExclude(target) || !visited.Add(target) || !ShouldInclude(target, options))
			return null;

		if (nodes.Count >= options.MaxNodeCount)
		{
			isTruncated = true;
			truncationReason = $"Snapshot node limit {options.MaxNodeCount} was reached.";
			return null;
		}

		using var wrapper = TargetObjectWrapper.Create(target);
		var targetId = targetIds.GetOrCreateId(target);
		var node = new VisualTreeNodeDto
		{
			TargetId = targetId,
			ParentId = parent?.TargetId,
			IsRoot = parent is null,
			Depth = depth,
			SiblingIndex = siblingIndex,
			TypeName = wrapper.Metadata.DisplayTypeName,
			FrameworkTypeName = wrapper.Metadata.TargetObjectType,
			TargetKind = wrapper.Metadata.Kind.ToString(),
			RuntimeFamily = wrapper.Metadata.RuntimeFamily,
			CanReceiveActions = wrapper.Metadata.CanReceiveActions,
			Hwnd = wrapper.Metadata.Hwnd,
			Properties = propertyExtractor.Extract(target, requestedProperties),
		};
		nodes.Add(node);
		parent?.ChildIds.Add(targetId);

		var children = EnumerateChildren(target, wrapper.Metadata)
			.Where(static child => child is not null)
			.Cast<object>()
			.ToList();
		if (options.MaxDepth.HasValue && depth >= options.MaxDepth.Value)
		{
			if (children.Count != 0)
			{
				isTruncated = true;
				truncationReason ??= $"Snapshot max depth {options.MaxDepth.Value} was reached.";
			}

			return node;
		}

		for (var i = 0; i < children.Count; i++)
		{
			if (isTruncated)
				break;

			AddNode(children[i], node, depth + 1, i, nodes, visited, requestedProperties, options, ref isTruncated, ref truncationReason);
		}

		return node;
	}

	private static IEnumerable<object?> EnumerateChildren(object target, TargetObjectMetadata metadata)
	{
		foreach (var adapter in TreeTargetAdapters)
		{
			if (!adapter.CanHandle(target, metadata))
				continue;

			foreach (var child in adapter.EnumerateChildren(target, metadata))
				yield return child;

			if (adapter.StopsChildEnumeration)
				yield break;
		}
	}

	internal static IEnumerable<DependencyObject> EnumerateVisualChildren(DependencyObject dependencyObject)
	{
		var count = 0;
		try
		{
			if (dependencyObject is Visual or Visual3D)
				count = VisualTreeHelper.GetChildrenCount(dependencyObject);
		}
		catch (InvalidOperationException)
		{
			yield break;
		}

		for (var i = 0; i < count; i++)
		{
			DependencyObject? child = null;
			try
			{
				child = VisualTreeHelper.GetChild(dependencyObject, i);
			}
			catch (InvalidOperationException)
			{
			}

			if (child is not null)
				yield return child;
		}
	}

	internal static IEnumerable<object> EnumerateLogicalChildren(DependencyObject dependencyObject)
	{
		IEnumerable? children;
		try
		{
			children = LogicalTreeHelper.GetChildren(dependencyObject);
		}
		catch (InvalidOperationException)
		{
			yield break;
		}

		if (children is null)
			yield break;

		foreach (var child in children)
			if (child is not null)
				yield return child;
	}

	internal static bool TryGetHybridBridgeChild(object target, out object? child)
	{
		child = null;
		var typeName = target.GetType().FullName;
		if (!string.Equals(typeName, "System.Windows.Forms.Integration.ElementHost", StringComparison.Ordinal)
			&& !string.Equals(typeName, "System.Windows.Forms.Integration.WindowsFormsHost", StringComparison.Ordinal))
		{
			return false;
		}

		var property = target.GetType().GetProperty("Child");
		if (property is null)
			return false;

		child = property.GetValue(target, null);
		return child is not null;
	}

	private static bool ShouldInclude(object target, TreeSnapshotOptions options)
	{
		if (options.IncludeHidden)
			return true;

		foreach (var adapter in TreeTargetAdapters)
			if (adapter.TryGetIsVisible(target, out var isVisible))
				return isVisible;

		return true;
	}

	private static bool ShouldExclude(object target)
	{
		if (target is SystemResourceRoot)
			return false;

		var type = target.GetType();
		var fullName = type.FullName ?? string.Empty;
		return fullName.StartsWith("DeepFlowTest.AppDriverPayload.", StringComparison.Ordinal)
			|| fullName.StartsWith("DeepFlowTest.Utility.", StringComparison.Ordinal);
	}

	private static string DetermineFrameworkFamily(IReadOnlyList<VisualTreeNodeDto> nodes)
	{
		var families = nodes
			.Select(static node => node.FrameworkTypeName)
			.Where(static typeName => string.IsNullOrWhiteSpace(typeName) == false)
			.Select(InferFamily)
			.Where(static family => family != "unknown")
			.Distinct(StringComparer.Ordinal)
			.ToArray();

		return families.Length switch
		{
			0 => string.Empty,
			1 => families[0],
			_ => "mixed",
		};
	}

	private static string InferFamily(string? typeName)
	{
		if (typeName is null)
			return string.Empty;

		if (typeName.StartsWith("System.Windows.Forms.", StringComparison.Ordinal))
			return "winforms";

		if (typeName == "HWND" || typeName.StartsWith("System.Windows.Automation.", StringComparison.Ordinal))
			return "native";

		if (typeName.StartsWith("System.Windows.", StringComparison.Ordinal)
			|| typeName.StartsWith("Microsoft.Windows.", StringComparison.Ordinal)
			|| typeName.StartsWith("MS.Internal.", StringComparison.Ordinal))
			return "wpf";

		return "unknown";
	}

	internal static IEnumerable<IntPtr> EnumerateNativeChildWindows(IntPtr hwnd)
	{
		if (hwnd == IntPtr.Zero)
			yield break;

		var children = new List<IntPtr>();
		NativeMethods.EnumChildWindows(hwnd, (child, _) =>
		{
			children.Add(child);
			return true;
		}, IntPtr.Zero);

		foreach (var child in children)
			yield return child;
	}

	private static IEnumerable<IntPtr> EnumerateProcessTopLevelWindows()
	{
		var processId = Process.GetCurrentProcess().Id;
		var windows = new List<IntPtr>();
		NativeMethods.EnumWindows((hwnd, _) =>
		{
			NativeMethods.GetWindowThreadProcessId(hwnd, out var windowProcessId);
			if (windowProcessId == processId)
				windows.Add(hwnd);

			return true;
		}, IntPtr.Zero);

		foreach (var window in windows)
			yield return window;
	}

	internal static AutomationElement? TryGetAutomationElement(IntPtr hwnd)
	{
		if (hwnd == IntPtr.Zero)
			return null;

		try
		{
			return AutomationElement.FromHandle(hwnd);
		}
		catch (ElementNotAvailableException)
		{
			return null;
		}
		catch (InvalidOperationException)
		{
			return null;
		}
	}

	internal static IEnumerable<AutomationElement> EnumerateAutomationChildren(AutomationElement element)
	{
		AutomationElement? child;
		try
		{
			child = TreeWalker.ControlViewWalker.GetFirstChild(element);
		}
		catch (ElementNotAvailableException)
		{
			yield break;
		}
		catch (InvalidOperationException)
		{
			yield break;
		}

		while (child is not null)
		{
			yield return child;
			try
			{
				child = TreeWalker.ControlViewWalker.GetNextSibling(child);
			}
			catch (ElementNotAvailableException)
			{
				yield break;
			}
			catch (InvalidOperationException)
			{
				yield break;
			}
		}
	}

	private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
	{
		public static readonly ReferenceEqualityComparer Instance = new();

		public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

		public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
	}
}
