# COMMERCE SERVICE — INTERNAL BACKEND TRACEABILITY MATRIX

> **Tài liệu nội bộ:** Backend Architecture & Quality Assurance Traceability  
> **Phạm vi:** 100% Nội bộ `CommerceService` (Không gửi cho Frontend)  
> **Cập nhật lần cuối:** 2026-11-27  

---

## 1. MA TRẬN TRUY VẾT ENDPOINT & CODE IMPLEMENTATION

Toàn bộ **57 endpoints** trong hệ thống CommerceService được ánh xạ chi tiết theo file và dòng mã nguồn:

### 1.1. Phân Hệ Giỏ Hàng (`CartsController.cs`)

| # | HTTP Method & Route | File & Dòng | Service & Method | Trách nhiệm & Logic chính |
|---|---|---|---|---|
| 1 | `GET /api/v1/carts/me` | `Commerce.Api/Controllers/Public/CartsController.cs:29` | `CartService.GetOrCreateActiveCartAsync` | Lấy hoặc khởi tạo giỏ hàng `Active` của Buyer |
| 2 | `GET /api/v1/carts/me/summary` | `Commerce.Api/Controllers/Public/CartsController.cs:42` | `CartService.GetCartSummaryAsync` | Lấy badge count và subtotal cho Header |
| 3 | `GET /api/v1/carts/me/grouped` | `Commerce.Api/Controllers/Public/CartsController.cs:55` | `CartService.GetGroupedCartAsync` | Phân nhóm giỏ hàng theo từng Seller/Shop |
| 4 | `POST /api/v1/carts/me/items` | `Commerce.Api/Controllers/Public/CartsController.cs:72` | `CartService.AddItemAsync` | Thêm SKU vào giỏ, kiểm tra tồn kho & giá |
| 5 | `PATCH /api/v1/carts/me/items/{cartItemId}` | `Commerce.Api/Controllers/Public/CartsController.cs:85` | `CartService.UpdateItemQuantityAsync` | Cập nhật số lượng mặt hàng (1..99) |
| 6 | `PATCH /api/v1/carts/me/items/{cartItemId}/variant` | `Commerce.Api/Controllers/Public/CartsController.cs:98` | `CartService.ChangeItemVariantAsync` | Đổi phân loại SKU trực tiếp trong giỏ |
| 7 | `DELETE /api/v1/carts/me/items/{cartItemId}` | `Commerce.Api/Controllers/Public/CartsController.cs:111` | `CartService.RemoveItemAsync` | Xóa 1 dòng hàng khỏi giỏ |
| 8 | `POST /api/v1/carts/me/items:bulk-remove` | `Commerce.Api/Controllers/Public/CartsController.cs:124` | `CartService.BulkRemoveOrClearAsync` | Xóa hàng loạt món hàng được chọn |
| 9 | `DELETE /api/v1/carts/me/clear` | `Commerce.Api/Controllers/Public/CartsController.cs:137` | `CartService.BulkRemoveOrClearAsync` | Dọn sạch toàn bộ giỏ hàng |
| 10 | `POST /api/v1/carts/me/calculate` | `Commerce.Api/Controllers/Public/CartsController.cs:154` | `CartService.CalculateSelectedItemsAsync` | Checkbox tính tiền tạm tính (Shopee selection) |
| 11 | `POST /api/v1/carts/me/validate` | `Commerce.Api/Controllers/Public/CartsController.cs:167` | `CartService.ValidateCartAsync` | Thẩm định giá & kho trước checkout |
| 12 | `POST /api/v1/carts/me/merge` | `Commerce.Api/Controllers/Public/CartsController.cs:180` | `CartService.MergeGuestCartAsync` | Gộp giỏ hàng khách vãng lai khi đăng nhập |
| 13 | `POST /api/v1/carts/me/cleanup-checked-out` | `Commerce.Api/Controllers/Public/CartsController.cs:197` | `CartService.CleanupCheckedOutItemsAsync` | Xóa các món đã mua sau checkout |
| 14 | `GET /api/v1/carts/active` | `Commerce.Api/Controllers/Public/CartsController.cs:210` | `CartService.GetActiveCartByBuyerIdAsync` | Tra cứu giỏ hàng theo `buyerId` cho Staff/Admin |

---

### 1.2. Phân Hệ Báo Giá & Đơn Hàng (`OrdersController.cs`)

| # | HTTP Method & Route | File & Dòng | Service & Method | Trách nhiệm & Logic chính |
|---|---|---|---|---|
| 15 | `POST /api/v1/checkouts/quote` | `Commerce.Api/Controllers/Public/OrdersController.cs:29` | `OrderService.QuoteCheckoutAsync` | Pha 1: Báo giá, tính ship GHN, voucher, TTL 15m |
| 16 | `POST /api/v1/checkouts/quote/{quoteId}/recalculate` | `Commerce.Api/Controllers/Public/OrdersController.cs:42` | `OrderService.RecalculateQuoteAsync` | Tính lại ship GHN khi đổi địa chỉ ở Checkout |
| 17 | `POST /api/v1/orders` | `Commerce.Api/Controllers/Public/OrdersController.cs:115` | `OrderService.CreateOrderAsync` | Pha 2: Tạo đơn, Redis pop, giữ kho, trừ tiền ví |
| 18 | `GET /api/v1/orders/{orderId}` | `Commerce.Api/Controllers/Public/OrdersController.cs:128` | `OrderService.GetOrderDetailAsync` | Chi tiết đơn hàng cha kèm danh sách sub-orders |
| 19 | `GET /api/v1/orders` | `Commerce.Api/Controllers/Public/OrdersController.cs:142` | `OrderService.ListOrdersAsync` | Danh sách lịch sử đơn hàng của người mua |
| 20 | `GET /api/v1/sub-orders` | `Commerce.Api/Controllers/Public/OrdersController.cs:159` | `OrderService.ListSellerSubOrdersAsync` | Danh sách đơn con thuộc Shop của Seller |
| 21 | `GET /api/v1/sub-orders/{subOrderId}` | `Commerce.Api/Controllers/Public/OrdersController.cs:176` | `OrderService.GetSubOrderDetailAsync` | Chi tiết đơn con kèm breakdown tài chính |
| 22 | `POST /api/v1/orders/{orderId}/cancel` | `Commerce.Api/Controllers/Public/OrdersController.cs:190` | `OrderService.CancelOrderAsync` | Hủy toàn bộ đơn hàng cha khi chưa giao |
| 23 | `POST /api/v1/orders/{orderId}/accept` | `Commerce.Api/Controllers/Public/OrdersController.cs:208` | `OrderService.SellerAcceptSubOrderAsync` | Seller duyệt đơn con -> tạo vận đơn GHN |
| 24 | `POST /api/v1/orders/{orderId}/reject` | `Commerce.Api/Controllers/Public/OrdersController.cs:224` | `OrderService.SellerRejectSubOrderAsync` | Seller từ chối đơn con -> hoàn tiền ví 100% |
| 25 | `GET /api/v1/orders/{orderId}/shipments` | `Commerce.Api/Controllers/Public/OrdersController.cs:246` | `OrderService.GetShipmentTrackingAsync` | Tra cứu thông tin vận đơn GHN của đơn cha |
| 26 | `POST /api/v1/orders/{orderId}/complete` | `Commerce.Api/Controllers/Public/OrdersController.cs:261` | `OrderService.CompleteOrderAsync` | Hoàn tất toàn bộ đơn hàng cha |
| 27 | `POST /api/v1/sub-orders/{subOrderId}/confirm-receipt` | `Commerce.Api/Controllers/Public/OrdersController.cs:276` | `OrderService.BuyerConfirmSubOrderReceiptAsync` | Người mua xác nhận nhận hàng -> giải ngân Escrow |
| 28 | `POST /api/v1/sub-orders/{subOrderId}/cancel` | `Commerce.Api/Controllers/Public/OrdersController.cs:291` | `OrderService.CancelSubOrderAsync` | Hủy riêng 1 đơn con trước khi giao |
| 29 | `POST /api/v1/sub-orders/{subOrderId}/refund` | `Commerce.Api/Controllers/Public/OrdersController.cs:308` | `OrderService.RefundSubOrderAsync` | Khởi tạo yêu cầu hoàn tiền cho đơn con |
| 30 | `POST /api/v1/sub-orders/{subOrderId}/shipments/retry` | `Commerce.Api/Controllers/Public/OrdersController.cs:401` | `OrderService.RetryCreateShipmentAsync` | Thử lại tạo vận đơn GHN khi gặp lỗi mạng |
| 31 | `GET /api/v1/sub-orders/{subOrderId}/shipments/timeline` | `Commerce.Api/Controllers/Public/OrdersController.cs:416` | `OrderService.GetShipmentTimelineAsync` | Theo dõi timeline chi tiết hành trình GHN |
| 32 | `POST /api/v1/orders/{orderId}/pay` | `Commerce.Api/Controllers/Public/OrdersController.cs:432` | `OrderService.RepayOrderAsync` | Thanh toán lại đơn hàng `PendingPayment` |
| 33 | `GET /api/v1/orders/{orderId}/refunds` | `Commerce.Api/Controllers/Public/OrdersController.cs:447` | `OrderService.GetOrderRefundsAsync` | Tra cứu lịch sử hoàn tiền của đơn hàng |
| 34 | `GET /api/v1/sub-orders/{subOrderId}/financial-breakdown` | `Commerce.Api/Controllers/Public/OrdersController.cs:462` | `OrderService.GetSubOrderFinancialBreakdownAsync` | Bảng kê phân rã tài chính cho Seller |
| 35 | `GET /api/v1/orders/buyer/{buyerId}` | `Commerce.Api/Controllers/Public/OrdersController.cs:477` | `OrderService.ListByBuyerIdAsync` | Tra cứu đơn hàng theo buyerId (tương thích) |
| 36 | `PATCH /api/v1/sub-orders/{subOrderId}/shipping-address` | `Commerce.Api/Controllers/Public/OrdersController.cs:497` | `OrderService.ChangeSubOrderShippingAddressAsync` | Đổi địa chỉ nhận hàng của đơn con |
| 37 | `PATCH /api/v1/orders/{orderId}/shipping-address` | `Commerce.Api/Controllers/Public/OrdersController.cs:515` | `OrderService.ChangeOrderShippingAddressAsync` | Đổi địa chỉ nhận hàng của toàn bộ đơn cha |
| 38 | `GET /api/v1/admin/orders` | `Commerce.Api/Controllers/Public/OrdersController.cs:533` | `OrderService.ListAdminOrdersAsync` | Tra cứu đơn hàng cha toàn sàn (Staff/Admin) |
| 39 | `GET /api/v1/admin/sub-orders` | `Commerce.Api/Controllers/Public/OrdersController.cs:544` | `OrderService.ListAdminSubOrdersAsync` | Tra cứu đơn hàng con toàn sàn (Staff/Admin) |
| * | `POST /api/v1/sub-orders/{subOrderId}/delivery-status` | `Commerce.Api/Controllers/Public/OrdersController.cs:354` | `OrderService.UpdateSubOrderDeliveryStatusAsync` | Webhook callback cập nhật tiến độ giao từ GHN |
| * | `GET /api/v1/internal/orders/dispute-eligibility` | `Commerce.Api/Controllers/Public/OrdersController.cs:58` | `OrderService.GetDisputeEligibilityAsync` | Internal S2S kiểm tra khiếu nại cho UserService |
| * | `POST /api/v1/internal/sub-orders/{subOrderId}/dispute-refund` | `Commerce.Api/Controllers/Public/OrdersController.cs:96` | `OrderService.DisputeRefundSubOrderAsync` | Internal S2S hoàn tiền tranh chấp cho UserService |
| * | `GET /api/v1/internal/review-eligibility` | `Commerce.Api/Controllers/Public/OrdersController.cs:327` | `OrderService.GetReviewEligibilityAsync` | Internal S2S thẩm định điều kiện đánh giá |
| * | `POST /api/v1/sub-orders/{subOrderId}/dispute-refund` | `Commerce.Api/Controllers/Public/OrdersController.cs:382` | `OrderService.RefundSubOrderForDisputeAsync` | Internal S2S hoàn tiền tranh chấp |

---

### 1.3. Phân Hệ Đơn Hàng Đấu Giá (`AuctionOrdersController.cs`)

| # | HTTP Method & Route | File & Dòng | Service & Method | Trách nhiệm & Logic chính |
|---|---|---|---|---|
| 40 | `PUT /api/v1/auction-orders/{id}/shipping` | `Commerce.Api/Controllers/Public/AuctionOrdersController.cs:49` | `AuctionOrderService.SubmitShippingAsync` | Winner nộp địa chỉ & tính cước GHN chốt giá |
| 41 | `POST /api/v1/auction-orders/{id}/pay` | `Commerce.Api/Controllers/Public/AuctionOrdersController.cs:66` | `AuctionOrderService.PayAuctionOrderAsync` | Winner thanh toán ví trừ tiền cọc |
| 42 | `POST /api/v1/auction-orders/{id}/accept` | `Commerce.Api/Controllers/Public/AuctionOrdersController.cs:83` | `AuctionOrderService.SellerAcceptAsync` | Seller duyệt đơn trong 24h -> tạo vận đơn GHN |
| 43 | `POST /api/v1/auction-orders/{id}/reject` | `Commerce.Api/Controllers/Public/AuctionOrdersController.cs:100` | `AuctionOrderService.SellerRejectAsync` | Seller từ chối -> hoàn tiền 100% (cọc + tiền hàng) |
| 44 | `POST /api/v1/auction-orders/{id}/confirm-receipt` | `Commerce.Api/Controllers/Public/AuctionOrdersController.cs:117` | `AuctionOrderService.ConfirmReceiptAsync` | Buyer xác nhận nhận hàng -> giải ngân Escrow |
| 45 | `GET /api/v1/auction-orders/me` | `Commerce.Api/Controllers/Public/AuctionOrdersController.cs:133` | `AuctionOrderService.ListBuyerOrdersAsync` | Tra cứu danh sách đơn đấu giá của Buyer |
| 46 | `GET /api/v1/auction-orders/{id}` | `Commerce.Api/Controllers/Public/AuctionOrdersController.cs:154` | `AuctionOrderService.GetOrderDetailAsync` | Tra cứu chi tiết đơn hàng đấu giá |
| 47 | `GET /api/v1/auction-orders/seller` | `Commerce.Api/Controllers/Public/AuctionOrdersController.cs:195` | `AuctionOrderService.ListSellerOrdersAsync` | Tra cứu danh sách đơn đấu giá của Seller |
| 48 | `PATCH /api/v1/auction-orders/{id}/shipping-address` | `Commerce.Api/Controllers/Public/AuctionOrdersController.cs:215` | `AuctionOrderService.ChangeAuctionShippingAddressAsync` | Đổi địa chỉ nhận hàng đơn đấu giá |
| 49 | `GET /api/v1/auction-orders/{id}/shipments/timeline` | `Commerce.Api/Controllers/Public/AuctionOrdersController.cs:233` | `AuctionOrderService.GetAuctionShipmentTimelineAsync` | Xem timeline giao hàng GHN của đơn đấu giá |
| 50 | `POST /api/v1/auction-orders/{id}/cancel` | `Commerce.Api/Controllers/Public/AuctionOrdersController.cs:250` | `AuctionOrderService.CancelAuctionOrderAsync` | Hủy đơn đấu giá trước khi Seller chấp nhận |
| 51 | `POST /api/v1/auction-orders/{id}/shipments/retry` | `Commerce.Api/Controllers/Public/AuctionOrdersController.cs:268` | `AuctionOrderService.RetryAuctionShipmentAsync` | Thử lại tạo vận đơn GHN cho đơn đấu giá |
| * | `POST /api/v1/auction-orders/settlement` | `Commerce.Api/Controllers/Public/AuctionOrdersController.cs:27` | `AuctionOrderService.ReceiveSettlementAsync` | Webhook settlement từ AuctionService |
| * | `GET /api/v1/internal/auction-orders/{id}/notification-snapshot` | `Commerce.Api/Controllers/Public/AuctionOrdersController.cs:175` | `AuctionOrderService.GetNotificationSnapshotAsync` | Internal S2S snapshot cho AuctionService |

---

### 1.4. Phân Hệ Mã Giảm Giá & Voucher (`DiscountsController.cs`)

| # | HTTP Method & Route | File & Dòng | Service & Method | Trách nhiệm & Logic chính |
|---|---|---|---|---|
| 52 | `GET /api/v1/discounts/available` | `Commerce.Api/Controllers/Public/DiscountsController.cs:28` | `DiscountService.GetAvailableVouchersAsync` | Lấy voucher khả dụng kèm lý do không đủ điều kiện |
| 53 | `POST /api/v1/discounts/validate` | `Commerce.Api/Controllers/Public/DiscountsController.cs:59` | `DiscountService.ValidateVoucherAsync` | Thẩm định 1 mã voucher đối với giỏ hàng |
| 54 | `POST /api/v1/discounts/recommend` | `Commerce.Api/Controllers/Public/DiscountsController.cs:75` | `DiscountService.RecommendVouchersAsync` | Thuật toán gợi ý combo voucher tối ưu nhất |
| 55 | `POST /api/v1/discounts` | `Commerce.Api/Controllers/Public/DiscountsController.cs:91` | `DiscountService.CreateDiscountAsync` | Tạo voucher khuyến mãi mới (Seller/Admin) |
| 56 | `GET /api/v1/discounts` | `Commerce.Api/Controllers/Public/DiscountsController.cs:109` | `DiscountService.ListDiscountsAsync` | Danh sách voucher có lọc trạng thái/phạm vi |
| 57 | `GET /api/v1/discounts/{discountId}` | `Commerce.Api/Controllers/Public/DiscountsController.cs:133` | `DiscountService.GetDiscountByIdAsync` | Xem chi tiết voucher khuyến mãi |
| 58 | `PUT /api/v1/discounts/{discountId}` | `Commerce.Api/Controllers/Public/DiscountsController.cs:151` | `DiscountService.UpdateDiscountAsync` | Cập nhật thông tin voucher |
| 59 | `PATCH /api/v1/discounts/{discountId}/status` | `Commerce.Api/Controllers/Public/DiscountsController.cs:170` | `DiscountService.UpdateDiscountStatusAsync` | Bật / Tắt trạng thái hoạt động của voucher |
| 60 | `DELETE /api/v1/discounts/{discountId}` | `Commerce.Api/Controllers/Public/DiscountsController.cs:189` | `DiscountService.DeleteDiscountAsync` | Xóa mềm voucher khuyến mãi |

---

### 1.5. Phân Hệ Hoàn Tiền (`RefundsController.cs`)

| # | HTTP Method & Route | File & Dòng | Service & Method | Trách nhiệm & Logic chính |
|---|---|---|---|---|
| 61 | `GET /api/v1/refunds` | `Commerce.Api/Controllers/Public/RefundsController.cs:25` | `OrderService.ListRefundsAsync` | Danh sách yêu cầu hoàn tiền (phân trang DB) |
| 62 | `GET /api/v1/refunds/{id}` | `Commerce.Api/Controllers/Public/RefundsController.cs:44` | `OrderService.GetRefundDetailAsync` | Xem chi tiết yêu cầu hoàn tiền |
| 63 | `POST /api/v1/refunds/{id}/approve` | `Commerce.Api/Controllers/Public/RefundsController.cs:60` | `OrderService.ApproveRefundAsync` | Duyệt hoàn tiền -> hoàn tiền ví trực tiếp |
| 64 | `POST /api/v1/refunds/{id}/reject` | `Commerce.Api/Controllers/Public/RefundsController.cs:78` | `OrderService.RejectRefundAsync` | Từ chối yêu cầu hoàn tiền kèm lý do |

---

### 1.6. Phân Hệ Quản Trị Sàn (`Admin` Controllers)

| # | HTTP Method & Route | File & Dòng | Service & Method | Trách nhiệm & Logic chính |
|---|---|---|---|---|
| 65 | `POST /api/v1/admin/buyer-refusal-penalties/{subOrderId}/revoke` | `Commerce.Api/Controllers/Admin/AdminBuyerRefusalPenaltiesController.cs:41` | `BuyerRefusalPenaltyService.RequestRevocationAsync` | Thu hồi án phạt boom hàng của người mua (Admin) |
| 66 | `GET /api/v1/admin/platform-fee` | `Commerce.Api/Controllers/Admin/AdminPlatformFeeController.cs:51` | `AdminPlatformFeeController.GetPlatformFee` | Xem % tỷ lệ phí hoa hồng sàn |
| 67 | `PUT /api/v1/admin/platform-fee` | `Commerce.Api/Controllers/Admin/AdminPlatformFeeController.cs:101` | `AdminPlatformFeeController.UpdatePlatformFee` | Cập nhật % tỷ lệ phí hoa hồng sàn (0%..100%) |
| 68 | `GET /api/v1/admin/refund-fund/cases` | `Commerce.Api/Controllers/Admin/AdminRefundFundCasesController.cs:51` | `RefundFundService.ListAsync` | Danh sách ca bồi hoàn quỹ bảo hiểm sàn |
| 69 | `POST /api/v1/admin/refund-fund/cases` | `Commerce.Api/Controllers/Admin/AdminRefundFundCasesController.cs:56` | `RefundFundService.RequestAsync` | Đề xuất bồi hoàn quỹ (vượt ngưỡng chờ duyệt 2) |
| 70 | `POST /api/v1/admin/refund-fund/cases/{subOrderId}/approve` | `Commerce.Api/Controllers/Admin/AdminRefundFundCasesController.cs:64` | `RefundFundService.ApproveSecondAsync` | Admin thứ 2 phê duyệt ca bồi hoàn quỹ |
| 71 | `POST /api/v1/admin/refund-fund/cases/{subOrderId}/retry` | `Commerce.Api/Controllers/Admin/AdminRefundFundCasesController.cs:72` | `RefundFundService.RetryAsync` | Thử lại giải ngân ví quỹ bồi hoàn |
| 72 | `POST /api/v1/admin/refund-fund/cases/{subOrderId}/resolve` | `Commerce.Api/Controllers/Admin/AdminRefundFundCasesController.cs:80` | `RefundFundService.ResolveAsync` | Đóng ca bồi hoàn đã xử lý thủ công ngoài hệ thống |

---

### 1.7. Phân Hệ S2S Nội Bộ (`Internal` Controllers)

| # | HTTP Method & Route | File & Dòng | Service & Method | Trách nhiệm & Logic chính |
|---|---|---|---|---|
| * | `GET /api/v1/internal/audit-logs/unsynced` | `Commerce.Api/Controllers/Internal/InternalAuditLogsController.cs:27` | `AuditSyncSourceService.GetUnsyncedAsync` | Đồng bộ audit log chưa sync sang central log |
| * | `POST /api/v1/internal/audit-logs/mark-synced` | `Commerce.Api/Controllers/Internal/InternalAuditLogsController.cs:35` | `AuditSyncSourceService.MarkSyncedAsync` | Đánh dấu audit log đã đồng bộ |
| * | `GET /api/v1/internal/reconciliation/orders` | `Commerce.Api/Controllers/Internal/InternalReconciliationController.cs:40` | `AppDbContext.Orders` | Phục vụ đối soát kế toán cho WalletService |
| * | `GET /api/v1/internal/reconciliation/legacy-settlement-classes` | `Commerce.Api/Controllers/Internal/InternalReconciliationController.cs:80` | `AppDbContext.SubOrders` | Phân loại đơn legacy settlement V1 |
