using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using IdentityService.API.Contracts;
using IdentityService.API.Data;
using IdentityService.API.Models;
using IdentityService.API.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityService.Tests;

[CollectionDefinition("SQL Identity", DisableParallelization = true)]
public sealed class SqlIdentityCollection : ICollectionFixture<SqlIdentityFixture>;

public sealed class SqlIdentityFixture : IAsyncLifetime
{
    public const string Password = "Test-only password 123!";
    public string SigningKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    public Dictionary<string, int> Roles { get; } = new(StringComparer.Ordinal);
    public User Admin { get; private set; } = null!;
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    private string connection = "";
    private string masterConnection = "";
    private string database = "";

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("IDENTITY_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException("IDENTITY_TEST_CONNECTION is required. Mandatory SQL Server tests cannot be skipped.");
        var builder = new SqlConnectionStringBuilder(configured);
        if (!Regex.IsMatch(builder.InitialCatalog, @"^IdentityTests_[A-Za-z0-9_]+$"))
            throw new InvalidOperationException("Refusing database access: test database name must start with IdentityTests_.");
        database = builder.InitialCatalog[..Math.Min(builder.InitialCatalog.Length, 60)] + "_" + Guid.NewGuid().ToString("N");
        builder.InitialCatalog = database;
        connection = builder.ConnectionString;
        builder.InitialCatalog = "master";
        masterConnection = builder.ConnectionString;
        await using var sql = new SqlConnection(masterConnection);
        await sql.OpenAsync();
        var ddl = (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "identity.sql")))
            .Replace("$(IdentityDatabase)", database, StringComparison.Ordinal);
        foreach (var batch in Regex.Split(ddl, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(batch)) continue;
            await using var command = new SqlCommand(batch, sql) { CommandTimeout = 60 };
            await command.ExecuteNonQueryAsync();
        }
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Testing");
            host.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Identity"] = connection,
                ["Jwt:Key"] = SigningKey,
                ["Jwt:Issuer"] = "IdentityService",
                ["Jwt:Audience"] = "RestaurantStaff",
                ["Jwt:LifetimeMinutes"] = "60",
                ["Logging:LogLevel:Default"] = "Warning"
            }));
        });
        await WithDb(async db =>
        {
            // Test rows only. Deliberately unrelated RoleId ordering.
            foreach (var name in new[] { "Customer", "Cashier", IdentityRoles.Admin, "Inventory Staff", "Waiter", "Unsupported Role", "Restaurant Manager", "Kitchen Staff" })
                db.Roles.Add(new Role { RoleName = name });
            await db.SaveChangesAsync();
            foreach (var role in await db.Roles.ToListAsync()) Roles.Add(role.RoleName, role.RoleId);
        });
        Admin = await NewUser(IdentityRoles.Admin);
    }

    public async Task<User> NewUser(string role, bool active = true)
    {
        User user = null!;
        await WithDb(async db =>
        {
            user = new User
            {
                RoleId = Roles[role], UserName = "test_" + Guid.NewGuid().ToString("N"),
                FullName = "Test User", IsActive = active, CreatedAt = DateTime.UtcNow,
                Email = "test@example.invalid", PhoneNumber = "0123456789"
            };
            using var scope = Factory.Services.CreateScope();
            user.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>().HashPassword(user, Password);
            db.Users.Add(user);
            await db.SaveChangesAsync();
        });
        return user;
    }

    public async Task WithDb(Func<IdentityDbContext, Task> action)
    {
        await using var db = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseSqlServer(connection).Options);
        await action(db);
    }

    public HttpClient Client(string? token = null)
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task<LoginResponse> Login(User user)
    {
        using var client = Client();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.UserName, Password));
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null) await Factory.DisposeAsync();
        if (string.IsNullOrEmpty(database)) return;
        // Only the random database created by this fixture; never the configured base database.
        if (!Regex.IsMatch(database, @"^IdentityTests_[A-Za-z0-9_]+_[a-f0-9]{32}$"))
            throw new InvalidOperationException("Refusing cleanup of a non-test database.");
        SqlConnection.ClearAllPools();
        await using var sql = new SqlConnection(masterConnection);
        await sql.OpenAsync();
        await using var command = new SqlCommand($"IF DB_ID(N'{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END", sql);
        await command.ExecuteNonQueryAsync();
    }
}
