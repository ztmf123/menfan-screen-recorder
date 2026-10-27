using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Menfan.App;

public partial class RecorderWindow : Window
{
    private readonly MainWindow controller;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private bool initializing = true;
    private int sourceRequest;
    public bool AllowClose { get; set; }
    public RecorderWindow(MainWindow controller)
    {
        this.controller = controller;
        InitializeComponent();
        Width = Math.Min(980, SystemParameters.WorkArea.Width - 24);
        Loaded += (_, _) => {
            RefreshSources(0);
            Microphone.IsChecked = controller.MicrophoneEnabled; SystemSound.IsChecked = controller.SystemSoundEnabled; Fps.SelectedIndex = controller.RecordingFpsIndex;
            HidePanelsToggle.IsChecked = controller.HidePanelsWhileRecording;
            initializing = false;
            timer.Start();
        };
        timer.Tick += (_, _) => RefreshState();
    }
    public void ExcludeFromCapture(bool exclude) => Native.SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, exclude ? 0x11u : 0u);
    internal void RefreshRecordingState() => RefreshState();
    private void RefreshState()
    {
        bool recording = controller.IsRecording;
        bool busy = controller.IsBusy;
        Preparation.Visibility = recording ? Visibility.Collapsed : Visibility.Visible;
        RecordingBar.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
        HeaderLinks.Visibility = recording ? Visibility.Collapsed : Visibility.Visible;
        PauseRecording.Content = controller.IsPaused ? "▶ 继续" : "Ⅱ 暂停";
        PauseRecording.IsEnabled = RecordingStop.IsEnabled = !busy;
        double desiredWidth = Math.Min(recording ? 640 : 980, SystemParameters.WorkArea.Width - 24);
        if (Math.Abs(Width - desiredWidth) > 2) Width = desiredWidth;
        Height = recording ? 142 : 178;
        ActionRow.Height = new GridLength(recording ? 42 : 74);
        StartStop.Content = recording ? "■  停止录制" : "●  开始录制";
        StartStop.IsEnabled = !busy;
        Modes.IsEnabled = Sources.IsEnabled = Microphone.IsEnabled = SystemSound.IsEnabled = Fps.IsEnabled = !busy && !recording;
        MarkButton.IsEnabled = recording && !busy;
        EditorButton.IsEnabled = controller.HasProject && !busy && !recording;
        HidePanelsToggle.IsEnabled = !busy && !recording;
        HidePanelsToggle.Visibility = recording ? Visibility.Collapsed : Visibility.Visible;
        Hint.Text = controller.RecorderStatus;
        Clock.Text = controller.RecorderClock;
    }
    private void RefreshSources(int mode)
    {
        Sources.ItemsSource = controller.GetCaptureTargets(mode);
        Sources.SelectedIndex = Sources.Items.Count > 0 ? 0 : -1;
        if (Sources.SelectedItem is CaptureTarget target) controller.SelectCaptureTarget(target);
    }
    private async void Source_Changed(object sender, RoutedEventArgs e)
    {
        if (initializing || controller == null) return;
        int request = ++sourceRequest;
        controller.ClearCaptureTarget();
        int mode = int.Parse(((RadioButton)sender).Tag.ToString()!);
        if (mode == 2)
        {
            Hide();
            try
            {
                var result = controller.PickCaptureRegion();
                if (result != null) { Sources.ItemsSource = new[] { result }; Sources.SelectedIndex = 0; }
                else { DisplayMode.IsChecked = true; }
            }
            finally { Show(); Activate(); }
        }
        else if (mode == 3)
        {
            Sources.ItemsSource = null;
            var devices = await controller.GetCaptureDevicesAsync();
            if (request != sourceRequest) return;
            Sources.ItemsSource = devices; Sources.SelectedIndex = Sources.Items.Count > 0 ? 0 : -1;
        }
        else RefreshSources(mode);
    }
    private void Target_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (Sources.SelectedItem is CaptureTarget target) controller.SelectCaptureTarget(target);
    }
    private void Audio_Changed(object sender, RoutedEventArgs e)
    {
        if (initializing || Microphone == null || SystemSound == null || Fps == null) return;
        controller.ConfigureRecording(Microphone.IsChecked == true, SystemSound.IsChecked == true, Fps.SelectedIndex);
    }
    private void Record_Click(object sender, RoutedEventArgs e) => controller.ToggleRecording();
    private void HidePanels_Changed(object sender, RoutedEventArgs e)
    { if (!initializing) controller.ConfigureHiddenPanels(HidePanelsToggle.IsChecked == true); }
    private void Marker_Click(object sender, RoutedEventArgs e) => controller.AddRecordingMarker();
    private void Editor_Click(object sender, RoutedEventArgs e) => controller.ShowEditor();
    private void Import_Click(object sender, RoutedEventArgs e) => controller.ImportVideo();
    private void Open_Click(object sender, RoutedEventArgs e) => controller.OpenProject();
    private void Exit_Click(object sender, RoutedEventArgs e) => controller.RequestExit();
    private void Devices_Click(object sender, RoutedEventArgs e) => controller.ConfigureDevices();
    private void Settings_Click(object sender, RoutedEventArgs e) => controller.ShowSettings();
    private void Pause_Click(object sender, RoutedEventArgs e) => controller.TogglePause();
    private void Notes_Click(object sender, RoutedEventArgs e) => controller.ShowNotes();
    private void Desktop_Click(object sender, RoutedEventArgs e) => controller.ToggleDesktopIcons();
    private void DragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.OriginalSource is not Control) DragMove();
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (AllowClose) return;
        e.Cancel = true;
        controller.RequestExit();
    }
}
