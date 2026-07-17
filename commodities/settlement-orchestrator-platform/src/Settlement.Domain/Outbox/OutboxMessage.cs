namespace Settlement.Domain.Outbox;

public sealed record OutboxMessage(
    Guid OutboxMessageId,
    Guid WorkflowId,
    string MessageType,
    string Payload,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt = null);

