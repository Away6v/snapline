using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SnapLine;

internal static class AppTheme
{
    private sealed class ColorValue : INotifyPropertyChanged
    {
        private Color value;
        public Color Value
        {
            get => value;
            set { this.value = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private static readonly ColorValue[] colors = new ColorValue[12];
    private static Brush Create(int index)
    {
        var value = new ColorValue();
        colors[index] = value;
        var brush = new SolidColorBrush();
        BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty, new Binding(nameof(ColorValue.Value)) { Source = value });
        return brush;
    }

    internal static readonly Brush Accent = Create(0);
    internal static readonly Brush Surface = Create(1);
    internal static readonly Brush AccentDark = Create(2);
    internal static readonly Brush AccentHover = Create(3);
    internal static readonly Brush AccentPressed = Create(4);
    internal static readonly Brush Ink = Create(5);
    internal static readonly Brush Muted = Create(6);
    internal static readonly Brush Outline = Create(7);
    internal static readonly Brush Soft = Create(8);
    internal static readonly Brush Hover = Create(9);
    internal static readonly Brush Pressed = Create(10);
    internal static readonly Brush Alert = Create(11);

    internal static void Initialize(Application application)
    {
        application.Resources["ThemeAccent"] = Accent;
        application.Resources["ThemeSurface"] = Surface;
        application.Resources["ThemeAccentDark"] = AccentDark;
        application.Resources["ThemeAccentHover"] = AccentHover;
        application.Resources["ThemeAccentPressed"] = AccentPressed;
        application.Resources["ThemeInk"] = Ink;
        application.Resources["ThemeMuted"] = Muted;
        application.Resources["ThemeOutline"] = Outline;
        application.Resources["ThemeSoft"] = Soft;
        application.Resources["ThemeHover"] = Hover;
        application.Resources["ThemePressed"] = Pressed;
        Apply(LineAppearance.Hemp);
    }

    internal static void Apply(string appearance)
    {
        // 每套配色分别定义表面、文字和交互状态，保持深浅主题的可读性。
        var palette = appearance switch
        {
            LineAppearance.Denim => new[] { 0x4A7090, 0xFCFEFF, 0x355B79, 0x3D607E, 0x304F68, 0x2E3942, 0x6D7E89, 0xD9E4EA, 0xF2F7FA, 0xE4EFF5, 0xD5E5EE },
            LineAppearance.Sakura => new[] { 0xA75772, 0xFFFDFE, 0x8C405D, 0x914963, 0x7C3D54, 0x40323A, 0x87727C, 0xEEDFE5, 0xFCF5F8, 0xF8E9F0, 0xF0D7E2 },
            LineAppearance.Silver => new[] { 0x666A70, 0xFEFEFE, 0x4D5157, 0x595D63, 0x484C51, 0x333538, 0x797C80, 0xDFE1E3, 0xF5F6F7, 0xEAECEE, 0xDDE0E2 },
            LineAppearance.Graphite => new[] { 0x526474, 0x252B30, 0xB7C9D3, 0x627688, 0x435361, 0xE8EDEF, 0xB5C0C6, 0x505C63, 0x30373D, 0x3B464E, 0x485762 },
            LineAppearance.Classic => new[] { 0xAD5975, 0xFFFDFE, 0x91445E, 0x994B65, 0x823E55, 0x392F3A, 0x7F707D, 0xEADFE6, 0xFAF5F8, 0xF6E8EF, 0xEFD7E2 },
            LineAppearance.Forest => new[] { 0x587D56, 0xFCFEFA, 0x3F653E, 0x496D47, 0x3B5A39, 0x303B30, 0x708071, 0xDCE6D8, 0xF3F8F0, 0xE6F0E2, 0xD4E6D0 },
            _ => new[] { 0x936A45, 0xFFFDFA, 0x785234, 0x805A39, 0x6D4B2F, 0x3D352E, 0x85786D, 0xE9DFD2, 0xFAF5EE, 0xF3E9DB, 0xEADBC8 }
        };
        for (var i = 0; i < palette.Length; i++)
            colors[i].Value = Color.FromRgb((byte)(palette[i] >> 16), (byte)(palette[i] >> 8), (byte)palette[i]);
        colors[11].Value = appearance == LineAppearance.Graphite
            ? Color.FromRgb(0xF0, 0xA7, 0x9A)
            : Color.FromRgb(0xA7, 0x5A, 0x4E);
    }
}
