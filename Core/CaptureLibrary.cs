using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.VisualBasic.FileIO;

namespace SnapLine;

public sealed class CaptureLibrary
{
    private readonly State state;
    private readonly SemaphoreSlim gate = new(1);
    private int automaticCapacity = 100;
    private DateTime nextRetry = DateTime.MinValue;
    private bool retrying;
    private readonly Dictionary<string, long> hangingOrder = new(StringComparer.Ordinal);
    private long nextHangingOrder;
    public event Action? Changed;
    public event Action<string>? Error;
    public IReadOnlyList<Capture> Items => state.Captures;
    public int Capacity => state.Preferences.Capacity == 0 ? automaticCapacity : state.Preferences.Capacity;
    public CaptureLibrary(State state) => this.state = state;

    internal async Task ExportBackupAsync(string destination)
    {
        await gate.WaitAsync();
        string snapshot;
        try { snapshot = await Task.Run(() => BackupService.CreateSnapshot(destination)); }
        finally { gate.Release(); }
        try { await Task.Run(() => BackupService.Export(destination, snapshot)); }
        finally { try { Directory.Delete(snapshot, true); } catch { } }
    }

    private sealed class RecoveryRecord
    {
        public Capture? Capture { get; set; }
        public string Kind { get; set; } = "pin";
        public bool Completed { get; set; }
    }
    private static RecoveryRecord ReadRecovery(string path)
    {
        var json = File.ReadAllText(path);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("Capture", out _)
            ? JsonSerializer.Deserialize<RecoveryRecord>(json) ?? throw new InvalidDataException()
            : new RecoveryRecord { Capture = JsonSerializer.Deserialize<Capture>(json) };
    }
    internal string BeginRecovery(Capture item, string kind = "pin")
    {
        var record = new Capture { Id = item.Id, Path = Storage.Owns(item.Path) ? Path.Combine("Inbox", Path.GetFileName(item.Path)) : item.Path,
            Hash = item.Hash, PixelWidth = item.PixelWidth, PixelHeight = item.PixelHeight, Added = item.Added };
        var path = Path.Combine(Storage.Root, "recovery-" + Guid.NewGuid().ToString("N") + ".json");
        Storage.WithSaveLock(() => File.WriteAllText(path, JsonSerializer.Serialize(new RecoveryRecord { Capture = record, Kind = kind })));
        return path;
    }
    internal bool CompleteRecovery(string path)
    {
        try
        {
            Storage.WithSaveLock(() =>
            {
                var record = ReadRecovery(path);
                record.Completed = true;
                var temporary = path + ".tmp";
                try { File.WriteAllText(temporary, JsonSerializer.Serialize(record)); File.Move(temporary, path, true); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                try { File.Delete(path); } catch { }
            });
            return true;
        }
        catch (Exception ex) { Error?.Invoke(Words.T("无法完成图片恢复记录：", "Could not complete the image recovery record: ") + ex.Message); return false; }
    }
    internal void RecoverInterrupted()
    {
        foreach (var path in Directory.EnumerateFiles(Storage.Root, "recovery-*.json"))
        {
            try
            {
                var record = ReadRecovery(path);
                if (record.Completed) { try { File.Delete(path); } catch { } continue; }
                var item = record.Capture;
                if (item == null || string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Path)) continue;
                if (!Path.IsPathFullyQualified(item.Path)) item.Path = Path.Combine(Storage.Inbox, Path.GetFileName(item.Path));
                if (!state.Captures.Any(existing => existing.Id == item.Id))
                {
                    if (record.Kind == "remove" && !File.Exists(item.Path)) continue;
                    state.Captures.Add(item);
                    if (!Save()) { state.Captures.Remove(item); continue; }
                }
                CompleteRecovery(path);
            }
            catch (Exception ex) { Error?.Invoke(Words.T("无法恢复中断的图片操作：", "Could not recover an interrupted image operation: ") + ex.Message); }
        }
    }

    public bool Save()
    {
        try { Storage.Save(state); return true; }
        catch (Exception ex) { Error?.Invoke(Words.T("无法保存设置：", "Cannot save settings: ") + ex.Message); return false; }
    }
    public async Task RestoreAsync()
    {
        var unavailable = 0;
        foreach (var item in state.Captures.ToArray())
        {
            try
            {
                var image = await Task.Run(() => Read(item.Path));
                item.Thumbnail = image.Thumb;
                item.Hash = image.Hash;
                item.PixelWidth = image.Full.PixelWidth;
                item.PixelHeight = image.Full.PixelHeight;

            }
            catch { item.Thumbnail = null; unavailable++; }
        }
        if (unavailable > 0) Error?.Invoke(Words.T($"{unavailable} 张图片暂时无法读取，记录已保留并会自动重试。", $"{unavailable} images are unavailable. Their records are kept and will be retried."));
        Save();
        Changed?.Invoke();
    }
    public async Task<bool> ImportAsync(string path, bool notify = true, System.Drawing.Point? animationOrigin = null, CancellationToken cancellationToken = default, Action<Exception>? failure = null)
    {
        try { await gate.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) { return false; }
        var before = state.Captures.ToList();
        Capture? modified = null;
        string? oldHash = null;
        BitmapSource? oldThumbnail = null;
        int oldWidth = 0, oldHeight = 0;
        var committed = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            path = System.IO.Path.GetFullPath(path);
            var image = await Task.Run(() => Read(path), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var samePath = state.Captures.FirstOrDefault(x => x.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (samePath != null)
            {
                if (samePath.Hash == image.Hash && samePath.Thumbnail != null) return true;
                modified = samePath; oldHash = samePath.Hash; oldThumbnail = samePath.Thumbnail;
                oldWidth = samePath.PixelWidth; oldHeight = samePath.PixelHeight;
                samePath.Hash = image.Hash;
                samePath.Thumbnail = image.Thumb;
                samePath.PixelWidth = image.Full.PixelWidth;
                samePath.PixelHeight = image.Full.PixelHeight;
            }
            else
            {
                if (state.Captures.Any(x => x.Hash == image.Hash)) return true;
                state.Captures.Add(new Capture { Path = path, Hash = image.Hash, Thumbnail = image.Thumb, PixelWidth = image.Full.PixelWidth, PixelHeight = image.Full.PixelHeight, AnimationOrigin = animationOrigin });
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!Save()) throw new IOException(Words.T("状态未保存。", "State was not saved."));
            committed = true;
            await TrimAsync();
            Changed?.Invoke();
            return true;
        }
        catch (OperationCanceledException)
        {
            if (!committed) { state.Captures.Clear(); state.Captures.AddRange(before); if (modified != null) { modified.Hash = oldHash!; modified.Thumbnail = oldThumbnail; modified.PixelWidth = oldWidth; modified.PixelHeight = oldHeight; } }
            return false;
        }
        catch (Exception ex)
        {
            if (!committed) { state.Captures.Clear(); state.Captures.AddRange(before); if (modified != null) { modified.Hash = oldHash!; modified.Thumbnail = oldThumbnail; modified.PixelWidth = oldWidth; modified.PixelHeight = oldHeight; } }
            failure?.Invoke(ex);
            if (notify) Error?.Invoke(Words.T("无法读取图片：", "Cannot read image: ") + ex.Message);
            return false;
        }
        finally { gate.Release(); }
    }
    public async Task ImportClipboardAsync(BitmapSource bitmap, System.Drawing.Point? animationOrigin = null)
    {
        await gate.WaitAsync();
        string? created = null;
        var before = state.Captures.ToList();
        try
        {
            bitmap = await Task.Run(() => Detach(bitmap));
            var hash = await Task.Run(() => Fingerprint(bitmap));
            if (state.Captures.Any(x => x.Hash == hash)) return;
            created = System.IO.Path.Combine(Storage.Inbox, $"Capture-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.png");
            var destination = created;
            var thumb = await Task.Run(() =>
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(destination)) encoder.Save(stream);
                return CreateThumbnail(bitmap);
            });
            state.Captures.Add(new Capture { Path = created, Hash = hash, Thumbnail = thumb, PixelWidth = bitmap.PixelWidth, PixelHeight = bitmap.PixelHeight, AnimationOrigin = animationOrigin });
            if (!Save()) throw new IOException(Words.T("状态未保存。", "State was not saved."));
            created = null;
            await TrimAsync();
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            state.Captures.Clear(); state.Captures.AddRange(before);
            Error?.Invoke(Words.T("无法收集剪贴板图片：", "Cannot collect clipboard image: ") + ex.Message);
        }
        finally
        {
            if (created != null) { try { File.Delete(created); } catch { } }
            gate.Release();
        }
    }
    public async Task SetCapacityAsync(int visible)
    {
        automaticCapacity = Math.Max(100, visible);
        await gate.WaitAsync();
        try { if (!Save()) return; await TrimAsync(); Changed?.Invoke(); }
        finally { gate.Release(); }
    }
    private async Task TrimAsync(Capture? keep = null)
    {
        foreach (var item in state.Captures.ToArray())
        {
            if (state.Captures.Count <= Capacity) break;
            if (ReferenceEquals(item, keep) || item.Thumbnail == null) continue;
            if (!await RemoveCommittedAsync(item)) break;
        }
        if (state.Captures.Count > Capacity)
            Error?.Invoke(Words.T("部分图片暂不可用，保留数量暂时超出限制。", "Some images are unavailable; the limit is temporarily exceeded."));
    }
    public async Task ConfirmMovedAsync(Capture item)
    {
        await gate.WaitAsync();
        try
        {
            if (!state.Captures.Contains(item) || File.Exists(item.Path)) return;
            var position = state.Captures.IndexOf(item);
            state.Captures.RemoveAt(position);
            if (!Save()) { state.Captures.Insert(position, item); return; }
            Changed?.Invoke();
        }
        finally { gate.Release(); }
    }
    public void MarkUnavailable(string path)
    {
        var affected = state.Captures.Where(item => item.Thumbnail != null && item.Path.Equals(path, StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var item in affected)
            item.Thumbnail = null;
        if (affected.Length > 0) Changed?.Invoke();
    }
    public async Task RenameAsync(string oldPath, string newPath)
    {
        await gate.WaitAsync();
        try
        {
            var item = state.Captures.FirstOrDefault(capture => capture.Path.Equals(oldPath, StringComparison.OrdinalIgnoreCase));
            if (item == null) return;
            if (state.Captures.Any(capture => !ReferenceEquals(capture, item) && capture.Path.Equals(newPath, StringComparison.OrdinalIgnoreCase)))
            {
                item.Thumbnail = null;
                Changed?.Invoke();
                return;
            }
            item.Path = newPath;
            if (!Save()) { item.Path = oldPath; item.Thumbnail = null; }
            Changed?.Invoke();
        }
        finally { gate.Release(); }
    }
    public async Task RemoveAsync(Capture item)
    {
        await gate.WaitAsync();
        try
        {
            if (await RemoveCommittedAsync(item)) Changed?.Invoke();
        }
        finally { gate.Release(); }
    }
    public async Task<bool> TakeOffAsync(Capture item)
    {
        await gate.WaitAsync();
        try
        {
            RememberHangingOrder();
            var position = state.Captures.IndexOf(item);
            if (position < 0) return false;
            state.Captures.RemoveAt(position);
            if (!Save()) { state.Captures.Insert(position, item); return false; }
            Changed?.Invoke();
            return true;
        }
        finally { gate.Release(); }
    }
    private void RememberHangingOrder()
    {
        // 保留取下图片的序号，多张图片以不同顺序挂回时也能恢复原有次序。
        foreach (var capture in state.Captures)
            if (!hangingOrder.ContainsKey(capture.Id)) hangingOrder[capture.Id] = nextHangingOrder++;
    }
    public async Task<bool> ReturnAsync(Capture item, System.Drawing.Rectangle origin)
    {
        await gate.WaitAsync();
        var before = state.Captures.ToList();
        try
        {
            var image = await Task.Run(() => Read(item.Path));
            RememberHangingOrder();
            if (!hangingOrder.TryGetValue(item.Id, out var order))
                hangingOrder[item.Id] = order = nextHangingOrder++;
            // 取下期间可能被目录监听再次导入，恢复原记录并合并重复项。
            state.Captures.RemoveAll(capture => capture.Id == item.Id
                || capture.Path.Equals(item.Path, StringComparison.OrdinalIgnoreCase) || capture.Hash == image.Hash);
            item.Hash = image.Hash;
            item.Thumbnail = image.Thumb;
            item.PixelWidth = image.Full.PixelWidth;
            item.PixelHeight = image.Full.PixelHeight;
            item.AnimationOrigin = new System.Drawing.Point(origin.Right, origin.Bottom);
            item.ReturnFlightBounds = origin;
            item.ReturningToLine = true;
            var index = state.Captures.FindIndex(capture => hangingOrder[capture.Id] > order);
            state.Captures.Insert(index < 0 ? state.Captures.Count : index, item);
            if (!Save()) { state.Captures.Clear(); state.Captures.AddRange(before); return false; }
            await TrimAsync(item);
            Changed?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            Error?.Invoke(Words.T("无法重新挂回：", "Cannot return image to line: ") + ex.Message);
            return false;
        }
        finally { gate.Release(); }
    }
    public async Task RemoveAllAsync()
    {
        await gate.WaitAsync();
        try
        {
            var before = state.Captures.ToArray();
            var selected = new List<(Capture Item, string? Recovery)>();
            foreach (var item in before)
            {
                try
                {
                    var recovery = Storage.Owns(item.Path) && File.Exists(item.Path) ? BeginRecovery(item, "remove") : null;
                    selected.Add((item, recovery));
                }
                catch (Exception ex) { Error?.Invoke(Words.T("无法保护图片记录：", "Could not protect the image record: ") + ex.Message); }
            }
            if (selected.Count == 0) return;
            foreach (var (item, _) in selected) state.Captures.Remove(item);
            if (!Save())
            {
                state.Captures.Clear(); state.Captures.AddRange(before);
                foreach (var (_, recovery) in selected) if (recovery != null) CompleteRecovery(recovery);
                return;
            }
            var failed = new HashSet<Capture>();
            foreach (var (item, recovery) in selected)
            {
                if (await RemoveFileAsync(item)) { if (recovery != null) CompleteRecovery(recovery); }
                else failed.Add(item);
            }
            if (failed.Count > 0)
            {
                var retained = new HashSet<Capture>(state.Captures);
                retained.UnionWith(failed);
                state.Captures.Clear(); state.Captures.AddRange(before.Where(retained.Contains));
                if (Save())
                    foreach (var (item, recovery) in selected) if (failed.Contains(item) && recovery != null) CompleteRecovery(recovery);
            }
            Changed?.Invoke();
        }
        finally { gate.Release(); }
    }
    private async Task<bool> RemoveCommittedAsync(Capture item)
    {
        var position = state.Captures.IndexOf(item);
        if (position < 0) return false;
        string? recovery = null;
        try { if (Storage.Owns(item.Path) && File.Exists(item.Path)) recovery = BeginRecovery(item, "remove"); }
        catch (Exception ex) { Error?.Invoke(Words.T("无法保护图片记录：", "Could not protect the image record: ") + ex.Message); return false; }
        state.Captures.RemoveAt(position);
        if (!Save()) { state.Captures.Insert(position, item); if (recovery != null) CompleteRecovery(recovery); return false; }
        if (await RemoveFileAsync(item)) { if (recovery != null) CompleteRecovery(recovery); return true; }
        state.Captures.Insert(Math.Min(position, state.Captures.Count), item);
        if (Save() && recovery != null) CompleteRecovery(recovery);
        return false;
    }
    private async Task<bool> RemoveFileAsync(Capture item)
    {
        if (!Storage.Owns(item.Path) || !File.Exists(item.Path)) return true;
        try
        {
            // 只对收件箱中的文件使用回收站，外部图片只解除引用。
            await Task.Run(() => FileSystem.DeleteFile(item.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException));
            return true;
        }
        catch (Exception ex) { Error?.Invoke(Words.T("无法移到回收站，图片已保留：", "Could not recycle; image was kept: ") + ex.Message); return false; }
    }
    public void Prune()
    {
        if (retrying || DateTime.UtcNow < nextRetry || !state.Captures.Any(item => item.Thumbnail == null)) return;
        nextRetry = DateTime.UtcNow.AddSeconds(30);
        _ = RetryUnavailableAsync();
    }
    private async Task RetryUnavailableAsync()
    {
        retrying = true;
        try
        {
            foreach (var item in state.Captures.Where(item => item.Thumbnail == null).ToArray())
                await ImportAsync(item.Path, false);
        }
        finally { retrying = false; }
    }
    public async Task SaveEditedAsync(Capture item, BitmapSource bitmap, bool replace)
    {
        await gate.WaitAsync();
        string? created = null;
        try
        {
            if (replace && !state.Captures.Contains(item))
                throw new InvalidOperationException(Words.T("原截图已被取下，请选择新增截图。", "The original screenshot was removed. Choose to add a new screenshot."));
            var image = await Task.Run(() => Detach(bitmap));
            var hash = await Task.Run(() => Fingerprint(image));
            var thumb = await Task.Run(() => CreateThumbnail(image));
            created = System.IO.Path.Combine(Storage.Inbox, "Edit-" + Guid.NewGuid().ToString("N") + ".png");
            var destination = created;
            await Task.Run(() => SavePng(destination, image));
            var oldPath = item.Path;
            var oldHash = item.Hash;
            var oldThumb = item.Thumbnail;
            var oldWidth = item.PixelWidth;
            var oldHeight = item.PixelHeight;
            var added = replace ? item : new Capture();
            added.Path = destination;
            added.Hash = hash;
            added.Thumbnail = thumb;
            added.PixelWidth = image.PixelWidth;
            added.PixelHeight = image.PixelHeight;
            if (!replace) state.Captures.Add(added);
            try { Storage.Save(state); }
            catch
            {
                if (!replace) state.Captures.Remove(added);
                else { item.Path = oldPath; item.Hash = oldHash; item.Thumbnail = oldThumb; item.PixelWidth = oldWidth; item.PixelHeight = oldHeight; }
                throw;
            }
            created = null;
            if (replace && Storage.Owns(oldPath) && !state.Captures.Any(c => c.Path.Equals(oldPath, StringComparison.OrdinalIgnoreCase)))
            {
                try { await Task.Run(() => FileSystem.DeleteFile(oldPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException)); }
                catch (Exception ex) { Error?.Invoke(Words.T("旧图片未能移到回收站，文件已保留：", "Could not recycle the previous image; it was kept: ") + ex.Message); }
            }
            if (!replace) await TrimAsync(added);
            Changed?.Invoke();
        }
        finally
        {
            if (created != null) { try { File.Delete(created); } catch { } }
            gate.Release();
        }
    }
    public async Task<string?> EditablePathAsync(Capture item)
    {
        if (Storage.Owns(item.Path)) return item.Path;
        await gate.WaitAsync();
        string? path = null;
        try
        {
            path = System.IO.Path.Combine(Storage.Inbox, "Edit-" + Guid.NewGuid().ToString("N") + ".png");
            var destination = path;
            await Task.Run(() =>
            {
                var bitmap = LoadFull(item.Path);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(destination);
                encoder.Save(stream);
            });
            item.Path = path;
            path = null;
            Save();
            Changed?.Invoke();
            return item.Path;
        }
        catch (Exception ex) { Error?.Invoke(Words.T("无法创建编辑副本：", "Cannot create editing copy: ") + ex.Message); return null; }
        finally
        {
            if (path != null) { try { File.Delete(path); } catch { } }
            gate.Release();
        }
    }
    public static BitmapSource LoadFull(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > 64L * 1024 * 1024) throw new InvalidDataException(Words.T("图片超过 64 MB 限制。", "Image exceeds the 64 MB limit."));
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        if ((long)frame.PixelWidth * frame.PixelHeight > 80_000_000) throw new InvalidDataException(Words.T("图片像素过大。", "Image dimensions are too large."));
        return Detach(frame);
    }
    private static BitmapSource Detach(BitmapSource frame)
    {
        if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0 || (long)frame.PixelWidth * frame.PixelHeight > 80_000_000) throw new IOException(Words.T("图片像素过大。", "Image dimensions are too large."));
        // 解码帧携带线程相关的元数据；转成独立像素源后再跨线程用于剪贴板和预览。
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var stride = checked(frame.PixelWidth * 4);
        var pixels = new byte[checked(stride * frame.PixelHeight)];
        converted.CopyPixels(pixels, stride, 0);
        var bitmap = BitmapSource.Create(frame.PixelWidth, frame.PixelHeight, frame.DpiX, frame.DpiY, PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }
    private static void SavePng(string path, BitmapSource bitmap)
    {
        var temporary = path + ".snapline.tmp";
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(temporary)) encoder.Save(stream);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
    private static (BitmapSource Thumb, string Hash, BitmapSource Full) Read(string path)
    {
        var full = LoadFull(path);
        var hash = Fingerprint(full);
        return (CreateThumbnail(full), hash, full);
    }
    private static BitmapSource CreateThumbnail(BitmapSource full)
    {
        var scale = Math.Min(1, 420.0 / Math.Max(full.PixelWidth, full.PixelHeight));
        var scaled = new TransformedBitmap(full, new ScaleTransform(scale, scale));
        var stride = checked(scaled.PixelWidth * 4);
        var pixels = new byte[checked(stride * scaled.PixelHeight)];
        scaled.CopyPixels(pixels, stride, 0);
        // 缩略图拥有独立像素，避免长期持有完整原图。
        var thumb = BitmapSource.Create(scaled.PixelWidth, scaled.PixelHeight, scaled.DpiX, scaled.DpiY, PixelFormats.Bgra32, null, pixels, stride);
        thumb.Freeze();
        return thumb;
    }
    private static string Fingerprint(BitmapSource source)
    {
        if (source.PixelWidth <= 0 || source.PixelHeight <= 0 || (long)source.PixelWidth * source.PixelHeight > 80_000_000) throw new IOException("Image dimensions exceed limit.");
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = checked(source.PixelWidth * 4);
        var rows = Math.Min(source.PixelHeight, Math.Max(1, 1024 * 1024 / stride));
        var pixels = new byte[checked(stride * rows)];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(BitConverter.GetBytes(source.PixelWidth));
        hash.AppendData(BitConverter.GetBytes(source.PixelHeight));
        // 按行分块计算，保持原有指纹格式并降低大图临时内存。
        for (var y = 0; y < source.PixelHeight; y += rows)
        {
            var count = Math.Min(rows, source.PixelHeight - y);
            converted.CopyPixels(new System.Windows.Int32Rect(0, y, source.PixelWidth, count), pixels, stride, 0);
            hash.AppendData(pixels, 0, checked(stride * count));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
