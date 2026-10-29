using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SnapLine;

internal sealed class UpdateWindow : Window
{
    internal UpdateWindow(UpdateRelease release)
    {
        Title = Words.T("检查更新", "Check for updates");
        Width = 480; Height = 420; MinWidth = 360; MinHeight = 280;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        Background = AppTheme.Surface;
        var layout = new DockPanel { Margin = new Thickness(22) };
        var title = new TextBlock { Text = Words.T("发现新版本 ", "New version ") + release.Version, FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = AppTheme.Ink, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(title, Dock.Top); layout.Children.Add(title);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = new Button { Content = Words.T("稍后", "Later"), MinWidth = 80, Height = 32, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => DialogResult = false;
        var install = new Button { Content = Words.T("下载并更新", "Download and update"), MinWidth = 110, Height = 32 };
        install.Click += (_, _) => DialogResult = true;
        actions.Children.Add(cancel); actions.Children.Add(install);
        DockPanel.SetDock(actions, Dock.Bottom); layout.Children.Add(actions);
        var notes = new TextBox { Text = string.IsNullOrWhiteSpace(release.Notes) ? Words.T("此版本没有提供更新说明。", "No release notes were provided.") : release.Notes, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(10), BorderBrush = AppTheme.Outline, Background = Brushes.White };
        layout.Children.Add(notes); Content = layout;
    }
}
