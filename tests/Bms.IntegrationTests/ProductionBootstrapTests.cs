using Bms.Infrastructure.Persistence;
using Bms.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Bms.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public sealed class ProductionBootstrapTests
{
    [Fact]
    public async Task FreshProductionRequiresStrongAdminAndDoesNotSeedDemoDataOrResetExistingUsers()
    {
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("BMS_TEST_POSTGRES")
            ?? "Host=localhost;Port=5432;Database=bms_platform_test;Username=bms_app;Password=devpassword");
        builder.Database = "postgres";
        var name = "bms_bootstrap_" + Guid.NewGuid().ToString("N");
        await using var control = new NpgsqlConnection(builder.ConnectionString);
        await control.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", control)) await create.ExecuteNonQueryAsync();
        try
        {
            builder.Database = name;
            var options = new DbContextOptionsBuilder<BmsDbContext>().UseNpgsql(builder.ConnectionString).Options;
            await using var db = new BmsDbContext(options);
            await db.Database.MigrateAsync();
            var hasher = new Rfc2898PasswordHasher();
            await Assert.ThrowsAsync<InvalidOperationException>(() => ProductionBootstrapper.EnsureAsync(db, hasher, "admin@example.com", "short"));
            Assert.False(await db.Users.AnyAsync());
            const string password = "Test-Only-Strong-Password-2026!";
            await ProductionBootstrapper.EnsureAsync(db, hasher, "admin@example.com", password);
            Assert.Equal(5, await db.Roles.CountAsync());
            var admin = Assert.Single(await db.Users.Include(u => u.Role).ToListAsync());
            Assert.Equal("SuperAdmin", admin.Role!.Name);
            Assert.True(hasher.Verify(password, admin.PasswordHash));
            Assert.False(await db.Tenants.AnyAsync());
            Assert.False(await db.Devices.AnyAsync());
            var originalHash = admin.PasswordHash;
            await ProductionBootstrapper.EnsureAsync(db, hasher, "different@example.com", "A-Different-Strong-Password!");
            Assert.Single(await db.Users.ToListAsync());
            Assert.Equal(originalHash, (await db.Users.SingleAsync()).PasswordHash);
        }
        finally
        {
            // The generated database belongs exclusively to this test.
            await using var drop = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", control);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
