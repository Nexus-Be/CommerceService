namespace Commerce.Application.Interfaces;

using System.Net;

public sealed record WalletPaymentResult(
    HttpStatusCode StatusCode,
    string ResponseBody,
    string? ErrorCode,
    string? TransactionId)
{
    public bool IsSuccess => StatusCode == HttpStatusCode.OK;
}