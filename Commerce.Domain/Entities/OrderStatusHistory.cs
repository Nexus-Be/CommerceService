using Commerce.Domain.Base;
using Commerce.Domain.Enums;

namespace Commerce.Domain.Entities;

public class OrderStatusHistory : BaseEntity<Guid>
{
    public Guid? OrderId { get; set; }
    public Guid? AuctionOrderId { get; set; }
    public OrderStatus? FromStatus { get; set; }
    public required OrderStatus ToStatus { get; set; }
    public Guid? ChangedBy { get; set; }
    public required OrderActorRole ActorRole { get; set; } = OrderActorRole.System;
    public string? Reason { get; set; }

    // Navigation
    public Order? Order { get; set; }
    public AuctionOrder? AuctionOrder { get; set; }
}
