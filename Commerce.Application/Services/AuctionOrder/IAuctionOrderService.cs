namespace Commerce.Application.Services.AuctionOrder;

using System;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Application.Common.Models;
using Commerce.Domain.Enums;

using Commerce.Application.Services.Order;

public sealed record AuctionSettlementResult(
    AuctionSettlementResponse Response,
    bool IsNewlyCreated
);

public interface IAuctionOrderService
{
    Task<ChangeShippingAddressResponse> ChangeAuctionShippingAddressAsync(
        Guid currentUserId,
        Guid auctionOrderId,
        ChangeShippingAddressRequest request,
        bool isAdminOrStaff = false,
        CancellationToken cancellationToken = default);

    Task<ShipmentTimelineResponse> GetAuctionShipmentTimelineAsync(
        Guid currentUserId,
        Guid auctionOrderId,
        bool isAdminOrStaff = false,
        CancellationToken cancellationToken = default);

    Task<CancelAuctionOrderResponse> CancelAuctionOrderAsync(
        Guid currentUserId,
        Guid auctionOrderId,
        CancelAuctionOrderRequest request,
        bool isAdminOrStaff = false,
        CancellationToken cancellationToken = default);

    Task<RetryAuctionShipmentResponse> RetryAuctionShipmentAsync(
        Guid currentUserId,
        Guid auctionOrderId,
        bool isAdminOrStaff = false,
        CancellationToken cancellationToken = default);

    Task<AuctionSettlementResult> ReceiveSettlementAsync(
        AuctionSettlementRequest request,
        CancellationToken cancellationToken = default);

    Task<AuctionOrderDetailResponse> SubmitShippingAsync(
        Guid auctionOrderId,
        Guid currentUserId,
        SubmitAuctionShippingRequest request,
        CancellationToken cancellationToken = default);

    Task<PayAuctionOrderResponse> PayAuctionOrderAsync(
        Guid auctionOrderId,
        Guid currentUserId,
        string? bearerToken = null,
        CancellationToken cancellationToken = default);

    Task<SellerRespondResponse> SellerAcceptAsync(
        Guid auctionOrderId,
        Guid currentUserId,
        SellerAcceptAuctionOrderRequest request,
        CancellationToken cancellationToken = default);

    Task<SellerRespondResponse> SellerRejectAsync(
        Guid auctionOrderId,
        Guid currentUserId,
        SellerRejectAuctionOrderRequest request,
        CancellationToken cancellationToken = default);

    Task<ConfirmReceiptResponse> ConfirmReceiptAsync(
        Guid auctionOrderId,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    /// <param name="isAdminOrStaff">An admin or a staff member may read any order; anyone else only their own as buyer or seller.</param>
    /// <summary>
    /// What the notification of a paid auction order needs. Null when the order does not exist or was never paid.
    /// For the internal service call from Auction only: it carries no per-user authorization.
    /// </summary>
    Task<AuctionOrderNotificationSnapshot?> GetNotificationSnapshotAsync(Guid auctionOrderId, CancellationToken ct = default);

    Task<AuctionOrderDetailResponse> GetOrderDetailAsync(
        Guid auctionOrderId,
        Guid currentUserId,
        bool isAdminOrStaff = false,
        CancellationToken cancellationToken = default);

    Task<PagedAuctionOrderSummary> ListSellerOrdersAsync(
        Guid sellerId,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PagedData<AuctionOrderSummaryResponse>> ListBuyerOrdersAsync(
        Guid buyerId,
        int pageIndex,
        int pageSize,
        SubOrderStatus? status = null,
        CancellationToken cancellationToken = default);
}
