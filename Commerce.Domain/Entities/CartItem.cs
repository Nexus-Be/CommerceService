using Commerce.Domain.Base;

namespace Commerce.Domain.Entities;

public class CartItem : BaseEntity<Guid>
{
    public required Guid CartId { get; set; }
    public required Guid ProductId { get; set; }
    public required Guid SkuId { get; set; }
    public required Guid SellerId { get; set; }
    public required int Quantity { get; set; }
    public required decimal UnitPriceSnapshot { get; set; }
    public required string ProductNameSnapshot { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation
    public Cart? Cart { get; set; }
}
