using System.Text.Json;
using System.Text.Json.Serialization;

namespace Menfan.Core;

public sealed class Clip
{
    public double Start { get; set; }
    public double End { get; set; }
    public double Speed { get; set; } = 1;
    [JsonIgnore] public double Duration => (End - Start) / Speed;
    public override string ToString() => $"{TimeSpan.FromSeconds(Start):mm\\:ss\\.f} → {TimeSpan.FromSeconds(End):mm\\:ss\\.f}   ·   {Speed:0.##}×";
}

public sealed class CursorSample
{
    public double Time { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public bool Click { get; set; }
    public bool Visible { get; set; } = true;
    public string? ShapePath { get; set; }
    public double HotspotX { get; set; }
    public double HotspotY { get; set; }
}

public sealed class Project
{
    public int Version { get; set; } = 2;
    public string MediaPath { get; set; } = "";
    public string? SystemAudioPath { get; set; }
    public string? MicrophonePath { get; set; }
    public string? CameraPath { get; set; }
    public double CameraOffset { get; set; }
    public bool CursorEmbedded { get; set; } = true;
    public double SystemVolume { get; set; } = 1;
    public double MicrophoneVolume { get; set; } = 1;
    public List<SceneEffect> Effects { get; set; } = [];
    public CameraStyle Camera { get; set; } = new();
    public double SystemAudioOffset { get; set; }
    public double MicrophoneOffset { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double FrameRate { get; set; } = 30;
    public double Duration { get; set; }
    public List<Clip> Clips { get; set; } = [];
    public List<double> Markers { get; set; } = [];
    public List<CursorSample> Cursor { get; set; } = [];
    public VisualStyle Style { get; set; } = new();
    [JsonIgnore] public double EditedDuration => Clips.Sum(c => c.Duration);
    public Project CopyForRendering()
    {
        var copy = (Project)MemberwiseClone();
        copy.Style = Style.Copy(); copy.Camera = Camera.Copy();
        copy.Clips = Clips.Select(c => new Clip { Start = c.Start, End = c.End, Speed = c.Speed }).ToList();
        copy.Effects = Effects.Select(e => e.Copy()).ToList();
        // Recorded cursor samples and media paths are immutable during editing.
        return copy;
    }

    public void ResetVisualEdits()
    {
        Style = new VisualStyle {
            Padding = 0, CornerRadius = 0, Shadow = 0, CursorSize = 1,
            CursorSmoothing = 0, ClickHighlight = false
        };
        Camera = new CameraStyle { Visible = false };
        Effects.Clear();
    }

    public static Project Load(string path)
    {
        var project = JsonSerializer.Deserialize<Project>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("项目文件为空。");
        if (project.Version is < 1 or > 2) throw new InvalidDataException("不支持的项目版本。");
        project.Version = 2;
        var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        project.MediaPath = Resolve(project.MediaPath, folder);
        if (project.SystemAudioPath != null) project.SystemAudioPath = Resolve(project.SystemAudioPath, folder);
        if (project.MicrophonePath != null) project.MicrophonePath = Resolve(project.MicrophonePath, folder);
        if (project.CameraPath != null) project.CameraPath = Resolve(project.CameraPath, folder);
        if (project.Style.BackgroundPath != null) project.Style.BackgroundPath = Resolve(project.Style.BackgroundPath, folder);
        foreach (var effect in project.Effects)
            if (effect.AssetPath != null) effect.AssetPath = Resolve(effect.AssetPath, folder);
        foreach (var sample in project.Cursor)
            if (sample.ShapePath != null) sample.ShapePath = Resolve(sample.ShapePath, folder);
        project.Validate();
        return project;
    }

    public void Save(string path)
    {
        Validate();
        // Save relative media references without mutating the open project.
        var clone = JsonSerializer.Deserialize<Project>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;
        var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        clone.MediaPath = Path.GetRelativePath(folder, MediaPath);
        if (SystemAudioPath != null) clone.SystemAudioPath = Path.GetRelativePath(folder, SystemAudioPath);
        if (MicrophonePath != null) clone.MicrophonePath = Path.GetRelativePath(folder, MicrophonePath);
        if (CameraPath != null) clone.CameraPath = Path.GetRelativePath(folder, CameraPath);
        if (Style.BackgroundPath != null) clone.Style.BackgroundPath = Path.GetRelativePath(folder, Style.BackgroundPath);
        for (int i = 0; i < Effects.Count; i++)
            if (Effects[i].AssetPath != null) clone.Effects[i].AssetPath = Path.GetRelativePath(folder, Effects[i].AssetPath!);
        for (int i = 0; i < Cursor.Count; i++)
            if (Cursor[i].ShapePath != null) clone.Cursor[i].ShapePath = Path.GetRelativePath(folder, Cursor[i].ShapePath!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(clone, JsonOptions));
        File.Move(temp, path, true);
    }

    public void Validate()
    {
        if (!File.Exists(MediaPath)) throw new FileNotFoundException("找不到录制素材，请恢复素材的位置。", MediaPath);
        if (Width < 2 || Height < 2 || !double.IsFinite(Duration) || Duration <= 0)
            throw new InvalidDataException("视频尺寸或时长无效。");
        if (Clips.Count == 0 || Clips.Count > 128) throw new InvalidDataException("项目需要 1–128 个片段。");
        foreach (var clip in Clips)
            if (!double.IsFinite(clip.Start) || !double.IsFinite(clip.End) || !double.IsFinite(clip.Speed)
                || clip.Start < 0 || clip.End > Duration + .1 || clip.End <= clip.Start
                || clip.Speed < .25 || clip.Speed > 4)
                throw new InvalidDataException("片段范围或速度无效（支持 0.25–4×）。");
        foreach (var audio in new[] { SystemAudioPath, MicrophonePath, CameraPath, Style.BackgroundPath }.Concat(Effects.Select(e => e.AssetPath)))
            if (audio != null && !File.Exists(audio)) throw new FileNotFoundException("音频素材丢失。", audio);
        if (!double.IsFinite(SystemAudioOffset) || !double.IsFinite(MicrophoneOffset)) throw new InvalidDataException("音频时间偏移无效。");
        if (Style.Padding is < 0 or > 300 || Style.CornerRadius is < 0 or > 200
            || !double.IsFinite(Style.Zoom) || Style.Zoom is < 1 or > 2)
            throw new InvalidDataException("画面样式参数无效。");
        if (Style.Background.Length != 7 || Style.Background[0] != '#' || !Style.Background.Skip(1).All(Uri.IsHexDigit))
            throw new InvalidDataException("背景色必须为 #RRGGBB。");
        if (Cursor.Any(c => !double.IsFinite(c.Time) || !double.IsFinite(c.X) || !double.IsFinite(c.Y)))
            throw new InvalidDataException("光标数据无效。");
        if (!double.IsFinite(SystemVolume) || !double.IsFinite(MicrophoneVolume) || SystemVolume is < 0 or > 3 || MicrophoneVolume is < 0 or > 3)
            throw new InvalidDataException("音量范围为 0–300%。");
        SceneValidation.Validate(this);
    }

    private static string Resolve(string path, string folder) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(folder, path));
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}

public sealed class VisualStyle
{
    public VisualStyle Copy() => (VisualStyle)MemberwiseClone();
    public int Padding { get; set; } = 64;
    public int CornerRadius { get; set; } = 18;
    public string Background { get; set; } = "#DCE7E2";
    public string BackgroundKind { get; set; } = "Solid";
    public string? BackgroundPath { get; set; }
    public string GradientColor { get; set; } = "#B5BDD9";
    public string Aspect { get; set; } = "Source";
    public double CropLeft { get; set; }
    public double CropTop { get; set; }
    public double CropRight { get; set; }
    public double CropBottom { get; set; }
    public double ScreenScale { get; set; } = 1;
    public double ScreenX { get; set; } = .5;
    public double ScreenY { get; set; } = .5;
    public int Stroke { get; set; }
    public double Shadow { get; set; } = .25;
    public string Frame { get; set; } = "None";
    public string FrameText { get; set; } = "menfan";
    public bool Intro { get; set; }
    public double IntroDuration { get; set; } = .8;
    public string IntroDirection { get; set; } = "Bottom";
    public string IntroStyle { get; set; } = "Smooth";
    public string MotionStyle { get; set; } = "Smooth";
    public string CursorMode { get; set; } = "System";
    public double CursorSize { get; set; } = 1.5;
    public string CursorColor { get; set; } = "#9074E8";
    public bool HideIdleCursor { get; set; }
    public string ClickStyle { get; set; } = "Ripple";
    public double ClickOpacity { get; set; } = .8;
    public double ClickSize { get; set; } = 1;
    public double CursorOffset { get; set; }
    public double CursorSmoothing { get; set; } = .12;
    public double Zoom { get; set; } = 1;
    public bool FollowCursor { get; set; }
    public bool MouseFollow { get; set; }
    public double MouseFollowZoom { get; set; } = 1.6;
    public double MouseFollowSmoothing { get; set; } = .2;
    public bool ClickHighlight { get; set; } = true;
    public bool Mute { get; set; }
}
