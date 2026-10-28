using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace Menfan.App;

// Keep controls usable on the desktop while excluding their HWNDs from capture.
internal sealed class RecordingPanelHider : IDisposable
{
    private readonly Dictionary<Window, uint> protectedWindows = [];
    private readonly Forms.NotifyIcon tray;
    private readonly System.Drawing.Icon icon = AppIcon.CreateTrayIcon();
    private readonly Action stop;
    private bool disposed;
    private static RecordingPanelHider? active;
    static RecordingPanelHider()
    {
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => { if (sender is Window window) active?.Protect(window); }));
    }
    public RecordingPanelHider(IEnumerable<Window> windows, Action stop)
    {
        this.stop = stop;
        tray = new Forms.NotifyIcon { Icon = icon, Text = "Menfan 正在录制 · 双击此图标停止", Visible = true };
        tray.MouseDoubleClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) RequestStop(); };
        try { active = this; foreach (var window in windows.Where(w => w.IsVisible).ToArray()) Protect(window); }
        catch { Dispose(); throw; }
    }
    private void Protect(Window window)
    {
        if (disposed || protectedWindows.ContainsKey(window)) return;
        var handle = new WindowInteropHelper(window).EnsureHandle();
        Native.GetWindowDisplayAffinity(handle, out uint previous);
        if (!Native.SetWindowDisplayAffinity(handle, 0x11))
            throw new InvalidOperationException("Windows 无法将面板排除出录制，请关闭“不录制面板”后重试。");
        protectedWindows.Add(window, previous);
    }
    internal void RequestStop() { if (!disposed) stop(); }
    public void Dispose()
    {
        if (disposed) return; disposed = true; if (active == this) active = null;
        tray.Visible = false; tray.Dispose(); icon.Dispose();
        foreach (var pair in protectedWindows)
        { var handle = new WindowInteropHelper(pair.Key).Handle; if (handle != IntPtr.Zero) Native.SetWindowDisplayAffinity(handle, pair.Value); }
        protectedWindows.Clear();
    }
}
