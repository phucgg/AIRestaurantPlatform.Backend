using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace IdentityService.API.OpenApi;

public sealed class BearerSecurityOperationFilter(IOptions<AuthorizationOptions> authorization) : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAllowAnonymous>().Any())
        {
            operation.Security = [];
            return;
        }
        var requirements = metadata.OfType<IAuthorizeData>().ToArray();
        if (requirements.Length == 0) return;

        operation.Security = [new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            }] = []
        }];

        var roles = requirements.SelectMany(requirement =>
            (requirement.Roles ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Concat(string.IsNullOrEmpty(requirement.Policy) ? [] :
                authorization.Value.GetPolicy(requirement.Policy)?.Requirements
                    .OfType<RolesAuthorizationRequirement>().SelectMany(x => x.AllowedRoles) ?? []))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (roles.Length > 0)
            operation.Description = (operation.Description + "\nRequired role: " + string.Join(", ", roles)).Trim();
    }
}
