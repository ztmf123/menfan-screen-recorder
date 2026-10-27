using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Menfan.Core;

namespace Menfan.App;

public sealed class TimelineControl : FrameworkElement
{
    public Project? Project { get; set; }
    public double PixelsPerSecond { get; set; } = 70;
    public double CurrentTime { get; set; }
    public int SelectedClipIndex { get; set; }
    public SceneEffect? SelectedEffect { get; set; }
    public bool Waveform { get; set; }
    public bool Snap { get; set; } = true;
    public Func<double, BitmapSource?>? ThumbnailAt { get; set; }
    public float[]? Peaks { get; set; }
    public event Action<double>? SeekRequested;
    public event Action? SelectionChanged;
    public event Action? BeginEdit;
    public event Action? Edited;
    public event Action<int, int>? MoveRequested;
    public event Action<int, double, double>? TrimRequested;
    public event Action<int>? RestoreJunction;
    private Point down;
    private bool dragging;
    private int dragClip = -1;
    private string edge = "";
    private SceneEffect? dragEffect;
    private double effectStart, effectEnd;
    private const double Label = 90, MainTop = 34, MainHeight = 65;
    private readonly string[] tracks = ["屏幕录制", "缩放", "屏幕 3D", "摄影机运动", "叠加层"];
    private static readonly DependencyProperty ThemeBrushProperty = DependencyProperty.Register("ThemeBrush", typeof(Brush), typeof(TimelineControl), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public TimelineControl() { Height = 256; Focusable = true; Cursor = Cursors.Hand; SetResourceReference(ThemeBrushProperty, "PanelBrush"); }
    private double X(double time) => Label + time * PixelsPerSecond;
    private double Time(double x) => Math.Clamp((x - Label) / PixelsPerSecond, 0, Project?.EditedDuration ?? 0);
    protected override void OnRender(DrawingContext dc)
    {
        Brush text = ThemeService.Brush("TextBrush"), border = ThemeService.Brush("BorderBrush");
        base.OnRender(dc); dc.DrawRectangle(ThemeService.Brush("PanelBrush"), null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (Project == null) { Text(dc, "录制或导入视频后开始剪辑", 110, 70, 13, text); return; }
        var p = Project;
        double tick = PixelsPerSecond < 30 ? 5 : PixelsPerSecond < 80 ? 2 : 1;
        for (double t = 0; t <= p.EditedDuration; t += tick)
        {
            dc.DrawLine(new Pen(ThemeService.Brush("GridBrush"), 1), new Point(X(t), 28), new Point(X(t), Height));
            Text(dc, TimeSpan.FromSeconds(t).ToString(@"m\:ss"), X(t) + 3, 6, 12, text);
        }
        for (int i = 0; i < tracks.Length; i++) Text(dc, tracks[i], 8, i == 0 ? 57 : 111 + (i - 1) * 34, 14, text);
        double output = 0;
        for (int index = 0; index < p.Clips.Count; index++)
        {
            var clip = p.Clips[index]; var rect = new Rect(X(output), MainTop, Math.Max(1, clip.Duration * PixelsPerSecond), MainHeight);
            dc.PushClip(new RectangleGeometry(rect, 5, 5));
            dc.DrawRectangle(ThemeService.Brush("ClipBrush"), null, rect);
            if (Waveform && Peaks is { Length: > 0 })
            {
                for (int x = 0; x < rect.Width; x += 2)
                {
                    double source = clip.Start + x / PixelsPerSecond * clip.Speed;
                    int peak = Math.Clamp((int)(source / p.Duration * Peaks.Length), 0, Peaks.Length - 1);
                    double amplitude = Peaks[peak] * 26;
                    dc.DrawLine(new Pen(text, 1.5), new Point(rect.X + x, rect.Y + 33 - amplitude), new Point(rect.X + x, rect.Y + 33 + amplitude));
                }
            }
            else for (double x = 0; x < rect.Width; x += 96)
            {
                var image = ThumbnailAt?.Invoke(clip.Start + x / PixelsPerSecond * clip.Speed);
                if (image != null) dc.DrawImage(image, new Rect(rect.X + x, rect.Y, 116, MainHeight));
            }
            dc.DrawRectangle(ThemeService.Brush("ClipLabelBrush"), null, new Rect(rect.X, rect.Y + 45, rect.Width, 20));
            Text(dc, $"{index + 1} · {clip.Duration:0.0}s · {clip.Speed:0.##}×", rect.X + 5, rect.Y + 45, 13, text);
            dc.Pop();
            dc.DrawRoundedRectangle(null, new Pen(index == SelectedClipIndex && SelectedEffect == null ? text : border, index == SelectedClipIndex ? 2 : 1), rect, 5, 5);
            if (index > 0)
            {
                dc.DrawEllipse(ThemeService.Brush("WindowBrush"), new Pen(text, 1), new Point(rect.X, MainTop + 33), 6, 6);
                Text(dc, "↔", rect.X - 5, MainTop + 25, 10, text);
            }
            output += clip.Duration;
        }
        foreach (var e in p.Effects)
        {
            double y = e.Kind == "Camera" ? 178 : e.Kind is "Image" or "Blur" ? 212 : e.Kind == "Screen3D" ? 144 : 110;
            var r = new Rect(X(e.Start), y, Math.Max(3, (e.End - e.Start) * PixelsPerSecond), 26);
            var brush = e.Kind switch { "Camera" => "CameraTrackBrush", "Screen3D" => "ThreeDTrackBrush", "Image" => "ImageTrackBrush", "Blur" => "BlurTrackBrush", _ => "ZoomTrackBrush" };
            dc.DrawRoundedRectangle(ThemeService.Brush(brush), new Pen(e == SelectedEffect ? text : border, e == SelectedEffect ? 2 : 1), r, 5, 5);
            dc.PushClip(new RectangleGeometry(r)); Text(dc, e.Name, r.X + 6, r.Y + 3, 14, text); dc.Pop();
        }
        foreach (var marker in p.Markers.SelectMany(t => TimelineMap.ToOutput(p, t)))
            dc.DrawEllipse(text, null, new Point(X(marker), 28), 3, 3);
        dc.DrawLine(new Pen(text, 1.5), new Point(X(CurrentTime), 25), new Point(X(CurrentTime), Height));
        dc.DrawEllipse(text, null, new Point(X(CurrentTime), 27), 3, 3);
    }
    private static void Text(DrawingContext dc, string value, double x, double y, double size, Brush brush)
        => dc.DrawText(new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, brush, 1), new Point(x, y));
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (Project == null) return;
        Focus(); down = e.GetPosition(this); dragging = true; dragClip = -1; dragEffect = null; edge = ""; CaptureMouse();
        double t = Time(down.X);
        if (down.Y >= MainTop && down.Y <= MainTop + MainHeight)
        {
            var position = TimelineMap.Locate(Project, t); dragClip = SelectedClipIndex = position.Index; SelectedEffect = null;
            if (e.ClickCount == 2 && position.Index > 0 && Math.Abs(down.X - X(position.OutputStart)) < 9) { RestoreJunction?.Invoke(position.Index); dragging = false; ReleaseMouseCapture(); return; }
            if (Math.Abs(down.X - X(position.OutputStart)) < 8) edge = "Start";
            else if (Math.Abs(down.X - X(position.OutputEnd)) < 8) edge = "End";
        }
        else if (down.Y >= 110)
        {
            string kind = down.Y >= 212 ? "Overlay" : down.Y >= 178 ? "Camera" : down.Y >= 144 ? "Screen3D" : "Zoom";
            dragEffect = Project.Effects.LastOrDefault(f => t >= f.Start && t <= f.End && (kind == "Overlay" ? f.Kind is "Image" or "Blur" : f.Kind == kind));
            SelectedEffect = dragEffect;
            if (dragEffect != null) { effectStart = dragEffect.Start; effectEnd = dragEffect.End; BeginEdit?.Invoke();
                if (Math.Abs(down.X - X(effectStart)) < 8) edge = "Start"; else if (Math.Abs(down.X - X(effectEnd)) < 8) edge = "End"; }
        }
        SeekRequested?.Invoke(t); SelectionChanged?.Invoke(); InvalidateVisual();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!dragging || Project == null) return;
        var point = e.GetPosition(this); double delta = (point.X - down.X) / PixelsPerSecond;
        if (dragEffect != null)
        {
            double length = effectEnd - effectStart;
            double minimumLength = Math.Min(.05, length);
            if (edge == "Start") dragEffect.Start = Math.Clamp(SnapTime(effectStart + delta), 0, effectEnd - minimumLength);
            else if (edge == "End") dragEffect.End = Math.Clamp(SnapTime(effectEnd + delta), effectStart + minimumLength, Project.EditedDuration);
            else { dragEffect.Start = Math.Clamp(SnapTime(effectStart + delta), 0, Math.Max(0, Project.EditedDuration - length)); dragEffect.End = dragEffect.Start + length; }
            InvalidateVisual();
        }
        else if (dragClip < 0) SeekRequested?.Invoke(Time(point.X));
    }
    private double SnapTime(double time)
    {
        if (!Snap || Project == null) return time;
        var boundaries = new List<double> { 0, CurrentTime, Project.EditedDuration }; double t = 0;
        foreach (var c in Project.Clips) { t += c.Duration; boundaries.Add(t); }
        var closest = boundaries.MinBy(b => Math.Abs(b - time)); return Math.Abs(closest - time) * PixelsPerSecond < 8 ? closest : time;
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!dragging || Project == null) return;
        var point = e.GetPosition(this); double delta = (point.X - down.X) / PixelsPerSecond;
        dragging = false; ReleaseMouseCapture();
        if (dragEffect != null) { Edited?.Invoke(); return; }
        if (dragClip >= 0 && Math.Abs(point.X - down.X) > 5)
        {
            var c = Project.Clips[dragClip];
            if (edge == "Start") TrimRequested?.Invoke(dragClip, Math.Clamp(c.Start + delta * c.Speed, 0, c.End - .05), c.End);
            else if (edge == "End") TrimRequested?.Invoke(dragClip, c.Start, Math.Clamp(c.End + delta * c.Speed, c.Start + .05, Project.Duration));
            else MoveRequested?.Invoke(dragClip, TimelineMap.Locate(Project, Time(point.X)).Index);
        }
    }
}
