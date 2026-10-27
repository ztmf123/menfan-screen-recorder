using System.Diagnostics;
using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace Menfan.App;

internal sealed class AudioRecorder : IDisposable
{
    private readonly WasapiCapture capture;
    private readonly WaveFileWriter writer;
    private readonly Stopwatch clock = new();
    private readonly object gate = new();
    private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool started;
    private readonly MMDevice? device;
    public AudioRecorder(string path, bool loopback, string? deviceId = null)
    {
        if (deviceId != null) { using var enumerator = new MMDeviceEnumerator(); device = enumerator.GetDevice(deviceId); }
        capture = loopback ? device == null ? new WasapiLoopbackCapture() : new WasapiLoopbackCapture(device)
            : device == null ? new WasapiCapture() : new WasapiCapture(device);
        writer = new WaveFileWriter(path, capture.WaveFormat);
        capture.DataAvailable += (_, e) => {
            lock (gate)
            {
                // WASAPI loopback emits no packets during silence. Preserve wall-clock gaps.
                long expectedStart = (long)((clock.Elapsed.TotalSeconds * writer.WaveFormat.AverageBytesPerSecond) - e.BytesRecorded);
                PadTo(expectedStart);
                writer.Write(e.Buffer, 0, e.BytesRecorded);
            }
        };
        capture.RecordingStopped += (_, e) => {
            if (e.Exception != null) stopped.TrySetException(e.Exception);
            else stopped.TrySetResult();
        };
    }
    public void Start() { clock.Start(); capture.StartRecording(); started = true; }
    public async Task StopAsync()
    {
        if (!started) return;
        capture.StopRecording();
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10));
        lock (gate)
        {
            PadTo((long)(clock.Elapsed.TotalSeconds * writer.WaveFormat.AverageBytesPerSecond));
            writer.Flush();
        }
        started = false;
    }
    private void PadTo(long length)
    {
        length -= length % writer.WaveFormat.BlockAlign;
        var zeros = new byte[8192 - 8192 % writer.WaveFormat.BlockAlign];
        while (writer.Length < length)
            writer.Write(zeros, 0, (int)Math.Min(zeros.Length, length - writer.Length));
    }
    public void Dispose() { capture.Dispose(); writer.Dispose(); device?.Dispose(); }
}
