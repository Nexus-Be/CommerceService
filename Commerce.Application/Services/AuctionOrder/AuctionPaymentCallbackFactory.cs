namespace Commerce.Application.Services.AuctionOrder;

using System;
using Commerce.Application.Interfaces;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;

/// <summary>
/// Builds the callback that tells Auction about a payment outcome of an auction order. The first send and every retry build it here, from
/// values stored on the order and on the history row, so the same event id always carries the same content (Auction refuses a repeated event
/// id whose content differs).
/// </summary>
public static class AuctionPaymentCallbackFactory
{
    public static AuctionPaymentStatusCallbackRequest Build(AuctionOrder order, OrderStatusHistory history)
    {
        var status = history.AuctionCallbackStatus ?? string.Empty;
        var paid = string.Equals(status, "Paid", StringComparison.Ordinal);
        return new AuctionPaymentStatusCallbackRequest(
            OrderId: order.Id,
            WinnerId: order.BuyerId,
            Status: status,
            Amount: paid ? order.WinningAmount : null,
            PaidAt: paid ? order.PaidAt : null,
            EventId: history.Id,
            OrderSource: order.OrderSource.ToWireValue(),
            OccurredAt: paid ? order.PaidAt : history.CreatedAt,
            OrderNo: paid ? order.OrderNo : null,
            TotalAmount: paid ? order.TotalAmount : null,
            SellerRespondDeadlineAt: paid ? order.SellerRespondDeadlineAt : null);
    }
}
