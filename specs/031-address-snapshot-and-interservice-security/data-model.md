# Data Model Specification: Address Snapshot Parity, Checkout Address Integration & S2S Internal Security

**Feature**: `031-address-snapshot-and-interservice-security`  
**Date**: 2026-11-19

---

## 1. Entities & Existing Schema Parity

Toàn bộ các thực thể chính (`Orders`, `SubOrders`, `AuctionOrders`, `Carts`, `CartItems`) đã có cấu trúc chuẩn. Không cần thêm migration database mới cho SPEC-031 vì các trường địa chỉ đều đã được lưu trữ dưới dạng JSON snapshot `ShippingAddressSnapshot` trong `SubOrders.ShippingAddress` và `AuctionOrders.ShippingAddress`.

### Existing ShippingAddressSnapshot Struct
```csharp
public sealed record ShippingAddressSnapshot(
    string ReceiverName,
    string ContactPhone,
    string Street,
    string Ward,
    string District,
    string Province,
    int? GhnDistrictId = null,
    string? GhnWardCode = null
)
{
    public string FullAddressText => string.Join(", ", new[] { Street, Ward, District, Province }.Where(s => !string.IsNullOrWhiteSpace(s)));
}
```

---

## 2. New & Updated Data Transfer Objects (DTOs)

### 2.1. `BuyerAddressDto` (Nhận từ UserService qua S2S)
Định nghĩa tại `Commerce.Application/Interfaces/UserClientModels.cs`:
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

### 2.2. Updated `CheckoutQuoteRequest`
Định nghĩa tại `Commerce.Application/Services/Order/OrderRequests.cs`:
```csharp
public sealed record CheckoutQuoteRequest(
    IReadOnlyList<Guid> CartItemIds,
    ShippingAddressInput? ShippingAddress = null,
    string? ContactPhone = null,
    IReadOnlyList<string>? VoucherCodes = null,
    Guid? AddressId = null
);
```

### 2.3. Updated `SubmitAuctionShippingRequest`
Định nghĩa tại `Commerce.Application/Services/AuctionOrder/AuctionOrderRequests.cs`:
```csharp
public sealed record SubmitAuctionShippingRequest(
    string? ShippingAddress,
    string? ContactPhone,
    int? GhnDistrictId = null,
    string? GhnWardCode = null,
    Guid? AddressId = null
);
```

### 2.4. `RecalculateQuoteRequest` (Mới)
Định nghĩa tại `Commerce.Application/Services/Order/OrderRequests.cs`:
```csharp
public sealed record RecalculateQuoteRequest(
    Guid? AddressId = null,
    ShippingAddressInput? ShippingAddress = null,
    string? ContactPhone = null
);
```

### 2.5. `DisputeEligibilityDto` (Mới)
Định nghĩa tại `Commerce.Application/Services/Order/DisputeEligibilityDto.cs`:
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

---

## 3. Validation & Business Rule Matrix

| DTO / Trường | Ràng Buộc | Mã Lỗi Khi Vi Phạm | Diễn Giải Lỗi |
|---|---|---|---|
| `AddressId` | Phải là UUID hợp lệ và tồn tại trong `UserService` | `ADDRESS_NOT_FOUND` (404) | "The specified shipping address was not found." |
| `Address Resolution` | Phải resolve được địa chỉ (qua `AddressId`, `Default`, hoặc `Raw`) | `ADDRESS_REQUIRED` (400) | "No shipping address provided and no default address found." |
| `rawAddress` | `GhnDistrictId > 0` và `GhnWardCode` không rỗng | `VAL_INVALID_INPUT` (400) | "Shipping address must contain valid GHN district and ward codes." |
| `RecalculateQuote` | `quoteId` phải tồn tại trong Distributed Cache | `QUOTE_NOT_FOUND` (404) | "The checkout quote has expired or does not exist." |
| `RecalculateQuote` | `currentUserId` phải là chủ sở hữu của báo giá | `FORBIDDEN` (403) | "You do not have permission to modify this quote." |
| `DisputeEligibility` | Phải có header `X-Internal-Service-Key` hợp lệ | `INTERNAL_KEY_INVALID` (401) | "Internal service key is missing or invalid." |
| `DisputeEligibility` | `orderOrSubOrderId` phải tồn tại trong hệ thống | `ORDER_NOT_FOUND` (404) | "Order with ID '{id}' was not found." |
