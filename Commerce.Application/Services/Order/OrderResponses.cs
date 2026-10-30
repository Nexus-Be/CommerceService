namespace Commerce.Application.Services.Order;

using System;

public sealed record OrderResponse(
    Guid Id,
    Guid BuyerId,
    string Status,
    decimal TotalAmount,
    DateTimeOffset PaymentDeadlineAt);
