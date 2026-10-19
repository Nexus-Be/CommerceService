using Commerce.Domain.Base;
using Commerce.Domain.Enums;

namespace Commerce.Domain.Entities;

public class DiscountCodeUsage : BaseEntity<Guid>
{
    public required Guid DiscountCodeId { get; set; }
    public required Guid UserId { get; set; }
    public Guid? OrderId { get; set; }
    public Guid? CheckoutGroupId { get; set; }
    public required DiscountUsageStatus Status { get; set; } = DiscountUsageStatus.Reserved;
    public decimal DiscountAmount { get; set; }
    public DateTimeOffset ReservedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }

    // Navigation
    public DiscountCode? DiscountCode { get; set; }
    public Order? Order { get; set; }
    public CheckoutGroup? CheckoutGroup { get; set; }
}
