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

    public const int MaxTextLength = 100_000;
    public bool Import(string text, System.Drawing.Point? animationOrigin = null)
    {
        if (!Valid(text) || state.Texts.Any(item => item.Text == text)) return false;
        var previous = state.Texts.ToList();
        state.Texts.Add(new TextEntry { Text = text, AnimationOrigin = animationOrigin });
        Trim();
        if (!Save()) { Restore(previous); return false; }
        Changed?.Invoke();
        return true;
    }

    public TextEntry? Add(string text)
    {
        if (!Valid(text)) return null;
        var previous = state.Texts.ToList();
        var entry = new TextEntry { Text = text };
        state.Texts.Add(entry);
        Trim();
        if (!Save()) { Restore(previous); return null; }
        Changed?.Invoke();
        return entry;
    }

    public void Edit(TextEntry entry, string text)
    {
        if (!state.Texts.Contains(entry) || !Valid(text)) return;
        var oldText = entry.Text;
        var oldModified = entry.Modified;
        entry.Text = text;
        entry.Modified = DateTime.UtcNow;
        if (!Save()) { entry.Text = oldText; entry.Modified = oldModified; return; }
        Changed?.Invoke();
    }

    public void Remove(TextEntry entry)
    {
        var index = state.Texts.IndexOf(entry);
        if (index < 0) return;
        state.Texts.RemoveAt(index);
        if (!Save()) { state.Texts.Insert(index, entry); return; }
        Changed?.Invoke();
    }

    public void RemoveAll()
    {
        if (state.Texts.Count == 0) return;
        var previous = state.Texts.ToList();
        state.Texts.Clear();
        if (!Save()) { Restore(previous); return; }
        Changed?.Invoke();
    }

    public bool SetCapacity(int capacity)
    {
        var previous = state.Texts.ToList();
        var oldCapacity = preferences.TextCapacity;
        preferences.TextCapacity = capacity is 25 or 50 or 100 ? capacity : 100;
        Trim();
        if (previous.Count == state.Texts.Count && oldCapacity == preferences.TextCapacity) return true;
        if (!Save()) { preferences.TextCapacity = oldCapacity; Restore(previous); return false; }
        Changed?.Invoke();
        return true;
    }

    private void Trim()
    {
        while (state.Texts.Count > preferences.TextCapacity)
        {
            var oldest = state.Texts.OrderBy(item => item.Added).First();
            state.Texts.Remove(oldest);
        }
    }
    private bool Valid(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text.Length <= MaxTextLength) return true;
        Error?.Invoke(Words.T("文本超过 100000 字符限制。", "Text exceeds the 100,000-character limit."));
        return false;
    }
    private void Restore(System.Collections.Generic.List<TextEntry> previous)
    {
        state.Texts.Clear();
        state.Texts.AddRange(previous);
    }
    private bool Save()
    {
        try { Storage.Save(state); return true; }
        catch (Exception ex) { Error?.Invoke(Words.T("无法保存文本：", "Cannot save text: ") + ex.Message); return false; }
    }
}
