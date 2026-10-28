using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
    public event Action? Changed;
    public event Action<string>? Error;
    public IReadOnlyList<Capture> Items => state.Captures;
    public int Capacity => state.Preferences.Capacity == 0 ? automaticCapacity : state.Preferences.Capacity;
    public CaptureLibrary(State state) => this.state = state;

    public void Save()
    {
        try { Storage.Save(state); }
        catch (Exception ex) { Error?.Invoke(Words.T("无法保存设置：", "Cannot save settings: ") + ex.Message); }
    }
    public async Task RestoreAsync()
    {
        foreach (var item in state.Captures.ToArray())
        {
            try
            {
                var image = await Task.Run(() => Read(item.Path));
                item.Thumbnail = image.Thumb;
                item.Hash = image.Hash;
                item.PixelWidth = image.Full.PixelWidth;
                item.PixelHeight = image.Full.PixelHeight;
                if (Storage.Owns(item.Path) && image.AlphaRepaired)
                {
                    try { await Task.Run(() => SavePng(item.Path, image.Full)); }
                    catch { }
                }
            }
            catch { state.Captures.Remove(item); }
        }
        Save();
        Changed?.Invoke();
    }
    public async Task<bool> ImportAsync(string path, bool notify = true, System.Drawing.Point? animationOrigin = null)
    {
        await gate.WaitAsync();
        try
        {
            path = System.IO.Path.GetFullPath(path);
            var image = await Task.Run(() => Read(path));
            var samePath = state.Captures.FirstOrDefault(x => x.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (samePath != null)
            {
                if (samePath.Hash == image.Hash) return true;
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
            await TrimAsync();
            Save();
            Changed?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            if (notify) Error?.Invoke(Words.T("无法读取图片：", "Cannot read image: ") + ex.Message);
            return false;
        }
        finally { gate.Release(); }
    }
    public async Task ImportClipboardAsync(BitmapSource bitmap, System.Drawing.Point? animationOrigin = null)
    {
        await gate.WaitAsync();
        string? created = null;
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
                return Read(destination).Thumb;
            });
            state.Captures.Add(new Capture { Path = created, Hash = hash, Thumbnail = thumb, PixelWidth = bitmap.PixelWidth, PixelHeight = bitmap.PixelHeight, AnimationOrigin = animationOrigin });
            created = null;
            await TrimAsync();
            Save();
            Changed?.Invoke();
        }
        catch (Exception ex) { Error?.Invoke(Words.T("无法收集剪贴板图片：", "Cannot collect clipboard image: ") + ex.Message); }
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
        try { await TrimAsync(); Save(); Changed?.Invoke(); }
        finally { gate.Release(); }
    }
    private async Task TrimAsync()
    {
        while (state.Captures.Count > Capacity)
        {
            var first = state.Captures[0];
            if (!await RemoveFileAsync(first)) break;
            state.Captures.Remove(first);
        }
    }
    public async Task RemoveAsync(Capture item)
    {
        await gate.WaitAsync();
        try
        {
            if (!await RemoveFileAsync(item)) return;
            state.Captures.Remove(item);
            Save();
            Changed?.Invoke();
        }
        finally { gate.Release(); }
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
        if (gate.CurrentCount == 0) return;
        if (state.Captures.RemoveAll(x => !File.Exists(x.Path)) == 0) return;
        Save();
        Changed?.Invoke();
    }
    public async Task<string?> EditablePathAsync(Capture item)
    {
        if (Storage.Owns(item.Path)) return item.Path;
        await gate.WaitAsync();
        try
        {
            var path = System.IO.Path.Combine(Storage.Inbox, "Edit-" + Guid.NewGuid().ToString("N") + ".png");
            await Task.Run(() =>
            {
                var bitmap = LoadFull(item.Path);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(path);
                encoder.Save(stream);
            });
            item.Path = path;
            Save();
            Changed?.Invoke();
            return path;
        }
        catch (Exception ex) { Error?.Invoke(Words.T("无法创建编辑副本：", "Cannot create editing copy: ") + ex.Message); return null; }
        finally { gate.Release(); }
    }
    public static BitmapSource LoadFull(string path)
    {
        return LoadFullWithInfo(path).Image;
    }
    private static (BitmapSource Image, bool AlphaRepaired) LoadFullWithInfo(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > 64L * 1024 * 1024) throw new IOException(Words.T("图片超过 64 MB 限制。", "Image exceeds the 64 MB limit."));
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        if ((long)frame.PixelWidth * frame.PixelHeight > 80_000_000) throw new IOException(Words.T("图片像素过大。", "Image dimensions are too large."));
        return DetachWithInfo(frame);
    }
    private static BitmapSource Detach(BitmapSource frame)
    {
        return DetachWithInfo(frame).Image;
    }
    private static (BitmapSource Image, bool AlphaRepaired) DetachWithInfo(BitmapSource frame)
    {
        if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0 || (long)frame.PixelWidth * frame.PixelHeight > 80_000_000) throw new IOException(Words.T("图片像素过大。", "Image dimensions are too large."));
        // 解码帧携带线程相关的元数据；转成独立像素源后再跨线程用于剪贴板和预览。
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var stride = checked(frame.PixelWidth * 4);
        var pixels = new byte[checked(stride * frame.PixelHeight)];
        converted.CopyPixels(pixels, stride, 0);
        var allTransparent = true;
        var hasColor = false;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 3] != 0) allTransparent = false;
            if (pixels[i] != 0 || pixels[i + 1] != 0 || pixels[i + 2] != 0) hasColor = true;
            if (!allTransparent && hasColor) break;
        }
        var alphaRepaired = allTransparent && hasColor;
        if (alphaRepaired)
            for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        var bitmap = BitmapSource.Create(frame.PixelWidth, frame.PixelHeight, frame.DpiX, frame.DpiY, PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();
        return (bitmap, alphaRepaired);
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
    private static (BitmapSource Thumb, string Hash, BitmapSource Full, bool AlphaRepaired) Read(string path)
    {
        var (full, alphaRepaired) = LoadFullWithInfo(path);
        var hash = Fingerprint(full);
        var scale = Math.Min(1, 420.0 / Math.Max(full.PixelWidth, full.PixelHeight));
        var thumb = new TransformedBitmap(full, new ScaleTransform(scale, scale));
        thumb.Freeze();
        return (thumb, hash, full, alphaRepaired);
    }
    private static string Fingerprint(BitmapSource source)
    {
        if (source.PixelWidth <= 0 || source.PixelHeight <= 0 || (long)source.PixelWidth * source.PixelHeight > 80_000_000) throw new IOException("Image dimensions exceed limit.");
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[checked(source.PixelWidth * source.PixelHeight * 4)];
        converted.CopyPixels(pixels, source.PixelWidth * 4, 0);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(BitConverter.GetBytes(source.PixelWidth));
        hash.AppendData(BitConverter.GetBytes(source.PixelHeight));
        hash.AppendData(pixels);
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
