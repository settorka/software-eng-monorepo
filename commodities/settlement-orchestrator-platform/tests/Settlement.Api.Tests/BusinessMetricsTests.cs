using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Settlement.Api.Tests;

[Collection("ApiHost")]
public sealed class BusinessMetricsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task AcceptedTradeIsExposedAsBusinessMetric()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/trades");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        request.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
        request.Content = new StringContent(
            $$"""
            {
              "tradeId": "TRD-METRICS-{{Guid.NewGuid():N}}",
              "tradeVersion": 1,
              "commodity": "POWER",
              "counterparty": "CP-METRICS",
              "quantity": 10,
              "unit": "MWH",
              "price": 50.25,
              "currency": "GBP",
              "tradeDate": "2026-07-18",
              "settlementDate": "2026-07-31"
            }
            """,
            Encoding.UTF8,
            "application/json");

        var tradeResponse = await client.SendAsync(request);
        var metrics = await client.GetStringAsync("/metrics");

        Assert.Equal(HttpStatusCode.Accepted, tradeResponse.StatusCode);
        Assert.Contains("settlement_trades_received_total", metrics, StringComparison.Ordinal);
        Assert.Contains("commodity=\"POWER\"", metrics, StringComparison.Ordinal);
        Assert.Contains("currency=\"GBP\"", metrics, StringComparison.Ordinal);
    }
}
