using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
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

        try
        {
            Environment.SetEnvironmentVariable("Auth__Enabled", "true");
            Environment.SetEnvironmentVariable("Auth__Authority", "http://localhost");
            Environment.SetEnvironmentVariable("Auth__Audience", "settlement-api");
            Environment.SetEnvironmentVariable("Auth__RequireHttpsMetadata", "false");

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
        }
    }
}
