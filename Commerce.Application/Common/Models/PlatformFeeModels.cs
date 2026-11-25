namespace Commerce.Application.Common.Models;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// The configured platform fee. <see cref="AppliesTo"/> says which orders the rate is charged on: wallet-paid orders only. COD orders are
/// fixed at <see cref="CodPercent"/> (0%) until the cash reconciliation path and the collecting party are agreed.
/// </summary>
public sealed record PlatformFeeResponse(
    decimal DefaultPercent,
    string UpdatedByRole,
    DateTimeOffset UpdatedAt,
    string AppliesTo = "WalletOrders",
    decimal CodPercent = 0m
);

public sealed record UpdatePlatformFeeRequest(
    [Range(0.0, 100.0, ErrorMessage = "Platform fee rate must be between 0% and 100%.")]
    decimal DefaultPercent
);
