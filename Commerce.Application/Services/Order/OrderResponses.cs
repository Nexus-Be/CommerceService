namespace Commerce.Application.Services.Order;

using System;
using System.Collections.Generic;
using Commerce.Domain.ValueObjects;

public sealed record ShopQuoteItem(
    Guid CartItemId,
    Guid ProductId,
    Guid SkuId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);

public sealed record ShopOrderQuote(
    Guid SellerId,
    string ShopName,
    List<ShopQuoteItem> Items,
    decimal ItemsAmount,
    decimal ProductDiscountAmount,
    decimal ShopDiscountAmount,
    decimal SystemDiscountAmount,
    decimal DiscountAmount,
    decimal ShippingFeeAmount,
    decimal TotalAmount);

public sealed record CheckoutQuoteResponse(
    Guid QuoteId,
    decimal GrandTotal,
    decimal WalletBalance,
    List<ShopOrderQuote> Shops,
    List<string> AppliedVouchers,
    List<string>? Warnings,
    DateTimeOffset ExpiredAt);

public sealed record PriceDropDto(
    Guid SkuId,
    string ProductName,
    decimal OldPrice,
    decimal NewPrice,
    decimal SavedAmount);

public sealed record CreateOrderResponse(
    Guid OrderId,
    string OrderNo,
    decimal TotalAmount,
    string Status,
    DateTimeOffset PaymentDeadlineAt,
    string? PaymentUrl,
    List<Guid> SubOrderIds,
    DateTimeOffset CreatedAt,
    List<PriceDropDto>? AppliedPriceDrops = null);

public sealed record SubOrderItemResponse(
    Guid OrderItemId,
    Guid ProductId,
    Guid SkuId,
    string ProductName,
    string? SkuCode,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);

public sealed record SubOrderDetailResponse(
    Guid SubOrderId,
    string SubOrderNo,
    Guid OrderId,
    Guid BuyerId,
    Guid SellerId,
    string ShopName,
    string Status,
    string PaymentMethod,
    ShippingAddressSnapshot ShippingAddress,
    string ContactPhone,
    decimal ItemsAmount,
    decimal ProductDiscountAmount,
    decimal ShopDiscountAmount,
    decimal SystemDiscountAmount,
    decimal DiscountAmount,
    decimal ShippingFeeAmount,
    decimal TotalAmount,
    DateTimeOffset CreatedAt,
    List<SubOrderItemResponse> Items);

public sealed record SubOrderShipmentInfo(
    Guid SubOrderId,
    string SubOrderNo,
    Guid SellerId,
    string ShopName,
    string Status,
    string? CarrierCode,
    string? TrackingCode,
    DateTimeOffset? ShippedAt,
    DateTimeOffset? DeliveredAt);

public sealed record ShipmentTrackingResponse(
    Guid OrderId,
    string OrderNo,
    List<SubOrderShipmentInfo> Shipments);

public sealed record OrderSummaryResponse(
    Guid OrderId,
    string OrderNo,
    decimal TotalAmount,
    string Status,
    DateTimeOffset PaymentDeadlineAt,
    int SubOrderCount,
    DateTimeOffset CreatedAt);

public sealed record OrderResponse(
    Guid Id,
    string OrderNo,
    Guid BuyerId,
    string Status,
    decimal TotalAmount,
    DateTimeOffset PaymentDeadlineAt,
    DateTimeOffset CreatedAt,
    List<SubOrderDetailResponse> SubOrders);

public sealed record SubOrderSummaryResponse(
    Guid SubOrderId,
    string SubOrderNo,
    Guid OrderId,
    Guid BuyerId,
    Guid SellerId,
    string Status,
    string PaymentMethod,
    decimal ItemsAmount,
    decimal ShippingFeeAmount,
    decimal TotalAmount,
    decimal SellerReceiveAmount,
    int ItemCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SellerAcceptedAt,
    DateTimeOffset? ShippedAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancelledAt
);

public sealed record ConfirmReceiptResponse(
    Guid SubOrderId,
    string Status,
    DateTimeOffset CompletedAt,
    bool EscrowSettled,
    decimal AmountSettled
);
