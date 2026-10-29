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
    private TrayPanelWindow? trayPanel;
    private State state = new();
    private CaptureLibrary library = null!;
    private TextLibrary textLibrary = null!;
    private DateTime lastNotice;
    private readonly UpdateStatus updateStatus = new();
    [STAThread]
    public static void Main()
    {
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        ThemeScrollBars.Apply(app);
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
            var migrationFailures = Storage.MigrateInbox(state);
            var recovered = Storage.Recovered;
            Words.Language = state.Preferences.Language;
            library = new CaptureLibrary(state);
            library.Error += Notify;
            await library.RestoreAsync();
            textLibrary = new TextLibrary(state);
            textLibrary.Error += Notify;
            textLibrary.Restore();
            var cleanupFailures = Storage.CompleteMigration(state, migrationFailures);
            collectors = new Collectors(library, textLibrary, state.Preferences);
            collectors.Error += Notify;
            line = new LineWindow(library, textLibrary, state.Preferences, Notify);
            MainWindow = line;
            collectors.ToggleRequested += line.Toggle;
            collectors.ModeToggleRequested += () =>
            {
                line.SetDisplayMode(state.Preferences.DisplayMode == "text" ? "screenshot" : "text");
                library.Save();
                trayPanel?.RefreshDisplayMode();
            };
            CreateTray();
            if (!collectors.RegisterShortcut(false, state.Preferences.HotkeyModifiers, state.Preferences.HotkeyKey))
                Notify(Words.T("显示快捷键已被占用，请在托盘面板中修改。", "Show shortcut is already in use. Change it in the tray panel."));
            if (!collectors.RegisterShortcut(true, state.Preferences.ModeHotkeyModifiers, state.Preferences.ModeHotkeyKey))
                Notify(Words.T("模式切换快捷键已被占用，请在托盘面板中修改。", "Mode shortcut is already in use. Change it in the tray panel."));
            if (!collectors.ClipboardAvailable) Notify(Words.T("剪贴板监听启动失败，目录收集仍可用。", "Clipboard listener failed; folder collection is still available."));
            collectors.Rewatch();
            line.Start();
            if (migrationFailures > 0 || cleanupFailures > 0) Notify(Words.T("部分旧数据未能迁入程序包，请检查 Data 文件夹。", "Some old data could not be moved into the app package. Check the Data folder."));
            if (recovered) Notify(Words.T("配置无法读取，已使用默认设置；原配置已保留为恢复副本。", "Settings could not be read. Defaults are in use; the original was kept as a recovery copy."));
            if (!state.Preferences.OnboardingComplete)
            {
                MessageBox.Show(Words.T(
                    "欢迎使用截图晾衣绳！\n\n已默认开启剪贴板截图与文本收集，内容仅保存在本机。复制的文件及本应用复制的内容会被忽略。\n\n鼠标停留屏幕顶部，或按 Ctrl+Alt+T 展开。托盘右键面板可切换“文本 / 截图”、新建文本及调整收集设置。\n\n截图卡片可复制、预览、编辑或拖出分享；文本卡片可复制、查看和编辑。\n\n右键托盘图标打开控制面板。检查更新时会连接 GitHub，内容不会上传。",
                    "Welcome to SnapLine!\n\nClipboard image and text collection are on by default. Content stays on this device. Copied files and SnapLine's own copies are ignored.\n\nRest the pointer at the top edge or press Ctrl+Alt+T. Right-click the tray icon to switch between Text and Screenshots, create text, and adjust collection settings.\n\nScreenshot cards can be copied, previewed, edited, or dragged to share. Text cards can be copied, viewed, and edited.\n\nChecking for updates connects to GitHub. Your content is not uploaded."), "SnapLine", MessageBoxButton.OK, MessageBoxImage.Information);
                state.Preferences.OnboardingComplete = true;
                library.Save();
                line.Toggle();
            }
            else if (Array.Exists(e.Args, arg => arg.Equals("--show", StringComparison.OrdinalIgnoreCase)))
            {
                // 手动测试时允许启动后直接展开晾衣绳。
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
        if (tray != null) return;
        using var resource = GetResourceStream(new Uri("pack://application:,,,/Assets/snapline.ico")).Stream;
        tray = new Forms.NotifyIcon { Icon = new System.Drawing.Icon(resource), Visible = true, Text = "SnapLine" };
        tray.DoubleClick += (_, _) => { trayPanel?.Close(); line?.Toggle(); };
        tray.MouseUp += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Right) Dispatcher.BeginInvoke(ShowTrayPanel);
        };
    }
    private void ShowTrayPanel()
    {
        if (trayPanel is { IsVisible: true }) { trayPanel.Close(); return; }
        var panel = new TrayPanelWindow(state.Preferences, () => library.Items.Count, () => textLibrary.Items.Count, () => System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Take(library.Items, 2)), () => line?.IsRevealed == true,
            () => line?.Toggle(), OpenInbox, () => Shutdown(),
            (mode, modifiers, key) => collectors!.RegisterShortcut(mode, modifiers, key), ApplyTraySettingsAsync,
            mode => line?.SetDisplayMode(mode), () => line?.CreateText(), updateStatus, () => _ = CheckUpdatesAsync(), () => _ = InstallUpdateAsync());
        trayPanel = panel;
        panel.Closed += (_, _) => { if (ReferenceEquals(trayPanel, panel)) trayPanel = null; };
        var cursor = Forms.Cursor.Position;
        panel.PrepareNear(cursor);
        panel.Show();
        panel.PlaceNear(cursor);
        panel.Activate();
        Dispatcher.BeginInvoke(() => { if (panel.IsVisible) panel.Activate(); }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }
    private async Task ApplyTraySettingsAsync(bool refreshLine, bool rewatch)
    {
        Words.Language = state.Preferences.Language;
        library.Save();
        textLibrary.SetCapacity(state.Preferences.TextCapacity);
        if (rewatch) collectors!.Rewatch();
        if (refreshLine) await line!.RefreshSettingsAsync();
    }
    private async Task CheckUpdatesAsync()
    {
        if (updateStatus.Busy) return;
        updateStatus.Busy = true;
        updateStatus.Release = null;
        updateStatus.Error = false;
        updateStatus.Text = Words.T("正在检查更新…", "Checking for updates…");
        trayPanel?.RefreshUpdate();
        try
        {
            updateStatus.Release = await UpdateService.CheckAsync();
            updateStatus.Text = updateStatus.Release == null
                ? Words.T("当前已是最新版本。", "You are using the latest version.")
                : Words.T("发现新版本 ", "New version ") + updateStatus.Release.Version;
        }
        catch (Exception ex)
        {
            updateStatus.Error = true;
            updateStatus.Text = Words.T("检查更新失败：", "Update check failed: ") + ex.Message;
        }
        finally
        {
            updateStatus.Busy = false;
            trayPanel?.RefreshUpdate();
        }
    }
    private async Task InstallUpdateAsync()
    {
        var release = updateStatus.Release;
        if (updateStatus.Busy || release == null) return;
        updateStatus.Busy = true;
        updateStatus.Error = false;
        updateStatus.Text = Words.T("正在下载并校验更新，请稍候…", "Downloading and verifying the update…");
        trayPanel?.RefreshUpdate();
        try
        {
            var progress = new Progress<string>(text =>
            {
                updateStatus.Text = text;
                trayPanel?.RefreshUpdate();
            });
            await UpdateService.DownloadAndInstallAsync(release, progress);
            Shutdown();
        }
        catch (Exception ex)
        {
            updateStatus.Error = true;
            updateStatus.Text = Words.T("更新失败：", "Update failed: ") + ex.Message;
            updateStatus.Busy = false;
            trayPanel?.RefreshUpdate();
        }
    }
    private void OpenInbox()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { Storage.Inbox }, UseShellExecute = true }); }
        catch (Exception ex) { Notify(ex.Message); }
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
        trayPanel?.Close();
        line?.Stop();
        collectors?.Dispose();
        if (tray != null) { tray.Visible = false; tray.Icon?.Dispose(); tray.Dispose(); }
        library?.Save();
        instance?.Dispose();
        base.OnExit(e);
    }
}

internal sealed class UpdateStatus
{
    internal bool Busy;
    internal bool Error;
    internal string? Text;
    internal UpdateRelease? Release;
}
