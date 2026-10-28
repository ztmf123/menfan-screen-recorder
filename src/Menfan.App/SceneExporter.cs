using System.Diagnostics;
using System.Windows.Media.Imaging;
using Menfan.Core;

namespace Menfan.App;

internal static class SceneExporter
{
    internal sealed record ExportProfile(int Frames, double DecodeMs, double RenderMs, double WriteMs, double TotalMs, string Encoder);
    internal static ExportProfile? LastProfile { get; private set; }
    private sealed class EncodingFailure(string encoder, string message, Exception? inner = null) : Exception(message, inner)
    { public string Encoder { get; } = encoder; }
    private sealed record DecodedFrame(double Time, BitmapSource Screen, BitmapSource? Camera, BitmapSource? Background, double Milliseconds);

    public static Task ExportAsync(MediaTools tools, Project project, string destination, ExportOptions options, IProgress<double>? progress, CancellationToken token)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => {
            try
            {
                try { Export(tools, project, destination, options, progress, token); }
                catch (EncodingFailure ex) when (options.Encoder == "Auto" && ex.Encoder != "libx264" && !token.IsCancellationRequested)
                { Export(tools, project, destination, options with { Encoder = "Software" }, progress, token); }
                completion.TrySetResult();
            }
            catch (OperationCanceledException) { completion.TrySetCanceled(token); }
            catch (Exception ex) { completion.TrySetException(ex); }
        }) { IsBackground = true, Name = "Menfan scene exporter" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
    private static void Export(MediaTools tools, Project p, string destination, ExportOptions options, IProgress<double>? progress, CancellationToken token)
    {
        var total = Stopwatch.StartNew(); double decodeMs = 0, renderMs = 0, writeMs = 0;
        p.Validate();
        if (options.AudioOnly) { tools.ExportAsync(p, destination, options, progress, token).GetAwaiter().GetResult(); return; }
        foreach (var source in new[] { p.MediaPath, p.CameraPath, p.Style.BackgroundPath, p.SystemAudioPath, p.MicrophonePath }.Concat(p.Effects.Select(e => e.AssetPath)))
            if (source != null && string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("导出文件不能覆盖原始素材。");
        var dimensions = SceneRenderer.Dimensions(p, options.Height); int width = dimensions.Width, height = dimensions.Height;
        string backend = VideoEncoderSelector.Select(tools, options, width, height, token);
        var folder = Path.GetDirectoryName(Path.GetFullPath(destination))!; Directory.CreateDirectory(folder);
        var temp = Path.Combine(folder, $".menfan-{Guid.NewGuid():N}.mp4");
        var audio = Path.Combine(folder, $".menfan-{Guid.NewGuid():N}.m4a");
        Process? encoder = null;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var ct = lifetime.Token;
        Task<DecodedFrame>? pending = null; Task writing = Task.CompletedTask;
        using var screen = new MediaFrameReader(tools, p.MediaPath, Math.Min(p.Width, width * 2), (int)(Math.Min(p.Width, width * 2) / (double)p.Width * p.Height), Math.Min(p.FrameRate, options.Fps));
        var cameraInfo = p.CameraPath != null ? tools.ProbeAsync(p.CameraPath, token).GetAwaiter().GetResult() : null;
        using var camera = cameraInfo != null ? new MediaFrameReader(tools, p.CameraPath!, Math.Min(cameraInfo.Width, width), Math.Min(cameraInfo.Width, width) * cameraInfo.Height / cameraInfo.Width, Math.Min(cameraInfo.FrameRate, options.Fps)) : null;
        var bgInfo = p.Style.BackgroundKind == "Video" && p.Style.BackgroundPath != null ? tools.ProbeAsync(p.Style.BackgroundPath, token).GetAwaiter().GetResult() : null;
        using var background = bgInfo != null ? new MediaFrameReader(tools, p.Style.BackgroundPath!, width, height, Math.Min(bgInfo.FrameRate, options.Fps)) : null;
        async Task<DecodedFrame> Decode(int frame)
        {
            var watch = Stopwatch.StartNew(); double time = Math.Min(frame / (double)options.Fps, p.EditedDuration - .0001);
            var state = SceneEvaluator.Evaluate(p, time);
            var source = screen.GetFrameAsync(state.SourceTime, ct);
            var cam = camera != null && state.SourceTime >= p.CameraOffset ? camera.GetFrameAsync(Math.Max(0, Math.Min(cameraInfo!.Duration - .02, state.SourceTime - p.CameraOffset)), ct) : Task.FromResult<BitmapSource>(null!);
            var bg = background != null ? background.GetFrameAsync(time % bgInfo!.Duration, ct) : Task.FromResult<BitmapSource>(null!);
            await Task.WhenAll(source, cam, bg).ConfigureAwait(false);
            return new(time, source.Result, cam.Result, bg.Result, watch.Elapsed.TotalMilliseconds);
        }
        try
        {
            var info = tools.ProbeAsync(p.MediaPath, token).GetAwaiter().GetResult();
            bool hasAudio = !p.Style.Mute && (info.HasAudio || p.SystemAudioPath != null || p.MicrophonePath != null);
            if (hasAudio) tools.ExportAsync(p, audio, new(AudioOnly: true), null, token).GetAwaiter().GetResult();
            var args = new List<string> { "-hide_banner", "-y", "-f", "rawvideo", "-pix_fmt", "bgra", "-s", $"{width}x{height}", "-r", options.Fps.ToString(), "-i", "pipe:0" };
            if (hasAudio) args.AddRange(["-i", audio]);
            args.AddRange(["-map", "0:v"]); args.AddRange(VideoEncoderSelector.Arguments(backend, options.Quality)); args.AddRange(["-pix_fmt", "yuv420p"]);
            if (hasAudio) args.AddRange(["-map", "1:a", "-c:a", "copy"]);
            args.AddRange(["-movflags", "+faststart", temp]);
            encoder = new() { StartInfo = MediaTools.StartInfo(tools.Ffmpeg, args) }; encoder.Start();
            var stderr = encoder.StandardError.ReadToEndAsync(); var stdout = encoder.StandardOutput.ReadToEndAsync();
            using var registration = ct.Register(() => { try { if (!encoder.HasExited) encoder.Kill(true); } catch (InvalidOperationException) { } });
            var renderer = new SceneRenderer { FfmpegPath = tools.Ffmpeg };
            // Only one decoded frame ahead and two output buffers: memory does not grow with duration.
            var buffers = new[] { new byte[checked(width * height * 4)], new byte[checked(width * height * 4)] };
            int frames = Math.Max(1, (int)Math.Ceiling(p.EditedDuration * options.Fps));
            pending = Decode(0); var progressClock = Stopwatch.StartNew();
            for (int frame = 0; frame < frames; frame++)
            {
                ct.ThrowIfCancellationRequested();
                var decoded = pending!.GetAwaiter().GetResult(); decodeMs += decoded.Milliseconds;
                pending = frame + 1 < frames ? Decode(frame + 1) : null;
                var stage = Stopwatch.StartNew();
                var rendered = renderer.Render(p, decoded.Time, decoded.Screen, decoded.Camera, decoded.Background, width, height);
                var pixels = buffers[frame % 2]; rendered.CopyPixels(pixels, width * 4, 0); renderMs += stage.Elapsed.TotalMilliseconds;
                stage.Restart();
                try { writing.GetAwaiter().GetResult(); }
                catch (Exception ex) when (!token.IsCancellationRequested) { throw new EncodingFailure(backend, "视频编码写入失败。", ex); }
                writeMs += stage.Elapsed.TotalMilliseconds;
                writing = encoder.StandardInput.BaseStream.WriteAsync(pixels.AsMemory(), ct).AsTask();
                if (frame == 0 || frame == frames - 1 || progressClock.ElapsedMilliseconds >= 100)
                { progress?.Report((frame + 1d) / frames); progressClock.Restart(); }
            }
            try { writing.GetAwaiter().GetResult(); encoder.StandardInput.Close(); }
            catch (Exception ex) when (!token.IsCancellationRequested) { throw new EncodingFailure(backend, "视频编码写入失败。", ex); }
            encoder.WaitForExit(); var error = stderr.GetAwaiter().GetResult(); stdout.GetAwaiter().GetResult(); token.ThrowIfCancellationRequested();
            if (encoder.ExitCode != 0) throw new EncodingFailure(backend, "合成导出失败：" + error);
            File.Move(temp, destination, true);
            LastProfile = new(frames, decodeMs, renderMs, writeMs, total.Elapsed.TotalMilliseconds, backend);
        }
        catch (Exception ex) when (token.IsCancellationRequested && ex is IOException or InvalidOperationException or ObjectDisposedException) { throw new OperationCanceledException(token); }
        finally
        {
            lifetime.Cancel();
            if (pending != null) { try { pending.GetAwaiter().GetResult(); } catch { } }
            try { writing.GetAwaiter().GetResult(); } catch { }
            if (encoder != null)
            {
                try { if (!encoder.HasExited) encoder.Kill(true); encoder.WaitForExit(); }
                catch (InvalidOperationException) { }
                finally { encoder.Dispose(); }
            }
            if (File.Exists(temp)) File.Delete(temp); if (File.Exists(audio)) File.Delete(audio);
        }
    }
}
