namespace DeepFlowTest.InjectorLauncher;

using System;
using System.Collections.Generic;
using System.Globalization;

internal sealed class InjectorLauncherCommandLineOptions
{
	public int TargetProcessId { get; private set; }

	public long TargetWindowHandle { get; private set; }

	public string Assembly { get; private set; } = string.Empty;

	public string ClassName { get; private set; } = string.Empty;

	public string MethodName { get; private set; } = string.Empty;

	public string StartupArgument { get; private set; } = string.Empty;

	public bool Verbose { get; private set; }

	public bool Debug { get; private set; }

	public bool AttachConsoleToParent { get; private set; }

	public static bool TryParse(string[] args, out InjectorLauncherCommandLineOptions options, out string error)
	{
		options = new InjectorLauncherCommandLineOptions();
		error = string.Empty;

		var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		for (var i = 0; i < args.Length; i++)
		{
			var arg = args[i];
			switch (arg)
			{
				case "-t":
				case "--targetPID":
				case "--targetPid":
				case "--pid":
					if (!ReadValue(args, ref i, arg, out var targetPid, out error))
						return false;
					values["targetPID"] = targetPid;
					break;
				case "-h":
				case "--targetHwnd":
				case "--hwnd":
					if (!ReadValue(args, ref i, arg, out var targetHwnd, out error))
						return false;
					values["targetHwnd"] = targetHwnd;
					break;
				case "-a":
				case "--assembly":
					if (!ReadValue(args, ref i, arg, out var assembly, out error))
						return false;
					values["assembly"] = assembly;
					break;
				case "-c":
				case "--className":
					if (!ReadValue(args, ref i, arg, out var className, out error))
						return false;
					values["className"] = className;
					break;
				case "-m":
				case "--methodName":
					if (!ReadValue(args, ref i, arg, out var methodName, out error))
						return false;
					values["methodName"] = methodName;
					break;
				case "-s":
				case "--startupArgument":
					if (!ReadValue(args, ref i, arg, out var startupArgument, out error))
						return false;
					values["startupArgument"] = startupArgument;
					break;
				case "-v":
				case "--verbose":
					options.Verbose = true;
					break;
				case "-d":
				case "--debug":
					options.Debug = true;
					break;
				case "--attachConsoleToParent":
					options.AttachConsoleToParent = true;
					break;
				default:
					error = $"Unknown option '{arg}'.";
					return false;
			}
		}

		if (!values.TryGetValue("targetPID", out var pidText) || !int.TryParse(pidText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid))
		{
			error = "Missing or invalid required option '--targetPID'.";
			return false;
		}

		options.TargetProcessId = pid;
		if (values.TryGetValue("targetHwnd", out var hwndText))
		{
			if (!long.TryParse(hwndText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hwnd))
			{
				error = "Invalid option '--targetHwnd'.";
				return false;
			}

			options.TargetWindowHandle = hwnd;
		}

		if (!ReadRequired(values, "assembly", out var assemblyValue, out error) ||
			!ReadRequired(values, "className", out var classNameValue, out error) ||
			!ReadRequired(values, "methodName", out var methodNameValue, out error))
		{
			return false;
		}

		options.Assembly = assemblyValue;
		options.ClassName = classNameValue;
		options.MethodName = methodNameValue;
		options.StartupArgument = values.TryGetValue("startupArgument", out var parsedStartupArgument) ? parsedStartupArgument : string.Empty;
		return true;
	}

	private static bool ReadValue(string[] args, ref int index, string option, out string value, out string error)
	{
		if (index + 1 >= args.Length)
		{
			value = string.Empty;
			error = $"Missing value for '{option}'.";
			return false;
		}

		value = args[++index];
		error = string.Empty;
		return true;
	}

	private static bool ReadRequired(Dictionary<string, string> values, string name, out string value, out string error)
	{
		if (values.TryGetValue(name, out value!) && !string.IsNullOrWhiteSpace(value))
		{
			error = string.Empty;
			return true;
		}

		error = $"Missing required option '--{name}'.";
		return false;
	}
}
