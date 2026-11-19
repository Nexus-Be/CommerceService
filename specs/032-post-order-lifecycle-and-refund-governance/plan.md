# Implementation Plan: Post-Order Lifecycle, Address Modification and Refund Governance

**Branch**: `032-post-order-lifecycle-and-refund-governance` | **Date**: 2026-11-19 | **Spec**: [specs/032-post-order-lifecycle-and-refund-governance/spec.md](specs/032-post-order-lifecycle-and-refund-governance/spec.md)

**Input**: Feature specification from `/specs/032-post-order-lifecycle-and-refund-governance/spec.md`

## Summary

Triển khai gói tính năng **SPEC-032 (P1/P2)** nhằm hoàn thiện các tính năng sau đặt hàng (post-order), quản trị hoàn tiền tập trung và mở rộng vòng đời đơn hàng đấu giá:
1. **Đổi địa chỉ giao hàng sau khi đặt hàng (Post-order Change Address)**:
   - Cung cấp `PATCH /api/v1/sub-orders/{subOrderId}/shipping-address`, `PATCH /api/v1/orders/{orderId}/shipping-address`, và `PATCH /api/v1/auction-orders/{auctionOrderId}/shipping-address`.
   - Kiểm soát tính hợp lệ: Chỉ cho phép đổi khi đơn ở trạng thái `PendingPayment`, `Paid`, hoặc `Confirmed` và chưa có `TrackingCode`.
   - Kiểm soát cước vận chuyển GHN: Nếu đã thanh toán (`Paid`), chỉ cho phép đổi khi `newShippingFee <= oldShippingFee`; nếu cước mới tăng, chặn với HTTP 400 `SHIPPING_FEE_INCREASED`.
2. **Chuẩn hóa & Mở rộng Quản trị Hoàn tiền (`OrderRefund`)**:
   - Sửa `OrderService.RefundSubOrderAsync`: Chặn tạo yêu cầu refund tùy tiện (chỉ cho phép khi `Delivered` hoặc `Completed`), chống gửi trùng khi đã có yêu cầu `Requested` (HTTP 409 `REFUND_REQUEST_ALREADY_EXISTS`).
   - Tạo `RefundsController` cung cấp:
     - `GET /api/v1/refunds` (Danh sách phân trang tại database theo role).
     - `GET /api/v1/refunds/{refundId}` (Chi tiết yêu cầu hoàn tiền).
     - `POST /api/v1/refunds/{refundId}/approve` (Duyệt hoàn tiền ví qua `WalletClient` và chuyển `SubOrder` sang `Refunded`).
     - `POST /api/v1/refunds/{refundId}/reject` (Từ chối hoàn tiền kèm lý do).
3. **Mở rộng vòng đời Đơn hàng Đấu giá**:
   - `GET /api/v1/auction-orders/{id}/shipments/timeline`: Lộ trình vận chuyển chi tiết.
   - `POST /api/v1/auction-orders/{id}/cancel`: Cho phép người mua hủy khi `PendingPayment` hoặc `Paid` trước khi Seller accept, hoàn tiền ví và gửi callback `AuctionService`.
   - `POST /api/v1/auction-orders/{id}/shipments/retry`: Thử lại tạo vận đơn GHN nếu nghẽn mạng lúc xác nhận đơn.
4. **Quản trị Đơn hàng Tập trung cho Admin & Staff**:
   - `GET /api/v1/admin/orders`: Tra cứu danh sách đơn cha toàn sàn, phân trang DB, lọc đa tiêu chí.
   - `GET /api/v1/admin/sub-orders`: Tra cứu danh sách đơn con toàn sàn, phân trang DB, lọc đa tiêu chí.

---

## Technical Context

**Language/Version**: C# 12 / .NET 8.0  
**Primary Dependencies**: ASP.NET Core, Entity Framework Core 8.0.10, Npgsql.EntityFrameworkCore.PostgreSQL 8.0.10  
**Storage**: PostgreSQL 16 (Port 5435, Database `commerce_db`) & Distributed Cache (Redis / Memory)  
**Testing**: xUnit, Moq, FluentAssertions (`tests/Commerce.UnitTests/`)  
**Target Platform**: Linux / Windows container, Port 8003  
**Project Type**: RESTful Web API Web Service (Clean Architecture 4 tầng)  
**Performance Goals**: < 100ms cho các API quản trị hoàn tiền & tra cứu admin; phân trang trực tiếp tại database qua `.Skip().Take()`  
**Constraints**: Inward-only boundaries, No Repository pattern over EF Core, 100% English sentence-case error messages, PascalCase enum serialization, buyer ownership check  
**Scale/Scope**: 10 files touched/created across Api, Application, and UnitTests; ~15 new unit tests  

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Nguyên tắc Constitution | Đánh giá Tuân thủ | Ghi chú & Rào chắn |
|---|:---:|---|
| **I. Clean Architecture & Direct EF Core** | ✅ PASS | Sử dụng trực tiếp `ICommerceDbContext` và `AppDbContext`. Cấm Repository pattern. |
| **II. Synchronous Typed HTTP REST** | ✅ PASS | Gọi `_walletClient`, `_fulfillmentClient`, `_userClient` đồng bộ qua typed HTTP clients kèm Polly. |
| **III. Immutable Infrastructure & Ports** | ✅ PASS | Cố định Port 8003 và Database 5435. Quy chuẩn RESTful URL số nhiều (`/api/v1/refunds`, `/api/v1/admin/orders`, `/api/v1/admin/sub-orders`). |
| **IV. System-Wide Error Protocol** | ✅ PASS | Mã lỗi `SCREAMING_SNAKE_CASE` (`CANNOT_CHANGE_ADDRESS_AFTER_SHIPPING`, `SHIPPING_FEE_INCREASED`, `INVALID_REFUND_STATUS`, `REFUND_REQUEST_ALREADY_EXISTS`, `REFUND_NOT_FOUND`), message 100% English sentence-case. |
| **V. Inward-Only Boundaries** | ✅ PASS | 100% tài nguyên nội bộ CommerceService. Không tham chiếu ra ngoài repo. |

---

## Project Structure

### Documentation (this feature)

```text
specs/032-post-order-lifecycle-and-refund-governance/
├── plan.md              # Kế hoạch triển khai kỹ thuật
├── research.md          # Quyết định thiết kế kỹ thuật cho đổi địa chỉ & hoàn tiền
├── data-model.md        # Đặc tả mô hình dữ liệu, DTOs & Validation
├── quickstart.md        # Hướng dẫn kiểm thử & xác minh tính năng
├── contracts/           # Hợp đồng API OpenAPI
│   ├── change-shipping-address.json
│   └── refund-governance.json
└── tasks.md             # Danh sách task triển khai chi tiết
```

### Source Code Touches

```text
Commerce.Api/
├── Controllers/Public/
│   ├── OrdersController.cs               # Thêm PATCH sub-orders/{id}/shipping-address, PATCH orders/{id}/shipping-address
│   ├── AuctionOrdersController.cs        # Thêm PATCH auction-orders/{id}/shipping-address, timeline, cancel, retry
│   ├── RefundsController.cs              # MỚI: CRUD & Approve/Reject hoàn tiền (/api/v1/refunds)
│   └── AdminOrdersController.cs          # MỚI hoặc trong OrdersController: GET /api/v1/admin/orders & /api/v1/admin/sub-orders

Commerce.Application/
├── Services/Order/
│   ├── OrderRequests.cs                  # ChangeShippingAddressRequest, ApproveRefundRequest, RejectRefundRequest
│   ├── OrderResponses.cs                 # RefundDetailResponse, AdminOrderFilterRequest
│   ├── IOrderService.cs                  # Khai báo ChangeShippingAddressAsync, ListRefundsAsync, ApproveRefundAsync, RejectRefundAsync, ListAdminOrdersAsync
│   └── OrderService.cs                   # Triển khai các use case hoàn tiền và đổi địa chỉ
├── Services/AuctionOrder/
│   ├── IAuctionOrderService.cs           # Khai báo ChangeAuctionShippingAddressAsync, CancelAuctionOrderAsync, RetryAuctionShipmentAsync, GetAuctionShipmentTimelineAsync
│   └── AuctionOrderService.cs            # Triển khai đổi địa chỉ & vòng đời mở rộng đơn đấu giá

tests/Commerce.UnitTests/
├── Application/
│   ├── OrderAddressChangeTests.cs        # Kiểm thử đổi địa chỉ SubOrder/Order/AuctionOrder & kiểm soát cước GHN
│   ├── RefundGovernanceTests.cs          # Kiểm thử tạo, duyệt, từ chối refund và kiểm tra trạng thái
│   └── AuctionOrderExtendedLifecycleTests.cs # Kiểm thử timeline, cancel, retry cho auction order
```

**Structure Decision**: Giữ vững kiến trúc 4 tầng Clean Architecture, No-Repository over EF Core.
