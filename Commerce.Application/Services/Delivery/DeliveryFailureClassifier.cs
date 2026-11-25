namespace Commerce.Application.Services.Delivery;

using System;
using System.Collections.Generic;
using System.Linq;
using Commerce.Application.Interfaces;
using Commerce.Domain.Entities;

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
    /// BuyerRefused only when the failed attempts of THE SAME PARCEL that the return belongs to prove it: the return names its tracking code and
    /// has a readable carrier time; there is at least one failed attempt with that tracking code; every one of them has a carrier time that is
    /// not after the return and carries an approved reason code; and, if the return itself carries a code, that code is approved too. The
    /// return does not need a code (the carrier does not send one with it). Anything else is Unknown: a return without a tracking code or time,
    /// no attempt of that parcel, an attempt without a time, a code or with another code, an attempt that the carrier dates after the return.
    /// Attempts of other parcels of the same sub-order are ignored; the tracking code stored on the sub-order is never used because it can change.
    /// </summary>
    public static string Classify(
        IReadOnlyCollection<DeliveryFailureAttempt> attempts,
        string? returnedTrackingCode,
        DateTimeOffset? returnedEventTime,
        string? returnedReasonCode,
        IDeliveryFailurePolicy policy)
    {
        if (string.IsNullOrWhiteSpace(returnedTrackingCode) || returnedEventTime is null)
        {
            return Unknown;
        }

        var tracking = returnedTrackingCode.Trim();
        var sameParcel = attempts.Where(item => string.Equals(item.TrackingCode?.Trim(), tracking, StringComparison.OrdinalIgnoreCase)).ToList();
        if (sameParcel.Count == 0)
        {
            return Unknown;
        }

        if (!string.IsNullOrWhiteSpace(returnedReasonCode) && !policy.IsBuyerRefusedCode(returnedReasonCode))
        {
            return Unknown;
        }

        foreach (var attempt in sameParcel)
        {
            if (attempt.EventTime is null || attempt.EventTime > returnedEventTime) return Unknown;
            if (string.IsNullOrWhiteSpace(attempt.ReasonCode) || !policy.IsBuyerRefusedCode(attempt.ReasonCode)) return Unknown;
        }

        return BuyerRefused;
    }
}
