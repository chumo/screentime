namespace ScreenTime.Common.Models;

public class AppConfig
{
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public int InactivityTimeoutMinutes { get; set; } = 5;
    public int WarningMinutes { get; set; } = 5;
    public string ResetTime { get; set; } = "01:00";
    public List<UserConfig> Users { get; set; } = new();
}

public class UserConfig
{
    public string Username { get; set; } = string.Empty;
    public DailyLimits Limits { get; set; } = new();
}

public class DailyLimits
{
    public int Monday { get; set; } = 120;
    public int Tuesday { get; set; } = 120;
    public int Wednesday { get; set; } = 120;
    public int Thursday { get; set; } = 120;
    public int Friday { get; set; } = 120;
    public int Saturday { get; set; } = 120;
    public int Sunday { get; set; } = 120;

    public int GetLimit(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => Monday,
        DayOfWeek.Tuesday => Tuesday,
        DayOfWeek.Wednesday => Wednesday,
        DayOfWeek.Thursday => Thursday,
        DayOfWeek.Friday => Friday,
        DayOfWeek.Saturday => Saturday,
        DayOfWeek.Sunday => Sunday,
        _ => 120
    };
}
