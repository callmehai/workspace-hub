using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Infrastructure.Services;
using WorkspaceHub.Infrastructure.Data;
using WorkspaceHub.Infrastructure.Http;
using WorkspaceHub.Infrastructure.Repositories;
using WorkspaceHub.Application.Security;
using WorkspaceHub.Infrastructure.Security;

namespace WorkspaceHub.Infrastructure;

/// <summary>Đăng ký DbContext + repository của tầng Infrastructure.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Thiếu ConnectionStrings:Default. Set qua user-secrets/appsettings (xem docs/SETUP.md).");

        services.AddDbContext<AppDbContext>(opt => opt.UseSqlServer(connectionString));

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


        // AdminService đặt tại Infrastructure vì cần inject AppDbContext trực tiếp
        // (EF projection no-N+1 cho ConnectionCount/ItemCount — xem AdminService.cs).
        services.AddScoped<IAdminService, AdminService>();

        return services;
    }
}
