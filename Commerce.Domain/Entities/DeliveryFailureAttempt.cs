using Commerce.Domain.Base;

namespace Commerce.Domain.Entities;

/// <summary>
/// One failed delivery attempt of one parcel, as the carrier reported it. The attempt belongs to the parcel (the tracking code) and carries the
/// carrier's own time, so a return can be judged only against the attempts of the same parcel that happened before it. A repeated callback
/// (same tracking code, event type and carrier time) is stored once.
/// </summary>
public class DeliveryFailureAttempt : BaseEntity<Guid>
{
    public Guid SubOrderId { get; set; }
    public string? TrackingCode { get; set; }
    public string? ReasonCode { get; set; }
    public string? Reason { get; set; }
    public string? EventType { get; set; }
    /// <summary>When the carrier says the attempt failed. Null when the carrier sent no readable time.</summary>
    public DateTimeOffset? EventTime { get; set; }
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>"tracking|event type|carrier time", or null when the tracking code or the time is missing (such an attempt cannot be de-duplicated).</summary>
    public string? DedupeKey { get; set; }

    public static string? KeyOf(string? trackingCode, string? eventType, DateTimeOffset? eventTime) =>
        string.IsNullOrWhiteSpace(trackingCode) || eventTime is null
            ? null
            : $"{trackingCode.Trim().ToUpperInvariant()}|{(string.IsNullOrWhiteSpace(eventType) ? "-" : eventType.Trim().ToLowerInvariant())}|{eventTime.Value.UtcDateTime:O}";
}
