# 04 — MÔ HÌNH THỰC THỂ & CƠ SỞ DỮ LIỆU (COMMERCE SERVICE DATA PERSISTENCE - V2)

---

## 1. MÔ HÌNH THỰC THỂ CHÍNH (DOMAIN ENTITIES)

1. **`Cart` (Giỏ hàng)**:
   - `Id`: `Guid`.
   - `BuyerId`: `Guid` (Liên kết logic `UserService.Users.Id`).
   - `Status`: `CartStatus` (`Active = 0`, `CheckedOut = 1`, `Abandoned = 2`).
   - `ItemCount`: `int`.
   - `SubtotalAmount`: `decimal`.
   - `LastActivityAt`: `DateTimeOffset`.
   - `RowVersion`: `long` (Optimistic locking chống ghi đè song song).
   - Navigation: `ICollection<CartItem> Items`, `ICollection<Order> Orders`.

2. **`CartItem`**:
   - `Id`: `Guid`.
   - `CartId`: `Guid` (FK $\rightarrow$ `Cart.Id`).
   - `ProductId`: `Guid` (Cross-service Ref $\rightarrow$ `CatalogService.Products.Id`).
   - `SkuId`: `Guid` (Cross-service Ref $\rightarrow$ `CatalogService.ProductVariants.Id`, bắt buộc).
   - `SellerId`: `Guid` (Cross-service Ref $\rightarrow$ `UserService.Users.Id`).
   - `Quantity`: `int` (Quantity > 0).
   - `UnitPriceSnapshot`: `decimal`.
   - `ProductNameSnapshot`: `string`.
   - `AddedAt`: `DateTimeOffset`.
   - Navigation: `Cart? Cart`.

3. **`Order` (Đơn hàng tổng / Master Order - Trước đây là CheckoutGroup)**:
   - `Id`: `Guid`.
   - `CartId`: `Guid?` (FK $\rightarrow$ `Cart.Id`).
   - `BuyerId`: `Guid` (Cross-service Ref $\rightarrow$ `UserService.Users.Id`).
   - `Status`: `OrderStatus` (`PendingPayment = 0`, `PaymentProcessing = 1`, `Paid = 2`, `Cancelled = 3`, `Expired = 4`).
   - `TotalAmount`: `decimal` (Precision: 18, Scale: 2).
   - `WalletTransactionId`: `Guid?` (Cross-service Ref $\rightarrow$ `WalletService.WalletTransactions.Id`).
   - `PaymentAttemptCount`: `int` (Số lần thử thanh toán, tối đa 3 lần).
   - `PaymentDeadlineAt`: `DateTimeOffset`.
   - `RowVersion`: `long` (Concurrency Token).
   - Navigation: `Cart?`, `ICollection<SubOrder> SubOrders`, `ICollection<DiscountCodeUsage> DiscountCodeUsages`, `OrderInvoice? Invoice`, `ICollection<OrderRefund> Refunds`.

4. **`SubOrder` (Đơn hàng con theo từng Shop/Seller - Trước đây là Order)**:
   - `Id`: `Guid`.
   - `SubOrderNo`: `string` (Unique Index, định dạng `ORD-YYYYMMDD-NNNNNN`).
   - `OrderId`: `Guid` (FK $\rightarrow$ `Order.Id`).
   - `BuyerId`, `SellerId`: `Guid`.
   - `Status`: `SubOrderStatus` (`PendingPayment = 0`, `Paid = 1`, `Confirmed = 2`, `Shipping = 3`, `Delivered = 4`, `Completed = 5`, `Cancelled = 6`, `Refunded = 7`).
   - `PaymentMethod`: `PaymentMethod` (`Wallet = 0`, `VnPay = 1`, `Cod = 2`).
   - `WalletTransactionId`: `Guid?` (Cross-service Ref $\rightarrow$ `WalletService.WalletTransactions.Id`).
   - `ShippingAddress`: `string` (JSON cấu trúc địa chỉ giao hàng).
   - `ContactPhone`: `string`.
   - `ItemsAmount`, `ProductDiscountAmount`, `ShopDiscountAmount`, `PlatformMarkupAmount`, `SystemDiscountAmount`, `ShippingFeeAmount`, `TotalAmount`, `SellerReceiveAmount`, `PlatformFeeAmount`: `decimal`.
   - Invariant tổng tiền: `TotalAmount = SellerReceiveAmount + PlatformFeeAmount + ShippingFeeAmount`.
   - `PaymentDeadlineAt`, `SellerRespondDeadlineAt`, `PaidAt`, `SellerAcceptedAt`, `ShippedAt`, `DeliveredAt`, `CompletedAt`, `CancelledAt`: `DateTimeOffset?`.
   - `RowVersion`: `long`.
   - Navigation: `Order`, `ICollection<SubOrderItem> Items`, `ICollection<OrderStatusHistory> StatusHistories`, `OrderInvoice? Invoice`, `OrderCancellation? Cancellation`, `ICollection<OrderRefund> Refunds`, `ICollection<DiscountCodeUsage> DiscountCodeUsages`.

5. **`SubOrderItem` (Dòng sản phẩm đơn con - Trước đây là OrderItem)**:
   - `Id`: `Guid`.
   - `SubOrderId`: `Guid` (FK $\rightarrow$ `SubOrder.Id`).
   - `ProductId`, `SkuId`, `SellerId`: `Guid`.
   - `ProductNameSnapshot`, `SkuCodeSnapshot`, `SkuAttributesSnapshot`, `ImageUrlSnapshot`: Snapshot sản phẩm tại thời điểm đặt hàng.
   - `Quantity`: `int`.
   - `UnitPrice`, `UnitPriceAfterDiscount`, `LineTotal`: `decimal`.
   - `DiscountCodeId`: `Guid?`.
   - Navigation: `SubOrder`.

6. **`AuctionOrder` (Đơn hàng từ kết quả đấu giá)**:
   - `Id`: `Guid`.
   - `OrderNo`: `string` (Unique Index, định dạng `AOR-YYYYMMDD-NNNNNN`).
   - `AuctionId`: `Guid` (Cross-service Ref $\rightarrow$ `AuctionService.Auctions.Id`).
   - `BuyerId`, `SellerId`, `WinningBidId`, `ProductId`: `Guid`.
   - `SkuId`: `Guid?`.
   - `ProductNameSnapshot`: `string`.
   - `ImageUrlSnapshot`: `string?` (Snapshot ảnh sản phẩm đấu giá).
   - `SkuAttributesSnapshot`: `string` (JSON snapshot thuộc tính SKU).
   - `WinningAmount`, `ShippingFeeAmount`, `TotalAmount`, `SellerReceiveAmount`, `PlatformFeeAmount`: `decimal`.
   - `Status`: `SubOrderStatus`.
   - `PaymentMethod`: `PaymentMethod`.
   - `WalletTransactionId`: `Guid?`.
   - Navigation: `StatusHistories`, `Invoice`, `Cancellation`, `ICollection<OrderRefund> Refunds`.

7. **`OrderRefund` (Yêu cầu hoàn tiền - Mới bổ sung ở v2)**:
   - `Id`: `Guid`.
   - `SubOrderId`: `Guid?` (Hoàn cho đơn hàng con của shop).
   - `AuctionOrderId`: `Guid?` (Hoàn cho đơn đấu giá).
   - `OrderId`: `Guid?` (Hoàn toàn bộ đơn tổng khi giao dịch lỗi/tiền về muộn).
   - `CancellationId`: `Guid?` (Liên kết bản ghi hủy đơn nếu có).
   - `RefundType`: `RefundType` (`Full = 0`, `Partial = 1`).
   - `Amount`: `decimal(18,2)`.
   - `Status`: `RefundStatus` (`Requested = 0`, `Completed = 1`, `Failed = 2`).
   - `ReasonCode`: `string`.
   - `ReasonNote`: `string?`.
   - `ActorRole`: `OrderActorRole` (`Buyer = 0`, `Seller = 1`, `Admin = 2`, `SupportStaff = 3`, `System = 4`).
   - `RequestedBy`: `Guid?`.
   - `WalletTransactionId`: `Guid?` (Giao dịch gốc cần hoàn).
   - `RefundTransactionId`: `Guid?` (Giao dịch hoàn tiền thành công).
   - `RequestEventId`: `Guid?` (Outbox event ID để đối soát).
   - `FailureCode`: `string?` (Mã lỗi khi hoàn tiền thất bại).
   - `RequestedAt`: `DateTimeOffset`.
   - `CompletedAt`, `FailedAt`: `DateTimeOffset?`.
   - `RowVersion`: `long`.
   - Navigation: `SubOrder?`, `AuctionOrder?`, `Order?`, `Cancellation?`.

8. **`DiscountCode`, `DiscountCodeScope`, `DiscountCodeUsage`**:
   - `DiscountCode`: `IsVisible = true` thay thế cho `IsDisabled`. `ScopeType`: `System`, `Shop`, `Product`.
   - `DiscountCodeScope`: `ScopeRefId` là nullable (cho phép `All` khi không gắn ID tham chiếu).
   - `DiscountCodeUsage`: Lưu vết giữ chỗ (`Reserved`), áp dụng (`Used`), và giải phóng (`Released`).

9. **`OrderStatusHistory`, `OrderInvoice`, `OrderCancellation`**:
   - `OrderStatusHistory`: Lịch sử trạng thái của SubOrder và AuctionOrder; hỗ trợ vai trò `SupportStaff`.
   - `OrderInvoice`: Quản lý xuất hóa đơn với `Status` (`Generated = 0`, `Failed = 1`).
   - `OrderCancellation`: Ghi nhận lý do hủy đơn, cờ giải phóng tồn kho `InventoryReleased`, trường tương thích `RefundTransactionId`, liên kết `Refunds`.

10. **`SystemSetting` (Cấu hình hệ thống động)**:
    - Quản lý các tham số timeout, hạn phản hồi, số lần retry, số lượng sản phẩm tối đa:
      `CART_EXPIRATION_DAYS`, `CHECKOUT_TIMEOUT_MINUTES`, `SELLER_RESPOND_HOURS`, `ORDER_AUTO_COMPLETE_DAYS`, `PAYMENT_RETRY_LIMIT`, `MAX_QTY_PER_ITEM`, `MAX_CART_ITEMS`.

11. **`DailyOrderSequence` (Chuỗi sinh số thứ tự đơn theo ngày)**:
    - Khóa chính kết hợp: `(SeqDate, SeqType)`.
    - Sinh mã `OrderNo`, `AuctionOrderNo`, `InvoiceNo` dạng sequence hàng ngày đảm bảo không trùng lặp và liên tục.

12. **`AuditLog`**:
    - Nhật ký kiểm toán toàn diện các thao tác thương mại.
