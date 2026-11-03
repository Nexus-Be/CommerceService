namespace Commerce.Application.Services.Cart;

using System;
using System.Collections.Generic;

public sealed record CartItemResponse(
    Guid Id,
    Guid ProductId,
    Guid SkuId,
    Guid SellerId,
    string ProductName,
    int Quantity,
    decimal UnitPriceSnapshot,
    decimal LineTotal = 0,
    DateTimeOffset AddedAt = default,
    bool IsAvailable = true,
    string? DisabledReason = null);

public sealed record CartResponse(
    Guid Id,
    Guid BuyerId,
    string Status,
    int ItemCount,
    decimal SubtotalAmount,
    IReadOnlyList<CartItemResponse> Items,
    DateTimeOffset LastActivityAt = default);

public sealed record CartSummaryResponse(
    Guid CartId,
    int TotalDistinctItems,
    int TotalQuantity,
    decimal EstimatedSubtotal);

public sealed record CalculatedCartItem(
    Guid CartItemId,
    Guid SkuId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    bool IsAvailable = true,
    int? AvailableStock = null,
    string? StockWarning = null);

public sealed record CalculateSelectedCartResponse(
    int SelectedItemCount,
    int TotalQuantity,
    decimal SubtotalAmount,
    IReadOnlyList<CalculatedCartItem> Items);

public sealed record CartItemIssue(
    Guid CartItemId,
    Guid SkuId,
    string IssueType,
    string Message,
    decimal? OldPrice = null,
    decimal? NewPrice = null,
    int? AvailableStock = null,
    int? RequestedQuantity = null,
    int? SuggestedQuantity = null);

public sealed record ValidateCartResponse(
    bool IsValid,
    bool HasPriceChanges,
    bool HasStockIssues,
    IReadOnlyList<CartItemIssue> Issues);

public sealed record ShopCartGroupResponse(
    Guid SellerId,
    string ShopName,
    int DistinctItemCount,
    int TotalQuantity,
    decimal ShopSubtotal,
    IReadOnlyList<CartItemResponse> Items);

public sealed record GroupedCartResponse(
    Guid CartId,
    Guid BuyerId,
    int TotalDistinctItems,
    int TotalQuantity,
    decimal SubtotalAmount,
    IReadOnlyList<ShopCartGroupResponse> Shops);
