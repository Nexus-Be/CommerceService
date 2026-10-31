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
    DateTimeOffset AddedAt = default);

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
    decimal LineTotal);

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
    int? RequestedQuantity = null);

public sealed record ValidateCartResponse(
    bool IsValid,
    bool HasPriceChanges,
    bool HasStockIssues,
    IReadOnlyList<CartItemIssue> Issues);
