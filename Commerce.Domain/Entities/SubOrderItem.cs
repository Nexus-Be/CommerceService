using Commerce.Domain.Base;

namespace Commerce.Domain.Entities;

public class SubOrderItem : BaseEntity<Guid>
{
    public required Guid SubOrderId { get; set; }
    public required Guid ProductId { get; set; }
    public required Guid SkuId { get; set; }
    public required Guid SellerId { get; set; }
    public required string ProductNameSnapshot { get; set; }
    public string? SkuCodeSnapshot { get; set; }
    public string SkuAttributesSnapshot { get; set; } = "{}";
    public string? ImageUrlSnapshot { get; set; }
    public required int Quantity { get; set; }
    public required decimal UnitPrice { get; set; }
    public required decimal UnitPriceAfterDiscount { get; set; }
    public required decimal LineTotal { get; set; }
    public Guid? DiscountCodeId { get; set; }

    // Navigation
    public SubOrder? SubOrder { get; set; }
}
