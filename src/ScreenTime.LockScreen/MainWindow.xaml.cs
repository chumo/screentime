using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using ScreenTime.Common.Models;
using ScreenTime.Common.Services;

namespace ScreenTime.LockScreen;

public partial class MainWindow : Window
{
    private int _failedAttempts;
    private DateTime _lockoutUntil = DateTime.MinValue;
    private CancellationTokenSource? _pipeCts;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc callback, IntPtr hInstance, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private LowLevelKeyboardProc? _hookProc;
    private IntPtr _hookId = IntPtr.Zero;
    private const int WH_KEYBOARD_LL = 13;

    public MainWindow()
    {
        InitializeComponent();
        LockScreenLog("MainWindow constructed");
        _pipeCts = new CancellationTokenSource();
        Task.Run(() => PollForCommands(_pipeCts.Token));
        Loaded += OnLoaded;
    }

    private static void LockScreenLog(string msg)
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

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
    }

    private async Task PollForCommands(CancellationToken ct)
    {
        var commandFile = PipeCommands.CommandFilePath(App.TargetUsername);
        LockScreenLog($"PollForCommands started, watching: {commandFile}");
        string lastCommand = "";

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (File.Exists(commandFile))
                {
                    var command = File.ReadAllText(commandFile).Trim();
                    if (!string.IsNullOrEmpty(command) && command != lastCommand)
                    {
                        LockScreenLog($"Command received: {command}");
                        lastCommand = command;
                        Dispatcher.Invoke(() => HandleCommand(command));
                        LockScreenLog($"Command handled: {command}");
                    }
                }
            }
            catch (Exception ex)
            {
                LockScreenLog($"Poll error: {ex.Message}");
            }

            await Task.Delay(2000, ct);
        }
    }

    private void HandleCommand(string command)
    {
        switch (command)
        {
            case PipeCommands.Warn5:
                ShowWarning("5 minutes of screen time remaining!");
                break;
            case PipeCommands.Warn1:
                ShowWarning("1 minute remaining! Save your work now!");
                break;
            case PipeCommands.Lock:
                ShowLockScreen();
                break;
            case PipeCommands.Unlock:
                HideLockScreen();
                break;
        }
    }

    private void ShowWarning(string message)
    {
        WarningText.Text = message;
        WarningPanel.Visibility = Visibility.Visible;
        LockPanel.Visibility = Visibility.Collapsed;
        WindowState = WindowState.Normal;
        SizeToContent = SizeToContent.WidthAndHeight;
        Show();
        Activate();
        Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
        Top = 40;
        Topmost = true;
    }

    private void ShowLockScreen()
    {
        WarningPanel.Visibility = Visibility.Collapsed;
        LockPanel.Visibility = Visibility.Visible;
        SizeToContent = SizeToContent.Manual;
        WindowState = WindowState.Normal;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        Topmost = true;
        Show();
        Activate();

        InstallKeyboardHook();
        var hwnd = new WindowInteropHelper(this).Handle;
        SetForegroundWindow(hwnd);
    }

    private void HideLockScreen()
    {
        LockPanel.Visibility = Visibility.Collapsed;
        WarningPanel.Visibility = Visibility.Collapsed;
        PasswordPanel.Visibility = Visibility.Collapsed;
        PasswordBox.Password = string.Empty;
        UninstallKeyboardHook();
        Hide();
    }

    private void DismissWarning_Click(object sender, RoutedEventArgs e)
    {
        WarningPanel.Visibility = Visibility.Collapsed;
        Hide();
    }

    private void UnlockButton_Click(object sender, RoutedEventArgs e)
    {
        if (DateTime.Now < _lockoutUntil)
        {
            ErrorText.Text = $"Too many attempts. Try again at {_lockoutUntil:HH:mm:ss}";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        PasswordPanel.Visibility = Visibility.Visible;
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void GrantAccess_Click(object sender, RoutedEventArgs e)
    {
        if (DateTime.Now < _lockoutUntil)
        {
            ErrorText.Text = $"Locked out until {_lockoutUntil:HH:mm:ss}";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        var config = ConfigService.LoadConfig();
        var password = PasswordBox.Password;

        if (!PasswordService.VerifyPassword(password, config))
        {
            _failedAttempts++;
            if (_failedAttempts >= 3)
            {
                _lockoutUntil = DateTime.Now.AddMinutes(5);
                _failedAttempts = 0;
                ErrorText.Text = "Too many failed attempts. Locked for 5 minutes.";
            }
            else
            {
                ErrorText.Text = $"Incorrect password. {3 - _failedAttempts} attempts remaining.";
            }
            ErrorText.Visibility = Visibility.Visible;
            PasswordBox.Password = string.Empty;
            return;
        }

        if (!int.TryParse(ExtraMinutesBox.Text, out var extraMinutes) || extraMinutes <= 0)
        {
            ErrorText.Text = "Enter a valid number of minutes.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            var state = ConfigService.LoadState();
            var userState = state.GetOrCreate(App.TargetUsername);
            userState.ExtraMinutesGranted += extraMinutes;
            userState.IsLocked = false;
            ConfigService.SaveState(state);

            LogService.Log(App.TargetUsername, $"Extra time granted: {extraMinutes} min");

            try { File.Delete(PipeCommands.CommandFilePath(App.TargetUsername)); } catch { }

            _failedAttempts = 0;
            HideLockScreen();
        }
        catch (Exception ex)
        {
            ErrorText.Text = $"Error: {ex.Message}";
            ErrorText.Visibility = Visibility.Visible;
            LockScreenLog($"GrantAccess error: {ex}");
        }
    }

    private void InstallKeyboardHook()
    {
        _hookProc = HookCallback;
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        using var module = process.MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(module.ModuleName), 0);
    }

    private void UninstallKeyboardHook()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && LockPanel.Visibility == Visibility.Visible)
        {
            var vkCode = Marshal.ReadInt32(lParam);
            // Block Alt+Tab, Alt+F4, Win key
            bool altPressed = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
            if (vkCode == 0x09 && altPressed) return (IntPtr)1; // Tab
            if (vkCode == 0x73 && altPressed) return (IntPtr)1; // F4
            if (vkCode == 0x5B || vkCode == 0x5C) return (IntPtr)1; // Win keys
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (LockPanel.Visibility == Visibility.Visible)
        {
            e.Cancel = true;
            return;
        }
        _pipeCts?.Cancel();
        UninstallKeyboardHook();
        base.OnClosing(e);
    }
}
