using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace WorkspaceHub.Infrastructure.Data.Converters;

/// <summary>
/// Ép mọi DateTime đọc từ DB về Kind=Utc và ghi xuống dưới dạng UTC.
/// SQL Server datetime2 không lưu offset, nên ta tự bảo toàn quy ước "luôn UTC".
/// </summary>
public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(
            v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    {
    }
}

/// <summary>Bản nullable của <see cref="UtcDateTimeConverter"/>.</summary>
public sealed class UtcNullableDateTimeConverter : ValueConverter<DateTime?, DateTime?>
{
    public UtcNullableDateTimeConverter()
        : base(
            v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v.Value : v.Value.ToUniversalTime()) : v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v)
    {
    }
}
