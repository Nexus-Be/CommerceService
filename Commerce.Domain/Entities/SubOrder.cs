using System;
using System.Collections.Generic;
using System.Linq;
using Commerce.Domain.Base;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;
using Commerce.Domain.ValueObjects;

namespace Commerce.Domain.Entities;

public class SubOrder : BaseEntity<Guid>
{
    public required string SubOrderNo { get; set; }
    public required Guid OrderId { get; set; }
    public required Guid BuyerId { get; set; }
    public required Guid SellerId { get; set; }
    public required SubOrderStatus Status { get; set; } = SubOrderStatus.PendingPayment;
    public required PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Wallet;
    public Guid? WalletTransactionId { get; set; }

    /// <summary>The seller profile id of the shop (not the seller's user id), saved when it is first looked up for a notification.</summary>
    public Guid? ShopId { get; set; }

    public ShippingAddressSnapshot ShippingAddress { get; set; } = new();
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
    /// <summary>Platform fee rate (percent) captured when the order was created; null for orders created before the snapshot existed.</summary>
    public decimal? PlatformFeePercent { get; set; }
    /// <summary>Seller payout protocol of this order: 1 = legacy (V1) route, 2 = atomic allocation command (V2). Set per order, never inferred from dates.</summary>
    public int SettlementProtocolVersion { get; set; } = 1;
    /// <summary>Lifecycle of the platform-funded voucher subsidy reserved for this sub-order (None for orders without one).</summary>
    public SubsidyState SubsidyState { get; set; } = SubsidyState.None;
    public required DateTimeOffset PaymentDeadlineAt { get; set; }
    public DateTimeOffset? SellerRespondDeadlineAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? SellerAcceptedAt { get; set; }
    public DateTimeOffset? ShippedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CarrierCode { get; set; }
    public string? TrackingCode { get; set; }
    public string? LabelPrintUrl { get; set; }
    public DateTimeOffset? EstimatedDeliveryAt { get; set; }
    public bool ShipmentPendingCreation { get; set; } = false;
    public bool IsEscrowSettled { get; set; } = false;
    public long RowVersion { get; set; }

    // Navigation
    public Order? Order { get; set; }
    public ICollection<SubOrderItem> Items { get; set; } = new List<SubOrderItem>();
    public ICollection<OrderStatusHistory> StatusHistories { get; set; } = new List<OrderStatusHistory>();
    public OrderInvoice? Invoice { get; set; }
    public OrderCancellation? Cancellation { get; set; }
    public ICollection<OrderRefund> Refunds { get; set; } = new List<OrderRefund>();
    public ICollection<DiscountCodeUsage> DiscountCodeUsages { get; set; } = new List<DiscountCodeUsage>();

    public void CalculateFinancials(
        decimal shippingFee,
        decimal shopDiscount,
        decimal systemDiscount,
        decimal platformMarkupPercent = 0m,
        decimal? productDiscount = null)
    {
        ItemsAmount = Items.Sum(i => i.Quantity * i.UnitPrice);
        ProductDiscountAmount = productDiscount ?? Items.Sum(i => i.Quantity * (i.UnitPrice - i.UnitPriceAfterDiscount));
        ShopDiscountAmount = shopDiscount;
        SystemDiscountAmount = systemDiscount;

        // Shopee / TikTok Shop Model:
        // NetSeller = ItemsAmount - ProductDiscount - ShopDiscount
        var netSeller = Math.Max(0m, ItemsAmount - ProductDiscountAmount - ShopDiscountAmount);

        // PlatformFee is deducted from Seller sales (5% platform commission)
        PlatformFeeAmount = Math.Round(netSeller * (platformMarkupPercent / 100m), 0, MidpointRounding.AwayFromZero);
        PlatformMarkupAmount = 0m;

        // Seller payout after commission
        SellerReceiveAmount = Math.Max(0m, netSeller - PlatformFeeAmount);

        // Buyer pays catalog prices minus discounts + shipping (no extra buyer markup)
        ShippingFeeAmount = shippingFee;
        TotalAmount = Math.Max(0m, netSeller - SystemDiscountAmount) + ShippingFeeAmount;

        // Accounting invariant check
        var accountingSum = SellerReceiveAmount + (PlatformFeeAmount - SystemDiscountAmount) + ShippingFeeAmount;
        if (TotalAmount != accountingSum)
        {
            throw new DomainException($"TotalAmount invariant violated: total ({TotalAmount}) must equal seller_receive + (platform_fee - system_discount) + shipping_fee ({accountingSum})");
        }
    }
}
