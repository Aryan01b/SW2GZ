/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8+ — one row in the Materials step: a link plus an optional override
material. When UseMaterial is on, the four RGBA channels (0..1) plus a name
build a MaterialDef. A two-way HexColor (#RRGGBB[AA]) mirrors RGBA for
convenience; parsing is manual (no System.Windows.Media.Color) so the VM
stays WPF-free and net48-safe. Out-of-range channels are clamped to 0..1.
*/
using System.Globalization;
using SW2GZ.Build.Model;
using SW2GZ.UI.Mvvm;

namespace SW2GZ.UI.ViewModels
{
    public sealed class LinkMaterialViewModel : ObservableObject
    {
        private bool _useMaterial;
        private string _materialName;
        private double _r = 0.8;
        private double _g = 0.8;
        private double _b = 0.8;
        private double _a = 1.0;

        public LinkMaterialViewModel(string linkName)
        {
            LinkName = linkName ?? string.Empty;
            _materialName = LinkName + "_material";
        }

        public string LinkName { get; }

        public bool UseMaterial
        {
            get => _useMaterial;
            set => SetProperty(ref _useMaterial, value);
        }

        public string MaterialName
        {
            get => _materialName;
            set => SetProperty(ref _materialName, value ?? string.Empty);
        }

        public double R
        {
            get => _r;
            set => SetChannel(ref _r, value);
        }

        public double G
        {
            get => _g;
            set => SetChannel(ref _g, value);
        }

        public double B
        {
            get => _b;
            set => SetChannel(ref _b, value);
        }

        public double A
        {
            get => _a;
            set => SetChannel(ref _a, value);
        }

        /// #RRGGBB (alpha implied 1.0 on read) two-way bound to RGBA. Setting an
        /// 8-digit #RRGGBBAA also drives A. Invalid strings are ignored.
        public string HexColor
        {
            get => ToHex(_r, _g, _b, _a);
            set
            {
                if (TryParseHex(value, out double r, out double g, out double b, out double a))
                {
                    _r = r; _g = g; _b = b; _a = a;
                    OnPropertyChanged(nameof(R));
                    OnPropertyChanged(nameof(G));
                    OnPropertyChanged(nameof(B));
                    OnPropertyChanged(nameof(A));
                    OnPropertyChanged(nameof(HexColor));
                }
            }
        }

        /// MaterialDef for this row (caller only calls when UseMaterial is true).
        public MaterialDef BuildMaterial() =>
            new MaterialDef(MaterialName, _r, _g, _b, _a);

        private void SetChannel(ref double field, double value)
        {
            double clamped = Clamp01(value);
            if (SetProperty(ref field, clamped))
                OnPropertyChanged(nameof(HexColor));
        }

        private static double Clamp01(double v) => v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);

        private static string ToHex(double r, double g, double b, double a)
        {
            int ri = (int)System.Math.Round(Clamp01(r) * 255.0);
            int gi = (int)System.Math.Round(Clamp01(g) * 255.0);
            int bi = (int)System.Math.Round(Clamp01(b) * 255.0);
            int ai = (int)System.Math.Round(Clamp01(a) * 255.0);
            return "#" + ri.ToString("X2", CultureInfo.InvariantCulture)
                       + gi.ToString("X2", CultureInfo.InvariantCulture)
                       + bi.ToString("X2", CultureInfo.InvariantCulture)
                       + ai.ToString("X2", CultureInfo.InvariantCulture);
        }

        private static bool TryParseHex(string hex, out double r, out double g, out double b, out double a)
        {
            r = g = b = 0.0; a = 1.0;
            if (string.IsNullOrWhiteSpace(hex))
                return false;
            string s = hex.Trim();
            if (s.Length > 0 && s[0] == '#')
                s = s.Substring(1);
            if (s.Length != 6 && s.Length != 8)
                return false;

            if (!TryByte(s, 0, out int ri) ||
                !TryByte(s, 2, out int gi) ||
                !TryByte(s, 4, out int bi))
                return false;
            int ai = 255;
            if (s.Length == 8 && !TryByte(s, 6, out ai))
                return false;

            r = ri / 255.0; g = gi / 255.0; b = bi / 255.0; a = ai / 255.0;
            return true;
        }

        private static bool TryByte(string s, int offset, out int value) =>
            int.TryParse(s.Substring(offset, 2), NumberStyles.HexNumber,
                         CultureInfo.InvariantCulture, out value);
    }
}
