using System;
using System.Windows.Input;

namespace VpnClientWPF.ViewModels;

public class RelayCommand : ICommand {
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
        
    public RelayCommand(Func<Task> execute, Func<bool>? canExecute = null) {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute == null || _canExecute();
    public async void Execute(object? parameter) => await _execute();
    public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();

    public event EventHandler? CanExecuteChanged {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}