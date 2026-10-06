using System;
using System.Collections.Generic;

namespace IdentityService.API.Models;

public partial class AuditLog
{
    public int AuditLogId { get; set; }

    public int ActorUserId { get; set; }

    public string Action { get; set; } = null!;

    public string EntityName { get; set; } = null!;

    public string? EntityId { get; set; }

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public string? IpAddress { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual User ActorUser { get; set; } = null!;
}
