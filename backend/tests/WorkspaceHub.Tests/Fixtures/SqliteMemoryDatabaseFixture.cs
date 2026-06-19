using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WorkspaceHub.Infrastructure.Data;

namespace WorkspaceHub.Tests.Fixtures;

public class SqliteMemoryDatabaseFixture : IDisposable
{
    private readonly SqliteConnection _connection;
    public AppDbContext DbContext { get; }

    public SqliteMemoryDatabaseFixture()
    {
        // Must keep connection open so the in-memory database persists
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        DbContext = new AppDbContext(options);
        DbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        DbContext.Dispose();
        _connection.Dispose();
    }
}
