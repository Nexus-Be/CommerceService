# VĂN BẢN QUYẾT ĐỊNH: [TÊN VẤN ĐỀ ĐÃ CHỐT]
> **Vị trí lưu trữ quy chuẩn:** `.agents/decisions/YYYY-MM-DD_HHmm-<tên-vấn-đề>.md` (hoặc `YYYY-MM-DD-<tên-vấn-đề>.md`)  
> **Nguyên tắc:** Ngắn gọn, súc tích, đủ ý, làm bằng chứng kiến trúc và chức năng bất biến. Không viết dài dòng lan man để tra cứu nhanh và không gây hiểu lầm.

---

## ⏱️ THỜI GIAN & BỐI CẢNH
- **Thời gian bắt đầu câu chuyện:** [Ngày, Giờ, Phút (UTC+7)]
- **Thời gian chốt quyết định:** [Ngày, Giờ, Phút (UTC+7)]
- **Tệp thảo luận nguồn:** `.agents/notes/[tên-tệp-note-tương-ứng].md`

---

## 1. VẤN ĐỀ ĐƯỢC CHỐT (THE CORE DECISION)
- **Tên quyết định / Định hướng:** [Nêu trực tiếp quyết định đã được thống nhất]
- **Bản chất kỹ thuật:** [1-2 câu giải thích tại sao chọn phương án này]

---

## 2. NỘI DUNG THỐNG NHẤT BẮT BUỘC (MANDATORY INVARIANTS)
1. **[Quy định 1]:** [Chi tiết ngắn gọn]
2. **[Quy định 2]:** [Chi tiết ngắn gọn]
3. **[Quy định 3]:** [Chi tiết ngắn gọn]

---

## 3. CÁC PHẦN ĐÃ THỐNG NHẤT LOẠI BỎ (NON-GOALS)
- 🔴 [Nêu rõ những gì KHÔNG làm để tránh phát sinh phạm vi sau này]

---

## 4. GIAO THỨC LỖ HỔNG & NGUỒN GỐC ĐỀ XUẤT (VULNERABILITY & ATTRIBUTION)
*(Bắt buộc điền khi quyết định xuất phát từ kiểm thử TDD, /interrogate hoặc rà soát phát hiện lỗi)*
- **Lỗ hổng / Lỗi phát hiện:** [Mô tả lỗi hoặc rủi ro phát hiện]
- **Lệnh chạy thực tế chứng minh (Repro Command & Output):**
  ```bash
  [Lệnh kiểm thử đã chạy chứng minh lỗi]
  ```
  *Output thực tế:* `[Log kết quả chứng minh lỗi]`
- **Phương án khắc phục được chốt:** [Mô tả thiết kế giải quyết dứt điểm nguyên nhân gốc]
- **Nguồn gốc đề xuất:**
  - 💡 **Đề xuất bởi:** [Người Dùng (Human Operator) | AI Agent đề cử/khuyến nghị]
  - ⚖️ **Quyết định bởi:** Người Dùng (Human Operator)

---

## 5. BẰNG CHỨNG & THỰC THI (VERIFICATION)
- **Cách thức nghiệm thu:** [Tiêu chí đo lường hoặc test case chứng minh quyết định được thực hiện đúng]
- **Tệp kế hoạch tiếp theo:** `.agents/plans/[tên-plan-nếu-có].md`
