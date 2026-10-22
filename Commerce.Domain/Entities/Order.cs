using Commerce.Domain.Base;
using Commerce.Domain.Enums;

namespace Commerce.Domain.Entities;

public class Order : BaseEntity<Guid>
{
    public Guid? CartId { get; set; }
    public required Guid BuyerId { get; set; }
    public required OrderStatus Status { get; set; } = OrderStatus.PendingPayment;
    public required decimal TotalAmount { get; set; }
    public required DateTimeOffset PaymentDeadlineAt { get; set; }
    public long RowVersion { get; set; }

    // Navigation
    public Cart? Cart { get; set; }
    public ICollection<SubOrder> SubOrders { get; set; } = new List<SubOrder>();
    public ICollection<DiscountCodeUsage> DiscountCodeUsages { get; set; } = new List<DiscountCodeUsage>();
    public OrderInvoice? Invoice { get; set; }
}
