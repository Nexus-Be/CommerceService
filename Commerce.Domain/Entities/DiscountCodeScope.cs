using Commerce.Domain.Base;
using Commerce.Domain.Enums;

namespace Commerce.Domain.Entities;

public class DiscountCodeScope : BaseEntity<Guid>
{
    public required Guid DiscountCodeId { get; set; }
    public required DiscountScopeRefType ScopeRefType { get; set; }
    public Guid? ScopeRefId { get; set; }

    // Navigation
    public DiscountCode? DiscountCode { get; set; }
}
