using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.OAuth.Core;
using WorkspaceHub.Application.OAuth.Providers.Google;
using WorkspaceHub.Application.OAuth.Providers.Jira;
using WorkspaceHub.Application.Services;

namespace WorkspaceHub.Application;

/// <summary>Đăng ký service + validator của tầng Application.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IHealthService, HealthService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IConnectionsService, ConnectionsService>();

        // Provider strategies — thêm provider mới: đăng ký thêm 1 dòng ở đây.
        services.AddScoped<IProviderStrategy, GoogleStrategy>();
        services.AddScoped<IProviderStrategy, JiraStrategy>();

        // Quét toàn bộ validator trong assembly này (hiện chưa có — sẽ thêm từ SCRUM-9).
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        return services;
    }
}
