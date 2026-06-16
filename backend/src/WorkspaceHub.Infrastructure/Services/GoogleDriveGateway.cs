using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Infrastructure.Services;

public class GoogleDriveGateway : IGoogleDriveGateway
{
    private readonly ITokenService _tokenService;

    public GoogleDriveGateway(ITokenService tokenService)
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

    public async Task<DriveSyncResult> SyncFilesAsync(Connection connection, string? pageToken, CancellationToken ct = default)
    {
        using var service = await BuildDriveServiceAsync(connection, ct);
        var filesDto = new List<DriveFileDto>();
        string? nextToken = null;

        try
        {
            if (string.IsNullOrEmpty(pageToken))
            {
                var listRequest = service.Files.List();
                listRequest.PageSize = 50;
                listRequest.Fields = "nextPageToken, files(id, name, mimeType, size, webViewLink, iconLink, modifiedTime, trashed)";
                listRequest.OrderBy = "modifiedTime desc";

                string? filesPageToken = null;
                do
                {
                    listRequest.PageToken = filesPageToken;
                    var response = await listRequest.ExecuteAsync(ct);
                    if (response.Files != null)
                    {
                        foreach (var file in response.Files)
                            filesDto.Add(MapToDto(file));
                    }
                    filesPageToken = response.NextPageToken;
                } while (!string.IsNullOrEmpty(filesPageToken));

                var tokenResponse = await service.Changes.GetStartPageToken().ExecuteAsync(ct);
                nextToken = tokenResponse.StartPageTokenValue;

                return new DriveSyncResult(false, filesDto, nextToken);
            }

            // Đưa việc khởi tạo request vào TONG vòng lặp để lách luật read-only của PageToken
            do
            {
                var changesRequest = service.Changes.List(pageToken);
                changesRequest.Fields = "nextPageToken, newStartPageToken, changes(fileId, file(id, name, mimeType, size, webViewLink, iconLink, modifiedTime, trashed), removed)";

                var response = await changesRequest.ExecuteAsync(ct);

                if (response.Changes != null)
                {
                    foreach (var change in response.Changes)
                    {
                        if (change.Removed == true || change.File == null)
                        {
                            filesDto.Add(new DriveFileDto { Id = change.FileId, Trashed = true });
                            continue;
                        }
                        filesDto.Add(MapToDto(change.File));
                    }
                }

                if (!string.IsNullOrEmpty(response.NextPageToken))
                {
                    pageToken = response.NextPageToken; // Cập nhật biến chuỗi thay vì cập nhật thuộc tính
                }
                else
                {
                    nextToken = response.NewStartPageToken;
                    break;
                }

            } while (!string.IsNullOrEmpty(pageToken));

            return new DriveSyncResult(false, filesDto, nextToken);
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Gone || ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new DriveSyncResult(true, new List<DriveFileDto>(), null);
        }
    }

    private DriveFileDto MapToDto(Google.Apis.Drive.v3.Data.File file)
    {
        return new DriveFileDto
        {
            Id = file.Id,
            Name = file.Name,
            MimeType = file.MimeType,
            Size = file.Size,
            WebViewLink = file.WebViewLink,
            IconLink = file.IconLink,
            // Sửa lỗi cảnh báo: Dùng trực tiếp ModifiedTimeDateTimeOffset theo khuyến nghị của Google
            ModifiedTime = file.ModifiedTimeDateTimeOffset ?? DateTimeOffset.UtcNow,
            Trashed = file.Trashed ?? false
        };
    }
}