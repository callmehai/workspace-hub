using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Infrastructure.Services;

public class DriveGateway : IDriveGateway
{
    private const string FileFields = "id, name, mimeType, version, modifiedTime, trashed, headRevisionId";

    private readonly ITokenService _tokenService;

    public DriveGateway(ITokenService tokenService)
    {
        _tokenService = tokenService;
    }

    private async Task<DriveService> BuildDriveServiceAsync(Connection connection, CancellationToken ct)
    {
        var accessToken = await _tokenService.GetFreshAccessTokenAsync(connection, ct);
        var credential = GoogleCredential.FromAccessToken(accessToken);
        return new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "WorkspaceHub"
        });
    }

    public async Task<DriveFile> GetFileAsync(Connection connection, string fileId, CancellationToken ct = default)
    {
        try
        {
            using var drive = await BuildDriveServiceAsync(connection, ct);
            var request = drive.Files.Get(fileId);
            request.Fields = FileFields;
            var file = await request.ExecuteAsync(ct);
            return MapToDto(file);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GoogleApiExceptionHandler.Handle(ex, "Drive", "File", fileId,
                forbiddenMessage: "Insufficient permissions to access this file.");
        }
    }

    public async Task<DriveFile> UpdateFileAsync(Connection connection, string fileId, string newName, CancellationToken ct = default)
    {
        try
        {
            using var drive = await BuildDriveServiceAsync(connection, ct);
            var fileMetadata = new Google.Apis.Drive.v3.Data.File { Name = newName };
            var request = drive.Files.Update(fileMetadata, fileId);
            request.Fields = FileFields;
            var updatedFile = await request.ExecuteAsync(ct);
            return MapToDto(updatedFile);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GoogleApiExceptionHandler.Handle(ex, "Drive", "File", fileId);
        }
    }

    public async Task<DriveFile> TrashFileAsync(Connection connection, string fileId, CancellationToken ct = default)
    {
        try
        {
            using var drive = await BuildDriveServiceAsync(connection, ct);
            var fileMetadata = new Google.Apis.Drive.v3.Data.File { Trashed = true };
            var request = drive.Files.Update(fileMetadata, fileId);
            request.Fields = FileFields;
            var updatedFile = await request.ExecuteAsync(ct);
            return MapToDto(updatedFile);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GoogleApiExceptionHandler.Handle(ex, "Drive", "File", fileId);
        }
    }

    public async Task<DriveFile> UntrashFileAsync(Connection connection, string fileId, CancellationToken ct = default)
    {
        try
        {
            using var drive = await BuildDriveServiceAsync(connection, ct);
            var fileMetadata = new Google.Apis.Drive.v3.Data.File { Trashed = false };
            var request = drive.Files.Update(fileMetadata, fileId);
            request.Fields = FileFields;
            var updatedFile = await request.ExecuteAsync(ct);
            return MapToDto(updatedFile);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GoogleApiExceptionHandler.Handle(ex, "Drive", "File", fileId);
        }
    }

    // ───────────────────────── Private helpers ─────────────────────────

    private static DriveFile MapToDto(Google.Apis.Drive.v3.Data.File file)
    {
        var etag = file.Version?.ToString()
            ?? file.HeadRevisionId
            ?? file.ModifiedTimeDateTimeOffset?.ToString("o");

        return new DriveFile(file.Id, etag, file.Name, file.MimeType);
    }
}
