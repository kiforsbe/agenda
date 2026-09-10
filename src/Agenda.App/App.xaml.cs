using System.Reflection;
using Microsoft.UI.Xaml;

namespace Agenda.App;

public partial class App : Application
{
    private MainWindow? _window;
    private Mutex? _instance;
    public static string DataDirectory { get; private set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Agenda");
    public static string Version { get; } =
        typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(App).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => {
            try {
                Directory.CreateDirectory(DataDirectory);
                // Exception messages may contain a private calendar URL or account data.
                File.AppendAllText(Path.Combine(DataDirectory, "errors.log"), $"{DateTimeOffset.Now:u} {e.Exception.GetType().Name} HRESULT 0x{e.Exception.HResult:X8}\n{e.Exception.StackTrace}\n");
            } catch (Exception logError) when (logError is IOException or UnauthorizedAccessException) { }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var arguments = Environment.GetCommandLineArgs();
        var dataIndex = Array.IndexOf(arguments, "--data-dir");
        if (dataIndex >= 0 && dataIndex + 1 < arguments.Length) DataDirectory = Path.GetFullPath(arguments[dataIndex + 1]);
        Directory.CreateDirectory(DataDirectory);
        _instance = new Mutex(true, "Local\\Agenda.NativeFlyout", out var isNew);
        if (!isNew) { Exit(); return; }
        _window = new MainWindow();
        _window.Activate();
        _window.InitializeShell();
    }
}
