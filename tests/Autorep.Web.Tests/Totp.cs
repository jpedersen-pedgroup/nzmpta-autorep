using System.Net;
using System.Security.Cryptography;

namespace Autorep.Web.Tests;

/// <summary>
/// RFC 6238 codes the way Identity's AuthenticatorTokenProvider checks them: HMAC-SHA1 over the
/// 30-second step counter, six digits, key supplied as the Base32 string the user would type into
/// their app. Lets a test stand in for the authenticator app.
/// </summary>
public static class Totp
{
    public static string Now(string base32Key) => Compute(FromBase32(base32Key), DateTimeOffset.UtcNow);

    /// <summary>A code that is certainly wrong right now: the current one with its last digit changed.</summary>
    public static string Wrong(string base32Key)
    {
        var right = Now(base32Key);
        var last = right[^1] == '9' ? '0' : (char)(right[^1] + 1);
        return right[..^1] + last;
    }

    public static string Compute(byte[] key, DateTimeOffset at)
    {
        var step = (long)Math.Floor((at - DateTimeOffset.UnixEpoch).TotalSeconds / 30);
        var message = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(step));
        var hash = HMACSHA1.HashData(key, message);
        var offset = hash[^1] & 0xf;
        var binary = ((hash[offset] & 0x7f) << 24)
                   | ((hash[offset + 1] & 0xff) << 16)
                   | ((hash[offset + 2] & 0xff) << 8)
                   | (hash[offset + 3] & 0xff);
        return (binary % 1_000_000).ToString("D6");
    }

    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static byte[] FromBase32(string input)
    {
        var s = input.Replace(" ", "").TrimEnd('=').ToUpperInvariant();
        var bytes = new List<byte>(s.Length * 5 / 8);
        var buffer = 0;
        var bits = 0;
        foreach (var c in s)
        {
            var value = Alphabet.IndexOf(c);
            if (value < 0) throw new FormatException($"'{c}' is not a Base32 character.");
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)((buffer >> bits) & 0xff));
            }
        }
        return bytes.ToArray();
    }
}
