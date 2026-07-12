using Microsoft.EntityFrameworkCore;
using WorkspaceHub.Application.DTOs.Emails;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using WorkspaceHub.Infrastructure.Data;

namespace WorkspaceHub.Api.BackgroundJobs;

/// <summary>
/// Background worker to poll and send due reminders via SignalR notifications and/or email.
/// Runs every 60 seconds.
/// </summary>
public class EventReminderProcessorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EventReminderProcessorService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(60);

    public EventReminderProcessorService(
        IServiceScopeFactory scopeFactory,
        ILogger<EventReminderProcessorService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EventReminderProcessorService started — polling every 60s.");

        using var timer = new PeriodicTimer(_interval);
        try
        {
            do
            {
                await ProcessOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // App shutting down
        }
    }

    private async Task ProcessOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
            var sendEmailService = scope.ServiceProvider.GetRequiredService<ISendEmailService>();
            var connectionRepo = scope.ServiceProvider.GetRequiredService<IConnectionRepository>();

            var now = DateTime.UtcNow;

            // Fetch unsent reminders of active (non-archived) events
            var reminders = await db.EventReminders
                .Include(r => r.EventItem)
                .Where(r => !r.IsSent && !r.EventItem.IsArchived)
                .ToListAsync(ct);

            var dueReminders = reminders.Where(r => CalculateTriggerTime(r) <= now).ToList();

            if (dueReminders.Count > 0)
            {
                _logger.LogInformation("Processing {Count} due event reminders.", dueReminders.Count);
            }

            foreach (var reminder in dueReminders)
            {
                try
                {
                    // 1. Send In-App Notification
                    if (reminder.ReminderType == ReminderType.Notification || reminder.ReminderType == ReminderType.Both)
                    {
                        await notificationService.CreateAndSendAsync(
                            userId: reminder.EventItem.UserId,
                            type: NotificationType.CalendarReminder,
                            title: "Nhắc nhở sự kiện: " + reminder.EventItem.Title,
                            body: $"Sự kiện \"{reminder.EventItem.Title}\" sẽ diễn ra lúc {reminder.EventItem.OccurredAt.ToLocalTime():dd/MM/yyyy HH:mm}.",
                            linkUrl: $"/calendar?eventId={reminder.EventItem.Id}",
                            ct: ct
                        );
                    }

                    // 2. Send Email Reminder
                    if (reminder.ReminderType == ReminderType.Email || reminder.ReminderType == ReminderType.Both)
                    {
                        var connections = await connectionRepo.GetByUserIdAsync(reminder.EventItem.UserId, ct);
                        var gmailConn = connections.FirstOrDefault(c => c.ServiceType == ServiceType.Gmail && c.Status == ConnectionStatus.Active);
                        if (gmailConn != null)
                        {
                            var emailReq = new SendEmailRequest
                            {
                                ConnectionId = gmailConn.Id,
                                To = new List<string> { gmailConn.ProviderAccountId },
                                Subject = "Nhắc nhở sự kiện: " + reminder.EventItem.Title,
                                BodyHtml = $@"
                                    <div style='font-family: sans-serif; padding: 20px; border: 1px solid #e2e8f0; border-radius: 8px;'>
                                        <h2 style='color: #4f46e5;'>Nhắc nhở sự kiện sắp diễn ra</h2>
                                        <p>Xin chào,</p>
                                        <p>Sự kiện của bạn <strong>{reminder.EventItem.Title}</strong> sẽ diễn ra vào lúc <strong>{reminder.EventItem.OccurredAt.ToLocalTime():dd/MM/yyyy HH:mm}</strong>.</p>
                                        {(string.IsNullOrEmpty(reminder.EventItem.Snippet) ? "" : $"<p><strong>Mô tả:</strong> {reminder.EventItem.Snippet}</p>")}
                                        <p>Trân trọng,<br/>Workspace Hub Team</p>
                                    </div>"
                            };
                            await sendEmailService.SendAsync(reminder.EventItem.UserId, emailReq, ct);
                        }
                    }

                    reminder.IsSent = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send reminder {ReminderId} for event {EventId}.", reminder.Id, reminder.EventItemId);
                }
            }

            if (dueReminders.Count > 0)
            {
                await db.SaveChangesAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred in EventReminderProcessorService tick.");
        }
    }

    private static DateTime CalculateTriggerTime(EventReminder reminder)
    {
        var occurredAt = reminder.EventItem.OccurredAt;
        var offsetValue = reminder.OffsetValue;
        switch (reminder.OffsetUnit)
        {
            case ReminderUnit.Minutes:
                return occurredAt.AddMinutes(-offsetValue);
            case ReminderUnit.Hours:
                return occurredAt.AddHours(-offsetValue);
            case ReminderUnit.Days:
                return ParseTimeOfDay(occurredAt.AddDays(-offsetValue), reminder.TimeOfDay);
            case ReminderUnit.Weeks:
                return ParseTimeOfDay(occurredAt.AddDays(-offsetValue * 7), reminder.TimeOfDay);
            default:
                return occurredAt;
        }
    }

    private static DateTime ParseTimeOfDay(DateTime baseDate, string? timeOfDay)
    {
        if (!string.IsNullOrEmpty(timeOfDay))
        {
            var parts = timeOfDay.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[0], out var hr) && int.TryParse(parts[1], out var min))
            {
                return new DateTime(baseDate.Year, baseDate.Month, baseDate.Day, hr, min, 0, DateTimeKind.Utc);
            }
        }
        return baseDate;
    }
}
