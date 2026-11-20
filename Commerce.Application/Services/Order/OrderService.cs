namespace Commerce.Application.Services.Order;

using Commerce.Application.Services.Shipment;
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
using Commerce.Application.Services.Discount;
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
    private readonly IUserClient _userClient;
    private readonly IDistributedCache _cache;
    private readonly ILogger<OrderService> _logger;
    private readonly IVoucherDiscountCalculator? _voucherCalculator;
    private readonly IVoucherLifecycleService? _voucherLifecycleService;
    private readonly INotificationSettings? _notificationSettings;

    public OrderService(
        ICommerceDbContext dbContext,
        IWalletClient walletClient,
        IFulfillmentClient fulfillmentClient,
        ICatalogClient catalogClient,
        IDistributedCache cache,
        ILogger<OrderService> logger)
        : this(dbContext, walletClient, fulfillmentClient, catalogClient, null!, cache, logger, null, null, null)
    {
    }

    public OrderService(
        ICommerceDbContext dbContext,
        IWalletClient walletClient,
        IFulfillmentClient fulfillmentClient,
        ICatalogClient catalogClient,
        IUserClient userClient,
        IDistributedCache cache,
        ILogger<OrderService> logger,
        IVoucherDiscountCalculator? voucherCalculator = null,
        IVoucherLifecycleService? voucherLifecycleService = null,
        INotificationSettings? notificationSettings = null)
    {
        _notificationSettings = notificationSettings;
        _dbContext = dbContext;
        _walletClient = walletClient;
        _fulfillmentClient = fulfillmentClient;
        _catalogClient = catalogClient;
        _userClient = userClient;
        _cache = cache;
        _logger = logger;
        _voucherCalculator = voucherCalculator;
        _voucherLifecycleService = voucherLifecycleService;
    }

    private async Task<decimal> GetActivePlatformMarkupPercentAsync(CancellationToken ct)
    {
        try
        {
            var cachedVal = await _cache.GetStringAsync("commerce:platform_fee_percent", ct);
            if (!string.IsNullOrEmpty(cachedVal) && decimal.TryParse(cachedVal, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            var cachedMetadata = await _cache.GetStringAsync("commerce:platform_fee:metadata", ct);
            if (!string.IsNullOrEmpty(cachedMetadata))
            {
                var response = JsonSerializer.Deserialize<PlatformFeeResponse>(cachedMetadata);
                if (response != null)
                {
                    return response.DefaultPercent;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve platform fee percent from cache. Using fallback default.");
        }

        return 0.0m;
    }

    private async Task<(ShippingAddressInput Address, string ContactPhone)> ResolveShippingAddressAsync(
        Guid buyerId,
        Guid? addressId,
        ShippingAddressInput? rawAddress,
        string? contactPhone,
        CancellationToken ct)
    {
        if (addressId.HasValue && addressId.Value != Guid.Empty)
        {
            var address = await _userClient.GetBuyerAddressAsync(buyerId, addressId.Value, ct);
            if (address == null)
            {
                throw new CommerceDomainException("ADDRESS_NOT_FOUND", "Shipping address not found in address book.", 404);
            }

            if (address.GhnDistrictId <= 0 || string.IsNullOrWhiteSpace(address.GhnWardCode))
            {
                throw new CommerceDomainException("INVALID_SHIPPING_ADDRESS", "Shipping address must contain valid GHN district and ward codes for carrier calculation.", 400);
            }

            var phone = !string.IsNullOrWhiteSpace(contactPhone) ? contactPhone : address.Phone;
            var resolved = new ShippingAddressInput(
                ReceiverName: address.RecipientName,
                ContactPhone: phone,
                Street: address.Street,
                Ward: address.Ward,
                District: address.District,
                Province: address.Province,
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

            return (rawAddress with { ContactPhone = phone }, phone);
        }

        var defaultAddr = await _userClient.GetBuyerDefaultAddressAsync(buyerId, ct);
        if (defaultAddr == null)
        {
            throw new CommerceDomainException("ADDRESS_REQUIRED", "No shipping address provided and buyer has no default address.", 400);
        }

        if (defaultAddr.GhnDistrictId <= 0 || string.IsNullOrWhiteSpace(defaultAddr.GhnWardCode))
        {
            throw new CommerceDomainException("INVALID_SHIPPING_ADDRESS", "Shipping address must contain valid GHN district and ward codes for carrier calculation.", 400);
        }

        var defaultPhone = !string.IsNullOrWhiteSpace(contactPhone) ? contactPhone : defaultAddr.Phone;
        var defaultResolved = new ShippingAddressInput(
            ReceiverName: defaultAddr.RecipientName,
            ContactPhone: defaultPhone,
            Street: defaultAddr.Street,
            Ward: defaultAddr.Ward,
            District: defaultAddr.District,
            Province: defaultAddr.Province,
            GhnDistrictId: defaultAddr.GhnDistrictId,
            GhnWardCode: defaultAddr.GhnWardCode
        );
        return (defaultResolved, defaultPhone);
    }

    /// <summary>
    /// Calculates chargeable weight according to the official Giao Hàng Nhanh (GHN) carrier formula:
    /// VolumetricWeight (g) = (LengthCm * WidthCm * HeightCm / 5000) * 1000 = (LengthCm * WidthCm * HeightCm) / 5
    /// ChargeableWeight = Max(ActualWeightGrams, VolumetricWeight)
    /// </summary>
    public static int CalculateGhnChargeableWeight(int weightGrams, int lengthCm, int widthCm, int heightCm)
    {
        int actualWeight = Math.Max(0, weightGrams);
        int volumetricWeight = 0;
        if (lengthCm > 0 && widthCm > 0 && heightCm > 0)
        {
            volumetricWeight = (int)Math.Ceiling((double)(lengthCm * widthCm * heightCm) / 5.0);
        }

        int chargeableWeight = Math.Max(actualWeight, volumetricWeight);
        return chargeableWeight > 0 ? chargeableWeight : 200;
    }

    public async Task<CheckoutQuoteResponse> QuoteCheckoutAsync(Guid buyerId, CheckoutQuoteRequest request, CancellationToken ct = default)
    {
        if (request.CartItemIds == null || request.CartItemIds.Count == 0)
        {
            throw new CommerceDomainException("CART_EMPTY", "No valid items selected for checkout.");
        }

        var (resolvedAddress, resolvedPhone) = await ResolveShippingAddressAsync(buyerId, request.AddressId, request.ShippingAddress, request.ContactPhone, ct);

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
        var platformMarkupPercent = await GetActivePlatformMarkupPercentAsync(ct);

        // Batch-fetch SKU packaging dimensions từ Catalog Service để tính cước thể tích chuẩn GHN (034)
        var allSkuIds = selectedItems.Select(i => i.SkuId).Distinct().ToList();
        IReadOnlyDictionary<Guid, CatalogProductCheckResult>? batchSkuDetails = null;
        if (_catalogClient != null && allSkuIds.Count > 0)
        {
            try
            {
                batchSkuDetails = await _catalogClient.GetSkuBatchDetailsAsync(allSkuIds, ct);
            }
            catch (Exception catEx)
            {
                _logger.LogWarning(catEx, "Failed to retrieve real-time variant dimensions from Catalog Service for checkout quote. Applying fallback weights.");
            }
        }
        batchSkuDetails ??= new Dictionary<Guid, CatalogProductCheckResult>();

        var sellerItemsAmounts = new Dictionary<Guid, decimal>();
        var sellerShippingFees = new Dictionary<Guid, decimal>();
        var preQuotes = new List<(Guid SellerId, List<ShopQuoteItem> ShopItems, decimal ItemsAmount, decimal ShippingFee)>();

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

            // Tính cước GHN động theo từng Shop qua Fulfillment Service chuẩn hóa theo kích thước thật (CHK-01 / T027 / 034)
            decimal shippingFeeAmount = 30000m;
            var quoteItems = shopItems.Select(i =>
            {
                int unitChargeableWeight = 200;
                if (batchSkuDetails != null && batchSkuDetails.TryGetValue(i.SkuId, out var skuInfo) && skuInfo != null)
                {
                    unitChargeableWeight = CalculateGhnChargeableWeight(skuInfo.WeightGrams, skuInfo.LengthCm, skuInfo.WidthCm, skuInfo.HeightCm);
                }
                return new ShippingQuoteItem(i.SkuId, i.Quantity, unitChargeableWeight);
            }).ToList();

            var ghnDistrictId = resolvedAddress.GhnDistrictId!.Value;
            var ghnWardCode = resolvedAddress.GhnWardCode!;

            try
            {
                var shippingQuoteRequest = new ShopShippingQuoteRequest(sellerId, ghnDistrictId, ghnWardCode, quoteItems);
                shippingFeeAmount = await _fulfillmentClient.CalculateShopShippingFeeAsync(shippingQuoteRequest, ct);
            }
            catch (CommerceDomainException ex) when (ex.StatusCode == 400)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to calculate dynamic carrier shipping fee for Shop '{SellerId}'. Falling back to default rate.", sellerId);
                shippingFeeAmount = 30000m;
            }

            sellerItemsAmounts[sellerId] = itemsAmount;
            sellerShippingFees[sellerId] = shippingFeeAmount;
            preQuotes.Add((sellerId, shopItems, itemsAmount, shippingFeeAmount));
        }

        VoucherCalculationResult? voucherResult = null;
        if (_voucherCalculator != null && request.VoucherCodes != null && request.VoucherCodes.Count > 0)
        {
            try
            {
                var calculationItems = preQuotes.SelectMany(pq => pq.ShopItems.Select(item => new CalculationItemInput(
                    CartItemId: Guid.Empty,
                    ProductId: item.ProductId,
                    SkuId: item.SkuId,
                    SellerId: pq.SellerId,
                    ProductName: item.ProductName,
                    Quantity: item.Quantity,
                    UnitPrice: item.UnitPrice
                ))).ToList();

                var vCalcRequest = new VoucherCalculationRequest(
                    buyerId,
                    request.VoucherCodes,
                    sellerItemsAmounts,
                    sellerShippingFees,
                    calculationItems);

                voucherResult = await _voucherCalculator.CalculateDiscountsAsync(vCalcRequest, ct);
            }
            catch (CommerceDomainException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error executing voucher discount calculation for Buyer '{BuyerId}'.", buyerId);
            }
        }

        foreach (var pq in preQuotes)
        {
            decimal productDiscount = 0m;
            decimal shopDiscount = 0m;
            decimal systemDiscount = 0m;

            if (voucherResult != null && voucherResult.ShopDiscounts.TryGetValue(pq.SellerId, out var breakdown))
            {
                productDiscount = breakdown.ProductDiscountAmount;
                shopDiscount = breakdown.ShopDiscountAmount;
                systemDiscount = breakdown.SystemDiscountAmount;
            }

            decimal discountAmount = productDiscount + shopDiscount + systemDiscount;
            decimal netSeller = Math.Max(0m, pq.ItemsAmount - productDiscount - shopDiscount);
            decimal platformFeeAmount = Math.Round(netSeller * (platformMarkupPercent / 100m), 0, MidpointRounding.AwayFromZero);
            decimal sellerReceiveAmount = Math.Max(0m, netSeller - platformFeeAmount);
            decimal totalAmount = Math.Max(0m, netSeller - systemDiscount) + pq.ShippingFee;

            shopQuotes.Add(new ShopOrderQuote(
                pq.SellerId,
                $"Shop {pq.SellerId.ToString()[..8]}",
                pq.ShopItems,
                pq.ItemsAmount,
                productDiscount,
                shopDiscount,
                systemDiscount,
                discountAmount,
                pq.ShippingFee,
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

        var appliedVouchers = voucherResult?.AppliedVouchers != null && voucherResult.AppliedVouchers.Count > 0
            ? voucherResult.AppliedVouchers.ToList()
            : request.VoucherCodes ?? new List<string>();

        var warnings = voucherResult?.Warnings != null && voucherResult.Warnings.Count > 0
            ? voucherResult.Warnings.ToList()
            : (voucherResult?.RejectedVouchers != null && voucherResult.RejectedVouchers.Count > 0
                ? voucherResult.RejectedVouchers.Select(v => $"Voucher '{v}' could not be applied.").ToList()
                : null);

        var quoteResponse = new CheckoutQuoteResponse(
            quoteId,
            grandTotal,
            walletBalance,
            shopQuotes,
            appliedVouchers,
            warnings,
            expiredAt
        );

        // Lưu vào Distributed Cache (Redis / Memory) với TTL 15 phút (ISSUE-01)
        var quoteKey = $"checkout_quote:{quoteId}";
        var effectiveRequest = request with
        {
            ShippingAddress = resolvedAddress,
            ContactPhone = resolvedPhone
        };

        var quotePayload = JsonSerializer.Serialize(new CachedQuote
        {
            QuoteId = quoteId,
            BuyerId = buyerId,
            Request = effectiveRequest,
            Response = quoteResponse,
            ExpiredAt = expiredAt
        });

        await _cache.SetStringAsync(quoteKey, quotePayload, new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = expiredAt
        }, ct);

        return quoteResponse;
    }

    public async Task<CheckoutQuoteResponse> RecalculateQuoteAsync(Guid buyerId, Guid quoteId, RecalculateQuoteRequest request, CancellationToken ct = default)
    {
        var quoteKey = $"checkout_quote:{quoteId}";
        var quoteJson = await _cache.GetStringAsync(quoteKey, ct);
        if (string.IsNullOrWhiteSpace(quoteJson))
        {
            throw new CommerceDomainException("QUOTE_NOT_FOUND", "Checkout quote not found or expired.", 404);
        }

        var cachedQuote = JsonSerializer.Deserialize<CachedQuote>(quoteJson);
        if (cachedQuote == null || cachedQuote.ExpiredAt < DateTimeOffset.UtcNow)
        {
            throw new CommerceDomainException("QUOTE_NOT_FOUND", "Checkout quote not found or expired.", 404);
        }

        if (cachedQuote.BuyerId != buyerId)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to access this checkout quote.", 403);
        }

        var (resolvedAddress, resolvedPhone) = await ResolveShippingAddressAsync(buyerId, request.AddressId, request.ShippingAddress, request.ContactPhone, ct);

        var updatedShops = new List<ShopOrderQuote>();
        foreach (var shop in cachedQuote.Response.Shops)
        {
            var quoteItems = shop.Items.Select(i => new ShippingQuoteItem(
                i.SkuId,
                i.Quantity,
                Math.Max(100, i.Quantity * 200)
            )).ToList();

            decimal shippingFeeAmount = 30000m;
            try
            {
                var shippingQuoteRequest = new ShopShippingQuoteRequest(
                    shop.SellerId,
                    resolvedAddress.GhnDistrictId!.Value,
                    resolvedAddress.GhnWardCode!,
                    quoteItems);
                shippingFeeAmount = await _fulfillmentClient.CalculateShopShippingFeeAsync(shippingQuoteRequest, ct);
            }
            catch (CommerceDomainException ex) when (ex.StatusCode == 400)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to recalculate carrier shipping fee for Shop '{SellerId}'. Falling back to default rate.", shop.SellerId);
                shippingFeeAmount = 30000m;
            }

            decimal netSeller = Math.Max(0m, shop.ItemsAmount - shop.ProductDiscountAmount - shop.ShopDiscountAmount);
            decimal totalAmount = Math.Max(0m, netSeller - shop.SystemDiscountAmount) + shippingFeeAmount;

            updatedShops.Add(new ShopOrderQuote(
                shop.SellerId,
                shop.ShopName,
                shop.Items,
                shop.ItemsAmount,
                shop.ProductDiscountAmount,
                shop.ShopDiscountAmount,
                shop.SystemDiscountAmount,
                shop.DiscountAmount,
                shippingFeeAmount,
                totalAmount
            ));
        }

        decimal grandTotal = updatedShops.Sum(s => s.TotalAmount);
        decimal walletBalance = cachedQuote.Response.WalletBalance;
        try
        {
            walletBalance = await _walletClient.GetWalletBalanceAsync(buyerId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh wallet balance for Buyer '{BuyerId}'.", buyerId);
        }

        var updatedResponse = new CheckoutQuoteResponse(
            quoteId,
            grandTotal,
            walletBalance,
            updatedShops,
            cachedQuote.Response.AppliedVouchers,
            cachedQuote.Response.Warnings,
            cachedQuote.ExpiredAt
        );

        var effectiveRequest = cachedQuote.Request with
        {
            ShippingAddress = resolvedAddress,
            ContactPhone = resolvedPhone,
            AddressId = request.AddressId ?? cachedQuote.Request.AddressId
        };

        var updatedCachedQuote = new CachedQuote
        {
            QuoteId = quoteId,
            BuyerId = buyerId,
            Request = effectiveRequest,
            Response = updatedResponse,
            ExpiredAt = cachedQuote.ExpiredAt
        };

        await _cache.SetStringAsync(quoteKey, JsonSerializer.Serialize(updatedCachedQuote), new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = cachedQuote.ExpiredAt
        }, ct);

        return updatedResponse;
    }

    public async Task<DisputeEligibilityDto?> GetDisputeEligibilityAsync(Guid orderOrSubOrderId, Guid reporterId, Guid reportedUserId, CancellationToken ct = default)
    {
        var subOrder = await _dbContext.SubOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == orderOrSubOrderId, ct);
        if (subOrder != null)
        {
            bool matchesDirect = subOrder.BuyerId == reporterId && subOrder.SellerId == reportedUserId;
            bool matchesReverse = subOrder.SellerId == reporterId && subOrder.BuyerId == reportedUserId;
            if (!matchesDirect && !matchesReverse)
            {
                return null;
            }

            bool isEligible = subOrder.Status == SubOrderStatus.Delivered || subOrder.Status == SubOrderStatus.Completed;
            return new DisputeEligibilityDto(
                IsEligible: isEligible,
                OrderType: "SubOrder",
                BuyerId: subOrder.BuyerId,
                SellerId: subOrder.SellerId,
                Status: subOrder.Status.ToString(),
                DeliveredAt: subOrder.DeliveredAt
            );
        }

        var order = await _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.SubOrders)
            .FirstOrDefaultAsync(o => o.Id == orderOrSubOrderId, ct);
        if (order != null)
        {
            var matchingSubOrder = order.SubOrders.FirstOrDefault(s =>
                (s.BuyerId == reporterId && s.SellerId == reportedUserId) ||
                (s.SellerId == reporterId && s.BuyerId == reportedUserId));

            if (matchingSubOrder == null)
            {
                return null;
            }

            bool isEligible = matchingSubOrder.Status == SubOrderStatus.Delivered || matchingSubOrder.Status == SubOrderStatus.Completed;
            return new DisputeEligibilityDto(
                IsEligible: isEligible,
                OrderType: "Order",
                BuyerId: matchingSubOrder.BuyerId,
                SellerId: matchingSubOrder.SellerId,
                Status: matchingSubOrder.Status.ToString(),
                DeliveredAt: matchingSubOrder.DeliveredAt
            );
        }

        var auctionOrder = await _dbContext.AuctionOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == orderOrSubOrderId, ct);
        if (auctionOrder != null)
        {
            bool matchesDirect = auctionOrder.BuyerId == reporterId && auctionOrder.SellerId == reportedUserId;
            bool matchesReverse = auctionOrder.SellerId == reporterId && auctionOrder.BuyerId == reportedUserId;
            if (!matchesDirect && !matchesReverse)
            {
                return null;
            }

            bool isEligible = auctionOrder.Status == SubOrderStatus.Delivered || auctionOrder.Status == SubOrderStatus.Completed;
            return new DisputeEligibilityDto(
                IsEligible: isEligible,
                OrderType: "AuctionOrder",
                BuyerId: auctionOrder.BuyerId,
                SellerId: auctionOrder.SellerId,
                Status: auctionOrder.Status.ToString(),
                DeliveredAt: auctionOrder.DeliveredAt
            );
        }

        return null;
    }

    public async Task<CreateOrderResponse> CreateOrderAsync(Guid buyerId, CreateOrderRequest request, CancellationToken ct = default)
    {
#pragma warning disable CS0618 // Suppress obsolete warning for decommissioned VNPay guard
        if (request.PaymentMethod == PaymentMethod.VnPay)
        {
            throw new CommerceDomainException(
                "UNSUPPORTED_PAYMENT_METHOD",
                "VNPay payment method is no longer supported. Please use Wallet or COD.",
                400);
        }
#pragma warning restore CS0618

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

        // THẨM ĐỊNH QUYỀN MUA HÀNG VỚI USER SERVICE (INT-01 / BUYER PERMISSION GATE)
        if (_userClient != null)
        {
            var permissionCheck = await _userClient.CheckBuyerPermissionAsync(buyerId, "buy", ct);
            if (!permissionCheck.IsAllowed)
            {
                throw new CommerceDomainException(
                    permissionCheck.ErrorCode ?? "BUYER_ACTION_RESTRICTED",
                    permissionCheck.Message ?? "User has an active restriction prohibiting purchase actions.",
                    403);
            }
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

        // TÁI TÍNH TOÁN VOUCHER TRÊN GIÁ MỚI & GIỮ CHỖ ATOMIC
        VoucherCalculationResult? recalculatedVouchers = null;
        var reservedUsageIds = new List<Guid>();

        if (_voucherCalculator != null && cachedQuote.Response.AppliedVouchers != null && cachedQuote.Response.AppliedVouchers.Count > 0)
        {
            try
            {
                var calcItems = allQuoteItems.Select(i => new CalculationItemInput(
                    CartItemId: Guid.Empty,
                    ProductId: i.ProductId,
                    SkuId: i.SkuId,
                    SellerId: cachedQuote.Response.Shops.First(s => s.Items.Any(si => si.SkuId == i.SkuId)).SellerId,
                    ProductName: i.ProductName,
                    Quantity: i.Quantity,
                    UnitPrice: freshSkuPrices.TryGetValue(i.SkuId, out var freshP) ? freshP : i.UnitPrice
                )).ToList();

                var shopAmounts = calcItems.GroupBy(ci => ci.SellerId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity * x.UnitPrice));
                var shopShipping = cachedQuote.Response.Shops.ToDictionary(s => s.SellerId, s => s.ShippingFeeAmount);

                var vReq = new VoucherCalculationRequest(
                    cachedQuote.BuyerId,
                    cachedQuote.Response.AppliedVouchers,
                    shopAmounts,
                    shopShipping,
                    calcItems
                );

                recalculatedVouchers = await _voucherCalculator.CalculateDiscountsAsync(vReq, ct);

                if (_voucherLifecycleService != null && recalculatedVouchers.AllocatedUsages != null && recalculatedVouchers.AllocatedUsages.Count > 0)
                {
                    var ids = await _voucherLifecycleService.ReserveVouchersAsync(buyerId, recalculatedVouchers.AllocatedUsages, ct);
                    reservedUsageIds.AddRange(ids);
                }
            }
            catch (CommerceDomainException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to re-evaluate vouchers during order creation for Buyer '{BuyerId}'.", buyerId);
            }
        }

        decimal grandTotal = 0m;
        var createdSubOrders = new List<SubOrder>();
        var platformMarkupPercent = await GetActivePlatformMarkupPercentAsync(ct);

        foreach (var shop in cachedQuote.Response.Shops)
        {
            var subOrderId = Guid.NewGuid();
            subOrderIds.Add(subOrderId);
            var subOrderNo = $"SUB-{DateTime.UtcNow:yyyyMMdd}-{subOrderId.ToString()[..6].ToUpperInvariant()}";

            var effectiveFullAddress = !string.IsNullOrWhiteSpace(cachedQuote.Request.ShippingAddress?.FullAddressText)
                ? cachedQuote.Request.ShippingAddress.FullAddressText
                : request.ShippingAddress;

            var addressSnapshot = new ShippingAddressSnapshot(
                cachedQuote.Request.ShippingAddress?.ReceiverName ?? string.Empty,
                cachedQuote.Request.ContactPhone ?? request.ContactPhone,
                cachedQuote.Request.ShippingAddress?.Street ?? string.Empty,
                cachedQuote.Request.ShippingAddress?.Ward ?? string.Empty,
                cachedQuote.Request.ShippingAddress?.District ?? string.Empty,
                cachedQuote.Request.ShippingAddress?.Province ?? string.Empty,
                cachedQuote.Request.ShippingAddress?.GhnDistrictId ?? 0,
                cachedQuote.Request.ShippingAddress?.GhnWardCode ?? "",
                effectiveFullAddress
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
                PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
            };

            foreach (var soi in subOrderItems)
            {
                subOrder.Items.Add(soi);
            }

            decimal shopDiscount = shop.ShopDiscountAmount;
            decimal systemDiscount = shop.SystemDiscountAmount;
            decimal productDiscount = shop.ProductDiscountAmount;
            if (recalculatedVouchers != null && recalculatedVouchers.ShopDiscounts.TryGetValue(shop.SellerId, out var freshBreakdown))
            {
                shopDiscount = freshBreakdown.ShopDiscountAmount;
                systemDiscount = freshBreakdown.SystemDiscountAmount;
                productDiscount = freshBreakdown.ProductDiscountAmount;
            }

            subOrder.CalculateFinancials(
                shippingFee: shop.ShippingFeeAmount,
                shopDiscount: shopDiscount,
                systemDiscount: systemDiscount,
                platformMarkupPercent: platformMarkupPercent,
                productDiscount: productDiscount);

            grandTotal += subOrder.TotalAmount;

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

        // Gán OrderId và SubOrderId cho các usage đã giữ chỗ
        if (reservedUsageIds.Count > 0)
        {
            var usages = await _dbContext.DiscountCodeUsages.Where(u => reservedUsageIds.Contains(u.Id)).ToListAsync(ct);
            foreach (var u in usages)
            {
                u.OrderId = orderId;
                var matchingSub = createdSubOrders.FirstOrDefault(so =>
                    recalculatedVouchers?.AllocatedUsages?.Any(au => au.DiscountCodeId == u.DiscountCodeId && au.SubOrderSellerId == so.SellerId) == true);
                if (matchingSub != null)
                {
                    u.SubOrderId = matchingSub.Id;
                }
            }
        }

        // Giữ hàng tạm thời tại Fulfillment Service (TTL 15 phút) (CHK-02)
        var reservationItems = createdSubOrders
            .SelectMany(so => so.Items.Select(i => new StockReservationItem(i.SkuId, i.Quantity, so.SellerId)))
            .ToList();

        var reserved = await _fulfillmentClient.ReserveStockAsync(orderId, reservationItems, ttlMinutes: 15, ct);
        if (!reserved)
        {
            if (reservedUsageIds.Count > 0 && _voucherLifecycleService != null)
            {
                await _voucherLifecycleService.ReleaseReservationsAsync(reservedUsageIds, ct);
            }
            throw new CommerceDomainException("INSUFFICIENT_STOCK", "One or more items in the order have insufficient stock.", 400);
        }

        bool paid = false;

        // Tích hợp thanh toán ví theo hợp đồng chuẩn của WalletService
        if (request.PaymentMethod == PaymentMethod.Wallet)
        {
            var paymentResult = await _walletClient.PayOrderAsync(order.Id, order.BuyerId, order.TotalAmount, cancellationToken: ct);

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
                    // A reminder for the seller, not a rule: no worker acts on this deadline for regular orders.
                    so.SellerRespondDeadlineAt = so.PaidAt.Value.Add(OrderPaidNotices.SellerAnswerWindow);
                    if (order.WalletTransactionId.HasValue)
                    {
                        so.WalletTransactionId = order.WalletTransactionId;
                    }

                    // Queued in this unit of work: saved together with the payment, never lost to a crash between the save and a later call.
                    OrderPaidNotices.Enqueue(_dbContext, _notificationSettings, so);
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
                // Lỗi dứt khoát (INSUFFICIENT_BALANCE, WALLET_NOT_FOUND): Thất bại fail-closed, trả lại tồn kho, giữ nguyên giỏ hàng
                _logger.LogWarning("Payment for Order '{OrderId}' failed definitively ({ErrorCode}): {Message}. Rolling back stock reservation and preserving cart.",
                    orderId, paymentResult?.ErrorCode, paymentResult?.ErrorMessage);

                await _fulfillmentClient.ReleaseStockAsync(orderId, ct);

                if (reservedUsageIds.Count > 0 && _voucherLifecycleService != null)
                {
                    await _voucherLifecycleService.ReleaseReservationsAsync(reservedUsageIds, ct);
                }

                if (paymentResult?.ErrorCode == "INSUFFICIENT_BALANCE" || paymentResult?.ErrorCode == "INSUFFICIENT_WALLET_BALANCE")
                {
                    throw new CommerceDomainException(
                        "INSUFFICIENT_WALLET_BALANCE",
                        "Buyer wallet balance is insufficient to complete the payment.",
                        400);
                }

                throw new CommerceDomainException(
                    paymentResult?.ErrorCode ?? "PAYMENT_FAILED",
                    paymentResult?.ErrorMessage ?? "Wallet payment failed.",
                    400);
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

            // Commit vouchers khi thanh toán thành công (Wallet) hoặc tạo đơn COD thành công
            if (_voucherLifecycleService != null && (paid || request.PaymentMethod == PaymentMethod.Cod))
            {
                try
                {
                    await _voucherLifecycleService.CommitOrderVouchersAsync(orderId, ct);
                }
                catch (Exception cvEx)
                {
                    _logger.LogWarning(cvEx, "Failed to commit voucher redemptions for Order '{OrderId}'.", orderId);
                }
            }

            // Đồng bộ cờ HasOrder = true sang Catalog Service để khóa quyền xóa SKU của Seller (INT-09 / 034)
            if (_catalogClient != null && allQuoteItems.Count > 0)
            {
                try
                {
                    var orderedSkuIds = allQuoteItems.Select(i => i.SkuId);
                    await _catalogClient.MarkVariantsOrderedAsync(orderedSkuIds, ct);
                }
                catch (Exception markEx)
                {
                    _logger.LogWarning(markEx, "Failed to mark variants as ordered in Catalog Service for Order '{OrderId}'.", orderId);
                }
            }
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

            // 3. Rollback Voucher: Giải phóng các voucher đã giữ chỗ
            if (reservedUsageIds.Count > 0 && _voucherLifecycleService != null)
            {
                try
                {
                    await _voucherLifecycleService.ReleaseReservationsAsync(reservedUsageIds, CancellationToken.None);
                    _logger.LogInformation("Compensation: Successfully released voucher reservations for Order '{OrderId}'.", orderId);
                }
                catch (Exception vEx)
                {
                    _logger.LogError(vEx, "Failed to release voucher reservations during compensation for Order '{OrderId}'.", orderId);
                }
            }

            throw new CommerceDomainException(
                "ORDER_CREATION_FAILED",
                "An error occurred while saving the order. All inventory allocations and payments have been safely rolled back.",
                500);
        }

        string? paymentUrl = null;

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

    public Task<OrderResponse> GetOrderDetailAsync(Guid currentUserId, Guid orderId, CancellationToken ct = default)
        => GetOrderDetailAsync(currentUserId, orderId, false, ct);

    public async Task<OrderResponse> GetOrderDetailAsync(Guid currentUserId, Guid orderId, bool isAdminOrStaff, CancellationToken ct = default)
    {
        // Passive Guard: Tự động hủy nếu đã quá hạn thanh toán
        await ExpireOrderIfOverdueAsync(orderId, ct);

        var order = await _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.SubOrders)
                .ThenInclude(s => s.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new EntityNotFoundException("ORDER_NOT_FOUND", "Order", orderId);

        if (!isAdminOrStaff && order.BuyerId != currentUserId)
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

    public Task<SubOrderDetailResponse> GetSubOrderDetailAsync(Guid currentUserId, Guid subOrderId, CancellationToken ct = default)
        => GetSubOrderDetailAsync(currentUserId, subOrderId, false, ct);

    public async Task<SubOrderDetailResponse> GetSubOrderDetailAsync(Guid currentUserId, Guid subOrderId, bool isAdminOrStaff, CancellationToken ct = default)
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

        if (!isAdminOrStaff && so.BuyerId != currentUserId && so.SellerId != currentUserId)
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

    private async Task ReleaseVoucherUsagesForSubOrderAsync(Guid subOrderId, DateTimeOffset now, CancellationToken ct)
    {
        if (_voucherLifecycleService != null)
        {
            await _voucherLifecycleService.ReleaseSubOrderVouchersAsync(subOrderId, ct);
            return;
        }

        var usages = await _dbContext.DiscountCodeUsages
            .Where(u => u.SubOrderId == subOrderId && (u.Status == DiscountUsageStatus.Reserved || u.Status == DiscountUsageStatus.Used))
            .ToListAsync(ct);

        if (usages.Count > 0)
        {
            var discountCodeIds = usages.Select(u => u.DiscountCodeId).Distinct().ToList();
            var discountCodes = await _dbContext.DiscountCodes
                .Where(d => discountCodeIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, ct);

            foreach (var usage in usages)
            {
                usage.Status = DiscountUsageStatus.Released;
                usage.ReleasedAt = now;

                if (discountCodes.TryGetValue(usage.DiscountCodeId, out var code))
                {
                    code.ReservedCount = Math.Max(0, code.ReservedCount - 1);
                }
            }
        }
    }

    public async Task CancelOrderAsync(Guid currentUserId, Guid orderId, CancelOrderRequest request, CancellationToken ct = default)
    {
        var order = await _dbContext.Orders
            .Include(o => o.SubOrders)
                .ThenInclude(s => s.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new EntityNotFoundException("ORDER_NOT_FOUND", "Order", orderId);

        bool isBuyer = order.BuyerId == currentUserId;
        bool isSeller = order.SubOrders.Any(s => s.SellerId == currentUserId);

        if (!isBuyer && !isSeller)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to cancel this order.", 403);
        }

        var targetSubOrders = isBuyer
            ? order.SubOrders.ToList()
            : order.SubOrders.Where(s => s.SellerId == currentUserId).ToList();

        var hasShippedOrCompletedSubOrder = targetSubOrders.Any(
            so => so.Status == SubOrderStatus.Shipping || so.Status == SubOrderStatus.Delivered || so.Status == SubOrderStatus.Completed);

        if ((order.Status != OrderStatus.PendingPayment && order.Status != OrderStatus.Paid) || hasShippedOrCompletedSubOrder)
        {
            throw new CommerceDomainException("ORDER_CANNOT_BE_CANCELLED", "Order cannot be cancelled once shipped, delivered, or completed.");
        }

        if (isBuyer || order.SubOrders.All(s => s.SellerId == currentUserId || s.Status == SubOrderStatus.Cancelled))
        {
            order.Status = OrderStatus.Cancelled;
        }

        bool wasOrderPaid = order.Status == OrderStatus.Paid || targetSubOrders.Any(s => s.PaidAt.HasValue || s.Status == SubOrderStatus.Paid);
        var orderPaymentMethod = targetSubOrders.FirstOrDefault()?.PaymentMethod ?? PaymentMethod.Wallet;

        foreach (var so in targetSubOrders)
        {
            if (so.Status == SubOrderStatus.Cancelled)
            {
                continue;
            }

            var wasConfirmedOrPaid = so.PaidAt.HasValue || so.Status == SubOrderStatus.Paid || so.Status == SubOrderStatus.Confirmed;
            var wasPaid = so.PaidAt.HasValue || so.Status == SubOrderStatus.Paid;

            // Hủy bưu tá / vận đơn giao hàng nếu đã tạo tracking
            if (!string.IsNullOrWhiteSpace(so.TrackingCode) || !string.IsNullOrWhiteSpace(so.CarrierCode))
            {
                try
                {
                    await _fulfillmentClient.CancelShipmentAsync(
                        so.Id,
                        request.ReasonText ?? "Order cancelled prior to courier pickup.",
                        ct);
                    _logger.LogInformation("Cancelled carrier shipment for SubOrder '{SubOrderId}' during master order cancellation.", so.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to cancel shipment for SubOrder '{SubOrderId}' via FulfillmentClient during master order cancellation.", so.Id);
                }
            }

            so.Status = SubOrderStatus.Cancelled;
            so.CancelledAt = DateTimeOffset.UtcNow;

            var cancellationId = Guid.NewGuid();
            _dbContext.OrderCancellations.Add(new OrderCancellation
            {
                Id = cancellationId,
                SubOrderId = so.Id,
                CancelledBy = currentUserId,
                ActorRole = isBuyer ? OrderActorRole.Buyer : OrderActorRole.Seller,
                ReasonCode = request.ReasonCode,
                ReasonNote = request.ReasonText,
                WasPaid = wasPaid,
                InventoryReleased = true
            });

            // TỰ ĐỘNG CHẾ TÀI PHẠT ĐIỂM NẾU SELLER TỰ Ý HỦY ĐƠN ĐÃ XÁC NHẬN (INT-06)
            bool isViolation = isSeller && wasConfirmedOrPaid &&
                (request.ReasonCode == CancellationReasonCode.CancellationWithoutReason ||
                 request.ReasonCode == CancellationReasonCode.SellerRejected);

            if (isViolation && _userClient != null)
            {
                try
                {
                    await _userClient.CreatePenaltyAsync(new UserPenaltyRequest(
                        UserId: so.SellerId,
                        TargetRole: "Seller",
                        ViolationType: "CancellationWithoutReason",
                        ReferenceType: "Order",
                        ReferenceId: so.SubOrderNo,
                        Reason: request.ReasonText ?? $"Seller cancelled confirmed order {so.SubOrderNo} without acceptable explanation.",
                        CustomScoreDeduction: 5,
                        PolicyClauseCode: "POLICY-SEC-3.2"
                    ), ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to apply penalty on Seller '{SellerId}' for unjustified cancellation.", so.SellerId);
                }
            }

            // Nếu người bán tự hủy SubOrder của mình và SubOrder đó đã thanh toán qua ví:
            if (!isBuyer && wasPaid && so.PaymentMethod == PaymentMethod.Wallet)
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
                    ReasonCode = "SELLER_CANCELLED",
                    ReasonNote = request.ReasonText ?? "Seller cancelled order.",
                    ActorRole = OrderActorRole.Seller,
                    RequestedBy = currentUserId,
                    RequestedAt = DateTimeOffset.UtcNow
                };

                try
                {
                    var refundSucceeded = await _walletClient.RefundSubOrderPaymentAsync(
                        so.Id,
                        so.BuyerId.ToString(),
                        so.TotalAmount,
                        request.ReasonText ?? "Seller cancelled order.",
                        idempotencyKey: $"refund-suborder-cancel-{so.Id}",
                        cancellationToken: ct);

                    if (refundSucceeded)
                    {
                        refund.Status = RefundStatus.Completed;
                        refund.CompletedAt = DateTimeOffset.UtcNow;
                        _logger.LogInformation("SubOrder '{SubOrderId}' refund on cancel succeeded via WalletClient. Amount: {Amount}", so.Id, so.TotalAmount);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to automatically refund wallet balance on cancel for SubOrder '{SubOrderId}'.", so.Id);
                }

                _dbContext.OrderRefunds.Add(refund);
            }
            else if (so.PaymentMethod == PaymentMethod.Cod)
            {
                _logger.LogInformation("SubOrder '{SubOrderId}' has COD payment method. Skipping wallet refund on cancellation.", so.Id);
            }

            // Giải phóng voucher đã giữ chỗ cho sub-order
            await ReleaseVoucherUsagesForSubOrderAsync(so.Id, DateTimeOffset.UtcNow, ct);
        }

        // KHI NGƯỜI MUA HỦY TOÀN BỘ ĐƠN MẸ:
        // Gọi duy nhất 1 lần endpoint đơn mẹ POST api/v1/wallets/orders/{orderId}/refund để khấu trừ thẳng từ OrderEscrow gốc
        if (isBuyer && wasOrderPaid && orderPaymentMethod == PaymentMethod.Wallet)
        {
            var refundAmount = order.TotalAmount;
            var refund = new OrderRefund
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                SubOrderId = null,
                CancellationId = null,
                RefundType = RefundType.Full,
                Amount = refundAmount,
                Status = RefundStatus.Requested,
                ReasonCode = "BUYER_CANCELLED",
                ReasonNote = request.ReasonText ?? "Buyer cancelled order before shipping.",
                ActorRole = OrderActorRole.Buyer,
                RequestedBy = currentUserId,
                RequestedAt = DateTimeOffset.UtcNow
            };

            try
            {
                var refundSucceeded = await _walletClient.RefundOrderPaymentAsync(
                    order.Id,
                    order.BuyerId.ToString(),
                    refundAmount,
                    request.ReasonText ?? "Buyer cancelled order before shipping.",
                    idempotencyKey: $"refund-order-cancel-{order.Id}",
                    cancellationToken: ct);

                if (refundSucceeded)
                {
                    refund.Status = RefundStatus.Completed;
                    refund.CompletedAt = DateTimeOffset.UtcNow;
                    _logger.LogInformation("Order '{OrderId}' master refund on cancel succeeded via WalletClient. Amount: {Amount}", order.Id, refundAmount);
                }
                else
                {
                    _logger.LogWarning("Order '{OrderId}' master refund returned false via WalletClient.", order.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to automatically refund master wallet balance on cancel for Order '{OrderId}'.", order.Id);
            }

            _dbContext.OrderRefunds.Add(refund);
        }
        else if (isBuyer && orderPaymentMethod == PaymentMethod.Cod)
        {
            _logger.LogInformation("Order '{OrderId}' has COD payment method. Skipping master wallet refund on cancellation.", order.Id);
        }

        await _dbContext.SaveChangesAsync(ct);

        // Nhả tồn kho lại cho Fulfillment Service (CHK-02 / FUL-10)
        if (isBuyer)
        {
            await _fulfillmentClient.ReleaseStockAsync(order.Id, ct);
        }
        else
        {
            var itemsToRelease = targetSubOrders
                .SelectMany(so => so.Items.Select(i => new StockReservationItem(i.SkuId, i.Quantity, so.SellerId)))
                .ToList();
            await _fulfillmentClient.ReleaseSubOrderStockAsync(order.Id, itemsToRelease, ct);
        }
    }

    public async Task<CancelSubOrderResponse> CancelSubOrderAsync(Guid currentUserId, Guid subOrderId, CancelSubOrderRequest request, CancellationToken ct = default)
    {
        var subOrder = await _dbContext.SubOrders
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == subOrderId, ct)
            ?? throw new EntityNotFoundException("SUB_ORDER_NOT_FOUND", "SubOrder", subOrderId);

        if (subOrder.BuyerId != currentUserId)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to cancel this sub-order.", 403);
        }

        if (subOrder.Status != SubOrderStatus.PendingPayment &&
            subOrder.Status != SubOrderStatus.Paid &&
            subOrder.Status != SubOrderStatus.Confirmed)
        {
            throw new CommerceDomainException("SUB_ORDER_CANNOT_BE_CANCELLED", "Sub-order cannot be cancelled once shipped or completed.");
        }

        var previousStatus = subOrder.Status;
        var wasPaid = subOrder.PaidAt.HasValue || previousStatus == SubOrderStatus.Paid || previousStatus == SubOrderStatus.Confirmed;
        var now = DateTimeOffset.UtcNow;

        subOrder.Status = SubOrderStatus.Cancelled;
        subOrder.CancelledAt = now;
        subOrder.UpdatedAt = now;

        var cancellationId = Guid.NewGuid();
        _dbContext.OrderCancellations.Add(new OrderCancellation
        {
            Id = cancellationId,
            SubOrderId = subOrder.Id,
            CancelledBy = currentUserId,
            ActorRole = OrderActorRole.Buyer,
            ReasonCode = request.ReasonCode,
            ReasonNote = request.ReasonNote ?? "Buyer cancelled sub-order.",
            WasPaid = wasPaid,
            InventoryReleased = true
        });

        _dbContext.OrderStatusHistories.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrder.Id,
            FromStatus = previousStatus,
            ToStatus = SubOrderStatus.Cancelled,
            ActorRole = OrderActorRole.Buyer,
            Reason = request.ReasonNote ?? "Buyer cancelled sub-order.",
            CreatedAt = now
        });

        decimal refundAmount = 0m;
        if (wasPaid && subOrder.PaymentMethod == PaymentMethod.Wallet && subOrder.TotalAmount > 0)
        {
            refundAmount = subOrder.TotalAmount;
            var refund = new OrderRefund
            {
                Id = Guid.NewGuid(),
                SubOrderId = subOrder.Id,
                OrderId = subOrder.OrderId,
                CancellationId = cancellationId,
                RefundType = RefundType.Full,
                Amount = refundAmount,
                Status = RefundStatus.Requested,
                ReasonCode = "BUYER_CANCELLED",
                ReasonNote = request.ReasonNote ?? "Buyer cancelled sub-order before shipping.",
                ActorRole = OrderActorRole.Buyer,
                RequestedBy = currentUserId,
                RequestedAt = now
            };

            try
            {
                var refundSucceeded = await _walletClient.RefundSubOrderPaymentAsync(
                    subOrder.Id,
                    subOrder.BuyerId.ToString(),
                    refundAmount,
                    request.ReasonNote ?? "Buyer cancelled sub-order before shipping.",
                    idempotencyKey: $"refund-suborder-cancel-{subOrder.Id}",
                    cancellationToken: ct);

                if (refundSucceeded)
                {
                    refund.Status = RefundStatus.Completed;
                    refund.CompletedAt = DateTimeOffset.UtcNow;
                    _logger.LogInformation("SubOrder '{SubOrderId}' refund on cancel succeeded via WalletClient. Amount: {Amount}", subOrder.Id, refundAmount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to automatically refund wallet balance on cancel for SubOrder '{SubOrderId}'.", subOrder.Id);
            }

            _dbContext.OrderRefunds.Add(refund);
        }
        else if (subOrder.PaymentMethod == PaymentMethod.Cod)
        {
            _logger.LogInformation("SubOrder '{SubOrderId}' has COD payment method. Skipping wallet refund on cancellation.", subOrder.Id);
        }

        // Giải phóng voucher đã giữ chỗ cho sub-order
        await ReleaseVoucherUsagesForSubOrderAsync(subOrder.Id, now, ct);

        // Đồng bộ trạng thái master order nếu tất cả đơn con còn lại đều Cancelled, Completed, hoặc Delivered
        var parentOrder = await _dbContext.Orders
            .Include(o => o.SubOrders)
            .FirstOrDefaultAsync(o => o.Id == subOrder.OrderId, ct);

        if (parentOrder != null)
        {
            var nonCancelledSubOrders = parentOrder.SubOrders
                .Where(s => s.Id != subOrder.Id && s.Status != SubOrderStatus.Cancelled)
                .ToList();

            if (nonCancelledSubOrders.Count == 0)
            {
                parentOrder.Status = OrderStatus.Cancelled;
                parentOrder.UpdatedAt = now;
            }
            else if (nonCancelledSubOrders.All(s => s.Status == SubOrderStatus.Completed))
            {
                parentOrder.Status = OrderStatus.Completed;
                parentOrder.UpdatedAt = now;
            }
            else if (nonCancelledSubOrders.All(s => s.Status == SubOrderStatus.Delivered))
            {
                parentOrder.Status = OrderStatus.Delivered;
                parentOrder.UpdatedAt = now;
            }
        }

        await _dbContext.SaveChangesAsync(ct);

        // Hủy bưu tá / vận đơn giao hàng nếu đã tạo tracking
        bool carrierShipmentCancelled = false;
        if (!string.IsNullOrWhiteSpace(subOrder.TrackingCode) || !string.IsNullOrWhiteSpace(subOrder.CarrierCode))
        {
            try
            {
                carrierShipmentCancelled = await _fulfillmentClient.CancelShipmentAsync(
                    subOrder.Id,
                    request.ReasonNote ?? "Buyer cancelled sub-order prior to courier pickup.",
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cancel shipment for SubOrder '{SubOrderId}' via FulfillmentClient.", subOrder.Id);
            }
        }

        // Nhả tồn kho riêng cho các món thuộc sub-order bị hủy
        bool inventoryReleased = false;
        var itemsToRelease = subOrder.Items
            .Select(i => new StockReservationItem(i.SkuId, i.Quantity, subOrder.SellerId))
            .ToList();

        if (itemsToRelease.Count > 0)
        {
            try
            {
                inventoryReleased = await _fulfillmentClient.ReleaseSubOrderStockAsync(
                    subOrder.OrderId,
                    itemsToRelease,
                    "BuyerCancelledSubOrder",
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to release stock for SubOrder '{SubOrderId}' via FulfillmentClient.", subOrder.Id);
            }
        }

        return new CancelSubOrderResponse(
            SubOrderId: subOrder.Id,
            Status: subOrder.Status.ToString(),
            CancelledAt: now,
            RefundAmount: refundAmount,
            InventoryReleased: inventoryReleased,
            CarrierShipmentCancelled: carrierShipmentCancelled
        );
    }

    public async Task SellerAcceptSubOrderAsync(Guid sellerId, Guid orderId, SellerAcceptOrderRequest request, CancellationToken ct = default)
    {
        var so = await _dbContext.SubOrders
            .Include(s => s.Items)
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

        var previousStatus = so.Status;
        so.Status = SubOrderStatus.Confirmed;
        so.SellerAcceptedAt = DateTimeOffset.UtcNow;
        so.UpdatedAt = DateTimeOffset.UtcNow;

        _dbContext.OrderStatusHistories.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            SubOrderId = so.Id,
            FromStatus = previousStatus,
            ToStatus = SubOrderStatus.Confirmed,
            ActorRole = OrderActorRole.Seller,
            Reason = !string.IsNullOrWhiteSpace(request.Note) ? request.Note : "Seller accepted sub-order for fulfillment."
        });

        // Tạo vận đơn GHN tự động qua Fulfillment Service (ORD-04 / T009)
        try
        {
            var shipmentRequest = ShipmentRequestFactory.ForSubOrder(so);
            var shipmentResult = await _fulfillmentClient.CreateShipmentAsync(shipmentRequest, ct);
            if (shipmentResult != null && !string.IsNullOrWhiteSpace(shipmentResult.TrackingCode))
            {
                so.TrackingCode = shipmentResult.TrackingCode;
                so.CarrierCode = !string.IsNullOrWhiteSpace(shipmentResult.CarrierCode) ? shipmentResult.CarrierCode : "GHN";
                so.LabelPrintUrl = shipmentResult.LabelPrintUrl;
                so.ShipmentPendingCreation = false;
            }
            else
            {
                so.ShipmentPendingCreation = true;
            }
        }
        catch (Exception ex)
        {
            // Tránh ngắt quãng luồng xác nhận nếu service giao vận tạm bận
            _logger.LogWarning(ex, "Transient error creating carrier shipment for SubOrder '{SubOrderId}'. Flagging for retry.", so.Id);
            so.ShipmentPendingCreation = true;
        }

        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<RetryShipmentResponse> RetryCreateShipmentAsync(Guid sellerId, Guid subOrderId, CancellationToken ct = default)
    {
        var so = await _dbContext.SubOrders
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == subOrderId, ct)
            ?? throw new EntityNotFoundException("SUB_ORDER_NOT_FOUND", "SubOrder", subOrderId);

        if (so.SellerId != sellerId)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to retry shipment creation for this sub-order.", 403);
        }

        if (so.Status != SubOrderStatus.Confirmed)
        {
            throw new CommerceDomainException("INVALID_STATUS", "Only confirmed sub-orders can have carrier shipments created.", 400);
        }

        if (!string.IsNullOrWhiteSpace(so.TrackingCode))
        {
            throw new CommerceDomainException("SHIPMENT_ALREADY_EXISTS", "Shipment has already been created for this sub-order.", 400);
        }

        try
        {
            var shipmentRequest = ShipmentRequestFactory.ForSubOrder(so);
            var shipmentResult = await _fulfillmentClient.CreateShipmentAsync(shipmentRequest, ct);
            if (shipmentResult == null || string.IsNullOrWhiteSpace(shipmentResult.TrackingCode))
            {
                throw new CommerceDomainException("FULFILLMENT_SERVICE_UNAVAILABLE", "Carrier logistics service returned an empty shipment response.", 503);
            }

            so.TrackingCode = shipmentResult.TrackingCode;
            so.CarrierCode = !string.IsNullOrWhiteSpace(shipmentResult.CarrierCode) ? shipmentResult.CarrierCode : "GHN";
            so.LabelPrintUrl = shipmentResult.LabelPrintUrl;
            so.ShipmentPendingCreation = false;
            so.UpdatedAt = DateTimeOffset.UtcNow;

            await _dbContext.SaveChangesAsync(ct);

            return new RetryShipmentResponse(
                so.Id,
                so.SubOrderNo,
                so.TrackingCode,
                so.CarrierCode,
                so.LabelPrintUrl ?? "",
                DateTimeOffset.UtcNow
            );
        }
        catch (CommerceDomainException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retry carrier shipment creation for SubOrder '{SubOrderId}'.", subOrderId);
            throw new CommerceDomainException("FULFILLMENT_SERVICE_UNAVAILABLE", "Carrier logistics service temporarily unreachable.", 503);
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

        // TỰ ĐỘNG CHẾ TÀI PHẠT ĐIỂM NẾU SELLER TỰ Ý HỦY ĐƠN ĐÃ XÁC NHẬN (INT-06)
        var wasConfirmedOrPaid = wasPaid || so.SellerAcceptedAt.HasValue;
        bool isViolation = wasConfirmedOrPaid &&
            (request.ReasonCode == CancellationReasonCode.CancellationWithoutReason ||
             request.ReasonCode == CancellationReasonCode.SellerRejected);

        if (isViolation && _userClient != null)
        {
            try
            {
                await _userClient.CreatePenaltyAsync(new UserPenaltyRequest(
                    UserId: so.SellerId,
                    TargetRole: "Seller",
                    ViolationType: "CancellationWithoutReason",
                    ReferenceType: "Order",
                    ReferenceId: so.SubOrderNo,
                    Reason: request.ReasonText ?? $"Seller cancelled confirmed order {so.SubOrderNo} without acceptable explanation.",
                    CustomScoreDeduction: 5,
                    PolicyClauseCode: "POLICY-SEC-3.2"
                ), ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to apply penalty on Seller '{SellerId}' for unjustified rejection.", so.SellerId);
            }
        }

        // TỰ ĐỘNG HOÀN TIỀN VÍ NẾU ĐƠN CON ĐÃ THANH TOÁN QUA VÍ (BUG-B, US5)
        if (wasPaid && so.PaymentMethod == PaymentMethod.Wallet)
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
                ReasonNote = request.ReasonText ?? "Shop rejected sub-order after payment.",
                ActorRole = OrderActorRole.Seller,
                RequestedBy = sellerId,
                RequestedAt = DateTimeOffset.UtcNow
            };

            try
            {
                var refundSucceeded = await _walletClient.RefundSubOrderPaymentAsync(
                    so.Id,
                    so.BuyerId.ToString(),
                    so.TotalAmount,
                    request.ReasonText ?? "Shop rejected sub-order after payment.",
                    idempotencyKey: $"refund-suborder-{so.Id}",
                    cancellationToken: ct);

                if (refundSucceeded)
                {
                    refund.Status = RefundStatus.Completed;
                    refund.CompletedAt = DateTimeOffset.UtcNow;
                    _logger.LogInformation("SubOrder '{SubOrderId}' refund succeeded automatically via WalletClient. Amount: {Amount}", so.Id, so.TotalAmount);
                }
                else
                {
                    _logger.LogWarning("SubOrder '{SubOrderId}' automated wallet refund returned false. Flagged as Requested for manual reconciliation.", so.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to automatically refund wallet balance for SubOrder '{SubOrderId}'. Flagged as Requested.", so.Id);
            }

            _dbContext.OrderRefunds.Add(refund);
        }
        else if (so.PaymentMethod == PaymentMethod.Cod)
        {
            _logger.LogInformation("SubOrder '{SubOrderId}' has COD payment method. Skipping wallet refund on rejection.", so.Id);
        }

        // Giải phóng voucher đã giữ chỗ cho sub-order
        await ReleaseVoucherUsagesForSubOrderAsync(so.Id, DateTimeOffset.UtcNow, ct);

        await _dbContext.SaveChangesAsync(ct);

        // CHỈ NHẢ TỒN KHO CỦA CÁC MÓN THUỘC ĐÚNG SUBORDER NÀY (BUG-A)
        var itemsToRelease = so.Items
            .Select(i => new StockReservationItem(i.SkuId, i.Quantity, so.SellerId))
            .ToList();

        await _fulfillmentClient.ReleaseSubOrderStockAsync(so.OrderId, itemsToRelease, ct);
    }

    public async Task<ConfirmReceiptResponse> BuyerConfirmSubOrderReceiptAsync(Guid currentUserId, Guid subOrderId, CancellationToken ct = default)
    {
        var so = await _dbContext.SubOrders
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == subOrderId, ct)
            ?? throw new EntityNotFoundException("SUB_ORDER_NOT_FOUND", "SubOrder", subOrderId);

        if (so.BuyerId != currentUserId)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to confirm this sub-order.", 403);
        }

        if (so.Status != SubOrderStatus.Delivered)
        {
            throw new CommerceDomainException("INVALID_STATUS", "Sub-order cannot be confirmed until it is delivered.");
        }

        var completedAt = DateTimeOffset.UtcNow;
        so.Status = SubOrderStatus.Completed;
        so.CompletedAt = completedAt;
        so.UpdatedAt = completedAt;

        bool settled = false;
        if (so.PaymentMethod == PaymentMethod.Cod)
        {
            _logger.LogInformation("COD SubOrder '{SubOrderId}' confirmed. Courier reconciles physical cash directly. Skipping wallet escrow disbursement.", so.Id);
            settled = true;
        }
        else if (so.SellerReceiveAmount > 0m)
        {
            try
            {
                settled = await _walletClient.SettleSellerSubOrderAsync(
                    so.Id,
                    so.SellerId,
                    so.SellerReceiveAmount,
                    idempotencyKey: $"settle-{so.Id}",
                    cancellationToken: ct);

                if (settled)
                {
                    _logger.LogInformation("Escrow funds settled successfully for SubOrder '{SubOrderId}', Seller '{SellerId}', Amount: {Amount}", so.Id, so.SellerId, so.SellerReceiveAmount);
                }
                else
                {
                    _logger.LogWarning("Escrow settlement for SubOrder '{SubOrderId}' returned false. Flagged for settlement reconciliation.", so.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling WalletService to settle escrow for SubOrder '{SubOrderId}'. Flagged for reconciliation.", so.Id);
            }
        }
        else
        {
            _logger.LogInformation("SubOrder '{SubOrderId}' has zero seller receive amount ({Amount}). Skipping escrow wallet disbursement.", so.Id, so.SellerReceiveAmount);
            settled = true;
        }

        so.IsEscrowSettled = settled;

        // TỰ ĐỘNG CỘNG ĐIỂM UY TÍN CHO SELLER (INT-07)
        if (_userClient != null)
        {
            try
            {
                await _userClient.AdjustReputationAsync(new ReputationAdjustRequest(
                    UserId: so.SellerId,
                    TargetRole: "Seller",
                    PartnerUserId: so.BuyerId,
                    ScoreDelta: 1,
                    Reason: "OrderCompleted",
                    ReferenceType: "Order",
                    ReferenceId: so.SubOrderNo,
                    Note: $"Reward for successfully completed sub-order {so.SubOrderNo}."
                ), ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to adjust reputation for Seller '{SellerId}' on sub-order '{SubOrderId}'.", so.SellerId, so.Id);
            }
        }

        // TỰ ĐỘNG ĐỒNG BỘ ĐƠN MUA HÀNG SANG CATALOG SERVICE ĐỂ MỞ QUYỀN ĐÁNH GIÁ (INT-08)
        if (_catalogClient != null && so.Items.Count > 0)
        {
            try
            {
                var productIds = so.Items.Select(i => i.ProductId);
                await _catalogClient.SyncPurchasesBatchAsync(so.BuyerId, so.Id, productIds, completedAt, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to sync purchase records to Catalog for SubOrder '{SubOrderId}'.", so.Id);
            }
        }

        var parentOrder = await _dbContext.Orders
            .Include(o => o.SubOrders)
            .FirstOrDefaultAsync(o => o.Id == so.OrderId, ct);

        if (parentOrder != null && parentOrder.SubOrders.All(s => s.Id == so.Id || s.Status == SubOrderStatus.Completed || s.Status == SubOrderStatus.Cancelled))
        {
            parentOrder.Status = OrderStatus.Completed;
        }

        await _dbContext.SaveChangesAsync(ct);

        return new ConfirmReceiptResponse(
            SubOrderId: so.Id,
            Status: so.Status.ToString(),
            CompletedAt: completedAt,
            EscrowSettled: settled,
            AmountSettled: so.SellerReceiveAmount
        );
    }

    public async Task<OrderCompleteResponse> CompleteOrderAsync(Guid currentUserId, Guid orderId, CancellationToken ct = default)
    {
        var order = await _dbContext.Orders
            .Include(o => o.SubOrders)
                .ThenInclude(so => so.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new EntityNotFoundException("ORDER_NOT_FOUND", "Order", orderId);

        if (order.BuyerId != currentUserId)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to complete this order.", 403);
        }

        if (order.Status == OrderStatus.Cancelled || order.Status == OrderStatus.Expired)
        {
            throw new CommerceDomainException("INVALID_STATUS", "Order cannot be completed in its current status.");
        }

        var activeSubOrders = order.SubOrders.Where(s => s.Status != SubOrderStatus.Cancelled).ToList();
        if (activeSubOrders.Count == 0 || activeSubOrders.Any(s => s.Status != SubOrderStatus.Delivered && s.Status != SubOrderStatus.Completed))
        {
            throw new CommerceDomainException("INVALID_STATUS", "Order cannot be completed until all active sub-orders are delivered.");
        }

        var completedAt = DateTimeOffset.UtcNow;
        var completedSubOrderIds = new List<Guid>();

        foreach (var so in order.SubOrders)
        {
            if (so.Status == SubOrderStatus.Cancelled)
            {
                continue;
            }

            if (so.Status != SubOrderStatus.Completed)
            {
                so.Status = SubOrderStatus.Completed;
                so.CompletedAt = completedAt;
                so.UpdatedAt = completedAt;

                bool subOrderSettled = false;
                if (so.PaymentMethod == PaymentMethod.Cod)
                {
                    _logger.LogInformation("COD SubOrder '{SubOrderId}' completed during master order complete. Courier reconciles physical cash directly. Skipping escrow disbursement.", so.Id);
                    subOrderSettled = true;
                }
                else if (so.SellerReceiveAmount > 0m)
                {
                    try
                    {
                        subOrderSettled = await _walletClient.SettleSellerSubOrderAsync(
                            so.Id,
                            so.SellerId,
                            so.SellerReceiveAmount,
                            idempotencyKey: $"settle-{so.Id}",
                            cancellationToken: ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error settling escrow for SubOrder '{SubOrderId}' during master order complete.", so.Id);
                    }
                }
                else
                {
                    _logger.LogInformation("SubOrder '{SubOrderId}' has zero seller receive amount. Skipping escrow disbursement.", so.Id);
                    subOrderSettled = true;
                }

                so.IsEscrowSettled = subOrderSettled;
            }

            completedSubOrderIds.Add(so.Id);

            if (_userClient != null)
            {
                try
                {
                    await _userClient.AdjustReputationAsync(new ReputationAdjustRequest(
                        UserId: so.SellerId,
                        TargetRole: "Seller",
                        PartnerUserId: order.BuyerId,
                        ScoreDelta: 1,
                        Reason: "OrderCompleted",
                        ReferenceType: "Order",
                        ReferenceId: so.SubOrderNo,
                        Note: $"Reward for successfully completed order {so.SubOrderNo}."
                    ), ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to adjust reputation for Seller '{SellerId}' on order complete.", so.SellerId);
                }
            }

            // TỰ ĐỘNG ĐỒNG BỘ ĐƠN MUA HÀNG SANG CATALOG SERVICE ĐỂ MỞ QUYỀN ĐÁNH GIÁ (INT-08)
            if (_catalogClient != null && so.Items.Count > 0)
            {
                try
                {
                    var productIds = so.Items.Select(i => i.ProductId);
                    await _catalogClient.SyncPurchasesBatchAsync(order.BuyerId, so.Id, productIds, completedAt, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to sync purchase records to Catalog for SubOrder '{SubOrderId}' during master order complete.", so.Id);
                }
            }
        }

        order.Status = OrderStatus.Completed;
        await _dbContext.SaveChangesAsync(ct);

        var orderNo = $"ORD-{order.Id.ToString()[..8].ToUpperInvariant()}";
        return new OrderCompleteResponse(
            OrderId: order.Id,
            OrderNo: orderNo,
            Status: order.Status.ToString(),
            CompletedAt: completedAt,
            CompletedSubOrderCount: completedSubOrderIds.Count,
            CompletedSubOrderIds: completedSubOrderIds
        );
    }

    public async Task<PagedData<SubOrderSummaryResponse>> ListSellerSubOrdersAsync(Guid sellerId, int page, int pageSize, SubOrderStatus? status = null, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var query = _dbContext.SubOrders
            .AsNoTracking()
            .Include(s => s.Items)
            .Where(s => s.SellerId == sellerId && !s.IsDeleted);

        if (status.HasValue)
        {
            query = query.Where(s => s.Status == status.Value);
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new SubOrderSummaryResponse(
                s.Id,
                s.SubOrderNo,
                s.OrderId,
                s.BuyerId,
                s.SellerId,
                s.Status.ToString(),
                s.PaymentMethod.ToString(),
                s.ItemsAmount,
                s.ShippingFeeAmount,
                s.TotalAmount,
                s.SellerReceiveAmount,
                s.Items.Count,
                s.CreatedAt,
                s.SellerAcceptedAt,
                s.ShippedAt,
                s.DeliveredAt,
                s.CompletedAt,
                s.CancelledAt
            ))
            .ToListAsync(ct);

        return new PagedData<SubOrderSummaryResponse>(items, PaginationMetadata.Create(page, pageSize, totalCount));
    }

    public Task<ShipmentTrackingResponse> GetShipmentTrackingAsync(Guid currentUserId, Guid orderId, CancellationToken ct = default)
        => GetShipmentTrackingAsync(currentUserId, orderId, false, ct);

    public async Task<ShipmentTrackingResponse> GetShipmentTrackingAsync(Guid currentUserId, Guid orderId, bool isAdminOrStaff, CancellationToken ct = default)
    {
        var order = await _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.SubOrders)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new EntityNotFoundException("ORDER_NOT_FOUND", "Order", orderId);

        if (!isAdminOrStaff && order.BuyerId != currentUserId)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to track this shipment.", 403);
        }

        var shipments = order.SubOrders.Select(so => new SubOrderShipmentInfo(
            so.Id,
            so.SubOrderNo,
            so.SellerId,
            $"Shop {so.SellerId.ToString()[..8]}",
            so.Status.ToString(),
            so.CarrierCode ?? "GHN",
            so.TrackingCode,
            so.ShippedAt,
            so.DeliveredAt,
            so.LabelPrintUrl
        )).ToList();

        return new ShipmentTrackingResponse(
            order.Id,
            $"ORD-{order.Id.ToString()[..8].ToUpperInvariant()}",
            shipments
        );
    }

    public Task<ShipmentTimelineResponse> GetShipmentTimelineAsync(Guid currentUserId, Guid subOrderId, CancellationToken ct = default)
        => GetShipmentTimelineAsync(currentUserId, subOrderId, false, ct);

    public async Task<ShipmentTimelineResponse> GetShipmentTimelineAsync(Guid currentUserId, Guid subOrderId, bool isAdminOrStaff, CancellationToken ct = default)
    {
        var so = await _dbContext.SubOrders
            .AsNoTracking()
            .Include(s => s.StatusHistories)
            .FirstOrDefaultAsync(s => s.Id == subOrderId, ct);

        if (so != null)
        {
            if (!isAdminOrStaff && so.BuyerId != currentUserId && so.SellerId != currentUserId)
            {
                throw new CommerceDomainException("FORBIDDEN", "You do not have permission to view this shipment timeline.", 403);
            }

            var timelineEvents = MapHistoriesToTimeline(so.StatusHistories, so.Status, so.CreatedAt);

            return new ShipmentTimelineResponse(
                so.Id,
                so.SubOrderNo,
                so.OrderId,
                so.CarrierCode ?? "GHN",
                FormatCarrierName(so.CarrierCode),
                so.TrackingCode,
                so.LabelPrintUrl,
                so.Status.ToString(),
                so.EstimatedDeliveryAt,
                so.ShippingAddress,
                timelineEvents
            );
        }

        var ao = await _dbContext.AuctionOrders
            .AsNoTracking()
            .Include(a => a.StatusHistories)
            .FirstOrDefaultAsync(a => a.Id == subOrderId, ct);

        if (ao != null)
        {
            if (!isAdminOrStaff && ao.BuyerId != currentUserId && ao.SellerId != currentUserId)
            {
                throw new CommerceDomainException("FORBIDDEN", "You do not have permission to view this shipment timeline.", 403);
            }

            var timelineEvents = MapHistoriesToTimeline(ao.StatusHistories, ao.Status, ao.CreatedAt);
            ShippingAddressSnapshot addressSnapshot;
            if (!string.IsNullOrWhiteSpace(ao.ShippingAddress))
            {
                try
                {
                    addressSnapshot = JsonSerializer.Deserialize<ShippingAddressSnapshot>(ao.ShippingAddress, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                        ?? new ShippingAddressSnapshot(
                            receiverName: "Auction Winner",
                            contactPhone: ao.ContactPhone ?? "",
                            street: "",
                            ward: "",
                            district: "",
                            province: ""
                        );
                }
                catch
                {
                    addressSnapshot = new ShippingAddressSnapshot(
                        receiverName: "Auction Winner",
                        contactPhone: ao.ContactPhone ?? "",
                        street: ao.ShippingAddress,
                        ward: "",
                        district: "",
                        province: ""
                    );
                }
            }
            else
            {
                addressSnapshot = new ShippingAddressSnapshot(
                    receiverName: "Auction Winner",
                    contactPhone: ao.ContactPhone ?? "",
                    street: "",
                    ward: "",
                    district: "",
                    province: ""
                );
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

        throw new EntityNotFoundException("SUB_ORDER_NOT_FOUND", "SubOrder", subOrderId);
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

        if (so.Status != SubOrderStatus.Delivered && so.Status != SubOrderStatus.Completed)
        {
            throw new CommerceDomainException("INVALID_REFUND_STATUS", "Sub-order status is not eligible for refund request.", 400);
        }

        var hasPending = await _dbContext.OrderRefunds.AnyAsync(
            r => r.SubOrderId == subOrderId && r.Status == RefundStatus.Requested && !r.IsDeleted,
            ct);
        if (hasPending)
        {
            throw new CommerceDomainException("REFUND_REQUEST_ALREADY_EXISTS", "A pending refund request already exists for this sub-order.", 409);
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

    public async Task<ReviewEligibilityDto?> GetReviewEligibilityAsync(string referenceType, Guid referenceId, CancellationToken ct = default)
    {
        if (!ReviewReferenceTypes.TryNormalize(referenceType, out var type) || referenceId == Guid.Empty)
            return null;

        if (type == ReviewReferenceTypes.Auction)
        {
            return await _dbContext.AuctionOrders
                .AsNoTracking()
                .Where(o => o.Id == referenceId && !o.IsDeleted)
                .Select(o => new ReviewEligibilityDto(type, o.Id, o.Status.ToString(), o.BuyerId, o.SellerId, o.CompletedAt))
                .FirstOrDefaultAsync(ct);
        }

        return await _dbContext.SubOrders
            .AsNoTracking()
            .Where(s => s.Id == referenceId && !s.IsDeleted)
            .Select(s => new ReviewEligibilityDto(type, s.Id, s.Status.ToString(), s.BuyerId, s.SellerId, s.CompletedAt))
            .FirstOrDefaultAsync(ct);
    }

    public Task UpdateSubOrderDeliveryStatusAsync(Guid subOrderId, string status, DateTimeOffset? deliveredAt, CancellationToken ct = default)
    {
        return UpdateSubOrderDeliveryStatusAsync(subOrderId, new UpdateDeliveryStatusRequest(status, deliveredAt), ct);
    }

    public async Task<DisputeRefundResponse> RefundSubOrderForDisputeAsync(Guid subOrderId, Guid reportId, string? note, CancellationToken ct = default)
    {
        if (reportId == Guid.Empty)
        {
            throw new CommerceDomainException("VAL_INVALID_INPUT", "Report ID is required.", 400);
        }

        var so = await _dbContext.SubOrders.FirstOrDefaultAsync(s => s.Id == subOrderId, ct)
            ?? throw new EntityNotFoundException("SUB_ORDER_NOT_FOUND", "SubOrder", subOrderId);

        var refunds = await _dbContext.OrderRefunds
            .Where(r => r.SubOrderId == subOrderId || (r.OrderId == so.OrderId && r.SubOrderId == null))
            .ToListAsync(ct);

        // Same report again: return what happened the first time, and retry only if the money did not move.
        var existing = refunds.FirstOrDefault(r => r.SubOrderId == subOrderId && r.RequestEventId == reportId);
        if (existing is { Status: RefundStatus.Completed })
        {
            return new DisputeRefundResponse("AlreadyRefunded", existing.Amount, existing.Id, "This dispute was already refunded.");
        }

        if (so.Status is not (SubOrderStatus.Delivered or SubOrderStatus.Completed))
        {
            throw new CommerceDomainException("REFUND_NOT_ALLOWED", "Only a delivered sub-order can be refunded because of a dispute.", 409);
        }

        if (!so.PaidAt.HasValue || so.PaymentMethod != PaymentMethod.Wallet)
        {
            return new DisputeRefundResponse("ManualProcessingRequired", 0m, null, "The sub-order was not paid from the wallet, so no wallet refund can be made automatically.");
        }

        // The money already went to the seller: refunding it needs a source of funds that does not exist yet (T30), so a person decides.
        if (so.IsEscrowSettled)
        {
            return new DisputeRefundResponse("ManualProcessingRequired", 0m, null, "The seller was already paid for this sub-order; the refund must be handled manually.");
        }

        // The wallet accepts one refund credit per reference, and the parent order and the sub-order are different references.
        if (refunds.Any(r => r.SubOrderId == null && r.Status == RefundStatus.Completed))
        {
            return new DisputeRefundResponse("ManualProcessingRequired", 0m, null, "The parent order was already refunded; the amount must be checked manually.");
        }

        var alreadyRefunded = refunds.Where(r => r.SubOrderId == subOrderId && r.Status == RefundStatus.Completed).Sum(r => r.Amount);
        var amount = so.TotalAmount - alreadyRefunded;
        if (amount <= 0)
        {
            return new DisputeRefundResponse("AlreadyRefunded", alreadyRefunded, null, "The sub-order is already fully refunded.");
        }

        var reason = string.IsNullOrWhiteSpace(note) ? "Dispute decided in favour of the buyer." : note.Trim();
        var refund = existing;
        if (refund == null)
        {
            refund = new OrderRefund
            {
                Id = Guid.NewGuid(),
                OrderId = so.OrderId,
                SubOrderId = so.Id,
                RefundType = alreadyRefunded > 0 ? RefundType.Partial : RefundType.Full,
                Amount = amount,
                Status = RefundStatus.Requested,
                ReasonCode = "DISPUTE_RESOLVED",
                ReasonNote = reason,
                ActorRole = OrderActorRole.System,
                RequestedBy = null,
                RequestEventId = reportId,
                RequestedAt = DateTimeOffset.UtcNow
            };
            _dbContext.OrderRefunds.Add(refund);
            await _dbContext.SaveChangesAsync(ct);
        }

        bool succeeded;
        try
        {
            // One stable key per report and sub-order: a retry cannot move the money twice.
            succeeded = await _walletClient.RefundSubOrderPaymentAsync(
                so.Id, so.BuyerId.ToString(), refund.Amount, reason, idempotencyKey: $"refund-dispute-{reportId}-{so.Id}", cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dispute refund for SubOrder '{SubOrderId}' (report '{ReportId}') could not reach the wallet.", so.Id, reportId);
            succeeded = false;
        }

        if (!succeeded)
        {
            refund.FailureCode = "WALLET_REFUND_FAILED";
            await _dbContext.SaveChangesAsync(ct);
            return new DisputeRefundResponse("Failed", refund.Amount, refund.Id, "The wallet did not confirm the refund. Nothing was recorded as refunded; try again.");
        }

        refund.Status = RefundStatus.Completed;
        refund.CompletedAt = DateTimeOffset.UtcNow;
        refund.FailureCode = null;
        await _dbContext.SaveChangesAsync(ct);
        return new DisputeRefundResponse("Refunded", refund.Amount, refund.Id, null);
    }

    public async Task UpdateSubOrderDeliveryStatusAsync(Guid subOrderId, UpdateDeliveryStatusRequest request, CancellationToken ct = default)
    {
        var so = await _dbContext.SubOrders
            .Include(s => s.Items)
            .Include(s => s.StatusHistories)
            .FirstOrDefaultAsync(s => s.Id == subOrderId, ct);

        if (so == null)
        {
            var auctionOrder = await _dbContext.AuctionOrders
                .Include(a => a.StatusHistories)
                .FirstOrDefaultAsync(a => a.Id == subOrderId, ct);

            if (auctionOrder != null)
            {
                await UpdateAuctionOrderDeliveryStatusAsync(auctionOrder, request, ct);
                return;
            }

            throw new EntityNotFoundException("SUB_ORDER_NOT_FOUND", "SubOrder", subOrderId);
        }

        var status = request.Status?.Trim() ?? "";
        var timestamp = request.DeliveredAt ?? DateTimeOffset.UtcNow;
        var previousStatus = so.Status;

        if (!DeliveryStatusTransitions.IsAllowed(so.Status, status))
        {
            _logger.LogInformation(
                "[OrderService] Ignoring out-of-order carrier status '{CarrierStatus}' for SubOrder '{SubOrderId}' currently at '{CurrentStatus}'.",
                status, subOrderId, so.Status);
            return;
        }

        if (!string.IsNullOrWhiteSpace(request.CarrierCode)) so.CarrierCode = request.CarrierCode;
        if (!string.IsNullOrWhiteSpace(request.TrackingCode)) so.TrackingCode = request.TrackingCode;

        // Idempotency check: duplicate event within 2 seconds
        var isDuplicate = so.StatusHistories.Any(h =>
            h.Reason != null && h.Reason.StartsWith($"[{status}]") &&
            Math.Abs((h.CreatedAt - timestamp).TotalSeconds) < 2);

        if (isDuplicate)
        {
            _logger.LogInformation("[OrderService] Ignoring duplicate delivery status '{Status}' for SubOrder '{SubOrderId}'.", status, subOrderId);
            return;
        }

        string formattedReason = FormatHistoryReason(status, request.Note, request.Location, request.ShipperName, request.ShipperPhone, request.PodUrl);

        if (string.Equals(status, "ReadyToPick", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "Picking", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "Packed", StringComparison.OrdinalIgnoreCase))
        {
            // Preparation phases: Idempotent without altering confirmed status
            so.UpdatedAt = timestamp;

            _dbContext.OrderStatusHistories.Add(new OrderStatusHistory
            {
                Id = Guid.NewGuid(),
                SubOrderId = so.Id,
                FromStatus = previousStatus,
                ToStatus = previousStatus,
                ActorRole = OrderActorRole.System,
                Reason = formattedReason,
                CreatedAt = timestamp
            });
        }
        else if (string.Equals(status, "HandedOver", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(status, "InTransit", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(status, "OutForDelivery", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(status, "Shipped", StringComparison.OrdinalIgnoreCase))
        {
            so.Status = SubOrderStatus.Shipped;
            so.ShippedAt ??= timestamp;
            so.UpdatedAt = timestamp;

            _dbContext.OrderStatusHistories.Add(new OrderStatusHistory
            {
                Id = Guid.NewGuid(),
                SubOrderId = so.Id,
                FromStatus = previousStatus,
                ToStatus = SubOrderStatus.Shipped,
                ActorRole = OrderActorRole.System,
                Reason = formattedReason,
                CreatedAt = timestamp
            });
        }
        else if (string.Equals(status, "Delivered", StringComparison.OrdinalIgnoreCase))
        {
            so.Status = SubOrderStatus.Delivered;
            so.DeliveredAt = timestamp;
            so.UpdatedAt = timestamp;

            _dbContext.OrderStatusHistories.Add(new OrderStatusHistory
            {
                Id = Guid.NewGuid(),
                SubOrderId = so.Id,
                FromStatus = previousStatus,
                ToStatus = SubOrderStatus.Delivered,
                ActorRole = OrderActorRole.System,
                Reason = formattedReason,
                CreatedAt = timestamp
            });

            // Master Order synchronization (T022 [US3])
            var parentOrder = await _dbContext.Orders
                .Include(o => o.SubOrders)
                .FirstOrDefaultAsync(o => o.Id == so.OrderId, ct);

            if (parentOrder != null)
            {
                bool allDeliveredOrCompleted = parentOrder.SubOrders.All(s =>
                    (s.Id == so.Id ? SubOrderStatus.Delivered : s.Status) == SubOrderStatus.Delivered ||
                    (s.Id == so.Id ? SubOrderStatus.Delivered : s.Status) == SubOrderStatus.Completed);

                if (allDeliveredOrCompleted && parentOrder.Status != OrderStatus.Completed)
                {
                    parentOrder.Status = OrderStatus.Delivered;
                    parentOrder.UpdatedAt = timestamp;
                }
            }
        }
        else if (string.Equals(status, "DeliveryAttemptFailed", StringComparison.OrdinalIgnoreCase))
        {
            // T024 [US4]: Retain Shipped status, log history with reason/location
            so.UpdatedAt = timestamp;

            _dbContext.OrderStatusHistories.Add(new OrderStatusHistory
            {
                Id = Guid.NewGuid(),
                SubOrderId = so.Id,
                FromStatus = previousStatus,
                ToStatus = SubOrderStatus.Shipped,
                ActorRole = OrderActorRole.System,
                Reason = formattedReason,
                CreatedAt = timestamp
            });
        }
        else if (string.Equals(status, "Returned", StringComparison.OrdinalIgnoreCase))
        {
            // T025 [US4]: Transition to Cancelled, refund wallet if paid, release stock with reason "PackageReturned"
            so.Status = SubOrderStatus.Cancelled;
            so.CancelledAt = timestamp;
            so.UpdatedAt = timestamp;

            _dbContext.OrderStatusHistories.Add(new OrderStatusHistory
            {
                Id = Guid.NewGuid(),
                SubOrderId = so.Id,
                FromStatus = previousStatus,
                ToStatus = SubOrderStatus.Cancelled,
                ActorRole = OrderActorRole.System,
                Reason = formattedReason,
                CreatedAt = timestamp
            });

            var cancellationId = Guid.NewGuid();
            var cancellation = new OrderCancellation
            {
                Id = cancellationId,
                SubOrderId = so.Id,
                CancelledBy = null,
                ActorRole = OrderActorRole.System,
                ReasonCode = CancellationReasonCode.PackageReturned,
                ReasonNote = request.Note ?? "Package returned to sender warehouse by logistics carrier.",
                WasPaid = so.PaidAt.HasValue,
                InventoryReleased = true
            };
            _dbContext.OrderCancellations.Add(cancellation);

            if (so.PaidAt.HasValue && so.PaymentMethod == PaymentMethod.Wallet)
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
                    ReasonCode = "PACKAGE_RETURNED",
                    ReasonNote = request.Note ?? "Automated refund created due to package returned to sender.",
                    ActorRole = OrderActorRole.System,
                    RequestedBy = null,
                    RequestedAt = timestamp
                };

                try
                {
                    var refundSucceeded = await _walletClient.RefundSubOrderPaymentAsync(
                        so.Id,
                        so.BuyerId.ToString(),
                        so.TotalAmount,
                        request.Note ?? "Refund for returned package.",
                        idempotencyKey: $"refund-delivery-returned-{so.Id}",
                        cancellationToken: ct);

                    if (refundSucceeded)
                    {
                        refund.Status = RefundStatus.Completed;
                        refund.CompletedAt = DateTimeOffset.UtcNow;
                        _logger.LogInformation("SubOrder '{SubOrderId}' refund on return succeeded via WalletClient. Amount: {Amount}", so.Id, so.TotalAmount);
                    }
                    else
                    {
                        _logger.LogWarning("SubOrder '{SubOrderId}' refund on return returned false via WalletClient.", so.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to automatically refund wallet balance for returned SubOrder '{SubOrderId}'.", so.Id);
                }

                _dbContext.OrderRefunds.Add(refund);
            }
            else if (so.PaymentMethod == PaymentMethod.Cod)
            {
                _logger.LogInformation("Returned SubOrder '{SubOrderId}' has COD payment method. Skipping wallet refund.", so.Id);
            }

            try
            {
                var itemsToRelease = so.Items
                    .Select(i => new StockReservationItem(i.SkuId, i.Quantity, so.SellerId))
                    .ToList();
                await _fulfillmentClient.ReleaseSubOrderStockAsync(so.OrderId, itemsToRelease, "PackageReturned", ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to release warehouse stock for returned SubOrder '{SubOrderId}'.", so.Id);
            }

            // Sync master order if all siblings cancelled
            var parentOrder = await _dbContext.Orders
                .Include(o => o.SubOrders)
                .FirstOrDefaultAsync(o => o.Id == so.OrderId, ct);

            if (parentOrder != null)
            {
                bool allCancelled = parentOrder.SubOrders.All(s =>
                    (s.Id == so.Id ? SubOrderStatus.Cancelled : s.Status) == SubOrderStatus.Cancelled);

                if (allCancelled)
                {
                    parentOrder.Status = OrderStatus.Cancelled;
                    parentOrder.UpdatedAt = timestamp;
                }
            }
        }
        else if (string.Equals(status, "DeliveryFailed", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            so.Status = SubOrderStatus.Cancelled;
            so.CancelledAt = timestamp;
            so.UpdatedAt = timestamp;

            _dbContext.OrderStatusHistories.Add(new OrderStatusHistory
            {
                Id = Guid.NewGuid(),
                SubOrderId = so.Id,
                FromStatus = previousStatus,
                ToStatus = SubOrderStatus.Cancelled,
                ActorRole = OrderActorRole.System,
                Reason = formattedReason,
                CreatedAt = timestamp
            });

            var cancellationId = Guid.NewGuid();
            var cancellation = new OrderCancellation
            {
                Id = cancellationId,
                SubOrderId = so.Id,
                CancelledBy = null,
                ActorRole = OrderActorRole.System,
                ReasonCode = CancellationReasonCode.DeliveryFailed,
                ReasonNote = $"Shipment delivery carrier status: {status}.",
                WasPaid = so.PaidAt.HasValue,
                InventoryReleased = false
            };
            _dbContext.OrderCancellations.Add(cancellation);

            if (so.PaidAt.HasValue && so.PaymentMethod == PaymentMethod.Wallet)
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
                    ReasonCode = "DELIVERY_FAILED",
                    ReasonNote = $"Automated refund request created due to delivery carrier status: {status}.",
                    ActorRole = OrderActorRole.System,
                    RequestedBy = null,
                    RequestedAt = timestamp
                };
                _dbContext.OrderRefunds.Add(refund);
            }
            else if (so.PaymentMethod == PaymentMethod.Cod)
            {
                _logger.LogInformation("Delivery-failed SubOrder '{SubOrderId}' has COD payment method. Skipping refund request.", so.Id);
            }
        }
        else
        {
            throw new CommerceDomainException("INVALID_DELIVERY_STATUS", "The provided delivery status is not supported.", 400);
        }

        await _dbContext.SaveChangesAsync(ct);
        _logger.LogInformation("[OrderService] SubOrder '{SubOrderId}' delivery status updated to '{Status}'.", subOrderId, so.Status);
    }

    private async Task UpdateAuctionOrderDeliveryStatusAsync(AuctionOrder auctionOrder, UpdateDeliveryStatusRequest request, CancellationToken ct)
    {
        var status = request.Status?.Trim() ?? "";
        var timestamp = request.DeliveredAt ?? DateTimeOffset.UtcNow;
        var previousStatus = auctionOrder.Status;

        if (!DeliveryStatusTransitions.IsAllowed(auctionOrder.Status, status))
        {
            _logger.LogInformation(
                "[OrderService] Ignoring out-of-order carrier status '{CarrierStatus}' for AuctionOrder '{OrderId}' currently at '{CurrentStatus}'.",
                status, auctionOrder.Id, auctionOrder.Status);
            return;
        }

        if (!string.IsNullOrWhiteSpace(request.CarrierCode)) auctionOrder.CarrierCode = request.CarrierCode;
        if (!string.IsNullOrWhiteSpace(request.TrackingCode)) auctionOrder.TrackingCode = request.TrackingCode;

        string formattedReason = FormatHistoryReason(status, request.Note, request.Location, request.ShipperName, request.ShipperPhone, request.PodUrl);

        if (string.Equals(status, "ReadyToPick", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "Picking", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "Packed", StringComparison.OrdinalIgnoreCase))
        {
            _dbContext.OrderStatusHistories.Add(new OrderStatusHistory
            {
                Id = Guid.NewGuid(),
                AuctionOrderId = auctionOrder.Id,
                FromStatus = previousStatus,
                ToStatus = previousStatus,
                ActorRole = OrderActorRole.System,
                Reason = formattedReason,
                CreatedAt = timestamp
            });
        }
        else if (string.Equals(status, "HandedOver", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(status, "InTransit", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(status, "OutForDelivery", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(status, "Shipped", StringComparison.OrdinalIgnoreCase))
        {
            auctionOrder.Status = SubOrderStatus.Shipped;
            auctionOrder.ShippedAt ??= timestamp;

            _dbContext.OrderStatusHistories.Add(new OrderStatusHistory
            {
                Id = Guid.NewGuid(),
                AuctionOrderId = auctionOrder.Id,
                FromStatus = previousStatus,
                ToStatus = SubOrderStatus.Shipped,
                ActorRole = OrderActorRole.System,
                Reason = formattedReason,
                CreatedAt = timestamp
            });
        }
        else if (string.Equals(status, "Delivered", StringComparison.OrdinalIgnoreCase))
        {
            auctionOrder.Status = SubOrderStatus.Delivered;
            auctionOrder.DeliveredAt = timestamp;

            _dbContext.OrderStatusHistories.Add(new OrderStatusHistory
            {
                Id = Guid.NewGuid(),
                AuctionOrderId = auctionOrder.Id,
                FromStatus = previousStatus,
                ToStatus = SubOrderStatus.Delivered,
                ActorRole = OrderActorRole.System,
                Reason = formattedReason,
                CreatedAt = timestamp
            });
        }
        else if (string.Equals(status, "DeliveryAttemptFailed", StringComparison.OrdinalIgnoreCase))
        {
            _dbContext.OrderStatusHistories.Add(new OrderStatusHistory
            {
                Id = Guid.NewGuid(),
                AuctionOrderId = auctionOrder.Id,
                FromStatus = previousStatus,
                ToStatus = SubOrderStatus.Shipped,
                ActorRole = OrderActorRole.System,
                Reason = formattedReason,
                CreatedAt = timestamp
            });
        }
        else if (string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(status, "DeliveryFailed", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(status, "Returned", StringComparison.OrdinalIgnoreCase))
        {
            var isReturned = string.Equals(status, "Returned", StringComparison.OrdinalIgnoreCase);
            auctionOrder.Status = SubOrderStatus.Cancelled;
            auctionOrder.CancelledAt = timestamp;

            var cancellationId = Guid.NewGuid();
            var cancellation = new OrderCancellation
            {
                Id = cancellationId,
                AuctionOrderId = auctionOrder.Id,
                SubOrderId = null,
                CancelledBy = null,
                ActorRole = OrderActorRole.System,
                ReasonCode = isReturned ? CancellationReasonCode.PackageReturned : CancellationReasonCode.DeliveryFailed,
                ReasonNote = $"Shipment delivery carrier status: {status}.",
                WasPaid = auctionOrder.PaidAt.HasValue,
                InventoryReleased = false
            };
            _dbContext.OrderCancellations.Add(cancellation);

            if (auctionOrder.PaidAt.HasValue)
            {
                var refundAmount = auctionOrder.TotalAmount ?? auctionOrder.WinningAmount;
                var refund = new OrderRefund
                {
                    Id = Guid.NewGuid(),
                    AuctionOrderId = auctionOrder.Id,
                    CancellationId = cancellationId,
                    RefundType = RefundType.Full,
                    Amount = refundAmount,
                    Status = RefundStatus.Requested,
                    ReasonCode = isReturned ? "PACKAGE_RETURNED" : "DELIVERY_FAILED",
                    ReasonNote = $"Automated refund request created due to delivery carrier status: {status}.",
                    ActorRole = OrderActorRole.System,
                    RequestedBy = null,
                    RequestedAt = timestamp
                };
                _dbContext.OrderRefunds.Add(refund);
            }

            _dbContext.OrderStatusHistories.Add(new OrderStatusHistory
            {
                Id = Guid.NewGuid(),
                AuctionOrderId = auctionOrder.Id,
                FromStatus = previousStatus,
                ToStatus = SubOrderStatus.Cancelled,
                ActorRole = OrderActorRole.System,
                Reason = formattedReason,
                CreatedAt = timestamp
            });
        }
        else
        {
            throw new CommerceDomainException("INVALID_DELIVERY_STATUS", "The provided delivery status is not supported.", 400);
        }

        await _dbContext.SaveChangesAsync(ct);
        _logger.LogInformation("[OrderService] AuctionOrder '{AuctionOrderId}' delivery status updated to '{Status}'.", auctionOrder.Id, auctionOrder.Status);
    }

    private static List<TimelineEventDto> MapHistoriesToTimeline(IEnumerable<OrderStatusHistory> histories, SubOrderStatus currentStatus, DateTimeOffset fallbackCreatedAt)
    {
        var sorted = histories.OrderBy(h => h.CreatedAt).ToList();
        var result = new List<TimelineEventDto>();

        foreach (var h in sorted)
        {
            string statusStr;
            string description;
            string? location = null;

            if (!string.IsNullOrWhiteSpace(h.Reason) && h.Reason.StartsWith("[") && h.Reason.Contains("]"))
            {
                var closeIndex = h.Reason.IndexOf(']');
                statusStr = h.Reason.Substring(1, closeIndex - 1).Trim();
                var remainder = h.Reason.Substring(closeIndex + 1).Trim();

                var locIndex = remainder.IndexOf(" | Location: ", StringComparison.OrdinalIgnoreCase);
                if (locIndex >= 0)
                {
                    description = remainder.Substring(0, locIndex).Trim();
                    location = remainder.Substring(locIndex + 13).Trim();
                }
                else
                {
                    description = remainder;
                }
            }
            else
            {
                statusStr = h.ToStatus.ToString();
                description = h.Reason ?? GetDefaultDescription(statusStr);
            }

            if (string.IsNullOrWhiteSpace(description))
            {
                description = GetDefaultDescription(statusStr);
            }

            result.Add(new TimelineEventDto(
                statusStr,
                GetEventTitle(statusStr),
                description,
                h.CreatedAt,
                location
            ));
        }

        if (result.Count == 0)
        {
            var statusName = currentStatus.ToString();
            result.Add(new TimelineEventDto(
                statusName,
                GetEventTitle(statusName),
                GetDefaultDescription(statusName),
                fallbackCreatedAt,
                null
            ));
        }

        return result;
    }

    private static string GetEventTitle(string status) => status switch
    {
        "PendingPayment" => "Order Placed",
        "Paid" => "Payment Received",
        "Confirmed" => "Order Confirmed",
        "ReadyToPick" => "Ready for Pickup",
        "Picking" => "Goods Being Picked",
        "Packed" => "Package Packed",
        "HandedOver" => "Handed Over to Carrier",
        "Shipped" => "Dispatched",
        "InTransit" => "In Transit",
        "OutForDelivery" => "Out for Delivery",
        "DeliveryAttemptFailed" => "Delivery Attempt Failed",
        "Delivered" => "Delivered Successfully",
        "Returned" => "Package Returned to Sender",
        "DeliveryFailed" => "Delivery Failed",
        "Cancelled" => "Order Cancelled",
        "Completed" => "Order Completed",
        _ => status
    };

    private static string GetDefaultDescription(string status) => status switch
    {
        "PendingPayment" => "Awaiting payment confirmation",
        "Paid" => "Payment successfully verified",
        "Confirmed" => "Seller confirmed and is preparing order",
        "ReadyToPick" => "Order is packed and ready for carrier pickup",
        "Picking" => "Warehouse staff is picking products",
        "Packed" => "Order has been packaged and ready for handover",
        "HandedOver" => "Package handed over to logistics carrier",
        "Shipped" => "Package is in transit with carrier",
        "InTransit" => "Package in transit to delivery station",
        "OutForDelivery" => "Package is out for delivery with courier",
        "Delivered" => "Package successfully delivered to recipient",
        "DeliveryAttemptFailed" => "Delivery attempt failed. Courier will reschedule",
        "Returned" => "Package returned to seller warehouse",
        "DeliveryFailed" => "Delivery failed by logistics carrier",
        "Cancelled" => "Order was cancelled",
        "Completed" => "Order completed and escrow settled",
        _ => $"Order status updated to {status}"
    };

    private static string FormatCarrierName(string? carrierCode) => carrierCode?.ToUpperInvariant() switch
    {
        "GHN" => "Giao Hang Nhanh",
        "GHTK" => "Giao Hang Tiet Kiem",
        "VIETTEL" or "VTPOST" => "Viettel Post",
        "VNPOST" => "VNPost",
        _ => carrierCode ?? "Standard Delivery"
    };

    private static string FormatHistoryReason(
        string status,
        string? note,
        string? location,
        string? shipperName = null,
        string? shipperPhone = null,
        string? podUrl = null)
    {
        var desc = !string.IsNullOrWhiteSpace(note) ? note : GetDefaultDescription(status);
        var sb = new System.Text.StringBuilder($"[{status}] {desc}");

        if (!string.IsNullOrWhiteSpace(location))
        {
            sb.Append($" | Location: {location}");
        }

        if (!string.IsNullOrWhiteSpace(shipperName) || !string.IsNullOrWhiteSpace(shipperPhone))
        {
            sb.Append($" | Courier: {shipperName?.Trim()} ({shipperPhone?.Trim()})");
        }

        if (!string.IsNullOrWhiteSpace(podUrl))
        {
            sb.Append($" | POD: {podUrl.Trim()}");
        }

        return sb.ToString();
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

    public async Task<RepayOrderResponse> RepayOrderAsync(Guid currentUserId, Guid orderId, CancellationToken ct = default)
    {
        var order = await _dbContext.Orders
            .Include(o => o.SubOrders)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new EntityNotFoundException("ORDER_NOT_FOUND", "Order", orderId);

        if (order.BuyerId != currentUserId)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to pay for this order.", 403);
        }

        if (order.Status != OrderStatus.PendingPayment)
        {
            throw new CommerceDomainException("INVALID_STATUS", "Order is not in pending payment status.");
        }

        if (order.PaymentDeadlineAt <= DateTimeOffset.UtcNow)
        {
            throw new CommerceDomainException("ORDER_PAYMENT_DEADLINE_EXPIRED", "Payment deadline for this order has expired.");
        }

        var paymentMethod = order.SubOrders.FirstOrDefault()?.PaymentMethod ?? PaymentMethod.Wallet;
        if (paymentMethod != PaymentMethod.Wallet)
        {
            throw new CommerceDomainException("INVALID_PAYMENT_METHOD", "COD orders cannot be paid online.");
        }

        var paymentResult = await _walletClient.PayOrderAsync(order.Id, order.BuyerId, order.TotalAmount, cancellationToken: ct);
        if (paymentResult?.IsSuccess != true)
        {
            if (paymentResult?.ErrorCode == "INSUFFICIENT_WALLET_BALANCE" || paymentResult?.ErrorCode == "INSUFFICIENT_BALANCE")
            {
                throw new CommerceDomainException("INSUFFICIENT_WALLET_BALANCE", "Your wallet balance is insufficient to pay for this order.");
            }

            throw new CommerceDomainException(
                paymentResult?.ErrorCode ?? "WALLET_PAYMENT_FAILED",
                paymentResult?.ErrorMessage ?? "Payment via wallet service failed.");
        }

        var now = DateTimeOffset.UtcNow;
        order.Status = OrderStatus.Paid;
        order.PaymentAttemptCount++;
        order.UpdatedAt = now;

        if (!string.IsNullOrWhiteSpace(paymentResult.TransactionId) &&
            Guid.TryParse(paymentResult.TransactionId, out var parsedTxId))
        {
            order.WalletTransactionId = parsedTxId;
        }

        foreach (var so in order.SubOrders)
        {
            var prevStatus = so.Status;
            so.Status = SubOrderStatus.Paid;
            so.PaidAt = now;
            so.SellerRespondDeadlineAt = so.PaidAt.Value.Add(OrderPaidNotices.SellerAnswerWindow);
            so.UpdatedAt = now;
            if (order.WalletTransactionId.HasValue)
            {
                so.WalletTransactionId = order.WalletTransactionId;
            }

            OrderPaidNotices.Enqueue(_dbContext, _notificationSettings, so);

            _dbContext.OrderStatusHistories.Add(new OrderStatusHistory
            {
                Id = Guid.NewGuid(),
                SubOrderId = so.Id,
                FromStatus = prevStatus,
                ToStatus = SubOrderStatus.Paid,
                ActorRole = OrderActorRole.Buyer,
                Reason = "Order repayment succeeded via wallet.",
                CreatedAt = now
            });
        }

        await _dbContext.SaveChangesAsync(ct);

        bool stockCommitted = await _fulfillmentClient.CommitStockAsync(order.Id, ct);
        if (!stockCommitted)
        {
            _logger.LogError("CRITICAL: Order '{OrderId}' was repaid via Wallet, but stock commit failed at Fulfillment Service. Flagged for reconciliation.", order.Id);
        }

        return new RepayOrderResponse(
            order.Id,
            order.Status.ToString(),
            order.TotalAmount,
            now,
            order.WalletTransactionId);
    }

    public async Task<List<OrderRefundResponse>> GetOrderRefundsAsync(Guid currentUserId, Guid orderId, CancellationToken ct = default)
    {
        var order = await _dbContext.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new EntityNotFoundException("ORDER_NOT_FOUND", "Order", orderId);

        if (order.BuyerId != currentUserId)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to view refunds for this order.", 403);
        }

        var refunds = await _dbContext.OrderRefunds
            .AsNoTracking()
            .Where(r => r.OrderId == orderId)
            .OrderByDescending(r => r.RequestedAt)
            .Select(r => new OrderRefundResponse(
                r.Id,
                r.OrderId,
                r.SubOrderId,
                r.Amount,
                r.RefundType.ToString(),
                r.Status.ToString(),
                r.ReasonCode,
                r.ReasonNote,
                r.RequestedAt,
                r.CompletedAt))
            .ToListAsync(ct);

        return refunds;
    }

    public async Task<SubOrderFinancialBreakdownResponse> GetSubOrderFinancialBreakdownAsync(Guid currentUserId, Guid subOrderId, CancellationToken ct = default)
    {
        var so = await _dbContext.SubOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == subOrderId, ct)
            ?? throw new EntityNotFoundException("SUB_ORDER_NOT_FOUND", "SubOrder", subOrderId);

        if (so.SellerId != currentUserId && so.BuyerId != currentUserId)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to view the financial breakdown of this sub-order.", 403);
        }

        return new SubOrderFinancialBreakdownResponse(
            so.Id,
            so.SubOrderNo,
            so.SellerId,
            so.BuyerId,
            so.TotalAmount,
            so.ItemsAmount,
            so.ProductDiscountAmount,
            so.ShopDiscountAmount,
            so.SystemDiscountAmount,
            so.ShippingFeeAmount,
            so.PlatformFeeAmount,
            so.SellerReceiveAmount,
            so.IsEscrowSettled,
            so.Status.ToString(),
            so.PaymentMethod.ToString(),
            so.CompletedAt);
    }

    public async Task<ChangeShippingAddressResponse> ChangeSubOrderShippingAddressAsync(
        Guid currentUserId,
        Guid subOrderId,
        ChangeShippingAddressRequest request,
        bool isAdminOrStaff = false,
        CancellationToken ct = default)
    {
        var subOrder = await _dbContext.SubOrders
            .Include(s => s.Items)
            .Include(s => s.Order)
            .FirstOrDefaultAsync(s => s.Id == subOrderId && !s.IsDeleted, ct)
            ?? throw new EntityNotFoundException("SUB_ORDER_NOT_FOUND", "SubOrder", subOrderId);

        if (!isAdminOrStaff && subOrder.BuyerId != currentUserId)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to modify this sub-order.", 403);
        }

        if (subOrder.Status is SubOrderStatus.Shipping or SubOrderStatus.Delivered or SubOrderStatus.Completed or SubOrderStatus.Cancelled or SubOrderStatus.Refunded ||
            !string.IsNullOrWhiteSpace(subOrder.TrackingCode))
        {
            throw new CommerceDomainException("CANNOT_CHANGE_ADDRESS_AFTER_SHIPPING", "Shipping address cannot be changed after the order has been shipped or completed.", 400);
        }

        var (resolvedAddress, resolvedPhone) = await ResolveShippingAddressAsync(subOrder.BuyerId, request.AddressId, request.ShippingAddress, request.ContactPhone, ct);

        var quoteItems = subOrder.Items
            .Where(i => !i.IsDeleted)
            .Select(i => new ShippingQuoteItem(
                i.SkuId,
                i.Quantity,
                Math.Max(100, i.Quantity * 200)
            )).ToList();

        var quoteRequest = new ShopShippingQuoteRequest(
            SellerId: subOrder.SellerId,
            GhnDistrictId: resolvedAddress.GhnDistrictId!.Value,
            GhnWardCode: resolvedAddress.GhnWardCode!,
            Items: quoteItems
        );

        var newShippingFee = await _fulfillmentClient.CalculateShopShippingFeeAsync(quoteRequest, ct);
        var oldShippingFee = subOrder.ShippingFeeAmount;

        if (subOrder.Status is SubOrderStatus.Paid or SubOrderStatus.Confirmed)
        {
            if (newShippingFee > oldShippingFee)
            {
                throw new CommerceDomainException("SHIPPING_FEE_INCREASED", "Shipping fee to new address exceeds prepaid amount. Please cancel and reorder.", 400);
            }
        }
        else if (subOrder.Status == SubOrderStatus.PendingPayment)
        {
            var feeDifference = newShippingFee - oldShippingFee;
            subOrder.ShippingFeeAmount = newShippingFee;
            subOrder.TotalAmount += feeDifference;

            if (subOrder.Order != null)
            {
                subOrder.Order.TotalAmount += feeDifference;
            }
        }

        var fullAddressText = $"{resolvedAddress.ReceiverName}, {resolvedAddress.Street}, {resolvedAddress.Ward}, {resolvedAddress.District}, {resolvedAddress.Province}";
        subOrder.ShippingAddress = new ShippingAddressSnapshot
        {
            ReceiverName = resolvedAddress.ReceiverName,
            ContactPhone = resolvedPhone,
            Street = resolvedAddress.Street,
            Ward = resolvedAddress.Ward,
            District = resolvedAddress.District,
            Province = resolvedAddress.Province,
            GhnDistrictId = resolvedAddress.GhnDistrictId ?? 0,
            GhnWardCode = resolvedAddress.GhnWardCode,
            FullAddressText = fullAddressText
        };
        subOrder.ContactPhone = resolvedPhone;

        var now = DateTimeOffset.UtcNow;
        _dbContext.OrderStatusHistories.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrder.Id,
            FromStatus = subOrder.Status,
            ToStatus = subOrder.Status,
            ChangedBy = currentUserId,
            ActorRole = isAdminOrStaff ? OrderActorRole.SupportStaff : OrderActorRole.Buyer,
            Reason = $"Shipping address updated to: {fullAddressText}",
            CreatedAt = now
        });

        await _dbContext.SaveChangesAsync(ct);

        return new ChangeShippingAddressResponse(
            TargetId: subOrder.Id,
            TargetType: "SubOrder",
            NewAddressText: fullAddressText,
            OldShippingFee: oldShippingFee,
            NewShippingFee: newShippingFee,
            TotalAmount: subOrder.TotalAmount,
            UpdatedAt: now
        );
    }

    public async Task<ChangeShippingAddressResponse> ChangeOrderShippingAddressAsync(
        Guid currentUserId,
        Guid orderId,
        ChangeShippingAddressRequest request,
        bool isAdminOrStaff = false,
        CancellationToken ct = default)
    {
        var order = await _dbContext.Orders
            .Include(o => o.SubOrders)
                .ThenInclude(s => s.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId && !o.IsDeleted, ct)
            ?? throw new EntityNotFoundException("ORDER_NOT_FOUND", "Order", orderId);

        if (!isAdminOrStaff && order.BuyerId != currentUserId)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to modify this order.", 403);
        }

        var activeSubOrders = order.SubOrders.Where(s => !s.IsDeleted).ToList();
        if (activeSubOrders.Count == 0)
        {
            throw new CommerceDomainException("ORDER_EMPTY", "Order contains no active sub-orders.", 400);
        }

        foreach (var so in activeSubOrders)
        {
            if (so.Status is SubOrderStatus.Shipping or SubOrderStatus.Delivered or SubOrderStatus.Completed or SubOrderStatus.Cancelled or SubOrderStatus.Refunded ||
                !string.IsNullOrWhiteSpace(so.TrackingCode))
            {
                throw new CommerceDomainException("CANNOT_CHANGE_ADDRESS_AFTER_SHIPPING", "Shipping address cannot be changed after the order has been shipped or completed.", 400);
            }
        }

        var (resolvedAddress, resolvedPhone) = await ResolveShippingAddressAsync(order.BuyerId, request.AddressId, request.ShippingAddress, request.ContactPhone, ct);

        decimal totalOldFee = 0m;
        decimal totalNewFee = 0m;
        var subOrderCalculations = new List<(SubOrder SubOrder, decimal OldFee, decimal NewFee)>();

        foreach (var so in activeSubOrders)
        {
            var quoteItems = so.Items
                .Where(i => !i.IsDeleted)
                .Select(i => new ShippingQuoteItem(
                    i.SkuId,
                    i.Quantity,
                    Math.Max(100, i.Quantity * 200)
                )).ToList();

            var quoteRequest = new ShopShippingQuoteRequest(
                SellerId: so.SellerId,
                GhnDistrictId: resolvedAddress.GhnDistrictId!.Value,
                GhnWardCode: resolvedAddress.GhnWardCode!,
                Items: quoteItems
            );

            var newFee = await _fulfillmentClient.CalculateShopShippingFeeAsync(quoteRequest, ct);
            var oldFee = so.ShippingFeeAmount;

            if (so.Status is SubOrderStatus.Paid or SubOrderStatus.Confirmed && newFee > oldFee)
            {
                throw new CommerceDomainException("SHIPPING_FEE_INCREASED", "Shipping fee to new address exceeds prepaid amount. Please cancel and reorder.", 400);
            }

            totalOldFee += oldFee;
            totalNewFee += newFee;
            subOrderCalculations.Add((so, oldFee, newFee));
        }

        var now = DateTimeOffset.UtcNow;
        var fullAddressText = $"{resolvedAddress.ReceiverName}, {resolvedAddress.Street}, {resolvedAddress.Ward}, {resolvedAddress.District}, {resolvedAddress.Province}";

        foreach (var (so, oldFee, newFee) in subOrderCalculations)
        {
            if (so.Status == SubOrderStatus.PendingPayment)
            {
                var feeDifference = newFee - oldFee;
                so.ShippingFeeAmount = newFee;
                so.TotalAmount += feeDifference;
                order.TotalAmount += feeDifference;
            }

            so.ShippingAddress = new ShippingAddressSnapshot
            {
                ReceiverName = resolvedAddress.ReceiverName,
                ContactPhone = resolvedPhone,
                Street = resolvedAddress.Street,
                Ward = resolvedAddress.Ward,
                District = resolvedAddress.District,
                Province = resolvedAddress.Province,
                GhnDistrictId = resolvedAddress.GhnDistrictId ?? 0,
                GhnWardCode = resolvedAddress.GhnWardCode,
                FullAddressText = fullAddressText
            };
            so.ContactPhone = resolvedPhone;

            _dbContext.OrderStatusHistories.Add(new OrderStatusHistory
            {
                Id = Guid.NewGuid(),
                SubOrderId = so.Id,
                FromStatus = so.Status,
                ToStatus = so.Status,
                ChangedBy = currentUserId,
                ActorRole = isAdminOrStaff ? OrderActorRole.SupportStaff : OrderActorRole.Buyer,
                Reason = $"Shipping address updated to: {fullAddressText}",
                CreatedAt = now
            });
        }

        await _dbContext.SaveChangesAsync(ct);

        return new ChangeShippingAddressResponse(
            TargetId: order.Id,
            TargetType: "Order",
            NewAddressText: fullAddressText,
            OldShippingFee: totalOldFee,
            NewShippingFee: totalNewFee,
            TotalAmount: order.TotalAmount,
            UpdatedAt: now
        );
    }

    public async Task<PagedData<RefundDetailResponse>> ListRefundsAsync(
        Guid currentUserId,
        int page,
        int pageSize,
        RefundStatus? status = null,
        Guid? subOrderId = null,
        bool isAdminOrStaff = false,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _dbContext.OrderRefunds
            .AsNoTracking()
            .Include(r => r.SubOrder)
            .Where(r => !r.IsDeleted);

        if (!isAdminOrStaff)
        {
            query = query.Where(r => r.RequestedBy == currentUserId ||
                                     (r.SubOrder != null && (r.SubOrder.BuyerId == currentUserId || r.SubOrder.SellerId == currentUserId)));
        }

        if (status.HasValue)
        {
            query = query.Where(r => r.Status == status.Value);
        }

        if (subOrderId.HasValue && subOrderId.Value != Guid.Empty)
        {
            query = query.Where(r => r.SubOrderId == subOrderId.Value);
        }

        var totalCount = await query.CountAsync(ct);

        var refunds = await query
            .OrderByDescending(r => r.RequestedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = refunds.Select(r => new RefundDetailResponse(
            Id: r.Id,
            SubOrderId: r.SubOrderId ?? Guid.Empty,
            SubOrderNo: r.SubOrder?.SubOrderNo ?? string.Empty,
            BuyerId: r.SubOrder?.BuyerId ?? r.RequestedBy ?? Guid.Empty,
            SellerId: r.SubOrder?.SellerId ?? Guid.Empty,
            Amount: r.Amount,
            Reason: r.ReasonNote ?? r.ReasonCode,
            Status: r.Status.ToString(),
            SellerNote: r.ReasonNote,
            RejectReason: r.FailureCode,
            RequestedAt: r.RequestedAt,
            CompletedAt: r.CompletedAt
        )).ToList();

        return new PagedData<RefundDetailResponse>(items, PaginationMetadata.Create(page, pageSize, totalCount));
    }

    public async Task<RefundDetailResponse> GetRefundDetailAsync(
        Guid currentUserId,
        Guid refundId,
        bool isAdminOrStaff = false,
        CancellationToken ct = default)
    {
        var refund = await _dbContext.OrderRefunds
            .AsNoTracking()
            .Include(r => r.SubOrder)
            .FirstOrDefaultAsync(r => r.Id == refundId && !r.IsDeleted, ct)
            ?? throw new EntityNotFoundException("REFUND_NOT_FOUND", "OrderRefund", refundId);

        if (!isAdminOrStaff &&
            refund.RequestedBy != currentUserId &&
            refund.SubOrder?.BuyerId != currentUserId &&
            refund.SubOrder?.SellerId != currentUserId)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to view this refund request.", 403);
        }

        return new RefundDetailResponse(
            Id: refund.Id,
            SubOrderId: refund.SubOrderId ?? Guid.Empty,
            SubOrderNo: refund.SubOrder?.SubOrderNo ?? string.Empty,
            BuyerId: refund.SubOrder?.BuyerId ?? refund.RequestedBy ?? Guid.Empty,
            SellerId: refund.SubOrder?.SellerId ?? Guid.Empty,
            Amount: refund.Amount,
            Reason: refund.ReasonNote ?? refund.ReasonCode,
            Status: refund.Status.ToString(),
            SellerNote: refund.ReasonNote,
            RejectReason: refund.FailureCode,
            RequestedAt: refund.RequestedAt,
            CompletedAt: refund.CompletedAt
        );
    }

    public async Task<RefundDetailResponse> ApproveRefundAsync(
        Guid currentUserId,
        Guid refundId,
        ApproveRefundRequest request,
        bool isAdminOrStaff = false,
        CancellationToken ct = default)
    {
        var refund = await _dbContext.OrderRefunds
            .Include(r => r.SubOrder)
            .FirstOrDefaultAsync(r => r.Id == refundId && !r.IsDeleted, ct)
            ?? throw new EntityNotFoundException("REFUND_NOT_FOUND", "OrderRefund", refundId);

        if (!isAdminOrStaff && refund.SubOrder?.SellerId != currentUserId)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to approve this refund request.", 403);
        }

        if (refund.Status != RefundStatus.Requested)
        {
            throw new CommerceDomainException("INVALID_REFUND_STATUS", "Only pending refund requests can be approved.", 400);
        }

        var subOrder = refund.SubOrder
            ?? throw new EntityNotFoundException("SUB_ORDER_NOT_FOUND", "SubOrder", refund.SubOrderId ?? Guid.Empty);

        var walletSuccess = await _walletClient.RefundSubOrderPaymentAsync(
            subOrder.Id,
            subOrder.BuyerId.ToString(),
            refund.Amount,
            request.Note ?? "Refund approved by seller",
            null,
            ct);

        if (!walletSuccess)
        {
            throw new CommerceDomainException("WALLET_REFUND_FAILED", "Failed to process refund payment via wallet service.", 400);
        }

        var now = DateTimeOffset.UtcNow;
        refund.Status = RefundStatus.Completed;
        refund.CompletedAt = now;
        if (!string.IsNullOrWhiteSpace(request.Note))
        {
            refund.ReasonNote = string.IsNullOrWhiteSpace(refund.ReasonNote)
                ? $"Note: {request.Note}"
                : $"{refund.ReasonNote} | Note: {request.Note}";
        }

        var fromStatus = subOrder.Status;
        subOrder.Status = SubOrderStatus.Refunded;
        subOrder.CompletedAt = now;

        _dbContext.OrderStatusHistories.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrder.Id,
            FromStatus = fromStatus,
            ToStatus = SubOrderStatus.Refunded,
            ChangedBy = currentUserId,
            ActorRole = isAdminOrStaff ? OrderActorRole.SupportStaff : OrderActorRole.Seller,
            Reason = $"Refund approved. Note: {request.Note}",
            CreatedAt = now
        });

        await _dbContext.SaveChangesAsync(ct);

        return new RefundDetailResponse(
            Id: refund.Id,
            SubOrderId: subOrder.Id,
            SubOrderNo: subOrder.SubOrderNo,
            BuyerId: subOrder.BuyerId,
            SellerId: subOrder.SellerId,
            Amount: refund.Amount,
            Reason: refund.ReasonNote ?? refund.ReasonCode,
            Status: refund.Status.ToString(),
            SellerNote: request.Note,
            RejectReason: null,
            RequestedAt: refund.RequestedAt,
            CompletedAt: refund.CompletedAt
        );
    }

    public async Task<RefundDetailResponse> RejectRefundAsync(
        Guid currentUserId,
        Guid refundId,
        RejectRefundRequest request,
        bool isAdminOrStaff = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new CommerceDomainException("VAL_INVALID_INPUT", "Rejection reason cannot be empty.", 400);
        }

        var refund = await _dbContext.OrderRefunds
            .Include(r => r.SubOrder)
            .FirstOrDefaultAsync(r => r.Id == refundId && !r.IsDeleted, ct)
            ?? throw new EntityNotFoundException("REFUND_NOT_FOUND", "OrderRefund", refundId);

        if (!isAdminOrStaff && refund.SubOrder?.SellerId != currentUserId)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to reject this refund request.", 403);
        }

        if (refund.Status != RefundStatus.Requested)
        {
            throw new CommerceDomainException("INVALID_REFUND_STATUS", "Only pending refund requests can be rejected.", 400);
        }

        var subOrder = refund.SubOrder
            ?? throw new EntityNotFoundException("SUB_ORDER_NOT_FOUND", "SubOrder", refund.SubOrderId ?? Guid.Empty);

        var now = DateTimeOffset.UtcNow;
        refund.Status = RefundStatus.Rejected;
        refund.CompletedAt = now;
        refund.FailureCode = request.Reason;
        if (!string.IsNullOrWhiteSpace(request.Note))
        {
            refund.ReasonNote = string.IsNullOrWhiteSpace(refund.ReasonNote)
                ? $"Note: {request.Note}"
                : $"{refund.ReasonNote} | Note: {request.Note}";
        }

        _dbContext.OrderStatusHistories.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrder.Id,
            FromStatus = subOrder.Status,
            ToStatus = subOrder.Status,
            ChangedBy = currentUserId,
            ActorRole = isAdminOrStaff ? OrderActorRole.SupportStaff : OrderActorRole.Seller,
            Reason = $"Refund rejected. Reason: {request.Reason}",
            CreatedAt = now
        });

        await _dbContext.SaveChangesAsync(ct);

        return new RefundDetailResponse(
            Id: refund.Id,
            SubOrderId: subOrder.Id,
            SubOrderNo: subOrder.SubOrderNo,
            BuyerId: subOrder.BuyerId,
            SellerId: subOrder.SellerId,
            Amount: refund.Amount,
            Reason: refund.ReasonNote ?? refund.ReasonCode,
            Status: refund.Status.ToString(),
            SellerNote: request.Note,
            RejectReason: request.Reason,
            RequestedAt: refund.RequestedAt,
            CompletedAt: refund.CompletedAt
        );
    }

    public async Task<PagedData<OrderSummaryResponse>> ListAdminOrdersAsync(AdminOrderFilterRequest filter, CancellationToken ct = default)
    {
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var query = _dbContext.Orders
            .AsNoTracking()
            .Where(o => !o.IsDeleted);

        if (filter.BuyerId.HasValue && filter.BuyerId.Value != Guid.Empty)
        {
            query = query.Where(o => o.BuyerId == filter.BuyerId.Value);
        }

        if (filter.SellerId.HasValue && filter.SellerId.Value != Guid.Empty)
        {
            query = query.Where(o => o.SubOrders.Any(s => s.SellerId == filter.SellerId.Value && !s.IsDeleted));
        }

        if (filter.Status.HasValue)
        {
            query = query.Where(o => o.Status == filter.Status.Value);
        }

        if (filter.FromDate.HasValue)
        {
            query = query.Where(o => o.CreatedAt >= filter.FromDate.Value);
        }

        if (filter.ToDate.HasValue)
        {
            query = query.Where(o => o.CreatedAt <= filter.ToDate.Value);
        }

        var totalCount = await query.CountAsync(ct);

        var rawItems = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new
            {
                o.Id,
                o.TotalAmount,
                Status = o.Status.ToString(),
                o.PaymentDeadlineAt,
                SubOrderCount = o.SubOrders.Count(s => !s.IsDeleted),
                o.CreatedAt
            })
            .ToListAsync(ct);

        var items = rawItems.Select(o => new OrderSummaryResponse(
            o.Id,
            $"ORD-{o.Id.ToString()[..8].ToUpperInvariant()}",
            o.TotalAmount,
            o.Status,
            o.PaymentDeadlineAt,
            o.SubOrderCount,
            o.CreatedAt
        )).ToList();

        return new PagedData<OrderSummaryResponse>(items, PaginationMetadata.Create(page, pageSize, totalCount));
    }

    public async Task<PagedData<SubOrderSummaryResponse>> ListAdminSubOrdersAsync(AdminSubOrderFilterRequest filter, CancellationToken ct = default)
    {
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var query = _dbContext.SubOrders
            .AsNoTracking()
            .Where(s => !s.IsDeleted);

        if (filter.BuyerId.HasValue && filter.BuyerId.Value != Guid.Empty)
        {
            query = query.Where(s => s.BuyerId == filter.BuyerId.Value);
        }

        if (filter.SellerId.HasValue && filter.SellerId.Value != Guid.Empty)
        {
            query = query.Where(s => s.SellerId == filter.SellerId.Value);
        }

        if (filter.Status.HasValue)
        {
            query = query.Where(s => s.Status == filter.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.TrackingCode))
        {
            query = query.Where(s => s.TrackingCode == filter.TrackingCode);
        }

        if (filter.FromDate.HasValue)
        {
            query = query.Where(s => s.CreatedAt >= filter.FromDate.Value);
        }

        if (filter.ToDate.HasValue)
        {
            query = query.Where(s => s.CreatedAt <= filter.ToDate.Value);
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new SubOrderSummaryResponse(
                s.Id,
                s.SubOrderNo,
                s.OrderId,
                s.BuyerId,
                s.SellerId,
                s.Status.ToString(),
                s.PaymentMethod.ToString(),
                s.ItemsAmount,
                s.ShippingFeeAmount,
                s.TotalAmount,
                s.SellerReceiveAmount,
                s.Items.Count(i => !i.IsDeleted),
                s.CreatedAt,
                s.SellerAcceptedAt,
                s.ShippedAt,
                s.DeliveredAt,
                s.CompletedAt,
                s.CancelledAt
            ))
            .ToListAsync(ct);

        return new PagedData<SubOrderSummaryResponse>(items, PaginationMetadata.Create(page, pageSize, totalCount));
    }
}
