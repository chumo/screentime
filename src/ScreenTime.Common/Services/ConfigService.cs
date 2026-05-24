using System.Text.Json;
using ScreenTime.Common.Models;

namespace ScreenTime.Common.Services;

public static class ConfigService
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ScreenTime");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");
    private static readonly string StatePath = Path.Combine(ConfigDir, "state.json");
    private static readonly string LogDir = Path.Combine(ConfigDir, "logs");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string GetConfigDir() => ConfigDir;
    public static string GetLogDir() => LogDir;

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(LogDir);
    }

    public static AppConfig LoadConfig()
    {
        if (!File.Exists(ConfigPath))
            return new AppConfig();

        var json = File.ReadAllText(ConfigPath);
        return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
    }

    public static void SaveConfig(AppConfig config)
    {
        EnsureDirectories();
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }

    public static AppState LoadState()
    {
        if (!File.Exists(StatePath))
            return new AppState();

        var json = File.ReadAllText(StatePath);
        return JsonSerializer.Deserialize<AppState>(json, JsonOptions) ?? new AppState();
    }

    public static void SaveState(AppState state)
    {
        EnsureDirectories();
        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(StatePath, json);
    }

    public static bool ConfigExists() => File.Exists(ConfigPath);
    public static bool HasPassword() => LoadConfig().PasswordHash.Length > 0;
}
