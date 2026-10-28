using System;
using System.Linq;

namespace SnapLine;

public sealed class TextLibrary
{
    private readonly State state;
    private readonly Preferences preferences;
    public event Action? Changed;
    public event Action<string>? Error;
    public System.Collections.Generic.IReadOnlyList<TextEntry> Items => state.Texts;

    public TextLibrary(State state)
    {
        this.state = state;
        preferences = state.Preferences;
    }

    public void Restore()
    {
        state.Texts ??= new();
        state.Texts.RemoveAll(item => string.IsNullOrWhiteSpace(item.Text));
        Trim();
        Changed?.Invoke();
    }

    public bool Import(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || state.Texts.Any(item => item.Text == text)) return false;
        state.Texts.Add(new TextEntry { Text = text });
        Trim();
        Save();
        Changed?.Invoke();
        return true;
    }

    public TextEntry? Add(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var entry = new TextEntry { Text = text };
        state.Texts.Add(entry);
        Trim();
        Save();
        Changed?.Invoke();
        return entry;
    }

    public void Edit(TextEntry entry, string text)
    {
        if (!state.Texts.Contains(entry) || string.IsNullOrWhiteSpace(text)) return;
        entry.Text = text;
        entry.Modified = DateTime.UtcNow;
        Save();
        Changed?.Invoke();
    }

    public void Remove(TextEntry entry)
    {
        if (!state.Texts.Remove(entry)) return;
        Save();
        Changed?.Invoke();
    }

    public void SetCapacity(int capacity)
    {
        preferences.TextCapacity = capacity is 25 or 50 or 100 ? capacity : 100;
        Trim();
        Save();
        Changed?.Invoke();
    }

    private void Trim()
    {
        while (state.Texts.Count > preferences.TextCapacity)
        {
            var oldest = state.Texts.OrderBy(item => item.Added).First();
            state.Texts.Remove(oldest);
        }
    }

    private void Save()
    {
        try { Storage.Save(state); }
        catch (Exception ex) { Error?.Invoke(Words.T("无法保存文本：", "Cannot save text: ") + ex.Message); }
    }
}
