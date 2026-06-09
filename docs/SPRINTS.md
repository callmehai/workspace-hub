# Sprints & Tickets — Workspace Hub

> Bản đồ ticket cho Claude Code biết phần nào thuộc ai và phụ thuộc nhau ra sao. Khi code một ticket, đọc dependency để biết cần gì trước.

## Team
| Tên | Vai trò chính |
|---|---|
| Hải | Lead — foundation (solution, schema, Data Protection) |
| Lộc | Auth + một phần connection/optimization |
| Khánh | OAuth (start + callback) |
| Vũ | Sync Gmail + scheduled email |
| Huy | Folders / Items / filter / Admin |
| Dũng | Frontend + sync stretch |

## Critical path (chuỗi block — ưu tiên cao nhất)
```
SCRUM-5 (solution) → SCRUM-6 (schema) → mọi thứ
SCRUM-6 → SCRUM-13 (OAuth callback) → SCRUM-15 → SCRUM-16 (sync)
```
SCRUM-6 (schema) phải xong sớm nhất vì chặn cả team.

---

## Sprint 1 (8–14 Jun) — Foundation & Auth

| Ticket | Mô tả | Assignee | Dependency |
|---|---|---|---|
| SCRUM-5 | Khởi tạo solution ASP.NET Core + layer architecture | Hải | — |
| SCRUM-6 | EF Core schema + migrations toàn bộ MVP | Hải | SCRUM-5 |
| SCRUM-7 | Setup Data Protection mã hoá token | Vũ | SCRUM-5 |
| SCRUM-8 | GitHub repo, branching, README | Dũng | — |
| SCRUM-9 | Register + login + BCrypt + JWT | Lộc | SCRUM-6 |
| SCRUM-10 | JWT middleware + protected route + GET /me | Lộc | SCRUM-9 |
| SCRUM-11 | Role-based authorization + logout | Khánh | SCRUM-10 |
| SCRUM-12 | OAuth start flow + đăng ký app Google Cloud | Khánh | SCRUM-6 |
| SCRUM-18 | Folder CRUD | Huy | SCRUM-6, SCRUM-10 |
| SCRUM-19 | Items list + filter + pagination + search | Huy | SCRUM-6, SCRUM-10 |
| SCRUM-21 | Frontend setup: routing, layout, protected route | Dũng | — |

## Sprint 2 (15–22 Jun) — OAuth & Sync

| Ticket | Mô tả | Assignee | Dependency |
|---|---|---|---|
| SCRUM-13 | OAuth callback + lưu token encrypted | Khánh | SCRUM-12, SCRUM-7 |
| SCRUM-14 | List / disconnect / refresh connection | Lộc | SCRUM-13 |
| SCRUM-15 | Gmail client + lấy message → Item | Vũ | SCRUM-13 |
| SCRUM-16 | Cron sync định kỳ + dedupe | Vũ | SCRUM-15 |
| SCRUM-17 | Sync Calendar + Drive (stretch) | Dũng | SCRUM-16 |
| SCRUM-20 | Kanban status + Note CRUD + ItemFolders | Huy | SCRUM-18, SCRUM-19 |
| SCRUM-22 | Auth pages (login/register) nối API | Dũng | SCRUM-9, SCRUM-21 |
| SCRUM-23 | Admin API: users + stats (stretch) | Huy | SCRUM-11 |
| SCRUM-25 | Logging + optimize queries | Hải | SCRUM-19 |

## Sprint 3 (22–29 Jun) — Optimization, Testing & Scheduled Email

| Ticket | Mô tả | Assignee | Dependency |
|---|---|---|---|
| SCRUM-24 | Exception middleware + error format chuẩn | Lộc | endpoint chính |
| SCRUM-26 | Refactor services + clean architecture | Khánh | feature ổn định |
| SCRUM-27 | API testing + Postman collection | Huy | endpoint hoàn thành |
| SCRUM-28 | README backend + setup guide | Dũng | cuối sprint |
| SCRUM-29 | Unit test cho service chính (stretch) | Vũ | SCRUM-26 |
| SCRUM-30 | Scheduled email: tạo / list / cancel | Vũ | SCRUM-13, SCRUM-6 |
| SCRUM-31 | Cron process-scheduled: gửi qua Gmail | Lộc | SCRUM-30, SCRUM-15 |

## Stretch (cắt đầu tiên nếu thiếu thời gian)
SCRUM-17 (Calendar/Drive sync), SCRUM-23 (Admin), SCRUM-29 (unit test).
