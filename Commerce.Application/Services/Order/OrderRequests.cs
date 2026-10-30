namespace Commerce.Application.Services.Order;

using System;

public sealed record CreateOrderRequest(
    Guid BuyerId,
    Guid? CartId,
    decimal TotalAmount);

public sealed record CancelOrderRequest(
    Guid OrderId,
    string Reason);
