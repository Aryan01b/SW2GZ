/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — MVVM infra. INotifyPropertyChanged base used by every wizard
view-model. Pure C#: depends only on System.ComponentModel, so it compiles
under both net48 (the add-in) and net8 (the source-linked test project).
*/
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SW2GZ.UI.Mvvm
{
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        /// Sets <paramref name="field"/> to <paramref name="value"/> and raises
        /// PropertyChanged when the value actually changed. Returns true if a
        /// change occurred (handy for chaining dependent-property notifications).
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(name);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
