namespace Commerce.Infrastructure.Services.Fulfillment;

using System;
using Microsoft.Extensions.Configuration;

/// <summary>
/// How long Commerce waits for Fulfillment before it gives up. The default is 30 seconds so a cold-start Fulfillment plus carrier
/// API call (e.g. GHN create shipment) does not abort mid-flight; it can be overridden with <c>FULFILLMENT_TIMEOUT_SECONDS</c> (1 to 120).
/// </summary>
public static class FulfillmentTimeout
{
    public const int DefaultSeconds = 30;
    public const int MaxSeconds = 120;

    public static TimeSpan Resolve(IConfiguration? configuration)
    {
        var raw = Environment.GetEnvironmentVariable("FULFILLMENT_TIMEOUT_SECONDS") ?? configuration?["FULFILLMENT_TIMEOUT_SECONDS"];
        return int.TryParse(raw, out var seconds) && seconds >= 1 && seconds <= MaxSeconds
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.FromSeconds(DefaultSeconds);
    }
}
