using System.IO;
using System.Windows;

namespace ScreenTime.LockScreen;

public partial class App : Application
{
    public static string TargetUsername { get; private set; } = string.Empty;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length > 0)
            TargetUsername = e.Args[0];

        DispatcherUnhandledException += (s, ex) =>
        {
            LogCrash($"Unhandled UI exception: {ex.Exception}");
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
            LogCrash($"Unhandled domain exception: {ex.ExceptionObject}");

        LogCrash($"App started, TargetUsername='{TargetUsername}'");
    }

    private static void LogCrash(string msg)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ScreenTime", "lockscreen_debug.log");
            File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss}] {msg}\n");
        }
        catch { }
    }
}
