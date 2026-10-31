namespace Commerce.Application.Services.Cart;

using System;
using System.Collections.Generic;

public sealed record AddCartItemRequest(
    Guid SkuId,
    int Quantity,
    Guid? ProductId = null);

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

public sealed record BulkRemoveCartItemsRequest(
    List<Guid>? CartItemIds,
    bool ClearAll = false);

public sealed record CalculateSelectedCartItemsRequest(
    List<Guid> SelectedItemIds);

public sealed record ValidateCartRequest(
    List<Guid>? SelectedItemIds = null);

public sealed record GuestCartItem(
    Guid SkuId,
    int Quantity,
    Guid? ProductId = null);

public sealed record MergeGuestCartRequest(
    List<GuestCartItem> GuestItems);
