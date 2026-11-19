namespace Commerce.Application.Services.AuctionOrder;

using System;
using System.Collections.Generic;

public sealed record AuctionSettlementResponse(
    Guid OrderId,
    string OrderNo,
    Guid AuctionId,
    Guid WinnerId,
    string Status,
    DateTimeOffset PaymentDeadlineAt
);

public sealed record AuctionOrderDetailResponse(
    Guid Id,
    string OrderNo,
    Guid AuctionId,
    Guid BuyerId,
    Guid SellerId,
    Guid WinningBidId,
    Guid ProductId,
    Guid? SkuId,
    string ProductName,
    string? ImageUrl,
    string? SkuAttributes,
    decimal WinningAmount,
    decimal DepositAmount,
    decimal RemainingAmount,
    string OrderSource,
    string? ShippingAddress,
    string? ContactPhone,
    decimal? ShippingFeeAmount,
    decimal? TotalAmount,
    decimal SellerReceiveAmount,
    decimal PlatformFeeAmount,
    string Status,
    string PaymentMethod,
    Guid? WalletTransactionId,
    DateTimeOffset PaymentDeadlineAt,
    DateTimeOffset? SellerRespondDeadlineAt,
    DateTimeOffset? PaidAt,
    DateTimeOffset? SellerAcceptedAt,
    DateTimeOffset? ShippedAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancelledAt,
    DateTimeOffset CreatedAt
);

public sealed record AuctionOrderSummaryResponse(
    Guid Id,
    string OrderNo,
    Guid AuctionId,
    Guid BuyerId,
    Guid SellerId,
    string ProductName,
    string? ImageUrl,
    decimal WinningAmount,
    decimal? TotalAmount,
    string Status,
    DateTimeOffset PaymentDeadlineAt,
    DateTimeOffset CreatedAt,
    Guid? WinningBidId,
    string? OrderSource,
    decimal DepositAmount,
    decimal? ShippingFeeAmount
)
{
    public AuctionOrderSummaryResponse(
        Guid id,
        string orderNo,
        Guid auctionId,
        Guid buyerId,
        Guid sellerId,
        string productName,
        string? imageUrl,
        decimal winningAmount,
        decimal? totalAmount,
        string status,
        DateTimeOffset paymentDeadlineAt,
        DateTimeOffset createdAt)
        : this(id, orderNo, auctionId, buyerId, sellerId, productName, imageUrl, winningAmount, totalAmount, status, paymentDeadlineAt, createdAt, null, null, 0m, null)
    {
    }
}

public sealed record PayAuctionOrderResponse(
    Guid OrderId,
    string OrderNo,
    string Status,
    DateTimeOffset PaidAt,
    DateTimeOffset SellerRespondDeadlineAt,
    Guid? WalletTransactionId
);

public sealed record SellerRespondResponse(
    Guid OrderId,
    string OrderNo,
    string Status,
    string Action,
    DateTimeOffset RespondedAt,
    string? TrackingCode = null,
    decimal? RefundAmount = null
);

public sealed record ConfirmReceiptResponse(
    Guid OrderId,
    string OrderNo,
    string Status,
    DateTimeOffset CompletedAt
);

public sealed record PagedAuctionOrderSummary(
    IReadOnlyList<AuctionOrderSummaryResponse> Items,
    int PageIndex,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage
);

/// <summary>The order data the "order paid" notification needs. Total includes shipping; it is not the winning amount.</summary>
public sealed record AuctionOrderNotificationSnapshot(
    Guid OrderId,
    string OrderNo,
    decimal TotalAmount,
    DateTimeOffset PaidAt,
    DateTimeOffset SellerRespondDeadlineAt,
    Guid BuyerId,
    Guid SellerId,
    string Status);
