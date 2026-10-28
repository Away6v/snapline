using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SnapLine;

internal sealed class TextEntryWindow : Window
{
    private readonly TextBox editor;
    private readonly Button primary;
    internal string? SavedText { get; private set; }

    internal TextEntryWindow(TextEntry? entry, bool readOnly = false)
    {
        Title = entry == null ? Words.T("新建文本", "New text") : readOnly ? Words.T("查看文本", "View text") : Words.T("编辑文本", "Edit text");
        Width = 520;
        Height = 390;
        MinWidth = 360;
        MinHeight = 260;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = AppTheme.Surface;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.CanResize;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        var layout = new Grid { Margin = new Thickness(22) };
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editor = new TextBox
        {
            Text = entry?.Text ?? "",
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsReadOnly = readOnly,
            Padding = new Thickness(12),
            FontSize = 14,
            Foreground = AppTheme.Ink,
            Background = AppTheme.Soft,
            BorderBrush = AppTheme.Outline,
            BorderThickness = new Thickness(0)
        };
        var editorFrame = new Border { Child = editor, CornerRadius = new CornerRadius(10), Background = AppTheme.Soft, BorderBrush = AppTheme.Outline, BorderThickness = new Thickness(1), ClipToBounds = true };
        layout.Children.Add(editorFrame);
        var footer = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var hint = new TextBlock { Text = readOnly ? "" : Words.T("Ctrl + Enter 保存", "Ctrl + Enter to save"), Foreground = AppTheme.Muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        footer.Children.Add(hint);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = Words.T("关闭", "Close"), Style = ThemeButtons.Secondary, MinWidth = 84, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => Close();
        actions.Children.Add(cancel);
        primary = new Button { Content = readOnly ? Words.T("编辑", "Edit") : Words.T("保存", "Save"), Style = ThemeButtons.Primary, MinWidth = 84 };
        primary.Click += (_, _) =>
        {
            if (editor.IsReadOnly)
            {
                editor.IsReadOnly = false;
                primary.Content = Words.T("保存", "Save");
                Title = Words.T("编辑文本", "Edit text");
                hint.Text = Words.T("Ctrl + Enter 保存", "Ctrl + Enter to save");
                editor.Focus();
                editor.CaretIndex = editor.Text.Length;
                return;
            }
            if (string.IsNullOrWhiteSpace(editor.Text))
            {
                hint.Text = Words.T("文本不能为空。", "Text cannot be empty.");
                hint.Foreground = AppTheme.Alert;
                editor.Focus();
                return;
            }
            SavedText = editor.Text;
            DialogResult = true;
        };
        actions.Children.Add(primary);
        Grid.SetColumn(actions, 1);
        footer.Children.Add(actions);
        Grid.SetRow(footer, 1);
        layout.Children.Add(footer);
        Content = layout;
        Loaded += (_, _) => editor.Focus();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; Close(); }
            else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0 && !editor.IsReadOnly)
            {
                e.Handled = true;
                primary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
        };
    }
}
