namespace DeepFlowTest.Interop;

using System;
using Newtonsoft.Json.Serialization;

internal sealed class CrossRuntimeSerializationBinder : ISerializationBinder
{
	public static CrossRuntimeSerializationBinder Instance { get; } = new();

	private static ISerializationBinder DefaultBinder { get; } = new DefaultSerializationBinder();

	private static string CurrentCoreLibraryName { get; } = typeof(object).Assembly.GetName().Name!;

	public void BindToName(Type serializedType, out string? assemblyName, out string? typeName)
	{
		DefaultBinder.BindToName(serializedType, out assemblyName, out typeName);
	}

	public Type BindToType(string? assemblyName, string typeName)
	{
		return DefaultBinder.BindToType(NormalizeCoreLibraryName(assemblyName), NormalizeCoreLibraryName(typeName)!);
	}

	private static string? NormalizeCoreLibraryName(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return value;

		return value!
			.Replace("System.Private.CoreLib", CurrentCoreLibraryName)
			.Replace("mscorlib", CurrentCoreLibraryName);
	}
}
