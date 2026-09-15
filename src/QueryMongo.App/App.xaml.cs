using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using QueryMongo.App.ViewModels;
using QueryMongo.Core.Connections;

namespace QueryMongo.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        Services = ConfigureServices();

        // A XAML or startup failure otherwise kills the process with only a stowed
        // exception code, which says nothing about the cause. Writing the exception
        // out first makes a crash on a user's machine diagnosable.
        UnhandledException += (_, e) => LogCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject as Exception);
    }

    /// <summary>Resolved by views through the static accessor; one container per process.</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    public static new App Current => (App)Application.Current;

    /// <summary>Where a startup crash is recorded, also shown in the README.</summary>
    public static string CrashLogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QueryMongo", "crash.log");

    private static void LogCrash(Exception? exception)
    {
        if (exception is null) return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            File.WriteAllText(CrashLogPath, $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}");
        }
        catch (IOException)
        {
            // Nothing useful to do while the process is already going down.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IConnectionStore>(_ => new FileConnectionStore());
        services.AddSingleton<ShellViewModel>();

        return services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
