namespace DeepFlowTest.AppDriverPayload.Commands;

using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Utility.WpfUtility.Tree;

internal static class GetVisualTreeCommand
{
	public static object Process(GetVisualTreeCommandRequest request, TreeService treeService)
	{
		_ = request ?? throw new System.ArgumentNullException(nameof(request));
		_ = treeService ?? throw new System.ArgumentNullException(nameof(treeService));

		try
		{
			var snapshot = treeService.CaptureSnapshot(new TreeSnapshotOptions
			{
				RequestedPropertyNames = request.PropNames,
				RootTargetId = request.RootTargetId,
				IncludeHidden = request.IncludeHidden,
				MaxDepth = request.MaxDepth,
				MaxNodeCount = request.MaxNodeCount ?? VisualTreeDefaults.DefaultMaxNodeCount,
			});

			if (snapshot.NodeCount == 0)
			{
				return StandardIpcResponse.FromError(
					"No supported UI roots were found.",
					ProtocolConstants.ErrorCodes.UnsupportedTarget,
					PayloadLog.CurrentCorrelationId);
			}

			return request.AsSnapshot ? snapshot : snapshot.Nodes;
		}
		catch (TreeSnapshotException ex)
		{
			return StandardIpcResponse.FromError(ex.Message, ex.ErrorCode, PayloadLog.CurrentCorrelationId);
		}
	}
}
