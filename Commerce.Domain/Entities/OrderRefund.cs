using Commerce.Domain.Base;
using Commerce.Domain.Enums;

namespace Commerce.Domain.Entities;

public class OrderRefund : BaseEntity<Guid>
{
    public Guid? SubOrderId { get; set; }
    public Guid? AuctionOrderId { get; set; }
    public Guid? OrderId { get; set; }
    public Guid? CancellationId { get; set; }
    public required RefundType RefundType { get; set; } = RefundType.Full;
    public required decimal Amount { get; set; }
    public required RefundStatus Status { get; set; } = RefundStatus.Requested;
    public required string ReasonCode { get; set; }
    public string? ReasonNote { get; set; }
    public required OrderActorRole ActorRole { get; set; } = OrderActorRole.System;
    public Guid? RequestedBy { get; set; }
    public Guid? WalletTransactionId { get; set; }
    public Guid? RefundTransactionId { get; set; }
    public Guid? RequestEventId { get; set; }
    public string? FailureCode { get; set; }
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? FailedAt { get; set; }
    public long RowVersion { get; set; }

    // Navigation
    public SubOrder? SubOrder { get; set; }
    public AuctionOrder? AuctionOrder { get; set; }
    public Order? Order { get; set; }
    public OrderCancellation? Cancellation { get; set; }
}
