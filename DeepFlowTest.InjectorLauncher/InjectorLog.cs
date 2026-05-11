namespace DeepFlowTest.InjectorLauncher;

using System;
using System.IO;

internal static class InjectorLog
{
	public static string DefaultLogDirectory =>
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeepFlowTest", "logs");

	public static string DefaultLogPath => Path.Combine(DefaultLogDirectory, "deepflowtest-injector.log");

	public static void Reset()
	{
		Directory.CreateDirectory(DefaultLogDirectory);
		if (File.Exists(DefaultLogPath))
			File.Delete(DefaultLogPath);
	}

	public static void Write(string message)
	{
		Directory.CreateDirectory(DefaultLogDirectory);
		File.AppendAllText(DefaultLogPath, $"{DateTimeOffset.Now:O}: {message}{Environment.NewLine}");
	}

	public static string CreateNativeLogPath(int processId)
	{
		Directory.CreateDirectory(DefaultLogDirectory);
		return Path.Combine(DefaultLogDirectory, $"deepflowtest-native-{processId}-{Guid.NewGuid():N}.log");
	}
}
