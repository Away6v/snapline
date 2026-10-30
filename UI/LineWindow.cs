using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
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
    private const double LineOffsetY = -10;
    private const double CaptureMaxWidth = 195;
    private const double CaptureMaxHeight = 124;
    private readonly CaptureLibrary library;
    private readonly TextLibrary textLibrary;
    private readonly Preferences preferences;
    private readonly Action<string> notify;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Grid root = new();
    private readonly Grid ropeHost = new();
    private readonly StackPanel cards = new() { Orientation = Orientation.Horizontal };
    private readonly Grid cardHost = new();
    private readonly List<Grid> cardContainers = new();
    private readonly Dictionary<string, Grid> cardsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Border> cardSurfacesById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Capture> capturesById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (ScaleTransform Scale, RotateTransform Tilt, TranslateTransform Shift, TranslateTransform Arrival)> transformsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ImageSource?> thumbnailsById = new(StringComparer.Ordinal);
    private Grid? emptyCard;
    private bool animateEmptyCard;
    private readonly SmoothScroll smoothScroll;
    private readonly Dictionary<string, Point> pendingReflowPositions = new(StringComparer.Ordinal);
    private readonly HashSet<string> knownCaptureIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> pendingAdditionIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> pendingRemovalIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (System.Drawing.Rectangle Bounds, TaskCompletionSource<bool> Started)> pinTransitions = new(StringComparer.Ordinal);
    private readonly ScrollViewer scroll;
    private readonly HashSet<(Grid Host, LinearGradientBrush BlurMask, LinearGradientBrush FadeMask)> edgeBlurCards = new();
    private sealed class DockCard
    {
        internal required Grid Container;
        internal required Border Card;
        internal readonly ScaleTransform Scale = new(1, 1);
        internal double Target = 1;
    }
    private readonly List<DockCard> dockCards = new();
    private readonly TranslateTransform slide = new();
    private readonly List<FrameworkElement> hitTargets = new();
    private readonly DispatcherTimer copyHintTimer = new() { Interval = TimeSpan.FromMilliseconds(1800) };
    private readonly DispatcherTimer copyBorderTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private Grid? copyHintContainer;
    private Border? copyHint;
    private Border? copyHintCard;
    private int copyHintVersion;
    private IntPtr handle;
    private Forms.Screen? screen;
    private DateTime edgeSince;
    private DateTime lastInside;
    private DateTime lastMaintenance;
    private bool revealed;
    private bool busy;
    private bool dirty;
    private bool closing;
    private bool removalAnimationRunning;
    private bool reflowAnimationRunning;
    private bool cardPositionUpdatePending;
    private bool libraryUpdateQueued;
    private int reflowEpoch;
    private int showEpoch;
    private int visibleCount = 8;
    private long lastEdgeBlurUpdate;

    public LineWindow(CaptureLibrary library, TextLibrary textLibrary, Preferences preferences, Action<string> notify)
    {
        this.library = library; this.textLibrary = textLibrary; this.preferences = preferences; this.notify = notify;
        copyHintTimer.Tick += (_, _) => ClearCopyHint();
        copyBorderTimer.Tick += (_, _) => { copyBorderTimer.Stop(); ClearCopyBorder(); };
        knownCaptureIds.UnionWith(library.Items.Select(capture => capture.Id));
        Title = "SnapLine"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        Height = 244; Width = 1000;
        FontFamily = new FontFamily("Segoe UI");
        // 整体上移，夹子、绳子和卡片的相对布局保持不变。
        var lineTransform = new TransformGroup();
        lineTransform.Children.Add(slide);
        lineTransform.Children.Add(new TranslateTransform(0, LineOffsetY));
        root.RenderTransform = lineTransform;
        Content = root;
        root.Children.Add(ropeHost);
        ApplyAppearance();
        cards.HorizontalAlignment = HorizontalAlignment.Center;
        cardHost.Children.Add(cards);
        scroll = new ScrollViewer { Content = cardHost, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(20, 0, 20, 6), Background = Brushes.Transparent };
        smoothScroll = new SmoothScroll(scroll, true);
        scroll.PreviewMouseMove += (_, _) => UpdateDockEffect();
        scroll.MouseLeave += (_, _) => ResetDockEffect();
        scroll.ScrollChanged += (_, _) => { UpdateCardHostWidth(); UpdateEdgeBlur(null, EventArgs.Empty); };
        scroll.SizeChanged += (_, _) => UpdateCardHostWidth();
        SizeChanged += (_, _) => UpdateCardPositions();
        Loaded += (_, _) => QueueCardPositionUpdate();
        root.Children.Add(scroll);
        CompositionTarget.Rendering += UpdateEdgeBlur;
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            Native.SetWindowLongPtr(handle, -20, new IntPtr(Native.GetWindowLongPtr(handle, -20).ToInt64() | 0x08000000 | 0x80));
            HwndSource.FromHwnd(handle).AddHook(Hook);
        };
        library.Changed += OnChanged;
        textLibrary.Changed += OnTextChanged;
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
                if (!target.IsVisible || !target.IsHitTestVisible || target.Opacity < 0.02) return false;
                var inverse = target.TransformToVisual(this).Inverse;
                return inverse != null && new Rect(target.RenderSize).Contains(inverse.Transform(local));
            });
            if (!interactive) { handled = true; return new IntPtr(-1); }
        }
        return IntPtr.Zero;
    }
    public void Start() => timer.Start();
    public void Stop() { CompositionTarget.Rendering -= UpdateEdgeBlur; closing = true; CancelPinTransitions(); ClearCopyHint(); smoothScroll.Stop(); timer.Stop(); library.Changed -= OnChanged; textLibrary.Changed -= OnTextChanged; }
    private void CancelPinTransitions()
    {
        foreach (var transition in pinTransitions.Values) transition.Started.TrySetResult(false);
        pinTransitions.Clear();
    }
    internal bool IsRevealed => revealed;
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
        visibleCount = CalculateVisibleCount(Width);
        if (!IsVisible) { slide.BeginAnimation(TranslateTransform.YProperty, null); slide.Y = -Height; Show(); }
        Native.SetWindowPos(handle, new IntPtr(-1), target.Bounds.Left, target.Bounds.Top, target.Bounds.Width, (int)(Height * scale), 0x0010);
        revealed = true;
        showEpoch++;
        lastInside = DateTime.UtcNow.AddSeconds(manual ? 2 : 0);
        var start = slide.Y;
        if (!SystemParameters.ClientAreaAnimation)
        {
            slide.BeginAnimation(TranslateTransform.YProperty, null);
            slide.Y = 0;
        }
        else
        {
            // 从当前显示位置继续，轻微越过终点后回弹，快速反向操作也不会跳帧。
            var bounce = Math.Min(12, Math.Abs(start) * 0.12);
            var animation = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(620), FillBehavior = FillBehavior.Stop
            };
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(start, KeyTime.FromPercent(0)));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(bounce, KeyTime.FromPercent(0.65), new CubicEase { EasingMode = EasingMode.EaseOut }));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(-bounce * 0.3, KeyTime.FromPercent(0.82), new SineEase { EasingMode = EasingMode.EaseInOut }));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), new SineEase { EasingMode = EasingMode.EaseOut }));
            slide.Y = 0;
            slide.BeginAnimation(TranslateTransform.YProperty, animation);
        }
        if (preferences.Capacity == 0 && library.Capacity < Math.Max(100, visibleCount)) await library.SetCapacityAsync(visibleCount);
    }
    private void Conceal()
    {
        if (!revealed || busy) return;
        revealed = false;
        var epoch = ++showEpoch;
        if (!SystemParameters.ClientAreaAnimation)
        {
            slide.BeginAnimation(TranslateTransform.YProperty, null);
            slide.Y = -Height;
            Hide();
            return;
        }
        var animation = new DoubleAnimation(slide.Y, -Height, TimeSpan.FromMilliseconds(480))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.Stop
        };
        slide.Y = -Height;
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
            if (screen != null && !Forms.Screen.AllScreens.Any(s => s.DeviceName == screen.DeviceName)) Conceal();
        }
    }
    public async Task RefreshSettingsAsync()
    {
        ApplyAppearance();
        Rebuild(true);
        await library.SetCapacityAsync(visibleCount);
    }
    internal void SetDisplayMode(string mode)
    {
        if (mode is not ("text" or "screenshot") || preferences.DisplayMode == mode) return;
        preferences.DisplayMode = mode;
        CancelPinTransitions();
        pendingAdditionIds.Clear();
        pendingRemovalIds.Clear();
        cards.Children.Clear();
        cardsById.Clear();
        cardSurfacesById.Clear();
        capturesById.Clear();
        transformsById.Clear();
        thumbnailsById.Clear();
        hitTargets.Clear();
        cardContainers.Clear();
        emptyCard = null;
        Rebuild(true);
    }
    internal void CreateText()
    {
        busy = true;
        try
        {
            var dialog = new TextEntryWindow(null);
            if (IsVisible) dialog.Owner = this;
            else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            if (dialog.ShowDialog() != true || dialog.SavedText == null) return;
            var entry = textLibrary.Add(dialog.SavedText);
            if (entry == null) return;
            SetDisplayMode("text");
            if (!revealed) Toggle();
        }
        finally { FinishInteraction(); }
    }
    private void ApplyAppearance()
    {
        ropeHost.Children.Clear();
        ropeHost.Children.Add(LineAppearance.Rope(preferences.Appearance));
    }
    private void OnChanged()
    {
        if (closing) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(OnChanged); return; }
        var currentIds = library.Items.Select(capture => capture.Id).ToHashSet(StringComparer.Ordinal);
        pendingAdditionIds.UnionWith(currentIds.Except(knownCaptureIds));
        pendingRemovalIds.UnionWith(knownCaptureIds.Except(currentIds));
        // 合并同一轮中的相反操作，避免已恢复的卡片仍执行取下动画。
        pendingRemovalIds.ExceptWith(currentIds);
        pendingAdditionIds.RemoveWhere(id => !currentIds.Contains(id));
        knownCaptureIds.Clear();
        knownCaptureIds.UnionWith(currentIds);
        if (pendingAdditionIds.Count > 0 && preferences.DisplayMode == "screenshot") ShowForNewCapture();
        if (preferences.DisplayMode != "screenshot") return;
        if (busy || removalAnimationRunning) { dirty = true; return; }
        if (libraryUpdateQueued) return;
        // 合并同一轮消息中的变化，避免连续导入反复中断排位动画。
        libraryUpdateQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            libraryUpdateQueued = false;
            if (closing) return;
            if (busy || removalAnimationRunning) { dirty = true; return; }
            dirty = false;
            ProcessLibraryChanges();
        }, DispatcherPriority.Render);
    }
    private void OnTextChanged()
    {
        if (closing) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(OnTextChanged); return; }
        if (preferences.DisplayMode == "text") Rebuild(true);
    }
    private void FinishInteraction()
    {
        busy = removalAnimationRunning;
        lastInside = DateTime.UtcNow;
        if (dirty && !removalAnimationRunning) { dirty = false; ProcessLibraryChanges(); }
    }
    private void ProcessLibraryChanges()
    {
        if (closing) return;
        if (removalAnimationRunning) { dirty = true; return; }
        if (pendingRemovalIds.Count > 0)
        {
            _ = AnimateRemovalsAsync();
            return;
        }
        Rebuild();
    }
    private void ShowForNewCapture()
    {
        if (!Native.GetCursorPos(out var point)) return;
        var target = Forms.Screen.FromPoint(new System.Drawing.Point(point.X, point.Y));
        if (revealed && screen?.DeviceName == target.DeviceName) lastInside = DateTime.UtcNow.AddSeconds(2);
        else Reveal(target, true);
    }
    private async Task AnimateRemovalsAsync()
    {
        if (!revealed || !SystemParameters.ClientAreaAnimation)
        {
            foreach (var id in pendingRemovalIds)
                if (pinTransitions.Remove(id, out var pending)) pending.Started.TrySetResult(false);
            pendingRemovalIds.Clear();
            Rebuild();
            return;
        }
        animateEmptyCard = true;
        removalAnimationRunning = true;
        busy = true;
        var removedIds = pendingRemovalIds.ToArray();
        pendingRemovalIds.Clear();
        var flights = new List<Task>();
        var delayedReflow = false;
        foreach (var id in removedIds.Where(id => !transformsById.ContainsKey(id)))
            if (pinTransitions.Remove(id, out var pending)) pending.Started.TrySetResult(false);
        foreach (var id in removedIds.Where(transformsById.ContainsKey))
        {
            var container = cardsById[id];
            var transforms = transformsById[id];
            try
            {
                StopSway(transforms.Tilt);
                var shiftX = transforms.Shift.X;
                var shiftY = transforms.Shift.Y;
                transforms.Shift.BeginAnimation(TranslateTransform.XProperty, null);
                transforms.Shift.BeginAnimation(TranslateTransform.YProperty, null);
                transforms.Shift.X = shiftX;
                transforms.Shift.Y = shiftY;
                var card = cardSurfacesById[id];
                var inViewport = card.TranslatePoint(new Point(), scroll);
                if (!pinTransitions.ContainsKey(id) && (inViewport.X + card.ActualWidth < 0 || inViewport.X > scroll.ViewportWidth))
                {
                    // 不为视口外的卡片创建全屏飞行窗口，批量移除时避免大量并发绘制。
                    container.Opacity = 0;
                    continue;
                }
                var bitmap = capturesById[id].Thumbnail ?? throw new InvalidOperationException("Thumbnail is unavailable.");
                var start = card.TransformToVisual(this).TransformBounds(new Rect(card.RenderSize));
                var targetScreen = screen ?? Forms.Screen.PrimaryScreen!;
                var monitorScale = Native.Scale(targetScreen);
                var screenHeight = targetScreen.Bounds.Height / monitorScale;
                var endY = Math.Max(start.Y, (screenHeight - start.Height) / 2);
                var isPin = pinTransitions.TryGetValue(id, out var transition);
                var target = transition.Bounds;
                var flight = isPin
                    ? new CaptureFlightWindow(targetScreen, bitmap, start.X, start.Y, start.Width, start.Height,
                        (target.Left - targetScreen.Bounds.Left) / monitorScale, (target.Top - targetScreen.Bounds.Top) / monitorScale,
                        target.Width / monitorScale, target.Height / monitorScale)
                    : new CaptureFlightWindow(targetScreen, bitmap, start.X, start.Y, start.Width, start.Height,
                        start.X, endY, start.Width, start.Height, true);
                flights.Add(flight.PlayAsync());
                if (isPin)
                {
                    pinTransitions.Remove(id);
                    _ = flight.Handoff.ContinueWith(_ => transition.Started.TrySetResult(true), TaskScheduler.Default);
                }
                Animate(container, OpacityProperty, 0, 120);
            }
            catch
            {
                if (pinTransitions.Remove(id, out var pending)) pending.Started.TrySetResult(false);
                delayedReflow = true;
                var duration = TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? 420 : 0);
                transforms.Shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(transforms.Shift.Y, transforms.Shift.Y + 300, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
                var direction = Random.Shared.Next(2) == 0 ? -1 : 1;
                transforms.Tilt.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(transforms.Tilt.Angle, transforms.Tilt.Angle + direction * Random.Shared.Next(7, 15), duration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
                container.BeginAnimation(OpacityProperty, new DoubleAnimation(container.Opacity, 0, duration));
                flights.Add(Task.Delay(duration));
            }
        }
        if (!delayedReflow)
        {
            await Task.Delay(SystemParameters.ClientAreaAnimation ? 120 : 0);
            if (closing) return;
            CaptureSurvivorPositions();
            Rebuild();
        }
        if (flights.Count > 0)
        {
            try { await Task.WhenAll(flights); }
            catch { }
        }
        removalAnimationRunning = false;
        busy = false;
        lastInside = DateTime.UtcNow;
        var changedDuringFlight = dirty || pendingRemovalIds.Count > 0 || pendingAdditionIds.Count > 0;
        dirty = false;
        if (delayedReflow)
        {
            CaptureSurvivorPositions();
            ProcessLibraryChanges();
        }
        else if (changedDuringFlight || library.Items.Count == 0) ProcessLibraryChanges();
    }
    private void CaptureSurvivorPositions()
    {
        pendingReflowPositions.Clear();
        foreach (var capture in library.Items)
        {
            if (!cardsById.TryGetValue(capture.Id, out var container) || !container.IsLoaded) continue;
            try { pendingReflowPositions[capture.Id] = container.TranslatePoint(new Point(container.ActualWidth / 2, container.ActualHeight * 0.04), root); }
            catch (InvalidOperationException) { }
        }
    }
    private void Rebuild(bool refreshAppearance = false)
    {
        if (closing) return;
        if (preferences.DisplayMode == "text")
        {
            RebuildText();
            return;
        }
        var currentCaptures = library.Items.ToDictionary(capture => capture.Id, StringComparer.Ordinal);
        // 无实际内容变化时保留所有动画，包括尚未结束的排位过渡。
        if (!refreshAppearance && currentCaptures.Count == cardsById.Count
            && (currentCaptures.Count > 0 || emptyCard != null)
            && currentCaptures.All(entry => thumbnailsById.TryGetValue(entry.Key, out var thumbnail)
                && ReferenceEquals(thumbnail, entry.Value.Thumbnail)))
        {
            pendingAdditionIds.Clear();
            pendingReflowPositions.Clear();
            return;
        }
        if (pendingReflowPositions.Count == 0) CaptureSurvivorPositions();
        var previousPositions = new Dictionary<string, Point>(pendingReflowPositions, StringComparer.Ordinal);
        pendingReflowPositions.Clear();
        ++reflowEpoch;
        reflowAnimationRunning = false;
        var remainingScroll = smoothScroll.SuspendForLayout();
        // 只移除失效的卡片，保留其余元素和正在运行的摇曳时钟。
        foreach (var id in cardsById.Keys.ToArray())
        {
            if (!refreshAppearance && currentCaptures.TryGetValue(id, out var capture)
                && ReferenceEquals(thumbnailsById[id], capture.Thumbnail)) continue;
            var retired = cardsById[id];
            hitTargets.RemoveAll(target => target.IsDescendantOf(retired));
            cards.Children.Remove(retired);
            cardsById.Remove(id);
            cardSurfacesById.Remove(id);
            capturesById.Remove(id);
            transformsById.Remove(id);
            thumbnailsById.Remove(id);
        }
        if (emptyCard != null && (library.Items.Count > 0 || refreshAppearance))
        {
            cards.Children.Remove(emptyCard);
            emptyCard = null;
        }
        foreach (var transforms in transformsById.Values)
        {
            transforms.Shift.BeginAnimation(TranslateTransform.XProperty, null);
            transforms.Shift.BeginAnimation(TranslateTransform.YProperty, null);
            transforms.Shift.X = transforms.Shift.Y = 0;
        }
        var foreground = AppTheme.Ink;
        if (library.Items.Count == 0 && emptyCard == null && !removalAnimationRunning)
        {
            emptyCard = (Grid)MakeEmptyCard(foreground);
            cards.Children.Add(emptyCard);
            if (animateEmptyCard)
            {
                emptyCard.Opacity = 0;
                var arrival = new TranslateTransform(0, 10);
                emptyCard.RenderTransform = arrival;
                // 首帧保持透明，动画时钟开始前不显示空卡片。
                var fade = Transition(0, 1, 260);
                var rise = Transition(10, 0, 320);
                fade.FillBehavior = rise.FillBehavior = FillBehavior.HoldEnd;
                emptyCard.BeginAnimation(OpacityProperty, fade);
                arrival.BeginAnimation(TranslateTransform.YProperty, rise);
                animateEmptyCard = false;
            }
        }
        for (var index = 0; index < library.Items.Count; index++)
        {
            var capture = library.Items[index];
            if (cardsById.ContainsKey(capture.Id)) continue;
            cards.Children.Insert(index, MakeCard(capture, foreground));
        }
        cardContainers.Clear();
        cardContainers.AddRange(cards.Children.OfType<Grid>());
        var returned = library.Items.FirstOrDefault(capture => capture.ReturningToLine);
        var scrollToNewest = pendingAdditionIds.Count > 0 && returned == null;
        pendingAdditionIds.Clear();
        // 在同一轮布局中完成移除、居中和位移补偿，避免先闪到终点再开始动画。
        if (scrollToNewest) scroll.ScrollToRightEnd();
        UpdateCardHostWidth();
        root.UpdateLayout();
        if (returned != null && cardsById.TryGetValue(returned.Id, out var returnedCard))
        {
            returnedCard.BringIntoView();
            foreach (var capture in library.Items) capture.ReturningToLine = false;
            root.UpdateLayout();
        }
        UpdateCardPositions();
        root.UpdateLayout();
        if (!scrollToNewest && returned == null) smoothScroll.ResumeAfterLayout(remainingScroll);
        AnimateReflowPositions(previousPositions);
    }
    private void RebuildText()
    {
        if (closing) return;
        cards.Children.Clear();
        cardsById.Clear();
        cardSurfacesById.Clear();
        capturesById.Clear();
        transformsById.Clear();
        thumbnailsById.Clear();
        hitTargets.Clear();
        cardContainers.Clear();
        emptyCard = null;
        var foreground = AppTheme.Ink;
        if (textLibrary.Items.Count == 0)
        {
            var empty = new Border
            {
                Width = 240, Height = 104,
                Padding = new Thickness(16), CornerRadius = new CornerRadius(10),
                Background = LineAppearance.CardSurface(preferences.Appearance),
                BorderBrush = LineAppearance.CardOutline(preferences.Appearance), BorderThickness = new Thickness(1),
                Margin = new Thickness(6, LineAppearance.CardTop, 6, 0),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = Words.T("晾衣绳还是空的", "Your text line is empty"), Foreground = foreground, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) },
                        new Button { Content = Words.T("＋ 新建文本", "＋ New text"), Style = ThemeButtons.Primary, Padding = new Thickness(14, 6, 14, 6), HorizontalAlignment = HorizontalAlignment.Center }
                    }
                }
            };
            empty.Effect = new DropShadowEffect { Color = Color.FromRgb(67, 57, 52), BlurRadius = 8, ShadowDepth = 2, Opacity = 0.12 };
            var create = (Button)((StackPanel)empty.Child).Children[1];
            create.Click += (_, _) => CreateText();
            hitTargets.Add(create);
            var holder = new Grid { Width = 252, Height = 148, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Top };
            AddEdgeBlurCard(holder, empty);
            holder.Children.Add(LineAppearance.Peg(preferences.Appearance));
            cards.Children.Add(holder);
            cardContainers.Add(holder);
        }
        else
        {
            foreach (var entry in textLibrary.Items)
                cards.Children.Add(MakeTextCard(entry, foreground));
        }
        UpdateCardHostWidth();
        root.UpdateLayout();
        UpdateCardPositions();
        scroll.ScrollToRightEnd();
    }
    private Grid MakeTextCard(TextEntry entry, Brush foreground)
    {
        var index = LineAppearance.PaletteIndex(entry.Id);
        var container = new Grid { MinWidth = 42, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Top };
        var cardStyle = new Style(typeof(Border));
        cardStyle.Setters.Add(new Setter(Border.BorderBrushProperty, LineAppearance.CardOutline(preferences.Appearance)));
        var cardHover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        cardHover.Setters.Add(new Setter(Border.BorderBrushProperty, LineAppearance.CardHover(preferences.Appearance, index)));
        cardStyle.Triggers.Add(cardHover);
        var text = new TextBlock
        {
            Text = entry.Text, Foreground = foreground, FontSize = 14, LineHeight = 20,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 214, MaxHeight = 120
        };
        var border = new Border
        {
            Margin = new Thickness(0, LineAppearance.CardTop, 0, 0),
            Padding = new Thickness(12), CornerRadius = new CornerRadius(10),
            Background = LineAppearance.CardSurface(preferences.Appearance),
            BorderThickness = new Thickness(1), Style = cardStyle,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            Effect = new DropShadowEffect { Color = Color.FromRgb(67, 57, 52), BlurRadius = 8, ShadowDepth = 2, Opacity = 0.12 },
            Child = text, Cursor = Cursors.Hand
        };
        AddEdgeBlurCard(container, border, true);
        var peg = LineAppearance.Peg(preferences.Appearance, index);
        container.Children.Add(peg);
        cardContainers.Add(container);
        hitTargets.Add(border);
        hitTargets.Add(peg);
        var menu = WarmCardMenu.Create();
        void Add(string zh, string en, string symbol, Action action, bool destructive = false)
        {
            var item = WarmCardMenu.Item(Words.T(zh, en), symbol, destructive);
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        void CopyCard() => _ = CopyWithFeedbackAsync(container, border, _ => { CopyText(entry); return Task.CompletedTask; });
        Add("复制文本", "Copy text", "⧉", CopyCard);
        Add("查看全文", "View full text", "◉", () => OpenText(entry, false));
        Add("编辑", "Edit", "✎", () => OpenText(entry, true));
        menu.Items.Add(WarmCardMenu.Separator());
        Add("从晾衣绳移除", "Remove from line", "×", () => textLibrary.Remove(entry), true);
        Add("取下全部文本", "Remove all text", "×", textLibrary.RemoveAll, true);
        ConfigureMenuAutoClose(menu);
        menu.Opened += (_, _) => busy = true;
        menu.Closed += (_, _) => FinishInteraction();
        border.ContextMenu = menu;
        Point start = default;
        var pressed = false;
        var consumed = false;
        var hold = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(550) };
        hold.Tick += (_, _) => { hold.Stop(); if (!pressed) return; consumed = true; OpenText(entry, true); };
        border.MouseLeftButtonDown += (_, e) =>
        {
            pressed = true; consumed = false; start = e.GetPosition(border); busy = true;
            border.CaptureMouse();
            if (e.ClickCount == 2) { consumed = true; OpenText(entry, false); }
            else hold.Start();
            e.Handled = true;
        };
        border.MouseMove += (_, e) =>
        {
            if (!pressed || consumed || e.LeftButton != MouseButtonState.Pressed) return;
            var point = e.GetPosition(border);
            if (Math.Abs(point.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            consumed = true; pressed = false; hold.Stop(); border.ReleaseMouseCapture();
            try { DragDrop.DoDragDrop(border, new DataObject(DataFormats.UnicodeText, entry.Text), DragDropEffects.Copy); }
            catch (Exception ex) { notify(Words.T("拖放失败：", "Drag failed: ") + ex.Message); }
            finally { FinishInteraction(); }
        };
        border.MouseLeftButtonUp += (_, e) =>
        {
            if (!pressed) return;
            hold.Stop(); pressed = false; border.ReleaseMouseCapture();
            if (!consumed && new Rect(border.RenderSize).Contains(e.GetPosition(border))) CopyCard();
            FinishInteraction(); e.Handled = true;
        };
        border.LostMouseCapture += (_, _) => { if (pressed) { pressed = false; hold.Stop(); FinishInteraction(); } };
        var remove = CreateRemoveButton();
        container.Children.Add(remove);
        hitTargets.Add(remove);
        remove.Click += (_, e) => { e.Handled = true; textLibrary.Remove(entry); };
        void ShowRemove(bool visible)
        {
            remove.IsHitTestVisible = visible;
            Animate(remove, OpacityProperty, visible ? 1 : 0, visible ? 140 : 180);
        }
        container.MouseEnter += (_, _) => ShowRemove(true);
        container.MouseLeave += (_, _) => ShowRemove(remove.IsKeyboardFocusWithin);
        remove.IsKeyboardFocusWithinChanged += (_, _) => ShowRemove(container.IsMouseOver || remove.IsKeyboardFocusWithin);
        container.Loaded += (_, _) => QueueCardPositionUpdate();
        container.Unloaded += (_, _) => { hold.Stop(); if (ReferenceEquals(copyHintContainer, container)) ClearCopyHint(); };
        return container;
    }
    private void AddEdgeBlurCard(Grid container, Border card, bool dockEffect = false)
    {
        // 在原卡片上叠加模糊副本和柔和的乳白色，形成两端的毛玻璃效果。
        var host = new Grid
        {
            Margin = card.Margin,
            HorizontalAlignment = card.HorizontalAlignment,
            VerticalAlignment = card.VerticalAlignment,
            Width = card.Width,
            Height = card.Height
        };
        card.Margin = new Thickness(0);
        card.HorizontalAlignment = HorizontalAlignment.Stretch;
        card.VerticalAlignment = VerticalAlignment.Stretch;
        var clearHost = new Grid();
        clearHost.Children.Add(card);
        var blurred = new System.Windows.Shapes.Rectangle
        {
            Fill = new VisualBrush(card) { Stretch = Stretch.Fill },
            Effect = new BlurEffect { Radius = 20 },
            IsHitTestVisible = false
        };
        var blurMask = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute };
        blurred.OpacityMask = blurMask;
        var frost = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(250, 248, 243)),
            CornerRadius = card.CornerRadius,
            Opacity = 0.36,
            OpacityMask = blurMask,
            IsHitTestVisible = false
        };
        host.Children.Add(clearHost);
        host.Children.Add(blurred);
        host.Children.Add(frost);
        container.Children.Add(host);
        var fadeMask = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute };
        host.OpacityMask = fadeMask;
        var item = (host, blurMask, fadeMask);
        host.Loaded += (_, _) => edgeBlurCards.Add(item);
        host.Unloaded += (_, _) => edgeBlurCards.Remove(item);
        if (dockEffect)
        {
            var dock = new DockCard { Container = container, Card = card };
            // 只缩放卡片内容，卡片顶边和夹子的位置均保持不变。
            host.RenderTransformOrigin = new Point(0.5, 0);
            host.RenderTransform = dock.Scale;
            host.Loaded += (_, _) => dockCards.Add(dock);
            host.Unloaded += (_, _) => dockCards.Remove(dock);
        }
    }
    private void UpdateDockEffect()
    {
        if (!IsVisible || !dockCards.Any(item => item.Card.IsMouseOver)) { ResetDockEffect(); return; }
        var pointer = Mouse.GetPosition(root);
        foreach (var item in dockCards)
        {
            if (!item.Container.IsLoaded || !item.Container.IsDescendantOf(scroll)) continue;
            var center = item.Container.TranslatePoint(new Point(item.Container.ActualWidth / 2, 0), root).X;
            var radius = Math.Max(120, item.Container.ActualWidth * 1.35);
            var proximity = Math.Max(0, 1 - Math.Abs(pointer.X - center) / radius);
            var target = 1 + 0.13 * proximity * proximity * (3 - 2 * proximity);
            SetDockScale(item, target);
        }
    }
    private void ResetDockEffect()
    {
        foreach (var item in dockCards) SetDockScale(item, 1);
    }
    private static void SetDockScale(DockCard item, double target)
    {
        if (Math.Abs(item.Target - target) < 0.01) return;
        item.Target = target;
        Animate(item.Scale, ScaleTransform.ScaleXProperty, target, 170);
        Animate(item.Scale, ScaleTransform.ScaleYProperty, target, 170);
    }
    private void UpdateEdgeBlur(object? sender, EventArgs e)
    {
        if (!IsVisible || scroll.ActualWidth <= 0) return;
        // 边缘蒙版不必跟随每次布局重复计算，限制刷新频率以减轻动画负担。
        var now = Stopwatch.GetTimestamp();
        if ((now - lastEdgeBlurUpdate) / (double)Stopwatch.Frequency < 1.0 / 30) return;
        lastEdgeBlurUpdate = now;
        var width = scroll.ViewportWidth;
        if (width <= 0) return;
        var edge = Math.Min(36 / width, 0.5);
        var fadeEdge = Math.Min(56 / width, 0.5);
        foreach (var (host, mask, fadeMask) in edgeBlurCards)
        {
            // 切换模式时卸载事件可能晚于卡片脱离视觉树。
            if (!host.IsDescendantOf(scroll)) continue;
            var inverse = host.TransformToVisual(scroll).Inverse;
            if (inverse == null) continue;
            var start = inverse.Transform(new Point(0, 0));
            var end = inverse.Transform(new Point(width, 0));
            if (mask.StartPoint != start) mask.StartPoint = start;
            if (mask.EndPoint != end) mask.EndPoint = end;
            if (fadeMask.StartPoint != start) fadeMask.StartPoint = start;
            if (fadeMask.EndPoint != end) fadeMask.EndPoint = end;
            if (fadeMask.GradientStops.Count != 10 || fadeMask.GradientStops[4].Offset != fadeEdge)
            {
                // 卡片在视口边界前平滑淡出，露出后方背景。
                fadeMask.GradientStops.Clear();
                var alpha = new byte[] { 0, 40, 128, 215, 255 };
                for (var i = 0; i < alpha.Length; i++)
                    fadeMask.GradientStops.Add(new GradientStop(Color.FromArgb(alpha[i], 255, 255, 255), fadeEdge * i / 4));
                fadeMask.GradientStops.Add(new GradientStop(Colors.White, 1 - fadeEdge));
                for (var i = 3; i >= 0; i--)
                    fadeMask.GradientStops.Add(new GradientStop(Color.FromArgb(alpha[i], 255, 255, 255), 1 - fadeEdge * i / 4));
            }
            if (mask.GradientStops.Count == 4 && mask.GradientStops[1].Offset == edge) continue;
            mask.GradientStops.Clear();
            mask.GradientStops.Add(new GradientStop(Colors.White, 0));
            mask.GradientStops.Add(new GradientStop(Colors.Transparent, edge));
            mask.GradientStops.Add(new GradientStop(Colors.Transparent, 1 - edge));
            mask.GradientStops.Add(new GradientStop(Colors.White, 1));
        }
    }
    private static void ConfigureMenuAutoClose(ContextMenu menu)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        var entered = false;
        menu.Opened += (_, _) => { entered = false; timer.Start(); };
        menu.Closed += (_, _) => timer.Stop();
        timer.Tick += (_, _) =>
        {
            if (!menu.IsOpen || !Native.GetCursorPos(out var cursor)) return;
            var local = menu.PointFromScreen(new Point(cursor.X, cursor.Y));
            var inside = new Rect(menu.RenderSize).Contains(local);
            if (inside) entered = true;
            else if (entered) menu.IsOpen = false;
        };
    }
    private Button CreateRemoveButton()
    {
        var remove = new Button
        {
            Width = 24, Height = 24, Padding = new Thickness(0), Margin = new Thickness(0, LineAppearance.CardTop - 10, -6, 0),
            ToolTip = Words.T("从晾衣绳移除", "Remove from line"),
            Opacity = 0, IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Background = LineAppearance.CardSurface(preferences.Appearance),
            BorderBrush = LineAppearance.CardOutline(preferences.Appearance),
            BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
            FocusVisualStyle = null,
            Effect = new DropShadowEffect { Color = Color.FromRgb(67, 57, 52), BlurRadius = 5, ShadowDepth = 1, Opacity = 0.14 }
        };
        var removeIcon = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 3,3 L 11,11 M 11,3 L 3,11"),
            Width = 10, Height = 10, Stretch = Stretch.Uniform, StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round
        };
        removeIcon.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, new Binding(nameof(Control.Foreground)) { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1) });
        remove.Content = removeIcon;
        var removeStyle = new Style(typeof(Button));
        removeStyle.Setters.Add(new Setter(Control.ForegroundProperty, AppTheme.Muted));
        var chrome = new FrameworkElementFactory(typeof(Border), "Chrome");
        chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
        chrome.SetValue(Border.SnapsToDevicePixelsProperty, true);
        chrome.SetBinding(Border.BackgroundProperty, new Binding(nameof(Control.Background)) { RelativeSource = RelativeSource.TemplatedParent });
        chrome.SetBinding(Border.BorderBrushProperty, new Binding(nameof(Control.BorderBrush)) { RelativeSource = RelativeSource.TemplatedParent });
        chrome.SetBinding(Border.BorderThicknessProperty, new Binding(nameof(Control.BorderThickness)) { RelativeSource = RelativeSource.TemplatedParent });
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        chrome.AppendChild(content);
        var removeTemplate = new ControlTemplate(typeof(Button)) { VisualTree = chrome };
        var hover = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(213, 67, 73)), "Chrome"));
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(246, 135, 137)), "Chrome"));
        hover.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        var pressedState = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressedState.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(156, 42, 48)), "Chrome"));
        pressedState.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(156, 42, 48)), "Chrome"));
        var focused = new Trigger { Property = Button.IsKeyboardFocusedProperty, Value = true };
        focused.Setters.Add(new Setter(Border.BorderBrushProperty, AppTheme.AccentDark, "Chrome"));
        focused.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "Chrome"));
        removeTemplate.Triggers.Add(hover);
        removeTemplate.Triggers.Add(pressedState);
        removeTemplate.Triggers.Add(focused);
        removeStyle.Setters.Add(new Setter(Control.TemplateProperty, removeTemplate));
        remove.Style = removeStyle;
        System.Windows.Automation.AutomationProperties.SetName(remove, Words.T("从晾衣绳移除", "Remove from line"));
        Panel.SetZIndex(remove, 2);
        return remove;
    }
    private async Task CopyWithFeedbackAsync(Grid container, Border card, Func<Func<bool>, Task> copy)
    {
        ClearCopyHint();
        var version = copyHintVersion;
        var label = new TextBlock { Text = Words.T("正在复制…", "Copying…"), Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold };
        var hint = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(54, 132, 110)),
            CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(3, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Child = label
        };
        copyHintContainer = container;
        copyHint = hint;
        copyHintCard = card;
        container.Children.Add(hint);
        card.BorderBrush = new SolidColorBrush(Color.FromRgb(70, 160, 132));
        try
        {
            await copy(() => version == copyHintVersion);
            if (version != copyHintVersion || !container.IsLoaded) return;
            label.Text = Words.T("已复制", "Copied");
            copyHintTimer.Start();
            copyBorderTimer.Start();
        }
        catch (Exception ex)
        {
            if (version == copyHintVersion) ClearCopyHint();
            notify(Words.T("无法复制：", "Cannot copy: ") + ex.Message);
        }
    }
    private void ClearCopyBorder()
    {
        if (copyHintCard == null) return;
        copyHintCard.ClearValue(Border.BorderBrushProperty);
        copyHintCard = null;
    }
    private void ClearCopyHint()
    {
        copyHintVersion++;
        copyHintTimer.Stop();
        copyBorderTimer.Stop();
        ClearCopyBorder();
        if (copyHintContainer != null && copyHint != null) copyHintContainer.Children.Remove(copyHint);
        copyHintContainer = null;
        copyHint = null;
    }
    private static void CopyText(TextEntry entry)
    {
        var data = new DataObject();
        data.SetText(entry.Text, TextDataFormat.UnicodeText);
        data.SetData("SnapLine.Copy", "1");
        Clipboard.SetDataObject(data, true);
    }
    private void OpenText(TextEntry? entry, bool editing)
    {
        var dialog = new TextEntryWindow(entry, !editing) { Owner = this };
        if (dialog.ShowDialog() == true && entry != null && dialog.SavedText != null) textLibrary.Edit(entry, dialog.SavedText);
        FinishInteraction();
    }
    private FrameworkElement MakeEmptyCard(Brush foreground)
    {
        var container = new Grid
        {
            Width = 360,
            Height = 138,
            Margin = new Thickness(6, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        var card = new Border
        {
            Width = 344,
            Height = 92,
            Margin = new Thickness(0, LineAppearance.CardTop, 0, 0),
            Padding = new Thickness(20, 14, 20, 14),
            CornerRadius = new CornerRadius(10),
            Background = LineAppearance.CardSurface(preferences.Appearance),
            BorderBrush = LineAppearance.CardOutline(preferences.Appearance),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Effect = new DropShadowEffect { Color = Color.FromRgb(67, 57, 52), BlurRadius = 8, ShadowDepth = 2, Opacity = 0.12 },
            Child = new TextBlock
            {
                Text = Words.T("晾衣绳还是空的\n按 Win+Shift+S 截图，让第一张图片挂上来。", "Your line is waiting.\nPress Win+Shift+S to hang your first screenshot."),
                Foreground = foreground,
                FontSize = 14,
                LineHeight = 24,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            }
        };
        AddEdgeBlurCard(container, card);
        container.Children.Add(LineAppearance.Peg(preferences.Appearance));
        cardContainers.Add(container);
        return container;
    }
    private FrameworkElement MakeCard(Capture capture, Brush foreground)
    {
        var (imageWidth, imageHeight) = CardImageSize(capture);
        var clipIndex = LineAppearance.PaletteIndex(capture.Id);
        var container = new Grid { Width = Math.Max(30, imageWidth + 16), Height = imageHeight + 16 + LineAppearance.CardTop, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Top };
        cardContainers.Add(container);
        var scale = new ScaleTransform(1, 1);
        var tilt = new RotateTransform((capture.Id[0] % 5) - 2);
        var shift = new TranslateTransform();
        var arrival = new TranslateTransform();
        var transforms = new TransformGroup();
        transforms.Children.Add(scale);
        transforms.Children.Add(tilt);
        transforms.Children.Add(arrival);
        transforms.Children.Add(shift);
        container.RenderTransformOrigin = new Point(0.5, 0.04);
        container.RenderTransform = transforms;
        cardsById[capture.Id] = container;
        capturesById[capture.Id] = capture;
        transformsById[capture.Id] = (scale, tilt, shift, arrival);
        thumbnailsById[capture.Id] = capture.Thumbnail;
        var animateEntry = (pendingAdditionIds.Contains(capture.Id) || capture.ReturningToLine) && SystemParameters.ClientAreaAnimation;
        if (animateEntry) container.Opacity = 0;
        var entryStarted = false;
        container.Loaded += (_, _) =>
        {
            if (entryStarted) return;
            entryStarted = true;
            QueueCardPositionUpdate();
            StartSway(tilt, capture);
            if (!animateEntry) return;
            Dispatcher.BeginInvoke(() =>
            {
                if (!IsCurrentCard(capture.Id, container)) return;
                if (capture.AnimationOrigin is System.Drawing.Point origin && screen != null && capture.PixelWidth > 0 && capture.PixelHeight > 0)
                {
                    var center = container.TranslatePoint(new Point(container.ActualWidth / 2, LineAppearance.CardTop + 8 + imageHeight / 2), root);
                    center.Y += LineOffsetY;
                    _ = AnimateCaptureArrivalAsync(container, capture, scale, tilt, center, imageWidth, imageHeight, origin, screen);
                    return;
                }
                // 入场与排位使用独立平移，避免连续添加时互相覆盖。
                arrival.Y = 18;
                scale.ScaleX = scale.ScaleY = 0.94;
                Animate(arrival, TranslateTransform.YProperty, 0, 360);
                Animate(scale, ScaleTransform.ScaleXProperty, 1, 360);
                Animate(scale, ScaleTransform.ScaleYProperty, 1, 360);
                Animate(container, OpacityProperty, 1, 220);
            }, DispatcherPriority.Loaded);
        };
        var cardStyle = new Style(typeof(Border));
        cardStyle.Setters.Add(new Setter(Border.BorderBrushProperty, LineAppearance.CardOutline(preferences.Appearance)));
        cardStyle.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(1)));
        cardStyle.Setters.Add(new Setter(Border.BackgroundProperty, LineAppearance.CardSurface(preferences.Appearance)));
        var cardBorderHover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        cardBorderHover.Setters.Add(new Setter(Border.BorderBrushProperty, LineAppearance.CardHover(preferences.Appearance, clipIndex)));
        cardStyle.Triggers.Add(cardBorderHover);
        var card = new Border { Margin = new Thickness(0, LineAppearance.CardTop, 0, 0), CornerRadius = new CornerRadius(10), Style = cardStyle, Padding = new Thickness(7), Cursor = Cursors.Hand, Effect = new DropShadowEffect { Color = Color.FromRgb(67, 57, 52), BlurRadius = 8, ShadowDepth = 2, Opacity = 0.12 } };
        cardSurfacesById[capture.Id] = card;
        card.CacheMode = new BitmapCache();
        card.Width = imageWidth + 16;
        card.Height = imageHeight + 16;
        card.HorizontalAlignment = HorizontalAlignment.Center;
        var interior = new Grid();
        interior.RowDefinitions.Add(new RowDefinition { Height = new GridLength(imageHeight) });
        var image = new Image { Source = capture.Thumbnail, Stretch = Stretch.Uniform, SnapsToDevicePixels = true, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center };
        var preview = new Border { Background = Brushes.Transparent, ClipToBounds = true, Child = capture.Thumbnail == null
            ? new TextBlock { Text = Words.T("图片暂不可用", "Image unavailable"), Foreground = AppTheme.Muted, FontSize = 10, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            : image };
        interior.Children.Add(preview);
        Grid.SetRow(preview, 0);
        var remove = CreateRemoveButton();
        card.Child = interior; AddEdgeBlurCard(container, card, true);
        container.Children.Add(remove);
        remove.Click += async (_, e) =>
        {
            e.Handled = true;
            remove.IsEnabled = false;
            try { await library.RemoveAsync(capture); }
            finally { if (IsCurrentCard(capture.Id, container)) remove.IsEnabled = true; }
        };
        void ShowRemove(bool visible)
        {
            remove.IsHitTestVisible = visible;
            Animate(remove, OpacityProperty, visible ? 1 : 0, visible ? 140 : 180);
        }
        container.MouseEnter += (_, _) => ShowRemove(true);
        container.MouseLeave += (_, _) => ShowRemove(remove.IsKeyboardFocusWithin);
        remove.IsKeyboardFocusWithinChanged += (_, _) => ShowRemove(container.IsMouseOver || remove.IsKeyboardFocusWithin);
        Task CopyCardAsync() => CopyWithFeedbackAsync(container, card, isCurrent => CopyToClipboardAsync(capture, isCurrent));
        var peg = LineAppearance.Peg(preferences.Appearance, clipIndex);
        peg.CacheMode = new BitmapCache();
        peg.Cursor = Cursors.Hand;
        peg.ToolTip = ThemeToolTips.Create(Words.T("取下并钉在桌面", "Pin to desktop"));
        ToolTipService.SetInitialShowDelay(peg, 350);
        async Task PinToScreenAsync()
        {
            if (!peg.IsEnabled) return;
            peg.IsEnabled = false;
            try
            {
                var bitmap = await Task.Run(() => PinnedCaptureWindow.PrepareImage(CaptureLibrary.LoadFull(capture.Path)));
                if (!IsCurrentCard(capture.Id, container)) { bitmap.Dispose(); return; }
                var target = screen ?? Forms.Screen.FromPoint(Forms.Cursor.Position);
                var pinned = new PinnedCaptureWindow(bitmap, target, capture, library, notify);
                // 在飞行动画开始前准备贴图画面，淡入期间只提交透明度。
                pinned.PrepareFrame();
                var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                pinTransitions[capture.Id] = (pinned.ImageBounds, started);
                if (!await library.TakeOffAsync(capture)) { pinTransitions.Remove(capture.Id); pinned.Dispose(); return; }
                var completed = await Task.WhenAny(started.Task, Task.Delay(5000));
                var animate = completed == started.Task && await started.Task;
                if (!animate) pinTransitions.Remove(capture.Id);
                if (closing) { pinned.Dispose(); return; }
                pinned.ShowWithFade(animate);
            }
            catch (Exception ex) { notify(Words.T("无法钉住图片：", "Cannot pin image: ") + ex.Message); }
            finally { peg.IsEnabled = true; }
        }
        peg.MouseLeftButtonDown += async (_, e) => { e.Handled = true; await PinToScreenAsync(); };
        container.Children.Add(peg);
        hitTargets.Add(peg);
        hitTargets.Add(card);
        hitTargets.Add(remove);
        var menu = WarmCardMenu.Create();
        void Add(string zh, string en, string symbol, Action action, bool destructive = false)
        {
            var entry = WarmCardMenu.Item(Words.T(zh, en), symbol, destructive);
            entry.Click += (_, _) => action();
            menu.Items.Add(entry);
        }
        Add("复制图片", "Copy image", "⧉", () => _ = CopyCardAsync());
        Add("预览", "Preview", "preview", () => Preview(capture));
        Add("钉在屏幕", "Pin to screen", "pin", () => _ = PinToScreenAsync());
        Add("用默认应用打开", "Open with default app", "open", () => Open(capture.Path));
        Add("用画图编辑副本", "Edit in Paint", "✎", () => _ = EditAsync(capture));
        Add("另存为…", "Save as…", "save", () => SaveAs(capture));
        Add("在文件夹中显示", "Show in folder", "folder", () => RevealFile(capture));
        menu.Items.Add(WarmCardMenu.Separator());
        Add("从晾衣绳移除", "Remove from line", "remove", () => _ = library.RemoveAsync(capture), true);
        Add("取下全部截图", "Remove all screenshots", "remove-all", () => _ = library.RemoveAllAsync(), true);
        ConfigureMenuAutoClose(menu);
        menu.Opened += (_, _) => busy = true;
        menu.Closed += (_, _) => FinishInteraction();
        card.ContextMenu = menu;
        Point start = default;
        bool pressed = false, consumed = false;
        var hold = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(550) };
        hold.Tick += (_, _) => { hold.Stop(); if (!pressed) return; consumed = true; _ = EditAsync(capture); };
        card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject target && IsButton(target)) return;
            pressed = true; consumed = false; start = e.GetPosition(card); busy = true;
            card.CaptureMouse();
            if (e.ClickCount == 2) { hold.Stop(); consumed = true; Open(capture.Path); }
            else hold.Start();
            e.Handled = true;
        };
        card.MouseMove += (_, e) =>
        {
            if (!pressed || consumed || e.LeftButton != MouseButtonState.Pressed) return;
            var point = e.GetPosition(card);
            if (Math.Abs(point.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            consumed = true; pressed = false; hold.Stop(); card.ReleaseMouseCapture();
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
        card.MouseLeftButtonUp += (_, e) => { if (!pressed) return; hold.Stop(); pressed = false; card.ReleaseMouseCapture(); if (!consumed && new Rect(card.RenderSize).Contains(e.GetPosition(card))) _ = CopyCardAsync(); FinishInteraction(); e.Handled = true; };
        card.MouseLeave += (_, _) => { if (pressed && Mouse.LeftButton != MouseButtonState.Pressed) { pressed = false; hold.Stop(); FinishInteraction(); } };
        card.LostMouseCapture += (_, _) => { if (pressed) { pressed = false; hold.Stop(); FinishInteraction(); } };
        container.Unloaded += (_, _) =>
        {
            hold.Stop();
            if (ReferenceEquals(copyHintContainer, container)) ClearCopyHint();
            tilt.BeginAnimation(RotateTransform.AngleProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            shift.BeginAnimation(TranslateTransform.XProperty, null);
            shift.BeginAnimation(TranslateTransform.YProperty, null);
            arrival.BeginAnimation(TranslateTransform.XProperty, null);
            arrival.BeginAnimation(TranslateTransform.YProperty, null);
            container.BeginAnimation(OpacityProperty, null);
        };

        return container;
    }
    private async Task AnimateCaptureArrivalAsync(Grid container, Capture capture, ScaleTransform scale, RotateTransform tilt, Point targetCenter, double targetWidth, double targetHeight, System.Drawing.Point origin, Forms.Screen targetScreen)
    {
        try
        {
            var bitmap = capture.Thumbnail ?? await Task.Run(() => CaptureLibrary.LoadFull(capture.Path));
            var monitorScale = Native.Scale(targetScreen);
            var screenWidth = targetScreen.Bounds.Width / monitorScale;
            var screenHeight = targetScreen.Bounds.Height / monitorScale;
            var startWidth = capture.PixelWidth / monitorScale;
            var startHeight = capture.PixelHeight / monitorScale;
            var fit = Math.Min(1, Math.Min(screenWidth * 0.82 / startWidth, screenHeight * 0.82 / startHeight));
            startWidth *= fit;
            startHeight *= fit;
            var pointerX = (origin.X - targetScreen.Bounds.Left) / monitorScale;
            var pointerY = (origin.Y - targetScreen.Bounds.Top) / monitorScale;
            var startX = Math.Clamp(pointerX - startWidth, 0, Math.Max(0, screenWidth - startWidth));
            var startY = Math.Clamp(pointerY - startHeight, 0, Math.Max(0, screenHeight - startHeight));
            if (capture.ReturnFlightBounds is System.Drawing.Rectangle bounds)
            {
                // 挂回时从贴图当前的位置和尺寸开始，沿用截图入场动画。
                startX = (bounds.Left - targetScreen.Bounds.Left) / monitorScale;
                startY = (bounds.Top - targetScreen.Bounds.Top) / monitorScale;
                startWidth = bounds.Width / monitorScale;
                startHeight = bounds.Height / monitorScale;
                capture.ReturnFlightBounds = null;
                capture.AnimationOrigin = null;
            }
            var targetX = targetCenter.X - targetWidth / 2;
            var targetY = targetCenter.Y - targetHeight / 2;
            var flight = new CaptureFlightWindow(targetScreen, bitmap, startX, startY, startWidth, startHeight, targetX, targetY, targetWidth, targetHeight);
            var animation = flight.PlayAsync();
            await flight.Handoff;
            if (!IsCurrentCard(capture.Id, container)) { await animation; return; }
            scale.ScaleX = 0.96;
            scale.ScaleY = 0.96;
            Animate(scale, ScaleTransform.ScaleXProperty, 1, 200);
            Animate(scale, ScaleTransform.ScaleYProperty, 1, 200);
            Animate(container, OpacityProperty, 1, 180);
            await animation;
        }
        catch
        {
            if (!IsCurrentCard(capture.Id, container)) return;
            container.BeginAnimation(OpacityProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = 1;
            scale.ScaleY = 1;
            container.Opacity = 1;
        }
    }
    private static void StartSway(RotateTransform tilt, Capture capture)
    {
        if (!SystemParameters.ClientAreaAnimation || tilt.HasAnimatedProperties) return;
        var center = (capture.Id[0] % 5) - 2;
        var amplitude = 1.5 + capture.Id[2] % 5 * 0.16;
        var period = TimeSpan.FromSeconds(7.6 + capture.Id[1] % 5 * 0.55);
        var direction = capture.Id[3] % 2 == 0 ? 1 : -1;
        // 从当前角度开始闭合循环，不在首帧跳到极值；不同卡片保留各自节奏。
        var sway = new DoubleAnimationUsingKeyFrames { Duration = period, RepeatBehavior = RepeatBehavior.Forever };
        sway.KeyFrames.Add(new LinearDoubleKeyFrame(tilt.Angle, KeyTime.FromPercent(0)));
        sway.KeyFrames.Add(new EasingDoubleKeyFrame(center + direction * amplitude, KeyTime.FromPercent(0.25), new SineEase { EasingMode = EasingMode.EaseOut }));
        sway.KeyFrames.Add(new EasingDoubleKeyFrame(center - direction * amplitude, KeyTime.FromPercent(0.75), new SineEase { EasingMode = EasingMode.EaseInOut }));
        sway.KeyFrames.Add(new EasingDoubleKeyFrame(center, KeyTime.FromPercent(1), new SineEase { EasingMode = EasingMode.EaseIn }));
        tilt.BeginAnimation(RotateTransform.AngleProperty, sway);
    }
    private bool IsCurrentCard(string id, Grid container) => !closing && cardsById.TryGetValue(id, out var current) && ReferenceEquals(current, container) && library.Items.Any(capture => capture.Id == id);

    private static DoubleAnimation Transition(double from, double to, double milliseconds) => new(from, to,
        TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? milliseconds : 0))
    {
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        FillBehavior = FillBehavior.Stop
    };

    private static void Animate(DependencyObject target, DependencyProperty property, double to, double milliseconds)
    {
        var animation = Transition((double)target.GetValue(property), to, milliseconds);
        target.SetValue(property, to);
        if (target is UIElement element) element.BeginAnimation(property, animation);
        else if (target is Animatable animatable) animatable.BeginAnimation(property, animation);
    }
    private static void StopSway(RotateTransform tilt)
    {
        var angle = tilt.Angle;
        tilt.BeginAnimation(RotateTransform.AngleProperty, null);
        tilt.Angle = angle;
    }
    private (double Width, double Height) CardImageSize(Capture capture)
    {
        var source = capture.Thumbnail;
        var sourceWidth = capture.PixelWidth > 0 ? capture.PixelWidth : source?.PixelWidth ?? 4;
        var sourceHeight = capture.PixelHeight > 0 ? capture.PixelHeight : source?.PixelHeight ?? 3;
        // 依据原图尺寸使用统一缩放比例，只有超出可用尺寸时才进一步缩小。
        var scale = Math.Min(preferences.ImageScalePercent / 100.0, Math.Min(CaptureMaxWidth / sourceWidth, CaptureMaxHeight / sourceHeight));
        return (Math.Max(1, Math.Round(sourceWidth * scale)), Math.Max(1, Math.Round(sourceHeight * scale)));
    }
    private int CalculateVisibleCount(double availableWidth)
    {
        var remaining = Math.Max(1, availableWidth - 40);
        var captures = library.Items.Reverse().ToArray();
        if (captures.Length == 0) return Math.Max(1, (int)(remaining / 180));
        var count = 0;
        var widths = new List<double>();
        foreach (var capture in captures)
        {
            var (width, _) = CardImageSize(capture);
            var cardWidth = Math.Max(30, width + 16) + 12;
            if (count > 0 && remaining < cardWidth) break;
            remaining -= cardWidth;
            widths.Add(cardWidth);
            count++;
        }
        if (count == captures.Length && remaining > 0)
            count += (int)(remaining / widths.Average());
        return Math.Max(1, count);
    }
    private void UpdateCardPositions()
    {
        if (reflowAnimationRunning || ActualWidth <= 16 || cardContainers.Count == 0) return;
        foreach (var container in cardContainers)
        {
            // 新卡片已完成布局但 Loaded 事件可能尚未触发，仍需在首帧前定位。
            // 空状态没有截图卡片的 Loaded 回调，不能依赖该事件补偿悬挂高度。
            if (!container.IsArrangeValid || container.ActualWidth <= 0) continue;
            Point center;
            try
            {
                // 绳子高度只依据布局位置，避免入场、排位和摇曳反过来改变挂点。
                var slot = LayoutInformation.GetLayoutSlot(container);
                center = cards.TranslatePoint(new Point(slot.X + slot.Width / 2, 0), root);
            }
            catch (InvalidOperationException) { continue; }
            var margin = container.Margin;
            var top = LineAppearance.PegTop(center.X, ActualWidth);
            if (Math.Abs(margin.Top - top) >= 0.25)
                container.Margin = new Thickness(margin.Left, top, margin.Right, margin.Bottom);
        }
    }
    private void QueueCardPositionUpdate()
    {
        if (cardPositionUpdatePending) return;
        cardPositionUpdatePending = true;
        Dispatcher.BeginInvoke(() =>
        {
            cardPositionUpdatePending = false;
            UpdateCardPositions();
        }, DispatcherPriority.Loaded);
    }
    private void AnimateReflowPositions(Dictionary<string, Point> previousPositions)
    {
        if (previousPositions.Count == 0) return;
        var movements = new List<(TranslateTransform Shift, double X, double Y)>();
        foreach (var (id, previous) in previousPositions)
        {
            if (!cardsById.TryGetValue(id, out var container) || !transformsById.TryGetValue(id, out var transforms) || !container.IsLoaded) continue;
            Point current;
            try { current = container.TranslatePoint(new Point(container.ActualWidth / 2, container.ActualHeight * 0.04), root); }
            catch (InvalidOperationException) { continue; }
            var offsetX = previous.X - current.X;
            var offsetY = previous.Y - current.Y;
            if (Math.Abs(offsetX) < 0.8 && Math.Abs(offsetY) < 0.8) continue;
            movements.Add((transforms.Shift, offsetX, offsetY));
        }
        if (movements.Count == 0) return;
        reflowAnimationRunning = true;
        var epoch = ++reflowEpoch;
        var duration = SystemParameters.ClientAreaAnimation ? 380 : 0;
        for (var index = 0; index < movements.Count; index++)
        {
            var (shift, offsetX, offsetY) = movements[index];
            shift.X = offsetX;
            shift.Y = offsetY;
            var horizontal = Transition(offsetX, 0, duration);
            var vertical = Transition(offsetY, 0, duration);
            // 首帧以旧位置为基值，时钟开始前也不会暴露新布局的位置。
            horizontal.FillBehavior = vertical.FillBehavior = FillBehavior.HoldEnd;
            if (index == movements.Count - 1)
                vertical.Completed += (_, _) =>
                {
                    if (closing || epoch != reflowEpoch) return;
                    foreach (var movement in movements)
                    {
                        movement.Shift.X = movement.Shift.Y = 0;
                        movement.Shift.BeginAnimation(TranslateTransform.XProperty, null);
                        movement.Shift.BeginAnimation(TranslateTransform.YProperty, null);
                    }
                    reflowAnimationRunning = false;
                    QueueCardPositionUpdate();
                };
            shift.BeginAnimation(TranslateTransform.XProperty, horizontal);
            shift.BeginAnimation(TranslateTransform.YProperty, vertical);
        }
    }
    private void UpdateCardHostWidth()
    {
        var viewport = Math.Max(0, scroll.ViewportWidth);
        if (Math.Abs(cardHost.MinWidth - viewport) > 0.5) cardHost.MinWidth = viewport;
        UpdateCardPositions();
    }
    private static bool IsButton(DependencyObject target)
    {
        while (target != null) { if (target is Button) return true; target = VisualTreeHelper.GetParent(target); }
        return false;
    }
    private async Task PruneAfterDragAsync() { await Task.Delay(700); library.Prune(); }
    private static async Task CopyToClipboardAsync(Capture capture, Func<bool> isCurrent)
    {
        var (bitmap, pngBytes) = await Task.Run(() =>
        {
            var bitmap = CaptureLibrary.LoadFull(capture.Path);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var png = new MemoryStream();
            encoder.Save(png);
            return (bitmap, png.ToArray());
        });
        var data = new DataObject();
        data.SetImage(bitmap); data.SetData("SnapLine.Copy", "1");
        data.SetData("PNG", new MemoryStream(pngBytes));
        for (var attempt = 0; ; attempt++)
        {
            if (!isCurrent()) return;
            try { Clipboard.SetDataObject(data, true); break; }
            catch (System.Runtime.InteropServices.COMException) when (attempt < 4) { await Task.Delay(100); }
        }
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
        try
        {
            if (!File.Exists(capture.Path)) throw new FileNotFoundException(Words.T("截图文件不存在。", "The screenshot file does not exist."), capture.Path);
            if (Process.Start(new ProcessStartInfo("explorer.exe", "/select, \"" + capture.Path + "\"") { UseShellExecute = true }) == null)
                throw new InvalidOperationException(Words.T("无法启动文件资源管理器。", "Could not start File Explorer."));
        }
        catch (Exception ex) { MessageBox.Show(Words.T("无法在文件夹中显示：", "Cannot show file in folder: ") + ex.Message, "SnapLine", MessageBoxButton.OK, MessageBoxImage.Warning); }
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
            var preview = new Window { Title = "SnapLine · " + Path.GetFileName(capture.Path), Width = 900, Height = 620, WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = AppTheme.Surface };
            preview.Content = new Image { Source = image, Stretch = Stretch.Uniform, Margin = new Thickness(16) };
            preview.KeyDown += (_, e) => { if (e.Key == Key.Escape) preview.Close(); };
            preview.Show();
        }
        catch (Exception ex) { notify(Words.T("无法预览：", "Cannot preview: ") + ex.Message); }
    }
}
