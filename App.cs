using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace SnapLine;

public sealed class App : Application
{
#if DEBUG
    internal const string DisplayName = "SnapLine 测试版";
    internal const bool IsDevelopment = true;
    private const string InstanceName = @"Local\SnapLine.Desktop.Development";
#else
    internal const string DisplayName = "SnapLine";
    internal const bool IsDevelopment = false;
    private const string InstanceName = @"Local\SnapLine.Desktop";
#endif
    private Mutex? instance;
    private Forms.NotifyIcon? tray;
    private Collectors? collectors;
    private LineWindow? line;
    private TrayPanelWindow? trayPanel;
    private State state = new();
    private CaptureLibrary library = null!;
    private TextLibrary textLibrary = null!;
    private readonly Queue<string> notices = new();
    private readonly DispatcherTimer noticeTimer = new() { Interval = TimeSpan.FromSeconds(6) };
    private readonly UpdateStatus updateStatus = new();
    [STAThread]
    public static void Main()
    {
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        ThemeScrollBars.Apply(app);
        ThemeToolTips.Apply(app);
        app.Run();
    }
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        noticeTimer.Tick += (_, _) => ShowNextNotice();
        instance = new Mutex(true, InstanceName, out var first);
        if (!first) { MessageBox.Show(DisplayName + " 已在托盘运行 / is already running in the tray.", DisplayName); Shutdown(); return; }
        var updateAck = Environment.GetEnvironmentVariable("SNAPLINE_UPDATE_ACK");
        var updateToken = Environment.GetEnvironmentVariable("SNAPLINE_UPDATE_TOKEN");
        DispatcherTimer? progressTimer = null;
        if (!string.IsNullOrWhiteSpace(updateAck) && !string.IsNullOrWhiteSpace(updateToken))
        {
            void ReportProgress() { try { File.WriteAllText(updateAck + ".progress", updateToken); } catch { } }
            ReportProgress();
            progressTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            progressTimer.Tick += (_, _) => ReportProgress();
            progressTimer.Start();
        }
        try
        {
            state = Storage.Load();
            var migrationFailures = Storage.MigrateInbox(state);
            var recovered = Storage.Recovered;
            Words.Language = state.Preferences.Language;
            library = new CaptureLibrary(state);
            library.Error += Notify;
            library.RecoverInterrupted();
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
            if (notices.Count > 0 && !noticeTimer.IsEnabled) ShowNextNotice();
            if (!collectors.RegisterShortcut(false, state.Preferences.HotkeyModifiers, state.Preferences.HotkeyKey))
                Notify(Words.T("显示快捷键已被占用，请在托盘面板中修改。", "Show shortcut is already in use. Change it in the tray panel."));
            if (!collectors.RegisterShortcut(true, state.Preferences.ModeHotkeyModifiers, state.Preferences.ModeHotkeyKey))
                Notify(Words.T("模式切换快捷键已被占用，请在托盘面板中修改。", "Mode shortcut is already in use. Change it in the tray panel."));
            if (!collectors.ClipboardAvailable) Notify(Words.T("剪贴板监听启动失败，目录收集仍可用。", "Clipboard listener failed; folder collection is still available."));
            collectors.Rewatch();
            line.Start();
            // 安装器只在应用完成初始化后接收健康确认。
            if (!Storage.NeedsManualRecovery && !string.IsNullOrWhiteSpace(updateAck) && !string.IsNullOrWhiteSpace(updateToken))
            {
                try { File.WriteAllText(updateAck, updateToken); }
                catch { }
            }
            progressTimer?.Stop();
            if (migrationFailures > 0 || cleanupFailures > 0) Notify(Words.T("部分旧数据未能迁入程序包，请检查 Data 文件夹。", "Some old data could not be moved into the app package. Check the Data folder."));
            if (recovered) Notify(Storage.NeedsManualRecovery
                ? Words.T("配置及其备份无法读取，已禁止覆盖原数据，请先备份 Data。", "The state and its backup could not be read. Original data is protected; back up Data first.")
                : Words.T("配置无法读取，已从备份恢复；原配置已保留为恢复副本。", "The state was restored from a backup; the original was kept as a recovery copy."));
            if (!state.Preferences.OnboardingComplete)
            {
                MessageBox.Show(Words.T(
                    "欢迎使用截图晾衣绳！\n\n已默认开启剪贴板截图与文本收集，内容仅保存在本机。复制的文件及本应用复制的内容会被忽略。\n\n鼠标停留屏幕顶部，或按 Ctrl+Alt+T 展开。托盘右键面板可切换“文本 / 截图”、新建文本及调整收集设置。\n\n截图卡片可复制、预览、编辑或拖出分享；文本卡片可复制、查看和编辑。\n\n右键托盘图标打开控制面板。检查更新时会连接 GitHub，内容不会上传。",
                    "Welcome to SnapLine!\n\nClipboard image and text collection are on by default. Content stays on this device. Copied files and SnapLine's own copies are ignored.\n\nRest the pointer at the top edge or use the show shortcut. Right-click the tray icon to switch between Text and Screenshots, create text, and adjust collection settings.\n\nScreenshot cards can be copied, previewed, edited, or dragged to share. Text cards can be copied, viewed, and edited.\n\nChecking for updates connects to GitHub. Your content is not uploaded.").Replace("Ctrl+Alt+T", IsDevelopment ? "Ctrl+Alt+Shift+T" : "Ctrl+Alt+T"), DisplayName, MessageBoxButton.OK, MessageBoxImage.Information);
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
            progressTimer?.Stop();
            MessageBox.Show(Words.T(DisplayName + " 无法启动：\n", DisplayName + " could not start:\n") + ex.Message, DisplayName, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    private void CreateTray()
    {
        if (tray != null) return;
        using var resource = GetResourceStream(new Uri("pack://application:,,,/Assets/snapline.ico")).Stream;
        tray = new Forms.NotifyIcon { Icon = new System.Drawing.Icon(resource), Visible = true, Text = DisplayName };
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
            mode => line?.SetDisplayMode(mode), () => line?.CreateText(), updateStatus, () => _ = CheckUpdatesAsync(), () => _ = InstallUpdateAsync(), () => line?.SearchQuery ?? "", query => line?.SetSearchQuery(query), ExportBackup, RestoreBackup);
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
        if (!library.Save()) throw new IOException(Words.T("设置未保存，本次更改可能只在当前运行期间生效。", "Settings were not saved; changes may only apply during this session."));
        if (!textLibrary.SetCapacity(state.Preferences.TextCapacity)) throw new IOException(Words.T("文本数量调整未保存。", "The text limit change was not saved."));
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
    private async void ExportBackup()
    {
        using var dialog = new Forms.SaveFileDialog { Filter = "SnapLine backup (*.zip)|*.zip", FileName = $"SnapLine-{DateTime.Now:yyyyMMdd}.zip", AddExtension = true };
        if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
        try { await library.ExportBackupAsync(dialog.FileName); Notify(Words.T("备份已导出。", "Backup exported.")); }
        catch (Exception ex) { Notify(Words.T("无法导出备份：", "Could not export backup: ") + ex.Message); }
    }
    private void RestoreBackup()
    {
        using var dialog = new Forms.OpenFileDialog { Filter = "SnapLine backup (*.zip)|*.zip", CheckFileExists = true };
        if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
        try
        {
            var missing = BackupService.CountMissingExternalImages(dialog.FileName);
            var warning = Words.T("恢复将替换当前本地数据并重启 SnapLine。", "Restoring replaces current local data and restarts SnapLine.");
            if (missing > 0) warning += Words.T($"\n\n备份中的 {missing} 张外部截图在此电脑上不可用，恢复后这些图片将无法显示。", $"\n\n{missing} external images in this backup are unavailable on this computer and will not be displayed after restoring.");
            if (MessageBox.Show(warning, "SnapLine", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            BackupService.RestoreAndRestart(dialog.FileName);
            Shutdown();
        }
        catch (Exception ex) { Notify(Words.T("无法恢复备份：", "Could not restore backup: ") + ex.Message); }
    }

    private void OpenInbox()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { Storage.Inbox }, UseShellExecute = true }); }
        catch (Exception ex) { Notify(ex.Message); }
    }
    private void Notify(string text)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => Notify(text)); return; }
        if (!notices.Contains(text)) notices.Enqueue(text);
        if (tray != null && !noticeTimer.IsEnabled) ShowNextNotice();
    }
    private void ShowNextNotice()
    {
        if (tray == null || notices.Count == 0) { noticeTimer.Stop(); return; }
        tray.ShowBalloonTip(6000, DisplayName, notices.Dequeue(), Forms.ToolTipIcon.Warning);
        noticeTimer.Start();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        trayPanel?.Close();
        noticeTimer.Stop();
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
