using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Services;

/// <summary>
/// Business logic cho Tag (SCRUM-70): CRUD tag của user + gắn/gỡ tag khỏi item.
/// Ownership check ở service; throw custom exception → middleware map sang status code.
/// </summary>
public class TagService : ITagService
{
    private readonly ITagRepository _tagRepo;
    private readonly IItemRepository _itemRepo;

    public TagService(ITagRepository tagRepo, IItemRepository itemRepo)
    {
        _tagRepo = tagRepo;
        _itemRepo = itemRepo;
    }

    public async Task<IReadOnlyList<TagResponse>> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var rows = await _tagRepo.GetByUserWithCountAsync(userId, ct);
        return rows.Select(r => new TagResponse(r.Tag.Id, r.Tag.Name, r.Tag.Color, r.ItemCount)).ToList();
    }

    public async Task<TagResponse> CreateAsync(Guid userId, CreateTagRequest request, CancellationToken ct = default)
    {
        var name = request.Name.Trim();

        if (await _tagRepo.NameExistsAsync(userId, name, null, ct))
            throw new ConflictException("Bạn đã có tag với tên này.");

        var tag = new Tag
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name,
            Color = request.Color.Trim()
        };

        await _tagRepo.AddAsync(tag, ct);
        await _tagRepo.SaveChangesAsync(ct);

        return new TagResponse(tag.Id, tag.Name, tag.Color, 0);
    }

    public async Task<TagResponse> UpdateAsync(Guid userId, Guid id, UpdateTagRequest request, CancellationToken ct = default)
    {
        var tag = await _tagRepo.GetByIdAndUserAsync(id, userId, ct)
            ?? throw new NotFoundException(nameof(Tag), id);

        var name = request.Name.Trim();

        if (await _tagRepo.NameExistsAsync(userId, name, id, ct))
            throw new ConflictException("Bạn đã có tag với tên này.");

        tag.Name = name;
        tag.Color = request.Color.Trim();

        _tagRepo.Update(tag);
        await _tagRepo.SaveChangesAsync(ct);

        var rows = await _tagRepo.GetByUserWithCountAsync(userId, ct);
        var count = rows.FirstOrDefault(r => r.Tag.Id == id).ItemCount;
        return new TagResponse(tag.Id, tag.Name, tag.Color, count);
    }

    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var tag = await _tagRepo.GetByIdAndUserAsync(id, userId, ct)
            ?? throw new NotFoundException(nameof(Tag), id);

        // Hard delete — DB cascade Tag → TagAssignment gỡ mọi liên kết. Item được giữ lại.
        _tagRepo.Remove(tag);
        await _tagRepo.SaveChangesAsync(ct);
    }

    public async Task<TagAssignmentResponse> AssignAsync(Guid userId, Guid tagId, AssignTagRequest request, CancellationToken ct = default)
    {
        // Tag phải thuộc user.
        _ = await _tagRepo.GetByIdAndUserAsync(tagId, userId, ct)
            ?? throw new NotFoundException(nameof(Tag), tagId);

        // Item phải thuộc user.
        _ = await _itemRepo.GetByIdAndUserAsync(request.ItemId, userId, ct)
            ?? throw new NotFoundException(nameof(Item), request.ItemId);

        if (await _tagRepo.AssignmentExistsAsync(tagId, request.ItemId, ct))
            throw new ConflictException("Tag đã được gắn vào item này.");

        var assignment = new TagAssignment
        {
            TagId = tagId,
            ItemId = request.ItemId,
            AssignedAt = DateTime.UtcNow
        };

        await _tagRepo.AddAssignmentAsync(assignment, ct);
        await _tagRepo.SaveChangesAsync(ct);

        return new TagAssignmentResponse(assignment.TagId, assignment.ItemId, assignment.AssignedAt);
    }

    public async Task UnassignAsync(Guid userId, Guid tagId, Guid itemId, CancellationToken ct = default)
    {
        // Tag phải thuộc user (chặn user khác gỡ tag của mình).
        _ = await _tagRepo.GetByIdAndUserAsync(tagId, userId, ct)
            ?? throw new NotFoundException(nameof(Tag), tagId);

        var assignment = await _tagRepo.GetAssignmentAsync(tagId, itemId, ct)
            ?? throw new NotFoundException($"Tag {tagId} chưa được gắn vào item {itemId}.");

        _tagRepo.RemoveAssignment(assignment);
        await _tagRepo.SaveChangesAsync(ct);
    }
}
