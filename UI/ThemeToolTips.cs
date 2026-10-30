using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace SnapLine;

internal static class ThemeToolTips
{
    internal static ToolTip Create(string text)
    {
        var frame = new FrameworkElementFactory(typeof(Border));
        frame.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0xFF, 0xFC, 0xF6)));
        frame.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0xDF, 0xD8, 0xCA)));
        frame.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        frame.SetValue(Border.PaddingProperty, new Thickness(11, 7, 11, 7));
        frame.SetValue(UIElement.EffectProperty, new DropShadowEffect
        {
            Color = Color.FromRgb(0x43, 0x39, 0x34), BlurRadius = 8, ShadowDepth = 2, Opacity = 0.16
        });
        frame.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        return new ToolTip
        {
            Content = text, Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x50, 0x45)),
            FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 12,
            Placement = PlacementMode.Bottom, VerticalOffset = 8,
            HasDropShadow = false, Template = new ControlTemplate(typeof(ToolTip)) { VisualTree = frame }
        };
    }
}
