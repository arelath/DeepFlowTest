namespace DeepFlowTest.InjectorLauncher;

internal static class ConsoleHelper
{
	public static void AttachConsoleToParentProcessOrAllocateNewOne()
	{
		if (!NativeMethods.AttachConsole(NativeMethods.AttachParentProcess))
			_ = NativeMethods.AllocConsole();
	}
}
