namespace Commerce.Application.Services.Shipment;

using System;
using System.Linq;
using System.Text.Json;
using Commerce.Application.Interfaces;
using Commerce.Application.Services.AuctionOrder;
using Commerce.Domain.Entities;
using Commerce.Domain.Exceptions;

/// <summary>Builds the Fulfillment shipment request from an order, so every flow sends the same, complete payload.</summary>
public static class ShipmentRequestFactory
{
    private const int DefaultWeightInGrams = 500;

    private static readonly JsonSerializerOptions AddressJson = new() { PropertyNameCaseInsensitive = true };

    public static ShipmentCreationRequest ForAuctionOrder(AuctionOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);

        ShippingAddressDto? address = null;
        if (!string.IsNullOrWhiteSpace(order.ShippingAddress))
        {
            try { address = JsonSerializer.Deserialize<ShippingAddressDto>(order.ShippingAddress, AddressJson); }
            catch (JsonException) { /* handled below: an unreadable address is as bad as a missing one */ }
        }

        if (address is null)
            throw new CommerceDomainException("SHIPPING_ADDRESS_MISSING", "The auction order has no readable shipping address.", 422);

        var weight = order.WeightGramsSnapshot > 0 ? order.WeightGramsSnapshot : DefaultWeightInGrams;
        var skuId = order.SkuId is { } sku && sku != Guid.Empty ? sku : order.ProductId;

        return new ShipmentCreationRequest(
            OrderId: order.Id,
            SubOrderId: order.Id,
            SellerId: order.SellerId,
            RecipientName: address.FullName,
            RecipientPhone: !string.IsNullOrWhiteSpace(order.ContactPhone) ? order.ContactPhone! : address.PhoneNumber,
            ShippingAddress: JoinAddress(address.AddressLine, address.WardName, address.DistrictName, address.ProvinceName),
            ToProvinceName: address.ProvinceName,
            ToDistrictId: address.GhnDistrictId,
            ToWardCode: address.GhnWardCode,
            WeightInGrams: weight,
            Items: new[] { new ShipmentCreationItem(skuId, 1, order.ProductNameSnapshot, order.WinningAmount, weight) });
    }

    public static ShipmentCreationRequest ForSubOrder(SubOrder subOrder, int? pickShiftId = null)
    {
        ArgumentNullException.ThrowIfNull(subOrder);

        var address = subOrder.ShippingAddress;
        var items = subOrder.Items
            .Where(i => !i.IsDeleted)
            .Select(i => new ShipmentCreationItem(i.SkuId, i.Quantity, i.ProductNameSnapshot, i.UnitPriceAfterDiscount, null))
            .ToList();

        return new ShipmentCreationRequest(
            OrderId: subOrder.OrderId,
            SubOrderId: subOrder.Id,
            SellerId: subOrder.SellerId,
            RecipientName: address.ReceiverName,
            RecipientPhone: !string.IsNullOrWhiteSpace(subOrder.ContactPhone) ? subOrder.ContactPhone : address.ContactPhone,
            ShippingAddress: !string.IsNullOrWhiteSpace(address.FullAddressText)
                ? address.FullAddressText
                : JoinAddress(address.Street, address.Ward, address.District, address.Province),
            ToProvinceName: address.Province,
            ToDistrictId: address.GhnDistrictId > 0 ? address.GhnDistrictId : null,
            ToWardCode: string.IsNullOrWhiteSpace(address.GhnWardCode) ? null : address.GhnWardCode,
            // Commerce does not keep per-SKU weights for regular orders; Fulfillment then applies its own default.
            WeightInGrams: DefaultWeightInGrams,
            Items: items,
            PickShiftId: pickShiftId);
    }

    private static string JoinAddress(params string?[] parts) =>
        string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
}
