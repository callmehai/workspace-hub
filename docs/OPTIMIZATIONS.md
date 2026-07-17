# Optimizations & Tech Debt — ghi chú tối ưu hoãn lại

> Nơi ghi các điểm **đã biết là chưa tối ưu nhưng chấp nhận được ở quy mô hiện tại** (đồ án PRN232),
> kèm hướng fix nếu sau này lên production thật / dữ liệu lớn. Phát hiện chủ yếu từ review PR (Drive UX #114).
> Không cái nào là bug — đừng "sửa" nếu chưa có lý do (đo được chậm / rủi ro thật).

Quy ước mức độ: 🟢 cosmetic/nhỏ · 🟡 nên làm khi scale · 🔴 làm trước khi lên prod thật.

---

## 1. 🟡 Filter/sort Drive quét `LIKE N'%...%'` trên `MetadataJson` (không index)

**Hiện trạng.** Nhiều nhánh trong `ItemRepository.GetPagedAsync` lọc/sắp xếp bằng `MetadataJson.Contains(...)`
→ EF dịch thành `LIKE N'%...%'`, **không dùng được index**, quét toàn bộ (sau khi đã lọc `UserId`/`!IsArchived`):
- `driveKind` filter: `"isFolder":true` / phần bù (NOT folder).
- Sort folders-first: `OrderByDescending(Contains("isFolder":true))`.
- Root/All: `"isTopLevel":true`.
- Trong 1 folder Drive: `parentToken` (`"<externalId>"`).

**Vì sao chấp nhận được bây giờ.** Đã lọc trước theo `UserId` + `!IsArchived` (có index) nên số dòng còn lại nhỏ
(mỗi user vài trăm–vài nghìn item). Pattern này vốn đã tồn tại từ trước (`isTopLevel`), không phải PR này đẻ ra.

**Hướng tối ưu khi cần.**
- **Promote field ra cột thật + index.** `IsFolder` (bool) và `DriveParentExternalId` (string) đang chôn trong JSON.
  Tách thành cột entity riêng (migration + set khi sync/upload) rồi đánh index → filter/sort thành `WHERE`/`ORDER BY`
  chạy trên index thay vì `LIKE`. Đây là fix "đúng nhất".
- Hoặc **computed persisted column** trích từ JSON (`JSON_VALUE(MetadataJson,'$.isFolder')`) + index — ít đụng code app hơn.
- `isTopLevel` cũng nên đi cùng lượt promote (cùng bản chất).

---

## 2. 🟡 SSRF surface nhỏ: bearer token đính vào `thumbnailLink` do Google trả

**Hiện trạng.** `DriveGateway.GetThumbnailAsync` lấy `thumbnailLink` từ metadata Google, regex `=s\d+→=s1024`,
rồi `SendMediaRequestAsync` **gắn access token của connection** vào URL đó và proxy về. URL đến từ API Google
(tin cậy) nên rủi ro thực tế thấp.

**Hướng tối ưu.** Trước khi đính bearer, **allowlist host** (`*.googleusercontent.com`, `*.googleapis.com`) —
nếu link Google trả về (giả sử bị chi phối) trỏ host lạ thì bỏ token / bỏ fetch. Rẻ, tăng defense-in-depth.

---

## 3. 🟢 Điều hướng folder Drive: F5 chỉ dựng lại 1 cấp

**Hiện trạng.** Stack drill-down sống trong `location.state.driveStack`; URL chỉ mang `?df=<internalId>` của folder
lá. Back/Forward trong phiên OK; **reload (F5) mất state** → chỉ fetch item lá dựng lại **1 cấp** (breadcrumb gọn còn
folder hiện tại). Đã ghi rõ ở CHANGELOG là hạn chế đã biết.

**Hướng tối ưu.** Dựng lại **đủ chuỗi tổ tiên** sau F5: lần theo `parent` trong metadata (hoặc thêm endpoint
`GET /api/drive/items/{id}/ancestors`) để build lại breadcrumb nhiều cấp. Hoặc encode cả stack (mảng internalId)
vào URL thay vì mỗi folder lá — đánh đổi URL dài hơn.

---

## 4. 🟢 `HttpClient "DriveMedia"` timeout Infinite

**Hiện trạng.** Named client proxy media để `Timeout.InfiniteTimeSpan`, hủy hoàn toàn dựa vào `CancellationToken`
của request (client ngắt → hủy). Đúng cho file lớn/stream lâu, nhưng **không có trần trên**: nếu client treo mà
không disconnect, kết nối tới Google có thể bị giữ lâu.

**Hướng tối ưu.** Đặt trần "đủ rộng" (vd 10–15 phút) qua `CancellationTokenSource.CreateLinkedTokenSource` thay vì
Infinite tuyệt đối, để backstop khi CancellationToken vì lý do nào đó không kích hoạt.

---

## 5. 🟢 Preview ảnh nạp blob tới 25MB vào RAM trình duyệt

**Hiện trạng.** `PREVIEW_IMAGE_MAX_BYTES = 25MB`: ảnh ≤ ngưỡng tải **nội dung gốc** làm preview (blob trong RAM tab).
Ảnh lớn hơn đã tự chuyển sang thumbnail. Chấp nhận được cho preview.

**Hướng tối ưu.** Hạ ngưỡng (vd 8–10MB) nếu muốn nhẹ RAM/băng thông hơn — ảnh trên ngưỡng vẫn có thumbnail nên UX
gần như không đổi.

---

## 6. 🟢 FE bundle > 500kB (cảnh báo chunk-size lúc build)

**Hiện trạng.** `npm run build` cảnh báo chunk > 500kB (sẵn có, không phải PR này). Không ảnh hưởng chức năng.

**Hướng tối ưu.** Code-split bằng `import()` động cho route nặng (Kanban, admin), hoặc cấu hình `output.codeSplitting`
/ tách vendor (signalr, tanstack). Chỉ làm khi quan tâm thời gian tải lần đầu.
