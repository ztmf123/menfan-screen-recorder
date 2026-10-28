using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Menfan.Core;

namespace Menfan.App;

internal sealed class SceneRenderer
{
    private readonly Dictionary<string, (BitmapSource Image, long Stamp, long Length)> assets = new(StringComparer.OrdinalIgnoreCase);
    private string? backgroundKey;
    private DrawingImage? staticBackground;
    private Viewport3D? retainedPlane;
    private ImageSource? retainedPlaneImage;
    public string? FfmpegPath { get; set; }
    public static (int Width, int Height) Dimensions(Project p, int height)
    {
        double ratio = (p.Width * (1 - p.Style.CropLeft - p.Style.CropRight) + p.Style.Padding * 2d) /
            (p.Height * (1 - p.Style.CropTop - p.Style.CropBottom) + p.Style.Padding * 2d);
        if (p.Style.Aspect != "Source") { var parts = p.Style.Aspect.Split(':'); ratio = double.Parse(parts[0], CultureInfo.InvariantCulture) / double.Parse(parts[1], CultureInfo.InvariantCulture); }
        if (height == 0) height = (int)(p.Height * (1 - p.Style.CropTop - p.Style.CropBottom) + p.Style.Padding * 2);
        height = Math.Max(2, height / 2 * 2);
        return (Math.Max(2, (int)(height * ratio) / 2 * 2), height);
    }
    public BitmapSource Load(string path)
    {
        var file = new FileInfo(path);
        if (assets.TryGetValue(path, out var cached) && cached.Stamp == file.LastWriteTimeUtc.Ticks && cached.Length == file.Length) return cached.Image;
        BitmapSource decoded;
        try
        {
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = new Uri(Path.GetFullPath(path)); bitmap.EndInit(); bitmap.Freeze(); decoded = bitmap;
        }
        catch (Exception ex) when (ex is NotSupportedException or System.IO.FileFormatException)
        {
            var executable = FfmpegPath ?? MediaTools.Locate() ?? throw new InvalidOperationException("此图片格式需要 FFmpeg。", ex);
            using var process = new System.Diagnostics.Process { StartInfo = MediaTools.StartInfo(executable, ["-loglevel", "error", "-i", path, "-frames:v", "1", "-c:v", "png", "-f", "image2pipe", "pipe:1"]) };
            process.Start(); var errors = process.StandardError.ReadToEndAsync(); using var stream = new MemoryStream();
            process.StandardOutput.BaseStream.CopyTo(stream); process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException("图片无法解码：" + errors.GetAwaiter().GetResult());
            stream.Position = 0; var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); decoded = bitmap;
        }
        assets[path] = (decoded, file.LastWriteTimeUtc.Ticks, file.Length); return decoded;
    }
    public BitmapSource Render(Project project, double time, BitmapSource screen, BitmapSource? camera, BitmapSource? background, int width, int height)
    {
        return Raster(Compose(project, time, screen, camera, background, width, height), width, height);
    }
    public DrawingImage Compose(Project project, double time, ImageSource screen, BitmapSource? camera, BitmapSource? background, int width, int height, bool freeze = true)
    {
        var state = SceneEvaluator.Evaluate(project, time); var style = project.Style;
        var visual = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(0, 0, width, height)) };
        using (var dc = visual.Open())
        {
            DrawBackground(dc, project, background, time, width, height);
            var images = project.Effects.Where(e => e.Kind == "Image" && time >= e.Start && time < e.End).ToList();
            bool screenVisible = !images.Any(e => e.BackgroundOnly);
            double intro = style.Intro ? SceneEvaluator.Ease(time / Math.Max(.05, style.IntroDuration), style.IntroStyle) : 1;
            var crop = Crop(screen, style.CropLeft, style.CropTop, style.CropRight, style.CropBottom);
            double pad = style.Padding * height / (double)(project.Height + style.Padding * 2);
            double bar = style.Frame == "None" ? 0 : height * .032;
            double fit = Math.Min((width - 2 * pad) / crop.Width, (height - 2 * pad - bar) / crop.Height) * style.ScreenScale;
            double sw = crop.Width * fit, sh = crop.Height * fit;
            var rect = new Rect((width - sw) * style.ScreenX, (height - sh - bar) * style.ScreenY, sw, sh + bar);
            if (screenVisible)
            {
                dc.PushOpacity(Math.Clamp(intro, 0, 1));
                double ix = style.IntroDirection == "Right" ? (1 - intro) * width : 0;
                double iy = style.IntroDirection == "Bottom" ? (1 - intro) * height : style.IntroDirection == "Top" ? -(1 - intro) * height : 0;
                dc.PushTransform(new TranslateTransform(ix, iy));
                var masked = MaskSource(project, state.SourceTime, time, screen, freeze);
                var content = ScreenTexture(project, state, masked, time, Math.Max(2, (int)sw), Math.Max(2, (int)sh), bar, freeze);
                var focusEffect = project.Effects.LastOrDefault(e => e.Kind == "Screen3D" && time >= e.Start && time < e.End && e.FocusBlur > 0);
                if (focusEffect != null) content = freeze
                    ? FocusBlur(Raster(content, (int)sw, (int)sh + (int)bar), focusEffect.FocusBlur, focusEffect.FocusY)
                    : FocusBlurLive(content, focusEffect.FocusBlur, focusEffect.FocusY, (int)sw, (int)sh + (int)bar);
                double blur = images.Count > 0 ? images.Max(e => e.Blur) : 0;
                if (blur > 0) content = freeze ? Blur(Raster(content, (int)sw, (int)sh + (int)bar), blur * height / 1080d)
                    : BlurLive(content, blur * height / 1080d, (int)sw, (int)sh + (int)bar);
                DrawShadow(dc, rect, style.CornerRadius * height / project.Height, style.Shadow);
                if (Math.Abs(state.RotateX) + Math.Abs(state.RotateY) + Math.Abs(state.RotateZ) > .01)
                    dc.DrawImage(freeze ? Raster(PlaneVisual(content, state, width, height, rect), width, height)
                        : RetainedPlane(content, state, width, height, rect), new Rect(0, 0, width, height));
                else dc.DrawImage(content, rect);
                dc.Pop(); dc.Pop();
            }
            if (camera != null && project.Camera.Visible)
            {
                var c = state.Camera;
                double aspect = CameraAspect(project.Camera.Shape), radius = CameraRadius(project.Camera.Shape);
                foreach (var e in project.Effects.Where(e => e.Kind == "Camera" && time >= e.Start && time < e.End))
                { aspect = SceneEvaluator.AnimatedValue(project, e, time, v => CameraAspect(v.Shape), aspect); radius = SceneEvaluator.AnimatedValue(project, e, time, v => CameraRadius(v.Shape), radius); }
                double cw = width * c.Size, ch = cw / aspect;
                var cr = new Rect(Math.Clamp(c.X * width - cw / 2, 0, Math.Max(0, width - cw)) + (1 - intro) * width,
                    Math.Clamp(c.Y * height - ch / 2, 0, Math.Max(0, height - ch)), cw, ch);
                dc.PushOpacity(Math.Clamp(intro, 0, 1)); DrawShadow(dc, cr, radius * Math.Min(cw, ch), c.Shadow);
                var shape = new RectangleGeometry(cr, radius * Math.Min(cw, ch), radius * Math.Min(cw, ch));
                dc.PushClip(shape);
                if (c.Mirror) dc.PushTransform(new ScaleTransform(-1, 1, cr.X + cr.Width / 2, cr.Y + cr.Height / 2));
                DrawCover(dc, camera, cr, c.PanX, c.PanY, c.Zoom);
                if (c.Mirror) dc.Pop(); dc.Pop();
                if (c.Stroke > 0) dc.DrawGeometry(null, new Pen(Brushes.White, c.Stroke * height / 1080d), shape);
                dc.Pop();
            }
            foreach (var e in images)
            {
                if (e.AssetPath == null) continue;
                double alpha = SceneEvaluator.Envelope(project, e, time);
                var image = Load(e.AssetPath);
                double ex = SceneEvaluator.AnimatedValue(project, e, time, v => v.X, e.X), ey = SceneEvaluator.AnimatedValue(project, e, time, v => v.Y, e.Y);
                double ew = SceneEvaluator.AnimatedValue(project, e, time, v => v.Width, e.Width), eh = SceneEvaluator.AnimatedValue(project, e, time, v => v.Height, e.Height);
                var ir = new Rect((ex - ew / 2) * width, (ey - eh / 2) * height, ew * width, eh * height);
                dc.PushOpacity(alpha); DrawShadow(dc, ir, 4, .15); dc.DrawImage(image, ir); dc.Pop();
            }
        }
        var composed = new DrawingImage(visual); if (freeze) composed.Freeze(); return composed;
    }

    private ImageSource ScreenTexture(Project p, SceneState state, ImageSource source, double time, int w, int h, double barHeight, bool freeze)
    {
        var s = p.Style; int bh = Math.Max(0, (int)barHeight);
        var visual = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(0, 0, w, h + bh)) };
        using (var dc = visual.Open())
        {
            double radius = s.CornerRadius * h / Math.Max(1, p.Height * (1 - s.CropTop - s.CropBottom));
            var bounds = new Rect(0, 0, w, h + bh);
            dc.PushClip(new RectangleGeometry(bounds, radius, radius));
            var cropped = Crop(source, s.CropLeft, s.CropTop, s.CropRight, s.CropBottom);
            dc.DrawRectangle(s.Frame.Contains("Dark") ? Brushes.DimGray : Brushes.WhiteSmoke, null, bounds);
            dc.PushClip(new RectangleGeometry(new Rect(0, bh, w, h)));
            double zx = Math.Clamp((state.FocusX - s.CropLeft) / (1 - s.CropLeft - s.CropRight), 0, 1);
            double zy = Math.Clamp((state.FocusY - s.CropTop) / (1 - s.CropTop - s.CropBottom), 0, 1);
            dc.PushTransform(new TranslateTransform(-(state.Zoom - 1) * w * zx, bh - (state.Zoom - 1) * h * zy));
            dc.PushTransform(new ScaleTransform(state.Zoom, state.Zoom));
            dc.DrawImage(cropped, new Rect(0, 0, w, h));
            DrawCursor(dc, p, state.SourceTime, w, h);
            dc.Pop(); dc.Pop(); dc.Pop();
            if (bh > 0)
            {
                var dark = s.Frame.Contains("Dark");
                dc.DrawRectangle(dark ? new SolidColorBrush(Color.FromRgb(45, 48, 49)) : new SolidColorBrush(Color.FromRgb(243, 245, 245)), null, new Rect(0, 0, w, bh));
                for (int i = 0; i < 3; i++) dc.DrawEllipse(new SolidColorBrush(new[] { Colors.Salmon, Colors.Khaki, Colors.LightGreen }[i]), null, new Point(12 + i * 12, bh / 2d), 3.5, 3.5);
                if (s.Frame.Contains("Browser"))
                {
                    var text = new FormattedText(s.FrameText, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"), Math.Max(8, bh * .36), dark ? Brushes.WhiteSmoke : Brushes.DimGray, 1);
                    dc.DrawText(text, new Point(Math.Max(55, (w - text.Width) / 2), (bh - text.Height) / 2));
                }
            }
            dc.Pop();
            if (s.Stroke > 0) dc.DrawRoundedRectangle(null, new Pen(Brushes.White, s.Stroke * h / 1080d), bounds, radius, radius);
        }
        var image = new DrawingImage(visual); if (freeze) image.Freeze(); return image;
    }
    private static ImageSource Crop(ImageSource source, double left, double top, double right, double bottom)
    {
        if (left == 0 && top == 0 && right == 0 && bottom == 0) return source;
        int x = (int)(left * source.Width), y = (int)(top * source.Height);
        int w = Math.Clamp((int)((1 - left - right) * source.Width), 1, (int)source.Width - x);
        int h = Math.Clamp((int)((1 - top - bottom) * source.Height), 1, (int)source.Height - y);
        if (source is BitmapSource bitmap) { var crop = new CroppedBitmap(bitmap, new Int32Rect(x, y, w, h)); crop.Freeze(); return crop; }
        var group = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(0, 0, w, h)) };
        using (var dc = group.Open()) dc.DrawImage(source, new Rect(-x, -y, source.Width, source.Height));
        return new DrawingImage(group);
    }
    private static ImageSource MaskSource(Project project, double sourceTime, double time, ImageSource screen, bool freeze)
    {
        var masks = project.Effects.Where(e => e.Kind == "Blur" && time >= e.Start && time <= e.End).ToList();
        if (masks.Count == 0) return screen;
        if (!freeze)
        {
            var group = new DrawingGroup();
            using (var dc = group.Open())
            {
                var area = new Rect(0, 0, screen.Width, screen.Height); dc.DrawImage(screen, area);
                var blurred = BlurLive(screen, masks.Max(e => e.Blur) * screen.Height / 1080d, (int)screen.Width, (int)screen.Height);
                foreach (var e in masks)
                {
                    var r = new Rect(Math.Clamp((e.X - e.Width / 2) * screen.Width, 0, screen.Width - 1), Math.Clamp((e.Y - e.Height / 2) * screen.Height, 0, screen.Height - 1), e.Width * screen.Width, e.Height * screen.Height);
                    dc.PushClip(new RectangleGeometry(r)); dc.DrawImage(blurred, area); dc.Pop();
                }
            }
            return new DrawingImage(group);
        }
        var bitmap = screen as BitmapSource ?? Raster(screen, (int)screen.Width, (int)screen.Height);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(bitmap, new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
            var blurred = Blur(bitmap, masks.Max(e => e.Blur) * bitmap.PixelHeight / 1080d);
            foreach (var e in masks)
            {
                var r = new Rect(Math.Clamp((e.X - e.Width / 2) * (int)screen.Width, 0, (int)screen.Width - 1),
                    Math.Clamp((e.Y - e.Height / 2) * (int)screen.Height, 0, (int)screen.Height - 1), e.Width * (int)screen.Width, e.Height * (int)screen.Height);
                dc.PushClip(new RectangleGeometry(r)); dc.DrawImage(blurred, new Rect(0, 0, (int)screen.Width, (int)screen.Height)); dc.Pop();
            }
        }
        return Raster(visual, (int)screen.Width, (int)screen.Height);
    }
    private void DrawCursor(DrawingContext dc, Project p, double time, double width, double height)
    {
        var s = p.Style; if (p.Cursor.Count == 0) return;
        var sample = SceneEvaluator.CursorAt(p, time);
        double cursorTime = time + s.CursorOffset;
        if (s.CursorSmoothing > 0)
        {
            var recent = p.Cursor.Where(c => c.Time >= cursorTime - s.CursorSmoothing && c.Time <= cursorTime).ToArray();
            if (recent.Length > 1) { sample.X = recent.Average(c => c.X); sample.Y = recent.Average(c => c.Y); }
        }
        double x = (sample.X - s.CropLeft) / (1 - s.CropLeft - s.CropRight) * width;
        double y = (sample.Y - s.CropTop) / (1 - s.CropTop - s.CropBottom) * height;
        double size = s.CursorSize * height / 600d;
        bool idle = s.HideIdleCursor && !p.Cursor.Any(c => c.Time > time - 1.5 && c.Time <= time && (Math.Abs(c.X - sample.X) + Math.Abs(c.Y - sample.Y)) > .003);
        if (!p.CursorEmbedded && sample.Visible && s.CursorMode != "Hidden" && !idle)
        {
            if (s.CursorMode == "Touch") dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(155, 130, 130, 135)), new Pen(Brushes.White, size), new Point(x, y), size * 9, size * 9);
            else if (sample.ShapePath != null && File.Exists(sample.ShapePath))
            {
                var cursor = Load(sample.ShapePath); double scale = s.CursorSize * height / Math.Max(1, p.Height * (1 - s.CropTop - s.CropBottom));
                dc.DrawImage(cursor, new Rect(x - sample.HotspotX * scale, y - sample.HotspotY * scale, cursor.PixelWidth * scale, cursor.PixelHeight * scale));
            }
            else
            {
                dc.PushTransform(new TranslateTransform(x, y)); dc.PushTransform(new ScaleTransform(size, size));
                var arrow = Geometry.Parse("M 0,0 L 0,22 L 6,17 L 11,27 L 15,25 L 10,15 L 18,15 Z");
                dc.DrawGeometry(Brushes.White, new Pen(Brushes.Black, 1.3), arrow); dc.Pop(); dc.Pop();
            }
        }
        if (!s.ClickHighlight || s.ClickStyle == "None") return;
        var color = (Color)ColorConverter.ConvertFromString(s.CursorColor);
        foreach (var click in p.Cursor.Where(c => c.Click && cursorTime >= c.Time && cursorTime < c.Time + .55))
        {
            double t = (cursorTime - click.Time) / .55;
            var point = new Point((click.X - s.CropLeft) / (1 - s.CropLeft - s.CropRight) * width, (click.Y - s.CropTop) / (1 - s.CropTop - s.CropBottom) * height);
            var brush = new SolidColorBrush(Color.FromArgb((byte)(255 * s.ClickOpacity * (1 - t)), color.R, color.G, color.B));
            double radius = (8 + t * 26) * s.ClickSize * height / 600d;
            dc.DrawEllipse(s.ClickStyle is "Halo" or "Flash" ? brush : null, new Pen(brush, Math.Max(1.2, height / 450)), point, radius, radius);
            if (s.ClickStyle == "Double") dc.DrawEllipse(null, new Pen(brush, 1.5), point, radius * .6, radius * .6);
        }
    }
    private void DrawBackground(DrawingContext dc, Project p, BitmapSource? video, double time, double w, double h)
    {
        if (p.Style.BackgroundKind is "MovingGrid" or "Video") { DrawBackgroundRaw(dc, p, video, time, w, h); return; }
        var s = p.Style;
        var stamp = s.BackgroundKind == "Image" && s.BackgroundPath != null ? Load(s.BackgroundPath).GetHashCode() : 0;
        string key = $"{s.BackgroundKind}|{s.Background}|{s.GradientColor}|{s.BackgroundPath}|{stamp}|{w}|{h}";
        if (backgroundKey != key || staticBackground == null)
        {
            var group = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(0, 0, w, h)) }; using (var context = group.Open()) DrawBackgroundRaw(context, p, video, time, w, h);
            staticBackground = new DrawingImage(group); staticBackground.Freeze(); backgroundKey = key;
        }
        dc.DrawImage(staticBackground, new Rect(0, 0, w, h));
    }
    private void DrawBackgroundRaw(DrawingContext dc, Project p, BitmapSource? video, double time, double w, double h)
    {
        var s = p.Style; var first = (Color)ColorConverter.ConvertFromString(s.Background);
        Brush brush = new SolidColorBrush(first);
        if (s.BackgroundKind == "Gradient") brush = new LinearGradientBrush(first, (Color)ColorConverter.ConvertFromString(s.GradientColor), new Point(0, 0), new Point(1, 1));
        dc.DrawRectangle(brush, null, new Rect(0, 0, w, h));
        if (s.BackgroundKind == "Image" && s.BackgroundPath != null) DrawCover(dc, Load(s.BackgroundPath), new Rect(0, 0, w, h));
        if (s.BackgroundKind == "Video" && video != null) DrawCover(dc, video, new Rect(0, 0, w, h));
        if (s.BackgroundKind is "Grid" or "Dots" or "MovingGrid")
        {
            double spacing = h / 20, shift = s.BackgroundKind == "MovingGrid" ? time * 10 % spacing : 0;
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(45, 130, 140, 145)), Math.Max(.5, h / 1080));
            for (double x = -spacing + shift; x < w; x += spacing)
                if (s.BackgroundKind == "Dots") for (double y = shift; y < h; y += spacing) dc.DrawEllipse(pen.Brush, null, new Point(x, y), 1, 1);
                else dc.DrawLine(pen, new Point(x, 0), new Point(x, h));
            if (s.BackgroundKind != "Dots") for (double y = -spacing + shift; y < h; y += spacing) dc.DrawLine(pen, new Point(0, y), new Point(w, y));
        }
    }
    private static double CameraAspect(string shape) => shape switch { "Landscape" => 16d / 9, "Portrait" => 3d / 4, _ => 1 };
    private static double CameraRadius(string shape) => shape == "Circle" ? .5 : shape == "Rectangle" ? 0 : .1;
    private static void DrawCover(DrawingContext dc, BitmapSource image, Rect r, double x = .5, double y = .5, double zoom = 1)
    {
        double scale = Math.Max(r.Width / image.PixelWidth, r.Height / image.PixelHeight) * zoom;
        dc.PushClip(new RectangleGeometry(r));
        dc.DrawImage(image, new Rect(r.X - (image.PixelWidth * scale - r.Width) * x, r.Y - (image.PixelHeight * scale - r.Height) * y, image.PixelWidth * scale, image.PixelHeight * scale)); dc.Pop();
    }
    private static void DrawShadow(DrawingContext dc, Rect r, double radius, double opacity)
    {
        if (opacity <= 0) return;
        for (int i = 10; i >= 1; i--) { var b = new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(opacity * 4, 0, 20), 0, 0, 0)); var expanded = r; expanded.Inflate(i * 1.5, i * 1.5); expanded.Offset(0, i * .8); dc.DrawRoundedRectangle(b, null, expanded, radius + i, radius + i); }
    }
    private static Viewport3D PlaneVisual(ImageSource texture, SceneState state, int w, int h, Rect bounds)
    {
        var viewport = new Viewport3D { Width = w, Height = h, ClipToBounds = true };
        double fieldOfView = 20 + Math.Clamp(state.Perspective, .1, 1.5) * 40;
        double distance = 1 / Math.Tan(fieldOfView / 2 * Math.PI / 180);
        viewport.Camera = new PerspectiveCamera(new Point3D(0, 0, distance), new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), fieldOfView);
        var mesh = new MeshGeometry3D {
            Positions = new Point3DCollection { new(-1, -1, 0), new(1, -1, 0), new(1, 1, 0), new(-1, 1, 0) },
            TextureCoordinates = new PointCollection { new(0, 1), new(1, 1), new(1, 0), new(0, 0) },
            TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 }
        };
        var material = new DiffuseMaterial(new ImageBrush(texture)); var model = new GeometryModel3D(mesh, material) { BackMaterial = material };
        viewport.Children.Add(new ModelVisual3D { Content = new AmbientLight(Colors.White) });
        var transform = new Transform3DGroup(); transform.Children.Add(new ScaleTransform3D(bounds.Width / w, bounds.Height / w, 1));
        transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), state.RotateX)));
        transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), state.RotateY)));
        transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), -state.RotateZ)));
        transform.Children.Add(new TranslateTransform3D((bounds.X + bounds.Width / 2 - w / 2d) / w * 2, -(bounds.Y + bounds.Height / 2 - h / 2d) / w * 2, 0));
        model.Transform = transform; viewport.Children.Add(new ModelVisual3D { Content = model });
        viewport.Measure(new Size(w, h)); viewport.Arrange(new Rect(0, 0, w, h)); viewport.UpdateLayout();
        return viewport;
    }
    private ImageSource RetainedPlane(ImageSource texture, SceneState state, int w, int h, Rect bounds)
    {
        if (retainedPlane == null || retainedPlane.Width != w || retainedPlane.Height != h)
        {
            retainedPlane = PlaneVisual(texture, state, w, h, bounds);
            retainedPlaneImage = LiveVisual(retainedPlane, w, h);
        }
        var model = (GeometryModel3D)retainedPlane.Children.OfType<ModelVisual3D>().Single(v => v.Content is GeometryModel3D).Content;
        ((ImageBrush)((DiffuseMaterial)model.Material).Brush).ImageSource = texture;
        var transform = (Transform3DGroup)model.Transform;
        var scale = (ScaleTransform3D)transform.Children[0]; scale.ScaleX = bounds.Width / w; scale.ScaleY = bounds.Height / w;
        ((AxisAngleRotation3D)((RotateTransform3D)transform.Children[1]).Rotation).Angle = state.RotateX;
        ((AxisAngleRotation3D)((RotateTransform3D)transform.Children[2]).Rotation).Angle = state.RotateY;
        ((AxisAngleRotation3D)((RotateTransform3D)transform.Children[3]).Rotation).Angle = -state.RotateZ;
        var offset = (TranslateTransform3D)transform.Children[4];
        offset.OffsetX = (bounds.X + bounds.Width / 2 - w / 2d) / w * 2; offset.OffsetY = -(bounds.Y + bounds.Height / 2 - h / 2d) / w * 2;
        var camera = (PerspectiveCamera)retainedPlane.Camera;
        camera.FieldOfView = 20 + Math.Clamp(state.Perspective, .1, 1.5) * 40;
        camera.Position = new Point3D(0, 0, 1 / Math.Tan(camera.FieldOfView / 2 * Math.PI / 180));
        return retainedPlaneImage!;
    }
    private static ImageSource LiveVisual(Visual visual, int width, int height)
    {
        var group = new DrawingGroup();
        using (var dc = group.Open()) dc.DrawRectangle(new VisualBrush(visual) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, width, height) }, null, new Rect(0, 0, width, height));
        return new DrawingImage(group);
    }
    private static ImageSource BlurLive(ImageSource source, double radius, int width, int height)
    {
        var image = new Image { Source = source, Width = width, Height = height, Effect = new BlurEffect { Radius = Math.Clamp(radius, 0, 100), RenderingBias = RenderingBias.Quality } };
        image.Measure(new Size(width, height)); image.Arrange(new Rect(0, 0, width, height));
        return LiveVisual(image, width, height);
    }
    private static ImageSource FocusBlurLive(ImageSource source, double radius, double focus, int width, int height)
    {
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            var rect = new Rect(0, 0, width, height); dc.DrawImage(source, rect);
            var mask = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            mask.GradientStops.Add(new(Colors.Black, 0)); mask.GradientStops.Add(new(Colors.Transparent, Math.Clamp(focus - .15, .01, .99)));
            mask.GradientStops.Add(new(Colors.Transparent, Math.Clamp(focus + .15, .01, .99))); mask.GradientStops.Add(new(Colors.Black, 1));
            dc.PushOpacityMask(mask); dc.DrawImage(BlurLive(source, radius * height / 1080d, width, height), rect); dc.Pop();
        }
        return new DrawingImage(group);
    }
    private static BitmapSource Blur(BitmapSource bitmap, double radius)
        => CpuBlur.Apply(bitmap, radius);
    private static BitmapSource FocusBlur(BitmapSource bitmap, double radius, double focus)
        => CpuBlur.Apply(bitmap, radius * bitmap.PixelHeight / 1080d, focus);
    private static BitmapSource Raster(ImageSource image, int width, int height)
    {
        var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) dc.DrawImage(image, new Rect(0, 0, Math.Max(1, width), Math.Max(1, height)));
        return Raster(visual, width, height);
    }
    private static BitmapSource Raster(Visual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(Math.Max(1, width), Math.Max(1, height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
}
