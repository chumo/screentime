# ScreenTime

A Windows 11 application that controls and limits the time a machine is accessible to specific users, managed by a superuser.

## Features

- **Per-user daily time limits** — configure allowed minutes for each day of the week per user
- **Activity-based tracking** — timer only runs when the user is actively using the computer (mouse/keyboard input)
- **Configurable inactivity timeout** — pauses the timer after N minutes of no input (default: 5 minutes)
- **Warnings before lockout** — persistent always-on-top popup at 5 minutes and 1 minute remaining
- **Fullscreen lock screen** — blocks access when time is exhausted, resistant to Alt+Tab/Alt+F4/Win key
- **Superuser unlock** — grant extra time from the lock screen with a password
- **Multi-user support** — tracks time independently per Windows user account
- **Usage history** — view daily usage summaries per user in the config app
- **Runs as a Windows Service** — starts at boot, cannot be easily killed by the controlled user
- **Configurable day reset time** — defaults to 1:00 AM

## Architecture

| Component | Description |
|-----------|-------------|
| `ScreenTime.Service` | Windows Service that tracks activity, manages timers, enforces limits |
| `ScreenTime.LockScreen` | WPF app that displays warnings and the fullscreen lock screen |
| `ScreenTime.Config` | WPF app for superuser configuration (password-protected) |
| `ScreenTime.Common` | Shared models, configuration, and services |

The service communicates with the lock screen process via named pipes.

## Installation

### From Release (Recommended)

1. Download the latest `ScreenTime.msi` from the [Releases](../../releases) page
2. Right-click the MSI and select **Run as administrator**
3. Follow the installation wizard
4. The service starts automatically after installation

### First-Time Setup

1. Open **ScreenTime Config** from the Start Menu
2. You will be prompted to create a superuser password — remember this, there is no recovery
3. Go to the **Users & Limits** tab:
   - Enter a Windows username in the text box and click **Add User**
   - Select the user and set daily time limits (in minutes) for each day of the week
4. Go to the **Settings** tab to adjust:
   - **Inactivity Timeout** — minutes of no input before the timer pauses (default: 5)
   - **Warning Before Lockout** — minutes before limit when the warning appears (default: 5)
   - **Day Reset Time** — when the daily counter resets (default: 01:00)
5. Click **Save Configuration**

Enforcement begins immediately for configured users.

## How It Works

1. The service polls every 30 seconds, checking which user is active on the console session
2. If the active user is in the controlled list and has had recent input (within the inactivity timeout), the timer increments
3. When remaining time reaches the warning threshold, a persistent popup appears
4. A second warning appears at 1 minute remaining
5. When time is exhausted, a fullscreen lock screen activates with keyboard hooks to prevent dismissal
6. The superuser can click "Superuser Unlock" on the lock screen, enter the password, and grant extra minutes
7. If the computer is shut down or the user goes idle, the timer pauses and resumes when activity returns
8. At the configured reset time (default 1:00 AM), all counters reset for the new day

## Superuser Unlock

When the lock screen is active:
1. Click **Superuser Unlock**
2. Enter the superuser password
3. Enter the number of extra minutes to grant
4. Click **Grant Access**

After 3 failed password attempts, the unlock is locked out for 5 minutes.

## Uninstallation

1. Open **Settings > Apps > Installed apps** in Windows
2. Find **ScreenTime** and click **Uninstall**
3. The service will be stopped and removed automatically

Configuration data is stored in `C:\ProgramData\ScreenTime\` and is not removed on uninstall. Delete this folder manually to remove all data.

## Building from Source

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (win-x64)
- [WiX Toolset v4](https://wixtoolset.org/) (`dotnet tool install --global wix`)

### Build

```powershell
dotnet restore ScreenTime.sln
dotnet build ScreenTime.sln -c Release

# Publish all components
dotnet publish src/ScreenTime.Service/ScreenTime.Service.csproj -c Release -r win-x64 --self-contained -o publish/Service
dotnet publish src/ScreenTime.LockScreen/ScreenTime.LockScreen.csproj -c Release -r win-x64 --self-contained -o publish/LockScreen
dotnet publish src/ScreenTime.Config/ScreenTime.Config.csproj -c Release -r win-x64 --self-contained -o publish/Config

# Build MSI
wix build installer/Package.wxs `
    -d ServicePublishDir=publish/Service `
    -d LockScreenPublishDir=publish/LockScreen `
    -d ConfigPublishDir=publish/Config `
    -o ScreenTime.msi
```

### Manual Service Installation (without MSI)

```powershell
# Run as Administrator
sc.exe create ScreenTimeService binPath="C:\path\to\ScreenTime.Service.exe" start=auto
sc.exe start ScreenTimeService
```

## Configuration Files

All configuration is stored in `C:\ProgramData\ScreenTime\`:

| File | Purpose |
|------|---------|
| `config.json` | User list, limits, settings, hashed password |
| `state.json` | Current day usage tracking per user |
| `logs/` | Daily log files (30-day retention) |

## Security Notes

- The superuser password is hashed with PBKDF2 (100,000 iterations, SHA-256)
- Configuration files are accessible only to SYSTEM and Administrators
- The service runs as LocalSystem for maximum privilege
- The lock screen blocks Alt+Tab, Alt+F4, and Windows key via low-level keyboard hooks
- There is no password recovery — if forgotten, delete `config.json` (requires admin access) and reconfigure

## License

MIT
