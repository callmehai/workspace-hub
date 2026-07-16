using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Security;
using WorkspaceHub.Infrastructure.Data;
using WorkspaceHub.Infrastructure.Http;
using WorkspaceHub.Infrastructure.Repositories;
using WorkspaceHub.Infrastructure.Security;
using WorkspaceHub.Infrastructure.Services;

namespace WorkspaceHub.Infrastructure;

/// <summary>Đăng ký DbContext + repository của tầng Infrastructure.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration config, bool isDevelopment = false)
    {
        var connectionString = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Thiếu ConnectionStrings:Default. Set qua user-secrets/appsettings (xem docs/SETUP.md).");

        services.AddDbContext<AppDbContext>(opt =>
        {
            opt.UseSqlServer(connectionString);

            // EF query logging để verify SQL sinh ra (SCRUM-25). CHỈ ở Development —
            // EnableSensitiveDataLogging log cả giá trị tham số nên KHÔNG bật ở production.
            if (isDevelopment)
            {
                opt.EnableSensitiveDataLogging();
                opt.EnableDetailedErrors();
            }
        });

        // Data Protection: mã hoá / giải mã token OAuth trước khi lưu DB
        services.AddDataProtection()
            .SetApplicationName("WorkspaceHub")
            .PersistKeysToFileSystem(new DirectoryInfo("dp-keys"));
        services.AddScoped<ITokenProtector, DataProtectionTokenProtector>();
        services.AddScoped<IGoogleTokenVerifier, GoogleTokenVerifier>();

        // SCRUM-63: Redis làm distributed cache (refresh token + OTP + OAuth state).
        // Có ConnectionStrings:Redis → dùng Redis; thiếu → fallback in-memory (dev),
        // log cảnh báo vì refresh token sẽ mất khi restart + không chia sẻ giữa instance.
        var redisConnection = config.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            services.AddStackExchangeRedisCache(o =>
            {
                o.Configuration = redisConnection;
                o.InstanceName = RefreshTokenService.RedisInstanceName; // 1 nguồn — khớp GETDEL atomic
            });
            // IConnectionMultiplexer cho thao tác atomic (GETDEL refresh jti — chống TOCTOU).
            services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(
                _ => StackExchange.Redis.ConnectionMultiplexer.Connect(redisConnection));
        }
        else
        {
            services.AddDistributedMemoryCache();
            // Không có ILogger lúc cấu hình DI → ghi ra console để cảnh báo rõ.
            Console.WriteLine(
                "[WARN] ConnectionStrings:Redis chưa cấu hình — dùng in-memory cache. " +
                "Refresh token (SCRUM-63) sẽ mất khi restart. Xem docs/SETUP.md.");
        }

        // OAuth token exchange (Google/Jira). Timeout rõ ràng để không treo theo default 100s.
        services.AddHttpClient("OAuthToken", c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient("Jira", c =>
        {
            // Accept header cấu hình 1 lần ở DI (tránh .Add tích luỹ mỗi request nếu handler được pool).
            // Authorization vẫn set per-request vì token đổi theo connection.
            c.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        });
        services.AddScoped<IOAuthTokenClient, HttpOAuthTokenClient>();

        services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IFolderRepository, FolderRepository>();
        services.AddScoped<IIntegrationRepository, IntegrationRepository>();
        services.AddScoped<IConnectionRepository, ConnectionRepository>();
        services.AddScoped<IItemRepository, ItemRepository>();
        services.AddScoped<IImportantContactRepository, ImportantContactRepository>();
        services.AddScoped<IGoogleContactRepository, GoogleContactRepository>();
        services.AddScoped<ITagRepository, TagRepository>();
        services.AddScoped<IScheduledEmailRepository, ScheduledEmailRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<ICalendarInvitationRepository, CalendarInvitationRepository>();
        services.AddScoped<IFriendshipRepository, FriendshipRepository>();
        services.AddScoped<IFriendInviteRepository, FriendInviteRepository>();

        services.AddScoped<IJwtTokenFactory, JwtTokenFactory>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddScoped<IOtpService, OtpService>();

        // System email sender (SCRUM-64): dùng Resend thật CHỈ khi đủ ApiKey + FromAddress.
        // Thiếu → LogEmailSender ghi OTP ra console cho dev/demo, KHÔNG gọi Resend.
        // ⚠️ Đây là sender HỆ THỐNG cho OTP, KHÔNG phải Gmail của user (IGmailGateway).
        services.AddHttpClient("Resend");
        var resendConfigured =
            !string.IsNullOrWhiteSpace(config["Email:Resend:ApiKey"]) &&
            !string.IsNullOrWhiteSpace(config["Email:Resend:FromAddress"]);
        if (resendConfigured)
            services.AddScoped<ISystemEmailSender, ResendEmailSender>();
        else
            services.AddScoped<ISystemEmailSender, LogEmailSender>();

        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IAtlassianTokenService, AtlassianTokenService>();
        services.AddScoped<IJiraGateway, JiraGateway>();
        
        // Gateways dùng cho luồng Writeback (Ghi/Cập nhật dữ liệu hai chiều)
        services.AddScoped<IGmailGateway, GmailGateway>();
        services.AddScoped<IPeopleGateway, PeopleGateway>();
        services.AddScoped<ICalendarGateway, CalendarGateway>();
        services.AddScoped<IDriveGateway, DriveGateway>();
        
        // Gateways dùng cho luồng Sync (Đọc/Đồng bộ background job)
        services.AddScoped<IGoogleDriveGateway, GoogleDriveGateway>();
        // AdminService đặt tại Infrastructure vì cần inject AppDbContext trực tiếp
        // (EF projection no-N+1 cho ConnectionCount/ItemCount — xem AdminService.cs).
        services.AddScoped<IAdminService, AdminService>();
        services.AddScoped<IProcessConnectionsSyncService, ProcessConnectionsSyncService>();

        // Object storage (Cloudflare R2) — avatar (SCRUM-75), attachment ở phase sau.
        services.AddScoped<Application.Abstractions.IFileStorageService, R2FileStorageService>();
        return services;
    }
}
