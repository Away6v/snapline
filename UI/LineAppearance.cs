using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SnapLine;

internal static class LineAppearance
{
    internal const string Hemp = "hemp";
    internal const string Classic = "classic";
    private const double RopeSideInset = 8;
    // 绳子穿过夹子上部，卡片与绳子之间留出空隙。
    private const double RopeTopInset = 28;
    private const double RopeSag = 26;
    // 增大此值会让夹子与卡片整体相对绳子上移，夹入深度保持不变。
    private const double PegRopeOverlap = 19;
    // 卡片顶部靠近夹子末端，夹入深度通过夹子的外边距微调。
    internal const double CardTop = 32;

    private static readonly Color[] MacaronColors =
    {
        Color.FromRgb(0xF2, 0xB4, 0xC5),
        Color.FromRgb(0xA9, 0xD9, 0xC9),
        Color.FromRgb(0xC5, 0xB6, 0xE8),
        Color.FromRgb(0xF1, 0xC9, 0xA5)
    };

    private static readonly Color[] MacaronEdges =
    {
        Color.FromRgb(0xB9, 0x78, 0x8A),
        Color.FromRgb(0x6F, 0xA9, 0x98),
        Color.FromRgb(0x8E, 0x7F, 0xB6),
        Color.FromRgb(0xB4, 0x8A, 0x68)
    };

    internal static int PaletteIndex(string stableKey)
    {
        uint hash = 2166136261u;
        foreach (var character in stableKey)
        {
            unchecked
            {
                hash ^= character;
                hash *= 16777619u;
            }
        }
        return (int)(hash % (uint)MacaronColors.Length);
    }

    internal static FrameworkElement Rope(string appearance)
    {
        var layer = new Grid { Height = RopeTopInset + RopeSag, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
        var fade = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(1, 0),
            GradientStops = new GradientStopCollection
            {
                new(Colors.Transparent, 0), new(Colors.White, 0),
                new(Colors.White, 1), new(Colors.Transparent, 1)
            }
        };
        layer.OpacityMask = fade;
        // 渐隐宽度保持适度，预览缩窄时按比例收敛。
        layer.SizeChanged += (_, _) =>
        {
            var edge = layer.ActualWidth > 0 ? Math.Min(48 / layer.ActualWidth, 0.18) : 0;
            fade.GradientStops[1].Offset = edge;
            fade.GradientStops[2].Offset = 1 - edge;
        };
        if (appearance == Classic)
        {
            layer.Children.Add(RopeStroke(new SolidColorBrush(Color.FromRgb(0xA8, 0xAE, 0xB7)), 2.2));
            layer.Children.Add(RopeStroke(new SolidColorBrush(Color.FromArgb(155, 255, 255, 255)), 0.65));
            return layer;
        }

        layer.Children.Add(RopeStroke(new SolidColorBrush(Color.FromRgb(0x84, 0x63, 0x46)), 2.8));
        layer.Children.Add(RopeStroke(RopeTwistBrush(), 2.3));
        return layer;
    }

    internal static double PegTop(double centerX, double layerWidth)
    {
        var ropeWidth = Math.Max(1, layerWidth - RopeSideInset * 2);
        var t = Math.Clamp((centerX - RopeSideInset) / ropeWidth, 0, 1);
        var ropeY = RopeTopInset + 4 * RopeSag * t * (1 - t);
        return Math.Max(0, ropeY - PegRopeOverlap);
    }

    private static Brush RopeTwistBrush()
    {
        var drawing = new DrawingGroup();
        var light = new Pen(new SolidColorBrush(Color.FromArgb(220, 0xDD, 0xC2, 0x9E)), 0.8)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        var shade = new Pen(new SolidColorBrush(Color.FromArgb(170, 0x6F, 0x50, 0x37)), 0.55)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        drawing.Children.Add(new GeometryDrawing(null, light,
            Geometry.Parse("M 0,2.4 L 4,0 L 8,2.4 L 12,0")));
        drawing.Children.Add(new GeometryDrawing(null, shade,
            Geometry.Parse("M 0,0 L 4,2.4 L 8,0 L 12,2.4")));
        return new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 8, 2.4),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 8, 2.4),
            Stretch = Stretch.Fill
        };
    }

    private static Path RopeStroke(Brush stroke, double thickness) => new()
    {
        Data = Geometry.Parse("M 0,0 Q 500,52 1000,0"),
        Stretch = Stretch.Fill,
        Height = RopeSag,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(RopeSideInset, RopeTopInset, RopeSideInset, 0),
        Stroke = stroke,
        StrokeThickness = thickness,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round
    };

    internal static FrameworkElement Peg(string appearance, int colorIndex = 0)
    {
        var peg = new Grid
        {
            Width = 30,
            Height = 40,
            Margin = new Thickness(0, 2, 0, 0),
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = true
        };

        var index = Mod(colorIndex, MacaronColors.Length);
        var plastic = appearance == Classic;
        var edge = plastic ? MacaronEdges[index] : Color.FromRgb(0x94, 0x68, 0x42);
        var baseColor = plastic ? MacaronColors[index] : Color.FromRgb(0xD6, 0xAA, 0x76);
        var fill = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            GradientStops = new GradientStopCollection
            {
                new(Mix(baseColor, Colors.White, 0.35), 0),
                new(Mix(baseColor, Colors.White, 0.12), 0.32),
                new(baseColor, 0.72),
                new(Mix(baseColor, edge, 0.28), 1)
            }
        };

        // 固定旋转中心，调整悬挂位置时保持夹子的轮廓与夹入深度。
        var body = new Canvas
        {
            Width = 30,
            Height = 40,
            IsHitTestVisible = false,
            RenderTransform = new RotateTransform(12, 15, 14),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(0x49, 0x35, 0x28),
                BlurRadius = 2.5,
                ShadowDepth = 1.2,
                Direction = 315,
                Opacity = 0.22
            }
        };
        peg.Children.Add(body);

        // 后夹臂只露出窄边，前夹臂保持完整宽面，表现参考图中的侧向视角。
        body.Children.Add(PegPath(
            "M 17,3 L 20.4,3.5 Q 21.5,3.8 21.5,5 L 20.7,19 L 20,24 L 21,36.5 Q 21,38 19.6,38.8 L 17,39 Z",
            new SolidColorBrush(Mix(baseColor, edge, 0.55)), new SolidColorBrush(edge), 0.65));
        body.Children.Add(PegPath(
            "M 17.6,2 L 20.5,3.5 L 20.5,19.5 L 19.5,23 L 20.5,27 L 20.5,36.8 Q 20.5,37.8 19.5,38.2 L 17.2,39 L 16.8,26 L 16.8,19 Z",
            new SolidColorBrush(Mix(baseColor, edge, 0.3)), new SolidColorBrush(edge), 0.65));
        body.Children.Add(PegPath(
            "M 10.2,2 L 17.2,2 Q 18.2,2 18.2,3.2 L 18.2,19 Q 18.2,20.5 17.6,22 L 17.6,24.5 Q 18.2,26 18.2,27.5 L 18.2,37.5 Q 18.2,39 16.8,39 L 10.2,39 Q 9,39 9,37.5 L 9,27.5 Q 9,26 9.6,24.5 L 9.6,22 Q 9,20.5 9,19 L 9,3.2 Q 9,2 10.2,2 Z",
            fill, new SolidColorBrush(edge), 0.75));

        var highlight = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255));
        body.Children.Add(PegPath("M 10.5,18.5 L 10.5,4 Q 10.5,3.5 11.2,3.5 L 16.6,3.5 M 10.5,30 L 10.5,36.8 Q 10.5,37.5 11.2,37.5 L 16.6,37.5", null, highlight, 0.7));
        body.Children.Add(PegPath("M 19.3,6 L 19.3,18 M 19.4,30 L 19.4,35.8", null,
            new SolidColorBrush(Mix(edge, baseColor, 0.25)), 0.55));
        if (plastic) AddPlasticDetails(body, edge);
        else AddWoodGrain(body, edge);
        AddMetalSpring(body);
        return peg;
    }

    private static Color Mix(Color from, Color to, double amount) => Color.FromRgb(
        (byte)(from.R + (to.R - from.R) * amount),
        (byte)(from.G + (to.G - from.G) * amount),
        (byte)(from.B + (to.B - from.B) * amount));

    private static Path PegPath(string geometry, Brush? fill, Brush? stroke, double thickness) => new()
    {
        Data = Geometry.Parse(geometry),
        Fill = fill,
        Stroke = stroke,
        StrokeThickness = thickness,
        StrokeLineJoin = PenLineJoin.Round,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        Stretch = Stretch.None,
        IsHitTestVisible = false
    };

    private static void AddMetalSpring(Canvas body)
    {
        var dark = new SolidColorBrush(Color.FromRgb(0x68, 0x67, 0x60));
        var metal = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops = new GradientStopCollection
            {
                new(Color.FromRgb(0x82, 0x84, 0x7F), 0),
                new(Color.FromRgb(0xF3, 0xF3, 0xEB), 0.3),
                new(Color.FromRgb(0xB9, 0xBB, 0xB2), 0.55),
                new(Color.FromRgb(0x72, 0x74, 0x6F), 1)
            }
        };
        // 弹簧从侧面绕到前夹臂，宽面只露出细金属压线。
        const string wire = "M 9.4,21.2 L 8.7,26.8 Q 8.7,28 9.8,28 L 18.1,28 L 20.5,26.7 L 20,22.5";
        body.Children.Add(PegPath(wire, null, dark, 1.4));
        body.Children.Add(PegPath(wire, null, metal, 0.7));
        for (var turn = 0; turn < 2; turn++)
        {
            var coil = new Ellipse
            {
                Width = 2.4,
                Height = 3.5,
                Fill = metal,
                Stroke = dark,
                StrokeThickness = 0.45
            };
            Canvas.SetLeft(coil, 18.2 + turn * 0.9);
            Canvas.SetTop(coil, 21.2 - turn * 0.3);
            body.Children.Add(coil);
        }
    }

    private static void AddWoodGrain(Canvas body, Color edge)
    {
        var grain = new SolidColorBrush(Color.FromArgb(85, edge.R, edge.G, edge.B));
        body.Children.Add(PegPath(
            "M 12.3,5.5 Q 11.7,11 12.4,18.8 M 15.6,7 Q 16.3,13 15.5,20 M 12.5,30 Q 11.9,33 12.5,36.5 M 15.5,29.5 L 15.8,35.5",
            null, grain, 0.55));
    }

    private static void AddPlasticDetails(Canvas body, Color edge)
    {
        var grooves = new SolidColorBrush(Color.FromArgb(125, edge.R, edge.G, edge.B));
        // 短横纹表现塑料手柄的防滑槽，夹口保持简洁。
        body.Children.Add(PegPath(
            "M 11.6,7 L 16.2,7 M 11.6,9.5 L 16.2,9.5 M 11.6,12 L 16.2,12",
            null, grooves, 0.7));
    }

    internal static Brush CardSurface(string appearance) => appearance == Classic
        ? Brushes.White
        : new SolidColorBrush(Color.FromRgb(0xFF, 0xFC, 0xF7));

    internal static Brush CardOutline(string appearance) => appearance == Classic
        ? new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB))
        : new SolidColorBrush(Color.FromRgb(0xDE, 0xD3, 0xC5));

    internal static Brush CardHover(string appearance, int colorIndex = 0) => appearance == Classic
        ? new SolidColorBrush(MacaronEdges[Mod(colorIndex, MacaronEdges.Length)])
        : new SolidColorBrush(Color.FromRgb(0xB8, 0x83, 0x5B));

    internal static Brush PreviewSurface(string appearance) => appearance == Classic
        ? new SolidColorBrush(Color.FromRgb(0xF5, 0xF6, 0xFA))
        : new SolidColorBrush(Color.FromRgb(0xF7, 0xF0, 0xE7));

    private static int Mod(int value, int length) => (value % length + length) % length;
}
