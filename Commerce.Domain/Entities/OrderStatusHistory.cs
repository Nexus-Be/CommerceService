using Commerce.Domain.Base;
using Commerce.Domain.Enums;

namespace Commerce.Domain.Entities;

public class OrderStatusHistory : BaseEntity<Guid>
{
    public Guid? SubOrderId { get; set; }
    public Guid? AuctionOrderId { get; set; }
    public SubOrderStatus? FromStatus { get; set; }
    public required SubOrderStatus ToStatus { get; set; }
    public Guid? ChangedBy { get; set; }
    public required OrderActorRole ActorRole { get; set; } = OrderActorRole.System;
    public string? Reason { get; set; }

    // Navigation
    public SubOrder? SubOrder { get; set; }
    public AuctionOrder? AuctionOrder { get; set; }
}
