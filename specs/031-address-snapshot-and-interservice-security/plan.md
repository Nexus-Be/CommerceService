# Implementation Plan: Address Snapshot Parity, Checkout Address Integration & S2S Internal Security

**Branch**: `031-address-snapshot-and-interservice-security` | **Date**: 2026-11-19 | **Spec**: [specs/031-address-snapshot-and-interservice-security/spec.md](specs/031-address-snapshot-and-interservice-security/spec.md)

**Input**: Feature specification from `/specs/031-address-snapshot-and-interservice-security/spec.md`

## Summary

Triển khai gói tính năng **SPEC-031 (P1)** nhằm hoàn thiện tích hợp địa chỉ, API báo giá động và củng cố bảo mật giao tiếp liên dịch vụ (S2S):
1. **Tích hợp `IUserClient`**: Thêm phương thức gọi sang `UserService` lấy thông tin địa chỉ chi tiết theo `AddressId` và lấy địa chỉ mặc định `GetBuyerDefaultAddressAsync`.
2. **Address Resolution Helper**: Hỗ trợ truyền `AddressId` và tự động fallback địa chỉ mặc định trong `QuoteCheckoutAsync` (`OrdersController`) và `SubmitShippingAsync` (`AuctionOrdersController`).
3. **API Re-quote phí ship GHN**: Cung cấp `POST /api/v1/checkouts/quote/{quoteId}/recalculate` cho phép tính lại phí ship GHN đa shop khi khách hàng đổi địa chỉ tại bước thanh toán.
4. **Internal Dispute Eligibility API**: Cung cấp `GET /api/v1/internal/orders/dispute-eligibility` bảo vệ bằng `X-Internal-Service-Key` phục vụ xác minh giao dịch cho tính năng Tố cáo / Khiếu nại của `UserService`.
5. **Đồng bộ hóa Background Workers**: Đồng bộ hóa `AuctionPaymentCallbackFactory.Build` trong `AuctionCallbackRetryWorker` và dọn khối xử lý phạt seller trùng lặp trong `PaymentTimeoutWorker`.

---

## Technical Context

**Language/Version**: C# 12 / .NET 8.0  
**Primary Dependencies**: ASP.NET Core, Entity Framework Core 8.0.10, Npgsql.EntityFrameworkCore.PostgreSQL 8.0.10  
**Storage**: PostgreSQL 16 (Port 5435, Database `commerce_db`) & Distributed Cache (Redis / Memory)  
**Testing**: xUnit, Moq, FluentAssertions (`tests/Commerce.UnitTests/`)  
**Target Platform**: Linux / Windows container, Port 8003  
**Project Type**: RESTful Web API Web Service (Clean Architecture 4 tầng)  
**Performance Goals**: < 150ms cho Re-quote và Dispute Eligibility verification; resilience retry < 3s  
**Constraints**: Inward-only boundaries, No Repository pattern over EF Core, 100% English sentence-case error messages, PascalCase enum serialization, zero plaintext secret leakage  
**Scale/Scope**: 8 files touched across Api, Application, and Infrastructure layers; ~12 new unit test cases  

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Nguyên tắc Constitution | Đánh giá Tuân thủ | Ghi chú & Rào chắn |
|---|:---:|---|
| **I. Clean Architecture & Direct EF Core** | ✅ PASS | Không tạo Repository mới. Tiếp tục dùng trực tiếp `ICommerceDbContext` và `AppDbContext`. |
| **II. Synchronous Typed HTTP REST** | ✅ PASS | Gọi `_userClient` và `_fulfillmentClient` đồng bộ qua typed HttpClientFactory kèm Polly retry, không dùng message broker/saga. |
| **III. Immutable Infrastructure & Ports** | ✅ PASS | Giữ nguyên cấu hình Port 8003 và Database 5435. Chuẩn RESTful URL (`/api/v1/checkouts/quote/{quoteId}/recalculate`, `/api/v1/internal/orders/dispute-eligibility`). |
| **IV. System-Wide Error Protocol** | ✅ PASS | Mã lỗi `SCREAMING_SNAKE_CASE` (`ADDRESS_NOT_FOUND`, `ADDRESS_REQUIRED`, `QUOTE_NOT_FOUND`, `GHN_ADDRESS_INCOMPLETE`, `INTERNAL_KEY_INVALID`), message 100% English sentence-case. |
| **V. Inward-Only Boundaries** | ✅ PASS | 100% tài nguyên nội bộ CommerceService, không tham chiếu ra ngoài repo. |

---

## Project Structure

### Documentation (this feature)

```text
specs/031-address-snapshot-and-interservice-security/
├── plan.md              # Kế hoạch triển khai kỹ thuật
├── research.md          # Báo cáo phân tích giải pháp kỹ thuật & quyết định thiết kế
├── data-model.md        # Đặc tả mô hình dữ liệu, DTOs và ràng buộc nghiệp vụ
├── quickstart.md        # Hướng dẫn kiểm thử & kịch bản xác minh end-to-end
├── contracts/           # Hợp đồng API OpenAPI cho các endpoint mới/sửa đổi
│   ├── recalculate-quote.json
│   └── dispute-eligibility.json
└── tasks.md             # Sẽ được sinh tự động bởi lệnh /speckit-tasks
```

### Source Code Touches

```text
Commerce.Api/
├── Controllers/Public/
│   ├── AuctionOrdersController.cs        # Hỗ trợ AddressId & fallback default address khi submit shipping
│   └── OrdersController.cs               # Thêm endpoint POST quote/{quoteId}/recalculate & GET internal/orders/dispute-eligibility
├── Workers/
│   ├── AuctionCallbackRetryWorker.cs     # Đồng bộ hóa AuctionPaymentCallbackFactory.Build
│   └── PaymentTimeoutWorker.cs           # Dọn dẹp khối phạt seller trùng lặp

Commerce.Application/
├── Interfaces/
│   ├── IUserClient.cs                    # Bổ sung GetBuyerAddressAsync & GetBuyerDefaultAddressAsync
│   └── UserClientModels.cs               # Thêm DTO BuyerAddressDto
├── Services/AuctionOrder/
│   ├── AuctionOrderRequests.cs           # Thêm AddressId vào SubmitAuctionShippingRequest
│   └── AuctionOrderService.cs            # Address resolution logic cho đơn đấu giá
└── Services/Order/
    ├── OrderRequests.cs                  # Thêm AddressId vào CheckoutQuoteRequest & RecalculateQuoteRequest
    ├── IOrderService.cs                  # Khai báo RecalculateQuoteAsync & GetDisputeEligibilityAsync
    ├── OrderService.cs                   # Triển khai RecalculateQuoteAsync & GetDisputeEligibilityAsync
    └── DisputeEligibilityDto.cs          # DTO kết quả xác thực giao dịch cho Dispute

Commerce.Infrastructure/
└── Services/User/
    └── UserClient.cs                     # Triển khai gọi S2S lấy địa chỉ từ UserService

tests/Commerce.UnitTests/
├── Application/
│   ├── OrderServiceTests.cs              # Test RecalculateQuote & DisputeEligibility
│   └── AuctionOrderServiceTests.cs       # Test AddressId & default address fallback
└── Services/
    └── UserClientTests.cs                # Test UserClient gọi S2S địa chỉ
```

**Structure Decision**: Giữ nguyên kiến trúc chuẩn 4 tầng của `Commerce.sln`.

---

## Complexity Tracking

| Thành phần | Độ phức tạp | Lý do & Biện pháp giảm thiểu |
|---|:---:|---|
| Address Resolution Helper | Thấp | Tạo hàm dùng chung để resolve địa chỉ từ `AddressId` -> `DefaultAddress` -> `RawInput`. |
| Re-quote GHN | Trung bình | Đọc `CachedQuote`, gọi `_fulfillmentClient.CalculateShopShippingFeeAsync` từng Shop, cập nhật cache nguyên vẹn. |
| Dispute Eligibility Query | Thấp | Truy vấn `SubOrders`, `Orders`, `AuctionOrders` theo `Id`, kiểm tra trạng thái và cặp user. |
| Worker Synchronization | Thấp | Thay thế code khởi tạo bằng Factory method có sẵn. |
