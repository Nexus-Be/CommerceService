namespace Commerce.Application.Common.Models;

using System;
using System.ComponentModel.DataAnnotations;

public sealed record PlatformFeeResponse(
    decimal DefaultPercent,
    string UpdatedByRole,
    DateTimeOffset UpdatedAt
);

public sealed record UpdatePlatformFeeRequest(
    [Range(0.0, 100.0, ErrorMessage = "Platform fee rate must be between 0% and 100%.")]
    decimal DefaultPercent
);
