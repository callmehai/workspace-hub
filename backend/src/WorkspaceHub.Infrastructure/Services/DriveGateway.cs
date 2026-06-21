using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Application.Common;

namespace WorkspaceHub.Infrastructure.Services;

public class DriveGateway : IDriveGateway
{
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
            request.Fields = "id, name, mimeType, version, modifiedTime, trashed, headRevisionId";
            var file = await request.ExecuteAsync(ct);
            return new DriveFile(
                file.Id,
                file.Version?.ToString() ?? file.HeadRevisionId ?? file.ModifiedTimeDateTimeOffset?.ToString("o"),
                file.Name,
                file.MimeType);
        }
        catch (Google.GoogleApiException ex)
        {
            if (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound) throw new NotFoundException("File", fileId);
            if (ex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden || (ex.Error != null && ex.Error.Errors != null && ex.Error.Errors.Any(e => e.Reason != null && e.Reason.Contains("insufficientPermissions", StringComparison.OrdinalIgnoreCase))))
            {
                throw new ForbiddenException("Cần reconnect với quyền ghi.");
            }
            throw new ProviderException($"Drive API error: {ex.Message}");
        }
    }

    public async Task<DriveFile> UpdateFileAsync(Connection connection, string fileId, string newName, CancellationToken ct = default)
    {
        try
        {
            using var drive = await BuildDriveServiceAsync(connection, ct);
            var fileMetadata = new Google.Apis.Drive.v3.Data.File
            {
                Name = newName
            };
            var request = drive.Files.Update(fileMetadata, fileId);
            request.Fields = "id, name, mimeType, version, modifiedTime, trashed, headRevisionId";
            var updatedFile = await request.ExecuteAsync(ct);
            return new DriveFile(
                updatedFile.Id,
                updatedFile.Version?.ToString() ?? updatedFile.HeadRevisionId ?? updatedFile.ModifiedTimeDateTimeOffset?.ToString("o"),
                updatedFile.Name,
                updatedFile.MimeType);
        }
        catch (Google.GoogleApiException ex)
        {
            if (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new NotFoundException("File", fileId);
            }
            if (ex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden || (ex.Error != null && ex.Error.Errors != null && ex.Error.Errors.Any(e => e.Reason != null && e.Reason.Contains("insufficientPermissions", StringComparison.OrdinalIgnoreCase))))
            {
                throw new ForbiddenException("Cần reconnect với quyền ghi.");
            }
            throw new ProviderException($"Drive API error: {ex.Message}");
        }
    }

    public async Task<DriveFile> TrashFileAsync(Connection connection, string fileId, CancellationToken ct = default)
    {
        try
        {
            using var drive = await BuildDriveServiceAsync(connection, ct);
            var fileMetadata = new Google.Apis.Drive.v3.Data.File
            {
                Trashed = true
            };
            var request = drive.Files.Update(fileMetadata, fileId);
            request.Fields = "id, name, mimeType, version, modifiedTime, trashed, headRevisionId";
            var updatedFile = await request.ExecuteAsync(ct);
            return new DriveFile(
                updatedFile.Id,
                updatedFile.Version?.ToString() ?? updatedFile.HeadRevisionId ?? updatedFile.ModifiedTimeDateTimeOffset?.ToString("o"),
                updatedFile.Name,
                updatedFile.MimeType);
        }
        catch (Google.GoogleApiException ex)
        {
            if (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound) throw new NotFoundException("File", fileId);
            if (ex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden || (ex.Error != null && ex.Error.Errors != null && ex.Error.Errors.Any(e => e.Reason != null && e.Reason.Contains("insufficientPermissions", StringComparison.OrdinalIgnoreCase))))
            {
                throw new ForbiddenException("Cần reconnect với quyền ghi.");
            }
            throw new ProviderException($"Drive API error: {ex.Message}");
        }
    }

    public async Task<DriveFile> UntrashFileAsync(Connection connection, string fileId, CancellationToken ct = default)
    {
        try
        {
            using var drive = await BuildDriveServiceAsync(connection, ct);
            var fileMetadata = new Google.Apis.Drive.v3.Data.File
            {
                Trashed = false
            };
            var request = drive.Files.Update(fileMetadata, fileId);
            request.Fields = "id, name, mimeType, version, modifiedTime, trashed, headRevisionId";
            var updatedFile = await request.ExecuteAsync(ct);
            return new DriveFile(
                updatedFile.Id,
                updatedFile.Version?.ToString() ?? updatedFile.HeadRevisionId ?? updatedFile.ModifiedTimeDateTimeOffset?.ToString("o"),
                updatedFile.Name,
                updatedFile.MimeType);
        }
        catch (Google.GoogleApiException ex)
        {
            if (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound) throw new NotFoundException("File", fileId);
            if (ex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden || (ex.Error != null && ex.Error.Errors != null && ex.Error.Errors.Any(e => e.Reason != null && e.Reason.Contains("insufficientPermissions", StringComparison.OrdinalIgnoreCase))))
            {
                throw new ForbiddenException("Cần reconnect với quyền ghi.");
            }
            throw new ProviderException($"Drive API error: {ex.Message}");
        }
    }
}

