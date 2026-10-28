using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Menfan.App;
using Menfan.Core;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--interactive-editing")) return InteractiveEditingProfile.Run(args.Contains("--4k"));
        if (args.Contains("--preview-performance")) return PreviewPlaybackProfile.Run(args.Contains("--baseline"), args.Contains("--4k"), args.Contains("--effects"), args.Contains("--mkv"));
        bool failed = false; string? originalTheme = null; var app = new App(); app.InitializeComponent();
        app.Startup += (_, _) => app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () => {
            try
            {
                var editor = app.Windows.OfType<Menfan.App.MainWindow>().Single(); var launcher = app.Windows.OfType<RecorderWindow>().Single();
                originalTheme = ThemeService.Mode; ThemeService.Apply("Dark");
                Check(launcher.IsVisible && !editor.IsVisible, "Startup shows only the compact recorder");
                Check(launcher.Width <= 981 && launcher.Height <= 200, "Compact recorder size");
                Check(((CheckBox)launcher.FindName("HidePanelsToggle")).IsChecked == editor.HidePanelsWhileRecording, "Hidden-panel switch reflects the saved recording setting");
                int hiddenStopRequests = 0;
                using (var hiddenPanels = new RecordingPanelHider(app.Windows.OfType<Window>(), () => hiddenStopRequests++))
                {
                    Check(launcher.IsVisible && !editor.IsVisible, "Capture exclusion preserves the visible recorder controls");
                    hiddenPanels.RequestStop(); Check(hiddenStopRequests == 1, "Tray stop action remains available while panels are hidden");
                }
                Check(launcher.IsVisible && !editor.IsVisible, "Leaving hidden recording restores the previous toolbar state");
                Check(((ComboBox)editor.FindName("QualityPicker")).SelectedIndex == 1, "Preview defaults to full resolution");
                Check(((SolidColorBrush)editor.Background).Color == Colors.Black && editor.Foreground is SolidColorBrush { Color: var textColor } && textColor == Colors.White && editor.FontWeight == FontWeights.Normal, "Black background, white text and regular-weight typography");
                SaveVisual(launcher, "artifacts/ui-tests/recorder.png");
                var source = Path.GetFullPath("artifacts/integration/test-source.mp4");
                if (File.Exists(source))
                {
                    var p = new Project { MediaPath = source, Width = 640, Height = 360, Duration = 4, Clips = [new() { End = 4 }] };
                    p.ResetVisualEdits();
                    var media = new MediaTools(MediaTools.Locate() ?? throw new Exception("FFmpeg missing"));
                    foreach (var mode in new[] { "Dark", "Light" })
                    {
                        ThemeService.Apply(mode); int saves = 0, edits = 0;
                        var completed = new RecordingCompleteWindow(p, media, () => edits++, () => saves++);
                        completed.Show(); await Task.Delay(400); completed.UpdateLayout();
                        CheckCompletionButtons(completed);
                        SaveVisual(completed, $"artifacts/ui-tests/completed-{mode.ToLowerInvariant()}.png");
                        completed.MaxHeight = 240; completed.FontSize = 20; await Task.Delay(150); completed.UpdateLayout();
                        CheckCompletionButtons(completed);
                        SaveVisual(completed, $"artifacts/ui-tests/completed-small-{mode.ToLowerInvariant()}.png");
                        FindButton(completed, "保存项目…")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        FindButton(completed, "进入编辑 →")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Check(saves == 1 && edits == 1 && !completed.IsVisible, "Completion card save and edit actions remain accessible in " + mode);
                    }
                    ThemeService.Apply("Dark");
                    p.Style.Frame = "LightBrowser"; p.Style.BackgroundKind = "Gradient";
                    p.Style.CropTop = .1;
                    p.Effects.Add(new() { Kind = "Zoom", Start = .3, End = 3.5, Name = "缩放 · 自动", Auto = true });
                    await editor.LoadForTestingAsync(p); editor.ShowEditor(); await Task.Delay(1500);
                    Check(((Image)editor.FindName("ScenePreview")).Source != null, "Editor produces composed preview");
                    var timeline = (TimelineControl)editor.FindName("Timeline");
                    Check(timeline.TransformToAncestor(editor).Transform(new Point()).X < 70, "Timeline begins at the left of the editing panel");
                    var player = (MediaElement)editor.FindName("Player"); player.Source = new Uri(source); p.Style.Mute = true;
                    await editor.PrepareAudioForTestingAsync(); Check(player.Source == null, "Mute clears the previous audio source"); p.Style.Mute = false;
                    SaveVisual(editor, "artifacts/ui-tests/editor.png");
                    FindButton(editor, "裁切")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(300);
                    var crop = (CropAdorner)editor.FindName("CropOverlay");
                    Check(crop.Visibility == Visibility.Visible && Math.Abs(crop.TopCrop - .1) < .001, "Crop mode displays source-aligned crop handles");
                    var pausedSeek = (Slider)editor.FindName("Seek");
                    pausedSeek.Value = .2; await Task.Delay(250);
                    var firstSeekPixels = PreviewPixels(((Image)editor.FindName("ScenePreview")).Source);
                    foreach (double value in new[] { 2.9, .5, 2.5 }) { pausedSeek.Value = value; await Task.Delay(60); }
                    await Task.Delay(250);
                    var lastSeekPixels = PreviewPixels(((Image)editor.FindName("ScenePreview")).Source);
                    SaveVisual(editor, "artifacts/ui-tests/paused-seek.png");
                    Console.WriteLine($"Seek diagnostics: native={editor.NativePreviewReady}, source={((Image)editor.FindName("ScenePreview")).Source.GetType().Name}, first={firstSeekPixels.Sum(v => (long)v)}, last={lastSeekPixels.Sum(v => (long)v)}, status={((TextBlock)editor.FindName("Status")).Text}");
                    Check(!editor.IsPreviewPlaying && firstSeekPixels.Where((v, i) => Math.Abs(v - lastSeekPixels[i]) > 20).Count() > 100,
                        "Paused scrubbing displays different source frames without pressing play");
                    SaveVisual(editor, "artifacts/ui-tests/crop.png");
                    FindButton(editor, "完成裁切")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(300);
                    Check(crop.Visibility == Visibility.Collapsed, "Finishing crop restores composed preview");
                    var originalBounds = new Rect(editor.Left, editor.Top, editor.Width, editor.Height);
                    FindButton(editor, "全屏剪辑 F11")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(250);
                    Check(editor.IsEditorFullscreen && editor.WindowStyle == WindowStyle.None && editor.WindowState == WindowState.Maximized && timeline.IsVisible, "Fullscreen editing retains the timeline");
                    FindButton(editor, "全屏预览")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(500);
                    var fullImage = (Image)editor.FindName("FullscreenImage");
                    Check(editor.IsPreviewFullscreen && !timeline.IsVisible && fullImage.IsVisible && ReferenceEquals(fullImage.Source, ((Image)editor.FindName("ScenePreview")).Source), "Fullscreen preview displays the shared composed frame and hides editing panels");
                    SaveVisual(editor, "artifacts/ui-tests/fullscreen-preview.png");
                    Check(editor.HandleDisplayShortcut(System.Windows.Input.Key.Escape) && editor.IsEditorFullscreen && !editor.IsPreviewFullscreen, "Escape returns preview to fullscreen editing");
                    Check(editor.HandleDisplayShortcut(System.Windows.Input.Key.Escape) && !editor.IsEditorFullscreen && editor.WindowStyle == WindowStyle.SingleBorderWindow && Math.Abs(editor.Width - originalBounds.Width) < 1 && Math.Abs(editor.Left - originalBounds.Left) < 1, "Second Escape restores the original editor window");
                    var seek = (Slider)editor.FindName("Seek"); seek.Value = .5; p.Style.Mute = true;
                    editor.SetPreviewFullscreen(true); await Task.Delay(200);
                    ((Button)editor.FindName("FullscreenPlayButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    for (int attempt = 0; attempt < 60 && !editor.IsPreviewPlaying; attempt++) await Task.Delay(100);
                    await Task.Delay(300);
                    Check(editor.IsPreviewPlaying && seek.Value > .65, "Playback advances inside fullscreen preview");
                    ((Button)editor.FindName("FullscreenPlayButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(!editor.IsPreviewPlaying, "Fullscreen pause controls the same playback session");
                    var fullSeek = (Slider)editor.FindName("FullscreenSeek"); fullSeek.Value = 1.2;
                    Check(Math.Abs(seek.Value - 1.2) < .01, "Fullscreen seeking updates the editor playhead");
                    editor.SetPreviewFullscreen(false); await Task.Delay(200); p.Style.Mute = false;
                    Check(Math.Abs(seek.Value - 1.2) < .01 && editor.WindowState == WindowState.Normal, "Leaving preview preserves the playhead and normal window state");
                    editor.WindowState = WindowState.Maximized; await Task.Delay(150);
                    editor.SetPreviewFullscreen(true); editor.SetPreviewFullscreen(false);
                    Check(editor.WindowState == WindowState.Maximized, "Preview restores an already maximized window");
                    editor.WindowState = WindowState.Normal;
                    var qualityModes = (ComboBox)editor.FindName("QualityPicker"); qualityModes.SelectedIndex = 2; await Task.Delay(200);
                    ((Button)editor.FindName("PlayButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(250);
                    Check(((Image)editor.FindName("ScenePreview")).Source.Height <= 720, "Automatic smooth preview limits playback composition size");
                    ((Button)editor.FindName("PlayButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(250);
                    Check(((Image)editor.FindName("ScenePreview")).Source.Height == SceneRenderer.Dimensions(p, 0).Height,
                        "Automatic smooth preview restores full dimensions after pausing");
                    qualityModes.SelectedIndex = 1; await Task.Delay(150);
                    await editor.PrepareAudioForTestingAsync();
                    var preparedAudio = player.Source;
                    ((Button)editor.FindName("PlayButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(100);
                    ((Button)editor.FindName("PlayButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var resumeClock = System.Diagnostics.Stopwatch.StartNew();
                    ((Button)editor.FindName("PlayButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(editor.IsPreviewPlaying && ReferenceEquals(preparedAudio, player.Source) && resumeClock.ElapsedMilliseconds < 250,
                        "Warm resume reuses opened audio and starts without rebuilding playback resources");
                    ((Button)editor.FindName("PlayButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    ThemeService.Apply("Light"); await Task.Delay(200);
                    Check(((SolidColorBrush)editor.Background).Color == Colors.White && ((SolidColorBrush)editor.Foreground).Color == Colors.Black && ((TextBlock)editor.FindName("ProjectTitle")).Foreground is SolidColorBrush { Color: var labelColor } && labelColor == Colors.Black, "Light mode updates existing windows and labels immediately");
                    Check(((Button)editor.FindName("PlayButton")).FontWeight == FontWeights.Normal, "Both themes retain regular-weight controls");
                    SaveVisual(editor, "artifacts/ui-tests/editor-light.png");
                    editor.ReturnToRecorder(); await Task.Delay(150); SaveVisual(launcher, "artifacts/ui-tests/recorder-light.png"); editor.ShowEditor();
                    var preferencesPath = Path.GetFullPath("artifacts/ui-tests/appearance.json");
                    new AppearanceSettings { Theme = "Light" }.Save(preferencesPath);
                    Check(AppearanceSettings.Load(preferencesPath).Theme == "Light", "Theme preference survives saving and reloading");
                    editor.ShowSettings(); await Task.Delay(150);
                    var settings = app.Windows.OfType<Window>().Single(w => w.Title == "设置");
                    SaveVisual(settings, "artifacts/ui-tests/settings.png"); settings.Close();
                    _ = app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => {
                        var exportDialog = app.Windows.OfType<Window>().Single(w => w.Title == "导出作品");
                        Check(exportDialog.ActualHeight <= SystemParameters.WorkArea.Height - 16, "Export options fit the work area");
                        SaveVisual(exportDialog, "artifacts/ui-tests/export-options.png"); exportDialog.Close();
                    }));
                    EditorDialogs.Export(p);
                    ThemeService.Apply("Dark"); await Task.Delay(150);
                    Check(((SolidColorBrush)editor.Background).Color == Colors.Black, "Dark mode can be restored without reopening the editor");
                }
                else editor.ShowEditor();
                Check(editor.IsVisible && !launcher.IsVisible, "Editor replaces recorder");
                editor.ReturnToRecorder(); Check(!editor.IsVisible && launcher.IsVisible, "Return to recorder retains application");
                editor.ShowEditor(); editor.Close(); Check(!editor.IsVisible && launcher.IsVisible, "Closing editor returns to recorder");
                ThemeService.Apply(originalTheme); editor.RequestExit(); Console.WriteLine("PASS Application exits from recorder");
            }
            catch (Exception ex) { failed = true; Console.Error.WriteLine(ex); app.Shutdown(1); }
        }));
        app.Run(); return failed ? 1 : 0;
    }
    private static void SaveVisual(Window window, string path)
    {
        window.UpdateLayout(); int width = (int)window.ActualWidth, height = (int)window.ActualHeight;
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream);
    }
    private static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL " + name); Console.WriteLine("PASS " + name); }
    private static byte[] PreviewPixels(ImageSource source)
    {
        var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) dc.DrawImage(source, new Rect(0, 0, 320, 180));
        var bitmap = new RenderTargetBitmap(320, 180, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var pixels = new byte[320 * 180 * 4]; bitmap.CopyPixels(pixels, 320 * 4, 0); return pixels;
    }
    private static void CheckCompletionButtons(Window window)
    {
        var root = (FrameworkElement)window.Content;
        foreach (var caption in new[] { "保存项目…", "进入编辑 →" })
        {
            var button = FindButton(window, caption) ?? throw new Exception("Missing completion action");
            var point = button.TransformToAncestor(root).Transform(new Point());
            Check(button.IsVisible && button.ActualHeight >= 40 && point.Y >= 0 && point.Y + button.ActualHeight <= root.ActualHeight + .5
                && point.X >= 0 && point.X + button.ActualWidth <= root.ActualWidth + .5, "Completion action is fully inside the visible card: " + caption);
        }
        var area = SystemParameters.WorkArea;
        Check(window.Top >= area.Top && window.Top + window.ActualHeight <= area.Bottom && window.Left >= area.Left && window.Left + window.ActualWidth <= area.Right, "Completion card fits inside the work area");
    }
    private static Button? FindButton(DependencyObject root, string content)
    {
        if (root is Button button && button.Content?.ToString() == content) return button;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var found = FindButton(VisualTreeHelper.GetChild(root, i), content); if (found != null) return found; }
        return null;
    }
}
