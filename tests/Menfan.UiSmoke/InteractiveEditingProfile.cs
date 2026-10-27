using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Menfan.App;
using Menfan.Core;

internal static class InteractiveEditingProfile
{
    public static int Run(bool fourK)
    {
        int result = 0; var app = new App(); app.InitializeComponent();
        app.Startup += (_, _) => app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () => {
            try
            {
                var editor = app.Windows.OfType<Menfan.App.MainWindow>().Single(); editor.Topmost = true;
                int width = fourK ? 3840 : 2560, height = fourK ? 2160 : 1440;
                var folder = Path.GetFullPath("artifacts/preview-profile"); Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, fourK ? "native-2160p60.mp4" : "native-1440p60.mp4");
                var tools = new MediaTools(MediaTools.Locate() ?? throw new Exception("FFmpeg missing"));
                if (!File.Exists(path)) await MediaTools.RunAsync(tools.Ffmpeg, ["-y", "-f", "lavfi", "-i", $"testsrc2=size={width}x{height}:rate=60", "-t", "4", "-c:v", "libx264", "-threads", "4", "-preset", "ultrafast", "-crf", "22", "-pix_fmt", "yuv420p", path]);
                var p = new Project { MediaPath = path, Width = width, Height = height, Duration = 4, FrameRate = 60, Clips = [new() { End = 4 }] };
                p.ResetVisualEdits();
                var effect = new SceneEffect { Kind = "Screen3D", Name = "交互测试 3D", Start = 0, End = 4, RotateX = 12, RotateY = -18, FocusBlur = 10 };
                p.Effects.Add(effect);
                await editor.LoadForTestingAsync(p); editor.ShowEditor(); await Task.Delay(300);
                var module = Descendants(editor).OfType<Button>().Single(b => b.Tag?.ToString() == "运镜"); module.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var properties = (StackPanel)editor.FindName("Properties");
                properties.Children.OfType<Button>().Single(b => b.Content?.ToString() == effect.ToString()).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(300);
                Slider Axis(string text) { var label = properties.Children.OfType<TextBlock>().Single(t => t.Text.StartsWith(text)); return (Slider)properties.Children[properties.Children.IndexOf(label) + 1]; }
                var x = Axis("X 轴倾斜"); var y = Axis("Y 轴倾斜"); y.BringIntoView(); await Task.Delay(100);
                if (!x.IsMoveToPointEnabled || !y.IsMoveToPointEnabled) throw new Exception("Axis sliders do not support click-to-position");
                // Keep the pointer-edit gesture open while values arrive faster than display refresh.
                x.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
                y.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
                int before = editor.PreviewPresentations, updates = 0; double gap = 0, previous = 0;
                var clock = Stopwatch.StartNew();
                var input = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(8) };
                input.Tick += (_, _) => { double now = clock.Elapsed.TotalMilliseconds; gap = Math.Max(gap, now - previous); previous = now; updates++; x.Value = 40 * Math.Sin(now / 260); y.Value = 45 * Math.Cos(now / 290); };
                input.Start(); await Task.Delay(2000); input.Stop();
                int whileDragging = editor.PreviewPresentations - before;
                x.Value = 31; y.Value = -27; await Task.Delay(100);
                var source = ((Image)editor.FindName("ScenePreview")).Source;
                var viewport = FindPlane(((DrawingImage)source).Drawing) ?? throw new Exception("Preview does not contain a live 3D plane");
                var transform = (Transform3DGroup)((GeometryModel3D)viewport.Children.OfType<ModelVisual3D>().Single(v => v.Content is GeometryModel3D).Content).Transform;
                var angles = transform.Children.OfType<RotateTransform3D>().Select(r => ((AxisAngleRotation3D)r.Rotation).Angle).ToArray();
                if (Math.Abs(angles[0] - 31) > .001 || Math.Abs(angles[1] + 27) > .001) throw new Exception("Preview did not present the latest angles before pointer release");
                x.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
                y.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
                editor.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)editor.ActualWidth, (int)editor.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(editor);
                using (var stream = File.Create(Path.Combine(folder, $"interactive-{height}p.png"))) { var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); png.Save(stream); }
                var metrics = new { width, height, updates, whileDragging, maximumInputGapMs = gap, angles, source.Width, source.Height };
                File.WriteAllText(Path.Combine(folder, $"interactive-{height}p.json"), JsonSerializer.Serialize(metrics, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine(JsonSerializer.Serialize(metrics));
                if (whileDragging < 20 || updates < 30 || gap > 300 || source.Width != width || source.Height != height) throw new Exception("Paused 3D interaction responsiveness failed");
                Console.WriteLine("PASS Full-resolution 3D preview updates continuously and presents final angles before release");
                await editor.LoadForTestingAsync(p); editor.RequestExit();
            }
            catch (Exception ex) { result = 1; Console.Error.WriteLine(ex); app.Shutdown(1); }
        }));
        app.Run(); return result;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { yield return root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child; }
    private static Viewport3D? FindPlane(Drawing drawing)
    {
        if (drawing is GeometryDrawing { Brush: VisualBrush { Visual: Viewport3D plane } }) return plane;
        if (drawing is ImageDrawing { ImageSource: DrawingImage image }) return FindPlane(image.Drawing);
        if (drawing is DrawingGroup group) foreach (var child in group.Children) { var found = FindPlane(child); if (found != null) return found; }
        return null;
    }
}
