namespace Commerce.Application.Services.AuctionOrder;

using System;
using System.Threading;
using System.Threading.Tasks;

public sealed record AuctionSettlementResult(
    AuctionSettlementResponse Response,
    bool IsNewlyCreated
);

public interface IAuctionOrderService
{
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

    Task<AuctionOrderDetailResponse> GetOrderDetailAsync(
        Guid auctionOrderId,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<PagedAuctionOrderSummary> ListSellerOrdersAsync(
        Guid sellerId,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default);
}
