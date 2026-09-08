namespace Harbor.Domain.Entities;

public enum WebhookAttemptOutcome { Started = 0, Succeeded = 1, Rejected = 2, TransportError = 3, Unknown = 4 }

public class WebhookAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid DeliveryId { get; set; }
    public long ClaimGeneration { get; set; }
    public int AttemptNumber { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public WebhookAttemptOutcome Outcome { get; set; }
    public int? ResponseStatusCode { get; set; }
    public string? FailureCode { get; set; }
}
