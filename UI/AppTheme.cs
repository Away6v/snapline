using System.Windows.Media;

namespace SnapLine;

internal static class AppTheme
{
    internal static readonly Color AccentColor = Color.FromRgb(0x38, 0xA9, 0xE8);
    internal static readonly Color SurfaceColor = Color.FromRgb(0xFF, 0xFF, 0xFF);
    internal static readonly Color AccentDarkColor = Color.FromRgb(0x0B, 0x78, 0xB4);
    internal static readonly Brush Accent = new SolidColorBrush(AccentColor);
    internal static readonly Brush Surface = new SolidColorBrush(SurfaceColor);
    internal static readonly Brush AccentDark = new SolidColorBrush(AccentDarkColor);
    internal static readonly Brush AccentHover = new SolidColorBrush(Color.FromRgb(0x20, 0x96, 0xD7));
    internal static readonly Brush AccentPressed = new SolidColorBrush(Color.FromRgb(0x0C, 0x80, 0xBF));
    internal static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0x27, 0x36, 0x44));
    internal static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x70, 0x80, 0x90));
    internal static readonly Brush Outline = new SolidColorBrush(Color.FromRgb(0xE2, 0xE9, 0xEF));
    internal static readonly Brush Soft = new SolidColorBrush(Color.FromRgb(0xF5, 0xF9, 0xFC));
    internal static readonly Brush Hover = new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xFE));
    internal static readonly Brush Pressed = new SolidColorBrush(Color.FromRgb(0xD5, 0xED, 0xFC));
    internal static readonly Brush Alert = new SolidColorBrush(Color.FromRgb(0xA7, 0x5A, 0x4E));
}
