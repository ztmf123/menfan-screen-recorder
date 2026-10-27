using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Menfan.App;

internal sealed class RegionPicker : Forms.Form
{
    private Drawing.Point start;
    private Drawing.Rectangle selection;
    private bool dragging;
    public CaptureTarget? Result { get; private set; }
    public RegionPicker()
    {
        FormBorderStyle = Forms.FormBorderStyle.None;
        StartPosition = Forms.FormStartPosition.Manual;
        Bounds = Forms.SystemInformation.VirtualScreen;
        BackColor = Drawing.Color.Black;
        Opacity = .45;
        TopMost = true;
        Cursor = Forms.Cursors.Cross;
        DoubleBuffered = true;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Forms.Keys.Escape) Close(); };
        MouseDown += (_, e) => { if (e.Button != Forms.MouseButtons.Left) return; start = e.Location; dragging = true; Capture = true; };
        MouseMove += (_, e) => {
            if (!dragging) return;
            selection = Drawing.Rectangle.FromLTRB(Math.Min(start.X, e.X), Math.Min(start.Y, e.Y), Math.Max(start.X, e.X), Math.Max(start.Y, e.Y));
            Invalidate();
        };
        MouseUp += (_, _) => {
            if (!dragging) return;
            dragging = false;
            if (selection.Width >= 32 && selection.Height >= 32)
                Result = new($"区域 · {selection.Width / 2 * 2} × {selection.Height / 2 * 2}", Left + selection.X, Top + selection.Y, selection.Width / 2 * 2, selection.Height / 2 * 2);
            Close();
        };
    }
    protected override void OnPaint(Forms.PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Drawing.Pen(Drawing.Color.White, 3);
        e.Graphics.DrawRectangle(pen, selection);
        using var font = new Drawing.Font("Segoe UI", 18, Drawing.FontStyle.Bold);
        e.Graphics.DrawString("Drag to select · Esc to cancel", font, Drawing.Brushes.White, 32, 32);
    }
}
