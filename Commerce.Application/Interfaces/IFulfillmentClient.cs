namespace Commerce.Application.Interfaces;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public sealed record SkuStockCheckResult(
    Guid SkuId,
    bool IsAvailable,
    int AvailableStock,
    string? DisabledReason = null
);

public sealed record StockReservationItem(
    Guid SkuId,
    int Quantity,
    Guid SellerId
);

public sealed record ShippingQuoteItem(
    Guid SkuId,
    int Quantity,
    int WeightGrams = 200
);

public sealed record ShopShippingQuoteRequest(
    Guid SellerId,
    int GhnDistrictId,
    string GhnWardCode,
    List<ShippingQuoteItem> Items
);

public sealed record ShipmentCreationResult(
    string TrackingCode,
    string CarrierCode,
    string LabelPrintUrl
);

public sealed record BatchReleaseStockResult(
    List<Guid> SucceededOrderIds,
    List<Guid> FailedOrderIds,
    bool IsSuccess = true
);

public interface IFulfillmentClient
{
    /// <summary>Check nhanh 1 SKU khi thêm món vào giỏ hàng (CART-04).</summary>
    Task<bool> CheckStockAvailabilityAsync(Guid skuId, int quantity, CancellationToken ct = default);

    /// <summary>Batch check danh sách SKU để làm xám các món hết hàng trong giỏ (CART-01, CART-03, CART-11).</summary>
    Task<Dictionary<Guid, SkuStockCheckResult>> BatchCheckStockAsync(List<Guid> skuIds, CancellationToken ct = default);

    /// <summary>Khóa giữ hàng tạm thời khi bấm Đặt hàng (TTL 15 phút) (CHK-02).</summary>
    Task<bool> ReserveStockAsync(Guid orderId, List<StockReservationItem> items, int ttlMinutes = 15, CancellationToken ct = default);

    /// <summary>Xác nhận trừ kho vĩnh viễn sau khi thanh toán thành công.</summary>
    Task<bool> CommitStockAsync(Guid orderId, CancellationToken ct = default);

    /// <summary>Nhả hàng lại cho kho khi đơn bị Hủy hoặc Quá hạn thanh toán 15 phút.</summary>
    Task<bool> ReleaseStockAsync(Guid orderId, CancellationToken ct = default);

    /// <summary>Nhả hàng lại cho kho hàng loạt khi nhiều đơn bị Quá hạn thanh toán (chống N+1 HTTP calls).</summary>
    Task<BatchReleaseStockResult> BatchReleaseStockAsync(List<Guid> orderIds, CancellationToken ct = default);

    /// <summary>Nhả hàng cho kho chỉ riêng các món thuộc về 1 SubOrder khi Shop từ chối đơn con.</summary>
    Task<bool> ReleaseSubOrderStockAsync(Guid subOrderId, List<StockReservationItem> items, CancellationToken ct = default);

    /// <summary>Tính cước phí ship GHN động theo từng Shop khi xem trước báo giá (CHK-01).</summary>
    Task<decimal> CalculateShopShippingFeeAsync(ShopShippingQuoteRequest request, CancellationToken ct = default);

    /// <summary>Tạo vận đơn GHN khi Shop tiếp nhận đơn hàng con (ORD-04).</summary>
    Task<ShipmentCreationResult> CreateShipmentAsync(Guid subOrderId, CancellationToken ct = default);
}
