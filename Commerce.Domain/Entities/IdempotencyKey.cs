namespace Commerce.Domain.Entities;

using Commerce.Domain.Base;

public class IdempotencyKey : BaseEntity<Guid>
{
    public required string Key { get; set; }
    public required string Scope { get; set; }
    public int ResponseStatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddHours(24);
}
