namespace DeepFlowTest.Mcp.ViewModels;

using System;
using System.Threading.Tasks;
using System.Windows.Input;

internal sealed class RelayCommand : ICommand
{
	private readonly Action execute;
	private readonly Func<bool>? canExecute;

	public RelayCommand(Action execute, Func<bool>? canExecute = null)
	{
		this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
		this.canExecute = canExecute;
	}

	public event EventHandler? CanExecuteChanged;

	public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

	public void Execute(object? parameter) => execute();

	public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

internal sealed class AsyncRelayCommand : ICommand
{
	private readonly Func<Task> execute;
	private readonly Func<bool>? canExecute;
	private bool isRunning;

	public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
	{
		this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
		this.canExecute = canExecute;
	}

	public event EventHandler? CanExecuteChanged;

	public bool CanExecute(object? parameter) => !isRunning && (canExecute?.Invoke() ?? true);

	public async void Execute(object? parameter)
	{
		if (!CanExecute(parameter))
			return;

		try
		{
			isRunning = true;
			RaiseCanExecuteChanged();
			await execute();
		}
		finally
		{
			isRunning = false;
			RaiseCanExecuteChanged();
		}
	}

	public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
