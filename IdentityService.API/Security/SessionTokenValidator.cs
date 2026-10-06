using System.Globalization;
using System.Security.Claims;
using IdentityService.API.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.API.Security;

public sealed class SessionTokenValidator(IdentityDbContext db) : JwtBearerEvents
{
    public override async Task TokenValidated(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (!int.TryParse(principal?.FindFirstValue("sub"), NumberStyles.None, CultureInfo.InvariantCulture, out var userId)
            || !int.TryParse(principal?.FindFirstValue("sid"), NumberStyles.None, CultureInfo.InvariantCulture, out var sessionId))
        {
            context.Fail("Invalid session claims.");
            return;
        }
        var session = await db.UserSessions.AsNoTracking().Include(x => x.User).ThenInclude(x => x.Role)
            .SingleOrDefaultAsync(x => x.UserSessionId == sessionId, context.HttpContext.RequestAborted);
        if (session is null || session.UserId != userId || session.ExpiresAt <= DateTime.UtcNow
            || !session.User.IsActive || !IdentityRoles.CanLogin(session.User.Role.RoleName))
        {
            context.Fail("Session is no longer valid.");
            return;
        }
        // Replace all token authorization data with current database data.
        context.Principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", userId.ToString(CultureInfo.InvariantCulture)),
            new Claim("sid", sessionId.ToString(CultureInfo.InvariantCulture)),
            new Claim("name", session.User.UserName),
            new Claim("role", session.User.Role.RoleName)
        }, "Bearer", "name", "role"));
    }
}
