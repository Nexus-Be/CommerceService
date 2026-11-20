using Commerce.Domain.Base;
using Commerce.Domain.Enums;

namespace Commerce.Domain.Entities;

/// <summary>
/// A notification for the Notification service. The row is added in the same SaveChanges that stores the payment, so a process that stops right
/// after the commit cannot lose the event; a worker sends it afterwards and retries with the same event id and the same frozen payload.
/// </summary>
public class NotificationOutboxItem : BaseEntity<Guid>
{
    /// <summary>The sourceEventId sent to Notification. Unique: the same event cannot be queued twice.</summary>
    public required Guid EventId { get; set; }

    /// <summary>order-payments.</summary>
    public required string Kind { get; set; }

    public required Guid SubOrderId { get; set; }
    public required Guid SellerUserId { get; set; }

    /// <summary>The request body, frozen once complete. Null while the row is AwaitingData.</summary>
    public string? PayloadJson { get; set; }

    public required NotificationOutboxStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset NextRetryAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LockedUntil { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset GiveUpAt { get; set; } = DateTimeOffset.UtcNow.AddHours(24);
}
