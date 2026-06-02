/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — small WPF value converters used by the wizard XAML. net48-only (depend
on System.Windows.Data); NOT source-linked into the test project. The VM
layer carries no WPF types, so these stay out of the testable surface.

REQUIRES VISUAL STUDIO BUILD to exercise via markup.
*/
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SW2GZ.UI.Wizard
{
    /// True -> Visible, False -> Collapsed.
    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is Visibility v && v == Visibility.Visible;
    }

    /// 0..1 fraction -> percentage width string is handled in XAML; this
    /// converter maps the fraction to a 0..100 double for a ProgressBar Value.
    public sealed class FractionToPercentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is double d ? d * 100.0 : 0.0;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is double d ? d / 100.0 : 0.0;
    }

    /// Rail glyph: complete steps render a check, pending/active render a dot.
    /// Exposes a singleton Instance so XAML can use it via x:Static without a
    /// resource declaration.
    public sealed class StepGlyphConverter : IValueConverter
    {
        public static readonly StepGlyphConverter Instance = new StepGlyphConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            (value is bool b && b) ? "✓" : "•";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }

    /// Equality check against the ConverterParameter (used for card / radio
    /// selection highlighting). Returns true when value.ToString() == parameter.
    public sealed class EnumEqualsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value != null && parameter != null &&
            string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            (value is bool b && b) ? parameter : Binding.DoNothing;
    }
}
