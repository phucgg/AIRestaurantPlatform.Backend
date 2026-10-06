using System.Text;
using IdentityService.API.Data;
using IdentityService.API.Models;
using IdentityService.API.Security;
using IdentityService.API.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddOptions<JwtSettings>().BindConfiguration("Jwt").ValidateDataAnnotations()
    .Validate(jwt => Encoding.UTF8.GetByteCount(jwt.Key) >= 32, "Jwt:Key must contain at least 32 bytes.")
    .ValidateOnStart();
builder.Services.AddOptions<IdentityDatabaseSettings>().BindConfiguration("ConnectionStrings")
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddDbContext<IdentityDbContext>((services, options) => options.UseSqlServer(
    services.GetRequiredService<Microsoft.Extensions.Options.IOptions<IdentityDatabaseSettings>>().Value.Identity));
builder.Services.Configure<PasswordHasherOptions>(options =>
{
    options.CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3;
    options.IterationCount = 100_000;
});
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<IdentityAuthService>();
builder.Services.AddScoped<SessionTokenValidator>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<Microsoft.Extensions.Options.IOptions<JwtSettings>>((options, configured) =>
{
    var jwt = configured.Value;
    options.MapInboundClaims = false;
    options.IncludeErrorDetails = false;
    options.EventsType = typeof(SessionTokenValidator);
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
        ValidateIssuer = true, ValidIssuer = jwt.Issuer,
        ValidateAudience = true, ValidAudience = jwt.Audience,
        ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ClockSkew = TimeSpan.Zero, NameClaimType = "name", RoleClaimType = "role"
    };
});
builder.Services.AddAuthorization(options => options.AddPolicy("Admin", policy =>
    policy.RequireAuthenticatedUser().RequireRole(IdentityRoles.Admin)));
var app = builder.Build();
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program;
