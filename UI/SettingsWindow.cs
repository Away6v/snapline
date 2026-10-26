using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace SnapLine;

internal sealed class SettingsWindow : Window
{
    private readonly Preferences preferences;
    private readonly Func<uint, uint, bool> register;
    private readonly CheckBox clipboard;
    private readonly CheckBox startup;
    private readonly ComboBox language;
    private readonly ComboBox capacity;
    private readonly TextBox shortcut;
    private readonly ListBox folders;
    private uint modifiers;
    private uint key;
    public event Action? Saved;
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public SettingsWindow(Preferences preferences, Func<uint, uint, bool> register)
    {
        this.preferences = preferences;
        this.register = register;
        modifiers = preferences.HotkeyModifiers;
        key = preferences.HotkeyKey;
        Title = Words.T("SnapLine · 设置", "SnapLine · Settings");
        Width = 560; Height = 660; MinWidth = 500; MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        var dark = Native.DarkTheme();
        Background = new SolidColorBrush(dark ? Color.FromRgb(28, 34, 44) : Color.FromRgb(246, 248, 252));
        Foreground = dark ? Brushes.WhiteSmoke : Brushes.Black;
        var panel = new StackPanel { Margin = new Thickness(28) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "SnapLine", FontSize = 30, FontWeight = FontWeights.Bold });
        panel.Children.Add(new TextBlock { Text = Words.T("截图，随手可取。所有图片仅保存在本机。", "Screenshots within reach. Everything stays on this PC."), Margin = new Thickness(0, 4, 0, 20) });
        clipboard = new CheckBox { Content = Words.T("收集剪贴板中的纯图片", "Collect images from the clipboard"), IsChecked = preferences.ClipboardEnabled, Margin = new Thickness(0, 0, 0, 12) };
        using (var run = Registry.CurrentUser.OpenSubKey(RunKey))
            startup = new CheckBox { Content = Words.T("登录 Windows 后启动", "Start when I sign in to Windows"), IsChecked = run?.GetValue("SnapLine") != null, Margin = new Thickness(0, 0, 0, 16) };
        panel.Children.Add(clipboard); panel.Children.Add(startup);
        language = new ComboBox { ItemsSource = new[] { "简体中文", "English" }, SelectedIndex = preferences.Language == "zh" ? 0 : 1, Height = 30 };
        AddField(panel, Words.T("界面语言", "Language"), language);
        capacity = new ComboBox { ItemsSource = new[] { Words.T("自动（按屏幕宽度）", "Automatic (screen width)"), "25", "50", "100" }, SelectedIndex = Array.IndexOf(new[] { 0, 25, 50, 100 }, preferences.Capacity), Height = 30 };
        AddField(panel, Words.T("保留数量（较旧的自有图片移入回收站）", "Capacity (older inbox images go to Recycle Bin)"), capacity);
        shortcut = new TextBox { Text = ShortcutText(modifiers, key), IsReadOnly = true, Height = 32, Padding = new Thickness(6), ToolTip = Words.T("点击后按下新组合，必须包含 Ctrl 或 Alt。", "Click and press a shortcut containing Ctrl or Alt.") };
        shortcut.PreviewKeyDown += (_, e) =>
        {
            e.Handled = true;
            var actual = e.Key == Key.System ? e.SystemKey : e.Key;
            if (actual is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
            var pressed = Keyboard.Modifiers;
            if ((pressed & (ModifierKeys.Control | ModifierKeys.Alt)) == 0) return;
            modifiers = ((pressed & ModifierKeys.Alt) != 0 ? 1u : 0) | ((pressed & ModifierKeys.Control) != 0 ? 2u : 0) | ((pressed & ModifierKeys.Shift) != 0 ? 4u : 0);
            key = (uint)KeyInterop.VirtualKeyFromKey(actual);
            shortcut.Text = ShortcutText(modifiers, key);
        };
        AddField(panel, Words.T("展开快捷键（点击后按组合键）", "Show shortcut (click, then press keys)"), shortcut);
        panel.Children.Add(new TextBlock { Text = Words.T("额外监控目录", "Additional watched folders"), Margin = new Thickness(0, 12, 0, 5) });
        folders = new ListBox { Height = 88 };
        foreach (var folder in preferences.WatchFolders) folders.Items.Add(folder);
        panel.Children.Add(folders);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var add = Button(Words.T("添加目录", "Add folder"));
        add.Click += (_, _) =>
        {
            using var dialog = new Forms.FolderBrowserDialog { Description = Words.T("选择截图目录", "Select screenshot folder"), UseDescriptionForTitle = true };
            if (dialog.ShowDialog() == Forms.DialogResult.OK && !folders.Items.Cast<string>().Contains(dialog.SelectedPath, StringComparer.OrdinalIgnoreCase)) folders.Items.Add(dialog.SelectedPath);
        };
        var remove = Button(Words.T("移除", "Remove"));
        remove.Click += (_, _) => { if (folders.SelectedItem != null) folders.Items.Remove(folders.SelectedItem); };
        actions.Children.Add(add); actions.Children.Add(remove); panel.Children.Add(actions);
        panel.Children.Add(new TextBlock { Text = Words.T("默认同时监控：", "Always watching: ") + Collectors.DefaultFolder(), TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 7, 0, 0), Opacity = 0.7 });
        var save = Button(Words.T("保存设置", "Save settings")); save.HorizontalAlignment = HorizontalAlignment.Right; save.Margin = new Thickness(0, 18, 0, 0);
        save.Click += Save;
        panel.Children.Add(save);
    }
    private void Save(object sender, RoutedEventArgs e)
    {
        var changed = modifiers != preferences.HotkeyModifiers || key != preferences.HotkeyKey;
        if (changed && !register(modifiers, key)) { MessageBox.Show(Words.T("该快捷键已被占用，请选择其他组合。", "That shortcut is already in use. Choose another."), "SnapLine"); return; }
        try
        {
            using var run = Registry.CurrentUser.CreateSubKey(RunKey);
            if (startup.IsChecked == true) run.SetValue("SnapLine", "\"" + Environment.ProcessPath + "\"");
            else run.DeleteValue("SnapLine", false);
        }
        catch (Exception ex)
        {
            if (changed) register(preferences.HotkeyModifiers, preferences.HotkeyKey);
            MessageBox.Show(Words.T("无法保存启动设置：", "Cannot save startup setting: ") + ex.Message, "SnapLine"); return;
        }
        preferences.ClipboardEnabled = clipboard.IsChecked == true;
        preferences.Language = language.SelectedIndex == 0 ? "zh" : "en";
        preferences.Capacity = new[] { 0, 25, 50, 100 }[Math.Max(0, capacity.SelectedIndex)];
        preferences.HotkeyModifiers = modifiers; preferences.HotkeyKey = key;
        preferences.WatchFolders = folders.Items.Cast<string>().ToList();
        Saved?.Invoke();
        Close();
    }
    private static Button Button(string title) => new() { Content = title, Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0) };
    private static void AddField(Panel panel, string title, Control control)
    {
        control.Foreground = Brushes.Black;
        panel.Children.Add(new TextBlock { Text = title, Margin = new Thickness(0, 7, 0, 5) });
        panel.Children.Add(control);
    }
    internal static string ShortcutText(uint modifiers, uint key) => ((modifiers & 2) != 0 ? "Ctrl+" : "") + ((modifiers & 1) != 0 ? "Alt+" : "") + ((modifiers & 4) != 0 ? "Shift+" : "") + KeyInterop.KeyFromVirtualKey((int)key);
}
