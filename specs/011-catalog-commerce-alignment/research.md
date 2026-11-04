# Architecture Research & Decisions: Catalog and Commerce Contract Alignment

**Feature**: `specs/011-catalog-commerce-alignment`  
**Date**: 2026-11-03  
**Status**: APPROVED  
**Related SRS**: `srs-catalog-commerce-alignment.md`

---

## 1. Architectural Decisions & Rationales

### Decision 1: Direct `IsPurchasable` Consumption (Bãi bỏ logic kiểm tra giá cục bộ)
- **Decision**: CommerceService (`CatalogClient.cs`) trực tiếp gán `IsPurchasable = item.IsPurchasable` từ dữ liệu phản hồi của CatalogService. Tuyệt đối không ghi đè lại bằng `item.Price > 0 && item.Status == Active`.
- **Rationale**:
  - `CatalogService` là Single Source of Truth về vòng đời sản phẩm, trạng thái hiển thị của danh mục, sản phẩm cha và biến thể con.
  - Công thức mở bán bên Catalog bao gồm: `!variant.IsDeleted && variant.Status == Active && !product.IsDeleted && product.Status == Active && (product.SaleType != Regular || (variant.Price > 0))`.
  - Nếu Commerce tự kiểm tra `item.Price > 0`, các sản phẩm đấu giá (`SaleType == Auction`) hoặc sản phẩm quà tặng có đơn giá 0 VNĐ sẽ bị coi là không mở bán (false negative).
- **Alternatives Considered**:
  - *Duy trì kiểm tra cục bộ kết hợp Catalog:* Bị bác bỏ vì gây lệch trạng thái giữa hai service và khiến code phân tán logic nghiệp vụ của Catalog sang Commerce.

---

### Decision 2: Triệt Tiêu Mock Fallback 100.000 VNĐ & Triển Khai Fail-Closed (HTTP 503)
- **Decision**: Xóa bỏ toàn bộ khối code fallback sinh giá `100.000 VNĐ` và `SellerId = 11111111-1111-1111-1111-111111111111` trong `CatalogClient.cs`. Khi Catalog bị ngắt kết nối hoặc trả về lỗi 5xx, CommerceService ném `CommerceDomainException("CATALOG_SERVICE_UNAVAILABLE", ..., 503)`.
- **Rationale**:
  - Việc tự động giả lập giá 100.000 VNĐ tạo ra lỗ hổng tài chính nghiêm trọng: người mua có thể mua hàng giá trị cao (ví dụ điện thoại, máy tính) với giá 100.000 VNĐ nếu Catalog bị chập chờn đúng thời điểm validate.
  - Fail-closed là tiêu chuẩn vàng của hệ thống thương mại điện tử: nếu không thể xác thực giá từ nguồn chân lý, hệ thống phải từ chối giao dịch và yêu cầu người dùng thử lại sau.
  - Đối với môi trường kiểm thử (UnitTests / CI), dữ liệu mock phải được kiểm soát qua `Moq<ICatalogClient>`, không được chèn code giả lập vào Production assembly.
- **Alternatives Considered**:
  - *Degraded Read-Only Cart:* Đã thảo luận trong phiên clarify (Q1); người dùng đã chọn Option A (Fail-Closed) để bảo đảm an toàn dữ liệu và cảnh báo sự cố ngay lập tức.

---

### Decision 3: Hỗ Trợ Nullable Price (`decimal?`) và Safe Defaulting 0m
- **Decision**: Khai báo `public decimal? Price { get; set; }` trong `CatalogVariantRawItem` và gán `decimal unitPrice = item.Price ?? 0m`.
- **Rationale**:
  - Trong CatalogService, các sản phẩm đấu giá (`Auction`) hoặc bản nháp sản phẩm có thể trả về `price: null` trong JSON payload.
  - Khai báo kiểu `decimal` không thể nhận giá trị `null` từ `System.Text.Json`, dẫn đến ném `JsonException` làm sập toàn bộ request batch.
  - Safe defaulting về `0m` bảo đảm parser luôn an toàn, đồng thời tôn trọng thuộc tính `SaleType`.
- **Alternatives Considered**:
  - *Yêu cầu Catalog luôn trả về số 0 thay vì null:* Bị bác bỏ vì `null` mang ngữ nghĩa chính xác là "chưa được định giá", việc ép kiểu phía Catalog sẽ làm sai lệch mô hình dữ liệu của Catalog.

---

### Decision 4: Đánh Giá Song Song Giỏ Hàng (`Task.WhenAll`) Cho Cơ Chế Làm Xám (`Cart Gray-out`)
- **Decision**: Khi gọi `GET /api/v1/carts/me`, `CartService` thực hiện song song 2 cuộc gọi mạng:
  ```csharp
  var stockTask = _fulfillmentClient.BatchCheckStockAsync(skuIds, ct);
  var catalogTask = _catalogClient.GetSkuBatchDetailsAsync(skuIds, ct);
  await Task.WhenAll(stockTask, catalogTask);
  ```
- **Rationale**:
  - Kiểm tra trạng thái Catalog (còn kinh doanh không) và tồn kho Fulfillment (còn hàng trong kho không) là hai tác vụ hoàn toàn độc lập.
  - Thực thi song song giúp thời gian phản hồi của `GET /api/v1/carts/me` chỉ bằng $\max(\text{Latency}_{\text{Catalog}}, \text{Latency}_{\text{Fulfillment}})$, thay vì cộng dồn tuần tự, bảo đảm trải nghiệm mượt mà cho người mua.
- **Alternatives Considered**:
  - *Chỉ kiểm tra tồn kho tại giỏ hàng, để Catalog kiểm tra lúc checkout:* Bị bác bỏ vì người mua sẽ không biết sản phẩm đã bị shop gỡ bán, gây thất vọng khi bấm thanh toán mới bị báo lỗi.

---

### Decision 5: Quy Tắc Thứ Tự Ưu Tiên Lý Do Vô Hiệu Hóa Món Hàng
- **Decision**: Thứ tự ưu tiên xác định `DisabledReason` trên dòng giỏ hàng:
  1. `PRODUCT_NOT_FOUND`: SKU không tồn tại trong Catalog.
  2. `PRODUCT_INACTIVE`: Sản phẩm ngừng kinh doanh (`IsPurchasable == false`).
  3. `OUT_OF_STOCK`: Sản phẩm vẫn mở bán nhưng kho Fulfillment báo hết hàng (`AvailableStock <= 0`).
- **Rationale**:
  - Đã chốt tại Clarification Q3. Nếu một sản phẩm đã ngừng bán (`PRODUCT_INACTIVE`), trạng thái tồn kho không còn ý nghĩa. Thông báo "Ngưng bán" giúp người mua hiểu rằng sản phẩm sẽ không được nhập thêm và nên xóa khỏi giỏ.
- **Alternatives Considered**:
  - *Ưu tiên OUT_OF_STOCK trước:* Bị bác bỏ vì gây hiểu nhầm cho người mua là sản phẩm sắp về hàng.

---

### Decision 6: Cập Nhật Tức Thì Snapshot Giá Vào PostgreSQL Khi Validate
- **Decision**: Trong `POST /api/v1/carts/me/validate`, nếu phát hiện đơn giá thực tế khác snapshot (`item.UnitPriceSnapshot != catalogSku.UnitPrice`), hệ thống cập nhật ngay `item.UnitPriceSnapshot = catalogSku.UnitPrice`, `item.UpdatedAt = DateTimeOffset.UtcNow` và gọi `_dbContext.SaveChangesAsync(ct)`.
- **Rationale**:
  - Đã chốt tại Clarification Q2. Đảm bảo dữ liệu giỏ hàng trong cơ sở dữ liệu luôn khớp với mức giá mới nhất mà người mua nhìn thấy, ngăn ngừa lệch giá khi người mua bấm tiếp sang bước Đặt hàng.
- **Alternatives Considered**:
  - *Chỉ cập nhật in-memory:* Bị bác bỏ vì nếu người mua tải lại trang, giá cũ lại xuất hiện gây nhầm lẫn.
