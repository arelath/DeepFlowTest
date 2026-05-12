namespace DeepFlowTest.AppDriverPayload.Patching;

using System;
using System.Collections.Generic;
using System.Reflection;
using DeepFlowTest.AppDriverPayload;

public sealed class NetFrameworkWpfPatcher : WpfPatcherBase
{
	public NetFrameworkWpfPatcher()
		: base(RuntimeFrameworkFamilies.NetFramework, DefaultWpfPatchCatalog.CreateOptionalPatches(RuntimeFrameworkFamilies.NetFramework))
	{
	}
}

public sealed class NetCoreWpfPatcher : WpfPatcherBase
{
	public NetCoreWpfPatcher()
		: base(RuntimeFrameworkFamilies.NetCore, DefaultWpfPatchCatalog.CreateOptionalPatches(RuntimeFrameworkFamilies.NetCore))
	{
	}
}

public sealed class ModernNetWpfPatcher : WpfPatcherBase
{
	public ModernNetWpfPatcher()
		: base(RuntimeFrameworkFamilies.ModernNet, DefaultWpfPatchCatalog.CreateOptionalPatches(RuntimeFrameworkFamilies.ModernNet))
	{
	}
}

public static class RuntimeFrameworkFamilies
{
	public const string NetFramework = "net-framework";
	public const string NetCore = "net-core";
	public const string ModernNet = "modern-net";
	public const string Unknown = "unknown";
}

internal static class DefaultWpfPatchCatalog
{
	public static IEnumerable<OptionalWpfPatch> CreateOptionalPatches(string frameworkFamily)
	{
		yield return OptionalPrivateMemberPatch("MouseButtonState", "System.Windows.Input.MouseDevice", "_mouseButtonState");
		yield return OptionalPrivateMemberPatch("ButtonPressedState", "System.Windows.Controls.Primitives.ButtonBase", "SetIsPressed");
		yield return OptionalPrivateMemberPatch("WpfDialogOwner", "System.Windows.Window", "ShowDialog");
		yield return OptionalPrivateMemberPatch("WinFormsDialogOwner", "System.Windows.Forms.Form", "ShowDialog");

		if (frameworkFamily == RuntimeFrameworkFamilies.ModernNet)
			yield return OptionalPrivateMemberPatch("ModernMenuMode", "System.Windows.Controls.MenuItem", "IgnoreNextLeftRelease");
	}

	private static OptionalWpfPatch OptionalPrivateMemberPatch(string name, string typeName, string memberName)
	{
		return new OptionalWpfPatch(
			name,
			() => TryFindMember(typeName, memberName),
			AppHooks.EnsureHooked);
	}

	private static bool TryFindMember(string typeName, string memberName)
	{
		var type = Type.GetType(typeName + ", PresentationFramework", throwOnError: false)
			?? Type.GetType(typeName + ", WindowsBase", throwOnError: false)
			?? Type.GetType(typeName + ", System.Windows.Forms", throwOnError: false);
		if (type is null)
			return false;

		const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
		return type.GetField(memberName, flags) is not null
			|| type.GetProperty(memberName, flags) is not null
			|| type.GetMethod(memberName, flags) is not null;
	}
}
