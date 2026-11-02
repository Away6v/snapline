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
    private readonly Dictionary<string, Dictionary<string, (long Length, long Modified, long Created)>> inventories = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> initializedInventories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> scanTouches = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> uncollected = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> unavailableFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> extensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };
    private bool disposed;
    private int watcherGeneration;
    private int retryGeneration = -1;
    private uint clipboardGeneration;
    private readonly Dictionary<bool, int> shortcutIds = new();
    private int nextShortcutId = 10;
    public event Action? ToggleRequested;
    public event Action? ModeToggleRequested;
    public event Action<string>? Error;
    public bool ClipboardAvailable { get; }
    private bool CollectionPaused
    {
        get
        {
            var until = preferences.CollectionPausedUntil;
            if (until == null) return false;
            if (until.Value == DateTime.MaxValue || until.Value > DateTime.UtcNow) return true;
            preferences.CollectionPausedUntil = null;
            return false;
        }
    }
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
        if (message == 0x031D && !CollectionPaused && (preferences.ClipboardEnabled || preferences.TextClipboardEnabled))
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
            if (generation != Native.GetClipboardSequenceNumber() || CollectionPaused || (!preferences.ClipboardEnabled && !preferences.TextClipboardEnabled)) return;
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
                        if (!string.IsNullOrWhiteSpace(text) && generation == Native.GetClipboardSequenceNumber() && preferences.TextClipboardEnabled) textLibrary.Import(text, animationOrigin);
                    }
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
        if (!disposed && generation == Native.GetClipboardSequenceNumber()) Error?.Invoke(Words.T("剪贴板正被占用，本次内容未收集。", "Clipboard is busy; this content was not collected."));
    }
    public static string DefaultFolder()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
        var configured = key?.GetValue("{B7BEDE81-DF94-4682-A7D8-57A52620B86F}") as string;
        if (!string.IsNullOrWhiteSpace(configured)) return Environment.ExpandEnvironmentVariables(configured);
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
    }
    private Dictionary<string, (long Length, long Modified, long Created)> ReadInventory(string folder)
    {
        var result = new Dictionary<string, (long, long, long)>(StringComparer.OrdinalIgnoreCase);
        try { _ = File.GetAttributes(folder); }
        catch (Exception ex) when (ex is DirectoryNotFoundException or FileNotFoundException && folder.Equals(Path.GetFullPath(DefaultFolder()), StringComparison.OrdinalIgnoreCase)) { return result; }
        foreach (var file in Directory.EnumerateFiles(folder).Where(path => extensions.Contains(Path.GetExtension(path))))
        {
            try
            {
                var info = new FileInfo(file);
                result[file] = (info.Length, info.LastWriteTimeUtc.Ticks, info.CreationTimeUtc.Ticks);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return result;
    }
    public void Rewatch(bool preservePending = false)
    {
        var generation = ++watcherGeneration;
        scanTouches.Clear();
        foreach (var watcher in watchers) watcher.Dispose();
        watchers.Clear();
        if (!preservePending)
        {
            foreach (var cancellation in pending.Values) { cancellation.Cancel(); cancellation.Dispose(); }
            pending.Clear();
            uncollected.Clear();
            unavailableFolders.Clear();
        }
        var folders = preferences.WatchFolders.Append(DefaultFolder()).Append(Storage.Inbox).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (!preservePending) { inventories.Clear(); initializedInventories.Clear(); }
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
                    dispatcher.BeginInvoke(() => { if (!disposed && generation == watcherGeneration) Schedule(path, origin); });
                }
                watcher.Created += (_, e) => Queue(e.FullPath);
                watcher.Changed += (_, e) => Queue(e.FullPath);
                watcher.Renamed += (_, e) => dispatcher.BeginInvoke(async () =>
                {
                    if (disposed || generation != watcherGeneration) return;
                    TouchScan(e.OldFullPath);
                    uncollected.Remove(e.OldFullPath);
                    if (inventories.TryGetValue(folder, out var inventory)) inventory.Remove(e.OldFullPath);
                    if (pending.Remove(e.OldFullPath, out var removed)) { removed.Cancel(); removed.Dispose(); }
                    if (Path.GetFullPath(e.FullPath).StartsWith(target, StringComparison.OrdinalIgnoreCase) && extensions.Contains(Path.GetExtension(e.FullPath)))
                        await library.RenameAsync(e.OldFullPath, e.FullPath);
                    else library.MarkUnavailable(e.OldFullPath);
                    if (!disposed && generation == watcherGeneration) Queue(e.FullPath);
                });
                watcher.Deleted += (_, e) => dispatcher.BeginInvoke(() =>
                {
                    if (disposed || generation != watcherGeneration) return;
                    TouchScan(e.FullPath);
                    uncollected.Remove(e.FullPath);
                    if (inventories.TryGetValue(folder, out var inventory)) inventory.Remove(e.FullPath);
                    if (pending.Remove(e.FullPath, out var removed)) { removed.Cancel(); removed.Dispose(); }
                    library.MarkUnavailable(e.FullPath);
                    library.Prune();
                });
                watcher.Error += (_, _) =>
                {
                    dispatcher.BeginInvoke(() =>
                    {
                        if (disposed || generation != watcherGeneration) return;
                        Error?.Invoke(Words.T("目录监控中断，正在自动恢复。", "Folder monitoring stopped. Recovering automatically."));
                        ScheduleRecovery(generation, TimeSpan.FromSeconds(2));
                    });
                };
                watcher.EnableRaisingEvents = true;
                watchers.Add(watcher);
                _ = ScanInventoryAsync(folder, generation);
            }
            catch (Exception ex) { if (unavailableFolders.Add(folder)) Error?.Invoke(Words.T("无法监控目录：", "Cannot watch folder: ") + folder + "\n" + ex.Message); ScheduleRecovery(generation, TimeSpan.FromSeconds(15)); }
        }
    }
    private void ScheduleRecovery(int generation, TimeSpan delay)
    {
        if (retryGeneration == generation) return;
        retryGeneration = generation;
        _ = RecoverWatchersAsync(generation, delay);
    }
    private async Task RecoverWatchersAsync(int generation, TimeSpan delay)
    {
        await Task.Delay(delay);
        if (disposed || generation != watcherGeneration) return;
        retryGeneration = -1;
        Rewatch(true);
    }
    private void TouchScan(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (parent != null && scanTouches.TryGetValue(parent, out var touched)) touched.Add(path);
    }
    private async Task ScanInventoryAsync(string folder, int generation)
    {
        var hadBaseline = initializedInventories.Contains(folder) && inventories.TryGetValue(folder, out _);
        inventories.TryGetValue(folder, out var baseline);
        var previous = hadBaseline ? new Dictionary<string, (long Length, long Modified, long Created)>(baseline!, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, (long Length, long Modified, long Created)>(StringComparer.OrdinalIgnoreCase);
        if (baseline == null) inventories[folder] = new(StringComparer.OrdinalIgnoreCase);
        var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        scanTouches[folder] = touched;
        try
        {
            var current = await Task.Run(() => ReadInventory(folder));
            if (disposed || generation != watcherGeneration) return;
            unavailableFolders.Remove(folder);
            initializedInventories.Add(folder);
            if (!inventories.TryGetValue(folder, out var inventory)) inventories[folder] = inventory = new(StringComparer.OrdinalIgnoreCase);
            foreach (var (file, metadata) in current)
            {
                if (touched.Contains(file)) continue;
                if (uncollected.Contains(file) || (hadBaseline && (!previous.TryGetValue(file, out var old) || old != metadata))) Schedule(file, null);
                else inventory[file] = metadata;
            }
            if (hadBaseline)
            {
                var missing = previous.Keys.Concat(library.Items.Where(item => string.Equals(Path.GetDirectoryName(item.Path), folder, StringComparison.OrdinalIgnoreCase)).Select(item => item.Path)).Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (var file in missing.Where(file => !current.ContainsKey(file) && !touched.Contains(file)).ToArray())
                {
                    inventory.Remove(file); uncollected.Remove(file);
                    if (pending.Remove(file, out var cancellation)) { cancellation.Cancel(); cancellation.Dispose(); }
                    library.MarkUnavailable(file);
                }
                library.Prune();
            }
        }
        catch (Exception ex)
        {
            if (!disposed && generation == watcherGeneration)
            {
                if (unavailableFolders.Add(folder)) Error?.Invoke(Words.T("无法补扫目录：", "Cannot rescan folder: ") + folder + "\n" + ex.Message);
                ScheduleRecovery(generation, TimeSpan.FromSeconds(15));
            }
        }
        finally { if (generation == watcherGeneration && scanTouches.TryGetValue(folder, out var active) && ReferenceEquals(active, touched)) scanTouches.Remove(folder); }
    }

    private void Schedule(string path, System.Drawing.Point? animationOrigin)
    {
        TouchScan(path);
        if (disposed || !extensions.Contains(Path.GetExtension(path))) return;
        if (CollectionPaused)
        {
            uncollected.Remove(path);
            if (pending.Remove(path, out var paused)) { paused.Cancel(); paused.Dispose(); }
            _ = RecordIgnoredAsync(path, watcherGeneration);
            return;
        }
        uncollected.Add(path);
        if (pending.Remove(path, out var previous)) { previous.Cancel(); previous.Dispose(); }
        var cancellation = new CancellationTokenSource();
        pending[path] = cancellation;
        _ = WaitForFileAsync(path, cancellation, animationOrigin);
    }
    private async Task RecordIgnoredAsync(string path, int generation)
    {
        try
        {
            var metadata = await Task.Run(() => { var info = new FileInfo(path); return (info.Length, info.LastWriteTimeUtc.Ticks, info.CreationTimeUtc.Ticks); });
            if (disposed || generation != watcherGeneration || pending.ContainsKey(path)) return;
            TouchScan(path);
            var parent = Path.GetDirectoryName(path);
            if (parent != null && inventories.TryGetValue(parent, out var inventory)) inventory[path] = metadata;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    private async Task WaitForFileAsync(string path, CancellationTokenSource cancellation, System.Drawing.Point? animationOrigin)
    {
        var token = cancellation.Token;
        try
        {
            var notified = false;
            while (!disposed)
            {
                for (var attempt = 0; attempt < 10; attempt++)
                {
                    await Task.Delay(300 + attempt * 100, token);
                    if (CollectionPaused)
                    {
                        uncollected.Remove(path);
                        if (pending.TryGetValue(path, out var paused) && paused == cancellation) { pending.Remove(path); cancellation.Dispose(); }
                        await RecordIgnoredAsync(path, watcherGeneration);
                        return;
                    }
                    try
                    {
                        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read)) { }
                        Exception? failure = null;
                        if (await library.ImportAsync(path, attempt == 9 && !notified, animationOrigin, token, error => failure = error))
                        {
                            var metadata = await Task.Run(() => { var info = new FileInfo(path); return (info.Length, info.LastWriteTimeUtc.Ticks, info.CreationTimeUtc.Ticks); }, token);
                            token.ThrowIfCancellationRequested();
                            TouchScan(path);
                            var parent = Path.GetDirectoryName(path);
                            if (parent != null && inventories.TryGetValue(parent, out var inventory)) inventory[path] = metadata;
                            uncollected.Remove(path);
                            return;
                        }
                        if (attempt == 9 && failure is InvalidDataException or System.IO.FileFormatException or NotSupportedException)
                        {
                            uncollected.Remove(path);
                            if (pending.TryGetValue(path, out var invalid) && invalid == cancellation) { pending.Remove(path); cancellation.Dispose(); }
                            await RecordIgnoredAsync(path, watcherGeneration);
                            return;
                        }
                        if (attempt == 9) notified = true;
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                if (!notified) { Error?.Invoke(Words.T("图片仍被占用，稍后会重试：", "Image is still locked and will be retried: ") + Path.GetFileName(path)); notified = true; }
                await Task.Delay(TimeSpan.FromSeconds(30), token);
            }
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
        foreach (var cancellation in pending.Values) { cancellation.Cancel(); cancellation.Dispose(); }
        pending.Clear();
        Native.RemoveClipboardFormatListener(source.Handle);
        foreach (var id in shortcutIds.Values) Native.UnregisterHotKey(source.Handle, id);
        source.Dispose();
    }
}
