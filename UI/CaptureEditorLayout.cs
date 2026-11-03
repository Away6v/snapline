using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace SnapLine;

internal sealed partial class CaptureEditorWindow
{
    private readonly StrokeWidthSlider strokeSlider = new();
    private readonly TextBlock fileLabel = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 230 };
    private readonly TextBlock fileSizeLabel = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock dimensionsLabel = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock dirtyLabel = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.DarkOrange };
    private readonly Dictionary<Tool, RadioButton> toolButtons = new();
    private readonly Dictionary<Tool, LucideIcon> toolIcons = new();
    private readonly List<Button> directSaveButtons = new();
    private readonly StackPanel toolbarLeft = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel shapeOptions = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 14, 8) };
    private readonly StackPanel textOptions = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 14, 8) };
    private readonly StackPanel toolbarRight = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private static readonly Brush EditorInk = AppTheme.Ink;

    private static LucideIcon EditorIcon(string name, Brush? color = null) => new(name, color ?? EditorInk);

    private Button EditorButton(string zh, string en, string geometry, Action action, bool primary = false, double width = 48)
    {
        var name = Words.T(zh, en);
        var button = new Button { Width = width, Height = 34, MinHeight = 34, Padding = new Thickness(2), Margin = new Thickness(3, 0, 3, 0),
            Style = primary ? EditorPrimaryButtonStyle : EditorButtonStyle, Content = EditorIcon(geometry, primary ? Brushes.White : null), ToolTip = name };
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => action();
        return button;
    }

    private static readonly Style EditorButtonStyle = CreateEditorButtonStyle(false);
    private static readonly Style EditorPrimaryButtonStyle = CreateEditorButtonStyle(true);
    private static Style CreateEditorButtonStyle(bool primary)
    {
        var xaml = """
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
                <Setter Property="Background" Value="BACKGROUND"/><Setter Property="BorderBrush" Value="BORDER"/><Setter Property="BorderThickness" Value="1"/>
                <Setter Property="Cursor" Value="Hand"/><Setter Property="FocusVisualStyle" Value="{x:Null}"/>
                <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
                    <Border x:Name="Frame" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="5" Padding="{TemplateBinding Padding}">
                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="Background" Value="HOVER"/></Trigger>
                        <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource ThemeAccentDark}"/></Trigger>
                        <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate></Setter.Value></Setter>
            </Style>
            """;
        return (Style)System.Windows.Markup.XamlReader.Parse(xaml.Replace("BACKGROUND", primary ? "{DynamicResource ThemeAccent}" : "{DynamicResource ThemeSurface}").Replace("BORDER", primary ? "{DynamicResource ThemeAccentDark}" : "{DynamicResource ThemeOutline}").Replace("HOVER", primary ? "{DynamicResource ThemeAccentHover}" : "{DynamicResource ThemeHover}"));
    }

    private static Border Separator() => new() { Width = 1, Height = 24, Background = AppTheme.Outline, Margin = new Thickness(5, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
    private static Border PopupFrame(UIElement child) => new() { Background = AppTheme.Surface, BorderBrush = AppTheme.Outline, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8), Padding = new Thickness(5), Margin = new Thickness(0, 4, 0, 0), Child = child,
        Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 12, ShadowDepth = 2, Opacity = 0.14 } };

    private void BuildEditorLayout()
    {
        root.Margin = new Thickness(0);
        var header = new DockPanel { LastChildFill = true, Margin = new Thickness(10, 6, 10, 6) };
        var toolbarFrame = new Border { Background = AppTheme.Surface, CornerRadius = new CornerRadius(32), Margin = new Thickness(18, 5, 18, 18), Padding = new Thickness(7), HorizontalAlignment = HorizontalAlignment.Center, Child = header, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = 0.18 } };
        DockPanel.SetDock(toolbarFrame, Dock.Bottom); root.Children.Add(toolbarFrame);
        DockPanel.SetDock(toolbarRight, Dock.Right); header.Children.Add(toolbarRight);
        header.Children.Add(toolbarLeft);
        var style = CreateToolStyle();
        var toolDefs = new (Tool tool, string zh, string en, string icon)[] {
            (Tool.View, "查看", "View", "mouse-pointer-2"),
            (Tool.Rectangle, "形状", "Shape", "square"),
            (Tool.Arrow, "箭头", "Arrow", "arrow-right"),
            (Tool.Pen, "画笔", "Pen", "pencil"),
            (Tool.Text, "文字", "Text", "type"),
            (Tool.Mosaic, "马赛克", "Mosaic", "mosaic"),
            (Tool.Number, "序号", "Number", "number-marker"),
            (Tool.Crop, "裁剪", "Crop", "crop") };
        foreach (var definition in toolDefs)
        {
            var item = definition;
            var icon = EditorIcon(item.icon);
            var select = new RadioButton { Content = icon, GroupName = "CaptureEditorTool", Style = style, Width = 38, Height = 34, Padding = new Thickness(7), ToolTip = Words.T(item.zh, item.en) };
            AutomationProperties.SetName(select, Words.T(item.zh, item.en));
            var menuPanel = new StackPanel();
            var popup = new Popup { PlacementTarget = select, Placement = PlacementMode.Top, AllowsTransparency = true, StaysOpen = false, Child = PopupFrame(item.tool == Tool.Rectangle ? shapeOptions : item.tool == Tool.Text ? textOptions : menuPanel) };
            var arrow = EditorButton("工具选项", "Tool options", "chevron-down", () => { select.IsChecked = true; popup.IsOpen = !popup.IsOpen; }, width: 19);
            arrow.Content = new LucideIcon("chevron-down", EditorInk, 14);
            arrow.Padding = new Thickness(2); arrow.Margin = new Thickness(0); arrow.Background = Brushes.Transparent; arrow.BorderThickness = new Thickness(0);
            var group = new StackPanel { Orientation = Orientation.Horizontal };
            group.Children.Add(select);
            if (item.tool is not Tool.View) group.Children.Add(arrow);
            var groupFrame = new Border { Child = group, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(8), Margin = new Thickness(3, 0, 3, 0) };
            toolbarLeft.Children.Add(groupFrame);
            void UpdateGroup() { var active = select.IsChecked == true; groupFrame.Background = active ? AppTheme.Hover : Brushes.Transparent; icon.Stroke = EditorInk; }
            select.Checked += (_, _) => UpdateGroup(); select.Unchecked += (_, _) => UpdateGroup();
            toolButtons.Add(item.tool, select); toolIcons.Add(item.tool, icon);
            select.Checked += (_, _) => { if (tool != item.tool) { DeactivateText(); CancelCrop(); CancelDrawing(); } tool = item.tool; UpdateToolOptions(); };
            ConfigureToolMenu(item.tool, item.tool == Tool.Rectangle ? shapeOptions : item.tool == Tool.Text ? textOptions : menuPanel, popup, icon);
        }
        toolButtons[tool].IsChecked = true;
        var colorButton = new Button { Width = 66, Height = 34, MinHeight = 34, Padding = new Thickness(5), Margin = new Thickness(3, 0, 3, 0), Style = EditorButtonStyle, ToolTip = Words.T("颜色", "Color") };
        AutomationProperties.SetName(colorButton, Words.T("颜色", "Color"));
        var colorContent = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var swatch = new System.Windows.Shapes.Ellipse { Width = 19, Height = 19, Stroke = AppTheme.Outline, StrokeThickness = 1, VerticalAlignment = VerticalAlignment.Center };
        colorContent.Children.Add(swatch); colorContent.Children.Add(new LucideIcon("chevron-down", EditorInk, 14) { Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }); colorButton.Content = colorContent;
        var palette = new UniformGrid { Columns = 6, Margin = new Thickness(2) };
        var palettePanel = new StackPanel(); palettePanel.Children.Add(palette);
        var colorPopup = new Popup { PlacementTarget = colorButton, Placement = PlacementMode.Bottom, AllowsTransparency = true, StaysOpen = false, Child = PopupFrame(palettePanel) };
        var paletteColors = new[] { ("珊瑚红", "Coral", new SolidColorBrush(Color.FromRgb(221, 107, 80))), ("深灰", "Dark gray", new SolidColorBrush(Color.FromRgb(65, 67, 68))), ("白色", "White", Brushes.White), ("金黄", "Gold", new SolidColorBrush(Color.FromRgb(232, 161, 54))), ("蓝色", "Blue", new SolidColorBrush(Color.FromRgb(62, 137, 204))), ("青绿", "Teal", new SolidColorBrush(Color.FromRgb(84, 151, 150))) };
        var colorButtons = new List<Button>();
        var colorChecks = new List<LucideIcon>();
        for (var i = 0; i < paletteColors.Length; i++)
        {
            var index = i; var entry = paletteColors[i]; colors.Items.Add(entry.Item2);
            var circle = new Grid { Width = 24, Height = 24 };
            circle.Children.Add(new System.Windows.Shapes.Ellipse { Fill = entry.Item3, Stroke = new SolidColorBrush(Color.FromRgb(205, 218, 230)), StrokeThickness = 1 });
            var check = new LucideIcon("check", index is 2 or 3 or 5 ? new SolidColorBrush(Color.FromRgb(49, 45, 42)) : Brushes.White, 14) { Visibility = Visibility.Collapsed };
            circle.Children.Add(check); colorChecks.Add(check);
            var name = Words.T(entry.Item1, entry.Item2);
            var btn = new Button { Width = 38, Height = 38, Padding = new Thickness(2), Margin = new Thickness(1), Content = circle, Style = EditorButtonStyle, ToolTip = name };
            AutomationProperties.SetName(btn, name);
            btn.Click += (_, _) => { colors.SelectedIndex = index; colorPopup.IsOpen = false; };
            colorButtons.Add(btn); palette.Children.Add(btn);
        }
        colors.Items.Add("Custom");
        var customButton = new Button { Content = Words.T("自定义颜色…", "Custom color…"), Height = 32, Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(3, 4, 3, 2), Style = EditorButtonStyle, Foreground = EditorInk };
        AutomationProperties.SetName(customButton, Words.T("自定义颜色", "Custom color"));
        palettePanel.Children.Add(customButton);
        void RefreshColor()
        {
            swatch.Fill = ColorBrush(); strokeSlider.StrokeColor = ColorBrush(); UpdateTextAppearance();
            for (var i = 0; i < colorButtons.Count; i++)
            {
                var selected = colors.SelectedIndex == i;
                colorButtons[i].BorderBrush = selected ? AppTheme.Accent : AppTheme.Outline;
                colorButtons[i].Background = selected ? AppTheme.Hover : AppTheme.Soft;
                colorChecks[i].Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            }
            customButton.BorderBrush = colors.SelectedIndex == 6 ? AppTheme.Accent : AppTheme.Outline;
            customButton.Background = colors.SelectedIndex == 6 ? AppTheme.Hover : AppTheme.Soft;
        }
        colors.SelectionChanged += (_, _) => RefreshColor();
        customButton.Click += (_, _) =>
        {
            colorPopup.IsOpen = false;
            var current = ((SolidColorBrush)ColorBrush()).Color;
            using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B) };
            if (dialog.ShowDialog(new EditorDialogOwner(new System.Windows.Interop.WindowInteropHelper(this).Handle)) != System.Windows.Forms.DialogResult.OK) return;
            customColor = new SolidColorBrush(Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B));
            colors.SelectedIndex = 6;
            RefreshColor();
        };
        colors.SelectedIndex = 0; colorButton.Click += (_, _) => colorPopup.IsOpen = !colorPopup.IsOpen;
        var paletteRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        palettePanel.Children.Remove(palette); paletteRow.Children.Add(palette);
        paletteRow.Children.Add(Separator());
        foreach (var lineWidth in new[] { 2.0, 4.0, 8.0, 14.0 })
        {
            var dot = new System.Windows.Shapes.Ellipse { Width = 4 + lineWidth * 0.7, Height = 4 + lineWidth * 0.7, Fill = EditorInk };
            var widthButton = new Button { Width = 30, Height = 38, Content = dot, Style = EditorButtonStyle, ToolTip = Words.T("线宽", "Line width") + $" {lineWidth:0} px" };
            widthButton.Click += (_, _) => strokeSlider.SetValue(lineWidth); paletteRow.Children.Add(widthButton);
        }
        paletteRow.Children.Add(colorButton);
        var paletteFrame = new Border { Background = AppTheme.Surface, CornerRadius = new CornerRadius(28), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 0, 4), HorizontalAlignment = HorizontalAlignment.Center, Child = paletteRow, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.18 } };
        DockPanel.SetDock(paletteFrame, Dock.Bottom); root.Children.Add(paletteFrame);

        cropApply = EditorButton("完成裁剪", "Apply crop", "check", ApplyCrop, true);
        cropCancel = EditorButton("取消裁剪", "Cancel crop", "x", CancelCrop);
        toolbarRight.Children.Add(cropApply); toolbarRight.Children.Add(cropCancel);
        ConfigureExistingButton(undoButton, "撤销", "Undo", "undo-2", Undo);
        ConfigureExistingButton(redoButton, "重做", "Redo", "redo-2", Redo);
        toolbarRight.Children.Add(undoButton); toolbarRight.Children.Add(redoButton); toolbarRight.Children.Add(Separator());
        var export = EditorButton("另存为", "Save as", "save", SaveAsFile);
        var add = EditorButton("新增截图", "Add screenshot", "file-plus", () => SaveDirect(false));
        ConfigureExistingButton(saveButton, "更新当前截图", "Update screenshot", "check", () => SaveDirect(true), true);
        toolbarRight.Children.Add(export); toolbarRight.Children.Add(add); toolbarRight.Children.Add(Separator()); saveButton.Width = 90; var doneContent = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center }; doneContent.Children.Add(new LucideIcon("check", Brushes.White, 18) { Margin = new Thickness(0, 0, 5, 0) }); doneContent.Children.Add(new TextBlock { Text = Words.T("完成", "Done"), VerticalAlignment = VerticalAlignment.Center }); saveButton.Content = doneContent; saveButton.Foreground = Brushes.White; saveButton.FontSize = 15; toolbarRight.Children.Add(saveButton); directSaveButtons.Add(export); directSaveButtons.Add(add);
        CreateSaveMenu();

        var status = new DockPanel { Background = Brushes.Transparent, Height = 42, Margin = new Thickness(14, 0, 14, 0), LastChildFill = true };
        DockPanel.SetDock(status, Dock.Top); root.Children.Add(status);
        var statusRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(statusRight, Dock.Right); status.Children.Add(statusRight);
        statusRight.Children.Add(EditorButton("适应画布", "Fit to window", "scan", Fit, width: 32));
        statusRight.Children.Add(EditorButton("缩小", "Zoom out", "zoom-out", () => SetZoom(zoom / 1.25), width: 30));
        zoomSlider.ToolTip = Words.T("缩放比例", "Zoom");
        AutomationProperties.SetName(zoomSlider, Words.T("缩放比例", "Zoom"));
        zoomSlider.ValueChanged += (_, _) => { if (!updatingZoomSlider) SetZoom(Math.Pow(2, zoomSlider.Value)); };
        statusRight.Children.Add(zoomSlider);
        zoomLabel.Foreground = EditorInk; zoomLabel.FontSize = 13; statusRight.Children.Add(new Border { Child = zoomLabel, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(8), Height = 34, Margin = new Thickness(3, 0, 3, 0) });
        statusRight.Children.Add(EditorButton("放大", "Zoom in", "zoom-in", () => SetZoom(zoom * 1.25), width: 30));
        statusRight.Children.Add(Separator()); statusRight.Children.Add(dirtyLabel);
        var info = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        fileLabel.MinWidth = 200; fileLabel.Foreground = fileSizeLabel.Foreground = dimensionsLabel.Foreground = EditorInk; fileSizeLabel.MinWidth = 125; dimensionsLabel.MinWidth = 160; info.Children.Add(fileLabel); info.Children.Add(Separator()); info.Children.Add(fileSizeLabel); info.Children.Add(Separator()); info.Children.Add(dimensionsLabel); status.Children.Add(info);
        var work = new DockPanel { Background = AppTheme.Soft, Margin = new Thickness(28, 12, 28, 8) };
        scroll.Background = Brushes.Transparent; scroll.HorizontalContentAlignment = HorizontalAlignment.Center; scroll.VerticalContentAlignment = VerticalAlignment.Center;
        work.Children.Add(scroll); root.Children.Add(work); Content = root;
        UpdateToolOptions(); UpdateHistoryButtons();
    }

    private sealed class EditorDialogOwner(IntPtr handle) : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle => handle;
    }

    private void ConfigureExistingButton(Button target, string zh, string en, string geometry, Action action, bool primary = false)
    {
        var source = EditorButton(zh, en, geometry, action, primary);
        target.Width = source.Width; target.Height = source.Height; target.MinHeight = source.MinHeight; target.Padding = source.Padding; target.Margin = source.Margin;
        target.Style = source.Style; target.Content = EditorIcon(geometry, primary ? Brushes.White : null); target.ToolTip = Words.T(zh, en);
        AutomationProperties.SetName(target, Words.T(zh, en)); target.Click += (_, _) => action();
    }

    private void ConfigureToolMenu(Tool owner, StackPanel panel, Popup popup, LucideIcon toolIcon)
    {
        panel.Orientation = Orientation.Horizontal;
        void Choices(ComboBox selection, (string zh, string en, string icon)[] choices, bool changesIcon)
        {
            foreach (var choice in choices)
            {
                var entry = choice; var index = selection.Items.Count; selection.Items.Add(entry.en);
                var option = new RadioButton { Content = EditorIcon(entry.icon), Style = CreateTextToggleStyle(), GroupName = owner + "Choice", Width = 38, Height = 32, Margin = new Thickness(2), ToolTip = Words.T(entry.zh, entry.en) };
                AutomationProperties.SetName(option, Words.T(entry.zh, entry.en));
                option.Checked += (_, _) => { selection.SelectedIndex = index; if (changesIcon) toolIcon.IconName = entry.icon; };
                panel.Children.Add(option);
                if (index == 0) option.IsChecked = true;
            }
        }
        if (owner == Tool.Mosaic)
            Choices(mosaicStyles, new[] { ("矩形选区", "Rectangle selection", "rectangle-select"), ("画笔涂抹", "Brush", "pencil") }, false);
        if (owner == Tool.Number)
        {
            var reset = EditorButton("重新从 1 开始", "Restart at 1", "number-reset", () => { nextNumber = 1; popup.IsOpen = false; }, width: 38);
            reset.Height = reset.MinHeight = 32; reset.Margin = new Thickness(2);
            panel.Children.Add(reset);
        }
        if (owner == Tool.Pen)
        {
            panel.Orientation = Orientation.Horizontal;
            var nibs = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var entry in new[] { ("铅笔", "Pencil", "pencil"), ("钢笔", "Pen", "pen-tool"), ("荧光笔", "Highlighter", "highlighter") })
            {
                var index = penStyles.Items.Count; penStyles.Items.Add(entry.Item2);
                var choice = new RadioButton { Content = EditorIcon(entry.Item3), Style = CreateTextToggleStyle(), GroupName = "PenChoice", Width = 38, Height = 32, Margin = new Thickness(2), ToolTip = Words.T(entry.Item1, entry.Item2) };
                AutomationProperties.SetName(choice, Words.T(entry.Item1, entry.Item2));
                choice.Checked += (_, _) => { penStyles.SelectedIndex = index; toolIcon.IconName = entry.Item3; };
                nibs.Children.Add(choice);
                if (index == 0) choice.IsChecked = true;
            }
            panel.Children.Add(nibs);
            panel.Children.Add(Separator());
            var lines = new StackPanel { Orientation = Orientation.Horizontal };
            var solid = new RadioButton { GroupName = "PenLineChoice" };
            foreach (var item in new[] { (solid, "实线", "Solid", "minus"), (penDashed, "虚线", "Dashed", "line-dashed") })
            {
                item.Item1.Content = EditorIcon(item.Item4); item.Item1.Style = CreateTextToggleStyle();
                item.Item1.Width = 38; item.Item1.Height = 32; item.Item1.Margin = new Thickness(2); item.Item1.GroupName = "PenLineChoice";
                item.Item1.ToolTip = Words.T(item.Item2, item.Item3); AutomationProperties.SetName(item.Item1, Words.T(item.Item2, item.Item3));
                lines.Children.Add(item.Item1);
            }
            solid.IsChecked = true; panel.Children.Add(lines);
        }
        if (owner == Tool.Arrow) Choices(arrowStyles, new[] { ("普通箭头", "Open arrow", "arrow-right"), ("实心箭头", "Solid arrow", "arrow-big-right"), ("双向箭头", "Double arrow", "move-horizontal") }, true);
        if (owner == Tool.Rectangle)
        {
            var shapes = new[] { ("矩形", "Rectangle", "square"), ("圆角矩形", "Rounded rectangle", "rectangle-horizontal"), ("椭圆", "Ellipse", "circle") };
            foreach (var entry in shapes)
            {
                var index = rectangleShapes.Items.Count; rectangleShapes.Items.Add(entry.Item2);
                var button = new RadioButton { Content = EditorIcon(entry.Item3), Style = CreateTextToggleStyle(), GroupName = "ShapeChoice", Width = 38, Height = 32, Margin = new Thickness(2, 0, 2, 0), ToolTip = Words.T(entry.Item1, entry.Item2) };
                AutomationProperties.SetName(button, Words.T(entry.Item1, entry.Item2));
                button.Checked += (_, _) => { rectangleShapes.SelectedIndex = index; toolIcon.IconName = entry.Item3; };
                panel.Children.Add(button);
                if (index == 0) button.IsChecked = true;
            }
            panel.Children.Add(Separator());
            shapeFill.Content = EditorIcon("paint-bucket"); shapeFill.Style = CreateTextToggleStyle();
            shapeFill.Width = 38; shapeFill.Height = 32; shapeFill.Margin = new Thickness(2, 0, 2, 0);
            shapeFill.ToolTip = Words.T("填充", "Fill"); AutomationProperties.SetName(shapeFill, Words.T("填充", "Fill"));
            panel.Children.Add(shapeFill);
            var solid = new RadioButton { GroupName = "ShapeLineChoice" };
            foreach (var item in new[] { (solid, "实线", "Solid", "minus"), (shapeDashed, "虚线", "Dashed", "line-dashed") })
            {
                item.Item1.Content = EditorIcon(item.Item4); item.Item1.Style = CreateTextToggleStyle();
                item.Item1.Width = 38; item.Item1.Height = 32; item.Item1.Margin = new Thickness(2, 0, 2, 0); item.Item1.GroupName = "ShapeLineChoice";
                item.Item1.ToolTip = Words.T(item.Item2, item.Item3); AutomationProperties.SetName(item.Item1, Words.T(item.Item2, item.Item3));
                panel.Children.Add(item.Item1);
            }
            solid.IsChecked = true;
        }
        if (owner == Tool.Crop)
        {
            Choices(cropStyles, new[] { ("矩形裁剪", "Rectangle crop", "crop"), ("椭圆裁剪", "Ellipse crop", "circle"), ("自由裁剪", "Freeform crop", "lasso-select") }, true);
            cropStyles.SelectionChanged += (_, _) => CancelCrop();
        }
        if (owner == Tool.Text)
        {
            foreach (var font in new[] { "Microsoft YaHei UI", "SimSun", "Arial", "Segoe UI", "Times New Roman" }) fontNames.Items.Add(font);
            foreach (var size in new[] { 12, 14, 16, 20, 24, 32, 48, 64, 96 }) sizes.Items.Add(size);
            fontNames.Style = sizes.Style = CreateComboStyle(); fontNames.SelectedIndex = 0; sizes.SelectedItem = 24;
            fontNames.ToolTip = Words.T("字体", "Font"); sizes.ToolTip = Words.T("字号", "Font size");
            fontNames.Margin = sizes.Margin = new Thickness(2); panel.Children.Add(fontNames); panel.Children.Add(sizes);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var toggle in new[] { boldText, italicText }) { toggle.Style = CreateTextToggleStyle(); toggle.Margin = new Thickness(2); toggle.Checked += (_, _) => UpdateTextAppearance(); toggle.Unchecked += (_, _) => UpdateTextAppearance(); row.Children.Add(toggle); }
            boldText.Content = "B"; boldText.FontWeight = FontWeights.Bold; boldText.ToolTip = Words.T("加粗", "Bold");
            italicText.Content = "I"; italicText.FontStyle = FontStyles.Italic; italicText.ToolTip = Words.T("斜体", "Italic"); panel.Children.Add(row);
            fontNames.SelectionChanged += (_, _) => UpdateTextAppearance(); sizes.SelectionChanged += (_, _) => UpdateTextAppearance();
        }
    }

    private MessageBoxResult ConfirmClose()
    {
        var dialog = new Window { Owner = this, Title = Words.T("未保存的修改", "Unsaved changes"), Width = 400, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            Background = AppTheme.Surface, FontFamily = FontFamily };
        var layout = new StackPanel { Margin = new Thickness(22) };
        layout.Children.Add(new TextBlock { Text = Words.T("保存对截图的修改吗？", "Save changes to the screenshot?"), FontSize = 15, Foreground = AppTheme.Ink, Margin = new Thickness(0, 0, 0, 22) });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var result = MessageBoxResult.Cancel;
        void AddAction(string label, MessageBoxResult value, Style style, bool cancel = false)
        {
            var button = new Button { Content = label, Style = style, MinWidth = 84, Margin = new Thickness(8, 0, 0, 0), IsCancel = cancel };
            button.Click += (_, _) => { result = value; dialog.Close(); };
            actions.Children.Add(button);
        }
        AddAction(Words.T("取消", "Cancel"), MessageBoxResult.Cancel, ThemeButtons.Secondary, true);
        AddAction(Words.T("不保存", "Don't save"), MessageBoxResult.No, ThemeButtons.Secondary);
        AddAction(Words.T("保存", "Save"), MessageBoxResult.Yes, ThemeButtons.Primary);
        layout.Children.Add(actions); dialog.Content = layout; dialog.ShowDialog();
        return result;
    }

    private void UpdateEditorToolState()
    {

    }
    private void UpdateEditorStatus()
    {
        fileLabel.Text = System.IO.Path.GetFileName(capture.Path);
        fileLabel.ToolTip = fileLabel.Text;
        try { var size = new FileInfo(capture.Path).Length; fileSizeLabel.Text = Words.T("文件大小  ", "File size  ") + (size >= 1024 * 1024 ? $"{size / 1048576.0:0.##} MB" : $"{size / 1024.0:0.#} KB"); } catch (IOException) { fileSizeLabel.Text = "—"; } catch (UnauthorizedAccessException) { fileSizeLabel.Text = "—"; }
        dimensionsLabel.Text = Words.T("尺寸  ", "Size  ") + $"{Current.PixelWidth} × {Current.PixelHeight} px";
        dirtyLabel.Text = Dirty ? Words.T("未保存", "Unsaved") : "";
    }
    private void SaveDirect(bool replace)
    {
        if (saving || cropOverlay != null) return;
        CommitText(); _ = SaveAsync(replace, false);
    }
    private void SaveAsFile()
    {
        if (saving || cropOverlay != null) return;
        CommitText();
        var dialog = new SaveFileDialog { Filter = Words.T("PNG 图片 (*.png)|*.png|JPEG 图片 (*.jpg)|*.jpg", "PNG image (*.png)|*.png|JPEG image (*.jpg)|*.jpg"), FileName = System.IO.Path.GetFileNameWithoutExtension(capture.Path), DefaultExt = ".png", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            BitmapEncoder encoder = dialog.FilterIndex == 2 ? new JpegBitmapEncoder() : new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(Current));
            using var stream = new FileStream(dialog.FileName, FileMode.Create, FileAccess.Write, FileShare.None); encoder.Save(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { MessageBox.Show(this, Words.T("无法另存截图：", "Cannot save screenshot: ") + ex.Message, "SnapLine", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}

internal sealed class StrokeWidthSlider : FrameworkElement
{
    private double value = 4;
    private Brush strokeColor = Brushes.Red;
    internal double Value => value;
    internal void SetValue(double width) { value = Math.Clamp(width, 1, 24); InvalidateVisual(); }
    internal Brush StrokeColor { get => strokeColor; set { strokeColor = value; InvalidateVisual(); } }
    internal StrokeWidthSlider() { Width = 86; Focusable = true; Cursor = Cursors.Hand; ToolTip = Words.T("线宽", "Line width"); AutomationProperties.SetName(this, Words.T("线宽", "Line width")); }
    private double TrackHeight => Math.Max(30, Math.Min(240, ActualHeight - 48));
    private double TrackTop => (ActualHeight - TrackHeight) / 2;
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var top = TrackTop; var height = TrackHeight; const double center = 25;
        var y = top + (value - 1) / 23 * height;
        var panel = new Rect(center - 11, top - 12, 22, height + 24);
        var color = (strokeColor as SolidColorBrush)?.Color ?? Colors.Red;
        var brightness = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255;
        var lightStroke = brightness > 0.72;
        var panelBrush = new SolidColorBrush(lightStroke ? Color.FromRgb(130, 150, 166) : Color.FromRgb(238, 246, 251));
        var panelOutline = new SolidColorBrush(lightStroke ? Color.FromRgb(113, 135, 152) : Color.FromRgb(204, 222, 235));
        dc.DrawRoundedRectangle(panelBrush, new Pen(panelOutline, 1), panel, 11, 11);
        var shape = new StreamGeometry();
        using (var ctx = shape.Open())
        {
            ctx.BeginFigure(new Point(center, top), true, true);
            ctx.LineTo(new Point(center + 6, top + height - 6), true, false);
            ctx.BezierTo(new Point(center + 6.5, top + height + 3), new Point(center + 4, top + height + 6), new Point(center, top + height + 6), true, false);
            ctx.BezierTo(new Point(center - 4, top + height + 6), new Point(center - 6.5, top + height + 3), new Point(center - 6, top + height - 6), true, false);
        }
        var trackBrush = strokeColor.Clone();
        trackBrush.Opacity = 0.85;
        var trackOutline = new Pen(new SolidColorBrush(lightStroke ? Color.FromArgb(95, 48, 72, 91) : Color.FromArgb(70, 255, 255, 255)), 0.7) { LineJoin = PenLineJoin.Round };
        dc.DrawGeometry(trackBrush, trackOutline, shape);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(32, 35, 92, 135)), null, new Rect(center - 9, y - 3, 18, 9), 4.5, 4.5);
        dc.DrawRoundedRectangle(AppTheme.Accent, new Pen(Brushes.White, 1), new Rect(center - 9, y - 4, 18, 8), 4, 4);
        var text = new FormattedText($"{value:0} px", System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), 12, EditorSliderInk, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new Point(42, y - text.Height / 2));
    }
    private static readonly Brush EditorSliderInk = AppTheme.Ink;
    private void SetFromPoint(Point point) { value = Math.Clamp(Math.Round(1 + (point.Y - TrackTop) / TrackHeight * 23), 1, 24); InvalidateVisual(); }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { base.OnMouseLeftButtonDown(e); Focus(); CaptureMouse(); SetFromPoint(e.GetPosition(this)); e.Handled = true; }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) { SetFromPoint(e.GetPosition(this)); e.Handled = true; } }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { base.OnMouseLeftButtonUp(e); if (IsMouseCaptured) { SetFromPoint(e.GetPosition(this)); ReleaseMouseCapture(); e.Handled = true; } }
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.Key is Key.Up or Key.Down) { value = Math.Clamp(value + (e.Key == Key.Down ? 1 : -1), 1, 24); InvalidateVisual(); e.Handled = true; } }
}
