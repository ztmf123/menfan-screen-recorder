namespace Menfan.App;

internal static class AppIcon
{
    public static System.Drawing.Icon CreateTrayIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Menfan.ScreenRecorder;component/Assets/menfan-icon-v3.ico"))
            ?? throw new InvalidOperationException("找不到应用图标资源。");
        using var stream = resource.Stream;
        using var icon = new System.Drawing.Icon(stream, 32, 32);
        return (System.Drawing.Icon)icon.Clone();
    }
}
