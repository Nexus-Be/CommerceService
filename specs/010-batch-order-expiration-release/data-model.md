# Data Model & DTO Specifications: Phương án 3 (The Golden Standard)

**Feature**: `specs/010-batch-order-expiration-release`  
**Date**: 2026-11-03  

---

## 1. DTOs Giao Tiếp Sang Fulfillment Service (Outbound)

### 1.1. Batch Check Tồn Kho (`POST /api/v1/inventories/by-skus`)
- **Request Payload:** `IReadOnlyList<Guid>` (Mảng JSON trần)
  ```json
  [
    "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
    "e3a1f802-5c8e-4b69-8d62-7f91c98a5e12"
  ]
  ```
- **Response Payload:** `IReadOnlyList<InventoryItemResponse>`
  ```json
  [
    {
      "id": "a1b2c3d4-0000-0000-0000-000000000001",
      "skuId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
      "warehouseId": "c4d5e6f7-0000-0000-0000-000000000001",
      "totalQuantity": 20,
      "reservedQuantity": 5,
      "availableQuantity": 15,
      "updatedAt": "2026-11-03T10:00:00Z"
    }
  ]
  ```

---

### 1.2. Khóa Giữ Tồn Kho (`POST /api/v1/internal/inventories/reservations`)
- **Headers:** `X-Internal-Service-Key: eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e`
- **Request Payload:**
  ```json
  {
    "referenceId": "b1a9e7bc-6b84-46d5-9e17-092347640841",
    "holdMinutes": 15,
    "items": [
      {
        "skuId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
        "quantity": 2
      }
    ]
  }
  ```
- **Response Payload (`200 OK`):**
  ```json
  {
    "id": "77777777-1111-2222-3333-444444444444",
    "referenceId": "b1a9e7bc-6b84-46d5-9e17-092347640841",
    "expiresAt": "2026-11-03T10:15:00Z",
    "createdAt": "2026-11-03T10:00:00Z",
    "items": [
      {
        "skuId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
        "quantity": 2
      }
    ]
  }
  ```

---

### 1.3. Trừ Kho Vĩnh Viễn (`POST /api/v1/internal/inventories/reservations/{orderId}/commit`)
- **Headers:** `X-Internal-Service-Key: eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e`
- **Path Param:** `orderId` (`Guid` $\rightarrow$ `string`)
- **Request Body:** Rỗng
- **Response Payload (`200 OK`):** `InventoryReservationResponse`

---

### 1.4. Nhả Kho Đơn Lẻ (`POST /api/v1/internal/inventories/reservations/{orderId}/release?reason=...`)
- **Headers:** `X-Internal-Service-Key: eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e`
- **Path Param:** `orderId`
- **Query Param:** `reason` (ví dụ: `OrderCancelled`)
- **Request Body:** Rỗng
- **Response Payload (`200 OK`):** `InventoryReservationResponse`

---

## 2. DTO Nhận Callback Trạng Thái Giao Hàng (Inbound)

### `POST /api/v1/sub-orders/{subOrderId}/delivery-status`
- **Headers:** `X-Internal-Service-Key: eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e`
- **Request Payload:**
  ```json
  {
    "status": "Delivered",
    "deliveredAt": "2026-11-03T16:00:00Z"
  }
  ```
- **Response Payload (`200 OK`):**
  ```json
  {
    "subOrderId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
    "status": "Delivered",
    "updatedAt": "2026-11-03T16:00:05Z"
  }
  ```

---

## 3. Vòng Đời Trạng Thái Đơn Hàng (State Transitions)

```
[Khởi tạo Đơn]
  │
  ▼
PendingPayment (Hạn 15 phút, Kho: Reserved)
  │
  ├─(Khách trả tiền trước 15m)──► Paid (Commit kho thành công) ──► Confirmed ──► Shipped ──► Delivered
  │
  ├─(Khách bấm Hủy đơn)──────────► Cancelled (Commerce gọi Release kho)
  │
  └─(Quá 15m không trả tiền)─────► Cancelled (Commerce đổi status DB; Fulfillment tự nhả kho độc lập)
```
