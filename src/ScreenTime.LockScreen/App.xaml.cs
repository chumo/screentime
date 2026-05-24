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
    }
}
