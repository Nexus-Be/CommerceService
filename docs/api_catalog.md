# Commerce Service REST API Catalog

> **Nguồn:** Trích xuất từ `Commerce_Function_API_Backlog.xlsx` và `docs/COMMERCE_API_RECONCILIATION_REPORT.md`. Chuẩn REST API đối chiếu Gateway.

| ID | Kind | Method | Path | Name | Permission | Idempotency-Key |
| --- | --- | --- | --- | --- | --- | --- |
| CART-01 | Public API | GET | /api/v1/carts/me | Get my cart / Lấy hoặc khởi tạo giỏ hàng | CART.VIEW / Buyer | - |
| CART-02 | Public API | GET | /api/v1/carts/me/summary | Cart summary badge / Huy hiệu số lượng giỏ hàng | CART.VIEW / Buyer | - |
| CART-03 | Public API | GET | /api/v1/carts/me/grouped | Group cart items by seller / Phân nhóm giỏ theo Shop | CART.VIEW / Buyer | - |
| CART-04 | Public API | POST | /api/v1/carts/me/items | Add item to cart / Thêm sản phẩm vào giỏ | CART.UPDATE / Buyer | - |
| CART-05 | Public API | PATCH | /api/v1/carts/me/items/{cartItemId} | Update cart item quantity / Cập nhật số lượng | CART.UPDATE / Buyer | - |
| CART-06 | Public API | PATCH | /api/v1/carts/me/items/{cartItemId}/variant | Change SKU variant / Đổi phân loại hàng trong giỏ | CART.UPDATE / Buyer | - |
| CART-07 | Public API | DELETE | /api/v1/carts/me/items/{cartItemId} | Remove cart item / Xoá một dòng khỏi giỏ | CART.REMOVE_ITEM / Buyer | - |
| CART-08 | Public API | POST | /api/v1/carts/me/items:bulk-remove | Bulk remove items / Xoá nhiều dòng được chọn | CART.REMOVE_ITEM / Buyer | - |
| CART-09 | Public API | DELETE | /api/v1/carts/me/clear | Clear cart / Dọn sạch toàn bộ giỏ hàng | CART.REMOVE_ITEM / Buyer | - |
| CART-10 | Public API | POST | /api/v1/carts/me/calculate | Calculate selected items / Tính tiền các món được chọn | CART.VIEW / Buyer | - |
| CART-11 | Public API | POST | /api/v1/carts/me/validate | Validate cart / Kiểm tra giỏ hàng trước checkout | CART.VIEW / Buyer | - |
| CART-12 | Public API | POST | /api/v1/carts/me/merge | Merge guest cart on login / Gộp giỏ khách vãng lai | CART.UPDATE / Buyer | - |
| CART-13 | Public API | POST | /api/v1/carts/me/cleanup-checked-out | Cleanup checked out items / Dọn dẹp món đã thanh toán | CART.UPDATE / Buyer, System | - |
| CART-14 | Public API | GET | /api/v1/carts/active?buyerId= | Get active cart / Quản trị tra cứu giỏ hàng | CART.VIEW / Admin, Staff | - |
| CHK-01 | Public API | POST | /api/v1/checkouts/quote | Checkout quote / Xem trước báo giá giỏ hàng (R75) | CHECKOUT.START / Buyer | - |
| CHK-02 | Public API | POST | /api/v1/orders | Create order from quote / Tạo đơn hàng từ quoteId (R76) | CHECKOUT.START + ORDER.CREATE / Buyer | Required |
| ORD-01 | Public API | GET | /api/v1/orders | List my orders / Lịch sử đơn hàng của tôi (R78) | ORDER.LIST / Buyer (own) | - |
| ORD-02 | Public API | GET | /api/v1/orders/{orderId} | Get order detail / Chi tiết đơn hàng cha (R77) | ORDER.VIEW / Buyer (own), Admin, Support | - |
| ORD-02B| Public API | GET | /api/v1/sub-orders/{subOrderId} | Get sub-order detail / Chi tiết đơn con của Shop (R79) | ORDER.VIEW / Buyer (own), Seller (own), Admin, Support | - |
| ORD-04 | Public API | POST | /api/v1/orders/{orderId}/accept | Seller accept order / Seller xác nhận đơn con (R83) | ORDER.CONFIRM / Seller | - |
| ORD-05 | Public API | POST | /api/v1/orders/{orderId}/reject | Seller reject order / Seller từ chối đơn con (R84) | ORDER.REJECT / Seller | - |
| ORD-06 | Public API | POST | /api/v1/orders/{orderId}/cancel | Cancel order / Huỷ đơn (Buyer, Support, Admin) (R80) | ORDER.CANCEL / Buyer (own), Support, Admin | Required |
| ORD-08 | Public API | GET | /api/v1/shipments/order/{orderId} | Track shipment / Theo dõi tiến độ vận chuyển (R87) | ORDER.VIEW / Buyer (own), Seller (own) | - |
| ORD-13 | Public API | POST | /api/v1/sub-orders/{subOrderId}/refund | Refund sub-order / Yêu cầu hoàn tiền đơn con (R99) | ORDER.REFUND / Buyer (own), Admin, Support | Required |
| DSC-01 | Public API | GET | /api/v1/vouchers/available?cartItemIds=&sellerId=&page=&size= | List available vouchers / Danh sách voucher khả dụng | CART.VIEW / Buyer | - |
| DSC-05 | Public API | POST | /api/v1/vouchers | Create voucher / Tạo voucher | DISCOUNT.CREATE (NEW) / Admin, Seller | - |
| DSC-06 | Public API | PATCH | /api/v1/vouchers/{voucherId} | Update / activate / delete voucher / Sửa, bật tắt, xoá voucher | DISCOUNT.UPDATE (NEW) / Admin, Seller (own) | - |
| DSC-07 | Public API | GET | /api/v1/vouchers | List vouchers for management / Quản lý danh sách voucher | DISCOUNT.VIEW (NEW) / Admin, Seller (own) | - |
| ORD-07 | Public API | POST | /api/v1/orders/{orderId}/confirm-received | Confirm received / Buyer xác nhận đã nhận hàng | ORDER.CONFIRM_RECEIVED / Buyer | - |
| ORD-11 | Public API | GET | /api/v1/admin/orders | Admin list orders / Quản trị xem tất cả đơn | ORDER.LIST / Admin, Support | - |
| ORD-12 | Public API | PATCH | /api/v1/admin/orders/{orderId}/status | Admin update order status / Admin chỉnh trạng thái ngoại lệ | ORDER.UPDATE_STATUS / Admin | - |
| ORD-16 | Public API | GET | /api/v1/orders/{orderId}/invoice | Download invoice / Tải hoá đơn | ORDER.INVOICE.VIEW / Buyer (own), Seller (own), Admin, Support | - |
| AO-02 | Public API | PUT | /api/v1/auction-orders/{auctionOrderId}/shipping | Submit shipping info & get quote / Nhập địa chỉ và báo giá ship | AUCTION.VIEW + ORDER.UPDATE / Buyer (winner) | - |
| AO-03 | Public API | POST | /api/v1/auction-orders/{auctionOrderId}/pay | Pay auction order / Thanh toán đơn đấu giá | CHECKOUT.PAY / Buyer (winner) | Required |
