using System.Windows;
using Forms = System.Windows.Forms;

namespace Menfan.App;

internal sealed class RecordingPanelHider : IDisposable
{
    private readonly List<Window> hidden = [];
    private readonly HashSet<Window> closed = [];
    private readonly Forms.NotifyIcon tray;
    private readonly System.Drawing.Icon icon = AppIcon.CreateTrayIcon();
    private readonly Action stop;
    private bool disposed;
    public RecordingPanelHider(IEnumerable<Window> windows, Action stop)
    {
        this.stop = stop;
        tray = new Forms.NotifyIcon { Icon = icon, Text = "Menfan 正在录制 · 双击此图标停止", Visible = true };
        // A direct action avoids putting a tray menu into the recorded desktop.
        tray.MouseDoubleClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) RequestStop(); };
        try
        {
            foreach (var window in windows.Where(w => w.IsVisible).ToArray())
            {
                hidden.Add(window); window.Closed += WindowClosed; window.Hide();
            }
        }
        catch { Dispose(); throw; }
    }
    private void WindowClosed(object? sender, EventArgs e) { if (sender is Window window) closed.Add(window); }
    internal void RequestStop() { if (!disposed) stop(); }
    public void Dispose()
    {
        if (disposed) return; disposed = true; tray.Visible = false; tray.Dispose(); icon.Dispose();
        foreach (var window in hidden)
        {
            window.Closed -= WindowClosed;
            if (closed.Contains(window) || System.Windows.Application.Current.Dispatcher.HasShutdownStarted) continue;
            bool activated = window.ShowActivated;
            window.ShowActivated = false; window.Show(); window.ShowActivated = activated;
        }
        hidden.Clear(); closed.Clear();
    }
}
