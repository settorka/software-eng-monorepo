using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Settlement.Infrastructure.Outbox;
using Settlement.Infrastructure.Persistence.Entities;
using Xunit;

namespace Settlement.IntegrationTests;

public sealed class HttpOutboxPublisherTests
{
    [Fact]
    public async Task PublishAsyncPostsEnvelopeWithIdempotencyHeaders()
    {
        var handler = new RecordingHandler();
        var publisher = new HttpOutboxPublisher(
            new HttpClient(handler),
            Options.Create(new OutboxPublisherOptions
            {
                Mode = "Http",
                Endpoint = "https://broker.example.test/payment-requests",
                ApiKey = "test-token",
                TimeoutMilliseconds = 1000,
                MaxPayloadBytes = 1024
            }),
            NullLogger<HttpOutboxPublisher>.Instance);

        var message = new OutboxMessageEntity
        {
            OutboxMessageId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            WorkflowId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            MessageType = "PaymentRequestCreated",
            PayloadHash = "payload-hash",
            Payload = "{\"paymentRequestId\":\"PAY-1\"}",
            CreatedAt = new DateTimeOffset(2026, 7, 18, 0, 0, 0, TimeSpan.Zero)
        };

        await publisher.PublishAsync(message, CancellationToken.None);

        Assert.NotNull(handler.Request);
        Assert.Equal(HttpMethod.Post, handler.Request.Method);
        Assert.Equal("https://broker.example.test/payment-requests", handler.Request.RequestUri?.ToString());
        Assert.Equal(message.OutboxMessageId.ToString("N"), handler.Request.Headers.GetValues("Idempotency-Key").Single());
        Assert.Equal(message.WorkflowId.ToString("N"), handler.Request.Headers.GetValues("X-Workflow-Id").Single());
        Assert.Equal("Bearer", handler.Request.Headers.Authorization?.Scheme);
        Assert.Equal("test-token", handler.Request.Headers.Authorization?.Parameter);

        using var document = JsonDocument.Parse(handler.Body);
        Assert.Equal("PaymentRequestCreated", document.RootElement.GetProperty("messageType").GetString());
        Assert.Equal("payload-hash", document.RootElement.GetProperty("payloadHash").GetString());
        Assert.Equal("{\"paymentRequestId\":\"PAY-1\"}", document.RootElement.GetProperty("payload").GetString());
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage Request { get; private set; } = null!;

        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }
    }
}
