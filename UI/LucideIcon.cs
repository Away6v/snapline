using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace SnapLine;

// 图标以 Lucide 为基础，虚线图标调整自实线图标，坐标使用 24 × 24 网格。
internal sealed class LucideIcon : FrameworkElement
{
    private static readonly Dictionary<string, Geometry[]> Icons = new()
    {
        ["square"] = new Geometry[] { new RectangleGeometry(new Rect(3, 3, 18, 18), 2, 2) },
        ["rectangle-horizontal"] = new Geometry[] { new RectangleGeometry(new Rect(2, 6, 20, 12), 2, 2) },
        ["circle"] = new Geometry[] { new EllipseGeometry(new Point(12, 12), 10, 10) },
        ["arrow-big-right"] = new Geometry[] { Geometry.Parse("M13.207 19.793a.707.707 0 0 1-1.207-.5V16a1 1 0 0 0-1-1H5a1 1 0 0 1-1-1v-4a1 1 0 0 1 1-1h6a1 1 0 0 0 1-1V4.707a.707.707 0 0 1 1.207-.5l6.94 6.94a1.207 1.207 0 0 1 0 1.707z") },
        ["paint-bucket"] = new Geometry[] { Geometry.Parse("M11 7 6 2"), Geometry.Parse("M18.992 12H2.041"), Geometry.Parse("M21.145 18.38A3.34 3.34 0 0 1 20 16.5a3.3 3.3 0 0 1-1.145 1.88c-.575.46-.855 1.02-.855 1.595A2 2 0 0 0 20 22a2 2 0 0 0 2-2.025c0-.58-.285-1.13-.855-1.595"), Geometry.Parse("m8.5 4.5 2.148-2.148a1.205 1.205 0 0 1 1.704 0l7.296 7.296a1.205 1.205 0 0 1 0 1.704l-7.592 7.592a3.615 3.615 0 0 1-5.112 0l-3.888-3.888a3.615 3.615 0 0 1 0-5.112L5.67 7.33") },
        ["pen-tool"] = new Geometry[] { Geometry.Parse("m18 13-.654-4.736a4 4 0 0 0-2.756-3.266l-9.163-2.9a2 2 0 0 0-2.017.493l-.82.819a2 2 0 0 0-.492 2.017l2.9 9.163a4 4 0 0 0 3.266 2.756L13 18"), Geometry.Parse("M18.648 12.352a1.205 1.205 0 0 1 1.704 0l1.296 1.296a1.205 1.205 0 0 1 0 1.704l-6.296 6.296a1.205 1.205 0 0 1-1.704 0l-1.296-1.296a1.205 1.205 0 0 1 0-1.704z"), Geometry.Parse("m3 3 6.586 6.586"), new EllipseGeometry(new Point(11, 11), 2, 2) },
        ["line-dashed"] = new Geometry[] { Geometry.Parse("M3 12h4"), Geometry.Parse("M10 12h4"), Geometry.Parse("M17 12h4") },
        ["arrow-right"] = new Geometry[] { Geometry.Parse("M5 12h14"), Geometry.Parse("m12 5 7 7-7 7") },
        ["arrow-up-right"] = new Geometry[] { Geometry.Parse("M7 7h10v10"), Geometry.Parse("M7 17 17 7") },
        ["pencil"] = new Geometry[] { Geometry.Parse("M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.497z"), Geometry.Parse("m15 5 4 4") },
        ["mouse-pointer-2"] = new Geometry[] { Geometry.Parse("M4.037 4.688a.495.495 0 0 1 .651-.651l16 6.5a.5.5 0 0 1-.063.947l-6.124 1.58a2 2 0 0 0-1.438 1.435l-1.579 6.126a.5.5 0 0 1-.947.063z") },
        ["type"] = new Geometry[] { Geometry.Parse("M12 4v16"), Geometry.Parse("M4 7V5a1 1 0 0 1 1-1h14a1 1 0 0 1 1 1v2"), Geometry.Parse("M9 20h6") },
        ["rectangle-select"] = new Geometry[] { Geometry.Parse("M4 8V4h4"), Geometry.Parse("M11 4h3"), Geometry.Parse("M17 4h3v4"), Geometry.Parse("M20 11v3"), Geometry.Parse("M20 17v3h-4"), Geometry.Parse("M13 20h-3"), Geometry.Parse("M7 20H4v-4"), Geometry.Parse("M4 13v-2") },
        ["number-reset"] = new Geometry[] { Geometry.Parse("M4 10a8 8 0 1 1 1.5 7"), Geometry.Parse("M4 4v6h6"), Geometry.Parse("M11 11l2-2v7"), Geometry.Parse("M11 16h4") },
        ["mosaic"] = new Geometry[] { Geometry.Parse("M3 3h7v7H3z"), Geometry.Parse("M14 3h3v3h-3z"), Geometry.Parse("M19 8h2v2h-2z"), Geometry.Parse("M3 14h3v3H3z"), Geometry.Parse("M8 19h2v2H8z"), Geometry.Parse("M14 14h7v7h-7z") },
        ["number-marker"] = new Geometry[] { new EllipseGeometry(new Point(12, 12), 9, 9), Geometry.Parse("M10 9l2-2v10"), Geometry.Parse("M10 17h4") },
        ["grid-3x3"] = new Geometry[] { Geometry.Parse("M3 3h5v5H3z"), Geometry.Parse("M10 3h4v5h-4z"), Geometry.Parse("M16 3h5v5h-5z"), Geometry.Parse("M3 10h5v4H3z"), Geometry.Parse("M10 10h4v4h-4z"), Geometry.Parse("M16 10h5v4h-5z"), Geometry.Parse("M3 16h5v5H3z"), Geometry.Parse("M10 16h4v5h-4z"), Geometry.Parse("M16 16h5v5h-5z") },
        ["circle-dot"] = new Geometry[] { new EllipseGeometry(new Point(12, 12), 9, 9), new EllipseGeometry(new Point(12, 12), 2, 2) },
        ["crop"] = new Geometry[] { Geometry.Parse("M6 2v14a2 2 0 0 0 2 2h14"), Geometry.Parse("M18 22V8a2 2 0 0 0-2-2H2") },
        ["chevron-down"] = new Geometry[] { Geometry.Parse("m6 9 6 6 6-6") },
        ["check"] = new Geometry[] { Geometry.Parse("M20 6 9 17l-5-5") },
        ["x"] = new Geometry[] { Geometry.Parse("M18 6 6 18"), Geometry.Parse("m6 6 12 12") },
        ["undo-2"] = new Geometry[] { Geometry.Parse("M9 14 4 9l5-5"), Geometry.Parse("M4 9h10.5a5.5 5.5 0 0 1 5.5 5.5a5.5 5.5 0 0 1-5.5 5.5H11") },
        ["redo-2"] = new Geometry[] { Geometry.Parse("m15 14 5-5-5-5"), Geometry.Parse("M20 9H9.5A5.5 5.5 0 0 0 4 14.5A5.5 5.5 0 0 0 9.5 20H13") },
        ["save"] = new Geometry[] { Geometry.Parse("M15.2 3a2 2 0 0 1 1.4.6l3.8 3.8a2 2 0 0 1 .6 1.4V19a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z"), Geometry.Parse("M17 21v-7a1 1 0 0 0-1-1H8a1 1 0 0 0-1 1v7"), Geometry.Parse("M7 3v4a1 1 0 0 0 1 1h7") },
        ["file-plus"] = new Geometry[] { Geometry.Parse("M6 22a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8a2.4 2.4 0 0 1 1.704.706l3.588 3.588A2.4 2.4 0 0 1 20 8v12a2 2 0 0 1-2 2z"), Geometry.Parse("M14 2v5a1 1 0 0 0 1 1h5"), Geometry.Parse("M9 15h6"), Geometry.Parse("M12 18v-6") },
        ["scan"] = new Geometry[] { Geometry.Parse("M3 7V5a2 2 0 0 1 2-2h2"), Geometry.Parse("M17 3h2a2 2 0 0 1 2 2v2"), Geometry.Parse("M21 17v2a2 2 0 0 1-2 2h-2"), Geometry.Parse("M7 21H5a2 2 0 0 1-2-2v-2") },
        ["zoom-out"] = new Geometry[] { new EllipseGeometry(new Point(11, 11), 8, 8), new LineGeometry(new Point(21, 21), new Point(16.65, 16.65)), new LineGeometry(new Point(8, 11), new Point(14, 11)) },
        ["zoom-in"] = new Geometry[] { new EllipseGeometry(new Point(11, 11), 8, 8), new LineGeometry(new Point(21, 21), new Point(16.65, 16.65)), new LineGeometry(new Point(11, 8), new Point(11, 14)), new LineGeometry(new Point(8, 11), new Point(14, 11)) },
        ["highlighter"] = new Geometry[] { Geometry.Parse("m9 11-6 6v3h9l3-3"), Geometry.Parse("m22 12-4.6 4.6a2 2 0 0 1-2.8 0l-5.2-5.2a2 2 0 0 1 0-2.8L14 4") },
        ["move-horizontal"] = new Geometry[] { Geometry.Parse("m18 8 4 4-4 4"), Geometry.Parse("M2 12h20"), Geometry.Parse("m6 8-4 4 4 4") },
        ["lasso-select"] = new Geometry[] { Geometry.Parse("M7 22a5 5 0 0 1-2-4"), Geometry.Parse("M7 16.93c.96.43 1.96.74 2.99.91"), Geometry.Parse("M3.34 14A6.8 6.8 0 0 1 2 10c0-4.42 4.48-8 10-8s10 3.58 10 8a7.19 7.19 0 0 1-.33 2"), Geometry.Parse("M5 18a2 2 0 1 0 0-4 2 2 0 0 0 0 4z"), Geometry.Parse("M14.33 22h-.09a.35.35 0 0 1-.24-.32v-10a.34.34 0 0 1 .33-.34c.08 0 .15.03.21.08l7.34 6a.33.33 0 0 1-.21.59h-4.49l-2.57 3.85a.35.35 0 0 1-.28.14z") },
        ["copy"] = new Geometry[] { new RectangleGeometry(new Rect(8, 8, 14, 14), 2, 2), Geometry.Parse("M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2") },
        ["eye"] = new Geometry[] { Geometry.Parse("M2.062 12.348a1 1 0 0 1 0-.696 10.75 10.75 0 0 1 19.876 0 1 1 0 0 1 0 .696 10.75 10.75 0 0 1-19.876 0"), new EllipseGeometry(new Point(12, 12), 3, 3) },
        ["pin"] = new Geometry[] { Geometry.Parse("M12 17v5"), Geometry.Parse("M9 10.76a2 2 0 0 1-1.11 1.79l-1.78.9A2 2 0 0 0 5 15.24V16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-.76a2 2 0 0 0-1.11-1.79l-1.78-.9A2 2 0 0 1 15 10.76V7a1 1 0 0 1 1-1 2 2 0 0 0 0-4H8a2 2 0 0 0 0 4 1 1 0 0 1 1 1z") },
        ["external-link"] = new Geometry[] { Geometry.Parse("M15 3h6v6"), Geometry.Parse("M10 14 21 3"), Geometry.Parse("M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6") },
        ["download"] = new Geometry[] { Geometry.Parse("M12 15V3"), Geometry.Parse("M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"), Geometry.Parse("m7 10 5 5 5-5") },
        ["folder-open"] = new Geometry[] { Geometry.Parse("m6 14 1.5-2.9A2 2 0 0 1 9.24 10H20a2 2 0 0 1 1.94 2.5l-1.54 6a2 2 0 0 1-1.95 1.5H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h3.9a2 2 0 0 1 1.69.9l.81 1.2a2 2 0 0 0 1.67.9H18a2 2 0 0 1 2 2v2") },
        ["trash"] = new Geometry[] { Geometry.Parse("M10 11v6"), Geometry.Parse("M14 11v6"), Geometry.Parse("M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6"), Geometry.Parse("M3 6h18"), Geometry.Parse("M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2") },
        ["arrow-left"] = new Geometry[] { Geometry.Parse("m12 19-7-7 7-7"), Geometry.Parse("M19 12H5") },
        ["log-out"] = new Geometry[] { Geometry.Parse("m16 17 5-5-5-5"), Geometry.Parse("M21 12H9"), Geometry.Parse("M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4") },
        ["inbox"] = new Geometry[] { Geometry.Parse("M 22,12 L 16,12 L 14,15 L 10,15 L 8,12 L 2,12"), Geometry.Parse("M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z") },
        ["folder"] = new Geometry[] { Geometry.Parse("M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z") },
        ["panel-top"] = new Geometry[] { new RectangleGeometry(new Rect(3, 3, 18, 18), 2, 2), Geometry.Parse("M3 9h18") },
        ["panel-top-open"] = new Geometry[] { new RectangleGeometry(new Rect(3, 3, 18, 18), 2, 2), Geometry.Parse("M3 9h18"), Geometry.Parse("m15 14-3 3-3-3") },
        ["panel-bottom-close"] = new Geometry[] { new RectangleGeometry(new Rect(3, 3, 18, 18), 2, 2), Geometry.Parse("M3 15h18"), Geometry.Parse("m15 8-3 3-3-3") },
        ["power-off"] = new Geometry[] { Geometry.Parse("M18.36 6.64A9 9 0 0 1 20.77 15"), Geometry.Parse("M6.16 6.16a9 9 0 1 0 12.68 12.68"), Geometry.Parse("M12 2v4"), Geometry.Parse("m2 2 20 20") },
        ["pen-line"] = new Geometry[] { Geometry.Parse("M13 21h8"), Geometry.Parse("M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.497z") },
        ["italic"] = new Geometry[] { new LineGeometry(new Point(19, 4), new Point(10, 4)), new LineGeometry(new Point(14, 20), new Point(5, 20)), new LineGeometry(new Point(15, 4), new Point(9, 20)) },
        ["plus"] = new Geometry[] { Geometry.Parse("M5 12h14"), Geometry.Parse("M12 5v14") },
        ["minus"] = new Geometry[] { Geometry.Parse("M5 12h14") },
    };
    private string iconName;
    internal static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(LucideIcon), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));
    internal string IconName { get => iconName; set { iconName = value; InvalidateVisual(); } }
    internal Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    internal LucideIcon(string name, Brush color, double size = 20)
    {
        iconName = name; Stroke = color; Width = size; Height = size; IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        if (!Icons.TryGetValue(iconName, out var paths)) return;
        var scale = Math.Min(ActualWidth, ActualHeight) / 24;
        context.PushTransform(new TranslateTransform((ActualWidth - 24 * scale) / 2, (ActualHeight - 24 * scale) / 2));
        context.PushTransform(new ScaleTransform(scale, scale));
        var pen = new Pen(Stroke, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        foreach (var path in paths) context.DrawGeometry(iconName == "arrow-big-right" ? Stroke : null, pen, path);
        context.Pop(); context.Pop();
    }
}
