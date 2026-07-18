using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Settlement.Infrastructure.Persistence.Entities;

namespace Settlement.Infrastructure.Outbox;

public sealed class HttpOutboxPublisher(
    HttpClient httpClient,
    IOptions<OutboxPublisherOptions> options,
    ILogger<HttpOutboxPublisher> logger) : IOutboxPublisher
{
    private readonly OutboxPublisherOptions options = options.Value;

    public async Task PublishAsync(OutboxMessageEntity message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(message);

        using var request = new HttpRequestMessage(HttpMethod.Post, options.Endpoint)
        {
            Content = JsonContent.Create(new OutboxPublishEnvelope(
                message.OutboxMessageId,
                message.WorkflowId,
                message.MessageType,
                message.PayloadHash,
                message.Payload,
                message.CreatedAt))
        };

        request.Headers.Add("Idempotency-Key", message.OutboxMessageId.ToString("N"));
        request.Headers.Add("X-Workflow-Id", message.WorkflowId.ToString("N"));

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Outbox publisher returned {(int)response.StatusCode} {response.ReasonPhrase}: {TrimBody(body)}");
        }

        logger.LogInformation(
            "Published outbox message {OutboxMessageId} to HTTP broker endpoint.",
            message.OutboxMessageId);
    }

    private void Validate(OutboxMessageEntity message)
    {
        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            throw new InvalidOperationException("OutboxPublisher:Endpoint is required when OutboxPublisher:Mode is Http.");
        }

        if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("OutboxPublisher:Endpoint must be an absolute URI.");
        }

        var payloadBytes = Encoding.UTF8.GetByteCount(message.Payload);
        if (payloadBytes > options.MaxPayloadBytes)
        {
            throw new InvalidOperationException(
                $"Outbox payload is {payloadBytes} bytes, above configured limit {options.MaxPayloadBytes} bytes.");
        }
    }

    private static string TrimBody(string body)
    {
        const int maxLength = 512;
        return body.Length <= maxLength ? body : body[..maxLength];
    }

    private sealed record OutboxPublishEnvelope(
        Guid OutboxMessageId,
        Guid WorkflowId,
        string MessageType,
        string PayloadHash,
        string Payload,
        DateTimeOffset CreatedAt);
}
