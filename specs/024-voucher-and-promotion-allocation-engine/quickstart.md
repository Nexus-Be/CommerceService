# Quickstart Guide: Voucher & Promotion Allocation Engine (Feature 024)

**Feature**: `024-voucher-and-promotion-allocation-engine` | **Target Service**: `CommerceService` (`:8003`)

---

## 1. Prerequisites & Environment Setup

- **Runtime**: .NET 8.0 SDK
- **Database**: PostgreSQL 16 on `localhost:5435` (`commerce_db`)
- **Port**: `8003` (Container) or `5105` (Local HTTP)
- **Zero Schema Drift**: No new database migrations are required; uses existing tables (`DiscountCodes`, `DiscountCodeScopes`, `DiscountCodeUsages`).

### Build & Run Unit Tests
```powershell
# 1. Clean release build (must be 0 errors, 0 warnings)
dotnet build Commerce.sln -c Release -m:1 /p:UseSharedCompilation=false

# 2. Run test suite
dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj -c Release --no-build
```

---

## 2. Seed Data Reference

The system includes pre-seeded vouchers in `CommerceSeedData.cs`:
- **Code**: `WELCOME2026`
- **Scope**: `System` (Global)
- **Type**: `FixedAmount` ($50,000đ$)
- **MinOrderAmount**: $200,000đ$
- **MaxUsesTotal**: 10,000
- **Status**: `Active`

---

## 3. End-to-End API Walkthrough

### Step 1: Admin Creates a System-Wide Voucher (`UC-04`)
```bash
curl -X POST "http://localhost:8003/api/v1/discounts" \
  -H "Authorization: Bearer {ADMIN_JWT}" \
  -H "Content-Type: application/json" \
  -d '{
    "code": "ALLSALL",
    "name": "Giảm 10% toàn sàn đơn từ 1 triệu",
    "scopeType": "System",
    "discountType": "Percentage",
    "discountValue": 10,
    "minOrderAmount": 1000000,
    "maxDiscountAmount": 100000,
    "maxUsesTotal": 5000,
    "maxUsesPerUser": 1,
    "isVisible": true
  }'
```

### Step 2: Seller Creates a Shop Voucher (`UC-04`)
*(System calls `_userClient.CheckBuyerPermissionAsync(sellerId, "manage_voucher")` to verify seller standing)*
```bash
curl -X POST "http://localhost:8003/api/v1/discounts" \
  -H "Authorization: Bearer {SELLER_JWT}" \
  -H "Content-Type: application/json" \
  -d '{
    "code": "SHOPA50",
    "name": "Giảm 50k cho đơn từ 700k",
    "scopeType": "Shop",
    "discountType": "FixedAmount",
    "discountValue": 50000,
    "minOrderAmount": 700000,
    "maxUsesTotal": 1000,
    "maxUsesPerUser": 1,
    "isVisible": true
  }'
```

### Step 3: Buyer Queries Available Vouchers (`UC-01`)
```bash
curl -X GET "http://localhost:8003/api/v1/discounts/available?cartItemIds=3fa85f64-5717-4562-b3fc-2c963f66afa6&page=1&pageSize=10" \
  -H "Authorization: Bearer {BUYER_JWT}"
```

### Step 4: Buyer Validates a Manual Code (`UC-02`)
```bash
curl -X POST "http://localhost:8003/api/v1/discounts/validate" \
  -H "Authorization: Bearer {BUYER_JWT}" \
  -H "Content-Type: application/json" \
  -d '{
    "code": "SHOPA50",
    "cartItemIds": ["3fa85f64-5717-4562-b3fc-2c963f66afa6"]
  }'
```

### Step 5: Buyer Requests Optimal Voucher Recommendation (`UC-03`)
```bash
curl -X POST "http://localhost:8003/api/v1/discounts/recommend" \
  -H "Authorization: Bearer {BUYER_JWT}" \
  -H "Content-Type: application/json" \
  -d '{
    "cartItemIds": [
      "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "4fa85f64-5717-4562-b3fc-2c963f66afa7"
    ]
  }'
```

### Step 6: Checkout Quote Preview with Stacked Vouchers (`UC-10`)
```bash
curl -X POST "http://localhost:8003/api/v1/orders/quote" \
  -H "Authorization: Bearer {BUYER_JWT}" \
  -H "Content-Type: application/json" \
  -d '{
    "cartItemIds": [
      "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "4fa85f64-5717-4562-b3fc-2c963f66afa7"
    ],
    "voucherCodes": ["JEAN20", "SHOPA50", "ALLSALL"],
    "shippingAddress": {
      "receiverName": "Nguyen Van A",
      "contactPhone": "0912345678",
      "street": "123 Nguyen Hue",
      "ward": "Ben Nghe",
      "district": "Quan 1",
      "province": "TP Ho Chi Minh",
      "ghnDistrictId": 1442,
      "ghnWardCode": "20101"
    }
  }'
```

### Step 7: Order Creation & Voucher Reservation (`UC-11`)
```bash
curl -X POST "http://localhost:8003/api/v1/orders" \
  -H "Authorization: Bearer {BUYER_JWT}" \
  -H "Content-Type: application/json" \
  -d '{
    "quoteId": "{QUOTE_ID_FROM_STEP_6}",
    "paymentMethod": "Wallet",
    "shippingAddress": "123 Nguyen Hue, Ben Nghe, Quan 1, TP Ho Chi Minh",
    "contactPhone": "0912345678"
  }'
```

---

## 4. Key Financial Invariants to Verify (Shopee / TikTok Shop Model)

1. **VND Integer Rounding**: No financial amount contains decimals.
2. **Buyer Transparency**:
   $$\text{TotalAmount} = \text{ItemsAmount} - \text{DiscountAmount} + \text{ShippingFeeAmount}$$
3. **Seller Payout After Commission**:
   $$\text{SellerReceiveAmount} = (\text{ItemsAmount} - \text{ProductDiscount} - \text{ShopDiscount}) - \text{PlatformFeeAmount}$$
4. **Platform Commission & Subsidy**:
   $$\text{NetPlatformRetained} = \text{PlatformFeeAmount} - \text{SystemDiscountAmount}$$
5. **Penny Rounding Conservation**:
   $$\sum \text{ShopSystemDiscounts} \equiv \text{MasterSystemDiscount}$$
