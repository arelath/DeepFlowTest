namespace DeepFlowTest.Assert.TestFrameworks;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

internal sealed class TestFrameworkArtifactSink : IDiagnosticsArtifactSink
{
	private readonly Func<object?> getXUnitContext;
	private DiagnosticsTestContext? explicitFailureContext;

	public TestFrameworkArtifactSink()
		: this(ResolveCurrentXUnitContext)
	{
	}

	internal TestFrameworkArtifactSink(Func<object?> getXUnitContext)
	{
		this.getXUnitContext = getXUnitContext ?? throw new ArgumentNullException(nameof(getXUnitContext));
	}

	public DiagnosticsTestContext GetCurrentTestContext()
	{
		return explicitFailureContext
			?? TryGetXUnitContext()
			?? TryGetNUnitContext()
			?? new DiagnosticsTestContext
			{
				ResultsDirectory = ResolveFallbackResultsDirectory(),
				TestName = ResolveFallbackTestName(),
			};
	}

	internal void SetExplicitFailureContext(string? testName)
	{
		if (explicitFailureContext is not null)
			return;

		var current = TryGetXUnitContext()
			?? TryGetNUnitContext()
			?? new DiagnosticsTestContext
			{
				ResultsDirectory = ResolveFallbackResultsDirectory(),
				TestName = ResolveFallbackTestName(),
			};
		explicitFailureContext = new DiagnosticsTestContext
		{
			ResultsDirectory = current.ResultsDirectory,
			TestName = string.IsNullOrWhiteSpace(testName) ? current.TestName : testName!.Trim(),
			HasFailed = true,
		};
	}

	public void AttachArtifact(string path, string description)
	{
		if (TryAttachXUnitArtifact(path))
			return;

		var testContextType = FindLoadedType("nunit.framework", "NUnit.Framework.TestContext");
		var attach = testContextType?.GetMethod(
			"AddTestAttachment",
			BindingFlags.Public | BindingFlags.Static,
			binder: null,
			types: [typeof(string), typeof(string)],
			modifiers: null);
		if (attach is not null)
		{
			attach.Invoke(null, [path, description]);
			return;
		}

		Trace.WriteLine($"DeepFlowTest artifact: {path} ({description})");
	}

	public void Log(AppDriverDiagnostic diagnostic)
	{
		var message = $"DeepFlowTest diagnostics [{diagnostic.Severity}] {diagnostic.Code}: {diagnostic.Message}";
		if (!TrySendXUnitDiagnostic(message))
			Trace.WriteLine(message);
	}

	private DiagnosticsTestContext? TryGetXUnitContext()
	{
		try
		{
			var current = getXUnitContext();
			var test = GetProperty(current, "Test");
			if (test is null)
				return null;

			var state = GetProperty(current, "TestState");
			var result = GetProperty(state, "Result")?.ToString();
			return new DiagnosticsTestContext
			{
				ResultsDirectory = ResolveFallbackResultsDirectory(),
				TestName = GetStringProperty(test, "TestDisplayName")
					?? GetStringProperty(test, "DisplayName")
					?? ResolveFallbackTestName(),
				HasFailed = string.Equals(result, "Failed", StringComparison.OrdinalIgnoreCase),
			};
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			return null;
		}
	}

	private bool TryAttachXUnitArtifact(string path)
	{
		try
		{
			var current = getXUnitContext();
			if (GetProperty(current, "Test") is null)
				return false;

			var attach = current?.GetType().GetMethod(
				"AddAttachment",
				BindingFlags.Public | BindingFlags.Instance,
				binder: null,
				types: [typeof(string), typeof(byte[]), typeof(string)],
				modifiers: null);
			if (attach is null)
				return false;

			attach.Invoke(current, [Path.GetFileName(path), File.ReadAllBytes(path), ResolveMediaType(path)]);
			return true;
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			return false;
		}
	}

	private bool TrySendXUnitDiagnostic(string message)
	{
		try
		{
			var current = getXUnitContext();
			if (GetProperty(current, "Test") is null)
				return false;

			var send = current?.GetType().GetMethod(
				"SendDiagnosticMessage",
				BindingFlags.Public | BindingFlags.Instance,
				binder: null,
				types: [typeof(string)],
				modifiers: null);
			if (send is null)
				return false;

			send.Invoke(current, [message]);
			return true;
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			return false;
		}
	}

	private static DiagnosticsTestContext? TryGetNUnitContext()
	{
		try
		{
			var testContextType = FindLoadedType("nunit.framework", "NUnit.Framework.TestContext");
			var current = testContextType?.GetProperty("CurrentContext", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
			if (current is null)
				return null;

			var workDirectory = GetStringProperty(current, "WorkDirectory");
			var test = current.GetType().GetProperty("Test")?.GetValue(current);
			var result = current.GetType().GetProperty("Result")?.GetValue(current);
			var outcome = result?.GetType().GetProperty("Outcome")?.GetValue(result);
			var status = outcome?.GetType().GetProperty("Status")?.GetValue(outcome)?.ToString();
			return new DiagnosticsTestContext
			{
				ResultsDirectory = string.IsNullOrWhiteSpace(workDirectory) ? ResolveFallbackResultsDirectory() : workDirectory!,
				TestName = GetStringProperty(test, "FullName") ?? GetStringProperty(test, "Name") ?? ResolveFallbackTestName(),
				HasFailed = string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase)
					|| string.Equals(status, "Error", StringComparison.OrdinalIgnoreCase),
			};
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			return null;
		}
	}

	private static Type? FindLoadedType(string assemblyName, string typeName) =>
		AppDomain.CurrentDomain.GetAssemblies()
			.FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))
			?.GetType(typeName);

	private static string? GetStringProperty(object? instance, string propertyName) =>
		GetProperty(instance, propertyName)?.ToString();

	private static object? GetProperty(object? instance, string propertyName) =>
		instance?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance);

	private static object? ResolveCurrentXUnitContext()
	{
		try
		{
			var testContextType = AppDomain.CurrentDomain.GetAssemblies()
				.Select(static assembly => assembly.GetType("Xunit.TestContext"))
				.FirstOrDefault(static type => type is not null);
			return testContextType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			return null;
		}
	}

	private static string ResolveMediaType(string path) =>
		Path.GetExtension(path).ToLowerInvariant() switch
		{
			".png" => "image/png",
			".jpg" or ".jpeg" => "image/jpeg",
			".json" => "application/json",
			".txt" or ".log" => "text/plain",
			_ => "application/octet-stream",
		};

	private static string ResolveFallbackResultsDirectory()
	{
		foreach (var variable in new[] { "VSTEST_RESULTS_DIRECTORY", "TEST_RESULTS_DIRECTORY", "NUNIT_WORK_DIRECTORY" })
		{
			var value = Environment.GetEnvironmentVariable(variable);
			if (!string.IsNullOrWhiteSpace(value))
				return Path.GetFullPath(value);
		}

		return Path.Combine(Path.GetTempPath(), "DeepFlowTest", "test-results");
	}

	private static string ResolveFallbackTestName() =>
		Environment.GetEnvironmentVariable("TEST_NAME")
		?? $"{Process.GetCurrentProcess().ProcessName}-{Process.GetCurrentProcess().Id}";
}
