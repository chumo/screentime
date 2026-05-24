using System.Security.Cryptography;
using ScreenTime.Common.Models;

namespace ScreenTime.Common.Services;

public static class PasswordService
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;

    public static (string hash, string salt) HashPassword(string password)
    {
        var saltBytes = RandomNumberGenerator.GetBytes(SaltSize);
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(
            password,
            saltBytes,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSize);

        return (Convert.ToBase64String(hashBytes), Convert.ToBase64String(saltBytes));
    }

    public static bool VerifyPassword(string password, AppConfig config)
    {
        if (string.IsNullOrEmpty(config.PasswordHash) || string.IsNullOrEmpty(config.PasswordSalt))
            return false;

        var saltBytes = Convert.FromBase64String(config.PasswordSalt);
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(
            password,
            saltBytes,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSize);

        return CryptographicOperations.FixedTimeEquals(
            hashBytes,
            Convert.FromBase64String(config.PasswordHash));
    }
}
