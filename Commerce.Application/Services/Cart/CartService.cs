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

    public CartService(ICommerceDbContext dbContext, ICatalogClient catalogClient)
    {
        _dbContext = dbContext;
        _catalogClient = catalogClient;
    }

    public async Task<CartDto> GetActiveCartByBuyerIdAsync(Guid buyerId, CancellationToken cancellationToken = default)
    {
        var cart = await GetActiveCartEntityAsync(buyerId, asNoTracking: true, cancellationToken)
            ?? throw new EntityNotFoundException("CART_NOT_FOUND", "Cart", buyerId);

        return MapToDto(cart);
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

        return MapToDto(cart);
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

        cart.AddOrUpdateItem(
            productId: request.ProductId ?? skuInfo.ProductId,
            skuId: skuInfo.SkuId,
            sellerId: skuInfo.SellerId,
            productName: skuInfo.ProductName,
            quantity: request.Quantity,
            unitPrice: skuInfo.UnitPrice);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapToDto(cart);
    }

    public async Task<CartDto> UpdateItemQuantityAsync(Guid buyerId, Guid cartItemId, UpdateCartItemQuantityRequest request, CancellationToken cancellationToken = default)
    {
        var cart = await GetActiveCartEntityAsync(buyerId, asNoTracking: false, cancellationToken)
            ?? throw new CommerceDomainException("CART_NOT_FOUND", "Active cart not found for the user.", 404);

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

        var calculatedItems = selectedItems.Select(i => new CalculatedCartItem(
            CartItemId: i.Id,
            SkuId: i.SkuId,
            ProductName: i.ProductNameSnapshot,
            Quantity: i.Quantity,
            UnitPrice: i.UnitPriceSnapshot,
            LineTotal: i.Quantity * i.UnitPriceSnapshot
        )).ToList();

        var totalQty = calculatedItems.Sum(i => i.Quantity);
        var subtotal = calculatedItems.Sum(i => i.LineTotal);

        return new CalculateSelectedCartResponse(
            SelectedItemCount: calculatedItems.Count,
            TotalQuantity: totalQty,
            SubtotalAmount: subtotal,
            Items: calculatedItems);
    }

    public async Task<ValidateCartResponse> ValidateCartAsync(Guid buyerId, ValidateCartRequest request, CancellationToken cancellationToken = default)
    {
        var cart = await GetActiveCartEntityAsync(buyerId, asNoTracking: true, cancellationToken);
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

        foreach (var item in targetItems)
        {
            var sku = await _catalogClient.GetSkuDetailsAsync(item.SkuId, cancellationToken);
            if (sku == null || !sku.IsPurchasable)
            {
                hasStockIssues = true;
                issues.Add(new CartItemIssue(
                    CartItemId: item.Id,
                    SkuId: item.SkuId,
                    IssueType: "OUT_OF_STOCK",
                    Message: "The product is currently unavailable or inactive.",
                    AvailableStock: 0,
                    RequestedQuantity: item.Quantity));
                continue;
            }

            if (sku.StockQuantity < item.Quantity)
            {
                hasStockIssues = true;
                issues.Add(new CartItemIssue(
                    CartItemId: item.Id,
                    SkuId: item.SkuId,
                    IssueType: "INSUFFICIENT_STOCK",
                    Message: "Requested quantity exceeds available stock.",
                    AvailableStock: sku.StockQuantity,
                    RequestedQuantity: item.Quantity));
            }

            if (sku.UnitPrice != item.UnitPriceSnapshot)
            {
                hasPriceChanges = true;
                issues.Add(new CartItemIssue(
                    CartItemId: item.Id,
                    SkuId: item.SkuId,
                    IssueType: "PRICE_CHANGED",
                    Message: $"Price has changed from {item.UnitPriceSnapshot} to {sku.UnitPrice}.",
                    OldPrice: item.UnitPriceSnapshot,
                    NewPrice: sku.UnitPrice));
            }
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

            cart.AddOrUpdateItem(
                productId: guestItem.ProductId ?? sku.ProductId,
                skuId: sku.SkuId,
                sellerId: sku.SellerId,
                productName: sku.ProductName,
                quantity: guestItem.Quantity,
                unitPrice: sku.UnitPrice);
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

        var shopGroups = cart.Items
            .GroupBy(i => i.SellerId)
            .Select(g => new ShopCartGroupResponse(
                SellerId: g.Key,
                ShopName: $"Shop {g.Key.ToString()[..8].ToUpperInvariant()}",
                DistinctItemCount: g.Count(),
                TotalQuantity: g.Sum(x => x.Quantity),
                ShopSubtotal: g.Sum(x => x.Quantity * x.UnitPriceSnapshot),
                Items: g.Select(i => new CartItemResponse(
                    i.Id,
                    i.ProductId,
                    i.SkuId,
                    i.SellerId,
                    i.ProductNameSnapshot,
                    i.Quantity,
                    i.UnitPriceSnapshot,
                    i.Quantity * i.UnitPriceSnapshot,
                    i.AddedAt)).ToList()))
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

        var skuInfo = await _catalogClient.GetSkuDetailsAsync(request.NewSkuId, cancellationToken);
        if (skuInfo == null)
        {
            throw new CommerceDomainException("CATALOG_SKU_NOT_FOUND", "The specified product SKU was not found in catalog.", 404);
        }

        if (!skuInfo.IsPurchasable)
        {
            throw new CommerceDomainException("CATALOG_SKU_INACTIVE", skuInfo.ErrorReason ?? "The specified product SKU is unavailable or out of stock.", 400);
        }

        cart.ChangeItemVariant(
            cartItemId: cartItemId,
            newSkuId: skuInfo.SkuId,
            newProductName: skuInfo.ProductName,
            newUnitPrice: skuInfo.UnitPrice);

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

    private static CartDto MapToDto(Cart cart)
    {
        var items = cart.Items.Select(i => new CartItemDto(
            i.Id,
            i.ProductId,
            i.SkuId,
            i.SellerId,
            i.ProductNameSnapshot,
            i.Quantity,
            i.UnitPriceSnapshot,
            i.Quantity * i.UnitPriceSnapshot,
            i.AddedAt)).ToList();

        return new CartDto(
            cart.Id,
            cart.BuyerId,
            cart.Status.ToString(),
            cart.ItemCount,
            cart.SubtotalAmount,
            items,
            cart.LastActivityAt);
    }
}
