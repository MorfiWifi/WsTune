using System.Security.Cryptography;

namespace WsTune.Performance.Tests.Infrastructure;

internal static class PayloadFactory
{
    public static byte[] CreatePatternBuffer(int length, byte seed = 0)
    {
        var buffer = new byte[length];
        for (var i = 0; i < length; i++)
            buffer[i] = (byte)((seed + i) & 0xFF);
        return buffer;
    }

    public static bool BuffersEqual(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
    {
        return expected.SequenceEqual(actual);
    }

    public static string HashHex(ReadOnlySpan<byte> data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash)[..16];
    }
}
