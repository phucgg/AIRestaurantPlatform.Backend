using System;
using System.Collections.Generic;

namespace IdentityService.API.Models;

public partial class UserSession
{
    public int UserSessionId { get; set; }

    public int UserId { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public string? DeviceType { get; set; }

    public DateTime LoggedInAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public virtual User User { get; set; } = null!;
}
