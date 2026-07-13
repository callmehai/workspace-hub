using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using WorkspaceHub.Infrastructure.Data;

namespace WorkspaceHub.Api.BackgroundJobs;

/// <summary>
/// Polls due Workspace Hub in-app calendar reminders every 60 seconds.
/// Google popup/email reminders are handled by Google Calendar.
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
        _logger.LogInformation("EventReminderProcessorService started - polling every 60s.");

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
            // App shutting down.
        }
    }

    private async Task ProcessOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

            var now = DateTime.UtcNow;

            var reminders = await db.EventReminders
                .Include(r => r.EventItem)
                .Where(r => !r.IsSent && !r.EventItem.IsArchived)
                .ToListAsync(ct);

            var dueReminders = reminders
                .Where(r => r.ReminderType == ReminderType.InApp && CalculateTriggerTime(r) <= now)
                .ToList();

            if (dueReminders.Count > 0)
            {
                _logger.LogInformation("Processing {Count} due in-app event reminders.", dueReminders.Count);
            }

            foreach (var reminder in dueReminders)
            {
                try
                {
                    var body = JsonSerializer.Serialize(new
                    {
                        itemTitle = reminder.EventItem.Title,
                        preview = string.IsNullOrWhiteSpace(reminder.EventItem.Snippet)
                            ? reminder.EventItem.OccurredAt.ToString("o")
                            : reminder.EventItem.Snippet,
                        start = reminder.EventItem.OccurredAt.ToString("o")
                    });

                    await notificationService.CreateAndSendAsync(
                        userId: reminder.EventItem.UserId,
                        type: NotificationType.CalendarReminder,
                        title: "notifications.calendarReminder",
                        body: body,
                        linkUrl: $"/calendar?eventId={reminder.EventItem.Id}",
                        ct: ct
                    );

                    reminder.IsSent = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send in-app reminder {ReminderId} for event {EventId}.", reminder.Id, reminder.EventItemId);
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
        return reminder.OffsetUnit switch
        {
            ReminderUnit.Minutes => occurredAt.AddMinutes(-offsetValue),
            ReminderUnit.Hours => occurredAt.AddHours(-offsetValue),
            ReminderUnit.Days => ParseTimeOfDay(occurredAt.AddDays(-offsetValue), reminder.TimeOfDay),
            ReminderUnit.Weeks => ParseTimeOfDay(occurredAt.AddDays(-offsetValue * 7), reminder.TimeOfDay),
            _ => occurredAt
        };
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
