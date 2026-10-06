using System.Security.Claims;
using IdentityService.API.Contracts;
using IdentityService.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IdentityService.API.Controllers;

[ApiController]
[Route("api/admin/users")]
[Authorize(Policy = "Admin")]
public sealed class AdminUsersController(IdentityAuthService auth) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest request, CancellationToken ct)
    {
        if (request.AvatarUrl is not null && (!Uri.TryCreate(request.AvatarUrl, UriKind.Absolute, out var url)
            || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp)))
            return BadRequest(new { message = "AvatarUrl must be an absolute HTTP(S) URL." });
        var (user, status) = await auth.CreateUser(int.Parse(User.FindFirstValue("sub")!),
            int.Parse(User.FindFirstValue("sid")!), request, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        return status switch
        {
            201 => StatusCode(201, user),
            400 => BadRequest(new { message = "Role is not an allowed staff role." }),
            409 => Conflict(new { message = "UserName already exists." }),
            _ => Forbid()
        };
    }
}
