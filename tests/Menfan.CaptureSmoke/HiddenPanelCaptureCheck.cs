using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Menfan.App;
using Menfan.Core;

internal static class HiddenPanelCaptureCheck
{
    public static int Run(string ffmpeg)
    {
        int result = 0;
        var thread = new Thread(() => {
            var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var background = new Window { Title = "Menfan capture test backdrop", Left = 0, Top = 0, Width = 640, Height = 360,
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Topmost = true, Background = Brushes.Navy };
            var panel = new Window { Title = "Menfan test panel", Left = 40, Top = 40, Width = 180, Height = 100,
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Topmost = true, Background = Brushes.Magenta };
            background.Loaded += async (_, _) => {
                RecordingPanelHider? hiding = null;
                using var recorder = new Recorder();
                try
                {
                    var tools = new MediaTools(ffmpeg);
                    string folder = Path.GetFullPath(Path.Combine("artifacts", "hidden-panel-test", DateTime.Now.ToString("yyyyMMdd-HHmmss")));
                    Directory.CreateDirectory(folder); panel.Owner = background; panel.Show(); await Task.Delay(200);
                    string before = Path.Combine(folder, "before.png");
                    await MediaTools.RunAsync(tools.Ffmpeg, ["-y", "-f", "gdigrab", "-framerate", "30", "-offset_x", "0", "-offset_y", "0", "-video_size", "640x360", "-i", "desktop", "-frames:v", "1", before]);
                    var original = new BitmapImage(); original.BeginInit(); original.CacheOption = BitmapCacheOption.OnLoad; original.UriSource = new Uri(before); original.EndInit();
                    if (MagentaPixels(original) < 1000) throw new Exception("Test panel was not visible in the capture baseline.");
                    bool stopRequested = false;
                    hiding = new RecordingPanelHider(new[] { panel }, () => stopRequested = true);
                    await Task.Run(() => Native.DwmFlush());
                    await recorder.StartAsync(tools, new CaptureTarget("Hidden panel test region", 0, 0, 640, 360), 30, false, false, folder);
                    await Task.Delay(1200); hiding.RequestStop();
                    if (!stopRequested || !panel.IsVisible) throw new Exception("Capture-excluded panel must remain visible and usable.");
                    var project = await recorder.StopAsync(tools);
                    using (var reader = new MediaFrameReader(tools, project.MediaPath, 640, 360))
                        foreach (double time in new[] { 0, project.Duration / 2, Math.Max(0, project.Duration - .08) })
                        {
                            var captured = await reader.GetFrameAsync(time);
                            if (MagentaPixels(captured) > 0) throw new Exception("The excluded panel appeared in recorded frames.");
                            var rgba = new FormatConvertedBitmap(captured, PixelFormats.Bgra32, null, 0);
                            var sample = new byte[40 * 20 * 4]; rgba.CopyPixels(new Int32Rect(80, 70, 40, 20), sample, 40 * 4, 0);
                            if (Enumerable.Range(0, 800).Count(i => sample[i * 4] > 80 && sample[i * 4 + 1] < 40 && sample[i * 4 + 2] < 40) < 750)
                                throw new Exception("Exclusion left a hole instead of the underlying desktop.");
                        }
                    hiding.Dispose(); hiding = null;
                    if (!panel.IsVisible) throw new Exception("The panel was not restored after recording.");
                    Console.WriteLine("PASS Panel stays visible; first, middle and final recorded frames exclude it.");
                    Console.WriteLine("Project: " + folder);
                }
                catch (Exception ex) { result = 1; Console.Error.WriteLine(ex); }
                finally { hiding?.Dispose(); panel.Close(); background.Close(); app.Shutdown(result); }
            };
            app.Run(background);
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); return result;
    }
    private static int MagentaPixels(BitmapSource source)
    {
        var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        int count = 0;
        for (int i = 0; i < pixels.Length; i += 4) if (pixels[i] > 180 && pixels[i + 1] < 60 && pixels[i + 2] > 180) count++;
        return count;
    }
}
