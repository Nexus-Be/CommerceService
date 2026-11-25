namespace Commerce.Application.Services.Delivery;

using System;
using System.Collections.Generic;
using System.Linq;
using Commerce.Application.Interfaces;

/// <summary>
/// Decides whether a parcel that came back was refused by the buyer. Deliberately conservative: a penalty needs clear, consistent evidence.
/// Anything missing, mixed or not in the approved table is "Unknown" and costs the buyer nothing.
/// </summary>
public static class DeliveryFailureClassifier
{
    public const string BuyerRefused = "BuyerRefused";
    public const string Unknown = "Unknown";
    public const string NoCode = "-";

    /// <summary>The codes of the failed attempts as stored on the sub-order ("102,102,-"), without the empty entries.</summary>
    public static IReadOnlyList<string> ParseAttempts(string? stored) =>
        string.IsNullOrWhiteSpace(stored)
            ? Array.Empty<string>()
            : stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string Append(string? stored, string? reasonCode)
    {
        var entry = string.IsNullOrWhiteSpace(reasonCode) ? NoCode : reasonCode.Trim().Replace(",", string.Empty);
        var joined = string.IsNullOrEmpty(stored) ? entry : stored + "," + entry;
        // Keep the newest entries if the column would overflow; the oldest attempts matter least.
        while (joined.Length > 500 && joined.Contains(',')) joined = joined[(joined.IndexOf(',') + 1)..];
        return joined;
    }

    /// <summary>
    /// BuyerRefused only when the return event itself carries an approved code and every recorded failed attempt carries an approved code too.
    /// A return without a code, a missing code on any attempt, a code outside the table, or no evidence at all gives Unknown. The stored attempt
    /// codes are the history of the whole sub-order and are not tied to the delivery that ended in this return, so they alone never justify a penalty.
    /// </summary>
    public static string Classify(string? storedAttemptCodes, string? returnedReasonCode, IDeliveryFailurePolicy policy)
    {
        var attempts = ParseAttempts(storedAttemptCodes);
        var returnedCode = string.IsNullOrWhiteSpace(returnedReasonCode) ? null : returnedReasonCode.Trim();

        if (returnedCode is null || !policy.IsBuyerRefusedCode(returnedCode))
        {
            return Unknown;
        }

        return attempts.All(code => code != NoCode && policy.IsBuyerRefusedCode(code)) ? BuyerRefused : Unknown;
    }
}
