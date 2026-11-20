# Feature Specification: Checkout Hardening & Comprehensive Remediation

**Feature Identifier**: `004-checkout-hardening`  
**Status**: In Progress  
**Priority**: CRITICAL  
**Date**: 2026-11-01  

---

## 1. Executive Summary & Problem Statements
Hệ thống Commerce Service đã hoàn thành cơ chế tích hợp Giỏ hàng - Đơn hàng với Fulfillment Service và Wallet Service. Tuy nhiên, qua quá trình rà soát kiến trúc (Architecture Audit), phát hiện 8 điểm rủi ro kỹ thuật và lỗi nghiệp vụ nghiêm trọng cần được xử lý dứt điểm:

1. **ISSUE-01 (Distributed Cache for Quotes)**: Thay thế In-memory RAM `ConcurrentDictionary` bằng `IDistributedCache` abstraction để hệ thống hoạt động ổn định khi scale-out đa pod/multi-instance.
2. **ISSUE-02 (Double Submit / Race Condition Prevention)**: Chuyển sang cơ chế Atomic Pop (`TryRemove` hoặc check-and-consume) tại đầu hàm `CreateOrderAsync` và truyền Idempotency Key sang Wallet Service để triệt tiêu nguy cơ trừ tiền ví 2 lần.
3. **BUG-A (Targeted Sub-Order Inventory Release)**: Sửa lỗi truyền nhầm `so.OrderId` khi Shop từ chối đơn hàng con. Phải chỉ nhả tồn kho của các sản phẩm thuộc đúng `SubOrder` đó.
4. **BUG-B (Automatic Refund for Seller-Rejected Paid Sub-Orders)**: Bổ sung luồng gọi `_walletClient.RefundSubOrderPaymentAsync` và tạo bản ghi `OrderRefund` khi Shop từ chối đơn hàng đã thanh toán.
5. **ISSUE-04 (Eliminate Silent Fallbacks in Stock Commit)**: Xóa bỏ `return true` trong khối catch của `FulfillmentClient.CommitStockAsync`; ghi nhận lỗi và kích hoạt cờ đối soát khi commit kho thất bại.
6. **ISSUE-03 (Price Drift Protection)**: Kiểm tra lại giá và trạng thái khả dụng của SKU từ `CatalogService` trong `CreateOrderAsync`. Ném lỗi `PRICE_CHANGED` nếu giá đã thay đổi so với báo giá ban đầu.
7. **BUG-C (Stock Check on Quantity Update in Cart)**: Bổ sung kiểm tra tồn kho kho Fulfillment khi người dùng tăng số lượng món hàng trong giỏ (`UpdateItemQuantityAsync` - CART-05).
8. **ISSUE-05 (Real Wallet Balance in Checkout Quote)**: Bổ sung `GetWalletBalanceAsync` vào `IWalletClient` và trả về số dư thực tế trong `CheckoutQuoteResponse.WalletBalance` thay vì giá trị hardcode `0m`.

---

## 2. Requirements & Bounded Context Guardrails
- **Inward-Only**: Toàn bộ thay đổi nằm trong phạm vi nội bộ `CommerceService`.
- **Clean Architecture**: Tầng Application định nghĩa Interface, Tầng Infrastructure hiện thực REST client, Tầng Domain lưu giữ invariant.
- **Financial Invariants**: Mọi giao dịch tài chính hoàn tiền phải lưu vết vào bảng `OrderRefunds`.
- **Zero Overselling**: Kiểm tra tồn kho trước khi tăng số lượng giỏ, không bao giờ nhả nhầm kho của shop khác.
