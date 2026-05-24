namespace ScreenTime.Common.Models;

public class UserState
{
    public string Username { get; set; } = string.Empty;
    public string CurrentDate { get; set; } = string.Empty;
    public int AccumulatedSeconds { get; set; }
    public int ExtraMinutesGranted { get; set; }
    public bool IsLocked { get; set; }
}

public class AppState
{
    public List<UserState> Users { get; set; } = new();

    public UserState GetOrCreate(string username)
    {
        var user = Users.FirstOrDefault(u =>
            u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        if (user == null)
        {
            user = new UserState { Username = username };
            Users.Add(user);
        }
        return user;
    }
}
