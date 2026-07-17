using System.Security.Cryptography;
using System.Text;

namespace Settlement.Application.Common;

public static class DeterministicGuid
{
    public static Guid From(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(value), hash);

        return new Guid(hash[..16]);
    }
}

