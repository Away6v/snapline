using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace SnapLine;

internal sealed class TrayPanelWindow : Window
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const double FrameHeight = 48;
    private static readonly Brush Surface = AppTheme.Surface;
    private static readonly Brush Ink = AppTheme.Ink;
    private static readonly Brush Muted = AppTheme.Muted;
    private static readonly Brush Accent = AppTheme.Accent;
    private static readonly Brush Outline = AppTheme.Outline;
    private static readonly Brush Soft = AppTheme.Soft;
    private static readonly Brush Hover = AppTheme.Hover;
    private static readonly Brush Alert = AppTheme.Alert;
    private static readonly Style PrimaryStyle = ButtonStyle(Accent, AppTheme.AccentHover, AppTheme.AccentPressed, Brushes.White, 10);
    private static readonly Style SoftStyle = ButtonStyle(Soft, Hover, AppTheme.Pressed, Ink, 9);
    private static readonly Style ModeActionStyle = ButtonStyle(Surface, Hover, AppTheme.Pressed, Ink, 9);
    private static readonly Style ModeTabStyle = ButtonStyle(Brushes.Transparent, Hover, AppTheme.Pressed, Ink, 6);
    private static readonly Style RowStyle = ButtonStyle(Brushes.Transparent, Soft, Hover, Ink, 8);
    private static readonly Style DangerStyle = ButtonStyle(Brushes.Transparent, Soft, Hover, Alert, 8);
    private static readonly Style CapacityStyle = CreateCapacityStyle();

    private readonly Preferences preferences;
    private readonly Func<int> captureCount;
    private readonly Func<int> textCount;
    private readonly Func<Capture[]> previewCaptures;
    private readonly Func<bool> lineVisible;
    private readonly Action toggleLine;
    private readonly Action openInbox;
    private readonly Action exit;
    private readonly Func<bool, uint, uint, bool> registerShortcut;
    private readonly Func<bool, bool, Task> savePreferences;
    private readonly Action<string> setDisplayMode;
    private readonly Action createText;
    private readonly ScrollViewer scroll;
    private readonly ContentControl header = new();
    private readonly ContentControl footer = new();
    private readonly SmoothScroll smoothScroll;
    private ComboBox? openCapacityChoice;
    private Page currentPage;
    private bool folderDialogOpen;
    private bool wasActivated;
    private string? message;
    private string settingsMode;
    private double maximumHeight = 560;
    private System.Drawing.Point? anchor;

    private enum Page { Home, Folders, Appearance, Language, Shortcut }

    internal TrayPanelWindow(Preferences preferences, Func<int> captureCount, Func<int> textCount, Func<Capture[]> previewCaptures, Func<bool> lineVisible,
        Action toggleLine, Action openInbox, Action exit, Func<bool, uint, uint, bool> registerShortcut,
        Func<bool, bool, Task> savePreferences, Action<string> setDisplayMode, Action createText)
    {
        this.preferences = preferences;
        settingsMode = preferences.DisplayMode;
        this.captureCount = captureCount;
        this.textCount = textCount;
        this.previewCaptures = previewCaptures;
        this.lineVisible = lineVisible;
        this.toggleLine = toggleLine;
        this.openInbox = openInbox;
        this.exit = exit;
        this.registerShortcut = registerShortcut;
        this.savePreferences = savePreferences;
        this.setDisplayMode = setDisplayMode;
        this.createText = createText;
        Title = "SnapLine";
        Width = 360;
        Height = 520;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = true;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        FontSize = 12;
        UseLayoutRounding = true;
        var frame = new Border
        {
            Margin = new Thickness(8),
            Padding = new Thickness(16, 15, 16, 13),
            CornerRadius = new CornerRadius(18),
            Background = Surface,
            BorderBrush = Outline,
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { Color = Color.FromRgb(67, 57, 52), BlurRadius = 18, ShadowDepth = 4, Opacity = 0.16 }
        };
        scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brushes.Transparent
        };
        smoothScroll = new SmoothScroll(scroll, false);
        Closed += (_, _) => smoothScroll.Stop();
        // 顶部品牌和底部退出操作独立于滚动区域，窗口受限时只滚动中间内容。
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        header.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        footer.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        layout.Children.Add(header);
        Grid.SetRow(scroll, 1);
        layout.Children.Add(scroll);
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);
        frame.Child = layout;
        Content = frame;
        Activated += (_, _) => wasActivated = true;
        Deactivated += (_, _) => { if (wasActivated && !folderDialogOpen && IsVisible) Close(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            if (openCapacityChoice is { IsDropDownOpen: true } choice)
            {
                choice.IsDropDownOpen = false;
                return;
            }
            if (currentPage == Page.Home) Close();
            else Navigate(Page.Home);
        };
        Navigate(Page.Home);
    }

    internal void PrepareNear(System.Drawing.Point cursor)
    {
        anchor = cursor;
        var screen = Forms.Screen.FromPoint(cursor);
        var scale = Native.Scale(screen);
        maximumHeight = Math.Max(170, Math.Min(560, (screen.WorkingArea.Height - 16) / scale));
        FitHeight();
        var (left, top, _, _) = Placement(cursor, screen, scale);
        Left = left / scale;
        Top = top / scale;
    }

    internal void PlaceNear(System.Drawing.Point cursor)
    {
        anchor = cursor;
        var screen = Forms.Screen.FromPoint(cursor);
        var scale = Native.Scale(screen);
        var (left, top, width, height) = Placement(cursor, screen, scale);
        Native.SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), left, top, width, height, 0x0010);
    }

    private (int Left, int Top, int Width, int Height) Placement(System.Drawing.Point cursor, Forms.Screen screen, double scale)
    {
        var width = (int)Math.Ceiling(Width * scale);
        var height = (int)Math.Ceiling(Height * scale);
        var work = screen.WorkingArea;
        var left = Math.Clamp(cursor.X - width / 2, work.Left + 6, Math.Max(work.Left + 6, work.Right - width - 6));
        var desiredTop = cursor.Y >= work.Top + height + 12 ? cursor.Y - height - 8 : cursor.Y + 12;
        var top = Math.Clamp(desiredTop, work.Top + 6, Math.Max(work.Top + 6, work.Bottom - height - 6));
        return (left, top, width, height);
    }

    private void Navigate(Page page)
    {
        currentPage = page;
        header.Content = page == Page.Home ? BrandHeader() : SubpageHeader(page);
        footer.Content = page == Page.Home ? ExitFooter() : null;
        footer.Visibility = page == Page.Home ? Visibility.Visible : Visibility.Collapsed;
        scroll.Content = page switch
        {
            Page.Home => Home(),
            Page.Folders => Folders(),
            Page.Appearance => AppearancePage(),
            Page.Language => LanguagePage(),
            _ => Shortcut()
        };
        smoothScroll.Stop();
        scroll.ScrollToTop();
        FitHeight();
        // 控件模板和字体生效后重新测量，避免初次测量偏小而出现多余滚动条。
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (currentPage == page && IsVisible) FitHeight();
        }));
    }

    internal void RefreshDisplayMode()
    {
        settingsMode = preferences.DisplayMode;
        if (currentPage == Page.Home && IsVisible) Navigate(Page.Home);
    }

    private void FitHeight()
    {
        if (scroll.Content is not FrameworkElement page) return;
        var available = new Size(Math.Max(1, Width - 50), double.PositiveInfinity);
        header.Measure(available);
        footer.Measure(available);
        page.Measure(available);
        var fixedHeight = header.DesiredSize.Height + (footer.Visibility == Visibility.Visible ? footer.DesiredSize.Height : 0) + FrameHeight;
        if (page.DesiredSize.Height + fixedHeight > maximumHeight)
            page.Measure(new Size(Math.Max(1, available.Width - 10), double.PositiveInfinity));
        Height = Math.Min(maximumHeight, Math.Max(170, Math.Ceiling(page.DesiredSize.Height + fixedHeight)));
        if (IsVisible && anchor is System.Drawing.Point cursor) PlaceNear(cursor);
    }

    private Grid BrandHeader()
    {
        var header = new Grid { Margin = new Thickness(0, 0, 0, 13) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.Children.Add(new Image
        {
            Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/snapline.png")),
            Width = 27,
            Height = 27,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center
        });
        var brand = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(Label("SnapLine", 17, Ink, FontWeights.Bold));
        Grid.SetColumn(brand, 1);
        header.Children.Add(brand);
        return header;
    }

    private Grid SubpageHeader(Page page)
    {
        var title = page switch
        {
            Page.Folders => Words.T("监控目录", "Watched folders"),
            Page.Appearance => Words.T("外观", "Appearance"),
            Page.Language => Words.T("界面语言", "Language"),
            _ => Words.T("快捷键设置", "Keyboard shortcuts")
        };
        var bar = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        bar.ColumnDefinitions.Add(new ColumnDefinition());
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.Children.Add(Label(title, 18, Ink, FontWeights.SemiBold));
        var back = new Button
        {
            Style = RowStyle,
            Width = 36,
            Height = 36,
            Padding = new Thickness(0),
            Content = new TextBlock
            {
                Text = "↩",
                FontFamily = new FontFamily("Segoe UI Symbol"),
                FontSize = 21,
                Foreground = Ink,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        System.Windows.Automation.AutomationProperties.SetName(back, Words.T("返回", "Back"));
        back.Click += (_, _) => Navigate(Page.Home);
        Grid.SetColumn(back, 1);
        bar.Children.Add(back);
        return bar;
    }

    private StackPanel ExitFooter()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(Rule());
        panel.Children.Add(Gap(5));
        var actions = new Grid();
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        var main = IconButton(Words.T(lineVisible() ? "收起晾衣绳" : "展开晾衣绳", lineVisible() ? "Hide line" : "Show line"), PrimaryStyle,
            "M 1,3 Q 8,8 15,3 M 4,5 L 4,10 L 7,10 L 7,6 M 10,6 L 10,11 L 13,11 L 13,5", Brushes.White, 38);
        main.Margin = new Thickness(0, 0, 4, 0);
        main.Click += (_, _) => { Close(); toggleLine(); };
        actions.Children.Add(main);
        var quit = IconButton(Words.T("退出 SnapLine", "Quit SnapLine"), DangerStyle, "M 7,2 L 3,2 L 3,14 L 7,14 M 9,5 L 12,8 L 9,11 M 6,8 L 12,8", Alert, 38);
        quit.Margin = new Thickness(4, 0, 0, 0);
        quit.Click += (_, _) => { Close(); exit(); };
        Grid.SetColumn(quit, 1);
        actions.Children.Add(quit);
        panel.Children.Add(actions);
        return panel;
    }

    private StackPanel Home()
    {
        var panel = new StackPanel();
        var lineMode = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        lineMode.ColumnDefinitions.Add(new ColumnDefinition());
        lineMode.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        lineMode.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        lineMode.Children.Add(Label(Words.T("晾衣绳模式", "Line mode"), 12, Ink, FontWeights.Medium));
        foreach (var (mode, zh, en, column) in new[] { ("screenshot", "截图", "Screenshots", 1), ("text", "文本", "Text", 2) })
        {
            var selected = preferences.DisplayMode == mode;
            var button = Button(Words.T(zh, en), selected ? PrimaryStyle : SoftStyle, 32);
            button.MinWidth = 88;
            button.Margin = new Thickness(4, 0, 0, 0);
            System.Windows.Automation.AutomationProperties.SetName(button, Words.T("晾衣绳模式：", "Line mode: ") + Words.T(zh, en));
            System.Windows.Automation.AutomationProperties.SetHelpText(button, selected ? Words.T("当前显示模式", "Current display mode") : Words.T("切换显示模式", "Switch display mode"));
            button.Click += async (_, _) =>
            {
                if (preferences.DisplayMode != mode)
                {
                    setDisplayMode(mode);
                    await Persist(false, false);
                }
                if (settingsMode == mode && selected) return;
                settingsMode = mode;
                Navigate(Page.Home);
            };
            Grid.SetColumn(button, column);
            lineMode.Children.Add(button);
        }
        panel.Children.Add(lineMode);
        var modeContent = new StackPanel();
        var modeFrame = new Border { Background = Soft, BorderBrush = Outline, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(6), MinHeight = 234, Child = modeContent };
        var modes = new Grid();
        modes.ColumnDefinitions.Add(new ColumnDefinition());
        modes.ColumnDefinitions.Add(new ColumnDefinition());
        foreach (var (mode, zh, en, column) in new[] { ("screenshot", "截图", "Screenshots", 0), ("text", "文本", "Text", 1) })
        {
            var selected = settingsMode == mode;
            var choose = Button(Words.T(zh, en), ModeTabStyle, 36);
            choose.Padding = new Thickness(0);
            System.Windows.Automation.AutomationProperties.SetName(choose, Words.T(zh, en));
            System.Windows.Automation.AutomationProperties.SetHelpText(choose, selected ? Words.T("当前配置页", "Current settings tab") : Words.T("查看配置", "View settings"));
            var count = mode == "text" ? textCount() : captureCount();
            var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            content.VerticalAlignment = VerticalAlignment.Center;
            content.Children.Add(Label(Words.T(zh, en), 12, selected ? Accent : Muted, selected ? FontWeights.SemiBold : FontWeights.Medium));
            content.Children.Add(new Border
            {
                Background = selected ? Hover : AppTheme.Outline,
                CornerRadius = new CornerRadius(9), MinWidth = 20, Height = 18,
                Margin = new Thickness(7, 0, 0, 0), Padding = new Thickness(5, 0, 5, 0),
                Child = Label(count.ToString(), 10, selected ? Accent : Muted, FontWeights.SemiBold, TextAlignment.Center)
            });
            var tab = new Grid { Height = 36 };
            tab.Children.Add(content);
            if (selected)
                tab.Children.Add(new Border { Width = 32, Height = 3, CornerRadius = new CornerRadius(2), Background = Accent, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom });
            choose.Content = tab;
            choose.Margin = new Thickness(column == 0 ? 0 : 4, 0, column == 0 ? 4 : 0, 0);
            choose.Click += (_, _) =>
            {
                if (settingsMode == mode) return;
                settingsMode = mode;
                Navigate(Page.Home);
            };
            Grid.SetColumn(choose, column);
            modes.Children.Add(choose);
        }
        modeContent.Children.Add(modes);
        modeContent.Children.Add(Gap(8));
        if (settingsMode == "text")
        {
            var create = Button(Words.T("＋ 新建文本", "＋ New text"), ModeActionStyle, 38);
            create.Click += (_, _) => { Close(); createText(); };
            modeContent.Children.Add(create);
            modeContent.Children.Add(Gap(4));
            modeContent.Children.Add(ToggleRow(Words.T("收集剪贴板文本", "Collect clipboard text"), preferences.TextClipboardEnabled, async () =>
            {
                preferences.TextClipboardEnabled = !preferences.TextClipboardEnabled;
                await Persist(false, false);
                Navigate(Page.Home);
            }));
            modeContent.Children.Add(CapacityRow(true));
        }
        if (settingsMode == "screenshot")
        {
            var quick = new Grid();
            quick.ColumnDefinitions.Add(new ColumnDefinition());
            quick.ColumnDefinitions.Add(new ColumnDefinition());
            var inbox = IconButton(Words.T("打开收件箱", "Open inbox"), ModeActionStyle, "M 2,3 L 14,3 L 14,13 L 2,13 Z M 2,9 L 5,9 L 6,11 L 10,11 L 11,9 L 14,9", Ink, 38);
            inbox.Margin = new Thickness(0, 0, 4, 0);
            inbox.Click += (_, _) => { Close(); openInbox(); };
            quick.Children.Add(inbox);
            var folders = IconButton(Words.T("监控目录", "Watched folders"), ModeActionStyle, "M 2,4 L 6,4 L 7,6 L 14,6 L 14,13 L 2,13 Z", Ink, 38);
            folders.Margin = new Thickness(4, 0, 0, 0);
            folders.Click += (_, _) => Navigate(Page.Folders);
            Grid.SetColumn(folders, 1);
            quick.Children.Add(folders);
            modeContent.Children.Add(quick);
            modeContent.Children.Add(Gap(4));
            modeContent.Children.Add(ToggleRow(Words.T("收集剪贴板图片", "Collect clipboard images"), preferences.ClipboardEnabled, async () =>
            {
                preferences.ClipboardEnabled = !preferences.ClipboardEnabled;
                await Persist(false, false);
                Navigate(Page.Home);
            }));
            modeContent.Children.Add(CapacityRow(false));
            modeContent.Children.Add(ScaleRow());
        }
        panel.Children.Add(modeFrame);
        panel.Children.Add(Gap(10));
        panel.Children.Add(Rule());
        panel.Children.Add(Gap(5));
        panel.Children.Add(ToggleRow(Words.T("登录 Windows 后启动", "Start with Windows"), StartupEnabled(), () =>
        {
            try { SetStartup(!StartupEnabled()); message = null; }
            catch (Exception ex) { message = ex.Message; }
            Navigate(Page.Home);
        }));
        panel.Children.Add(NavigationRow(Words.T("外观", "Appearance"), AppearanceText(), Page.Appearance));
        panel.Children.Add(NavigationRow(Words.T("界面语言", "Language"), preferences.Language == "zh" ? "简体中文" : "English", Page.Language));
        panel.Children.Add(NavigationRow(Words.T("快捷键设置", "Keyboard shortcuts"), "", Page.Shortcut));
        if (!string.IsNullOrWhiteSpace(message))
        {
            panel.Children.Add(Label(message, 10, Alert));
            panel.Children.Add(Gap(5));
        }
        return panel;
    }

    private StackPanel Folders()
    {
        var panel = Subpage();
        panel.Children.Add(Section(Words.T("默认监控", "ALWAYS WATCHING")));
        panel.Children.Add(PathCard(Collectors.DefaultFolder()));
        panel.Children.Add(Gap(15));
        panel.Children.Add(Section(Words.T("额外目录", "ADDITIONAL FOLDERS")));
        if (preferences.WatchFolders.Count == 0)
            panel.Children.Add(Label(Words.T("尚未添加额外目录", "No additional folders"), 11, Muted));
        foreach (var path in preferences.WatchFolders.ToArray())
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 7) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(PathCard(path));
            var remove = Button(Words.T("移除", "Remove"), DangerStyle, 34);
            remove.Width = 55;
            remove.Margin = new Thickness(7, 0, 0, 0);
            remove.Click += async (_, _) =>
            {
                preferences.WatchFolders.Remove(path);
                await Persist(false, true);
                Navigate(Page.Folders);
            };
            Grid.SetColumn(remove, 1);
            row.Children.Add(remove);
            panel.Children.Add(row);
        }
        panel.Children.Add(Gap(8));
        var add = Button(Words.T("＋ 添加目录", "＋ Add folder"), PrimaryStyle, 39);
        add.Click += async (_, _) =>
        {
            folderDialogOpen = true;
            string? chosen = null;
            try
            {
                using var dialog = new Forms.FolderBrowserDialog { Description = Words.T("选择截图目录", "Select screenshot folder"), UseDescriptionForTitle = true };
                if (dialog.ShowDialog(new DialogOwner(new WindowInteropHelper(this).Handle)) == Forms.DialogResult.OK)
                    chosen = dialog.SelectedPath;
            }
            catch (Exception ex) { message = ex.Message; }
            finally { folderDialogOpen = false; Activate(); }
            if (chosen != null && !preferences.WatchFolders.Contains(chosen, StringComparer.OrdinalIgnoreCase))
            {
                preferences.WatchFolders.Add(chosen);
                await Persist(false, true);
            }
            Navigate(Page.Folders);
        };
        panel.Children.Add(add);
        panel.Children.Add(Gap(14));
        panel.Children.Add(Label(Words.T("收件箱也会一直监控。移除目录不会删除其中的图片。", "The inbox is always watched. Removing a folder does not delete its images."), 10, Muted));
        return panel;
    }

    private StackPanel ScaleRow()
    {
        var panel = new StackPanel();
        var row = new Grid { Height = 44, Margin = new Thickness(9, 0, 9, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Words.T("截图缩放比例", "Screenshot scale");
        row.Children.Add(Label(title, 12, Ink));
        var choices = new ComboBox { Style = CapacityStyle };
        choices.DropDownOpened += (_, _) => openCapacityChoice = choices;
        choices.DropDownClosed += (_, _) => { if (ReferenceEquals(openCapacityChoice, choices)) openCapacityChoice = null; };
        System.Windows.Automation.AutomationProperties.SetName(choices, title);
        foreach (var value in new[] { 5, 10, 15, 20, 25 })
            choices.Items.Add(new ComboBoxItem { Tag = value, Content = $"{value}%" });
        choices.SelectedItem = choices.Items.Cast<ComboBoxItem>().First(item => (int)item.Tag == preferences.ImageScalePercent);
        Grid.SetColumn(choices, 1);
        row.Children.Add(choices);
        panel.Children.Add(row);
        var error = Label("", 10, Alert);
        error.Margin = new Thickness(9, 0, 9, 5);
        error.Visibility = Visibility.Collapsed;
        panel.Children.Add(error);
        choices.SelectionChanged += async (_, _) =>
        {
            if (choices.SelectedItem is not ComboBoxItem item || (int)item.Tag == preferences.ImageScalePercent) return;
            choices.IsEnabled = false;
            preferences.ImageScalePercent = (int)item.Tag;
            await Persist(true, false);
            choices.IsEnabled = true;
            error.Text = message ?? "";
            error.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
            FitHeight();
        };
        return panel;
    }

    private StackPanel CapacityRow(bool text)
    {
        var panel = new StackPanel();
        var row = new Grid { Height = 44, Margin = new Thickness(9, 0, 9, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = text ? Words.T("文本保留数量", "Text limit") : Words.T("图片保留数量", "Image limit");
        row.Children.Add(Label(title, 12, Ink));
        var choices = new ComboBox { Style = CapacityStyle };
        choices.DropDownOpened += (_, _) => openCapacityChoice = choices;
        choices.DropDownClosed += (_, _) => { if (ReferenceEquals(openCapacityChoice, choices)) openCapacityChoice = null; };
        System.Windows.Automation.AutomationProperties.SetName(choices, title);
        foreach (var value in new[] { 25, 50, 100 })
            choices.Items.Add(new ComboBoxItem
            {
                Tag = value,
                Content = text ? Words.T($"{value} 条", $"{value} entries") : Words.T($"{value} 张", $"{value} images")
            });
        int CurrentValue() => text ? preferences.TextCapacity : preferences.Capacity;
        choices.SelectedItem = choices.Items.Cast<ComboBoxItem>().First(item => (int)item.Tag == CurrentValue());
        Grid.SetColumn(choices, 1);
        row.Children.Add(choices);
        panel.Children.Add(row);
        var confirmation = new StackPanel { Margin = new Thickness(9, 5, 9, 9), Visibility = Visibility.Collapsed };
        panel.Children.Add(confirmation);

        async Task Apply(int value)
        {
            choices.IsEnabled = false;
            confirmation.IsEnabled = false;
            if (text) preferences.TextCapacity = value;
            else preferences.Capacity = value;
            await Persist(!text, false);
            choices.IsEnabled = true;
            confirmation.IsEnabled = true;
            confirmation.Children.Clear();
            confirmation.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
            if (!string.IsNullOrWhiteSpace(message)) confirmation.Children.Add(Label(message, 10, Alert));
            header.Content = BrandHeader();
            FitHeight();
        }

        choices.SelectionChanged += async (_, _) =>
        {
            if (choices.SelectedItem is not ComboBoxItem item) return;
            var value = (int)item.Tag;
            confirmation.Children.Clear();
            confirmation.Visibility = Visibility.Collapsed;
            if (value == CurrentValue()) { FitHeight(); return; }
            var count = text ? textCount() : captureCount();
            var limit = value;
            if (count <= limit) { await Apply(value); return; }
            confirmation.Visibility = Visibility.Visible;
            confirmation.Children.Add(Label(text
                ? Words.T($"将移除 {count - limit} 条较早的文本。", $"{count - limit} older text entries will be removed.")
                : Words.T($"将移除 {count - limit} 张较旧图片；收件箱图片会移入回收站，外部图片只从列表移除。", $"{count - limit} older images will be removed. Inbox images go to Recycle Bin; external images are only removed from the list."), 10, Alert));
            var actions = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            actions.ColumnDefinitions.Add(new ColumnDefinition());
            actions.ColumnDefinitions.Add(new ColumnDefinition());
            var confirm = Button(Words.T("确认修改", "Confirm change"), PrimaryStyle, 32);
            confirm.Margin = new Thickness(0, 0, 4, 0);
            confirm.Click += async (_, _) => await Apply(value);
            actions.Children.Add(confirm);
            var cancel = Button(Words.T("取消", "Cancel"), SoftStyle, 32);
            cancel.Margin = new Thickness(4, 0, 0, 0);
            cancel.Click += (_, _) => choices.SelectedItem = choices.Items.Cast<ComboBoxItem>().First(option => (int)option.Tag == CurrentValue());
            Grid.SetColumn(cancel, 1);
            actions.Children.Add(cancel);
            confirmation.Children.Add(actions);
            FitHeight();
        };
        return panel;
    }
    private StackPanel LanguagePage()
    {
        var panel = Subpage();
        foreach (var (code, name) in new[] { ("zh", "简体中文"), ("en", "English") })
        {
            var selected = preferences.Language == code;
            var choice = Button(selected ? $"✓  {name}" : $"○  {name}", selected ? SoftStyle : RowStyle, 43);
            choice.Margin = new Thickness(0, 0, 0, 5);
            choice.Click += async (_, _) =>
            {
                preferences.Language = code;
                await Persist(true, false);
                Navigate(Page.Language);
            };
            panel.Children.Add(choice);
        }
        return panel;
    }

    private StackPanel AppearancePage()
    {
        var panel = Subpage();
        foreach (var (code, title) in new[]
        {
            (LineAppearance.Hemp, Words.T("原木", "Natural wood")),
            (LineAppearance.Classic, Words.T("马卡龙", "Macaron"))
        })
        {
            var selected = preferences.Appearance == code;
            var choice = new Button { Style = RowStyle, Padding = new Thickness(5), Margin = new Thickness(0, 0, 0, 9) };
            var frame = new Border
            {
                CornerRadius = new CornerRadius(12),
                BorderBrush = selected ? Accent : Outline,
                BorderThickness = new Thickness(selected ? 2 : 1),
                Padding = new Thickness(7),
                Background = Surface
            };
            var contents = new StackPanel();
            contents.Children.Add(Label((selected ? "✓  " : "○  ") + title, 12, Ink, FontWeights.Medium));
            contents.Children.Add(Gap(8));
            contents.Children.Add(AppearancePreview(code));
            frame.Child = contents;
            choice.Content = frame;
            choice.Click += async (_, _) =>
            {
                if (preferences.Appearance == code) return;
                preferences.Appearance = code;
                await Persist(true, false);
                Navigate(Page.Appearance);
            };
            panel.Children.Add(choice);
        }
        return panel;
    }

    private FrameworkElement AppearancePreview(string appearance)
    {
        var preview = new Grid { Width = 280, Height = 148, Background = LineAppearance.PreviewSurface(appearance), ClipToBounds = true };
        preview.Children.Add(LineAppearance.Rope(appearance));
        var cardLayer = new Canvas { IsHitTestVisible = false };
        var samples = previewCaptures();
        for (var index = 0; index < 2; index++)
        {
            var sample = new Grid { Width = 80, Height = 100 };
            var card = new Border
            {
                Width = 74, Height = 66, Margin = new Thickness(0, LineAppearance.CardTop, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                CornerRadius = new CornerRadius(10),
                BorderBrush = LineAppearance.CardOutline(appearance),
                BorderThickness = new Thickness(1),
                Background = LineAppearance.CardSurface(appearance),
                Padding = new Thickness(7),
                Effect = new DropShadowEffect { Color = Color.FromRgb(67, 57, 52), BlurRadius = 8, ShadowDepth = 2, Opacity = 0.12 }
            };
            if (preferences.DisplayMode == "text")
                card.Child = new TextBlock { Text = Words.T("短文本预览\n长文本会自动折行并省略", "Text preview\nLong text wraps and truncates"), Foreground = Ink, FontSize = 9, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            else if (index < samples.Length && samples[index].Thumbnail != null)
                card.Child = new Image { Source = samples[index].Thumbnail, Stretch = Stretch.Uniform };
            else
                card.Child = new Border { Background = new SolidColorBrush(index == 0 ? Color.FromRgb(242, 231, 224) : Color.FromRgb(231, 239, 237)), CornerRadius = new CornerRadius(4) };
            sample.Children.Add(card);
            sample.Children.Add(LineAppearance.Peg(appearance, index));
            Canvas.SetLeft(sample, 28 + index * 96);
            Canvas.SetTop(sample, LineAppearance.PegTop(28 + index * 96 + sample.Width / 2, preview.Width));
            cardLayer.Children.Add(sample);
        }
        preview.Children.Add(cardLayer);
        return preview;
    }

    private StackPanel Shortcut()
    {
        var panel = Subpage();
        panel.Children.Add(ShortcutField(false));
        panel.Children.Add(Gap(18));
        panel.Children.Add(ShortcutField(true));
        return panel;
    }

    private StackPanel ShortcutField(bool mode)
    {
        var panel = new StackPanel();
        panel.Children.Add(Label(mode ? Words.T("切换文本 / 截图", "Switch text / screenshots") : Words.T("收起 / 展开晾衣绳", "Hide / show the line"), 12, Ink, FontWeights.Medium));
        panel.Children.Add(Gap(7));
        var field = new TextBox
        {
            Text = mode ? ShortcutText(preferences.ModeHotkeyModifiers, preferences.ModeHotkeyKey) : ShortcutText(preferences.HotkeyModifiers, preferences.HotkeyKey),
            IsReadOnly = true,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Ink,
            FontSize = 17,
            Padding = new Thickness(11, 9, 11, 9),
            Cursor = Cursors.Hand
        };
        var fieldFrame = new Border { Child = field, CornerRadius = new CornerRadius(9), BorderBrush = Outline, BorderThickness = new Thickness(1), Background = Soft };
        panel.Children.Add(fieldFrame);
        panel.Children.Add(Gap(10));
        var error = Label(Words.T("组合键必须包含 Ctrl 或 Alt。", "The shortcut must include Ctrl or Alt."), 10, Muted);
        panel.Children.Add(error);
        field.PreviewKeyDown += async (_, e) =>
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
            var pressed = Keyboard.Modifiers;
            if ((pressed & (ModifierKeys.Control | ModifierKeys.Alt)) == 0)
            {
                error.Text = Words.T("请同时按下 Ctrl 或 Alt。", "Hold Ctrl or Alt with the key.");
                error.Foreground = Alert;
                return;
            }
            var modifiers = ((pressed & ModifierKeys.Alt) != 0 ? 1u : 0) |
                            ((pressed & ModifierKeys.Control) != 0 ? 2u : 0) |
                            ((pressed & ModifierKeys.Shift) != 0 ? 4u : 0);
            var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
            var oldModifiers = mode ? preferences.ModeHotkeyModifiers : preferences.HotkeyModifiers;
            var oldKey = mode ? preferences.ModeHotkeyKey : preferences.HotkeyKey;
            if (modifiers == oldModifiers && virtualKey == oldKey) return;
            if (!registerShortcut(mode, modifiers, virtualKey))
            {
                error.Text = Words.T("该组合键已被占用，请换一个。", "That shortcut is already in use.");
                error.Foreground = Alert;
                return;
            }
            if (mode) { preferences.ModeHotkeyModifiers = modifiers; preferences.ModeHotkeyKey = virtualKey; }
            else { preferences.HotkeyModifiers = modifiers; preferences.HotkeyKey = virtualKey; }
            await Persist(false, false);
            field.Text = ShortcutText(modifiers, virtualKey);
            error.Text = string.IsNullOrWhiteSpace(message)
                ? Words.T("已保存，新的快捷键立即生效。", "Saved. The new shortcut works now.")
                : message;
            error.Foreground = string.IsNullOrWhiteSpace(message) ? Accent : Alert;
        };
        return panel;
    }

    private StackPanel Subpage()
    {
        var panel = new StackPanel();
        if (!string.IsNullOrWhiteSpace(message))
        {
            panel.Children.Add(Label(message, 10, Alert));
            panel.Children.Add(Gap(10));
        }
        return panel;
    }

    private Button NavigationRow(string title, string detail, Page page)
    {
        var button = new Button { Style = RowStyle, Height = 42, Padding = new Thickness(9, 0, 9, 0) };
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        row.Children.Add(Label(title, 12, Ink));
        var value = Label(detail, 10, Muted);
        value.VerticalAlignment = VerticalAlignment.Center;
        value.Margin = new Thickness(4, 0, 6, 0);
        Grid.SetColumn(value, 1);
        row.Children.Add(value);
        var arrow = Label("›", 19, Muted);
        arrow.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(arrow, 2);
        row.Children.Add(arrow);
        button.Content = row;
        button.Click += (_, _) => Navigate(page);
        return button;
    }

    private static Button ToggleRow(string title, bool enabled, Action toggle)
    {
        var button = new Button { Style = RowStyle, Height = 46, Padding = new Thickness(9, 0, 9, 0) };
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(Label(title, 12, Ink));
        var track = new Border
        {
            Width = 42, Height = 23, CornerRadius = new CornerRadius(12),
            Background = enabled ? Accent : Outline,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new Ellipse { Width = 17, Height = 17, Fill = Brushes.White, Margin = new Thickness(3), HorizontalAlignment = enabled ? HorizontalAlignment.Right : HorizontalAlignment.Left }
        };
        Grid.SetColumn(track, 1);
        row.Children.Add(track);
        button.Content = row;
        button.Click += (_, _) => toggle();
        return button;
    }

    private static Border PathCard(string path) => new()
    {
        Background = Soft,
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(9, 7, 9, 7),
        Child = Label(path, 10, Ink)
    };

    private static Button Button(string text, Style style, double height) => new()
    {
        Content = Label(text, 12, style == PrimaryStyle ? Brushes.White : style == DangerStyle ? Alert : Ink, FontWeights.Medium, TextAlignment.Center),
        Style = style,
        Height = height,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };

    private static Button IconButton(string text, Style style, string geometry, Brush color, double height)
    {
        var button = Button(text, style, height);
        var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(geometry), Width = 16, Height = 16, Stretch = Stretch.Uniform,
            Stroke = color, StrokeThickness = 1.4, StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center
        });
        content.Children.Add(Label(text, 12, color, FontWeights.Medium, TextAlignment.Center));
        button.Content = content;
        return button;
    }

    private static TextBlock Label(string text, double size, Brush color, FontWeight? weight = null, TextAlignment alignment = TextAlignment.Left) => new()
    {
        Text = text,
        Foreground = color,
        FontSize = size,
        FontWeight = weight ?? FontWeights.Normal,
        TextAlignment = alignment,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static FrameworkElement Gap(double height) => new Border { Height = height };
    private static Border Rule() => new() { Height = 1, Background = Outline };
    private static TextBlock Section(string title) => new()
    {
        Text = title,
        Foreground = Muted,
        FontSize = 10,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(8, 0, 0, 4)
    };

    private static Style ButtonStyle(Brush normal, Brush hover, Brush pressed, Brush foreground, double radius)
    {
        var frame = new FrameworkElementFactory(typeof(Border));
        frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        frame.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Control.Background)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        frame.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding(nameof(Control.Padding)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        frame.AppendChild(content);
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Button)) { VisualTree = frame }));
        style.Setters.Add(new Setter(Control.BackgroundProperty, normal));
        style.Setters.Add(new Setter(Control.ForegroundProperty, foreground));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 5, 10, 5)));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        over.Setters.Add(new Setter(Control.BackgroundProperty, hover));
        style.Triggers.Add(over);
        var down = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
        down.Setters.Add(new Setter(Control.BackgroundProperty, pressed));
        style.Triggers.Add(down);
        return style;
    }

    private static Style CreateCapacityStyle()
    {
        const string xaml = """
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ComboBox">
                <Setter Property="MinWidth" Value="146" />
                <Setter Property="Height" Value="32" />
                <Setter Property="FontSize" Value="11" />
                <Setter Property="VerticalAlignment" Value="Center" />
                <Setter Property="Foreground" Value="{DynamicResource Ink}" />
                <Setter Property="Background" Value="{DynamicResource Soft}" />
                <Setter Property="BorderBrush" Value="{DynamicResource Outline}" />
                <Setter Property="ItemContainerStyle">
                    <Setter.Value>
                        <Style TargetType="ComboBoxItem">
                            <Setter Property="Padding" Value="9,7" />
                            <Setter Property="Foreground" Value="{DynamicResource Ink}" />
                            <Setter Property="Background" Value="Transparent" />
                            <Setter Property="Template">
                                <Setter.Value>
                                    <ControlTemplate TargetType="ComboBoxItem">
                                        <Border Background="{TemplateBinding Background}" Padding="{TemplateBinding Padding}" CornerRadius="5">
                                            <ContentPresenter />
                                        </Border>
                                    </ControlTemplate>
                                </Setter.Value>
                            </Setter>
                            <Style.Triggers>
                                <Trigger Property="IsSelected" Value="True"><Setter Property="Background" Value="{DynamicResource Hover}" /></Trigger>
                                <Trigger Property="IsHighlighted" Value="True"><Setter Property="Background" Value="{DynamicResource Hover}" /></Trigger>
                            </Style.Triggers>
                        </Style>
                    </Setter.Value>
                </Setter>
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="ComboBox">
                            <Grid>
                                <ToggleButton x:Name="Toggle" Focusable="False" ClickMode="Press"
                                              Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                                              IsChecked="{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}">
                                    <ToggleButton.Template>
                                        <ControlTemplate TargetType="ToggleButton">
                                            <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="7" />
                                        </ControlTemplate>
                                    </ToggleButton.Template>
                                </ToggleButton>
                                <ContentPresenter Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"
                                                  Margin="9,0,25,0" VerticalAlignment="Center" IsHitTestVisible="False" />
                                <Path Data="M 0 0 L 4 4 L 8 0" Stroke="{DynamicResource Muted}" StrokeThickness="1.3"
                                      HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,10,0" IsHitTestVisible="False" />
                                <Popup x:Name="PART_Popup" IsOpen="{TemplateBinding IsDropDownOpen}" Placement="Bottom" AllowsTransparency="True" Focusable="False">
                                    <Border MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}"
                                            Background="{DynamicResource Surface}" BorderBrush="{DynamicResource Outline}"
                                            BorderThickness="1" CornerRadius="8" Padding="4" Margin="0,4,0,0">
                                        <ScrollViewer MaxHeight="200" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
                                            <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained" />
                                        </ScrollViewer>
                                    </Border>
                                </Popup>
                            </Grid>
                            <ControlTemplate.Triggers>
                                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Toggle" Property="Background" Value="{DynamicResource Hover}" /></Trigger>
                                <Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="Toggle" Property="BorderBrush" Value="{DynamicResource Accent}" /></Trigger>
                                <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.55" /></Trigger>
                            </ControlTemplate.Triggers>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>
            """;
        var style = (Style)XamlReader.Parse(xaml);
        style.Resources["Ink"] = Ink;
        style.Resources["Muted"] = Muted;
        style.Resources["Soft"] = Soft;
        style.Resources["Hover"] = Hover;
        style.Resources["Outline"] = Outline;
        style.Resources["Surface"] = Surface;
        style.Resources["Accent"] = Accent;
        return style;
    }

    private static bool StartupEnabled()
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey);
            return run?.GetValue("SnapLine") != null;
        }
        catch { return false; }
    }

    private static void SetStartup(bool enabled)
    {
        using var run = Registry.CurrentUser.CreateSubKey(RunKey) ?? throw new IOException("无法打开开机启动设置。");
        if (enabled) run.SetValue("SnapLine", "\"" + Environment.ProcessPath + "\"");
        else run.DeleteValue("SnapLine", false);
    }

    private string AppearanceText() => preferences.Appearance == LineAppearance.Classic
        ? Words.T("马卡龙", "Macaron")
        : Words.T("原木", "Natural wood");

    private static string ShortcutText(uint modifiers, uint key) =>
        ((modifiers & 2) != 0 ? "Ctrl+" : "") +
        ((modifiers & 1) != 0 ? "Alt+" : "") +
        ((modifiers & 4) != 0 ? "Shift+" : "") +
        KeyInterop.KeyFromVirtualKey((int)key);

    private async Task Persist(bool refreshLine, bool rewatch)
    {
        try { await savePreferences(refreshLine, rewatch); message = null; }
        catch (Exception ex) { message = ex.Message; }
    }

    private sealed class DialogOwner(IntPtr handle) : Forms.IWin32Window
    {
        public IntPtr Handle { get; } = handle;
    }
}
