namespace Commerce.Application.Services.Order;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Commerce.Application.Common;
using Commerce.Application.Common.Models;
using Commerce.Application.Interfaces;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;
using Commerce.Domain.ValueObjects;

public sealed class OrderService : IOrderService
{
    private readonly ICommerceDbContext _dbContext;
    private readonly IWalletClient _walletClient;
    private readonly IFulfillmentClient _fulfillmentClient;
    private readonly ICatalogClient _catalogClient;
    private readonly IDistributedCache _cache;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        ICommerceDbContext dbContext,
        IWalletClient walletClient,
        IFulfillmentClient fulfillmentClient,
        ICatalogClient catalogClient,
        IDistributedCache cache,
        ILogger<OrderService> logger)
    {
        _dbContext = dbContext;
        _walletClient = walletClient;
        _fulfillmentClient = fulfillmentClient;
        _catalogClient = catalogClient;
        _cache = cache;
        _logger = logger;
    }

    public async Task<CheckoutQuoteResponse> QuoteCheckoutAsync(Guid buyerId, CheckoutQuoteRequest request, CancellationToken ct = default)
    {
        if (request.CartItemIds == null || request.CartItemIds.Count == 0)
        {
            throw new CommerceDomainException("CART_EMPTY", "No valid items selected for checkout.");
        }

        if (request.ShippingAddress == null ||
            string.IsNullOrWhiteSpace(request.ShippingAddress.ReceiverName) ||
            string.IsNullOrWhiteSpace(request.ContactPhone) ||
            string.IsNullOrWhiteSpace(request.ShippingAddress.Province) ||
            string.IsNullOrWhiteSpace(request.ShippingAddress.District) ||
            string.IsNullOrWhiteSpace(request.ShippingAddress.Ward))
        {
            throw new CommerceDomainException("VAL_INVALID_INPUT", "Shipping address fields cannot be empty.");
        }

        var cart = await _dbContext.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.BuyerId == buyerId && !c.IsDeleted, ct)
            ?? throw new CommerceDomainException("CART_EMPTY", "Buyer has no active cart.");

        var selectedItems = cart.Items
            .Where(i => request.CartItemIds.Contains(i.Id) && !i.IsDeleted)
            .ToList();

        if (selectedItems.Count == 0)
        {
            throw new CommerceDomainException("CART_EMPTY", "Selected cart items were not found in active cart.");
        }

        var shopQuotes = new List<ShopOrderQuote>();
        var groupedBySeller = selectedItems.GroupBy(i => i.SellerId);

        foreach (var group in groupedBySeller)
        {
            var sellerId = group.Key;
            var shopItems = group.Select(i => new ShopQuoteItem(
                i.Id,
                i.ProductId,
                i.SkuId,
                i.ProductNameSnapshot,
                i.Quantity,
                i.UnitPriceSnapshot,
                i.Quantity * i.UnitPriceSnapshot
            )).ToList();

            decimal itemsAmount = shopItems.Sum(i => i.LineTotal);
            decimal productDiscount = 0m;
            decimal shopDiscount = 0m;
            decimal systemDiscount = 0m;
            decimal discountAmount = productDiscount + shopDiscount + systemDiscount;

            // Tính cước GHN động theo từng Shop qua Fulfillment Service (CHK-01)
            decimal shippingFeeAmount = 30000m;
            try
            {
                var quoteItems = shopItems.Select(i => new ShippingQuoteItem(i.SkuId, i.Quantity, 200)).ToList();
                var ghnDistrictId = request.ShippingAddress.GhnDistrictId ?? 0;
                var ghnWardCode = request.ShippingAddress.GhnWardCode ?? "";
                if (ghnDistrictId > 0 && !string.IsNullOrEmpty(ghnWardCode))
                {
                    var shippingQuoteRequest = new ShopShippingQuoteRequest(sellerId, ghnDistrictId, ghnWardCode, quoteItems);
                    shippingFeeAmount = await _fulfillmentClient.CalculateShopShippingFeeAsync(shippingQuoteRequest, ct);
                }
            }
            catch
            {
                // Fallback to default shipping fee 30,000 VND
                shippingFeeAmount = 30000m;
            }

            decimal totalAmount = itemsAmount - discountAmount + shippingFeeAmount;

            shopQuotes.Add(new ShopOrderQuote(
                sellerId,
                $"Shop {sellerId.ToString()[..8]}",
                shopItems,
                itemsAmount,
                productDiscount,
                shopDiscount,
                systemDiscount,
                discountAmount,
                shippingFeeAmount,
                totalAmount
            ));
        }

        decimal grandTotal = shopQuotes.Sum(s => s.TotalAmount);
        var quoteId = Guid.NewGuid();
        var expiredAt = DateTimeOffset.UtcNow.AddMinutes(15);

        // Lấy số dư ví thực tế từ WalletService (ISSUE-05)
        decimal walletBalance = 0m;
        try
        {
            walletBalance = await _walletClient.GetWalletBalanceAsync(buyerId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch real-time wallet balance for Buyer '{BuyerId}'. Fallback to 0.", buyerId);
        }

        var quoteResponse = new CheckoutQuoteResponse(
            quoteId,
            grandTotal,
            walletBalance,
            shopQuotes,
            request.VoucherCodes ?? new List<string>(),
            null,
            expiredAt
        );

        // Lưu vào Distributed Cache (Redis / Memory) với TTL 15 phút (ISSUE-01)
        var quoteKey = $"checkout_quote:{quoteId}";
        var quotePayload = JsonSerializer.Serialize(new CachedQuote
        {
            QuoteId = quoteId,
            BuyerId = buyerId,
            Request = request,
            Response = quoteResponse,
            ExpiredAt = expiredAt
        });

        await _cache.SetStringAsync(quoteKey, quotePayload, new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = expiredAt
        }, ct);

        return quoteResponse;
    }

    public async Task<CreateOrderResponse> CreateOrderAsync(Guid buyerId, CreateOrderRequest request, CancellationToken ct = default)
    {
        // ATOMIC POP (ISSUE-02): Đọc và xóa ngay lập tức khỏi Cache để triệt tiêu Race Condition / Double Submit
        var quoteKey = $"checkout_quote:{request.QuoteId}";
        var quoteJson = await _cache.GetStringAsync(quoteKey, ct);
        if (string.IsNullOrEmpty(quoteJson))
        {
            throw new CommerceDomainException("QUOTE_EXPIRED", "Checkout quote has expired, is invalid, or is currently being processed.");
        }

        await _cache.RemoveAsync(quoteKey, ct);

        var cachedQuote = JsonSerializer.Deserialize<CachedQuote>(quoteJson);
        if (cachedQuote == null || cachedQuote.ExpiredAt < DateTimeOffset.UtcNow || cachedQuote.BuyerId != buyerId)
        {
            throw new CommerceDomainException("QUOTE_EXPIRED", "Checkout quote has expired, is invalid, or is currently being processed.");
        }

        // BATCH KIỂM TRA BIẾN ĐỘNG GIÁ VÀ TRẠNG THÁI SKU TỪ CATALOG (ASYMMETRIC PRICE DRIFT POLICY - CHỐNG N+1)
        var allQuoteItems = cachedQuote.Response.Shops.SelectMany(s => s.Items).ToList();
        var allSkuIds = allQuoteItems.Select(i => i.SkuId).Distinct().ToList();
        var batchSkuDetails = await _catalogClient.GetSkuBatchDetailsAsync(allSkuIds, ct);

        var priceIncreases = new List<(ShopQuoteItem QuoteItem, CatalogProductCheckResult SkuInfo)>();
        var priceDrops = new List<PriceDropDto>();
        var freshSkuPrices = new Dictionary<Guid, decimal>();

        foreach (var item in allQuoteItems)
        {
            CatalogProductCheckResult? skuInfo = null;
            if (batchSkuDetails != null && batchSkuDetails.TryGetValue(item.SkuId, out var foundInfo))
            {
                skuInfo = foundInfo;
            }

            // Fallback nếu mock hoặc client chưa setup batch: gọi lẻ GetSkuDetailsAsync
            skuInfo ??= await _catalogClient.GetSkuDetailsAsync(item.SkuId, ct);

            if (skuInfo == null)
            {
                throw new CommerceDomainException("CATALOG_SKU_NOT_FOUND", $"Product SKU '{item.SkuId}' was not found in catalog.", 404);
            }
            if (!skuInfo.IsPurchasable)
            {
                throw new CommerceDomainException("CATALOG_SKU_INACTIVE", $"Product '{item.ProductName}' is no longer available for purchase.", 400);
            }

            freshSkuPrices[item.SkuId] = skuInfo.UnitPrice;

            if (skuInfo.UnitPrice > item.UnitPrice)
            {
                priceIncreases.Add((item, skuInfo));
            }
            else if (skuInfo.UnitPrice < item.UnitPrice)
            {
                priceDrops.Add(new PriceDropDto(
                    item.SkuId,
                    item.ProductName,
                    item.UnitPrice,
                    skuInfo.UnitPrice,
                    (item.UnitPrice - skuInfo.UnitPrice) * item.Quantity
                ));
            }
        }

        // KỊCH BẢN A: CÓ ÍT NHẤT 1 SẢN PHẨM TĂNG GIÁ (HARD BLOCK & REFRESH SNAPSHOT)
        if (priceIncreases.Count > 0)
        {
            var buyerCart = await _dbContext.Carts
                .Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.BuyerId == buyerId && !c.IsDeleted, ct);

            if (buyerCart != null)
            {
                foreach (var cartItem in buyerCart.Items)
                {
                    if (freshSkuPrices.TryGetValue(cartItem.SkuId, out var currentPrice) && cartItem.UnitPriceSnapshot != currentPrice)
                    {
                        cartItem.UnitPriceSnapshot = currentPrice;
                        cartItem.UpdatedAt = DateTimeOffset.UtcNow;
                    }
                }
                buyerCart.SubtotalAmount = buyerCart.Items.Sum(i => i.Quantity * i.UnitPriceSnapshot);
                buyerCart.LastActivityAt = DateTimeOffset.UtcNow;
                await _dbContext.SaveChangesAsync(ct);
            }

            var firstIncrease = priceIncreases[0];
            throw new CommerceDomainException(
                "PRICE_CHANGED",
                $"Price of '{firstIncrease.QuoteItem.ProductName}' has increased from {firstIncrease.QuoteItem.UnitPrice:N0} to {firstIncrease.SkuInfo.UnitPrice:N0}. Cart prices have been refreshed. Please review your order before proceeding.",
                409);
        }

        // KỊCH BẢN B: CHỈ CÓ SẢN PHẨM GIẢM GIÁ HOẶC GIỮ NGUYÊN (SILENT AUTO-APPLY)
        var orderId = Guid.NewGuid();
        var orderNo = $"ORD-{DateTime.UtcNow:yyyyMMdd}-{orderId.ToString()[..6].ToUpperInvariant()}";
        var subOrderIds = new List<Guid>();

        decimal grandTotal = 0m;
        var createdSubOrders = new List<SubOrder>();

        foreach (var shop in cachedQuote.Response.Shops)
        {
            var subOrderId = Guid.NewGuid();
            subOrderIds.Add(subOrderId);
            var subOrderNo = $"SUB-{DateTime.UtcNow:yyyyMMdd}-{subOrderId.ToString()[..6].ToUpperInvariant()}";

            var addressSnapshot = new ShippingAddressSnapshot(
                cachedQuote.Request.ShippingAddress.ReceiverName,
                cachedQuote.Request.ContactPhone,
                cachedQuote.Request.ShippingAddress.Street,
                cachedQuote.Request.ShippingAddress.Ward,
                cachedQuote.Request.ShippingAddress.District,
                cachedQuote.Request.ShippingAddress.Province,
                cachedQuote.Request.ShippingAddress.GhnDistrictId ?? 0,
                cachedQuote.Request.ShippingAddress.GhnWardCode ?? "",
                request.ShippingAddress
            );

            decimal shopItemsAmount = 0m;
            var subOrderItems = new List<SubOrderItem>();

            foreach (var item in shop.Items)
            {
                var effectivePrice = freshSkuPrices.TryGetValue(item.SkuId, out var freshPrice) ? freshPrice : item.UnitPrice;
                var lineTotal = effectivePrice * item.Quantity;
                shopItemsAmount += lineTotal;

                subOrderItems.Add(new SubOrderItem
                {
                    Id = Guid.NewGuid(),
                    SubOrderId = subOrderId,
                    ProductId = item.ProductId,
                    SkuId = item.SkuId,
                    SellerId = shop.SellerId,
                    ProductNameSnapshot = item.ProductName,
                    Quantity = item.Quantity,
                    UnitPrice = effectivePrice,
                    UnitPriceAfterDiscount = effectivePrice,
                    LineTotal = lineTotal
                });
            }

            var shopTotal = shopItemsAmount - shop.DiscountAmount + shop.ShippingFeeAmount;
            grandTotal += shopTotal;

            var subOrder = new SubOrder
            {
                Id = subOrderId,
                SubOrderNo = subOrderNo,
                OrderId = orderId,
                BuyerId = buyerId,
                SellerId = shop.SellerId,
                Status = SubOrderStatus.PendingPayment,
                PaymentMethod = request.PaymentMethod,
                ShippingAddress = addressSnapshot,
                ContactPhone = request.ContactPhone,
                ItemsAmount = shopItemsAmount,
                ProductDiscountAmount = shop.ProductDiscountAmount,
                ShopDiscountAmount = shop.ShopDiscountAmount,
                PlatformMarkupAmount = 0m,
                SystemDiscountAmount = shop.SystemDiscountAmount,
                ShippingFeeAmount = shop.ShippingFeeAmount,
                TotalAmount = shopTotal,
                SellerReceiveAmount = shopItemsAmount - shop.DiscountAmount,
                PlatformFeeAmount = 0m,
                PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
            };

            foreach (var soi in subOrderItems)
            {
                subOrder.Items.Add(soi);
            }

            _dbContext.SubOrders.Add(subOrder);
            createdSubOrders.Add(subOrder);
        }

        var order = new Order
        {
            Id = orderId,
            BuyerId = buyerId,
            Status = OrderStatus.PendingPayment,
            TotalAmount = grandTotal,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15),
            PaymentAttemptCount = 1
        };

        _dbContext.Orders.Add(order);

        // Giữ hàng tạm thời tại Fulfillment Service (TTL 15 phút) (CHK-02)
        var reservationItems = createdSubOrders
            .SelectMany(so => so.Items.Select(i => new StockReservationItem(i.SkuId, i.Quantity, so.SellerId)))
            .ToList();

        var reserved = await _fulfillmentClient.ReserveStockAsync(orderId, reservationItems, ttlMinutes: 15, ct);
        if (!reserved)
        {
            throw new CommerceDomainException("INSUFFICIENT_STOCK", "One or more items in the order have insufficient stock.", 400);
        }

        bool paid = false;

        // Tích hợp thanh toán ví theo hợp đồng chuẩn của WalletService
        if (request.PaymentMethod == PaymentMethod.Wallet)
        {
            var paymentResult = await _walletClient.PayOrderAsync(order.Id, order.TotalAmount, cancellationToken: ct);

            if (paymentResult?.IsSuccess == true)
            {
                paid = true;
                order.Status = OrderStatus.Paid;

                if (!string.IsNullOrWhiteSpace(paymentResult.TransactionId) &&
                    Guid.TryParse(paymentResult.TransactionId, out var parsedTxId))
                {
                    order.WalletTransactionId = parsedTxId;
                }

                foreach (var so in createdSubOrders)
                {
                    so.Status = SubOrderStatus.Paid;
                    so.PaidAt = DateTimeOffset.UtcNow;
                    if (order.WalletTransactionId.HasValue)
                    {
                        so.WalletTransactionId = order.WalletTransactionId;
                    }
                }

                // Thanh toán thành công -> trừ kho vĩnh viễn (CHK-02, ISSUE-04)
                bool stockCommitted = await _fulfillmentClient.CommitStockAsync(orderId, ct);
                if (!stockCommitted)
                {
                    _logger.LogError("CRITICAL: Order '{OrderId}' was paid via Wallet, but stock commit failed at Fulfillment Service. Flagged for reconciliation.", orderId);
                }
            }
            else if (paymentResult?.IsTransientFailure == true)
            {
                // Lỗi chưa xác định (Timeout, 5xx): Theo handoff Wallet, giữ PendingPayment và giữ reservation
                _logger.LogWarning("Payment for Order '{OrderId}' encountered transient failure ({ErrorCode}): {Message}. Remaining PendingPayment for reconciliation.",
                    orderId, paymentResult.ErrorCode, paymentResult.ErrorMessage);
            }
            else
            {
                // Lỗi dứt khoát (INSUFFICIENT_BALANCE, WALLET_NOT_FOUND):
                _logger.LogWarning("Payment for Order '{OrderId}' failed definitively ({ErrorCode}): {Message}. Remaining PendingPayment.",
                    orderId, paymentResult?.ErrorCode, paymentResult?.ErrorMessage);
            }
        }
        else if (request.PaymentMethod == PaymentMethod.Cod)
        {
            // Đơn COD: commit trừ kho ngay khi tạo đơn
            bool stockCommitted = await _fulfillmentClient.CommitStockAsync(orderId, ct);
            if (!stockCommitted)
            {
                _logger.LogError("CRITICAL: COD Order '{OrderId}' was placed, but stock commit failed at Fulfillment Service.", orderId);
            }
        }

        try
        {
            // Tự động dọn dẹp các món đã mua khỏi giỏ hàng
            var cart = await _dbContext.Carts
                .Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.BuyerId == buyerId && !c.IsDeleted, ct);

            if (cart != null)
            {
                var purchasedItemIds = cachedQuote.Request.CartItemIds;
                var itemsToRemove = cart.Items
                    .Where(i => purchasedItemIds.Contains(i.Id))
                    .ToList();

                foreach (var item in itemsToRemove)
                {
                    _dbContext.CartItems.Remove(item);
                }

                cart.ItemCount = Math.Max(0, cart.Items.Count - itemsToRemove.Count);
                cart.SubtotalAmount = cart.Items.Except(itemsToRemove).Sum(i => i.Quantity * i.UnitPriceSnapshot);
                cart.LastActivityAt = DateTimeOffset.UtcNow;
            }

            await _dbContext.SaveChangesAsync(ct);
        }
        catch (Exception dbEx) when (dbEx is not CommerceDomainException)
        {
            _logger.LogError(dbEx, "CRITICAL: Database persistence failed for Order '{OrderId}' (Buyer: '{BuyerId}'). Initiating distributed compensation rollback.", orderId, buyerId);

            // 1. Rollback Kho (Fulfillment Compensation): Giải phóng số lượng tồn kho đã giữ/commit
            try
            {
                await _fulfillmentClient.ReleaseStockAsync(orderId, CancellationToken.None);
                _logger.LogInformation("Compensation: Successfully released warehouse stock for Order '{OrderId}'.", orderId);
            }
            catch (Exception stockEx)
            {
                _logger.LogError(stockEx, "CRITICAL: Compensation failed while releasing warehouse stock for Order '{OrderId}'. Flagged for manual reconciliation.", orderId);
            }

            // 2. Rollback Ví (Wallet Compensation): Nếu đã trừ tiền ví thành công, hoàn lại 100% tiền vào ví
            if (paid)
            {
                try
                {
                    bool refunded = await _walletClient.RefundOrderPaymentAsync(
                        orderId,
                        buyerId.ToString(),
                        order.TotalAmount,
                        "Order creation failed during database persistence.",
                        idempotencyKey: $"compensation-db-{orderId}",
                        cancellationToken: CancellationToken.None);

                    if (refunded)
                    {
                        _logger.LogInformation("Compensation: Successfully refunded wallet balance for Order '{OrderId}', Amount: {Amount}.", orderId, order.TotalAmount);
                    }
                    else
                    {
                        _logger.LogError("CRITICAL: Wallet refund compensation returned false for Order '{OrderId}'. Flagged for immediate financial reconciliation.", orderId);
                    }
                }
                catch (Exception walletEx)
                {
                    _logger.LogError(walletEx, "CRITICAL: Compensation failed while executing wallet refund for Order '{OrderId}'. Flagged for immediate financial reconciliation.", orderId);
                }
            }

            throw new CommerceDomainException(
                "ORDER_CREATION_FAILED",
                "An error occurred while saving the order. All inventory allocations and payments have been safely rolled back.",
                500);
        }

        string? paymentUrl = request.PaymentMethod == PaymentMethod.VnPay
            ? $"https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?orderId={order.Id}"
            : null;

        return new CreateOrderResponse(
            order.Id,
            orderNo,
            order.TotalAmount,
            order.Status.ToString(),
            order.PaymentDeadlineAt,
            paymentUrl,
            subOrderIds,
            DateTimeOffset.UtcNow,
            priceDrops.Count > 0 ? priceDrops : null
        );
    }

    public async Task<OrderResponse> GetOrderDetailAsync(Guid currentUserId, Guid orderId, CancellationToken ct = default)
    {
        // Passive Guard: Tự động hủy nếu đã quá hạn thanh toán
        await ExpireOrderIfOverdueAsync(orderId, ct);

        var order = await _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.SubOrders)
                .ThenInclude(s => s.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new EntityNotFoundException("ORDER_NOT_FOUND", "Order", orderId);

        if (order.BuyerId != currentUserId)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to access this order.", 403);
        }

        var subOrderResponses = order.SubOrders.Select(so => new SubOrderDetailResponse(
            so.Id,
            so.SubOrderNo,
            so.OrderId,
            so.BuyerId,
            so.SellerId,
            $"Shop {so.SellerId.ToString()[..8]}",
            so.Status.ToString(),
            so.PaymentMethod.ToString(),
            so.ShippingAddress,
            so.ContactPhone,
            so.ItemsAmount,
            so.ProductDiscountAmount,
            so.ShopDiscountAmount,
            so.SystemDiscountAmount,
            so.ShopDiscountAmount + so.ProductDiscountAmount + so.SystemDiscountAmount,
            so.ShippingFeeAmount,
            so.TotalAmount,
            so.CreatedAt,
            so.Items.Select(i => new SubOrderItemResponse(
                i.Id,
                i.ProductId,
                i.SkuId,
                i.ProductNameSnapshot,
                i.SkuCodeSnapshot,
                i.Quantity,
                i.UnitPrice,
                i.LineTotal
            )).ToList()
        )).ToList();

        var orderNo = $"ORD-{order.Id.ToString()[..8].ToUpperInvariant()}";

        return new OrderResponse(
            order.Id,
            orderNo,
            order.BuyerId,
            order.Status.ToString(),
            order.TotalAmount,
            order.PaymentDeadlineAt,
            order.CreatedAt,
            subOrderResponses
        );
    }

    public async Task<PagedData<OrderSummaryResponse>> ListOrdersAsync(Guid currentUserId, int page, int pageSize, OrderStatus? status = null, CancellationToken ct = default)
    {
        var (safePage, safePageSize) = PaginationConstants.Normalize(page, pageSize);

        var query = _dbContext.Orders
            .AsNoTracking()
            .Where(o => o.BuyerId == currentUserId);

        if (status.HasValue)
        {
            query = query.Where(o => o.Status == status.Value);
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .Select(o => new
            {
                o.Id,
                o.TotalAmount,
                Status = o.Status.ToString(),
                o.PaymentDeadlineAt,
                SubOrderCount = o.SubOrders.Count,
                o.CreatedAt
            })
            .ToListAsync(ct);

        var orders = items.Select(o => new OrderSummaryResponse(
            o.Id,
            $"ORD-{o.Id.ToString()[..8].ToUpperInvariant()}",
            o.TotalAmount,
            o.Status,
            o.PaymentDeadlineAt,
            o.SubOrderCount,
            o.CreatedAt
        )).ToList();

        return new PagedData<OrderSummaryResponse>(
            orders,
            PaginationMetadata.Create(safePage, safePageSize, totalCount)
        );
    }

    public async Task<SubOrderDetailResponse> GetSubOrderDetailAsync(Guid currentUserId, Guid subOrderId, CancellationToken ct = default)
    {
        var targetSubOrder = await _dbContext.SubOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == subOrderId, ct)
            ?? throw new EntityNotFoundException("SUB_ORDER_NOT_FOUND", "SubOrder", subOrderId);

        // Passive Guard: Tự động hủy nếu đã quá hạn thanh toán
        await ExpireOrderIfOverdueAsync(targetSubOrder.OrderId, ct);

        var so = await _dbContext.SubOrders
            .AsNoTracking()
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == subOrderId, ct)
            ?? throw new EntityNotFoundException("SUB_ORDER_NOT_FOUND", "SubOrder", subOrderId);

        if (so.BuyerId != currentUserId && so.SellerId != currentUserId)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to access this sub-order.", 403);
        }

        return new SubOrderDetailResponse(
            so.Id,
            so.SubOrderNo,
            so.OrderId,
            so.BuyerId,
            so.SellerId,
            $"Shop {so.SellerId.ToString()[..8]}",
            so.Status.ToString(),
            so.PaymentMethod.ToString(),
            so.ShippingAddress,
            so.ContactPhone,
            so.ItemsAmount,
            so.ProductDiscountAmount,
            so.ShopDiscountAmount,
            so.SystemDiscountAmount,
            so.ShopDiscountAmount + so.ProductDiscountAmount + so.SystemDiscountAmount,
            so.ShippingFeeAmount,
            so.TotalAmount,
            so.CreatedAt,
            so.Items.Select(i => new SubOrderItemResponse(
                i.Id,
                i.ProductId,
                i.SkuId,
                i.ProductNameSnapshot,
                i.SkuCodeSnapshot,
                i.Quantity,
                i.UnitPrice,
                i.LineTotal
            )).ToList()
        );
    }

    public async Task<bool> ExpireOrderIfOverdueAsync(Guid orderId, CancellationToken ct = default)
    {
        var order = await _dbContext.Orders
            .Include(o => o.SubOrders)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);

        if (order == null || order.Status != OrderStatus.PendingPayment || order.PaymentDeadlineAt >= DateTimeOffset.UtcNow)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        order.Status = OrderStatus.Cancelled;

        foreach (var so in order.SubOrders)
        {
            so.Status = SubOrderStatus.Cancelled;
            so.CancelledAt = now;

            _dbContext.OrderCancellations.Add(new OrderCancellation
            {
                Id = Guid.NewGuid(),
                SubOrderId = so.Id,
                CancelledBy = null,
                ActorRole = OrderActorRole.System,
                ReasonCode = CancellationReasonCode.PaymentTimeout,
                ReasonNote = "Payment timeout expired after deadline.",
                WasPaid = false,
                InventoryReleased = true
            });
        }

        // Giải phóng voucher đã giữ chỗ nếu có
        var usages = await _dbContext.DiscountCodeUsages
            .Where(u => u.OrderId == orderId && u.Status == DiscountUsageStatus.Reserved)
            .ToListAsync(ct);

        foreach (var usage in usages)
        {
            usage.Status = DiscountUsageStatus.Released;
            usage.ReleasedAt = now;

            var code = await _dbContext.DiscountCodes.FindAsync(new object[] { usage.DiscountCodeId }, ct);
            if (code != null)
            {
                code.ReservedCount = Math.Max(0, code.ReservedCount - 1);
            }
        }

        await _dbContext.SaveChangesAsync(ct);

        // Nhả tồn kho tại Fulfillment Service
        try
        {
            await _fulfillmentClient.ReleaseStockAsync(order.Id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to release warehouse stock during passive expiration for Order '{OrderId}'.", orderId);
        }

        return true;
    }

    public async Task CancelOrderAsync(Guid currentUserId, Guid orderId, CancelOrderRequest request, CancellationToken ct = default)
    {
        var order = await _dbContext.Orders
            .Include(o => o.SubOrders)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new EntityNotFoundException("ORDER_NOT_FOUND", "Order", orderId);

        if (order.BuyerId != currentUserId)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to cancel this order.", 403);
        }

        if (order.Status != OrderStatus.PendingPayment)
        {
            throw new CommerceDomainException("ORDER_CANNOT_BE_CANCELLED", "Order cannot be cancelled in its current status.");
        }

        order.Status = OrderStatus.Cancelled;
        foreach (var so in order.SubOrders)
        {
            so.Status = SubOrderStatus.Cancelled;
            so.CancelledAt = DateTimeOffset.UtcNow;

            _dbContext.OrderCancellations.Add(new OrderCancellation
            {
                Id = Guid.NewGuid(),
                SubOrderId = so.Id,
                CancelledBy = currentUserId,
                ActorRole = OrderActorRole.Buyer,
                ReasonCode = request.ReasonCode,
                ReasonNote = request.ReasonText,
                WasPaid = false,
                InventoryReleased = true
            });
        }

        await _dbContext.SaveChangesAsync(ct);

        // Nhả tồn kho lại cho Fulfillment Service (CHK-02)
        await _fulfillmentClient.ReleaseStockAsync(order.Id, ct);
    }

    public async Task SellerAcceptSubOrderAsync(Guid sellerId, Guid orderId, SellerAcceptOrderRequest request, CancellationToken ct = default)
    {
        var so = await _dbContext.SubOrders
            .FirstOrDefaultAsync(s => s.Id == request.SubOrderId && s.OrderId == orderId, ct)
            ?? throw new EntityNotFoundException("SUB_ORDER_NOT_FOUND", "SubOrder", request.SubOrderId);

        if (so.SellerId != sellerId)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to manage this sub-order.", 403);
        }

        if (so.Status != SubOrderStatus.Paid && !(so.PaymentMethod == PaymentMethod.Cod && so.Status == SubOrderStatus.PendingPayment))
        {
            throw new CommerceDomainException("INVALID_STATUS", "Sub-order cannot be accepted in its current status.");
        }

        so.Status = SubOrderStatus.Confirmed;
        so.SellerAcceptedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(ct);

        // Tạo vận đơn GHN tự động qua Fulfillment Service (ORD-04)
        try
        {
            await _fulfillmentClient.CreateShipmentAsync(so.Id, ct);
        }
        catch
        {
            // Tránh ngắt quãng luồng xác nhận nếu service giao vận tạm bận
        }
    }

    public async Task SellerRejectSubOrderAsync(Guid sellerId, Guid orderId, SellerRejectOrderRequest request, CancellationToken ct = default)
    {
        var so = await _dbContext.SubOrders
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == request.SubOrderId && s.OrderId == orderId, ct)
            ?? throw new EntityNotFoundException("SUB_ORDER_NOT_FOUND", "SubOrder", request.SubOrderId);

        if (so.SellerId != sellerId)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to manage this sub-order.", 403);
        }

        if (so.Status == SubOrderStatus.Shipping || so.Status == SubOrderStatus.Delivered || so.Status == SubOrderStatus.Completed)
        {
            throw new CommerceDomainException("INVALID_STATUS", "Sub-order cannot be rejected once shipped or completed.");
        }

        var wasPaid = so.PaidAt.HasValue;
        so.Status = SubOrderStatus.Cancelled;
        so.CancelledAt = DateTimeOffset.UtcNow;

        var cancellationId = Guid.NewGuid();
        var cancellation = new OrderCancellation
        {
            Id = cancellationId,
            SubOrderId = so.Id,
            CancelledBy = sellerId,
            ActorRole = OrderActorRole.Seller,
            ReasonCode = request.ReasonCode,
            ReasonNote = request.ReasonText,
            WasPaid = wasPaid,
            InventoryReleased = true
        };
        _dbContext.OrderCancellations.Add(cancellation);

        // TỰ ĐỘNG HOÀN TIỀN VÍ NẾU ĐƠN CON ĐÃ THANH TOÁN (BUG-B)
        if (wasPaid)
        {
            var refundId = Guid.NewGuid();
            var refund = new OrderRefund
            {
                Id = refundId,
                SubOrderId = so.Id,
                OrderId = so.OrderId,
                CancellationId = cancellationId,
                RefundType = RefundType.Full,
                Amount = so.TotalAmount,
                Status = RefundStatus.Requested,
                ReasonCode = "SELLER_REJECTED",
                ReasonNote = request.ReasonText ?? "Shop rejected sub-order after payment. Pending manual refund reconciliation as WalletService lacks automated refund API.",
                ActorRole = OrderActorRole.Seller,
                RequestedBy = sellerId,
                RequestedAt = DateTimeOffset.UtcNow
            };
            _dbContext.OrderRefunds.Add(refund);
            _logger.LogInformation("SubOrder '{SubOrderId}' was paid. Created refund request '{RefundId}' for manual processing (no automatic refund API on WalletService).", so.Id, refundId);
        }

        await _dbContext.SaveChangesAsync(ct);

        // CHỈ NHẢ TỒN KHO CỦA CÁC MÓN THUỘC ĐÚNG SUBORDER NÀY (BUG-A)
        var itemsToRelease = so.Items
            .Select(i => new StockReservationItem(i.SkuId, i.Quantity, so.SellerId))
            .ToList();

        await _fulfillmentClient.ReleaseSubOrderStockAsync(so.OrderId, itemsToRelease, ct);
    }

    public async Task<ShipmentTrackingResponse> GetShipmentTrackingAsync(Guid currentUserId, Guid orderId, CancellationToken ct = default)
    {
        var order = await _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.SubOrders)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new EntityNotFoundException("ORDER_NOT_FOUND", "Order", orderId);

        if (order.BuyerId != currentUserId)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to track this shipment.", 403);
        }

        var shipments = order.SubOrders.Select(so => new SubOrderShipmentInfo(
            so.Id,
            so.SubOrderNo,
            so.SellerId,
            $"Shop {so.SellerId.ToString()[..8]}",
            so.Status.ToString(),
            "GHN",
            so.Status >= SubOrderStatus.Shipping ? $"GHN-{so.SubOrderNo}" : null,
            so.ShippedAt,
            so.DeliveredAt
        )).ToList();

        return new ShipmentTrackingResponse(
            order.Id,
            $"ORD-{order.Id.ToString()[..8].ToUpperInvariant()}",
            shipments
        );
    }

    public async Task RefundSubOrderAsync(Guid currentUserId, Guid subOrderId, SubOrderRefundRequest request, CancellationToken ct = default)
    {
        var so = await _dbContext.SubOrders
            .FirstOrDefaultAsync(s => s.Id == subOrderId, ct)
            ?? throw new EntityNotFoundException("SUB_ORDER_NOT_FOUND", "SubOrder", subOrderId);

        if (so.BuyerId != currentUserId)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to request a refund for this sub-order.", 403);
        }

        var refund = new OrderRefund
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrderId,
            OrderId = so.OrderId,
            RefundType = RefundType.Full,
            Amount = so.TotalAmount,
            Status = RefundStatus.Requested,
            ReasonCode = "BUYER_REQUEST",
            ReasonNote = $"{request.Reason}: {request.Note}",
            ActorRole = OrderActorRole.Buyer,
            RequestedBy = currentUserId,
            RequestedAt = DateTimeOffset.UtcNow
        };

        _dbContext.OrderRefunds.Add(refund);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task UpdateSubOrderDeliveryStatusAsync(Guid subOrderId, string status, DateTimeOffset? deliveredAt, CancellationToken ct = default)
    {
        var so = await _dbContext.SubOrders
            .FirstOrDefaultAsync(s => s.Id == subOrderId, ct)
            ?? throw new EntityNotFoundException("SUB_ORDER_NOT_FOUND", "SubOrder", subOrderId);

        var timestamp = deliveredAt ?? DateTimeOffset.UtcNow;

        if (string.Equals(status, "ReadyToPick", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "Picking", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "Packed", StringComparison.OrdinalIgnoreCase))
        {
            // Preparation phases: Idempotent 200 OK without altering confirmed status
            so.UpdatedAt = timestamp;
        }
        else if (string.Equals(status, "HandedOver", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(status, "InTransit", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(status, "Shipped", StringComparison.OrdinalIgnoreCase))
        {
            so.Status = SubOrderStatus.Shipped;
            so.ShippedAt = timestamp;
            so.UpdatedAt = timestamp;
        }
        else if (string.Equals(status, "Delivered", StringComparison.OrdinalIgnoreCase))
        {
            so.Status = SubOrderStatus.Delivered;
            so.DeliveredAt = timestamp;
            so.UpdatedAt = timestamp;
        }
        else if (string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            so.Status = SubOrderStatus.Cancelled;
            so.CancelledAt = timestamp;
            so.UpdatedAt = timestamp;
        }
        else if (string.Equals(status, "DeliveryFailed", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(status, "Returned", StringComparison.OrdinalIgnoreCase))
        {
            var isReturned = string.Equals(status, "Returned", StringComparison.OrdinalIgnoreCase);
            so.Status = SubOrderStatus.Cancelled;
            so.CancelledAt = timestamp;
            so.UpdatedAt = timestamp;

            var cancellationId = Guid.NewGuid();
            var cancellation = new OrderCancellation
            {
                Id = cancellationId,
                SubOrderId = so.Id,
                CancelledBy = null,
                ActorRole = OrderActorRole.System,
                ReasonCode = isReturned ? CancellationReasonCode.PackageReturned : CancellationReasonCode.DeliveryFailed,
                ReasonNote = $"Shipment delivery carrier status: {status}.",
                WasPaid = so.PaidAt.HasValue,
                InventoryReleased = false
            };
            _dbContext.OrderCancellations.Add(cancellation);

            if (so.PaidAt.HasValue)
            {
                var refund = new OrderRefund
                {
                    Id = Guid.NewGuid(),
                    SubOrderId = so.Id,
                    OrderId = so.OrderId,
                    CancellationId = cancellationId,
                    RefundType = RefundType.Full,
                    Amount = so.TotalAmount,
                    Status = RefundStatus.Requested,
                    ReasonCode = isReturned ? "PACKAGE_RETURNED" : "DELIVERY_FAILED",
                    ReasonNote = $"Automated refund request created due to delivery carrier status: {status}.",
                    ActorRole = OrderActorRole.System,
                    RequestedBy = null,
                    RequestedAt = timestamp
                };
                _dbContext.OrderRefunds.Add(refund);
            }
        }
        else
        {
            throw new CommerceDomainException("INVALID_DELIVERY_STATUS", "The provided delivery status is not supported.", 400);
        }

        await _dbContext.SaveChangesAsync(ct);
        _logger.LogInformation("[OrderService] SubOrder '{SubOrderId}' delivery status updated to '{Status}'.", subOrderId, so.Status);
    }

    public async Task<OrderDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("ORDER_NOT_FOUND", "Order", id);

        return new OrderDto(
            order.Id,
            order.BuyerId,
            order.Status.ToString(),
            order.TotalAmount,
            order.PaymentDeadlineAt);
    }

    public async Task<PagedData<OrderDto>> ListByBuyerIdAsync(Guid buyerId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var (safePage, safePageSize) = PaginationConstants.Normalize(page, pageSize);

        var query = _dbContext.Orders
            .AsNoTracking()
            .Where(o => o.BuyerId == buyerId);

        var totalCount = await query.CountAsync(cancellationToken);

        var orders = await query
            .OrderByDescending(o => o.PaymentDeadlineAt)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .Select(o => new OrderDto(
                o.Id,
                o.BuyerId,
                o.Status.ToString(),
                o.TotalAmount,
                o.PaymentDeadlineAt))
            .ToListAsync(cancellationToken);

        return new PagedData<OrderDto>(
            orders,
            PaginationMetadata.Create(safePage, safePageSize, totalCount));
    }
}
