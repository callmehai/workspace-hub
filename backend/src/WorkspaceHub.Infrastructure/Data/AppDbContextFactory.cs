using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace WorkspaceHub.Infrastructure.Data;

/// <summary>
/// Factory design-time cho EF CLI (migrations add / database update) — không cần chạy app.
/// Đọc connection string GIỐNG HỆT lúc chạy app: ưu tiên env WORKSPACEHUB_CONNECTION,
/// rồi tới appsettings.json / appsettings.Development.json của project Api.
/// Nhờ vậy `dotnet ef` và `dotnet run` luôn dùng chung 1 DB.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // Khi chạy qua `--startup-project src/WorkspaceHub.Api`, thư mục hiện tại là project Api.
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var conn = Environment.GetEnvironmentVariable("WORKSPACEHUB_CONNECTION")
            ?? config.GetConnectionString("Default")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=WorkspaceHub;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(conn)
            .Options;

        return new AppDbContext(options);
    }
}
