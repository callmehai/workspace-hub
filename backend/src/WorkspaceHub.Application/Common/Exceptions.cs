namespace WorkspaceHub.Application.Common;

using System.Net;

/// <summary>
/// Mã lỗi nghiệp vụ ổn định để FE dịch sang ngôn ngữ đang chọn (i18n).
///
/// <para><b>Vì sao cần:</b> API không nên biết ngôn ngữ hiển thị. <c>Message</c> của exception
/// giữ tiếng Anh (log, Swagger, debug); FE map <c>code</c> → key i18n và tự dịch. Trước đây FE
/// hiển thị thẳng <c>message</c> nên toast luôn ra tiếng Anh giữa giao diện tiếng Việt.</para>
///
/// <para><b>Hợp đồng:</b> giá trị ở đây là <b>API contract</b> — đổi tên = breaking change cho FE.
/// Thêm mã mới thì thêm key tương ứng ở <c>frontend/src/i18n/translations.ts</c> (cả vi lẫn en).
/// Exception KHÔNG gắn code vẫn hợp lệ: FE fallback về message chung theo status code.</para>
/// </summary>
public static class ErrorCodes
{
    // ── Folder / item membership ──
    /// <summary>Thao tác chỉ dành cho chủ sở hữu folder.</summary>
    public const string FolderOwnerOnly = "FOLDER_OWNER_ONLY";
    /// <summary>Item không thuộc user hiện tại (hoặc không tồn tại).</summary>
    public const string ItemNotOwned = "ITEM_NOT_OWNED";
    /// <summary>Một hoặc nhiều item không thuộc user hiện tại.</summary>
    public const string ItemsNotOwned = "ITEMS_NOT_OWNED";
    /// <summary>Item đã nằm sẵn trong folder này.</summary>
    public const string ItemAlreadyInFolder = "ITEM_ALREADY_IN_FOLDER";

    // ── Chia sẻ folder ──
    /// <summary>Người được chia sẻ chỉ có quyền xem (Viewer) nhưng đang cố ghi.</summary>
    public const string SharedViewerReadOnly = "SHARED_VIEWER_READ_ONLY";

    // ── Item / connection ──
    /// <summary>Connection không thuộc chủ sở hữu của item.</summary>
    public const string NotYourConnection = "NOT_YOUR_CONNECTION";
    /// <summary>Item không phải sự kiện lịch.</summary>
    public const string ItemNotCalendarEvent = "ITEM_NOT_CALENDAR_EVENT";
    /// <summary>Item chưa gắn với connection nào.</summary>
    public const string ItemNotLinkedToConnection = "ITEM_NOT_LINKED_TO_CONNECTION";
}

/// <summary>
/// Lỗi 404 — resource không tồn tại.
/// Middleware sẽ map sang HTTP 404 NotFound.
/// </summary>
public class NotFoundException : Exception
{
    /// <summary>Mã lỗi ổn định cho FE dịch (xem <see cref="ErrorCodes"/>). Null = FE dùng message chung.</summary>
    public string? Code { get; }

    public NotFoundException(string message, string? code = null) : base(message) { Code = code; }
    public NotFoundException(string entityName, object key)
        : base($"{entityName} with id '{key}' was not found.") { }
}

/// <summary>
/// Lỗi 403 — user có token nhưng không đủ quyền (vd: không phải Owner của Folder).
/// Middleware sẽ map sang HTTP 403 Forbidden.
/// </summary>
public class ForbiddenException : Exception
{
    /// <summary>Mã lỗi ổn định cho FE dịch (xem <see cref="ErrorCodes"/>). Null = FE dùng message chung.</summary>
    public string? Code { get; }

    public ForbiddenException(string message, string? code = null) : base(message) { Code = code; }
    public ForbiddenException()
        : base("You do not have permission to perform this action.") { }
}

/// <summary>
/// Lỗi 409 — vi phạm unique constraint hoặc trùng lặp.
/// Middleware map sang HTTP 409 Conflict.
/// Khi <see cref="Payload"/> khác null, middleware ghi <b>payload làm body 409</b>
/// (vd. <c>DriveLinkRestrictConflict</c> cho Case 1 tắt link) — không bọc envelope
/// <c>{ error, message, ... }</c>, để FE parse đúng contract.
/// </summary>
public class ConflictException : Exception
{
    /// <summary>Body 409 tuỳ chọn (serialize trực tiếp). Null = envelope lỗi chuẩn.</summary>
    public object? Payload { get; }

    /// <summary>Mã lỗi ổn định cho FE dịch (xem <see cref="ErrorCodes"/>). Null = FE dùng message chung.</summary>
    public string? Code { get; }

    public ConflictException(string message, string? code = null) : base(message) { Code = code; }

    /// <summary>
    /// Ctor cho 409 có body tuỳ biến. <b>Bắt buộc gọi bằng tên tham số</b>
    /// (<c>new ConflictException(msg, payload: obj)</c>) — nếu không, một <c>string</c> truyền vào
    /// sẽ bind sang ctor <c>code</c> ở trên và im lặng ra body sai. Ràng buộc <c>notnull</c> +
    /// tên tham số khác nhau khiến nhầm lẫn lộ ra ở compile-time thay vì runtime.
    /// </summary>
    public ConflictException(string message, object payload) : base(message)
    {
        if (payload is string)
            throw new ArgumentException(
                "Payload không được là string — dùng ctor (message, code) cho mã lỗi i18n.", nameof(payload));

        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
    }
}

/// <summary>
/// Lỗi 422 — vi phạm business rule (không phải validation input đơn thuần).
/// Middleware sẽ map sang HTTP 422 Unprocessable Entity.
/// </summary>
public class BusinessRuleException : Exception
{
    /// <summary>Mã lỗi ổn định cho FE dịch (xem <see cref="ErrorCodes"/>). Null = FE dùng message chung.</summary>
    public string? Code { get; }

    public BusinessRuleException(string message, string? code = null) : base(message) { Code = code; }
}

/// <summary>
/// Lỗi 401 — Không có quyền truy cập hoặc credentials không đúng.
/// Middleware sẽ map sang HTTP 401 Unauthorized.
/// </summary>
public class UnauthorizedException : Exception
{
    public UnauthorizedException(string message) : base(message) { }
}

/// <summary>
/// Lỗi 400 — Sai lệch State (CSRF) trong luồng OAuth.
/// Middleware sẽ map sang HTTP 400 Bad Request.
/// </summary>
public class CsrfException : Exception
{
    public CsrfException(string message) : base(message) { }
}

/// <summary>
/// Lỗi 502 — Provider (Google) trả về lỗi không phải expired/not-found.
/// Ví dụ: 403 thiếu scope, 429 rate limit, 5xx server error.
/// Middleware sẽ map sang HTTP 502 Bad Gateway.
/// </summary>
public class ProviderException : Exception
{
    public HttpStatusCode? StatusCode { get; }

    public ProviderException(string message) : base(message) { }

    public ProviderException(string message, Exception inner) : base(message, inner) { }

    public ProviderException(string message, HttpStatusCode statusCode, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
