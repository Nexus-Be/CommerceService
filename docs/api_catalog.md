# Commerce Service REST API Catalog

> **Nguồn sự thật:** Khớp 100% với Controllers thực tế (`Commerce.Api/Controllers/Public/`), Swagger UI và tài liệu kiến trúc `docs/COMMERCE_ARCHITECTURE_AND_FLOW_SPEC.md` (v3.0).

---

## 1. BẢNG TRA CỨU ENDPOINTS CHÍNH THỨC (PUBLIC / INCOMING REST API)

| Mã | Nhóm | Method | Route | Tên Chức Năng / Nghiệp Vụ | Quyền Hạn (RBAC) | Idempotency |
| :--- | :--- | :---: | :--- | :--- | :--- | :---: |
| **CART-01** | Cart | `GET` | `/api/v1/carts/me` | Lấy hoặc tự động khởi tạo giỏ hàng người mua | Buyer | - |
| **CART-02** | Cart | `GET` | `/api/v1/carts/me/summary` | Lấy số lượng badge và tổng tiền tạm tính Header | Buyer | - |
| **CART-03** | Cart | `GET` | `/api/v1/carts/me/grouped` | Phân nhóm các món trong giỏ hàng theo Shop | Buyer | - |
| **CART-04** | Cart | `POST`| `/api/v1/carts/me/items` | Thêm SKU sản phẩm vào giỏ hàng | Buyer | - |
| **CART-05** | Cart | `PATCH`| `/api/v1/carts/me/items/{cartItemId}` | Cập nhật số lượng của dòng sản phẩm | Buyer | - |
| **CART-06** | Cart | `PATCH`| `/api/v1/carts/me/items/{cartItemId}/variant` | Đổi phân loại hàng trực tiếp trong giỏ | Buyer | - |
| **CART-07** | Cart | `DELETE`| `/api/v1/carts/me/items/{cartItemId}` | Xóa một món lẻ khỏi giỏ hàng | Buyer | - |
| **CART-08** | Cart | `POST`| `/api/v1/carts/me/items:bulk-remove` | Xóa hàng loạt các món được chọn | Buyer | - |
| **CART-09** | Cart | `DELETE`| `/api/v1/carts/me/clear` | Dọn sạch toàn bộ giỏ hàng | Buyer | - |
| **CART-10** | Cart | `POST`| `/api/v1/carts/me/calculate` | Tính tiền tạm tính theo checkbox chọn món (Shopee model) | Buyer | - |
| **CART-11** | Cart | `POST`| `/api/v1/carts/me/validate` | Kiểm tra tồn kho & biến động giá trước Checkout | Buyer | - |
| **CART-12** | Cart | `POST`| `/api/v1/carts/me/merge` | Gộp giỏ hàng khách vãng lai khi đăng nhập | Buyer | - |
| **CART-13** | Cart | `POST`| `/api/v1/carts/me/cleanup-checked-out` | Dọn dẹp món đã thanh toán sau khi đặt hàng thành công | Buyer, System | - |
| **CART-14** | Cart | `GET` | `/api/v1/carts/active` | Tra cứu giỏ hàng theo buyerId (Quản trị tra cứu) | Admin, Staff | - |
| **CHK-01** | Order | `POST`| `/api/v1/checkouts/quote` | Tạo phiên báo giá thanh toán xem trước (tính ship GHN, 15m TTL) (`R75`) | Buyer | - |
| **CHK-02** | Order | `POST`| `/api/v1/orders` | Tạo đơn hàng chính thức từ `quoteId` (`R76`) | Buyer | Required |
| **ORD-01** | Order | `GET` | `/api/v1/orders` | Danh sách lịch sử đơn hàng của người mua (`R78`) | Buyer (own) | - |
| **ORD-02** | Order | `GET` | `/api/v1/orders/{orderId}` | Chi tiết đơn hàng cha (Master Order) (`R77`) | Buyer (own), Admin, Support | - |
| **ORD-02B**| Order | `GET` | `/api/v1/sub-orders` | Người bán tra cứu danh sách đơn hàng con của Shop | Seller (own) | - |
| **ORD-03** | Order | `GET` | `/api/v1/sub-orders/{subOrderId}` | Chi tiết đơn con của Shop (Single DTO, JSONB address) (`R79`) | Buyer, Seller (own), Admin | - |
| **ORD-04** | Order | `POST`| `/api/v1/orders/{orderId}/accept` | Người bán tiếp nhận đơn con (`SubOrderStatus.Confirmed`) (`R83`) | Seller (own) | - |
| **ORD-05** | Order | `POST`| `/api/v1/orders/{orderId}/reject` | Người bán từ chối đơn con (`SubOrderStatus.Cancelled`) (`R84`) | Seller (own) | - |
| **ORD-06** | Order | `POST`| `/api/v1/orders/{orderId}/cancel` | Hủy đơn hàng khi chưa vận chuyển (`R80`) | Buyer (own), Admin | Required |
| **ORD-07** | Order | `POST`| `/api/v1/orders/{orderId}/complete` | Người mua xác nhận hoàn tất đơn hàng cha, thưởng điểm uy tín | Buyer (own) | - |
| **ORD-08** | Order | `GET` | `/api/v1/orders/{orderId}/shipments` | Tra cứu tiến độ vận chuyển đơn hàng (`R87`) (alias: `/api/v1/shipments/order/{orderId}`) | Buyer, Seller (own) | - |
| **ORD-08B**| Order | `GET` | `/api/v1/sub-orders/{subOrderId}/shipments/timeline` | Theo dõi dòng thời gian hành trình giao hàng GHN (`R101`) | Buyer, Seller (own) | - |
| **ORD-09** | Order | `POST`| `/api/v1/sub-orders/{subOrderId}/confirm-receipt` | Buyer xác nhận đã nhận hàng đơn con, kích hoạt giải ngân Escrow | Buyer (own) | - |
| **ORD-10** | Order | `POST`| `/api/v1/sub-orders/{subOrderId}/delivery-status` | Webhook callback cập nhật tiến độ giao vận từ Fulfillment | System / Webhook | Header Key |
| **ORD-11** | Order | `POST`| `/api/v1/sub-orders/{subOrderId}/shipments/retry` | Tạo lại vận đơn giao hàng khi gặp sự cố mạng tạm thời (`R100`) | Seller (own) | - |
| **ORD-12** | Order | `GET` | `/api/v1/orders/buyer/{buyerId}` | Tra cứu danh sách đơn của buyer (tương thích ngược) | Admin, Staff | - |
| **ORD-13** | Order | `POST`| `/api/v1/sub-orders/{subOrderId}/refund` | Khởi tạo yêu cầu hoàn tiền cho đơn hàng con (`R99`) | Buyer (own), Admin | Required |
| **AO-01** | Auction | `POST`| `/api/v1/auction-orders/settlement` | Tiếp nhận kết quả thắng cuộc từ Auction Service (`US1`) | System (Auction) | Header Key |
| **AO-02** | Auction | `PUT` | `/api/v1/auction-orders/{auctionOrderId}/shipping` | Winner gửi địa chỉ nhận hàng và tính phí vận chuyển GHN (`US2`) | Buyer (winner) | - |
| **AO-03** | Auction | `POST`| `/api/v1/auction-orders/{auctionOrderId}/pay` | Winner thanh toán đơn hàng đấu giá qua ví Nexus (`US3`) | Buyer (winner) | Required |
| **AO-04** | Auction | `POST`| `/api/v1/auction-orders/{auctionOrderId}/accept` | Seller tiếp nhận đơn hàng đấu giá trong 24h (`US4`) | Seller (own) | - |
| **AO-05** | Auction | `POST`| `/api/v1/auction-orders/{auctionOrderId}/reject` | Seller từ chối đơn đấu giá và hoàn tiền ví 100% (`US5`) | Seller (own) | - |
| **AO-06** | Auction | `POST`| `/api/v1/auction-orders/{auctionOrderId}/confirm-receipt` | Buyer xác nhận đã nhận hàng đấu giá, giải ngân Escrow (`US8`) | Buyer (winner) | - |
| **AO-07** | Auction | `GET` | `/api/v1/auction-orders/{auctionOrderId}` | Tra cứu chi tiết đơn hàng đấu giá (`US9`) | Buyer, Seller (own) | - |
| **AO-08** | Auction | `GET` | `/api/v1/auction-orders/seller` | Người bán tra cứu danh sách đơn đấu giá của Shop mình (`US9`) | Seller (own) | - |

---

## 2. HỢP ĐỒNG GỌI RA NGOÀI LIÊN DỊCH VỤ (OUTBOUND TYPED REST CLIENTS)

CommerceService **không sở hữu các tài nguyên ngoại vi**, việc tương tác thực hiện qua Typed HTTP Clients:
1. **`IWalletClient` (Port 8008 - WalletService)**:
   - `POST /api/v1/wallets/checkout/direct`: Thanh toán trừ số dư khả dụng ví người mua.
   - `POST /api/v1/wallets/refund/direct`: Hoàn tiền ví khi đơn hàng bị từ chối/hủy.
   - `POST /api/v1/wallets/escrow/release`: Giải ngân số tiền chờ về ví chính của người bán.
2. **`IFulfillmentClient` (Port 8005 - FulfillmentService)**:
   - `POST /api/v1/fulfillment/shipping/fee`: Tính phí vận chuyển độc lập từng kho GHN.
   - `POST /api/v1/fulfillment/inventory/reserve`: Giữ tồn kho vật lý 5 phút.
   - `POST /api/v1/fulfillment/inventory/release`: Xả tồn kho khi giao dịch thất bại (Compensating action).
   - `POST /api/v1/fulfillment/shipments`: Khởi tạo mã vận đơn giao hàng đối tác.
3. **`ICatalogClient` (Port 8002 - CatalogService)**:
   - `GET /api/v1/catalog/skus/{skuId}`: Xác minh trạng thái mua bán và snapshot giá niêm yết thời gian thực.
4. **`IUserClient` (Port 8001 - UserService)**:
   - `GET /api/v1/internal/users/{buyerId}/permission-check?action=buy`: Thẩm định quyền mua sắm.
   - `POST /api/v1/internal/users/reputation-adjust`: Thưởng điểm uy tín Seller sau khi hoàn thành đơn.
   - `POST /api/v1/internal/users/penalties`: Phạt điểm uy tín khi Seller tự ý từ chối đơn hàng hợp lệ.
5. **`IAuctionClient` (Port 8004 - AuctionService)**:
   - Thẩm định và đối soát thông tin phiên đấu giá thắng cuộc.
