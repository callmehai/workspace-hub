namespace WorkspaceHub.Application.DTOs.Drive;

/// <summary>
/// Case 1 giống Google Drive: tắt link file khi folder mẹ đang "ai có link".
/// FE dùng để hiện popup "Xoá quyền truy cập khỏi thư mục mẹ?".
/// Null từ Detect = không xung đột → tắt link thẳng được.
/// </summary>
/// <param name="Code">Mã cố định — FE nhận diện loại dialog.</param>
/// <param name="ItemId">Item (file) user đang tắt link.</param>
/// <param name="ItemTitle">Tên file — hiện trên cây quyền popup.</param>
/// <param name="ItemExternalId">Google file id của file.</param>
/// <param name="ParentItemId">Item folder mẹ trong app (null nếu chưa sync về DB).</param>
/// <param name="ParentExternalId">Google folder id của thư mục mẹ.</param>
/// <param name="ParentTitle">Tên folder mẹ — hiện trên cây quyền popup.</param>
/// <param name="ItemFromAccess">Trạng thái hiện tại của file (vd. anyone).</param>
/// <param name="ItemToAccess">Trạng thái sau khi xác nhận (restricted).</param>
/// <param name="ParentFromAccess">Trạng thái hiện tại của folder mẹ (anyone).</param>
/// <param name="ParentToAccess">Trạng thái folder mẹ sau khi xác nhận (restricted).</param>
public record DriveLinkRestrictConflict(
    string Code,
    Guid ItemId,
    string ItemTitle,
    string ItemExternalId,
    Guid? ParentItemId,
    string ParentExternalId,
    string ParentTitle,
    string ItemFromAccess,
    string ItemToAccess,
    string ParentFromAccess,
    string ParentToAccess)
{
    /// <summary>Mã Case 1 — tắt link file kéo theo tắt link folder mẹ.</summary>
    public const string RestrictAffectsParentCode = "LINK_RESTRICT_AFFECTS_PARENT";

    public const string AccessAnyone = "anyone";
    public const string AccessRestricted = "restricted";
}
