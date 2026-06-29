using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests.Services;

public class ImportantContactServiceTests
{
    private readonly Mock<IImportantContactRepository> _repo = new();
    private readonly ImportantContactService _service;
    private readonly Guid _userId = Guid.NewGuid();

    public ImportantContactServiceTests()
    {
        _service = new ImportantContactService(_repo.Object);
    }

    [Fact]
    public async Task Create_JiraAccount_Persists()
    {
        _repo.Setup(m => m.ExistsAsync(_userId, ImportantContactType.JiraAccount, "acc-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        ImportantContact? added = null;
        _repo.Setup(m => m.AddAsync(It.IsAny<ImportantContact>(), It.IsAny<CancellationToken>()))
            .Callback((ImportantContact c, CancellationToken _) => added = c)
            .Returns(Task.CompletedTask);

        var result = await _service.CreateAsync(_userId,
            new CreateImportantContactRequest(ImportantContactType.JiraAccount, "acc-1", "Boss"));

        result.Type.Should().Be(ImportantContactType.JiraAccount);
        result.Identifier.Should().Be("acc-1");
        result.Label.Should().Be("Boss");
        added.Should().NotBeNull();
        added!.UserId.Should().Be(_userId);
        _repo.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_Duplicate_Throws409()
    {
        _repo.Setup(m => m.ExistsAsync(_userId, ImportantContactType.JiraAccount, "acc-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var act = () => _service.CreateAsync(_userId,
            new CreateImportantContactRequest(ImportantContactType.JiraAccount, "acc-1", "Boss"));

        await act.Should().ThrowAsync<ConflictException>();
        _repo.Verify(m => m.AddAsync(It.IsAny<ImportantContact>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_TrimsIdentifierAndLabel()
    {
        _repo.Setup(m => m.ExistsAsync(_userId, ImportantContactType.JiraAccount, "acc-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        ImportantContact? added = null;
        _repo.Setup(m => m.AddAsync(It.IsAny<ImportantContact>(), It.IsAny<CancellationToken>()))
            .Callback((ImportantContact c, CancellationToken _) => added = c)
            .Returns(Task.CompletedTask);

        await _service.CreateAsync(_userId,
            new CreateImportantContactRequest(ImportantContactType.JiraAccount, "  acc-1  ", "  Boss  "));

        added!.Identifier.Should().Be("acc-1");
        added.Label.Should().Be("Boss");
    }

    [Fact]
    public async Task Get_FiltersByType()
    {
        _repo.Setup(m => m.GetByUserAsync(_userId, ImportantContactType.JiraAccount, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ImportantContact>
            {
                new() { Id = Guid.NewGuid(), UserId = _userId, Type = ImportantContactType.JiraAccount, Identifier = "acc-1", Label = "Boss" }
            });

        var result = await _service.GetAsync(_userId, ImportantContactType.JiraAccount);

        result.Should().ContainSingle(c => c.Identifier == "acc-1" && c.Type == ImportantContactType.JiraAccount);
    }

    [Fact]
    public async Task Delete_OwnedContact_Removes()
    {
        var id = Guid.NewGuid();
        var contact = new ImportantContact { Id = id, UserId = _userId, Type = ImportantContactType.Email, Identifier = "a@b.com", Label = "X" };
        _repo.Setup(m => m.GetByIdAndUserAsync(id, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(contact);

        await _service.DeleteAsync(_userId, id);

        _repo.Verify(m => m.Remove(contact), Times.Once);
        _repo.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_NotFoundOrNotOwner_Throws404()
    {
        var id = Guid.NewGuid();
        _repo.Setup(m => m.GetByIdAndUserAsync(id, _userId, It.IsAny<CancellationToken>())).ReturnsAsync((ImportantContact?)null);

        var act = () => _service.DeleteAsync(_userId, id);

        await act.Should().ThrowAsync<NotFoundException>();
        _repo.Verify(m => m.Remove(It.IsAny<ImportantContact>()), Times.Never);
    }
}
