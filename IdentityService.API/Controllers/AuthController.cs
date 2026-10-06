using System.Security.Claims;
using IdentityService.API.Contracts;
using IdentityService.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IdentityService.API.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IdentityAuthService auth) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await auth.Login(request, HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(), ct);
        return result is null ? Unauthorized(new { message = "Invalid credentials or account unavailable." }) : Ok(result);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct) =>
        await auth.Logout(int.Parse(User.FindFirstValue("sub")!), int.Parse(User.FindFirstValue("sid")!),
            HttpContext.Connection.RemoteIpAddress?.ToString(), ct) ? NoContent() : Unauthorized();

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var result = await auth.Me(int.Parse(User.FindFirstValue("sub")!), int.Parse(User.FindFirstValue("sid")!), ct);
        return result is null ? Unauthorized() : Ok(result);
    }
}
