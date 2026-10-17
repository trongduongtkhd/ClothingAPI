using Microsoft.EntityFrameworkCore.Migrations;

namespace ClothingAPI.IntegrationTests;

// Program.Main gọi Database.Migrate() với migrations viết cho SQL Server.
// Trong test, schema được tạo bằng EnsureCreated() trên SQLite nên Migrate() không cần làm gì.
public class NoOpMigrator : IMigrator
{
    public void Migrate(string? targetMigration = null)
    {
    }

    public Task MigrateAsync(string? targetMigration = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public string GenerateScript(
        string? fromMigration = null,
        string? toMigration = null,
        MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
        => string.Empty;

    public bool HasPendingModelChanges() => false;
}
