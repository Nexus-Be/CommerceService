namespace Commerce.Application.Services.Order;

using System;
using System.Collections.Generic;

public sealed record OrderDto(
    Guid Id,
    Guid BuyerId,
    string Status,
    decimal TotalAmount,
    DateTimeOffset PaymentDeadlineAt);

public sealed record CreateOrderDto(
    Guid BuyerId,
    Guid? CartId,
    decimal TotalAmount);
