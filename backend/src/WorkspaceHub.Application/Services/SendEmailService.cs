using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs.Emails;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Services;

/// <summary>Gửi email trực tiếp qua Gmail (đồng bộ, không hẹn giờ). Validate connection thuộc user + Gmail + Active.</summary>
public class SendEmailService : ISendEmailService
{
    private readonly IConnectionRepository _connections;
    private readonly IGmailGateway _gmail;
    private readonly IGoogleContactRepository _googleContacts;

    public SendEmailService(
        IConnectionRepository connections,
        IGmailGateway gmail,
        IGoogleContactRepository googleContacts)
    {
        _connections = connections;
        _gmail = gmail;
        _googleContacts = googleContacts;
    }

    public async Task<SendEmailResult> SendAsync(Guid userId, SendEmailRequest request, CancellationToken ct = default)
    {
        var connection = await _connections.GetByIdTrackedAsync(request.ConnectionId, ct)
            ?? throw new NotFoundException("Connection", request.ConnectionId);

        if (connection.UserId != userId)
            throw new NotFoundException("Connection", request.ConnectionId);

        if (connection.ServiceType != ServiceType.Gmail)
            throw new BusinessRuleException("Only Gmail connections can be used to send emails.");

        if (connection.Status != ConnectionStatus.Active)
            throw new BusinessRuleException($"Connection is not active (status: {connection.Status}).");

        var messageId = await _gmail.SendMessageAsync(
            connection, request.To, request.Cc, request.Bcc, request.Subject, request.BodyHtml, ct);

        return new SendEmailResult(messageId, DateTime.UtcNow);
    }

    public async Task<string?> GetSignatureAsync(Guid userId, Guid connectionId, CancellationToken ct = default)
    {
        var connection = await _connections.GetByIdTrackedAsync(connectionId, ct)
            ?? throw new NotFoundException("Connection", connectionId);

        if (connection.UserId != userId)
            throw new NotFoundException("Connection", connectionId);

        if (connection.ServiceType != ServiceType.Gmail)
            throw new BusinessRuleException("Only Gmail connections have a signature.");

        return await _gmail.GetSignatureAsync(connection, ct);
    }

    public async Task<IReadOnlyList<ContactSuggestionDto>> GetContactSuggestionsAsync(
        Guid userId, Guid connectionId, CancellationToken ct = default)
    {
        var query = await QueryContactSuggestionsAsync(userId, connectionId, ct);
        return query.ToList().AsReadOnly();
    }

    public async Task<IQueryable<ContactSuggestionDto>> QueryContactSuggestionsAsync(
        Guid userId, Guid connectionId, CancellationToken ct = default)
    {
        await ValidateGmailConnectionAsync(userId, connectionId, ct);
        return _googleContacts.QueryByConnectionId(connectionId);
    }

    private async Task ValidateGmailConnectionAsync(Guid userId, Guid connectionId, CancellationToken ct)
    {
        var connection = await _connections.GetByIdAsync(connectionId, ct)
            ?? throw new NotFoundException("Connection", connectionId);

        if (connection.UserId != userId)
            throw new NotFoundException("Connection", connectionId);

        if (connection.ServiceType != ServiceType.Gmail)
            throw new BusinessRuleException("Only Gmail connections can be used for contact suggestions.");

        if (connection.Status != ConnectionStatus.Active)
            throw new BusinessRuleException($"Connection is not active (status: {connection.Status}).");
    }
}
