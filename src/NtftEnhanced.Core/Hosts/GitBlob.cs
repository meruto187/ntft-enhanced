using System.Security.Cryptography;
using System.Text;

namespace NtftEnhanced.Hosts;

public static class GitBlob
{
    public static string Sha1Hex(ReadOnlySpan<byte> content)
    {
        var header = Encoding.ASCII.GetBytes($"blob {content.Length}\0");
        var buffer = new byte[header.Length + content.Length];
        header.CopyTo(buffer, 0);
        content.CopyTo(buffer.AsSpan(header.Length));
        return Convert.ToHexString(SHA1.HashData(buffer)).ToLowerInvariant();
    }

    public static bool ShaMatches(string expected, string actual)
    {
        if (expected.Length != actual.Length || expected.Length == 0)
            return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected.ToLowerInvariant()),
            Encoding.ASCII.GetBytes(actual.ToLowerInvariant()));
    }
}
