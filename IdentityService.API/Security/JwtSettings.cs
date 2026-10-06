using System.ComponentModel.DataAnnotations;

namespace IdentityService.API.Security;

public sealed class JwtSettings
{
    [Required] public string Issuer { get; set; } = "";
    [Required] public string Audience { get; set; } = "";
    [Required, MinLength(32)] public string Key { get; set; } = "";
    [Range(1, 1440)] public int LifetimeMinutes { get; set; } = 60;
}
