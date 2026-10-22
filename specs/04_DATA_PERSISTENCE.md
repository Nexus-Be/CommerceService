# 04 — MÔ HÌNH THỰC THỂ & CƠ SỞ DỮ LIỆU (COMMERCE SERVICE DATA PERSISTENCE)

---

## 1. MÔ HÌNH THỰC THỂ CHÍNH (DOMAIN ENTITIES)

1. **`Cart` (Giỏ hàng)**:
   - `Id`: `Guid`.
   - `BuyerId`: `Guid` (Liên kết logic `UserService.Users.Id`).
   - `Status`: `CartStatus` (`Active = 0`, `Abandoned = 1`, `Converted = 2`).
   - `ItemCount`: `int`.
   - `SubtotalAmount`: `decimal`.
   - `LastActivityAt`: `DateTimeOffset`.
   - Navigation: `ICollection<CartItem> Items`, `ICollection<Order> Orders`.

2. **`CartItem`**:
   - `Id`: `Guid`.
   - `CartId`: `Guid` (FK $\rightarrow$ `Cart.Id`).
   - `ProductId`: `Guid` (Cross-service Ref $\rightarrow$ `CatalogService.Products.Id`).
   - `SkuId`: `Guid?` (Cross-service Ref $\rightarrow$ `CatalogService.ProductVariants.Id`).
   - `SellerId`: `Guid` (Cross-service Ref $\rightarrow$ `UserService.Users.Id`).
   - `Quantity`: `int`.
   - `UnitPriceSnapshot`: `decimal`.
   - `ProductNameSnapshot`: `string`.

3. **`Order` (Đơn hàng tổng / Master Order - Trước đây là CheckoutGroup)**:
   - `Id`: `Guid`.
   - `CartId`: `Guid?` (FK $\rightarrow$ `Cart.Id`).
   - `BuyerId`: `Guid` (Cross-service Ref $\rightarrow$ `UserService.Users.Id`).
   - `Status`: `OrderStatus` (`PendingPayment = 0`, `Paid = 1`, `Failed = 2`, `Expired = 3`, `Cancelled = 4`).
   - `TotalAmount`: `decimal` (Precision: 18, Scale: 2).
   - `PaymentDeadlineAt`: `DateTimeOffset`.
   - `RowVersion`: `long` (Concurrency Token).
   - Navigation: `Cart?`, `ICollection<SubOrder> SubOrders`, `ICollection<DiscountCodeUsage> DiscountCodeUsages`, `OrderInvoice? Invoice`.

4. **`SubOrder` (Đơn hàng con theo từng Shop/Seller - Trước đây là Order)**:
   - `Id`: `Guid`.
   - `SubOrderNo`: `string` (Unique Index).
   - `OrderId`: `Guid` (FK $\rightarrow$ `Order.Id`).
   - `BuyerId`, `SellerId`: `Guid`.
   - `Status`: `SubOrderStatus` (`PendingPayment = 0`, `Paid = 1`, `SellerAccepted = 2`, `Shipped = 3`, `Delivered = 4`, `Completed = 5`, `Cancelled = 6`).
   - `ShippingAddress`: `string` (JSON cấu trúc địa chỉ).
   - `ContactPhone`: `string`.
   - `ItemsAmount`, `ProductDiscountAmount`, `ShopDiscountAmount`, `PlatformMarkupAmount`, `SystemDiscountAmount`, `ShippingFeeAmount`, `TotalAmount`, `SellerReceiveAmount`, `PlatformFeeAmount`: `decimal`.
   - `PaymentDeadlineAt`, `SellerRespondDeadlineAt`, `PaidAt`, `SellerAcceptedAt`, `ShippedAt`, `DeliveredAt`, `CompletedAt`, `CancelledAt`: `DateTimeOffset?`.
   - `RowVersion`: `long`.
   - Navigation: `Order`, `ICollection<SubOrderItem> Items`, `ICollection<OrderStatusHistory> StatusHistories`, `OrderInvoice? Invoice`, `OrderCancellation? Cancellation`, `ICollection<DiscountCodeUsage> DiscountCodeUsages`.

5. **`SubOrderItem` (Dòng sản phẩm đơn con - Trước đây là OrderItem)**:
   - `Id`: `Guid`.
   - `SubOrderId`: `Guid` (FK $\rightarrow$ `SubOrder.Id`).
   - `ProductId`, `SkuId`, `SellerId`: `Guid`.
   - `ProductNameSnapshot`, `SkuCodeSnapshot`, `SkuAttributesSnapshot`, `ImageUrlSnapshot`: Snapshot thông tin sản phẩm.
   - `Quantity`: `int`.
   - `UnitPrice`, `UnitPriceAfterDiscount`, `LineTotal`: `decimal`.
   - `DiscountCodeId`: `Guid?`.
   - Navigation: `SubOrder`.

6. **`AuctionOrder` (Đơn hàng từ kết quả đấu giá)**:
   - `Id`: `Guid`.
   - `OrderNo`: `string` (Unique Index).
   - `AuctionId`: `Guid` (Cross-service Ref $\rightarrow$ `AuctionService.Auctions.Id`).
   - `BuyerId`, `SellerId`, `WinningBidId`, `ProductId`, `SkuId`: `Guid`.
   - `WinningAmount`, `ShippingFeeAmount`, `TotalAmount`, `SellerReceiveAmount`, `PlatformFeeAmount`: `decimal`.
   - `Status`: `SubOrderStatus`.
   - Navigation: `StatusHistories`, `Invoice`, `Cancellation`.

7. **`DiscountCode`, `DiscountCodeScope`, `DiscountCodeUsage`**:
   - Quản lý mã giảm giá toàn sàn và theo shop, phạm vi áp dụng và lịch sử giữ chỗ/sử dụng.

8. **`OrderStatusHistory`, `OrderInvoice`, `OrderCancellation`**:
   - Lịch sử biến động trạng thái đơn, hóa đơn điện tử và ghi nhận lý do hủy đơn.

9. **`AuditLog`**:
   - Nhật ký kiểm toán thao tác thương mại.
