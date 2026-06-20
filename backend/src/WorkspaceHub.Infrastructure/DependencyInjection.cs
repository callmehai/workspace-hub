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

        services.AddDistributedMemoryCache();

        services.AddHttpClient("OAuthToken");
        services.AddScoped<IOAuthTokenClient, HttpOAuthTokenClient>();

        services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IFolderRepository, FolderRepository>();
        services.AddScoped<IIntegrationRepository, IntegrationRepository>();
        services.AddScoped<IConnectionRepository, ConnectionRepository>();
        services.AddScoped<IItemRepository, ItemRepository>();
        services.AddScoped<IImportantContactRepository, ImportantContactRepository>();
        services.AddScoped<IScheduledEmailRepository, ScheduledEmailRepository>();

        services.AddScoped<WorkspaceHub.Application.Abstractions.ITokenService, WorkspaceHub.Infrastructure.Services.TokenService>();
        services.AddScoped<WorkspaceHub.Application.Abstractions.IGmailGateway, WorkspaceHub.Infrastructure.Services.GmailGateway>();
        services.AddScoped<WorkspaceHub.Application.Abstractions.ICalendarGateway, WorkspaceHub.Infrastructure.Services.CalendarGateway>();
        services.AddScoped<WorkspaceHub.Application.Abstractions.IDriveGateway, WorkspaceHub.Infrastructure.Services.DriveGateway>();
        services.AddScoped<WorkspaceHub.Application.Abstractions.IGoogleCalendarGateway, WorkspaceHub.Infrastructure.Services.GoogleCalendarGateway>();
        services.AddScoped<WorkspaceHub.Application.Abstractions.IGoogleDriveGateway, WorkspaceHub.Infrastructure.Services.GoogleDriveGateway>();
        // AdminService đặt tại Infrastructure vì cần inject AppDbContext trực tiếp
        // (EF projection no-N+1 cho ConnectionCount/ItemCount — xem AdminService.cs).
        services.AddScoped<IAdminService, AdminService>();

        return services;
    }
}
