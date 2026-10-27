using Menfan.Core;

var path = args.FirstOrDefault() ?? MediaTools.Locate() ?? throw new Exception("Pass ffmpeg.exe path.");
var tools = new MediaTools(path);
var root = Path.GetFullPath(Path.Combine("artifacts", "integration"));
Directory.CreateDirectory(root);
var source = Path.Combine(root, "test-source.mp4");
await MediaTools.RunAsync(tools.Ffmpeg, ["-y", "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=30:duration=4", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=4", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", source]);
var info = await tools.ProbeAsync(source);
Assert(info.Width == 640 && info.Height == 360 && Math.Abs(info.Duration - 4) < .2 && info.HasAudio, "Source probe");
var project = new Project { MediaPath = source, Width = info.Width, Height = info.Height, Duration = info.Duration,
    Clips = [new() { Start = 2, End = 4, Speed = 2 }, new() { Start = 0, End = 1, Speed = .5 }] };
project.Cursor = Enumerable.Range(0, 240).Select(i => new CursorSample { Time = i / 60d, X = .2 + .6 * i / 240, Y = .5, Click = i == 30 }).ToList();
var projectPath = Path.Combine(root, "roundtrip.menfan"); project.Save(projectPath);
var loaded = Project.Load(projectPath);
Assert(loaded.MediaPath == project.MediaPath && loaded.Clips.Count == 2 && loaded.Cursor.Count == 240, "Project roundtrip with relative references");
var output = Path.Combine(root, "styled.mp4");
await tools.ExportAsync(project, output, new(720, 30));
var rendered = await tools.ProbeAsync(output);
Assert(rendered.Height == 720 && Math.Abs(rendered.Duration - 3) < .2 && rendered.HasAudio, "Trim/reorder/speed/style/audio export");
await MediaTools.RunAsync(tools.Ffmpeg, ["-y", "-ss", "0.5", "-i", output, "-frames:v", "1", Path.Combine(root, "styled-preview.png")]);
project.Style.Zoom = 1.5; project.Style.FollowCursor = true;
await tools.ExportAsync(project, Path.Combine(root, "follow.mp4"), new(720, 60));
Assert((await tools.ProbeAsync(Path.Combine(root, "follow.mp4"))).Height == 720, "Cursor follow export at 60fps");
await tools.ExportAsync(project, Path.Combine(root, "audio.m4a"), new(AudioOnly: true));
Console.WriteLine("PASS Audio-only M4A export");
project.Style.Mute = true;
await tools.ExportAsync(project, Path.Combine(root, "silent.mp4"), new(720));
Assert(!(await tools.ProbeAsync(Path.Combine(root, "silent.mp4"))).HasAudio, "Mute export");
try { await tools.ExportAsync(project, source, new(720)); throw new Exception("Source overwrite was accepted"); }
catch (InvalidOperationException e) when (e.Message.Contains("覆盖原始")) { Console.WriteLine("PASS Source overwrite protection"); }
var cancelOutput = Path.Combine(root, "cancelled.mp4");
await File.WriteAllTextAsync(cancelOutput, "existing file must survive");
using var cancel = new CancellationTokenSource(); cancel.Cancel();
try { await tools.ExportAsync(project, cancelOutput, new(720), ct: cancel.Token); throw new Exception("Cancellation ignored"); }
catch (OperationCanceledException) { Assert(await File.ReadAllTextAsync(cancelOutput) == "existing file must survive", "Cancelled export preserves existing file"); }
// Exercise external audio mixing and offset with audio shorter than the edited source.
var wav = Path.Combine(root, "microphone.wav");
await MediaTools.RunAsync(tools.Ffmpeg, ["-y", "-f", "lavfi", "-i", "sine=frequency=880:sample_rate=48000:duration=1", wav]);
project.Style.Mute = false; project.MicrophonePath = wav; project.MicrophoneOffset = .2;
await tools.ExportAsync(project, Path.Combine(root, "mixed.mp4"), new(720));
Assert(Math.Abs((await tools.ProbeAsync(Path.Combine(root, "mixed.mp4"))).Duration - 3) < .2, "Short external audio is padded and mixed");
Console.WriteLine("All integration checks passed.");

static void Assert(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL " + name);
    Console.WriteLine("PASS " + name);
}
