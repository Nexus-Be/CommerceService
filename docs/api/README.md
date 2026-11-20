# COMMERCE SERVICE — API DOCUMENTATION INDEX

> **Căn cứ quy chuẩn:** Kỹ năng chuyên biệt [service-api-doc](file:///d:/CommerceService/.agents/skills/service-api-doc/SKILL.md) và mẫu tài liệu [SERVICE_API_DOC_TEMPLATE.md](file:///d:/CommerceService/docs/api/SERVICE_API_DOC_TEMPLATE.md).
> **Nguyên tắc cốt lõi:** 100% Inward-Only, Pure DTO (không bọc `ApiResponse<T>`), 100% English sentence-case error messages, PascalCase enum serialization.

---

## Danh mục Tài liệu API Chuyên biệt theo Tính năng (Feature Breakdown)

### 1. Đơn hàng & Thanh toán (Orders & Checkouts)
- [POST /api/v1/orders](file:///d:/CommerceService/docs/api/Orders/POST_ORDERS.md): Đặt đơn hàng chính thức từ báo giá (15m TTL).
- [POST /api/v1/checkouts/quote](file:///d:/CommerceService/docs/api/Orders/POST_CHECKOUTS_QUOTE.md): Tạo phiên báo giá thanh toán xem trước và tính cước GHN.
- [POST /api/v1/sub-orders/{id}/delivery-status](file:///d:/CommerceService/docs/api/Orders/POST_SUB_ORDERS_DELIVERY_STATUS.md): Webhook tiếp nhận trạng thái vận chuyển từ Fulfillment/GHN (`Returning`, `Returned` trừ điểm bom hàng).
- [POST /api/v1/internal/sub-orders/{id}/dispute-refund](file:///d:/CommerceService/docs/api/Orders/POST_INTERNAL_SUB_ORDERS_DISPUTE_REFUND.md): Giải ngân hoàn tiền ví khi giải quyết khiếu nại thành công (Internal S2S).

### 2. Giỏ hàng (Carts)
- [GET /api/v1/carts/me](file:///d:/CommerceService/docs/api/Carts/GET_CARTS_ME.md): Tra cứu giỏ hàng hiện tại của người mua.
- [POST /api/v1/carts/me/items](file:///d:/CommerceService/docs/api/Carts/POST_CARTS_ME_ITEMS.md): Thêm sản phẩm vào giỏ hàng (giới hạn 50 món / 99 cái).

### 3. Đơn Đấu giá (Auction Orders)
- [POST /api/v1/auction-orders/settlement](file:///d:/CommerceService/docs/api/AuctionOrders/POST_AUCTION_ORDERS_SETTLEMENT.md): Tiếp nhận kết quả đấu giá tạo đơn `PendingPayment` (hỗ trợ Winner, Runner-up & Mua ngay với `WinningBidId` nullable).

### 4. Quản lý Hoàn tiền (Refunds)
- [GET /api/v1/refunds](file:///d:/CommerceService/docs/api/Refunds/GET_REFUNDS.md): Tra cứu danh sách yêu cầu hoàn tiền (phân trang database `PagedData<T>`).
- [POST /api/v1/refunds/{id}/approve](file:///d:/CommerceService/docs/api/Refunds/POST_REFUNDS_APPROVE.md): Duyệt yêu cầu hoàn tiền và hoàn tiền ví cho người mua.

### 5. Mã giảm giá & Voucher (Discounts)
- [GET /api/v1/discounts/available](file:///d:/CommerceService/docs/api/Discounts/GET_DISCOUNTS_AVAILABLE.md): Tra cứu voucher khả dụng theo shop và giá trị đơn hàng.

### 6. Quản trị Phí sàn (Admin Platform Fee)
- [GET /api/v1/admin/platform-fee](file:///d:/CommerceService/docs/api/Admin/GET_ADMIN_PLATFORM_FEE.md): Tra cứu cấu hình phần trăm phí hoa hồng sàn (mặc định 5%).

---

> **Lưu ý:** Hợp đồng toàn diện tổng thể cho cả hệ thống tiếp tục được đồng bộ tại [docs/COMMERCE_API_CONTRACT.md](file:///d:/CommerceService/docs/COMMERCE_API_CONTRACT.md).
