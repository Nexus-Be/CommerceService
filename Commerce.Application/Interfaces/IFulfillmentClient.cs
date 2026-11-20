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

/// <summary>One parcel line sent to Fulfillment when a shipment is created. Weight is unknown for some orders, in which case Fulfillment applies its default.</summary>
public sealed record ShipmentCreationItem(Guid SkuId, int Quantity, string? Name, decimal Price, int? WeightInGrams);

/// <summary>
/// Everything Fulfillment needs to register a carrier shipment (it rejects a request without the order, seller and recipient).
/// For an auction order OrderId and SubOrderId are both the auction order ID, because it has no parent order.
/// </summary>
public sealed record ShipmentCreationRequest(
    Guid OrderId,
    Guid SubOrderId,
    Guid SellerId,
    string RecipientName,
    string RecipientPhone,
    string ShippingAddress,
    string? ToProvinceName,
    int? ToDistrictId,
    string? ToWardCode,
    int WeightInGrams,
    IReadOnlyList<ShipmentCreationItem> Items);

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

    /// <summary>Nhả hàng cho kho chỉ riêng các món thuộc về 1 SubOrder khi Shop từ chối đơn con (truyền Master orderId và danh sách items).</summary>
    Task<bool> ReleaseSubOrderStockAsync(Guid orderId, List<StockReservationItem> items, CancellationToken ct = default);

    /// <summary>Nhả hàng cho kho chỉ riêng các món thuộc về 1 SubOrder kèm lý do cụ thể (SubOrderRejected, PackageReturned).</summary>
    Task<bool> ReleaseSubOrderStockAsync(Guid orderId, List<StockReservationItem> items, string reason, CancellationToken ct = default);

    /// <summary>Tính cước phí ship GHN động theo từng Shop khi xem trước báo giá (CHK-01).</summary>
    Task<decimal> CalculateShopShippingFeeAsync(ShopShippingQuoteRequest request, CancellationToken ct = default);

    /// <summary>Tạo vận đơn GHN khi Shop tiếp nhận đơn hàng con (ORD-04).</summary>
    Task<ShipmentCreationResult> CreateShipmentAsync(ShipmentCreationRequest request, CancellationToken ct = default);

    /// <summary>Hủy vận đơn đã tạo bên bưu cục GHN khi đơn con bị hủy trước khi lấy hàng.</summary>
    Task<bool> CancelShipmentAsync(Guid subOrderId, string reason = "SubOrderCancelled", CancellationToken ct = default);
}
