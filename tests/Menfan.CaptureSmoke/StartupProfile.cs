using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Menfan.App;
using Menfan.Core;

internal static class StartupProfile
{
    public static int Run(string ffmpeg, string label, bool enforce, bool large = false)
    {
        int result = 0;
        var thread = new Thread(() => {
            var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var text = new TextBlock { FontSize = 24, Text = "Menfan capture startup responsiveness", Margin = new Thickness(24) };
            double width = large ? SystemParameters.PrimaryScreenWidth : 640, height = large ? SystemParameters.PrimaryScreenHeight : 360;
            int fps = large ? 60 : 30;
            var window = new Window { Title = "Menfan startup check", Width = width, Height = height, Left = 0, Top = 0,
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Topmost = true, Background = System.Windows.Media.Brushes.White, Content = text };
            window.Loaded += async (_, _) => {
                using var recorder = new Recorder();
                var heartbeat = new Stopwatch();
                double previous = 0, maximumGap = 0; int ticks = 0;
                var timer = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromMilliseconds(16) };
                timer.Tick += (_, _) => {
                    double now = heartbeat.Elapsed.TotalMilliseconds;
                    maximumGap = Math.Max(maximumGap, now - previous); previous = now; ticks++;
                    text.Text = $"Capture startup responsiveness\nUI heartbeat {ticks}\n{now / 1000:0.00} seconds";
                };
                try
                {
                    await Task.Delay(200);
                    var folder = Path.GetFullPath(Path.Combine("artifacts", "startup-profile", label)); Directory.CreateDirectory(folder);
                    var tools = new MediaTools(ffmpeg);
                    var target = new CaptureTarget("Startup check window", 0, 0, (int)width, (int)height, new WindowInteropHelper(window).Handle);
                    heartbeat.Start(); timer.Start();
                    var startup = Stopwatch.StartNew();
                    var task = recorder.StartAsync(tools, target, fps, true, false, folder);
                    double synchronousMs = startup.Elapsed.TotalMilliseconds;
                    await task; double startupMs = startup.Elapsed.TotalMilliseconds;
                    await Task.Delay(300); timer.Stop(); heartbeat.Stop();
                    var metrics = new { synchronousMs, startupMs, maximumGap, ticks };
                    File.WriteAllText(Path.Combine(folder, "metrics.json"), JsonSerializer.Serialize(metrics, new JsonSerializerOptions { WriteIndented = true }));
                    Console.WriteLine($"STARTUP {label}: caller {synchronousMs:0.0}ms, ready {startupMs:0.0}ms, maximum UI heartbeat gap {maximumGap:0.0}ms, {ticks} ticks");
                    await Task.Delay(900); recorder.AddMarker();
                    var project = await recorder.StopAsync(tools);
                    if (project.Duration < .8 || project.SystemAudioPath == null || project.Cursor.Count == 0 || project.Markers.Count != 1) throw new Exception("Startup capture lost media or tracks.");
                    var info = await tools.ProbeAsync(project.MediaPath);
                    if (Math.Abs(info.FrameRate - fps) > .01) throw new Exception("Capture frame rate changed.");
                    if (enforce && (synchronousMs > 25 || maximumGap > 150 || ticks < 3)) throw new Exception("Capture startup blocked the dispatcher.");
                    Console.WriteLine($"PASS Startup capture retains {project.Width}x{project.Height}/{fps}FPS screen, system audio, cursor and marker.");
                }
                catch (Exception ex) { result = 1; Console.Error.WriteLine(ex); }
                finally { timer.Stop(); window.Close(); app.Shutdown(result); }
            };
            app.Run(window);
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); return result;
    }
}
