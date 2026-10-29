using System.Security.Cryptography;
using System.Text;

namespace NtftEnhanced.Security;

public sealed record PasswordRecord(int Iterations, byte[] Salt, byte[] Hash)
{
    public string ToStorage() =>
        $"{Iterations}.{Convert.ToBase64String(Salt)}.{Convert.ToBase64String(Hash)}";

    public static PasswordRecord? Parse(string? storage)
    {
        if (string.IsNullOrWhiteSpace(storage))
            return null;
        var parts = storage.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations) || iterations < 1)
            return null;
        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var hash = Convert.FromBase64String(parts[2]);
            if (salt.Length != 16 || hash.Length != 32)
                return null;
            return new PasswordRecord(iterations, salt, hash);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

public static class PasswordHasher
{
    public const int ProductionIterations = 600_000;

    public static PasswordRecord Hash(string password, int iterations)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Derive(password, salt, iterations);
        return new PasswordRecord(iterations, salt, hash);
    }

    public static bool Verify(string password, PasswordRecord record)
    {
        var hash = Derive(password, record.Salt, record.Iterations);
        return CryptographicOperations.FixedTimeEquals(hash, record.Hash);
    }

    public static bool VerifyStored(string password, string? storage)
    {
        var record = PasswordRecord.Parse(storage);
        if (record is null)
        {
            Derive(password, new byte[16], PasswordHasher.ProductionIterations > 10_000 ? 10_000 : 1_000);
            return false;
        }
        return Verify(password, record);
    }

    private static byte[] Derive(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
}

public static class RecoveryKey
{
    public static (string Display, PasswordRecord Record) Create(int iterations)
    {
        var display = Format(RandomNumberGenerator.GetBytes(16));
        return (display, PasswordHasher.Hash(Normalize(display), iterations));
    }

    public static string Normalize(string key)
    {
        var hex = new string(key.Where(Uri.IsHexDigit).ToArray());
        return hex.ToUpperInvariant();
    }

    public static string Format(byte[] bytes)
    {
        var hex = Convert.ToHexString(bytes);
        return string.Join('-', Enumerable.Range(0, hex.Length / 4).Select(index => hex.Substring(index * 4, 4)));
    }
}
