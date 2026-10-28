using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Menfan.Core;

public sealed record MediaInfo(int Width, int Height, double Duration, bool HasAudio, double FrameRate = 30);
public sealed record ExportOptions(int Height = 1080, int Fps = 30, int Quality = 20, bool AudioOnly = false, string Encoder = "Software");

public sealed class MediaTools
{
    public string Ffmpeg { get; }
    public string Ffprobe { get; }
    public MediaTools(string ffmpeg)
    {
        Ffmpeg = Path.GetFullPath(ffmpeg);
        Ffprobe = Path.Combine(Path.GetDirectoryName(Ffmpeg)!, "ffprobe.exe");
    }

    public static string? Locate()
    {
        var configured = Environment.GetEnvironmentVariable("MENFAN_FFMPEG");
        var candidates = new[] { configured, Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe"),
            Path.Combine(Environment.CurrentDirectory, ".tools", "ffmpeg", "bin", "ffmpeg.exe") };
        foreach (var path in candidates)
            if (path != null && File.Exists(path) && File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "ffprobe.exe"))) return path;
        // Development and local portable builds may be launched from a different working directory.
        var ancestor = new DirectoryInfo(AppContext.BaseDirectory);
        for (int level = 0; ancestor != null && level < 6; level++, ancestor = ancestor.Parent)
        {
            var path = Path.Combine(ancestor.FullName, ".tools", "ffmpeg", "bin", "ffmpeg.exe");
            if (File.Exists(path) && File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "ffprobe.exe"))) return path;
        }
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try
            {
                var path = Path.Combine(folder.Trim('"'), "ffmpeg.exe");
                if (File.Exists(path) && File.Exists(Path.Combine(folder.Trim('"'), "ffprobe.exe"))) return path;
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    public static ProcessStartInfo StartInfo(string executable, IEnumerable<string> args)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true, RedirectStandardInput = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        return info;
    }

    public static async Task<string> RunAsync(string executable, IEnumerable<string> args, CancellationToken ct = default,
        Action<string>? progress = null)
    {
        using var process = new Process { StartInfo = StartInfo(executable, args) };
        process.Start();
        var error = new StringBuilder();
        var readError = Task.Run(async () => {
            while (await process.StandardError.ReadLineAsync() is { } line)
            {
                if (error.Length > 24000) error.Remove(0, 12000);
                error.AppendLine(line);
            }
        });
        var output = new StringBuilder();
        var readOutput = Task.Run(async () => {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                output.AppendLine(line);
                progress?.Invoke(line);
            }
        });
        try { await process.WaitForExitAsync(ct); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync();
            await Task.WhenAll(readError, readOutput);
            throw;
        }
        await Task.WhenAll(readError, readOutput);
        if (process.ExitCode != 0) throw new InvalidOperationException($"媒体处理失败（{process.ExitCode}）。\n{error}");
        return output.ToString();
    }

    public async Task<MediaInfo> ProbeAsync(string path, CancellationToken ct = default)
    {
        var text = await RunAsync(Ffprobe, ["-v", "error", "-show_streams", "-show_format", "-of", "json", path], ct);
        using var doc = JsonDocument.Parse(text);
        var streams = doc.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var video = streams.FirstOrDefault(s => s.GetProperty("codec_type").GetString() == "video");
        if (video.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("素材中没有视频轨道。");
        var duration = double.Parse(doc.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
        double rate = 30;
        if (video.TryGetProperty("avg_frame_rate", out var rateText))
        {
            var parts = (rateText.GetString() ?? "").Split('/');
            if (parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) && denominator > 0 && numerator > 0) rate = numerator / denominator;
        }
        return new(video.GetProperty("width").GetInt32(), video.GetProperty("height").GetInt32(), duration,
            streams.Any(s => s.GetProperty("codec_type").GetString() == "audio"), rate);
    }

    public async Task ExportAsync(Project project, string destination, ExportOptions options,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        project.Validate();
        if (options.Height is not (0 or 720 or 1080 or 1440 or 2160) || options.Fps is not (30 or 60)
            || options.Quality is < 0 or > 51) throw new ArgumentException("不支持的导出参数。");
        var fullDestination = Path.GetFullPath(destination);
        foreach (var source in new[] { project.MediaPath, project.SystemAudioPath, project.MicrophonePath })
            if (source != null && string.Equals(Path.GetFullPath(source), fullDestination, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("导出文件不能覆盖原始素材。");
        var media = await ProbeAsync(project.MediaPath, ct);
        var args = new List<string> { "-hide_banner", "-y", "-i", project.MediaPath };
        var audioSources = new List<(int Index, double Offset, double Volume)>();
        if (media.HasAudio) audioSources.Add((0, 0, project.SystemVolume));
        int index = 1;
        foreach (var item in new[] { (project.SystemAudioPath, project.SystemAudioOffset, project.SystemVolume), (project.MicrophonePath, project.MicrophoneOffset, project.MicrophoneVolume) })
            if (item.Item1 != null) { args.AddRange(["-i", item.Item1]); audioSources.Add((index++, item.Item2, item.Item3)); }
        bool audio = audioSources.Count > 0 && !project.Style.Mute;
        if (options.AudioOnly && !audio) throw new InvalidOperationException("项目没有可导出的声音，或已静音。");
        var filters = new StringBuilder();
        var style = project.Style;
        for (int i = 0; i < project.Clips.Count; i++)
        {
            var clip = project.Clips[i];
            if (!options.AudioOnly)
            {
                filters.Append($"[0:v]trim=start={N(clip.Start)}:end={N(clip.End)},setpts=(PTS-STARTPTS)/{N(clip.Speed)}");
                if (style.Zoom > 1.001)
                {
                    var cw = Math.Max(2, (int)(project.Width / style.Zoom) / 2 * 2);
                    var ch = Math.Max(2, (int)(project.Height / style.Zoom) / 2 * 2);
                    var xs = $"(iw-ow)/2"; var ys = "(ih-oh)/2";
                    if (style.FollowCursor && project.Cursor.Count > 0)
                    {
                        xs = CursorExpression(project, clip, true, cw);
                        ys = CursorExpression(project, clip, false, ch);
                    }
                    filters.Append($",crop={cw}:{ch}:x='{xs}':y='{ys}',scale={project.Width}:{project.Height}");
                }
                if (style.ClickHighlight)
                {
                    // A short rectangular halo is drawn at recorded click coordinates before padding.
                    foreach (var sample in project.Cursor.Where(s => s.Click && s.Time >= clip.Start && s.Time < clip.End).Take(150))
                    {
                        var t = (sample.Time - clip.Start) / clip.Speed;
                        // The cursor is embedded in capture; halos at zoomed coordinates are deferred.
                        if (style.Zoom <= 1.001)
                            filters.Append($",drawbox=x={N(sample.X * project.Width - 16)}:y={N(sample.Y * project.Height - 16)}:w=32:h=32:color=0x70E4CA@0.8:t=3:enable='between(t,{N(t)},{N(t + .25)})'");
                    }
                }
                var pad = style.Padding / 2 * 2;
                // Round source corners against the same solid background as the outer canvas.
                if (style.CornerRadius > 0)
                {
                    int radius = Math.Min(style.CornerRadius, Math.Min(project.Width, project.Height) / 2);
                    var hex = style.Background[1..];
                    int red = Convert.ToInt32(hex[..2], 16), green = Convert.ToInt32(hex.Substring(2, 2), 16), blue = Convert.ToInt32(hex.Substring(4, 2), 16);
                    string distance = $"pow(max({radius}-X,max(X-(W-1-{radius}),0)),2)+pow(max({radius}-Y,max(Y-(H-1-{radius}),0)),2)";
                    filters.Append($",format=gbrp,geq=r='if(lte({distance},{radius * radius}),r(X,Y),{red})':g='if(lte({distance},{radius * radius}),g(X,Y),{green})':b='if(lte({distance},{radius * radius}),b(X,Y),{blue})'");
                }
                filters.Append($",pad=iw+{pad * 2}:ih+{pad * 2}:{pad}:{pad}:color=0x{style.Background[1..]}");
                if (options.Height > 0) filters.Append($",scale=-2:{options.Height}:flags=lanczos");
                else filters.Append(",scale=trunc(iw/2)*2:trunc(ih/2)*2");
                filters.Append($",setsar=1,fps={options.Fps},format=yuv420p[v{i}];");
            }
            if (audio)
            {
                for (int a = 0; a < audioSources.Count; a++)
                {
                    var source = audioSources[a];
                    var offset = source.Offset;
                    filters.Append($"[{source.Index}:a]aresample=48000,asetpts=PTS-STARTPTS,volume={N(source.Volume)}");
                    if (offset > 0) filters.Append($",adelay={N(offset * 1000)}:all=1");
                    else if (offset < 0) filters.Append($",atrim=start={N(-offset)},asetpts=PTS-STARTPTS");
                    filters.Append($",apad,atrim=start={N(clip.Start)}:end={N(clip.End)},asetpts=PTS-STARTPTS,{Tempo(clip.Speed)}[a{i}_{a}];");
                }
                for (int a = 0; a < audioSources.Count; a++) filters.Append($"[a{i}_{a}]");
                filters.Append($"amix=inputs={audioSources.Count}:duration=longest:normalize=0,alimiter=limit=0.95[a{i}];");
            }
        }
        for (int i = 0; i < project.Clips.Count; i++)
        {
            if (!options.AudioOnly) filters.Append($"[v{i}]");
            if (audio) filters.Append($"[a{i}]");
        }
        filters.Append($"concat=n={project.Clips.Count}:v={(options.AudioOnly ? 0 : 1)}:a={(audio ? 1 : 0)}");
        if (!options.AudioOnly) filters.Append("[video]");
        if (audio) filters.Append("[audio]");
        var folder = Path.GetDirectoryName(fullDestination)!;
        Directory.CreateDirectory(folder);
        var filterFile = Path.Combine(folder, $".menfan-{Guid.NewGuid():N}.filter");
        var outputFile = Path.Combine(folder, $".menfan-{Guid.NewGuid():N}{(options.AudioOnly ? ".m4a" : ".mp4")}");
        try
        {
            await File.WriteAllTextAsync(filterFile, filters.ToString(), ct);
            args.AddRange(["-/filter_complex", filterFile]);
            if (!options.AudioOnly) args.AddRange(["-map", "[video]", "-c:v", "libx264", "-preset", "fast", "-crf", options.Quality.ToString()]);
            if (audio) args.AddRange(["-map", "[audio]", "-c:a", "aac", "-b:a", "192k"]);
            args.AddRange(["-movflags", "+faststart", "-progress", "pipe:1", "-nostats", outputFile]);
            await RunAsync(Ffmpeg, args, ct, line => {
                if (line.StartsWith("out_time_us=") && long.TryParse(line[12..], out var us))
                    progress?.Report(Math.Clamp(us / 1_000_000d / project.EditedDuration, 0, 1));
            });
            File.Move(outputFile, fullDestination, true);
            progress?.Report(1);
        }
        finally
        {
            File.Delete(filterFile);
            if (File.Exists(outputFile)) File.Delete(outputFile);
        }
    }

    private static string CursorExpression(Project project, Clip clip, bool horizontal, int cropSize)
    {
        var bucketSize = Math.Max(.5, clip.Duration / 240);
        var samples = project.Cursor.Where(s => s.Time >= clip.Start && s.Time < clip.End)
            .GroupBy(s => (int)((s.Time - clip.Start) / clip.Speed / bucketSize)).Select(g => g.Last()).ToList();
        if (samples.Count == 0) return horizontal ? "(iw-ow)/2" : "(ih-oh)/2";
        var dimension = horizontal ? project.Width : project.Height;
        double Position(CursorSample sample) => Math.Clamp((horizontal ? sample.X : sample.Y) * dimension - cropSize / 2d, 0, dimension - cropSize);
        // A balanced expression avoids FFmpeg parser nesting limits on long recordings.
        string Build(int first, int last)
        {
            if (first == last)
            {
                if (first == samples.Count - 1) return N(Position(samples[first]));
                double t0 = (samples[first].Time - clip.Start) / clip.Speed;
                double t1 = (samples[first + 1].Time - clip.Start) / clip.Speed;
                double p0 = Position(samples[first]), p1 = Position(samples[first + 1]);
                return $"{N(p0)}+({N(p1 - p0)})*clip((t-{N(t0)})/{N(t1 - t0)},0,1)";
            }
            int middle = (first + last) / 2;
            double boundary = (samples[middle + 1].Time - clip.Start) / clip.Speed;
            return $"if(lt(t,{N(boundary)}),{Build(first, middle)},{Build(middle + 1, last)})";
        }
        return Build(0, samples.Count - 1);
    }

    private static string Tempo(double speed)
    {
        var values = new List<string>();
        while (speed > 2) { values.Add("atempo=2"); speed /= 2; }
        while (speed < .5) { values.Add("atempo=0.5"); speed /= .5; }
        values.Add("atempo=" + N(speed));
        return string.Join(',', values);
    }
    public static string N(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
