# Feature Specification: Hybrid Payment Expiration & Asymmetric Price Drift Policy

**Feature Identifier**: `006-hybrid-expiration-and-price-drift`  
**Status**: In Progress  
**Priority**: HIGH  
**Date**: 2026-11-02  

---

## 1. Executive Summary & Problem Statement

Hệ thống Commerce Service hiện đang áp dụng quy tắc kiểm tra giá đối xứng nghiêm ngặt (`Strict All-or-Nothing`): Khi người dùng bấm tạo đơn, nếu có bất kỳ biến động giá nào (dù tăng hay giảm) so với lúc lấy báo giá (`Quote`), hệ thống đều chặn đơn và ném lỗi `PRICE_CHANGED`. Điều này làm gián đoạn trải nghiệm người mua khi sản phẩm được giảm giá (hạ giá khuyến mãi), làm giảm tỷ lệ chuyển đổi đơn hàng.

Đồng thời, cơ chế xử lý đơn quá hạn thanh toán hiện mới chỉ phụ thuộc vào tiến trình nền định kỳ (`PaymentTimeoutWorker`), chưa có tầng phòng vệ thụ động (`Passive Guard / Lazy Check`) tại các API tra cứu và thanh toán, dẫn đến khả năng xảy ra race-condition trong khoảng thời gian giữa 2 chu kỳ quét của worker.

Tính năng này triển khai 2 module nghiệp vụ cốt lõi:
1. **Module 1: Chiến lược Xử lý Biến động Giá Bất đối xứng (Asymmetric Price Drift Policy)**:
   - **Giá Tăng**: Chặn đơn (`Hard Block`), cập nhật giá mới vào giỏ hàng (`CartItems`), trả về mã lỗi `PRICE_CHANGED` (HTTP 409 Conflict) kèm chi tiết các món tăng giá để người mua xác nhận.
   - **Giá Giảm hoặc Không đổi**: Tự động áp dụng mức giá thấp hơn (`Silent Auto-Apply`), cập nhật snapshot giỏ hàng, cho phép tiếp tục tạo đơn hàng và trả về thông tin tiết kiệm `appliedPriceDrops`.
   - **Đóng băng giá sau Checkout (Post-Checkout Price Lock)**: Đơn hàng sau khi tạo ở trạng thái `PendingPayment` sẽ đóng băng giá vĩnh viễn, không kiểm tra lại giá Catalog khi thanh toán.
2. **Module 2: Chiến lược Hết hạn Thanh toán Lai (Hybrid Payment Expiration Strategy)**:
   - **Passive Guard (Lazy API Check)**: Kiểm tra hạn `PaymentDeadlineAt` ngay tại các API tra cứu chi tiết đơn hàng (`GetOrderDetail`, `GetSubOrderDetail`) và API thanh toán. Nếu quá hạn, tự động hủy đơn, nhả kho Fulfillment và giải phóng voucher ngay lập tức.
   - **Active Sweeper (`PaymentTimeoutWorker`)**: Quét định kỳ cả đơn thường (`Orders` quá hạn 15 phút) và đơn đấu giá (`AuctionOrders` quá hạn 24 giờ).

---

## 2. User Stories & Acceptance Criteria

### User Story 1: Tự động Áp dụng Giá Giảm khi Đặt Hàng
> **Là một** Người mua hàng (Buyer),  
> **Tôi muốn** nếu sản phẩm trong giỏ hàng vừa được giảm giá, tôi vẫn được đặt hàng bình thường với mức giá rẻ hơn,  
> **Thì** hệ thống không chặn đơn mà tự động tính tiền theo giá thấp hơn và hiển thị số tiền tôi vừa tiết kiệm được.

#### Scenario 1.1: Giá giảm khi tạo đơn
- **GIVEN** Sản phẩm A có giá trong Quote là `100.000 VNĐ`.
- **AND** Catalog Service cập nhật giá sản phẩm A xuống `80.000 VNĐ`.
- **WHEN** Người mua bấm "Đặt hàng" (`POST /api/v1/orders`).
- **THEN** Hệ thống không ném lỗi `PRICE_CHANGED`.
- **AND** Đơn hàng được tạo thành công với đơn giá `80.000 VNĐ`.
- **AND** Response trả về danh sách `appliedPriceDrops` ghi nhận tiết kiệm `20.000 VNĐ`.

#### Scenario 1.2: Có ít nhất 1 sản phẩm tăng giá
- **GIVEN** Giỏ hàng có sản phẩm A (giá cũ 100k -> giá mới 120k) và sản phẩm B (giá cũ 50k -> giá mới 40k).
- **WHEN** Người mua bấm "Đặt hàng".
- **THEN** Hệ thống chặn đơn, trả về HTTP 409 Conflict với mã lỗi `PRICE_CHANGED`.
- **AND** Snapshot giá của cả sản phẩm A và B trong giỏ hàng `CartItems` được cập nhật giá mới ngay lập tức.
- **AND** Khi người mua bấm lại lần 2, đơn hàng được tạo thành công với giá mới.

---

### User Story 2: Hủy Đơn Quá Hạn Tức Thì khi Tra cứu (Passive Guard)
> **Là một** Hệ thống thương mại điện tử,  
> **Tôi muốn** ngay khi người dùng mở xem một đơn hàng đã quá hạn thanh toán 15 phút,  
> **Thì** đơn hàng phải lập tức được chuyển sang trạng thái `Cancelled`, nhả kho và nhả voucher ngay tại chỗ mà không cần đợi tiến trình nền chạy tới.

#### Scenario 2.1: Xem chi tiết đơn hàng đã quá hạn
- **GIVEN** Đơn hàng có trạng thái `PendingPayment` và `PaymentDeadlineAt < DateTimeOffset.UtcNow`.
- **WHEN** Khách hàng gọi `GET /api/v1/orders/{orderId}`.
- **THEN** Hệ thống tự động chuyển trạng thái đơn sang `Cancelled`.
- **AND** Gọi `_fulfillmentClient.ReleaseStockAsync(order.Id)` để trả hàng về kho.
- **AND** Trả về DTO đơn hàng với trạng thái `Cancelled`.

---

## 3. Bounded Context & Guardrails
- **Inward-Only**: Port 8003, Database `commerce_db:5435`.
- **Clean Architecture & No-Repository**: Xử lý trực tiếp qua `ICommerceDbContext` và `AppDbContext`.
- **100% RESTful Web API**: Tuyệt đối không dùng RabbitMQ / MassTransit / Saga / Outbox. Mọi tương tác kho thực hiện qua `IFulfillmentClient` đồng bộ.
- **RFC 9110 Error Standards**: `PRICE_CHANGED` trả về HTTP 409 Conflict; thông báo lỗi 100% English sentence case.
