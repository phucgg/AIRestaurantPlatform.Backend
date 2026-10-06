using System.ComponentModel.DataAnnotations;

namespace IdentityService.API.Security;

public sealed class IdentityDatabaseSettings
{
    [Required] public string Identity { get; set; } = "";
}
