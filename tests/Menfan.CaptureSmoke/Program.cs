using Menfan.App;
using Menfan.Core;
using System.IO;

var path = args.FirstOrDefault() ?? MediaTools.Locate() ?? throw new Exception("FFmpeg not found");
if (args.Contains("--hidden-panels")) { Environment.ExitCode = HiddenPanelCaptureCheck.Run(path); return; }
if (args.Contains("--startup-profile"))
{
    Environment.ExitCode = StartupProfile.Run(path, args.Contains("--baseline") ? "baseline" : args.Contains("--large") ? "optimized-large" : "optimized", args.Contains("--assert-responsive"), args.Contains("--large"));
    return;
}
var folder = Path.GetFullPath(Path.Combine("artifacts", "capture-smoke", DateTime.Now.ToString("yyyyMMdd-HHmmss")));
var tools = new MediaTools(path);
using var recorder = new Recorder();
bool audio = args.Contains("--audio");
await recorder.StartAsync(tools, new CaptureTarget("Smoke-test region", 0, 0, 640, 360), 30, audio, false, folder);
await Task.Delay(3000);
if (args.Contains("--pause"))
{
    await recorder.PauseAsync();
    var elapsed = recorder.Elapsed;
    await Task.Delay(1600);
    if (Math.Abs(recorder.Elapsed - elapsed) > .05 || !recorder.IsPaused) throw new Exception("Pause clock continued.");
    await recorder.ResumeAsync();
    await Task.Delay(1800);
    Console.WriteLine("PASS Pause excludes wall-clock delay and resumes capture.");
}
recorder.AddMarker();
var project = await recorder.StopAsync(tools);
if (project.Duration < 2 || project.Cursor.Count == 0 || project.Markers.Count != 1)
    throw new Exception("Capture did not produce duration, cursor data and a marker.");
if (project.CursorEmbedded) throw new Exception("New capture should not burn in cursor.");
if (project.Effects.Count != 0 || project.Style.Padding != 0 || project.Style.CornerRadius != 0 || project.Style.Zoom != 1 || project.Style.ClickHighlight || project.Style.CursorSize != 1 || project.Camera.Visible)
    throw new Exception("New capture should start without automatic visual edits.");
var savedProject = Project.Load(Path.Combine(folder, "project.menfan"));
if (savedProject.Effects.Count != 0 || savedProject.Style.Padding != 0 || savedProject.Style.ClickHighlight) throw new Exception("Saved capture added visual effects.");
Console.WriteLine("PASS Capture and saved project start with an empty effect timeline and original framing.");
if (project.Cursor.Any(c => c.ShapePath != null && !File.Exists(c.ShapePath))) throw new Exception("Cursor shape file is missing.");
Console.WriteLine($"PASS Separate cursor track: {project.Cursor.Count(c => c.ShapePath != null)} samples reference cursor assets.");
if (project.SystemAudioPath != null)
{
    var text = await MediaTools.RunAsync(tools.Ffprobe, ["-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1", project.SystemAudioPath]);
    var seconds = double.Parse(text.Trim(), System.Globalization.CultureInfo.InvariantCulture);
    if (Math.Abs(seconds - project.Duration) > .12) throw new Exception("Merged audio and video durations diverged after pause.");
    Console.WriteLine("PASS Merged system audio duration matches paused video.");
}
await SceneExporter.ExportAsync(tools, project, Path.Combine(folder, "recording.mp4"), new ExportOptions(720), null, CancellationToken.None);
Console.WriteLine($"PASS Real desktop capture: {project.Width}x{project.Height}, {project.Duration:0.00}s, {project.Cursor.Count} cursor samples, {project.Markers.Count} marker.");
Console.WriteLine("Project: " + folder);
