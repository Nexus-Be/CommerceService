namespace Commerce.Application.Services.Order;

using System;

/// <summary>
/// What UserService needs to decide whether a partner review is allowed: who bought, who sold, and whether and when the order finished.
/// ReferenceType Order means a sub-order (never the parent order); Auction means an auction order.
/// </summary>
public sealed record ReviewEligibilityDto(
    string ReferenceType,
    Guid ReferenceId,
    string Status,
    Guid BuyerId,
    Guid SellerId,
    DateTimeOffset? CompletedAt);

public static class ReviewReferenceTypes
{
    public const string Order = "Order";
    public const string Auction = "Auction";

    public static bool TryNormalize(string? value, out string normalized)
    {
        if (string.Equals(value?.Trim(), Order, StringComparison.OrdinalIgnoreCase))
        {
            normalized = Order;
            return true;
        }

        if (string.Equals(value?.Trim(), Auction, StringComparison.OrdinalIgnoreCase))
        {
            normalized = Auction;
            return true;
        }

        normalized = string.Empty;
        return false;
    }
}
