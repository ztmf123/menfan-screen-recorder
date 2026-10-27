using System.Diagnostics;
using System.Windows.Media.Imaging;
using Menfan.Core;

namespace Menfan.App;

internal static class SceneExporter
{
    public static Task ExportAsync(MediaTools tools, Project project, string destination, ExportOptions options, IProgress<double>? progress, CancellationToken token)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => {
            try { Export(tools, project, destination, options, progress, token); completion.TrySetResult(); }
            catch (OperationCanceledException) { completion.TrySetCanceled(token); }
            catch (Exception ex) { completion.TrySetException(ex); }
        }) { IsBackground = true, Name = "Menfan scene exporter" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
    private static void Export(MediaTools tools, Project p, string destination, ExportOptions options, IProgress<double>? progress, CancellationToken token)
    {
        p.Validate();
        if (options.AudioOnly) { tools.ExportAsync(p, destination, options, progress, token).GetAwaiter().GetResult(); return; }
        foreach (var source in new[] { p.MediaPath, p.CameraPath, p.Style.BackgroundPath, p.SystemAudioPath, p.MicrophonePath }.Concat(p.Effects.Select(e => e.AssetPath)))
            if (source != null && string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("导出文件不能覆盖原始素材。");
        var dimensions = SceneRenderer.Dimensions(p, options.Height); int width = dimensions.Width, height = dimensions.Height;
        var folder = Path.GetDirectoryName(Path.GetFullPath(destination))!; Directory.CreateDirectory(folder);
        var temp = Path.Combine(folder, $".menfan-{Guid.NewGuid():N}.mp4");
        var audio = Path.Combine(folder, $".menfan-{Guid.NewGuid():N}.m4a");
        Process? encoder = null;
        using var screen = new MediaFrameReader(tools, p.MediaPath, Math.Min(p.Width, width * 2), (int)(Math.Min(p.Width, width * 2) / (double)p.Width * p.Height));
        var cameraInfo = p.CameraPath != null ? tools.ProbeAsync(p.CameraPath, token).GetAwaiter().GetResult() : null;
        using var camera = cameraInfo != null ? new MediaFrameReader(tools, p.CameraPath!, Math.Min(cameraInfo.Width, width), Math.Min(cameraInfo.Width, width) * cameraInfo.Height / cameraInfo.Width) : null;
        var bgInfo = p.Style.BackgroundKind == "Video" && p.Style.BackgroundPath != null ? tools.ProbeAsync(p.Style.BackgroundPath, token).GetAwaiter().GetResult() : null;
        using var background = bgInfo != null ? new MediaFrameReader(tools, p.Style.BackgroundPath!, width, height) : null;
        try
        {
            var info = tools.ProbeAsync(p.MediaPath, token).GetAwaiter().GetResult();
            bool hasAudio = !p.Style.Mute && (info.HasAudio || p.SystemAudioPath != null || p.MicrophonePath != null);
            if (hasAudio) tools.ExportAsync(p, audio, new(AudioOnly: true), null, token).GetAwaiter().GetResult();
            var args = new List<string> { "-hide_banner", "-y", "-f", "rawvideo", "-pix_fmt", "bgra", "-s", $"{width}x{height}", "-r", options.Fps.ToString(), "-i", "pipe:0" };
            if (hasAudio) args.AddRange(["-i", audio]);
            args.AddRange(["-map", "0:v", "-c:v", "libx264", "-preset", "fast", "-crf", options.Quality.ToString(), "-pix_fmt", "yuv420p"]);
            if (hasAudio) args.AddRange(["-map", "1:a", "-c:a", "copy"]);
            args.AddRange(["-movflags", "+faststart", temp]);
            encoder = new() { StartInfo = MediaTools.StartInfo(tools.Ffmpeg, args) }; encoder.Start();
            var stderr = encoder.StandardError.ReadToEndAsync(); var stdout = encoder.StandardOutput.ReadToEndAsync();
            using var registration = token.Register(() => { try { if (!encoder.HasExited) encoder.Kill(true); } catch (InvalidOperationException) { } });
            var renderer = new SceneRenderer { FfmpegPath = tools.Ffmpeg }; var pixels = new byte[width * height * 4];
            int frames = Math.Max(1, (int)Math.Ceiling(p.EditedDuration * options.Fps));
            for (int frame = 0; frame < frames; frame++)
            {
                token.ThrowIfCancellationRequested();
                double time = Math.Min(frame / (double)options.Fps, p.EditedDuration - .0001);
                var state = SceneEvaluator.Evaluate(p, time);
                var source = screen.GetFrameAsync(state.SourceTime, token).GetAwaiter().GetResult();
                BitmapSource? cam = camera != null && state.SourceTime >= p.CameraOffset ? camera.GetFrameAsync(Math.Max(0, Math.Min(cameraInfo!.Duration - .02, state.SourceTime - p.CameraOffset)), token).GetAwaiter().GetResult() : null;
                BitmapSource? bg = background != null ? background.GetFrameAsync(time % bgInfo!.Duration, token).GetAwaiter().GetResult() : null;
                var rendered = renderer.Render(p, time, source, cam, bg, width, height);
                rendered.CopyPixels(pixels, width * 4, 0);
                encoder.StandardInput.BaseStream.Write(pixels);
                progress?.Report((frame + 1d) / frames);
            }
            encoder.StandardInput.Close(); encoder.WaitForExit();
            var error = stderr.GetAwaiter().GetResult(); stdout.GetAwaiter().GetResult(); token.ThrowIfCancellationRequested();
            if (encoder.ExitCode != 0) throw new InvalidOperationException("合成导出失败：" + error);
            File.Move(temp, destination, true);
        }
        catch (Exception ex) when (token.IsCancellationRequested && ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            throw new OperationCanceledException(token);
        }
        finally
        {
            if (encoder != null)
            {
                try
                {
                    if (!encoder.HasExited) encoder.Kill(true);
                    // Kill is asynchronous; wait for file handles to close before cleanup.
                    encoder.WaitForExit();
                }
                catch (InvalidOperationException) { }
                finally { encoder.Dispose(); }
            }
            if (File.Exists(temp)) File.Delete(temp); if (File.Exists(audio)) File.Delete(audio);
        }
    }
}
