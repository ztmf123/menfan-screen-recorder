using System.Diagnostics;
using Menfan.Core;

namespace Menfan.App;

internal sealed class Recorder : IDisposable
{
    private SegmentRecorder? active;
    private MediaTools tools = null!;
    private CaptureTarget target = null!;
    private int fps;
    private bool withSystem, withMicrophone;
    private string folder = "";
    private string? systemDevice, microphoneDevice, cameraName;
    private CameraCapture? camera;
    private readonly List<Project> segments = [];
    public bool IsPaused => active == null;
    public double Elapsed => segments.Sum(s => s.Duration) + (active?.Elapsed ?? 0);
    public bool HasExited => active?.HasExited ?? false;
    public async Task StartAsync(MediaTools tools, CaptureTarget target, int fps, bool withSystem, bool withMicrophone, string folder,
        string? systemDevice = null, string? microphoneDevice = null, string? cameraName = null)
    {
        this.tools = tools; this.target = target; this.fps = fps; this.withSystem = withSystem; this.withMicrophone = withMicrophone;
        this.folder = folder; this.systemDevice = systemDevice; this.microphoneDevice = microphoneDevice; this.cameraName = cameraName;
        await ResumeAsync();
    }
    public async Task PauseAsync()
    {
        if (active == null) return;
        var segment = await active.StopAsync(tools); active.Dispose(); active = null;
        segments.Add(segment);
        if (camera != null)
        {
            var capture = camera; camera = null;
            try { await capture.StopAsync(); segment.CameraPath = capture.Path; segment.CameraOffset = capture.Offset; }
            finally { capture.Dispose(); }
        }
    }
    // Device discovery/activation and Process.Start can block before their first await.
    // Keep the entire startup (including the camera) away from the WPF dispatcher.
    public Task ResumeAsync() => Task.Run(ResumeCoreAsync);
    private async Task ResumeCoreAsync()
    {
        if (active != null) return;
        var segmentFolder = Path.Combine(folder, $"take-{segments.Count + 1:D3}");
        var pending = new SegmentRecorder();
        try
        {
            await pending.StartAsync(tools, target, fps, withSystem, withMicrophone, segmentFolder, systemDevice, microphoneDevice);
            if (cameraName != null)
            {
                camera = new CameraCapture(tools, cameraName, Path.Combine(segmentFolder, "camera.mkv"), pending.Elapsed);
                await camera.StartAsync();
            }
            active = pending;
        }
        catch { pending.Dispose(); camera?.Dispose(); camera = null; throw; }
    }
    public void AddMarker() => active?.AddMarker();
    public async Task<Project> StopAsync(MediaTools tools)
    {
        await PauseAsync();
        if (segments.Count == 0) throw new InvalidOperationException("没有可保存的录制片段。");
        var first = segments[0];
        var result = new Project { MediaPath = Path.Combine(folder, "screen.mkv"), Width = first.Width, Height = first.Height, FrameRate = fps, CursorEmbedded = false };
        result.ResetVisualEdits();
        var args = new List<string> { "-y" }; foreach (var s in segments) args.AddRange(["-i", s.MediaPath]);
        var graph = new System.Text.StringBuilder();
        for (int i = 0; i < segments.Count; i++) graph.Append($"[{i}:v]setpts=PTS-STARTPTS,scale={first.Width}:{first.Height},setsar=1,fps={fps}[v{i}];");
        for (int i = 0; i < segments.Count; i++) graph.Append($"[v{i}]");
        graph.Append($"concat=n={segments.Count}:v=1:a=0[v]");
        args.AddRange(["-filter_complex", graph.ToString(), "-map", "[v]", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "18", result.MediaPath]);
        await MediaTools.RunAsync(tools.Ffmpeg, args);
        if (withSystem) { result.SystemAudioPath = Path.Combine(folder, "system.wav"); await MergeAudio(true, result.SystemAudioPath); }
        if (withMicrophone) { result.MicrophonePath = Path.Combine(folder, "microphone.wav"); await MergeAudio(false, result.MicrophonePath); }
        if (cameraName != null && segments.All(s => s.CameraPath != null))
        {
            result.CameraPath = Path.Combine(folder, "camera.mp4"); await MergeCamera(result.CameraPath);
        }
        foreach (var s in segments)
        {
            foreach (var c in s.Cursor) result.Cursor.Add(new() { Time = result.Duration + c.Time, X = c.X, Y = c.Y, Click = c.Click, ShapePath = c.ShapePath, HotspotX = c.HotspotX, HotspotY = c.HotspotY, Visible = c.Visible });
            result.Markers.AddRange(s.Markers.Select(t => result.Duration + t)); result.Duration += s.Duration;
        }
        result.Duration = (await tools.ProbeAsync(result.MediaPath)).Duration;
        result.Clips = [new() { End = result.Duration }];
        result.Markers = result.Markers.Select(t => Math.Min(t, result.Duration)).ToList();
        result.Save(Path.Combine(folder, "project.menfan"));
        return result;
    }
    private async Task MergeAudio(bool system, string destination)
    {
        var args = new List<string> { "-y" }; var graph = new System.Text.StringBuilder();
        for (int i = 0; i < segments.Count; i++)
        {
            var s = segments[i]; var path = system ? s.SystemAudioPath : s.MicrophonePath;
            if (path == null) throw new InvalidOperationException("录音片段丢失，请检查录制目录。");
            args.AddRange(["-i", path]); double offset = system ? s.SystemAudioOffset : s.MicrophoneOffset;
            graph.Append($"[{i}:a]aresample=48000,aformat=channel_layouts=stereo,asetpts=PTS-STARTPTS");
            if (offset > 0) graph.Append($",adelay={MediaTools.N(offset * 1000)}:all=1");
            else if (offset < 0) graph.Append($",atrim=start={MediaTools.N(-offset)},asetpts=PTS-STARTPTS");
            graph.Append($",apad,atrim=duration={MediaTools.N(s.Duration)}[a{i}];");
        }
        for (int i = 0; i < segments.Count; i++) graph.Append($"[a{i}]");
        graph.Append($"concat=n={segments.Count}:v=0:a=1[a]");
        args.AddRange(["-filter_complex", graph.ToString(), "-map", "[a]", "-c:a", "pcm_s16le", destination]);
        await MediaTools.RunAsync(tools.Ffmpeg, args);
    }
    private async Task MergeCamera(string destination)
    {
        var args = new List<string> { "-y" }; var graph = new System.Text.StringBuilder();
        for (int i = 0; i < segments.Count; i++)
        {
            var s = segments[i]; args.AddRange(["-i", s.CameraPath!]);
            graph.Append($"[{i}:v]setpts=PTS-STARTPTS,scale=1280:720:force_original_aspect_ratio=decrease,pad=1280:720:(ow-iw)/2:(oh-ih)/2,fps={fps},setsar=1,tpad=start_duration={MediaTools.N(Math.Max(0, s.CameraOffset))}:start_mode=clone:stop_duration={MediaTools.N(s.Duration)}:stop_mode=clone,trim=duration={MediaTools.N(s.Duration)}[c{i}];");
        }
        for (int i = 0; i < segments.Count; i++) graph.Append($"[c{i}]");
        graph.Append($"concat=n={segments.Count}:v=1:a=0[c]"); args.AddRange(["-filter_complex", graph.ToString(), "-map", "[c]", "-c:v", "libx264", "-preset", "ultrafast", destination]);
        await MediaTools.RunAsync(tools.Ffmpeg, args);
    }
    public void Dispose() { active?.Dispose(); active = null; camera?.Dispose(); camera = null; }
}

internal sealed class CameraCapture : IDisposable
{
    private readonly Process process;
    private Task<string>? errors;
    public string Path { get; }
    public double Offset { get; }
    public CameraCapture(MediaTools tools, string name, string path, double offset)
    {
        Path = path; Offset = offset;
        process = new() { StartInfo = MediaTools.StartInfo(tools.Ffmpeg, ["-hide_banner", "-y", "-f", "dshow", "-rtbufsize", "256M", "-i", "video=" + name,
            "-an", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", path]) };
    }
    public async Task StartAsync()
    {
        process.Start(); errors = process.StandardError.ReadToEndAsync(); _ = process.StandardOutput.ReadToEndAsync();
        await Task.Delay(600);
        if (process.HasExited) throw new InvalidOperationException("摄像头无法启动：" + await errors);
    }
    public async Task StopAsync()
    {
        if (!process.HasExited) { await process.StandardInput.WriteLineAsync("q"); await process.StandardInput.FlushAsync(); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
        if (process.ExitCode != 0) throw new InvalidOperationException("摄像头录制失败：" + (errors != null ? await errors : ""));
    }
    public void Dispose() { if (!process.HasExited) process.Kill(true); process.Dispose(); }
}
