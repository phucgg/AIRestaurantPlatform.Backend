namespace IdentityService.API.Security;

public static class IdentityRoles
{
    public const string Admin = "System Administrator";
    public const string Waiter = "Waiter";
    public static readonly IReadOnlySet<string> Staff = new HashSet<string>(StringComparer.Ordinal)
    {
        "Restaurant Manager", Waiter, "Cashier", "Kitchen Staff", "Inventory Staff"
    };
    public static bool CanLogin(string role) => role == Admin || Staff.Contains(role);
}
