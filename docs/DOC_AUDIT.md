# DOC AUDIT — Workspace Hub (2026-06-18)

> Báo cáo tự audit toàn bộ tài liệu so với CODE thật + chéo giữa các file. Branch: `docs/cleanup-drift-sync`.
> File này là báo cáo tạm — KHÔNG commit (trừ khi được yêu cầu).

## Phạm vi đã quét

13 file tài liệu (không có .docx/.txt nào tracked):
`CLAUDE.md` · `docs/CLAUDE.md` · `.claude/CLAUDE.md` · `README.md` · `backend/README.md` · `frontend/README.md` · `docs/API.md` · `docs/SPRINTS.md` · `docs/DATABASE.md` · `docs/CONVENTIONS.md` · `docs/SETUP.md` · `docs/CHANGELOG.md` · `docs/TODO-ServiceType-Split.md`

## Ground truth từ code (bảng fact)

| Hạng mục | Sự thật trong code |
|---|---|
| Config section | `OAuth:{provider}:ClientId/ClientSecret` ở mọi nơi (ConnectionsService, AuthService, TokenService, GoogleTokenVerifier). KHÔNG còn `Dev:`. appsettings.json có key `OAuth`, không có `Dev`. |
| Scope/service | Gmail=[`gmail.modify`,`gmail.send`], GCal=[`calendar`], Drive=[`drive`]; Login=[`openid`,`email`,`profile`] (GoogleScopes.cs) |
| Migrations | **4**: InitialCreate, UsersMultiAuth, ModelBConnections, **RemoveClientCredentialsFromIntegration** |
| ServiceType enum | Gmail, GCal, Drive, Jira (Jira seed sẵn cho phase sau) |
| Admin endpoint | `PATCH /api/admin/integrations/{key}/enable`, [Authorize(Roles=Admin)], trả `IntegrationResponse{id,key,displayName,isEnabled}` |
| Health endpoint | `GET /api/health` [AllowAnonymous] → `HealthDto{status,database,userCount,serverTimeUtc}` (status="Healthy"/"Degraded", database="Connected"/"Unreachable") |
| Integration entity | KHÔNG còn cột ClientId/ClientSecretEncrypted (đã drop); không endpoint PUT credentials |

## Phát hiện MỚI (chưa nằm trong 5 điểm đã biết)

| # | File · Vị trí | Loại | Bằng chứng | Mức | Đề xuất | Xử lý |
|---|---|---|---|---|---|---|
| N1 | README.md:39 | Doc lag (cấu trúc FE) | Doc: `src/{...lib, services, types}`. Code: `services/` đã xoá (PR #25), có thêm `hooks/`. | Vừa | Bỏ `services`, thêm `hooks` | ✅ Tự sửa |
| N2 | README.md:76 | Khớp code (sai shape) | Doc: health trả `{api:"ok", db:"ok"}`. Code: `HealthDto{status,database,userCount,serverTimeUtc}`. | Vừa | Sửa theo HealthDto | ✅ Tự sửa |
| N3 | README.md:123-137 | Status drift | Doc liệt 35/36 ở "⏳ Đang tới" + "Nợ SCRUM-14". SPRINTS: 35/36 ✅ Done, 14 ✅ Done. | Vừa | Sync theo SPRINTS | ✅ Tự sửa |
| N4 | backend/README.md:47 | Self-consistency + code | Liệt 3 migration (thiếu `RemoveClientCredentialsFromIntegration`). Thực tế 4. | Vừa | Thêm migration thứ 4 | ✅ Tự sửa |
| N5 | backend/README.md:49-54 | Status drift | "2026-06-11", 35/36 = "Kế", "Nợ SCRUM-14". | Vừa | Sync theo SPRINTS | ✅ Tự sửa |
| N6 | frontend/README.md:30 | File ref không tồn tại | Doc: `components/ # AppLayout (shell)`. Code: `AppLayout.tsx` đã xoá; shell thật = `layouts/MainLayout.tsx`. | Vừa | Sửa AppLayout→MainLayout | ✅ Tự sửa |
| N7 | .claude/CLAUDE.md:49 | Self-consistency + code | "Hiện có 3: ..." migration. Thực tế 4. | Vừa | "Hiện có 4" + tên mới | ✅ Tự sửa |
| N8 | docs/SETUP.md:36 | Self-consistency + code | "hiện có 3 migration: ...". Thực tế 4. | Vừa | "4 migration" + tên mới | ✅ Tự sửa |
| N9 | frontend/README.md:29 | Doc lag (nhỏ) | Pages "Login, Register, Inbox" — thiếu Projects; Login.tsx không có hậu tố Page (lệch quy ước, nhưng là code). | Thấp | Cập nhật danh sách pages | ✅ Tự sửa (chỉ doc) |
| N10 | frontend/README.md:47 | Rác | Trailing whitespace cuối dòng "proxy lo)." | Thấp | Xoá | ✅ Tự sửa |

## Xác nhận 5 điểm đã biết (đã sửa ở commit trước trên branch này — KHÔNG phải phát hiện mới)

| Mã | Tình trạng |
|---|---|
| (a) status 35/36 lệch | ✅ Đã sửa: CLAUDE.md root + docs/CLAUDE.md + .claude (35/36 Done, dừng ở 38) |
| (b) Dev: vs OAuth: | ✅ Đã sửa: .claude Gotcha #4, SPRINTS SCRUM-47 ✅ Done in code |
| (c) SETUP scope readonly | ✅ Đã sửa: read-write (gmail.modify+send/calendar/drive) |
| (d) dòng SCRUM-20 thừa cột + "Antigravity" | ✅ Đã sửa: 4 cột, assignee `—` |
| (e) TODO tách ServiceType (mô hình cũ) | ✅ Đã viết lại: đánh dấu LỖI THỜI, giữ làm lịch sử |

## Kiểm tra PASS (không có lỗi)

- CLAUDE.md root **giống hệt** docs/CLAUDE.md (`diff` = identical). ✓
- API.md: callback trả list connections ✓, admin enable shape khớp `IntegrationResponse` ✓, error format/mapping khớp ExceptionMiddleware ✓.
- Thuật ngữ mô hình A (ServiceConnections/OAuthConnection) chỉ còn ở ngữ cảnh **lịch sử/cấm-dùng** (CHANGELOG, mô tả migration, "đừng tạo lại") — đúng, không phải drift.
- Phase discipline: webhook/Jira đều đánh dấu "phase sau/backlog" ở mọi file. ✓
- ServiceType enum khớp DATABASE SupportedServices. ✓

## Thống kê

- Quét: **13** file.
- Phát hiện mới: **10** (8 vừa + 2 thấp) — tất cả doc-only, tự sửa được.
- Xác nhận đã sửa trước: **5**.
- Cần người quyết: **3** (xem dưới).

## Câu hỏi cần người quyết

1. **SCRUM-47 — tạo issue Jira?** Code đã xong (migration `RemoveClientCredentialsFromIntegration` + bỏ PUT /credentials + đọc `OAuth:`), nhưng chưa có issue Jira tương ứng (ticket số "47*" là tạm). Tạo issue hồi tố để khớp Jira, hay bỏ?
2. **Xoá hẳn `docs/TODO-ServiceType-Split.md`?** Hiện giữ làm lịch sử (đã đánh dấu lỗi thời). Có muốn xoá hẳn cho gọn không?
3. **Assignee SCRUM-14 / SCRUM-20** hiện để `—` (theo quyết định trước "tự điền"). Xác nhận giữ trống hay điền tên thật?
