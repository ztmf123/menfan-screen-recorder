using System.Collections.Concurrent;
using Menfan.Core;

namespace Menfan.App;

internal static class VideoEncoderSelector
{
    private static readonly ConcurrentDictionary<string, Task<string>> probes = new();
    public static string Select(MediaTools tools, ExportOptions options, int width, int height, CancellationToken token)
    {
        if (options.Encoder == "Software") return "libx264";
        if (options.Encoder != "Auto") throw new InvalidDataException("未知编码模式。");
        string key = $"{tools.Ffmpeg}|{width}|{height}|{options.Fps}";
        return probes.GetOrAdd(key, _ => ProbeAsync(tools, options, width, height)).WaitAsync(token).GetAwaiter().GetResult();
    }
    private static async Task<string> ProbeAsync(MediaTools tools, ExportOptions options, int width, int height)
    {
        foreach (var name in new[] { "h264_nvenc", "h264_qsv", "h264_amf" })
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                await MediaTools.RunAsync(tools.Ffmpeg, new[] { "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", $"color=size={width}x{height}:rate={options.Fps}", "-frames:v", "1" }
                    .Concat(Arguments(name, options.Quality)).Concat(new[] { "-pix_fmt", "yuv420p", "-f", "null", "-" }), timeout.Token);
                return name;
            }
            catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException or IOException) { }
        }
        return "libx264";
    }
    public static string[] Arguments(string name, int quality) => name switch {
        "h264_nvenc" => ["-c:v", name, "-preset", "p4", "-rc", "vbr", "-cq", quality.ToString(), "-b:v", "0"],
        "h264_qsv" => ["-c:v", name, "-preset", "veryfast", "-global_quality", quality.ToString()],
        "h264_amf" => ["-c:v", name, "-usage", "transcoding", "-quality", "speed", "-rc", "cqp", "-qp_i", quality.ToString(), "-qp_p", quality.ToString()],
        _ => ["-c:v", "libx264", "-preset", "fast", "-crf", quality.ToString()]
    };
}
