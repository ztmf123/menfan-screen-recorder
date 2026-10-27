namespace Menfan.App;
public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeService.Apply(AppearanceSettings.Load().Theme);
        System.Windows.EventManager.RegisterClassHandler(typeof(System.Windows.Window), System.Windows.FrameworkElement.LoadedEvent,
            new System.Windows.RoutedEventHandler((sender, args) => {
                if (sender is System.Windows.Window window && ReferenceEquals(args.OriginalSource, window))
                {
                    ThemeService.ApplyTitleBar(window);
                }
            }));
        var editor = new MainWindow();
        var recorder = new RecorderWindow(editor);
        editor.Launcher = recorder;
        MainWindow = recorder;
        editor.InitializeController();
        recorder.Show();
    }
}
