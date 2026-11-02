using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace SnapLine;

internal sealed partial class CaptureEditorWindow
{
    private readonly ComboBox penStyles = new() { Width = 122 };
    private readonly ComboBox arrowStyles = new() { Width = 122 };
    private readonly ComboBox cropStyles = new() { Width = 122 };
    private readonly ComboBox fontNames = new() { Width = 145 };
    private readonly ToggleButton boldText = new();
    private readonly ToggleButton italicText = new();
    private Button? cropApply;
    private Button? cropCancel;
    private sealed class TextAnnotation
    {
        public required Border Frame { get; init; }
        public required TextBox Editor { get; init; }
        public required Thumb MoveSurface { get; init; }
        public required System.Windows.Shapes.Rectangle Outline { get; init; }
        public bool Resized { get; set; }
        public double BaseHeight { get; set; }
    }
    private readonly List<TextAnnotation> textAnnotations = new();
    private TextAnnotation? activeTextAnnotation;
    private Border? textFrame;
    private TextBox? textEditor;
    private Thumb? textMoveSurface;
    private bool textEditing;
    private bool textFrameResized;
    private double textFrameBaseHeight;
    private Canvas? cropOverlay;
    private Rect cropRect;
    private List<Point>? cropPoints;
    private int cropKind;
    private Point cropDragStart;
    private Rect cropDragRect;
    private List<Point>? cropDragPoints;

    private static Style CreateTextToggleStyle()
    {
        const string xaml = """
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ToggleButton">
                <Setter Property="Width" Value="32"/><Setter Property="Height" Value="32"/><Setter Property="Foreground" Value="#273644"/>
                <Setter Property="FocusVisualStyle" Value="{x:Null}"/><Setter Property="Cursor" Value="Hand"/>
                <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ToggleButton">
                    <Border x:Name="Frame" Background="#F5F9FC" BorderBrush="Transparent" BorderThickness="1" CornerRadius="7"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="Background" Value="#E8F5FE"/></Trigger>
                        <Trigger Property="IsChecked" Value="True"><Setter TargetName="Frame" Property="Background" Value="#D5EDFC"/><Setter Property="Foreground" Value="#0B78B4"/></Trigger>
                        <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="#0B78B4"/></Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate></Setter.Value></Setter>
            </Style>
            """;
        return (Style)XamlReader.Parse(xaml);
    }

    private void OnEditorMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (textFrame == null || !textEditing || e.OriginalSource is not DependencyObject source) return;
        if (source == textFrame || source is Visual visual && visual.IsDescendantOf(textFrame)) return;
        if (source == canvas || source is Visual target && (target.IsDescendantOf(canvas) || target.IsDescendantOf(scroll)))
        {
            DeactivateText();
        }
    }

    private static Thumb MakeThumb(Cursor cursor, bool visible)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, visible ? AppTheme.Surface : Brushes.Transparent);
        border.SetValue(Border.BorderBrushProperty, visible ? AppTheme.AccentDark : Brushes.Transparent);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(visible ? 1 : 0));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        return new Thumb { Cursor = cursor, Template = new ControlTemplate(typeof(Thumb)) { VisualTree = border } };
    }

    private void BeginText(Rect rectangle)
    {
        DeactivateText();
        SaveTextAnnotationState();
        TextAnnotation? annotation = null;
        var fontSize = sizes.SelectedItem is int selectedSize ? selectedSize : 24;
        var fontName = fontNames.SelectedItem as string ?? "Microsoft YaHei UI";
        var lineHeight = TextLineHeight(fontName, fontSize);
        var left = Math.Clamp(rectangle.Left, 0, Math.Max(0, Current.PixelWidth - 1));
        var top = Math.Clamp(rectangle.Top, 0, Math.Max(0, Current.PixelHeight - lineHeight));
        var width = rectangle.Width < 4 ? Math.Min(220, Current.PixelWidth - left) : Math.Min(rectangle.Width, Current.PixelWidth - left);
        var height = rectangle.Height < 4 ? lineHeight : Math.Max(lineHeight, rectangle.Height);
        height = Math.Min(height, Current.PixelHeight - top);
        if (width < 1 || height < 1) return;
        textFrameResized = rectangle.Height >= 4;
        textFrameBaseHeight = height;
        var grid = new Grid();
        textEditor = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), Padding = new Thickness(2), VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        textEditor.TextChanged += (_, _) => { GrowTextFrame(); UpdateHistoryButtons(); };
        grid.Children.Add(textEditor);
        var outline = new System.Windows.Shapes.Rectangle { Stroke = AppTheme.Ink, StrokeThickness = 1 / zoom,
            StrokeDashArray = new DoubleCollection { 3, 2 }, SnapsToDevicePixels = true, IsHitTestVisible = false };
        grid.Children.Add(outline);
        var moveEdges = new[] { Dock.Top, Dock.Bottom, Dock.Left, Dock.Right };
        foreach (var edge in moveEdges)
        {
            var move = MakeThumb(Cursors.SizeAll, false);
            if (edge is Dock.Top or Dock.Bottom)
            {
                move.Height = 8 / zoom; move.VerticalAlignment = edge == Dock.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
                move.Margin = new Thickness(8 / zoom, 0, 8 / zoom, 0);
            }
            else
            {
                move.Width = 8 / zoom; move.HorizontalAlignment = edge == Dock.Left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                move.Margin = new Thickness(0, 8 / zoom, 0, 8 / zoom);
            }
            move.ToolTip = Words.T("拖动文字框", "Move text box");
            move.DragDelta += (_, e) =>
            {
                if (annotation != null) SelectTextAnnotation(annotation);
                MoveTextFrame(e.HorizontalChange, e.VerticalChange);
            };
            grid.Children.Add(move);
        }
        var resize = MakeThumb(Cursors.SizeNWSE, false);
        resize.Width = resize.Height = 12 / zoom; resize.HorizontalAlignment = HorizontalAlignment.Right; resize.VerticalAlignment = VerticalAlignment.Bottom;
        grid.Children.Add(resize);
        textMoveSurface = MakeThumb(Cursors.SizeAll, false);
        textMoveSurface.Visibility = Visibility.Collapsed;
        textMoveSurface.ToolTip = Words.T("拖动文字，双击编辑", "Drag text, double-click to edit");
        textMoveSurface.DragStarted += (_, _) =>
        {
            if (annotation == null) return;
            SelectTextAnnotation(annotation);
            annotation.Outline.Visibility = Visibility.Visible;
        };
        textMoveSurface.DragCompleted += (_, _) =>
        {
            if (annotation != null && (!ReferenceEquals(activeTextAnnotation, annotation) || !textEditing))
                annotation.Outline.Visibility = Visibility.Collapsed;
        };
        textMoveSurface.DragDelta += (_, e) => MoveTextFrame(e.HorizontalChange, e.VerticalChange);
        textMoveSurface.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2 || annotation == null) return;
            toolButtons[Tool.Text].IsChecked = true;
            SelectTextAnnotation(annotation);
            if (textEditor == null || textMoveSurface == null) return;
            textEditing = true; textEditor.IsReadOnly = false; textMoveSurface.Visibility = Visibility.Collapsed;
            annotation.Outline.Visibility = Visibility.Visible;
            textEditor.Focus(); textEditor.CaretIndex = textEditor.Text.Length; UpdateHistoryButtons(); e.Handled = true;
        };
        grid.Children.Add(textMoveSurface);
        textFrame = new Border { Width = width, Height = height, Child = grid, Background = Brushes.Transparent };
        Canvas.SetLeft(textFrame, left); Canvas.SetTop(textFrame, top); canvas.Children.Add(textFrame);
        annotation = new TextAnnotation { Frame = textFrame, Editor = textEditor, MoveSurface = textMoveSurface, Outline = outline, Resized = textFrameResized, BaseHeight = textFrameBaseHeight };
        textAnnotations.Add(annotation); activeTextAnnotation = annotation;
        resize.DragDelta += (_, e) =>
        {
            if (annotation != null) SelectTextAnnotation(annotation);
            if (textFrame == null || textEditor == null) return;
            textFrameResized = true;
            textFrame.Width = Math.Clamp(textFrame.Width + e.HorizontalChange, Math.Min(30, Current.PixelWidth - Canvas.GetLeft(textFrame)), Current.PixelWidth - Canvas.GetLeft(textFrame));
            var minimumHeight = Math.Min(TextLineHeight(textEditor.FontFamily.Source, textEditor.FontSize), Current.PixelHeight - Canvas.GetTop(textFrame));
            textFrameBaseHeight = Math.Clamp(textFrameBaseHeight + e.VerticalChange, minimumHeight, Current.PixelHeight - Canvas.GetTop(textFrame));
            GrowTextFrame();
        };
        textEditing = true;
        UpdateTextAppearance(); UpdateHistoryButtons();
        textEditor.Focus();
    }

    private void SaveTextAnnotationState()
    {
        if (activeTextAnnotation == null) return;
        activeTextAnnotation.Resized = textFrameResized;
        activeTextAnnotation.BaseHeight = textFrameBaseHeight;
    }

    private void SelectTextAnnotation(TextAnnotation annotation)
    {
        if (ReferenceEquals(activeTextAnnotation, annotation)) return;
        DeactivateText();
        SaveTextAnnotationState();
        activeTextAnnotation = annotation;
        textFrame = annotation.Frame; textEditor = annotation.Editor; textMoveSurface = annotation.MoveSurface;
        textFrameResized = annotation.Resized; textFrameBaseHeight = annotation.BaseHeight;
        textEditing = false;
    }

    private bool IsTextAnnotationElement(DependencyObject source)
    {
        foreach (var annotation in textAnnotations)
            if (source == annotation.Frame || source is Visual visual && visual.IsDescendantOf(annotation.Frame)) return true;
        return false;
    }

    private bool HasTextAnnotations()
    {
        foreach (var annotation in textAnnotations)
            if (!string.IsNullOrWhiteSpace(annotation.Editor.Text)) return true;
        return false;
    }

    private void MoveTextFrame(double horizontal, double vertical)
    {
        if (textFrame == null) return;
        Canvas.SetLeft(textFrame, Math.Clamp(Canvas.GetLeft(textFrame) + horizontal, 0, Math.Max(0, Current.PixelWidth - textFrame.Width)));
        Canvas.SetTop(textFrame, Math.Clamp(Canvas.GetTop(textFrame) + vertical, 0, Math.Max(0, Current.PixelHeight - textFrame.Height)));
    }

    private void DeactivateText()
    {
        if (textEditor == null || textMoveSurface == null || !textEditing) return;
        if (string.IsNullOrWhiteSpace(textEditor.Text)) { CancelText(); return; }
        textEditing = false; textEditor.IsReadOnly = true;
        if (activeTextAnnotation != null) activeTextAnnotation.Outline.Visibility = Visibility.Collapsed;
        textMoveSurface.Visibility = Visibility.Visible;
        Keyboard.ClearFocus(); UpdateHistoryButtons();
    }

    private static double TextLineHeight(string font, double size)
    {
        var sample = new FormattedText("国Ag", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(font), size, Brushes.Black, 1.0);
        return Math.Ceiling(sample.Height) + 4;
    }

    private void GrowTextFrame(bool resetEmpty = false)
    {
        if (textEditor == null || textFrame == null) return;
        var formatted = new FormattedText(string.IsNullOrEmpty(textEditor.Text) ? "国Ag" : textEditor.Text.EndsWith("\n", StringComparison.Ordinal) ? textEditor.Text + "国" : textEditor.Text, CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, new Typeface(textEditor.FontFamily, textEditor.FontStyle, textEditor.FontWeight, FontStretches.Normal),
            textEditor.FontSize, textEditor.Foreground, 1.0) { MaxTextWidth = Math.Max(1, textFrame.Width - 4), Trimming = TextTrimming.None };
        var required = Math.Ceiling(formatted.Height) + 4;
        var available = Current.PixelHeight - Canvas.GetTop(textFrame);
        if (resetEmpty && !textFrameResized) textFrameBaseHeight = TextLineHeight(textEditor.FontFamily.Source, textEditor.FontSize);
        textFrame.Height = Math.Min(available, Math.Max(textFrameBaseHeight, required));
    }

    private void UpdateTextAppearance()
    {
        if (!textEditing || textEditor == null || fontNames.SelectedItem is not string font || sizes.SelectedItem is not int size) return;
        textEditor.FontFamily = new FontFamily(font);
        textEditor.FontSize = size;
        textEditor.Foreground = ColorBrush();
        textEditor.FontWeight = boldText.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;
        textEditor.FontStyle = italicText.IsChecked == true ? FontStyles.Italic : FontStyles.Normal;
        GrowTextFrame(true);
    }

    private void CommitText()
    {
        if (textAnnotations.Count == 0) return;
        BitmapSource? result = null;
        if (HasTextAnnotations())
        {
            result = Render(context =>
            {
                foreach (var annotation in textAnnotations)
                {
                    var editor = annotation.Editor;
                    var frame = annotation.Frame;
                    if (string.IsNullOrWhiteSpace(editor.Text)) continue;
                    var left = Canvas.GetLeft(frame) + 2;
                    var top = Canvas.GetTop(frame) + 2;
                    var width = Math.Max(1, frame.Width - 4);
                    var height = Math.Max(1, frame.Height - 4);
                    var formatted = new FormattedText(editor.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                        new Typeface(editor.FontFamily, editor.FontStyle, editor.FontWeight, FontStretches.Normal), editor.FontSize, editor.Foreground, 1.0)
                        { MaxTextWidth = width, MaxTextHeight = height, Trimming = TextTrimming.None };
                    context.PushClip(new RectangleGeometry(new Rect(left, top, width, height)));
                    context.DrawText(formatted, new Point(left, top));
                    context.Pop();
                }
            });
        }
        foreach (var annotation in textAnnotations) canvas.Children.Remove(annotation.Frame);
        textAnnotations.Clear(); activeTextAnnotation = null;
        textFrame = null; textEditor = null; textMoveSurface = null; textEditing = false;
        if (result != null) Commit(result); else UpdateHistoryButtons();
    }
    private void CancelText()
    {
        if (activeTextAnnotation != null)
        {
            canvas.Children.Remove(activeTextAnnotation.Frame);
            textAnnotations.Remove(activeTextAnnotation);
        }
        activeTextAnnotation = null;
        textFrame = null; textEditor = null; textMoveSurface = null; textEditing = false; UpdateHistoryButtons();
    }

    private void UpdateCropButtons()
    {
        if (cropApply != null) cropApply.Visibility = cropOverlay != null && tool == Tool.Crop ? Visibility.Visible : Visibility.Collapsed;
        if (cropCancel != null) cropCancel.Visibility = cropOverlay != null && tool == Tool.Crop ? Visibility.Visible : Visibility.Collapsed;
        saveButton.IsEnabled = cropOverlay == null && !saving;
        saveButton.Opacity = saveButton.IsEnabled ? 1 : 0.5;
        saveButton.ToolTip = cropOverlay == null ? Words.T("保存", "Save") : Words.T("请先完成或取消裁剪", "Apply or cancel the crop first");
        ToolTipService.SetShowOnDisabled(saveButton, true);
    }
    private void BeginCrop(Point end)
    {
        cropKind = cropStyles.SelectedIndex;
        cropPoints = cropKind == 2 ? new List<Point>(points) : null;
        if (cropPoints != null)
        {
            if (cropPoints.Count < 3) return;
            var bounds = new Rect(cropPoints[0], cropPoints[0]);
            foreach (var point in cropPoints) bounds.Union(point);
            cropRect = bounds;
        }
        else cropRect = new Rect(start, end);
        if (cropRect.Width < 1 || cropRect.Height < 1) { cropPoints = null; return; }
        cropOverlay = new Canvas { Width = Current.PixelWidth, Height = Current.PixelHeight };
        canvas.Children.Add(cropOverlay);
        RefreshCropOverlay(); UpdateCropButtons();
    }
    private Geometry CropGeometry()
    {
        if (cropKind == 0) return new RectangleGeometry(cropRect);
        if (cropKind == 1) return new EllipseGeometry(cropRect);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(cropPoints![0], true, true);
            for (var i = 1; i < cropPoints.Count; i++) context.LineTo(cropPoints[i], true, false);
        }
        geometry.Freeze(); return geometry;
    }
    private void RefreshCropOverlay()
    {
        if (cropOverlay == null) return;
        cropOverlay.Children.Clear();
        var outline = new System.Windows.Shapes.Path { Data = CropGeometry(), Stroke = AppTheme.AccentDark, StrokeThickness = 1.5 / zoom,
            StrokeDashArray = new DoubleCollection { 4, 3 }, Fill = new SolidColorBrush(Color.FromArgb(20, 56, 169, 232)), IsHitTestVisible = false };
        cropOverlay.Children.Add(outline);
        var move = MakeThumb(Cursors.SizeAll, false);
        move.Width = cropRect.Width; move.Height = cropRect.Height;
        Canvas.SetLeft(move, cropRect.Left); Canvas.SetTop(move, cropRect.Top); cropOverlay.Children.Add(move);
        move.DragStarted += (_, _) => { cropDragStart = Mouse.GetPosition(canvas); cropDragRect = cropRect; cropDragPoints = cropPoints == null ? null : new List<Point>(cropPoints); };
        move.DragDelta += (_, _) =>
        {
            var current = Mouse.GetPosition(canvas); var dx = Math.Clamp(current.X - cropDragStart.X, -cropDragRect.Left, Current.PixelWidth - cropDragRect.Right);
            var dy = Math.Clamp(current.Y - cropDragStart.Y, -cropDragRect.Top, Current.PixelHeight - cropDragRect.Bottom);
            cropRect = new Rect(cropDragRect.Left + dx, cropDragRect.Top + dy, cropDragRect.Width, cropDragRect.Height);
            if (cropDragPoints != null) { cropPoints = new List<Point>(); foreach (var p in cropDragPoints) cropPoints.Add(new Point(p.X + dx, p.Y + dy)); }
            UpdateCropVisual();
        };
        if (cropKind != 2)
        {
            for (var i = 0; i < 4; i++)
            {
                var corner = i; var handle = MakeThumb(i is 0 or 3 ? Cursors.SizeNWSE : Cursors.SizeNESW, true);
                handle.Width = handle.Height = 10 / zoom; handle.Tag = corner;
                cropOverlay.Children.Add(handle);
                handle.DragStarted += (_, _) => { cropDragRect = cropRect; };
                handle.DragDelta += (_, _) =>
                {
                    var point = Mouse.GetPosition(canvas);
                    var x = Math.Clamp(point.X, 0, Current.PixelWidth); var y = Math.Clamp(point.Y, 0, Current.PixelHeight);
                    var opposite = corner switch { 0 => cropDragRect.BottomRight, 1 => cropDragRect.BottomLeft, 2 => cropDragRect.TopRight, _ => cropDragRect.TopLeft };
                    cropRect = new Rect(opposite, new Point(x, y)); UpdateCropVisual();
                };
            }
        }
        UpdateCropVisual();
    }
    private void UpdateCropVisual()
    {
        if (cropOverlay == null) return;
        ((System.Windows.Shapes.Path)cropOverlay.Children[0]).Data = CropGeometry();
        var move = (Thumb)cropOverlay.Children[1]; move.Width = cropRect.Width; move.Height = cropRect.Height;
        Canvas.SetLeft(move, cropRect.Left); Canvas.SetTop(move, cropRect.Top);
        for (var i = 2; i < cropOverlay.Children.Count; i++)
        {
            var handle = (Thumb)cropOverlay.Children[i];
            var point = (int)handle.Tag switch { 0 => cropRect.TopLeft, 1 => cropRect.TopRight, 2 => cropRect.BottomLeft, _ => cropRect.BottomRight };
            Canvas.SetLeft(handle, point.X - handle.Width / 2); Canvas.SetTop(handle, point.Y - handle.Height / 2);
        }
    }
    private void CancelCrop()
    {
        if (cropOverlay != null) canvas.Children.Remove(cropOverlay);
        cropOverlay = null; cropPoints = null; UpdateCropButtons();
    }
    private void ApplyCrop()
    {
        if (cropOverlay == null || cropRect.Width < 1 || cropRect.Height < 1) return;
        var x = Math.Clamp((int)Math.Floor(cropRect.Left), 0, Current.PixelWidth - 1);
        var y = Math.Clamp((int)Math.Floor(cropRect.Top), 0, Current.PixelHeight - 1);
        var right = Math.Clamp((int)Math.Ceiling(cropRect.Right), x + 1, Current.PixelWidth);
        var bottom = Math.Clamp((int)Math.Ceiling(cropRect.Bottom), y + 1, Current.PixelHeight);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.PushTransform(new TranslateTransform(-x, -y));
            context.PushClip(CropGeometry());
            context.DrawImage(Current, new Rect(0, 0, Current.PixelWidth, Current.PixelHeight));
            context.Pop(); context.Pop();
        }
        var result = new RenderTargetBitmap(right - x, bottom - y, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual); result.Freeze(); CancelCrop(); Commit(result);
    }
}
