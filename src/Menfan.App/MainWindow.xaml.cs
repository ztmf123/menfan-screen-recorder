using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Menfan.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Menfan.App;

public partial class MainWindow : Window
{
    private MediaTools? tools;
    private Project? project;
    private string? projectPath;
    private Recorder? recorder;
    private bool busy, playing, preparingPlayback, initialized, exitRequested, updatingSeek, rendering, renderPending, loading;
    private double position, playbackStart;
    private readonly Stopwatch playbackClock = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly RecordingSettings recordingSettings = RecordingSettings.Load();
    private readonly DesktopVisibility desktopVisibility = new();
    private NotesWindow? notes;
    private RecordingCompleteWindow? completion;
    private RecordingPanelHider? hiddenRecordingPanels;
    private IntPtr handle;
    private bool dirty;
    private string module = "画面";
    private MediaFrameReader? screenReader, cameraReader, backgroundReader;
    private double cameraDuration, backgroundDuration;
    private readonly PreviewComposer previewComposer = new();
    private readonly NativeVideoPreview nativeVideo = new();
    private readonly SceneRenderer renderer = new();
    private readonly Dictionary<MediaFrameReader, (double Time, bool Exact, BitmapSource Frame)> previewFrames = new();
    private bool propertyPreviewQueued;
    private readonly Stopwatch timelineClock = Stopwatch.StartNew();
    private int previewRevision;
    private CancellationTokenSource mediaLifetime = new();
    private CancellationTokenSource renderLifetime = new();
    private CancellationTokenSource? exportCancel;
    private readonly Stack<string> undo = new(), redo = new();
    private CaptureTarget? captureTarget;
    private int captureMode, captureFps;
    private bool microphone, systemSound = true;
    private bool canvasDragging;
    private bool fitTimeline;
    internal int PreviewPresentations { get; private set; }
    internal bool KeepPreviewPlayingForDiagnostics { get; set; }
    internal double PreviewDecodeMilliseconds { get; private set; }
    internal double PreviewComposeMilliseconds { get; private set; }
    internal bool NativePreviewReady => nativeVideo.Ready;
    public RecorderWindow? Launcher { get; set; }
    public bool IsRecording => recorder != null;
    public bool IsPaused => recorder?.IsPaused == true;
    public bool IsBusy => busy;
    public bool MicrophoneEnabled => microphone;
    public bool SystemSoundEnabled => systemSound;
    public int RecordingFpsIndex => captureFps;
    public bool HasProject => project != null;
    public bool HidePanelsWhileRecording => recordingSettings.HidePanelsWhileRecording;
    public string RecorderStatus => Status.Text;
    public string RecorderClock => TimeSpan.FromSeconds(recorder?.Elapsed ?? 0).ToString(@"hh\:mm\:ss");

    public MainWindow()
    {
        InitializeComponent();
        InitializeDisplayUI();
        microphone = recordingSettings.MicrophoneEnabled; systemSound = recordingSettings.SystemEnabled; captureFps = Math.Clamp(recordingSettings.FpsIndex, 0, 1);
        Loaded += (_, _) => InitializeController();
        Activated += (_, _) => { if (project != null) _ = RefreshPreviewAsync(); };
        Deactivated += (_, _) => { if (playing && !KeepPreviewPlayingForDiagnostics) Pause(); };
        Closed += (_, _) => {
            CompositionTarget.Rendering -= RenderPropertyPreview;
            RestoreRecordingPanels(); desktopVisibility.Dispose(); notes?.Close(); completion?.Close();
            Native.UnregisterHotKey(handle, 1); Native.UnregisterHotKey(handle, 2); timer.Stop(); Player.Close();
            mediaLifetime.Cancel(); renderLifetime.Cancel(); previewComposer.Dispose(); nativeVideo.Dispose(); screenReader?.Dispose(); cameraReader?.Dispose(); backgroundReader?.Dispose();
            if (Launcher != null) Launcher.AllowClose = true;
            System.Windows.Application.Current.Shutdown();
        };
        timer.Tick += async (_, _) => {
            if (recorder != null && recorder.HasExited && !busy) await StopRecordingAsync();
            if (!playing || project == null || !IsActive && !KeepPreviewPlayingForDiagnostics) return;
            position = playbackStart + playbackClock.Elapsed.TotalSeconds;
            if (position >= project.EditedDuration) { position = project.EditedDuration; Pause(); }
            UpdateTransport(); await RefreshPreviewAsync();
        };
        Timeline.SeekRequested += SeekTo;
        Timeline.SelectionChanged += () => { if (Timeline.SelectedEffect != null) module = "效果"; BuildProperties(); };
        Timeline.BeginEdit += Snapshot;
        Timeline.Edited += () => { Dirty(); BuildProperties(); };
        Timeline.MoveRequested += MoveClip;
        Timeline.TrimRequested += TrimClip;
        Timeline.RestoreJunction += RestoreCut;
        CropOverlay.BeginEdit += Snapshot;
        CropOverlay.Changed += (left, top, right, bottom) => {
            if (project == null) return;
            project.Style.CropLeft = left; project.Style.CropTop = top; project.Style.CropRight = right; project.Style.CropBottom = bottom; Dirty();
        };
        TimelineScroll.ScrollChanged += (_, e) => {
            if (project == null || Math.Abs(e.ViewportWidthChange) < .1) return;
            if (fitTimeline && TimelineScroll.ViewportWidth > 0) { Timeline.PixelsPerSecond = Math.Clamp((TimelineScroll.ViewportWidth - 110) / project.EditedDuration, 8, 300); fitTimeline = false; }
            UpdateTransport();
        };
        BuildProperties();
    }
    public void InitializeController()
    {
        if (initialized) return; initialized = true;
        var path = MediaTools.Locate() ?? recordingSettings.FfmpegPath;
        tools = path != null && File.Exists(path) && File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "ffprobe.exe")) ? new(path) : null;
        renderer.FfmpegPath = previewComposer.FfmpegPath = tools?.Ffmpeg;
        captureTarget = GetCaptureTargets(0).FirstOrDefault();
        handle = new WindowInteropHelper(this).EnsureHandle(); HwndSource.FromHwnd(handle)?.AddHook(WindowMessage);
        bool r = Native.RegisterHotKey(handle, 1, 0x4003, 0x52), m = Native.RegisterHotKey(handle, 2, 0x4003, 0x4D);
        Status.Text = tools == null ? "首次录制时请选择 FFmpeg" : !r || !m ? "部分快捷键已被占用，请使用录制按钮" : "准备就绪 · Ctrl+Alt+R 开始 / 停止";
        timer.Start();
    }
    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr w, IntPtr l, ref bool handled)
    {
        if (message != 0x0312) return IntPtr.Zero;
        if (w.ToInt32() == 1 && !busy) { ToggleRecording(); handled = true; }
        if (w.ToInt32() == 2) { AddRecordingMarker(); handled = true; }
        return IntPtr.Zero;
    }
    public CaptureTarget[] GetCaptureTargets(int mode)
    {
        captureMode = mode; captureTarget = null;
        return mode == 0 ? Forms.Screen.AllScreens.Select(s => new CaptureTarget($"{(s.Primary ? "主显示器" : "显示器")} · {s.Bounds.Width} × {s.Bounds.Height}", s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height)).ToArray()
            : mode == 1 ? Native.Windows().ToArray() : [];
    }
    public void SelectCaptureTarget(CaptureTarget target) => captureTarget = target;
    public void ClearCaptureTarget() => captureTarget = null;
    public async Task<CaptureTarget[]> GetCaptureDevicesAsync()
    {
        captureTarget = null;
        if (!RequireTools()) return [];
        try { return (await VideoDevices.ListAsync(tools!)).Select(name => new CaptureTarget(name, 0, 0, 1280, 720, DeviceName: name)).ToArray(); }
        catch (Exception ex) { ShowError(ex.Message); return []; }
    }
    public CaptureTarget? PickCaptureRegion()
    {
        captureMode = 2; using var picker = new RegionPicker(); picker.ShowDialog();
        if (picker.Result != null) captureTarget = picker.Result; return picker.Result;
    }
    public void ConfigureRecording(bool mic, bool system, int fpsIndex)
    {
        microphone = recordingSettings.MicrophoneEnabled = mic; systemSound = recordingSettings.SystemEnabled = system;
        captureFps = recordingSettings.FpsIndex = Math.Clamp(fpsIndex, 0, 1);
        try { recordingSettings.Save(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status.Text = "本次设置已生效，但未能记住设置。"; }
    }
    public void ConfigureDevices() { if (!busy && recorder == null && RequireTools()) { try { new DeviceWindow(recordingSettings, tools!).ShowDialog(); } catch (Exception ex) { ShowError(ex.Message); } } }
    public void ConfigureHiddenPanels(bool enabled)
    {
        if (busy || recorder != null) return;
        recordingSettings.HidePanelsWhileRecording = enabled;
        Status.Text = enabled ? "面板保持可见，但不出现在录制画面中" : "录制时显示控制面板";
        try { recordingSettings.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status.Text = "本次设置已生效，但未能保存。"; }
    }
    public async void ToggleRecording() { if (busy) return; if (recorder == null) await StartRecordingAsync(); else await StopRecordingAsync(); }
    public async void TogglePause()
    {
        if (recorder == null || busy) return;
        busy = true;
        try { if (recorder.IsPaused) await recorder.ResumeAsync(); else await recorder.PauseAsync(); Status.Text = recorder.IsPaused ? "已暂停 · 点击继续" : "正在录制"; }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { busy = false; }
    }
    public void ShowNotes()
    {

        if (notes == null) { notes = new NotesWindow(); notes.Closed += (_, _) => notes = null; }
        notes.Show(); notes.Activate();
    }
    public void ToggleDesktopIcons() { try { desktopVisibility.Toggle(); } catch (Exception ex) { ShowError(ex.Message); } }
    public void AddRecordingMarker() { recorder?.AddMarker(); Status.Text = $"已添加标记 · {RecorderClock}"; }
    public void ImportVideo() => Import_Click(this, new());
    public void OpenProject() => Open_Click(this, new());
    public void ShowEditor()
    {
        if (busy || recorder != null) return;
        Launcher?.Hide(); completion?.Close(); Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); _ = RefreshPreviewAsync();
    }
    public void ReturnToRecorder() { if (busy || recorder != null) return; Pause(); ExitDisplayModes(); Hide(); Launcher?.Show(); Launcher?.Activate(); }
    public void RequestExit() { exitRequested = true; Close(); }
    private void Back_Click(object sender, RoutedEventArgs e) => ReturnToRecorder();
    private bool RequireTools()
    {
        if (tools != null) return true;
        if (MediaTools.Locate() is { } path) { tools = new(path); renderer.FfmpegPath = previewComposer.FfmpegPath = path; return true; }
        var dialog = new OpenFileDialog { Title = "请选择 ffmpeg.exe（同目录需要 ffprobe.exe）", Filter = "FFmpeg|ffmpeg.exe" };
        if (dialog.ShowDialog() != true) return false;
        if (!File.Exists(Path.Combine(Path.GetDirectoryName(dialog.FileName)!, "ffprobe.exe"))) { ShowError("同目录未找到 ffprobe.exe。"); return false; }
        tools = new(dialog.FileName); recordingSettings.FfmpegPath = dialog.FileName;
        renderer.FfmpegPath = previewComposer.FfmpegPath = dialog.FileName;
        try { recordingSettings.Save(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status.Text = "FFmpeg 已选择，本次无法保存路径。"; }
        return true;
    }
    private async Task StartRecordingAsync()
    {
        if (!RequireTools() || !ConfirmSave()) return;
        if (captureTarget == null) { ShowError("请先选择录制来源。"); return; }
        if (captureTarget.DeviceName != null && captureTarget.DeviceName == recordingSettings.CameraName) { ShowError("同一摄像设备不能同时作为主画面和画中画来源。"); return; }
        bool hidePanels = recordingSettings.HidePanelsWhileRecording;
        if (hidePanels && captureTarget.Handle != IntPtr.Zero && System.Windows.Application.Current.Windows.OfType<Window>().Any(w => new WindowInteropHelper(w).Handle == captureTarget.Handle))
        { ShowError("排除面板模式不能录制本软件窗口，请选择显示器、区域或其他应用。"); return; }
        busy = true; Pause(); ExitDisplayModes(); completion?.Close();
        Recorder? pending = null;
        try
        {
            for (int i = 3; i > 0; i--) { Status.Text = $"{i} 秒后开始录制…"; await Task.Delay(1000); }
            Status.Text = "正在启动录制…";
            Launcher?.RefreshRecordingState();
            Hide(); Launcher?.Show(); Launcher?.ExcludeFromCapture(false);
            if (hidePanels)
            {
                hiddenRecordingPanels = new RecordingPanelHider(System.Windows.Application.Current.Windows.OfType<Window>(), () => {
                    if (recorder != null && !busy) ToggleRecording();
                });
                await Task.Run(() => Native.DwmFlush());
            }
            pending = new();
            string folder = Path.Combine(recordingSettings.Folder, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_" + Guid.NewGuid().ToString("N")[..6]);
            await pending.StartAsync(tools!, captureTarget, captureFps == 1 ? 60 : 30, systemSound, microphone, folder,
                recordingSettings.SystemDevice, recordingSettings.MicrophoneDevice, recordingSettings.CameraName);
            recorder = pending; pending = null; Status.Text = "正在录制 · Ctrl+Alt+R 停止 · Ctrl+Alt+M 标记";
        }
        catch (Exception ex)
        {
            try { pending?.Dispose(); }
            finally { RestoreRecordingPanels(); Launcher?.ExcludeFromCapture(false); Launcher?.Show(); }
            ShowError(ex.Message);
        }
        finally { busy = false; Launcher?.RefreshRecordingState(); }
    }
    private async Task StopRecordingAsync()
    {
        if (recorder == null || busy) return;
        busy = true; Status.Text = "正在封装屏幕、声音与暂停片段…";
        try
        {
            var p = await recorder.StopAsync(tools!);
            RestoreRecordingPanels();
            p.Save(Path.Combine(Path.GetDirectoryName(p.MediaPath)!, "project.menfan"));
            await LoadProjectAsync(p); projectPath = Path.Combine(Path.GetDirectoryName(p.MediaPath)!, "project.menfan");
            Status.Text = "录制完成 · 项目已自动保存";
            completion?.Close(); completion = new RecordingCompleteWindow(p, tools!, ShowEditor, () => SaveProject(true)); completion.Show();
        }
        catch (Exception ex) { RestoreRecordingPanels(); ShowError(ex.Message); }
        finally { recorder.Dispose(); recorder = null; RestoreRecordingPanels(); busy = false; Launcher?.ExcludeFromCapture(false); desktopVisibility.Dispose(); notes?.Hide(); Launcher?.Show(); }
    }
    private void RestoreRecordingPanels() { hiddenRecordingPanels?.Dispose(); hiddenRecordingPanels = null; }
    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (busy || recorder != null || !RequireTools() || !ConfirmSave()) return;
        var dialog = new OpenFileDialog { Filter = "视频文件|*.mp4;*.mkv;*.mov;*.avi;*.webm|全部文件|*.*" };
        if (dialog.ShowDialog() != true) return;
        bool success = false;
        await GuardAsync(async () => { var info = await tools!.ProbeAsync(dialog.FileName); var p = new Project { MediaPath = dialog.FileName, Width = info.Width, Height = info.Height, Duration = info.Duration, FrameRate = info.FrameRate,
            Clips = [new() { End = info.Duration }] }; p.ResetVisualEdits(); await LoadProjectAsync(p); projectPath = null; dirty = true; success = true; });
        if (success) ShowEditor();
    }
    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (busy || recorder != null || !RequireTools() || !ConfirmSave()) return;
        var dialog = new OpenFileDialog { Filter = "Menfan 项目|*.menfan" }; if (dialog.ShowDialog() != true) return;
        bool success = false;
        await GuardAsync(async () => { await LoadProjectAsync(Project.Load(dialog.FileName)); projectPath = dialog.FileName; success = true; });
        if (success) ShowEditor();
    }
    private async Task LoadProjectAsync(Project p)
    {
        p.Validate(); Pause(); nativeVideo.Dispose(); mediaLifetime.Cancel(); screenReader?.Dispose(); cameraReader?.Dispose(); backgroundReader?.Dispose();
        mediaLifetime = new(); project = p; dirty = false; undo.Clear(); redo.Clear(); position = 0;
        Timeline.Project = p; Timeline.SelectedEffect = null; Timeline.SelectedClipIndex = 0; Timeline.Peaks = null; thumbnails.Clear(); fitTimeline = true;
        if (TimelineScroll.ViewportWidth > 0) { Timeline.PixelsPerSecond = Math.Clamp((TimelineScroll.ViewportWidth - 110) / p.EditedDuration, 8, 300); fitTimeline = false; }
        await PrepareReadersAsync();
        loading = true; foreach (ComboBoxItem item in AspectPicker.Items) if (item.Tag.ToString() == p.Style.Aspect) AspectPicker.SelectedItem = item; loading = false;
        ProjectTitle.Text = Path.GetFileNameWithoutExtension(p.MediaPath); EmptyState.Visibility = Visibility.Collapsed;
        BuildProperties(); UpdateTransport(); _ = LoadTimelineAssetsAsync(p, mediaLifetime.Token);
        _ = OpenNativePreviewAsync(p); await RefreshPreviewAsync();
    }
    private async Task OpenNativePreviewAsync(Project p)
    {
        if (await nativeVideo.OpenAsync(p.MediaPath) && ReferenceEquals(project, p))
            await RefreshPreviewAsync();
    }
    internal Task LoadForTestingAsync(Project p) => LoadProjectAsync(p);
    internal Task PrepareAudioForTestingAsync() => PrepareAudioPreviewAsync();
    private async Task PrepareReadersAsync()
    {
        if (project == null || tools == null) return;
        renderLifetime.Cancel(); renderLifetime = new(); previewRevision++;
        previewFrames.Clear();
        int height = QualityPicker.SelectedIndex == 1 ? project.Height : Math.Min(project.Height, 720);
        screenReader?.Dispose(); screenReader = new(tools, project.MediaPath, (int)(project.Width * height / (double)project.Height), height, Math.Min(30, project.FrameRate));
        timer.Interval = TimeSpan.FromSeconds(1 / Math.Clamp(project.FrameRate, 1, 60));
        cameraReader?.Dispose(); cameraReader = null;
        if (project.CameraPath != null) { var info = await tools.ProbeAsync(project.CameraPath); cameraDuration = info.Duration; int cameraWidth = QualityPicker.SelectedIndex == 1 ? info.Width : Math.Min(640, info.Width); cameraReader = new(tools, project.CameraPath, cameraWidth, Math.Max(2, cameraWidth * info.Height / info.Width), Math.Min(30, info.FrameRate)); }
        backgroundReader?.Dispose(); backgroundReader = null;
        if (project.Style.BackgroundKind == "Video" && project.Style.BackgroundPath != null)
        { var info = await tools.ProbeAsync(project.Style.BackgroundPath); backgroundDuration = info.Duration; int bgHeight = QualityPicker.SelectedIndex == 1 ? info.Height : Math.Min(720, info.Height); backgroundReader = new(tools, project.Style.BackgroundPath, Math.Max(2, bgHeight * info.Width / info.Height), bgHeight, Math.Min(30, info.FrameRate)); }
    }
    private async Task<BitmapSource> PreviewFrameAsync(MediaFrameReader reader, double time, CancellationToken ct, bool exact = false)
    {
        time = Math.Max(0, time);
        if (!playing && previewFrames.TryGetValue(reader, out var cached) && Math.Abs(cached.Time - time) < .000001 && (!exact || cached.Exact))
            return cached.Frame;
        var frame = await reader.GetFrameAsync(time, ct, exact);
        ct.ThrowIfCancellationRequested();
        previewFrames[reader] = (time, exact, frame);
        return frame;
    }
    private void RequestPropertyPreview()
    {
        if (propertyPreviewQueued) return;
        propertyPreviewQueued = true;
        CompositionTarget.Rendering += RenderPropertyPreview;
    }
    private void RenderPropertyPreview(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= RenderPropertyPreview;
        propertyPreviewQueued = false;
        _ = RefreshPreviewAsync();
    }
    private async Task RefreshPreviewAsync()
    {
        if (project == null || screenReader == null || !IsVisible || preparingPlayback) return;
        if (rendering) { renderPending = true; return; }
        rendering = true;
        try
        {
            do
            {
                renderPending = false; var p = project.CopyForRendering(); var ct = renderLifetime.Token; int revision = previewRevision;
                double time = position;
                var sourceFrames = screenReader; var cameraFrames = cameraReader; var backgroundFrames = backgroundReader;
                var state = SceneEvaluator.Evaluate(p, time);
                var stage = Stopwatch.StartNew();
                bool live = nativeVideo.Matches(p.Width, p.Height);
                ImageSource source = live ? nativeVideo.Frame(p, time, playing) : await PreviewFrameAsync(sourceFrames, Math.Min(p.Duration - .02, state.SourceTime), ct, exact: !playing);
                PreviewDecodeMilliseconds += stage.Elapsed.TotalMilliseconds;
                if (module == "裁切" && !previewFullscreen)
                {
                    ct.ThrowIfCancellationRequested(); if (revision != previewRevision) { renderPending = true; continue; } ScenePreview.Source = source;
                    CropOverlay.SourceAspect = p.Width / (double)p.Height;
                    CropOverlay.LeftCrop = p.Style.CropLeft; CropOverlay.TopCrop = p.Style.CropTop;
                    CropOverlay.RightCrop = p.Style.CropRight; CropOverlay.BottomCrop = p.Style.CropBottom; CropOverlay.InvalidateVisual();
                    continue;
                }
                BitmapSource? cameraFrame = p.Camera.Visible && cameraFrames != null && state.SourceTime >= p.CameraOffset ? await PreviewFrameAsync(cameraFrames, Math.Min(cameraDuration - .02, state.SourceTime - p.CameraOffset), ct) : null;
                BitmapSource? backgroundFrame = backgroundFrames != null ? await PreviewFrameAsync(backgroundFrames, time % backgroundDuration, ct) : null;
                ct.ThrowIfCancellationRequested();
                int height = QualityPicker.SelectedIndex == 1 ? 0 : previewFullscreen ? Math.Min(p.Height, 1080) : 540;
                var dimensions = SceneRenderer.Dimensions(p, height);
                stage.Restart();
                var composed = live || !playing ? renderer.Compose(p, time, source, cameraFrame, backgroundFrame, dimensions.Width, dimensions.Height, freeze: false)
                    : await previewComposer.ComposeAsync(p, time, (BitmapSource)source, cameraFrame, backgroundFrame, dimensions.Width, dimensions.Height, ct);
                PreviewComposeMilliseconds += stage.Elapsed.TotalMilliseconds;
                ct.ThrowIfCancellationRequested();
                if (revision != previewRevision) { renderPending = true; continue; }
                ScenePreview.Source = composed;
                PreviewPresentations++;
            } while (renderPending && (IsActive || KeepPreviewPlayingForDiagnostics));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Pause(); Status.Text = "预览失败：" + ex.Message.Split('\n')[0]; }
        finally { rendering = false; if (renderPending && IsVisible) _ = RefreshPreviewAsync(); }
    }
    private void SeekTo(double time)
    {
        if (project == null || busy) return;
        Pause(); previewRevision++; renderLifetime.Cancel(); renderLifetime = new();
        position = Math.Clamp(time, 0, project.EditedDuration); playbackStart = position;
        if (Player.Source != null) Player.Position = TimeSpan.FromSeconds(position);
        UpdateTransport(); _ = RefreshPreviewAsync();
    }
    private void UpdateTransport()
    {
        if (project == null) return;
        updatingSeek = true; Seek.Maximum = project.EditedDuration; Seek.Value = position; updatingSeek = false;
        if (!playing || !previewFullscreen && timelineClock.ElapsedMilliseconds >= 66)
        {
            timelineClock.Restart(); Timeline.CurrentTime = position;
            Timeline.Width = Math.Max(Math.Max(800, TimelineScroll.ViewportWidth), 110 + project.EditedDuration * Timeline.PixelsPerSecond); Timeline.InvalidateVisual();
        }
        PositionText.Text = $"{TimeSpan.FromSeconds(position):mm\\:ss} / {TimeSpan.FromSeconds(project.EditedDuration):mm\\:ss}";
    }
    private async void Play_Click(object sender, RoutedEventArgs e)
    {
        if (playing) { Pause(); return; }
        if (project == null || tools == null || busy) return;
        if (position >= project.EditedDuration - .02) position = 0;
        try
        {
            busy = true; preparingPlayback = true; Status.Text = "准备预览…";
            await PrepareAudioPreviewAsync();
            await nativeVideo.OpenAsync(project.MediaPath);
            if (!IsActive && !KeepPreviewPlayingForDiagnostics) return;
            playbackStart = position; playbackClock.Restart(); playing = true; preparingPlayback = false; previewRevision++; PlayButton.Content = "Ⅱ";
            if (Player.Source != null) { Player.Position = TimeSpan.FromSeconds(position); Player.Play(); }
            Status.Text = "播放预览";
            await RefreshPreviewAsync();
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { preparingPlayback = false; busy = false; }
    }
    private async Task PrepareAudioPreviewAsync()
    {
        if (project == null || tools == null) return;
        if (project.Style.Mute) { Player.Close(); Player.Source = null; return; }
        var files = new[] { project.MediaPath, project.SystemAudioPath, project.MicrophonePath }.Where(p => p != null).Select(p => new FileInfo(p!)).Select(f => new { f.FullName, f.Length, f.LastWriteTimeUtc }).ToArray();
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { files, project.SystemAudioOffset, project.MicrophoneOffset, project.Clips, project.SystemVolume, project.MicrophoneVolume }))));
        var folder = Path.Combine(Path.GetTempPath(), "MenfanAudio"); Directory.CreateDirectory(folder); string path = Path.Combine(folder, key + ".m4a");
        if (Player.Source?.LocalPath == path) return;
        Player.Close(); Player.Source = null;
        var info = await tools.ProbeAsync(project.MediaPath);
        if (!info.HasAudio && project.SystemAudioPath == null && project.MicrophonePath == null) return;
        if (!File.Exists(path)) await tools.ExportAsync(project, path, new(AudioOnly: true));
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RoutedEventHandler onOpened = (_, _) => opened.TrySetResult();
        EventHandler<ExceptionRoutedEventArgs> onFailed = (_, e) => opened.TrySetException(e.ErrorException);
        Player.MediaOpened += onOpened; Player.MediaFailed += onFailed;
        try
        {
            Player.Source = new Uri(path); Player.Volume = 1; Player.Play();
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(8)); Player.Pause();
        }
        finally { Player.MediaOpened -= onOpened; Player.MediaFailed -= onFailed; }
    }
    private void Pause() { nativeVideo.Pause(); playing = false; playbackClock.Stop(); Player.Pause(); PlayButton.Content = "▶"; }
    private void Seek_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (!updatingSeek) SeekTo(e.NewValue); }
    private void Player_Failed(object sender, ExceptionRoutedEventArgs e) { Status.Text = "声音预览失败：" + e.ErrorException.Message; }
    private void Save_Click(object sender, RoutedEventArgs e) => SaveProject();
    private bool SaveProject(bool choose = false)
    {
        if (project == null) return false;
        try
        {
            if (choose || projectPath == null) { var dialog = new SaveFileDialog { Filter = "Menfan 项目|*.menfan", FileName = "Menfan-project.menfan" }; if (dialog.ShowDialog() != true) return false; projectPath = dialog.FileName; }
            project.Save(projectPath!); dirty = false; Status.Text = "项目已保存"; return true;
        }
        catch (Exception ex) { ShowError(ex.Message); return false; }
    }
    private bool ConfirmSave()
    {
        if (!dirty) return true;
        var choice = MessageBox.Show("保存当前项目的修改？", "Menfan", MessageBoxButton.YesNoCancel);
        return choice == MessageBoxResult.No || choice == MessageBoxResult.Yes && SaveProject();
    }
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (project == null || tools == null || busy || recorder != null) return;
        var choice = EditorDialogs.Export(project); if (choice == null) return;
        Pause(); exportCancel = new(); ExportProgress.Value = 0; ExportProgress.Visibility = CancelExport.Visibility = Visibility.Visible;
        await GuardAsync(async () => {
            var copy = JsonSerializer.Deserialize<Project>(JsonSerializer.Serialize(project))!;
            await SceneExporter.ExportAsync(tools, copy, choice.Value.Path, choice.Value.Options,
                new Progress<double>(p => { ExportProgress.Value = p; Status.Text = $"导出作品 · {p:P0}"; }), exportCancel.Token);
            Status.Text = "导出完成 · " + choice.Value.Path;
            if (MessageBox.Show("导出完成。是否在文件夹中显示？", "Menfan", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + choice.Value.Path + "\"") { UseShellExecute = true });
        });
        ExportProgress.Visibility = CancelExport.Visibility = Visibility.Collapsed; exportCancel.Dispose(); exportCancel = null;
    }
    private void CancelExport_Click(object sender, RoutedEventArgs e) => exportCancel?.Cancel();
    private async Task GuardAsync(Func<Task> action)
    {
        busy = true;
        try { await action(); }
        catch (OperationCanceledException) { Status.Text = "操作已取消"; }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { busy = false; }
    }
    private void ShowError(string message) { Status.Text = message.Split('\n')[0]; MessageBox.Show(message.Length > 4500 ? message[^4500..] : message, "Menfan", MessageBoxButton.OK, MessageBoxImage.Error); }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!exitRequested && previewFullscreen) { e.Cancel = true; SetPreviewFullscreen(false); return; }
        if (!exitRequested && Launcher != null) { e.Cancel = true; ReturnToRecorder(); return; }
        if (busy) { e.Cancel = true; exitRequested = false; Status.Text = "请等待当前操作结束，可取消导出。"; return; }
        if (recorder != null) { e.Cancel = true; await StopRecordingAsync(); if (ConfirmSave()) { exitRequested = true; Close(); } return; }
        if (!ConfirmSave()) { e.Cancel = true; exitRequested = false; }
    }
}
