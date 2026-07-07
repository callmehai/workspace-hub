using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using Xunit;

namespace WorkspaceHub.Tests.Services;

public class TagServiceTests
{
    private readonly Mock<ITagRepository> _tagRepo = new();
    private readonly Mock<IItemRepository> _itemRepo = new();
    private readonly TagService _service;
    private readonly Guid _userId = Guid.NewGuid();

    public TagServiceTests()
    {
        _service = new TagService(_tagRepo.Object, _itemRepo.Object);
    }

    [Fact]
    public async Task Create_Persists_TrimmedAndZeroCount()
    {
        _tagRepo.Setup(m => m.NameExistsAsync(_userId, "Urgent", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        Tag? added = null;
        _tagRepo.Setup(m => m.AddAsync(It.IsAny<Tag>(), It.IsAny<CancellationToken>()))
            .Callback((Tag t, CancellationToken _) => added = t)
            .Returns(Task.CompletedTask);

        var result = await _service.CreateAsync(_userId, new CreateTagRequest("  Urgent  ", "  #FF0000  "));

        result.Name.Should().Be("Urgent");
        result.Color.Should().Be("#FF0000");
        result.ItemCount.Should().Be(0);
        added.Should().NotBeNull();
        added!.UserId.Should().Be(_userId);
        _tagRepo.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_DuplicateName_Throws409()
    {
        _tagRepo.Setup(m => m.NameExistsAsync(_userId, "Urgent", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var act = () => _service.CreateAsync(_userId, new CreateTagRequest("Urgent", "#FF0000"));

        await act.Should().ThrowAsync<ConflictException>();
        _tagRepo.Verify(m => m.AddAsync(It.IsAny<Tag>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Get_MapsItemCount()
    {
        var tag = new Tag { Id = Guid.NewGuid(), UserId = _userId, Name = "Work", Color = "#111" };
        _tagRepo.Setup(m => m.GetByUserWithCountAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(Tag, int)> { (tag, 3) });

        var result = await _service.GetAsync(_userId);

        result.Should().ContainSingle(t => t.Name == "Work" && t.ItemCount == 3);
    }

    [Fact]
    public async Task Update_NotOwned_Throws404()
    {
        var id = Guid.NewGuid();
        _tagRepo.Setup(m => m.GetByIdAndUserAsync(id, _userId, It.IsAny<CancellationToken>())).ReturnsAsync((Tag?)null);

        var act = () => _service.UpdateAsync(_userId, id, new UpdateTagRequest("New", "#000"));

        await act.Should().ThrowAsync<NotFoundException>();
        _tagRepo.Verify(m => m.Update(It.IsAny<Tag>()), Times.Never);
    }

    [Fact]
    public async Task Update_DuplicateName_Throws409()
    {
        var id = Guid.NewGuid();
        var tag = new Tag { Id = id, UserId = _userId, Name = "Old", Color = "#000" };
        _tagRepo.Setup(m => m.GetByIdAndUserAsync(id, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(tag);
        _tagRepo.Setup(m => m.NameExistsAsync(_userId, "Taken", id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var act = () => _service.UpdateAsync(_userId, id, new UpdateTagRequest("Taken", "#000"));

        await act.Should().ThrowAsync<ConflictException>();
        _tagRepo.Verify(m => m.Update(It.IsAny<Tag>()), Times.Never);
    }

    [Fact]
    public async Task Update_Owned_UsesItemCountQuery()
    {
        var id = Guid.NewGuid();
        var tag = new Tag { Id = id, UserId = _userId, Name = "Old", Color = "#000" };
        _tagRepo.Setup(m => m.GetByIdAndUserAsync(id, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(tag);
        _tagRepo.Setup(m => m.NameExistsAsync(_userId, "New", id, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _tagRepo.Setup(m => m.GetItemCountAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(5);

        var result = await _service.UpdateAsync(_userId, id, new UpdateTagRequest("New", "#111"));

        result.Name.Should().Be("New");
        result.ItemCount.Should().Be(5);
        // Không quét toàn bộ tag của user chỉ để lấy 1 ItemCount.
        _tagRepo.Verify(m => m.GetItemCountAsync(id, It.IsAny<CancellationToken>()), Times.Once);
        _tagRepo.Verify(m => m.GetByUserWithCountAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_UniqueIndexRace_MapsDbUpdateExceptionTo409()
    {
        // NameExistsAsync qua được (race), nhưng unique index (UserId,Name) chặn ở SaveChanges.
        _tagRepo.Setup(m => m.NameExistsAsync(_userId, "Urgent", null, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _tagRepo.Setup(m => m.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new DbUpdateException("unique violation"));

        var act = () => _service.CreateAsync(_userId, new CreateTagRequest("Urgent", "#FF0000"));

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Delete_Owned_Removes()
    {
        var id = Guid.NewGuid();
        var tag = new Tag { Id = id, UserId = _userId, Name = "X", Color = "#000" };
        _tagRepo.Setup(m => m.GetByIdAndUserAsync(id, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(tag);

        await _service.DeleteAsync(_userId, id);

        _tagRepo.Verify(m => m.Remove(tag), Times.Once);
        _tagRepo.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Assign_TagAndItemOwned_Creates()
    {
        var tagId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        _tagRepo.Setup(m => m.GetByIdAndUserAsync(tagId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tag { Id = tagId, UserId = _userId, Name = "X", Color = "#000" });
        _itemRepo.Setup(m => m.GetByIdAndUserAsync(itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Item { Id = itemId, UserId = _userId, Title = "i", Snippet = "s" });
        _tagRepo.Setup(m => m.AssignmentExistsAsync(tagId, itemId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        TagAssignment? added = null;
        _tagRepo.Setup(m => m.AddAssignmentAsync(It.IsAny<TagAssignment>(), It.IsAny<CancellationToken>()))
            .Callback((TagAssignment a, CancellationToken _) => added = a)
            .Returns(Task.CompletedTask);

        var result = await _service.AssignAsync(_userId, tagId, new AssignTagRequest(itemId));

        result.TagId.Should().Be(tagId);
        result.ItemId.Should().Be(itemId);
        added.Should().NotBeNull();
        _tagRepo.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Assign_ItemNotOwned_Throws404()
    {
        var tagId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        _tagRepo.Setup(m => m.GetByIdAndUserAsync(tagId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tag { Id = tagId, UserId = _userId, Name = "X", Color = "#000" });
        _itemRepo.Setup(m => m.GetByIdAndUserAsync(itemId, _userId, It.IsAny<CancellationToken>())).ReturnsAsync((Item?)null);

        var act = () => _service.AssignAsync(_userId, tagId, new AssignTagRequest(itemId));

        await act.Should().ThrowAsync<NotFoundException>();
        _tagRepo.Verify(m => m.AddAssignmentAsync(It.IsAny<TagAssignment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Assign_AlreadyAssigned_Throws409()
    {
        var tagId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        _tagRepo.Setup(m => m.GetByIdAndUserAsync(tagId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tag { Id = tagId, UserId = _userId, Name = "X", Color = "#000" });
        _itemRepo.Setup(m => m.GetByIdAndUserAsync(itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Item { Id = itemId, UserId = _userId, Title = "i", Snippet = "s" });
        _tagRepo.Setup(m => m.AssignmentExistsAsync(tagId, itemId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var act = () => _service.AssignAsync(_userId, tagId, new AssignTagRequest(itemId));

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Unassign_NotAssigned_Throws404()
    {
        var tagId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        _tagRepo.Setup(m => m.GetByIdAndUserAsync(tagId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tag { Id = tagId, UserId = _userId, Name = "X", Color = "#000" });
        _tagRepo.Setup(m => m.GetAssignmentAsync(tagId, itemId, It.IsAny<CancellationToken>())).ReturnsAsync((TagAssignment?)null);

        var act = () => _service.UnassignAsync(_userId, tagId, itemId);

        await act.Should().ThrowAsync<NotFoundException>();
        _tagRepo.Verify(m => m.RemoveAssignment(It.IsAny<TagAssignment>()), Times.Never);
    }

    [Fact]
    public async Task Unassign_Assigned_Removes()
    {
        var tagId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var assignment = new TagAssignment { TagId = tagId, ItemId = itemId, AssignedAt = DateTime.UtcNow };
        _tagRepo.Setup(m => m.GetByIdAndUserAsync(tagId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tag { Id = tagId, UserId = _userId, Name = "X", Color = "#000" });
        _tagRepo.Setup(m => m.GetAssignmentAsync(tagId, itemId, It.IsAny<CancellationToken>())).ReturnsAsync(assignment);

        await _service.UnassignAsync(_userId, tagId, itemId);

        _tagRepo.Verify(m => m.RemoveAssignment(assignment), Times.Once);
        _tagRepo.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
