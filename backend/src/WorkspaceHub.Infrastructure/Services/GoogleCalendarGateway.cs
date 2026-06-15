using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
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
            request.TimeMin = DateTime.UtcNow.AddMonths(-3);
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
                        var evDto = new CalendarEventDto
                        {
                            Id = item.Id,
                            Title = item.Summary ?? string.Empty,
                            Snippet = item.Description ?? string.Empty,
                            Start = item.Start?.DateTime != null ? new DateTimeOffset(item.Start.DateTime.Value) : (item.Start?.Date != null ? DateTimeOffset.Parse(item.Start.Date) : null),
                            End = item.End?.DateTime != null ? new DateTimeOffset(item.End.DateTime.Value) : (item.End?.Date != null ? DateTimeOffset.Parse(item.End.Date) : null),
                            Location = item.Location,
                            Attendees = item.Attendees?.Select(a => a.Email).ToList() ?? new List<string>(),
                            MeetUrl = item.HangoutLink ?? item.HtmlLink,
                            OccurredAt = item.UpdatedDateTimeOffset ?? DateTimeOffset.UtcNow
                        };
                        eventsDto.Add(evDto);
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
}