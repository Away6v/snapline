using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SnapLine;

internal sealed partial class CaptureEditorWindow
{
    private sealed class ColorPickerWindow : Window
    {
        private readonly Grid field = new() { Width = 280, Height = 190, ClipToBounds = true, Cursor = Cursors.Cross };
        private readonly Border hueLayer = new();
        private readonly Ellipse marker = new() { Width = 14, Height = 14, Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Opacity = 0.8 } };
        private readonly Slider hueSlider = new() { Minimum = 0, Maximum = 360, Height = 24, Margin = new Thickness(0, 12, 0, 0) };
        private readonly TextBox hex = new() { Width = 110, Height = 34, Padding = new Thickness(9, 6, 9, 6), FontSize = 14, CharacterCasing = CharacterCasing.Upper, MaxLength = 7, VerticalContentAlignment = VerticalAlignment.Center };
        private readonly Border preview = new() { Width = 46, Height = 34, CornerRadius = new CornerRadius(5), BorderBrush = AppTheme.Outline, BorderThickness = new Thickness(1) };
        private double hue, saturation, value;
        internal Color SelectedColor { get; private set; }

        internal ColorPickerWindow(Window owner, Color initial)
        {
            Owner = owner; Title = Words.T("自定义颜色", "Custom color"); WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight; ResizeMode = ResizeMode.NoResize; Background = AppTheme.Surface; Foreground = AppTheme.Ink; ShowInTaskbar = false;
            var panel = new StackPanel { Margin = new Thickness(22) }; Content = panel;

            field.Children.Add(hueLayer);
            field.Children.Add(new Border { Background = new LinearGradientBrush(Colors.White, Colors.Transparent, 0) });
            field.Children.Add(new Border { Background = new LinearGradientBrush(Colors.Transparent, Colors.Black, 90) });
            field.Children.Add(marker); panel.Children.Add(new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = field });
            AutomationProperties.SetName(field, Words.T("饱和度和亮度", "Saturation and brightness"));
            var spectrum = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            for (var i = 0; i <= 6; i++) spectrum.GradientStops.Add(new GradientStop(FromHsv(i * 60, 1, 1), i / 6.0));
            var sliderGrid = new Grid(); sliderGrid.Children.Add(new Border { Height = 8, Margin = new Thickness(5, 12, 5, 0), CornerRadius = new CornerRadius(4), Background = spectrum, VerticalAlignment = VerticalAlignment.Center });
            hueSlider.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
                <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Slider">
                    <Track x:Name="PART_Track">
                        <Track.DecreaseRepeatButton><RepeatButton Command="Slider.DecreaseLarge" Opacity="0" /></Track.DecreaseRepeatButton>
                        <Track.Thumb><Thumb Width="14" Height="18"><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="White" BorderBrush="#888888" BorderThickness="1" CornerRadius="7" /></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
                        <Track.IncreaseRepeatButton><RepeatButton Command="Slider.IncreaseLarge" Opacity="0" /></Track.IncreaseRepeatButton>
                    </Track>
                </ControlTemplate>
                """);
            sliderGrid.Children.Add(hueSlider); panel.Children.Add(sliderGrid);
            AutomationProperties.SetName(hueSlider, Words.T("色相", "Hue"));
            var row = new DockPanel { Margin = new Thickness(0, 14, 0, 0), LastChildFill = false };
            row.Children.Add(preview); var label = new TextBlock { Text = "HEX", Foreground = AppTheme.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 8, 0) }; row.Children.Add(label); row.Children.Add(hex); panel.Children.Add(row);
            AutomationProperties.SetName(hex, Words.T("十六进制颜色", "Hex color"));
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
            var cancel = new Button { Content = Words.T("取消", "Cancel"), Width = 76, Height = 34, Style = EditorButtonStyle, Foreground = AppTheme.Ink, IsCancel = true };
            var confirm = new Button { Content = Words.T("确定", "OK"), Width = 76, Height = 34, Margin = new Thickness(8, 0, 0, 0), Style = EditorPrimaryButtonStyle, Foreground = Brushes.White, IsDefault = true };
            actions.Children.Add(cancel); actions.Children.Add(confirm); panel.Children.Add(actions);
            confirm.Click += (_, _) => { if (ApplyHex()) DialogResult = true; };
            hex.LostKeyboardFocus += (_, _) => ApplyHex();
            hueSlider.ValueChanged += (_, _) => { hue = hueSlider.Value; Refresh(); };
            field.MouseLeftButtonDown += (_, e) => { field.CaptureMouse(); Pick(e.GetPosition(field)); };
            field.MouseMove += (_, e) => { if (field.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) Pick(e.GetPosition(field)); };
            field.MouseLeftButtonUp += (_, _) => field.ReleaseMouseCapture();
            SetFromColor(initial); hueSlider.Value = hue; Refresh(); ThemeWindow.Apply(this);
        }

        private void Pick(Point point)
        {
            saturation = Math.Clamp(point.X / field.ActualWidth, 0, 1); value = 1 - Math.Clamp(point.Y / field.ActualHeight, 0, 1); Refresh();
        }
        private void Refresh()
        {
            SelectedColor = FromHsv(hue, saturation, value); hueLayer.Background = new SolidColorBrush(FromHsv(hue, 1, 1));
            preview.Background = new SolidColorBrush(SelectedColor); hex.Text = $"#{SelectedColor.R:X2}{SelectedColor.G:X2}{SelectedColor.B:X2}";
            marker.HorizontalAlignment = HorizontalAlignment.Left; marker.VerticalAlignment = VerticalAlignment.Top;
            marker.Margin = new Thickness(saturation * field.Width - 7, (1 - value) * field.Height - 7, 0, 0);
        }
        private bool ApplyHex()
        {
            var text = hex.Text.Trim().TrimStart('#');
            if (text.Length != 6 || !int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            {
                hex.BorderBrush = AppTheme.Alert; hex.ToolTip = Words.T("请输入 6 位十六进制颜色", "Enter a 6-digit hex color"); return false;
            }
            hex.ClearValue(BorderBrushProperty); hex.ToolTip = null;
            SetFromColor(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)); hueSlider.Value = hue; Refresh(); return true;
        }
        private void SetFromColor(Color color)
        {
            var r = color.R / 255.0; var g = color.G / 255.0; var b = color.B / 255.0;
            var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b)); var delta = max - min;
            value = max; saturation = max == 0 ? 0 : delta / max;
            hue = delta == 0 ? 0 : max == r ? 60 * (((g - b) / delta + 6) % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
        }
        private static Color FromHsv(double h, double s, double v)
        {
            var c = v * s; var x = c * (1 - Math.Abs((h / 60 % 2) - 1)); var m = v - c;
            var (r, g, b) = h < 60 ? (c, x, 0d) : h < 120 ? (x, c, 0d) : h < 180 ? (0d, c, x) : h < 240 ? (0d, x, c) : h < 300 ? (x, 0d, c) : (c, 0d, x);
            return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
        }
    }
}




