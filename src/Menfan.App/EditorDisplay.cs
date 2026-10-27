using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Menfan.App;

public partial class MainWindow
{
    private bool editorFullscreen, previewFullscreen;
    private WindowState previousWindowState;
    private WindowStyle previousWindowStyle;
    private ResizeMode previousResizeMode;
    private Rect previousBounds;
    private readonly DispatcherTimer previewChromeTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    internal bool IsPreviewFullscreen => previewFullscreen;
    internal bool IsEditorFullscreen => editorFullscreen;
    internal bool IsPreviewPlaying => playing;
    private void InitializeDisplayUI()
    {
        previewChromeTimer.Tick += (_, _) => {
            if (!previewFullscreen || FullscreenControls.IsMouseOver || Mouse.Captured != null) return;
            FullscreenControls.Visibility = Visibility.Hidden; FullscreenOverlay.Cursor = Cursors.None; previewChromeTimer.Stop();
        };
        Closed += (_, _) => previewChromeTimer.Stop();
    }
    public void ShowSettings() { if (hiddenRecordingPanels == null) ThemeService.ShowSettings(IsVisible ? this : Launcher); }
    private void Settings_Click(object sender, RoutedEventArgs e) => ShowSettings();
    private void EditorFullscreen_Click(object sender, RoutedEventArgs e)
    {
        if (editorFullscreen) { editorFullscreen = false; RestoreWindowIfNeeded(); }
        else { EnterBorderlessWindow(); editorFullscreen = true; }
        EditorFullscreenButton.Content = editorFullscreen ? "退出全屏 F11" : "全屏剪辑 F11";
        Focus();
    }
    private void PreviewFullscreen_Click(object sender, RoutedEventArgs e) => SetPreviewFullscreen(!previewFullscreen);
    internal void SetPreviewFullscreen(bool enabled)
    {
        if (enabled == previewFullscreen || enabled && (project == null || busy)) return;
        if (enabled) EnterBorderlessWindow();
        previewFullscreen = enabled;
        EditorLayout.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        FullscreenOverlay.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (enabled) ShowPreviewControls();
        else { previewChromeTimer.Stop(); FullscreenOverlay.Cursor = Cursors.Arrow; RestoreWindowIfNeeded(); }
        UpdateLayout(); Focus(); UpdateTransport(); _ = RefreshPreviewAsync();
    }
    private void EnterBorderlessWindow()
    {
        if (editorFullscreen || previewFullscreen) return;
        previousWindowState = WindowState == WindowState.Minimized ? WindowState.Normal : WindowState;
        previousWindowStyle = WindowStyle; previousResizeMode = ResizeMode;
        previousBounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        WindowState = WindowState.Normal; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; WindowState = WindowState.Maximized;
    }
    private void RestoreWindowIfNeeded()
    {
        if (editorFullscreen || previewFullscreen) return;
        WindowState = WindowState.Normal; WindowStyle = previousWindowStyle; ResizeMode = previousResizeMode;
        if (!previousBounds.IsEmpty && double.IsFinite(previousBounds.X) && double.IsFinite(previousBounds.Y))
        { Left = previousBounds.Left; Top = previousBounds.Top; Width = previousBounds.Width; Height = previousBounds.Height; }
        WindowState = previousWindowState;
    }
    private void ExitDisplayModes()
    {
        if (previewFullscreen) SetPreviewFullscreen(false);
        if (editorFullscreen) { editorFullscreen = false; RestoreWindowIfNeeded(); EditorFullscreenButton.Content = "全屏剪辑 F11"; }
    }
    internal bool HandleDisplayShortcut(Key key)
    {
        if (key == Key.Escape)
        {
            if (previewFullscreen) SetPreviewFullscreen(false);
            else if (editorFullscreen) EditorFullscreen_Click(this, new());
            else return false;
            return true;
        }
        if (key == Key.F11)
        {
            if (previewFullscreen) SetPreviewFullscreen(false); else EditorFullscreen_Click(this, new());
            return true;
        }
        if (key == Key.F && project != null && !busy) { SetPreviewFullscreen(!previewFullscreen); return true; }
        return false;
    }
    private void ShowPreviewControls()
    {
        FullscreenControls.Visibility = Visibility.Visible; FullscreenOverlay.Cursor = Cursors.Arrow;
        previewChromeTimer.Stop(); previewChromeTimer.Start();
    }
    private void Fullscreen_MouseMove(object sender, MouseEventArgs e) => ShowPreviewControls();
    private void FullscreenImage_Click(object sender, MouseButtonEventArgs e)
    { if (e.ChangedButton == MouseButton.Left) { ShowPreviewControls(); Play_Click(sender, e); e.Handled = true; } }
}
