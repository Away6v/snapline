using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace SnapLine;

internal static class ThemeWindow
{
    internal static void Apply(Window window)
    {
        var content = window.Content as UIElement ?? throw new InvalidOperationException("窗口内容必须是界面元素。");
        window.Content = null;
        window.WindowStyle = WindowStyle.None;
        var resizable = window.ResizeMode == ResizeMode.CanResize || window.ResizeMode == ResizeMode.CanResizeWithGrip;
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(resizable ? 6 : 0),
            CornerRadius = new CornerRadius(12),
            GlassFrameThickness = new Thickness(0),
            UseAeroCaptionButtons = false
        });
        // 将描边移入系统圆角裁切区域，避免边缘像素被窗口区域裁掉。
        var frame = new Border { Background = AppTheme.Surface, BorderBrush = AppTheme.Outline, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Margin = new Thickness(1), SnapsToDevicePixels = true };
        var layout = new DockPanel(); frame.Child = layout;
        var titleBar = new DockPanel { Height = 38, Background = AppTheme.Surface, LastChildFill = true };
        DockPanel.SetDock(titleBar, Dock.Top); layout.Children.Add(titleBar);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(actions, Dock.Right); titleBar.Children.Add(actions);
        Button AddButton(string icon, string name, Action click)
        {
            var button = new Button { Content = new LucideIcon(icon, AppTheme.Ink, 15), Width = 42, Height = 36, Padding = new Thickness(0), Style = ThemeButtons.Secondary, Background = Brushes.Transparent, ToolTip = name, Focusable = true };
            AutomationProperties.SetName(button, name); button.Click += (_, _) => click(); actions.Children.Add(button); return button;
        }
        if (window.ResizeMode != ResizeMode.NoResize)
            AddButton("minus", Words.T("最小化", "Minimize"), () => window.WindowState = WindowState.Minimized);
        Button? maximize = null;
        if (resizable)
            maximize = AddButton("square", Words.T("最大化", "Maximize"), () => window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized);
        AddButton("x", Words.T("关闭", "Close"), window.Close);
        if (maximize != null)
        {
            void UpdateMaximize()
            {
                var restored = window.WindowState == WindowState.Maximized;
                var name = restored ? Words.T("还原", "Restore") : Words.T("最大化", "Maximize");
                maximize.Content = new LucideIcon(restored ? "copy" : "square", AppTheme.Ink, 15); maximize.ToolTip = name; AutomationProperties.SetName(maximize, name);
            }
            window.StateChanged += (_, _) => UpdateMaximize();
        }
        var title = new TextBlock { Foreground = AppTheme.Ink, FontSize = 13, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        title.SetBinding(TextBlock.TextProperty, new Binding(nameof(Window.Title)) { Source = window }); titleBar.Children.Add(title);
        titleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2 && resizable) { window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; return; }
            if (e.ButtonState == MouseButtonState.Pressed) window.DragMove();
        };
        layout.Children.Add(content); window.Content = frame;
    }
}

internal static class ThemeMessageBox
{
    internal static MessageBoxResult Show(string message, string title, MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None)
        => Show(null, message, title, buttons, image);

    internal static MessageBoxResult Show(Window? owner, string message, string title, MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None)
    {
        var dialog = new Window { Title = title, Width = 480, MaxWidth = 560, MaxHeight = SystemParameters.WorkArea.Height * 0.85,
            SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ShowInTaskbar = owner == null, Owner = owner, FontFamily = new FontFamily("Microsoft YaHei UI"), Background = AppTheme.Surface };
        var layout = new DockPanel { Margin = new Thickness(22) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        DockPanel.SetDock(actions, Dock.Bottom); layout.Children.Add(actions);
        var result = buttons == MessageBoxButton.OKCancel ? MessageBoxResult.Cancel : MessageBoxResult.OK;
        if (buttons == MessageBoxButton.OKCancel)
        {
            var cancel = new Button { Content = Words.T("取消", "Cancel"), Style = ThemeButtons.Secondary, MinWidth = 84, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
            cancel.Click += (_, _) => { result = MessageBoxResult.Cancel; dialog.Close(); }; actions.Children.Add(cancel);
        }
        var ok = new Button { Content = Words.T("确定", "OK"), Style = ThemeButtons.Primary, MinWidth = 84, IsDefault = true, IsCancel = buttons == MessageBoxButton.OK };
        ok.Click += (_, _) => { result = MessageBoxResult.OK; dialog.Close(); }; actions.Children.Add(ok);
        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.Ink, FontSize = 14 };
        var messageRow = new DockPanel();
        if (image != MessageBoxImage.None)
        {
            var warning = image == MessageBoxImage.Warning || image == MessageBoxImage.Error;
            var badge = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = warning ? AppTheme.Alert : AppTheme.Accent, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 12, 0), Child = new TextBlock { Text = warning ? "!" : "i", Foreground = Brushes.White, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            DockPanel.SetDock(badge, Dock.Left); messageRow.Children.Add(badge);
        }
        messageRow.Children.Add(text);
        layout.Children.Add(new ScrollViewer { Content = messageRow, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        dialog.Content = layout; ThemeWindow.Apply(dialog); dialog.ShowDialog(); return result;
    }
}




