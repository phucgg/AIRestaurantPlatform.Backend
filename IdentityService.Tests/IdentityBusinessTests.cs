using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using IdentityService.API.Contracts;
using IdentityService.API.Models;
using IdentityService.API.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace IdentityService.Tests;

[Collection("SQL Identity")]
public sealed class IdentityBusinessTests(SqlIdentityFixture fixture)
{
    [Theory]
    [InlineData("System Administrator")]
    [InlineData("Restaurant Manager")]
    [InlineData("Waiter")]
    [InlineData("Cashier")]
    [InlineData("Kitchen Staff")]
    [InlineData("Inventory Staff")]
    public async Task AllowedRolesCanLogin(string role)
    {
        var user = role == IdentityRoles.Admin ? fixture.Admin : await fixture.NewUser(role);
        var login = await fixture.Login(user);
        Assert.Equal(role, login.User.RoleName);
        Assert.Equal(user.UserId, login.User.UserId);
        using var client = fixture.Client(login.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(login.AccessToken);
        Assert.Equal(user.UserId.ToString(), jwt.Claims.Single(x => x.Type == "sub").Value);
        var sessionId = int.Parse(jwt.Claims.Single(x => x.Type == "sid").Value);
        await fixture.WithDb(async db =>
        {
            var session = await db.UserSessions.SingleAsync(x => x.UserSessionId == sessionId);
            Assert.Equal(user.UserId, session.UserId);
            Assert.True(session.ExpiresAt > DateTime.UtcNow);
            Assert.True(await db.AuditLogs.AnyAsync(x => x.ActorUserId == user.UserId && x.Action == "Login" && x.EntityId == sessionId.ToString()));
        });
    }

    [Fact]
    public async Task WrongPasswordIsRejected()
    {
        var user = await fixture.NewUser("Waiter");
        await RejectLogin(user.UserName, "Wrong test password!");
        await AssertNoSessions(user.UserId);
    }

    [Fact]
    public async Task UnknownUserIsRejected()
    {
        var name = "missing_" + Guid.NewGuid().ToString("N");
        await RejectLogin(name, SqlIdentityFixture.Password);
        await fixture.WithDb(async db => Assert.False(await db.Users.AnyAsync(x => x.UserName == name)));
    }

    [Fact]
    public async Task InactiveUserCannotLogin()
    {
        var user = await fixture.NewUser("Cashier", false);
        await RejectLogin(user.UserName, SqlIdentityFixture.Password);
        await AssertNoSessions(user.UserId);
    }

    [Fact]
    public async Task CustomerCannotLogin()
    {
        var user = await fixture.NewUser("Customer");
        await RejectLogin(user.UserName, SqlIdentityFixture.Password);
        await AssertNoSessions(user.UserId);
    }

    [Fact]
    public async Task UnknownRoleCannotLogin()
    {
        var user = await fixture.NewUser("Unsupported Role");
        await RejectLogin(user.UserName, SqlIdentityFixture.Password);
        await AssertNoSessions(user.UserId);
    }

    [Theory]
    [InlineData("GET", "/api/auth/me")]
    [InlineData("POST", "/api/auth/logout")]
    [InlineData("POST", "/api/admin/users")]
    public async Task AnonymousRequestsAreRejected(string method, string url)
    {
        using var client = fixture.Client();
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (url == "/api/admin/users") request.Content = JsonContent.Create(CreateRequest("Waiter"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task LogoutInvalidatesSessionAndToken()
    {
        var user = await fixture.NewUser("Cashier");
        var login = await fixture.Login(user);
        var other = await fixture.Login(user);
        using var client = fixture.Client(login.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        using var otherClient = fixture.Client(other.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await otherClient.GetAsync("/api/auth/me")).StatusCode);
        await fixture.WithDb(async db =>
        {
            Assert.Equal(1, await db.UserSessions.CountAsync(x => x.UserId == user.UserId && x.ExpiresAt > DateTime.UtcNow));
            Assert.True(await db.AuditLogs.AnyAsync(x => x.ActorUserId == user.UserId && x.Action == "Logout"));
        });
    }

    [Fact]
    public async Task DisabledUserTokenIsRejected()
    {
        var user = await fixture.NewUser("Waiter");
        var login = await fixture.Login(user);
        await fixture.WithDb(db => db.Users.Where(x => x.UserId == user.UserId)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.IsActive, false)));
        using var client = fixture.Client(login.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
    }

    [Fact]
    public async Task WaiterLoginInvalidatesOldSession()
    {
        var user = await fixture.NewUser("Waiter");
        var first = await fixture.Login(user);
        var second = await fixture.Login(user);
        using var oldClient = fixture.Client(first.AccessToken);
        using var newClient = fixture.Client(second.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await oldClient.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await newClient.GetAsync("/api/auth/me")).StatusCode);
        await fixture.WithDb(async db => Assert.Equal(1, await db.UserSessions.CountAsync(x => x.UserId == user.UserId && x.ExpiresAt > DateTime.UtcNow)));
    }

    [Fact]
    public async Task ConcurrentWaiterLoginsLeaveOneValidSession()
    {
        var user = await fixture.NewUser("Waiter");
        for (var round = 0; round < 3; round++)
        {
            // Separate hosts/DI roots sharing only SQL Server: no process-local lock can make this pass.
            using var otherHost = fixture.Factory.WithWebHostBuilder(_ => { });
            using var firstClient = fixture.Client();
            using var secondClient = otherHost.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<LoginResponse> ConcurrentLogin(HttpClient client)
            {
                await start.Task;
                var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.UserName, SqlIdentityFixture.Password));
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
            }
            var firstTask = ConcurrentLogin(firstClient);
            var secondTask = ConcurrentLogin(secondClient);
            start.SetResult();
            var logins = await Task.WhenAll(firstTask, secondTask);
            var codes = new List<HttpStatusCode>();
            foreach (var login in logins)
            {
                using var client = fixture.Client(login.AccessToken);
                codes.Add((await client.GetAsync("/api/auth/me")).StatusCode);
            }
            Assert.Equal(1, codes.Count(x => x == HttpStatusCode.OK));
            Assert.Equal(1, codes.Count(x => x == HttpStatusCode.Unauthorized));
            await fixture.WithDb(async db => Assert.Equal(1, await db.UserSessions.CountAsync(x => x.UserId == user.UserId && x.ExpiresAt > DateTime.UtcNow)));
        }
    }

    [Theory]
    [InlineData("Cashier")]
    [InlineData("Kitchen Staff")]
    [InlineData("Inventory Staff")]
    [InlineData("Restaurant Manager")]
    [InlineData("System Administrator")]
    public async Task OtherRolesCanKeepMultipleSessions(string role)
    {
        var user = role == IdentityRoles.Admin ? fixture.Admin : await fixture.NewUser(role);
        var first = await fixture.Login(user);
        var second = await fixture.Login(user);
        using var oldClient = fixture.Client(first.AccessToken);
        using var newClient = fixture.Client(second.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await oldClient.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await newClient.GetAsync("/api/auth/me")).StatusCode);
    }

    [Theory]
    [InlineData("System Administrator", 201)]
    [InlineData("Restaurant Manager", 403)]
    [InlineData("Waiter", 403)]
    [InlineData("Cashier", 403)]
    [InlineData("Kitchen Staff", 403)]
    [InlineData("Inventory Staff", 403)]
    public async Task OnlyAdminCanCreateStaff(string actorRole, int expected)
    {
        var actor = actorRole == IdentityRoles.Admin ? fixture.Admin : await fixture.NewUser(actorRole);
        var login = await fixture.Login(actor);
        using var client = fixture.Client(login.AccessToken);
        var request = CreateRequest("Waiter");
        var response = await client.PostAsJsonAsync("/api/admin/users", request);
        Assert.Equal((HttpStatusCode)expected, response.StatusCode);
        await fixture.WithDb(async db => Assert.Equal(expected == 201, await db.Users.AnyAsync(x => x.UserName == request.UserName)));
        if (expected == 201)
        {
            using var anonymous = fixture.Client();
            var staffLogin = await anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequest(request.UserName, request.Password));
            Assert.Equal(HttpStatusCode.OK, staffLogin.StatusCode);
        }
    }

    [Theory]
    [InlineData("Restaurant Manager")]
    [InlineData("Waiter")]
    [InlineData("Cashier")]
    [InlineData("Kitchen Staff")]
    [InlineData("Inventory Staff")]
    public async Task AdminCanCreateEachStaffRole(string role)
    {
        var login = await fixture.Login(fixture.Admin);
        using var client = fixture.Client(login.AccessToken);
        var request = CreateRequest(role);
        var response = await client.PostAsJsonAsync("/api/admin/users", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<UserResponse>())!;
        Assert.Equal(role, created.RoleName);
        await fixture.WithDb(async db =>
        {
            var user = await db.Users.SingleAsync(x => x.UserId == created.UserId);
            Assert.True(user.PasswordHash != request.Password, "Password must be hashed.");
            Assert.True(await db.AuditLogs.AnyAsync(x => x.ActorUserId == fixture.Admin.UserId && x.Action == "CreateUser" && x.EntityId == user.UserId.ToString()));
        });
    }

    [Theory]
    [InlineData("System Administrator")]
    [InlineData("Customer")]
    [InlineData("Unsupported Role")]
    [InlineData("Missing Role")]
    public async Task AdminCannotCreateForbiddenRoles(string role)
    {
        var login = await fixture.Login(fixture.Admin);
        using var client = fixture.Client(login.AccessToken);
        var request = CreateRequest(role);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/admin/users", request)).StatusCode);
        await fixture.WithDb(async db => Assert.False(await db.Users.AnyAsync(x => x.UserName == request.UserName)));
    }

    [Fact]
    public async Task ResponsesNeverExposeSensitiveData()
    {
        var login = await fixture.Login(fixture.Admin);
        using var client = fixture.Client(login.AccessToken);
        var request = CreateRequest("Cashier");
        foreach (var response in new[]
        {
            await client.GetAsync("/api/auth/me"),
            await client.PostAsJsonAsync("/api/admin/users", request),
            await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(fixture.Admin.UserName, SqlIdentityFixture.Password)),
            await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("unknown", SqlIdentityFixture.Password))
        })
        {
            using (response)
            using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
            {
                AssertSafe(json.RootElement);
                Assert.True(!json.RootElement.GetRawText().Contains(SqlIdentityFixture.Password, StringComparison.Ordinal), "Response must not contain plaintext password.");
                Assert.True(!json.RootElement.GetRawText().Contains(fixture.Admin.PasswordHash, StringComparison.Ordinal), "Response must not contain stored password hash.");
            }
        }
        await fixture.WithDb(async db =>
        {
            foreach (var audit in await db.AuditLogs.ToListAsync())
            {
                var value = (audit.OldValue ?? "") + (audit.NewValue ?? "");
                Assert.True(!value.Contains(SqlIdentityFixture.Password) && !value.Contains(fixture.Admin.PasswordHash)
                    && !value.Contains(login.AccessToken) && !value.Contains("PasswordHash", StringComparison.OrdinalIgnoreCase), "Audit must contain no credentials.");
            }
        });
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("expired")]
    [InlineData("unsigned")]
    [InlineData("algorithm")]
    [InlineData("missing-session")]
    public async Task TokenValidationRejectsInvalidTokens(string fault)
    {
        var user = await fixture.NewUser("Cashier");
        var login = await fixture.Login(user);
        var parsed = new JwtSecurityTokenHandler().ReadJwtToken(login.AccessToken);
        var token = MintToken(parsed.Claims.Where(x => fault != "missing-session" || x.Type != "sid"), fault);
        using var client = fixture.Client(token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task CurrentRoleIsUsedForAuthorization()
    {
        var login = await fixture.Login(fixture.Admin);
        try
        {
            await fixture.WithDb(db => db.Users.Where(x => x.UserId == fixture.Admin.UserId)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.RoleId, fixture.Roles["Restaurant Manager"])));
            using var client = fixture.Client(login.AccessToken);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/admin/users", CreateRequest("Waiter"))).StatusCode);
            var me = await client.GetFromJsonAsync<UserResponse>("/api/auth/me");
            Assert.Equal("Restaurant Manager", me!.RoleName);
        }
        finally
        {
            await fixture.WithDb(db => db.Users.Where(x => x.UserId == fixture.Admin.UserId)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.RoleId, fixture.Roles[IdentityRoles.Admin])));
        }
        var waiter = await fixture.NewUser("Waiter");
        var waiterLogin = await fixture.Login(waiter);
        await fixture.WithDb(db => db.Users.Where(x => x.UserId == waiter.UserId)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.RoleId, fixture.Roles["Customer"])));
        using var changedClient = fixture.Client(waiterLogin.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await changedClient.GetAsync("/api/auth/me")).StatusCode);
    }

    [Theory]
    [InlineData("wrong-user")]
    [InlineData("unknown-session")]
    [InlineData("expired-session")]
    public async Task SessionMustBelongToUserAndBeUnexpired(string fault)
    {
        var user = await fixture.NewUser("Cashier");
        var login = await fixture.Login(user);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(login.AccessToken);
        var claims = jwt.Claims.ToList();
        if (fault == "wrong-user")
        {
            claims.RemoveAll(x => x.Type == "sub");
            claims.Add(new Claim("sub", fixture.Admin.UserId.ToString()));
        }
        if (fault == "unknown-session")
        {
            claims.RemoveAll(x => x.Type == "sid");
            claims.Add(new Claim("sid", int.MaxValue.ToString()));
        }
        if (fault == "expired-session")
            await fixture.WithDb(db => db.UserSessions.Where(x => x.UserId == user.UserId)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddMinutes(-1))));
        using var client = fixture.Client(MintToken(claims));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task UserNameUniquenessIsCaseInsensitive()
    {
        var user = await fixture.NewUser("Cashier");
        var login = await fixture.Login(fixture.Admin);
        using var client = fixture.Client(login.AccessToken);
        var request = CreateRequest("Waiter") with { UserName = user.UserName.ToUpperInvariant() };
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/admin/users", request)).StatusCode);
        using var anonymous = fixture.Client();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.UserName.ToUpperInvariant(), SqlIdentityFixture.Password))).StatusCode);
    }

    [Fact]
    public async Task PublicRegistrationAndRefreshAreAbsent()
    {
        using var client = fixture.Client();
        foreach (var url in new[] { "/api/auth/register", "/api/auth/refresh", "/api/auth/forgot-password" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync(url, JsonContent.Create(new { }))).StatusCode);
    }

    [Fact]
    public async Task DatabaseFirstSchemaHasOnlyApprovedTablesAndColumns()
    {
        var expected = new Dictionary<string, string[]>
        {
            ["Roles"] = ["RoleId", "RoleName", "Description"],
            ["Users"] = ["UserId", "RoleId", "UserName", "Email", "PhoneNumber", "FullName", "AvatarUrl", "PasswordHash", "IsActive", "CreatedAt", "UpdatedAt"],
            ["UserSessions"] = ["UserSessionId", "UserId", "IpAddress", "UserAgent", "DeviceType", "LoggedInAt", "ExpiresAt"],
            ["AuditLogs"] = ["AuditLogId", "ActorUserId", "Action", "EntityName", "EntityId", "OldValue", "NewValue", "IpAddress", "CreatedAt"]
        };
        await fixture.WithDb(async db =>
        {
            var connection = db.Database.GetDbConnection();
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT t.name, c.name FROM sys.tables t JOIN sys.columns c ON t.object_id=c.object_id WHERE t.is_ms_shipped=0 ORDER BY t.name, c.column_id";
            using var reader = await command.ExecuteReaderAsync();
            var actual = new Dictionary<string, List<string>>();
            while (await reader.ReadAsync())
            {
                var table = reader.GetString(0);
                if (!actual.ContainsKey(table)) actual[table] = [];
                actual[table].Add(reader.GetString(1));
            }
            Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
            foreach (var table in expected) Assert.Equal(table.Value, actual[table.Key]);
        });
    }

    [Theory]
    [InlineData("username-whitespace")]
    [InlineData("password-too-short")]
    [InlineData("fullname-empty")]
    [InlineData("email-invalid")]
    [InlineData("avatar-invalid")]
    public async Task InvalidCreateRequestsAreRejected(string fault)
    {
        var login = await fixture.Login(fixture.Admin);
        using var client = fixture.Client(login.AccessToken);
        var request = CreateRequest("Waiter");
        request = fault switch
        {
            "username-whitespace" => request with { UserName = " invalid " },
            "password-too-short" => request with { Password = "short" },
            "fullname-empty" => request with { FullName = " " },
            "email-invalid" => request with { Email = "invalid" },
            _ => request with { AvatarUrl = "file:///secret" }
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/admin/users", request)).StatusCode);
        await fixture.WithDb(async db => Assert.False(await db.Users.AnyAsync(x => x.UserName == request.UserName)));
    }

    private CreateUserRequest CreateRequest(string role) => new("new_" + Guid.NewGuid().ToString("N"),
        SqlIdentityFixture.Password, fixture.Roles.GetValueOrDefault(role, int.MaxValue), "New Test Staff");
    private async Task RejectLogin(string userName, string password)
    {
        using var client = fixture.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(userName, password))).StatusCode);
    }
    private Task AssertNoSessions(int userId) => fixture.WithDb(async db => Assert.False(await db.UserSessions.AnyAsync(x => x.UserId == userId)));
    private string MintToken(IEnumerable<Claim> claims, string? fault = null)
    {
        var now = DateTime.UtcNow;
        var key = fault == "signature" ? new string('x', 64) : fixture.SigningKey;
        var credentials = fault == "unsigned" ? null : new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            fault == "algorithm" ? SecurityAlgorithms.HmacSha384 : SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(fault == "issuer" ? "WrongIssuer" : "IdentityService",
            fault == "audience" ? "WrongAudience" : "RestaurantStaff",
            claims.Where(x => x.Type is not ("exp" or "nbf" or "iss" or "aud")),
            now.AddHours(-2), fault == "expired" ? now.AddHours(-1) : now.AddHours(1), credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    private static void AssertSafe(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
            {
                Assert.True(!new[] { "password", "passwordHash", "refreshToken" }.Contains(property.Name, StringComparer.OrdinalIgnoreCase), "Response contains a forbidden sensitive property.");
                AssertSafe(property.Value);
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) AssertSafe(child);
    }
}
