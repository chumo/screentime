using System.Windows;
using System.Windows.Controls;
using ScreenTime.Common.Models;
using ScreenTime.Common.Services;

namespace ScreenTime.Config;

public partial class MainWindow : Window
{
    private AppConfig _config = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _config = ConfigService.LoadConfig();

        if (!ConfigService.HasPassword())
        {
            PasswordTitle.Visibility = Visibility.Collapsed;
            GatePasswordBox.Visibility = Visibility.Collapsed;
            SetupPanel.Visibility = Visibility.Visible;
            // Hide the login button for first-time setup
            ((Button)((StackPanel)PasswordGate.Children[0]).Children[2]).Visibility = Visibility.Collapsed;
        }
    }

    private void GateLogin_Click(object sender, RoutedEventArgs e)
    {
        if (PasswordService.VerifyPassword(GatePasswordBox.Password, _config))
        {
            ShowConfigPanel();
        }
        else
        {
            GateError.Text = "Incorrect password.";
            GateError.Visibility = Visibility.Visible;
            GatePasswordBox.Password = string.Empty;
        }
    }

    private void SetPassword_Click(object sender, RoutedEventArgs e)
    {
        var pass = NewPasswordBox.Password;
        var confirm = ConfirmPasswordBox.Password;

        if (string.IsNullOrWhiteSpace(pass) || pass.Length < 4)
        {
            GateError.Text = "Password must be at least 4 characters.";
            GateError.Visibility = Visibility.Visible;
            return;
        }
        if (pass != confirm)
        {
            GateError.Text = "Passwords do not match.";
            GateError.Visibility = Visibility.Visible;
            return;
        }

        var (hash, salt) = PasswordService.HashPassword(pass);
        _config.PasswordHash = hash;
        _config.PasswordSalt = salt;
        ConfigService.SaveConfig(_config);
        ShowConfigPanel();
    }

    private void ShowConfigPanel()
    {
        PasswordGate.Visibility = Visibility.Collapsed;
        ConfigPanel.Visibility = Visibility.Visible;
        LoadConfigToUI();
    }

    private void LoadConfigToUI()
    {
        InactivityBox.Text = _config.InactivityTimeoutMinutes.ToString();
        WarningBox.Text = _config.WarningMinutes.ToString();
        ResetTimeBox.Text = _config.ResetTime;

        UserList.Items.Clear();
        foreach (var user in _config.Users)
            UserList.Items.Add(user.Username);

        HistoryUserCombo.Items.Clear();
        foreach (var user in _config.Users)
            HistoryUserCombo.Items.Add(user.Username);
    }

    private void AddUser_Click(object sender, RoutedEventArgs e)
    {
        var username = NewUsernameBox.Text.Trim();
        if (string.IsNullOrEmpty(username)) return;
        if (_config.Users.Any(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase))) return;

        _config.Users.Add(new UserConfig { Username = username });
        UserList.Items.Add(username);
        HistoryUserCombo.Items.Add(username);
        NewUsernameBox.Text = string.Empty;
    }

    private void RemoveUser_Click(object sender, RoutedEventArgs e)
    {
        if (UserList.SelectedItem is not string selected) return;
        _config.Users.RemoveAll(u => u.Username.Equals(selected, StringComparison.OrdinalIgnoreCase));
        UserList.Items.Remove(selected);
        HistoryUserCombo.Items.Remove(selected);
        LimitsPanel.Visibility = Visibility.Collapsed;
    }

    private void UserList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UserList.SelectedItem is not string selected) return;

        var user = _config.Users.FirstOrDefault(u =>
            u.Username.Equals(selected, StringComparison.OrdinalIgnoreCase));
        if (user == null) return;

        LimitsPanel.Visibility = Visibility.Visible;
        MonBox.Text = user.Limits.Monday.ToString();
        TueBox.Text = user.Limits.Tuesday.ToString();
        WedBox.Text = user.Limits.Wednesday.ToString();
        ThuBox.Text = user.Limits.Thursday.ToString();
        FriBox.Text = user.Limits.Friday.ToString();
        SatBox.Text = user.Limits.Saturday.ToString();
        SunBox.Text = user.Limits.Sunday.ToString();
    }

    private void HistoryUser_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryUserCombo.SelectedItem is not string username) return;
        var summaries = LogService.GetSummaries(username);
        HistoryGrid.ItemsSource = summaries;
    }

    private void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        var pass = ChangePasswordBox.Password;
        var confirm = ChangePasswordConfirmBox.Password;

        if (string.IsNullOrWhiteSpace(pass) || pass.Length < 4)
        {
            MessageBox.Show("Password must be at least 4 characters.", "Error");
            return;
        }
        if (pass != confirm)
        {
            MessageBox.Show("Passwords do not match.", "Error");
            return;
        }

        var (hash, salt) = PasswordService.HashPassword(pass);
        _config.PasswordHash = hash;
        _config.PasswordSalt = salt;
        ChangePasswordBox.Password = string.Empty;
        ChangePasswordConfirmBox.Password = string.Empty;
        MessageBox.Show("Password changed successfully.", "Success");
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(InactivityBox.Text, out var inactivity))
            _config.InactivityTimeoutMinutes = inactivity;
        if (int.TryParse(WarningBox.Text, out var warning))
            _config.WarningMinutes = warning;
        _config.ResetTime = ResetTimeBox.Text;

        // Save limits for selected user
        if (UserList.SelectedItem is string selected)
        {
            var user = _config.Users.FirstOrDefault(u =>
                u.Username.Equals(selected, StringComparison.OrdinalIgnoreCase));
            if (user != null)
            {
                if (int.TryParse(MonBox.Text, out var mon)) user.Limits.Monday = mon;
                if (int.TryParse(TueBox.Text, out var tue)) user.Limits.Tuesday = tue;
                if (int.TryParse(WedBox.Text, out var wed)) user.Limits.Wednesday = wed;
                if (int.TryParse(ThuBox.Text, out var thu)) user.Limits.Thursday = thu;
                if (int.TryParse(FriBox.Text, out var fri)) user.Limits.Friday = fri;
                if (int.TryParse(SatBox.Text, out var sat)) user.Limits.Saturday = sat;
                if (int.TryParse(SunBox.Text, out var sun)) user.Limits.Sunday = sun;
            }
        }

        ConfigService.SaveConfig(_config);
        MessageBox.Show("Configuration saved.", "ScreenTime");
    }
}
