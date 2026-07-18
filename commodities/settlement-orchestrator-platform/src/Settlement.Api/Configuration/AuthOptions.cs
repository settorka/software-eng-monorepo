namespace Settlement.Api.Configuration;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public bool Enabled { get; init; }

    public string Authority { get; init; } = string.Empty;

    public string Audience { get; init; } = string.Empty;

    public bool RequireHttpsMetadata { get; init; } = true;

    public string SigningKey { get; init; } = string.Empty;
}
