namespace DeepFlowTest.Tests;

using System;
using System.Linq;
using System.Reflection;
using DeepFlowTest.AppDriverPayload.Commands;
using DeepFlowTest.AppDriverPayload.Commands.TargetAdapters;
using DeepFlowTest.Utility.WpfUtility.Tree;
using DeepFlowTest.Utility.WpfUtility.Tree.TreeAdapters;
using NUnit.Framework;

[TestFixture]
public sealed class AdapterRegistrationTests
{
	[Test]
	public void TargetActionCommandRegistersExtractedUiAdaptersInPrecedenceOrder()
	{
		var adapters = GetStaticArray<IUiTargetAdapter>(typeof(TargetActionCommand), "TargetAdapters");

		Assert.That(
			adapters.Select(static adapter => adapter.GetType()),
			Is.EqualTo(new[]
			{
				typeof(WpfTargetAdapter),
				typeof(WinFormsTargetAdapter),
				typeof(AutomationTargetAdapter),
				typeof(NativeHwndTargetAdapter),
				typeof(ReflectionTargetAdapter),
			}));
		Assert.That(
			typeof(TargetActionCommand)
				.GetNestedTypes(BindingFlags.NonPublic)
				.Where(static type => type.Name.Contains("Adapter")),
			Is.Empty);
	}

	[Test]
	public void TreeServiceRegistersExtractedTreeAdaptersInTraversalOrder()
	{
		var adapters = GetStaticArray<ITreeTargetAdapter>(typeof(TreeService), "TreeTargetAdapters");

		Assert.That(
			adapters.Select(static adapter => adapter.GetType()),
			Is.EqualTo(new[]
			{
				typeof(HybridBridgeTreeTargetAdapter),
				typeof(ApplicationTreeTargetAdapter),
				typeof(ResourceDictionaryTreeTargetAdapter),
				typeof(SystemResourceTreeTargetAdapter),
				typeof(FrameworkElementResourceTreeTargetAdapter),
				typeof(ImageTreeTargetAdapter),
				typeof(WpfTreeTargetAdapter),
				typeof(WinFormsTreeTargetAdapter),
				typeof(NativeHwndTreeTargetAdapter),
				typeof(AutomationTreeTargetAdapter),
			}));
		Assert.That(
			typeof(TreeService)
				.GetNestedTypes(BindingFlags.NonPublic)
				.Where(static type => type.Name.Contains("Adapter")),
			Is.Empty);
	}

	private static T[] GetStaticArray<T>(Type ownerType, string fieldName)
	{
		var field = ownerType.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
		Assert.That(field, Is.Not.Null, fieldName);

		var value = field!.GetValue(null);
		Assert.That(value, Is.TypeOf<T[]>());
		return (T[])value!;
	}
}
