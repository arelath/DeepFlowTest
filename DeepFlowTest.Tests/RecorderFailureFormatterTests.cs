namespace DeepFlowTest.Tests;

using System;
using DeepFlowTest.Recorder;
using NUnit.Framework;

[TestFixture]
public sealed class RecorderFailureFormatterTests
{
	[Test]
	public void InjectionFailuresShowInnerSummaryAndStartupDiagnostics()
	{
		var exception = new AppConnectionException(
			"Target injection failed: Injector launcher exited with code 6.",
			new AppDriverException(
				AppDriverErrorCodes.InjectorFailed,
				$"Injector launcher exited with code 6.{Environment.NewLine}{Environment.NewLine}Injection diagnostics:{Environment.NewLine}injector tail"),
			"Payload log tail: payload crash");

		var failure = RecorderFailureFormatter.Format(exception);

		Assert.That(failure.Status, Is.EqualTo("Target injection failed: Injector launcher exited with code 6."));
		Assert.That(failure.Details, Does.Contain("Exception chain:"));
		Assert.That(failure.Details, Does.Contain(nameof(AppConnectionException)));
		Assert.That(failure.Details, Does.Contain("Injector launcher exited with code 6."));
		Assert.That(failure.Details, Does.Contain("Startup diagnostics:"));
		Assert.That(failure.Details, Does.Contain("Payload log tail: payload crash"));
	}

	[Test]
	public void LongStatusesAreTruncatedForTheStatusBar()
	{
		var exception = new InvalidOperationException(new string('a', 400));

		var failure = RecorderFailureFormatter.Format(exception);

		Assert.That(failure.Status.Length, Is.EqualTo(240));
		Assert.That(failure.Status, Does.EndWith("..."));
		Assert.That(failure.Details, Does.Contain(new string('a', 400)));
	}
}
