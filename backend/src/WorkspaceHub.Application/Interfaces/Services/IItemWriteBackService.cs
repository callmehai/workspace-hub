using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Interfaces.Services;

public interface IItemWriteBackService
{
    Task<ItemResponse> PatchItemAsync(Guid itemId, Guid userId, PatchItemRequest payload, CancellationToken ct = default);
    Task<ItemResponse> CreateEventAsync(Guid userId, CreateEventRequest payload, CancellationToken ct = default);
    Task DeleteItemAsync(Guid itemId, Guid userId, CancellationToken ct = default);
}
