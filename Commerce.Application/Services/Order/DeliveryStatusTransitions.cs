namespace Commerce.Application.Services.Order;

using System;
using System.Linq;
using Commerce.Domain.Enums;

/// <summary>
/// Decides whether a carrier update may still change an order. Carrier events arrive late, twice, or out of order, so an update
/// that does not move the order forward (for example "in transit" after the order is completed) is ignored instead of undoing progress.
/// </summary>
public static class DeliveryStatusTransitions
{
    public static bool IsAllowed(SubOrderStatus current, string? carrierStatus)
    {
        var status = carrierStatus?.Trim() ?? "";
        bool Is(params string[] names) => names.Any(n => string.Equals(n, status, StringComparison.OrdinalIgnoreCase));

        if (Is("ReadyToPick", "Picking", "Packed"))
            return current is SubOrderStatus.Paid or SubOrderStatus.Confirmed;

        if (Is("HandedOver", "InTransit", "OutForDelivery", "Shipped", "DeliveryAttemptFailed"))
            return current is SubOrderStatus.Confirmed or SubOrderStatus.Shipped;

        if (Is("Delivered"))
            return current is SubOrderStatus.Confirmed or SubOrderStatus.Shipped;

        if (Is("Cancelled", "DeliveryFailed", "Returned"))
            return current is SubOrderStatus.Confirmed or SubOrderStatus.Shipped or SubOrderStatus.Delivered;

        return true; // unknown statuses keep their existing handling
    }
}
