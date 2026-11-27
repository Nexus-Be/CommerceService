# BƯỚC 1: KHẢO SÁT TOÀN DIỆN COMMERCE SERVICE

> **Ngày thực hiện:** 2026-11-27  
> **Người thực hiện:** Software Architect & Technical Writer  
> **Trạng thái:** Hoàn thành khảo sát  

---

## 1. THÔNG TIN TỔNG QUAN SERVICE

| Tiêu chí | Chi tiết thực tế trong Codebase |
|---|---|
| **Tên Service** | `CommerceService` (Solution: `Commerce.sln`) |
| **Thư mục gốc** | `d:\CommerceService` |
| **Cổng lắng nghe (Port)** | `8003` (Kestrel HTTP/1.1 & HTTP/2) |
| **Cơ sở dữ liệu** | PostgreSQL 16 (`commerce_db` tại cổng `5435`) |
| **Cache & Khóa phân tán** | Redis Distributed Cache (`StackExchange.Redis` / `IDistributedCache`) |
| **Công nghệ / Nền tảng** | .NET 8 (C# 12), ASP.NET Core Thin Controllers, EF Core 8 (Npgsql) |
| **Kiến trúc ứng dụng** | Clean Architecture 4 tầng chuẩn mực (`Domain`, `Application`, `Infrastructure`, `Api`) + Feature Co-location |
| **Mô hình giao tiếp** | 100% RESTful Web API (RFC 9110 Problem Details). **Bãi bỏ hoàn toàn gRPC, RabbitMQ, MassTransit, Saga State Machine và Outbox tables.** |

---

## 2. TRÁCH NHIỆM & RANH GIỚI NGHIỆP VỤ (BOUNDED CONTEXT)

### 2.1. Việc CommerceService LÀM (In-Scope)
1. **Quản lý Giỏ hàng (Cart Management):**
   - Giỏ hàng cá nhân hóa của Buyer (`CartsController`), hỗ trợ thêm/bớt/sửa số lượng, đổi biến thể phân loại (SKU Variant switch), gộp giỏ vãng lai (Guest Cart merge).
   - Tính toán tạm tính theo từng mặt hàng được tick chọn (cơ chế Shopee Checkbox).
   - Thẩm định tồn kho và biến động giá tức thời trước khi checkout (`/api/v1/carts/me/validate`).
2. **Báo giá & Đặt hàng Đa Người Bán (Multi-Vendor Checkout & Order Lifecycle):**
   - Tiếp nhận xem trước báo giá (`POST /api/v1/checkouts/quote`, TTL 15 phút), gọi sang đơn vị vận chuyển (GHN qua FulfillmentService) tính phí ship độc lập từng Shop.
   - Điều phối giao dịch thanh toán 2 pha (Two-Phase Order Placement) qua `WalletService` hoặc hỗ trợ `COD`.
   - Sinh cấu trúc Đơn hàng cha (`Order` - Master Order) và các Đơn hàng con (`SubOrder`) chia theo từng Người bán (Shop/Seller).
   - Tự động xả dọn các sản phẩm đã thanh toán khỏi giỏ hàng.
3. **Vòng đời Đơn hàng & Quản lý Giao vận (Order & Shipment Tracking):**
   - Tiếp nhận hành động của Seller: Chấp nhận đơn (`Confirmed`), từ chối đơn (`Cancelled` + hoàn tiền).
   - Tiếp nhận webhook giao vận từ GHN (thông qua `FulfillmentService` callback).
   - Cung cấp timeline hành trình vận chuyển thời gian thực (`/api/v1/sub-orders/{subOrderId}/shipments/timeline`).
   - Xử lý đổi địa chỉ giao hàng sau khi đặt hàng (`PATCH .../shipping-address`).
4. **Đơn hàng Đấu giá (Auction Orders):**
   - Tiếp nhận settlement từ `AuctionService` (`POST /api/v1/auction-orders/settlement`, xác thực `X-Internal-Service-Key`, chống trùng lặp `AuctionId + WinnerId`).
   - Cho phép Winner nộp địa chỉ nhận hàng, tính phí vận chuyển GHN chốt `TotalAmount`.
   - Thanh toán số tiền còn lại qua ví Nexus, chuyển Seller tiếp nhận trong 24 giờ.
   - Giải ngân Escrow hoặc hoàn tiền 100% (cọc + tiền hàng) nếu Seller từ chối hoặc giao thất bại.
5. **Mã Giảm Giá & Voucher Đa Tầng (Discount & Promotion Engine):**
   - Phân tầng voucher: System Voucher (Sàn), Shop Voucher (Người bán), Product Voucher (Sản phẩm).
   - Thẩm định điều kiện áp dụng, thuật toán gợi ý combo voucher tối ưu (Voucher Recommender).
   - Quản lý vòng đời voucher (Reserve -> Commit -> Release).
6. **Xử lý Hoàn tiền & Tranh chấp (Refunds & Dispute):**
   - Người mua yêu cầu hoàn tiền đơn con (`POST /api/v1/sub-orders/{subOrderId}/refund`).
   - Người bán / Admin duyệt (`approve`) hoặc từ chối (`reject`) hoàn tiền.
   - Xử lý hoàn tiền tranh chấp từ `UserService` (`POST .../dispute-refund`).
   - Xử lý ca quỹ bảo hiểm sàn (`AdminRefundFundCasesController`) khi đơn đã thanh toán cho người bán.
7. **Tác vụ Nền Tự Động (Hosted Background Workers):**
   - `PaymentTimeoutWorker`: Tự động hủy đơn hàng và giải phóng kho khi quá hạn thanh toán 15 phút.
   - `SettlementRetryWorker`: Thử lại giải ngân Escrow khi gặp lỗi tạm thời với ví.
   - `AuctionFinancialRetryWorker`: Thử lại các giao dịch tài chính (hoàn tiền, trả tiền) của đơn đấu giá.
   - `AuctionCallbackRetryWorker`: Thử lại đồng bộ trạng thái đơn đấu giá sang AuctionService.
   - `NotificationOutboxWorker`: Đẩy thông báo đơn hàng sang Notification Service.

### 2.2. Việc CommerceService KHÔNG LÀM (Out-of-Scope)
- **Không quản lý tồn kho vật lý:** Trách nhiệm thuộc về `FulfillmentService` và `CatalogService`.
- **Không quản lý số dư ví hay cổng thanh toán trực tiếp:** Trách nhiệm thuộc về `WalletService`.
- **Không trực tiếp liên kết API bên thứ 3 (GHN, VNPay):** Cổng giao vận do `FulfillmentService` thụ lý; Cổng thanh toán do `WalletService` thụ lý.
- **Không quản lý tài khoản người dùng và phân quyền chi tiết:** Trách nhiệm thuộc về `UserService`.

---

## 3. CÁC PHỤ THUỘC BÊN NGOÀI (EXTERNAL SATELLITE SERVICES)

Mọi giao tiếp gọi ra ngoài đều sử dụng **Outbound Typed REST Clients** qua `IHttpClientFactory` kèm Polly Resilience Policy (Retry, Circuit Breaker, Fail-Closed):

| Microservice Vệ Tinh | Cổng mặc định | Interface Client | Trách nhiệm tích hợp |
|---|---|---|---|
| **`WalletService`** | `8008` | `IWalletClient` | Khấu trừ tiền ví (`/checkout/direct`), hoàn tiền (`/refund/direct`), giải ngân Escrow (`/escrow/release`), khóa/xả trợ cấp sàn. |
| **`FulfillmentService`** | `8005` | `IFulfillmentClient` | Tính phí GHN (`/shipping/fee`), khóa giữ kho tạm thời (`/inventory/reserve`), xác nhận trừ kho (`/inventory/commit`), nhả kho bồi hoàn (`/inventory/release`), tạo vận đơn GHN (`/shipments`). |
| **`CatalogService`** | `8002` | `ICatalogClient` | Tra cứu chi tiết SKU & đơn giá niêm yết thời gian thực (`/catalog/skus/{id}`), kiểm tra tính khả dụng, đánh dấu SKU đã phát sinh đơn (`/variants/mark-ordered`). |
| **`UserService`** | `8001` | `IUserClient` | Kiểm tra quyền mua của Buyer (`/permission-check?action=buy`), thưởng điểm uy tín khi xong đơn (`/reputation-adjust`), phạt điểm khi vi phạm (`/penalties`). |
| **`AuctionService`** | `8004` | `IAuctionClient` | Tra cứu chi tiết phiên đấu giá, số tiền trúng cọc, xác minh người thắng. |
| **`NotificationService`** | Configured URL | `NotificationOutboxWorker` | Nhận bản tin outbox thông báo thanh toán thành công gửi đến Buyer và Seller. |

---

## 4. CHECKLIST CÁC MODULE VÀ NGHIỆP VỤ PHÂN TÍCH

- [x] **Module Giỏ hàng (Cart Module):**
  - CRUD mặt hàng giỏ, cập nhật số lượng, đổi biến thể SKU trực tiếp.
  - Phân nhóm giỏ hàng theo Shop (`/grouped`).
  - Checkbox tính tiền chọn lọc, kiểm tra biến động giá và tồn kho trước thanh toán.
  - Gộp giỏ vãng lai khi đăng nhập, dọn dẹp giỏ sau thanh toán.
- [x] **Module Đặt hàng & Báo giá (Checkout & Order Placement Module):**
  - Sinh báo giá xem trước (`/checkouts/quote`), tính ship GHN đa shop, áp voucher đa tầng, tính toán TTL 15m.
  - Đổi địa chỉ và tính lại báo giá (`/recalculate`).
  - Tạo đơn hàng chính thức từ báo giá (chống click đúp Redis Atomic Pop, 2-phase lock kho & thanh toán ví).
  - Bồi hoàn phân tán (Distributed Compensation) khi lưu CSDL thất bại.
- [x] **Module Quản lý Vòng đời Đơn hàng (Order Lifecycle Module):**
  - Tra cứu đơn cha, đơn con, lịch sử đơn theo Buyer và theo Seller.
  - Hủy đơn cha / hủy đơn con kèm lý do hủy.
  - Seller tiếp nhận (`accept`) -> tự động sinh vận đơn GHN; Seller từ chối (`reject`) -> hoàn tiền ví 100%.
  - Theo dõi hành trình GHN (`/shipments/timeline`), retry tạo vận đơn khi lỗi mạng (`/shipments/retry`).
  - Thay đổi địa chỉ nhận hàng sau khi đặt.
  - Buyer xác nhận nhận hàng (`confirm-receipt`) -> giải ngân Escrow sang ví Shop.
  - Thanh toán lại đơn hàng quá hạn chưa thanh toán (`repay`).
- [x] **Module Đơn hàng Đấu giá (Auction Order Module):**
  - Tiếp nhận settlement từ AuctionService (`POST /settlement`).
  - Winner cập nhật địa chỉ giao hàng và phí GHN (`PUT /shipping`).
  - Winner thanh toán ví Nexus (`POST /pay`).
  - Seller tiếp nhận trong 24h hoặc từ chối (`/accept`, `/reject`).
  - Hủy đơn đấu giá, theo dõi hành trình giao hàng GHN, xác nhận nhận hàng và giải ngân Escrow.
- [x] **Module Khuyến mãi & Voucher (Discount Module):**
  - Lấy danh sách voucher khả dụng của Buyer kèm lý do không đủ điều kiện (`/available`).
  - Thẩm định mã voucher (`/validate`).
  - Thuật toán gợi ý combo voucher tối ưu (`/recommend`).
  - CRUD voucher dành cho Seller và Admin (`/api/v1/discounts`).
- [x] **Module Hoàn tiền & Tranh chấp (Refund & Dispute Module):**
  - Buyer tạo yêu cầu hoàn tiền (`/refund`).
  - Seller / Admin duyệt hoặc từ chối (`/approve`, `/reject`).
  - Webhook hoàn tiền tranh chấp tự động từ UserService (`/dispute-refund`).
- [x] **Module Quản trị Sàn (Admin Operations Module):**
  - Quản lý % phí sàn hoa hồng (`/api/v1/admin/platform-fee`).
  - Rút lại án phạt boom hàng đối với Buyer (`/api/v1/admin/buyer-refusal-penalties`).
  - Phê duyệt hoàn tiền từ quỹ bảo hiểm sàn có cơ chế duyệt 2 cấp (`/api/v1/admin/refund-fund/cases`).
  - Tra cứu danh sách đơn hàng cha và đơn hàng con toàn sàn (`/api/v1/admin/orders`, `/api/v1/admin/sub-orders`).
