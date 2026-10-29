# Commerce Service REST API Catalog

> **Nguồn:** Trích xuất từ `Commerce_Function_API_Backlog.xlsx` (Sheet `API_Catalog`). Chuẩn REST API đối chiếu Gateway.

| ID | Kind | Method | Path | Name | Permission | Idempotency-Key |
| --- | --- | --- | --- | --- | --- | --- |
| CART-01 | Public API | GET | /api/v1/carts/me | Get my cart / Lấy giỏ hàng của tôi | CART.VIEW / Buyer | - |
| CART-02 | Public API | POST | /api/v1/carts/me/items | Add item to cart / Thêm sản phẩm vào giỏ | CART.UPDATE / Buyer | - |
| CART-03 | Public API | PATCH | /api/v1/carts/me/items/{cartItemId} | Update cart item quantity / Cập nhật số lượng | CART.UPDATE / Buyer | - |
| CART-04 | Public API | DELETE | /api/v1/carts/me/items/{cartItemId} | Remove cart item / Xoá một dòng khỏi giỏ | CART.REMOVE_ITEM / Buyer | - |
| CART-05 | Public API | POST | /api/v1/carts/me/items:bulk-remove | Bulk remove / clear cart / Xoá nhiều dòng hoặc xoá hết | CART.REMOVE_ITEM / Buyer | - |
| CART-06 | Public API | POST | /api/v1/carts/me/validate | Validate cart / Kiểm tra giỏ hàng trước checkout | CART.VIEW / Buyer | - |
| CART-08 | Public API | GET | /api/v1/carts/me/summary | Cart summary (badge) / Số lượng giỏ hàng | CART.VIEW / Buyer | - |
| CART-10 | Public API | POST | /api/v1/carts/me/merge | Merge guest cart on login / Gộp giỏ khách (OPTIONAL) | CART.UPDATE / Buyer | - |
| CHK-01 | Public API | POST | /api/v1/checkout/preview | Checkout preview / Xem trước thanh toán | CHECKOUT.START / Buyer | - |
| CHK-02 | Public API | POST | /api/v1/checkout | Create checkout (group + orders) / Tạo checkout và đơn hàng | CHECKOUT.START + ORDER.CREATE / Buyer | Required |
| CHK-03 | Public API | POST | /api/v1/checkout-groups/{checkoutGroupId}/pay | Pay checkout group / Thanh toán nhóm đơn | CHECKOUT.PAY (NEW) / Buyer | Required |
| CHK-04 | Public API | GET | /api/v1/checkout-groups/{checkoutGroupId} | Get checkout group / Xem trạng thái checkout | ORDER.VIEW / Buyer (own) | - |
| CHK-05 | Public API | POST | /api/v1/checkout-groups/{checkoutGroupId}/cancel | Cancel checkout group / Huỷ checkout chưa thanh toán | ORDER.CANCEL / Buyer | - |
| DSC-01 | Public API | GET | /api/v1/vouchers/available?cartItemIds=&sellerId=&page=&size= | List available vouchers / Danh sách voucher khả dụng | CART.VIEW / Buyer | - |
| DSC-05 | Public API | POST | /api/v1/vouchers | Create voucher / Tạo voucher | DISCOUNT.CREATE (NEW) / Admin, Seller | - |
| DSC-06 | Public API | PATCH | /api/v1/vouchers/{voucherId} | Update / activate / delete voucher / Sửa, bật tắt, xoá voucher | DISCOUNT.UPDATE (NEW) / Admin, Seller (own) | - |
| DSC-07 | Public API | GET | /api/v1/vouchers | List vouchers for management / Quản lý danh sách voucher | DISCOUNT.VIEW (NEW) / Admin, Seller (own) | - |
| ORD-01 | Public API | GET | /api/v1/orders | List my orders / Lịch sử đơn hàng của tôi | ORDER.LIST / Buyer (own) | - |
| ORD-02 | Public API | GET | /api/v1/orders/{orderId}?orderType=REGULAR/AUCTION | Get order detail / Chi tiết & theo dõi đơn hàng | ORDER.VIEW / Buyer (own), Seller (own), Admin, Support | - |
| ORD-03 | Public API | GET | /api/v1/seller/orders | Seller order list / Danh sách đơn của seller | ORDER.LIST / Seller (own) | - |
| ORD-04 | Public API | POST | /api/v1/seller/orders/{orderId}/accept?orderType= | Seller accept order / Seller xác nhận đơn | ORDER.CONFIRM (SRS OQ-12) / Seller | - |
| ORD-05 | Public API | POST | /api/v1/seller/orders/{orderId}/reject?orderType= | Seller reject order / Seller từ chối đơn | ORDER.REJECT (SRS OQ-12) / Seller | - |
| ORD-06 | Public API | POST | /api/v1/orders/{orderId}/cancel?orderType= | Cancel order / Huỷ đơn (Buyer, Support, Admin) | ORDER.CANCEL / Buyer (own), Support, Admin | Required |
| ORD-07 | Public API | POST | /api/v1/orders/{orderId}/confirm-received?orderType= | Confirm received / Buyer xác nhận đã nhận hàng | ORDER.CONFIRM_RECEIVED (NEW) / Buyer | - |
| ORD-11 | Public API | GET | /api/v1/admin/orders | Admin list orders / Quản trị xem tất cả đơn | ORDER.LIST / Admin, Support | - |
| ORD-12 | Public API | PATCH | /api/v1/admin/orders/{orderId}/status?orderType= | Admin update order status / Admin chỉnh trạng thái ngoại lệ | ORDER.UPDATE_STATUS (NEW) / Admin | - |
| ORD-13 | Public API | POST | /api/v1/admin/orders/{orderId}/refund?orderType= | Admin/Support refund / Hoàn tiền thủ công | ORDER.REFUND / Admin, Support | Required |
| ORD-16 | Public API | GET | /api/v1/orders/{orderId}/invoice?orderType= | Download invoice / Tải hoá đơn | ORDER.INVOICE.VIEW (NEW) / Buyer (own), Seller (own), Admin, Support | - |
| ORD-23 | Internal API | GET | /internal/v1/orders/{orderId}/rating-eligibility?userId=&productId= | Rating eligibility check / Kiểm tra đủ điều kiện đánh giá | System (service-to-service) | - |
| AO-02 | Public API | PUT | /api/v1/auction-orders/{auctionOrderId}/shipping | Submit shipping info & get quote / Nhập địa chỉ và báo giá ship | AUCTION.VIEW + ORDER.UPDATE (NEW) / Buyer (winner) | - |
| AO-03 | Public API | POST | /api/v1/auction-orders/{auctionOrderId}/pay | Pay auction order / Thanh toán đơn đấu giá | CHECKOUT.PAY (NEW) / Buyer (winner) | Required |
