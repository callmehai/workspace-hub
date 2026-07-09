using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Infrastructure.Services;

public class GoogleDriveGateway : IGoogleDriveGateway
{
    private readonly ITokenService _tokenService;
    private readonly ILogger<GoogleDriveGateway> _logger;
    private const int InitialSyncPageSize = 100; // MVP: chỉ lấy N file mới nhất ở lần sync đầu tiên
   
    private const string SyncFileFields =
    "id, name, mimeType, size, webViewLink, iconLink, modifiedTime, trashed, version, headRevisionId, parents";

    private const string SyncChangeFields =
        "nextPageToken, newStartPageToken, changes(fileId, file(id, name, mimeType, size, webViewLink, iconLink, modifiedTime, trashed, version, headRevisionId, parents), removed)";

    public GoogleDriveGateway(ITokenService tokenService, ILogger<GoogleDriveGateway> logger)
    {
        _tokenService = tokenService;
        _logger = logger;
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
                listRequest.PageSize = InitialSyncPageSize;
                listRequest.Fields = $"files({SyncFileFields})";
                listRequest.OrderBy = "modifiedTime desc";

                var response = await listRequest.ExecuteAsync(ct);
                if (response.Files != null)
                {
                    foreach (var file in response.Files)
                        filesDto.Add(MapToDto(file));
                    // [Info] Silent truncation warning: nếu Drive có > InitialSyncPageSize file,
                    // user sẽ không thấy toàn bộ — chấp nhận được ở MVP.
                    if ((response.Files?.Count ?? 0) >= InitialSyncPageSize)
                        _logger.LogWarning("Drive initial sync capped at {PageSize} files — account may have more. Silent truncation in effect (MVP).", InitialSyncPageSize);
                }

                var tokenResponse = await service.Changes.GetStartPageToken().ExecuteAsync(ct);
                nextToken = tokenResponse.StartPageTokenValue
                    ?? throw new InvalidOperationException("Drive API returned null start page token.");

                return new DriveSyncResult(false, filesDto, nextToken); // nextToken = NewStartPageToken (sync cursor)
            }

            // Đưa việc khởi tạo request vào trong vòng lặp để lách luật read-only của PageToken
            while (true)
            {
                var changesRequest = service.Changes.List(pageToken);
                changesRequest.PageSize = 1000;
                changesRequest.Fields = SyncChangeFields;

                var response = await changesRequest.ExecuteAsync(ct);

                if (response.Changes != null)
                {
                    foreach (var change in response.Changes)
                    {
                        if (change.Removed == true || change.File == null)
                        {
                            if (string.IsNullOrEmpty(change.FileId)) continue;
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
                    nextToken = response.NewStartPageToken
                        ?? throw new InvalidOperationException("Drive Changes API returned null NewStartPageToken.");
                    break;
                }
            }

            return new DriveSyncResult(false, filesDto, nextToken); // nextToken = NewStartPageToken (sync cursor)
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Gone || ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new DriveSyncResult(true, new List<DriveFileDto>(), null);
        }
    }
    //Mục đích: Map Google.Apis.Drive.v3.Data.File → DriveFileDto (DTO dùng trong app)
    private DriveFileDto MapToDto(Google.Apis.Drive.v3.Data.File file)
    {
        return new DriveFileDto
        {
            Id = file.Id ?? string.Empty,
            Name = file.Name ?? string.Empty,
            MimeType = file.MimeType ?? string.Empty,
            Size = file.Size,
            WebViewLink = file.WebViewLink,
            IconLink = file.IconLink,
            ModifiedTime = file.ModifiedTimeDateTimeOffset ?? DateTimeOffset.UtcNow,
            Trashed = file.Trashed ?? false,
            Version = file.Version,
            HeadRevisionId = file.HeadRevisionId,
            Parents = file.Parents
        };
    }
}