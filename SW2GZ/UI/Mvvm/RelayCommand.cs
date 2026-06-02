/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — MVVM infra. ICommand implementations backing the wizard buttons.
System.Windows.Input.ICommand lives in System.dll on net48 and in the
shared framework on net8, so this file is net-portable and source-links
cleanly into the test project.
*/
using System;
using System.Windows.Input;

namespace SW2GZ.UI.Mvvm
{
    /// Parameterless relay command.
    public sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute();

        public void Execute(object parameter) => _execute();

        public void RaiseCanExecuteChanged() =>
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    /// Strongly-typed relay command. The parameter is coerced from the
    /// ICommand object; a null/incompatible parameter yields default(T).
    public sealed class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Func<T, bool> _canExecute;

        public RelayCommand(Action<T> execute, Func<T, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter) =>
            _canExecute == null || _canExecute(Coerce(parameter));

        public void Execute(object parameter) => _execute(Coerce(parameter));

        public void RaiseCanExecuteChanged() =>
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);

        private static T Coerce(object parameter) =>
            parameter is T typed ? typed : default;
    }
}
