# Implementation Plan: Batch Order Expiration and Fulfillment Stock Release
## Phương án 3: Chuẩn Hóa Tối Ưu (The Golden Standard)

**Feature Directory**: `specs/010-batch-order-expiration-release`  
**Prerequisites**: `spec.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`  
**Constitution Authority**: `.specify/memory/constitution.md` (Version 1.3.0)  

---

## 1. Architecture & Layer Mapping

```mermaid
flowchart TD
    subgraph Commerce Api [Port 8003]
        Worker["PaymentTimeoutWorker (BackgroundService)"]
        OrdersCtrl["OrdersController.cs\nPOST /sub-orders/{id}/delivery-status"]
    end

    subgraph Commerce Application
        IOrderSvc["IOrderService"]
        IFulfillment["IFulfillmentClient"]
        ICommerceDb["ICommerceDbContext"]
    end

    subgraph Commerce Infrastructure
        FulfillmentClient["FulfillmentClient (Typed HttpClient)"]
        FulfillmentClient -.->|Default Header| AuthHeader["X-Internal-Service-Key"]
    end

    subgraph External Fulfillment Service [Port 8005 - dev branch]
        InventoriesCtrl["Public/InventoriesController\nPOST /api/v1/inventories/by-skus (Raw Array)"]
        ReservationsCtrl["Internal/InternalInventoryReservationsController\nPOST /api/v1/internal/inventories/reservations\nPOST /{referenceId}/commit\nPOST /{referenceId}/release"]
        FulfillWorker["AutoExpireReservationsAsync (Scans every 60s)"]
    end

    Worker -->|1. Quét đơn quá hạn & Batch SaveChanges (Zero-HTTP)| ICommerceDb
    FulfillmentClient -->|POST [skuIds]| InventoriesCtrl
    FulfillmentClient -->|POST {referenceId, holdMinutes, items}| ReservationsCtrl
    FulfillmentClient -->|POST /{orderId}/commit| ReservationsCtrl
    FulfillmentClient -->|POST /{orderId}/release (On Buyer Cancel)| ReservationsCtrl
    FulfillWorker -.->|Tự động nhả kho độc lập sau 15m| ReservationsCtrl
    ReservationsCtrl -.->|Webhook GHN Delivered| OrdersCtrl
```

---

## 2. Constitution & Quality Gates Check

| Nguyên Tắc Hiến Pháp | Đánh Giá Tuân Thủ | Giải Pháp Thực Thi |
| :--- | :---: | :--- |
| **I. Clean Architecture & No-Repository** | ✅ TUÂN THỦ | Không tạo repository mới. Tương tác trực tiếp qua `ICommerceDbContext` và `IFulfillmentClient`. |
| **II. Bãi bỏ RabbitMQ / Saga / gRPC** | ✅ TUÂN THỦ | 100% RESTful Web API đồng bộ và Background Service tự chủ nội bộ. |
| **III. Port & Database Bất Biến** | ✅ TUÂN THỦ | Commerce chạy Port `8003`, DB `5435`. Fulfillment chạy Port `8005`. |
| **IV. Mã Lỗi Chuẩn (RFC 9110)** | ✅ TUÂN THỦ | 100% English sentence-case message, `SCREAMING_SNAKE_CASE` mã lỗi (`INSUFFICIENT_STOCK`, `FORBIDDEN`, `STOCK_CHECK_UNAVAILABLE`). |
| **V. Inward-Only & Self-Contained** | ✅ TUÂN THỦ | Toàn bộ đường dẫn và cấu hình tự cung tự cấp bên trong repo CommerceService. |

---

## 3. File Modification & Creation Breakdown

### Phase 1: Cấu Hình Bảo Mật & Đăng Ký DI
- [`Commerce.Api/appsettings.json`](file:///d:/CommerceService/Commerce.Api/appsettings.json) & [`appsettings.Development.json`](file:///d:/CommerceService/Commerce.Api/appsettings.Development.json):
  - Bổ sung `InternalSecurity:ServiceKey: "eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e"`.
- [`Commerce.Infrastructure/DependencyInjection.cs`](file:///d:/CommerceService/Commerce.Infrastructure/DependencyInjection.cs):
  - Đăng ký Header `X-Internal-Service-Key` mặc định trong cấu hình `AddHttpClient<IFulfillmentClient, FulfillmentClient>`.

---

### Phase 2: Cập Nhật Client Giao Tiếp Kho (`FulfillmentClient.cs`)
- [`Commerce.Infrastructure/Services/Fulfillment/FulfillmentClient.cs`](file:///d:/CommerceService/Commerce.Infrastructure/Services/Fulfillment/FulfillmentClient.cs):
  - **`BatchCheckStockAsync`**: Gửi trực tiếp mảng `skuIds` (`[uuid, uuid]`) sang `POST api/v1/inventories/by-skus`.
  - **`ReserveStockAsync`**: Trỏ đến `POST api/v1/internal/inventories/reservations`, payload:
    `{ referenceId = orderId.ToString(), holdMinutes = ttlMinutes, items = items.Select(...) }`.
  - **`CommitStockAsync`**: Trỏ đến `POST api/v1/internal/inventories/reservations/{orderId}/commit` (Path param, Content rỗng). Thêm vòng lặp retry 3 lần với exponential backoff khi lỗi mạng.
  - **`ReleaseStockAsync`**: Trỏ đến `POST api/v1/internal/inventories/reservations/{orderId}/release?reason=OrderCancelled` (Path param, Content rỗng).
  - Loại bỏ silent fallback trả về `true` khi `_failOpen = false`.

---

### Phase 3: Tối Ưu Hóa Background Worker (`PaymentTimeoutWorker.cs`)
- [`Commerce.Api/Workers/PaymentTimeoutWorker.cs`](file:///d:/CommerceService/Commerce.Api/Workers/PaymentTimeoutWorker.cs):
  - Refactor `ProcessExpiredOrdersAsync`:
    - Quét danh sách đơn hàng `PendingPayment` quá hạn 15 phút.
    - Cập nhật trạng thái `Cancelled`, lưu lý do hủy, giải phóng vouchers.
    - Gọi `await dbContext.SaveChangesAsync(stoppingToken)` **1 lần duy nhất** cho toàn bộ batch.
    - **Loại bỏ hoàn toàn cuộc gọi HTTP nhả kho sang Fulfillment khi timeout** (để Fulfillment tự nhả kho).

---

### Phase 4: Endpoint Nhận Callback Trạng Thái Giao Hàng
- [`Commerce.Application/Services/Order/IOrderService.cs`](file:///d:/CommerceService/Commerce.Application/Services/Order/IOrderService.cs) & [`OrderService.cs`](file:///d:/CommerceService/Commerce.Application/Services/Order/OrderService.cs):
  - Thêm phương thức `Task UpdateSubOrderDeliveryStatusAsync(Guid subOrderId, string status, DateTimeOffset? deliveredAt, CancellationToken ct)`.
- [`Commerce.Api/Controllers/Public/OrdersController.cs`](file:///d:/CommerceService/Commerce.Api/Controllers/Public/OrdersController.cs):
  - Thêm Action `[HttpPost("/api/v1/sub-orders/{subOrderId:guid}/delivery-status")]`.
  - Kiểm tra Header `X-Internal-Service-Key` trước khi cập nhật.

---

### Phase 5: Cập Nhật Tài Liệu & Kiểm Thử Tự Động
- [`docs/FULFILLMENT_SERVICE_ALIGNMENT_CONTRACT.md`](file:///d:/CommerceService/docs/FULFILLMENT_SERVICE_ALIGNMENT_CONTRACT.md): Đã hoàn thiện bản đối soát gửi Fulfillment Lead.
- [`docs/COMMERCE_FULFILLMENT_IMPLEMENTATION_SPEC.md`](file:///d:/CommerceService/docs/COMMERCE_FULFILLMENT_IMPLEMENTATION_SPEC.md): Đã hoàn thiện hướng dẫn nội bộ.
- Unit Tests:
  - Cập nhật các mock test của `FulfillmentClient` và `PaymentTimeoutWorker` trong `tests/Commerce.UnitTests/`.
  - Chạy `dotnet test Commerce.sln -c Release` đạt 100% pass.
