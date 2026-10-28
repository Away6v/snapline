using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace SnapLine;

internal sealed class Collectors : IDisposable
{
    private readonly CaptureLibrary library;
    private readonly TextLibrary textLibrary;
    private readonly Preferences preferences;
    private readonly Dispatcher dispatcher;
    private readonly HwndSource source;
    private readonly List<FileSystemWatcher> watchers = new();
    private readonly Dictionary<string, CancellationTokenSource> pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> extensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };
    private bool disposed;
    private uint clipboardGeneration;
    private readonly Dictionary<bool, int> shortcutIds = new();
    private int nextShortcutId = 10;
    public event Action? ToggleRequested;
    public event Action? ModeToggleRequested;
    public event Action<string>? Error;
    public bool ClipboardAvailable { get; }
    public Collectors(CaptureLibrary library, TextLibrary textLibrary, Preferences preferences)
    {
        this.library = library;
        this.textLibrary = textLibrary;
        this.preferences = preferences;
        dispatcher = Dispatcher.CurrentDispatcher;
        source = new HwndSource(new HwndSourceParameters("SnapLine.Events") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 });
        source.AddHook(Hook);
        ClipboardAvailable = Native.AddClipboardFormatListener(source.Handle);
        clipboardGeneration = Native.GetClipboardSequenceNumber();
    }
    public bool RegisterShortcut(bool mode, uint modifiers, uint key)
    {
        // 先注册新组合，再撤销旧组合，避免注册失败时丢失原快捷键。
        var id = nextShortcutId++;
        if (!Native.RegisterHotKey(source.Handle, id, modifiers | 0x4000, key)) return false;
        if (shortcutIds.TryGetValue(mode, out var oldId)) Native.UnregisterHotKey(source.Handle, oldId);
        shortcutIds[mode] = id;
        return true;
    }
    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312)
        {
            var id = wParam.ToInt32();
            if (shortcutIds.TryGetValue(false, out var toggleId) && id == toggleId) { ToggleRequested?.Invoke(); handled = true; }
            else if (shortcutIds.TryGetValue(true, out var modeId) && id == modeId) { ModeToggleRequested?.Invoke(); handled = true; }
        }
        if (message == 0x031D && (preferences.ClipboardEnabled || preferences.TextClipboardEnabled))
        {
            clipboardGeneration = Native.GetClipboardSequenceNumber();
            _ = ReadClipboardAsync(clipboardGeneration);
        }
        return IntPtr.Zero;
    }
    private async Task ReadClipboardAsync(uint generation)
    {
        System.Drawing.Point? animationOrigin = Native.GetCursorPos(out var cursor) ? new System.Drawing.Point(cursor.X, cursor.Y) : null;
        for (var attempt = 0; attempt < 6 && !disposed; attempt++)
        {
            if (generation != Native.GetClipboardSequenceNumber() || (!preferences.ClipboardEnabled && !preferences.TextClipboardEnabled)) return;
            try
            {
                var data = Clipboard.GetDataObject();
                if (data == null) return;
                var rejected = new[] { DataFormats.FileDrop, "SnapLine.Copy", "ExcludeClipboardContentFromMonitorProcessing", "Clipboard Viewer Ignore", "org.nspasteboard.ConcealedType" };
                if (rejected.Any(format => data.GetDataPresent(format, false))) return;
                if (data.GetDataPresent(DataFormats.UnicodeText, false) || data.GetDataPresent(DataFormats.Text, false))
                {
                    if (preferences.TextClipboardEnabled)
                    {
                        var text = data.GetData(DataFormats.UnicodeText, false) as string ?? data.GetData(DataFormats.Text, false) as string;
                        if (!string.IsNullOrWhiteSpace(text) && generation == Native.GetClipboardSequenceNumber() && preferences.TextClipboardEnabled) textLibrary.Import(text);
                    }
                    return;
                }
                if (!preferences.ClipboardEnabled) return;
                BitmapSource? image = null;
                if (data.GetDataPresent("PNG", false) && data.GetData("PNG") is Stream png)
                {
                    if (png.Length > 64L * 1024 * 1024) return;
                    image = BitmapFrame.Create(png, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                }
                else if (Clipboard.ContainsImage()) image = Clipboard.GetImage();
                if (image == null || (long)image.PixelWidth * image.PixelHeight > 80_000_000) return;
                image.Freeze();
                if (generation != Native.GetClipboardSequenceNumber()) return;
                await library.ImportClipboardAsync(image, animationOrigin);
                return;
            }
            catch (System.Runtime.InteropServices.COMException) { await Task.Delay(80 * (attempt + 1)); }
            catch (Exception ex) { Error?.Invoke(Words.T("无法读取剪贴板内容：", "Cannot read clipboard content: ") + ex.Message); return; }
        }
        if (!disposed) Error?.Invoke(Words.T("剪贴板正被占用，本次图片未收集。", "Clipboard is busy; this image was not collected."));
    }
    public static string DefaultFolder()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
        var configured = key?.GetValue("{B7BEDE81-DF94-4682-A7D8-57A52620B86F}") as string;
        if (!string.IsNullOrWhiteSpace(configured)) return Environment.ExpandEnvironmentVariables(configured);
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
    }
    public void Rewatch()
    {
        foreach (var watcher in watchers) watcher.Dispose();
        watchers.Clear();
        var folders = preferences.WatchFolders.Append(DefaultFolder()).Append(Storage.Inbox).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
        {
            try
            {
                var actual = folder;
                var recursive = false;
                if (!Directory.Exists(actual))
                {
                    if (!folder.Equals(DefaultFolder(), StringComparison.OrdinalIgnoreCase)) throw new DirectoryNotFoundException(folder);
                    actual = Path.GetDirectoryName(folder)!;
                    recursive = true;
                }
                var watcher = new FileSystemWatcher(actual) { IncludeSubdirectories = recursive, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.DirectoryName };
                var target = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                void Queue(string path)
                {
                    if (!Path.GetFullPath(path).StartsWith(target, StringComparison.OrdinalIgnoreCase)) return;
                    System.Drawing.Point? origin = Native.GetCursorPos(out var cursor) ? new System.Drawing.Point(cursor.X, cursor.Y) : null;
                    dispatcher.BeginInvoke(() => Schedule(path, origin));
                }
                watcher.Created += (_, e) => Queue(e.FullPath);
                watcher.Changed += (_, e) => Queue(e.FullPath);
                watcher.Renamed += (_, e) => Queue(e.FullPath);
                watcher.Deleted += (_, e) => dispatcher.BeginInvoke(() =>
                {
                    if (pending.Remove(e.FullPath, out var removed)) { removed.Cancel(); removed.Dispose(); }
                    library.Prune();
                });
                watcher.Error += (_, _) => dispatcher.BeginInvoke(() => Error?.Invoke(Words.T("目录监控中断，请在托盘面板中重新选择目录。", "Folder monitoring stopped. Select the folder again in the tray panel.")));
                watcher.EnableRaisingEvents = true;
                watchers.Add(watcher);
            }
            catch (Exception ex) { Error?.Invoke(Words.T("无法监控目录：", "Cannot watch folder: ") + folder + "\n" + ex.Message); }
        }
    }
    private void Schedule(string path, System.Drawing.Point? animationOrigin)
    {
        if (disposed || !extensions.Contains(Path.GetExtension(path))) return;
        if (pending.Remove(path, out var previous)) { previous.Cancel(); previous.Dispose(); }
        var cancellation = new CancellationTokenSource();
        pending[path] = cancellation;
        _ = WaitForFileAsync(path, cancellation, animationOrigin);
    }
    private async Task WaitForFileAsync(string path, CancellationTokenSource cancellation, System.Drawing.Point? animationOrigin)
    {
        var token = cancellation.Token;
        try
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                await Task.Delay(300 + attempt * 100, token);
                try
                {
                    using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read)) { }
                    // 自有文件写入完成后也会被观察到，由内容指纹消除重复。
                    if (await library.ImportAsync(path, attempt == 9, animationOrigin)) return;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            Error?.Invoke(Words.T("图片仍被占用，暂未收集：", "Image is still locked and was not collected: ") + Path.GetFileName(path));
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (pending.TryGetValue(path, out var current) && current == cancellation) { pending.Remove(path); cancellation.Dispose(); }
        }
    }
    public void Dispose()
    {
        disposed = true;
        foreach (var watcher in watchers) watcher.Dispose();
        foreach (var cancellation in pending.Values) cancellation.Cancel();
        Native.RemoveClipboardFormatListener(source.Handle);
        foreach (var id in shortcutIds.Values) Native.UnregisterHotKey(source.Handle, id);
        source.Dispose();
    }
}
