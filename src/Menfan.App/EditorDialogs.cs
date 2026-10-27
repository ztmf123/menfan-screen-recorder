using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Menfan.Core;

namespace Menfan.App;

internal static class EditorDialogs
{
    public static string? Ask(string title, string initial)
    {
        var window = new Window { Title = title, Width = 370, Height = 175, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Style = (Style)System.Windows.Application.Current.FindResource(typeof(Window)) };
        var panel = new StackPanel { Margin = new Thickness(20) }; window.Content = panel;
        var text = new TextBox { Text = initial }; panel.Children.Add(text);
        var save = new Button { Content = "确定", IsDefault = true }; panel.Children.Add(save);
        save.Click += (_, _) => window.DialogResult = true;
        window.Loaded += (_, _) => { text.Focus(); text.SelectAll(); };
        return window.ShowDialog() == true ? text.Text : null;
    }
    public static (string Path, ExportOptions Options)? Export(Project p)
    {
        var window = new Window { Title = "导出作品", Width = 490, Height = 440, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Style = (Style)System.Windows.Application.Current.FindResource(typeof(Window)) };
        var panel = new StackPanel { Margin = new Thickness(24) }; window.Content = panel;
        panel.Children.Add(new TextBlock { Text = "导出作品", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) });
        var audio = new CheckBox { Content = "仅音频（M4A）" }; panel.Children.Add(audio);
        var size = new ComboBox { ItemsSource = new[] { "原始画布", "1080p", "1440p", "4K", "720p" }, SelectedIndex = 1 }; panel.Children.Add(size);
        var fps = new ComboBox { ItemsSource = new[] { "30 FPS", "60 FPS" }, SelectedIndex = 0 }; panel.Children.Add(fps);
        var info = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 22), }; panel.Children.Add(info);
        void Update() {
            var dimensions = SceneRenderer.Dimensions(p, new[] { 0, 1080, 1440, 2160, 720 }[size.SelectedIndex]);
            double megabytes = p.EditedDuration * (audio.IsChecked == true ? 192000 : dimensions.Width * (double)dimensions.Height * (fps.SelectedIndex == 1 ? 60 : 30) * .08 + 192000) / 8 / 1024 / 1024;
            size.IsEnabled = fps.IsEnabled = audio.IsChecked != true;
            info.Text = $"时长 {p.EditedDuration:0.00} 秒\n画布 {dimensions.Width} × {dimensions.Height}\n预计约 {megabytes:0.0} MB（实际大小取决于画面内容）\n音频：系统 {p.SystemVolume:P0} · 麦克风 {p.MicrophoneVolume:P0}";
        }
        size.SelectionChanged += (_, _) => Update(); fps.SelectionChanged += (_, _) => Update(); audio.Checked += (_, _) => Update(); audio.Unchecked += (_, _) => Update(); Update();
        var export = new Button { Content = "选择位置并导出" }; panel.Children.Add(export);
        (string, ExportOptions)? result = null;
        export.Click += (_, _) => {
            bool audioOnly = audio.IsChecked == true;
            var dialog = new SaveFileDialog { FileName = "Menfan-export", Filter = audioOnly ? "M4A 音频|*.m4a" : "MP4 视频|*.mp4" };
            if (dialog.ShowDialog() != true) return;
            result = (dialog.FileName, new(new[] { 0, 1080, 1440, 2160, 720 }[size.SelectedIndex], fps.SelectedIndex == 1 ? 60 : 30, 20, audioOnly)); window.DialogResult = true;
        };
        window.ShowDialog(); return result;
    }
}

internal sealed class RecordingCompleteWindow : Window
{
    public RecordingCompleteWindow(Project project, MediaTools tools, Action edit, Action save)
    {
        Title = "录制完成"; Width = Math.Min(380, SystemParameters.WorkArea.Width - 32);
        SizeToContent = SizeToContent.Height; MaxHeight = SystemParameters.WorkArea.Height - 32; ResizeMode = ResizeMode.NoResize; Topmost = true;
        Style = (Style)System.Windows.Application.Current.FindResource(typeof(Window));
        var panel = new Grid { Margin = new Thickness(18) }; Content = panel;
        panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        panel.Children.Add(new TextBlock { Text = "✓ 录制完成", FontSize = 19, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        var body = new StackPanel();
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); panel.Children.Add(scroll);
        var thumbnail = new Image { Height = 140, Stretch = Stretch.Uniform }; body.Children.Add(thumbnail);
        body.Children.Add(new TextBlock { Text = $"{project.Duration:0.0} 秒 · {project.Width} × {project.Height}\n原始素材和项目已自动保存", TextWrapping = TextWrapping.Wrap, FontSize = 15, FontWeight = FontWeights.Normal, Margin = new Thickness(0, 10, 0, 0) });
        var buttons = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        buttons.ColumnDefinitions.Add(new()); buttons.ColumnDefinitions.Add(new()); Grid.SetRow(buttons, 2); panel.Children.Add(buttons);
        var saveButton = new Button { Content = "保存项目…", MinHeight = 40, Margin = new Thickness(0, 0, 6, 0) }; buttons.Children.Add(saveButton); saveButton.Click += (_, _) => save();
        var editButton = new Button { Content = "进入编辑 →", IsDefault = true, MinHeight = 40, Margin = new Thickness(6, 0, 0, 0) }; Grid.SetColumn(editButton, 1); buttons.Children.Add(editButton); editButton.Click += (_, _) => { Close(); edit(); };
        SizeChanged += (_, _) => {
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - 16; Top = area.Bottom - ActualHeight - 16;
        };
        Loaded += async (_, _) => { try { using var reader = new MediaFrameReader(tools, project.MediaPath, 480, Math.Max(2, 480 * project.Height / project.Width)); thumbnail.Source = await reader.GetFrameAsync(Math.Min(.5, project.Duration / 2)); } catch { } };
    }
}

internal sealed class ScenePreset
{
    public string Name { get; set; } = "场景";
    public VisualStyle Style { get; set; } = new();
    public CameraStyle Camera { get; set; } = new();
    public double SystemVolume { get; set; } = 1;
    public double MicrophoneVolume { get; set; } = 1;
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Menfan", "Scenes");
    public static void Save(Project p, string name)
    {
        Directory.CreateDirectory(Folder);
        var preset = JsonSerializer.Deserialize<ScenePreset>(JsonSerializer.Serialize(new ScenePreset { Name = name, Style = p.Style, Camera = p.Camera, SystemVolume = p.SystemVolume, MicrophoneVolume = p.MicrophoneVolume }))!;
        if (preset.Style.BackgroundPath != null)
        {
            var asset = Path.Combine(Folder, Guid.NewGuid().ToString("N") + Path.GetExtension(preset.Style.BackgroundPath));
            File.Copy(preset.Style.BackgroundPath, asset); preset.Style.BackgroundPath = asset;
        }
        File.WriteAllText(Path.Combine(Folder, Guid.NewGuid().ToString("N") + ".json"), JsonSerializer.Serialize(preset, Project.JsonOptions));
    }
    public static IEnumerable<(string Path, ScenePreset Value)> List()
    {
        if (!Directory.Exists(Folder)) yield break;
        foreach (var file in Directory.EnumerateFiles(Folder, "*.json"))
        {
            ScenePreset? p = null; try { p = JsonSerializer.Deserialize<ScenePreset>(File.ReadAllText(file)); } catch (JsonException) { }
            if (p != null) yield return (file, p);
        }
    }
    public void Apply(Project project)
    {
        project.Style = JsonSerializer.Deserialize<VisualStyle>(JsonSerializer.Serialize(Style))!;
        project.Camera = JsonSerializer.Deserialize<CameraStyle>(JsonSerializer.Serialize(Camera))!;
        project.SystemVolume = SystemVolume; project.MicrophoneVolume = MicrophoneVolume;
    }
}
