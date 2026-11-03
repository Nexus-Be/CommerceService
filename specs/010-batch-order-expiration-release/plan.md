# Implementation Plan: Batch Order Expiration and Fulfillment Stock Release

**Feature Directory**: `specs/010-batch-order-expiration-release`  
**Prerequisites**: `spec.md`  

---

## 1. Architecture & Layer Mapping

```mermaid
flowchart TD
    subgraph Commerce Api
        Worker["PaymentTimeoutWorker (BackgroundService)"]
    end

    subgraph Commerce Application
        IFulfillment["IFulfillmentClient"]
        ICommerceDb["ICommerceDbContext"]
    end

    subgraph Commerce Infrastructure
        FulfillmentClient["FulfillmentClient (Typed HttpClient)"]
    end

    subgraph External Fulfillment Service [Port 8005]
        BatchReleaseEP["POST /api/v1/fulfillment/inventory/batch-release\nhoặc /api/v1/inventories/batch-release"]
    end

    Worker -->|1. Fetch expired orders (Take 50)| ICommerceDb
    Worker -->|2. Batch SaveChangesAsync (1 Call)| ICommerceDb
    Worker -->|3. BatchReleaseStockAsync (1 Call)| IFulfillment
    IFulfillment -.->|Implemented by| FulfillmentClient
    FulfillmentClient -->|POST { orderIds }| BatchReleaseEP
```

---

## 2. File Modification & Creation Breakdown

### Phase 1: Application Layer Contract
- [`Commerce.Application/Interfaces/IFulfillmentClient.cs`](file:///d:/CommerceService/Commerce.Application/Interfaces/IFulfillmentClient.cs):
  - Thêm record:
    ```csharp
    public sealed record BatchReleaseStockResult(
        List<Guid> SucceededOrderIds,
        List<Guid> FailedOrderIds,
        bool IsSuccess = true
    );
    ```
  - Thêm method vào interface:
    ```csharp
    Task<BatchReleaseStockResult> BatchReleaseStockAsync(List<Guid> orderIds, CancellationToken ct = default);
    ```

### Phase 2: Infrastructure Layer Client Implementation
- [`Commerce.Infrastructure/Services/Fulfillment/FulfillmentClient.cs`](file:///d:/CommerceService/Commerce.Infrastructure/Services/Fulfillment/FulfillmentClient.cs):
  - Cài đặt `BatchReleaseStockAsync(List<Guid> orderIds, CancellationToken ct = default)`:
    - Nếu danh sách rỗng, return ngay.
    - POST tới `api/v1/fulfillment/inventory/batch-release` với payload `{ orderIds }`.
    - Phản hồi 200/204: Parse response hoặc map `SucceededOrderIds = orderIds`.
    - Xử lý lỗi theo chính sách `_failOpen` tương tự như các hàm khác trong client.

### Phase 3: Api Layer Background Worker Refactoring
- [`Commerce.Api/Workers/PaymentTimeoutWorker.cs`](file:///d:/CommerceService/Commerce.Api/Workers/PaymentTimeoutWorker.cs):
  - Refactor `ProcessExpiredOrdersAsync`:
    - Gom `orderIds = expiredOrders.Select(o => o.Id).ToList()`.
    - Đổi trạng thái toàn bộ `order` và `subOrders` sang `Cancelled`.
    - Tạo các bản ghi `OrderCancellation`.
    - Lấy danh sách voucher usages liên quan của toàn bộ các orders trong batch và giải phóng voucher.
    - Gọi `await dbContext.SaveChangesAsync(ct);` **1 lần duy nhất**.
    - Gọi `await fulfillmentClient.BatchReleaseStockAsync(orderIds, ct);` **1 lần duy nhất**.
    - Xử lý ngoại lệ với try/catch bọc an toàn để không làm gián đoạn chu kỳ tiếp theo của worker.

### Phase 4: Unit Testing & Verification
- [`tests/Commerce.UnitTests/Application/PaymentTimeoutWorkerTests.cs`](file:///d:/CommerceService/tests/Commerce.UnitTests/Application/PaymentTimeoutWorkerTests.cs):
  - Viết test kiểm tra worker xử lý batch đơn quá hạn:
    - Kiểm tra `SaveChangesAsync` được gọi đúng 1 lần cho cả batch.
    - Kiểm tra `BatchReleaseStockAsync` được gọi đúng 1 lần với đúng danh sách `orderIds`.
    - Kiểm tra các `Order` và `SubOrder` đều được chuyển sang trạng thái `Cancelled`.
- [`tests/Commerce.UnitTests/Infrastructure/FulfillmentClientTests.cs`](file:///d:/CommerceService/tests/Commerce.UnitTests/Infrastructure/FulfillmentClientTests.cs) (hoặc cập nhật file test hiện tại):
  - Kiểm tra `BatchReleaseStockAsync` gửi đúng URL và payload `{ orderIds }`.
- Chạy `dotnet build Commerce.sln -c Release` và `dotnet test Commerce.sln -c Release` đảm bảo 100% test pass.
