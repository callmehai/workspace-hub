using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Services;

public class ImportantContactService : IImportantContactService
{
    private readonly IImportantContactRepository _repo;

    public ImportantContactService(IImportantContactRepository repo)
    {
        _repo = repo;
    }

    public async Task<IReadOnlyList<ImportantContactResponse>> GetAsync(Guid userId, ImportantContactType? type, CancellationToken ct = default)
    {
        var contacts = await _repo.GetByUserAsync(userId, type, ct);
        return contacts.Select(Map).ToList();
    }

    public async Task<ImportantContactResponse> CreateAsync(Guid userId, CreateImportantContactRequest request, CancellationToken ct = default)
    {
        var identifier = request.Identifier.Trim();

        if (await _repo.ExistsAsync(userId, request.Type, identifier, ct))
            throw new ConflictException("Liên hệ này đã được đánh dấu quan trọng.");

        var contact = new ImportantContact
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = request.Type,
            Identifier = identifier,
            Label = request.Label.Trim()
        };

        await _repo.AddAsync(contact, ct);
        await _repo.SaveChangesAsync(ct);

        return Map(contact);
    }

    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var contact = await _repo.GetByIdAndUserAsync(id, userId, ct)
            ?? throw new NotFoundException("ImportantContact", id);

        _repo.Remove(contact);
        await _repo.SaveChangesAsync(ct);
    }

    private static ImportantContactResponse Map(ImportantContact c) =>
        new(c.Id, c.Type, c.Identifier, c.Label);
}
