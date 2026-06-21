# [LỖI THỜI — KHÔNG IMPLEMENT] Tách ServiceType thành read/write riêng lẻ

> **Trạng thái: ĐÃ BỊ MÔ HÌNH B THAY THẾ. Giữ file làm lịch sử, đừng code theo.**
> File này viết theo mô hình A cũ (ServiceConnections, OAuthConnection→ServiceConnection, scope `*Readonly`). Hiện trạng đã khác hẳn — xem 3 lý do bên dưới trước khi định đụng vào enum/schema.

## Vì sao không còn làm

1. **Không còn `ServiceConnections`.** Mô hình B (SCRUM-34, đã migrate) gộp `OAuthConnections` + `ServiceConnections` thành một bảng `Connections` — mỗi service = 1 row, token riêng. Toàn bộ phần "data migration repoint ServiceConnections", "ServiceConnectionId", "ServiceConnectionSync" trong bản cũ không còn đối tượng để áp dụng.

2. **Quy ước "bật service = FULL scope" → tách read/write ở mức connection là trái thiết kế.** CLAUDE.md chốt: không có cột `Permission`/`AccessLevel`, không có trạng thái "một phần quyền". Connect một service Google = cấp full scope read-write của service đó (Gmail luôn kèm `gmail.modify` + `gmail.send`). Vì vậy "user đã grant write chưa" = "có Connection Gmail Active chưa" — không cần enum `GmailRead`/`GmailSend` tách rời. `GoogleScopes.ServiceScopes` đã map mỗi `ServiceType` sang đúng bộ scope read-write cố định.

3. **Jira ngoài scope.** Mọi nội dung `JiraRead`/`JiraWrite`/`ReadJiraWork`/`WriteJiraWork` thuộc về Jira/Atlassian — **hiện không có ticket Jira** (đã bỏ khỏi backlog; số SCRUM-39→46 nay dùng cho việc khác). Đừng thêm enum/scope Jira ở giai đoạn này.

## Nếu sau này phát sinh nhu cầu thật

- **Google:** hiện gần như không còn nhu cầu tách — write đi kèm read trong cùng scope service. Nếu phát sinh case thật (vd muốn cho phép connect Gmail chỉ-đọc), **dừng và bàn lại thiết kế** (sẽ phải xét lại quy ước "bật = full"), đừng tự thêm enum.
- **Jira:** chỉ xem lại **nếu/khi Jira được đưa trở lại scope và có ticket riêng** — lúc đó mới quyết mô hình read/write cho Atlassian scope (`read:jira-work` vs `write:jira-work`), dựa trên mô hình B hiện hành chứ không phải bản cũ này.

---

<details>
<summary>📜 Nội dung gốc (mô hình A — chỉ để tham khảo lịch sử)</summary>

`ServiceType` enum gộp read + write vào 1 giá trị (`Gmail` = đọc + gửi; `Jira` = đọc + tạo/sửa). Bản cũ đề xuất thêm `GmailRead`/`GmailSend`/`JiraRead`/`JiraWrite`, sửa `ServicesFromGrantedScopes`, data migration repoint `ServiceConnections`, cập nhật seed `SupportedServices`, và đồng bộ frontend theo chuỗi `serviceType` mới. Toàn bộ dựa trên bảng `ServiceConnections` (mô hình A) nay không còn tồn tại.

</details>
