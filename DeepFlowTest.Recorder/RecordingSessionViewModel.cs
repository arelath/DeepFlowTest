namespace DeepFlowTest.Recorder;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using DeepFlowTest.Contracts;

public sealed class RecordingSessionViewModel : ObservableObject
{
	private readonly List<SemanticRecordingFrame> sourceFrames = [];
	private readonly HashSet<string> expandedTargetIds = new(StringComparer.Ordinal);
	private SemanticRecordingTreeProjector liveProjector = CreateProjector();
	private RecordingFrameViewModel? selectedFrame;
	private SemanticTreeNodeViewModel? selectedTreeNode;
	private string? selectedTreeNodeTargetId;
	private bool isFollowingLatest = true;
	private bool hasInitializedTreeExpansion;
	private string? projectionErrorDetails;

	public ObservableCollection<RecordingFrameViewModel> Frames { get; } = [];

	public ObservableCollection<SemanticTreeNodeViewModel> CurrentRoots { get; } = [];

	public RecordingFrameViewModel? SelectedFrame
	{
		get => selectedFrame;
		private set
		{
			if (SetProperty(ref selectedFrame, value))
				NotifyNavigationProperties();
		}
	}

	public SemanticTreeNodeViewModel? SelectedTreeNode
	{
		get => selectedTreeNode;
		set
		{
			if (value is not null && !string.IsNullOrWhiteSpace(value.TargetId))
				selectedTreeNodeTargetId = value.TargetId;

			if (SetProperty(ref selectedTreeNode, value)
				&& value is not null
				&& !value.IsSelected)
			{
				value.IsSelected = true;
			}
		}
	}

	public bool IsFollowingLatest
	{
		get => isFollowingLatest;
		private set
		{
			if (SetProperty(ref isFollowingLatest, value))
				OnPropertyChanged(nameof(ModeText));
		}
	}

	public string? ProjectionErrorDetails
	{
		get => projectionErrorDetails;
		private set => SetProperty(ref projectionErrorDetails, value);
	}

	public string ModeText => IsFollowingLatest ? "Live" : "Reviewing";

	public string FramePosition => SelectedFrame is null
		? $"Frame 0/{Frames.Count}"
		: $"Frame {SelectedFrame.FrameNumber}/{Frames.Count}";

	public bool CanMovePrevious => SelectedFrame is not null && SelectedFrame.FrameNumber > 1;

	public bool CanMoveNext => SelectedFrame is not null && SelectedFrame.FrameNumber < Frames.Count;

	public bool CanJumpToLatest => Frames.Count != 0 && !IsFollowingLatest;

	public void Reset()
	{
		sourceFrames.Clear();
		expandedTargetIds.Clear();
		Frames.Clear();
		CurrentRoots.Clear();
		liveProjector = CreateProjector();
		selectedTreeNodeTargetId = null;
		SelectedFrame = null;
		SelectedTreeNode = null;
		IsFollowingLatest = true;
		hasInitializedTreeExpansion = false;
		ProjectionErrorDetails = null;
		NotifyNavigationProperties();
	}

	public void ReceiveBatch(SemanticRecordingBatch batch)
	{
		if (batch is null)
			return;

		foreach (var frame in batch.Frames ?? [])
			AppendFrame(frame);
	}

	public void SelectFrame(RecordingFrameViewModel? frame)
	{
		if (frame is null)
			return;

		IsFollowingLatest = Frames.Count != 0 && ReferenceEquals(frame, Frames[^1]);
		var projected = TryProjectThrough(frame.SourceFrameIndex, out var errorDetails);
		ProjectionErrorDetails = errorDetails;
		SelectedFrame = frame;
		ApplyTree(projected);
		NotifyNavigationProperties();
	}

	public void SelectPrevious()
	{
		if (!CanMovePrevious || SelectedFrame is null)
			return;

		SelectFrame(Frames[SelectedFrame.FrameNumber - 2]);
	}

	public void SelectNext()
	{
		if (!CanMoveNext || SelectedFrame is null)
			return;

		SelectFrame(Frames[SelectedFrame.FrameNumber]);
	}

	public void JumpToLatest()
	{
		if (Frames.Count == 0)
			return;

		IsFollowingLatest = true;
		SelectFrame(Frames[^1]);
	}

	private void AppendFrame(SemanticRecordingFrame frame)
	{
		sourceFrames.Add(frame);
		var sourceFrameIndex = sourceFrames.Count - 1;

		SemanticRecordingTreeFrame? projected = null;
		string? errorDetails = null;
		try
		{
			projected = liveProjector.Apply(frame);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			errorDetails = ex.ToString();
			liveProjector = TryRebuildLiveProjector();
		}

		var summary = projected?.Summary ?? "projection error";
		if (ShouldOmitVisibleFrame(projected, errorDetails))
		{
			ProjectionErrorDetails = errorDetails;
			NotifyNavigationProperties();
			return;
		}

		var frameVm = new RecordingFrameViewModel(Frames.Count + 1, sourceFrameIndex, frame, summary, errorDetails);
		Frames.Add(frameVm);
		ProjectionErrorDetails = errorDetails;

		if (IsFollowingLatest)
		{
			SelectedFrame = frameVm;
			ApplyTree(projected);
		}

		NotifyNavigationProperties();
	}

	private static bool ShouldOmitVisibleFrame(SemanticRecordingTreeFrame? projected, string? errorDetails) =>
		string.IsNullOrWhiteSpace(errorDetails)
		&& projected is not null
		&& string.Equals(projected.FrameKind, "delta", StringComparison.Ordinal)
		&& projected.Markers.Count == 0;

	private SemanticRecordingTreeFrame? TryProjectThrough(int selectedIndex, out string? errorDetails)
	{
		errorDetails = null;
		var projector = CreateProjector();
		SemanticRecordingTreeFrame? projected = null;
		for (var i = 0; i <= selectedIndex && i < sourceFrames.Count; i++)
		{
			try
			{
				projected = projector.Apply(sourceFrames[i]);
			}
			catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
			{
				errorDetails = ex.ToString();
				return null;
			}
		}

		return projected;
	}

	private SemanticRecordingTreeProjector TryRebuildLiveProjector()
	{
		var projector = CreateProjector();
		foreach (var frame in sourceFrames)
		{
			try
			{
				projector.Apply(frame);
			}
			catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
			{
				break;
			}
		}

		return projector;
	}

	private void ApplyTree(SemanticRecordingTreeFrame? frame)
	{
		CurrentRoots.Clear();
		if (frame is null)
			return;

		if (!hasInitializedTreeExpansion && frame.Roots.Count != 0)
		{
			foreach (var root in frame.Roots)
				if (!string.IsNullOrWhiteSpace(root.TargetId))
					expandedTargetIds.Add(root.TargetId);
			hasInitializedTreeExpansion = true;
		}

		foreach (var root in frame.Roots.Select(node => new SemanticTreeNodeViewModel(node, IsExpanded, SetExpanded, IsSelected, SetSelected)))
			CurrentRoots.Add(root);

		SelectedTreeNode = string.IsNullOrWhiteSpace(selectedTreeNodeTargetId)
			? null
			: FindNode(CurrentRoots, selectedTreeNodeTargetId!);
	}

	private bool IsExpanded(string targetId) =>
		!string.IsNullOrWhiteSpace(targetId) && expandedTargetIds.Contains(targetId);

	private void SetExpanded(string targetId, bool isExpanded)
	{
		if (string.IsNullOrWhiteSpace(targetId))
			return;

		if (isExpanded)
			expandedTargetIds.Add(targetId);
		else
			expandedTargetIds.Remove(targetId);
	}

	private bool IsSelected(string targetId) =>
		!string.IsNullOrWhiteSpace(targetId)
		&& string.Equals(targetId, selectedTreeNodeTargetId, StringComparison.Ordinal);

	private void SetSelected(string targetId, bool isSelected)
	{
		if (!isSelected || string.IsNullOrWhiteSpace(targetId))
			return;

		selectedTreeNodeTargetId = targetId;
		var node = FindNode(CurrentRoots, targetId);
		if (node is not null && !ReferenceEquals(selectedTreeNode, node))
			SetProperty(ref selectedTreeNode, node, nameof(SelectedTreeNode));
	}

	private static SemanticTreeNodeViewModel? FindNode(
		IEnumerable<SemanticTreeNodeViewModel> nodes,
		string targetId)
	{
		foreach (var node in nodes)
		{
			if (string.Equals(node.TargetId, targetId, StringComparison.Ordinal))
				return node;

			var child = FindNode(node.Children, targetId);
			if (child is not null)
				return child;
		}

		return null;
	}

	private void NotifyNavigationProperties()
	{
		OnPropertyChanged(nameof(FramePosition));
		OnPropertyChanged(nameof(CanMovePrevious));
		OnPropertyChanged(nameof(CanMoveNext));
		OnPropertyChanged(nameof(CanJumpToLatest));
		OnPropertyChanged(nameof(ModeText));
	}

	private static SemanticRecordingTreeProjector CreateProjector() =>
		new(new SemanticRecordingFormattingOptions
		{
			PruneStructuralLayoutNodes = true,
		});
}
