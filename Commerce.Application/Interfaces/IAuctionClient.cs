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
    Guid? EventId = null
);

public interface IAuctionClient
{
    Task<bool> NotifyPaymentStatusAsync(
        Guid auctionId,
        AuctionPaymentStatusCallbackRequest request,
        CancellationToken cancellationToken = default);
}
