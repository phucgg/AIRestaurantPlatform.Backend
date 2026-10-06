using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.Text;

// Deliberately has no database dependency. Never pass passwords as command-line arguments.
if (args.Length != 0 || Console.IsInputRedirected)
{
    Console.Error.WriteLine("Run interactively without arguments; enter the password at the hidden prompt.");
    return 1;
}

Console.Error.Write("Password: ");
var password = new StringBuilder();
while (true)
{
    var key = Console.ReadKey(intercept: true);
    if (key.Key == ConsoleKey.Enter) break;
    if (key.Key == ConsoleKey.Backspace)
    {
        if (password.Length > 0) password.Length--;
    }
    else if (!char.IsControl(key.KeyChar)) password.Append(key.KeyChar);
}
Console.Error.WriteLine();
if (password.Length == 0)
{
    Console.Error.WriteLine("Password cannot be empty.");
    return 1;
}

var hasher = new PasswordHasher<object>(Options.Create(new PasswordHasherOptions
{
    CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
    IterationCount = 100_000
}));
Console.WriteLine(hasher.HashPassword(new object(), password.ToString()));
password.Clear();
return 0;
