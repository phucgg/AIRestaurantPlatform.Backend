using System.ComponentModel.DataAnnotations;
using IdentityService.API.Models;

namespace IdentityService.API.Contracts;

public sealed record LoginRequest(
    [Required, StringLength(100), RegularExpression(@"^[\p{L}\p{Nd}_.-]+$")] string UserName,
    [Required, StringLength(256)] string Password);

public sealed record CreateUserRequest(
    [Required, StringLength(100), RegularExpression(@"^[\p{L}\p{Nd}_.-]+$")] string UserName,
    [Required, StringLength(256, MinimumLength = 12)] string Password,
    [Range(1, int.MaxValue)] int RoleId,
    [Required, StringLength(200)] string FullName,
    [EmailAddress, StringLength(254)] string? Email = null,
    [StringLength(30)] string? PhoneNumber = null,
    [StringLength(2048)] string? AvatarUrl = null);

public sealed record UserResponse(int UserId, int RoleId, string RoleName, string UserName,
    string? Email, string? PhoneNumber, string FullName, string? AvatarUrl, bool IsActive,
    DateTime CreatedAt, DateTime? UpdatedAt)
{
    public static UserResponse From(User user) => new(user.UserId, user.RoleId, user.Role.RoleName,
        user.UserName, user.Email, user.PhoneNumber, user.FullName, user.AvatarUrl,
        user.IsActive, DateTime.SpecifyKind(user.CreatedAt, DateTimeKind.Utc),
        user.UpdatedAt is null ? null : DateTime.SpecifyKind(user.UpdatedAt.Value, DateTimeKind.Utc));
}

public sealed record LoginResponse(string AccessToken, DateTime ExpiresAt, UserResponse User);
