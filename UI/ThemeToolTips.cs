using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace SnapLine;

internal static class ThemeToolTips
{
    internal static void Apply(Application application)
    {
        var frame = new FrameworkElementFactory(typeof(Border));
        frame.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x35, 0x3B, 0x40)));
        frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        frame.SetValue(Border.PaddingProperty, new Thickness(10, 6, 10, 6));
        frame.SetValue(UIElement.EffectProperty, new DropShadowEffect
        {
            Color = Colors.Black, BlurRadius = 10, ShadowDepth = 2, Opacity = 0.18
        });
        frame.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        var style = new Style(typeof(ToolTip));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        style.Setters.Add(new Setter(Control.FontFamilyProperty, new FontFamily("Microsoft YaHei UI")));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
        style.Setters.Add(new Setter(ToolTip.HasDropShadowProperty, false));
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(ToolTip)) { VisualTree = frame }));
        application.Resources[typeof(ToolTip)] = style;
    }

    internal static ToolTip Create(string text) => new()
    {
        Content = text, Placement = PlacementMode.Bottom, VerticalOffset = 8
    };
}
