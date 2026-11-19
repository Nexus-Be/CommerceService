namespace Commerce.Application.Services.AuctionOrder;

using Commerce.Application.Common.Models;
using Commerce.Application.Services.Order;
using Commerce.Application.Services.Shipment;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Commerce.Application.Interfaces;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;
using Commerce.Domain.ValueObjects;

public class AuctionOrderService : IAuctionOrderService
{
    private readonly ICommerceDbContext _dbContext;
    private readonly IWalletClient _walletClient;
    private readonly IFulfillmentClient _fulfillmentClient;
    private readonly IAuctionClient _auctionClient;
    private readonly ICatalogClient _catalogClient;
    private readonly IUserClient _userClient;
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
        ICatalogClient catalogClient,
        IUserClient userClient,
        ILogger<AuctionOrderService> logger)
    {
        _dbContext = dbContext;
        _walletClient = walletClient;
        _fulfillmentClient = fulfillmentClient;
        _auctionClient = auctionClient;
        _catalogClient = catalogClient;
        _userClient = userClient;
        _logger = logger;
    }

    public async Task<AuctionSettlementResult> ReceiveSettlementAsync(
        AuctionSettlementRequest request,
        CancellationToken cancellationToken = default)
    {
        var depositAmount = request.DepositAmount ?? 0m;
        var sourceText = request.Source?.Trim().ToUpperInvariant() ?? "WINNER";
        if (sourceText is not ("WINNER" or "SECOND_CHANCE"))
            throw new CommerceDomainException("VAL_INVALID_SETTLEMENT", "Auction settlement source must be WINNER or SECOND_CHANCE.", 400);
        var source = sourceText == "SECOND_CHANCE"
            ? AuctionOrderSource.SecondChance
            : AuctionOrderSource.Winner;

        if (request.WinningAmount <= 0 || depositAmount < 0)
        {
            throw new CommerceDomainException("INVALID_AMOUNT", "Winning amount must be greater than zero and deposit amount cannot be negative.", 422);
        }

        if (depositAmount > request.WinningAmount)
        {
            throw new CommerceDomainException("DEPOSIT_EXCEEDS_WINNING_AMOUNT", "Deposit amount cannot exceed winning amount.", 422);
        }

        // Amounts are whole VND. The deposit reference (DepositId) is checked when the seller accepts, where the hold is verified.
        if (depositAmount != decimal.Truncate(depositAmount))
            throw new CommerceDomainException("VAL_INVALID_SETTLEMENT", "Deposit amount must be a whole number.", 400);

        var existingOrders = await _dbContext.AuctionOrders
            .Where(a => a.AuctionId == request.AuctionId)
            .ToListAsync(cancellationToken);

        // Idempotency: exact (AuctionId, OrderSource, BuyerId) match returns existing order
        var identicalOrder = existingOrders.FirstOrDefault(a => a.BuyerId == request.WinnerId && a.OrderSource == source);
        if (identicalOrder != null)
        {
            if (identicalOrder.WinningBidId != request.WinningBidId ||
                identicalOrder.WinningAmount != request.WinningAmount ||
                identicalOrder.DepositAmount != depositAmount ||
                identicalOrder.DepositId != request.DepositId)
                throw new CommerceDomainException("SETTLEMENT_CONFLICT", "The auction order already has different settlement terms.", 409);
            _logger.LogInformation("[AuctionOrder] Idempotent duplicate settlement for Auction '{AuctionId}', Source '{Source}', Winner '{WinnerId}'. Returning Order '{OrderId}'.",
                request.AuctionId, source, request.WinnerId, identicalOrder.Id);
            _logger.LogInformation("[AuctionOrder] Idempotent duplicate settlement for Auction '{AuctionId}', Source '{Source}', Winner '{WinnerId}'. Returning Order '{OrderId}'.",
                request.AuctionId, source, request.WinnerId, identicalOrder.Id);

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

        // Check if an active (non-cancelled) order already exists for this auction
        var activeOtherOrder = existingOrders.FirstOrDefault(a => a.Status != SubOrderStatus.Cancelled);
        if (activeOtherOrder != null)
        {
            _logger.LogWarning("[AuctionOrder] Conflicting settlement for Auction '{AuctionId}' with Winner '{WinnerId}'. Order '{OrderId}' is currently '{Status}'.",
                request.AuctionId, request.WinnerId, activeOtherOrder.Id, activeOtherOrder.Status);

            throw new CommerceDomainException(
                "SETTLEMENT_CONFLICT",
                "An active auction order already exists for this auction.",
                409);
        }

        // Calculate financials
        var remainingAmount = request.WinningAmount - depositAmount;
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
            ImageUrlSnapshot = request.ProductImageUrl ?? request.ImageUrl,
            SkuAttributesSnapshot = request.SkuAttributes ?? "{}",
            WinningAmount = request.WinningAmount,
            DepositAmount = depositAmount,
            DepositId = request.DepositId,
            RemainingAmount = remainingAmount,
            OrderSource = source,
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

        // Đồng bộ cờ HasOrder = true sang Catalog Service nếu có SkuId (INT-09 / 034)
        if (_catalogClient != null && request.SkuId.HasValue && request.SkuId.Value != Guid.Empty)
        {
            try
            {
                await _catalogClient.MarkVariantsOrderedAsync(new[] { request.SkuId.Value }, cancellationToken);
            }
            catch (Exception markEx)
            {
                _logger.LogWarning(markEx, "[AuctionOrder] Failed to mark variant '{SkuId}' as ordered for AuctionOrder '{OrderId}'.",
                    request.SkuId.Value, orderId);
            }
        }

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

        if (auctionOrder.SkuId is not Guid skuId || skuId == Guid.Empty)
            throw new CommerceDomainException("SKU_WEIGHT_UNAVAILABLE", "Auction SKU is required to calculate shipping.", 422);
        var sku = await _catalogClient.GetSkuDetailsAsync(skuId, cancellationToken);
        if (sku is null || sku.WeightGrams <= 0 || sku.ProductId != auctionOrder.ProductId)
            throw new CommerceDomainException("SKU_WEIGHT_UNAVAILABLE", "Auction SKU weight is required to calculate shipping.", 422);

        auctionOrder.WeightGramsSnapshot = sku.WeightGrams;

        ShippingAddressDto resolvedShippingAddress;
        string resolvedPhone;

        if (request.AddressId.HasValue && request.AddressId.Value != Guid.Empty)
        {
            var address = await _userClient.GetBuyerAddressAsync(currentUserId, request.AddressId.Value, cancellationToken);
            if (address == null)
            {
                throw new CommerceDomainException("ADDRESS_NOT_FOUND", "Shipping address not found in address book.", 404);
            }

            if (address.GhnDistrictId <= 0 || string.IsNullOrWhiteSpace(address.GhnWardCode))
            {
                throw new CommerceDomainException("INVALID_SHIPPING_ADDRESS", "Shipping address must contain valid GHN district and ward codes for carrier calculation.", 400);
            }

            resolvedPhone = !string.IsNullOrWhiteSpace(request.ContactPhone) ? request.ContactPhone : address.Phone;
            resolvedShippingAddress = new ShippingAddressDto(
                FullName: address.RecipientName,
                PhoneNumber: resolvedPhone,
                AddressLine: address.Street,
                ProvinceName: address.Province,
                DistrictName: address.District,
                WardName: address.Ward,
                GhnDistrictId: address.GhnDistrictId,
                GhnWardCode: address.GhnWardCode
            );
        }
        else if (request.ShippingAddress != null)
        {
            if (string.IsNullOrWhiteSpace(request.ShippingAddress.FullName) ||
                string.IsNullOrWhiteSpace(request.ShippingAddress.AddressLine) ||
                string.IsNullOrWhiteSpace(request.ShippingAddress.ProvinceName) ||
                string.IsNullOrWhiteSpace(request.ShippingAddress.WardName))
            {
                throw new CommerceDomainException("VAL_INVALID_INPUT", "Shipping address fields cannot be empty.", 400);
            }

            resolvedPhone = !string.IsNullOrWhiteSpace(request.ContactPhone) ? request.ContactPhone : request.ShippingAddress.PhoneNumber;
            if (string.IsNullOrWhiteSpace(resolvedPhone))
            {
                throw new CommerceDomainException("VAL_INVALID_INPUT", "Contact phone cannot be empty.", 400);
            }

            if (request.ShippingAddress.GhnDistrictId <= 0 || string.IsNullOrWhiteSpace(request.ShippingAddress.GhnWardCode))
            {
                throw new CommerceDomainException("INVALID_SHIPPING_ADDRESS", "Shipping address must contain valid GHN district and ward codes for carrier calculation.", 400);
            }

            resolvedShippingAddress = request.ShippingAddress with { PhoneNumber = resolvedPhone };
        }
        else
        {
            var defaultAddr = await _userClient.GetBuyerDefaultAddressAsync(currentUserId, cancellationToken);
            if (defaultAddr == null)
            {
                throw new CommerceDomainException("ADDRESS_REQUIRED", "No shipping address provided and buyer has no default address.", 400);
            }

            if (defaultAddr.GhnDistrictId <= 0 || string.IsNullOrWhiteSpace(defaultAddr.GhnWardCode))
            {
                throw new CommerceDomainException("INVALID_SHIPPING_ADDRESS", "Shipping address must contain valid GHN district and ward codes for carrier calculation.", 400);
            }

            resolvedPhone = !string.IsNullOrWhiteSpace(request.ContactPhone) ? request.ContactPhone : defaultAddr.Phone;
            resolvedShippingAddress = new ShippingAddressDto(
                FullName: defaultAddr.RecipientName,
                PhoneNumber: resolvedPhone,
                AddressLine: defaultAddr.Street,
                ProvinceName: defaultAddr.Province,
                DistrictName: defaultAddr.District,
                WardName: defaultAddr.Ward,
                GhnDistrictId: defaultAddr.GhnDistrictId,
                GhnWardCode: defaultAddr.GhnWardCode
            );
        }

        var quoteRequest = new ShopShippingQuoteRequest(
            SellerId: auctionOrder.SellerId,
            GhnDistrictId: resolvedShippingAddress.GhnDistrictId,
            GhnWardCode: resolvedShippingAddress.GhnWardCode,
            Items: new List<ShippingQuoteItem>
            {
                new(
                    SkuId: skuId,
                    Quantity: 1,
                    WeightGrams: sku.WeightGrams
                )
            }
        );

        var shippingFee = await _fulfillmentClient.CalculateShopShippingFeeAsync(quoteRequest, cancellationToken);

        auctionOrder.ShippingAddress = JsonSerializer.Serialize(resolvedShippingAddress, JsonOptions);
        auctionOrder.ContactPhone = resolvedPhone;
        auctionOrder.ShippingFeeAmount = shippingFee;
        auctionOrder.TotalAmount = auctionOrder.RemainingAmount + shippingFee;

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
            DepositAmount: order.DepositAmount,
            RemainingAmount: order.RemainingAmount,
            OrderSource: order.OrderSource.ToString(),
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

        var history = new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            AuctionOrderId = auctionOrder.Id,
            FromStatus = SubOrderStatus.PendingPayment,
            ToStatus = SubOrderStatus.Paid,
            ActorRole = OrderActorRole.Buyer,
            Reason = "Buyer paid auction order via Nexus Wallet.",
            AuctionCallbackStatus = "Paid"
        };
        _dbContext.OrderStatusHistories.Add(history);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("[AuctionOrder] Order '{OrderId}' paid successfully. Dispatched callback to Auction '{AuctionId}'.",
            auctionOrder.Id, auctionOrder.AuctionId);

        // Immediate callback to Auction Service
        try
        {
            var callbackReq = AuctionPaymentCallbackFactory.Build(auctionOrder, history);
            if (await _auctionClient.NotifyPaymentStatusAsync(auctionOrder.AuctionId, callbackReq, cancellationToken))
            {
                history.AuctionCallbackSentAt = DateTimeOffset.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AuctionOrder] Immediate callback to AuctionService failed for order '{OrderId}'. Worker will retry.", auctionOrder.Id);
        }

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

        var shipmentRequest = ShipmentRequestFactory.ForAuctionOrder(auctionOrder);
        var shipmentResult = await _fulfillmentClient.CreateShipmentAsync(shipmentRequest, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        auctionOrder.Status = SubOrderStatus.Confirmed;
        auctionOrder.SellerAcceptedAt = now;
        if (shipmentResult != null)
        {
            auctionOrder.TrackingCode = shipmentResult.TrackingCode;
            auctionOrder.CarrierCode = shipmentResult.CarrierCode;
            auctionOrder.LabelPrintUrl = shipmentResult.LabelPrintUrl;
        }

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
            EventId: Guid.NewGuid(),
            OrderSource: auctionOrder.OrderSource.ToString().ToUpperInvariant(),
            OccurredAt: now
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
            refundAmount = auctionOrder.TotalAmount ?? auctionOrder.RemainingAmount;

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

        // Fail-safe outbound penalty for seller rejection after payment
        if (previousStatus == SubOrderStatus.Paid)
        {
            try
            {
                await _userClient.CreatePenaltyAsync(new UserPenaltyRequest(
                    UserId: auctionOrder.SellerId,
                    TargetRole: "Seller",
                    ViolationType: UserPenaltyViolationTypes.SellerRejectedPaidOrder,
                    ReferenceType: "AuctionOrder",
                    ReferenceId: auctionOrder.OrderNo,
                    Reason: request.ReasonNote ?? "Seller rejected the auction order after payment.",
                    CustomScoreDeduction: 5,
                    PolicyClauseCode: "POLICY-AUC-SELLER-REJECT"
                ), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AuctionOrder] Fail-safe: Failed to apply penalty on Seller '{SellerId}' for Order '{OrderNo}'.", auctionOrder.SellerId, auctionOrder.OrderNo);
            }
        }

        // Fail-safe outbound callback: does not affect wallet refund
        try
        {
            var callbackReq = new AuctionPaymentStatusCallbackRequest(
                OrderId: auctionOrder.Id,
                WinnerId: auctionOrder.BuyerId,
                Status: "SellerRejected",
                EventId: Guid.NewGuid(),
                OrderSource: auctionOrder.OrderSource.ToString().ToUpperInvariant(),
                OccurredAt: now
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

        // TỰ ĐỘNG ĐỒNG BỘ ĐƠN MUA HÀNG SANG CATALOG SERVICE ĐỂ MỞ QUYỀN ĐÁNH GIÁ (INT-08)
        if (_catalogClient != null && auctionOrder.ProductId != Guid.Empty)
        {
            try
            {
                await _catalogClient.SyncPurchaseRecordAsync(new CatalogPurchaseSyncRequest(
                    auctionOrder.BuyerId,
                    auctionOrder.Id,
                    auctionOrder.ProductId,
                    now
                ), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AuctionOrder] Failed to sync purchase record to Catalog for Order '{OrderId}', Product '{ProductId}'.",
                    auctionOrder.Id, auctionOrder.ProductId);
            }
        }

        return new ConfirmReceiptResponse(
            OrderId: auctionOrder.Id,
            OrderNo: auctionOrder.OrderNo,
            Status: auctionOrder.Status.ToString(),
            CompletedAt: now
        );
    }

    public async Task<AuctionOrderNotificationSnapshot?> GetNotificationSnapshotAsync(Guid auctionOrderId, CancellationToken ct = default)
    {
        var order = await _dbContext.AuctionOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == auctionOrderId && !o.IsDeleted, ct);

        // An order that was never paid has nothing to announce.
        if (order is null || order.PaidAt is null || order.TotalAmount is null || order.SellerRespondDeadlineAt is null)
            return null;

        return new AuctionOrderNotificationSnapshot(
            order.Id,
            order.OrderNo,
            order.TotalAmount.Value,
            order.PaidAt.Value,
            order.SellerRespondDeadlineAt.Value,
            order.BuyerId,
            order.SellerId,
            order.Status.ToString());
    }

    public async Task<AuctionOrderDetailResponse> GetOrderDetailAsync(
        Guid auctionOrderId,
        Guid currentUserId,
        bool isAdminOrStaff = false,
        CancellationToken cancellationToken = default)
    {
        var auctionOrder = await _dbContext.AuctionOrders.FirstOrDefaultAsync(a => a.Id == auctionOrderId, cancellationToken);
        if (auctionOrder == null)
        {
            throw new CommerceDomainException("AUCTION_ORDER_NOT_FOUND", "Auction order not found.", 404);
        }

        if (!isAdminOrStaff && auctionOrder.BuyerId != currentUserId && auctionOrder.SellerId != currentUserId)
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

    public async Task<PagedData<AuctionOrderSummaryResponse>> ListBuyerOrdersAsync(
        Guid buyerId,
        int pageIndex,
        int pageSize,
        SubOrderStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        if (pageIndex < 1) pageIndex = 1;
        if (pageSize < 1) pageSize = 20;

        var query = _dbContext.AuctionOrders.Where(a => a.BuyerId == buyerId && !a.IsDeleted);
        if (status.HasValue)
        {
            query = query.Where(a => a.Status == status.Value);
        }

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
                a.CreatedAt,
                a.WinningBidId,
                a.OrderSource.ToString(),
                a.DepositAmount,
                a.ShippingFeeAmount
            ))
            .ToListAsync(cancellationToken);

        return new PagedData<AuctionOrderSummaryResponse>(
            items,
            PaginationMetadata.Create(pageIndex, pageSize, totalCount));
    }

    public async Task<ChangeShippingAddressResponse> ChangeAuctionShippingAddressAsync(
        Guid currentUserId,
        Guid auctionOrderId,
        ChangeShippingAddressRequest request,
        bool isAdminOrStaff = false,
        CancellationToken cancellationToken = default)
    {
        var auctionOrder = await _dbContext.AuctionOrders
            .FirstOrDefaultAsync(a => a.Id == auctionOrderId && !a.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException("AUCTION_ORDER_NOT_FOUND", "AuctionOrder", auctionOrderId);

        if (!isAdminOrStaff && auctionOrder.BuyerId != currentUserId)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to modify this auction order.", 403);
        }

        if (auctionOrder.Status is SubOrderStatus.Shipping or SubOrderStatus.Delivered or SubOrderStatus.Completed or SubOrderStatus.Cancelled or SubOrderStatus.Refunded ||
            !string.IsNullOrWhiteSpace(auctionOrder.TrackingCode))
        {
            throw new CommerceDomainException("CANNOT_CHANGE_ADDRESS_AFTER_SHIPPING", "Shipping address cannot be changed after the order has been shipped or completed.", 400);
        }

        var (resolvedAddress, resolvedPhone) = await ResolveAuctionShippingAddressAsync(auctionOrder.BuyerId, request.AddressId, request.ShippingAddress, request.ContactPhone, cancellationToken);

        var skuId = auctionOrder.SkuId is { } sku && sku != Guid.Empty ? sku : auctionOrder.ProductId;
        var weight = auctionOrder.WeightGramsSnapshot > 0 ? auctionOrder.WeightGramsSnapshot : 500;

        var quoteRequest = new ShopShippingQuoteRequest(
            SellerId: auctionOrder.SellerId,
            GhnDistrictId: resolvedAddress.GhnDistrictId,
            GhnWardCode: resolvedAddress.GhnWardCode,
            Items: new List<ShippingQuoteItem>
            {
                new(SkuId: skuId, Quantity: 1, WeightGrams: weight)
            }
        );

        var newShippingFee = await _fulfillmentClient.CalculateShopShippingFeeAsync(quoteRequest, cancellationToken);
        var oldShippingFee = auctionOrder.ShippingFeeAmount ?? 0m;

        if (auctionOrder.Status is SubOrderStatus.Paid or SubOrderStatus.Confirmed)
        {
            if (newShippingFee > oldShippingFee)
            {
                throw new CommerceDomainException("SHIPPING_FEE_INCREASED", "Shipping fee to new address exceeds prepaid amount. Please cancel and reorder.", 400);
            }
        }
        else if (auctionOrder.Status == SubOrderStatus.PendingPayment)
        {
            auctionOrder.ShippingFeeAmount = newShippingFee;
            auctionOrder.TotalAmount = auctionOrder.RemainingAmount + newShippingFee;
        }

        var fullAddressText = $"{resolvedAddress.FullName}, {resolvedAddress.AddressLine}, {resolvedAddress.WardName}, {resolvedAddress.DistrictName}, {resolvedAddress.ProvinceName}";
        auctionOrder.ShippingAddress = JsonSerializer.Serialize(resolvedAddress, JsonOptions);
        auctionOrder.ContactPhone = resolvedPhone;

        var now = DateTimeOffset.UtcNow;
        _dbContext.OrderStatusHistories.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            AuctionOrderId = auctionOrder.Id,
            FromStatus = auctionOrder.Status,
            ToStatus = auctionOrder.Status,
            ChangedBy = currentUserId,
            ActorRole = isAdminOrStaff ? OrderActorRole.SupportStaff : OrderActorRole.Buyer,
            Reason = $"Shipping address updated to: {fullAddressText}",
            CreatedAt = now
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new ChangeShippingAddressResponse(
            TargetId: auctionOrder.Id,
            TargetType: "AuctionOrder",
            NewAddressText: fullAddressText,
            OldShippingFee: oldShippingFee,
            NewShippingFee: newShippingFee,
            TotalAmount: auctionOrder.TotalAmount ?? auctionOrder.RemainingAmount,
            UpdatedAt: now
        );
    }

    public async Task<ShipmentTimelineResponse> GetAuctionShipmentTimelineAsync(
        Guid currentUserId,
        Guid auctionOrderId,
        bool isAdminOrStaff = false,
        CancellationToken cancellationToken = default)
    {
        var ao = await _dbContext.AuctionOrders
            .AsNoTracking()
            .Include(a => a.StatusHistories)
            .FirstOrDefaultAsync(a => a.Id == auctionOrderId && !a.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException("AUCTION_ORDER_NOT_FOUND", "AuctionOrder", auctionOrderId);

        if (!isAdminOrStaff && ao.BuyerId != currentUserId && ao.SellerId != currentUserId)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to view shipment timeline for this auction order.", 403);
        }

        var timelineEvents = MapHistoriesToTimeline(ao.StatusHistories, ao.Status, ao.CreatedAt);
        ShippingAddressSnapshot addressSnapshot;
        if (!string.IsNullOrWhiteSpace(ao.ShippingAddress))
        {
            try
            {
                addressSnapshot = JsonSerializer.Deserialize<ShippingAddressSnapshot>(ao.ShippingAddress, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? new ShippingAddressSnapshot
                    {
                        ReceiverName = "Auction Winner",
                        ContactPhone = ao.ContactPhone ?? ""
                    };
            }
            catch
            {
                addressSnapshot = new ShippingAddressSnapshot
                {
                    ReceiverName = "Auction Winner",
                    ContactPhone = ao.ContactPhone ?? "",
                    Street = ao.ShippingAddress
                };
            }
        }
        else
        {
            addressSnapshot = new ShippingAddressSnapshot
            {
                ReceiverName = "Auction Winner",
                ContactPhone = ao.ContactPhone ?? ""
            };
        }

        return new ShipmentTimelineResponse(
            ao.Id,
            ao.OrderNo,
            ao.Id,
            ao.CarrierCode ?? "GHN",
            FormatCarrierName(ao.CarrierCode),
            ao.TrackingCode,
            ao.LabelPrintUrl,
            ao.Status.ToString(),
            ao.EstimatedDeliveryAt,
            addressSnapshot,
            timelineEvents
        );
    }

    public async Task<CancelAuctionOrderResponse> CancelAuctionOrderAsync(
        Guid currentUserId,
        Guid auctionOrderId,
        CancelAuctionOrderRequest request,
        bool isAdminOrStaff = false,
        CancellationToken cancellationToken = default)
    {
        var auctionOrder = await _dbContext.AuctionOrders
            .FirstOrDefaultAsync(a => a.Id == auctionOrderId && !a.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException("AUCTION_ORDER_NOT_FOUND", "AuctionOrder", auctionOrderId);

        if (!isAdminOrStaff && auctionOrder.BuyerId != currentUserId)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to cancel this auction order.", 403);
        }

        if ((auctionOrder.Status != SubOrderStatus.PendingPayment && auctionOrder.Status != SubOrderStatus.Paid) ||
            auctionOrder.SellerAcceptedAt != null)
        {
            throw new CommerceDomainException("AUCTION_CANNOT_BE_CANCELLED", "Auction order cannot be cancelled in its current status.", 400);
        }

        decimal refundedAmount = 0m;
        var now = DateTimeOffset.UtcNow;
        var cancellation = new OrderCancellation
        {
            Id = Guid.NewGuid(),
            AuctionOrderId = auctionOrder.Id,
            ReasonCode = CancellationReasonCode.BuyerChangedMind,
            ReasonNote = request.Reason ?? "Cancelled by buyer before seller acceptance",
            WasPaid = auctionOrder.Status == SubOrderStatus.Paid,
            ActorRole = isAdminOrStaff ? OrderActorRole.SupportStaff : OrderActorRole.Buyer,
            CancelledBy = currentUserId
        };
        _dbContext.OrderCancellations.Add(cancellation);

        if (auctionOrder.Status == SubOrderStatus.Paid)
        {
            refundedAmount = auctionOrder.TotalAmount ?? auctionOrder.RemainingAmount;
            var refundSuccess = await _walletClient.RefundOrderPaymentAsync(
                auctionOrder.Id,
                auctionOrder.BuyerId.ToString(),
                refundedAmount,
                request.Reason ?? "Auction order cancelled prior to seller acceptance.",
                null,
                cancellationToken);

            if (!refundSuccess)
            {
                throw new CommerceDomainException("WALLET_REFUND_FAILED", "Failed to process refund payment via wallet service.", 400);
            }

            _dbContext.OrderRefunds.Add(new OrderRefund
            {
                Id = Guid.NewGuid(),
                AuctionOrderId = auctionOrder.Id,
                CancellationId = cancellation.Id,
                RefundType = RefundType.Full,
                Amount = refundedAmount,
                Status = RefundStatus.Completed,
                ReasonCode = "BUYER_CANCELLED",
                ReasonNote = request.Reason ?? "Auction order cancelled prior to seller acceptance.",
                ActorRole = isAdminOrStaff ? OrderActorRole.SupportStaff : OrderActorRole.Buyer,
                RequestedBy = currentUserId,
                RequestedAt = now,
                CompletedAt = now
            });

            try
            {
                var callbackReq = new AuctionPaymentStatusCallbackRequest(
                    OrderId: auctionOrder.Id,
                    WinnerId: auctionOrder.BuyerId,
                    Status: "Cancelled",
                    Amount: auctionOrder.TotalAmount ?? auctionOrder.RemainingAmount,
                    OccurredAt: now,
                    OrderNo: auctionOrder.OrderNo
                );
                await _auctionClient.NotifyPaymentStatusAsync(auctionOrder.AuctionId, callbackReq, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AuctionOrder] Non-fatal callback error notifying AuctionService of cancellation for AuctionOrder '{OrderId}'.", auctionOrder.Id);
            }
        }

        var fromStatus = auctionOrder.Status;
        auctionOrder.Status = SubOrderStatus.Cancelled;
        auctionOrder.CancelledAt = now;

        _dbContext.OrderStatusHistories.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            AuctionOrderId = auctionOrder.Id,
            FromStatus = fromStatus,
            ToStatus = SubOrderStatus.Cancelled,
            ChangedBy = currentUserId,
            ActorRole = isAdminOrStaff ? OrderActorRole.SupportStaff : OrderActorRole.Buyer,
            Reason = $"Auction order cancelled. Reason: {request.Reason ?? "Cancelled by buyer"}",
            CreatedAt = now
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new CancelAuctionOrderResponse(
            AuctionOrderId: auctionOrder.Id,
            OrderNo: auctionOrder.OrderNo,
            Status: auctionOrder.Status.ToString(),
            RefundedAmount: refundedAmount,
            CancelledAt: now
        );
    }

    public async Task<RetryAuctionShipmentResponse> RetryAuctionShipmentAsync(
        Guid currentUserId,
        Guid auctionOrderId,
        bool isAdminOrStaff = false,
        CancellationToken cancellationToken = default)
    {
        var auctionOrder = await _dbContext.AuctionOrders
            .FirstOrDefaultAsync(a => a.Id == auctionOrderId && !a.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException("AUCTION_ORDER_NOT_FOUND", "AuctionOrder", auctionOrderId);

        if (!isAdminOrStaff && auctionOrder.SellerId != currentUserId)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to retry shipment creation for this auction order.", 403);
        }

        if (auctionOrder.Status != SubOrderStatus.Confirmed)
        {
            throw new CommerceDomainException("INVALID_STATUS", "Only confirmed auction orders can have carrier shipments created.", 400);
        }

        if (!string.IsNullOrWhiteSpace(auctionOrder.TrackingCode))
        {
            throw new CommerceDomainException("SHIPMENT_ALREADY_EXISTS", "Shipment has already been created for this auction order.", 400);
        }

        var shipmentRequest = ShipmentRequestFactory.ForAuctionOrder(auctionOrder);
        var shipmentResult = await _fulfillmentClient.CreateShipmentAsync(shipmentRequest, cancellationToken);

        if (shipmentResult == null || string.IsNullOrWhiteSpace(shipmentResult.TrackingCode))
        {
            throw new CommerceDomainException("SHIPMENT_CREATION_FAILED", "Failed to create shipment with carrier.", 500);
        }

        var now = DateTimeOffset.UtcNow;
        auctionOrder.TrackingCode = shipmentResult.TrackingCode;
        auctionOrder.CarrierCode = shipmentResult.CarrierCode ?? "GHN";
        auctionOrder.LabelPrintUrl = shipmentResult.LabelPrintUrl;
        auctionOrder.Status = SubOrderStatus.Shipping;
        auctionOrder.ShippedAt = now;

        _dbContext.OrderStatusHistories.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            AuctionOrderId = auctionOrder.Id,
            FromStatus = SubOrderStatus.Confirmed,
            ToStatus = SubOrderStatus.Shipping,
            ChangedBy = currentUserId,
            ActorRole = isAdminOrStaff ? OrderActorRole.SupportStaff : OrderActorRole.Seller,
            Reason = $"Shipment retried and created successfully. TrackingCode: {shipmentResult.TrackingCode}",
            CreatedAt = now
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new RetryAuctionShipmentResponse(
            AuctionOrderId: auctionOrder.Id,
            OrderNo: auctionOrder.OrderNo,
            TrackingCode: shipmentResult.TrackingCode,
            CarrierCode: auctionOrder.CarrierCode,
            LabelPrintUrl: shipmentResult.LabelPrintUrl,
            RetriedAt: now
        );
    }

    private async Task<(ShippingAddressDto Address, string ContactPhone)> ResolveAuctionShippingAddressAsync(
        Guid buyerId,
        Guid? addressId,
        ShippingAddressInput? rawAddress,
        string? contactPhone,
        CancellationToken cancellationToken)
    {
        if (addressId.HasValue && addressId.Value != Guid.Empty)
        {
            var address = await _userClient.GetBuyerAddressAsync(buyerId, addressId.Value, cancellationToken);
            if (address == null)
            {
                throw new CommerceDomainException("ADDRESS_NOT_FOUND", "Shipping address not found in address book.", 404);
            }

            if (address.GhnDistrictId <= 0 || string.IsNullOrWhiteSpace(address.GhnWardCode))
            {
                throw new CommerceDomainException("INVALID_SHIPPING_ADDRESS", "Shipping address must contain valid GHN district and ward codes for carrier calculation.", 400);
            }

            var phone = !string.IsNullOrWhiteSpace(contactPhone) ? contactPhone : address.Phone;
            var resolved = new ShippingAddressDto(
                FullName: address.RecipientName,
                PhoneNumber: phone,
                AddressLine: address.Street,
                ProvinceName: address.Province,
                DistrictName: address.District,
                WardName: address.Ward,
                GhnDistrictId: address.GhnDistrictId,
                GhnWardCode: address.GhnWardCode
            );
            return (resolved, phone);
        }

        if (rawAddress != null)
        {
            if (string.IsNullOrWhiteSpace(rawAddress.ReceiverName) ||
                string.IsNullOrWhiteSpace(rawAddress.Street) ||
                string.IsNullOrWhiteSpace(rawAddress.Province) ||
                string.IsNullOrWhiteSpace(rawAddress.Ward))
            {
                throw new CommerceDomainException("VAL_INVALID_INPUT", "Shipping address fields cannot be empty.", 400);
            }

            var phone = !string.IsNullOrWhiteSpace(contactPhone) ? contactPhone : rawAddress.ContactPhone;
            if (string.IsNullOrWhiteSpace(phone))
            {
                throw new CommerceDomainException("VAL_INVALID_INPUT", "Contact phone cannot be empty.", 400);
            }

            if (!rawAddress.GhnDistrictId.HasValue || rawAddress.GhnDistrictId.Value <= 0 || string.IsNullOrWhiteSpace(rawAddress.GhnWardCode))
            {
                throw new CommerceDomainException("INVALID_SHIPPING_ADDRESS", "Shipping address must contain valid GHN district and ward codes for carrier calculation.", 400);
            }

            var resolved = new ShippingAddressDto(
                FullName: rawAddress.ReceiverName,
                PhoneNumber: phone,
                AddressLine: rawAddress.Street,
                ProvinceName: rawAddress.Province,
                DistrictName: rawAddress.District,
                WardName: rawAddress.Ward,
                GhnDistrictId: rawAddress.GhnDistrictId.Value,
                GhnWardCode: rawAddress.GhnWardCode
            );
            return (resolved, phone);
        }

        var defaultAddr = await _userClient.GetBuyerDefaultAddressAsync(buyerId, cancellationToken);
        if (defaultAddr == null)
        {
            throw new CommerceDomainException("ADDRESS_REQUIRED", "No shipping address provided and buyer has no default address.", 400);
        }

        if (defaultAddr.GhnDistrictId <= 0 || string.IsNullOrWhiteSpace(defaultAddr.GhnWardCode))
        {
            throw new CommerceDomainException("INVALID_SHIPPING_ADDRESS", "Shipping address must contain valid GHN district and ward codes for carrier calculation.", 400);
        }

        var defaultPhone = !string.IsNullOrWhiteSpace(contactPhone) ? contactPhone : defaultAddr.Phone;
        var defaultResolved = new ShippingAddressDto(
            FullName: defaultAddr.RecipientName,
            PhoneNumber: defaultPhone,
            AddressLine: defaultAddr.Street,
            ProvinceName: defaultAddr.Province,
            DistrictName: defaultAddr.District,
            WardName: defaultAddr.Ward,
            GhnDistrictId: defaultAddr.GhnDistrictId,
            GhnWardCode: defaultAddr.GhnWardCode
        );
        return (defaultResolved, defaultPhone);
    }

    private static List<TimelineEventDto> MapHistoriesToTimeline(ICollection<OrderStatusHistory> histories, SubOrderStatus currentStatus, DateTimeOffset createdAt)
    {
        var events = new List<TimelineEventDto>
        {
            new("OrderPlaced", "Order Created", "Auction order created successfully.", createdAt)
        };

        foreach (var h in histories.OrderBy(h => h.CreatedAt))
        {
            events.Add(new TimelineEventDto(
                h.ToStatus.ToString(),
                h.ToStatus.ToString(),
                h.Reason ?? $"Status changed to {h.ToStatus}",
                h.CreatedAt
            ));
        }

        return events;
    }

    private static string FormatCarrierName(string? carrierCode) => carrierCode?.ToUpperInvariant() switch
    {
        "GHN" => "Giao Hàng Nhanh (GHN)",
        "GHTK" => "Giao Hàng Tiết Kiệm (GHTK)",
        "VTPOST" => "Viettel Post",
        _ => carrierCode ?? "Standard Delivery"
    };
}
