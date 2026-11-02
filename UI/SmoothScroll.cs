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
        // 内层说明仍有可滚动内容时，优先让鼠标所在区域处理滚轮。
        for (var source = e.OriginalSource as DependencyObject; source != null && source != viewer;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source))
        {
            if (source is not ScrollViewer nested) continue;
            var offset = horizontal ? nested.HorizontalOffset : nested.VerticalOffset;
            var maximum = horizontal ? nested.ScrollableWidth : nested.ScrollableHeight;
            if (e.Delta > 0 ? offset > 0 : offset < maximum)
            {
                Stop();
                return;
            }
        }
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

    internal double SuspendForLayout()
    {
        var remaining = running ? target - Offset : 0;
        Stop();
        return remaining;
    }

    internal void ResumeAfterLayout(double remaining)
    {
        if (Math.Abs(remaining) < 0.25 || Maximum <= 0 || !viewer.IsVisible) return;
        position = Offset;
        target = Math.Clamp(position + remaining, 0, Maximum);
        if (Math.Abs(target - position) < 0.25) return;
        running = true;
        clock.Restart();
        CompositionTarget.Rendering += Render;
    }
}
