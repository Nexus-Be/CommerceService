namespace Commerce.Domain.Entities;

using System;
using System.Collections.Generic;
using System.Linq;
using Commerce.Domain.Base;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;

public class SubOrder : BaseEntity<Guid>
{
    public required string SubOrderNo { get; set; }
    public required Guid OrderId { get; set; }
    public required Guid BuyerId { get; set; }
    public required Guid SellerId { get; set; }
    public required SubOrderStatus Status { get; set; } = SubOrderStatus.PendingPayment;
    public required PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Wallet;
    public Guid? WalletTransactionId { get; set; }
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
        decimal platformMarkupPercent = 0m)
    {
        ItemsAmount = Items.Sum(i => i.Quantity * i.UnitPrice);
        ProductDiscountAmount = Items.Sum(i => i.Quantity * (i.UnitPrice - i.UnitPriceAfterDiscount));
        ShopDiscountAmount = shopDiscount;

        SellerReceiveAmount = ItemsAmount - ProductDiscountAmount - ShopDiscountAmount;
        PlatformMarkupAmount = Math.Round(SellerReceiveAmount * (platformMarkupPercent / 100m), 2, MidpointRounding.AwayFromZero);
        SystemDiscountAmount = systemDiscount;
        PlatformFeeAmount = PlatformMarkupAmount - SystemDiscountAmount;

        ShippingFeeAmount = shippingFee;
        TotalAmount = SellerReceiveAmount + PlatformFeeAmount + ShippingFeeAmount;

        if (TotalAmount != (SellerReceiveAmount + PlatformFeeAmount + ShippingFeeAmount))
        {
            throw new DomainException("TotalAmount invariant violated: total must equal seller_receive + platform_fee + shipping_fee");
        }
    }
}
