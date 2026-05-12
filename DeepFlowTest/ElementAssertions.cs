namespace DeepFlowTest;

using System;
using System.Linq;
using DeepFlowTest.Assert;
using Newtonsoft.Json;

public static class ElementAssertions
{
	public static void ShouldHaveProperty(this Element element, string propertyName, object? expected)
	{
		_ = element ?? throw new ArgumentNullException(nameof(element));
		element.Properties.TryGetValue(propertyName, out var actual);
		if (Equals(actual, expected))
			return;

		throw new AppDriverAssertionException(
			$"Expected property '{propertyName}' to be '{expected}', but was '{actual}'. " +
			$"TargetId={element.TargetId}; Selector={element.Selector}; LastProperties={FormatProperties(element)}");
	}

	public static void ShouldBeVisible(this Element element)
	{
		_ = element ?? throw new ArgumentNullException(nameof(element));
		element.ShouldHaveProperty("IsVisible", true);
	}

	private static string FormatProperties(Element element)
	{
		var values = element.Properties
			.OrderBy(static property => property.Key, StringComparer.Ordinal)
			.ToDictionary(static property => property.Key, static property => property.Value, StringComparer.Ordinal);
		return JsonConvert.SerializeObject(values);
	}
}

public sealed class AppDriverAssertionException : AssertionFailedException
{
	public AppDriverAssertionException(string message)
		: base(message)
	{
	}
}
