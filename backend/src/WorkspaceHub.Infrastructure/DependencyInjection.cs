using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Infrastructure.Data;
using WorkspaceHub.Infrastructure.Repositories;

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

        services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IFolderRepository, FolderRepository>();
        services.AddScoped<IIntegrationRepository, IntegrationRepository>();
        services.AddScoped<IOAuthConnectionRepository, OAuthConnectionRepository>();

        // Register framework services needed by Application layer (OAuth / connections cache & http client)
        services.AddDistributedMemoryCache();
        services.AddHttpClient();

        return services;
    }
}
