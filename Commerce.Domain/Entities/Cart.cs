using Commerce.Domain.Base;
using Commerce.Domain.Enums;

namespace Commerce.Domain.Entities;

public class Cart : BaseEntity<Guid>
{
    public required Guid BuyerId { get; set; }
    public required CartStatus Status { get; set; } = CartStatus.Active;
    public int ItemCount { get; set; }
    public decimal SubtotalAmount { get; set; }
    public DateTimeOffset LastActivityAt { get; set; } = DateTimeOffset.UtcNow;
    public long RowVersion { get; set; }

    // Navigation
    public ICollection<CartItem> Items { get; set; } = new List<CartItem>();
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}
