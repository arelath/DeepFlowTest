namespace DeepFlowTest;

using System;
using System.Diagnostics;
using System.IO;
using DeepFlowTest.Utility;

internal interface IRecordingProcess : IDisposable
{
	TextWriter StandardInput { get; }

	void RegisterForParentClose();

	void WaitForExit();
}

internal sealed class ProcessRecordingProcess : IRecordingProcess
{
	private readonly Process process;

	private ProcessRecordingProcess(Process process)
	{
		this.process = process;
	}

	public TextWriter StandardInput => process.StandardInput;

	public void RegisterForParentClose()
	{
		try
		{
			ProcessCloseOnParentClose.Add(process);
		}
		catch
		{
			try
			{
				if (!process.HasExited)
					process.Kill();
			}
			catch (InvalidOperationException)
			{
			}

			throw;
		}
	}

	public static IRecordingProcess Start(ProcessStartInfo startInfo)
	{
		_ = startInfo ?? throw new ArgumentNullException(nameof(startInfo));
		var process = new Process
		{
			EnableRaisingEvents = true,
			StartInfo = startInfo,
		};
		process.Start();
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		return new ProcessRecordingProcess(process);
	}

	public void WaitForExit()
	{
		process.WaitForExit();
	}

	public void Dispose()
	{
		process.Dispose();
	}
}

internal sealed class RecordingScope : IDisposable
{
	private readonly IRecordingProcess process;
	private readonly Action onDisposed;
	private bool disposed;

	public RecordingScope(IRecordingProcess process, Action onDisposed)
	{
		this.process = process ?? throw new ArgumentNullException(nameof(process));
		this.onDisposed = onDisposed ?? throw new ArgumentNullException(nameof(onDisposed));
	}

	public void Dispose()
	{
		if (disposed)
			return;

		disposed = true;
		try
		{
			process.StandardInput.WriteLine("q");
			process.WaitForExit();
		}
		finally
		{
			process.Dispose();
			onDisposed();
		}
	}
}
