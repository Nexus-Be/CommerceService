namespace Commerce.Application.Services.Cart;

using System;

public sealed record AddItemToCartRequest(
    Guid BuyerId,
    Guid ProductId,
    Guid SkuId,
    Guid SellerId,
    string ProductName,
    int Quantity,
    decimal UnitPrice);

public sealed record UpdateCartItemQuantityRequest(
    Guid CartItemId,
    int Quantity);

public sealed record RemoveCartItemRequest(
    Guid CartItemId);
