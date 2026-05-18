namespace DeepFlowTest.Cli;

using System;
using System.Collections.Generic;
using System.Linq;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using Newtonsoft.Json;

public sealed class VisualTreeResponseReader
{
	public VisualTreeSnapshot Read(object response, IReadOnlyList<string>? requestedProperties = null)
	{
		if (response is null)
			throw new CliException(CliErrorCodes.ProtocolError, "Visual tree response was empty.");

		if (response is StandardIpcResponse standard && standard.Success == false)
			throw new CliException(ProtocolErrorMapper.Map(standard.ErrorCode), standard.Error ?? "Visual tree command failed.");

		if (response is VisualTreeSnapshot snapshot)
			return NormalizeSnapshot(snapshot, requestedProperties);

		if (response is IEnumerable<VisualTreeNodeDto> nodes)
			return NormalizeSnapshot(VisualTreeSnapshot.Create(0, nodes, requestedProperties), requestedProperties);

		try
		{
			var convertedSnapshot = MessagePacker.ConvertTo<VisualTreeSnapshot>(response);
			if (convertedSnapshot is VisualTreeSnapshot snapshotResponse && snapshotResponse.Nodes.Count != 0)
				return NormalizeSnapshot(snapshotResponse, requestedProperties);
		}
		catch (Exception ex) when (ex is ProtocolException or InvalidCastException or JsonException)
		{
		}

		try
		{
			var convertedNodes = MessagePacker.ConvertTo<List<VisualTreeNodeDto>>(response);
			return NormalizeSnapshot(VisualTreeSnapshot.Create(0, convertedNodes, requestedProperties), requestedProperties);
		}
		catch (Exception ex) when (ex is ProtocolException or InvalidCastException or JsonException)
		{
			throw new CliException(CliErrorCodes.ProtocolError, "Visual tree response was malformed.");
		}
	}

	private static VisualTreeSnapshot NormalizeSnapshot(VisualTreeSnapshot snapshot, IReadOnlyList<string>? requestedProperties)
	{
		var uniqueNodes = new List<VisualTreeNodeDto>(snapshot.Nodes.Count);
		var duplicateCheck = new HashSet<string>(StringComparer.Ordinal);
		foreach (var node in snapshot.Nodes)
		{
			if (string.IsNullOrWhiteSpace(node.TargetId))
				throw new CliException(CliErrorCodes.ProtocolError, "Visual tree response contained a blank target ID.");
			if (!duplicateCheck.Add(node.TargetId))
				continue;

			node.ChildIds ??= [];
			node.Properties ??= [];
			uniqueNodes.Add(node);
		}

		snapshot.Nodes = uniqueNodes;
		var validIds = uniqueNodes.Select(static node => node.TargetId).ToHashSet(StringComparer.Ordinal);
		foreach (var node in uniqueNodes)
		{
			if (!string.IsNullOrWhiteSpace(node.ParentId) && !validIds.Contains(node.ParentId!))
				node.ParentId = null;

			node.ChildIds = node.ChildIds
				.Where(childId => validIds.Contains(childId) && !string.Equals(childId, node.TargetId, StringComparison.Ordinal))
				.Distinct(StringComparer.Ordinal)
				.ToList();
		}

		var byParent = uniqueNodes
			.Where(static node => !string.IsNullOrWhiteSpace(node.ParentId))
			.GroupBy(static node => node.ParentId!, StringComparer.Ordinal)
			.ToDictionary(static group => group.Key, static group => group.Select(static node => node.TargetId).ToList(), StringComparer.Ordinal);
		foreach (var node in uniqueNodes)
		{
			if (node.ChildIds.Count == 0 && byParent.TryGetValue(node.TargetId, out var childIds))
				node.ChildIds = childIds;
		}

		snapshot.RootIds = snapshot.RootIds.Count == 0
			? uniqueNodes.Where(static node => node.IsRoot || string.IsNullOrWhiteSpace(node.ParentId)).Select(static node => node.TargetId).ToList()
			: snapshot.RootIds.Where(validIds.Contains).Distinct(StringComparer.Ordinal).ToList();
		snapshot.NodeCount = uniqueNodes.Count;
		if (requestedProperties is not null && snapshot.RequestedPropertyNames.Count == 0)
			snapshot.RequestedPropertyNames = requestedProperties.ToList();
		return snapshot;
	}

}
