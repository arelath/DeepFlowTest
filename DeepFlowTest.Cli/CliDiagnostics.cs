namespace DeepFlowTest.Cli;

public static class CliDiagnostics
{
	public static bool TryReadPayloadLogTail(string pipeName, int processId, out string tail, int maxCharacters = 4096)
	{
		return DeepFlowTest.AppDriverPayload.PayloadLog.TryReadTailForPipe(pipeName, processId, out tail, maxCharacters);
	}
}
