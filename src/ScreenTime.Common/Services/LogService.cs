namespace ScreenTime.Common.Services;

public static class LogService
{
    public static void Log(string username, string message)
    {
        var logDir = ConfigService.GetLogDir();
        Directory.CreateDirectory(logDir);

        var logFile = Path.Combine(logDir, $"{DateTime.Now:yyyy-MM-dd}.log");
        var line = $"[{DateTime.Now:HH:mm:ss}] [{username}] {message}";
        File.AppendAllText(logFile, line + Environment.NewLine);
    }

    public static void CleanOldLogs(int retentionDays = 30)
    {
        var logDir = ConfigService.GetLogDir();
        if (!Directory.Exists(logDir)) return;

        var cutoff = DateTime.Now.AddDays(-retentionDays);
        foreach (var file in Directory.GetFiles(logDir, "*.log"))
        {
            if (File.GetCreationTime(file) < cutoff)
                File.Delete(file);
        }
    }

    public static List<DailyLogSummary> GetSummaries(string username, int days = 30)
    {
        var logDir = ConfigService.GetLogDir();
        if (!Directory.Exists(logDir)) return new();

        var summaries = new List<DailyLogSummary>();
        var startDate = DateTime.Now.AddDays(-days);

        for (var date = startDate.Date; date <= DateTime.Now.Date; date = date.AddDays(1))
        {
            var logFile = Path.Combine(logDir, $"{date:yyyy-MM-dd}.log");
            if (!File.Exists(logFile)) continue;

            var lines = File.ReadAllLines(logFile)
                .Where(l => l.Contains($"[{username}]", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (lines.Count == 0) continue;

            var summary = new DailyLogSummary
            {
                Date = date,
                SessionCount = lines.Count(l => l.Contains("Session started")),
                LimitReached = lines.Any(l => l.Contains("Limit reached")),
                ExtraTimeGranted = lines.Count(l => l.Contains("Extra time granted")),
                TotalMinutes = ExtractTotalMinutes(lines)
            };
            summaries.Add(summary);
        }

        return summaries;
    }

    private static int ExtractTotalMinutes(List<string> lines)
    {
        var lastTotal = lines.LastOrDefault(l => l.Contains("Total active:"));
        if (lastTotal == null) return 0;

        var idx = lastTotal.IndexOf("Total active:");
        var rest = lastTotal[(idx + 13)..].Trim();
        if (int.TryParse(rest.Split(' ')[0], out var mins))
            return mins;
        return 0;
    }
}

public class DailyLogSummary
{
    public DateTime Date { get; set; }
    public int TotalMinutes { get; set; }
    public int SessionCount { get; set; }
    public bool LimitReached { get; set; }
    public int ExtraTimeGranted { get; set; }
}
