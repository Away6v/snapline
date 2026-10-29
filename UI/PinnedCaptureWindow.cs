using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace SnapLine;

internal sealed class PinnedCaptureWindow : Window
{
    public PinnedCaptureWindow(BitmapSource bitmap, Forms.Screen screen, string path, CaptureLibrary library, Action<string> notify)
    {
        var scale = Native.Scale(screen);
        var area = screen.WorkingArea;
        var fit = Math.Min(1, Math.Min(area.Width * 0.7 / bitmap.PixelWidth, area.Height * 0.7 / bitmap.PixelHeight));
        Width = Math.Max(1, bitmap.PixelWidth * fit / scale);
        Height = Math.Max(1, bitmap.PixelHeight * fit / scale);
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        var surface = new Grid { Background = Brushes.Transparent };
        surface.Children.Add(new Image { Source = bitmap, Stretch = Stretch.Fill });
        var pin = new Button
        {
            Width = 32, Height = 32,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(4), ToolTip = Words.T("重新挂回晾衣绳", "Return to line"),
            Opacity = 0, IsHitTestVisible = false
        };
        var buttonSurface = new FrameworkElementFactory(typeof(Border));
        buttonSurface.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        buttonSurface.AppendChild(presenter);
        pin.Template = new ControlTemplate(typeof(Button)) { VisualTree = buttonSurface };
        var icon = new Canvas { Width = 32, Height = 32, IsHitTestVisible = false };
        icon.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 18,19 L 27,27"), Stroke = new SolidColorBrush(Color.FromRgb(0x70, 0x78, 0x77)),
            StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
        });
        icon.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 8,6 L 15,4 L 18,7 L 17,11 L 20,15 L 23,16 L 22,19 L 18,22 L 15,22 L 14,19 L 10,16 L 6,17 L 3,14 L 4,9 Z"),
            Fill = new SolidColorBrush(Color.FromRgb(0x79, 0x8E, 0x78)),
            Stroke = new SolidColorBrush(Color.FromRgb(0x5D, 0x72, 0x5F)), StrokeThickness = 0.8, StrokeLineJoin = PenLineJoin.Round
        });
        icon.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 6,9 Q 9,5 14,6 M 16,16 L 19,19"), Stroke = new SolidColorBrush(Color.FromRgb(0xCD, 0xDB, 0xC6)),
            StrokeThickness = 1.4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
        });
        pin.Content = icon;
        System.Windows.Automation.AutomationProperties.SetName(pin, Words.T("重新挂回晾衣绳", "Return to line"));
        surface.Children.Add(pin);
        Content = surface;
        surface.MouseEnter += (_, _) => { pin.Opacity = 1; pin.IsHitTestVisible = true; };
        surface.MouseLeave += (_, _) => { pin.Opacity = 0; pin.IsHitTestVisible = false; };
        pin.Click += async (_, e) =>
        {
            e.Handled = true;
            pin.IsEnabled = false;
            try { if (await library.ImportAsync(path)) Close(); }
            catch (Exception ex) { notify(Words.T("无法重新挂回：", "Cannot return image to line: ") + ex.Message); }
            finally { pin.IsEnabled = true; }
        };
        var menu = new ContextMenu();
        var close = new MenuItem { Header = Words.T("关闭贴图", "Close pinned image") };
        close.Click += (_, _) => Close();
        menu.Items.Add(close);
        ContextMenu = menu;
        MouseLeftButtonDown += (_, e) => { if (!e.Handled && e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        MouseWheel += (_, e) =>
        {
            var factor = Math.Pow(1.1, e.Delta / 120.0);
            var minFactor = Math.Max(40 / Width, 40 / Height);
            var maxFactor = Math.Min(4096 / Width, 4096 / Height);
            factor = Math.Clamp(factor, Math.Min(1, minFactor), Math.Max(1, maxFactor));
            var point = e.GetPosition(this);
            Left -= point.X * (factor - 1);
            Top -= point.Y * (factor - 1);
            Width *= factor;
            Height *= factor;
            e.Handled = true;
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        SourceInitialized += (_, _) =>
        {
            var width = (int)Math.Round(Width * scale);
            var height = (int)Math.Round(Height * scale);
            Native.SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1),
                area.Left + (area.Width - width) / 2, area.Top + (area.Height - height) / 2,
                width, height, 0x0010);
        };
    }
}
