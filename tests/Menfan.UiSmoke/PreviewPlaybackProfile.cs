using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Menfan.App;
using Menfan.Core;

internal static class PreviewPlaybackProfile
{
    public static int Run(bool baseline, bool fourK = false, bool effects = false, bool mkv = false)
    {
        int result = 0; var app = new App(); app.InitializeComponent();
        app.Startup += (_, _) => app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () => {
            var heartbeat = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromMilliseconds(16) };
            try
            {
                var editor = app.Windows.OfType<Menfan.App.MainWindow>().Single();
                editor.KeepPreviewPlayingForDiagnostics = true; editor.Topmost = true;
                var tools = new MediaTools(MediaTools.Locate() ?? throw new Exception("FFmpeg missing"));
                var folder = Path.GetFullPath("artifacts/preview-profile"); Directory.CreateDirectory(folder);
                int width = fourK ? 3840 : 2560, height = fourK ? 2160 : 1440;
                var path = Path.Combine(folder, fourK ? "native-2160p60.mp4" : "native-1440p60.mp4");
                if (!File.Exists(path)) await MediaTools.RunAsync(tools.Ffmpeg, ["-y", "-f", "lavfi", "-i", $"testsrc2=size={width}x{height}:rate=60", "-t", "4", "-c:v", "libx264", "-threads", "4", "-preset", "ultrafast", "-crf", "22", "-pix_fmt", "yuv420p", path]);
                if (mkv) { string remux = Path.ChangeExtension(path, ".mkv"); if (!File.Exists(remux)) await MediaTools.RunAsync(tools.Ffmpeg, ["-y", "-i", path, "-c", "copy", remux]); path = remux; }
                ((ComboBox)editor.FindName("QualityPicker")).SelectedIndex = 1;
                var p = new Project { MediaPath = path, Width = width, Height = height, Duration = 4, FrameRate = 60, Clips = [new() { End = 4 }] };
                p.ResetVisualEdits(); p.Style.Mute = true;
                if (effects) { p.Style.CornerRadius = 18; p.Effects.Add(new() { Kind = "Screen3D", Start = 0, End = 4, RotateY = 12, Zoom = 1.1, FocusBlur = 10 }); }
                await editor.LoadForTestingAsync(p); editor.ShowEditor(); await Task.Delay(400);
                var play = (Button)editor.FindName("PlayButton");
                double previous = 0, maximumGap = 0; int ticks = 0, count = editor.PreviewPresentations;
                double decode = editor.PreviewDecodeMilliseconds, compose = editor.PreviewComposeMilliseconds;
                int rendered = 0, lastRendered = count;
                EventHandler onRendering = (_, _) => { if (editor.PreviewPresentations != lastRendered) { rendered++; lastRendered = editor.PreviewPresentations; } };
                CompositionTarget.Rendering += onRendering;
                var clock = Stopwatch.StartNew();
                heartbeat.Tick += (_, _) => { var now = clock.Elapsed.TotalMilliseconds; maximumGap = Math.Max(maximumGap, now - previous); previous = now; ticks++; };
                play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                for (int attempt = 0; attempt < 70 && !editor.IsPreviewPlaying; attempt++) await Task.Delay(100);
                if (!editor.IsPreviewPlaying) throw new Exception("Preview did not start");
                await Task.Delay(200);
                count = editor.PreviewPresentations; lastRendered = count; rendered = 0;
                decode = editor.PreviewDecodeMilliseconds; compose = editor.PreviewComposeMilliseconds;
                clock.Restart(); heartbeat.Start();
                await Task.Delay(2000); heartbeat.Stop(); CompositionTarget.Rendering -= onRendering;
                double elapsed = clock.Elapsed.TotalSeconds;
                bool wasPlaying = editor.IsPreviewPlaying;
                string suffix = effects ? "effects" : mkv ? "mkv" : fourK ? "4k" : "plain";
                editor.UpdateLayout(); var shot = new RenderTargetBitmap((int)editor.ActualWidth, (int)editor.ActualHeight, 96, 96, PixelFormats.Pbgra32); shot.Render(editor);
                using (var imageFile = File.Create(Path.Combine(folder, suffix + ".png"))) { var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(shot)); png.Save(imageFile); }
                if (wasPlaying) play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var image = ((Image)editor.FindName("ScenePreview")).Source!;
                var metrics = new { elapsed, presentations = editor.PreviewPresentations - count, fps = (editor.PreviewPresentations - count) / elapsed,
                    renderCallbacks = rendered, renderFps = rendered / elapsed, wasPlaying, active = editor.IsActive, tier = RenderCapability.Tier >> 16,
                    native = editor.NativePreviewReady, decodeMs = editor.PreviewDecodeMilliseconds - decode, composeMs = editor.PreviewComposeMilliseconds - compose,
                    playhead = ((Slider)editor.FindName("Seek")).Value, maximumGap, ticks, width = image.Width, height = image.Height, quality = ((ComboBox)editor.FindName("QualityPicker")).SelectedIndex };
                File.WriteAllText(Path.Combine(folder, baseline ? "baseline.json" : mkv ? "optimized-mkv.json" : effects ? "optimized-effects.json" : fourK ? "optimized-4k.json" : "optimized.json"), JsonSerializer.Serialize(metrics, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine(JsonSerializer.Serialize(metrics));
                if (!baseline && (!wasPlaying || !metrics.native || metrics.playhead < 1.5 || image.Width != width || image.Height != height || metrics.fps < 18 || maximumGap > 250)) throw new Exception("Native-resolution playback responsiveness check failed.");
                Console.WriteLine($"PASS Native {width}x{height} playback profile"); editor.RequestExit();
            }
            catch (Exception ex) { result = 1; Console.Error.WriteLine(ex); app.Shutdown(1); }
            finally { heartbeat.Stop(); }
        }));
        app.Run(); return result;
    }
}
