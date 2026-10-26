using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace SnapLine;

public sealed class Preferences
{
    public bool ClipboardEnabled { get; set; } = true;
    public bool OnboardingComplete { get; set; }
    public string Language { get; set; } = CultureInfo.CurrentUICulture.Name.StartsWith("zh") ? "zh" : "en";
    public int Capacity { get; set; }
    public uint HotkeyModifiers { get; set; } = 3;
    public uint HotkeyKey { get; set; } = 0x54;
    public List<string> WatchFolders { get; set; } = new();
}

public sealed class Capture
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Path { get; set; } = "";
    public string Hash { get; set; } = "";
    public DateTime Added { get; set; } = DateTime.UtcNow;
    [System.Text.Json.Serialization.JsonIgnore]
    public BitmapSource? Thumbnail { get; set; }
}

public sealed class State
{
    public Preferences Preferences { get; set; } = new();
    public List<Capture> Captures { get; set; } = new();
}

public static class Storage
{
    // 独立数据目录用于便携配置和隔离验证，默认仍使用当前用户的本地数据目录。
    public static readonly string Root = System.IO.Path.GetFullPath(Environment.GetEnvironmentVariable("SNAPLINE_DATA_DIR") ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnapLine"));
    public static readonly string Inbox = System.IO.Path.Combine(Root, "Inbox");
    public static readonly string StatePath = System.IO.Path.Combine(Root, "state.json");
    public static bool Recovered { get; private set; }
    public static State Load()
    {
        Directory.CreateDirectory(Inbox);
        if (!File.Exists(StatePath)) return new();
        try
        {
            var state = JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath)) ?? new();
            state.Preferences ??= new();
            state.Captures ??= new();
            state.Preferences.WatchFolders ??= new();
            if (state.Preferences.Capacity is not (0 or 25 or 50 or 100)) state.Preferences.Capacity = 0;
            return state;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 损坏的配置保留在原处，用户保存新设置前不覆盖。
            Recovered = true;
            return new();
        }
    }
    public static void Save(State state)
    {
        var temporary = StatePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        if (Recovered && File.Exists(StatePath))
        {
            File.Copy(StatePath, StatePath + ".recovered", true);
            Recovered = false;
        }
        File.Move(temporary, StatePath, true);
    }
    public static bool Owns(string path)
    {
        return System.IO.Path.GetFullPath(path).StartsWith(Inbox + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}

public static class Words
{
    public static string Language { get; set; } = "zh";
    public static string T(string zh, string en) => Language == "zh" ? zh : en;
}
