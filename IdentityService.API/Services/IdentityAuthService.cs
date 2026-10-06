using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using IdentityService.API.Contracts;
using IdentityService.API.Data;
using IdentityService.API.Models;
using IdentityService.API.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace IdentityService.API.Services;

public sealed class IdentityAuthService(IdentityDbContext db, IPasswordHasher<User> hasher,
    IOptions<JwtSettings> settings, ILogger<IdentityAuthService> logger)
{
    public async Task<LoginResponse?> Login(LoginRequest request, string? ip, string? agent, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // UPDLOCK is held until commit, serializing this user's logins across API instances.
        // Only Waiter sessions are expired; other roles retain multiple sessions.
        var user = await db.Users.FromSqlInterpolated($"SELECT * FROM dbo.Users WITH (UPDLOCK, ROWLOCK) WHERE UserName = {request.UserName}")
            .Include(x => x.Role).SingleOrDefaultAsync(ct);
        if (user is null || !user.IsActive || !IdentityRoles.CanLogin(user.Role.RoleName)
            || hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
        {
            logger.LogInformation("Identity login rejected.");
            if (user is not null)
            {
                Audit(user.UserId, "LoginFailed", "User", user.UserId, ip, DateTime.UtcNow);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            return null;
        }
        var now = DateTime.UtcNow;
        if (user.Role.RoleName == IdentityRoles.Waiter)
            await db.UserSessions.Where(x => x.UserId == user.UserId && x.ExpiresAt > now)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.ExpiresAt, now), ct);
        var session = new UserSession
        {
            UserId = user.UserId, IpAddress = Limit(ip, 45), UserAgent = Limit(agent, 512),
            DeviceType = null, LoggedInAt = now,
            ExpiresAt = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc)
                .AddMinutes(settings.Value.LifetimeMinutes)
        };
        db.UserSessions.Add(session);
        await db.SaveChangesAsync(ct);
        Audit(user.UserId, "Login", "UserSession", session.UserSessionId, ip, now);
        await db.SaveChangesAsync(ct);
        var token = CreateToken(user, session);
        await transaction.CommitAsync(ct);
        return new LoginResponse(token, AsUtc(session.ExpiresAt), UserResponse.From(user));
    }

    public async Task<UserResponse?> Me(int userId, int sessionId, CancellationToken ct)
    {
        var session = await ValidSession(userId, sessionId, ct);
        return session is null ? null : UserResponse.From(session.User);
    }

    public async Task<bool> Logout(int userId, int sessionId, string? ip, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow;
        var affected = await db.UserSessions.Where(x => x.UserSessionId == sessionId && x.UserId == userId
            && x.ExpiresAt > now && x.User.IsActive)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.ExpiresAt, now), ct);
        if (affected == 0) return false;
        Audit(userId, "Logout", "UserSession", sessionId, ip, now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<(UserResponse? User, int Status)> CreateUser(int actorId, int sessionId,
        CreateUserRequest request, string? ip, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Recheck Admin inside the write transaction, holding the actor's user lock.
        var actor = await db.Users.FromSqlInterpolated($"SELECT * FROM dbo.Users WITH (UPDLOCK, ROWLOCK) WHERE UserId = {actorId}")
            .Include(x => x.Role).SingleOrDefaultAsync(ct);
        if (actor is null || !actor.IsActive || actor.Role.RoleName != IdentityRoles.Admin
            || !await db.UserSessions.AnyAsync(x => x.UserSessionId == sessionId && x.UserId == actorId
                && x.ExpiresAt > DateTime.UtcNow, ct)) return (null, 403);
        var role = await db.Roles.SingleOrDefaultAsync(x => x.RoleId == request.RoleId, ct);
        if (role is null || !IdentityRoles.Staff.Contains(role.RoleName)) return (null, 400);
        if (await db.Users.AnyAsync(x => x.UserName == request.UserName, ct)) return (null, 409);
        var now = DateTime.UtcNow;
        var user = new User
        {
            UserName = request.UserName, RoleId = role.RoleId, Role = role, FullName = request.FullName,
            Email = request.Email, PhoneNumber = request.PhoneNumber, AvatarUrl = request.AvatarUrl,
            IsActive = true, CreatedAt = now
        };
        user.PasswordHash = hasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
            Audit(actorId, "CreateUser", "User", user.UserId, ip, now,
                JsonSerializer.Serialize(new { user.UserId, user.RoleId, user.UserName, user.IsActive }));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return (UserResponse.From(user), 201);
        }
        catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return (null, 409);
        }
    }

    private async Task<UserSession?> ValidSession(int userId, int sessionId, CancellationToken ct)
    {
        var session = await db.UserSessions.AsNoTracking().Include(x => x.User).ThenInclude(x => x.Role)
            .SingleOrDefaultAsync(x => x.UserSessionId == sessionId && x.UserId == userId && x.ExpiresAt > DateTime.UtcNow, ct);
        return session is not null && session.User.IsActive && IdentityRoles.CanLogin(session.User.Role.RoleName)
            ? session : null;
    }

    private string CreateToken(User user, UserSession session)
    {
        var jwt = settings.Value;
        var token = new JwtSecurityToken(jwt.Issuer, jwt.Audience,
            [new Claim("sub", user.UserId.ToString(CultureInfo.InvariantCulture)),
             new Claim("sid", session.UserSessionId.ToString(CultureInfo.InvariantCulture)),
             new Claim("name", user.UserName), new Claim("role", user.Role.RoleName),
             new Claim("jti", Guid.NewGuid().ToString("N"))],
            AsUtc(session.LoggedInAt), AsUtc(session.ExpiresAt),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private void Audit(int actorId, string action, string entity, int entityId, string? ip, DateTime now, string? value = null)
        => db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorId, Action = action, EntityName = entity,
            EntityId = entityId.ToString(CultureInfo.InvariantCulture), NewValue = value,
            IpAddress = Limit(ip, 45), CreatedAt = now
        });
    private static string? Limit(string? value, int length) => value is null ? null : value[..Math.Min(value.Length, length)];
    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
