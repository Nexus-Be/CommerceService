namespace Commerce.Application.Services.AuctionOrder;

using System;
using System.ComponentModel.DataAnnotations;

public sealed record ShippingAddressDto(
    [Required] string FullName,
    [Required] string PhoneNumber,
    [Required] string AddressLine,
    [Required] string ProvinceName,
    [Required] string DistrictName,
    [Required] string WardName,
    [Range(1, int.MaxValue)] int GhnDistrictId,
    [Required] string GhnWardCode
);

public sealed record AuctionSettlementRequest(
    [Required] Guid AuctionId,
    [Required] Guid WinnerId,
    [Required] Guid SellerId,
    [Required] Guid WinningBidId,
    [Required] Guid ProductId,
    Guid? SkuId,
    [Required, StringLength(255, MinimumLength = 1)] string ProductName,
    string? ProductImageUrl,
    string? SkuAttributes,
    [Range(0.01, double.MaxValue)] decimal WinningAmount,
    [Required] DateTimeOffset PaymentDeadlineAt,
    ShippingAddressDto? ShippingAddress = null,
    string? ContactPhone = null
);

public sealed record SubmitAuctionShippingRequest(
    [Required] ShippingAddressDto ShippingAddress,
    [Required] string ContactPhone
);

public sealed record SellerAcceptAuctionOrderRequest(
    string? Note = null
);

public sealed record SellerRejectAuctionOrderRequest(
    [Required] string ReasonNote,
    string? ReasonCode = "SellerRejected"
);
