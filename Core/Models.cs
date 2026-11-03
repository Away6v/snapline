using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace SnapLine;

public sealed class Preferences
{
    public bool ClipboardEnabled { get; set; } = true;
    public bool TextClipboardEnabled { get; set; } = true;
    public int TextCapacity { get; set; } = 100;
    public string DisplayMode { get; set; } = "screenshot";
    public bool OnboardingComplete { get; set; }
    public string Language { get; set; } = CultureInfo.CurrentUICulture.Name.StartsWith("zh") ? "zh" : "en";
    public int Capacity { get; set; } = 100;
    public int ImageScalePercent { get; set; } = 10;
    public uint HotkeyModifiers { get; set; } = App.IsDevelopment ? 7u : 3u;
    public uint HotkeyKey { get; set; } = 0x54;
    public uint ModeHotkeyModifiers { get; set; } = App.IsDevelopment ? 7u : 3u;
    public uint ModeHotkeyKey { get; set; } = 0x4D;
    public string Appearance { get; set; } = "hemp";
    public List<string> WatchFolders { get; set; } = new();
    public DateTime? CollectionPausedUntil { get; set; }
}

public sealed class Capture
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Path { get; set; } = "";
    public string Hash { get; set; } = "";
    public int PixelWidth { get; set; }
    public int PixelHeight { get; set; }
    public DateTime Added { get; set; } = DateTime.UtcNow;
    [System.Text.Json.Serialization.JsonIgnore]
    public BitmapSource? Thumbnail { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public System.Drawing.Point? AnimationOrigin { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool ReturningToLine { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public System.Drawing.Rectangle? ReturnFlightBounds { get; set; }
}

public sealed class State
{
    public int SchemaVersion { get; set; } = 1;
    public Preferences Preferences { get; set; } = new();
    public List<Capture> Captures { get; set; } = new();
    public List<TextEntry> Texts { get; set; } = new();
}

public sealed class TextEntry
{
    [System.Text.Json.Serialization.JsonIgnore]
    public System.Drawing.Point? AnimationOrigin { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public DateTime Added { get; set; } = DateTime.UtcNow;
    public DateTime Modified { get; set; } = DateTime.UtcNow;
}

public static class Storage
{
    // 默认把设置和截图放在程序包内；显式指定数据目录时仍使用指定位置。
    private static readonly bool PortableDefault = !App.IsDevelopment && Environment.GetEnvironmentVariable("SNAPLINE_DATA_DIR") == null;
    public static readonly string Root = System.IO.Path.GetFullPath(Environment.GetEnvironmentVariable("SNAPLINE_DATA_DIR") ?? System.IO.Path.Combine(AppContext.BaseDirectory, "Data"));
    public static readonly string Inbox = System.IO.Path.Combine(Root, "Inbox");
    public static readonly string StatePath = System.IO.Path.Combine(Root, "state.json");
    private static readonly string LegacyRoot = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnapLine");
    private static readonly string LegacyStatePath = System.IO.Path.Combine(LegacyRoot, "state.json");
    private static readonly string LegacyInbox = System.IO.Path.Combine(LegacyRoot, "Inbox");
    private static readonly string PicturesInbox = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "SnapLine");
    private static readonly Dictionary<string, string> migratedFiles = new(StringComparer.OrdinalIgnoreCase);
    private static bool stateLoadFailed;
    private static bool loadedLegacyState;
    private static readonly object saveLock = new();
    internal static void WithSaveLock(Action action) { lock (saveLock) action(); }
    public static bool NeedsManualRecovery => stateLoadFailed;
    public static bool Recovered { get; private set; }
    public static State Load()
    {
        Directory.CreateDirectory(Inbox);
        var source = File.Exists(StatePath) ? StatePath : PortableDefault && File.Exists(LegacyStatePath) ? LegacyStatePath : null;
        if (source == null) return new();
        try
        {
            var state = ReadState(source);
            loadedLegacyState = source.Equals(LegacyStatePath, StringComparison.OrdinalIgnoreCase);
            return state;
        }
        catch (InvalidDataException ex) when (ex.Message.StartsWith("Unsupported schema", StringComparison.Ordinal)) { throw; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            // 仅恢复本程序上一次成功提交的状态，不用空状态覆盖损坏文件。
            try
            {
                var backup = StatePath + ".bak";
                if (source == StatePath && File.Exists(backup))
                {
                    var recovered = ReadState(backup);
                    Recovered = true;
                    return recovered;
                }
            }
            catch (InvalidDataException error) when (error.Message.StartsWith("Unsupported schema", StringComparison.Ordinal)) { throw; }
            catch (Exception) { }
            stateLoadFailed = true;
            Recovered = true;
            return new();
        }
    }
    private static State ReadState(string source)
    {
        var state = JsonSerializer.Deserialize<State>(File.ReadAllText(source)) ?? throw new InvalidDataException("Empty state");
        if (state.SchemaVersion > 1) throw new InvalidDataException("Unsupported schema: " + state.SchemaVersion);
        state.SchemaVersion = 1;
        state.Preferences ??= new();
        state.Captures ??= new();
        state.Texts ??= new();
        state.Preferences.WatchFolders ??= new();
        if (state.Preferences.Capacity is not (0 or 25 or 50 or 100)) state.Preferences.Capacity = 100;
        if (state.Preferences.TextCapacity is not (25 or 50 or 100)) state.Preferences.TextCapacity = 100;
        if (state.Preferences.ImageScalePercent is not (5 or 10 or 15 or 20 or 25)) state.Preferences.ImageScalePercent = 10;
        if (state.Preferences.DisplayMode is not ("text" or "screenshot")) state.Preferences.DisplayMode = "screenshot";
        // 将已移除的近似主题映射到最接近的保留主题。
        state.Preferences.Appearance = state.Preferences.Appearance switch
        {
            "candy" => "classic",
            "amber" or "brass" => "hemp",
            "seaSalt" => "denim",
            var appearance => appearance
        };
        if (state.Preferences.Appearance is not ("hemp" or "classic" or "forest" or "graphite" or "denim" or "sakura" or "silver")) state.Preferences.Appearance = "hemp";
        state.Preferences.WatchFolders = state.Preferences.WatchFolders.Where(folder => !string.IsNullOrWhiteSpace(folder) && System.IO.Path.IsPathFullyQualified(folder)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var captureIds = new HashSet<string>(StringComparer.Ordinal);
        state.Captures = state.Captures.Where(capture => capture != null && !string.IsNullOrWhiteSpace(capture.Id)
            && !string.IsNullOrWhiteSpace(capture.Path) && captureIds.Add(capture.Id)).ToList();
        foreach (var capture in state.Captures.ToArray())
        {
            try
            {
                capture.Path = System.IO.Path.IsPathFullyQualified(capture.Path)
                    ? System.IO.Path.GetFullPath(capture.Path)
                    : System.IO.Path.Combine(Inbox, System.IO.Path.GetFileName(capture.Path));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or System.IO.PathTooLongException)
            { state.Captures.Remove(capture); }
        }
        var textIds = new HashSet<string>(StringComparer.Ordinal);
        state.Texts = state.Texts.Where(text => text != null && !string.IsNullOrWhiteSpace(text.Id)
            && !string.IsNullOrWhiteSpace(text.Text) && textIds.Add(text.Id)).ToList();
        return state;
    }
    public static void Save(State state)
    {
        lock (saveLock)
        {
            if (stateLoadFailed || state.SchemaVersion > 1) throw new InvalidDataException(Words.T("原状态无法安全读取，已禁止覆盖。请先备份 Data。", "The original state could not be read safely. Back up Data before recovery."));
            var temporary = StatePath + ".tmp";
            var portable = new State
            {
                Preferences = state.Preferences,
                Texts = state.Texts,
                Captures = state.Captures.Select(capture => new Capture
                {
                    Id = capture.Id,
                    Path = IsInDirectory(capture.Path, Inbox) ? System.IO.Path.Combine("Inbox", System.IO.Path.GetFileName(capture.Path)) : capture.Path,
                    Hash = capture.Hash,
                    PixelWidth = capture.PixelWidth,
                    PixelHeight = capture.PixelHeight,
                    Added = capture.Added
                }).ToList()
            };
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(portable, new JsonSerializerOptions { WriteIndented = true }));
                _ = ReadState(temporary);
                if (File.Exists(StatePath))
                {
                    if (Recovered)
                    {
                        File.Copy(StatePath, StatePath + ".recovered", true);
                        File.Move(temporary, StatePath, true);
                        Recovered = false;
                    }
                    else File.Replace(temporary, StatePath, StatePath + ".bak");
                }
                else File.Move(temporary, StatePath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
    public static int MigrateInbox(State state)
    {
        if (!PortableDefault) return 0;
        var failures = 0;
        foreach (var capture in state.Captures)
        {
            try
            {
                if ((IsInDirectory(capture.Path, LegacyInbox) || IsInDirectory(capture.Path, PicturesInbox)) && File.Exists(capture.Path))
                    capture.Path = CopyLegacyFile(capture.Path);
            }
            catch { failures++; }
        }
        if (!loadedLegacyState) return failures;
        foreach (var directory in new[] { LegacyInbox, PicturesInbox })
        {
            try
            {
                if (!Directory.Exists(directory)) continue;
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    if (migratedFiles.ContainsKey(file)) continue;
                    try { CopyLegacyFile(file); }
                    catch { failures++; }
                }
            }
            catch { failures++; }
        }
        return failures;
    }
    public static int CompleteMigration(State state, int migrationFailures)
    {
        if (!PortableDefault || !loadedLegacyState || migrationFailures > 0 || stateLoadFailed) return 0;
        if (migratedFiles.Count == 0 && !File.Exists(LegacyStatePath)) return 0;
        Save(state);
        var cleanupFailures = 0;
        foreach (var (source, destination) in migratedFiles)
        {
            try
            {
                if (!SameContent(source, destination)) { cleanupFailures++; continue; }
                File.Delete(source);
            }
            catch { cleanupFailures++; }
        }
        if (cleanupFailures == 0 && !Recovered && !StatePath.Equals(LegacyStatePath, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(LegacyStatePath); }
            catch { cleanupFailures++; }
        }
        foreach (var directory in new[] { LegacyInbox, PicturesInbox, LegacyRoot })
        {
            try { if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory); }
            catch { cleanupFailures++; }
        }
        return cleanupFailures;
    }
    public static bool Owns(string path)
    {
        return IsInDirectory(path, Inbox);
    }
    private static string CopyLegacyFile(string source)
    {
        if (migratedFiles.TryGetValue(source, out var copied)) return copied;
        var destination = System.IO.Path.Combine(Inbox, System.IO.Path.GetFileName(source));
        if (File.Exists(destination) && !SameContent(source, destination))
            destination = System.IO.Path.Combine(Inbox, System.IO.Path.GetFileNameWithoutExtension(source) + "-" + Guid.NewGuid().ToString("N") + System.IO.Path.GetExtension(source));
        if (!File.Exists(destination)) File.Copy(source, destination);
        if (!SameContent(source, destination)) throw new IOException("截图迁移校验失败。");
        migratedFiles[source] = destination;
        return destination;
    }
    private static bool SameContent(string first, string second)
    {
        using var left = File.OpenRead(first);
        using var right = File.OpenRead(second);
        return SHA256.HashData(left).AsSpan().SequenceEqual(SHA256.HashData(right));
    }
    private static bool IsInDirectory(string path, string directory) =>
        System.IO.Path.GetFullPath(path).StartsWith(directory + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}

public static class Words
{
    public static string Language { get; set; } = "zh";
    public static string T(string zh, string en) => Language == "zh" ? zh : en;
}
