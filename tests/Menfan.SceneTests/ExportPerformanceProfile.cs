using System.IO;
using System.Text.Json;
using Menfan.App;
using Menfan.Core;

internal static class ExportPerformanceProfile
{
    public static void Run(string label)
    {
        var tools = new MediaTools(MediaTools.Locate() ?? throw new Exception("FFmpeg missing"));
        var folder = Path.GetFullPath("artifacts/export-profile"); Directory.CreateDirectory(folder);
        var source = Path.Combine(folder, "source.mp4");
        if (!File.Exists(source)) MediaTools.RunAsync(tools.Ffmpeg, ["-y", "-f", "lavfi", "-i", "testsrc2=size=1920x1080:rate=30:duration=2", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", source]).GetAwaiter().GetResult();
        var p = new Project { MediaPath = source, Width = 1920, Height = 1080, Duration = 2, Clips = [new() { End = 2 }] };
        p.ResetVisualEdits(); p.Style.Aspect = "16:9"; p.Style.BackgroundKind = "Grid"; p.Style.Padding = 60; p.Style.Shadow = .2;
        p.Effects.Add(new() { Kind = "Screen3D", Start = 0, End = 2, RotateY = 15, FocusBlur = 8, Zoom = 1.1 });
        SceneExporter.ExportAsync(tools, p, Path.Combine(folder, label + ".mp4"), new(1080, 30, Encoder: label == "auto" ? "Auto" : "Software"), null, CancellationToken.None).GetAwaiter().GetResult();
        var profile = SceneExporter.LastProfile ?? throw new Exception("Missing metrics");
        var info = tools.ProbeAsync(Path.Combine(folder, label + ".mp4")).GetAwaiter().GetResult();
        if (info.Width != 1920 || info.Height != 1080 || Math.Abs(info.Duration - 2) > .1) throw new Exception("Export dimensions/duration changed");
        var json = JsonSerializer.Serialize(profile); File.WriteAllText(Path.Combine(folder, label + ".json"), json); Console.WriteLine(json);
    }
}
