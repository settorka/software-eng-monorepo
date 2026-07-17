using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Settlement.Application.Common;

public static class StablePayloadHash
{
    public static string From<T>(T payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }
}

