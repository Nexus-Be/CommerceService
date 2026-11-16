# 05 — DANH SÁCH KIỂM CHỨNG & NGHIỆM THU (COMMERCE SERVICE CHECKLIST)

> **Trạng thái triển khai (2026-11-16):** ✅ ĐÃ HOÀN THIỆN & CHUẨN HÓA — Hệ thống sở hữu bộ kiểm thử tự động toàn diện với hơn 60 Test Cases bao phủ toàn bộ Domain Invariants, Business Use Cases, Fault Tolerance, Idempotency và End-to-End Flows.
> **Nguồn sự thật:** Code trong `Commerce.sln` và `tests/Commerce.UnitTests/`.
> **Cập nhật lần cuối:** 2026-11-16.

---

## 1. LỆNH BIÊN DỊCH & CHẠY KIỂM THỬ TỰ ĐỘNG

Tên solution chuẩn xác là **`Commerce.sln`**:

```bash
# 1. Biên dịch toàn bộ Solution ở chế độ Release:
dotnet build Commerce.sln -c Release

# 2. Thực thi toàn bộ bộ kiểm thử tự động:
dotnet test Commerce.sln -c Release

# Hoặc thực thi riêng project Unit & Integration Tests:
dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj -c Release

# 3. Khởi chạy service tại cổng 8003:
dotnet run --project Commerce.Api

# 4. Kiểm tra sức khỏe của dịch vụ:
curl http://localhost:8003/health
```

---

## 2. DANH MỤC CÁC BỘ TEST TRONG `Commerce.UnitTests`

| Phân hệ kiểm thử | File Test | Mục tiêu xác minh |
|---|---|---|
| **Domain Invariants** | `CartDomainTests.cs` | Bất biến tính toán tổng tiền giỏ hàng, số lượng mặt hàng, variant. |
| | `FinancialInvariantTests.cs` | Bất biến công thức tài chính: `Total = SellerReceive + PlatformFee + ShippingFee`. |
| **Application Services** | `CartServiceTests.cs` | Thêm món, đổi variant, dồn số lượng, tính tiền chọn lọc (checkbox), gộp giỏ. |
| | `OrderServiceTests.cs` | Lấy chi tiết đơn cha, đơn con, lịch sử đơn hàng, hủy đơn, hoàn tiền. |
| | `OrderCheckoutTests.cs` | Tạo quote 15 phút, tính phí ship GHN, kiểm tra số dư ví, chốt đơn hàng. |
| | `OrderLifecycleTests.cs` | Chu kỳ trạng thái đơn: `PendingPayment` $\rightarrow$ `Paid` $\rightarrow$ `Confirmed` $\rightarrow$ `Shipped` $\rightarrow$ `Completed`. |
| | `AuctionOrderServiceTests.cs` | Tiếp nhận settlement, chốt ship, thanh toán ví, seller duyệt/từ chối, giải ngân. |
| | `EndToEndFlowTests.cs` | Kiểm thử xuyên suốt 8 E2E business flows từ Controller đến CSDL. |
| **Hạ Tầng & Chịu Lỗi** | `ExternalClientFaultTests.cs` | Kiểm thử cơ chế Fail-Closed và bồi hoàn đền bù khi dịch vụ ngoài bị lỗi. |
| | `IdempotencyKeyTests.cs` | Chống click lặp và gửi trùng request tạo đơn hoặc thanh toán. |
| | `CatalogClientTests.cs` | Phát hiện biến động giá bất đối xứng (Asymmetric Price Drift). |
| | `FulfillmentClientTests.cs` | Thẩm định tồn kho và giữ kho vật lý 15 phút. |
| | `UserClientTests.cs` | Kiểm tra quyền mua hàng và cơ chế thưởng/phạt điểm uy tín. |
| **Nền Tảng & Host** | `PaymentTimeoutWorkerTests.cs` | Tự động hủy đơn hàng và xả kho khi hết hạn thanh toán. |
| | `VnPayPaymentPollingWorkerTests.cs` | Quét kiểm tra trạng thái thanh toán VNPay định kỳ. |
| | `OrdersControllerDeliveryCallbackTests.cs` | Webhook callback cập nhật trạng thái giao hàng từ Fulfillment. |

---

## 3. CHECKLIST TRƯỚC KHI COMMIT DÀNH CHO AI AGENTS

Mọi lập trình viên và AI Agent khi hoàn thành nhiệm vụ bắt buộc phải kiểm tra và thỏa mãn 100% danh sách sau:
- [ ] **Lập Plan trước khi hành động**: Có plan phân tích rủi ro và phạm vi ảnh hưởng trước khi sửa code.
- [ ] **Biên dịch thành công 100%**: `dotnet build Commerce.sln -c Release` đạt 0 Error, 0 Warning nghiêm trọng.
- [ ] **Kiểm thử tự động đạt 100%**: Toàn bộ test cases trong `Commerce.UnitTests` đều passed.
- [ ] **Tuân thủ Clean Architecture**: 
  - Không bọc Repository Pattern ngoài EF Core (dùng `ICommerceDbContext` trực tiếp).
  - Feature Co-Location đầy đủ trong `Commerce.Application/Services/{Feature}/`.
  - Thin Controller ghép cặp 1-to-1 với Feature Service tương ứng.
- [ ] **Quy tắc Bounded Context & Fail-Closed**:
  - Không tự trừ tiền ví hay trừ tồn kho nội bộ; gọi qua Outbound Typed REST Clients.
  - Tuyệt đối cấm gRPC, RabbitMQ, MassTransit và Saga state machine.
- [ ] **Bảo vệ Quyền Sở Hữu Tài Nguyên (Resource Ownership)**:
  - Lấy `currentUserId` từ `HttpContext.GetUserId()` và gọi `EnsureOwnership()`.
  - Cấm tin tưởng tham số `buyerId` từ query string hay route URL.
- [ ] **Quy chuẩn Thông Báo Lỗi**:
  - 100% English Sentence case cho message (`"Order cannot be cancelled in its current status."`).
  - Mã lỗi `SCREAMING_SNAKE_CASE` (ví dụ: `ORDER_NOT_FOUND`, `CART_NOT_FOUND`, `VAL_INVALID_INPUT`).
- [ ] **Quy chuẩn Tuần Tự Hóa Enum**:
  - Trả về JSON String dạng PascalCase (ví dụ: `"PendingPayment"`, `"Paid"`, `"Delivered"`).
- [ ] **Quy tắc Phạm vi Nội bộ (Inward-Only)**:
  - Tuyệt đối không chứa liên kết trỏ ra ngoài repository (`../../../docs/...` hay `../../Catalog/...`).
- [ ] **Vệ sinh Git Status**:
  - Không commit file rác (`bin/`, `obj/`, `.vs/`, `.idea/`, `.env`).
