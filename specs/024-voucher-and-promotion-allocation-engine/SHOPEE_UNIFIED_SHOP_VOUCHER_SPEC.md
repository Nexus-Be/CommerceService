# TÀI LIỆU ĐẶC TẢ ĐIỀU CHỈNH KỸ THUẬT (ENGINEERING SPECIFICATION)
## ĐỒNG NHẤT VOUCHER THEO CHUẨN SHOPEE: GỘP VOUCHER CẤP SHOP & CHỐNG LỖ KÉP CHO SELLER

> **Mã phân hệ:** `specs/024-voucher-and-promotion-allocation-engine`  
> **Dịch vụ phụ trách:** `CommerceService` (Port `8003`, `commerce_db`)  
> **Phiên bản tài liệu:** v2.0 (Shopee Alignment Standard)  
> **Ngày lập:** 2026-11-17  
> **Trạng thái:** 🎯 DỰ THẢO THIẾT KẾ ĐẶC TẢ CHI TIẾT — SẴN SÀNG THỰC THI (READY FOR IMPLEMENTATION)  

---

## 1. BỐI CẢNH & MỤC TIÊU CẢI TIẾN

### 1.1. Hiện trạng của Hệ thống (Feature 024 v1.0)
Hệ thống hiện tại hỗ trợ cơ chế cộng dồn độc lập 3 tầng (**3-Tier Stacking**):
* **Tầng 1:** Voucher Sản phẩm (`ScopeType = Product`) — Giảm cho từng SKU cụ thể.
* **Tầng 2:** Voucher toàn Shop (`ScopeType = Shop`) — Giảm cho cả gian hàng của Shop.
* **Tầng 3:** Voucher của Sàn (`ScopeType = System`) — Sàn tài trợ toàn đơn.

### 1.2. Vấn đề phát sinh trong thực tế vận hành
1. **Rủi ro "Lỗ kép" (Double Discounting Margin Erosion) cho Seller:**
   * Nếu Seller phát hành một Voucher Sản phẩm giảm 30% cho món Áo sơ mi (để xả hàng tồn), đồng thời Shop đang chạy Voucher toàn Shop giảm 20% cho đơn từ 500k.
   * Khi khách mua chiếc áo sơ mi đó kèm một món khác để đủ 500k: Chiếc áo sơ mi bị **trừ chồng 2 lần voucher của cùng 1 Shop** (tổng giảm có thể lên tới 50%). Điều này làm chủ Shop bị lỗ vốn nặng do sơ suất cấu hình.
2. **Trải nghiệm Người mua phức tạp (Cluttered Checkout UX):**
   * Người mua phải thao tác qua 3 ô bấm chọn mã riêng biệt (chọn mã sản phẩm 1, sản phẩm 2, chọn mã shop, chọn mã sàn). Giao diện bị rối và không quen thuộc với thói quen mua sắm của người dùng Việt Nam.

### 1.3. Mục tiêu cải tiến theo Hướng 1 (Chuẩn thực tế Shopee / TikTok Shop)
1. **Gộp phạm vi ưu đãi cấp Shop (Unified Shop Voucher Scope):**
   * Coi cả Voucher Sản phẩm và Voucher toàn Shop đều là **"Ưu đãi từ Người bán" (Seller Discounts)**.
   * **Rào chắn bất biến (Single Shop-Voucher Invariant):** Trong cùng một đơn hàng, mỗi Shop (SubOrder) **CHỈ ĐƯỢC ÁP DỤNG DUY NHẤT 1 VOUCHER CỦA SHOP ĐÓ** (Hoặc là mã Sản phẩm, HOẶC là mã toàn Shop). Tuyệt đối **KHÔNG CỘNG DỒN CẢ HAI MÃ CỦA CÙNG MỘT SHOP**.
2. **Mô hình Thanh toán 2 Tầng Tinh gọn (2-Tier Checkout Experience):**
   * **Tầng 1: Voucher của Shop (Shop Voucher)** — Tối đa 1 voucher cho mỗi Shop trong đơn hàng (do Shop chi trả).
   * **Tầng 2: Voucher của Sàn (Platform Voucher)** — Tối đa 1 voucher cho toàn bộ đơn hàng (do Sàn chi trả/bù tiền).
3. **Động cơ tự động chọn mã tối ưu (Best-Voucher Auto-Selection Engine):**
   * Nếu giỏ hàng của Buyer có thể áp dụng cả mã Sản phẩm và mã toàn Shop của cùng một Shop A, hệ thống sẽ **tự động tính toán và chọn mã mang lại số tiền giảm cao nhất** cho Buyer mà không cần Buyer phải thử từng mã thủ công.
4. **Bảo toàn Cơ sở dữ liệu (Zero Database Drift):**
   * **Không thay đổi Database Schema**, không tạo migration mới. Giữ nguyên enum `DiscountScopeType` (`System = 0, Shop = 1, Product = 2`) để phục vụ cấu hình linh hoạt của Seller.

---

## 2. QUY TẮC NGHIỆP VỤ & CÔNG THỨC TOÁN HỌC TÀI CHÍNH

### 2.1. Quy tắc Áp dụng Voucher cấp Shop (Shop-Level Exclusivity Rule)

Trong một đơn hàng gồm $N$ Shop (tương ứng $N$ SubOrders):
* Với mỗi Shop $k \in \{1, \dots, N\}$:
  * Cho phép truyền vào danh sách mã voucher.
  * Hệ thống kiểm tra: Nếu trong danh sách voucher người dùng chọn có **nhiều hơn 1 voucher thuộc quyền sở hữu của cùng Shop $k$** (`voucher.OwnerSellerId == Shop_k`), hệ thống sẽ:
    * **Tại chế độ Validate/CreateOrder:** Báo lỗi vi phạm hợp đồng **HTTP 400 `SHOP_MULTIPLE_VOUCHERS_NOT_ALLOWED`** (Message: `"Only one voucher per shop can be applied."`).
    * **Tại chế độ Recommend (Gợi ý tự động):** Hệ thống chỉ chọn ra **1 voucher duy nhất** có $DiscountAmount$ lớn nhất của Shop $k$.

### 2.2. Công thức Tính toán Tài chính theo Mô hình 2 Tầng Chuẩn Hóa

Giả sử đơn hàng có $N$ Shop. Xét riêng Shop $k$:
* **Tổng tiền hàng thô của Shop $k$ (Shop Gross Merchandise Value):**
  $$G_k = \sum_{i \in Items_k} (UnitPrice_i \times Quantity_i)$$

* **Bước 1: Tính Giảm giá cấp Shop ($D_{shop, k}$):**
  Buyer chỉ được chọn 1 trong 2 loại:
  * **Trường hợp A (Chọn Voucher toàn Shop):**
    $$D_{shop, k} = \begin{cases} 
      \min(G_k \times \text{DiscountValue}, \text{MaxDiscountAmount}) & \text{nếu } DiscountType = \text{Percentage} \\
      \min(\text{DiscountValue}, G_k) & \text{nếu } DiscountType = \text{FixedAmount}
    \end{cases}$$
    *(Điều kiện: $G_k \ge MinOrderAmount$)*
  * **Trường hợp B (Chọn Voucher Sản phẩm):**
    Chỉ tính giảm giá trên tổng tiền của các sản phẩm nằm trong danh sách SKU hợp lệ của voucher:
    $$G_{k, eligible} = \sum_{i \in Items_k \cap Scopes} (UnitPrice_i \times Quantity_i)$$
    $$D_{shop, k} = \begin{cases}
      \min(G_{k, eligible} \times \text{DiscountValue}, \text{MaxDiscountAmount}) & \text{nếu } DiscountType = \text{Percentage} \\
      \min(\text{DiscountValue}, G_{k, eligible}) & \text{nếu } DiscountType = \text{FixedAmount}
    \end{cases}$$
    *(Điều kiện: $G_{k, eligible} \ge MinOrderAmount$)*

* **Bước 2: Doanh thu thuần của Shop sau Voucher Shop ($R_{net, k}$):**
  $$R_{net, k} = G_k - D_{shop, k}$$
  *(Đây là căn cứ để tính Phí Sàn 5%)*

* **Bước 3: Phân bổ Giảm giá cấp Sàn ($D_{platform, k}$):**
  Tổng giá trị còn lại của toàn bộ các shop trên đơn hàng:
  $$R_{net, total} = \sum_{k=1}^N R_{net, k}$$
  Nếu đơn hàng có áp dụng Voucher Sàn với tổng giá trị giảm là $D_{platform, total}$:
  Mỗi Shop được phân bổ theo tỷ trọng doanh thu thuần:
  $$D_{platform, k} = \text{Round}\left( D_{platform, total} \times \frac{R_{net, k}}{R_{net, total}} \right)$$
  *(Khóa chốt Penny-rounding: phần dư 1-2 đồng lẻ được dồn vào Shop có $R_{net, k}$ lớn nhất, đảm bảo $\sum D_{platform, k} = D_{platform, total}$ tuyệt đối).*

* **Bước 4: Hạch toán Doanh thu & Phí Sàn (Chuẩn Hướng 1 Shopee):**
  * **Phí dịch vụ Sàn (5%):**
    $$Fee_k = \text{Round}(R_{net, k} \times 5\%)$$
  * **Tiền Sàn bù lại cho Shop (Platform Rebate):**
    $$Rebate_k = D_{platform, k}$$
  * **Tiền thực tế Shop $k$ nhận về Ví (Shop Net Earnings):**
    $$E_k = R_{net, k} - Fee_k + Rebate_k - D_{platform, k} = R_{net, k} - Fee_k$$
    *(Bản chất: Khách trả thiếu do voucher sàn thì Sàn bù đủ, Shop chỉ mất tiền voucher của chính mình và tiền phí sàn 5%).*

---

## 3. THIẾT KẾ CHI TIẾT CÁC THÀNH PHẦN MÃ NGUỒN CẦN SỬA ĐỔI

### 3.1. Tầng Domain & DTOs (`Commerce.Application`)

#### A. Cập nhật `TierSavingsDto` trong [Commerce.Application/Services/Discount/DiscountDtos.cs](file:///d:/CommerceService/Commerce.Application/Services/Discount/DiscountDtos.cs)
Chuyển đổi từ cấu trúc 3 tầng cũ sang cấu trúc 2 tầng chuẩn Shopee:
```csharp
// TRƯỚC ĐÂY (3 Tầng cũ):
public sealed record TierSavingsDto(
    decimal ProductDiscount,
    decimal ShopDiscount,
    decimal SystemDiscount,
    decimal TotalSavings
);

// ĐIỀU CHỈNH CHUẨN SHOPEE (2 Tầng tinh gọn, tương thích ngược):
public sealed record TierSavingsDto(
    decimal ShopDiscount,       // Tổng tiền giảm từ các Shop (bao gồm cả mã sản phẩm hoặc mã toàn shop của các shop)
    decimal SystemDiscount,     // Tiền giảm do Sàn tài trợ
    decimal TotalSavings        // Tổng tiết kiệm = ShopDiscount + SystemDiscount
);
```

#### B. Cập nhật Request / Response kiểm tra Voucher
Trong `ValidateVoucherResponse`:
* Bổ sung thông tin xác định Shop sở hữu: `Guid? OwnerSellerId`.
* Trả về cờ cảnh báo nếu người dùng cố tình gửi nhiều voucher của cùng một shop.

---

### 3.2. Động cơ Tính toán Giảm giá (`IVoucherCalculationEngine.cs` & `VoucherCalculationEngine.cs`)

File vị trí: [Commerce.Application/Services/Discount/VoucherCalculationEngine.cs](file:///d:/CommerceService/Commerce.Application/Services/Discount/VoucherCalculationEngine.cs)

#### Logic điều chỉnh trong hàm `CalculateOrderDiscounts`:
1. **Kiểm tra tính độc quyền cấp Shop (Single Shop-Voucher Guard):**
   ```csharp
   // Nhóm các voucher theo SellerId
   var shopVoucherGroups = candidateVouchers
       .Where(v => v.ScopeType == DiscountScopeType.Shop || v.ScopeType == DiscountScopeType.Product)
       .GroupBy(v => v.OwnerSellerId);

   foreach (var group in shopVoucherGroups)
   {
       if (group.Count() > 1)
       {
           throw new CommerceDomainException(
               "SHOP_MULTIPLE_VOUCHERS_NOT_ALLOWED",
               $"Only one voucher can be applied for shop '{group.Key}'.",
               400);
       }
   }
   ```
2. **Quy trình tính toán cho từng Shop:**
   * Lặp qua từng Shop trong giỏ hàng.
   * Lấy duy nhất 1 voucher của Shop đó (nếu có):
     * Nếu là `ScopeType == Product`: Tính giảm trên các SKU khớp scopes.
     * Nếu là `ScopeType == Shop`: Tính giảm trên tổng tiền Shop.
   * Tính ra $R_{net, k}$ cho Shop đó.
3. **Tính tiếp Tầng Sàn:**
   * Tính trên tổng $\sum R_{net, k}$ và phân bổ tỷ lệ $D_{platform, k}$.

#### Logic điều chỉnh trong hàm `RecommendVouchersAsync` (Tự động chọn mã hời nhất):
* Khi tìm voucher tốt nhất cho Shop A:
  * Thuật toán lấy tất cả voucher của Shop A mà giỏ hàng thỏa mãn điều kiện.
  * Tính thử số tiền giảm của từng voucher:
    * Thử Voucher Sản phẩm ➔ Giảm được $X$ đồng.
    * Thử Voucher Toàn shop ➔ Giảm được $Y$ đồng.
  * **Lựa chọn mã có $\max(X, Y)$ làm đại diện duy nhất cho Shop A đưa vào danh sách đề xuất.**
  * Nhờ đó, người mua luôn được hưởng mức giảm cao nhất mà Seller không bao giờ bị trừ trùng 2 lần!

---

### 3.3. Dịch vụ Điều phối Đơn hàng (`OrderService.cs` & `VoucherDiscountCalculator.cs`)

File vị trí: [Commerce.Application/Services/Order/OrderService.cs](file:///d:/CommerceService/Commerce.Application/Services/Order/OrderService.cs)

* Khi Buyer gọi `POST /api/v1/orders/checkout/quote` hoặc `POST /api/v1/orders/checkout`:
  * Hệ thống truyền danh sách `VoucherCodes` vào `IVoucherDiscountCalculator`.
  * Bộ tính toán tự động thực thi quy tắc Shop-Level Exclusivity. Nếu Buyer cố tình truyền 2 mã của cùng một Shop, hệ thống ném `SHOP_MULTIPLE_VOUCHERS_NOT_ALLOWED` (HTTP 400).
  * Đảm bảo tính nhất quán giữa màn hình Báo giá (Quote) và màn hình Tạo đơn chính thức.

---

### 3.4. Giao diện Người dùng & Frontend Gateway (`DiscountsController.cs`)

File vị trí: [Commerce.Api/Controllers/Public/DiscountsController.cs](file:///d:/CommerceService/Commerce.Api/Controllers/Public/DiscountsController.cs)

* Endpoint `POST /api/v1/discounts/recommend`:
  * Trả về danh sách `recommendedVoucherCodes`: Tối đa $N$ mã Shop (mỗi Shop 1 mã tốt nhất) + 1 mã Sàn tốt nhất.
  * Frontend chỉ cần áp đúng danh sách này vào giỏ hàng là đảm bảo hoàn hảo 100%.

---

## 4. MA TRẬN MÃ LỖI (ERROR CATALOG RFC 9110)

Tất cả các thông báo lỗi tuân thủ nghiêm ngặt **100% Tiếng Anh, chuẩn Sentence case**:

| HTTP Status | Error Code (`SCREAMING_SNAKE_CASE`) | Error Message (`Sentence case`) | Nguyên nhân kích hoạt |
|:---|:---|:---|:---|
| **400 Bad Request** | `SHOP_MULTIPLE_VOUCHERS_NOT_ALLOWED` | `"Only one voucher per shop can be applied."` | Người mua cố tình gửi từ 2 voucher trở lên của cùng một Shop trong cùng một đơn hàng. |
| **400 Bad Request** | `VOUCHER_SKU_AUCTION_NOT_ALLOWED` | `"Voucher cannot be applied to auction products."` | Voucher sản phẩm gán vào sản phẩm đấu giá (từ Catalog). |
| **400 Bad Request** | `CATALOG_SKU_INACTIVE` | `"Product '{productName}' is no longer available for purchase."` | Sản phẩm trong voucher đã ngừng bán. |
| **403 Forbidden** | `VOUCHER_SELLER_SKU_FORBIDDEN` | `"Product SKU '{skuId}' does not belong to your shop."` | Shop A gán SKU của Shop B. |
| **400 Bad Request** | `VOUCHER_MIN_ORDER_NOT_MET` | `"Order amount does not meet the minimum requirement of the voucher."` | Chưa đạt giá trị đơn tối thiểu. |

---

## 5. KẾ HOẠCH KIỂM THỬ & KIỂM CHỨNG TỰ ĐỘNG (VERIFICATION PLAN)

### 5.1. Kịch bản Unit Tests Bổ sung (`Commerce.UnitTests`)
1. **Test Case 1: Chặn cộng dồn 2 voucher của cùng một Shop (`ShouldThrow_WhenMultipleVouchersAppliedForSameShop`)**
   * Giỏ hàng có 2 sản phẩm của Shop A.
   * Áp dụng Voucher Sản phẩm (giảm 20k) VÀ Voucher Toàn shop A (giảm 10%).
   * Kết quả mong đợi: Quăng ngoại lệ `CommerceDomainException` với mã `SHOP_MULTIPLE_VOUCHERS_NOT_ALLOWED` (HTTP 400).
2. **Test Case 2: Tự động chọn voucher hời nhất cho Shop (`RecommendVouchers_ShouldSelectBestVoucherForShop`)**
   * Shop A có Mã Sản phẩm (giảm 50k) và Mã Toàn shop (giảm 30k).
   * Gọi `RecommendVouchersAsync`.
   * Kết quả mong đợi: Hệ thống chỉ trả về Mã Sản phẩm (50k) cho Shop A, bỏ qua mã 30k.
3. **Test Case 3: Đơn hàng Đa Shop (Multi-Shop Order)**
   * Khách mua hàng của Shop A và Shop B.
   * Áp dụng: 1 Voucher Shop A + 1 Voucher Shop B + 1 Voucher Sàn.
   * Kết quả mong đợi: Thành công 100%. Mỗi shop được trừ đúng 1 mã của mình, và Voucher Sàn được chia tỷ trọng hoàn hảo không lệch 1 đồng.
4. **Test Case 4: Kiểm tra Hạch toán Doanh thu & Phí sàn 5% (Financial Invariant)**
   * Kiểm tra công thức tài chính: $ShopNetEarnings = ShopGrossRevenue - ShopDiscounts - PlatformFeeAmount + PlatformVoucherRebate$.

### 5.2. Tiêu chuẩn Hoàn thành (Definition of Done - DoD)
* [ ] Biên soạn xong tài liệu đặc tả (File này).
* [ ] Sửa đổi `VoucherCalculationEngine.cs` và các DTO liên quan.
* [ ] `dotnet build Commerce.sln -c Release` thành công 100% với **0 Error, 0 Warning**.
* [ ] Tất cả các test cases mới và cũ chạy qua 100%.
* [ ] Cập nhật tài liệu bàn giao Frontend Gateway Contract.
