namespace DeepFlowTest.Assert.TestFrameworks;

using System;
using System.Collections.Generic;
using System.Linq;

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

	public static void Throw(string message)
	{
		testFramework ??= DetectFramework();
		testFramework.Throw(message);
	}

	private static ITestFramework DetectFramework() =>
		Frameworks.Values.FirstOrDefault(static framework => framework.IsAvailable) ?? new FallbackTestFramework();
}
