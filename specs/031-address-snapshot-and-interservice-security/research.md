# Technical Research: Address Snapshot Parity, Checkout Address Integration & S2S Internal Security

**Feature**: `031-address-snapshot-and-interservice-security`  
**Date**: 2026-11-19

---

## 1. Tích Hợp Sổ Địa Chỉ Từ UserService (`IUserClient`)

### Bối Cảnh & Vấn Đề
Trước đây, khách hàng gọi `POST /api/v1/orders/quote` hay `POST /api/v1/orders` phải gửi trọn vẹn đối tượng `ShippingAddressInput` (chứa `ReceiverName`, `ContactPhone`, `Street`, `Ward`, `District`, `Province`, `GhnDistrictId`, `GhnWardCode`). Điều này buộc Frontend phải tự cache và mapping mã vùng GHN. Nếu khách hàng chọn một địa chỉ đã lưu trong sổ địa chỉ của tài khoản (quản lý bởi `UserService`), CommerceService chưa có API S2S để nạp địa chỉ này.

### Thiết Kế DTO & Interface
Thêm vào `Commerce.Application/Interfaces/UserClientModels.cs`:
```csharp
public sealed record BuyerAddressDto(
    Guid Id,
    string RecipientName,
    string Phone,
    string Street,
    string Ward,
    string District,
    string Province,
    int GhnDistrictId,
    string GhnWardCode,
    string FullAddressText,
    bool IsDefault
);
```

Bổ sung vào `IUserClient.cs`:
```csharp
Task<BuyerAddressDto?> GetBuyerAddressAsync(Guid buyerId, Guid addressId, CancellationToken ct = default);
Task<BuyerAddressDto?> GetBuyerDefaultAddressAsync(Guid buyerId, CancellationToken ct = default);
```

Triển khai trong `UserClient.cs`:
- Gọi `GET /api/v1/internal/users/{buyerId}/addresses/{addressId}`
- Gọi `GET /api/v1/internal/users/{buyerId}/addresses/default`
- Header: `X-Internal-Service-Key` lấy từ cấu hình.
- Xử lý 404: Trả về `null` thay vì ném exception, giúp tầng Application linh hoạt fallback.
- Xử lý 503/Timeout: Áp dụng Polly policy retry 3 lần với exponential backoff.

---

## 2. Quy Trình Phân Giải Địa Chỉ (Address Resolution Algorithm)

### Mục Tiêu
Chuẩn hóa logic phân giải địa chỉ nhận hàng để dùng chung cho cả Báo giá Checkout (`QuoteCheckoutAsync`), Đặt hàng (`CreateOrderAsync`), Tính lại cước (`RecalculateQuoteAsync`) và Đơn đấu giá (`SubmitShippingAsync`).

### Thuật Toán Phân Giải
```csharp
public static async Task<ShippingAddressSnapshot> ResolveShippingAddressAsync(
    IUserClient userClient,
    Guid buyerId,
    Guid? addressId,
    ShippingAddressInput? rawAddress,
    string? contactPhone,
    CancellationToken ct)
{
    // Bước 1: Ưu tiên AddressId nếu được truyền
    if (addressId.HasValue && addressId.Value != Guid.Empty)
    {
        var address = await userClient.GetBuyerAddressAsync(buyerId, addressId.Value, ct);
        if (address == null)
        {
            throw new CommerceDomainException("ADDRESS_NOT_FOUND", "The specified shipping address was not found.", 404);
        }
        return MapFromBuyerAddress(address);
    }

    // Bước 2: Nếu không truyền cả AddressId lẫn rawAddress -> Tự động fallback địa chỉ mặc định
    if (rawAddress == null)
    {
        var defaultAddress = await userClient.GetBuyerDefaultAddressAsync(buyerId, ct);
        if (defaultAddress == null)
        {
            throw new CommerceDomainException("ADDRESS_REQUIRED", "No shipping address provided and no default address found.", 400);
        }
        return MapFromBuyerAddress(defaultAddress);
    }

    // Bước 3: Nếu truyền rawAddress trực tiếp -> Validate mã vùng GHN bắt buộc
    if (rawAddress.GhnDistrictId <= 0 || string.IsNullOrWhiteSpace(rawAddress.GhnWardCode))
    {
        throw new CommerceDomainException("VAL_INVALID_INPUT", "Shipping address must contain valid GHN district and ward codes.", 400);
    }

    return new ShippingAddressSnapshot(
        receiverName: rawAddress.RecipientName,
        contactPhone: !string.IsNullOrWhiteSpace(contactPhone) ? contactPhone : rawAddress.Phone,
        street: rawAddress.Street,
        ward: rawAddress.Ward,
        district: rawAddress.District,
        province: rawAddress.Province,
        ghnDistrictId: rawAddress.GhnDistrictId,
        ghnWardCode: rawAddress.GhnWardCode
    );
}
```

---

## 3. Re-Quote Phí Ship GHN Khi Đổi Địa Chỉ Trên Trang Checkout

### Endpoint
`POST /api/v1/checkouts/quote/{quoteId:guid}/recalculate`

### Luồng Xử Lý
1. Trích xuất `currentUserId` từ claims.
2. Đọc `CachedQuote` từ `IDistributedCache` qua key `quote:{quoteId}`. Nếu không tìm thấy $\rightarrow$ ném HTTP 404 `QUOTE_NOT_FOUND`.
3. Kiểm tra tính sở hữu: `cachedQuote.BuyerId == currentUserId`. Nếu không khớp $\rightarrow$ ném HTTP 403 `FORBIDDEN`.
4. Phân giải địa chỉ mới qua `ResolveShippingAddressAsync`.
5. Với mỗi `ShopQuote` trong đơn hàng đa người bán:
   - Gọi `_fulfillmentClient.CalculateShopShippingFeeAsync(shop.FromDistrictId, newAddress.GhnDistrictId, newAddress.GhnWardCode, shop.WeightInGrams)`.
   - Cập nhật lại `ShippingFeeAmount` mới của từng Shop.
6. Tính toán lại voucher (nếu có voucher Freeship hoặc giảm giá theo phí ship).
7. Lưu lại `CachedQuote` đã cập nhật vào cache với TTL 15 phút.
8. Trả về `CheckoutQuoteResponse` mới cho Frontend hiển thị.

---

## 4. Internal API Xác Minh Đơn Hàng Cho Dispute / Report (`UserService`)

### Endpoint
`GET /api/v1/internal/orders/dispute-eligibility`

### Query Parameters
- `orderOrSubOrderId` (Guid, required)
- `reporterId` (Guid, required)
- `reportedUserId` (Guid, required)

### Bảo Mật
- Gắn `[AllowAnonymous]`.
- Kiểm tra `InternalServiceKeyGuard.Check(Request, configuration)`. Nếu không hợp lệ $\rightarrow$ HTTP 401 `INTERNAL_KEY_INVALID`.

### Logic Xác Minh
1. Tra cứu `SubOrders` theo `Id == orderOrSubOrderId`:
   - Khớp quan hệ: `(so.BuyerId == reporterId && so.SellerId == reportedUserId) || (so.BuyerId == reportedUserId && so.SellerId == reporterId)`.
   - Khớp trạng thái: `so.Status == SubOrderStatus.Delivered || so.Status == SubOrderStatus.Completed`.
2. Nếu không có trong `SubOrders`, tra cứu `Orders` theo `Id == orderOrSubOrderId`:
   - Duyệt các `SubOrders` con để kiểm tra quan hệ `BuyerId` và `SellerId`.
3. Nếu không có trong `Orders`, tra cứu `AuctionOrders`:
   - Khớp quan hệ `BuyerId` và `SellerId`, trạng thái `Delivered` hoặc `Completed`.
4. Nếu tìm thấy: trả về HTTP 200 `DisputeEligibilityDto`:
   ```csharp
   public sealed record DisputeEligibilityDto(
       bool IsEligible,
       string OrderType,
       Guid BuyerId,
       Guid SellerId,
       string Status,
       DateTimeOffset? DeliveredAt
   );
   ```
5. Nếu không tìm thấy bất kỳ đơn nào với ID đó $\rightarrow$ HTTP 404 `ORDER_NOT_FOUND`.

---

## 5. Đồng Bộ Background Workers

### `AuctionCallbackRetryWorker.cs`
- Thay thế đoạn khởi tạo thủ công `new AuctionPaymentStatusCallbackRequest(...)` vốn thiếu `OrderNo`, `TotalAmount`, `SellerRespondDeadlineAt`, `OrderSource` bằng:
  ```csharp
  var callbackReq = AuctionPaymentCallbackFactory.Build(order, history);
  ```
- Đảm bảo tính idempotent và khớp payload 100% giữa lần gửi trực tiếp và các lần retry tự động.

### `PaymentTimeoutWorker.cs`
- Khối xử lý phạt seller (`SellerExpired`): Đảm bảo chỉ gọi `_userClient.CreatePenaltyAsync` một lần duy nhất với `UserPenaltyViolationTypes.SellerResponseTimeout`, tránh spam bản ghi vi phạm.
