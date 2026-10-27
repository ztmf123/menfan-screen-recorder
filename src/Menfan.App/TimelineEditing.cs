using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Menfan.Core;

namespace Menfan.App;

public partial class MainWindow
{
    private readonly List<(double Time, BitmapSource Image)> thumbnails = [];
    private void Snapshot()
    {
        if (project == null || loading) return;
        undo.Push(JsonSerializer.Serialize(project)); redo.Clear();
        if (undo.Count > 40) { var recent = undo.Take(40).Reverse().ToArray(); undo.Clear(); foreach (var entry in recent) undo.Push(entry); }
    }
    private void Dirty()
    {
        if (project == null) return;
        Pause(); previewRevision++;
        dirty = true; position = Math.Clamp(position, 0, project.EditedDuration);
        Timeline.Project = project; UpdateTransport(); RequestPropertyPreview();
    }
    private async void Undo_Click(object sender, RoutedEventArgs e) => await RestoreHistoryAsync(undo, redo);
    private async void Redo_Click(object sender, RoutedEventArgs e) => await RestoreHistoryAsync(redo, undo);
    private async Task RestoreHistoryAsync(Stack<string> from, Stack<string> to)
    {
        if (project == null || from.Count == 0 || busy) return;
        Pause(); to.Push(JsonSerializer.Serialize(project)); project = JsonSerializer.Deserialize<Project>(from.Pop())!;
        Timeline.Project = project; Timeline.SelectedEffect = null; Timeline.SelectedClipIndex = Math.Min(Timeline.SelectedClipIndex, project.Clips.Count - 1);
        await GuardAsync(PrepareReadersAsync); BuildProperties(); Dirty();
    }
    private void Split_Click(object sender, RoutedEventArgs e)
    {
        if (project == null || busy) return; Pause(); Snapshot();
        if (Timeline.SelectedEffect is { } effect)
        {
            if (position <= effect.Start + .05 || position >= effect.End - .05) return;
            var copy = effect.Copy(); copy.Id = Guid.NewGuid().ToString("N"); copy.Start = position; effect.End = position; project.Effects.Add(copy); Timeline.SelectedEffect = copy;
        }
        else if (!TimelineMap.Split(project, position)) return;
        Dirty(); BuildProperties();
    }
    private void RippleBefore_Click(object sender, RoutedEventArgs e) => Ripple(true);
    private void RippleAfter_Click(object sender, RoutedEventArgs e) => Ripple(false);
    private void Ripple(bool before)
    {
        if (project == null || busy) return; Pause(); Snapshot();
        if (Timeline.SelectedEffect is { } effect)
        {
            if (position <= effect.Start || position >= effect.End) return;
            if (before) effect.Start = position; else effect.End = position;
        }
        else TimelineMap.Ripple(project, position, before);
        Dirty(); BuildProperties();
    }
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (project == null || busy) return; Pause(); Snapshot();
        if (Timeline.SelectedEffect is { } effect) { project.Effects.Remove(effect); Timeline.SelectedEffect = null; }
        else if (project.Clips.Count > 1)
        {
            var list = CloneClips(); list.RemoveAt(Math.Clamp(Timeline.SelectedClipIndex, 0, list.Count - 1)); TimelineMap.ReplaceClips(project, list);
            Timeline.SelectedClipIndex = Math.Min(Timeline.SelectedClipIndex, list.Count - 1);
        }
        Dirty(); BuildProperties();
    }
    private List<Clip> CloneClips() => project!.Clips.Select(c => new Clip { Start = c.Start, End = c.End, Speed = c.Speed }).ToList();
    private void MoveClip(int from, int to)
    {
        if (project == null || busy || from == to) return; Pause(); Snapshot();
        var list = CloneClips(); var clip = list[from]; list.RemoveAt(from); list.Insert(to, clip); TimelineMap.ReplaceClips(project, list);
        Timeline.SelectedClipIndex = to; Dirty(); BuildProperties();
    }
    private void TrimClip(int index, double start, double end)
    {
        if (project == null || busy || end - start < .03) return; Pause(); Snapshot();
        var list = CloneClips(); list[index].Start = start; list[index].End = end; TimelineMap.ReplaceClips(project, list);
        Dirty(); BuildProperties();
    }
    private void RestoreCut(int index)
    {
        if (project == null || busy || index <= 0 || index >= project.Clips.Count) return;
        var a = project.Clips[index - 1]; var b = project.Clips[index];
        if (a.Start > b.Start || Math.Abs(a.Speed - b.Speed) > .001) { Status.Text = "仅能连接源时间顺序相同且速度一致的相邻片段。"; return; }
        Snapshot(); var list = CloneClips(); list[index - 1].End = b.End; list.RemoveAt(index); TimelineMap.ReplaceClips(project, list); Timeline.SelectedClipIndex = index - 1; Dirty(); BuildProperties();
    }
    private void Reset_Click(object sender, RoutedEventArgs e)
    { if (project == null || busy) return; Snapshot(); TimelineMap.ReplaceClips(project, [new() { End = project.Duration }]); Timeline.SelectedClipIndex = 0; Dirty(); BuildProperties(); }
    private void ZoomOut_Click(object sender, RoutedEventArgs e) { Timeline.PixelsPerSecond = Math.Max(8, Timeline.PixelsPerSecond / 1.4); UpdateTransport(); }
    private void ZoomIn_Click(object sender, RoutedEventArgs e) { Timeline.PixelsPerSecond = Math.Min(500, Timeline.PixelsPerSecond * 1.4); UpdateTransport(); }
    private void TimelineOption_Changed(object sender, RoutedEventArgs e)
    { if (Timeline == null) return; Timeline.Waveform = WaveformToggle?.IsChecked == true; Timeline.Snap = SnapToggle?.IsChecked == true; Timeline.InvalidateVisual(); }
    private void Editor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.F11 && HandleDisplayShortcut(e.Key)) { e.Handled = true; return; }
        if (previewFullscreen)
        {
            if (e.Key == Key.F) HandleDisplayShortcut(e.Key);
            else if (e.Key == Key.Space) { ShowPreviewControls(); Play_Click(this, new()); }
            else if (!busy && project != null)
            {
                if (e.Key is Key.Left or Key.Right) { Pause(); SeekTo(position + (e.Key == Key.Right ? 1 : -1) * ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 5 : 1) / project.FrameRate); }
                else if (e.Key == Key.Home) { Pause(); SeekTo(0); }
                else if (e.Key == Key.End) { Pause(); SeekTo(project.EditedDuration); }
            }
            e.Handled = true; return;
        }
        if (Keyboard.FocusedElement is TextBoxBase or ComboBox or System.Windows.Controls.Slider || busy || project == null) return;
        if (Keyboard.Modifiers == ModifierKeys.None && HandleDisplayShortcut(e.Key)) { e.Handled = true; return; }
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            if (e.Key == Key.Z) { Undo_Click(this, new()); e.Handled = true; }
            else if (e.Key == Key.Y) { Redo_Click(this, new()); e.Handled = true; }
            else if (e.Key == Key.S) { SaveProject(); e.Handled = true; }
            else if (e.Key == Key.C && Timeline.SelectedEffect != null) { copiedEffect = Timeline.SelectedEffect.Copy(); Clipboard.SetData("Menfan.SceneEffect.Json", JsonSerializer.Serialize(copiedEffect)); e.Handled = true; }
            else if (e.Key == Key.V && Clipboard.ContainsImage()) { PasteImage(); e.Handled = true; }
            else if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Shift) != 0 && copiedEffect != null && Timeline.SelectedEffect is { } target && target.Kind == copiedEffect.Kind)
            {
                Snapshot(); var copy = copiedEffect.Copy(); copy.Id = target.Id; copy.Start = target.Start; copy.End = target.End; copy.AssetPath = target.AssetPath;
                project.Effects[project.Effects.IndexOf(target)] = copy; Timeline.SelectedEffect = copy; Dirty(); BuildProperties(); e.Handled = true;
            }
            else if (e.Key == Key.V && copiedEffect != null) { Snapshot(); var copy = copiedEffect.Copy(); copy.Id = Guid.NewGuid().ToString("N"); double length = copy.End - copy.Start;
                copy.Start = Math.Min(position, Math.Max(0, project.EditedDuration - .1)); copy.End = Math.Min(project.EditedDuration, copy.Start + length); project.Effects.Add(copy); Timeline.SelectedEffect = copy; module = "效果"; Dirty(); BuildProperties(); e.Handled = true; }
            return;
        }
        if (e.Key == Key.Space) Play_Click(this, new());
        else if (e.Key == Key.S) Split_Click(this, new());
        else if (e.Key == Key.Q) Ripple(true);
        else if (e.Key == Key.W) Ripple(false);
        else if (e.Key is Key.Delete or Key.D) Delete_Click(this, new());
        else if (e.Key is Key.Left or Key.Right) { Pause(); SeekTo(position + (e.Key == Key.Right ? 1 : -1) * ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 5 : 1) / project.FrameRate); }
        else return;
        e.Handled = true;
    }
    private SceneEffect? copiedEffect;
    private async Task LoadTimelineAssetsAsync(Project p, CancellationToken token)
    {
        if (tools == null) return;
        try
        {
            var sourceInfo = new FileInfo(p.MediaPath);
            var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(p.MediaPath + sourceInfo.Length + sourceInfo.LastWriteTimeUtc.Ticks)))[..24];
            var folder = Path.Combine(Path.GetTempPath(), "MenfanTimeline", key); Directory.CreateDirectory(folder);
            double step = Math.Max(.5, p.Duration / 90);
            var marker = Path.Combine(folder, "complete");
            if (!File.Exists(marker))
            {
                await MediaTools.RunAsync(tools.Ffmpeg, ["-y", "-i", p.MediaPath, "-vf", $"fps=1/{MediaTools.N(step)},scale=160:90", "-frames:v", "100", Path.Combine(folder, "thumb-%04d.jpg")], token);
                await File.WriteAllTextAsync(marker, "ok", token);
            }
            token.ThrowIfCancellationRequested(); thumbnails.Clear(); int count = 0;
            foreach (var file in Directory.EnumerateFiles(folder, "thumb-*.jpg").Order()) thumbnails.Add(((count++ + .5) * step, renderer.Load(file)));
            Timeline.ThumbnailAt = time => thumbnails.Count == 0 ? null : thumbnails.MinBy(t => Math.Abs(t.Time - time)).Image;
            Timeline.InvalidateVisual();
            var info = await tools.ProbeAsync(p.MediaPath, token);
            string? sound = p.SystemAudioPath ?? p.MicrophonePath ?? (info.HasAudio ? p.MediaPath : null);
            if (sound != null)
            {
                var raw = Path.Combine(folder, "wave.pcm");
                if (!File.Exists(raw)) await MediaTools.RunAsync(tools.Ffmpeg, ["-y", "-i", sound, "-vn", "-ac", "1", "-ar", "8000", "-c:a", "pcm_s16le", "-f", "s16le", raw], token);
                var peaks = await Task.Run(() => {
                    var data = new List<float>(); using var stream = new BinaryReader(File.OpenRead(raw));
                    while (stream.BaseStream.Position < stream.BaseStream.Length)
                    { token.ThrowIfCancellationRequested(); float peak = 0; for (int i = 0; i < 266 && stream.BaseStream.Position + 1 < stream.BaseStream.Length; i++) peak = Math.Max(peak, Math.Abs(stream.ReadInt16() / 32768f)); data.Add(peak); }
                    return data.ToArray();
                }, token);
                token.ThrowIfCancellationRequested(); Timeline.Peaks = peaks; Timeline.InvalidateVisual();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Status.Text = "时间线素材生成失败：" + ex.Message.Split('\n')[0]; }
    }
    private void Canvas_Down(object sender, MouseButtonEventArgs e)
    {
        if (project == null || busy || (Timeline.SelectedEffect == null && module != "摄像头")) return;
        Snapshot(); Pause(); canvasDragging = true; ScenePreview.CaptureMouse(); Canvas_Move(sender, e);
    }
    private void Canvas_Move(object sender, MouseEventArgs e)
    {
        if (!canvasDragging || project == null || ScenePreview.Source is not { } source) return;
        var point = e.GetPosition(ScenePreview); double scale = Math.Min(ScenePreview.ActualWidth / source.Width, ScenePreview.ActualHeight / source.Height);
        double x = Math.Clamp((point.X - (ScenePreview.ActualWidth - source.Width * scale) / 2) / (source.Width * scale), 0, 1);
        double y = Math.Clamp((point.Y - (ScenePreview.ActualHeight - source.Height * scale) / 2) / (source.Height * scale), 0, 1);
        if (Timeline.SelectedEffect is { } effect) { effect.X = x; effect.Y = y; } else { project.Camera.X = x; project.Camera.Y = y; }
        Dirty();
    }
    private void Canvas_Up(object sender, MouseButtonEventArgs e) { if (!canvasDragging) return; canvasDragging = false; ScenePreview.ReleaseMouseCapture(); BuildProperties(); }
}
