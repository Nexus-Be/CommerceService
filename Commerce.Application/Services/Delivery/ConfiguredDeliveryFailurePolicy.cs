namespace Commerce.Application.Services.Delivery;

using System;
using System.Collections.Generic;
using System.Linq;
using Commerce.Application.Interfaces;
using Microsoft.Extensions.Configuration;

/// <summary>Reads the approved refusal table from the "DeliveryFailure" configuration section.</summary>
public sealed class ConfiguredDeliveryFailurePolicy : IDeliveryFailurePolicy
{
    private readonly HashSet<string> _codes;

    public ConfiguredDeliveryFailurePolicy(IConfiguration configuration)
    {
        var section = configuration.GetSection("DeliveryFailure");
        // Either a JSON array (BuyerRefusedReasonCodes:0, :1 ...) or one comma separated value, which is easier to set as an environment variable.
        var codesSection = section.GetSection("BuyerRefusedReasonCodes");
        var listed = codesSection.GetChildren().Select(child => child.Value)
            .Concat((codesSection.Value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries));
        _codes = new HashSet<string>(
            listed.Where(code => !string.IsNullOrWhiteSpace(code)).Select(code => code!.Trim()),
            StringComparer.OrdinalIgnoreCase);
        TableVersion = section["TableVersion"] ?? "unversioned";
        var delta = int.TryParse(section["BuyerRefusalScoreDelta"], out var parsed) ? parsed : 0;
        BuyerRefusalScoreDelta = delta < 0 ? Math.Max(delta, -50) : 0;
    }

    public string TableVersion { get; }

    public int BuyerRefusalScoreDelta { get; }

    public bool Enabled => _codes.Count > 0 && BuyerRefusalScoreDelta < 0;

    public bool IsBuyerRefusedCode(string? reasonCode) =>
        !string.IsNullOrWhiteSpace(reasonCode) && _codes.Contains(reasonCode.Trim());
}
