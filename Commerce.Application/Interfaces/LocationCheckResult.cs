namespace Commerce.Application.Interfaces;

/// <summary>What the GHN lists (served by Fulfillment) said about a province, district and ward chain.</summary>
public enum LocationCheckOutcome
{
    /// <summary>No answer was produced. A caller that did not get an explicit outcome treats this as "cannot say".</summary>
    Unknown = 0,
    /// <summary>The three codes are in the GHN lists and belong together.</summary>
    Confirmed,
    /// <summary>The GHN lists were read and the chain is not valid.</summary>
    Rejected,
    /// <summary>No trusted GHN source answered, so nothing can be said about the chain.</summary>
    SourceUnavailable,
}

public sealed record LocationCheckResult(
    LocationCheckOutcome Outcome,
    string? Message = null,
    string? ProvinceName = null,
    string? DistrictName = null,
    string? WardName = null)
{
    /// <summary>The chain is confirmed; the names are the ones the GHN lists gave for the three codes (null when the answer carried none).</summary>
    public static LocationCheckResult Confirmed(string? provinceName = null, string? districtName = null, string? wardName = null) =>
        new(LocationCheckOutcome.Confirmed, null, provinceName, districtName, wardName);
    public static LocationCheckResult Rejected(string? message = null) => new(LocationCheckOutcome.Rejected, message);
    public static LocationCheckResult SourceUnavailable() => new(LocationCheckOutcome.SourceUnavailable);
}
