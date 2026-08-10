namespace DeepFlowTest.Assert.TestFrameworks;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

internal static class TestFrameworkProvider
{
	private static readonly Dictionary<string, ITestFramework> Frameworks = new(StringComparer.OrdinalIgnoreCase)
	{
		["mspec"] = new MSpecFramework(),
		["nspec3"] = new NSpecFramework(),
		["nunit"] = new NUnitTestFramework(),
		["mstestv2"] = new MSTestFrameworkV2(),
		["xunit2"] = new XUnit2TestFramework(),
	};

	private static ITestFramework? testFramework;
	private static readonly AsyncLocal<Action<string>?> CurrentAssertionFailure = new();

	internal static event Action<string>? AssertionFailure
	{
		add => CurrentAssertionFailure.Value += value;
		remove => CurrentAssertionFailure.Value -= value;
	}

	public static void Throw(string message)
	{
		foreach (var handler in CurrentAssertionFailure.Value?.GetInvocationList().Cast<Action<string>>() ?? [])
		{
			try
			{
				handler(message);
			}
			catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
			{
			}
		}
		testFramework ??= DetectFramework();
		testFramework.Throw(message);
	}

	private static ITestFramework DetectFramework() =>
		Frameworks.Values.FirstOrDefault(static framework => framework.IsAvailable) ?? new FallbackTestFramework();
}
