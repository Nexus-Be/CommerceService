using Commerce.Domain.Base;
using Commerce.Domain.Enums;

namespace Commerce.Domain.Entities;

public class DiscountCode : BaseEntity<Guid>
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required DiscountScopeType ScopeType { get; set; }
    public Guid? OwnerSellerId { get; set; }
    public required DiscountType DiscountType { get; set; }
    public required decimal DiscountValue { get; set; }
    public decimal? MaxDiscountAmount { get; set; }
    public decimal MinOrderAmount { get; set; }
    public int? MaxUsesTotal { get; set; }
    public int? MaxUsesPerUser { get; set; }
    public int UsedCount { get; set; }
    public int ReservedCount { get; set; }
    public bool IsDisabled { get; set; }
    public required DiscountStatus Status { get; set; } = DiscountStatus.Draft;
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Navigation
    public ICollection<DiscountCodeScope> Scopes { get; set; } = new List<DiscountCodeScope>();
    public ICollection<DiscountCodeUsage> Usages { get; set; } = new List<DiscountCodeUsage>();
}
