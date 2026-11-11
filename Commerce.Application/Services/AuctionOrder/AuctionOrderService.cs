namespace Commerce.Application.Services.AuctionOrder;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Commerce.Application.Interfaces;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;

public class AuctionOrderService : IAuctionOrderService
{
    private readonly ICommerceDbContext _dbContext;
    private readonly IWalletClient _walletClient;
    private readonly IFulfillmentClient _fulfillmentClient;
    private readonly IAuctionClient _auctionClient;
    private readonly ILogger<AuctionOrderService> _logger;
    private const decimal DefaultPlatformFeeRate = 0.05m; // 5%

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AuctionOrderService(
        ICommerceDbContext dbContext,
        IWalletClient walletClient,
        IFulfillmentClient fulfillmentClient,
        IAuctionClient auctionClient,
        ILogger<AuctionOrderService> logger)
    {
        _dbContext = dbContext;
        _walletClient = walletClient;
        _fulfillmentClient = fulfillmentClient;
        _auctionClient = auctionClient;
        _logger = logger;
    }

    public async Task<AuctionSettlementResult> ReceiveSettlementAsync(
        AuctionSettlementRequest request,
        CancellationToken cancellationToken = default)
    {
        var existingOrders = await _dbContext.AuctionOrders
            .Where(a => a.AuctionId == request.AuctionId)
            .ToListAsync(cancellationToken);

        // Idempotency: exact (AuctionId, WinnerId) match returns existing order
        var identicalOrder = existingOrders.FirstOrDefault(a => a.BuyerId == request.WinnerId);
        if (identicalOrder != null)
        {
            _logger.LogInformation("[AuctionOrder] Idempotent duplicate settlement for Auction '{AuctionId}', Winner '{WinnerId}'. Returning Order '{OrderId}'.",
                request.AuctionId, request.WinnerId, identicalOrder.Id);

            return new AuctionSettlementResult(
                new AuctionSettlementResponse(
                    identicalOrder.Id,
                    identicalOrder.OrderNo,
                    identicalOrder.AuctionId,
                    identicalOrder.BuyerId,
                    identicalOrder.Status.ToString(),
                    identicalOrder.PaymentDeadlineAt
                ),
                IsNewlyCreated: false
            );
        }

        // Check if another winner already has an active (non-cancelled) order
        var activeOtherOrder = existingOrders.FirstOrDefault(a => a.BuyerId != request.WinnerId && a.Status != SubOrderStatus.Cancelled);
        if (activeOtherOrder != null)
        {
            _logger.LogWarning("[AuctionOrder] Conflicting settlement for Auction '{AuctionId}' with Winner '{WinnerId}'. Order '{OrderId}' is currently '{Status}'.",
                request.AuctionId, request.WinnerId, activeOtherOrder.Id, activeOtherOrder.Status);

            throw new CommerceDomainException(
                "AUCTION_ALREADY_SETTLED",
                "An active auction order already exists for this auction with another winner.",
                409);
        }

        // Calculate financials
        var platformFeeAmount = Math.Round(request.WinningAmount * DefaultPlatformFeeRate, 2);
        var sellerReceiveAmount = request.WinningAmount - platformFeeAmount;
        var orderId = Guid.NewGuid();
        var orderNo = $"AO-{DateTime.UtcNow:yyyyMMdd}-{orderId.ToString()[..6].ToUpperInvariant()}";

        string? shippingAddressJson = null;
        if (request.ShippingAddress != null)
        {
            shippingAddressJson = JsonSerializer.Serialize(request.ShippingAddress, JsonOptions);
        }

        var auctionOrder = new AuctionOrder
        {
            Id = orderId,
            OrderNo = orderNo,
            AuctionId = request.AuctionId,
            BuyerId = request.WinnerId,
            SellerId = request.SellerId,
            WinningBidId = request.WinningBidId,
            ProductId = request.ProductId,
            SkuId = request.SkuId,
            ProductNameSnapshot = request.ProductName,
            ImageUrlSnapshot = request.ProductImageUrl,
            SkuAttributesSnapshot = request.SkuAttributes ?? "{}",
            WinningAmount = request.WinningAmount,
            ShippingAddress = shippingAddressJson,
            ContactPhone = request.ContactPhone ?? request.ShippingAddress?.PhoneNumber,
            ShippingFeeAmount = null,
            TotalAmount = null,
            SellerReceiveAmount = sellerReceiveAmount,
            PlatformFeeAmount = platformFeeAmount,
            Status = SubOrderStatus.PendingPayment,
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = request.PaymentDeadlineAt,
            RowVersion = 1
        };

        _dbContext.AuctionOrders.Add(auctionOrder);
        _dbContext.OrderStatusHistories.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            AuctionOrderId = orderId,
            FromStatus = null,
            ToStatus = SubOrderStatus.PendingPayment,
            ActorRole = OrderActorRole.System,
            Reason = "Auction won settlement received from Auction Service."
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("[AuctionOrder] Created AuctionOrder '{OrderId}' ({OrderNo}) for Auction '{AuctionId}', Winner '{WinnerId}'.",
            orderId, orderNo, request.AuctionId, request.WinnerId);

        return new AuctionSettlementResult(
            new AuctionSettlementResponse(
                auctionOrder.Id,
                auctionOrder.OrderNo,
                auctionOrder.AuctionId,
                auctionOrder.BuyerId,
                auctionOrder.Status.ToString(),
                auctionOrder.PaymentDeadlineAt
            ),
            IsNewlyCreated: true
        );
    }

    public async Task<AuctionOrderDetailResponse> SubmitShippingAsync(
        Guid auctionOrderId,
        Guid currentUserId,
        SubmitAuctionShippingRequest request,
        CancellationToken cancellationToken = default)
    {
        var auctionOrder = await _dbContext.AuctionOrders.FirstOrDefaultAsync(a => a.Id == auctionOrderId, cancellationToken);
        if (auctionOrder == null)
        {
            throw new CommerceDomainException("AUCTION_ORDER_NOT_FOUND", "Auction order not found.", 404);
        }

        if (auctionOrder.BuyerId != currentUserId)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to manage this auction order.", 403);
        }

        if (auctionOrder.Status != SubOrderStatus.PendingPayment)
        {
            throw new CommerceDomainException("INVALID_STATUS", "Auction order is not in pending payment status.", 400);
        }

        if (DateTimeOffset.UtcNow > auctionOrder.PaymentDeadlineAt)
        {
            throw new CommerceDomainException("PAYMENT_DEADLINE_EXPIRED", "Auction payment deadline has expired.", 400);
        }

        var quoteRequest = new ShopShippingQuoteRequest(
            SellerId: auctionOrder.SellerId,
            GhnDistrictId: request.ShippingAddress.GhnDistrictId,
            GhnWardCode: request.ShippingAddress.GhnWardCode,
            Items: new List<ShippingQuoteItem>
            {
                new(
                    SkuId: auctionOrder.SkuId ?? Guid.Empty,
                    Quantity: 1,
                    WeightGrams: 500
                )
            }
        );

        var shippingFee = await _fulfillmentClient.CalculateShopShippingFeeAsync(quoteRequest, cancellationToken);

        auctionOrder.ShippingAddress = JsonSerializer.Serialize(request.ShippingAddress, JsonOptions);
        auctionOrder.ContactPhone = request.ContactPhone;
        auctionOrder.ShippingFeeAmount = shippingFee;
        auctionOrder.TotalAmount = auctionOrder.WinningAmount + shippingFee;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("[AuctionOrder] Updated shipping for AuctionOrder '{OrderId}': ShippingFee={ShippingFee}, Total={TotalAmount}.",
            auctionOrder.Id, shippingFee, auctionOrder.TotalAmount);

        return MapToDetailResponse(auctionOrder);
    }

    private static AuctionOrderDetailResponse MapToDetailResponse(AuctionOrder order) =>
        new(
            Id: order.Id,
            OrderNo: order.OrderNo,
            AuctionId: order.AuctionId,
            BuyerId: order.BuyerId,
            SellerId: order.SellerId,
            WinningBidId: order.WinningBidId,
            ProductId: order.ProductId,
            SkuId: order.SkuId,
            ProductName: order.ProductNameSnapshot,
            ImageUrl: order.ImageUrlSnapshot,
            SkuAttributes: order.SkuAttributesSnapshot,
            WinningAmount: order.WinningAmount,
            ShippingAddress: order.ShippingAddress,
            ContactPhone: order.ContactPhone,
            ShippingFeeAmount: order.ShippingFeeAmount,
            TotalAmount: order.TotalAmount,
            SellerReceiveAmount: order.SellerReceiveAmount,
            PlatformFeeAmount: order.PlatformFeeAmount,
            Status: order.Status.ToString(),
            PaymentMethod: order.PaymentMethod.ToString(),
            WalletTransactionId: order.WalletTransactionId,
            PaymentDeadlineAt: order.PaymentDeadlineAt,
            SellerRespondDeadlineAt: order.SellerRespondDeadlineAt,
            PaidAt: order.PaidAt,
            SellerAcceptedAt: order.SellerAcceptedAt,
            ShippedAt: order.ShippedAt,
            DeliveredAt: order.DeliveredAt,
            CompletedAt: order.CompletedAt,
            CancelledAt: order.CancelledAt,
            CreatedAt: order.CreatedAt
        );

    public async Task<PayAuctionOrderResponse> PayAuctionOrderAsync(
        Guid auctionOrderId,
        Guid currentUserId,
        string? bearerToken = null,
        CancellationToken cancellationToken = default)
    {
        var auctionOrder = await _dbContext.AuctionOrders.FirstOrDefaultAsync(a => a.Id == auctionOrderId, cancellationToken);
        if (auctionOrder == null)
        {
            throw new CommerceDomainException("AUCTION_ORDER_NOT_FOUND", "Auction order not found.", 404);
        }

        if (auctionOrder.BuyerId != currentUserId)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to manage this auction order.", 403);
        }

        if (auctionOrder.Status != SubOrderStatus.PendingPayment)
        {
            throw new CommerceDomainException("INVALID_STATUS", "Auction order is not in pending payment status.", 400);
        }

        if (DateTimeOffset.UtcNow > auctionOrder.PaymentDeadlineAt)
        {
            throw new CommerceDomainException("PAYMENT_DEADLINE_EXPIRED", "Auction payment deadline has expired.", 400);
        }

        if (!auctionOrder.TotalAmount.HasValue || string.IsNullOrWhiteSpace(auctionOrder.ShippingAddress))
        {
            throw new CommerceDomainException("SHIPPING_ADDRESS_REQUIRED", "Shipping address must be submitted before paying for the auction order.", 400);
        }

        var paymentResult = await _walletClient.PayOrderAsync(
            auctionOrder.Id,
            auctionOrder.BuyerId,
            auctionOrder.TotalAmount.Value,
            bearerToken,
            cancellationToken);

        if (!paymentResult.IsSuccess)
        {
            _logger.LogWarning("[AuctionOrder] Wallet payment failed for Order '{OrderId}'. Code: {Code}, Error: {Error}",
                auctionOrder.Id, paymentResult.ErrorCode, paymentResult.ErrorMessage);

            throw new CommerceDomainException(
                paymentResult.ErrorCode ?? "PAYMENT_FAILED",
                paymentResult.ErrorMessage ?? "Wallet payment failed.",
                400);
        }

        var now = DateTimeOffset.UtcNow;
        var sellerRespondDeadline = now.AddHours(24);
        Guid parsedTxId;
        if (!Guid.TryParse(paymentResult.TransactionId, out parsedTxId))
        {
            parsedTxId = Guid.NewGuid();
        }

        auctionOrder.Status = SubOrderStatus.Paid;
        auctionOrder.PaidAt = now;
        auctionOrder.SellerRespondDeadlineAt = sellerRespondDeadline;
        auctionOrder.WalletTransactionId = parsedTxId;

        _dbContext.OrderStatusHistories.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            AuctionOrderId = auctionOrder.Id,
            FromStatus = SubOrderStatus.PendingPayment,
            ToStatus = SubOrderStatus.Paid,
            ActorRole = OrderActorRole.Buyer,
            Reason = "Buyer paid auction order via Nexus Wallet."
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("[AuctionOrder] Order '{OrderId}' paid successfully. Dispatched callback to Auction '{AuctionId}'.",
            auctionOrder.Id, auctionOrder.AuctionId);

        // Immediate callback to Auction Service
        var callbackReq = new AuctionPaymentStatusCallbackRequest(
            OrderId: auctionOrder.Id,
            WinnerId: auctionOrder.BuyerId,
            Status: "Paid",
            Amount: auctionOrder.WinningAmount,
            PaidAt: now,
            EventId: Guid.NewGuid()
        );

        await _auctionClient.NotifyPaymentStatusAsync(auctionOrder.AuctionId, callbackReq, cancellationToken);

        return new PayAuctionOrderResponse(
            OrderId: auctionOrder.Id,
            OrderNo: auctionOrder.OrderNo,
            Status: auctionOrder.Status.ToString(),
            PaidAt: now,
            SellerRespondDeadlineAt: sellerRespondDeadline,
            WalletTransactionId: parsedTxId
        );
    }

    public async Task<SellerRespondResponse> SellerAcceptAsync(
        Guid auctionOrderId,
        Guid currentUserId,
        SellerAcceptAuctionOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        var auctionOrder = await _dbContext.AuctionOrders.FirstOrDefaultAsync(a => a.Id == auctionOrderId, cancellationToken);
        if (auctionOrder == null)
        {
            throw new CommerceDomainException("AUCTION_ORDER_NOT_FOUND", "Auction order not found.", 404);
        }

        if (auctionOrder.SellerId != currentUserId)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to manage this auction order.", 403);
        }

        if (auctionOrder.Status != SubOrderStatus.Paid)
        {
            throw new CommerceDomainException("INVALID_STATUS", "Auction order must be in paid status to be accepted by seller.", 400);
        }

        if (auctionOrder.SellerRespondDeadlineAt.HasValue && DateTimeOffset.UtcNow > auctionOrder.SellerRespondDeadlineAt.Value)
        {
            throw new CommerceDomainException("SELLER_RESPOND_TIMEOUT", "Seller response deadline has expired. Order is awaiting cancellation.", 400);
        }

        var shipmentResult = await _fulfillmentClient.CreateShipmentAsync(auctionOrder.Id, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        auctionOrder.Status = SubOrderStatus.Confirmed;
        auctionOrder.SellerAcceptedAt = now;

        _dbContext.OrderStatusHistories.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            AuctionOrderId = auctionOrder.Id,
            FromStatus = SubOrderStatus.Paid,
            ToStatus = SubOrderStatus.Confirmed,
            ActorRole = OrderActorRole.Seller,
            Reason = string.IsNullOrWhiteSpace(request.Note) ? "Seller accepted auction order." : request.Note
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("[AuctionOrder] Seller accepted Order '{OrderId}'. Shipment created with TrackingCode '{TrackingCode}'.",
            auctionOrder.Id, shipmentResult?.TrackingCode);

        // Outbound callback to Auction Service
        var callbackReq = new AuctionPaymentStatusCallbackRequest(
            OrderId: auctionOrder.Id,
            WinnerId: auctionOrder.BuyerId,
            Status: "SellerAccepted",
            EventId: Guid.NewGuid()
        );

        await _auctionClient.NotifyPaymentStatusAsync(auctionOrder.AuctionId, callbackReq, cancellationToken);

        return new SellerRespondResponse(
            OrderId: auctionOrder.Id,
            OrderNo: auctionOrder.OrderNo,
            Status: auctionOrder.Status.ToString(),
            Action: "Accepted",
            RespondedAt: now,
            TrackingCode: shipmentResult?.TrackingCode
        );
    }

    public async Task<SellerRespondResponse> SellerRejectAsync(
        Guid auctionOrderId,
        Guid currentUserId,
        SellerRejectAuctionOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        var auctionOrder = await _dbContext.AuctionOrders.FirstOrDefaultAsync(a => a.Id == auctionOrderId, cancellationToken);
        if (auctionOrder == null)
        {
            throw new CommerceDomainException("AUCTION_ORDER_NOT_FOUND", "Auction order not found.", 404);
        }

        if (auctionOrder.SellerId != currentUserId)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to manage this auction order.", 403);
        }

        if (auctionOrder.Status == SubOrderStatus.Shipped ||
            auctionOrder.Status == SubOrderStatus.Delivered ||
            auctionOrder.Status == SubOrderStatus.Completed ||
            auctionOrder.Status == SubOrderStatus.Cancelled)
        {
            throw new CommerceDomainException("INVALID_STATUS", "Auction order cannot be rejected in its current status.", 400);
        }

        var now = DateTimeOffset.UtcNow;
        var previousStatus = auctionOrder.Status;
        auctionOrder.Status = SubOrderStatus.Cancelled;
        auctionOrder.CancelledAt = now;

        var cancellation = new OrderCancellation
        {
            Id = Guid.NewGuid(),
            AuctionOrderId = auctionOrder.Id,
            SubOrderId = null,
            CancelledBy = currentUserId,
            ActorRole = OrderActorRole.Seller,
            ReasonCode = CancellationReasonCode.SellerRejected,
            ReasonNote = request.ReasonNote,
            WasPaid = previousStatus == SubOrderStatus.Paid,
            InventoryReleased = true
        };
        _dbContext.OrderCancellations.Add(cancellation);

        decimal? refundAmount = null;
        if (previousStatus == SubOrderStatus.Paid)
        {
            refundAmount = auctionOrder.TotalAmount ?? auctionOrder.WinningAmount;

            var refundSuccess = await _walletClient.RefundOrderPaymentAsync(
                auctionOrder.Id,
                auctionOrder.BuyerId.ToString(),
                refundAmount.Value,
                request.ReasonNote,
                null,
                cancellationToken);

            _dbContext.OrderRefunds.Add(new OrderRefund
            {
                Id = Guid.NewGuid(),
                AuctionOrderId = auctionOrder.Id,
                CancellationId = cancellation.Id,
                RefundType = RefundType.Full,
                Amount = refundAmount.Value,
                Status = refundSuccess ? RefundStatus.Completed : RefundStatus.Requested,
                ReasonCode = "SellerRejected",
                ReasonNote = request.ReasonNote,
                ActorRole = OrderActorRole.Seller,
                RequestedBy = currentUserId,
                CompletedAt = refundSuccess ? now : null
            });
        }

        _dbContext.OrderStatusHistories.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            AuctionOrderId = auctionOrder.Id,
            FromStatus = previousStatus,
            ToStatus = SubOrderStatus.Cancelled,
            ActorRole = OrderActorRole.Seller,
            Reason = request.ReasonNote
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("[AuctionOrder] Seller rejected Order '{OrderId}'. RefundAmount: {RefundAmount}.",
            auctionOrder.Id, refundAmount);

        // Fail-safe outbound callback: does not affect wallet refund
        try
        {
            var callbackReq = new AuctionPaymentStatusCallbackRequest(
                OrderId: auctionOrder.Id,
                WinnerId: auctionOrder.BuyerId,
                Status: "SellerRejected",
                EventId: Guid.NewGuid()
            );

            await _auctionClient.NotifyPaymentStatusAsync(auctionOrder.AuctionId, callbackReq, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AuctionOrder] Fail-safe: Outbound callback to Auction Service failed for rejected Order '{OrderId}'.", auctionOrder.Id);
        }

        return new SellerRespondResponse(
            OrderId: auctionOrder.Id,
            OrderNo: auctionOrder.OrderNo,
            Status: auctionOrder.Status.ToString(),
            Action: "Rejected",
            RespondedAt: now,
            RefundAmount: refundAmount
        );
    }

    public async Task<ConfirmReceiptResponse> ConfirmReceiptAsync(
        Guid auctionOrderId,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var auctionOrder = await _dbContext.AuctionOrders.FirstOrDefaultAsync(a => a.Id == auctionOrderId, cancellationToken);
        if (auctionOrder == null)
        {
            throw new CommerceDomainException("AUCTION_ORDER_NOT_FOUND", "Auction order not found.", 404);
        }

        if (auctionOrder.BuyerId != currentUserId)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to manage this auction order.", 403);
        }

        if (auctionOrder.Status != SubOrderStatus.Delivered && auctionOrder.Status != SubOrderStatus.Shipped)
        {
            throw new CommerceDomainException("INVALID_STATUS", "Auction order cannot be confirmed in its current status.", 400);
        }

        var now = DateTimeOffset.UtcNow;
        var previousStatus = auctionOrder.Status;
        auctionOrder.Status = SubOrderStatus.Completed;
        auctionOrder.CompletedAt = now;

        _dbContext.OrderStatusHistories.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            AuctionOrderId = auctionOrder.Id,
            FromStatus = previousStatus,
            ToStatus = SubOrderStatus.Completed,
            ActorRole = OrderActorRole.Buyer,
            Reason = "Buyer confirmed package delivery receipt."
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Escrow release payout to seller
        try
        {
            await _walletClient.SettleSellerSubOrderAsync(
                auctionOrder.Id,
                auctionOrder.SellerId,
                auctionOrder.SellerReceiveAmount,
                null,
                cancellationToken);

            _logger.LogInformation("[AuctionOrder] Escrow payout of {Amount} settled to Seller '{SellerId}' for Order '{OrderId}'.",
                auctionOrder.SellerReceiveAmount, auctionOrder.SellerId, auctionOrder.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AuctionOrder] Escrow settlement failed for Order '{OrderId}'. Manual settlement may be required.", auctionOrder.Id);
        }

        return new ConfirmReceiptResponse(
            OrderId: auctionOrder.Id,
            OrderNo: auctionOrder.OrderNo,
            Status: auctionOrder.Status.ToString(),
            CompletedAt: now
        );
    }

    public async Task<AuctionOrderDetailResponse> GetOrderDetailAsync(
        Guid auctionOrderId,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var auctionOrder = await _dbContext.AuctionOrders.FirstOrDefaultAsync(a => a.Id == auctionOrderId, cancellationToken);
        if (auctionOrder == null)
        {
            throw new CommerceDomainException("AUCTION_ORDER_NOT_FOUND", "Auction order not found.", 404);
        }

        if (auctionOrder.BuyerId != currentUserId && auctionOrder.SellerId != currentUserId)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to access this auction order.", 403);
        }

        return MapToDetailResponse(auctionOrder);
    }

    public async Task<PagedAuctionOrderSummary> ListSellerOrdersAsync(
        Guid sellerId,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (pageIndex < 1) pageIndex = 1;
        if (pageSize < 1) pageSize = 20;

        var query = _dbContext.AuctionOrders.Where(a => a.SellerId == sellerId);
        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuctionOrderSummaryResponse(
                a.Id,
                a.OrderNo,
                a.AuctionId,
                a.BuyerId,
                a.SellerId,
                a.ProductNameSnapshot,
                a.ImageUrlSnapshot,
                a.WinningAmount,
                a.TotalAmount,
                a.Status.ToString(),
                a.PaymentDeadlineAt,
                a.CreatedAt
            ))
            .ToListAsync(cancellationToken);

        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling((double)totalCount / pageSize);
        var hasPreviousPage = pageIndex > 1;
        var hasNextPage = pageIndex < totalPages;

        return new PagedAuctionOrderSummary(
            Items: items,
            PageIndex: pageIndex,
            PageSize: pageSize,
            TotalCount: totalCount,
            TotalPages: totalPages,
            HasPreviousPage: hasPreviousPage,
            HasNextPage: hasNextPage
        );
    }
}
