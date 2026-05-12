namespace DeepFlowTest.Interop;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

public sealed class VisualTreeSnapshot
{
	public long SequenceNumber { get; set; }

	public int ProcessId { get; set; } = Process.GetCurrentProcess().Id;

	public DateTimeOffset GeneratedUtc { get; set; } = DateTimeOffset.UtcNow;

	public List<string> RootIds { get; set; } = [];

	public List<VisualTreeNodeDto> Nodes { get; set; } = [];

	public int NodeCount { get; set; }

	public List<string> RequestedPropertyNames { get; set; } = [];

	public string TargetFrameworkFamily { get; set; } = string.Empty;

	public bool IsTruncated { get; set; }

	public string? TruncationReason { get; set; }

	public static VisualTreeSnapshot Create(
		long sequenceNumber,
		IEnumerable<VisualTreeNodeDto> nodes,
		IEnumerable<string>? requestedPropertyNames = null,
		string targetFrameworkFamily = "",
		bool isTruncated = false,
		string? truncationReason = null)
	{
		var nodeList = nodes.ToList();
		return new VisualTreeSnapshot
		{
			SequenceNumber = sequenceNumber,
			GeneratedUtc = DateTimeOffset.UtcNow,
			RootIds = nodeList.Where(static node => node.IsRoot || node.ParentId is null).Select(static node => node.TargetId).ToList(),
			Nodes = nodeList,
			NodeCount = nodeList.Count,
			RequestedPropertyNames = requestedPropertyNames?.ToList() ?? [],
			TargetFrameworkFamily = targetFrameworkFamily,
			IsTruncated = isTruncated,
			TruncationReason = truncationReason,
		};
	}
}
