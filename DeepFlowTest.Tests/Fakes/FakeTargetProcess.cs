namespace DeepFlowTest.Tests.Fakes;

internal sealed class FakeTargetProcess : ITargetProcess
{
	public int Id { get; set; } = 123;

	public string ProcessName { get; set; } = "target";

	public bool HasExited { get; set; }

	public int? ExitCode
	{
		get => HasExited ? exitCode ?? 0 : null;
		set => exitCode = value;
	}

	public int KillCount { get; private set; }

	public int DisposeCount { get; private set; }

	private int? exitCode;

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
