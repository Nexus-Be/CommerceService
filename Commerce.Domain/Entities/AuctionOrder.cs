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
    public string? ImageUrlSnapshot { get; set; }
    public string SkuAttributesSnapshot { get; set; } = "{}";
    public required decimal WinningAmount { get; set; }
    public decimal DepositAmount { get; set; } = 0m;
    public decimal RemainingAmount { get; set; }
    public AuctionOrderSource OrderSource { get; set; } = AuctionOrderSource.Winner;
    public Guid? DepositId { get; set; }
    public int WeightGramsSnapshot { get; set; }
    public string? ShippingAddress { get; set; }
    public string? ContactPhone { get; set; }
    public decimal? ShippingFeeAmount { get; set; }
    public decimal? TotalAmount { get; set; }
    public required decimal SellerReceiveAmount { get; set; }
    public required decimal PlatformFeeAmount { get; set; }
    /// <summary>Platform fee rate (percent) applied to this order; null for orders created before the snapshot existed.</summary>
    public decimal? PlatformFeePercent { get; set; }
    /// <summary>Seller payout protocol of this order: 1 = legacy (V1) route, 2 = atomic allocation command (V2). Set per order, never inferred from dates.</summary>
    public int SettlementProtocolVersion { get; set; } = 1;
    public required SubOrderStatus Status { get; set; } = SubOrderStatus.PendingPayment;
    public required PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Wallet;
    public Guid? WalletTransactionId { get; set; }
    public required DateTimeOffset PaymentDeadlineAt { get; set; }
    public DateTimeOffset? SellerRespondDeadlineAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? SellerAcceptedAt { get; set; }
    public DateTimeOffset? ShippedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? PayoutCompletedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CarrierCode { get; set; }
    public string? TrackingCode { get; set; }
    public string? LabelPrintUrl { get; set; }
    public DateTimeOffset? EstimatedDeliveryAt { get; set; }
    public long RowVersion { get; set; }

    // Navigation
    public ICollection<OrderStatusHistory> StatusHistories { get; set; } = new List<OrderStatusHistory>();
    public OrderInvoice? Invoice { get; set; }
    public OrderCancellation? Cancellation { get; set; }
    public ICollection<OrderRefund> Refunds { get; set; } = new List<OrderRefund>();
}
