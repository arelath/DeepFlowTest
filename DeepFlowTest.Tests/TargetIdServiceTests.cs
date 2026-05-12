namespace DeepFlowTest.Tests;

using System;
using DeepFlowTest.Contracts;
using DeepFlowTest.Utility;
using NUnit.Framework;

[TestFixture]
public sealed class TargetIdServiceTests
{
	[Test]
	public void SameObjectKeepsSameId()
	{
		var service = new TargetIdService();
		var target = new object();

		var first = service.GetOrCreateId(target);
		var second = service.GetOrCreateId(target);

		Assert.That(second, Is.EqualTo(first));
		Assert.That(service.TryGetTarget(first, out var resolved), Is.True);
		Assert.That(resolved, Is.SameAs(target));
	}

	[Test]
	public void DifferentObjectsReceiveDifferentIds()
	{
		var service = new TargetIdService();

		var first = service.GetOrCreateId(new object());
		var second = service.GetOrCreateId(new object());

		Assert.That(second, Is.Not.EqualTo(first));
	}

	[Test]
	public void NativeWindowHandlesResolveWithStableValueIds()
	{
		var service = new TargetIdService();
		var first = service.GetOrCreateId(new IntPtr(1234));
		var second = service.GetOrCreateId(new IntPtr(1234));

		var resolution = service.Resolve(first);

		Assert.That(second, Is.EqualTo(first));
		Assert.That(resolution.Status, Is.EqualTo(TargetIdResolutionStatus.Found));
		Assert.That(resolution.Target, Is.EqualTo(new IntPtr(1234)));
	}

	[Test]
	public void DeadObjectBecomesStaleWithoutBeingKeptAliveByIdService()
	{
		var service = new TargetIdService();
		var targetId = CreateCollectableTargetId(service, out var weakReference);

		ForceCollection(weakReference);
		var resolution = service.Resolve(targetId);

		Assert.That(weakReference.IsAlive, Is.False);
		Assert.That(resolution.Status, Is.EqualTo(TargetIdResolutionStatus.Stale));
		Assert.That(service.Resolve(targetId).Status, Is.EqualTo(TargetIdResolutionStatus.Stale));
		Assert.That(ProtocolConstants.ErrorCodes.StaleTarget, Is.EqualTo("stale-target"));
	}

	[Test]
	public void StaleTargetIsDistinctFromNoMatch()
	{
		var service = new TargetIdService();

		var missing = service.Resolve("dft-target-missing");

		Assert.That(missing.Status, Is.EqualTo(TargetIdResolutionStatus.NotFound));
	}

	private static string CreateCollectableTargetId(TargetIdService service, out WeakReference weakReference)
	{
		var target = new object();
		weakReference = new WeakReference(target);
		return service.GetOrCreateId(target);
	}

	private static void ForceCollection(WeakReference weakReference)
	{
		for (var i = 0; i < 10 && weakReference.IsAlive; i++)
		{
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();
		}
	}
}
