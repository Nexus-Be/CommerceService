# Feature Specification: Cart Service (Quản lý Giỏ hàng)

**Feature Directory**: `specs/001-cart-service`  
**Feature Branch**: `feat/cart-service-v2`  
**Created**: 2026-10-29  
**Updated**: 2026-10-31  
**Status**: Completed (Phase 1 & Phase 2 Ready)  
**Input**: Triển khai trọn bộ REST API Giỏ hàng (Cart Service) gồm CART-01 đến CART-05 và mở rộng CART-06, CART-08, CART-09, CART-10 theo api_catalog.md và SRS_Commerce_Service_v1.md.  

---

## 1. User Scenarios & Testing *(mandatory)*

### User Story 1 - Xem Giỏ Hàng Của Tôi (Priority: P1)
Là một người mua (Buyer) đã đăng nhập hệ thống, tôi muốn xem chi tiết giỏ hàng hiện tại của mình bao gồm các sản phẩm đã chọn, số lượng, đơn giá snapshot và tổng tiền tạm tính, để tôi có thể chuẩn bị tiến hành đặt hàng.

* **Independent Test**: Gửi request `GET /api/v1/carts/me` kèm header `X-User-Id`. Hệ thống trả về `200 OK` kèm giỏ hàng `ACTIVE`. Nếu chưa có giỏ nào, hệ thống tự động khởi tạo 1 giỏ rỗng mới và trả về.
* **Acceptance Scenarios**:
  1. **Given** Người mua đã đăng nhập và có 1 giỏ `ACTIVE` với 2 sản phẩm, **When** gọi `GET /api/v1/carts/me`, **Then** trả về mã `200 OK` kèm danh sách 2 sản phẩm, `item_count = 2`, và `subtotal_amount` bằng tổng tiền các dòng.
  2. **Given** Người mua mới tinh chưa từng có giỏ hàng nào, **When** gọi `GET /api/v1/carts/me`, **Then** hệ thống tự tạo một giỏ mới `ACTIVE`, trả về mã `200 OK` với danh sách items rỗng, `item_count = 0`, `subtotal_amount = 0`.
  3. **Given** Request không mang header `X-User-Id`, **When** gọi `GET /api/v1/carts/me`, **Then** trả về mã `401 Unauthorized` kèm Problem Details tiếng Anh.

---

### User Story 2 - Thêm Sản Phẩm Vào Giỏ Hàng (Priority: P1)
Là một người mua, tôi muốn thêm một mặt hàng (SKU) từ trang chi tiết sản phẩm vào giỏ hàng với số lượng mong muốn. Nếu mặt hàng này đã có trong giỏ, hệ thống phải tự động cộng dồn số lượng.

* **Independent Test**: Gửi `POST /api/v1/carts/me/items` với `{ skuId, quantity }`. Hệ thống gọi `ICatalogClient` xác thực SKU/giá, lưu snapshot, cập nhật tổng giỏ và trả về `200 OK`.
* **Acceptance Scenarios**:
  1. **Given** SKU hợp lệ từ Catalog và chưa có trong giỏ, **When** người mua thêm với `quantity = 2`, **Then** hệ thống tạo 1 dòng `cart_items` mới, lưu `unit_price_snapshot`, `product_name_snapshot`, tăng `item_count` lên 1, cộng `subtotal_amount`.
  2. **Given** SKU đã có sẵn trong giỏ với `quantity = 2`, **When** người mua thêm tiếp SKU đó với `quantity = 3`, **Then** hệ thống không tạo dòng mới mà cộng dồn `quantity = 5`, cập nhật `subtotal_amount`, giữ nguyên `item_count`.
  3. **Given** Người mua thêm sản phẩm với tổng số lượng vượt quá `MAX_QTY_PER_ITEM (99)`, **When** gửi request, **Then** trả về `400 Bad Request` với mã lỗi `CART_ITEM_LIMIT_EXCEEDED`.
  4. **Given** Giỏ đã đạt tối đa `MAX_CART_ITEMS (50 dòng)`, **When** người mua thêm 1 SKU mới tinh khác, **Then** trả về `400 Bad Request` với mã lỗi `CART_LIMIT_EXCEEDED`.
  5. **Given** SKU không tồn tại trong Catalog hoặc đã bị ẩn (Inactive/OutOfStock), **When** người mua thêm vào giỏ, **Then** trả về `400 Bad Request` với mã lỗi `CATALOG_SKU_NOT_FOUND` hoặc `CATALOG_SKU_INACTIVE`.

---

### User Story 3 - Cập Nhật Số Lượng Sản Phẩm Trong Giỏ (Priority: P1)
Là một người mua, tôi muốn thay đổi số lượng của một mặt hàng đã có trong giỏ (tăng hoặc giảm số lượng) và thấy tổng tiền giỏ hàng được tính toán lại ngay lập tức.

* **Independent Test**: Gửi `PATCH /api/v1/carts/me/items/{cartItemId}` với `{ quantity }`. Hệ thống kiểm tra số lượng hợp lệ, tính lại `subtotal_amount` và trả về giỏ hàng đã cập nhật.
* **Acceptance Scenarios**:
  1. **Given** Dòng sản phẩm `cartItemId` tồn tại và thuộc giỏ của buyer, **When** gửi `quantity = 5`, **Then** cập nhật số lượng thành 5, tính lại `subtotal_amount`, tăng `row_version`.
  2. **Given** Request gửi `quantity <= 0`, **When** gửi request, **Then** trả về `400 Bad Request` với mã lỗi `VAL_INVALID_INPUT`.
  3. **Given** `cartItemId` không tồn tại hoặc thuộc về giỏ của người khác, **When** gửi request, **Then** trả về `404 Not Found` với mã lỗi `CART_ITEM_NOT_FOUND`.

---

### User Story 4 - Xóa Một Sản Phẩm Khỏi Giỏ (Priority: P2)
Là một người mua, tôi muốn loại bỏ một mặt hàng không còn muốn mua ra khỏi giỏ.

* **Independent Test**: Gửi `DELETE /api/v1/carts/me/items/{cartItemId}`. Hệ thống xóa dòng đó, giảm `item_count`, trừ `subtotal_amount`, trả về `200 OK`.
* **Acceptance Scenarios**:
  1. **Given** Dòng `cartItemId` có trong giỏ, **When** gọi xóa, **Then** dòng sản phẩm bị xóa khỏi DB, `item_count` giảm 1, `subtotal_amount` giảm tương ứng, trả về `200 OK`.
  2. **Given** `cartItemId` không tồn tại trong giỏ của buyer, **When** gọi xóa, **Then** trả về `404 Not Found` với mã lỗi `CART_ITEM_NOT_FOUND`.

---

### User Story 5 - Xóa Nhiều Dòng Hoặc Làm Rỗng Giỏ Hàng (Priority: P2)
Là một người mua, tôi muốn xóa nhiều món cùng lúc hoặc làm trống toàn bộ giỏ hàng của mình chỉ trong 1 thao tác.

* **Independent Test**: Gửi `POST /api/v1/carts/me/items:bulk-remove` với danh sách `cartItemIds: [...]` hoặc `DELETE /api/v1/carts/me/clear`. Hệ thống dọn các dòng được chọn và cập nhật lại giỏ.

---

### User Story 6 - Xem Tóm Tắt Giỏ Hàng & Badge Count (CART-08 - Priority: P1)
Là một người dùng duyệt trang web/app, tôi muốn hệ thống cung cấp nhanh tổng số lượng món hàng trong giỏ để hiển thị lên icon giỏ hàng trên thanh Header mà không phải tải toàn bộ thông tin chi tiết nặng nề của giỏ hàng.

* **Independent Test**: Gửi `GET /api/v1/carts/me/summary` kèm `X-User-Id`.
* **Acceptance Scenarios**:
  1. **Given** Giỏ hàng có 3 loại món, tổng số lượng là 7 chiếc, **When** gọi tóm tắt, **Then** trả về `{ total_distinct_items: 3, total_quantity: 7, estimated_subtotal: ... }`.
  2. **Given** Người dùng chưa có giỏ hoặc giỏ trống, **When** gọi tóm tắt, **Then** trả về số lượng 0 và subtotal 0.

---

### User Story 7 - Tính Toán Tạm Tính Các Món Được Tick Chọn (CART-09 - Shopee Style - Priority: P1)
Là một người mua tại trang giỏ hàng, khi tôi tick chọn một hoặc một số món đồ muốn mua, tôi muốn hệ thống tính toán ngay tổng tiền tạm tính và số lượng của các món được chọn để chuẩn bị sang bước thanh toán.

* **Independent Test**: Gửi `POST /api/v1/carts/me/calculate` với danh sách `selectedItemIds: [...]`.
* **Acceptance Scenarios**:
  1. **Given** Danh sách các `cartItemId` hợp lệ, **When** gửi request tính toán, **Then** hệ thống trả về tổng tiền tạm tính chính xác của các món được chọn kèm chi tiết line-total của từng món.
  2. **Given** Danh sách rỗng, **When** gửi request, **Then** trả về tổng tiền 0 và số lượng 0.

---

### User Story 8 - Pre-Checkout Validation (CART-06 - Priority: P1)
Là một người mua chuẩn bị bấm nút Mua Hàng, hệ thống phải tự động kiểm tra lại tính hợp lệ của các mặt hàng được chọn xem còn hàng không, có bị đổi giá không.

* **Independent Test**: Gửi `POST /api/v1/carts/me/validate` với `{ selectedItemIds }`.
* **Acceptance Scenarios**:
  1. **Given** Sản phẩm hết hàng hoặc ngừng kinh doanh, **Then** trả về `isValid: false`, `hasStockIssues: true` kèm danh sách chi tiết lỗi.
  2. **Given** Sản phẩm bị người bán đổi giá, **Then** trả về `hasPriceChanges: true` kèm giá cũ và giá mới.

---

### User Story 9 - Gộp Giỏ Hàng Khách Vãng Lai Khi Đăng Nhập (CART-10 - Priority: P2)
Là một khách vãng lai đã thêm vài món vào giỏ hàng (lưu tại trình duyệt), khi tôi đăng nhập, hệ thống tự động gộp các món này vào giỏ hàng của tài khoản.

* **Independent Test**: Gửi `POST /api/v1/carts/me/merge` kèm mảng `guestItems: [{ skuId, quantity }]`.

---

### User Story 10 - Phân Nhóm Giỏ Hàng Theo Shop (CART-11 - Priority: P1)
Là một người mua tại trang giỏ hàng của sàn đa người bán, tôi muốn nhìn thấy các sản phẩm được gom theo từng Shop riêng biệt kèm tổng tiền của từng Shop, để tôi có thể tick chọn toàn bộ sản phẩm của Shop đó hoặc áp mã giảm giá của Shop.

* **Independent Test**: Gửi `GET /api/v1/carts/me/grouped`.
* **Acceptance Scenarios**:
  1. **Given** Giỏ hàng chứa 3 món từ 2 Shop khác nhau (Shop A có 2 món, Shop B có 1 món), **When** gọi API phân nhóm, **Then** trả về danh sách 2 Shop, mỗi Shop có `seller_id`, `shop_subtotal` bằng tổng tiền các món của Shop đó, và mảng `items` thuộc về Shop.
  2. **Given** Giỏ hàng trống, **When** gọi API phân nhóm, **Then** trả về danh sách shops rỗng, `total_distinct_items = 0`, `subtotal_amount = 0`.

---

### User Story 11 - Đổi Phân Loại Hàng Trực Tiếp Trong Giỏ (CART-12 - Priority: P1)
Là một người mua, tôi muốn thay đổi phân loại (Màu sắc / Kích cỡ) của một món đồ ngay trong giỏ hàng mà không cần phải xóa đi rồi tìm lại trang sản phẩm.

* **Independent Test**: Gửi `PATCH /api/v1/carts/me/items/{cartItemId}/variant` với `{ newSkuId }`.
* **Acceptance Scenarios**:
  1. **Given** `cartItemId` tồn tại và `newSkuId` hợp lệ từ Catalog (còn hàng, có giá), **When** gọi API đổi phân loại, **Then** cập nhật `sku_id`, `product_name_snapshot`, `unit_price_snapshot` mới và tính lại tổng tiền giỏ hàng.
  2. **Given** `newSkuId` này đã tình cờ có sẵn trong giỏ hàng, **When** đổi sang SKU đó, **Then** hệ thống tự động cộng dồn số lượng vào dòng đã có và xóa dòng hiện tại (đảm bảo không bị trùng SKU).
  3. **Given** `newSkuId` không tồn tại hoặc hết hàng, **When** gọi API đổi, **Then** trả về `400 Bad Request` hoặc `404 Not Found` kèm mã lỗi thích hợp.

---

### User Story 12 - Dọn Dẹp Món Đã Thanh Toán Sau Checkout (CART-15 - Priority: P0)
Là hệ thống sau khi người mua đã hoàn tất đặt hàng và thanh toán thành công (`PAID`), tôi muốn dọn dẹp chính xác các món hàng đã mua ra khỏi giỏ, giữ lại các món chưa mua để khách hàng tiếp tục mua sắm sau.

* **Independent Test**: Gửi `POST /api/v1/carts/me/cleanup-checked-out` với `{ purchasedCartItemIds: [...] }`.
* **Acceptance Scenarios**:
  1. **Given** Giỏ hàng có 5 món, khách thanh toán thành công 3 món, **When** gọi cleanup với 3 ID đó, **Then** 3 món bị xóa khỏi DB, 2 món còn lại được giữ nguyên, `item_count` và `subtotal_amount` tính lại chính xác theo 2 món còn lại.
  2. **Given** Khách thanh toán tất cả các món trong giỏ, **When** gọi cleanup, **Then** giỏ trở về trạng thái trống `item_count = 0`, `subtotal_amount = 0`.

---

## 2. Requirements & Success Criteria

- **SC-001**: 100% các endpoint tuân thủ chuẩn RESTful, `[RequireRole("buyer", "admin", "staff")]`, kiểm tra quyền sở hữu giỏ qua `this.TryGetUserId()`.
- **SC-002**: 100% unit tests của Cart Service và Financial Invariants vượt qua thành công (`dotnet test` 100% pass).
- **SC-003**: Tuân thủ triệt để nguyên tắc không dùng Repository Pattern bọc ngoài EF Core.
