namespace ScreenTime.Common.Models;

public static class PipeCommands
{
    public const string Warn5 = "WARN_5";
    public const string Warn1 = "WARN_1";
    public const string Lock = "LOCK";
    public const string Unlock = "UNLOCK";
    public const string DismissWarning = "DISMISS_WARNING";
    public const string LaunchConfig = "LAUNCH_CONFIG";

    public static string PipeName(string username) => $"ScreenTime_{username}";

    public static string CommandFilePath(string username) =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ScreenTime",
            $"command_{username}.txt");

    public static string TimeRemainingFilePath(string username) =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ScreenTime",
            $"remaining_{username}.txt");
}
