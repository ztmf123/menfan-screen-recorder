using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Menfan.Core;

namespace Menfan.App;

public partial class MainWindow
{
    private void Module_Click(object sender, RoutedEventArgs e)
    { module = ((Button)sender).Tag.ToString()!; Timeline.SelectedEffect = null; Timeline.InvalidateVisual(); BuildProperties(); _ = RefreshPreviewAsync(); }
    private void BuildProperties()
    {
        if (Properties == null) return;
        CropOverlay.Visibility = project != null && module == "裁切" ? Visibility.Visible : Visibility.Collapsed;
        Properties.Children.Clear(); Header(module);
        if (project == null) { Hint("录制或导入视频后，可以在这里调整画面和效果。"); return; }
        var p = project; var s = p.Style;
        if (Timeline.SelectedEffect is { } effect) { EffectProperties(effect); return; }
        switch (module)
        {
            case "画面":
                Choice("背景", ["Solid", "Gradient", "Grid", "Dots", "MovingGrid", "Image", "Video"], ["纯色", "渐变", "网格", "圆点", "动态网格", "图片", "视频"], s.BackgroundKind, value => s.BackgroundKind = value);
                ColorField("背景颜色", s.Background, color => s.Background = color); ColorField("渐变终点", s.GradientColor, color => s.GradientColor = color);
                ActionButton("选择图片 / 视频背景…", async () => {
                    var dialog = new OpenFileDialog { Filter = "背景素材|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.mp4;*.mov;*.mkv" };
                    if (dialog.ShowDialog() != true) return; Snapshot(); s.BackgroundPath = dialog.FileName;
                    s.BackgroundKind = new[] { ".mp4", ".mov", ".mkv" }.Contains(Path.GetExtension(dialog.FileName).ToLowerInvariant()) ? "Video" : "Image";
                    await GuardAsync(PrepareReadersAsync); Dirty(); BuildProperties();
                });
                ActionButton("使用当前 Windows 桌面壁纸", () => {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"); var path = key?.GetValue("Wallpaper") as string;
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { Status.Text = "未找到当前静态壁纸，请使用自定义背景。"; return; }
                    Snapshot(); s.BackgroundPath = path; s.BackgroundKind = "Image"; Dirty(); BuildProperties();
                });
                Slider("画面留白", 0, 300, s.Padding, v => s.Padding = (int)v);
                Slider("画面大小", .1, 2, s.ScreenScale, v => s.ScreenScale = v);
                Slider("水平位置", 0, 1, s.ScreenX, v => s.ScreenX = v); Slider("垂直位置", 0, 1, s.ScreenY, v => s.ScreenY = v);
                Slider("内容圆角", 0, 120, s.CornerRadius, v => s.CornerRadius = (int)v); Slider("外描边", 0, 20, s.Stroke, v => s.Stroke = (int)v);
                Slider("阴影强度", 0, 1, s.Shadow, v => s.Shadow = v);
                Choice("样机外框", ["None", "LightWindow", "DarkWindow", "LightBrowser", "DarkBrowser"], ["无", "窗口浅色", "窗口深色", "浏览器浅色", "浏览器深色"], s.Frame, v => s.Frame = v);
                TextField("网址或文字", s.FrameText, text => s.FrameText = text);
                ActionButton("调整画面裁切…", () => { module = "裁切"; BuildProperties(); _ = RefreshPreviewAsync(); });
                ClipProperties(); break;
            case "裁切":
                Hint("裁掉源画面的边缘。原始素材保留，随时可以恢复。");
                Hint("拖动画布上的边缘或四角调整裁切；拖动选区内部移动取景范围。");
                ActionButton("完成裁切", () => { module = "画面"; BuildProperties(); _ = RefreshPreviewAsync(); });
                Slider("左侧裁切", 0, Math.Min(.8, .94 - s.CropRight), s.CropLeft, v => s.CropLeft = v);
                Slider("右侧裁切", 0, Math.Min(.8, .94 - s.CropLeft), s.CropRight, v => s.CropRight = v);
                Slider("上侧裁切", 0, Math.Min(.8, .94 - s.CropBottom), s.CropTop, v => s.CropTop = v);
                Slider("下侧裁切", 0, Math.Min(.8, .94 - s.CropTop), s.CropBottom, v => s.CropBottom = v);
                ActionButton("恢复全部画面", () => { Snapshot(); s.CropLeft = s.CropRight = s.CropTop = s.CropBottom = 0; Dirty(); BuildProperties(); }); break;
            case "开场":
                Check("启用开场动画", s.Intro, v => s.Intro = v);
                Slider("进入时长 / 秒", .1, 3, s.IntroDuration, v => s.IntroDuration = v);
                Choice("动画节奏", ["Smooth", "Brisk", "Gentle", "Bouncy"], ["顺滑", "利落", "柔和", "轻弹"], s.IntroStyle, v => s.IntroStyle = v);
                Choice("屏幕进入方向", ["Bottom", "Right", "Top"], ["下方", "右侧", "上方"], s.IntroDirection, v => s.IntroDirection = v);
                Hint("背景先出现，屏幕和摄像头随后进入。播放头移到片头查看效果。"); break;
            case "运镜":
                Check("跟随鼠标运镜（无需点击）", s.MouseFollow, v => s.MouseFollow = v);
                Slider("跟随放大倍率", 1, 3, s.MouseFollowZoom, v => s.MouseFollowZoom = v);
                Slider("跟随平滑 / 秒", 0, 1, s.MouseFollowSmoothing, v => s.MouseFollowSmoothing = v);
                Hint(p.Cursor.Count > 0 ? "开启后按录制时的鼠标移动持续运镜，无需点击；手动镜头片段优先。倍率大于 1 时可看到画面平移。" : "此素材没有独立鼠标轨迹，无法跟随。请使用 Menfan 新录制的素材；普通导入视频无法还原鼠标轨迹。");
                Choice("新运镜默认节奏", ["Smooth", "Brisk", "Gentle", "Bouncy"], ["顺滑", "利落", "柔和", "轻弹"], s.MotionStyle, v => s.MotionStyle = v);
                ActionButton("根据点击生成自动缩放", () => { Snapshot(); TimelineMap.GenerateAutoZoom(p); Dirty(); BuildProperties(); });
                ActionButton("＋ 手动缩放", () => AddEffect("Zoom")); ActionButton("＋ 屏幕 3D", () => AddEffect("Screen3D"));
                Slider("基础缩放倍率", 1, 2, s.Zoom, v => s.Zoom = v); Check("基础缩放跟随光标", s.FollowCursor, v => s.FollowCursor = v);
                Hint("在下方选择镜头片段调整构图。拖动片段可移动，拖动边缘调整范围。"); EffectList("Zoom", "Screen3D"); break;
            case "光标":
                if (p.CursorEmbedded) Hint("此素材已包含原始光标，无法完整替换或隐藏原光标。Menfan 新录制会独立保存光标。");
                Choice("光标样式", ["System", "Touch", "Hidden"], ["箭头", "触控圆点", "始终隐藏"], s.CursorMode, v => s.CursorMode = v);
                Slider("光标大小", .25, 5, s.CursorSize, v => s.CursorSize = v); Check("静止后隐藏", s.HideIdleCursor, v => s.HideIdleCursor = v);
                Slider("移动平滑 / 秒", 0, .4, s.CursorSmoothing, v => s.CursorSmoothing = v);
                Slider("光标时间校准 / 秒", -2, 2, s.CursorOffset, v => s.CursorOffset = v);
                Check("显示点击反馈", s.ClickHighlight, v => s.ClickHighlight = v);
                Choice("点击样式", ["Ripple", "Halo", "Double", "Flash", "None"], ["涟漪", "光晕", "双环", "微闪", "无"], s.ClickStyle, v => s.ClickStyle = v);
                ColorField("反馈颜色", s.CursorColor, v => s.CursorColor = v); Slider("不透明度", 0, 1, s.ClickOpacity, v => s.ClickOpacity = v); Slider("反馈范围", .3, 3, s.ClickSize, v => s.ClickSize = v); break;
            case "摄像头":
                ActionButton("导入摄像头素材…", async () => {
                    var dialog = new OpenFileDialog { Filter = "视频|*.mp4;*.mkv;*.mov;*.avi" }; if (dialog.ShowDialog() != true || tools == null) return;
                    await GuardAsync(async () => { await tools.ProbeAsync(dialog.FileName); Snapshot(); p.CameraPath = dialog.FileName; p.Camera.Visible = true; await PrepareReadersAsync(); }); Dirty(); BuildProperties();
                });
                if (p.CameraPath == null) { Hint("当前项目没有摄像头素材。录制前可在设备设置中选择摄像头，或导入视频。"); break; }
                var c = p.Camera;
                Check("显示摄像头", c.Visible, v => c.Visible = v);
                Slider("水平位置", 0, 1, c.X, v => c.X = v); Slider("垂直位置", 0, 1, c.Y, v => c.Y = v); Slider("摄像头大小", .05, .8, c.Size, v => c.Size = v);
                Choice("形状", ["Circle", "Rounded", "Rectangle", "Landscape", "Portrait"], ["圆形", "圆角方形", "方形", "横向", "竖向"], c.Shape, v => c.Shape = v);
                Check("镜像摄像头", c.Mirror, v => c.Mirror = v); Slider("描边", 0, 15, c.Stroke, v => c.Stroke = (int)v); Slider("阴影", 0, 1, c.Shadow, v => c.Shadow = v);
                Slider("蒙版内水平取景", 0, 1, c.PanX, v => c.PanX = v); Slider("蒙版内垂直取景", 0, 1, c.PanY, v => c.PanY = v); Slider("蒙版内放大", 1, 4, c.Zoom, v => c.Zoom = v);
                Slider("素材时间偏移 / 秒", -3, 3, p.CameraOffset, v => p.CameraOffset = v);
                ActionButton("＋ 摄影机运动", () => AddEffect("Camera")); EffectList("Camera"); break;
            case "声音":
                Check("静音整个作品", s.Mute, v => s.Mute = v); Slider("系统 / 原片音量", 0, 3, p.SystemVolume, v => p.SystemVolume = v);
                Slider("麦克风音量", 0, 3, p.MicrophoneVolume, v => p.MicrophoneVolume = v);
                Hint("声音与剪辑顺序、变速同步。调整后重新播放即可预览混音。"); break;
            default:
                Hint("点击时间线上的片段调整属性。"); ClipProperties(); break;
        }
    }
    private void ClipProperties()
    {
        if (project == null || project.Clips.Count == 0) return;
        int index = Math.Clamp(Timeline.SelectedClipIndex, 0, project.Clips.Count - 1); var clip = project.Clips[index];
        Header($"片段 {index + 1}", 13);
        Hint($"素材 {clip.Start:0.00}–{clip.End:0.00}s · 成片 {clip.Duration:0.00}s");
        var start = Input("入点 / 秒", MediaTools.N(clip.Start)); var end = Input("出点 / 秒", MediaTools.N(clip.End)); var speed = Input("速度 / 倍率", MediaTools.N(clip.Speed));
        ActionButton("应用片段修改", () => {
            if (!double.TryParse(start.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var a) || !double.TryParse(end.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var b)
                || !double.TryParse(speed.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate) || !double.IsFinite(a) || !double.IsFinite(b) || !double.IsFinite(rate)
                || a < 0 || b > project.Duration + .01 || b - a < .05 || rate < .25 || rate > 4) { ShowError("入出点须位于素材范围内，速度为 0.25–4×。"); return; }
            Snapshot(); var list = CloneClips(); list[index] = new() { Start = a, End = b, Speed = rate }; TimelineMap.ReplaceClips(project, list); Dirty(); BuildProperties();
        });
    }
    private void EffectProperties(SceneEffect e)
    {
        Header(e.Name, 14);
        var start = Input("开始 / 秒", MediaTools.N(e.Start)); var end = Input("结束 / 秒", MediaTools.N(e.End));
        ActionButton("应用时间范围", () => {
            if (project == null || !double.TryParse(start.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var a) || !double.TryParse(end.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var b)
                || !double.IsFinite(a) || !double.IsFinite(b) || a < 0 || b > project.EditedDuration || b - a < .05) { ShowError("效果范围必须位于成片时间内。"); return; }
            Snapshot(); e.Start = a; e.End = b; Dirty();
        });
        Slider("过渡时长 / 秒", 0, Math.Min(3, (e.End - e.Start) / 2), e.Transition, v => e.Transition = v);
        Choice("过渡节奏", ["Smooth", "Brisk", "Gentle", "Bouncy"], ["顺滑", "利落", "柔和", "轻弹"], e.MotionStyle, v => e.MotionStyle = v);
        Slider(e.Kind == "Blur" ? "源画面水平位置" : "水平位置 / 锚点", 0, 1, e.X, v => e.X = v);
        Slider(e.Kind == "Blur" ? "源画面垂直位置" : "垂直位置 / 锚点", 0, 1, e.Y, v => e.Y = v);
        if (e.Kind is "Zoom" or "Screen3D") Slider("目标缩放", 1, 4, e.Zoom, v => e.Zoom = v);
        if (e.Kind == "Screen3D")
        { Slider("X 轴倾斜 / °", -65, 65, e.RotateX, v => e.RotateX = v); Slider("Y 轴倾斜 / °", -65, 65, e.RotateY, v => e.RotateY = v); Slider("平面旋转 / °", -180, 180, e.RotateZ, v => e.RotateZ = v); Slider("透视强度", .1, 1.5, e.Perspective, v => e.Perspective = v); Slider("线性聚焦虚化", 0, 70, e.FocusBlur, v => e.FocusBlur = v); Slider("焦点位置", .1, .9, e.FocusY, v => e.FocusY = v); }
        if (e.Kind == "Camera")
        { Slider("摄像头大小", .05, 1, e.Width, v => e.Width = v); Choice("目标形状", ["Circle", "Rounded", "Rectangle", "Landscape", "Portrait"], ["圆形", "圆角方形", "方形", "横向", "竖向"], e.Shape, v => e.Shape = v); }
        if (e.Kind is "Blur" or "Image")
        {
            Slider("宽度", .02, 1, e.Width, v => e.Width = v); Slider("高度", .02, 1, e.Height, v => e.Height = v);
            Slider(e.Kind == "Blur" ? "模糊强度" : "底层柔化", 0, 70, e.Blur, v => e.Blur = v);
            if (e.Kind == "Image") Check("仅显示背景与贴图", e.BackgroundOnly, v => e.BackgroundOnly = v);
            else Hint("模糊区域绑定源画面，会随屏幕缩放与 3D 一起变化。");
        }
        ActionButton("分割这个效果 S", () => Split_Click(this, new())); ActionButton("删除效果", () => Delete_Click(this, new()));
    }
    private void AddEffect(string kind, string? path = null)
    {
        if (project == null || busy) return;
        if (project.Effects.Count >= 512) { ShowError("当前项目最多支持 512 个效果片段。"); return; }
        Snapshot();
        double start = Math.Min(position, Math.Max(0, project.EditedDuration - .1));
        var e = new SceneEffect { Kind = kind, Name = kind switch { "Screen3D" => "屏幕 3D", "Camera" => "摄影机运动", "Image" => "贴图", "Blur" => "模糊遮罩", _ => "缩放 · 手动" },
            Start = start, End = Math.Min(project.EditedDuration, start + 3), AssetPath = path, MotionStyle = project.Style.MotionStyle };
        if (kind == "Screen3D") { e.RotateX = 15; e.RotateY = -18; e.Zoom = 1.15; }
        if (kind == "Camera") { e.X = project.Camera.X; e.Y = project.Camera.Y; e.Width = project.Camera.Size; e.Shape = project.Camera.Shape; }
        if (kind == "Image")
        {
            e.Width = .45; e.Blur = 0;
            if (path != null) { var bitmap = renderer.Load(path); var size = SceneRenderer.Dimensions(project, 1080);
                e.Height = e.Width * size.Width / size.Height * bitmap.PixelHeight / bitmap.PixelWidth;
                if (e.Height > .8) { e.Width *= .8 / e.Height; e.Height = .8; } }
        }
        project.Effects.Add(e); Timeline.SelectedEffect = e; module = "效果";
        if (kind == "Screen3D") SeekTo((e.Start + e.End) / 2);
        Dirty(); BuildProperties();
    }
    private void EffectList(params string[] kinds)
    {
        if (project == null) return;
        foreach (var effect in project.Effects.Where(e => kinds.Contains(e.Kind)).OrderBy(e => e.Start))
            ActionButton(effect.ToString(), () => { Timeline.SelectedEffect = effect; SeekTo(effect.Start + Math.Min(.6, (effect.End - effect.Start) / 2)); module = "效果"; BuildProperties(); Timeline.InvalidateVisual(); });
    }
    private void Overlay_Click(object sender, RoutedEventArgs e)
    {
        if (project == null || busy) return;
        var menu = new ContextMenu();
        var blur = new MenuItem { Header = "添加模糊遮罩" }; blur.Click += (_, _) => AddEffect("Blur"); menu.Items.Add(blur);
        var image = new MenuItem { Header = "导入贴图…" }; image.Click += (_, _) => { var dialog = new OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif" }; if (dialog.ShowDialog() == true) AddEffect("Image", dialog.FileName); }; menu.Items.Add(image);
        var clipboard = new MenuItem { Header = "粘贴剪贴板图片", IsEnabled = Clipboard.ContainsImage() }; clipboard.Click += (_, _) => PasteImage(); menu.Items.Add(clipboard); menu.IsOpen = true;
        var existing = new MenuItem { Header = "选择已有叠加层" };
        foreach (var effect in project.Effects.Where(effect => effect.Kind is "Blur" or "Image"))
        { var item = new MenuItem { Header = effect.ToString() }; item.Click += (_, _) => { Timeline.SelectedEffect = effect; module = "效果"; SeekTo(effect.Start + (effect.End - effect.Start) / 2); BuildProperties(); Timeline.InvalidateVisual(); }; existing.Items.Add(item); }
        if (existing.Items.Count > 0) menu.Items.Add(existing);
    }
    private void PasteImage()
    {
        if (project == null || !Clipboard.ContainsImage()) return;
        var image = Clipboard.GetImage(); if (image == null) return;
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Menfan", "Images"); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".png"); using (var stream = File.Create(path)) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); encoder.Save(stream); }
        AddEffect("Image", path);
    }
    private void Presets_Click(object sender, RoutedEventArgs e)
    {
        if (project == null || busy) return;
        var menu = new ContextMenu(); var save = new MenuItem { Header = "保存当前场景…" };
        var reset = new MenuItem { Header = "清空画面效果" };
        reset.Click += async (_, _) => {
            Pause(); Snapshot(); project.ResetVisualEdits(); Timeline.SelectedEffect = null; module = "画面";
            await GuardAsync(PrepareReadersAsync); Dirty(); BuildProperties(); Status.Text = "已清空画面效果，剪辑片段已保留。";
        }; menu.Items.Add(reset);
        save.Click += (_, _) => { var name = EditorDialogs.Ask("保存场景预设", "我的场景"); if (!string.IsNullOrWhiteSpace(name)) { ScenePreset.Save(project, name); Status.Text = "场景已保存；素材和剪辑没有复制到预设。"; } }; menu.Items.Add(save);
        foreach (var item in ScenePreset.List())
        {
            var group = new MenuItem { Header = item.Value.Name };
            var apply = new MenuItem { Header = "应用到当前项目" }; apply.Click += async (_, _) => { Snapshot(); item.Value.Apply(project); await GuardAsync(PrepareReadersAsync); Dirty(); BuildProperties(); };
            group.Items.Add(apply); menu.Items.Add(group);
        }
        menu.IsOpen = true;
    }
    private void Aspect_Changed(object sender, SelectionChangedEventArgs e)
    { if (project == null || loading) return; Snapshot(); project.Style.Aspect = ((ComboBoxItem)AspectPicker.SelectedItem).Tag.ToString()!; Dirty(); }
    private async void Quality_Changed(object sender, SelectionChangedEventArgs e)
    { if (project == null || loading || tools == null) return; Pause(); await GuardAsync(PrepareReadersAsync); await RefreshPreviewAsync(); }
    private void Crop_Click(object sender, RoutedEventArgs e) { module = "裁切"; Timeline.SelectedEffect = null; Pause(); BuildProperties(); _ = RefreshPreviewAsync(); }

    private void Header(string text, double size = 18) => Properties.Children.Add(new TextBlock { Text = text, FontSize = size, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });
    private void Hint(string text) => Properties.Children.Add(new TextBlock { Text = text, FontSize = 15, FontWeight = FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) });
    private void Label(string text) => Properties.Children.Add(new TextBlock { Text = text, FontSize = 16, FontWeight = FontWeights.Normal, Margin = new Thickness(0, 3, 0, 0) });
    private TextBox Input(string label, string value) { Label(label); var input = new TextBox { Text = value }; Properties.Children.Add(input); return input; }
    private void TextField(string label, string value, Action<string> set)
    { var input = Input(label, value); input.LostKeyboardFocus += (_, _) => { if (input.Text != value) { Snapshot(); set(input.Text); value = input.Text; Dirty(); } }; }
    private void ColorField(string label, string value, Action<string> set)
    { TextField(label + " · #RRGGBB", value, text => { if (text.Length == 7 && text[0] == '#' && text.Skip(1).All(Uri.IsHexDigit)) set(text); else Status.Text = "请输入 #RRGGBB 格式的颜色"; }); }
    private void Slider(string text, double min, double max, double value, Action<double> set)
    {
        var label = new TextBlock { Text = $"{text}   {value:0.##}", FontSize = 16, FontWeight = FontWeights.Normal, }; Properties.Children.Add(label);
        var slider = new Slider { Minimum = min, Maximum = Math.Max(min + .001, max), Value = value, Margin = new Thickness(0, 6, 0, 14) };
        bool edited = false; slider.PreviewMouseLeftButtonDown += (_, _) => { Snapshot(); edited = true; Pause(); };
        slider.PreviewMouseLeftButtonUp += (_, _) => edited = false;
        slider.LostMouseCapture += (_, _) => edited = false;
        slider.ValueChanged += (_, _) => { if (!edited) Snapshot(); set(slider.Value); label.Text = $"{text}   {slider.Value:0.##}"; Dirty(); };
        Properties.Children.Add(slider);
    }
    private void Check(string label, bool value, Action<bool> set)
    {
        var check = new CheckBox { Content = label, IsChecked = value }; check.Checked += (_, _) => { Snapshot(); set(true); Dirty(); }; check.Unchecked += (_, _) => { Snapshot(); set(false); Dirty(); }; Properties.Children.Add(check);
    }
    private void Choice(string label, string[] values, string[] names, string value, Action<string> set)
    {
        Label(label); var combo = new ComboBox { ItemsSource = names, SelectedIndex = Math.Max(0, Array.IndexOf(values, value)) };
        combo.SelectionChanged += (_, _) => { Snapshot(); set(values[combo.SelectedIndex]); Dirty(); }; Properties.Children.Add(combo);
    }
    private void ActionButton(string label, Action action)
    { var button = new Button { Content = label, Margin = new Thickness(0, 0, 0, 10), HorizontalContentAlignment = HorizontalAlignment.Left }; button.Click += (_, _) => action(); Properties.Children.Add(button); }
    private void ActionButton(string label, Func<Task> action)
    { var button = new Button { Content = label, Margin = new Thickness(0, 0, 0, 10) }; button.Click += async (_, _) => { try { await action(); } catch (Exception ex) { ShowError(ex.Message); } }; Properties.Children.Add(button); }
}
