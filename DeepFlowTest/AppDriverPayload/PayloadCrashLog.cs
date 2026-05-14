namespace DeepFlowTest.AppDriverPayload;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using DeepFlowTest.Utility;
using WinForms = System.Windows.Forms;

public static class PayloadCrashLog
{
	private const int DefaultMaxCharacters = 8192;
	private const int DefaultWaitMs = 250;
	private static readonly object Sync = new();
	private static readonly List<CrashLogRegistration> Registrations = new();

	public static string GetLogPath(string pipeName) =>
		Path.Combine(PayloadLog.DefaultLogDirectory, $"{SanitizeFileName(pipeName)}-crash.txt");

	public static void Register(string pipeName)
	{
		if (string.IsNullOrWhiteSpace(pipeName))
			return;

		lock (Sync)
		{
			if (Registrations.Any(registration => string.Equals(registration.PipeName, pipeName, StringComparison.Ordinal)))
				return;

			var appDomainHandler = CreateAppDomainHandler(pipeName);
			AppDomain.CurrentDomain.UnhandledException += appDomainHandler;

			var winFormsHandler = CreateWinFormsHandler(pipeName);
			WinForms.Application.ThreadException += winFormsHandler;

			Application? application = null;
			DispatcherUnhandledExceptionEventHandler? dispatcherHandler = null;
			try
			{
				ThreadUtility.RunOnUIThread(() =>
				{
					if (Application.Current is null)
						return;

					application = Application.Current;
					dispatcherHandler = CreateDispatcherHandler(pipeName);
					application.DispatcherUnhandledException += dispatcherHandler;
				});
			}
			catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
			{
			}

			Registrations.Add(new CrashLogRegistration(
				pipeName,
				appDomainHandler,
				winFormsHandler,
				application,
				dispatcherHandler));
		}
	}

	public static void Write(string pipeName, Exception exception)
	{
		_ = exception ?? throw new ArgumentNullException(nameof(exception));
		WriteText(pipeName, exception.ToString());
	}

	public static bool TryRead(string pipeName, out string crashLog, int maxCharacters = DefaultMaxCharacters, int waitMs = DefaultWaitMs)
	{
		crashLog = string.Empty;
		if (string.IsNullOrWhiteSpace(pipeName))
			return false;

		var path = GetLogPath(pipeName);
		var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(0, waitMs));
		do
		{
			try
			{
				if (File.Exists(path))
				{
					crashLog = Tail(File.ReadAllText(path), maxCharacters);
					return !string.IsNullOrWhiteSpace(crashLog);
				}
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}

			if (waitMs <= 0 || DateTime.UtcNow >= deadline)
				break;

			Thread.Sleep(25);
		}
		while (true);

		return false;
	}

	public static void ResetForTests()
	{
		lock (Sync)
		{
			foreach (var registration in Registrations)
			{
				AppDomain.CurrentDomain.UnhandledException -= registration.AppDomainHandler;
				WinForms.Application.ThreadException -= registration.WinFormsHandler;
				if (registration.Application is not null && registration.DispatcherHandler is not null)
					registration.Application.DispatcherUnhandledException -= registration.DispatcherHandler;
			}

			Registrations.Clear();
		}
	}

	private static UnhandledExceptionEventHandler CreateAppDomainHandler(string pipeName) =>
		(_, args) =>
		{
			if (args.ExceptionObject is Exception exception)
			{
				Write(pipeName, exception);
				return;
			}

			WriteText(pipeName, $"Unhandled exception object: {args.ExceptionObject}");
		};

	private static DispatcherUnhandledExceptionEventHandler CreateDispatcherHandler(string pipeName) =>
		(_, args) => Write(pipeName, args.Exception);

	private static ThreadExceptionEventHandler CreateWinFormsHandler(string pipeName) =>
		(_, args) => Write(pipeName, args.Exception);

	private static void WriteText(string pipeName, string text)
	{
		try
		{
			var path = GetLogPath(pipeName);
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, text);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
		}
	}

	private static string Tail(string value, int maxCharacters)
	{
		if (string.IsNullOrEmpty(value) || value.Length <= maxCharacters)
			return value;

		return value.Substring(value.Length - maxCharacters);
	}

	private static string SanitizeFileName(string name)
	{
		var invalid = Path.GetInvalidFileNameChars();
		var chars = name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
		return new string(chars);
	}

	private sealed class CrashLogRegistration
	{
		public CrashLogRegistration(
			string pipeName,
			UnhandledExceptionEventHandler appDomainHandler,
			ThreadExceptionEventHandler winFormsHandler,
			Application? application,
			DispatcherUnhandledExceptionEventHandler? dispatcherHandler)
		{
			PipeName = pipeName;
			AppDomainHandler = appDomainHandler;
			WinFormsHandler = winFormsHandler;
			Application = application;
			DispatcherHandler = dispatcherHandler;
		}

		public string PipeName { get; }

		public UnhandledExceptionEventHandler AppDomainHandler { get; }

		public ThreadExceptionEventHandler WinFormsHandler { get; }

		public Application? Application { get; }

		public DispatcherUnhandledExceptionEventHandler? DispatcherHandler { get; }
	}
}
