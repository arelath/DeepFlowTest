namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using System.Linq;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

internal sealed class VisualTreeClient
{
	private readonly DriverCommandClient commandClient;
	private readonly ElementRegistry elementRegistry;
	private readonly ElementFactory elementFactory;

	public VisualTreeClient(
		DriverCommandClient commandClient,
		ElementRegistry elementRegistry,
		ElementFactory elementFactory)
	{
		this.commandClient = commandClient ?? throw new ArgumentNullException(nameof(commandClient));
		this.elementRegistry = elementRegistry ?? throw new ArgumentNullException(nameof(elementRegistry));
		this.elementFactory = elementFactory ?? throw new ArgumentNullException(nameof(elementFactory));
	}

	public VisualTreeSnapshot GetVisualTree(string? rootTargetId = null) =>
		GetVisualTree(rootTargetId, propNames: null);

	public VisualTreeSnapshot GetVisualTree(string? rootTargetId, IReadOnlyList<string>? propNames) =>
		GetVisualTree(rootTargetId, propNames, maxNodeCount: null);

	public VisualTreeSnapshot GetVisualTree(string? rootTargetId, IReadOnlyList<string>? propNames, int? maxNodeCount)
	{
		var snapshot = commandClient.Send<VisualTreeSnapshot>(new GetVisualTreeCommandRequest
		{
			AsSnapshot = true,
			RootTargetId = rootTargetId,
			PropNames = propNames,
			MaxNodeCount = maxNodeCount,
		});
		elementRegistry.Refresh(snapshot);
		return snapshot;
	}

	public VisualTreeSnapshot GetVisualTreeForRepair(ElementRepairInfo repairInfo)
	{
		_ = repairInfo ?? throw new ArgumentNullException(nameof(repairInfo));
		return GetVisualTree(
			rootTargetId: null,
			propNames: repairInfo.RequestedPropertyNames.Count == 0
				? null
				: repairInfo.RequestedPropertyNames.ToArray(),
			maxNodeCount: null);
	}

	public IReadOnlyList<Element> GetRootElements()
	{
		var snapshot = GetVisualTree();
		var byId = snapshot.Nodes.ToDictionary(static node => node.TargetId, StringComparer.Ordinal);
		return snapshot.RootIds
			.Where(byId.ContainsKey)
			.Select(rootId => elementFactory.FromNode(byId[rootId], snapshot))
			.ToArray();
	}
}
