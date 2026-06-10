using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Services;

namespace WorkspaceHub.Application;

/// <summary>Đăng ký service + validator của tầng Application.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IHealthService, HealthService>();
        services.AddScoped<IFolderService, FolderService>();

        // Quét toàn bộ validator trong assembly này (hiện chưa có — sẽ thêm từ SCRUM-9).
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        return services;
    }
}
