namespace DeepFlowTest.Utility.WpfUtility.Tree;

using System;

public sealed class PropertyExtractionError
{
	[Newtonsoft.Json.JsonConstructor]
	public PropertyExtractionError(string propertyName, string errorCode, string message)
	{
		PropertyName = propertyName;
		ErrorCode = errorCode;
		Message = message;
	}

	public string PropertyName { get; }

	public string ErrorCode { get; }

	public string Message { get; }

	public static PropertyExtractionError Missing(string propertyName) =>
		new(propertyName, "missing-property", $"Property '{propertyName}' was not found.");

	public static PropertyExtractionError Failed(string propertyName, Exception exception) =>
		new(propertyName, "property-read-failed", exception.Message);
}
