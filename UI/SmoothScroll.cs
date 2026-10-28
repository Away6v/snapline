using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SnapLine;

internal sealed class SmoothScroll
{
    private readonly ScrollViewer viewer;
    private readonly bool horizontal;
    private readonly Stopwatch clock = new();
    private double target;
    private double position;
    private bool running;

    internal SmoothScroll(ScrollViewer viewer, bool horizontal)
    {
        this.viewer = viewer;
        this.horizontal = horizontal;
        viewer.PreviewMouseWheel += Wheel;
        viewer.Unloaded += (_, _) => Stop();
        viewer.IsVisibleChanged += (_, _) => { if (!viewer.IsVisible) Stop(); };
    }

    private double Offset => horizontal ? viewer.HorizontalOffset : viewer.VerticalOffset;
    private double Maximum => horizontal ? viewer.ScrollableWidth : viewer.ScrollableHeight;

    private void Wheel(object sender, MouseWheelEventArgs e)
    {
        if (Maximum <= 0) return;
        if (!running) target = position = Offset;
        target = Math.Clamp(target - e.Delta * (horizontal ? 1.25 : 0.7), 0, Maximum);
        e.Handled = true;
        if (!SystemParameters.ClientAreaAnimation) { Apply(target); return; }
        if (running) return;
        running = true;
        clock.Restart();
        CompositionTarget.Rendering += Render;
    }

    private void Render(object? sender, EventArgs e)
    {
        // 按真实帧间隔收敛，连续滚轮输入只改变目标，不重新开始动画。
        var elapsed = clock.Elapsed.TotalSeconds;
        clock.Restart();
        target = Math.Clamp(target, 0, Maximum);
        position += (target - position) * (1 - Math.Exp(-18 * elapsed));
        if (Math.Abs(target - position) < 0.25)
        {
            Apply(target);
            Stop();
            return;
        }
        Apply(position);
    }

    private void Apply(double value)
    {
        if (horizontal) viewer.ScrollToHorizontalOffset(value);
        else viewer.ScrollToVerticalOffset(value);
    }

    internal void Stop()
    {
        if (!running) return;
        CompositionTarget.Rendering -= Render;
        running = false;
        clock.Stop();
    }
}
