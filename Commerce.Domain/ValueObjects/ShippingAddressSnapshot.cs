namespace Commerce.Domain.ValueObjects;

public class ShippingAddressSnapshot
{
    public string ReceiverName { get; set; } = string.Empty;
    public string ContactPhone { get; set; } = string.Empty;
    public string Street { get; set; } = string.Empty;
    public string Ward { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public string Province { get; set; } = string.Empty;
    public int GhnDistrictId { get; set; }
    public string GhnWardCode { get; set; } = string.Empty;
    public string FullAddressText { get; set; } = string.Empty;

    public ShippingAddressSnapshot() { }

    public ShippingAddressSnapshot(
        string receiverName,
        string contactPhone,
        string street,
        string ward,
        string district,
        string province,
        int ghnDistrictId = 0,
        string ghnWardCode = "",
        string? fullAddressText = null)
    {
        ReceiverName = receiverName;
        ContactPhone = contactPhone;
        Street = street;
        Ward = ward;
        District = district;
        Province = province;
        GhnDistrictId = ghnDistrictId;
        GhnWardCode = ghnWardCode;
        FullAddressText = fullAddressText ?? $"{street}, {ward}, {district}, {province}".Trim().TrimStart(',').Trim();
    }
}
