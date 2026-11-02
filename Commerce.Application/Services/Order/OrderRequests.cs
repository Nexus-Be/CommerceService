namespace Commerce.Application.Services.Order;

using System;
using System.Collections.Generic;
using Commerce.Domain.Enums;

public sealed record ShippingAddressInput(
    string ReceiverName,
    string ContactPhone,
    string Street,
    string Ward,
    string District,
    string Province,
    int? GhnDistrictId = null,
    string? GhnWardCode = null);

public sealed record CheckoutQuoteRequest(
    List<Guid> CartItemIds,
    ShippingAddressInput ShippingAddress,
    string ContactPhone,
    PaymentMethod PaymentMethod = PaymentMethod.Wallet,
    string? CarrierCode = null,
    List<string>? VoucherCodes = null);

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
