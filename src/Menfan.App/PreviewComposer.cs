using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Menfan.App;

internal sealed class PreviewComposer : IDisposable
{
    private readonly BlockingCollection<Action> jobs = new();
    private readonly SceneRenderer renderer = new();
    public string? FfmpegPath { get; set; }
    public PreviewComposer()
    {
        var worker = new Thread(() => { foreach (var job in jobs.GetConsumingEnumerable()) job(); }) { IsBackground = true, Name = "Menfan preview composer" };
        worker.SetApartmentState(ApartmentState.STA); worker.Start();
    }
    public Task<ImageSource> ComposeAsync(Menfan.Core.Project snapshot, double time, BitmapSource screen, BitmapSource? camera, BitmapSource? background, int width, int height, CancellationToken token)
    {
        var completion = new TaskCompletionSource<ImageSource>(TaskCreationOptions.RunContinuationsAsynchronously);
        string? executable = FfmpegPath;
        Action job = () => {
            try
            {
                token.ThrowIfCancellationRequested(); renderer.FfmpegPath = executable;
                var result = renderer.Compose(snapshot, time, screen, camera, background, width, height);
                token.ThrowIfCancellationRequested(); completion.TrySetResult(result);
            }
            catch (OperationCanceledException) { completion.TrySetCanceled(token); }
            catch (Exception ex) { completion.TrySetException(ex); }
        };
        try { jobs.Add(job, token); }
        catch (InvalidOperationException) { completion.TrySetCanceled(); }
        catch (OperationCanceledException) { completion.TrySetCanceled(token); }
        return completion.Task;
    }
    public void Dispose() => jobs.CompleteAdding();
}
