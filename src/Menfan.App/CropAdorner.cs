using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Menfan.App;

public sealed class CropAdorner : FrameworkElement
{
    public double SourceAspect { get; set; } = 16d / 9;
    public double LeftCrop { get; set; }
    public double TopCrop { get; set; }
    public double RightCrop { get; set; }
    public double BottomCrop { get; set; }
    public event Action? BeginEdit;
    public event Action<double, double, double, double>? Changed;
    private string mode = "";
    private Point down;
    private double left, top, right, bottom;
    private Rect SourceRect()
    {
        double w = Math.Min(ActualWidth, ActualHeight * SourceAspect), h = w / SourceAspect;
        return new Rect((ActualWidth - w) / 2, (ActualHeight - h) / 2, w, h);
    }
    private Rect Selection(Rect source) => new(source.X + source.Width * LeftCrop, source.Y + source.Height * TopCrop,
        source.Width * (1 - LeftCrop - RightCrop), source.Height * (1 - TopCrop - BottomCrop));
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var source = SourceRect(); if (source.Width < 1 || source.Height < 1) return;
        var selection = Selection(source);
        var shade = new SolidColorBrush(Color.FromArgb(140, 20, 28, 23));
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        dc.DrawRectangle(shade, null, new Rect(source.Left, source.Top, source.Width, Math.Max(0, selection.Top - source.Top)));
        dc.DrawRectangle(shade, null, new Rect(source.Left, selection.Bottom, source.Width, Math.Max(0, source.Bottom - selection.Bottom)));
        dc.DrawRectangle(shade, null, new Rect(source.Left, selection.Top, Math.Max(0, selection.Left - source.Left), selection.Height));
        dc.DrawRectangle(shade, null, new Rect(selection.Right, selection.Top, Math.Max(0, source.Right - selection.Right), selection.Height));
        dc.DrawRectangle(null, new Pen(Brushes.White, 1.5), selection);
        foreach (var point in new[] { selection.TopLeft, selection.TopRight, selection.BottomLeft, selection.BottomRight,
            new Point(selection.Left + selection.Width / 2, selection.Top), new Point(selection.Left + selection.Width / 2, selection.Bottom),
            new Point(selection.Left, selection.Top + selection.Height / 2), new Point(selection.Right, selection.Top + selection.Height / 2) })
            dc.DrawRoundedRectangle(Brushes.White, new Pen(Brushes.Gray, 1), new Rect(point.X - 4, point.Y - 4, 8, 8), 2, 2);
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var source = SourceRect(); var selection = Selection(source); down = e.GetPosition(this); mode = "";
        if (!source.Contains(down)) return;
        if (Math.Abs(down.X - selection.Left) < 12) mode += "L";
        else if (Math.Abs(down.X - selection.Right) < 12) mode += "R";
        if (Math.Abs(down.Y - selection.Top) < 12) mode += "T";
        else if (Math.Abs(down.Y - selection.Bottom) < 12) mode += "B";
        if (mode == "" && selection.Contains(down)) mode = "Move";
        if (mode == "") return;
        left = LeftCrop; top = TopCrop; right = RightCrop; bottom = BottomCrop; CaptureMouse(); BeginEdit?.Invoke(); e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        var source = SourceRect(); if (source.Width <= 0 || source.Height <= 0) return;
        if (!IsMouseCaptured) { Cursor = Cursors.Cross; return; }
        var point = e.GetPosition(this); double dx = (point.X - down.X) / source.Width, dy = (point.Y - down.Y) / source.Height;
        if (mode == "Move")
        {
            dx = Math.Clamp(dx, -left, right); dy = Math.Clamp(dy, -top, bottom);
            LeftCrop = left + dx; RightCrop = right - dx; TopCrop = top + dy; BottomCrop = bottom - dy;
        }
        else
        {
            if (mode.Contains('L')) LeftCrop = Math.Clamp(left + dx, 0, Math.Min(.9, .94 - right));
            if (mode.Contains('R')) RightCrop = Math.Clamp(right - dx, 0, Math.Min(.9, .94 - left));
            if (mode.Contains('T')) TopCrop = Math.Clamp(top + dy, 0, Math.Min(.9, .94 - bottom));
            if (mode.Contains('B')) BottomCrop = Math.Clamp(bottom - dy, 0, Math.Min(.9, .94 - top));
        }
        Changed?.Invoke(LeftCrop, TopCrop, RightCrop, BottomCrop); InvalidateVisual(); e.Handled = true;
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { if (IsMouseCaptured) { ReleaseMouseCapture(); mode = ""; e.Handled = true; } }
}
