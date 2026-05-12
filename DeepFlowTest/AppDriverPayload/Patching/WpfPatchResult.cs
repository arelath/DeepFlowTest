namespace DeepFlowTest.AppDriverPayload.Patching;

using System;
using System.Collections.Generic;
using System.Linq;

public sealed class WpfPatchResult
{
	public string FrameworkFamily { get; set; } = string.Empty;

	public List<string> AppliedPatchNames { get; } = new();

	public List<string> SkippedPatchNames { get; } = new();

	public List<WpfPatchFailure> FailedPatches { get; } = new();

	public bool HasFailures => FailedPatches.Count != 0;

	public string Summary =>
		$"Framework={FrameworkFamily}; Applied={AppliedPatchNames.Count}; Skipped={SkippedPatchNames.Count}; Failed={FailedPatches.Count}";

	public void AddApplied(string patchName) => AppliedPatchNames.Add(patchName);

	public void AddSkipped(string patchName) => SkippedPatchNames.Add(patchName);

	public void AddFailed(string patchName, Exception exception) => FailedPatches.Add(new WpfPatchFailure(patchName, exception.GetType().FullName ?? exception.GetType().Name, exception.Message));

	public IReadOnlyList<string> FailedPatchNames => FailedPatches.Select(static failure => failure.PatchName).ToArray();
}

public sealed class WpfPatchFailure
{
	public WpfPatchFailure(string patchName, string exceptionType, string message)
	{
		PatchName = patchName;
		ExceptionType = exceptionType;
		Message = message;
	}

	public string PatchName { get; }

	public string ExceptionType { get; }

	public string Message { get; }
}
