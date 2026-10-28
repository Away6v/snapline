using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace SnapLine;

internal static class WarmCardMenu
{
    private static readonly Brush Surface = AppTheme.Surface;
    private static readonly Brush Outline = AppTheme.Outline;
    private static readonly Brush Ink = AppTheme.Ink;
    private static readonly Brush Accent = AppTheme.AccentDark;
    private static readonly Brush Hover = AppTheme.Hover;
    private static readonly Brush Pressed = AppTheme.Pressed;
    private static readonly Style ItemStyle = CreateItemStyle();
    private static readonly Style SeparatorStyle = CreateSeparatorStyle();
    private static readonly ControlTemplate MenuTemplate = CreateMenuTemplate();

    internal static ContextMenu Create() => new()
    {
        Template = MenuTemplate,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        HasDropShadow = false,
        MinWidth = 215,
        FontFamily = new FontFamily("Microsoft YaHei UI"),
        FontSize = 13
    };

    internal static MenuItem Item(string title, string symbol, bool destructive = false)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock
        {
            Text = symbol,
            Width = 26,
            FontFamily = new FontFamily("Segoe UI Symbol"),
            FontSize = 15,
            Foreground = destructive ? new SolidColorBrush(Color.FromRgb(178, 82, 72)) : Accent,
            VerticalAlignment = VerticalAlignment.Center
        });
        header.Children.Add(new TextBlock { Text = title, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center });
        return new MenuItem { Header = header, Style = ItemStyle };
    }

    internal static Separator Separator() => new() { Style = SeparatorStyle };

    private static ControlTemplate CreateMenuTemplate()
    {
        var frame = new FrameworkElementFactory(typeof(Border));
        frame.SetValue(Border.BackgroundProperty, Surface);
        frame.SetValue(Border.BorderBrushProperty, Outline);
        frame.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(13));
        frame.SetValue(Border.PaddingProperty, new Thickness(7));
        frame.SetValue(UIElement.EffectProperty, new DropShadowEffect
        {
            Color = Color.FromRgb(67, 57, 52), BlurRadius = 16, ShadowDepth = 3, Opacity = 0.15
        });
        frame.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
        return new ControlTemplate(typeof(ContextMenu)) { VisualTree = frame };
    }

    private static Style CreateItemStyle()
    {
        var surface = new FrameworkElementFactory(typeof(Border), "ItemSurface");
        surface.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        surface.SetValue(Border.PaddingProperty, new Thickness(11, 8, 17, 8));
        surface.SetValue(FrameworkElement.MinHeightProperty, 35.0);
        var header = new FrameworkElementFactory(typeof(ContentPresenter));
        header.SetBinding(ContentPresenter.ContentProperty, new Binding(nameof(MenuItem.Header))
        {
            RelativeSource = RelativeSource.TemplatedParent
        });
        surface.AppendChild(header);
        var template = new ControlTemplate(typeof(MenuItem)) { VisualTree = surface };
        var highlighted = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
        highlighted.Setters.Add(new Setter(Border.BackgroundProperty, Hover, "ItemSurface"));
        template.Triggers.Add(highlighted);
        var pressed = new Trigger { Property = MenuItem.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.BackgroundProperty, Pressed, "ItemSurface"));
        template.Triggers.Add(pressed);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45, "ItemSurface"));
        template.Triggers.Add(disabled);
        var style = new Style(typeof(MenuItem));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        return style;
    }

    private static Style CreateSeparatorStyle()
    {
        var line = new FrameworkElementFactory(typeof(Border));
        line.SetValue(Border.BackgroundProperty, Outline);
        line.SetValue(FrameworkElement.HeightProperty, 1.0);
        line.SetValue(FrameworkElement.MarginProperty, new Thickness(11, 6, 11, 6));
        var style = new Style(typeof(Separator));
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Separator)) { VisualTree = line }));
        return style;
    }
}
