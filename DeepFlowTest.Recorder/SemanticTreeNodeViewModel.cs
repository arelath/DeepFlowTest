namespace DeepFlowTest.Recorder;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

public sealed class SemanticTreeNodeViewModel : ObservableObject
{
	private readonly Action<string, bool>? expansionChanged;
	private readonly Action<string, bool>? selectionChanged;
	private bool isExpanded;
	private bool isSelected;

	public SemanticTreeNodeViewModel(SemanticRecordingTreeNode node)
		: this(
			node,
			static _ => false,
			expansionChanged: null,
			static _ => false,
			selectionChanged: null)
	{
	}

	internal SemanticTreeNodeViewModel(
		SemanticRecordingTreeNode node,
		Func<string, bool> isExpandedLookup,
		Action<string, bool>? expansionChanged,
		Func<string, bool> isSelectedLookup,
		Action<string, bool>? selectionChanged)
	{
		this.expansionChanged = expansionChanged;
		this.selectionChanged = selectionChanged;
		TargetId = node.TargetId;
		ShortId = node.ShortId;
		TypeName = node.TypeName;
		Label = node.Label;
		ChangeKind = node.ChangeKind;
		IsActionTarget = node.IsActionTarget;
		Style = SemanticTreeStyle.For(ChangeKind);
		isExpanded = isExpandedLookup(TargetId);
		isSelected = isSelectedLookup(TargetId);
		Properties = FormatProperties(node.Properties);
		ChangedProperties = FormatProperties(node.ChangedProperties);
		foreach (var child in node.Children)
			Children.Add(new SemanticTreeNodeViewModel(child, isExpandedLookup, expansionChanged, isSelectedLookup, selectionChanged));
	}

	public string TargetId { get; }

	public string ShortId { get; }

	public string TypeName { get; }

	public string Label { get; }

	public SemanticRecordingChangeKind ChangeKind { get; }

	public bool IsActionTarget { get; }

	public SemanticTreeStyle Style { get; }

	public IReadOnlyList<string> Properties { get; }

	public IReadOnlyList<string> ChangedProperties { get; }

	public ObservableCollection<SemanticTreeNodeViewModel> Children { get; } = [];

	public string Marker => Style.Marker;

	public bool IsExpanded
	{
		get => isExpanded;
		set
		{
			if (SetProperty(ref isExpanded, value))
				expansionChanged?.Invoke(TargetId, value);
		}
	}

	public bool IsSelected
	{
		get => isSelected;
		set
		{
			if (SetProperty(ref isSelected, value))
				selectionChanged?.Invoke(TargetId, value);
		}
	}

	public bool HasChangedProperties => ChangedProperties.Count != 0;

	public bool HasProperties => Properties.Count != 0;

	private static IReadOnlyList<string> FormatProperties(IReadOnlyDictionary<string, object?> properties) =>
		properties
			.OrderBy(static item => item.Key, System.StringComparer.Ordinal)
			.Select(static item => $"{item.Key}={FormatValue(item.Value)}")
			.ToArray();

	private static string FormatValue(object? value) =>
		value switch
		{
			null => "null",
			string text => text,
			System.IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
			_ => value.ToString() ?? string.Empty,
		};
}
