namespace DeepFlowTest.Cli;

using System;
using System.Diagnostics;
using System.IO;

internal sealed class CliCommandExecutionContext
{
	private readonly Stopwatch stopwatch;
	private CliDefaults? defaults;
	private CliCommonOptions? commonOptions;

	public CliCommandExecutionContext(
		string[] args,
		CliServices services,
		TextWriter stdout,
		TextWriter stderr,
		Stopwatch stopwatch)
	{
		Args = args ?? throw new ArgumentNullException(nameof(args));
		Services = services ?? throw new ArgumentNullException(nameof(services));
		Stdout = stdout ?? throw new ArgumentNullException(nameof(stdout));
		Stderr = stderr ?? throw new ArgumentNullException(nameof(stderr));
		this.stopwatch = stopwatch ?? throw new ArgumentNullException(nameof(stopwatch));
	}

	public string[] Args { get; }

	public CliServices Services { get; }

	public TextWriter Stdout { get; }

	public TextWriter Stderr { get; }

	public CliDefaults Defaults => defaults ?? throw new InvalidOperationException("CLI defaults were not loaded.");

	public CliCommonOptions CommonOptions => commonOptions ?? throw new InvalidOperationException("CLI common options were not loaded.");

	public void Configure(CliDefaults loadedDefaults, CliCommonOptions loadedCommonOptions)
	{
		defaults = loadedDefaults ?? throw new ArgumentNullException(nameof(loadedDefaults));
		commonOptions = loadedCommonOptions ?? throw new ArgumentNullException(nameof(loadedCommonOptions));
	}

	public int NotImplemented(string commandName) =>
		Execute(
			commandName,
			targetBound: false,
			() => throw new CliException(CliErrorCodes.NotImplemented, $"Command '{commandName}' is not implemented."));

	public int Execute(string commandName, bool targetBound, Func<object> execute)
	{
		_ = execute ?? throw new ArgumentNullException(nameof(execute));
		var options = CommonOptions;
		try
		{
			options.ValidateEnums();
			if (targetBound)
				options.ValidateTargetSelectorRequired();

			var data = execute();
			if (data is CliResponseSequence sequence)
			{
				foreach (var envelope in sequence.Envelopes)
					CliOutput.Write(envelope, options, Stdout);
				return 0;
			}

			CliOutput.Write(CliResponseFactory.Success(commandName, data, stopwatch), options, Stdout);
			return 0;
		}
		catch (CliException ex)
		{
			CliOutput.Write(CliResponseFactory.Error(commandName, ex.ErrorCode, ex.Message, stopwatch, ex.Details), options, Stdout);
			return ExitCodeMapper.Map(ex.ErrorCode);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
		{
			CliOutput.Write(CliResponseFactory.Error(commandName, CliErrorCodes.UnexpectedError, ex.Message, stopwatch), options, Stdout);
			return ExitCodeMapper.Map(CliErrorCodes.UnexpectedError);
		}
	}
}
