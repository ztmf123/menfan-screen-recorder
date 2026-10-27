using System.Text.Json;

namespace Menfan.Core;

public sealed class CameraStyle
{
    public CameraStyle Copy() => (CameraStyle)MemberwiseClone();
    public bool Visible { get; set; } = true;
    public double X { get; set; } = .83;
    public double Y { get; set; } = .78;
    public double Size { get; set; } = .22;
    public string Shape { get; set; } = "Circle";
    public bool Mirror { get; set; }
    public int Stroke { get; set; } = 3;
    public double Shadow { get; set; } = .25;
    public double PanX { get; set; } = .5;
    public double PanY { get; set; } = .5;
    public double Zoom { get; set; } = 1;
}

public sealed class SceneEffect
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "Zoom";
    public string Name { get; set; } = "缩放";
    public double Start { get; set; }
    public double End { get; set; }
    public double Transition { get; set; } = .4;
    public string MotionStyle { get; set; } = "Smooth";
    public double X { get; set; } = .5;
    public double Y { get; set; } = .5;
    public double Width { get; set; } = .3;
    public double Height { get; set; } = .2;
    public double Zoom { get; set; } = 1.6;
    public double RotateX { get; set; }
    public double RotateY { get; set; }
    public double RotateZ { get; set; }
    public double Perspective { get; set; } = .65;
    public double Blur { get; set; } = 18;
    public double FocusBlur { get; set; }
    public double FocusY { get; set; } = .5;
    public string Shape { get; set; } = "Circle";
    public string? AssetPath { get; set; }
    public bool BackgroundOnly { get; set; }
    public bool Auto { get; set; }
    public SceneEffect Copy() => (SceneEffect)MemberwiseClone();
    public override string ToString() => $"{Name} · {Start:0.00}–{End:0.00}s";
}

public readonly record struct ClipPosition(int Index, double OutputStart, double OutputEnd, double SourceTime);

public static class TimelineMap
{
    public static ClipPosition Locate(Project project, double time)
    {
        double output = 0;
        for (int i = 0; i < project.Clips.Count; i++)
        {
            var clip = project.Clips[i];
            if (time < output + clip.Duration || i == project.Clips.Count - 1)
                return new(i, output, output + clip.Duration, Math.Clamp(clip.Start + (time - output) * clip.Speed, clip.Start, clip.End));
            output += clip.Duration;
        }
        throw new InvalidOperationException("项目没有片段。");
    }
    public static IEnumerable<double> ToOutput(Project project, double sourceTime)
    {
        double output = 0;
        foreach (var clip in project.Clips)
        {
            if (sourceTime >= clip.Start && sourceTime < clip.End) yield return output + (sourceTime - clip.Start) / clip.Speed;
            output += clip.Duration;
        }
    }
    public static void ReplaceClips(Project project, List<Clip> replacement)
    {
        if (replacement.Count is < 1 or > 128) throw new InvalidOperationException("需要保留 1–128 个片段。");
        var transformed = new List<SceneEffect>();
        foreach (var effect in project.Effects)
        {
            var emitted = new HashSet<string>();
            double oldOutput = 0;
            foreach (var old in project.Clips)
            {
                double begin = Math.Max(effect.Start, oldOutput), end = Math.Min(effect.End, oldOutput + old.Duration);
                if (end > begin)
                {
                    double sourceStart = old.Start + (begin - oldOutput) * old.Speed, sourceEnd = old.Start + (end - oldOutput) * old.Speed;
                    double nextOutput = 0;
                    foreach (var next in replacement)
                    {
                        double s = Math.Max(sourceStart, next.Start), e = Math.Min(sourceEnd, next.End);
                        if (e > s)
                        {
                            var copy = effect.Copy(); copy.Id = Guid.NewGuid().ToString("N");
                            copy.Start = nextOutput + (s - next.Start) / next.Speed;
                            copy.End = nextOutput + (e - next.Start) / next.Speed;
                            if (emitted.Add($"{copy.Start:0.000000}:{copy.End:0.000000}")) transformed.Add(copy);
                        }
                        nextOutput += next.Duration;
                    }
                }
                oldOutput += old.Duration;
            }
        }
        project.Clips = replacement; project.Effects = transformed;
    }
    public static bool Split(Project project, double time)
    {
        var p = Locate(project, time); var clip = project.Clips[p.Index];
        if (p.SourceTime < clip.Start + .03 || p.SourceTime > clip.End - .03 || project.Clips.Count >= 128) return false;
        project.Clips.Insert(p.Index + 1, new() { Start = p.SourceTime, End = clip.End, Speed = clip.Speed });
        clip.End = p.SourceTime;
        return true;
    }
    public static bool Ripple(Project project, double time, bool before)
    {
        var p = Locate(project, time);
        var list = project.Clips.Select(c => new Clip { Start = c.Start, End = c.End, Speed = c.Speed }).ToList();
        if (before) list[p.Index].Start = p.SourceTime; else list[p.Index].End = p.SourceTime;
        list.RemoveAll(c => c.End - c.Start < .03);
        if (list.Count == 0) return false;
        ReplaceClips(project, list); return true;
    }
    public static void GenerateAutoZoom(Project project)
    {
        project.Effects.RemoveAll(e => e.Kind == "Zoom" && e.Auto);
        var clicks = project.Cursor.Where(c => c.Click).SelectMany(c => ToOutput(project, c.Time).Select(t => (Time: t, c.X, c.Y))).OrderBy(c => c.Time).ToList();
        double previousEnd = -1;
        foreach (var click in clicks)
        {
            if (project.Effects.Count >= 512) break;
            if (click.Time < previousEnd - .4) continue;
            var effect = new SceneEffect { Kind = "Zoom", Name = "缩放 · 自动", Auto = true,
                Start = Math.Max(0, click.Time - .25), End = Math.Min(project.EditedDuration, click.Time + 2.5), X = click.X, Y = click.Y, Zoom = 1.6, Transition = .4, MotionStyle = project.Style.MotionStyle };
            if (effect.End - effect.Start >= .2) { project.Effects.Add(effect); previousEnd = effect.End; }
        }
    }
}

public readonly record struct SceneState(double SourceTime, double Zoom, double FocusX, double FocusY,
    double RotateX, double RotateY, double RotateZ, double Perspective, CameraStyle Camera);

public static class SceneEvaluator
{
    public static double Ease(double t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }
    public static double Ease(double t, string style)
    {
        t = Math.Clamp(t, 0, 1);
        return style switch {
            "Brisk" => 1 - Math.Pow(1 - t, 3),
            "Gentle" => t * t * t * (t * (t * 6 - 15) + 10),
            "Bouncy" when t > 0 && t < 1 => 1 - Math.Pow(2, -8 * t) * Math.Cos(t * Math.PI * 3),
            _ => Ease(t)
        };
    }
    public static double Weight(SceneEffect e, double t)
    {
        if (t < e.Start || t > e.End) return 0;
        double transition = Math.Min(e.Transition, (e.End - e.Start) / 2);
        if (transition <= 0) return 1;
        return Math.Min(Ease((t - e.Start) / transition), Ease((e.End - t) / transition));
    }
    private static bool SameChannel(SceneEffect a, SceneEffect b) => a.Kind == b.Kind && (a.Kind != "Image" || a.AssetPath == b.AssetPath);
    public static SceneEffect? Previous(Project p, SceneEffect e) => p.Effects.LastOrDefault(other => other != e && SameChannel(other, e) && Math.Abs(other.End - e.Start) < .001);
    public static SceneEffect? Next(Project p, SceneEffect e) => p.Effects.FirstOrDefault(other => other != e && SameChannel(other, e) && Math.Abs(other.Start - e.End) < .001);
    public static double Envelope(Project p, SceneEffect e, double t)
    {
        if (t < e.Start || t >= e.End) return 0;
        double transition = Math.Min(e.Transition, (e.End - e.Start) / 2); if (transition <= 0) return 1;
        double enter = Previous(p, e) == null ? Ease((t - e.Start) / transition) : 1;
        double leave = Next(p, e) == null ? Ease((e.End - t) / transition) : 1;
        return Math.Min(enter, leave);
    }
    public static double AnimatedValue(Project p, SceneEffect e, double t, Func<SceneEffect, double> get, double basis)
    {
        double transition = Math.Min(e.Transition, (e.End - e.Start) / 2);
        double entry = transition > 0 ? Ease((t - e.Start) / transition, e.MotionStyle) : 1;
        var previous = Previous(p, e);
        double from = previous != null ? get(previous) : basis;
        double result = from + (get(e) - from) * entry;
        if (Next(p, e) == null && transition > 0) result = basis + (result - basis) * Ease((e.End - t) / transition);
        return result;
    }
    public static SceneState Evaluate(Project project, double time)
    {
        var source = TimelineMap.Locate(project, time).SourceTime;
        var camera = project.Camera.Copy();
        double zoom = project.Style.Zoom, x = .5, y = .5, rx = 0, ry = 0, rz = 0, perspective = .65;
        if (project.Style.FollowCursor && project.Cursor.Count > 0)
        { var cursor = CursorAt(project, source); x = cursor.X; y = cursor.Y; }
        foreach (var e in project.Effects.Where(e => e.Kind is "Zoom" or "Screen3D" or "Camera"))
        {
            if (time < e.Start || time >= e.End) continue;
            if (e.Kind == "Camera")
            {
                camera.X = AnimatedValue(project, e, time, v => v.X, camera.X);
                camera.Y = AnimatedValue(project, e, time, v => v.Y, camera.Y);
                camera.Size = AnimatedValue(project, e, time, v => v.Width, camera.Size);
            }
            else
            {
                zoom = AnimatedValue(project, e, time, v => v.Zoom, zoom);
                x = AnimatedValue(project, e, time, v => v.X, x); y = AnimatedValue(project, e, time, v => v.Y, y);
                if (e.Kind == "Screen3D")
                { rx = AnimatedValue(project, e, time, v => v.RotateX, 0); ry = AnimatedValue(project, e, time, v => v.RotateY, 0); rz = AnimatedValue(project, e, time, v => v.RotateZ, 0); perspective = e.Perspective; }
            }
        }
        return new(source, zoom, x, y, rx, ry, rz, perspective, camera);
    }
    public static CursorSample CursorAt(Project project, double sourceTime)
    {
        if (project.Cursor.Count == 0) return new() { X = .5, Y = .5 };
        double t = sourceTime + project.Style.CursorOffset;
        int low = 0, high = project.Cursor.Count - 1;
        while (low < high) { int middle = (low + high + 1) / 2; if (project.Cursor[middle].Time <= t) low = middle; else high = middle - 1; }
        var a = project.Cursor[low]; var b = project.Cursor[Math.Min(low + 1, project.Cursor.Count - 1)];
        double w = b.Time > a.Time ? Math.Clamp((t - a.Time) / (b.Time - a.Time), 0, 1) : 0;
        return new() { Time = a.Time, X = a.X + (b.X - a.X) * w, Y = a.Y + (b.Y - a.Y) * w, ShapePath = a.ShapePath, HotspotX = a.HotspotX, HotspotY = a.HotspotY, Visible = a.Visible };
    }
}

public static class SceneValidation
{
    private static bool Between(double x, double min, double max) => double.IsFinite(x) && x >= min && x <= max;
    public static void Validate(Project p)
    {
        var s = p.Style;
        if (!Between(p.FrameRate, 1, 1000)) throw new InvalidDataException("帧率无效。");
        if (!new[] { "Source", "16:9", "9:16", "1:1", "4:3", "3:4", "21:9" }.Contains(s.Aspect)) throw new InvalidDataException("画布比例无效。");
        if (!new[] { s.CropLeft, s.CropTop, s.CropRight, s.CropBottom }.All(c => Between(c, 0, .9)) || s.CropLeft + s.CropRight >= .95 || s.CropTop + s.CropBottom >= .95)
            throw new InvalidDataException("画面裁切范围无效。");
        if (!Between(s.ScreenScale, .1, 2) || !Between(s.ScreenX, 0, 1) || !Between(s.ScreenY, 0, 1) || !Between(s.CursorSize, .25, 5) || !Between(s.CursorOffset, -5, 5)) throw new InvalidDataException("场景参数无效。");
        if (!Between(p.Camera.Size, .05, 1) || !Between(p.Camera.X, 0, 1) || !Between(p.Camera.Y, 0, 1) || !Between(p.Camera.Zoom, 1, 4)
            || !Between(p.Camera.PanX, 0, 1) || !Between(p.Camera.PanY, 0, 1) || !Between(p.CameraOffset, -86400, 86400)) throw new InvalidDataException("摄像头参数无效。");
        if (!Between(s.Shadow, 0, 1) || !Between(s.ClickOpacity, 0, 1) || !Between(s.ClickSize, .1, 5) || !Between(s.CursorSmoothing, 0, 1)
            || !Between(s.IntroDuration, .05, 10) || s.Stroke is < 0 or > 100) throw new InvalidDataException("场景外观参数无效。");
        foreach (var color in new[] { s.GradientColor, s.CursorColor })
            if (color == null || color.Length != 7 || color[0] != '#' || !color.Skip(1).All(Uri.IsHexDigit)) throw new InvalidDataException("颜色格式无效。");
        if (p.Effects.Count > 512) throw new InvalidDataException("效果片段过多。");
        foreach (var e in p.Effects)
            if (!Between(e.Start, 0, p.EditedDuration + .1) || !Between(e.End, 0, p.EditedDuration + .1) || e.End <= e.Start
                || !Between(e.X, 0, 1) || !Between(e.Y, 0, 1) || !Between(e.Width, .01, 2) || !Between(e.Height, .01, 2)
                || !Between(e.Zoom, 1, 4) || !Between(e.Transition, 0, 5) || !Between(e.RotateX, -70, 70) || !Between(e.RotateY, -70, 70) || !Between(e.RotateZ, -180, 180)
                || !Between(e.Blur, 0, 100) || !Between(e.FocusBlur, 0, 100) || !Between(e.FocusY, 0, 1) || !Between(e.Perspective, .1, 1.5))
                throw new InvalidDataException("效果片段的范围或参数无效。");
    }
}
