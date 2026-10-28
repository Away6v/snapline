using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace SnapLine;

internal static class ThemeButtons
{
    internal static readonly Style Primary = Create(AppTheme.Accent, AppTheme.AccentHover, AppTheme.AccentPressed, Brushes.White);
    internal static readonly Style Secondary = Create(AppTheme.Soft, AppTheme.Hover, AppTheme.Pressed, AppTheme.Ink);

    private static Style Create(Brush background, Brush hover, Brush pressed, Brush foreground)
    {
        var frame = new FrameworkElementFactory(typeof(Border), "Frame");
        frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        frame.SetBinding(Border.BackgroundProperty, new Binding(nameof(Control.Background)) { RelativeSource = RelativeSource.TemplatedParent });
        frame.SetBinding(Border.PaddingProperty, new Binding(nameof(Control.Padding)) { RelativeSource = RelativeSource.TemplatedParent });
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        frame.AppendChild(content);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = frame };
        var focused = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focused.Setters.Add(new Setter(Border.BorderBrushProperty, AppTheme.AccentDark, "Frame"));
        focused.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "Frame"));
        template.Triggers.Add(focused);
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(Control.BackgroundProperty, background));
        style.Setters.Add(new Setter(Control.ForegroundProperty, foreground));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(16, 8, 16, 8)));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Medium));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        over.Setters.Add(new Setter(Control.BackgroundProperty, hover));
        style.Triggers.Add(over);
        var down = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        down.Setters.Add(new Setter(Control.BackgroundProperty, pressed));
        style.Triggers.Add(down);
        return style;
    }
}
