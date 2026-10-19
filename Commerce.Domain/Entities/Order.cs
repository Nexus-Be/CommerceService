using Commerce.Domain.Base;
using Commerce.Domain.Enums;

namespace Commerce.Domain.Entities;

public class Order : BaseEntity<Guid>
{
    public required string OrderNo { get; set; }
    public required Guid BuyerId { get; set; }
    public required Guid SellerId { get; set; }
    public required Guid CheckoutGroupId { get; set; }
    public required OrderStatus Status { get; set; } = OrderStatus.PendingPayment;
    public required string ShippingAddress { get; set; }
    public required string ContactPhone { get; set; }
    public decimal ItemsAmount { get; set; }
    public decimal ProductDiscountAmount { get; set; }
    public decimal ShopDiscountAmount { get; set; }
    public decimal PlatformMarkupAmount { get; set; }
    public decimal SystemDiscountAmount { get; set; }
    public decimal ShippingFeeAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal SellerReceiveAmount { get; set; }
    public decimal PlatformFeeAmount { get; set; }
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
    public CheckoutGroup? CheckoutGroup { get; set; }
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public ICollection<OrderStatusHistory> StatusHistories { get; set; } = new List<OrderStatusHistory>();
    public OrderInvoice? Invoice { get; set; }
    public OrderCancellation? Cancellation { get; set; }
    public ICollection<DiscountCodeUsage> DiscountCodeUsages { get; set; } = new List<DiscountCodeUsage>();
}
