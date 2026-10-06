using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Xunit;

namespace IdentityService.Tests;

public class PasswordHasherTests
{
    private static PasswordHasher<object> CreateHasher() => new(Options.Create(new PasswordHasherOptions
    {
        CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
        IterationCount = 100_000
    }));

    [Fact]
    public void HashRoundTripAcceptsCorrectPassword()
    {
        var user = new object();
        var hash = CreateHasher().HashPassword(user, "Test-only password 123!");
        Assert.Equal(PasswordVerificationResult.Success,
            CreateHasher().VerifyHashedPassword(user, hash, "Test-only password 123!"));
    }

    [Fact]
    public void HashRejectsWrongPassword()
    {
        var user = new object();
        var hash = CreateHasher().HashPassword(user, "Test-only password 123!");
        Assert.Equal(PasswordVerificationResult.Failed,
            CreateHasher().VerifyHashedPassword(user, hash, "Incorrect test password"));
    }

    [Fact]
    public void HashesHaveIndependentSaltsAndContainNoPlaintext()
    {
        const string password = "Test-only password 123!";
        var user = new object();
        var first = CreateHasher().HashPassword(user, password);
        var second = CreateHasher().HashPassword(user, password);
        Assert.NotEqual(first, second);
        Assert.DoesNotContain(password, first);
        Assert.DoesNotContain(password, second);
    }
}
