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
    internal const string Forest = "forest";
    internal const string Graphite = "graphite";
    internal const string Denim = "denim";
    internal const string Sakura = "sakura";
    internal const string Silver = "silver";
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
        Color.FromRgb(0xF1, 0xC9, 0xA5),
        Color.FromRgb(0xAE, 0xD2, 0xEB),
        Color.FromRgb(0xF1, 0xDE, 0x9F),
        Color.FromRgb(0xC5, 0xDB, 0xAF),
        Color.FromRgb(0xDC, 0xBE, 0xD4)
    };

    private static readonly Color[] MacaronEdges =
    {
        Color.FromRgb(0xB9, 0x78, 0x8A),
        Color.FromRgb(0x6F, 0xA9, 0x98),
        Color.FromRgb(0x8E, 0x7F, 0xB6),
        Color.FromRgb(0xB4, 0x8A, 0x68),
        Color.FromRgb(0x72, 0xA2, 0xC2),
        Color.FromRgb(0xB4, 0x9A, 0x5D),
        Color.FromRgb(0x89, 0xA6, 0x76),
        Color.FromRgb(0xAB, 0x83, 0xA0)
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
        if (appearance == Graphite)
        {
            // 浅色外缘让石墨绳在深色桌面上仍有轮廓，细纹保持克制。
            layer.Children.Add(RopeStroke(new SolidColorBrush(Color.FromArgb(120, 0xB2, 0xB9, 0xC0)), 3.5));
            layer.Children.Add(RopeStroke(new SolidColorBrush(Color.FromRgb(0x35, 0x3A, 0x40)), 2.5));
            layer.Children.Add(RopeStroke(GraphiteRopeTexture(), 1.8));
            return layer;
        }
        if (appearance != Hemp)
        {
            var (shade, fiber, light, crossing) = appearance switch
            {
                Denim => (Color.FromRgb(0x24, 0x3C, 0x55), Color.FromRgb(0x3D, 0x5D, 0x79), Color.FromRgb(0xA4, 0xB8, 0xC4), true),
                Sakura => (Color.FromRgb(0xB2, 0x9E, 0x9D), Color.FromRgb(0xE2, 0xD6, 0xCC), Color.FromRgb(0xFF, 0xF6, 0xEE), false),
                Silver => (Color.FromRgb(0x7B, 0x7E, 0x82), Color.FromRgb(0xBC, 0xBF, 0xC2), Color.FromRgb(0xF3, 0xF4, 0xF5), true),
                Forest => (Color.FromRgb(0x79, 0x7D, 0x64), Color.FromRgb(0xB4, 0xB1, 0x8F), Color.FromRgb(0xDA, 0xD5, 0xB5), false),
                _ => (Color.FromRgb(0x9D, 0xA3, 0xAF), Color.FromRgb(0xD2, 0xD4, 0xDB), Color.FromRgb(0xF6, 0xF3, 0xF4), true)
            };
            // 深色细边和浅色纤维分别保证绳子在明暗背景上可辨认。
            var ropeEdge = appearance == Denim ? Color.FromArgb(130, 0xB0, 0xBE, 0xC8) : Color.FromArgb(120, shade.R, shade.G, shade.B);
            layer.Children.Add(RopeStroke(new SolidColorBrush(ropeEdge), 3.4));
            layer.Children.Add(RopeStroke(new SolidColorBrush(fiber), 2.45));
            layer.Children.Add(RopeStroke(FiberRopeTexture(shade, light, crossing), 2.15));
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

    private static Brush FiberRopeTexture(Color shade, Color light, bool crossing)
    {
        var drawing = new DrawingGroup();
        // 交错短线表现细编织，单向斜线表现天然纤维的捻向。
        drawing.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(Color.FromArgb(145, shade.R, shade.G, shade.B)), 0.55),
            Geometry.Parse("M -1,2.3 L 3,-0.3 M 3,2.3 L 7,-0.3 M 7,2.3 L 11,-0.3")));
        drawing.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(Color.FromArgb(190, light.R, light.G, light.B)), 0.6),
            Geometry.Parse(crossing ? "M -1,-0.3 L 3,2.3 M 3,-0.3 L 7,2.3 M 7,-0.3 L 11,2.3" : "M 0,2.3 L 4,-0.3 M 4,2.3 L 8,-0.3")));
        return new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 8, 2),
            ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 8, 2), Stretch = Stretch.Fill
        };
    }

    private static Brush GraphiteRopeTexture()
    {
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(Color.FromArgb(115, 0xAC, 0xB5, 0xBC)), 0.55),
            Geometry.Parse("M 0,1.7 L 3,0.3 M 4,1.7 L 7,0.3")));
        drawing.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(Color.FromArgb(100, 0x15, 0x19, 0x1D)), 0.45),
            Geometry.Parse("M 0,0.3 L 3,1.7 M 4,0.3 L 7,1.7")));
        return new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 8, 2),
            ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 8, 2), Stretch = Stretch.Fill
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
        var graphite = appearance == Graphite;
        var metallic = appearance is Denim or Silver;
        var translucent = appearance is Sakura;
        var softHighlight = appearance is Hemp or Forest;
        var (baseColor, edge) = appearance switch
        {
            Forest => index % 2 == 0
                ? (Color.FromRgb(0x9F, 0xB7, 0x96), Color.FromRgb(0x65, 0x80, 0x60))
                : (Color.FromRgb(0xD0, 0xC8, 0xA3), Color.FromRgb(0x9D, 0x94, 0x72)),
            Denim => (Color.FromRgb(0x9C, 0xA9, 0xB0), Color.FromRgb(0x5A, 0x6C, 0x77)),
            Sakura => (Color.FromRgb(0xE9, 0xB6, 0xC2), Color.FromRgb(0xB8, 0x7E, 0x90)),
            Silver => (Color.FromRgb(0xC2, 0xC5, 0xC8), Color.FromRgb(0x7E, 0x82, 0x87)),
            Classic => (MacaronColors[index], MacaronEdges[index]),
            Graphite => (Color.FromRgb(0x45, 0x4B, 0x51), Color.FromRgb(0x23, 0x27, 0x2B)),
            _ => (Color.FromRgb(0xDF, 0xBD, 0x91), Color.FromRgb(0x9C, 0x78, 0x52))
        };
        var fill = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            GradientStops = new GradientStopCollection
            {
                new(Mix(baseColor, Colors.White, graphite ? 0.24 : softHighlight ? 0.22 : 0.35), 0),
                new(Mix(baseColor, Colors.White, graphite ? 0.08 : softHighlight ? 0.07 : 0.12), 0.32),
                new(baseColor, 0.72),
                new(Mix(baseColor, edge, 0.28), 1)
            }
        };

        if (metallic)
        {
            // 金属表面使用窄高光带表现拉丝反射。
            fill.GradientStops.Clear();
            fill.GradientStops.Add(new GradientStop(Mix(baseColor, edge, 0.45), 0));
            fill.GradientStops.Add(new GradientStop(Mix(baseColor, Colors.White, 0.32), 0.23));
            fill.GradientStops.Add(new GradientStop(baseColor, 0.5));
            fill.GradientStops.Add(new GradientStop(Mix(baseColor, Colors.White, 0.16), 0.7));
            fill.GradientStops.Add(new GradientStop(Mix(baseColor, edge, 0.42), 1));
        }
        else if (translucent)
        {
            // 仅夹臂正面的填充透光，保留不透明边缘和金属弹簧。
            foreach (var stop in fill.GradientStops)
                stop.Color = Color.FromArgb(205, stop.Color.R, stop.Color.G, stop.Color.B);
        }

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

        var highlight = graphite
            ? new SolidColorBrush(Color.FromArgb(100, 0xD5, 0xDE, 0xE2))
            : new SolidColorBrush(Color.FromArgb(softHighlight ? (byte)95 : (byte)160, 255, 255, 255));
        body.Children.Add(PegPath("M 10.5,18.5 L 10.5,4 Q 10.5,3.5 11.2,3.5 L 16.6,3.5 M 10.5,30 L 10.5,36.8 Q 10.5,37.5 11.2,37.5 L 16.6,37.5", null, highlight, 0.7));
        body.Children.Add(PegPath("M 19.3,6 L 19.3,18 M 19.4,30 L 19.4,35.8", null,
            new SolidColorBrush(Mix(edge, baseColor, 0.25)), 0.55));
        if (graphite) AddGraphiteDetails(body);
        else if (metallic) AddMetalDetails(body, appearance, edge);
        else if (translucent) AddResinDetails(body, edge);
        else if (appearance == Forest) AddForestDetails(body, edge);
        else if (appearance == Classic) AddPlasticDetails(body, edge);
        else AddWoodGrain(body, edge);
        AddMetalSpring(body, graphite, appearance == Denim);
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

    private static void AddMetalSpring(Canvas body, bool graphite, bool cool)
    {
        var dark = new SolidColorBrush(graphite ? Color.FromRgb(0x24, 0x2A, 0x2E) : Color.FromRgb(0x68, 0x67, 0x60));
        var metal = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops = new GradientStopCollection
            {
                new(graphite ? Color.FromRgb(0x6F, 0x7A, 0x81) : cool ? Color.FromRgb(0x7A, 0x90, 0x98) : Color.FromRgb(0x82, 0x84, 0x7F), 0),
                new(graphite ? Color.FromRgb(0xD0, 0xDA, 0xDE) : cool ? Color.FromRgb(0xEB, 0xF4, 0xF5) : Color.FromRgb(0xF3, 0xF3, 0xEB), 0.3),
                new(graphite ? Color.FromRgb(0x95, 0xA2, 0xA8) : cool ? Color.FromRgb(0xAE, 0xC3, 0xC8) : Color.FromRgb(0xB9, 0xBB, 0xB2), 0.55),
                new(graphite ? Color.FromRgb(0x55, 0x5F, 0x65) : cool ? Color.FromRgb(0x66, 0x7E, 0x86) : Color.FromRgb(0x72, 0x74, 0x6F), 1)
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

    private static void AddMetalDetails(Canvas body, string appearance, Color edge)
    {
        // 银色夹臂保留细拉丝，牛仔主题额外加入少量磨损。
        var lines = new SolidColorBrush(Color.FromArgb(80, 0xFF, 0xFF, 0xFF));
        body.Children.Add(PegPath("M 11.5,5 L 11.5,19 M 13.3,4.5 L 13.3,18.8 M 15.1,5 L 15.1,19 M 16.5,5.5 L 16.5,18 M 11.7,30 L 11.7,36 M 13.8,30 L 13.8,37 M 15.8,30 L 15.8,36", null, lines, 0.4));
        if (appearance == Denim)
        {
            // 不规则浅划痕和暗纹表现牛仔主题的旧银金属。
            body.Children.Add(PegPath("M 11.4,8.4 L 13,7.9 M 14.8,14.5 L 16.4,13.9 M 11.5,18 L 12.8,17.6 M 12,34 L 13.5,33.5 M 15,36 L 16.2,35.6", null,
                new SolidColorBrush(Color.FromArgb(110, edge.R, edge.G, edge.B)), 0.55));
            body.Children.Add(PegPath("M 13,9.3 L 14.3,8.9 M 12,15.8 L 13.5,15.3 M 14.6,33.4 L 16,32.9", null,
                new SolidColorBrush(Color.FromArgb(100, 0xE5, 0xEC, 0xEC)), 0.45));
        }
    }

    private static void AddResinDetails(Canvas body, Color edge)
    {
        // 半透明夹的边缘、内部反射和微弱成型线共同形成树脂厚度。
        body.Children.Add(PegPath("M 12,4.5 L 12,17.8 M 12,30 L 12,36 M 13,5 Q 15,4.5 16.2,5 M 13,35 Q 14.8,36 16.2,35.5", null,
            new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), 0.65));
        body.Children.Add(PegPath("M 16.5,6 L 16.5,18 M 16.5,30 L 16.5,35.5", null,
            new SolidColorBrush(Color.FromArgb(80, edge.R, edge.G, edge.B)), 0.5));
    }

    private static void AddForestDetails(Canvas body, Color edge)
    {
        // 淡漆下保留细木纹，避免木纹盖过夹子的颜色。
        var grain = new SolidColorBrush(Color.FromArgb(75, edge.R, edge.G, edge.B));
        body.Children.Add(PegPath("M 12,5 Q 11.2,11 12.2,19 M 14.8,4.8 Q 15.6,11 14.9,19 M 12.4,30 Q 11.8,33 12.4,36.5 M 15.4,30 Q 15.9,33 15.4,36.5", null, grain, 0.5));
    }

    private static void AddGraphiteDetails(Canvas body)
    {
        // 低对比度细线表现磨砂金属的拉丝，不改变夹子形状。
        var grain = new SolidColorBrush(Color.FromArgb(45, 0xD4, 0xDE, 0xE2));
        body.Children.Add(PegPath("M 11.6,5 L 11.6,18 M 13.3,4.5 L 13.3,19 M 15.1,5 L 15.1,18.5 M 16.5,6 L 16.5,18 M 11.6,30 L 11.6,36 M 13.3,29.5 L 13.3,37 M 15.1,30 L 15.1,36.5", null, grain, 0.4));
    }

    private static void AddWoodGrain(Canvas body, Color edge)
    {
        // 深浅交错的细木纹保留木质层次，避免表面显得像塑料。
        var grain = new SolidColorBrush(Color.FromArgb(100, edge.R, edge.G, edge.B));
        var paleGrain = new SolidColorBrush(Color.FromArgb(75, 255, 245, 222));
        body.Children.Add(PegPath(
            "M 11.7,4.6 Q 11.3,10 11.8,16.7 M 13.4,5.2 Q 12.8,11 13.5,19 M 15.6,5.5 Q 16.2,12 15.6,19.5 M 11.8,30 Q 11.4,33.5 11.9,36.8 M 14,29.6 Q 14.5,33 14,37 M 16,30 L 16.2,36.5",
            null, grain, 0.48));
        body.Children.Add(PegPath("M 12.5,5.5 Q 12.1,11 12.7,17.5 M 14.6,6 Q 15,12 14.5,18.8 M 12.8,30.5 L 12.8,36.5 M 15,30.5 Q 15.3,33.5 15,36.7", null, paleGrain, 0.4));
    }

    private static void AddPlasticDetails(Canvas body, Color edge)
    {
        var grooves = new SolidColorBrush(Color.FromArgb(125, edge.R, edge.G, edge.B));
        // 成型边线和短横纹表现柔和彩色塑料。
        body.Children.Add(PegPath(
            "M 11.6,7 L 16.2,7 M 11.6,9.5 L 16.2,9.5 M 11.6,12 L 16.2,12 M 16.8,5 L 16.8,18",
            null, grooves, 0.7));
        body.Children.Add(PegPath("M 11.3,7.8 L 16,7.8 M 11.3,10.3 L 16,10.3 M 11.3,12.8 L 16,12.8", null,
            new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), 0.45));
    }

    internal static Brush CardSurface(string appearance) => new SolidColorBrush(appearance switch
    {
        Denim => Color.FromRgb(0xF8, 0xFB, 0xFD),
        Sakura => Color.FromRgb(0xFF, 0xFA, 0xFC),
        Silver => Color.FromRgb(0xFC, 0xFC, 0xFC),
        Classic => Colors.White,
        Graphite => Color.FromRgb(0x2D, 0x33, 0x39),
        Forest => Color.FromRgb(0xFA, 0xFC, 0xF6),
        _ => Color.FromRgb(0xFF, 0xFC, 0xF7)
    });

    internal static Brush CardOutline(string appearance) => new SolidColorBrush(appearance switch
    {
        Denim => Color.FromRgb(0xD1, 0xDD, 0xE5),
        Sakura => Color.FromRgb(0xEB, 0xD8, 0xDF),
        Silver => Color.FromRgb(0xDD, 0xDF, 0xE1),
        Classic => Color.FromRgb(0xE5, 0xE7, 0xEB),
        Graphite => Color.FromRgb(0x55, 0x60, 0x68),
        Forest => Color.FromRgb(0xD4, 0xDF, 0xD0),
        _ => Color.FromRgb(0xDE, 0xD3, 0xC5)
    });

    internal static Brush CardHover(string appearance, int colorIndex = 0) => new SolidColorBrush(appearance switch
    {
        Denim => Color.FromRgb(0x64, 0x87, 0xA3),
        Sakura => Color.FromRgb(0xBF, 0x84, 0x96),
        Silver => Color.FromRgb(0x91, 0x95, 0x99),
        Classic => MacaronEdges[Mod(colorIndex, MacaronEdges.Length)],
        Graphite => Color.FromRgb(0xA5, 0xB6, 0xC0),
        Forest => Color.FromRgb(0x77, 0x98, 0x71),
        _ => Color.FromRgb(0xB8, 0x83, 0x5B)
    });

    internal static Brush PreviewSurface(string appearance) => new SolidColorBrush(appearance switch
    {
        Denim => Color.FromRgb(0xE2, 0xED, 0xF3),
        Sakura => Color.FromRgb(0xF9, 0xE9, 0xEF),
        Silver => Color.FromRgb(0xED, 0xEE, 0xEF),
        Classic => Color.FromRgb(0xF5, 0xF6, 0xFA),
        Graphite => Color.FromRgb(0x1E, 0x23, 0x28),
        Forest => Color.FromRgb(0xE9, 0xF0, 0xE5),
        _ => Color.FromRgb(0xF7, 0xF0, 0xE7)
    });

    internal static Brush CardText(string appearance) => appearance == Graphite
        ? new SolidColorBrush(Color.FromRgb(0xE8, 0xED, 0xEF))
        : new SolidColorBrush(Color.FromRgb(0x39, 0x35, 0x32));

    private static int Mod(int value, int length) => (value % length + length) % length;
}
