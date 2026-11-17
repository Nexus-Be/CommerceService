namespace Commerce.Application.Interfaces;

using System;
using System.Threading;
using System.Threading.Tasks;

public sealed record AuctionPaymentStatusCallbackRequest(
    Guid OrderId,
    Guid WinnerId,
    string Status,
    decimal? Amount = null,
    DateTimeOffset? PaidAt = null,
    Guid? EventId = null,
    string? OrderSource = null,
    DateTimeOffset? OccurredAt = null,
    // What the "order paid" notification needs. Sent with the Paid callback; the receiver treats them as optional.
    string? OrderNo = null,
    decimal? TotalAmount = null,
    DateTimeOffset? SellerRespondDeadlineAt = null
);

public interface IAuctionClient
{
    Task<bool> NotifyPaymentStatusAsync(
        Guid auctionId,
        AuctionPaymentStatusCallbackRequest request,
        CancellationToken cancellationToken = default);
}
