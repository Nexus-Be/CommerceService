# Feature Specification: User Service Alignment — Buyer Permission Gate, Seller Reputation Rewards & Violation Penalties

**Feature Identifier**: `016-user-service-alignment`  
**Created**: 2026-11-10  
**Status**: Ready for Planning (`speckit-plan`)  
**Target Service**: `CommerceService` (Port `8003`, Database `commerce_db:5435`)  
**Priority**: CRITICAL (P1)  
**Input**: Đặc tả tích hợp giữa CommerceService và UserService (Tạo đơn hàng, Hoàn tất đơn hàng, Hủy đơn vi phạm)

---

## 1. Executive Summary & Problem Statement

### 1.1. Hiện trạng & Khoảng trống nghiệp vụ
Hệ thống Thương mại điện tử Nexus vận hành mô hình kiến trúc phân tán. Mặc dù `CommerceService` đã hoàn tất chuẩn hóa 100% RESTful và giao tiếp trực tiếp với `WalletService`, `CatalogService`, và `FulfillmentService`, dịch vụ này hiện **hoàn toàn cô lập với `UserService` (:8001)**:
1. **Thiếu chốt chặn quyền mua hàng (Buyer Permission Gate)**: Khách hàng đang chịu các biện pháp trừng phạt cấm mua sắm (`activeRestrictions` chứa `Buyer:Restriction` hoặc `Account:BanAccount`) hoặc tài khoản bị khóa (`UserStatus != Active`) vẫn có thể tiến hành tạo đơn hàng và thanh toán bình thường.
2. **Thiếu cơ chế ghi nhận điểm thưởng uy tín (Reputation Rewards)**: Khi người mua đã nhận hàng thành công và đơn hàng hoàn tất, Người bán (Seller) không được ghi nhận cộng điểm uy tín (`+1 điểm`), làm mất động lực của đối tác bán hàng.
3. **Thiếu chế tài răn đe người bán tự ý hủy đơn (Violation Penalties)**: Người bán tự ý hủy đơn hàng đã được xác nhận mà không có lý do chính đáng/bất khả kháng chưa bị áp dụng chế tài trừ điểm phạt (`-5 điểm`), gây tổn hại nghiêm trọng đến trải nghiệm mua sắm của khách hàng.
4. **Lệch bề mặt API Controller**: Chưa có endpoint chuẩn `POST /api/v1/orders/{orderId}/complete`, và endpoint `POST /api/v1/orders/{orderId}/cancel` đang chặn cứng Người bán, không cho phép hủy đơn hàng vi phạm từ phía người bán.

### 1.2. Mục tiêu kỹ thuật
- Tích hợp Typed REST Client `IUserClient` gọi trực tiếp sang `UserService` (:8001) với thời gian chờ tối đa 10s và bảo tồn W3C Distributed Tracing.
- Thẩm định quyền mua hàng của Buyer trước khi giữ tồn kho và thanh toán ví.
- Triển khai endpoint `POST /api/v1/orders/{orderId}/complete` và kích hoạt cộng điểm thưởng uy tín đối tác (`INT-07`).
- Thiết lập cơ chế phát hiện người bán vi phạm hủy đơn và kích hoạt trừ điểm uy tín (`INT-06`).

---

## 2. User Scenarios & Testing (Mandatory)

### User Story 1: Buyer Purchasing Permission Enforcement (Priority: P1)
> **Là một** Hệ thống Quản trị Sàn Nexus,  
> **Tôi muốn** mọi hành động tạo đơn hàng từ Người mua phải được thẩm định trạng thái tài khoản và lệnh cấm tại UserService,  
> **Để** ngăn chặn người dùng vi phạm/gian lận thực hiện hành vi mua sắm trên sàn.

#### Acceptance Scenarios:
1. **Scenario 1.1: Tạo đơn thành công khi tài khoản người mua hợp lệ**
   - **GIVEN** Người mua có tài khoản trạng thái `UserStatus.Active` và không có lệnh phạt cấm mua hàng.
   - **WHEN** Người mua gửi request tạo đơn `POST /api/v1/orders`.
   - **THEN** CommerceService gọi `GET /api/v1/internal/users/{buyerId}/permission-check?action=buy`.
   - **AND** UserService trả về kết quả hợp lệ (`200 OK`).
   - **AND** CommerceService tiếp tục thực thi quy trình giữ kho, trừ tiền và tạo đơn hàng với HTTP 201 Created.

2. **Scenario 1.2: Chặn tạo đơn khi tài khoản bị xử phạt hoặc cấm mua hàng**
   - **GIVEN** Người mua có lệnh phạt hiệu lực (`activeRestrictions` chứa `Buyer:Restriction` hoặc `Account:BanAccount`) hoặc tài khoản không Active.
   - **WHEN** Người mua gửi request tạo đơn `POST /api/v1/orders`.
   - **THEN** CommerceService gọi `GET /api/v1/internal/users/{buyerId}/permission-check?action=buy`.
   - **AND** UserService trả về `403 Forbidden` kèm mã lỗi `BUYER_ACTION_RESTRICTED`.
   - **AND** CommerceService ngay lập tức dừng quy trình, KHÔNG giữ tồn kho Fulfillment, KHÔNG trừ tiền ví, và phản hồi HTTP 403 Forbidden:
     ```json
     {
       "errorCode": "BUYER_ACTION_RESTRICTED",
       "message": "User has an active restriction prohibiting purchase actions."
     }
     ```

---

### User Story 2: Order Completion & Partner Reputation Adjustment (Priority: P1)
> **Là một** Người bán hàng (Seller) trên Nexus,  
> **Tôi muốn** được tự động cộng 1 điểm thưởng uy tín khi đơn hàng của tôi hoàn tất giao dịch thành công,  
> **Để** nâng cao điểm tín nhiệm, mở khóa các quyền lợi bán hàng và phiên đấu giá trên sàn.

#### Acceptance Scenarios:
1. **Scenario 2.1: Hoàn tất toàn bộ đơn hàng qua `POST /api/v1/orders/{orderId}/complete`**
   - **GIVEN** Đơn hàng của Buyer có các đơn con (`SubOrders`) ở trạng thái đã giao hàng (`Delivered`).
   - **WHEN** Buyer gửi request `POST /api/v1/orders/{orderId}/complete`.
   - **THEN** Trạng thái `Order.Status` chuyển thành `OrderStatus.Completed`.
   - **AND** Tất cả SubOrder chuyển thành `SubOrderStatus.Completed` kèm giải ngân Escrow ví.
   - **AND** Với mỗi Seller trong đơn, CommerceService gọi `POST /api/v1/internal/users/reputation-adjust`:
     ```json
     {
       "userId": "<Seller_Id>",
       "targetRole": "Seller",
       "partnerUserId": "<Buyer_Id>",
       "scoreDelta": 1,
       "reason": "OrderCompleted",
       "referenceType": "Order",
       "referenceId": "ORD-YYYYMMDD-XXXXXX",
       "note": "Reward for successfully completed order ORD-YYYYMMDD-XXXXXX."
     }
     ```
   - **AND** Trả về HTTP 200 OK kèm thông tin đơn hàng đã hoàn tất.

2. **Scenario 2.2: Idempotency và Cooldown khi cộng điểm thưởng**
   - **GIVEN** Một request cộng điểm uy tín đã được gửi thành công cho mã đơn hàng `ORD-20261107-0091`.
   - **WHEN** Do mạng chập chờn, CommerceService gửi lại request cộng điểm cho cùng `referenceId`.
   - **THEN** UserService trả về `isDuplicate: true, appliedDelta: 0`.
   - **AND** CommerceService ghi log kiểm toán và không gặp lỗi ngoại lệ hệ thống.

3. **Scenario 2.3: Hoàn tất theo từng đơn hàng con (`confirm-receipt`)**
   - **GIVEN** Buyer gọi `POST /api/v1/sub-orders/{subOrderId}/confirm-receipt`.
   - **WHEN** SubOrder chuyển sang `Completed`.
   - **THEN** Hệ thống tự động kích hoạt cộng điểm uy tín cho Seller của SubOrder đó.
   - **AND** Nếu toàn bộ SubOrder của Order cha đều đã `Completed`, `Order.Status` tự động cập nhật `Completed`.

---

### User Story 3: Seller Violation Cancellation Penalties (Priority: P1)
> **Là một** Quản trị viên Sàn Giao dịch,  
> **Tôi muốn** hệ thống tự động ban hành chế tài trừ 5 điểm uy tín đối với Người bán tự ý hủy đơn hàng đã xác nhận mà không có lý do bất khả kháng,  
> **Để** đảm bảo tính nghiêm minh của chính sách sàn và bảo vệ quyền lợi người mua.

#### Acceptance Scenarios:
1. **Scenario 3.1: Người bán tự ý hủy đơn đã xác nhận (Vi phạm chính sách)**
   - **GIVEN** Đơn hàng con (`SubOrder`) đã ở trạng thái `Confirmed` hoặc `Paid`.
   - **WHEN** Người bán gửi yêu cầu hủy đơn hàng qua `POST /api/v1/orders/{orderId}/cancel` (hoặc `/reject`) với lý do không thuộc diện bất khả kháng (ví dụ: `CancellationWithoutReason`, hoặc lý do chủ quan từ shop).
   - **THEN** Trạng thái đơn chuyển sang `Cancelled`, kích hoạt hoàn tiền ví cho Buyer và nhả tồn kho.
   - **AND** CommerceService tự động gửi yêu cầu phạt điểm tới UserService:
     - Endpoint: `POST /api/v1/internal/users/penalties`
     - Request Body:
       ```json
       {
         "userId": "<Seller_Id>",
         "targetRole": "Seller",
         "violationType": "CancellationWithoutReason",
         "referenceType": "Order",
         "referenceId": "ORD-YYYYMMDD-XXXXXX",
         "reason": "Seller cancelled confirmed order ORD-YYYYMMDD-XXXXXX without acceptable explanation.",
         "customScoreDeduction": 5,
         "policyClauseCode": "POLICY-SEC-3.2"
       }
       ```
   - **AND** UserService trừ 5 điểm của Seller, trả về `appliedDelta: -5`.
   - **AND** Hệ thống ghi nhận log xử phạt trong `OrderCancellation`.

2. **Scenario 3.2: Người bán từ chối đơn hàng khi chưa xác nhận (Không phạt điểm)**
   - **GIVEN** Đơn hàng con đang ở trạng thái `PendingPayment` hoặc chưa được Seller tiếp nhận xác nhận.
   - **WHEN** Người bán từ chối đơn với lý do hợp lệ (hết hàng do lỗi hệ thống, sự cố bất khả kháng).
   - **THEN** Hệ thống hủy đơn, hoàn tiền nếu có, và KHÔNG gọi lệnh phạt điểm uy tín sang UserService.

---

## 3. Edge Cases & Defensive Invariants

1. **UserService Unavailability (Network Timeout / 5xx)**:
   - Khi gọi kiểm tra quyền mua (`permission-check`), nếu UserService bị timeout (quá 10s) hoặc sập, CommerceService áp dụng nguyên tắc phòng vệ an toàn (Fail-Closed cho giao dịch tài chính): Không tạo đơn hàng nếu chưa xác nhận được quyền mua hợp lệ, trả về HTTP 503 với mã lỗi `USER_SERVICE_UNAVAILABLE`.
2. **7-Day Anti-Farming Cooldown**:
   - Nếu cùng một Buyer và Seller thông đồng hoàn tất nhiều đơn hàng liên tục trong 7 ngày để cày điểm uy tín, UserService sẽ phản hồi `cooldownSuppressed: true, appliedDelta: 0`. CommerceService tiếp nhận phản hồi này an toàn mà không làm gián đoạn luồng hoàn tất đơn của khách hàng.
3. **Idempotency Protection on Retries**:
   - Các lệnh cộng điểm (`reputation-adjust`) và phạt điểm (`penalties`) bắt buộc truyền trường `referenceId` đồng nhất theo mã đơn hàng (`OrderNo` hoặc `SubOrderNo`). Khi có retry, UserService sẽ nhận diện trùng lặp (`isDuplicate = true`) và không cộng/trừ 2 lần.

---

## 4. Requirements

### Functional Requirements
- **FR-001**: Hệ thống BẮT BUỘC cung cấp Typed REST Client `IUserClient` kết nối tới `UserService` (:8001).
- **FR-002**: Hệ thống BẮT BUỘC thẩm định quyền mua của Buyer tại UserService trước khi thực thi giữ kho và trừ tiền ví trong `CreateOrderAsync`.
- **FR-003**: Hệ thống BẮT BUỘC trả về HTTP 403 `BUYER_ACTION_RESTRICTED` kèm thông điệp `"User has an active restriction prohibiting purchase actions."` khi Buyer bị cấm mua hàng.
- **FR-004**: Hệ thống BẮT BUỘC cung cấp endpoint RESTful `POST /api/v1/orders/{orderId}/complete` để người mua xác nhận hoàn tất toàn bộ đơn hàng.
- **FR-005**: Khi đơn hàng hoặc đơn con chuyển sang trạng thái `Completed`, hệ thống BẮT BUỘC gửi request cộng 1 điểm uy tín đối tác (`INT-07`) tới UserService.
- **FR-006**: Hệ thống BẮT BUỘC mở rộng endpoint `POST /api/v1/orders/{orderId}/cancel` cho phép Người bán hủy đơn hàng/đơn con thuộc quyền sở hữu của mình.
- **FR-007**: Khi Người bán hủy đơn hàng đã xác nhận mà không có lý do bất khả kháng, hệ thống BẮT BUỘC tự động gửi request chế tài trừ 5 điểm uy tín (`INT-06`) tới UserService với mã điều khoản `POLICY-SEC-3.2`.
- **FR-008**: Bổ sung giá trị `Completed = 5` vào enum `OrderStatus` và `CancellationWithoutReason = 7` vào enum `CancellationReasonCode`.
- **FR-009**: Hệ thống BẮT BUỘC gửi kèm header `X-Internal-Service-Key` và lan truyền định danh `x-user-id` trong mọi cuộc gọi nội bộ sang UserService.

---

## 5. Key Entities & Data Contracts

### 5.1. Tầng Application Interface (`Commerce.Application/Interfaces/IUserClient.cs`)
```csharp
namespace Commerce.Application.Interfaces;

public interface IUserClient
{
    Task<UserPermissionCheckResult> CheckBuyerPermissionAsync(
        Guid buyerId,
        string action = "buy",
        CancellationToken ct = default);

    Task<ReputationAdjustResponse?> AdjustReputationAsync(
        ReputationAdjustRequest request,
        CancellationToken ct = default);

    Task<UserPenaltyResponse?> CreatePenaltyAsync(
        UserPenaltyRequest request,
        CancellationToken ct = default);
}
```

### 5.2. Data Transfer Objects (DTOs)
```csharp
public sealed record UserPermissionCheckResult(
    bool IsAllowed,
    string? ErrorCode,
    string? Message);

public sealed record ReputationAdjustRequest(
    Guid UserId,
    string TargetRole,
    Guid PartnerUserId,
    int ScoreDelta,
    string Reason,
    string ReferenceType,
    string ReferenceId,
    string Note);

public sealed record ReputationAdjustResponse(
    Guid UserId,
    string TargetRole,
    bool IsDuplicate,
    bool CooldownSuppressed,
    int PreviousScore,
    int NewScore,
    int AppliedDelta,
    string NewTrustLevel);

public sealed record UserPenaltyRequest(
    Guid UserId,
    string TargetRole,
    string ViolationType,
    string ReferenceType,
    string ReferenceId,
    string Reason,
    int CustomScoreDeduction,
    string PolicyClauseCode);

public sealed record UserPenaltyResponse(
    Guid UserId,
    string TargetRole,
    bool IsDuplicate,
    Guid? PenaltyId,
    int PreviousScore,
    int NewScore,
    int AppliedDelta,
    string NewTrustLevel);
```

---

## 6. Success Criteria (Measurable Outcomes)

- **SC-001**: 100% người mua có tài khoản bị hạn chế hoặc bị khóa đều bị chặn tại `POST /api/v1/orders` với mã lỗi `BUYER_ACTION_RESTRICTED` mà không bị giam tồn kho hay trừ tiền ví.
- **SC-002**: 100% đơn hàng hoàn tất thành công đều phát sinh request cộng điểm uy tín tới `UserService` với độ trễ xử lý tăng thêm dưới 150ms.
- **SC-003**: 100% các vi phạm người bán hủy đơn vô cớ đã xác nhận đều được ban hành chế tài trừ 5 điểm uy tín tự động tới UserService.
- **SC-004**: Giải pháp biên dịch thành công 100% (`0 warnings, 0 errors`) với lệnh `dotnet build Commerce.sln -c Release --no-restore -m:1 /p:UseSharedCompilation=false`.
- **SC-005**: 100% Unit Tests (bao gồm các test case mới cho `UserClient` và `OrderService`) chạy thành công.

---

## 7. Constitution & Architecture Checklist

| Rào chắn bất biến | Trạng thái tuân thủ | Bằng chứng đối chiếu |
| :--- | :---: | :--- |
| **No-Repository Pattern** | **TUÂN THỦ** | Sử dụng trực tiếp `ICommerceDbContext` và `DbSet<T>`, không tạo Repository bọc ngoài. |
| **100% RESTful & Zero Saga** | **TUÂN THỦ** | Toàn bộ giao tiếp sử dụng Typed `HttpClient`, không sử dụng RabbitMQ hay MassTransit. |
| **Universal Error Protocol** | **TUÂN THỦ** | 100% English Sentence-case message: `"User has an active restriction prohibiting purchase actions."`. Mã lỗi `BUYER_ACTION_RESTRICTED`. |
| **Inward-Only Scope** | **TUÂN THỦ** | 100% tự cung tự cấp bên trong `CommerceService`, không tham chiếu thư mục ngoài repo. |
| **Port & DB Invariants** | **TUÂN THỦ** | Giữ nguyên Port `8003`, Database `commerce_db:5435`. |
