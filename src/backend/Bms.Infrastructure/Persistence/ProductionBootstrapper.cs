using Bms.Application.Abstractions.Security;
using Bms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

/// <summary>Production starts with roles and one administrator, without demo tenants or passwords.</summary>
public static class ProductionBootstrapper
{
    public static async Task EnsureAsync(BmsDbContext db, IPasswordHasher hasher, string? email, string? password, CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(ct)) return;
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') || string.IsNullOrWhiteSpace(password) || password.Length < 16)
            throw new InvalidOperationException("An empty production database requires Bootstrap:AdminEmail and Bootstrap:AdminPassword (at least 16 characters).");
        var roles = await db.Roles.ToListAsync(ct);
        foreach (var name in new[] { "SuperAdmin", "Admin", "Engineer", "Viewer", "Customer" })
            if (roles.All(r => r.Name != name)) { var role = new Role { Name = name }; roles.Add(role); db.Roles.Add(role); }
        db.Users.Add(new User { RoleId = roles.Single(r => r.Name == "SuperAdmin").Id, Email = email.Trim(), DisplayName = "Protec Administrator", PasswordHash = hasher.Hash(password), IsActive = true });
        await db.SaveChangesAsync(ct);
    }
}
