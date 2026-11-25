namespace Commerce.Application.Services.Common;

using System.Threading;
using System.Threading.Tasks;
using Commerce.Application.Interfaces;
using Commerce.Domain.Exceptions;

/// <summary>
/// A delivery address is used (quoted, ordered, changed) only when its province, district and ward are all given and the GHN lists confirm
/// that they belong together. The carrier's own fee answer is not proof: it prices by the ward and ignores a district that does not match.
/// A missing province id or a chain the lists reject is 400 INVALID_SHIPPING_ADDRESS; with no trusted GHN source it is 503
/// LOCATION_SOURCE_UNAVAILABLE and nothing is quoted or changed.
/// </summary>
public static class ShippingChain
{
    public static async Task<LocationCheckResult> EnsureConfirmedAsync(
        IFulfillmentClient fulfillment,
        int? provinceId,
        int? districtId,
        string? wardCode,
        CancellationToken cancellationToken = default)
    {
        if (!provinceId.HasValue || provinceId.Value <= 0 || !districtId.HasValue || districtId.Value <= 0 || string.IsNullOrWhiteSpace(wardCode))
        {
            throw new CommerceDomainException("INVALID_SHIPPING_ADDRESS", "Shipping address must contain the GHN province, district and ward codes.", 400);
        }

        var check = await fulfillment.ValidateLocationAsync(provinceId.Value, districtId.Value, wardCode.Trim(), cancellationToken);
        switch (check?.Outcome)
        {
            case LocationCheckOutcome.Confirmed:
                return check;
            case LocationCheckOutcome.Rejected:
                throw new CommerceDomainException("INVALID_SHIPPING_ADDRESS", check.Message ?? "The province, district and ward of the shipping address do not match.", 400);
            default:
                throw new CommerceDomainException("LOCATION_SOURCE_UNAVAILABLE", "Shipping locations are temporarily unavailable, so the address cannot be used yet. Please try again shortly.", 503);
        }
    }
}
