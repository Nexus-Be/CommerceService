# TÀI LIỆU ĐẶC TẢ KỸ THUẬT & HƯỚNG DẪN TRIỂN KHAI NỘI BỘ
## COMMERCE SERVICE ↔ FULFILLMENT SERVICE (THE GOLDEN STANDARD)

> **Dự án:** Nexus Commerce Ecosystem  
> **Phạm vi tài liệu:** Dành riêng cho đội ngũ phát triển **CommerceService** (Port `8003`, DB `commerce_db:5435`)  
> **Phiên bản:** `v1.2.0-INTERNAL`  
> **Ngày lập:** 2026-11-03  
> **Trạng thái:** ✅ Sẵn sàng triển khai mã nguồn  

---

## 1. TỔNG QUAN KIẾN TRÚC & NGUYÊN TẮC VẬN HÀNH

Theo **Phương án 3 (The Golden Standard)** đã được thống nhất:
1. **Phân tách trách nhiệm dữ liệu (Separation of Concerns)**:
   - FulfillmentService sở hữu Tồn kho vật lý $\rightarrow$ Tự động nhả tồn kho quá hạn qua Background Service `AutoExpireReservationsAsync` sau 15 phút.
   - CommerceService sở hữu Đơn hàng $\rightarrow$ `PaymentTimeoutWorker` chỉ quét DB nội bộ để đổi trạng thái đơn sang `Cancelled` và giải phóng voucher bằng **1 lần `SaveChangesAsync` duy nhất**. **Tuyệt đối không gửi request HTTP nhả kho dư thừa sang Fulfillment khi hết hạn.**
2. **Khi nào CommerceService gọi nhả kho?**
   - Chỉ gọi nhả kho duy nhất khi **Buyer chủ động bấm "Hủy đơn" trên App** (`OrderService.CancelOrderAsync`).
3. **Bảo mật liên dịch vụ**:
   - Mọi cuộc gọi từ Commerce sang các route `/api/v1/internal/*` của Fulfillment đều phải đính kèm Header:
     `X-Internal-Service-Key: eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e`.
   - Chiều ngược lại, endpoint nhận trạng thái giao hàng `POST /api/v1/sub-orders/{subOrderId}/delivery-status` tại Commerce cũng yêu cầu Header `X-Internal-Service-Key` này để chống giả mạo.

---

## 2. CHI TIẾT CẤU HÌNH & CODE TẦNG INFRASTRUCTURE

### 2.1. Cấu hình `appsettings.json` & `appsettings.Development.json`
Bổ sung ServiceKey vào cấu hình:
```json
{
  "InternalSecurity": {
    "ServiceKey": "eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e"
  },
  "StockPolicy": {
    "FailOpen": false
  }
}
```

### 2.2. Đăng ký `HttpClient` tại `Commerce.Infrastructure/DependencyInjection.cs`
Inject Header `X-Internal-Service-Key` mặc định:
```csharp
var internalServiceKey = Environment.GetEnvironmentVariable("INTERNAL_SERVICE_SECRET")
    ?? configuration["INTERNAL_SERVICE_SECRET"]
    ?? configuration["InternalSecurity:ServiceKey"]
    ?? "eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e";

services.AddHttpClient<IFulfillmentClient, FulfillmentClient>(client =>
{
    client.BaseAddress = new Uri(fulfillmentBaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.Add("X-Internal-Service-Key", internalServiceKey);
});
```

### 2.3. Chi tiết triển khai tại `FulfillmentClient.cs`

#### A. Kiểm tra tồn lẻ 1 SKU (`CheckStockAvailabilityAsync`)
* **Route:** `GET api/v1/inventories/{skuId}`
* **Xử lý:** Parse `availableQuantity` từ response. Nếu `availableQuantity >= quantity` $\rightarrow$ `true`, ngược lại `false`.

#### B. Batch check tồn kho giỏ hàng (`BatchCheckStockAsync`)
* **Route:** `POST api/v1/inventories/by-skus`
* **Request Body:** Gửi **MẢNG TRẦN JSON**:
  ```csharp
  var response = await _httpClient.PostAsJsonAsync("api/v1/inventories/by-skus", skuIds, JsonOptions, ct);
  ```
* **Response:** Parse mảng `InventoryItemResponse`, lấy `availableQuantity` để xác định món nào hết hàng làm xám giỏ.

#### C. Khóa giữ kho (`ReserveStockAsync`)
* **Route:** `POST api/v1/internal/inventories/reservations`
* **Payload:**
  ```json
  {
    "referenceId": "b1a9e7bc-6b84-46d5-9e17-092347640841",
    "holdMinutes": 15,
    "items": [
      {
        "skuId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
        "quantity": 2
      }
    ]
  }
  ```
* **Lỗi thiếu hàng:** Nếu nhận HTTP 400 (`INSUFFICIENT_INVENTORY`), ném `CommerceDomainException("INSUFFICIENT_STOCK", ..., 400)`.

#### D. Trừ kho vĩnh viễn (`CommitStockAsync`)
* **Route:** `POST api/v1/internal/inventories/reservations/{orderId}/commit`
* **Request Body:** Rỗng (`new StringContent("", Encoding.UTF8, "application/json")` hoặc `null`).
* **Đặc tính:** Safe Idempotency. Nếu retry lần 2, Fulfillment vẫn trả về `200 OK`.

#### E. Nhả kho khi khách bấm Hủy đơn (`ReleaseStockAsync`)
* **Route:** `POST api/v1/internal/inventories/reservations/{orderId}/release?reason=OrderCancelled`
* **Request Body:** Rỗng.

---

## 3. TỐI ƯU HÓA `PaymentTimeoutWorker.cs`

Khi đơn quá hạn 15 phút:
1. Quét danh sách đơn hàng `PendingPayment` quá hạn:
   ```csharp
   var expiredOrders = await dbContext.Orders
       .Include(o => o.SubOrders)
       .Where(o => o.Status == OrderStatus.PendingPayment && o.PaymentDeadlineAt <= now)
       .Take(50)
       .ToListAsync(stoppingToken);
   ```
2. Đổi trạng thái sang `Cancelled`, lưu lý do hủy, giải phóng mã giảm giá.
3. Commit xuống DB bằng **1 lần gọi duy nhất**:
   ```csharp
   await dbContext.SaveChangesAsync(stoppingToken);
   ```
4. **Không gọi HTTP sang Fulfillment Service.**

---

## 4. ENDPOINT CALLBACK NHẬN TRẠNG THÁI GIAO HÀNG TỪ FULFILLMENT

### 4.1. Controller Route tại `OrdersController.cs`
* **Route:** `POST /api/v1/sub-orders/{subOrderId:guid}/delivery-status`
* **Header bắt buộc:** `X-Internal-Service-Key`
* **Body:**
  ```json
  {
    "status": "Delivered",
    "deliveredAt": "2026-11-03T16:00:00Z"
  }
  ```
* **Hành vi:**
  - Kiểm tra `X-Internal-Service-Key`. Nếu không khớp $\rightarrow$ HTTP 403 `FORBIDDEN`.
  - Cập nhật `SubOrder.Status = SubOrderStatus.Delivered` và `DeliveredAt = deliveredAt`.
  - Nếu toàn bộ các `SubOrders` thuộc đơn cha đã `Delivered` $\rightarrow$ Cập nhật `Order.Status = OrderStatus.Delivered`.
