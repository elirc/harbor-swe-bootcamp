namespace Harbor.Api.Contracts;

public record WebhookHealthResponse(DateTimeOffset EvaluatedAt, int Total, int Pending, int Succeeded, int Failed,
    int ReadyPending, int DeferredPending, int PausedPending, DateTimeOffset? OldestReadyAt,
    DateTimeOffset? LatestDeliveredAt, double? TerminalFailureRate);
