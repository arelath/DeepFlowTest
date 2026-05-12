namespace DeepFlowTest.Cli.Tests;

using System;
using System.IO;
using NUnit.Framework;

[TestFixture]
public sealed class ProcessNameCacheTests
{
	[Test]
	public void FileProcessNameCacheStoresAndReadsPidByProcessName()
	{
		var path = CreateTempCachePath();
		try
		{
			var cache = new FileProcessNameCache(path);

			cache.Set("Sage.exe", 1234);

			Assert.That(new FileProcessNameCache(path).TryGet("sage"), Is.EqualTo(1234));
			Assert.That(new FileProcessNameCache(path).TryGet("SAGE.EXE"), Is.EqualTo(1234));
		}
		finally
		{
			DeleteTempCache(path);
		}
	}

	[Test]
	public void FileProcessNameCacheRemoveDeletesCachedPid()
	{
		var path = CreateTempCachePath();
		try
		{
			var cache = new FileProcessNameCache(path);
			cache.Set("Sage", 1234);

			cache.Remove("Sage.exe");

			Assert.That(new FileProcessNameCache(path).TryGet("Sage"), Is.Null);
		}
		finally
		{
			DeleteTempCache(path);
		}
	}

	[Test]
	public void FileProcessNameCacheIgnoresInvalidJsonAndCanReplaceIt()
	{
		var path = CreateTempCachePath();
		try
		{
			File.WriteAllText(path, "{");
			var cache = new FileProcessNameCache(path);

			Assert.That(cache.TryGet("Sage"), Is.Null);

			cache.Set("Sage", 1234);

			Assert.That(new FileProcessNameCache(path).TryGet("Sage"), Is.EqualTo(1234));
		}
		finally
		{
			DeleteTempCache(path);
		}
	}

	private static string CreateTempCachePath()
	{
		var directory = Path.Combine(Path.GetTempPath(), "DeepFlowTest.Cli.Tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		return Path.Combine(directory, FileProcessNameCache.FileName);
	}

	private static void DeleteTempCache(string path)
	{
		var directory = Path.GetDirectoryName(path);
		if (directory is not null && Directory.Exists(directory))
			Directory.Delete(directory, recursive: true);
	}
}
