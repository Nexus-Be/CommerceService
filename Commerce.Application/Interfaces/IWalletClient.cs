namespace Commerce.Application.Interfaces;

using System;
using System.Threading;
using System.Threading.Tasks;

public interface IWalletClient
{
    Task<WalletPaymentResult> PayOrderAsync(
        Guid orderId,
        decimal amount,
        string accessToken,
        CancellationToken cancellationToken = default);
}