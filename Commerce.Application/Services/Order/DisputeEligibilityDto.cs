namespace Commerce.Application.Services.Order;

using System;

public sealed record DisputeEligibilityDto(
    bool IsEligible,
    string OrderType,
    Guid BuyerId,
    Guid SellerId,
    string Status,
    DateTimeOffset? DeliveredAt
);
