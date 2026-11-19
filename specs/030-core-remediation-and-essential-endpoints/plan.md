# Implementation Plan: Core Remediation, Security Hardening & Essential Endpoints

**Branch**: `030-core-remediation-and-essential-endpoints` | **Date**: 2026-11-18 | **Spec**: [specs/030-core-remediation-and-essential-endpoints/spec.md](specs/030-core-remediation-and-essential-endpoints/spec.md)

**Input**: Feature specification from `/specs/030-core-remediation-and-essential-endpoints/spec.md`

## Summary

Triển khai gói tính năng nền tảng **SPEC-030 (P0)** nhằm khắc phục 6 lỗi logic, lỗ hổng bảo mật và khiếm khuyết schema nghiêm trọng trong `CommerceService`:
1. Hủy vận đơn GHN tự động khi hủy đơn hàng mẹ (`CancelOrderAsync` $\rightarrow$ `_fulfillmentClient.CancelShipmentAsync`).
2. Khóa cứng `ShippingAddressSnapshot` trong `CreateOrderAsync` theo báo giá đã chốt với GHN, ngăn ngừa sai lệch cước và mã vùng.
3. Bổ sung endpoint `GET /api/v1/auction-orders/me` cho người mua tra cứu các đơn hàng đấu giá của mình.
4. Vá lỗ hổng IDOR và kiểm tra giới hạn lượt dùng (`MaxUsesTotal`, `MaxUsesPerUser`) trong `DiscountService`.
5. Tạo file migration EF Core cho cột `SubOrders.IsEscrowSettled` để đồng bộ hoàn toàn database schema.
6. Mở quyền `isAdminOrStaff` trong `OrderService` cho phép Admin và Staff tra cứu đơn hàng phục vụ vận hành.

---

## Technical Context

**Language/Version**: C# 12 / .NET 8.0  
**Primary Dependencies**: ASP.NET Core, Entity Framework Core 8.0.10, Npgsql.EntityFrameworkCore.PostgreSQL 8.0.10  
**Storage**: PostgreSQL 16 (Port 5435, Database `commerce_db`)  
**Testing**: xUnit, Moq, FluentAssertions (`tests/Commerce.UnitTests/`)  
**Target Platform**: Linux / Windows container, Port 8003  
**Project Type**: RESTful Web API Web Service (Clean Architecture 4 tầng)  
**Performance Goals**: < 100ms response time for in-database order queries and voucher validations  
**Constraints**: Inward-only boundaries, No Repository pattern over EF Core, 100% English sentence-case error messages, PascalCase enum serialization  
**Scale/Scope**: 6 core files touched across Api, Application, and Infrastructure layers; 1 migration added; ~10 new unit test cases  

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Nguyên tắc Constitution | Đánh giá Tuân thủ | Ghi chú & Rào chắn |
|---|:---:|---|
| **I. Clean Architecture & Direct EF Core** | ✅ PASS | Không tạo Repository mới. Tiếp tục dùng trực tiếp `ICommerceDbContext` và `AppDbContext`. |
| **II. Synchronous Typed HTTP REST** | ✅ PASS | Gọi `_fulfillmentClient.CancelShipmentAsync` đồng bộ qua typed client, fail-safe logging, không dùng message broker/saga. |
| **III. Immutable Infrastructure & Ports** | ✅ PASS | Giữ nguyên cấu hình Port 8003 và Database 5435. Chuẩn RESTful URL (`/api/v1/auction-orders/me`). |
| **IV. System-Wide Error Protocol** | ✅ PASS | Mã lỗi `SCREAMING_SNAKE_CASE` (`VAL_INVALID_INPUT`, `VOUCHER_OUT_OF_STOCK`, `VOUCHER_USAGE_LIMIT_EXCEEDED`, `ORDER_CANNOT_BE_CANCELLED`), message 100% English sentence-case. |
| **V. Inward-Only Boundaries** | ✅ PASS | 100% tài nguyên nội bộ CommerceService, không tham chiếu ra ngoài repo. |

---

## Project Structure

### Documentation (this feature)

```text
specs/030-core-remediation-and-essential-endpoints/
├── plan.md              # Kế hoạch triển khai kỹ thuật
├── research.md          # Báo cáo phân tích giải pháp kỹ thuật & quyết định thiết kế
├── data-model.md        # Đặc tả mô hình dữ liệu, DTOs và ràng buộc nghiệp vụ
├── quickstart.md        # Hướng dẫn kiểm thử & kịch bản xác minh end-to-end
├── contracts/           # Hợp đồng API OpenAPI cho các endpoint mới/sửa đổi
│   ├── auction-orders-me.json
│   └── discount-validation.json
└── tasks.md             # Sẽ được sinh tự động bởi lệnh /speckit-tasks
```

### Source Code Touches

```text
Commerce.Api/
├── Controllers/Public/
│   ├── AuctionOrdersController.cs        # Thêm endpoint GET /me cho Buyer
│   └── OrdersController.cs               # Truyền cờ isAdminOrStaff vào OrderService

Commerce.Application/
├── Services/AuctionOrder/
│   ├── IAuctionOrderService.cs           # Khai báo ListBuyerOrdersAsync
│   └── AuctionOrderService.cs            # Triển khai ListBuyerOrdersAsync phân trang
├── Services/Discount/
│   └── DiscountService.cs                # Vá IDOR giỏ hàng & kiểm tra MaxUsesTotal/MaxUsesPerUser
└── Services/Order/
    ├── IOrderService.cs                  # Mở rộng chữ ký nhận isAdminOrStaff
    └── OrderService.cs                   # Hủy vận đơn GHN trong CancelOrderAsync, khóa snapshot địa chỉ

Commerce.Infrastructure/
└── Persistence/Migrations/
    ├── 20261118010000_AddSubOrderIsEscrowSettled.cs          # Migration thêm cột IsEscrowSettled
    └── 20261118010000_AddSubOrderIsEscrowSettled.Designer.cs # Migration designer file

tests/Commerce.UnitTests/
├── Application/
│   ├── OrderLifecycleTests.cs            # Test CancelOrderAsync gọi CancelShipmentAsync
│   ├── AuctionOrderServiceTests.cs       # Test ListBuyerOrdersAsync phân trang
│   └── OrderServiceTests.cs              # Test CreateOrderAsync khóa snapshot địa chỉ
├── DiscountServiceTests.cs               # Test IDOR giỏ hàng & kiểm tra usage limit
└── MigrationConsistencyTests.cs          # Kiểm chứng model snapshot khớp migration
```

**Structure Decision**: Giữ nguyên kiến trúc chuẩn 4 tầng của `Commerce.sln`.

---

## Complexity Tracking

> **Constitution Check passed 100% with 0 violations. No complexity bypasses needed.**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| *None* | *None* | *None* |
