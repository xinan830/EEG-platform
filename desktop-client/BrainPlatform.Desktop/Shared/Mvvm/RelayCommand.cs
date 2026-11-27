using System.Windows.Input;

namespace BrainPlatform.Desktop.Shared.Mvvm;

internal sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    private readonly Action execute = execute;
    private readonly Func<bool> canExecute = canExecute ?? (() => true);

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute();

    public void Execute(object? parameter) => execute();

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

internal sealed class RelayCommand<T>(Action<T?> execute, Func<T?, bool>? canExecute = null) : ICommand
{
    private readonly Action<T?> execute = execute;
    private readonly Func<T?, bool> canExecute = canExecute ?? (_ => true);

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => parameter is T value ? canExecute(value) : parameter is null && canExecute(default);

    public void Execute(object? parameter) => execute(parameter is T value ? value : default);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
