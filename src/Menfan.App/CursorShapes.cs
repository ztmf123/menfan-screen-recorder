using System.Runtime.InteropServices;

namespace Menfan.App;

internal sealed class CursorShapes(string folder)
{
    private readonly Dictionary<IntPtr, (string? Path, double X, double Y)> shapes = [];
    public (string? Path, double X, double Y, bool Visible) Current()
    {
        var info = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        if (!GetCursorInfo(ref info) || info.Cursor == IntPtr.Zero) return (null, 0, 0, false);
        if (shapes.TryGetValue(info.Cursor, out var cached)) return (cached.Path, cached.X, cached.Y, info.Flags == 1);
        if (shapes.Count > 64 || !GetIconInfo(info.Cursor, out var icon)) return (null, 0, 0, info.Flags == 1);
        try
        {
            int width = 32, height = 32;
            if (icon.Color != IntPtr.Zero) { using var original = System.Drawing.Image.FromHbitmap(icon.Color); width = original.Width; height = original.Height; }
            using var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var graphics = System.Drawing.Graphics.FromImage(bitmap); var dc = graphics.GetHdc();
            try { DrawIconEx(dc, 0, 0, info.Cursor, width, height, 0, IntPtr.Zero, 3); } finally { graphics.ReleaseHdc(dc); }
            Directory.CreateDirectory(folder); string path = Path.Combine(folder, "cursor-" + shapes.Count.ToString("D2") + ".png");
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png); var value = (path, (double)icon.HotspotX, (double)icon.HotspotY); shapes[info.Cursor] = value; return (value.path, value.Item2, value.Item3, info.Flags == 1);
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException or IOException) { return (null, 0, 0, info.Flags == 1); }
        finally { if (icon.Color != IntPtr.Zero) DeleteObject(icon.Color); if (icon.Mask != IntPtr.Zero) DeleteObject(icon.Mask); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct CursorInfo { public int Size, Flags; public IntPtr Cursor; public Native.Point Position; }
    [StructLayout(LayoutKind.Sequential)] private struct IconInfo { [MarshalAs(UnmanagedType.Bool)] public bool IsIcon; public uint HotspotX, HotspotY; public IntPtr Mask, Color; }
    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int width, int height, int step, IntPtr brush, int flags);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
}
