using System.Text;
using System.Text.Json.Serialization;
using FluentValidation;
using FluentValidation.AspNetCore;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.OData;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OData.ModelBuilder;
using Microsoft.OpenApi.Models;
using WorkspaceHub.Api.Auth;
using WorkspaceHub.Api.Hubs;
using WorkspaceHub.Api.Middleware;
using WorkspaceHub.Application;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.DTOs.Notifications;
using WorkspaceHub.Application.DTOs.Emails;
using WorkspaceHub.Application.DTOs.ScheduledEmails;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// OData EDM model — expose FolderResponse cho $filter/$orderby/$select/$top/$skip/$count.
var edmBuilder = new ODataConventionModelBuilder();
edmBuilder.EnableLowerCamelCase(); // Force camelCase cho tất cả OData response
edmBuilder.EntitySet<FolderResponse>("Folders");
var scheduledEmails = edmBuilder.EntitySet<ScheduledEmailDto>("ScheduledEmails");
scheduledEmails.EntityType.HasKey(e => e.Id);
var contactSuggestionType = edmBuilder.EntityType<ContactSuggestionDto>();
contactSuggestionType.HasKey(c => c.Email);
edmBuilder.EntitySet<ContactSuggestionDto>("EmailContactSuggestions");
var notifications = edmBuilder.EntitySet<NotificationDto>("Notifications");
notifications.EntityType.HasKey(n => n.Id);

// SCRUM-64: đọc IP thật của client từ X-Forwarded-For do reverse-proxy (Caddy) gắn — cần cho
// rate limit partition theo IP. Service `api` KHÔNG expose port ra ngoài (chỉ Caddy 80/443 tới
// được) nên clear KnownNetworks/KnownProxies là an toàn (Caddy trong docker network có IP động).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// SCRUM-64: rate limit theo IP cho các endpoint OTP — chống spam đốt quota email (Resend free
// tier). Cooldown OtpService theo userId không chặn được register hàng loạt email khác nhau, nên
// cần chặn ở tầng IP. 429 khi vượt. Partition theo IP THẬT của client (X-Forwarded-For qua
// UseForwardedHeaders) — sau reverse-proxy Caddy, RemoteIpAddress = IP container Caddy nên nếu
// không đọc header thì mọi user chung 1 partition = giới hạn toàn cục.
// register + send-otp tách 2 policy để KHÔNG chia chung hạn mức (đăng ký ≠ gửi lại mã).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    static RateLimitPartition<string> PerIpFixedWindow(HttpContext ctx, int permitLimit) =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });

    options.AddPolicy("otp-register", ctx => PerIpFixedWindow(ctx, 5));
    options.AddPolicy("otp-send", ctx => PerIpFixedWindow(ctx, 5));
});

// Controllers + serialize enum dạng string (khớp cách lưu DB) + OData.
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .AddOData(o => o
        .SetMaxTop(100)
        .Filter()
        .OrderBy()
        .Select()
        .Expand()
        .Count()
        .SkipToken()
        .AddRouteComponents("api", edmBuilder.GetEdmModel()));

builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options =>
{
    // Factory này chạy khi ASP.NET Core model binder thất bại TRƯỚC FluentValidation:
    // ví dụ sai kiểu JSON, thiếu [Required] property. Lỗi FluentValidation đi qua ExceptionMiddleware.
    options.InvalidModelStateResponseFactory = context =>
    {
        var traceId = context.HttpContext.TraceIdentifier;
        var details = context.ModelState
            .Where(ms => ms.Value!.Errors.Any())
            .SelectMany(ms => ms.Value!.Errors.Select(e => $"{ms.Key}: {e.ErrorMessage}"))
            .ToArray();

        // Log để truy vết request thất bại do model binding — tương tự cách ExceptionMiddleware
        // log ValidationException. Không có dòng này thì lỗi biến mất khỏi server log.
        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("ModelBinding");
        logger.LogWarning(
            "Model binding failed ({Count} error(s)). Path={Path}, TraceId={TraceId}",
            details.Length, context.HttpContext.Request.Path, traceId);

        var body = new
        {
            error = "ValidationError",
            message = "One or more validation errors occurred.",
            details,
            traceId
        };

        return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(body);
    };
});

builder.Services.AddMemoryCache();

// Swagger.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new() { Title = "Workspace Hub API", Version = "v1" });
    // Giải quyết xung đột giữa OData convention route và attribute route của Controller
    o.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());

    // Cấu hình Swagger hỗ trợ gửi JWT Bearer Token
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token"
    });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// Authentication & Authorization
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    var jwtSection = builder.Configuration.GetSection("Jwt");
    // Không fallback secret mặc định — thiếu config thì fail ngay lúc startup (CLAUDE.md: không hardcode secret).
    var secretKey = jwtSection["Secret"];
    if (string.IsNullOrEmpty(secretKey))
        throw new InvalidOperationException("Jwt:Secret is not configured. Set it in appsettings or user-secrets.");

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSection["Issuer"] ?? "WorkspaceHub",
        ValidAudience = jwtSection["Audience"] ?? "WorkspaceHub",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
        ClockSkew = TimeSpan.Zero
    };

    // SCRUM-62: đọc JWT từ HttpOnly cookie wh_access. ƯU TIÊN header Authorization: Bearer
    // (Swagger/Postman/server-to-server) — chỉ dùng cookie khi KHÔNG có Bearer.
    // Lưu ý: OnMessageReceived fire TRƯỚC khi handler tự đọc header nên ctx.Token luôn rỗng
    // lúc này → phải tự kiểm tra header, không thể dựa vào ctx.Token.
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = ctx =>
        {
            var authHeader = ctx.Request.Headers.Authorization.ToString();
            if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return Task.CompletedTask; // có Bearer → để handler tự đọc header

            if (ctx.Request.Cookies.TryGetValue(AuthCookieService.AccessCookieName, out var cookieToken))
                ctx.Token = cookieToken;

            if (string.IsNullOrEmpty(ctx.Token)
                && ctx.Request.Path.StartsWithSegments("/api/hubs")
                && ctx.Request.Query.TryGetValue("access_token", out var accessToken))
                ctx.Token = accessToken;

            return Task.CompletedTask;
        }
    };
});

// CORS cho cookie auth cross-site (prod). Dev dùng Vite proxy → same-origin, không cần.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
if (allowedOrigins is { Length: > 0 })
{
    builder.Services.AddCors(o => o.AddPolicy("frontend", p => p
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials())); // cookie cross-site cần credentials; KHÔNG kèm AllowAnyOrigin
}

builder.Services.AddSingleton<AuthCookieService>();

builder.Services.AddSignalR();
builder.Services.AddSingleton<IUserIdProvider, SubUserIdProvider>();
builder.Services.AddScoped<INotificationPublisher, SignalRNotificationPublisher>();

// Register application and infrastructure services
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.IsDevelopment());

// Auto-cron gửi scheduled email — CHỈ khi Cron:AutoRun=true (mặc định prod=false, dev=true).
// Thiết kế gốc dùng cron ngoài gọi /api/internal/process-scheduled; đây là tuỳ chọn tiện lợi.
if (builder.Configuration.GetValue<bool>("Cron:AutoRun"))
{
    builder.Services.AddHostedService<WorkspaceHub.Api.BackgroundJobs.ScheduledEmailProcessorService>();
}
if (builder.Configuration.GetValue<bool>("Cron:SyncAutoRun"))
{
    builder.Services.AddHostedService<WorkspaceHub.Api.BackgroundJobs.ConnectionSyncProcessorService>();
}
builder.Services.AddHostedService<WorkspaceHub.Api.BackgroundJobs.EventReminderProcessorService>();

var app = builder.Build();

// Prod deploy 1-instance: tự áp EF migration lúc khởi động khi Db:AutoMigrate=true.
// Mặc định TẮT — môi trường multi-instance nên chạy migration riêng (tránh race).
if (app.Configuration.GetValue<bool>("Db:AutoMigrate"))
{
    using var migrateScope = app.Services.CreateScope();
    migrateScope.ServiceProvider
        .GetRequiredService<WorkspaceHub.Infrastructure.Data.AppDbContext>()
        .Database.Migrate();
}

// SCRUM-64: áp X-Forwarded-For SỚM NHẤT để RemoteIpAddress = IP thật client (dùng cho rate
// limit partition + log). Sau reverse-proxy Caddy, thiếu bước này thì mọi IP = IP container Caddy.
app.UseForwardedHeaders();

// Request logging (SCRUM-25) — đặt NGOÀI CÙNG để đo trọn thời gian xử lý và đọc đúng
// status code cuối (kể cả 5xx do ExceptionMiddleware set sau khi nuốt exception).
app.UseMiddleware<RequestLoggingMiddleware>();

// Exception middleware — bắt mọi lỗi → error format chuẩn (SCRUM-24 / CONVENTIONS.md).
app.UseMiddleware<ExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseStaticFiles();
    app.UseSwagger();
    app.UseSwaggerUI(o => o.InjectJavascript("/swagger-auto-auth.js"));
}

app.UseHttpsRedirection();

if (allowedOrigins is { Length: > 0 })
    app.UseCors("frontend");

app.UseRateLimiter(); // SCRUM-64: áp policy "otp" cho register + send-otp

app.UseAuthentication();
// CSRF double-submit check (SCRUM-62) — sau Authentication (cần biết request dùng cookie),
// trước Authorization/endpoint để chặn sớm request mutating thiếu token.
app.UseMiddleware<CsrfMiddleware>();
app.UseAuthorization();
app.MapControllers();
// Hub nằm DƯỚI /api để dùng chung 1 luật reverse-proxy /api/* (Caddy prod) — tránh phải
// proxy riêng /hubs và tránh 405 khi negotiate. SignalR không bắt buộc prefix, đường tự chọn.
app.MapHub<NotificationHub>("/api/hubs/notifications");

app.Run();

public partial class Program { }
