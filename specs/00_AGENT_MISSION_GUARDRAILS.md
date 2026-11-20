# 00 — NHIỆM VỤ CỐT LÕI & RÀO CHẮN CHỐNG ẢO GIÁC (COMMERCE SERVICE)

> **Trạng thái triển khai (2026-11-16):** ✅ ĐÃ HOÀN THIỆN & CHUẨN HÓA 100% RESTFUL — Quản lý giỏ hàng, đơn hàng đa người bán, đơn hàng đấu giá, điều phối thanh toán 2 pha, vận đơn GHN và giải ngân Escrow.
> **Nguồn sự thật:** Code trong `Commerce.sln` (`Commerce.Api/Controllers/`, `Commerce.Application/Services/`).
> **Cập nhật lần cuối:** 2026-11-16.

---

## 1. NHIỆM VỤ & RANH GIỚI MIỀN (BOUNDED CONTEXT)

**CommerceService** chịu trách nhiệm độc quyền về điều phối giao dịch thương mại, giỏ hàng và vòng đời đơn hàng:
| # | Phân hệ nghiệp vụ | Phạm vi & Trách nhiệm | Bằng chứng thực tế trong Code |
|---|---|---|---|
| 1 | **Quản lý Giỏ hàng (Cart)** | Quản lý giỏ hàng active, thêm, sửa số lượng, đổi variant SKU, gộp giỏ vãng lai, tính tiền theo selection (Shopee checkbox), dọn giỏ sau checkout. | `CartsController.cs`, `CartService.cs` (`CART-01` .. `CART-14`) |
| 2 | **Báo giá & Đặt hàng Đa Người Bán** | Tiếp nhận báo giá (`POST /api/v1/checkouts/quote`, TTL 15m), tính ship GHN theo từng Shop, tạo Master Order và các SubOrder con theo Seller. | `OrdersController.cs`, `OrderService.cs` (`R75`, `R76`, `CHK-01` .. `CHK-02`) |
| 3 | **Vòng đời Đơn hàng & Escrow Settlement** | Chuyển trạng thái SubOrder (`Confirmed`, `Shipped`, `Completed`, `Cancelled`, `Refunded`), tự động giải ngân quỹ Escrow sang ví Seller khi hoàn tất. | `OrdersController.cs`, `OrderService.cs` (`R77` .. `R101`) |
| 4 | **Đơn hàng Đấu giá (Auction Orders)** | Tiếp nhận webhook settlement từ Auction Service (`US1`), chốt địa chỉ & phí ship (`US2`), thanh toán ví (`US3`), người bán duyệt/từ chối (`US4`, `US5`), buyer xác nhận nhận hàng (`US8`). | `AuctionOrdersController.cs`, `AuctionOrderService.cs` (`US1` .. `US9`) |
| 5 | **Điều phối Thanh toán Đồng bộ (Typed REST Orchestration)** | Tích hợp đồng bộ qua `HttpClientFactory` với `WalletService` (8008), `CatalogService` (8002), `FulfillmentService` (8005), `UserService` (8001), `AuctionService` (8004). | `Commerce.Infrastructure/Services/*Client.cs` |
| 6 | **Công việc Nền Tự động (Hosted Background Workers)** | Tự động hủy đơn hàng / quote hết hạn thanh toán (`PaymentTimeoutWorker`), quét đối soát trạng thái giao dịch VNPay (`VnPayPaymentPollingWorker`). | `Commerce.Api/Workers/*.cs` |

---

## 2. NHỮNG ĐIỀU CẤM KỴ ĐỐI VỚI AI AGENTS (ANTI-HALLUCINATION GUARDRAILS)

1. **TUYỆT ĐỐI KHÔNG DÙNG REPOSITORY PATTERN BỌC NGOÀI EF CORE**:
   - Sử dụng trực tiếp `ICommerceDbContext` và `DbSet<T>` trong tầng Application.
   - Tuyệt đối cấm tạo `IOrderRepository`, `ICartRepository`, `UnitOfWork`.
2. **CẤM SỬ DỤNG gRPC, RABBITMQ, MASSTRANSIT VÀ SAGA STATE MACHINE**:
   - Hệ thống hoạt động 100% bằng RESTful Web API (JSON RFC 9110).
   - Tuyệt đối không thêm package gRPC, proto files hay hàng đợi message broker.
3. **KHÔNG TỰ Ý TRỪ TIỀN HOẶC TRỪ KHO NỘI BỘ BÊN TRONG COMMERCE**:
   - `CommerceService` **KHÔNG** sở hữu kho hàng và **KHÔNG** sở hữu số dư tiền ví.
   - Giữ kho/trừ kho do `FulfillmentService` (và Catalog) thụ lý. Trừ tiền ví do `WalletService` thụ lý qua Typed Client `IWalletClient`.
   - Khi có lỗi ở một pha trong chuỗi checkout, phải kích hoạt lệnh đền bù (Compensating action, e.g. `ReleaseStockAsync`).
4. **BẮT BUỘC KIỂM TRA IDEMPOTENCY-KEY KHI THAY ĐỔI TRẠNG THÁI TIỀN / ĐƠN**:
   - Các API tạo đơn (`POST /api/v1/orders`), thanh toán đơn đấu giá (`POST /api/v1/auction-orders/{id}/pay`), hoàn tiền (`POST /api/v1/sub-orders/{id}/refund`) phải hỗ trợ bảo vệ idempotency để chống click đúp.
5. **CẤU HÌNH PORT 8003 VÀ DATABASE 5435 LÀ BẤT BIẾN**:
   - Port runtime của CommerceService là `8003`.
   - CSDL biệt lập là PostgreSQL `commerce_db` tại port `5435`.
6. **QUY TẮC PHẠM VI NỘI BỘ (INWARD-ONLY)**:
   - Mọi tài liệu và đường dẫn chỉ được trỏ tới nội bộ `CommerceService`, tuyệt đối không trỏ link ngược ra bên ngoài root (`../../../docs/...`) hay các microservice khác (`../../Catalog/...`).
7. **QUY CHUẨN THÔNG BÁO LỖI**:
   - 100% Tiếng Anh, viết hoa dạng Sentence case (`"Cart item not found."`, `"Order cannot be cancelled in its current status."`).
   - Mã lỗi định dạng `SCREAMING_SNAKE_CASE` (ví dụ: `ORDER_NOT_FOUND`, `CART_NOT_FOUND`, `VAL_INVALID_INPUT`).
