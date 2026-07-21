# Workspace Hub — System Architecture (Mermaid)

> Sơ đồ kiến trúc hệ thống, sinh từ code thực tế trên nhánh `develop`.
> Xem kèm: `docs/DATABASE.md` (schema), `docs/API.md` (endpoint), `docs/DEPLOY.md` (hạ tầng).
>
> **Bản draw.io (để chèn vào báo cáo / slide):** `docs/diagrams/backend-architecture.drawio` và
> `docs/diagrams/frontend-architecture.drawio` (kèm `.drawio.png` — mở lại bằng draw.io vẫn sửa được — và `.svg`).
>
> **Detailed Design (§4 của SDS):** 14 class + sequence diagram theo 7 nhóm use case ở
> `docs/diagrams/detailed-design/` — danh mục và cách sinh lại: `docs/diagrams/README.md`.

---

## 1. System Context — ai dùng, nối với gì

```mermaid
graph TB
    User["👤 User<br/>(nhân viên / sinh viên)"]
    Admin["👤 Admin<br/>(quản trị hệ thống)"]

    subgraph WH["Workspace Hub"]
        FE["Frontend SPA<br/>Vite + React + TS"]
        API["Backend API<br/>ASP.NET Core 8"]
        DB[("SQL Server")]
        Redis[("Redis<br/>refresh token / OTP")]
    end

    subgraph Google["Google Workspace APIs"]
        Gmail["Gmail API"]
        GCal["Calendar API"]
        Drive["Drive API"]
        People["People API"]
    end

    Atlassian["Atlassian / Jira Cloud<br/>OAuth 3LO + REST v3"]
    Resend["Resend<br/>(email OTP / mời bạn)"]
    R2["Cloudflare R2<br/>(file đính kèm)"]
    Cron["Cron / Scheduler<br/>X-Cron-Secret"]

    User --> FE
    Admin --> FE
    FE -->|"REST /api/*<br/>HttpOnly cookie + CSRF"| API
    API --> DB
    API --> Redis
    API -->|OAuth 2.0 + REST| Gmail
    API --> GCal
    API --> Drive
    API --> People
    API -->|OAuth 3LO + cloudId| Atlassian
    API --> Resend
    API --> R2
    Cron -->|"/api/internal/process-scheduled<br/>/api/internal/process-sync"| API
```

---

## 2. Kiến trúc backend — 4 project, layered

```mermaid
graph TB
    subgraph Api["WorkspaceHub.Api — Presentation"]
        direction TB
        Ctrl["Controllers<br/>Auth · Connections · Items · Folders<br/>Emails · Drive · Jira · Tags · Notifications<br/>Admin · Internal · Friends"]
        MW["Middleware<br/>Exception · RequestLogging · Csrf"]
        Auth["JWT Bearer (cookie)<br/>+ Swagger + OData base"]
    end

    subgraph App["WorkspaceHub.Application — Business"]
        direction TB
        Svc["Services<br/>AuthService · ConnectionsService<br/>GmailSync · CalendarSync · DriveSync · JiraSync<br/>ItemService · ItemWriteBackService · FolderService<br/>ScheduledEmails · Notification · Tag · Friend"]
        Guard["IWriteBackGuard<br/>(ETag / fields.updated conflict)"]
        OAuthS["OAuth Strategies<br/>IProviderStrategy → Google | Jira"]
        DTO["DTOs · Validators (FluentValidation) · Mapping"]
    end

    subgraph Infra["WorkspaceHub.Infrastructure — Data & Integrations"]
        direction TB
        Repo["Repositories<br/>Generic + Item/Folder/Connection/User/..."]
        Ctx["AppDbContext (EF Core 8)<br/>+ Migrations + UtcDateTimeConverter"]
        GW["Provider Gateways<br/>GmailGateway · CalendarGateway<br/>GoogleDriveGateway · PeopleGateway · JiraGateway"]
        Sec["Security<br/>DataProtection token protector<br/>JwtTokenFactory · RefreshTokenService · OtpService<br/>GoogleTokenVerifier"]
        Ext["ResendEmailSender · R2FileStorage<br/>ProcessConnectionsSyncService"]
    end

    subgraph Dom["WorkspaceHub.Domain — Core"]
        Ent["Entities<br/>User · Connection · Integration · Item<br/>Folder · FolderShare · ItemFolder · Tag/TagAssignment<br/>ScheduledEmail · Notification · Friendship/FriendInvite<br/>CalendarInvitation · EventReminder · ImportantContact"]
        Enum["Enums (lưu string)"]
    end

    Ctrl --> Svc
    MW -.-> Ctrl
    Svc --> Guard
    Svc --> OAuthS
    Svc --> Repo
    Svc --> GW
    Svc --> Sec
    Svc --> Ext
    Repo --> Ctx
    Ctx --> Ent
    Svc --> DTO
    App --> Dom
    Infra --> Dom
```

**Nguyên tắc phụ thuộc:** `Api → Application → Domain`, `Infrastructure → Application/Domain`.
Controller mỏng, không business logic. Không expose Entity ra API (luôn qua DTO). DI cho mọi service/repo.

---

## 3. Mô hình dữ liệu cốt lõi (rút gọn)

```mermaid
erDiagram
    USER ||--o{ CONNECTION : "sở hữu"
    USER ||--o{ FOLDER : "tạo"
    USER ||--o{ ITEM : "sở hữu"
    USER ||--o{ SCHEDULED_EMAIL : "hẹn giờ"
    USER ||--o{ TAG : "định nghĩa"
    USER ||--o{ NOTIFICATION : "nhận"
    USER ||--o{ FRIENDSHIP : "kết bạn"

    INTEGRATION ||--o{ CONNECTION : "loại dịch vụ"
    CONNECTION ||--o{ ITEM : "nguồn đồng bộ"

    FOLDER ||--o{ ITEM_FOLDER : ""
    ITEM   ||--o{ ITEM_FOLDER : ""
    FOLDER ||--o{ FOLDER_SHARE : "chia sẻ (Viewer)"
    USER   ||--o{ FOLDER_SHARE : "được share"

    ITEM ||--o{ TAG_ASSIGNMENT : ""
    TAG  ||--o{ TAG_ASSIGNMENT : ""

    USER {
        guid Id PK
        string Email UK
        string Role "Admin|User"
        bool EmailVerified
    }
    CONNECTION {
        guid Id PK
        guid UserId FK
        string ServiceType "Gmail|Calendar|Drive|Jira"
        string AccessTokenEncrypted "DataProtection"
        string RefreshTokenEncrypted
        datetime LastSyncedAt
    }
    ITEM {
        guid Id PK
        string Type "Email|Event|File|Note|Ticket"
        string ExternalId
        string ETag "conflict token"
        string Status "Inbox|Doing|Done"
        string MetadataJson "nvarchar(max)"
    }
    FOLDER {
        guid Id PK
        guid OwnerId FK
        string Name
        bool IsArchived
    }
```

> Quy ước: PK `Guid`, timestamp UTC `datetime2`, enum lưu string, JSON `nvarchar(max)`,
> KHÔNG soft delete (dùng `IsArchived`), junction dùng composite PK.

---

## 4. Luồng OAuth — Sign-In vs Connect service (khác nhau!)

```mermaid
sequenceDiagram
    autonumber
    actor U as User
    participant FE as Frontend SPA
    participant API as API
    participant P as Provider (Google/Atlassian)
    participant DB as SQL Server

    rect rgb(235,245,255)
    note over U,DB: A. Google Sign-In — chỉ ĐĂNG NHẬP (scope openid/email/profile)
    U->>FE: Bấm "Đăng nhập với Google"
    FE->>P: OAuth (id_token)
    P-->>FE: id_token
    FE->>API: POST /api/auth/google
    API->>API: GoogleTokenVerifier xác thực id_token
    API->>DB: tìm/tạo User (KHÔNG tạo Connection)
    API-->>FE: Set-Cookie access (HttpOnly) + refresh + CSRF token
    end

    rect rgb(240,255,240)
    note over U,DB: B. Connect service — CẤP QUYỀN đọc/ghi (mô hình B: 1 service = 1 Connection, full scope)
    U->>FE: Bật Gmail / Calendar / Drive / Jira
    FE->>API: POST /api/connections/initiate {serviceType}
    API->>API: IProviderStrategy.BuildAuthUrl + state payload
    API-->>FE: authUrl
    FE->>P: redirect consent (full scope của service)
    P-->>API: callback ?code&state
    API->>P: exchange code → access/refresh token (+ cloudId nếu Jira)
    API->>DB: lưu Connection (token mã hoá bằng Data Protection)
    API->>API: sync lần đầu → Items
    end
```

---

## 5. Luồng đồng bộ 2 chiều (đọc on-demand + write-back synchronous)

```mermaid
flowchart LR
    subgraph Read["📥 Đọc — on-demand + cron định kỳ (SCRUM-72)"]
        direction TB
        R1["FE gọi GET /api/items<br/>hoặc cron /api/internal/process-sync"]
        R2["ConnectionSyncDispatcher"]
        R3["GmailSyncService<br/>CalendarSyncService<br/>DriveSyncService<br/>JiraSyncService"]
        R4["Gateway gọi Provider API"]
        R5["Upsert Items theo ExternalId<br/>lưu ETag / fields.updated"]
        R6["SyncItemNotificationService<br/>→ Notification in-app"]
        R1 --> R2 --> R3 --> R4 --> R5 --> R6
    end

    subgraph Write["📤 Ghi ngược — synchronous"]
        direction TB
        W1["PATCH / POST / DELETE /api/items"]
        W2["ItemWriteBackService"]
        W3{"IWriteBackGuard<br/>ETag khớp?"}
        W4["Gọi Provider API (ghi thật)"]
        W5["Cập nhật Item + ETag mới"]
        W6["409 Conflict"]
        W1 --> W2 --> W3
        W3 -- khớp --> W4 --> W5
        W3 -- lệch --> W6
    end

    Read -.->|"KHÔNG webhook / push realtime<br/>(ngoài scope)"| Write
```

**Khả năng write-back theo loại item:**

| Item | Ghi được | Không ghi được |
|---|---|---|
| Email (Gmail) | label, read/unread, star, trash, **gửi mới** | sửa nội dung (Gmail immutable) |
| Event (Calendar) | CRUD đầy đủ | — |
| File (Drive) | rename, trash, upload, share | sửa nội dung file |
| Ticket (Jira) | tạo / update / xoá (ADF 2 chiều) | — |

Mã lỗi: `403` thiếu scope · `409` conflict ETag · `502` provider lỗi.

---

## 6. Kiến trúc frontend

```mermaid
graph TB
    Router["router.tsx (React Router)"]

    subgraph Pages["src/pages"]
        P1["Login / RegisterPage (OTP email)"]
        P2["Inbox · KanbanBoard · CalendarPage"]
        P3["Integrations / connections"]
        P4["SendEmail · ScheduledEmails"]
        P5["Friends · ProfilePage"]
        P6["AdminDashboard"]
    end

    Comp["src/components — UI tái dùng (Tailwind)"]
    Hooks["src/hooks — TanStack Query hooks<br/>(KHÔNG useEffect + fetch thủ công)"]
    Lib["src/lib — axios instance<br/>credentials + CSRF header + refresh interceptor"]
    Ctx["src/context — Auth / Theme"]
    Toast["react-hot-toast"]

    Router --> Pages
    Pages --> Comp
    Pages --> Hooks
    Comp --> Hooks
    Hooks --> Lib
    Lib -->|"REST /api"| BE["Backend API"]
    Ctx -.-> Pages
    Hooks -.-> Toast
```

---

## 7. Deployment — AWS Lightsail (Docker Compose)

```mermaid
graph TB
    Dev["Developer"] -->|"PR → merge develop"| GH["GitHub"]
    GH -->|"ci.yml: dotnet build/test<br/>npm install/lint/build"| CI["GitHub Actions CI"]
    GH -->|"deploy.yml: SSH + git reset --hard<br/>docker compose up -d --build"| LS

    subgraph LS["AWS Lightsail — 2GB, Singapore"]
        direction TB
        Caddy["web: Caddy<br/>auto-HTTPS, serve SPA, proxy /api"]
        ApiC["api: .NET 8<br/>Db__AutoMigrate=true"]
        Sql[("mssql — cap 1GB RAM")]
        Rds[("redis")]
        Caddy --> ApiC
        ApiC --> Sql
        ApiC --> Rds
    end

    Browser["🌐 app.workspace-hub.space"] --> Caddy
    ApiC --> Ext["Google APIs · Jira Cloud<br/>Resend · Cloudflare R2"]
    CronJob["Cron (X-Cron-Secret)"] --> ApiC
```

> Secret prod nằm ở `.env` trên server — **không commit**. Chi tiết vận hành: `docs/DEPLOY.md`.
