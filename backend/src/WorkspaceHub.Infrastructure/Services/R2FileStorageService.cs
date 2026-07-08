using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using WorkspaceHub.Application.Abstractions;

namespace WorkspaceHub.Infrastructure.Services;

/// <summary>
/// Cloudflare R2 (S3-compatible) qua AWSSDK.S3. Config đọc từ section "R2"
/// (R2:AccountId/BucketName/PublicUrl/AccessKeyId/SecretAccessKey) — xem docs/SETUP.md.
/// </summary>
public class R2FileStorageService : IFileStorageService
{
    private readonly AmazonS3Client _client;
    private readonly string _bucketName;
    private readonly string _publicUrl;

    public R2FileStorageService(IConfiguration config)
    {
        var accountId = config["R2:AccountId"]
            ?? throw new InvalidOperationException("Thiếu R2:AccountId (xem docs/SETUP.md).");
        var accessKey = config["R2:AccessKeyId"]
            ?? throw new InvalidOperationException("Thiếu R2:AccessKeyId (xem docs/SETUP.md).");
        var secretKey = config["R2:SecretAccessKey"]
            ?? throw new InvalidOperationException("Thiếu R2:SecretAccessKey (xem docs/SETUP.md).");
        _bucketName = config["R2:BucketName"]
            ?? throw new InvalidOperationException("Thiếu R2:BucketName (xem docs/SETUP.md).");
        _publicUrl = (config["R2:PublicUrl"]
            ?? throw new InvalidOperationException("Thiếu R2:PublicUrl (xem docs/SETUP.md).")).TrimEnd('/');

        _client = new AmazonS3Client(
            new BasicAWSCredentials(accessKey, secretKey),
            new AmazonS3Config
            {
                ServiceURL = $"https://{accountId}.r2.cloudflarestorage.com",
                ForcePathStyle = true, // bắt buộc với R2 (không hỗ trợ virtual-hosted style)
                AuthenticationRegion = "auto",
                // R2 không hỗ trợ chunked payload signing + flexible checksum trailer mà
                // AWSSDK.S3 v4 dùng mặc định (lỗi "STREAMING-...-PAYLOAD-TRAILER not implemented").
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            });
    }

    public async Task<string> UploadAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        var response = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
            DisablePayloadSigning = true, // R2 không hỗ trợ chunked SigV4 streaming payload (chỉ chấp nhận UNSIGNED-PAYLOAD)
        }, ct);

        // Key cố định theo userId ⇒ re-upload cùng định dạng giữ nguyên URL. Gắn ETag (đổi theo nội
        // dung) làm query cache-bust, nếu không FE/trình duyệt vẫn hiện ảnh cũ đã cache cho tới khi
        // reload cứng (báo lỗi thực tế: "sửa avatar phải load lại trang mới thấy").
        var version = response.ETag?.Trim('"');
        return string.IsNullOrEmpty(version) ? $"{_publicUrl}/{key}" : $"{_publicUrl}/{key}?v={version}";
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _client.DeleteObjectAsync(_bucketName, key, ct);
        }
        catch (AmazonS3Exception)
        {
            // Object không tồn tại/đã xoá — không cần fail luồng xoá avatar vì việc này.
        }
    }
}
