namespace DeepFlowTest.Assert.TestFrameworks;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

internal sealed class TestFrameworkArtifactSink : IDiagnosticsArtifactSink
{
	public DiagnosticsTestContext GetCurrentTestContext()
	{
		return TryGetNUnitContext()
			?? new DiagnosticsTestContext
			{
				ResultsDirectory = ResolveFallbackResultsDirectory(),
				TestName = ResolveFallbackTestName(),
			};
	}

	public void AttachArtifact(string path, string description)
	{
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

	public void Log(AppDriverDiagnostic diagnostic) =>
		Trace.WriteLine($"DeepFlowTest diagnostics [{diagnostic.Severity}] {diagnostic.Code}: {diagnostic.Message}");

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
		instance?.GetType().GetProperty(propertyName)?.GetValue(instance)?.ToString();

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
