# Research Report: Core Remediation, Security Hardening & Essential Endpoints

**Feature Branch**: `030-core-remediation-and-essential-endpoints`  
**Created**: 2026-11-18  
**Status**: Completed  
**Target Service**: `CommerceService`

---

## 1. Overview of Research Goals

Mục tiêu của giai đoạn nghiên cứu là phân tích chi tiết các điểm chạm kỹ thuật của 6 hạng mục P0, đối soát giải pháp thiết kế với kiến trúc Clean Architecture và bộ quy tắc bất biến trong Constitution của `CommerceService`, đảm bảo 0% lỗi hồi quy (zero regressions) và không phụ thuộc dịch vụ ngoài.

---

## 2. Research Items & Technical Decisions

### Decision 1: Cơ chế Hủy Vận đơn GHN trong `OrderService.CancelOrderAsync`
- **Vấn đề**: `CancelOrderAsync` hiện chỉ hủy trạng thái nội bộ trong DB, hoàn tiền ví, và nhả tồn kho qua `ReleaseStockAsync`. Các `SubOrder` đã `Confirmed` (đã sinh mã `TrackingCode`/`CarrierCode`) không được hủy vận đơn sang `FulfillmentService`, dẫn đến bưu tá GHN vẫn đến lấy hàng.
- **Giải pháp**:
  - Đối chiếu với `CancelSubOrderAsync`: Phương thức này đã có đoạn kiểm tra `so.TrackingCode` và gọi `_fulfillmentClient.CancelShipmentAsync(so.Id, reason, ct)`.
  - Trong vòng lặp `foreach (var so in targetSubOrders)` của `CancelOrderAsync`:
    ```csharp
    if (!string.IsNullOrWhiteSpace(so.TrackingCode) || !string.IsNullOrWhiteSpace(so.CarrierCode))
    {
        try
        {
            await _fulfillmentClient.CancelShipmentAsync(
                so.Id,
                request.ReasonText ?? "Buyer cancelled order before shipping.",
                ct);
            _logger.LogInformation("Cancelled fulfillment shipment for sub-order {SubOrderId}.", so.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cancel shipment for sub-order {SubOrderId} with carrier. Proceeding with order cancellation.", so.Id);
        }
    }
    ```
  - **Rationale**: Bọc trong `try-catch` đảm bảo tính fail-safe: việc hủy đơn hàng và hoàn tiền ví cho người mua không bị đình trệ nếu hệ thống đối tác Fulfillment tạm thời gián đoạn hoặc bưu cục GHN trả về lỗi từ chối hủy.

---

### Decision 2: Khóa dữ liệu `ShippingAddressSnapshot` trong `OrderService.CreateOrderAsync`
- **Vấn đề**: `CreateOrderRequest.ShippingAddress` là chuỗi text tự do. Code hiện tại đang gán trực tiếp:
  ```csharp
  var addressSnapshot = new ShippingAddressSnapshot(
      cachedQuote.Request.ShippingAddress.ReceiverName,
      cachedQuote.Request.ContactPhone,
      cachedQuote.Request.ShippingAddress.Street,
      cachedQuote.Request.ShippingAddress.Ward,
      cachedQuote.Request.ShippingAddress.District ?? string.Empty,
      cachedQuote.Request.ShippingAddress.Province,
      cachedQuote.Request.ShippingAddress.GhnDistrictId ?? 0,
      cachedQuote.Request.ShippingAddress.GhnWardCode ?? "",
      request.ShippingAddress // <-- NGUY HIỂM: Ghi đè text tự do làm lệch với GhnDistrictId
  );
  ```
- **Giải pháp**:
  - Ưu tiên sử dụng `cachedQuote.Request.ShippingAddress.FullAddressText`. Chỉ fallback sang `request.ShippingAddress` nếu `FullAddressText` trong snapshot báo giá rỗng:
  ```csharp
  var effectiveFullAddress = !string.IsNullOrWhiteSpace(cachedQuote.Request.ShippingAddress.FullAddressText)
      ? cachedQuote.Request.ShippingAddress.FullAddressText
      : request.ShippingAddress;
  ```
  - **Rationale**: Bảo toàn 100% tính nhất quán giữa chuỗi địa chỉ hiển thị với mã quận/huyện và phường/xã của GHN đã được chốt và tính cước trong `CachedCheckoutQuote`.

---

### Decision 3: Thiết kế API `GET /api/v1/auction-orders/me` cho Buyer
- **Vấn đề**: Người mua thắng đấu giá chưa có API xem danh sách các đơn đấu giá của mình.
- **Giải pháp**:
  - Controller: `AuctionOrdersController.cs`
    - Action: `[HttpGet("me")]`
    - Filter: `[RequireRole("buyer", "admin", "staff")]`
    - Query params: `int page = 1`, `int pageSize = 20`, `SubOrderStatus? status = null`.
    - Trích xuất `currentUserId` an toàn qua `this.TryGetUserId()`.
  - Service: Thêm vào `IAuctionOrderService` và `AuctionOrderService`:
    ```csharp
    Task<PagedData<AuctionOrderSummaryResponse>> ListBuyerOrdersAsync(
        Guid buyerId,
        int page,
        int pageSize,
        SubOrderStatus? status = null,
        CancellationToken ct = default);
    ```
  - Query DB:
    ```csharp
    var query = _dbContext.AuctionOrders
        .AsNoTracking()
        .Where(a => a.BuyerId == buyerId && !a.IsDeleted);

    if (status.HasValue)
    {
        query = query.Where(a => a.Status == status.Value);
    }

    var totalCount = await query.CountAsync(ct);
    var items = await query
        .OrderByDescending(a => a.CreatedAt)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync(ct);
    ```
  - Map sang `AuctionOrderSummaryResponse` thông qua `MapToSummary` (đã hỗ trợ `DepositAmount`).
  - **Rationale**: Tương thích hoàn toàn với cấu trúc phân trang chuẩn `PagedData<T>` của hệ thống, tuân thủ Nguyên tắc 4.6 (Pagination Governance).

---

### Decision 4: Vá IDOR & Kiểm tra Giới hạn Lượt dùng trong `DiscountService`
- **Vấn đề**:
  1. `GetAvailableVouchersAsync`, `ValidateVoucherAsync`, `RecommendVouchersAsync` nạp `CartItems` không lọc `ci.Cart.BuyerId == buyerId`.
  2. `ValidateVoucherAsync` chưa kiểm tra `MaxUsesTotal` và `MaxUsesPerUser`.
- **Giải pháp**:
  1. **Chống IDOR**: Cập nhật cả 3 phương thức:
     ```csharp
     cartItems = await _db.CartItems
         .AsNoTracking()
         .Where(ci => request.CartItemIds.Contains(ci.Id) && ci.Cart.BuyerId == buyerId && !ci.IsDeleted)
         .ToListAsync(ct);
     ```
  2. **Kiểm tra giới hạn tổng (`MaxUsesTotal`)**:
     ```csharp
     if (voucher.MaxUsesTotal.HasValue && (voucher.UsedCount + voucher.ReservedCount) >= voucher.MaxUsesTotal.Value)
     {
         return new ValidateVoucherResponse(false, "VOUCHER_OUT_OF_STOCK", "Voucher has reached its total usage limit.", 0m);
     }
     ```
  3. **Kiểm tra giới hạn trên từng User (`MaxUsesPerUser`)**:
     ```csharp
     if (voucher.MaxUsesPerUser.HasValue)
     {
         var userUsageCount = await _db.DiscountCodeUsages
             .AsNoTracking()
             .CountAsync(u => u.DiscountCodeId == voucher.Id && u.UserId == buyerId && u.Status != DiscountUsageStatus.Released, ct);

         if (userUsageCount >= voucher.MaxUsesPerUser.Value)
         {
             return new ValidateVoucherResponse(false, "VOUCHER_USAGE_LIMIT_EXCEEDED", "You have reached the usage limit for this voucher.", 0m);
         }
     }
     ```
  - **Rationale**: Đảm bảo an toàn tài nguyên giỏ hàng của người dùng khác và kiểm soát chặt chẽ ngân sách khuyến mãi của sàn/shop.

---

### Decision 5: Bổ sung Migration EF Core cho `SubOrders.IsEscrowSettled`
- **Vấn đề**: `SubOrder.cs` và `AppDbContextModelSnapshot.cs` đã khai báo property `IsEscrowSettled`, nhưng chưa có file migration vật lý tương ứng.
- **Giải pháp**:
  - Tạo migration file: `Commerce.Infrastructure/Persistence/Migrations/20261118010000_AddSubOrderIsEscrowSettled.cs`.
  - Cập nhật migration code:
    ```csharp
    migrationBuilder.AddColumn<bool>(
        name: "IsEscrowSettled",
        table: "SubOrders",
        type: "boolean",
        nullable: false,
        defaultValue: false);
    ```
  - Tạo file designer đi kèm `20261118010000_AddSubOrderIsEscrowSettled.Designer.cs`.
  - **Rationale**: Đảm bảo cơ sở dữ liệu vật lý khớp 100% với model snapshot của EF Core, vượt qua bài kiểm tra `MigrationConsistencyTests`.

---

### Decision 6: Mở quyền Admin / Staff trong `OrderService`
- **Vấn đề**: Các phương thức tra cứu đơn hàng ném `403 FORBIDDEN` khi người dùng không phải là `BuyerId` hoặc `SellerId`, khiến Admin/Staff không thể truy xuất thông tin để hỗ trợ khách hàng.
- **Giải pháp**:
  - Mở rộng chữ ký 4 hàm trong `IOrderService` và `OrderService`:
    ```csharp
    Task<OrderResponse> GetOrderDetailAsync(Guid currentUserId, Guid orderId, bool isAdminOrStaff = false, CancellationToken ct = default);
    Task<SubOrderDetailResponse> GetSubOrderDetailAsync(Guid currentUserId, Guid subOrderId, bool isAdminOrStaff = false, CancellationToken ct = default);
    Task<ShipmentTrackingResponse> GetShipmentTrackingAsync(Guid currentUserId, Guid orderId, bool isAdminOrStaff = false, CancellationToken ct = default);
    Task<ShipmentTimelineResponse> GetShipmentTimelineAsync(Guid currentUserId, Guid subOrderId, bool isAdminOrStaff = false, CancellationToken ct = default);
    ```
  - Cập nhật điều kiện kiểm tra quyền:
    ```csharp
    if (order.BuyerId != currentUserId && !isAdminOrStaff)
    {
        throw new CommerceDomainException("FORBIDDEN", "You do not have permission to access this order.", 403);
    }
    ```
  - Trên `OrdersController.cs`:
    ```csharp
    var isAdminOrStaff = this.IsAdmin() || this.HasRole("staff");
    var order = await _orderService.GetOrderDetailAsync(currentUserId, orderId, isAdminOrStaff, ct);
    ```
  - **Rationale**: Đồng bộ chuẩn phân quyền đã được áp dụng thành công trên `AuctionOrdersController.GetOrderDetail`.
