using System.Diagnostics;
using System.IO.Pipes;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Menfan.Core;

namespace Menfan.App;

internal sealed class MediaFrameReader : IDisposable
{
    private readonly MediaTools tools;
    private readonly string path;
    private readonly int width, height;
    private readonly SemaphoreSlim gate = new(1);
    private Process? process;
    private NamedPipeServerStream? framePipe;
    private Task<string>? errors;
    private double start;
    private int index = -1;
    private BitmapSource? last;
    private byte[]? pixels;
    private readonly double fps;
    private readonly CancellationTokenSource lifetime = new();
    private readonly object processGate = new();
    public MediaFrameReader(MediaTools tools, string path, int width, int height, double fps = 60)
    { this.tools = tools; this.path = path; this.fps = Math.Clamp(fps, 1, 240); this.width = Math.Max(2, width / 2 * 2); this.height = Math.Max(2, height / 2 * 2); }
    public Task<BitmapSource> GetFrameAsync(double time, CancellationToken ct = default, bool exact = false) => Task.Run(async () => {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
        return await ReadFrameAsync(time, linked.Token, exact).ConfigureAwait(false);
    }, ct);
    private async Task<BitmapSource> ReadFrameAsync(double time, CancellationToken ct, bool exact)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            time = Math.Max(0, time);
            int wanted = (int)Math.Floor((time - start) * fps + .001);
            if (process == null || wanted < index || wanted - index > fps * 2 || exact && Math.Abs(time - (start + index / fps)) > .0005)
            {
                lock (processGate)
                {
                    ct.ThrowIfCancellationRequested(); Stop(); start = time; index = -1; wanted = 0;
                    string pipeName = "menfan-frames-" + Guid.NewGuid().ToString("N");
                    framePipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 4 * 1024 * 1024, 0);
                    process = new() { StartInfo = MediaTools.StartInfo(tools.Ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-threads", "4", "-filter_threads", "2", "-ss", MediaTools.N(start), "-i", path,
                        "-an", "-vf", $"fps={MediaTools.N(fps)},scale={width}:{height}:flags=bilinear", "-pix_fmt", "bgra", "-f", "rawvideo", "-y", @"\\.\pipe\" + pipeName]) };
                    process.Start(); errors = process.StandardError.ReadToEndAsync();
                    _ = process.StandardOutput.ReadToEndAsync();
                }
            }
            if (index == wanted && last != null) return last;
            var bytes = pixels ??= new byte[width * height * 4];
            var stream = framePipe!;
            if (!stream.IsConnected)
            {
                var connected = stream.WaitForConnectionAsync(ct);
                var exited = process!.WaitForExitAsync(ct);
                if (await Task.WhenAny(connected, exited).ConfigureAwait(false) == exited && !stream.IsConnected)
                    throw new InvalidOperationException("无法打开视频解码管道：" + (errors != null ? await errors.ConfigureAwait(false) : path));
                await connected.WaitAsync(TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
            }
            while (index < wanted)
            {
                int read = 0;
                while (read < bytes.Length)
                {
                    int count = await stream.ReadAsync(bytes.AsMemory(read), ct).ConfigureAwait(false);
                    if (count == 0)
                    {
                        if (last != null) return last;
                        throw new InvalidOperationException("无法读取视频帧：" + (errors != null ? await errors.ConfigureAwait(false) : path));
                    }
                    read += count;
                }
                index++;
            }
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bytes, width * 4);
            bitmap.Freeze(); last = bitmap; return bitmap;
        }
        catch (Exception ex) when (ct.IsCancellationRequested && ex is IOException or ObjectDisposedException or InvalidOperationException) { throw new OperationCanceledException(ct); }
        finally { gate.Release(); }
    }
    private void Stop()
    {
        lock (processGate)
        {
            try { if (process is { HasExited: false }) process.Kill(true); } catch (InvalidOperationException) { }
            framePipe?.Dispose(); framePipe = null;
            process?.Dispose(); process = null;
        }
    }
    public void Dispose() { lifetime.Cancel(); Stop(); }
}
