namespace Commerce.Application.Services.AuctionOrder;

using System.Text.Json;
using Commerce.Domain.ValueObjects;

/// <summary>
/// An auction order keeps its address as the JSON of <see cref="ShippingAddressDto"/> (fullName, addressLine, wardName, ...), while the shipment
/// timeline answers with a <see cref="ShippingAddressSnapshot"/> (receiverName, street, ward, ...). Reading the first as the second left the
/// receiver, street, ward, district and province empty. This reader recognises the stored shape, maps it field by field, still reads the snapshot
/// shape, and returns null for anything that carries no address ({}, an array, a value of another shape) so the caller's fallback applies.
/// Text that is not JSON at all raises a <see cref="JsonException"/>, which the caller already turns into its fallback.
/// </summary>
internal static class AuctionAddressSnapshot
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static ShippingAddressSnapshot? TryRead(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (HasText(document.RootElement, "fullName", "phoneNumber", "addressLine", "provinceName", "districtName", "wardName"))
        {
            var dto = JsonSerializer.Deserialize<ShippingAddressDto>(json, Options);
            if (dto is null)
            {
                return null;
            }

            return new ShippingAddressSnapshot(
                receiverName: dto.FullName ?? string.Empty,
                contactPhone: dto.PhoneNumber ?? string.Empty,
                street: dto.AddressLine ?? string.Empty,
                ward: dto.WardName ?? string.Empty,
                district: dto.DistrictName,
                province: dto.ProvinceName ?? string.Empty,
                ghnDistrictId: dto.GhnDistrictId,
                ghnWardCode: dto.GhnWardCode ?? string.Empty,
                provinceId: dto.ProvinceId);
        }

        return HasText(document.RootElement, "receiverName", "contactPhone", "street", "ward", "district", "province", "fullAddressText")
            ? JsonSerializer.Deserialize<ShippingAddressSnapshot>(json, Options)
            : null;
    }

    private static bool HasText(JsonElement root, params string[] names)
    {
        foreach (var property in root.EnumerateObject())
        {
            foreach (var name in names)
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(property.Value.GetString()))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
