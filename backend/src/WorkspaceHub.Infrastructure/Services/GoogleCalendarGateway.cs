using System.Globalization;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Infrastructure.Services;

public class GoogleCalendarGateway : IGoogleCalendarGateway
{
    private readonly ITokenService _tokenService;

    public GoogleCalendarGateway(ITokenService tokenService)
    {
        _tokenService = tokenService;
    }

    private async Task<CalendarService> BuildCalendarServiceAsync(Connection connection, CancellationToken ct)
    {
        var accessToken = await _tokenService.GetFreshAccessTokenAsync(connection, ct);
        var credential = GoogleCredential.FromAccessToken(accessToken);
        return new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "WorkspaceHub"
        });
    }

    public async Task<CalendarSyncResult> SyncEventsAsync(Connection connection, string? syncToken, CancellationToken ct = default)
    {
        using var service = await BuildCalendarServiceAsync(connection, ct);
        var request = service.Events.List("primary");

        if (!string.IsNullOrEmpty(syncToken))
        {
            request.SyncToken = syncToken;
        }
        else
        {
            request.TimeMinDateTimeOffset = DateTimeOffset.UtcNow.AddMonths(-3);
            request.TimeMaxDateTimeOffset = DateTimeOffset.UtcNow.AddYears(1); // Chỉ đồng bộ các sự kiện tối đa 1 năm tới
        }

        var eventsDto = new List<CalendarEventDto>();
        string? nextSyncToken = null;

        try
        {
            do
            {
                var response = await request.ExecuteAsync(ct);

                if (response.Items != null)
                {
                    foreach (var item in response.Items)
                    {
                        eventsDto.Add(new CalendarEventDto
                        {
                            Id = item.Id,
                            Title = item.Summary ?? string.Empty,
                            Snippet = item.Description ?? string.Empty,
                            Start = ParseEventDateTime(item.Start),
                            End = ParseEventDateTime(item.End),
                            Location = item.Location,
                            Attendees = item.Attendees?
                                .Select(a => a.Email)
                                .Where(e => !string.IsNullOrEmpty(e))
                                .ToList() ?? [],
                            MeetUrl = item.HangoutLink,
                            HtmlLink = item.HtmlLink,
                            OccurredAt = item.UpdatedDateTimeOffset ?? DateTimeOffset.UtcNow
                        });
                    }
                }

                request.PageToken = response.NextPageToken;
                nextSyncToken = response.NextSyncToken;

            } while (!string.IsNullOrEmpty(request.PageToken));

            return new CalendarSyncResult(false, eventsDto, nextSyncToken);
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Gone || ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new CalendarSyncResult(true, new List<CalendarEventDto>(), null);
        }
    }

    private static DateTimeOffset? ParseEventDateTime(EventDateTime? eventTime)
    {
        if (eventTime?.DateTimeDateTimeOffset != null)
            return eventTime.DateTimeDateTimeOffset;

        if (!string.IsNullOrEmpty(eventTime?.Date))
            return DateTimeOffset.Parse(eventTime.Date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

        return null;
    }
}
