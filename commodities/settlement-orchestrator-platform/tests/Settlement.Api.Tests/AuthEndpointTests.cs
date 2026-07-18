using System.Net;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Settlement.Api.Tests;

[Collection("ApiHost")]
public sealed class AuthEndpointTests
{
    [Fact]
    public async Task OperatorEndpointReturnsUnauthorizedWhenAuthIsEnabledAndTokenIsMissing()
    {
        var previousEnabled = Environment.GetEnvironmentVariable("Auth__Enabled");
        var previousAuthority = Environment.GetEnvironmentVariable("Auth__Authority");
        var previousAudience = Environment.GetEnvironmentVariable("Auth__Audience");
        var previousRequireHttpsMetadata = Environment.GetEnvironmentVariable("Auth__RequireHttpsMetadata");
        var previousSigningKey = Environment.GetEnvironmentVariable("Auth__SigningKey");

        try
        {
            Environment.SetEnvironmentVariable("Auth__Enabled", "true");
            Environment.SetEnvironmentVariable("Auth__Authority", "");
            Environment.SetEnvironmentVariable("Auth__Audience", "settlement-api");
            Environment.SetEnvironmentVariable("Auth__RequireHttpsMetadata", "false");
            Environment.SetEnvironmentVariable("Auth__SigningKey", TestSigningKey);

            await using var factory = new WebApplicationFactory<Program>();
            using var client = factory.CreateClient();

            var response = await client.GetAsync("/api/v1/workflows");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Auth__Enabled", previousEnabled);
            Environment.SetEnvironmentVariable("Auth__Authority", previousAuthority);
            Environment.SetEnvironmentVariable("Auth__Audience", previousAudience);
            Environment.SetEnvironmentVariable("Auth__RequireHttpsMetadata", previousRequireHttpsMetadata);
            Environment.SetEnvironmentVariable("Auth__SigningKey", previousSigningKey);
        }
    }

    [Fact]
    public async Task OperatorEndpointAllowsSignedTokenWithOperatorRole()
    {
        var previousEnabled = Environment.GetEnvironmentVariable("Auth__Enabled");
        var previousAuthority = Environment.GetEnvironmentVariable("Auth__Authority");
        var previousAudience = Environment.GetEnvironmentVariable("Auth__Audience");
        var previousRequireHttpsMetadata = Environment.GetEnvironmentVariable("Auth__RequireHttpsMetadata");
        var previousSigningKey = Environment.GetEnvironmentVariable("Auth__SigningKey");

        try
        {
            Environment.SetEnvironmentVariable("Auth__Enabled", "true");
            Environment.SetEnvironmentVariable("Auth__Authority", "");
            Environment.SetEnvironmentVariable("Auth__Audience", "settlement-api");
            Environment.SetEnvironmentVariable("Auth__RequireHttpsMetadata", "false");
            Environment.SetEnvironmentVariable("Auth__SigningKey", TestSigningKey);

            await using var factory = new WebApplicationFactory<Program>();
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken("settlement.operator"));

            var response = await client.GetAsync("/api/v1/workflows");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Auth__Enabled", previousEnabled);
            Environment.SetEnvironmentVariable("Auth__Authority", previousAuthority);
            Environment.SetEnvironmentVariable("Auth__Audience", previousAudience);
            Environment.SetEnvironmentVariable("Auth__RequireHttpsMetadata", previousRequireHttpsMetadata);
            Environment.SetEnvironmentVariable("Auth__SigningKey", previousSigningKey);
        }
    }

    private const string TestSigningKey = "local-test-signing-key-32-bytes-long";

    private static string CreateToken(string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            audience: "settlement-api",
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, "operator-1"),
                new Claim(ClaimTypes.Role, role)
            ],
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
