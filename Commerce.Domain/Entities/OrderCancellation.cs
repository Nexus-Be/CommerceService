using Commerce.Domain.Base;
using Commerce.Domain.Enums;

namespace Commerce.Domain.Entities;

public class OrderCancellation : BaseEntity<Guid>
{
    public Guid? OrderId { get; set; }
    public Guid? AuctionOrderId { get; set; }
    public Guid? CancelledBy { get; set; }
    public required OrderActorRole ActorRole { get; set; }
    public required CancellationReasonCode ReasonCode { get; set; }
    public string? ReasonNote { get; set; }
    public bool WasPaid { get; set; }
    public bool InventoryReleased { get; set; }

    // Navigation
    public Order? Order { get; set; }
    public AuctionOrder? AuctionOrder { get; set; }
}
