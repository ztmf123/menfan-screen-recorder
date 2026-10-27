using System.Diagnostics;
using System.Text;
using Menfan.Core;

namespace Menfan.App;

internal sealed class SegmentRecorder : IDisposable
{
    private Process? process;
    private Task? errorReader;
    private Task? outputReader;
    private AudioRecorder? system, microphone;
    private readonly StringBuilder errors = new();
    private readonly Stopwatch clock = new();
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? cursorCancel;
    private Task? cursorTask;
    private Project? project;
    public double Elapsed => clock.Elapsed.TotalSeconds;
    public bool HasExited => process?.HasExited ?? true;

    public async Task StartAsync(MediaTools tools, CaptureTarget target, int fps, bool withSystem, bool withMicrophone, string folder, string? systemDevice = null, string? microphoneDevice = null)
    {
        Directory.CreateDirectory(folder);
        int width = target.Width / 2 * 2, height = target.Height / 2 * 2;
        if (target.Handle != IntPtr.Zero && Native.GetClientRect(target.Handle, out var client))
        { width = (client.Right - client.Left) / 2 * 2; height = (client.Bottom - client.Top) / 2 * 2; }
        project = new() { MediaPath = Path.Combine(folder, "screen.mkv"), Width = width, Height = height, CursorEmbedded = false };
        project.ResetVisualEdits();
        var args = new List<string> { "-hide_banner", "-y", "-thread_queue_size", "1024", "-f", "gdigrab", "-framerate", fps.ToString(), "-draw_mouse", "0" };
        if (target.DeviceName != null)
        {
            args = ["-hide_banner", "-y", "-f", "dshow", "-rtbufsize", "256M", "-i", "video=" + target.DeviceName];
        }
        else if (target.Handle != IntPtr.Zero)
        {
            // Window capture uses FFmpeg's HWND input, independent of localized/duplicate titles.
            args.AddRange(["-i", $"hwnd={target.Handle.ToInt64()}"]);
        }
        else args.AddRange(["-offset_x", target.X.ToString(), "-offset_y", target.Y.ToString(), "-video_size", $"{width}x{height}", "-i", "desktop"]);
        args.AddRange(["-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2", "-c:v", "libx264", "-preset", "ultrafast", "-tune", "zerolatency", "-crf", "18", "-pix_fmt", "yuv420p", "-progress", "pipe:1", "-stats_period", "0.1", project.MediaPath]);
        try
        {
            if (withSystem) { project.SystemAudioPath = Path.Combine(folder, "system.wav"); system = new(project.SystemAudioPath, true, systemDevice); }
            if (withMicrophone) { project.MicrophonePath = Path.Combine(folder, "microphone.wav"); microphone = new(project.MicrophonePath, false, microphoneDevice); }
            process = new() { StartInfo = MediaTools.StartInfo(tools.Ffmpeg, args) };
            clock.Start();
            process.Start();
            errorReader = Task.Run(async () => {
                while (await process.StandardError.ReadLineAsync() is { } line)
                    lock (errors) { if (errors.Length > 16000) errors.Remove(0, 8000); errors.AppendLine(line); }
            });
            outputReader = Task.Run(async () => {
                while (await process.StandardOutput.ReadLineAsync() is { } line)
                    if (line.StartsWith("frame=") && int.TryParse(line[6..], out var frame) && frame > 0) ready.TrySetResult();
            });
            // Wait for an actual encoded frame, fail promptly if FFmpeg exits.
            var exited = process.WaitForExitAsync();
            if (await Task.WhenAny(ready.Task, exited, Task.Delay(15000)) != ready.Task)
            {
                if (!process.HasExited) process.Kill(true);
                await process.WaitForExitAsync();
                await errorReader;
                throw new InvalidOperationException("无法开始屏幕采集。\n" + errors);
            }
            if (system != null) { project.SystemAudioOffset = Elapsed; system.Start(); }
            if (microphone != null) { project.MicrophoneOffset = Elapsed; microphone.Start(); }
            cursorCancel = new();
            var cursorShapes = new CursorShapes(Path.Combine(folder, "cursors"));
            cursorTask = Task.Run(async () => {
                bool previousDown = false;
                try
                {
                    while (!cursorCancel.IsCancellationRequested)
                    {
                        Native.GetCursorPos(out var point);
                        var down = (Native.GetAsyncKeyState(1) & 0x8000) != 0;
                        var x = target.X; var y = target.Y;
                        if (target.Handle != IntPtr.Zero)
                        { var origin = new Native.Point(); if (Native.ClientToScreen(target.Handle, ref origin)) { x = origin.X; y = origin.Y; } }
                        var shape = cursorShapes.Current();
                        bool inside = point.X >= x && point.X < x + width && point.Y >= y && point.Y < y + height;
                        if (target.DeviceName == null) project.Cursor.Add(new() { Time = Elapsed, X = Math.Clamp((point.X - x) / (double)width, 0, 1),
                            Y = Math.Clamp((point.Y - y) / (double)height, 0, 1), Click = inside && down && !previousDown, Visible = inside && shape.Visible,
                            ShapePath = shape.Path, HotspotX = shape.X, HotspotY = shape.Y });
                        previousDown = down;
                        await Task.Delay(16, cursorCancel.Token);
                    }
                }
                catch (OperationCanceledException) { }
            });
        }
        catch { Dispose(); throw; }
    }

    public void AddMarker() { project?.Markers.Add(Elapsed); }

    public async Task<Project> StopAsync(MediaTools tools)
    {
        if (process == null || project == null) throw new InvalidOperationException("没有正在进行的录制。");
        var stopWallTime = Elapsed;
        cursorCancel?.Cancel();
        if (cursorTask != null) await cursorTask;
        var audioTasks = new[] { system?.StopAsync() ?? Task.CompletedTask, microphone?.StopAsync() ?? Task.CompletedTask };
        try
        {
            if (!process.HasExited)
            {
                await process.StandardInput.WriteLineAsync("q");
                await process.StandardInput.FlushAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) { process.Kill(true); throw new TimeoutException("录制停止超时，已保留 MKV 素材供恢复。"); }
            }
            if (errorReader != null) await errorReader;
            if (outputReader != null) await outputReader;
            await Task.WhenAll(audioTasks);
            if (process.ExitCode != 0) throw new InvalidOperationException("录制异常结束，素材保留在项目目录。\n" + errors);
            // Close WAV headers before probing or exporting.
            system?.Dispose(); system = null; microphone?.Dispose(); microphone = null;
            var info = await tools.ProbeAsync(project.MediaPath);
            project.Width = info.Width; project.Height = info.Height; project.Duration = info.Duration;
            // FFmpeg startup precedes its first encoded frame. Calibrate approximate timestamps.
            double correction = Math.Max(0, stopWallTime - info.Duration);
            project.SystemAudioOffset -= correction;
            project.MicrophoneOffset -= correction;
            foreach (var c in project.Cursor) c.Time = Math.Max(0, c.Time - correction);
            project.Markers = project.Markers.Select(m => Math.Clamp(m - correction, 0, info.Duration)).ToList();
            project.Clips.Add(new() { Start = 0, End = info.Duration });
            project.Save(Path.Combine(Path.GetDirectoryName(project.MediaPath)!, "project.menfan"));
            return project;
        }
        finally { clock.Stop(); Dispose(); }
    }

    public void Dispose()
    {
        cursorCancel?.Cancel();
        if (process is { HasExited: false }) process.Kill(true);
        process?.Dispose(); process = null;
        system?.Dispose(); system = null;
        microphone?.Dispose(); microphone = null;
    }
}
