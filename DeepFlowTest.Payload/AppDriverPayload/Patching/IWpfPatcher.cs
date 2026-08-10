namespace DeepFlowTest.AppDriverPayload.Patching;

using System;

public interface IWpfPatcher
{
	string FrameworkFamily { get; }

	WpfPatchResult Apply(Action<string, Exception?>? log = null);
}
