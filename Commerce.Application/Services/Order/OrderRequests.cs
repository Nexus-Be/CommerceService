namespace Commerce.Application.Services.Order;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Commerce.Domain.Enums;

public sealed record ShippingAddressInput(
    string ReceiverName,
    string ContactPhone,
    string Street,
    string Ward,
    string? District = null,
    string Province = "",
    int? GhnDistrictId = null,
    string? GhnWardCode = null,
    int? ProvinceId = null)
{
    public string FullAddressText => string.Join(", ", new[] { Street, Ward, District, Province }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

public sealed record CheckoutQuoteRequest(
    List<Guid> CartItemIds,
    ShippingAddressInput? ShippingAddress = null,
    string? ContactPhone = null,
    PaymentMethod PaymentMethod = PaymentMethod.Wallet,
    string? CarrierCode = null,
    List<string>? VoucherCodes = null,
    Guid? AddressId = null);

public sealed record RecalculateQuoteRequest(
    Guid? AddressId = null,
    ShippingAddressInput? ShippingAddress = null,
    string? ContactPhone = null);

public sealed record CreateOrderRequest(
    Guid QuoteId,
    PaymentMethod PaymentMethod,
    string ShippingAddress,
    string ContactPhone,
    string? Note = null);

public sealed record CancelOrderRequest(
    CancellationReasonCode ReasonCode = CancellationReasonCode.BuyerChangedMind,
    string? ReasonText = null);

public sealed record SellerAcceptOrderRequest(
    Guid SubOrderId,
    string? Note = null);

public sealed record SellerRejectOrderRequest(
    Guid SubOrderId,
    CancellationReasonCode ReasonCode = CancellationReasonCode.SellerRejected,
    string? ReasonText = null);

public sealed record SubOrderRefundRequest(
    string Reason,
    string? Note = null);

public sealed record UpdateDeliveryStatusRequest(
    string Status,
    DateTimeOffset? DeliveredAt = null,
    string? CarrierCode = null,
    string? TrackingCode = null,
    string? Note = null,
    string? Location = null,
    string? PodUrl = null,
    string? ShipperName = null,
    string? ShipperPhone = null,
    string? ReasonCode = null,
    string? Reason = null,
    string? EventType = null,
    DateTimeOffset? EventTime = null);

public sealed record CancelSubOrderRequest(
    CancellationReasonCode ReasonCode = CancellationReasonCode.BuyerChangedMind,
    string? ReasonNote = null);

public sealed record ChangeShippingAddressRequest(
    Guid? AddressId = null,
    ShippingAddressInput? ShippingAddress = null,
    string? ContactPhone = null);

/// <summary>Asks Commerce to refund a sub-order because a dispute was decided for the buyer. The amount is never sent: Commerce works it out.</summary>
public sealed record SubOrderDisputeRefundRequest(
    Guid ReportId,
    string? Note = null);

public sealed record ApproveRefundRequest(
    string? Note = null);

public sealed record RejectRefundRequest(
    string Reason,
    string? Note = null);

public sealed record AdminOrderFilterRequest(
    Guid? BuyerId = null,
    Guid? SellerId = null,
    OrderStatus? Status = null,
    DateTimeOffset? FromDate = null,
    DateTimeOffset? ToDate = null,
    int Page = 1,
    int PageSize = 20);

public sealed record AdminSubOrderFilterRequest(
    Guid? SellerId = null,
    Guid? BuyerId = null,
    SubOrderStatus? Status = null,
    string? TrackingCode = null,
    DateTimeOffset? FromDate = null,
    DateTimeOffset? ToDate = null,
    int Page = 1,
    int PageSize = 20);

public sealed record DisputeRefundRequest(
    [Required] Guid DisputeId,
    [Range(0.01, double.MaxValue)] decimal RefundAmount,
    [Required, StringLength(255, MinimumLength = 1)] string Reason,
    string? Note = null);
