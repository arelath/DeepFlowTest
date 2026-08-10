namespace DeepFlowTest.AppDriverPayload.Patching;

using System;

public sealed class OptionalWpfPatch
{
	public OptionalWpfPatch(string name, Func<bool> isAvailable, Action apply)
	{
		Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Patch name is required.", nameof(name)) : name;
		IsAvailable = isAvailable ?? throw new ArgumentNullException(nameof(isAvailable));
		Apply = apply ?? throw new ArgumentNullException(nameof(apply));
	}

	public string Name { get; }

	public Func<bool> IsAvailable { get; }

	public Action Apply { get; }
}
