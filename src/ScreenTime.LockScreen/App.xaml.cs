using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using ScreenTime.Common.Models;

namespace ScreenTime.LockScreen;

public partial class App : Application
{
    public static string TargetUsername { get; private set; } = string.Empty;
    private TaskbarIcon? _trayIcon;
    private DispatcherTimer? _trayTimer;

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

        InitTrayIcon();
    }

    private void InitTrayIcon()
    {
        var contextMenu = new ContextMenu();
        var configItem = new MenuItem { Header = "Configure..." };
        configItem.Click += (_, _) => LaunchConfigAsAdmin();
        contextMenu.Items.Add(configItem);

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "ScreenTime",
            Icon = CreateTextIcon("--"),
            ContextMenu = contextMenu
        };

        _trayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _trayTimer.Tick += (_, _) => UpdateTrayIcon();
        _trayTimer.Start();
        UpdateTrayIcon();
    }

    private void UpdateTrayIcon()
    {
        try
        {
            var path = PipeCommands.TimeRemainingFilePath(TargetUsername);
            if (File.Exists(path))
            {
                var text = File.ReadAllText(path).Trim();
                if (int.TryParse(text, out var minutes))
                {
                    var label = minutes > 99 ? $"{minutes}" : $"{minutes}m";
                    var isLow = minutes <= 5;
                    _trayIcon!.Icon = CreateTextIcon(label, isLow);
                    _trayIcon.ToolTipText = $"ScreenTime: {minutes} min remaining";
                    return;
                }
            }
        }
        catch { }
        _trayIcon!.Icon = CreateTextIcon("--");
        _trayIcon.ToolTipText = "ScreenTime";
    }

    private static Icon CreateTextIcon(string text, bool isWarning = false)
    {
        var bitmap = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.Transparent);

        var color = isWarning ? Color.OrangeRed : Color.LimeGreen;
        var fontSize = text.Length > 2 ? 6.5f : 8f;
        using var font = new Font("Segoe UI", fontSize, System.Drawing.FontStyle.Bold);
        using var brush = new SolidBrush(color);

        var size = g.MeasureString(text, font);
        var x = (16 - size.Width) / 2;
        var y = (16 - size.Height) / 2;
        g.DrawString(text, font, brush, x, y);

        var handle = bitmap.GetHicon();
        return Icon.FromHandle(handle);
    }

    private static void LaunchConfigAsAdmin()
    {
        try
        {
            var exePath = @"C:\Program Files\ScreenTime\Config\ScreenTime.Config.exe";
            if (!File.Exists(exePath))
            {
                MessageBox.Show($"Config not found at:\n{exePath}", "ScreenTime", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exePath)!
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to launch Config:\n{ex.Message}", "ScreenTime", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayTimer?.Stop();
        _trayIcon?.Dispose();
        base.OnExit(e);
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
