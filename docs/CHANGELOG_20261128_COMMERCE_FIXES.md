# Ghi nhận thay đổi CommerceService ngày 28/11/2026

Tài liệu này tổng hợp các thay đổi đã thực hiện tại CommerceService để xử lý lỗi kiểm thử đơn vị và chuẩn hóa luồng hủy đơn con trước khi triển khai kiểm thử thực tế.

## Hủy đơn hàng chỉ thực hiện ở cấp đơn con

Endpoint hủy đơn cha `POST /api/v1/orders/{orderId}/cancel` và `PUT /api/v1/orders/{orderId}/cancel` đã bị gỡ khỏi `Commerce.Api/Controllers/Public/OrdersController.cs`, cùng với `CancelOrderAsync` trong `IOrderService` và `CancelOrderRequest` trong `OrderRequests.cs`. Người mua chỉ hủy từng đơn hàng của từng gian hàng qua `POST /api/v1/sub-orders/{subOrderId}/cancel`.

Trong `Commerce.Application/Services/Order/OrderService.cs`, `CancelSubOrderAsync` chỉ cho phép hủy khi đơn con chưa được người bán xác nhận (`SellerAcceptedAt` là null) và chưa có mã vận đơn (`TrackingCode` là null). Các bài kiểm thử trong `OrderLifecycleTests.cs` và `OrderSettlementRefundTests.cs`, cùng tài liệu `docs/api/Orders/POST_SUB_ORDERS_CANCEL.md` và `docs/api/README.md`, đã được cập nhật theo hành vi này.

## Sửa bốn lỗi trong luồng giỏ hàng, voucher và đặt hàng

Bốn lỗi sau đã được xác định từ bộ kiểm thử `Commerce.UnitTests` và xử lý trực tiếp tại nguồn phát sinh.

### Trả về mã lý do cụ thể khi thẩm định voucher không hợp lệ

Trong `Commerce.Application/Services/Discount/DiscountService.cs`, phương thức `ValidateVoucherAsync` trước đây ném ngoại lệ `CommerceDomainException` với mã `VOUCHER_NOT_APPLICABLE` mỗi khi `VoucherCalculationEngine` từ chối voucher. Cách xử lý này làm mất các mã lý do cụ thể mà engine đã phân loại.

Phương thức đã được sửa để đọc kết quả từ `evalResult.RejectedReasons` và trả về `ValidateVoucherResponse` với `IsValid = false` kèm mã lý do tương ứng: `VOUCHER_EXPIRED`, `VOUCHER_NOT_STARTED`, `VOUCHER_MIN_ORDER_NOT_MET`, `VOUCHER_OUT_OF_STOCK`, `VOUCHER_SCOPE_MISMATCH`, hoặc `VOUCHER_NOT_APPLICABLE` cho các trường hợp còn lại.

### Hiển thị tên gian hàng thực tế trong giỏ hàng nhóm theo shop

Trong `Commerce.Application/Services/Cart/CartService.cs`, `GetGroupedCartAsync` trước đây luôn gán tên gian hàng dạng rút gọn `Shop {8 ký tự đầu của SellerId}` dù `CartService` đã có `IUserClient`.

Phương thức đã được cập nhật để gọi song song `_userClient.GetSellerShopAsync(sellerId, cancellationToken)` cho các gian hàng trong giỏ. Nếu UserService trả về tên gian hàng hợp lệ, hệ thống dùng tên đó. Nếu cuộc gọi lỗi hoặc không có tên, hệ thống giữ lại tên rút gọn dự phòng.

### Gán đúng voucher shop về từng đơn con bất kể thứ tự GUID

Trong `Commerce.Application/Services/Order/OrderService.cs`, khi tạo đơn hàng nhiều gian hàng, `CreateOrderAsync` trước đây ánh xạ `reservedUsageIds[idx]` với `allocatedList[idx]` theo vị trí mảng. Tuy nhiên `ReserveVouchersAsync` trong `VoucherLifecycleService` sắp xếp `DiscountCodeId` tăng dần để khóa chống deadlock, nên thứ tự `reservedUsageIds` có thể khác với thứ tự `AllocatedUsages`. Khi GUID của voucher shop B nhỏ hơn shop A, voucher bị gán nhầm sang đơn con của shop còn lại.

Đoạn gán `SubOrderId` đã được sửa để khớp theo `u.DiscountCodeId` thay vì chỉ số mảng. Nếu các phân bổ của một `DiscountCodeId` chỉ thuộc về một `SellerId`, hệ thống gán `u.SubOrderId` vào đúng đơn con của người bán đó.

### Cập nhật lại giá snapshot trong giỏ hàng khi giá giảm làm mất điều kiện voucher

Trong `Commerce.Application/Services/Order/OrderService.cs`, khi giá sản phẩm tăng giữa lúc báo giá và lúc đặt hàng, `CreateOrderAsync` đã cập nhật lại `UnitPriceSnapshot` của giỏ hàng trước khi báo `PRICE_CHANGED`. Ngược lại, khi giá sản phẩm giảm khiến tổng đơn tụt xuống dưới mức tối thiểu của voucher và bị chặn với mã `VOUCHER_NO_LONGER_VALID`, giỏ hàng vẫn giữ giá cũ.

Phương thức đã được bổ sung bước cập nhật `UnitPriceSnapshot`, `SubtotalAmount`, và `LastActivityAt` của giỏ hàng đang hoạt động trước khi ném `VOUCHER_NO_LONGER_VALID`.

## Kết quả kiểm chứng

Lệnh kiểm chứng đã chạy trong cấu hình Release:

```bash
dotnet build Commerce.sln -c Release --no-restore
dotnet test Commerce.sln -c Release --no-build
```

Kết quả build đạt 0 lỗi, 0 cảnh báo. Bộ kiểm thử `Commerce.UnitTests` đạt 716 pass, 0 fail, 13 skipped (các test yêu cầu PostgreSQL cục bộ).
