using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace SnapLine;

internal sealed class LineWindow : Window
{
    private readonly CaptureLibrary library;
    private readonly Preferences preferences;
    private readonly Action<string> notify;
    private readonly Action settings;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Grid root = new();
    private readonly StackPanel cards = new() { Orientation = Orientation.Horizontal };
    private readonly ScrollViewer scroll;
    private readonly TextBlock caption = new();
    private readonly TranslateTransform slide = new();
    private readonly List<FrameworkElement> hitTargets = new();
    private IntPtr handle;
    private Forms.Screen? screen;
    private DateTime edgeSince;
    private DateTime lastInside;
    private DateTime lastMaintenance;
    private bool revealed;
    private bool busy;
    private bool dirty;
    private bool closing;
    private bool dark;
    private int showEpoch;
    private int visibleCount = 8;
    private Border? header;

    public LineWindow(CaptureLibrary library, Preferences preferences, Action<string> notify, Action settings)
    {
        this.library = library; this.preferences = preferences; this.notify = notify; this.settings = settings;
        Title = "SnapLine"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        Height = 244; Width = 1000;
        FontFamily = new FontFamily("Segoe UI");
        root.RenderTransform = slide;
        Content = root;
        var wire = new Border { Height = 2, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(24, 43, 24, 0), Background = new SolidColorBrush(Color.FromArgb(200, 107, 141, 166)), IsHitTestVisible = false };
        root.Children.Add(wire);
        header = new Border { CornerRadius = new CornerRadius(13), Padding = new Thickness(15, 5, 15, 5), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 7, 0, 0), Cursor = Cursors.Hand };
        caption.FontSize = 11; caption.FontWeight = FontWeights.SemiBold;
        header.Child = caption;
        header.ToolTip = Words.T("打开设置", "Open settings");
        header.MouseLeftButtonUp += (_, _) => settings();
        root.Children.Add(header);
        scroll = new ScrollViewer { Content = cards, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(20, 43, 20, 6), Background = Brushes.Transparent };
        scroll.PreviewMouseWheel += (_, e) => { scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset - e.Delta); e.Handled = true; };
        root.Children.Add(scroll);
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            Native.SetWindowLongPtr(handle, -20, new IntPtr(Native.GetWindowLongPtr(handle, -20).ToInt64() | 0x08000000 | 0x80));
            HwndSource.FromHwnd(handle).AddHook(Hook);
        };
        library.Changed += OnChanged;
        timer.Tick += Tick;
        Closed += (_, _) => Stop();
        Rebuild();
    }
    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0021) { handled = true; return new IntPtr(3); }
        if (message == 0x0084)
        {
            Native.GetCursorPos(out var cursor);
            var local = PointFromScreen(new Point(cursor.X, cursor.Y));
            var interactive = hitTargets.Any(target =>
            {
                if (!target.IsVisible) return false;
                var origin = target.TranslatePoint(new Point(), this);
                return new Rect(origin, target.RenderSize).Contains(local);
            });
            if (!interactive) { handled = true; return new IntPtr(-1); }
        }
        return IntPtr.Zero;
    }
    public void Start() => timer.Start();
    public void Stop() { closing = true; timer.Stop(); library.Changed -= OnChanged; }
    public void Toggle()
    {
        if (revealed) Conceal();
        else { Native.GetCursorPos(out var point); Reveal(Forms.Screen.FromPoint(new System.Drawing.Point(point.X, point.Y)), true); }
    }
    private async void Reveal(Forms.Screen target, bool manual = false)
    {
        if (closing) return;
        if (Native.FullScreen(target, handle)) return;
        var scale = Native.Scale(target);
        screen = target;
        Width = target.Bounds.Width / scale;
        visibleCount = Math.Max(1, (int)((Width - 40) / 174));
        if (!IsVisible) Show();
        Native.SetWindowPos(handle, new IntPtr(-1), target.Bounds.Left, target.Bounds.Top, target.Bounds.Width, (int)(Height * scale), 0x0010);
        revealed = true;
        showEpoch++;
        lastInside = DateTime.UtcNow.AddSeconds(manual ? 2 : 0);
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-Height, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        if (preferences.Capacity == 0 && library.Capacity != visibleCount) await library.SetCapacityAsync(visibleCount);
    }
    private void Conceal()
    {
        if (!revealed || busy) return;
        revealed = false;
        var epoch = ++showEpoch;
        var animation = new DoubleAnimation(0, -Height, TimeSpan.FromMilliseconds(210)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        animation.Completed += (_, _) => { if (!revealed && epoch == showEpoch) Hide(); };
        slide.BeginAnimation(TranslateTransform.YProperty, animation);
    }
    private void Tick(object? sender, EventArgs e)
    {
        if (closing || !Native.GetCursorPos(out var point)) return;
        var now = DateTime.UtcNow;
        var target = Forms.Screen.FromPoint(new System.Drawing.Point(point.X, point.Y));
        if (revealed && screen != null && Native.FullScreen(screen, handle)) { Conceal(); return; }
        var atEdge = point.Y >= target.Bounds.Top && point.Y <= target.Bounds.Top + 2;
        if (atEdge)
        {
            if (edgeSince == default) edgeSince = now;
            if (!busy && now - edgeSince > TimeSpan.FromMilliseconds(350) && (!revealed || screen?.DeviceName != target.DeviceName)) Reveal(target);
        }
        else edgeSince = default;
        if (revealed && screen != null)
        {
            var inside = point.X >= screen.Bounds.Left && point.X < screen.Bounds.Right && point.Y >= screen.Bounds.Top && point.Y < screen.Bounds.Top + Height * Native.Scale(screen);
            if (inside || busy) lastInside = now;
            else if (now - lastInside > TimeSpan.FromMilliseconds(650)) Conceal();
            if (revealed && !busy && target.DeviceName == screen.DeviceName && target.Bounds != screen.Bounds) Reveal(target);
        }
        if (now - lastMaintenance > TimeSpan.FromSeconds(2))
        {
            lastMaintenance = now;
            library.Prune();
            if (dark != Native.DarkTheme()) OnChanged();
            if (screen != null && !Forms.Screen.AllScreens.Any(s => s.DeviceName == screen.DeviceName)) Conceal();
        }
    }
    public async Task RefreshSettingsAsync()
    {
        await library.SetCapacityAsync(visibleCount);
        Rebuild();
    }
    private void OnChanged()
    {
        if (busy) { dirty = true; return; }
        Rebuild();
    }
    private void FinishInteraction()
    {
        busy = false;
        lastInside = DateTime.UtcNow;
        if (dirty) { dirty = false; Rebuild(); }
    }
    private void Rebuild()
    {
        dark = Native.DarkTheme();
        var foreground = dark ? Brushes.WhiteSmoke : new SolidColorBrush(Color.FromRgb(29, 49, 66));
        header!.Background = new SolidColorBrush(dark ? Color.FromArgb(245, 32, 43, 55) : Color.FromArgb(245, 243, 248, 252));
        caption.Foreground = foreground;
        caption.Text = "SNAPLINE  ·  " + library.Items.Count + "  ·  " + SettingsWindow.ShortcutText(preferences.HotkeyModifiers, preferences.HotkeyKey);
        hitTargets.Clear(); hitTargets.Add(header);
        cards.Children.Clear();
        if (library.Items.Count == 0)
        {
            var empty = new Border { Margin = new Thickness(20, 24, 0, 0), Padding = new Thickness(24, 20, 24, 20), CornerRadius = new CornerRadius(16), Background = header.Background, VerticalAlignment = VerticalAlignment.Top };
            empty.Child = new TextBlock { Text = Words.T("晾衣绳还是空的\n按 Win+Shift+S 截图，让第一张图片挂上来。", "Your line is waiting.\nPress Win+Shift+S to hang your first screenshot."), Foreground = foreground, FontSize = 15, LineHeight = 25 };
            cards.Children.Add(empty); hitTargets.Add(empty);
        }
        foreach (var capture in library.Items.Reverse()) cards.Children.Add(MakeCard(capture, foreground));
    }
    private FrameworkElement MakeCard(Capture capture, Brush foreground)
    {
        var container = new Grid { Width = 162, Height = 178, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Top };
        var tilt = new RotateTransform((capture.Id[0] % 5) - 2);
        container.RenderTransformOrigin = new Point(0.5, 0.04);
        container.RenderTransform = tilt;
        if (SystemParameters.ClientAreaAnimation)
            tilt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(tilt.Angle - 0.7, tilt.Angle + 0.7, TimeSpan.FromSeconds(2.5 + capture.Id[1] % 3)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
        var card = new Border { Margin = new Thickness(0, 13, 0, 0), CornerRadius = new CornerRadius(13), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(170, 192, 211, 225)), Background = new SolidColorBrush(dark ? Color.FromArgb(245, 34, 47, 60) : Color.FromArgb(248, 249, 252, 255)), Padding = new Thickness(7), Cursor = Cursors.Hand, Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 3, Opacity = 0.18 } };
        var interior = new Grid();
        interior.RowDefinitions.Add(new RowDefinition()); interior.RowDefinitions.Add(new RowDefinition { Height = new GridLength(25) });
        var image = new Image { Source = capture.Thumbnail, Stretch = Stretch.Uniform, SnapsToDevicePixels = true };
        interior.Children.Add(image);
        var label = new TextBlock { Text = Path.GetFileName(capture.Path), TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 10, Foreground = foreground, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1, 3, 20, 0) };
        Grid.SetRow(label, 1); interior.Children.Add(label);
        var remove = new Button { Content = "×", FontSize = 16, Width = 22, Height = 22, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = foreground, ToolTip = Words.T("移除（自有图片移至回收站）", "Remove (inbox images go to Recycle Bin)") };
        Grid.SetRow(remove, 1); interior.Children.Add(remove);
        remove.Click += async (_, e) => { e.Handled = true; await library.RemoveAsync(capture); };
        card.Child = interior; container.Children.Add(card);
        var peg = new Border { Width = 12, Height = 25, Background = new SolidColorBrush(Color.FromRgb(59, 169, 173)), CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, BorderBrush = new SolidColorBrush(Color.FromRgb(27, 104, 119)), BorderThickness = new Thickness(1) };
        container.Children.Add(peg);
        hitTargets.Add(card);
        card.ToolTip = Words.T("单击复制 · 双击打开 · 长按编辑 · 拖出分享", "Click to copy · Double-click to open · Hold to edit · Drag to share");
        var menu = new ContextMenu();
        void Add(string zh, string en, Action action) { var entry = new MenuItem { Header = Words.T(zh, en) }; entry.Click += (_, _) => action(); menu.Items.Add(entry); }
        Add("复制图片", "Copy image", () => _ = CopyAsync(capture, label));
        Add("预览", "Preview", () => Preview(capture));
        Add("用默认应用打开", "Open with default app", () => Open(capture.Path));
        Add("用画图编辑副本", "Edit in Paint", () => _ = EditAsync(capture));
        Add("另存为…", "Save as…", () => SaveAs(capture));
        Add("在文件夹中显示", "Show in folder", () => RevealFile(capture));
        menu.Items.Add(new Separator());
        Add("从晾衣绳移除", "Remove from line", () => _ = library.RemoveAsync(capture));
        menu.Opened += (_, _) => busy = true;
        menu.Closed += (_, _) => FinishInteraction();
        card.ContextMenu = menu;
        Point start = default;
        bool pressed = false, consumed = false;
        var hold = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(550) };
        var click = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Forms.SystemInformation.DoubleClickTime) };
        hold.Tick += (_, _) => { hold.Stop(); if (!pressed) return; consumed = true; _ = EditAsync(capture); };
        click.Tick += (_, _) => { click.Stop(); _ = CopyAsync(capture, label); };
        card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject target && IsButton(target)) return;
            pressed = true; consumed = false; start = e.GetPosition(card); busy = true;
            card.CaptureMouse();
            if (e.ClickCount == 2) { click.Stop(); hold.Stop(); consumed = true; Open(capture.Path); }
            else hold.Start();
            e.Handled = true;
        };
        card.MouseMove += (_, e) =>
        {
            if (!pressed || consumed || e.LeftButton != MouseButtonState.Pressed) return;
            var point = e.GetPosition(card);
            if (Math.Abs(point.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            consumed = true; pressed = false; hold.Stop(); click.Stop(); card.ReleaseMouseCapture();
            try
            {
                var data = new DataObject();
                data.SetFileDropList(new StringCollection { capture.Path });
                data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(1)));
                var result = DragDrop.DoDragDrop(card, data, DragDropEffects.Copy | DragDropEffects.Move);
                // Windows 文件管理器负责实际移动；只有源文件确实消失才移除。
                if ((result & DragDropEffects.Move) != 0) _ = PruneAfterDragAsync();
            }
            catch (Exception ex) { notify(Words.T("拖放失败：", "Drag failed: ") + ex.Message); }
            finally { FinishInteraction(); }
        };
        card.MouseLeftButtonUp += (_, e) => { if (!pressed) return; hold.Stop(); pressed = false; card.ReleaseMouseCapture(); if (!consumed && new Rect(card.RenderSize).Contains(e.GetPosition(card))) click.Start(); FinishInteraction(); e.Handled = true; };
        card.MouseLeave += (_, _) => { if (pressed && Mouse.LeftButton != MouseButtonState.Pressed) { pressed = false; hold.Stop(); FinishInteraction(); } };
        card.LostMouseCapture += (_, _) => { if (pressed) { pressed = false; hold.Stop(); FinishInteraction(); } };
        container.Unloaded += (_, _) => { hold.Stop(); click.Stop(); tilt.BeginAnimation(RotateTransform.AngleProperty, null); };
        return container;
    }
    private static bool IsButton(DependencyObject target)
    {
        while (target != null) { if (target is Button) return true; target = VisualTreeHelper.GetParent(target); }
        return false;
    }
    private async Task PruneAfterDragAsync() { await Task.Delay(700); library.Prune(); }
    private async Task CopyAsync(Capture capture, TextBlock label)
    {
        try
        {
            var bitmap = await Task.Run(() => CaptureLibrary.LoadFull(capture.Path));
            var data = new DataObject();
            data.SetImage(bitmap); data.SetData("SnapLine.Copy", "1");
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            var png = new MemoryStream(); encoder.Save(png); png.Position = 0; data.SetData("PNG", png);
            for (var attempt = 0; ; attempt++)
            {
                try { Clipboard.SetDataObject(data, true); break; }
                catch (System.Runtime.InteropServices.COMException) when (attempt < 4) { await Task.Delay(100); }
            }
            label.Text = Words.T("✓ 已复制", "✓ Copied");
            await Task.Delay(1200); label.Text = Path.GetFileName(capture.Path);
        }
        catch (Exception ex) { notify(Words.T("无法复制：", "Cannot copy: ") + ex.Message); }
    }
    private void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { notify(Words.T("无法打开：", "Cannot open: ") + ex.Message); }
    }
    private async Task EditAsync(Capture capture)
    {
        var path = await library.EditablePathAsync(capture);
        if (path == null) return;
        try { Process.Start(new ProcessStartInfo("mspaint.exe") { UseShellExecute = true, ArgumentList = { path } }); }
        catch (Exception ex) { notify(Words.T("无法启动 Windows 画图，请确认已安装：", "Cannot start Windows Paint. Check that it is installed: ") + ex.Message); }
    }
    private void RevealFile(Capture capture)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + capture.Path + "\"") { UseShellExecute = true }); }
        catch (Exception ex) { notify(ex.Message); }
    }
    private void SaveAs(Capture capture)
    {
        busy = true;
        try
        {
            var extension = Path.GetExtension(capture.Path);
            var dialog = new SaveFileDialog { FileName = Path.GetFileName(capture.Path), Filter = $"{extension}|*{extension}", AddExtension = true, DefaultExt = extension, OverwritePrompt = true };
            if (dialog.ShowDialog() == true && !Path.GetFullPath(dialog.FileName).Equals(Path.GetFullPath(capture.Path), StringComparison.OrdinalIgnoreCase)) File.Copy(capture.Path, dialog.FileName, true);
        }
        catch (Exception ex) { notify(Words.T("无法另存图片：", "Cannot save image: ") + ex.Message); }
        finally { FinishInteraction(); }
    }
    private async void Preview(Capture capture)
    {
        try
        {
            var image = await Task.Run(() => CaptureLibrary.LoadFull(capture.Path));
            var preview = new Window { Title = "SnapLine · " + Path.GetFileName(capture.Path), Width = 900, Height = 620, WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = new SolidColorBrush(Color.FromRgb(26, 32, 40)) };
            preview.Content = new Image { Source = image, Stretch = Stretch.Uniform, Margin = new Thickness(16) };
            preview.KeyDown += (_, e) => { if (e.Key == Key.Escape) preview.Close(); };
            preview.Show();
        }
        catch (Exception ex) { notify(Words.T("无法预览：", "Cannot preview: ") + ex.Message); }
    }
}
