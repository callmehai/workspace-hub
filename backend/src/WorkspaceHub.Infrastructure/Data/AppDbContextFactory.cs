using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WorkspaceHub.Infrastructure.Data;

/// <summary>
/// Factory design-time cho EF CLI (migrations add / database update) — không cần chạy app.
/// `migrations add` chỉ build model nên connection string là placeholder; khi `database update`
/// thật thì set env `WORKSPACEHUB_CONNECTION` hoặc dùng startup project Api đọc user-secrets.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("WORKSPACEHUB_CONNECTION")
            ?? "Server=localhost;Database=WorkspaceHub;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(conn)
            .Options;

        return new AppDbContext(options);
    }
}
