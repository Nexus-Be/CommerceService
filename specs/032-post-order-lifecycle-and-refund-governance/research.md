# Research & Technical Decisions: Post-Order Lifecycle, Address Modification and Refund Governance

**Feature Branch**: `032-post-order-lifecycle-and-refund-governance`  
**Created**: 2026-11-19  
**Status**: Completed  

---

## 1. Post-Order Shipping Address Change Logic

### Decision 1: Boundary & Allowed Order States for Address Change
- **Vấn đề**: Người mua muốn đổi địa chỉ nhận hàng sau khi đơn hàng đã được tạo. Nếu đơn hàng đã xuất kho hoặc đã in vận đơn GHN (`TrackingCode` đã tồn tại), việc đổi địa chỉ trên CommerceService sẽ làm sai lệch thông tin bưu kiện thực tế trên hệ thống bưu cục GHN.
- **Quyết định kỹ thuật**:
  - Chỉ cho phép đổi địa chỉ khi:
    1. Đơn ở trạng thái `PendingPayment`, `Paid`, hoặc `Confirmed`.
    2. Chưa phát sinh mã vận đơn: `string.IsNullOrEmpty(subOrder.TrackingCode)` (và đối với AuctionOrder: `string.IsNullOrEmpty(auctionOrder.TrackingCode)`).
  - Nếu đơn đã ở trạng thái `Shipping`, `Delivered`, `Completed`, hoặc `Cancelled` $\rightarrow$ Ném lỗi HTTP 400 `CANNOT_CHANGE_ADDRESS_AFTER_SHIPPING` ("Shipping address cannot be changed after the order has been shipped or completed.").

### Decision 2: Shipping Fee Adjustment & Prepaid Protection Policy
- **Vấn đề**: Khi đổi địa chỉ sang một quận/tỉnh khác, cước vận chuyển GHN có thể tăng hoặc giảm.
- **Quyết định kỹ thuật**:
  - Đối với đơn chưa thanh toán (`PendingPayment`):
    - Tính lại cước phí qua `_fulfillmentClient.CalculateShopShippingFeeAsync`.
    - Cập nhật lại `subOrder.ShippingFeeAmount` và `subOrder.TotalAmount`.
    - Đồng thời cập nhật tổng tiền đơn cha `order.TotalAmount`.
  - Đối với đơn đã thanh toán (`Paid` hoặc `Confirmed`):
    - Tính cước phí mới `newShippingFee`.
    - **Quy tắc bảo vệ dòng tiền**: Nếu `newShippingFee <= oldShippingFee`, hệ thống cho phép đổi địa chỉ (phần chênh lệch giảm được bảo lưu hoặc sàn chịu/tiết kiệm chi phí vận chuyển).
    - Nếu `newShippingFee > oldShippingFee`, hệ thống từ chối đổi địa chỉ với HTTP 400 `SHIPPING_FEE_INCREASED` ("Shipping fee to new address exceeds prepaid amount. Please cancel and reorder."). Lý do: Không có cơ chế thu thêm tiền lẻ cước ship sau khi đơn đã thanh toán ví/escrow.

### Decision 3: Audit Trail for Address Changes
- Mỗi lần đổi địa chỉ thành công, ghi 1 bản ghi vào `OrderStatusHistory`:
  - `ActorRole = OrderActorRole.Buyer` (hoặc `Admin`/`Staff`).
  - Ghi nhận `FromStatus = subOrder.Status`, `ToStatus = subOrder.Status`.
  - Ghi chú `Notes = $"Shipping address updated to: {resolvedAddress.FullAddressText}"`.

---

## 2. Refund Governance & SubOrder Status Restrictions

### Decision 4: Refund Eligibility State Restrictions
- **Vấn đề**: Hiện tại `RefundSubOrderAsync` trong `OrderService` chưa kiểm tra chặt chẽ trạng thái đơn hàng khi tạo yêu cầu, dẫn tới rủi ro người mua yêu cầu hoàn tiền khi đơn chưa thanh toán (`PendingPayment`) hoặc đơn đã hủy (`Cancelled`).
- **Quyết định kỹ thuật**:
  - Chỉ cho phép tạo yêu cầu refund khi:
    - Đơn `SubOrder` ở trạng thái `Delivered` hoặc `Completed`.
    - Nếu đơn ở các trạng thái khác (`PendingPayment`, `Paid`, `Shipping`, `Cancelled`, `Refunded`) $\rightarrow$ Ném HTTP 400 `INVALID_REFUND_STATUS` ("Sub-order status is not eligible for refund request.").
  - **Chống spam trùng lặp**:
    - Kiểm tra xem đã có bản ghi `OrderRefund` nào thuộc `subOrderId` này đang ở trạng thái `RefundStatus.Requested` chưa.
    - Nếu đã có $\rightarrow$ Ném HTTP 409 `REFUND_REQUEST_ALREADY_EXISTS` ("A pending refund request already exists for this sub-order.").

### Decision 5: Dedicated RefundsController Architecture
- Tách biệt các thao tác quản trị hoàn tiền vào `RefundsController` theo đúng chuẩn RESTful:
  - `GET /api/v1/refunds`: Lấy danh sách yêu cầu hoàn tiền (hỗ trợ phân trang database `PagedData<RefundSummaryResponse>`, lọc theo `status`, `subOrderId`).
    - Buyer: chỉ xem của mình (`BuyerId == currentUserId`).
    - Seller: chỉ xem của shop mình (`SellerId == currentUserId`).
    - Admin/Staff: xem toàn bộ.
  - `GET /api/v1/refunds/{refundId}`: Lấy chi tiết yêu cầu hoàn tiền kèm kiểm tra quyền sở hữu.
  - `POST /api/v1/refunds/{refundId}/approve`: Duyệt hoàn tiền. Gọi `_walletClient.RefundSubOrderPaymentAsync`, chuyển `SubOrder.Status = Refunded`, `OrderRefund.Status = Completed`, cập nhật `CompletedAt`.
  - `POST /api/v1/refunds/{refundId}/reject`: Từ chối hoàn tiền. Bắt buộc truyền `reason`, chuyển `OrderRefund.Status = Rejected`, cập nhật `CompletedAt`.

---

## 3. Auction Order Extended Lifecycle

### Decision 6: Auction Order Shipping Timeline
- Thêm `GET /api/v1/auction-orders/{id}/shipments/timeline` cho phép theo dõi toàn bộ lịch sử trạng thái đơn hàng đấu giá (`OrderStatusHistories`) tương tự đơn hàng thường.
- Nếu đơn có `TrackingCode`, kết hợp thông tin lộ trình từ `FulfillmentService` nếu có.

### Decision 7: Auction Order Pre-Acceptance Cancellation
- Thêm `POST /api/v1/auction-orders/{id}/cancel` cho phép người mua (hoặc admin/staff) hủy đơn đấu giá trước khi người bán bấm chấp nhận (`PendingPayment` hoặc `Paid`).
- Nếu đơn đã `Paid`: Hoàn trả toàn bộ số tiền thanh toán vào ví người mua qua `_walletClient.RefundAuctionOrderPaymentAsync`, cập nhật `auctionOrder.Status = SubOrderStatus.Cancelled`, và gửi callback trạng thái `Cancelled` sang `AuctionService`.

### Decision 8: Auction Order Shipment Creation Retry
- Thêm `POST /api/v1/auction-orders/{id}/shipments/retry` cho phép người bán (hoặc staff) tạo lại vận đơn GHN nếu đơn đã ở trạng thái `Confirmed` nhưng chưa có `TrackingCode` (do sự cố mạng tạm thời lúc seller accept).
- Tái sử dụng `ShipmentRequestFactory.ForAuctionOrder(auctionOrder)` để đảm bảo tính đồng bộ payload 100%.

---

## 4. Backoffice Admin & Staff Endpoints

### Decision 9: Global Order Queries
- Cung cấp:
  - `GET /api/v1/admin/orders`: Tra cứu danh sách đơn cha toàn sàn, phân trang `PagedData<OrderSummaryResponse>`, lọc theo `buyerId`, `sellerId`, `status`, `fromDate`, `toDate`.
  - `GET /api/v1/admin/sub-orders`: Tra cứu danh sách đơn con toàn sàn, phân trang `PagedData<SubOrderSummaryResponse>`, lọc theo `status`, `sellerId`, `buyerId`, `trackingCode`.
- Bảo vệ bằng `[RequireRole("admin", "staff")]`.
