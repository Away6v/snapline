using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace SnapLine;

internal sealed class CaptureFlightWindow : Window
{
    private readonly Forms.Screen screen;
    private readonly Border image;
    private readonly double startX;
    private readonly double startY;
    private readonly double startWidth;
    private readonly double startHeight;
    private readonly double endX;
    private readonly double endY;
    private readonly double endWidth;
    private readonly double endHeight;
    private readonly bool dropping;
    private readonly ScaleTransform scale = new();
    private readonly RotateTransform rotation = new();
    private readonly TranslateTransform travel = new();
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool started;
    private IntPtr handle;

    public CaptureFlightWindow(Forms.Screen screen, BitmapSource bitmap, double startX, double startY, double startWidth, double startHeight, double endX, double endY, double endWidth, double endHeight, bool dropping = false)
    {
        this.screen = screen;
        this.dropping = dropping;
        this.startX = startX;
        this.startY = startY;
        this.startWidth = startWidth;
        this.startHeight = startHeight;
        this.endX = endX;
        this.endY = endY;
        this.endWidth = endWidth;
        this.endHeight = endHeight;
        var monitorScale = Native.Scale(screen);
        Width = screen.Bounds.Width / monitorScale;
        Height = screen.Bounds.Height / monitorScale;
        Left = screen.Bounds.Left;
        Top = screen.Bounds.Top;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        var canvas = new Canvas { Width = Width, Height = Height, Background = Brushes.Transparent, IsHitTestVisible = false };
        image = new Border { Width = startWidth, Height = startHeight, Background = Brushes.Transparent, ClipToBounds = true, Child = new Image { Source = bitmap, Stretch = Stretch.Uniform } };
        if (dropping)
        {
            image.Opacity = 0;
            image.CornerRadius = new CornerRadius(13);
            image.Padding = new Thickness(7);
            image.BorderThickness = new Thickness(1);
            image.BorderBrush = AppTheme.Outline;
            image.Background = AppTheme.Surface;
            image.Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 3, Opacity = 0.18 };
            rotation.CenterX = startWidth * 0.5;
            rotation.CenterY = startHeight * 0.3;
        }
        // 飞行只修改渲染变换，避免每帧重排大图及其阴影。
        var transforms = new TransformGroup();
        transforms.Children.Add(scale);
        transforms.Children.Add(rotation);
        transforms.Children.Add(travel);
        image.RenderTransform = transforms;
        image.CacheMode = new BitmapCache();
        travel.X = startX;
        travel.Y = startY;
        canvas.Children.Add(image);
        Content = canvas;
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            Native.SetWindowLongPtr(handle, -20, new IntPtr(Native.GetWindowLongPtr(handle, -20).ToInt64() | 0x20 | 0x08000000 | 0x80));
            HwndSource.FromHwnd(handle).AddHook(HitTest);
        };
    }

    public Task PlayAsync()
    {
        if (started) return completion.Task;
        started = true;
        if (!SystemParameters.ClientAreaAnimation) { completion.TrySetResult(); return completion.Task; }
        Show();
        Native.SetWindowPos(handle, new IntPtr(-1), screen.Bounds.Left, screen.Bounds.Top, screen.Bounds.Width, screen.Bounds.Height, 0x0010);
        if (dropping)
        {
            var duration = TimeSpan.FromMilliseconds(620);
            travel.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(startY, endY, duration)
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
            var direction = Random.Shared.Next(2) == 0 ? -1 : 1;
            rotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, direction * 9, duration)
                { EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut } });
            var opacity = new DoubleAnimationUsingKeyFrames { Duration = duration };
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            opacity.KeyFrames.Add(new SplineDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(100))) { KeySpline = new KeySpline(0.2, 0, 0.2, 1) });
            opacity.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromTimeSpan(duration)) { KeySpline = new KeySpline(0.4, 0, 0.8, 1) });
            opacity.Completed += (_, _) => Close();
            image.BeginAnimation(OpacityProperty, opacity);
        }
        else
        {
            var duration = TimeSpan.FromMilliseconds(560);
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            travel.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(startX, endX, duration) { EasingFunction = easing });
            travel.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(startY, endY, duration) { EasingFunction = easing });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, endWidth / Math.Max(1, startWidth), duration) { EasingFunction = easing });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, endHeight / Math.Max(1, startHeight), duration) { EasingFunction = easing });
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180)) { BeginTime = TimeSpan.FromMilliseconds(380) };
            fade.Completed += (_, _) => Close();
            image.BeginAnimation(OpacityProperty, fade);
        }
        return completion.Task;
    }

    protected override void OnClosed(EventArgs e)
    {
        image.BeginAnimation(OpacityProperty, null);
        travel.BeginAnimation(TranslateTransform.XProperty, null);
        travel.BeginAnimation(TranslateTransform.YProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        rotation.BeginAnimation(RotateTransform.AngleProperty, null);
        completion.TrySetResult();
        base.OnClosed(e);
    }

    private IntPtr HitTest(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != 0x0084) return IntPtr.Zero;
        handled = true;
        return new IntPtr(-1);
    }
}
