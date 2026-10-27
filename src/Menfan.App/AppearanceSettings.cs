using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Menfan.App;

internal sealed class AppearanceSettings
{
    public string Theme { get; set; } = "Dark";
    private static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Menfan", "appearance.json");
    public static AppearanceSettings Load(string? path = null)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<AppearanceSettings>(File.ReadAllText(path ?? DefaultPath));
            if (settings?.Theme is "Dark" or "Light") return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return new();
    }
    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(this)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

internal static class ThemeService
{
    public static string Mode { get; private set; } = "Dark";
    private static Window? settingsWindow;
    public static Brush Brush(string key) => (Brush)System.Windows.Application.Current.FindResource(key);
    public static void Apply(string mode)
    {
        Mode = mode == "Light" ? "Light" : "Dark";
        bool dark = Mode == "Dark";
        var palette = new (string Key, string Dark, string Light)[] {
            ("WindowBrush", "#000000", "#FFFFFF"), ("PanelBrush", "#101010", "#F7F7F7"),
            ("ControlBrush", "#202020", "#EEEEEE"), ("BorderBrush", "#484848", "#B8B8B8"),
            ("TextBrush", "#FFFFFF", "#000000"), ("HoverBrush", "#383838", "#DDDDDD"),
            ("SelectedBrush", "#484848", "#CCCCCC"), ("InputBrush", "#080808", "#FFFFFF"),
            ("PreviewBrush", "#080808", "#E6E6E6"), ("GridBrush", "#303030", "#DDDDDD"),
            ("SliderFillBrush", "#CCCCCC", "#444444"), ("SliderTrackBrush", "#404040", "#CCCCCC"),
            ("ScrollThumbBrush", "#555555", "#999999"), ("SelectionBrush", "#666666", "#BBBBBB"),
            ("ClipBrush", "#282828", "#E0E0E0"), ("ClipLabelBrush", "#101010", "#F3F3F3"),
            ("ZoomTrackBrush", "#484848", "#D4D4D4"), ("CameraTrackBrush", "#303030", "#EEEEEE"),
            ("ThreeDTrackBrush", "#404040", "#DDDDDD"), ("ImageTrackBrush", "#383838", "#E4E4E4"),
            ("BlurTrackBrush", "#282828", "#F0F0F0")
        };
        foreach (var item in palette)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? item.Dark : item.Light)); brush.Freeze();
            System.Windows.Application.Current.Resources[item.Key] = brush;
        }
        foreach (Window window in System.Windows.Application.Current.Windows) ApplyTitleBar(window);
    }
    public static void ApplyTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        int enabled = Mode == "Dark" ? 1 : 0;
        if (Native.DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
            Native.DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
    }
    public static void ShowSettings(Window? owner)
    {
        if (settingsWindow != null)
        {
            if (owner?.IsVisible == true && settingsWindow.Owner != owner) settingsWindow.Owner = owner;
            settingsWindow.Show(); settingsWindow.Activate(); return;
        }
        var window = new Window { Title = "设置", Width = 390, Height = 290, ResizeMode = ResizeMode.NoResize,
            Style = (Style)System.Windows.Application.Current.FindResource(typeof(Window)), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        if (owner?.IsVisible == true) window.Owner = owner; else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new StackPanel { Margin = new Thickness(24) }; window.Content = panel;
        panel.Children.Add(new TextBlock { Text = "外观", FontSize = 22, Margin = new Thickness(0, 0, 0, 16) });
        panel.Children.Add(new TextBlock { Text = "配色模式" });
        var theme = new ComboBox { ItemsSource = new[] { "黑底白字", "白底黑字" }, SelectedIndex = Mode == "Dark" ? 0 : 1 }; panel.Children.Add(theme);
        var status = new TextBlock { Text = "切换后立即生效，并记住你的选择。", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18) }; panel.Children.Add(status);
        theme.SelectionChanged += (_, _) => {
            Apply(theme.SelectedIndex == 1 ? "Light" : "Dark");
            try { new AppearanceSettings { Theme = Mode }.Save(); status.Text = "已保存，下次启动继续使用此模式。"; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { status.Text = "配色已切换，但未能保存设置：" + ex.Message; }
        };
        var close = new Button { Content = "完成", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0) }; panel.Children.Add(close); close.Click += (_, _) => window.Close();
        settingsWindow = window; window.Closed += (_, _) => settingsWindow = null; window.Show();
    }
}
