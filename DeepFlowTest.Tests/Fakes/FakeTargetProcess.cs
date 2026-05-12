namespace DeepFlowTest.Tests.Fakes;

internal sealed class FakeTargetProcess : ITargetProcess
{
	public int Id { get; set; } = 123;

	public string ProcessName { get; set; } = "target";

	public bool HasExited { get; set; }

	public int KillCount { get; private set; }

	public int DisposeCount { get; private set; }

	public void Kill()
	{
		KillCount++;
		HasExited = true;
	}

	public void Dispose()
	{
		DisposeCount++;
	}
}
