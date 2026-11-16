# 02 — HỢP ĐỒNG GIAO TIẾP RESTFUL (COMMERCE SERVICE REST CONTRACTS)

> **Trạng thái triển khai (2026-11-16):** ✅ ĐÃ HOÀN THIỆN & CHUẨN HÓA 100% RESTFUL — Toàn bộ giao tiếp giỏ hàng, đơn hàng thường và đơn hàng đấu giá thông qua RESTful Web API (`CartsController`, `OrdersController`, `AuctionOrdersController`). Bãi bỏ hoàn toàn gRPC, Saga phân tán và RabbitMQ.
> **Nguồn sự thật:** Code trong `Commerce.Api/Controllers/Public/` và `docs/COMMERCE_ARCHITECTURE_AND_FLOW_SPEC.md` (v3.0).
> **Cập nhật lần cuối:** 2026-11-16.

---

## 1. DANH MỤC ENDPOINTS RESTFUL CHÍNH THỨC

### 1.1. Giỏ Hàng (`CartsController` — `/api/v1/carts`)
* `GET /api/v1/carts/me`: Lấy hoặc tự động khởi tạo giỏ hàng của chính người mua đang đăng nhập (`CART-01`).
* `GET /api/v1/carts/me/summary`: Lấy số lượng và tổng tạm tính hiển thị Badge Icon Header (`CART-02`).
* `GET /api/v1/carts/me/grouped`: Phân nhóm giỏ hàng theo từng Người bán/Shop (`CART-03`).
* `POST /api/v1/carts/me/items`: Thêm sản phẩm (SKU) vào giỏ hàng (`CART-04`).
* `PATCH /api/v1/carts/me/items/{cartItemId}`: Cập nhật số lượng của dòng sản phẩm (`CART-05`).
* `PATCH /api/v1/carts/me/items/{cartItemId}/variant`: Đổi phân loại hàng trực tiếp trong giỏ (`CART-06`).
* `DELETE /api/v1/carts/me/items/{cartItemId}`: Xóa một món khỏi giỏ hàng (`CART-07`).
* `POST /api/v1/carts/me/items:bulk-remove`: Xóa hàng loạt món được chọn (`CART-08`).
* `DELETE /api/v1/carts/me/clear`: Dọn sạch toàn bộ giỏ hàng (`CART-09`).
* `POST /api/v1/carts/me/calculate`: Tính tiền tạm tính các món được tick chọn — cơ chế Shopee Checkbox (`CART-10`).
* `POST /api/v1/carts/me/validate`: Kiểm tra tồn kho & biến động giá trước khi thanh toán (`CART-11`).
* `POST /api/v1/carts/me/merge`: Gộp giỏ hàng khách vãng lai khi đăng nhập (`CART-12`).
* `POST /api/v1/carts/me/cleanup-checked-out`: Dọn dẹp các món đã thanh toán thành công sau checkout (`CART-13`).
* `GET /api/v1/carts/active`: Tra cứu giỏ hàng đang hoạt động của người mua (`buyerId`) cho Admin/Staff (`CART-14`).

### 1.2. Đơn Hàng & Thanh Toán (`OrdersController` — `/api/v1/orders`, `/api/v1/checkouts`, `/api/v1/sub-orders`)
* `POST /api/v1/checkouts/quote`: Tạo phiên xem trước báo giá (tính ship GHN độc lập từng Shop, áp voucher, sinh `quoteId` TTL 15 phút) (`R75` / `CHK-01`).
* `POST /api/v1/orders`: Tạo đơn hàng chính thức từ `quoteId` (gồm Order cha và các SubOrder con theo Shop) (`R76` / `CHK-02`).
* `GET /api/v1/orders/{orderId}`: Tra cứu chi tiết đơn hàng cha (`OrderResponse`) (`R77` / `ORD-02`).
* `GET /api/v1/orders`: Danh sách lịch sử đơn hàng của người mua (phân trang chuẩn `PagedData<T>`) (`R78` / `ORD-01`).
* `GET /api/v1/sub-orders`: Người bán tra cứu danh sách đơn hàng con thuộc Shop của mình (phân trang tại DB).
* `GET /api/v1/sub-orders/{subOrderId}`: Tra cứu chi tiết đơn con của Shop (Single `SubOrderDetailResponse` có discount breakdown & địa chỉ JSONB) (`R79`).
* `POST /api/v1/orders/{orderId}/cancel`: Người mua hoặc hỗ trợ hủy đơn hàng khi chưa giao (`R80` / `ORD-06`).
* `POST /api/v1/orders/{orderId}/accept`: Người bán xác nhận nhận đơn con (`SubOrderStatus.Confirmed`) (`R83` / `ORD-04`).
* `POST /api/v1/orders/{orderId}/reject`: Người bán từ chối đơn con (`SubOrderStatus.Cancelled` + `cancellationReason: "SellerRejected"`) (`R84` / `ORD-05`).
* `GET /api/v1/shipments/order/{orderId}`: Tra cứu tiến độ vận chuyển đơn hàng (`R87`).
* `POST /api/v1/orders/{orderId}/complete`: Người mua xác nhận hoàn tất toàn bộ đơn hàng (`OrderStatus.Completed`), tự động kích hoạt thưởng điểm uy tín đối tác.
* `POST /api/v1/sub-orders/{subOrderId}/confirm-receipt`: Người mua xác nhận đã nhận hàng đơn con, kích hoạt giải ngân Escrow sang ví Seller.
* `POST /api/v1/sub-orders/{subOrderId}/refund`: Khởi tạo yêu cầu hoàn tiền cho đơn hàng con (`R99` / `ORD-13`).
* `POST /api/v1/sub-orders/{subOrderId}/delivery-status`: Webhook callback từ Fulfillment Service cập nhật trạng thái giao hàng (yêu cầu header `X-Internal-Service-Key`).
* `POST /api/v1/sub-orders/{subOrderId}/shipments/retry`: Tạo lại vận đơn giao hàng khi gặp sự cố tạm thời với nhà vận chuyển (`R100`).
* `GET /api/v1/sub-orders/{subOrderId}/shipments/timeline`: Theo dõi hành trình giao hàng chi tiết cho đơn con (`R101`).
* `GET /api/v1/orders/buyer/{buyerId}`: Tra cứu danh sách đơn của buyer (dành cho Admin/Staff/tương thích ngược).

### 1.3. Đơn Hàng Đấu Giá (`AuctionOrdersController` — `/api/v1/auction-orders`)
* `POST /api/v1/auction-orders/settlement`: Tiếp nhận settlement từ Auction Service tạo đơn đấu giá (`US1`, Idempotency composite AuctionId + WinnerId, header `X-Internal-Service-Key`).
* `PUT /api/v1/auction-orders/{auctionOrderId}/shipping`: Winner gửi địa chỉ giao hàng và tính phí vận chuyển GHN chốt `TotalAmount` (`US2`).
* `POST /api/v1/auction-orders/{auctionOrderId}/pay`: Winner thanh toán đơn đấu giá qua ví Nexus (`US3`).
* `POST /api/v1/auction-orders/{auctionOrderId}/accept`: Seller tiếp nhận đơn hàng đấu giá trong hạn 24 giờ (`US4`).
* `POST /api/v1/auction-orders/{auctionOrderId}/reject`: Seller từ chối đơn đấu giá và kích hoạt hoàn tiền ví 100% (`US5`).
* `POST /api/v1/auction-orders/{auctionOrderId}/confirm-receipt`: Buyer xác nhận đã nhận hàng đấu giá và giải ngân Escrow cho Seller (`US8`).
* `GET /api/v1/auction-orders/{auctionOrderId}`: Tra cứu chi tiết đơn hàng đấu giá (`US9`).
* `GET /api/v1/auction-orders/seller`: Người bán tra cứu danh sách đơn hàng đấu giá của Shop mình (`US9`, có phân trang).

---

## 2. HỢP ĐỒNG GỌI RA NGOÀI (OUTBOUND TYPED REST CLIENTS)

CommerceService sử dụng `HttpClientFactory` kèm cơ chế chịu lỗi Fail-Closed:
* **`IWalletClient`** (`WalletService` — Port 8008):
  - `POST /api/v1/wallets/checkout/direct`: Thanh toán trực tiếp tiền hàng từ ví Buyer (`R181`).
  - `POST /api/v1/wallets/refund/direct`: Hoàn tiền trực tiếp về ví Buyer khi đơn bị hủy / từ chối.
  - `POST /api/v1/wallets/escrow/release`: Giải ngân tiền Escrow sang ví Seller khi giao hàng thành công.
* **`IFulfillmentClient`** (`FulfillmentService` — Port 8005):
  - `POST /api/v1/fulfillment/shipping/fee`: Tính phí vận chuyển đa shop qua GHN.
  - `POST /api/v1/fulfillment/inventory/reserve`: Giữ tồn kho vật lý 15 phút.
  - `POST /api/v1/fulfillment/inventory/release`: Xả tồn kho đã giữ (Compensating action).
  - `POST /api/v1/fulfillment/shipments`: Tạo vận đơn giao hàng sang đơn vị vận chuyển.
* **`ICatalogClient`** (`CatalogService` — Port 8002):
  - `GET /api/v1/catalog/skus/{skuId}`: Thẩm định trạng thái SKU khả dụng và lấy giá niêm yết thời gian thực (chống Price Drift).
* **`IUserClient`** (`UserService` — Port 8001):
  - `GET /api/v1/internal/users/{buyerId}/permission-check?action=buy`: Kiểm tra quyền mua hàng.
  - `POST /api/v1/internal/users/reputation-adjust`: Thưởng điểm uy tín Seller khi hoàn thành đơn.
  - `POST /api/v1/internal/users/penalties`: Phạt điểm uy tín khi Seller hủy đơn vi phạm.
* **`IAuctionClient`** (`AuctionService` — Port 8004):
  - Tra cứu thông tin phiên đấu giá và đối soát số tiền thắng cuộc.
