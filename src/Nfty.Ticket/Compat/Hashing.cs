using System.Security.Cryptography;

namespace Nfty.Ticket;

/// <summary>
/// SHA-256 and lowercase hex, written to run on netstandard2.1 (no <c>SHA256.HashData</c>, no
/// <c>Convert.ToHexStringLower</c>). Used on every target, so a DNA and a seed hash are computed by one
/// code path whichever assembly a host loads.
/// </summary>
internal static class Hashing
{
    public static byte[] Sha256(byte[] data)
    {
        using var sha = SHA256.Create();
        return sha.ComputeHash(data);
    }

    public static string LowerHex(byte[] bytes)
    {
        const string digits = "0123456789abcdef";
        var chars = new char[bytes.Length * 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            chars[2 * i] = digits[bytes[i] >> 4];
            chars[2 * i + 1] = digits[bytes[i] & 0xF];
        }
        return new string(chars);
    }
}
