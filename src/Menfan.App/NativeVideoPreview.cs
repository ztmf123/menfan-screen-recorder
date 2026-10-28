using System.Windows.Media;

namespace Menfan.App;

internal sealed class NativeVideoPreview : IDisposable
{
    private readonly MediaPlayer player = new() { IsMuted = true, Volume = 0, ScrubbingEnabled = true };
    private string? path;
    private Task<bool>? opening;
    private bool ready, running;
    private int clip = -1;
    private ImageSource? image;
    private double pausedTime = double.NaN;
    private readonly System.Windows.Threading.DispatcherTimer scrubTimer = new() { Interval = TimeSpan.FromMilliseconds(60) };
    public bool Ready => ready;
    public NativeVideoPreview()
    {
        player.MediaFailed += (_, _) => { ready = false; running = false; };
        scrubTimer.Tick += (_, _) => { scrubTimer.Stop(); if (!running) { player.Pause(); player.Position = TimeSpan.FromSeconds(pausedTime); } };
    }
    public async Task<bool> OpenAsync(string source)
    {
        if (path == source && opening != null) return await opening;
        player.Close(); path = source; ready = false; running = false; clip = -1; image = null; pausedTime = double.NaN;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler opened = (_, _) => completion.TrySetResult(player.NaturalVideoWidth > 0 && player.NaturalVideoHeight > 0);
        EventHandler<ExceptionEventArgs> failed = (_, _) => { ready = false; completion.TrySetResult(false); };
        player.MediaOpened += opened; player.MediaFailed += failed;
        opening = completion.Task;
        try
        {
            player.Open(new Uri(source));
            ready = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (ready) { player.Play(); await Task.Delay(40); player.Pause(); }
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or NotSupportedException) { ready = false; }
        finally { player.MediaOpened -= opened; player.MediaFailed -= failed; }
        opening = Task.FromResult(ready);
        return ready;
    }
    public bool Matches(int width, int height) => ready && player.NaturalVideoWidth == width && player.NaturalVideoHeight == height;
    public ImageSource Frame(Menfan.Core.Project p, double outputTime, bool play = true)
    {
        var position = Menfan.Core.TimelineMap.Locate(p, outputTime);
        double drift = Math.Abs(player.Position.TotalSeconds - position.SourceTime);
        bool seeking = !play && (double.IsNaN(pausedTime) || Math.Abs(pausedTime - position.SourceTime) > .00001);
        if (seeking ||
            play && (clip != position.Index && drift > .08 || drift > .5))
            player.Position = TimeSpan.FromSeconds(position.SourceTime);
        if (player.SpeedRatio != p.Clips[position.Index].Speed) player.SpeedRatio = p.Clips[position.Index].Speed;
        if (play) scrubTimer.Stop();
        if (play && !running) { player.Play(); running = true; }
        else if (!play)
        {
            running = false; pausedTime = position.SourceTime;
            if (seeking) { player.Play(); scrubTimer.Stop(); scrubTimer.Start(); }
        }
        if (play) pausedTime = double.NaN;
        clip = position.Index;
        return image ??= new DrawingImage(new VideoDrawing { Player = player, Rect = new System.Windows.Rect(0, 0, p.Width, p.Height) });
    }
    public void Pause() { scrubTimer.Stop(); player.Pause(); running = false; }
    public void Dispose() { scrubTimer.Stop(); player.Close(); ready = false; running = false; path = null; opening = null; image = null; pausedTime = double.NaN; }
}
