namespace DeepFlowTest.InjectorLauncher;

using DeepFlowTest.Shared;

public static class Program
{
	public static int Main(string[] args)
	{
		var data = new InjectorData
		{
			StartupArgument = string.Join(" ", args),
		};

		return string.IsNullOrWhiteSpace(data.StartupArgument) ? 0 : 0;
	}
}
