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

public sealed class CachedQuote
{
    public Guid QuoteId { get; init; }
    public Guid BuyerId { get; init; }
    public CheckoutQuoteRequest Request { get; init; } = null!;
    public CheckoutQuoteResponse Response { get; init; } = null!;
    public DateTimeOffset ExpiredAt { get; init; }
    /// <summary>Platform fee rate used to price this quote; order creation reuses it so the rate cannot change between quote and order.</summary>
    public decimal? PlatformFeePercent { get; init; }
}
