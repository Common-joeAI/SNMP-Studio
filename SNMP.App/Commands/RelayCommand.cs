using System.Windows.Input;

namespace SNMP.App;

public sealed class RelayCommand : ICommand
{
    private readonly Func<Task>? _asyncAction;
    private readonly Action?     _syncAction;
    private readonly Func<bool>? _canExecute;
    private bool _isExecuting;

    public RelayCommand(Func<Task> action, Func<bool>? canExecute = null)
    { _asyncAction = action; _canExecute = canExecute; }

    public RelayCommand(Action action, Func<bool>? canExecute = null)
    { _syncAction = action; _canExecute = canExecute; }

    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() =>
        App.Current.Dispatcher.Invoke(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty));

    public bool CanExecute(object? _) => !_isExecuting && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? _)
    {
        if (!CanExecute(null)) return;
        _isExecuting = true; RaiseCanExecuteChanged();
        try
        {
            if (_asyncAction != null) await _asyncAction();
            else _syncAction?.Invoke();
        }
        finally { _isExecuting = false; RaiseCanExecuteChanged(); }
    }
}

/// <summary>Generic relay command — passes a typed parameter to action.</summary>
public sealed class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _action;
    private readonly Func<T?, bool>? _canExecute;

    public RelayCommand(Action<T?> action, Func<T?, bool>? canExecute = null)
    { _action = action; _canExecute = canExecute; }

    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() =>
        App.Current.Dispatcher.Invoke(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty));

    public bool CanExecute(object? p) => _canExecute?.Invoke(p is T t ? t : default) ?? true;
    public void Execute(object? p)    => _action(p is T t ? t : default);
}
