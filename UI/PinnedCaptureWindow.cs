using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using PixelFormats = System.Windows.Media.PixelFormats;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace SnapLine;

internal sealed class PinnedCaptureWindow : Forms.Form
{
    [StructLayout(LayoutKind.Sequential)] private struct PointNative { public int X, Y; public PointNative(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] private struct SizeNative { public int Width, Height; public SizeNative(int width, int height) { Width = width; Height = height; } }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct Blend { public byte Operation, Flags, Alpha, Format; }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr screen, ref PointNative destination, ref SizeNative size, IntPtr source, ref PointNative origin, int key, ref Blend blend, int flags);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll")] private static extern IntPtr SetCursor(IntPtr cursor);
    private readonly Bitmap image;
    private readonly Capture capture;
    private readonly CaptureLibrary library;
    private readonly Action<string> notify;
    private readonly Forms.Timer redraw = new() { Interval = 16 };
    private readonly Forms.Timer tipTimer = new() { Interval = 350 };
    private readonly Forms.Timer fadeTimer = new() { Interval = 16 };
    private readonly Stopwatch fadeClock = new();
    private byte frameOpacity = 255;
    private IntPtr cachedBitmap;
    private int cachedWidth, cachedHeight;
    private readonly System.Windows.Controls.ToolTip tip;
    private readonly System.Windows.Controls.ContextMenu menu;
    private const int PinAreaHeight = 44;
    private const int ShadowSize = 10;
    private double x, y, zoom;
    private bool pinHovered, pinPressed, updating, returning;
    private Rectangle PinBounds => new((ClientSize.Width - 38) / 2, 2, 38, 38);
    internal Rectangle ImageBounds => new((int)Math.Round(x) + (Math.Max(38, Math.Max(1, (int)Math.Round(image.Width * zoom)) + ShadowSize * 2) - Math.Max(1, (int)Math.Round(image.Width * zoom))) / 2,
        (int)Math.Round(y) + PinAreaHeight, Math.Max(1, (int)Math.Round(image.Width * zoom)), Math.Max(1, (int)Math.Round(image.Height * zoom)));
    protected override Forms.CreateParams CreateParams { get { var value = base.CreateParams; value.ExStyle |= 0x80000; return value; } }

    protected override void WndProc(ref Forms.Message message)
    {
        // 缩放会改变分层窗口的位置和尺寸，系统询问光标时按当前原生窗口坐标重新判断。
        if (message.Msg == 0x20 && (message.LParam.ToInt64() & 0xffff) == 1
            && Native.GetWindowRect(message.HWnd, out var rect) && Native.GetCursorPos(out var cursor))
        {
            var left = rect.Left + (rect.Right - rect.Left - 38) / 2;
            if (cursor.X >= left && cursor.X < left + 38 && cursor.Y >= rect.Top + 2 && cursor.Y < rect.Top + 40)
            {
                SetCursor(Forms.Cursors.Hand.Handle);
                message.Result = new IntPtr(1);
                return;
            }
        }
        base.WndProc(ref message);
    }

    internal static Bitmap PrepareImage(BitmapSource bitmap)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Pbgra32, null, 0);
        var result = new Bitmap(bitmap.PixelWidth, bitmap.PixelHeight, PixelFormat.Format32bppPArgb);
        try
        {
            var data = result.LockBits(new Rectangle(0, 0, result.Width, result.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try { var pixels = new byte[data.Stride * result.Height]; converted.CopyPixels(pixels, data.Stride, 0); Marshal.Copy(pixels, 0, data.Scan0, pixels.Length); }
            finally { result.UnlockBits(data); }
            return result;
        }
        catch { result.Dispose(); throw; }
    }
    public PinnedCaptureWindow(Bitmap bitmap, Forms.Screen screen, Capture capture, CaptureLibrary library, Action<string> notify)
    {
        this.capture = capture; this.library = library; this.notify = notify; image = bitmap;
        var area = screen.WorkingArea;
        zoom = Math.Min(1, Math.Min(area.Width * 0.7 / image.Width, area.Height * 0.7 / image.Height));
        x = area.Left + (area.Width - image.Width * zoom) / 2 - ShadowSize;
        y = area.Top + (area.Height - image.Height * zoom) / 2 - PinAreaHeight;
        FormBorderStyle = Forms.FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        StartPosition = Forms.FormStartPosition.Manual; AutoScaleMode = Forms.AutoScaleMode.None;
        Bounds = new Rectangle((int)Math.Round(x), (int)Math.Round(y), Math.Max(38, (int)Math.Round(image.Width * zoom) + ShadowSize * 2), PinAreaHeight + Math.Max(1, (int)Math.Round(image.Height * zoom)) + ShadowSize);
        Text = "SnapLine 贴图"; KeyPreview = true;
        tip = ThemeToolTips.Create(Words.T("重新挂回晾衣绳", "Return to line"));
        tip.Placement = System.Windows.Controls.Primitives.PlacementMode.AbsolutePoint;
        redraw.Tick += (_, _) => { redraw.Stop(); RenderFrame(); };
        fadeTimer.Tick += (_, _) => { frameOpacity = (byte)Math.Min(255, fadeClock.ElapsedMilliseconds * 255 / 180); RenderFrame(); if (frameOpacity == 255) { fadeTimer.Stop(); fadeClock.Stop(); } };
        tipTimer.Tick += (_, _) => { tipTimer.Stop(); if (!pinHovered) return; var p = PointToScreen(new Point(PinBounds.Right + 5, PinBounds.Top)); var scale = Native.Scale(Forms.Screen.FromPoint(p)); tip.HorizontalOffset = p.X / scale; tip.VerticalOffset = p.Y / scale; tip.IsOpen = true; };
        menu = WarmCardMenu.Create();
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.AbsolutePoint;
        void AddMenuItem(string zh, string en, string symbol, Action action, bool destructive = false)
        {
            var item = WarmCardMenu.Item(Words.T(zh, en), symbol, destructive);
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        AddMenuItem("复制图片", "Copy image", "⧉", () => _ = CopyImageAsync());
        AddMenuItem("另存为…", "Save as…", "save", () => _ = SaveImageAsync());
        AddMenuItem("用默认应用打开", "Open with default app", "open", OpenImage);
        AddMenuItem("在文件夹中显示", "Show in folder", "folder", RevealImage);
        menu.Items.Add(WarmCardMenu.Separator());
        AddMenuItem("适合屏幕", "Fit to screen", "reset", ResetImageSize);
        AddMenuItem("重新挂回", "Return to line", "pin", () => _ = ReturnAsync());
        AddMenuItem("关闭贴图", "Close pinned image", "close", Close, true);
        Shown += (_, _) => { RenderFrame(); if (frameOpacity < 255) { fadeClock.Restart(); fadeTimer.Start(); } };
        MouseLeave += (_, _) => { pinHovered = false; tipTimer.Stop(); tip.IsOpen = false; Cursor = Forms.Cursors.Default; };
        MouseMove += (_, e) => { var over = PinBounds.Contains(e.Location); Cursor = over ? Forms.Cursors.Hand : Forms.Cursors.Default; if (over == pinHovered) return; pinHovered = over; tip.IsOpen = false; tipTimer.Stop(); if (over) tipTimer.Start(); };
        MouseDown += (_, e) => { if (e.Button != Forms.MouseButtons.Left) return; if (PinBounds.Contains(e.Location)) { pinPressed = true; return; } tip.IsOpen = false; ReleaseCapture(); SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero); };
        MouseUp += async (_, e) => { if (e.Button == Forms.MouseButtons.Right) { ShowMenu(); return; } if (e.Button == Forms.MouseButtons.Left && pinPressed) { pinPressed = false; if (PinBounds.Contains(e.Location)) await ReturnAsync(); } };
        MouseWheel += (_, e) => { var p = Forms.Cursor.Position; var factor = Math.Pow(1.1, e.Delta / 120.0); var min = Math.Max(40.0 / image.Width, 40.0 / image.Height); var max = Math.Min(4096.0 / image.Width, 4096.0 / image.Height); var next = Math.Clamp(zoom * factor, Math.Min(zoom, min), Math.Max(zoom, max)); x = p.X - ShadowSize - (p.X - x - ShadowSize) * next / zoom; y = p.Y - PinAreaHeight - (p.Y - y - PinAreaHeight) * next / zoom; zoom = next; tip.IsOpen = false; redraw.Stop(); redraw.Start(); };
        Move += (_, _) => { if (!updating && IsHandleCreated) { x = Left; y = Top; } };
        KeyDown += (_, e) => { if (e.KeyCode == Forms.Keys.Escape) Close(); };
    }
    internal void ShowWithFade(bool animate)
    {
        frameOpacity = animate ? (byte)0 : (byte)255;
        // 窗口显示前先提交透明首帧，避免首帧默认以不透明状态出现。
        _ = Handle;
        RenderFrame();
        Show();
    }
    internal void PrepareFrame()
    {
        _ = Handle;
        RenderFrame();
    }
    private void ShowMenu()
    {
        tipTimer.Stop(); tip.IsOpen = false;
        var point = Forms.Cursor.Position;
        var scale = Native.Scale(Forms.Screen.FromPoint(point));
        menu.HorizontalOffset = point.X / scale;
        menu.VerticalOffset = point.Y / scale;
        menu.IsOpen = true;
    }
    private async Task<byte[]> EncodeImageAsync()
    {
        // 克隆当前贴图后在后台编码，既保留透明度也避免阻塞界面。
        var copy = (Bitmap)image.Clone();
        return await Task.Run(() =>
        {
            using (copy)
            using (var stream = new MemoryStream())
            {
                copy.Save(stream, ImageFormat.Png);
                return stream.ToArray();
            }
        });
    }
    private async Task CopyImageAsync()
    {
        try
        {
            var bytes = await EncodeImageAsync();
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
            var data = new System.Windows.DataObject();
            data.SetImage(bitmap); data.SetData("PNG", new MemoryStream(bytes)); data.SetData("SnapLine.Copy", "1");
            for (var attempt = 0; ; attempt++)
            {
                try { System.Windows.Clipboard.SetDataObject(data, true); break; }
                catch (COMException) when (attempt < 4) { await Task.Delay(100); }
            }
        }
        catch (Exception ex) { notify(Words.T("无法复制图片：", "Cannot copy image: ") + ex.Message); }
    }
    private async Task SaveImageAsync()
    {
        try
        {
            using var dialog = new Forms.SaveFileDialog
            {
                FileName = Path.GetFileNameWithoutExtension(capture.Path) + ".png",
                Filter = Words.T("PNG 图片|*.png", "PNG image|*.png"), DefaultExt = "png", AddExtension = true, OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) != Forms.DialogResult.OK) return;
            if (Path.GetFullPath(dialog.FileName).Equals(Path.GetFullPath(capture.Path), StringComparison.OrdinalIgnoreCase)) return;
            var bytes = await EncodeImageAsync();
            await File.WriteAllBytesAsync(dialog.FileName, bytes);
        }
        catch (Exception ex) { notify(Words.T("无法另存图片：", "Cannot save image: ") + ex.Message); }
    }
    private void OpenImage()
    {
        try { Process.Start(new ProcessStartInfo(capture.Path) { UseShellExecute = true }); }
        catch (Exception ex) { notify(Words.T("无法打开图片：", "Cannot open image: ") + ex.Message); }
    }
    private void RevealImage()
    {
        try
        {
            if (!File.Exists(capture.Path)) throw new FileNotFoundException(Words.T("截图文件不存在。", "The screenshot file does not exist."));
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, ArgumentList = { "/select,", capture.Path } });
        }
        catch (Exception ex) { notify(Words.T("无法在文件夹中显示：", "Cannot show file in folder: ") + ex.Message); }
    }
    private void ResetImageSize()
    {
        var current = ImageBounds;
        var area = Forms.Screen.FromRectangle(current).WorkingArea;
        zoom = Math.Min(1, Math.Min(area.Width * 0.7 / image.Width, area.Height * 0.7 / image.Height));
        x = area.Left + (area.Width - image.Width * zoom) / 2 - ShadowSize;
        y = area.Top + (area.Height - image.Height * zoom) / 2 - PinAreaHeight;
        redraw.Stop(); redraw.Start();
    }
    private async Task ReturnAsync()
    {
        if (returning) return;
        returning = true;
        try { var bounds = Bounds; bounds.X += (bounds.Width - Math.Max(1, (int)Math.Round(image.Width * zoom))) / 2; bounds.Y += PinAreaHeight; bounds.Width = Math.Max(1, (int)Math.Round(image.Width * zoom)); bounds.Height = Math.Max(1, (int)Math.Round(image.Height * zoom)); if (await library.ReturnAsync(capture, bounds)) Close(); }
        catch (Exception ex) { notify(Words.T("无法重新挂回：", "Cannot return image to line: ") + ex.Message); }
        finally { returning = false; }
    }
    private void RenderFrame()
    {
        if (!IsHandleCreated || IsDisposed) return;
        var imageWidth = Math.Max(1, (int)Math.Round(image.Width * zoom));
        var width = Math.Max(38, imageWidth + ShadowSize * 2);
        var imageHeight = Math.Max(1, (int)Math.Round(image.Height * zoom));
        var height = PinAreaHeight + imageHeight + ShadowSize;
        if (cachedBitmap == IntPtr.Zero || width != cachedWidth || height != cachedHeight)
        {
            using var frame = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(frame))
            {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(image, new Rectangle((width - imageWidth) / 2, PinAreaHeight, imageWidth, imageHeight));
            g.CompositingMode = CompositingMode.SourceOver; g.SmoothingMode = SmoothingMode.AntiAlias;
            // 由内向外逐渐淡出，保留透明图片本身的透明区域。
            var imageLeft = (width - imageWidth) / 2;
            for (var distance = 1; distance <= ShadowSize; distance++)
            {
                var alpha = (int)(75 * Math.Pow((ShadowSize + 1.0 - distance) / ShadowSize, 2));
                using var shadow = new Pen(Color.FromArgb(alpha, 0x38, 0xA9, 0xE8), 1);
                g.DrawRectangle(shadow, imageLeft - distance, PinAreaHeight - distance, imageWidth + distance * 2 - 1, imageHeight + distance * 2 - 1);
            }
            var pinLeft = (width - 38) / 2;
            using var background = new SolidBrush(Color.FromArgb(250, 255, 252, 246));
            using var border = new Pen(Color.FromArgb(220, 205, 215, 201), 1);
            // 保留可命中的透明连接区，鼠标能从截图连续移入图钉。
            using var bridge = new SolidBrush(Color.FromArgb(1, 255, 255, 255));
            g.FillRectangle(bridge, pinLeft, 37, 38, PinAreaHeight - 37);
            g.FillEllipse(background, pinLeft + 1, 3, 36, 36);
            g.DrawEllipse(border, pinLeft + 1, 3, 36, 36);
            DrawPin(g, pinLeft + 3, 5);
            }
            var replacement = frame.GetHbitmap(Color.FromArgb(0));
            if (cachedBitmap != IntPtr.Zero) DeleteObject(cachedBitmap);
            cachedBitmap = replacement;
            cachedWidth = width;
            cachedHeight = height;
        }
        var screen = GetDC(IntPtr.Zero); var dc = CreateCompatibleDC(screen); var old = SelectObject(dc, cachedBitmap);
        try
        {
            var destination = new PointNative((int)Math.Round(x), (int)Math.Round(y)); var size = new SizeNative(width, height); var origin = new PointNative(0, 0);
            var blend = new Blend { Alpha = frameOpacity, Format = 1 };
            // 在同一次提交中替换像素、位置和尺寸，避免透明窗口先清空再重绘。
            updating = true;
            if (!UpdateLayeredWindow(Handle, screen, ref destination, ref size, dc, ref origin, 0, ref blend, 2)) throw new System.ComponentModel.Win32Exception();
        }
        finally { updating = false; SelectObject(dc, old); DeleteDC(dc); ReleaseDC(IntPtr.Zero, screen); }
    }
    private static void DrawPin(Graphics g, int left, int top)
    {
        var state = g.Save();
        try
        {
            g.TranslateTransform(left + 16, top + 16);
            g.RotateTransform(-35);
            g.TranslateTransform(-16, -16);
            // 针尖分成亮面和暗面，在小尺寸下保留金属质感。
            using var needle = new GraphicsPath();
            needle.AddPolygon(new PointF[] { new(14.5f, 18), new(17.5f, 18), new(16.6f, 25), new(16, 29), new(15.3f, 25) });
            using var metal = new LinearGradientBrush(new RectangleF(14, 18, 4, 11), Color.FromArgb(242, 246, 239), Color.FromArgb(95, 112, 109), 0f);
            using var metalEdge = new Pen(Color.FromArgb(75, 91, 87), 0.7f) { LineJoin = LineJoin.Round };
            g.FillPath(metal, needle); g.DrawPath(metalEdge, needle);
            // 深色侧面和亮色正面共同构成有厚度的搪瓷底座。
            using var body = new GraphicsPath();
            body.AddBezier(12, 8, 12, 13, 13, 14, 10, 17);
            body.AddBezier(10, 17, 8, 19, 9, 20, 12, 21);
            body.AddBezier(12, 21, 15, 22, 20, 22, 23, 20);
            body.AddBezier(23, 20, 24, 19, 23, 18, 21, 16);
            body.AddBezier(21, 16, 19, 14, 20, 12, 20, 8);
            body.CloseFigure();
            using var bodyFill = new LinearGradientBrush(new RectangleF(9, 8, 15, 14), Color.FromArgb(195, 220, 191), Color.FromArgb(71, 108, 88), 15f);
            using var edge = new Pen(Color.FromArgb(57, 84, 68), 0.85f) { LineJoin = LineJoin.Round };
            g.FillPath(bodyFill, body); g.DrawPath(edge, body);
            using var rimShade = new SolidBrush(Color.FromArgb(85, 39, 70, 53));
            using var rim = new GraphicsPath();
            rim.AddBezier(9.5f, 18.3f, 12, 20, 20, 20.8f, 22.5f, 18.4f);
            rim.AddBezier(22.5f, 18.4f, 23.5f, 20.7f, 9, 22.5f, 9.5f, 18.3f);
            rim.CloseFigure(); g.FillPath(rimShade, rim);
            // 钉帽侧壁采用较深的渐变，上表面单独打光，表现厚度。
            using var capSide = new GraphicsPath();
            capSide.AddBezier(8, 6, 8, 2, 24, 2, 24, 6);
            capSide.AddLine(24, 6, 24, 9);
            capSide.AddBezier(24, 9, 24, 13, 8, 13, 8, 9);
            capSide.CloseFigure();
            using var sideFill = new LinearGradientBrush(new RectangleF(8, 5, 16, 7), Color.FromArgb(185, 211, 180), Color.FromArgb(66, 103, 80), 90f);
            g.FillPath(sideFill, capSide); g.DrawPath(edge, capSide);
            using var capFill = new LinearGradientBrush(new RectangleF(8, 2, 16, 8), Color.FromArgb(235, 244, 221), Color.FromArgb(117, 163, 127), 70f);
            g.FillEllipse(capFill, 8, 2.5f, 16, 7.5f); g.DrawEllipse(edge, 8, 2.5f, 16, 7.5f);
            using var shine = new Pen(Color.FromArgb(200, 249, 255, 239), 1.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(shine, 10.5f, 3.4f, 9.5f, 4, 190, 105);
            using var bodyShine = new Pen(Color.FromArgb(155, 235, 250, 221), 0.9f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawBezier(bodyShine, 13, 12, 14, 14, 13, 16, 11.5f, 17.5f);
        }
        finally { g.Restore(state); }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { redraw.Dispose(); tipTimer.Dispose(); fadeTimer.Dispose(); tip.IsOpen = false; menu.IsOpen = false; image.Dispose(); if (cachedBitmap != IntPtr.Zero) { DeleteObject(cachedBitmap); cachedBitmap = IntPtr.Zero; } }
        base.Dispose(disposing);
    }
}
