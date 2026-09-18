using MetadataTagging.Data;
using MetadataTagging.Models;
using MetadataTagging.Services;
using MetadataTagger.Options;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MetadataTagging.Tests;

public class AuthServiceEntraGuardTests
{
    [Fact]
    public async Task AuthenticateAsync_ReturnsNull_ForEntraOnlyAccountWithNoPasswordHash()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();

        context.Users.Add(new User
        {
            Username = "entra-only-user",
            Email = "entra-only@example.com",
            PasswordHash = null,
            Role = UserRoles.Tagger,
            AuthProvider = UserAuthProviders.Entra,
            IsActive = true
        });
        await context.SaveChangesAsync();

        var jwtOptions = Options.Create(new JwtOptions
        {
            SecretKey = "unit-test-secret-key-that-is-long-enough-1234567890",
            Issuer = "test-issuer",
            Audience = "test-audience"
        });

        var authService = new AuthService(context, jwtOptions);
        var token = await authService.AuthenticateAsync("entra-only@example.com", "any-password");

        Assert.Null(token);
    }
}
