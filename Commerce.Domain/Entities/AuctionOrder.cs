using Commerce.Domain.Base;
using Commerce.Domain.Enums;

namespace Commerce.Domain.Entities;

public class AuctionOrder : BaseEntity<Guid>
{
    public required string OrderNo { get; set; }
    public required Guid AuctionId { get; set; }
    public required Guid BuyerId { get; set; }
    public required Guid SellerId { get; set; }
    public required Guid WinningBidId { get; set; }
    public required Guid ProductId { get; set; }
    public Guid? SkuId { get; set; }
    public required string ProductNameSnapshot { get; set; }
    public required decimal WinningAmount { get; set; }
    public string? ShippingAddress { get; set; }
    public string? ContactPhone { get; set; }
    public decimal? ShippingFeeAmount { get; set; }
    public decimal? TotalAmount { get; set; }
    public required decimal SellerReceiveAmount { get; set; }
    public required decimal PlatformFeeAmount { get; set; }
    public required OrderStatus Status { get; set; } = OrderStatus.PendingPayment;
    public required DateTimeOffset PaymentDeadlineAt { get; set; }
    public DateTimeOffset? SellerRespondDeadlineAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? SellerAcceptedAt { get; set; }
    public DateTimeOffset? ShippedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public long RowVersion { get; set; }

    // Navigation
    public ICollection<OrderStatusHistory> StatusHistories { get; set; } = new List<OrderStatusHistory>();
    public OrderInvoice? Invoice { get; set; }
    public OrderCancellation? Cancellation { get; set; }
}
