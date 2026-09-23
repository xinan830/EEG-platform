using System.Windows.Input;

namespace BrainPlatform.Desktop.ViewModels;

/// <summary>
/// Runs UI tasks serially and requires a local error sink. Commands must never
/// let expected business failures escape through WPF's global dispatcher.
/// </summary>
public sealed class AsyncRelayCommand(Func<Task> execute, Action<Exception> reportError) : ICommand
{
    private bool isExecuting;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !isExecuting;

    public async void Execute(object? parameter)
    {
        if (isExecuting)
        {
            return;
        }

        isExecuting = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await execute();
        }
        catch (Exception exception)
        {
            reportError(exception);
        }
        finally
        {
            isExecuting = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
