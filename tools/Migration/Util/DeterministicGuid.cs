using System.Security.Cryptography;
using System.Text;

namespace Nzmpta.AutoRep.Migration.Util;

/// <summary>
/// RFC 4122 v5 (SHA-1, name-based) GUIDs. Stable across runs for the same (namespace, name), which
/// is what makes the migration idempotent: the same legacy row always maps to the same target id.
/// </summary>
public static class DeterministicGuid
{
    public static Guid Create(Guid namespaceId, string name)
    {
        var ns = namespaceId.ToByteArray();
        SwapByteOrder(ns); // .NET stores the first 3 groups little-endian; the RFC wants big-endian

        var nameBytes = Encoding.UTF8.GetBytes(name);
        var input = new byte[ns.Length + nameBytes.Length];
        Buffer.BlockCopy(ns, 0, input, 0, ns.Length);
        Buffer.BlockCopy(nameBytes, 0, input, ns.Length, nameBytes.Length);

        var hash = SHA1.HashData(input);

        var guid = new byte[16];
        Array.Copy(hash, 0, guid, 0, 16);
        guid[6] = (byte)((guid[6] & 0x0F) | (5 << 4)); // version 5
        guid[8] = (byte)((guid[8] & 0x3F) | 0x80);     // RFC variant

        SwapByteOrder(guid);
        return new Guid(guid);
    }

    private static void SwapByteOrder(byte[] g)
    {
        (g[0], g[3]) = (g[3], g[0]);
        (g[1], g[2]) = (g[2], g[1]);
        (g[4], g[5]) = (g[5], g[4]);
        (g[6], g[7]) = (g[7], g[6]);
    }
}
