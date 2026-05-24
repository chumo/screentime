using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScreenTime.Common.Models;
using ScreenTime.Common.Services;

namespace ScreenTime.Service;

public class ScreenTimeWorker : BackgroundService
{
    private readonly ILogger<ScreenTimeWorker> _logger;
    private readonly Dictionary<string, bool> _warn5Sent = new();
    private readonly Dictionary<string, bool> _warn1Sent = new();
    private readonly Dictionary<string, Process?> _lockScreenProcesses = new();

    [StructLayout(LayoutKind.Sequential)]
    private struct WTSINFO
    {
        public int State;
        public int SessionId;
        public int IncomingBytes;
        public int OutgoingBytes;
        public int IncomingFrames;
        public int OutgoingFrames;
        public int IncomingCompressedBytes;
        public int OutgoingCompressedBytes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string WinStationName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 17)]
        public string Domain;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
        public string UserName;
        public long ConnectTime;
        public long DisconnectTime;
        public long LastInputTime;
        public long LogonTime;
        public long CurrentTime;
    }

    public ScreenTimeWorker(ILogger<ScreenTimeWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ConfigService.EnsureDirectories();
        LogService.CleanOldLogs();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessTick();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in main loop");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    private void DebugLog(string message)
    {
        var debugFile = Path.Combine(ConfigService.GetConfigDir(), "debug.log");
        File.AppendAllText(debugFile, $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private async Task ProcessTick()
    {
        var config = ConfigService.LoadConfig();
        if (config.Users.Count == 0)
        {
            DebugLog("No users configured");
            return;
        }

        var state = ConfigService.LoadState();
        var activeUser = GetActiveConsoleUser();
        DebugLog($"Tick: activeUser='{activeUser}', configuredUsers={string.Join(",", config.Users.Select(u => u.Username))}");

        foreach (var userConfig in config.Users)
        {
            var username = userConfig.Username;
            var userState = state.GetOrCreate(username);
            var resetTime = TimeSpan.TryParse(config.ResetTime, out var rt) ? rt : new TimeSpan(1, 0, 0);

            CheckDayReset(userState, resetTime);

            var isActiveUser = !string.IsNullOrEmpty(activeUser) &&
                               username.Equals(activeUser, StringComparison.OrdinalIgnoreCase);
            var isUserActive = isActiveUser && IsUserActive(config.InactivityTimeoutMinutes);
            DebugLog($"  {username}: isActiveUser={isActiveUser}, isUserActive={isUserActive}, accumulated={userState.AccumulatedSeconds}");

            if (isUserActive && !userState.IsLocked)
            {
                userState.AccumulatedSeconds += 30;
                var totalAllowedMinutes = userConfig.Limits.GetLimit(GetEffectiveDay(resetTime))
                                          + userState.ExtraMinutesGranted;
                var remainingSeconds = (totalAllowedMinutes * 60) - userState.AccumulatedSeconds;

                if (remainingSeconds <= 0)
                {
                    userState.IsLocked = true;
                    await SendCommand(username, PipeCommands.Lock);
                    LogService.Log(username, $"Limit reached. Total active: {userState.AccumulatedSeconds / 60} min");
                    _warn5Sent[username] = false;
                    _warn1Sent[username] = false;
                }
                else if (remainingSeconds <= 60 && !_warn1Sent.GetValueOrDefault(username))
                {
                    await SendCommand(username, PipeCommands.Warn1);
                    _warn1Sent[username] = true;
                    LogService.Log(username, "1-minute warning shown");
                }
                else if (remainingSeconds <= config.WarningMinutes * 60 && !_warn5Sent.GetValueOrDefault(username))
                {
                    await SendCommand(username, PipeCommands.Warn5);
                    _warn5Sent[username] = true;
                    LogService.Log(username, $"{config.WarningMinutes}-minute warning shown");
                }
            }

            EnsureLockScreenProcess(username, isActiveUser);

            if (userState.IsLocked && isActiveUser)
            {
                await SendCommand(username, PipeCommands.Lock);
            }
        }

        ConfigService.SaveState(state);
    }

    private void CheckDayReset(UserState userState, TimeSpan resetTime)
    {
        var now = DateTime.Now;
        var effectiveDate = now.TimeOfDay < resetTime
            ? now.Date.AddDays(-1).ToString("yyyy-MM-dd")
            : now.Date.ToString("yyyy-MM-dd");

        if (userState.CurrentDate != effectiveDate)
        {
            userState.CurrentDate = effectiveDate;
            userState.AccumulatedSeconds = 0;
            userState.ExtraMinutesGranted = 0;
            userState.IsLocked = false;
            LogService.Log(userState.Username, "Day reset");
        }
    }

    private DayOfWeek GetEffectiveDay(TimeSpan resetTime)
    {
        var now = DateTime.Now;
        return now.TimeOfDay < resetTime
            ? now.AddDays(-1).DayOfWeek
            : now.DayOfWeek;
    }

    private bool IsUserActive(int inactivityTimeoutMinutes)
    {
        // WTSSessionInfo idle detection is unreliable from session 0.
        // For now, assume active if there's an active console session.
        // Idle detection will be handled in a future update via the LockScreen process.
        var sessionId = WTSGetActiveConsoleSessionId();
        return sessionId != 0xFFFFFFFF;
    }

    private string? GetActiveConsoleUser()
    {
        try
        {
            var sessionId = WTSGetActiveConsoleSessionId();
            if (sessionId == 0xFFFFFFFF) return null;

            if (WTSQuerySessionInformation(IntPtr.Zero, sessionId, WTS_INFO_CLASS.WTSUserName, out var buffer, out _))
            {
                var username = Marshal.PtrToStringUni(buffer);
                WTSFreeMemory(buffer);
                return username;
            }
        }
        catch { }
        return null;
    }

    private void EnsureLockScreenProcess(string username, bool isActiveUser)
    {
        if (!isActiveUser) return;

        if (_lockScreenProcesses.TryGetValue(username, out var proc) && proc != null && !proc.HasExited)
            return;

        try
        {
            var serviceDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            var installDir = Path.GetDirectoryName(serviceDir)!;
            var exePath = Path.Combine(installDir, "LockScreen", "ScreenTime.LockScreen.exe");
            DebugLog($"  LockScreen path: {exePath}, exists={File.Exists(exePath)}");
            if (!File.Exists(exePath)) return;

            var sessionId = WTSGetActiveConsoleSessionId();
            if (sessionId == 0xFFFFFFFF) return;

            if (!LaunchProcessInSession(exePath, username, sessionId))
            {
                DebugLog($"  Failed to launch LockScreen in session {sessionId}");
            }
        }
        catch (Exception ex)
        {
            DebugLog($"  LockScreen launch error: {ex.Message}");
            _logger.LogError(ex, "Failed to launch lock screen for {User}", username);
        }
    }

    private bool LaunchProcessInSession(string exePath, string args, uint sessionId)
    {
        if (!WTSQueryUserToken(sessionId, out var userToken))
        {
            DebugLog($"  WTSQueryUserToken failed: {Marshal.GetLastWin32Error()}");
            return false;
        }

        try
        {
            var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
            si.lpDesktop = "winsta0\\default";
            var pi = new PROCESS_INFORMATION();

            var cmdLine = $"\"{exePath}\" {args}";
            var result = CreateProcessAsUser(
                userToken, null, cmdLine,
                IntPtr.Zero, IntPtr.Zero, false,
                0x00000010, // CREATE_NEW_CONSOLE
                IntPtr.Zero, null, ref si, out pi);

            if (result)
            {
                _lockScreenProcesses[args] = Process.GetProcessById(pi.dwProcessId);
                CloseHandle(pi.hProcess);
                CloseHandle(pi.hThread);
                LogService.Log(args, "Session started");
                DebugLog($"  LockScreen launched in session {sessionId}, PID={pi.dwProcessId}");
                return true;
            }
            else
            {
                DebugLog($"  CreateProcessAsUser failed: {Marshal.GetLastWin32Error()}");
                return false;
            }
        }
        finally
        {
            CloseHandle(userToken);
        }
    }

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr phToken);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(
        IntPtr hToken, string? lpApplicationName, string lpCommandLine,
        IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles,
        uint dwCreationFlags, IntPtr lpEnvironment, string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public int dwX, dwY, dwXSize, dwYSize;
        public int dwXCountChars, dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    private async Task SendCommand(string username, string command)
    {
        var pipeName = PipeCommands.PipeName(username);
        try
        {
            DebugLog($"  SendCommand: pipe='{pipeName}', command='{command}'");
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            await client.ConnectAsync(2000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            await writer.WriteLineAsync(command);
            DebugLog($"  SendCommand: success");
        }
        catch (Exception ex)
        {
            DebugLog($"  SendCommand: FAILED - {ex.Message}");
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool WTSQuerySessionInformation(IntPtr hServer, uint sessionId, WTS_INFO_CLASS wtsInfoClass, out IntPtr ppBuffer, out uint bytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr pointer);

    private enum WTS_INFO_CLASS { WTSUserName = 5, WTSSessionInfo = 24 }
}
