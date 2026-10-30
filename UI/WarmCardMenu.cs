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
        var icon = symbol switch
        {
            "preview" => "M 1,8 C 3,4 5.5,2.5 8,2.5 C 10.5,2.5 13,4 15,8 C 13,12 10.5,13.5 8,13.5 C 5.5,13.5 3,12 1,8 Z M 10,8 A 2,2 0 1 1 6,8 A 2,2 0 1 1 10,8 Z",
            "pin" => "M 5,2 L 11,2 M 6,2 L 6,6 L 3.5,9 L 12.5,9 L 10,6 L 10,2 M 8,9 L 8,14.5",
            "reset" => "M 3,6 A 5.5,5.5 0 1 1 3,10 M 3,2.5 L 3,6 L 6.5,6 M 8,5 L 8,11",
            "close" => "M 3,3 L 13,13 M 13,3 L 3,13",
            "open" => "M 9,2 L 14,2 L 14,7 M 14,2 L 7,9 M 12,9 L 12,13 L 3,13 L 3,4 L 7,4",
            "save" => "M 8,1 L 8,10 M 4.5,6.5 L 8,10 L 11.5,6.5 M 2,11 L 2,14 L 14,14 L 14,11",
            "folder" => "M 1.5,4 L 1.5,13 L 14.5,13 L 14.5,5.5 L 8,5.5 L 6.5,4 Z",
            "remove" => "M 3,4 L 13,4 M 6,4 L 6,2 L 10,2 L 10,4 M 4.5,4 L 5,14 L 11,14 L 11.5,4 M 7,7 L 7,11 M 9,7 L 9,11",
            "remove-all" => "M 2,5 L 12,5 M 5,5 L 5,3 L 9,3 L 9,5 M 3.5,5 L 4,14 L 10,14 L 10.5,5 M 6,8 L 6,11 M 8,8 L 8,11 M 12,2 L 15,2 M 13.5,0.5 L 13.5,3.5",
            _ => null
        };
        var color = destructive ? new SolidColorBrush(Color.FromRgb(178, 82, 72)) : Accent;
        if (icon != null)
        {
            var holder = new Grid { Width = 26, Height = 16, VerticalAlignment = VerticalAlignment.Center };
            holder.Children.Add(new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(icon), Stroke = color, StrokeThickness = 1.4,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round, Width = 16, Height = 16,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center
            });
            header.Children.Add(holder);
        }
        else
        {
            header.Children.Add(new TextBlock
            {
                Text = symbol, Width = 26, FontFamily = new FontFamily("Segoe UI Symbol"),
                FontSize = 15, Foreground = color, VerticalAlignment = VerticalAlignment.Center
            });
        }
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
