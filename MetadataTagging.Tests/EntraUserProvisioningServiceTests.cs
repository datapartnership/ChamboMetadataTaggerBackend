using MetadataTagging.Data;
using MetadataTagging.Models;
using MetadataTagging.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MetadataTagging.Tests;

public class EntraUserProvisioningServiceTests
{
    [Fact]
    public async Task ProvisionOrSyncUserAsync_CreatesLocalUser_WhenNoneExists()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = await CreateContextAsync(connection);
        var service = new EntraUserProvisioningService(context);

        var user = await service.ProvisionOrSyncUserAsync(
            "new.user@example.com", "New User", new[] { UserRoles.Tagger });

        Assert.NotNull(user);
        Assert.Equal("new.user@example.com", user!.Email);
        Assert.Equal("New User", user.Username);
        Assert.Equal(UserRoles.Tagger, user.Role);
        Assert.Equal(UserAuthProviders.Entra, user.AuthProvider);
        Assert.Null(user.PasswordHash);
        Assert.True(user.IsActive);
        Assert.NotNull(user.LastLoginAt);
        Assert.Equal(1, await context.Users.CountAsync());
    }

    [Fact]
    public async Task ProvisionOrSyncUserAsync_SyncsExistingUser_MatchedByEmailCaseInsensitively()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = await CreateContextAsync(connection);
        context.Users.Add(new User
        {
            Username = "old-name",
            Email = "Existing.User@Example.com",
            PasswordHash = "some-local-hash",
            Role = UserRoles.Tagger,
            AuthProvider = UserAuthProviders.Local,
            IsActive = true
        });
        await context.SaveChangesAsync();

        var service = new EntraUserProvisioningService(context);
        var user = await service.ProvisionOrSyncUserAsync(
            "existing.user@example.com", "Updated Name", new[] { UserRoles.Supervisor });

        Assert.NotNull(user);
        Assert.Equal(1, await context.Users.CountAsync());
        Assert.Equal("Updated Name", user!.Username);
        Assert.Equal(UserRoles.Supervisor, user.Role);
        Assert.Equal(UserAuthProviders.Entra, user.AuthProvider);
        // The local password hash is preserved so the user can still log in locally too.
        Assert.Equal("some-local-hash", user.PasswordHash);
    }

    [Fact]
    public async Task ProvisionOrSyncUserAsync_ReturnsNull_WhenNoRecognizedAppRole()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = await CreateContextAsync(connection);
        var service = new EntraUserProvisioningService(context);

        var user = await service.ProvisionOrSyncUserAsync(
            "no-role@example.com", "No Role", new[] { "SomeUnrelatedRole" });

        Assert.Null(user);
        Assert.Equal(0, await context.Users.CountAsync());
    }

    [Fact]
    public async Task ProvisionOrSyncUserAsync_ReturnsNull_WhenMatchedUserIsDeactivated()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = await CreateContextAsync(connection);
        context.Users.Add(new User
        {
            Username = "inactive-user",
            Email = "inactive@example.com",
            PasswordHash = null,
            Role = UserRoles.Tagger,
            AuthProvider = UserAuthProviders.Entra,
            IsActive = false
        });
        await context.SaveChangesAsync();

        var service = new EntraUserProvisioningService(context);
        var user = await service.ProvisionOrSyncUserAsync(
            "inactive@example.com", "Inactive User", new[] { UserRoles.Tagger });

        Assert.Null(user);
    }

    [Fact]
    public async Task ProvisionOrSyncUserAsync_PrefersMostPrivilegedRole_WhenMultipleRecognizedRolesPresent()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = await CreateContextAsync(connection);
        var service = new EntraUserProvisioningService(context);

        var user = await service.ProvisionOrSyncUserAsync(
            "multi-role@example.com", "Multi Role", new[] { UserRoles.Tagger, UserRoles.Admin });

        Assert.NotNull(user);
        Assert.Equal(UserRoles.Admin, user!.Role);
    }

    private static async Task<ApplicationDbContext> CreateContextAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }
}
