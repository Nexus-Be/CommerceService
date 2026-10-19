using Commerce.Domain.Base;
using Commerce.Domain.Enums;

namespace Commerce.Domain.Entities;

public class CheckoutGroup : BaseEntity<Guid>
{
    public Guid? CartId { get; set; }
    public required Guid BuyerId { get; set; }
    public required CheckoutGroupStatus Status { get; set; } = CheckoutGroupStatus.PendingPayment;
    public required decimal TotalAmount { get; set; }
    public required DateTimeOffset PaymentDeadlineAt { get; set; }
    public long RowVersion { get; set; }

    // Navigation
    public Cart? Cart { get; set; }
    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<DiscountCodeUsage> DiscountCodeUsages { get; set; } = new List<DiscountCodeUsage>();
}
