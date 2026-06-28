using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.DTOs;

/// <summary>
/// Tạo important contact (SCRUM-60). Type=Email (gmail address) hoặc JiraAccount (accountId).
/// </summary>
public record CreateImportantContactRequest(
    ImportantContactType Type,
    string Identifier,
    string Label);

public record ImportantContactResponse(
    Guid Id,
    ImportantContactType Type,
    string Identifier,
    string Label);
