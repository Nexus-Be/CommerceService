namespace Commerce.Application.Services.Cart;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Commerce.Application.Interfaces;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;

public sealed class CartService : ICartService
{
    private readonly ICommerceDbContext _dbContext;
    private readonly ICatalogClient _catalogClient;
    private readonly IFulfillmentClient _fulfillmentClient;

    public CartService(
        ICommerceDbContext dbContext,
        ICatalogClient catalogClient,
        IFulfillmentClient fulfillmentClient)
    {
        _dbContext = dbContext;
        _catalogClient = catalogClient;
        _fulfillmentClient = fulfillmentClient;
    }

    public async Task<CartDto> GetActiveCartByBuyerIdAsync(Guid buyerId, CancellationToken cancellationToken = default)
    {
        var cart = await GetActiveCartEntityAsync(buyerId, asNoTracking: true, cancellationToken)
            ?? throw new EntityNotFoundException("CART_NOT_FOUND", "Cart", buyerId);

        var (stockResults, catalogResults) = await CheckStockAndCatalogForCartItemsAsync(cart.Items, cancellationToken);
        return MapToDto(cart, stockResults, catalogResults);
    }

    public async Task<CartDto> GetOrCreateActiveCartAsync(Guid buyerId, CancellationToken cancellationToken = default)
    {
        var cart = await GetActiveCartEntityAsync(buyerId, asNoTracking: false, cancellationToken);
        if (cart == null)
        {
            cart = new Cart
            {
                Id = Guid.NewGuid(),
                BuyerId = buyerId,
                Status = CartStatus.Active,
                ItemCount = 0,
                SubtotalAmount = 0m,
                LastActivityAt = DateTimeOffset.UtcNow,
                RowVersion = 1
            };

            _dbContext.Carts.Add(cart);
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // In case of concurrent creation race, fallback to query existing
                cart = await GetActiveCartEntityAsync(buyerId, asNoTracking: false, cancellationToken);
                if (cart == null) throw;
            }
        }

        var (stockResults, catalogResults) = await CheckStockAndCatalogForCartItemsAsync(cart.Items, cancellationToken);
        return MapToDto(cart, stockResults, catalogResults);
    }

    public async Task<CartDto> AddItemAsync(Guid buyerId, AddCartItemRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Quantity <= 0)
        {
            throw new CommerceDomainException("VAL_INVALID_INPUT", "Quantity must be greater than zero.");
        }

        var skuInfo = await _catalogClient.GetSkuDetailsAsync(request.SkuId, cancellationToken);
        if (skuInfo == null)
        {
            throw new CommerceDomainException("CATALOG_SKU_NOT_FOUND", "The specified product SKU was not found in catalog.", 404);
        }

        if (!skuInfo.IsPurchasable)
        {
            throw new CommerceDomainException("CATALOG_SKU_INACTIVE", skuInfo.ErrorReason ?? "The specified product SKU is unavailable or out of stock.", 400);
        }

        // Check tồn kho tại Fulfillment Service trước khi thêm vào giỏ hàng (CART-04)
        var isStockAvailable = await _fulfillmentClient.CheckStockAvailabilityAsync(request.SkuId, request.Quantity, cancellationToken);
        if (!isStockAvailable)
        {
            throw new CommerceDomainException("INSUFFICIENT_STOCK", "Insufficient stock available in warehouse.", 400);
        }

        var cart = await GetActiveCartEntityAsync(buyerId, asNoTracking: false, cancellationToken);
        if (cart == null)
        {
            cart = new Cart
            {
                Id = Guid.NewGuid(),
                BuyerId = buyerId,
                Status = CartStatus.Active,
                ItemCount = 0,
                SubtotalAmount = 0m,
                LastActivityAt = DateTimeOffset.UtcNow,
                RowVersion = 1
            };
            _dbContext.Carts.Add(cart);
        }

        var isExistingItem = cart.Items.Any(i => i.SkuId == skuInfo.SkuId);

        var cartItem = cart.AddOrUpdateItem(
            productId: request.ProductId ?? skuInfo.ProductId,
            skuId: skuInfo.SkuId,
            sellerId: skuInfo.SellerId,
            productName: skuInfo.ProductName,
            quantity: request.Quantity,
            unitPrice: skuInfo.UnitPrice);

        if (!isExistingItem)
        {
            _dbContext.CartItems.Add(cartItem);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException dbEx) when (IsUniqueConstraintViolation(dbEx))
        {
            (_dbContext as DbContext)?.ChangeTracker.Clear();

            var refreshedCart = await GetActiveCartEntityAsync(buyerId, asNoTracking: false, cancellationToken)
                ?? throw new CommerceDomainException("CART_NOT_FOUND", "Active cart not found for the user.", 404);

            refreshedCart.AddOrUpdateItem(
                productId: request.ProductId ?? skuInfo.ProductId,
                skuId: skuInfo.SkuId,
                sellerId: skuInfo.SellerId,
                productName: skuInfo.ProductName,
                quantity: request.Quantity,
                unitPrice: skuInfo.UnitPrice);

            await _dbContext.SaveChangesAsync(cancellationToken);
            return MapToDto(refreshedCart);
        }

        return MapToDto(cart);
    }

    public async Task<CartDto> UpdateItemQuantityAsync(Guid buyerId, Guid cartItemId, UpdateCartItemQuantityRequest request, CancellationToken cancellationToken = default)
    {
        var cart = await GetActiveCartEntityAsync(buyerId, asNoTracking: false, cancellationToken)
            ?? throw new CommerceDomainException("CART_NOT_FOUND", "Active cart not found for the user.", 404);

        var item = cart.Items.FirstOrDefault(i => i.Id == cartItemId)
            ?? throw new CommerceDomainException("CART_ITEM_NOT_FOUND", "Cart item not found in the current cart.", 404);

        // Bắt buộc kiểm tra tồn kho Fulfillment trước khi cho phép cập nhật số lượng (BUG-C)
        var isStockAvailable = await _fulfillmentClient.CheckStockAvailabilityAsync(item.SkuId, request.Quantity, cancellationToken);
        if (!isStockAvailable)
        {
            throw new CommerceDomainException("INSUFFICIENT_STOCK", "Requested quantity exceeds available warehouse stock.", 400);
        }

        cart.UpdateItemQuantity(cartItemId, request.Quantity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapToDto(cart);
    }

    public async Task<CartDto> RemoveItemAsync(Guid buyerId, Guid cartItemId, CancellationToken cancellationToken = default)
    {
        var cart = await GetActiveCartEntityAsync(buyerId, asNoTracking: false, cancellationToken)
            ?? throw new CommerceDomainException("CART_NOT_FOUND", "Active cart not found for the user.", 404);

        cart.RemoveItem(cartItemId);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapToDto(cart);
    }

    public async Task<CartDto> BulkRemoveOrClearAsync(Guid buyerId, BulkRemoveCartItemsRequest request, CancellationToken cancellationToken = default)
    {
        var cart = await GetActiveCartEntityAsync(buyerId, asNoTracking: false, cancellationToken)
            ?? throw new CommerceDomainException("CART_NOT_FOUND", "Active cart not found for the user.", 404);

        if (request.ClearAll || request.CartItemIds == null || request.CartItemIds.Count == 0)
        {
            cart.ClearItems();
        }
        else
        {
            cart.BulkRemoveItems(request.CartItemIds);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapToDto(cart);
    }

    public async Task<CartSummaryResponse> GetCartSummaryAsync(Guid buyerId, CancellationToken cancellationToken = default)
    {
        var cart = await _dbContext.Carts
            .AsNoTracking()
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.BuyerId == buyerId && c.Status == CartStatus.Active, cancellationToken);

        if (cart == null)
        {
            return new CartSummaryResponse(Guid.Empty, 0, 0, 0m);
        }

        var totalQty = cart.Items.Sum(i => i.Quantity);
        return new CartSummaryResponse(cart.Id, cart.ItemCount, totalQty, cart.SubtotalAmount);
    }

    public async Task<CalculateSelectedCartResponse> CalculateSelectedItemsAsync(Guid buyerId, CalculateSelectedCartItemsRequest request, CancellationToken cancellationToken = default)
    {
        if (request.SelectedItemIds == null || request.SelectedItemIds.Count == 0)
        {
            return new CalculateSelectedCartResponse(0, 0, 0m, Array.Empty<CalculatedCartItem>());
        }

        var cart = await GetActiveCartEntityAsync(buyerId, asNoTracking: true, cancellationToken);
        if (cart == null)
        {
            return new CalculateSelectedCartResponse(0, 0, 0m, Array.Empty<CalculatedCartItem>());
        }

        var selectedIds = request.SelectedItemIds.ToHashSet();
        var selectedItems = cart.Items.Where(i => selectedIds.Contains(i.Id)).ToList();

        var stockResults = await CheckStockForCartItemsAsync(selectedItems, cancellationToken);

        var calculatedItems = selectedItems.Select(i =>
        {
            var stock = stockResults != null && stockResults.TryGetValue(i.SkuId, out var s) ? s : null;
            var isAvailable = stock?.IsAvailable ?? true;
            var availableStock = stock?.AvailableStock;
            string? stockWarning = null;

            if (!isAvailable)
            {
                stockWarning = stock?.DisabledReason ?? "OUT_OF_STOCK";
            }
            else if (availableStock.HasValue && availableStock.Value < i.Quantity)
            {
                isAvailable = false;
                stockWarning = "INSUFFICIENT_STOCK";
            }

            return new CalculatedCartItem(
                CartItemId: i.Id,
                SkuId: i.SkuId,
                ProductName: i.ProductNameSnapshot,
                Quantity: i.Quantity,
                UnitPrice: i.UnitPriceSnapshot,
                LineTotal: i.Quantity * i.UnitPriceSnapshot,
                IsAvailable: isAvailable,
                AvailableStock: availableStock,
                StockWarning: stockWarning
            );
        }).ToList();

        var billableItems = calculatedItems.Where(i => i.IsAvailable).ToList();
        var totalQty = billableItems.Sum(i => i.Quantity);
        var subtotal = billableItems.Sum(i => i.LineTotal);

        return new CalculateSelectedCartResponse(
            SelectedItemCount: calculatedItems.Count,
            TotalQuantity: totalQty,
            SubtotalAmount: subtotal,
            Items: calculatedItems);
    }

    public async Task<ValidateCartResponse> ValidateCartAsync(Guid buyerId, ValidateCartRequest request, CancellationToken cancellationToken = default)
    {
        var cart = await GetActiveCartEntityAsync(buyerId, asNoTracking: false, cancellationToken);
        if (cart == null || cart.Items.Count == 0)
        {
            return new ValidateCartResponse(true, false, false, Array.Empty<CartItemIssue>());
        }

        var targetItems = request.SelectedItemIds != null && request.SelectedItemIds.Count > 0
            ? cart.Items.Where(i => request.SelectedItemIds.Contains(i.Id)).ToList()
            : cart.Items.ToList();

        var issues = new List<CartItemIssue>();
        var hasPriceChanges = false;
        var hasStockIssues = false;
        var hasDbUpdates = false;

        // Parallel batch check from Fulfillment and Catalog (Anti N+1 & Concurrent Latency Optimization)
        var skuIds = targetItems.Select(i => i.SkuId).Distinct().ToList();

        var stockTask = _fulfillmentClient.BatchCheckStockAsync(skuIds, cancellationToken);
        var catalogTask = _catalogClient.GetSkuBatchDetailsAsync(skuIds, cancellationToken);

        await Task.WhenAll(stockTask, catalogTask);

        var stockResults = (await stockTask) ?? new Dictionary<Guid, SkuStockCheckResult>();
        var catalogBatch = await catalogTask;

        foreach (var item in targetItems)
        {
            CatalogProductCheckResult? sku = null;
            if (catalogBatch != null && catalogBatch.TryGetValue(item.SkuId, out var foundSku))
            {
                sku = foundSku;
            }
            else
            {
                // Fallback nếu SKU không có trong batch
                sku = await _catalogClient.GetSkuDetailsAsync(item.SkuId, cancellationToken);
            }

            if (sku == null)
            {
                hasStockIssues = true;
                issues.Add(new CartItemIssue(
                    CartItemId: item.Id,
                    SkuId: item.SkuId,
                    IssueType: "PRODUCT_NOT_FOUND",
                    Message: "The product variant was not found in catalog.",
                    AvailableStock: 0,
                    RequestedQuantity: item.Quantity,
                    SuggestedQuantity: null));
                continue;
            }

            if (!sku.IsPurchasable)
            {
                hasStockIssues = true;
                issues.Add(new CartItemIssue(
                    CartItemId: item.Id,
                    SkuId: item.SkuId,
                    IssueType: "PRODUCT_INACTIVE",
                    Message: "This product variant is no longer active or available for sale.",
                    AvailableStock: 0,
                    RequestedQuantity: item.Quantity,
                    SuggestedQuantity: null));
                continue;
            }

            // Check stock availability via Fulfillment Service
            var stock = stockResults.TryGetValue(item.SkuId, out var s) ? s : null;
            var availableStock = stock != null ? stock.AvailableStock : sku.StockQuantity;
            var isStockAvailable = stock != null ? stock.IsAvailable : (sku.StockQuantity >= item.Quantity);

            if (!isStockAvailable || availableStock < item.Quantity)
            {
                hasStockIssues = true;
                var suggestedQty = availableStock > 0 ? availableStock : (int?)null;
                issues.Add(new CartItemIssue(
                    CartItemId: item.Id,
                    SkuId: item.SkuId,
                    IssueType: availableStock == 0 ? "OUT_OF_STOCK" : "INSUFFICIENT_STOCK",
                    Message: availableStock == 0
                        ? "The product is out of stock in warehouse."
                        : $"Requested quantity ({item.Quantity}) exceeds available warehouse stock ({availableStock}).",
                    AvailableStock: availableStock,
                    RequestedQuantity: item.Quantity,
                    SuggestedQuantity: suggestedQty));
            }

            if (sku.UnitPrice != item.UnitPriceSnapshot)
            {
                hasPriceChanges = true;
                var oldPrice = item.UnitPriceSnapshot;
                item.UnitPriceSnapshot = sku.UnitPrice;
                item.UpdatedAt = DateTimeOffset.UtcNow;
                hasDbUpdates = true;

                issues.Add(new CartItemIssue(
                    CartItemId: item.Id,
                    SkuId: item.SkuId,
                    IssueType: "PRICE_CHANGED",
                    Message: $"Price has changed from {oldPrice:N0} to {sku.UnitPrice:N0}.",
                    OldPrice: oldPrice,
                    NewPrice: sku.UnitPrice));
            }
        }

        if (hasDbUpdates)
        {
            cart.SubtotalAmount = cart.Items.Sum(i => i.Quantity * i.UnitPriceSnapshot);
            cart.LastActivityAt = DateTimeOffset.UtcNow;
            cart.RowVersion++;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var isValid = issues.Count == 0;
        return new ValidateCartResponse(isValid, hasPriceChanges, hasStockIssues, issues);
    }

    public async Task<CartDto> MergeGuestCartAsync(Guid buyerId, MergeGuestCartRequest request, CancellationToken cancellationToken = default)
    {
        var cart = await GetActiveCartEntityAsync(buyerId, asNoTracking: false, cancellationToken);
        if (cart == null)
        {
            cart = new Cart
            {
                Id = Guid.NewGuid(),
                BuyerId = buyerId,
                Status = CartStatus.Active,
                ItemCount = 0,
                SubtotalAmount = 0m,
                LastActivityAt = DateTimeOffset.UtcNow,
                RowVersion = 1
            };
            _dbContext.Carts.Add(cart);
        }

        foreach (var guestItem in request.GuestItems)
        {
            if (guestItem.Quantity <= 0) continue;

            var sku = await _catalogClient.GetSkuDetailsAsync(guestItem.SkuId, cancellationToken);
            if (sku == null || !sku.IsPurchasable) continue;

            // Kiểm tra tồn kho kho trước khi merge món guest
            var isStockAvailable = await _fulfillmentClient.CheckStockAvailabilityAsync(guestItem.SkuId, guestItem.Quantity, cancellationToken);
            if (!isStockAvailable) continue;

            var isExistingItem = cart.Items.Any(i => i.SkuId == sku.SkuId);
            var cartItem = cart.AddOrUpdateItem(
                productId: guestItem.ProductId ?? sku.ProductId,
                skuId: sku.SkuId,
                sellerId: sku.SellerId,
                productName: sku.ProductName,
                quantity: guestItem.Quantity,
                unitPrice: sku.UnitPrice);

            if (!isExistingItem)
            {
                _dbContext.CartItems.Add(cartItem);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapToDto(cart);
    }

    public async Task<GroupedCartResponse> GetGroupedCartAsync(Guid buyerId, CancellationToken cancellationToken = default)
    {
        var cart = await GetActiveCartEntityAsync(buyerId, asNoTracking: true, cancellationToken);
        if (cart == null || cart.Items.Count == 0)
        {
            return new GroupedCartResponse(
                CartId: cart?.Id ?? Guid.Empty,
                BuyerId: buyerId,
                TotalDistinctItems: 0,
                TotalQuantity: 0,
                SubtotalAmount: 0m,
                Shops: Array.Empty<ShopCartGroupResponse>());
        }

        var (stockResults, catalogResults) = await CheckStockAndCatalogForCartItemsAsync(cart.Items, cancellationToken);

        var shopGroups = cart.Items
            .GroupBy(i => i.SellerId)
            .Select(g => new ShopCartGroupResponse(
                SellerId: g.Key,
                ShopName: $"Shop {g.Key.ToString()[..8].ToUpperInvariant()}",
                DistinctItemCount: g.Count(),
                TotalQuantity: g.Sum(x => x.Quantity),
                ShopSubtotal: g.Sum(x => x.Quantity * x.UnitPriceSnapshot),
                Items: g.Select(i =>
                {
                    stockResults.TryGetValue(i.SkuId, out var stock);
                    CatalogProductCheckResult? catalog = null;
                    bool hasCatalogResults = catalogResults != null;
                    if (hasCatalogResults)
                    {
                        catalogResults!.TryGetValue(i.SkuId, out catalog);
                    }

                    bool isAvailable = true;
                    string? disabledReason = null;

                    if (hasCatalogResults && catalog == null)
                    {
                        isAvailable = false;
                        disabledReason = "PRODUCT_NOT_FOUND";
                    }
                    else if (catalog != null && !catalog.IsPurchasable)
                    {
                        isAvailable = false;
                        disabledReason = "PRODUCT_INACTIVE";
                    }
                    else if (stock != null && (!stock.IsAvailable || stock.AvailableStock <= 0))
                    {
                        isAvailable = false;
                        disabledReason = stock.DisabledReason ?? "OUT_OF_STOCK";
                    }

                    return new CartItemResponse(
                        i.Id,
                        i.ProductId,
                        i.SkuId,
                        i.SellerId,
                        i.ProductNameSnapshot,
                        i.Quantity,
                        i.UnitPriceSnapshot,
                        i.Quantity * i.UnitPriceSnapshot,
                        i.AddedAt,
                        IsAvailable: isAvailable,
                        DisabledReason: disabledReason);
                }).ToList()))
            .ToList();

        return new GroupedCartResponse(
            CartId: cart.Id,
            BuyerId: cart.BuyerId,
            TotalDistinctItems: cart.ItemCount,
            TotalQuantity: cart.Items.Sum(i => i.Quantity),
            SubtotalAmount: cart.SubtotalAmount,
            Shops: shopGroups);
    }

    public async Task<CartDto> ChangeItemVariantAsync(Guid buyerId, Guid cartItemId, ChangeCartItemVariantRequest request, CancellationToken cancellationToken = default)
    {
        var cart = await GetActiveCartEntityAsync(buyerId, asNoTracking: false, cancellationToken)
            ?? throw new CommerceDomainException("CART_NOT_FOUND", "Active cart not found for the user.", 404);

        var currentItem = cart.Items.FirstOrDefault(i => i.Id == cartItemId)
            ?? throw new CommerceDomainException("CART_ITEM_NOT_FOUND", "Cart item not found in the current cart.", 404);

        // Xác thực tính hợp lệ của việc đổi variant từ Catalog Service (cùng cha, cùng shop, đang active)
        var switchValidation = await _catalogClient.ValidateVariantSwitchAsync(currentItem.SkuId, request.NewSkuId, cancellationToken);
        if (switchValidation == null || !switchValidation.IsValid)
        {
            throw new CommerceDomainException("CATALOG_VARIANT_MISMATCH", "New SKU does not belong to the same parent product.", 400);
        }

        if (!switchValidation.IsPurchasable)
        {
            throw new CommerceDomainException("CATALOG_SKU_INACTIVE", "The requested product variant is unavailable or inactive.", 400);
        }

        // Bắt buộc kiểm tra tồn kho Fulfillment trước khi đổi variant
        var isStockAvailable = await _fulfillmentClient.CheckStockAvailabilityAsync(request.NewSkuId, currentItem.Quantity, cancellationToken);
        if (!isStockAvailable)
        {
            throw new CommerceDomainException("INSUFFICIENT_STOCK", "The requested product variant does not have enough warehouse stock available.", 400);
        }

        cart.ChangeItemVariant(
            cartItemId: cartItemId,
            newSkuId: switchValidation.NewSkuId,
            newProductName: switchValidation.NewSkuName,
            newUnitPrice: switchValidation.NewUnitPrice);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapToDto(cart);
    }

    public async Task<CartDto> CleanupCheckedOutItemsAsync(Guid buyerId, CleanupCheckedOutItemsRequest request, CancellationToken cancellationToken = default)
    {
        var cart = await GetActiveCartEntityAsync(buyerId, asNoTracking: false, cancellationToken)
            ?? throw new CommerceDomainException("CART_NOT_FOUND", "Active cart not found for the user.", 404);

        if (request.PurchasedCartItemIds != null && request.PurchasedCartItemIds.Count > 0)
        {
            cart.BulkRemoveItems(request.PurchasedCartItemIds);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return MapToDto(cart);
    }

    private async Task<Cart?> GetActiveCartEntityAsync(Guid buyerId, bool asNoTracking, CancellationToken cancellationToken)
    {
        if (_dbContext.Carts == null)
        {
            return null;
        }

        IQueryable<Cart> query = _dbContext.Carts.Include(c => c.Items);
        if (asNoTracking)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(c => c.BuyerId == buyerId && c.Status == CartStatus.Active, cancellationToken);
    }

    private async Task<(Dictionary<Guid, SkuStockCheckResult> StockResults, IReadOnlyDictionary<Guid, CatalogProductCheckResult> CatalogResults)> CheckStockAndCatalogForCartItemsAsync(
        ICollection<CartItem> items, CancellationToken cancellationToken)
    {
        if (items == null || items.Count == 0)
        {
            return (new Dictionary<Guid, SkuStockCheckResult>(), new Dictionary<Guid, CatalogProductCheckResult>());
        }

        var skuIds = items.Select(i => i.SkuId).Distinct().ToList();
        var stockTask = _fulfillmentClient.BatchCheckStockAsync(skuIds, cancellationToken);
        var catalogTask = _catalogClient.GetSkuBatchDetailsAsync(skuIds, cancellationToken);

        await Task.WhenAll(stockTask, catalogTask);
        var stockResults = await stockTask;
        var catalogResults = await catalogTask;
        return (stockResults ?? new Dictionary<Guid, SkuStockCheckResult>(), catalogResults ?? new Dictionary<Guid, CatalogProductCheckResult>());
    }

    private async Task<Dictionary<Guid, SkuStockCheckResult>> CheckStockForCartItemsAsync(
        ICollection<CartItem> items, CancellationToken cancellationToken)
    {
        if (items == null || items.Count == 0)
        {
            return new Dictionary<Guid, SkuStockCheckResult>();
        }

        var skuIds = items.Select(i => i.SkuId).Distinct().ToList();
        return await _fulfillmentClient.BatchCheckStockAsync(skuIds, cancellationToken);
    }

    private static CartDto MapToDto(
        Cart cart,
        Dictionary<Guid, SkuStockCheckResult>? stockResults = null,
        IReadOnlyDictionary<Guid, CatalogProductCheckResult>? catalogResults = null)
    {
        var items = cart.Items.Select(i =>
        {
            SkuStockCheckResult? stock = null;
            if (stockResults != null)
            {
                stockResults.TryGetValue(i.SkuId, out stock);
            }

            CatalogProductCheckResult? catalog = null;
            bool hasCatalogResults = catalogResults != null;
            if (hasCatalogResults)
            {
                catalogResults!.TryGetValue(i.SkuId, out catalog);
            }

            bool isAvailable = true;
            string? disabledReason = null;

            if (hasCatalogResults && catalog == null)
            {
                isAvailable = false;
                disabledReason = "PRODUCT_NOT_FOUND";
            }
            else if (catalog != null && !catalog.IsPurchasable)
            {
                isAvailable = false;
                disabledReason = "PRODUCT_INACTIVE";
            }
            else if (stock != null && (!stock.IsAvailable || stock.AvailableStock <= 0))
            {
                isAvailable = false;
                disabledReason = stock.DisabledReason ?? "OUT_OF_STOCK";
            }

            return new CartItemDto(
                i.Id,
                i.ProductId,
                i.SkuId,
                i.SellerId,
                i.ProductNameSnapshot,
                i.Quantity,
                i.UnitPriceSnapshot,
                i.Quantity * i.UnitPriceSnapshot,
                i.AddedAt,
                IsAvailable: isAvailable,
                DisabledReason: disabledReason);
        }).ToList();

        return new CartDto(
            cart.Id,
            cart.BuyerId,
            cart.Status.ToString(),
            cart.ItemCount,
            cart.SubtotalAmount,
            items,
            cart.LastActivityAt);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("23505", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("IX_CartItems_CartId_SkuId", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase);
    }
}
