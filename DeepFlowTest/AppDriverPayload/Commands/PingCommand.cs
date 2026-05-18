namespace DeepFlowTest.AppDriverPayload.Commands;

using System;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Utility;
using DeepFlowTest.Utility.WpfUtility.Tree;

internal static class PingCommand
{
	public static object Process(PingCommandRequest request, TreeService? treeService = null)
	{
		var availability = ThreadUtility.GetAvailability();
		var rootCount = availability.RootCount;
		var nodeCount = availability.RootCount;
		if (treeService is not null)
		{
			try
			{
				var snapshot = treeService.CaptureSnapshot(new TreeSnapshotOptions
				{
					RequestedPropertyNames = [],
					IncludeHidden = true,
					MaxNodeCount = VisualTreeDefaults.DefaultMaxNodeCount,
				});
				rootCount = snapshot.RootIds.Count;
				nodeCount = snapshot.Nodes.Count;
			}
			catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
			{
			}
		}

		return new PingCommandResponse
		{
			ProcessId = PayloadEnvironment.ProcessId,
			IsWpfAvailable = availability.IsWpfAvailable,
			IsWinFormsAvailable = availability.IsWinFormsAvailable,
			IsNativeFallbackAvailable = availability.IsNativeFallbackAvailable,
			IsDispatcherAvailable = availability.IsDispatcherAvailable,
			RootCount = rootCount,
			NodeCount = nodeCount,
		};
	}
}
