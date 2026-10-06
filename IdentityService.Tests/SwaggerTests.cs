using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using IdentityService.API.Controllers;
using IdentityService.API.OpenApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using Xunit;

namespace IdentityService.Tests;

public sealed class SwaggerTests
{
    private static WebApplicationFactory<Program> Factory(string environment) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment(environment);
            host.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Swagger requests never access a database. Do not use development/application secrets.
                ["ConnectionStrings:Identity"] = "Server=127.0.0.1,1;Database=IdentityTests_swagger;Integrated Security=True;Encrypt=True",
                ["Jwt:Key"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
                ["Jwt:Issuer"] = "IdentityService",
                ["Jwt:Audience"] = "RestaurantStaff",
                ["Logging:LogLevel:Default"] = "Warning"
            }));
        });

    [Fact]
    public async Task DevelopmentSwaggerUiAndAssetsAreAvailable()
    {
        using var factory = Factory("Development");
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/swagger");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("AI-Powered Smart Restaurant Platform", html);
        Assert.Contains("swagger-ui-bundle.js", html);
        var initialization = await client.GetStringAsync("/swagger/index.js");
        Assert.Contains("./v1/swagger.json", initialization);
        Assert.Contains("\"persistAuthorization\":false", initialization);
        foreach (var asset in new[] { "swagger-ui-bundle.js", "swagger-ui.css" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/swagger/" + asset)).StatusCode);
    }

    [Fact]
    public async Task DevelopmentOpenApiHasExactEndpointsBearerMetadataAndSafeDtos()
    {
        using var factory = Factory("Development");
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(document);
        var root = json.RootElement;
        Assert.Equal("AI-Powered Smart Restaurant Platform", root.GetProperty("info").GetProperty("title").GetString());
        var paths = root.GetProperty("paths");
        var expected = new Dictionary<string, string>
        {
            ["/api/auth/login"] = "post", ["/api/auth/me"] = "get",
            ["/api/auth/logout"] = "post", ["/api/admin/users"] = "post"
        };
        Assert.Equal(expected.Keys.Order(), paths.EnumerateObject().Select(x => x.Name).Order());
        foreach (var endpoint in expected)
        {
            var path = paths.GetProperty(endpoint.Key);
            Assert.Single(path.EnumerateObject());
            var operation = path.GetProperty(endpoint.Value);
            if (endpoint.Key == "/api/auth/login")
                Assert.True(!operation.TryGetProperty("security", out var security) || security.GetArrayLength() == 0);
            else
            {
                var security = operation.GetProperty("security");
                Assert.Single(security.EnumerateArray());
                Assert.Equal(0, security[0].GetProperty("Bearer").GetArrayLength());
            }
        }
        Assert.Contains("System Administrator", paths.GetProperty("/api/admin/users").GetProperty("post").GetProperty("description").GetString());
        var bearer = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        Assert.Equal("JWT", bearer.GetProperty("bearerFormat").GetString());
        Assert.Equal("#/components/schemas/LoginRequest", RequestSchema(paths, "/api/auth/login"));
        Assert.Equal("#/components/schemas/CreateUserRequest", RequestSchema(paths, "/api/admin/users"));
        Assert.Equal("#/components/schemas/LoginResponse", ResponseSchema(paths, "/api/auth/login", "post", "200"));
        Assert.Equal("#/components/schemas/UserResponse", ResponseSchema(paths, "/api/auth/me", "get", "200"));
        Assert.Equal("#/components/schemas/UserResponse", ResponseSchema(paths, "/api/admin/users", "post", "201"));
        var schemas = root.GetProperty("components").GetProperty("schemas");
        Assert.False(schemas.TryGetProperty("User", out _));
        Assert.DoesNotContain("passwordHash", document, StringComparison.OrdinalIgnoreCase);
        Assert.True(schemas.GetProperty("UserResponse").GetProperty("properties").TryGetProperty("userId", out _));
    }

    [Theory]
    [InlineData("Production", "/swagger")]
    [InlineData("Production", "/swagger/index.html")]
    [InlineData("Production", "/swagger/v1/swagger.json")]
    [InlineData("Staging", "/swagger")]
    [InlineData("Staging", "/swagger/v1/swagger.json")]
    public async Task SwaggerIsUnavailableOutsideDevelopment(string environment, string path)
    {
        using var factory = Factory(environment);
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public void AllowAnonymousOverridesAuthorizationMetadata()
    {
        var apiDescription = new ApiDescription
        {
            ActionDescriptor = new ActionDescriptor
            {
                EndpointMetadata = [new AuthorizeAttribute(), new AllowAnonymousAttribute()]
            }
        };
        var operation = new OpenApiOperation
        {
            Security = [new OpenApiSecurityRequirement { [new OpenApiSecurityScheme()] = [] }]
        };
        var context = new OperationFilterContext(apiDescription, null!, new SchemaRepository(),
            typeof(AuthController).GetMethod(nameof(AuthController.Login))!);
        new BearerSecurityOperationFilter(Options.Create(new AuthorizationOptions())).Apply(operation, context);
        Assert.Empty(operation.Security);
    }

    private static string? RequestSchema(JsonElement paths, string path) => paths.GetProperty(path).GetProperty("post")
        .GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString();
    private static string? ResponseSchema(JsonElement paths, string path, string method, string status) => paths.GetProperty(path)
        .GetProperty(method).GetProperty("responses").GetProperty(status).GetProperty("content").GetProperty("application/json")
        .GetProperty("schema").GetProperty("$ref").GetString();
}
