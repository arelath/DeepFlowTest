namespace DeepFlowTest.Tests;

using System.Diagnostics;
using System.Linq;
using DeepFlowTest.AppDriverPayload.Diagnostics;
using DeepFlowTest.Contracts;
using NUnit.Framework;

[TestFixture]
public sealed class BindingFailureCaptureServiceTests
{
	[TearDown]
	public void TearDown()
	{
		BindingFailureCaptureService.Instance.ResetForTests();
	}

	[Test]
	public void ReadSinceNullReturnsCursorWithoutFailures()
	{
		using var _ = BindingFailureCaptureService.Instance.Start(new BindingFailureCaptureSettings());
		BindingFailureCaptureService.Instance.Record(BindingFailureSeverity.Error, "System.Windows.Data Error: one");

		var batch = BindingFailureCaptureService.Instance.ReadSince(afterSequenceNumber: null, maxCount: 10);

		Assert.That(batch.LastSequenceNumber, Is.EqualTo(1));
		Assert.That(batch.Failures, Is.Empty);
	}

	[Test]
	public void BoundedBufferReportsDroppedFailures()
	{
		using var _ = BindingFailureCaptureService.Instance.Start(new BindingFailureCaptureSettings { MaxStoredFailures = 2 });
		BindingFailureCaptureService.Instance.Record(BindingFailureSeverity.Error, "System.Windows.Data Error: one");
		BindingFailureCaptureService.Instance.Record(BindingFailureSeverity.Error, "System.Windows.Data Error: two");
		BindingFailureCaptureService.Instance.Record(BindingFailureSeverity.Error, "System.Windows.Data Error: three");

		var batch = BindingFailureCaptureService.Instance.ReadSince(afterSequenceNumber: 0, maxCount: 10);

		Assert.That(batch.DroppedCount, Is.EqualTo(1));
		Assert.That(batch.Failures.Select(static failure => failure.Message), Is.EqualTo(new[]
		{
			"System.Windows.Data Error: two",
			"System.Windows.Data Error: three",
		}));
		Assert.That(batch.LastSequenceNumber, Is.EqualTo(3));
	}

	[Test]
	public void TraceListenerCapturesWriteLineAndTraceEvent()
	{
		var listener = new BindingFailureTraceListener(BindingFailureCaptureService.Instance);

		listener.WriteLine("System.Windows.Data Error: write line");
		listener.TraceEvent(new TraceEventCache(), "System.Windows.Data", TraceEventType.Error, 40, "System.Windows.Data Error: trace event");

		var batch = BindingFailureCaptureService.Instance.ReadSince(afterSequenceNumber: 0, maxCount: 10);

		Assert.That(batch.Failures.Select(static failure => failure.Message), Does.Contain("System.Windows.Data Error: write line"));
		Assert.That(batch.Failures.Select(static failure => failure.EventId), Does.Contain(40));
		Assert.That(batch.Failures.Last().Severity, Is.EqualTo(BindingFailureSeverity.Error));
	}

	[Test]
	public void CaptureRegistrationsReferenceCountTraceListener()
	{
		var first = BindingFailureCaptureService.Instance.Start(new BindingFailureCaptureSettings());
		var second = BindingFailureCaptureService.Instance.Start(new BindingFailureCaptureSettings());

		Assert.That(BindingFailureCaptureService.Instance.ActiveRegistrationCount, Is.EqualTo(2));

		first.Dispose();
		Assert.That(BindingFailureCaptureService.Instance.ActiveRegistrationCount, Is.EqualTo(1));

		second.Dispose();
		Assert.That(BindingFailureCaptureService.Instance.ActiveRegistrationCount, Is.EqualTo(0));
	}
}
