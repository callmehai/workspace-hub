using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Mapping;
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
        services.AddScoped<IFolderService, FolderService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IConnectionsService, ConnectionsService>();
        services.AddScoped<IItemService, ItemService>();
        services.AddScoped<IItemWriteBackService, ItemWriteBackService>();
        services.AddScoped<IWriteBackGuard, WriteBackGuard>();
        services.AddScoped<IScheduledEmailsService, ScheduledEmailsService>();
        services.AddScoped<IProcessScheduledEmailsService, ProcessScheduledEmailsService>();
        services.AddScoped<ISendEmailService, SendEmailService>();

        // Register OAuth Provider Strategies
        services.AddScoped<IProviderStrategy, GoogleStrategy>();
        services.AddScoped<IProviderStrategy, JiraStrategy>();

        // Quét toàn bộ validator (FluentValidation) trong assembly này.
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        services.AddScoped<IGmailItemMapper, GmailItemMapper>();
        services.AddScoped<IGoogleContactMapper, GoogleContactMapper>();
        services.AddScoped<IGmailSyncService, GmailSyncService>();
        services.AddScoped<IConnectionHealthChecker, ConnectionHealthChecker>();

        // Đăng ký cho Calendar
        services.AddScoped<ICalendarItemMapper, CalendarItemMapper>();
        services.AddScoped<ICalendarSyncService, CalendarSyncService>();
        // Đăng ký cho Drive
        services.AddScoped<IDriveItemMapper, DriveItemMapper>();
        services.AddScoped<IDriveSyncService, DriveSyncService>();
        // Đăng ký cho Jira (SCRUM-55)
        services.AddScoped<IJiraItemMapper, JiraItemMapper>();
        services.AddScoped<IJiraSyncService, JiraSyncService>();
        // Jira metadata helpers (SCRUM-59)
        services.AddScoped<IJiraMetadataService, JiraMetadataService>();
        // Comment + attachment 2 chiều cho ticket Jira
        services.AddScoped<IJiraTicketService, JiraTicketService>();
        // Important contacts (SCRUM-60)
        services.AddScoped<IImportantContactService, ImportantContactService>();

        // Tags (SCRUM-70)
        services.AddScoped<ITagService, TagService>();
        services.AddScoped<IConnectionSyncDispatcher, ConnectionSyncDispatcher>();
        services.AddScoped<ISyncItemNotificationService, SyncItemNotificationService>();
        services.AddScoped<INotificationService, NotificationService>();

        // Avatar upload (SCRUM-75)
        services.AddScoped<IUserProfileService, UserProfileService>();

        return services;
    }
}
