using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace SnapLine;

public sealed class App : Application
{
    private Mutex? instance;
    private Forms.NotifyIcon? tray;
    private Collectors? collectors;
    private LineWindow? line;
    private SettingsWindow? settings;
    private State state = new();
    private CaptureLibrary library = null!;
    private DateTime lastNotice;
    [STAThread]
    public static void Main()
    {
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Run();
    }
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = new Mutex(true, @"Local\SnapLine.Desktop", out var first);
        if (!first) { MessageBox.Show("SnapLine 已在托盘运行 / SnapLine is already running in the tray.", "SnapLine"); Shutdown(); return; }
        try
        {
            state = Storage.Load();
            var recovered = Storage.Recovered;
            Words.Language = state.Preferences.Language;
            library = new CaptureLibrary(state);
            library.Error += Notify;
            await library.RestoreAsync();
            collectors = new Collectors(library, state.Preferences);
            collectors.Error += Notify;
            line = new LineWindow(library, state.Preferences, Notify, ShowSettings);
            MainWindow = line;
            collectors.ToggleRequested += line.Toggle;
            CreateTray();
            if (!collectors.RegisterShortcut(state.Preferences.HotkeyModifiers, state.Preferences.HotkeyKey))
                Notify(Words.T("显示快捷键已被占用，请在设置中修改。", "Show shortcut is already in use. Change it in Settings."));
            if (!collectors.ClipboardAvailable) Notify(Words.T("剪贴板监听启动失败，目录收集仍可用。", "Clipboard listener failed; folder collection is still available."));
            collectors.Rewatch();
            line.Start();
            if (recovered) Notify(Words.T("配置无法读取，已使用默认设置；原配置已保留为恢复副本。", "Settings could not be read. Defaults are in use; the original was kept as a recovery copy."));
            if (!state.Preferences.OnboardingComplete)
            {
                MessageBox.Show(Words.T(
                    "欢迎使用截图晾衣绳！\n\n已默认开启“收集剪贴板图片”：Win+Shift+S 截图和其他纯图片复制会保存在本机。文字、复制的文件和本应用复制的图片会被忽略。\n\n鼠标停留屏幕顶部，或按 Ctrl+Alt+T 展开。单击复制、双击打开、长按用画图编辑，拖出即可分享。\n\n托盘图标 → 设置，可关闭收集、选择目录或更改快捷键。程序不联网，不上传图片。",
                    "Welcome to SnapLine!\n\nClipboard image collection is ON: Win+Shift+S captures and other copied images are saved locally. Text, copied files, and SnapLine's own copies are ignored.\n\nRest the pointer at the top edge or press Ctrl+Alt+T. Click to copy, double-click to open, hold to edit in Paint, or drag to share.\n\nUse the tray → Settings to disable collection, select folders, or change the shortcut. No network access or uploads."), "SnapLine", MessageBoxButton.OK, MessageBoxImage.Information);
                state.Preferences.OnboardingComplete = true;
                library.Save();
                line.Toggle();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(Words.T("SnapLine 无法启动：\n", "SnapLine could not start:\n") + ex.Message, "SnapLine", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    private void CreateTray()
    {
        if (tray == null)
        {
            using var resource = GetResourceStream(new Uri("pack://application:,,,/Assets/snapline.ico")).Stream;
            tray = new Forms.NotifyIcon { Icon = new System.Drawing.Icon(resource), Visible = true, Text = "SnapLine" };
            tray.DoubleClick += (_, _) => line?.Toggle();
        }
        var old = tray.ContextMenuStrip;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(Words.T("展开 / 收起", "Show / hide"), null, (_, _) => line?.Toggle());
        var clipboard = new Forms.ToolStripMenuItem(Words.T("收集剪贴板图片", "Collect clipboard images")) { Checked = state.Preferences.ClipboardEnabled, CheckOnClick = true };
        clipboard.CheckedChanged += (_, _) => { state.Preferences.ClipboardEnabled = clipboard.Checked; library.Save(); };
        menu.Items.Add(clipboard);
        menu.Items.Add(Words.T("设置…", "Settings…"), null, (_, _) => ShowSettings());
        menu.Items.Add(Words.T("打开收件箱", "Open inbox"), null, (_, _) => OpenInbox());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Words.T("退出", "Quit"), null, (_, _) => Shutdown());
        tray.ContextMenuStrip = menu;
        old?.Dispose();
    }
    private void OpenInbox()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { Storage.Inbox }, UseShellExecute = true }); }
        catch (Exception ex) { Notify(ex.Message); }
    }
    private void ShowSettings()
    {
        if (settings != null) { settings.Activate(); return; }
        settings = new SettingsWindow(state.Preferences, (modifiers, key) => collectors!.RegisterShortcut(modifiers, key));
        settings.Saved += async () =>
        {
            Words.Language = state.Preferences.Language;
            library.Save();
            collectors!.Rewatch();
            CreateTray();
            await line!.RefreshSettingsAsync();
        };
        settings.Closed += (_, _) => settings = null;
        settings.Show();
    }
    private void Notify(string text)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => Notify(text)); return; }
        if (tray == null) { MessageBox.Show(text, "SnapLine", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (DateTime.UtcNow - lastNotice < TimeSpan.FromSeconds(3)) return;
        lastNotice = DateTime.UtcNow;
        tray.ShowBalloonTip(6000, "SnapLine", text, Forms.ToolTipIcon.Warning);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        line?.Stop();
        collectors?.Dispose();
        if (tray != null) { tray.Visible = false; tray.Icon?.Dispose(); tray.Dispose(); }
        library?.Save();
        instance?.Dispose();
        base.OnExit(e);
    }
}
