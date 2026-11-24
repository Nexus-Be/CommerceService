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

public sealed record LocationCheckResult(LocationCheckOutcome Outcome, string? Message = null)
{
    public static LocationCheckResult Confirmed() => new(LocationCheckOutcome.Confirmed);
    public static LocationCheckResult Rejected(string? message = null) => new(LocationCheckOutcome.Rejected, message);
    public static LocationCheckResult SourceUnavailable() => new(LocationCheckOutcome.SourceUnavailable);
}
