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
    decimal UnitPriceSnapshot);

public sealed record CartResponse(
    Guid Id,
    Guid BuyerId,
    string Status,
    int ItemCount,
    decimal SubtotalAmount,
    IReadOnlyList<CartItemResponse> Items);
