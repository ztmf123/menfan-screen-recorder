using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Menfan.Core;
using Menfan.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--export-profile") { try { ExportPerformanceProfile.Run(args[1]); return 0; } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; } }
        try { Run(); return 0; } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Run()
    {
        var root = Path.GetFullPath("artifacts/scene-tests"); Directory.CreateDirectory(root);
        var tools = new MediaTools(MediaTools.Locate() ?? throw new Exception("FFmpeg missing"));
        var source = Path.GetFullPath("artifacts/integration/test-source.mp4");
        var p = new Project { MediaPath = source, Width = 640, Height = 360, Duration = 4, CursorEmbedded = false,
            Clips = [new() { End = 2 }, new() { Start = 2, End = 4 }] };
        p.Cursor = Enumerable.Range(0, 120).Select(i => new CursorSample { Time = i / 30d, X = .2 + i / 200d, Y = .6, Click = i == 30 }).ToList();
        p.Effects = [new() { Kind = "Blur", Name = "first", Start = .5, End = 3, X = .25 }, new() { Kind = "Blur", Name = "second", Start = .5, End = 3, X = .75 }];
        TimelineMap.ReplaceClips(p, [new() { Start = 2, End = 4 }, new() { End = 2 }]);
        Check(p.Effects.Count(e => e.Name == "first") == 2 && p.Effects.Count(e => e.Name == "second") == 2, "Reorder preserves multiple simultaneous masks");
        Check(Math.Abs(TimelineMap.Locate(p, .5).SourceTime - 2.5) < .01, "Output-to-source mapping after reorder");
        Check(TimelineMap.Split(p, .7), "Split at output time");
        Check(TimelineMap.Ripple(p, 1, true), "Ripple trim after split"); p.Validate();
        var continuity = new Project { MediaPath = source, Width = 640, Height = 360, Duration = 4, Clips = [new() { End = 4 }],
            Effects = [new() { Kind = "Screen3D", Start = 0, End = 2, RotateY = 25, Zoom = 1.6 }, new() { Kind = "Screen3D", Start = 2, End = 4, RotateY = -20, Zoom = 1.2 }] };
        var before = SceneEvaluator.Evaluate(continuity, 1.9999); var after = SceneEvaluator.Evaluate(continuity, 2.0001);
        Check(Math.Abs(before.RotateY - after.RotateY) < .01 && Math.Abs(before.Zoom - after.Zoom) < .01, "Adjacent 3D animations remain continuous at their seam");
        p.Clips = [new() { End = 4 }]; p.Effects.Clear(); TimelineMap.GenerateAutoZoom(p);
        Check(p.Effects.Any(e => e.Auto), "Clicks produce timed zoom effects");
        var follow = new Project { MediaPath = source, Width = 640, Height = 360, Duration = 4, Clips = [new() { End = 4 }],
            Cursor = Enumerable.Range(0, 121).Select(i => new CursorSample { Time = i / 30d, X = .1 + .8 * i / 120, Y = .2 + .6 * i / 120, Click = false }).ToList() };
        follow.ResetVisualEdits();
        Check(!follow.Style.MouseFollow && SceneEvaluator.Evaluate(follow, 1).Zoom == 1, "Mouse follow is opt-in and new recordings remain blank");
        follow.Style.MouseFollow = true;
        var followEarly = SceneEvaluator.Evaluate(follow, .5); var followLate = SceneEvaluator.Evaluate(follow, 3);
        Check(followLate.FocusX > followEarly.FocusX + .3 && followLate.FocusY > followEarly.FocusY + .2 && followLate.Zoom == 1.6 && follow.Effects.Count == 0,
            "Mouse movement drives zoom and pan without any clicks or generated effects");
        var followRaw = SceneEvaluator.CursorAt(follow, 3);
        Check(followLate.FocusX < followRaw.X && followLate.FocusX > followRaw.X - .1, "Mouse-follow smoothing reduces abrupt camera movement");
        _ = SceneEvaluator.Evaluate(follow, .1);
        var followAfterSeek = SceneEvaluator.Evaluate(follow, 3);
        Check(followAfterSeek.FocusX == followLate.FocusX && followAfterSeek.FocusY == followLate.FocusY && followAfterSeek.Zoom == followLate.Zoom, "Mouse-follow camera is deterministic after seeking backwards");
        follow.Clips = [new() { Start = 2, End = 4, Speed = 2 }]; follow.Style.MouseFollowSmoothing = 0;
        Check(Math.Abs(SceneEvaluator.Evaluate(follow, .25).FocusX - SceneEvaluator.CursorAt(follow, 2.5).X) < .00001, "Mouse follow respects trim and playback speed");
        follow.Clips = [new() { End = 1 }]; follow.Style.MouseFollowSmoothing = .2;
        var followProject = Path.Combine(root, "mouse-follow.menfan"); follow.Save(followProject);
        Check(Project.Load(followProject).Style.MouseFollow && Project.Load(followProject).Style.MouseFollowZoom == 1.6, "Mouse follow settings survive project saving");
        var followVideo = Path.Combine(root, "mouse-follow.mp4");
        SceneExporter.ExportAsync(tools, follow, followVideo, new(360, 30), null, CancellationToken.None).GetAwaiter().GetResult();
        Check(tools.ProbeAsync(followVideo).GetAwaiter().GetResult().Duration > .9, "Click-free mouse-follow scene exports successfully");
        follow.Cursor.Clear(); Check(SceneEvaluator.Evaluate(follow, .5).Zoom == 1, "Missing cursor metadata leaves original framing unchanged");
        p.Style.BackgroundKind = "Gradient"; p.Style.Frame = "LightBrowser"; p.Style.FrameText = "menfan.app";
        p.CameraPath = source; p.Camera.Size = .25;
        p.Effects.Add(new() { Kind = "Screen3D", Start = .5, End = 3.5, RotateX = 12, RotateY = -18, RotateZ = 3, Zoom = 1.1 });
        p.Effects.Add(new() { Kind = "Blur", Start = 0, End = 4, X = .25, Y = .5, Width = .15, Height = .2 });
        p.Effects.Add(new() { Kind = "Camera", Start = .5, End = 3.5, Shape = "Landscape", X = .7, Y = .2, Width = .32 });
        var still = Path.Combine(root, "sticker.png");
        var pixels = Enumerable.Range(0, 64 * 64).SelectMany(_ => new byte[] { 30, 190, 110, 255 }).ToArray();
        var image = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, pixels, 64 * 4); Save(image, still);
        p.Effects.Add(new() { Kind = "Image", Start = .5, End = 3.5, AssetPath = still, X = .2, Y = .8, Width = .15, Height = .15, Blur = 0 });
        p.Validate();
        var renderer = new SceneRenderer { FfmpegPath = tools.Ffmpeg }; using var reader = new MediaFrameReader(tools, source, 640, 360);
        var webp = Path.Combine(root, "sticker.webp");
        MediaTools.RunAsync(tools.Ffmpeg, ["-y", "-i", still, webp]).GetAwaiter().GetResult();
        Check(renderer.Load(webp).PixelWidth == 64, "WebP asset decoding works with the configured media tools");
        var frame = reader.GetFrameAsync(1).GetAwaiter().GetResult();
        var plain = new Project { MediaPath = source, Width = 640, Height = 360, Duration = 4, CameraPath = source,
            Clips = [new() { Start = 1, End = 3, Speed = 2 }], Effects = [new() { Start = 0, End = 1, Kind = "Zoom", Auto = true }] };
        plain.Cursor.Add(new() { Time = 1, X = .5, Y = .5, Click = true });
        plain.ResetVisualEdits();
        var rawDimensions = SceneRenderer.Dimensions(plain, 0);
        Check(plain.Effects.Count == 0 && rawDimensions == (640, 360) && !plain.Camera.Visible && plain.Clips[0].Start == 1 && plain.Clips[0].Speed == 2 && plain.Cursor.Count == 1 && plain.CameraPath == source,
            "Clearing visual edits preserves source media, cursor metadata and timeline cuts");
        var untouched = renderer.Render(plain, 0, frame, frame, null, 640, 360);
        var expected = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
        var originalPixels = new byte[640 * 360 * 4]; var plainPixels = new byte[originalPixels.Length];
        expected.CopyPixels(originalPixels, 640 * 4, 0); untouched.CopyPixels(plainPixels, 640 * 4, 0);
        Check(originalPixels.SequenceEqual(plainPixels), "Blank scene renders the source unchanged with no automatic styling or camera overlay");
        var scene = renderer.Render(p, 1, frame, frame, null, 960, 540); Save(scene, Path.Combine(root, "scene-preview.png"));
        Check(scene.PixelWidth == 960 && scene.PixelHeight == 540, "Shared renderer produces scene with 3D, camera, cursor, mask and sticker");
        var renderedPixels = new byte[960 * 540 * 4]; scene.CopyPixels(renderedPixels, 960 * 4, 0);
        var live = renderer.Compose(p, 1, frame, frame, null, 960, 540, freeze: false);
        var liveVisual = new DrawingVisual(); using (var dc = liveVisual.RenderOpen()) dc.DrawImage(live, new Rect(0, 0, 960, 540));
        var liveBitmap = new RenderTargetBitmap(960, 540, 96, 96, PixelFormats.Pbgra32); liveBitmap.Render(liveVisual);
        var livePixels = new byte[renderedPixels.Length]; liveBitmap.CopyPixels(livePixels, 960 * 4, 0);
        double agreement = Enumerable.Range(0, 960 * 540).Count(i => Enumerable.Range(0, 3).All(c => Math.Abs(livePixels[i * 4 + c] - renderedPixels[i * 4 + c]) <= 15)) / (960d * 540);
        Check(agreement > .9, "Live 3D, masking and overlay geometry agrees with exported composition");
        int saturatedBlue = Enumerable.Range(0, 960 * 540).Count(i => renderedPixels[i * 4] > 220 && renderedPixels[i * 4 + 1] < 25 && renderedPixels[i * 4 + 2] < 25);
        Check(saturatedBlue > 1000, "3D plane preserves saturated source colors");
        var again = reader.GetFrameAsync(1).GetAwaiter().GetResult(); Check(ReferenceEquals(frame, again), "Repeated frame reuses cached bitmap");
        p.Clips = [new() { End = 1 }]; p.Effects = p.Effects.Where(e => e.Start < 1).Select(e => { e.End = Math.Min(e.End, 1); return e; }).ToList();
        var output = Path.Combine(root, "composed.mp4");
        SceneExporter.ExportAsync(tools, p, output, new(720, 30), null, CancellationToken.None).GetAwaiter().GetResult();
        var info = tools.ProbeAsync(output).GetAwaiter().GetResult();
        Check(info.Height == 720 && Math.Abs(info.Duration - 1) < .15 && info.HasAudio, "Scene export contains composed frames and synchronized audio");
        var protectedOutput = Path.Combine(root, "cancelled.mp4"); File.WriteAllText(protectedOutput, "preserve-existing");
        using (var cancellation = new CancellationTokenSource())
        {
            var progress = new CancelProgress(cancellation);
            try { SceneExporter.ExportAsync(tools, p, protectedOutput, new(720, 30), progress, cancellation.Token).GetAwaiter().GetResult(); throw new Exception("Cancellation ignored"); }
            catch (OperationCanceledException) { Check(File.ReadAllText(protectedOutput) == "preserve-existing", "Mid-export cancellation preserves the existing file"); }
        }
        var roundtrip = Path.Combine(root, "project.menfan"); p.Save(roundtrip); var loaded = Project.Load(roundtrip);
        Check(loaded.Effects.Count == p.Effects.Count && loaded.CameraPath == p.CameraPath && !loaded.CursorEmbedded, "Version 2 project roundtrip retains scene and separate cursor");
        p.Style.Intro = true; p.Style.IntroStyle = "Bouncy";
        Save(renderer.Render(p, .2, frame, frame, null, 960, 540), Path.Combine(root, "bouncy-intro.png"));
        Check(double.IsFinite(SceneEvaluator.Ease(.25, "Bouncy")), "Bouncy intro renders without invalid opacity");
        var highResolution = new Project { MediaPath = source, Width = 640, Height = 360, Duration = 4, Clips = [new() { End = .15 }] };
        highResolution.Style.Aspect = "16:9";
        var highOutput = Path.Combine(root, "4k60.mp4");
        SceneExporter.ExportAsync(tools, highResolution, highOutput, new(2160, 60), null, CancellationToken.None).GetAwaiter().GetResult();
        var highInfo = tools.ProbeAsync(highOutput).GetAwaiter().GetResult();
        Check(highInfo.Width == 3840 && highInfo.Height == 2160 && Math.Abs(highInfo.FrameRate - 60) < .01, "Real 3840x2160 output is encoded at 60fps");
        Console.WriteLine("All scene checks passed.");
    }
    private static void Save(BitmapSource image, string path) { using var stream = File.Create(path); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); encoder.Save(stream); }
    private static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL " + name); Console.WriteLine("PASS " + name); }
    private sealed class CancelProgress(CancellationTokenSource cancellation) : IProgress<double>
    { public void Report(double value) { if (value > .1) cancellation.Cancel(); } }
}
