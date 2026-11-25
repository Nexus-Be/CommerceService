namespace Commerce.Infrastructure.Services.Fulfillment;

using System;
using Microsoft.Extensions.Configuration;

/// <summary>
/// How long Commerce waits for Fulfillment before it gives up. The default stays 10 seconds; a Fulfillment that runs where it can be
/// put to sleep when idle (for example a free hosting plan) needs a longer wait for the first call, so it can be set with
/// <c>FULFILLMENT_TIMEOUT_SECONDS</c> (1 to 120). Anything else falls back to the default.
/// </summary>
public static class FulfillmentTimeout
{
    public const int DefaultSeconds = 10;
    public const int MaxSeconds = 120;

    public static TimeSpan Resolve(IConfiguration? configuration)
    {
        var raw = Environment.GetEnvironmentVariable("FULFILLMENT_TIMEOUT_SECONDS") ?? configuration?["FULFILLMENT_TIMEOUT_SECONDS"];
        return int.TryParse(raw, out var seconds) && seconds >= 1 && seconds <= MaxSeconds
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.FromSeconds(DefaultSeconds);
    }
}
