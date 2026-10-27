using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using NAudio.CoreAudioApi;
using Menfan.Core;

namespace Menfan.App;

internal sealed class RecordingSettings
{
    public bool HidePanelsWhileRecording { get; set; }
    public string? SystemDevice { get; set; }
    public string? MicrophoneDevice { get; set; }
    public string? CameraName { get; set; }
    public string? FfmpegPath { get; set; }
    public string Folder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Menfan Screen Recorder");
    public bool MicrophoneEnabled { get; set; }
    public bool SystemEnabled { get; set; } = true;
    public int FpsIndex { get; set; }
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Menfan", "recording.json");
    public static RecordingSettings Load()
    {
        try { return File.Exists(SettingsPath) ? JsonSerializer.Deserialize<RecordingSettings>(File.ReadAllText(SettingsPath)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var temporary = SettingsPath + ".tmp"; File.WriteAllText(temporary, JsonSerializer.Serialize(this)); File.Move(temporary, SettingsPath, true);
    }
}
internal sealed record AudioDevice(string Name, string? Id)
{
    public override string ToString() => Name;
    public static List<AudioDevice> List(DataFlow flow)
    {
        var list = new List<AudioDevice> { new("系统默认设备", null) };
        using var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        { list.Add(new(device.FriendlyName, device.ID)); device.Dispose(); }
        return list;
    }
}

internal static class VideoDevices
{
    public static async Task<List<string>> ListAsync(MediaTools tools)
    {
        using var process = new Process { StartInfo = MediaTools.StartInfo(tools.Ffmpeg, ["-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy"]) };
        process.Start(); var error = process.StandardError.ReadToEndAsync(); var output = process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync(); var text = await error; await output;
        return Regex.Matches(text, "\"([^\"]+)\" \\(video\\)").Select(m => m.Groups[1].Value).Distinct().ToList();
    }
}

internal sealed class DeviceWindow : Window
{
    public DeviceWindow(RecordingSettings settings, MediaTools tools)
    {
        Title = "录制设备与保存位置"; Width = 440; Height = 470; ResizeMode = ResizeMode.NoResize;
        Style = (Style)System.Windows.Application.Current.FindResource(typeof(Window)); WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new StackPanel { Margin = new Thickness(24) }; Content = panel;
        AddLabel(panel, "麦克风输入"); var mic = new ComboBox { ItemsSource = AudioDevice.List(DataFlow.Capture), SelectedIndex = 0 }; panel.Children.Add(mic);
        AddLabel(panel, "系统声音输出设备"); var system = new ComboBox { ItemsSource = AudioDevice.List(DataFlow.Render), SelectedIndex = 0 }; panel.Children.Add(system);
        AddLabel(panel, "摄像头（DirectShow）"); var camera = new ComboBox { ItemsSource = new[] { "不录制摄像头" }, SelectedIndex = 0 }; panel.Children.Add(camera);
        var hint = new TextBlock { Text = "正在读取摄像头列表…", FontSize = 11 }; panel.Children.Add(hint);
        var folder = new Button { Content = "选择保存位置…", Margin = new Thickness(0, 16, 0, 5) }; panel.Children.Add(folder);
        var folderText = new TextBlock { Text = settings.Folder, TextWrapping = TextWrapping.Wrap, FontSize = 11, }; panel.Children.Add(folderText);
        folder.Click += (_, _) => { using var dialog = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = settings.Folder }; if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) { settings.Folder = dialog.SelectedPath; folderText.Text = settings.Folder; } };
        var save = new Button { Content = "完成", Margin = new Thickness(0, 20, 0, 0) }; panel.Children.Add(save);
        save.Click += (_, _) => { settings.MicrophoneDevice = (mic.SelectedItem as AudioDevice)?.Id; settings.SystemDevice = (system.SelectedItem as AudioDevice)?.Id;
            settings.CameraName = camera.SelectedIndex > 0 ? camera.SelectedItem?.ToString() : null;
            try { settings.Save(); Close(); } catch (IOException ex) { hint.Text = "设置未能保存：" + ex.Message; } };
        mic.SelectedItem = mic.Items.Cast<AudioDevice>().FirstOrDefault(d => d.Id == settings.MicrophoneDevice) ?? mic.Items[0];
        system.SelectedItem = system.Items.Cast<AudioDevice>().FirstOrDefault(d => d.Id == settings.SystemDevice) ?? system.Items[0];
        Loaded += async (_, _) => {
            try
            {
                var devices = await VideoDevices.ListAsync(tools);
                devices.Insert(0, "不录制摄像头"); camera.ItemsSource = devices;
                camera.SelectedIndex = settings.CameraName != null && devices.Contains(settings.CameraName) ? devices.IndexOf(settings.CameraName) : 0;
                hint.Text = devices.Count > 1 ? "摄像头将独立录制，剪辑时可调整布局与形状。" : "未发现摄像头；如已连接，请检查设备是否被其他应用占用。";
            }
            catch (Exception ex) { hint.Text = ex.Message; }
        };
    }
    private static void AddLabel(Panel panel, string text) => panel.Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.Normal, Margin = new Thickness(0, 4, 0, 0) });
}

internal sealed class NotesWindow : Window
{
    public NotesWindow()
    {
        Title = "备忘录 · 仅自己可见"; Width = 350; Height = 440; Topmost = true;
        Style = (Style)System.Windows.Application.Current.FindResource(typeof(Window));
        var panel = new DockPanel { Margin = new Thickness(14) }; Content = panel;
        var controls = new StackPanel(); DockPanel.SetDock(controls, Dock.Bottom); panel.Children.Add(controls);
        controls.Children.Add(new TextBlock { Text = "字号", FontSize = 11 }); var size = new Slider { Minimum = 14, Maximum = 44, Value = 22 }; controls.Children.Add(size);
        controls.Children.Add(new TextBlock { Text = "窗口不透明度", FontSize = 11 }); var opacity = new Slider { Minimum = .35, Maximum = 1, Value = 1 }; controls.Children.Add(opacity);
        var mode = new CheckBox { Content = "阅读模式（锁定文字）" }; controls.Children.Add(mode);
        var text = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 22, BorderThickness = new Thickness(0) };
        panel.Children.Add(text); size.ValueChanged += (_, _) => text.FontSize = size.Value; opacity.ValueChanged += (_, _) => Opacity = opacity.Value;
        mode.Checked += (_, _) => text.IsReadOnly = true; mode.Unchecked += (_, _) => text.IsReadOnly = false;
        SourceInitialized += (_, _) => { if (!Native.SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, 0x11)) Title = "备忘录 · 当前系统未能排除采集"; };
    }
}

internal sealed class DesktopVisibility : IDisposable
{
    private IntPtr desktop;
    private bool hidden;
    public void Toggle()
    {
        if (hidden) { Dispose(); return; }
        Native.EnumWindows((window, _) => {
            var def = FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (def != IntPtr.Zero) desktop = FindWindowEx(def, IntPtr.Zero, "SysListView32", "FolderView");
            return desktop == IntPtr.Zero;
        }, IntPtr.Zero);
        if (desktop == IntPtr.Zero) throw new InvalidOperationException("找不到 Windows 桌面图标窗口。");
        if (!Native.IsWindowVisible(desktop)) return;
        ShowWindow(desktop, 0); hidden = true;
    }
    public void Dispose() { if (hidden && desktop != IntPtr.Zero) ShowWindow(desktop, 5); hidden = false; }
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? title);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
}
