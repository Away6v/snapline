using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Markup;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using System.Windows.Shapes;

namespace SnapLine;

internal sealed partial class CaptureEditorWindow : Window
{
    private enum Tool { View, Pen, Arrow, Rectangle, Text, Mosaic, Number, Crop }
    private const int MaxHistoryEntries = 20;
    private const long MaxHistoryBytes = 256L * 1024 * 1024;
    private readonly CaptureLibrary library;
    private readonly Capture capture;
    private readonly Canvas canvas = new() { Background = Brushes.Transparent, ClipToBounds = true };
    private readonly Image image = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly ScaleTransform zoomTransform = new(1, 1);
    private readonly ScrollViewer scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = AppTheme.Soft };
    private readonly List<BitmapSource> history = new();
    private readonly ComboBox colors = new() { Width = 84, Margin = new Thickness(4, 0, 4, 0) };
    private readonly ComboBox widths = new() { Width = 64, Margin = new Thickness(4, 0, 4, 0) };
    private readonly ComboBox sizes = new() { Width = 64, Margin = new Thickness(4, 0, 4, 0) };
    private bool updatingZoomSlider;
    private readonly Slider zoomSlider = new() { Minimum = -5.643856189774724, Maximum = 3, Width = 112, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
    private readonly TextBlock zoomLabel = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 48, TextAlignment = TextAlignment.Center };
    private readonly Button undoButton = new();
    private readonly Button redoButton = new();
    private readonly Button saveButton = new();
    private readonly Popup savePopup = new();
    private readonly WrapPanel optionBar = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly Dictionary<Tool, StackPanel> styleGroups = new();
    private readonly ComboBox rectangleShapes = new();
    private readonly ComboBox mosaicStyles = new();
    private readonly ToggleButton shapeFill = new();
    private readonly RadioButton shapeDashed = new();
    private readonly RadioButton penDashed = new();
    private readonly DockPanel root = new() { Margin = new Thickness(12) };
    private bool closeAfterSave;
    private int index;
    private BitmapSource? saved;
    private Tool tool = Tool.View;
    private Point start;
    private readonly List<Point> points = new();
    private Shape? preview;
    private bool drawing;
    private bool saving;
    private bool allowClose;
    private double zoom = 1;
    private int nextNumber = 1;
    private BitmapSource Current => history[index];
    private bool Dirty => !ReferenceEquals(saved, Current);

    internal CaptureEditorWindow(CaptureLibrary library, Capture capture, BitmapSource bitmap)
    {
        this.library = library;
        this.capture = capture;
        history.Add(bitmap);
        saved = bitmap;
        Title = "SnapLine Editor";
        Width = 1050; Height = 720; MinWidth = 650; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(250, 248, 243));
        FontFamily = new FontFamily("Microsoft YaHei UI");
        BuildEditorLayout();
        canvas.LayoutTransform = zoomTransform;
        canvas.Children.Add(image);
        scroll.Content = canvas;
        canvas.MouseLeftButtonDown += MouseDownOnCanvas;
        canvas.MouseMove += MouseMoveOnCanvas;
        canvas.MouseLeftButtonUp += MouseUpOnCanvas;
        canvas.LostMouseCapture += (_, _) => { if (drawing) CancelDrawing(); };
        PreviewKeyDown += OnKeyDown;
        Closing += OnClosing;
        Loaded += (_, _) => { ShowBitmap(); Fit(); };
        PreviewMouseDown += OnEditorMouseDown;
    }

    private static Style CreateToolStyle()
    {
        const string xaml = """
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="RadioButton">
                <Setter Property="Foreground" Value="#273644"/><Setter Property="Padding" Value="10,7"/>
                <Setter Property="Cursor" Value="Hand"/><Setter Property="FocusVisualStyle" Value="{x:Null}"/>
                <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="RadioButton">
                    <Border x:Name="Frame" Background="Transparent" BorderBrush="Transparent" BorderThickness="0" CornerRadius="5" Padding="{TemplateBinding Padding}">
                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="Background" Value="#F1EBE2"/></Trigger>
                        <Trigger Property="IsPressed" Value="True"><Setter TargetName="Frame" Property="Background" Value="#E8E0D5"/></Trigger>
                        <Trigger Property="IsChecked" Value="True"><Setter TargetName="Frame" Property="Background" Value="Transparent"/><Setter Property="Foreground" Value="#C96B50"/><Setter Property="FontWeight" Value="SemiBold"/></Trigger>
                        <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="#C96B50"/></Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate></Setter.Value></Setter>
            </Style>
            """;
        return (Style)XamlReader.Parse(xaml);
    }
    private static Style CreateComboStyle()
    {
        const string xaml = """
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ComboBox">
                <Setter Property="Height" Value="32"/><Setter Property="Foreground" Value="#273644"/>
                <Setter Property="Background" Value="#FFFCF7"/><Setter Property="BorderBrush" Value="#E6DFD5"/>
                <Setter Property="ItemContainerStyle"><Setter.Value><Style TargetType="ComboBoxItem">
                    <Setter Property="Padding" Value="8,6"/><Setter Property="Background" Value="Transparent"/>
                    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBoxItem">
                        <Border x:Name="Item" Background="{TemplateBinding Background}" CornerRadius="6" Padding="{TemplateBinding Padding}"><ContentPresenter/></Border>
                        <ControlTemplate.Triggers><Trigger Property="IsHighlighted" Value="True"><Setter TargetName="Item" Property="Background" Value="#F1EBE2"/></Trigger></ControlTemplate.Triggers>
                    </ControlTemplate></Setter.Value></Setter>
                </Style></Setter.Value></Setter>
                <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox">
                    <Grid>
                        <ToggleButton x:Name="Toggle" Focusable="False" ClickMode="Press" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                                      IsChecked="{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}">
                            <ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="7"/></ControlTemplate></ToggleButton.Template>
                        </ToggleButton>
                        <ContentPresenter Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" Margin="8,0,22,0" VerticalAlignment="Center" IsHitTestVisible="False"/>
                        <Path Data="M 0,0 L 4,4 L 8,0" Stroke="#708090" StrokeThickness="1.3" HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,8,0" IsHitTestVisible="False"/>
                        <Popup x:Name="PART_Popup" IsOpen="{TemplateBinding IsDropDownOpen}" Placement="Bottom" AllowsTransparency="True" Focusable="False">
                            <Border MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}" Background="White" BorderBrush="#E6DFD5" BorderThickness="1" CornerRadius="8" Padding="4" Margin="0,4,0,0">
                                <ScrollViewer MaxHeight="200" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled"><ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/></ScrollViewer>
                            </Border>
                        </Popup>
                    </Grid>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Toggle" Property="Background" Value="#F1EBE2"/></Trigger>
                        <Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="Toggle" Property="BorderBrush" Value="#C96B50"/></Trigger>
                        <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.5"/></Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate></Setter.Value></Setter>
            </Style>
            """;
        return (Style)XamlReader.Parse(xaml);
    }
    private void UpdateToolOptions()
    {
        UpdateEditorToolState();
        UpdateCropButtons();
    }
    private void UpdateHistoryButtons()
    {
        undoButton.IsEnabled = (textEditing ? textEditor?.CanUndo == true : index > 0) && !saving;
        redoButton.IsEnabled = (textEditing ? textEditor?.CanRedo == true : index + 1 < history.Count) && !saving;
        undoButton.Opacity = undoButton.IsEnabled ? 1 : 0.4;
        redoButton.Opacity = redoButton.IsEnabled ? 1 : 0.4;
    }
    private void CreateSaveMenu()
    {
        var panel = new StackPanel { Margin = new Thickness(5) };
        Button Option(string label, bool replace)
        {
            var button = new Button { Content = label, Style = ThemeButtons.Secondary, HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 1, 0, 1), MinWidth = 165 };
            button.Click += (_, _) => { var close = closeAfterSave; savePopup.IsOpen = false; _ = SaveAsync(replace, close); };
            panel.Children.Add(button); return button;
        }
        var first = Option(Words.T("更新当前截图", "Update screenshot"), true);
        Option(Words.T("新增截图", "Add screenshot"), false);
        panel.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { savePopup.IsOpen = false; saveButton.Focus(); e.Handled = true; } };
        var border = new Border { Background = AppTheme.Surface, BorderBrush = AppTheme.Outline, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11), Child = panel, Margin = new Thickness(8),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 14, ShadowDepth = 2, Opacity = 0.15 } };
        savePopup.PlacementTarget = saveButton; savePopup.Placement = PlacementMode.Bottom; savePopup.AllowsTransparency = true;
        savePopup.StaysOpen = false; savePopup.Child = border;
        savePopup.Opened += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => first.Focus()));
        savePopup.Closed += (_, _) => closeAfterSave = false;
    }
    private void OpenSaveMenu(bool closeAfter)
    {
        if (saving || cropOverlay != null) return;
        CommitText();
        if (savePopup.IsOpen) { savePopup.IsOpen = false; if (!closeAfter) return; }
        closeAfterSave = closeAfter;
        savePopup.IsOpen = true;
    }
    private Brush customColor = Brushes.Red;
    private Brush ColorBrush() => colors.SelectedIndex switch { 6 => customColor, 1 => new SolidColorBrush(Color.FromRgb(65, 67, 68)), 2 => Brushes.White, 3 => new SolidColorBrush(Color.FromRgb(232, 161, 54)), 4 => new SolidColorBrush(Color.FromRgb(62, 137, 204)), 5 => new SolidColorBrush(Color.FromRgb(84, 151, 150)), _ => new SolidColorBrush(Color.FromRgb(221, 107, 80)) };
    private double LineWidth => strokeSlider.Value;
    private void ShowBitmap()
    {
        image.Source = Current; image.Width = canvas.Width = Current.PixelWidth; image.Height = canvas.Height = Current.PixelHeight; UpdateEditorStatus();
    }
    private void SetZoom(double value)
    {
        zoom = Math.Clamp(value, 0.02, 8);
        zoomTransform.ScaleX = zoomTransform.ScaleY = zoom;
        zoomLabel.Text = $"{zoom * 100:0}%";
        updatingZoomSlider = true; zoomSlider.Value = Math.Log(zoom, 2); updatingZoomSlider = false;
        RefreshCropOverlay();
    }
    private void Fit() => SetZoom(Math.Min(1, Math.Min(Math.Max(1, scroll.ViewportWidth - 20) / Current.PixelWidth, Math.Max(1, scroll.ViewportHeight - 20) / Current.PixelHeight)));
    private Point Position(MouseEventArgs e)
    {
        var p = e.GetPosition(canvas);
        return new Point(Math.Clamp(p.X, 0, Current.PixelWidth), Math.Clamp(p.Y, 0, Current.PixelHeight));
    }
    private void MouseDownOnCanvas(object sender, MouseButtonEventArgs e)
    {
        if (saving || cropOverlay != null || tool == Tool.View) return;
        if (e.OriginalSource is DependencyObject source && IsTextAnnotationElement(source)) return;
        if (textAnnotations.Count > 0 && tool == Tool.Crop) CommitText();
        start = Position(e);
        drawing = true; points.Clear(); points.Add(start); canvas.CaptureMouse();
        preview = new System.Windows.Shapes.Path { Stroke = DrawingBrush(), StrokeThickness = LineWidth, StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false };
        if (tool == Tool.Pen && penDashed.IsChecked == true || tool == Tool.Rectangle && shapeDashed.IsChecked == true) preview.StrokeDashArray = new DoubleCollection { 3, 2 };
        if (tool == Tool.Rectangle && shapeFill.IsChecked == true) preview.Fill = DrawingBrush();
        if (tool == Tool.Mosaic && mosaicStyles.SelectedIndex == 1) { preview.Stroke = AppTheme.AccentDark; preview.StrokeThickness = Math.Max(12, LineWidth * 3); preview.Opacity = .45; }
        if (tool == Tool.Crop || tool == Tool.Text || tool == Tool.Mosaic && mosaicStyles.SelectedIndex == 0) { preview.Stroke = AppTheme.AccentDark; preview.StrokeThickness = Math.Max(1, 1 / zoom); preview.StrokeDashArray = new DoubleCollection { 4, 3 }; }
        if (tool == Tool.Arrow && arrowStyles.SelectedIndex == 1) preview.Fill = DrawingBrush();
        canvas.Children.Add(preview); e.Handled = true;
    }
    private void MouseMoveOnCanvas(object sender, MouseEventArgs e)
    {
        if (!drawing || preview is not System.Windows.Shapes.Path path) return;
        var end = Position(e);
        if (tool == Tool.Pen || tool == Tool.Mosaic && mosaicStyles.SelectedIndex == 1 || tool == Tool.Crop && cropStyles.SelectedIndex == 2) points.Add(end);
        path.Data = GeometryFor(end);
    }
    private Geometry GeometryFor(Point end)
    {
        if (tool is Tool.Rectangle or Tool.Text || tool == Tool.Mosaic && mosaicStyles.SelectedIndex == 0 || tool == Tool.Crop && cropStyles.SelectedIndex != 2)
        {
            if (tool == Tool.Rectangle && (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                var dx = end.X - start.X;
                var dy = end.Y - start.Y;
                var maxWidth = dx < 0 ? start.X : Current.PixelWidth - start.X;
                var maxHeight = dy < 0 ? start.Y : Current.PixelHeight - start.Y;
                var size = Math.Min(Math.Max(Math.Abs(dx), Math.Abs(dy)), Math.Min(maxWidth, maxHeight));
                end = new Point(start.X + (dx < 0 ? -size : size), start.Y + (dy < 0 ? -size : size));
            }
            var rect = new Rect(start, end);
            if (tool == Tool.Crop && cropStyles.SelectedIndex == 1 || tool == Tool.Rectangle && rectangleShapes.SelectedIndex == 2) return new EllipseGeometry(rect);
            if (tool == Tool.Rectangle && rectangleShapes.SelectedIndex == 1)
            {
                var radius = Math.Min(12, Math.Min(rect.Width, rect.Height) / 2);
                return new RectangleGeometry(rect, radius, radius);
            }
            return new RectangleGeometry(rect);
        }
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            if (tool == Tool.Pen || tool == Tool.Crop || tool == Tool.Mosaic)
            {
                context.BeginFigure(start, tool == Tool.Crop, tool == Tool.Crop);
                foreach (var point in points) context.LineTo(point, true, false);
            }
            else
            {
                var vector = end - start;
                if (vector.Length > 0.1)
                {
                    var length = Math.Min(Math.Max(LineWidth * 4, 12), vector.Length * .8);
                    vector.Normalize(); var side = new Vector(-vector.Y, vector.X);
                    context.BeginFigure(start, false, false); context.LineTo(end, true, false);
                    var left = end - vector * length + side * length * .45; var right = end - vector * length - side * length * .45;
                    context.BeginFigure(left, arrowStyles.SelectedIndex == 1, arrowStyles.SelectedIndex == 1);
                    context.LineTo(end, true, false); context.LineTo(right, true, false);
                    if (arrowStyles.SelectedIndex == 2)
                    {
                        context.BeginFigure(start + vector * length + side * length * .45, false, false);
                        context.LineTo(start, true, false); context.LineTo(start + vector * length - side * length * .45, true, false);
                    }
                }
            }
        }
        geometry.Freeze(); return geometry;
    }
    private Brush DrawingBrush()
    {
        var brush = ColorBrush().Clone();
        if (tool == Tool.Pen) brush.Opacity = penStyles.SelectedIndex switch { 0 => .8, 2 => .35, _ => 1 };
        brush.Freeze(); return brush;
    }
    private void MouseUpOnCanvas(object sender, MouseButtonEventArgs e)
    {
        if (!drawing) return;
        var end = Position(e); if (tool == Tool.Pen || tool == Tool.Mosaic && mosaicStyles.SelectedIndex == 1 || tool == Tool.Crop && cropStyles.SelectedIndex == 2) points.Add(end);
        var geometry = GeometryFor(end);
        CancelDrawing();
        if (tool == Tool.Crop) BeginCrop(end);
        else if (tool == Tool.Text) BeginText(new Rect(start, end));
        else if (tool == Tool.Mosaic)
        {
            if (mosaicStyles.SelectedIndex == 1) Commit(RenderMosaicBrush(geometry));
            else
            {
                var rectangle = new Rect(start, end);
                if (rectangle.Width > 1 && rectangle.Height > 1) Commit(RenderMosaic(rectangle));
            }
        }
        else if (tool == Tool.Number) Commit(RenderNumber(start, nextNumber++));
        else if ((end - start).Length > 1 || tool == Tool.Pen)
        {
            var pen = new Pen(DrawingBrush(), LineWidth) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            if (tool == Tool.Pen && penDashed.IsChecked == true || tool == Tool.Rectangle && shapeDashed.IsChecked == true) pen.DashStyle = new DashStyle(new double[] { 3, 2 }, 0);
            var fill = tool == Tool.Arrow && arrowStyles.SelectedIndex == 1 || tool == Tool.Rectangle && shapeFill.IsChecked == true ? DrawingBrush() : null;
            Commit(Render(context => context.DrawGeometry(fill, pen, geometry)));
        }
        e.Handled = true;
    }
    private void CancelDrawing() { drawing = false; if (preview != null) canvas.Children.Remove(preview); preview = null; if (canvas.IsMouseCaptured) canvas.ReleaseMouseCapture(); }
    private BitmapSource Render(Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) { context.DrawImage(Current, new Rect(0, 0, Current.PixelWidth, Current.PixelHeight)); draw(context); }
        var result = new RenderTargetBitmap(Current.PixelWidth, Current.PixelHeight, 96, 96, PixelFormats.Pbgra32); result.Render(visual); result.Freeze(); return result;
    }
    private BitmapSource RenderMosaic(Rect rectangle)
    {
        var bounds = new Int32Rect(Math.Max(0, (int)Math.Floor(rectangle.X)), Math.Max(0, (int)Math.Floor(rectangle.Y)),
            Math.Min(Current.PixelWidth - Math.Max(0, (int)Math.Floor(rectangle.X)), Math.Max(1, (int)Math.Ceiling(rectangle.Width))),
            Math.Min(Current.PixelHeight - Math.Max(0, (int)Math.Floor(rectangle.Y)), Math.Max(1, (int)Math.Ceiling(rectangle.Height))));
        var cropped = new CroppedBitmap(Current, bounds);
        var block = 10.0;
        var small = new TransformedBitmap(cropped, new ScaleTransform(Math.Max(1.0 / cropped.PixelWidth, 1.0 / block), Math.Max(1.0 / cropped.PixelHeight, 1.0 / block)));
        small.Freeze();
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(Current, new Rect(0, 0, Current.PixelWidth, Current.PixelHeight));
            context.DrawImage(small, new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        }
        var result = new RenderTargetBitmap(Current.PixelWidth, Current.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual); result.Freeze(); return result;
    }
    private BitmapSource RenderMosaicBrush(Geometry path)
    {
        var width = Math.Max(12, LineWidth * 3);
        var mask = path.GetWidenedPathGeometry(new Pen(Brushes.Black, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round });
        var combined = new CombinedGeometry(GeometryCombineMode.Union, mask, new EllipseGeometry(start, width / 2, width / 2));
        var small = new TransformedBitmap(Current, new ScaleTransform(Math.Max(1.0 / Current.PixelWidth, .1), Math.Max(1.0 / Current.PixelHeight, .1)));
        small.Freeze();
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        using (var context = visual.RenderOpen())
        {
            var bounds = new Rect(0, 0, Current.PixelWidth, Current.PixelHeight);
            context.DrawImage(Current, bounds);
            context.PushClip(combined);
            context.DrawImage(small, bounds);
            context.Pop();
        }
        var result = new RenderTargetBitmap(Current.PixelWidth, Current.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual); result.Freeze(); return result;
    }
    private BitmapSource RenderNumber(Point point, int number)
    {
        var radius = Math.Max(14, LineWidth * 2.5);
        var typeface = new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var formatted = new FormattedText(number.ToString(CultureInfo.CurrentCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, radius, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        return Render(context =>
        {
            context.DrawEllipse(DrawingBrush(), null, point, radius, radius);
            context.DrawText(formatted, new Point(point.X - formatted.Width / 2, point.Y - formatted.Height / 2));
        });
    }

    private void Commit(BitmapSource result)
    {
        if (index + 1 < history.Count) history.RemoveRange(index + 1, history.Count - index - 1);
        history.Add(result); index++;
        TrimHistory();
        ShowBitmap(); UpdateHistoryButtons();
    }
    private void TrimHistory()
    {
        bool RemovedOne()
        {
            var candidate = Enumerable.Range(0, history.Count).FirstOrDefault(position => position != index && !ReferenceEquals(history[position], saved));
            if (candidate == index || ReferenceEquals(history[candidate], saved)) return false;
            history.RemoveAt(candidate);
            if (candidate < index) index--;
            return true;
        }
        long HistoricalBytes() => history.Where((bitmap, position) => position != index && !ReferenceEquals(bitmap, saved))
            .Sum(bitmap => (long)bitmap.PixelWidth * bitmap.PixelHeight * 4);
        while (history.Count > MaxHistoryEntries || HistoricalBytes() > MaxHistoryBytes)
        {
            if (!RemovedOne()) break;
        }
        if (HistoricalBytes() > MaxHistoryBytes)
            dirtyLabel.Text = Words.T("撤销历史受图片大小限制", "Undo history is limited by image size.");
    }
    private void Undo() { if (textEditing && textEditor != null) { if (textEditor.CanUndo) textEditor.Undo(); UpdateHistoryButtons(); return; } CancelCrop(); if (index > 0) { index--; ShowBitmap(); UpdateHistoryButtons(); } }
    private void Redo() { if (textEditing && textEditor != null) { if (textEditor.CanRedo) textEditor.Redo(); UpdateHistoryButtons(); return; } CancelCrop(); if (index + 1 < history.Count) { index++; ShowBitmap(); UpdateHistoryButtons(); } }
    private async Task SaveAsync(bool replace, bool closeAfter)
    {
        if (saving) return;
        saving = true;
        root.IsEnabled = false;
        var imageToSave = Current;
        try
        {
            await library.SaveEditedAsync(capture, imageToSave, replace);
            saved = imageToSave; UpdateEditorStatus();
            if (closeAfter) { allowClose = true; Close(); }
        }
        catch (Exception ex) { MessageBox.Show(this, Words.T("无法保存截图：", "Cannot save screenshot: ") + ex.Message, "SnapLine", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { saving = false; root.IsEnabled = true; UpdateHistoryButtons(); }
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        if (saving) { e.Cancel = true; return; }
        if (!Dirty && !HasTextAnnotations()) return;
        var choice = ConfirmClose();
        if (choice == MessageBoxResult.No) return;
        e.Cancel = true;
        if (choice == MessageBoxResult.Yes) { CommitText(); CancelCrop(); Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => OpenSaveMenu(true))); }
    }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (savePopup.IsOpen) savePopup.IsOpen = false;
            DeactivateText();
            CancelCrop();
            CancelDrawing();
            toolButtons[Tool.View].IsChecked = true;
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter && cropOverlay != null && Keyboard.Modifiers == ModifierKeys.None) { ApplyCrop(); e.Handled = true; return; }
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || saving) return;
        if (e.Key == Key.S) { OpenSaveMenu(false); e.Handled = true; return; }
        if (textEditor?.IsKeyboardFocusWithin == true) return;
        if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Shift) != 0) Redo();
        else if (e.Key == Key.Z) Undo(); else if (e.Key == Key.Y) Redo(); else return;
        e.Handled = true;
    }
}
