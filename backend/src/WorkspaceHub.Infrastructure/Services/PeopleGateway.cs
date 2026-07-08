using Google.Apis.Auth.OAuth2;
using Google.Apis.PeopleService.v1;
using Google.Apis.PeopleService.v1.Data;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.DTOs.Contacts;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Infrastructure.Services;

public class PeopleGateway : IPeopleGateway
{
    private const string PersonFields = "names,emailAddresses";
    private const string ReadMask = "names,emailAddresses";
    private const int PageSize = 100;

    private readonly ITokenService _tokenService;
    private readonly ILogger<PeopleGateway> _logger;

    public PeopleGateway(ITokenService tokenService, ILogger<PeopleGateway> logger)
    {
        _tokenService = tokenService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PeopleContactRow>> ListAllAsync(Connection connection, CancellationToken ct = default)
    {
        using var people = await BuildPeopleServiceAsync(connection, ct);
        var byEmail = new Dictionary<string, PeopleContactRow>(StringComparer.OrdinalIgnoreCase);

        await CollectConnectionsAsync(people, connection.Id, byEmail, ct);
        await CollectOtherContactsAsync(people, connection.Id, byEmail, ct);

        return byEmail.Values.ToList();
    }

    private async Task CollectConnectionsAsync(
        PeopleServiceService people,
        Guid connectionId,
        Dictionary<string, PeopleContactRow> byEmail,
        CancellationToken ct)
    {
        try
        {
            string? pageToken = null;
            do
            {
                var request = people.People.Connections.List("people/me");
                request.PersonFields = PersonFields;
                request.PageSize = PageSize;
                request.PageToken = pageToken;

                var response = await request.ExecuteAsync(ct);
                if (response.Connections != null)
                {
                    foreach (var person in response.Connections)
                        MergePerson(byEmail, person, GoogleContactSource.Contact, overwrite: true);
                }

                pageToken = response.NextPageToken;
            } while (!string.IsNullOrEmpty(pageToken));
        }
        catch (Google.GoogleApiException ex)
        {
            _logger.LogWarning(ex,
                "People API connections.list skipped for connection {ConnectionId} (missing scope or provider error)",
                connectionId);
        }
    }

    private async Task CollectOtherContactsAsync(
        PeopleServiceService people,
        Guid connectionId,
        Dictionary<string, PeopleContactRow> byEmail,
        CancellationToken ct)
    {
        try
        {
            string? pageToken = null;
            do
            {
                var request = people.OtherContacts.List();
                request.ReadMask = ReadMask;
                request.PageSize = PageSize;
                request.PageToken = pageToken;

                var response = await request.ExecuteAsync(ct);
                if (response.OtherContacts != null)
                {
                    foreach (var person in response.OtherContacts)
                        MergePerson(byEmail, person, GoogleContactSource.OtherContact, overwrite: false);
                }

                pageToken = response.NextPageToken;
            } while (!string.IsNullOrEmpty(pageToken));
        }
        catch (Google.GoogleApiException ex)
        {
            _logger.LogWarning(ex,
                "People API otherContacts.list skipped for connection {ConnectionId} (missing scope or provider error)",
                connectionId);
        }
    }

    private static void MergePerson(
        Dictionary<string, PeopleContactRow> byEmail,
        Person person,
        GoogleContactSource source,
        bool overwrite)
    {
        var displayName = person.Names?.FirstOrDefault()?.DisplayName
            ?? person.Names?.FirstOrDefault()?.GivenName;
        var resourceName = person.ResourceName;

        if (person.EmailAddresses == null) return;

        foreach (var addr in person.EmailAddresses)
        {
            if (string.IsNullOrWhiteSpace(addr.Value)) continue;

            var email = addr.Value.Trim().ToLowerInvariant();
            var row = new PeopleContactRow
            {
                Email = email,
                DisplayName = displayName,
                Source = source,
                ExternalResourceName = resourceName
            };

            if (overwrite || !byEmail.ContainsKey(email))
                byEmail[email] = row;
        }
    }

    private async Task<PeopleServiceService> BuildPeopleServiceAsync(Connection connection, CancellationToken ct)
    {
        var accessToken = await _tokenService.GetFreshAccessTokenAsync(connection, ct);
        var credential = GoogleCredential.FromAccessToken(accessToken);
        return new PeopleServiceService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "WorkspaceHub"
        });
    }
}
