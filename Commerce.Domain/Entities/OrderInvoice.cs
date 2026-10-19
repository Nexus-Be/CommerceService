using Commerce.Domain.Base;
using Commerce.Domain.Enums;

namespace Commerce.Domain.Entities;

public class OrderInvoice : BaseEntity<Guid>
{
    public required string InvoiceNo { get; set; }
    public Guid? OrderId { get; set; }
    public Guid? AuctionOrderId { get; set; }
    public required decimal TotalAmount { get; set; }
    public string? FileUrl { get; set; }
    public required InvoiceStatus Status { get; set; } = InvoiceStatus.Generated;
    public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation
    public Order? Order { get; set; }
    public AuctionOrder? AuctionOrder { get; set; }
}
