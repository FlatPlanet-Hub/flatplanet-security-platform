using System.Text;
using FlatPlanet.Security.API.Authentication;
using Microsoft.AspNetCore.Authentication;
using FlatPlanet.Security.API.Middleware;
using FlatPlanet.Security.Application.Common.Options;
using FlatPlanet.Security.Application.Interfaces;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Application.Services;
using FlatPlanet.Security.Infrastructure.Persistence;
using FlatPlanet.Security.Infrastructure.Repositories;
using FlatPlanet.Security.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

// Enable Dapper snake_case → PascalCase column mapping globally
// Without this, columns like config_key, full_name, company_id are not mapped
// to ConfigKey, FullName, CompanyId — leaving all properties at default values.
Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

var builder = WebApplication.CreateBuilder(args);

// Options
var dbOptions = builder.Configuration.GetSection(DatabaseOptions.Section).Get<DatabaseOptions>()!;
var jwtOptions = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>()!;

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.Section));
builder.Services.Configure<ServiceTokenOptions>(builder.Configuration.GetSection(ServiceTokenOptions.Section));

// Database
builder.Services.AddSingleton<IDbConnectionFactory>(
    new NpgsqlConnectionFactory(dbOptions.BuildConnectionString()));

// CORS — load origins from registered apps + appsettings fallback
var configOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
var dbOrigins = Array.Empty<string>();
try
{
    await using var tempConn = new Npgsql.NpgsqlConnection(dbOptions.BuildConnectionString());
    dbOrigins = (await Dapper.SqlMapper.QueryAsync<string>(
        tempConn,
        "SELECT DISTINCT base_url FROM apps WHERE status = 'active' AND base_url IS NOT NULL AND base_url <> ''"))
        .ToArray();
}
catch (Exception ex)
{
    Console.WriteLine($"[CORS] Could not load origins from DB, using config only: {ex.Message}");
}

var allowedOrigins = configOrigins.Union(dbOrigins).ToArray();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddScheme<AuthenticationSchemeOptions, ServiceTokenAuthHandler>("ServiceToken", _ => { })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SecretKey)),
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("PlatformOwner", policy =>
        policy.AddAuthenticationSchemes("ServiceToken", JwtBearerDefaults.AuthenticationScheme)
              .RequireRole("platform_owner"));
    options.AddPolicy("AdminAccess", policy =>
        policy.AddAuthenticationSchemes("ServiceToken", JwtBearerDefaults.AuthenticationScheme)
              .RequireRole("platform_owner", "app_admin"));
});

// OpenAPI
builder.Services.AddOpenApi();

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(e => e.Value?.Errors.Count > 0)
                .ToDictionary(
                    e => e.Key,
                    e => e.Value!.Errors.Select(x => x.ErrorMessage).ToArray());

            return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(new
            {
                success = false,
                message = "Validation failed.",
                errors
            });
        };
    });
builder.Services.AddHealthChecks();

// Repositories
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ISessionRepository, SessionRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<ILoginAttemptRepository, LoginAttemptRepository>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
builder.Services.AddScoped<ISecurityConfigRepository, SecurityConfigRepository>();
builder.Services.AddScoped<IUserAppRoleRepository, UserAppRoleRepository>();
builder.Services.AddScoped<IRolePermissionRepository, RolePermissionRepository>();
builder.Services.AddScoped<IAppRepository, AppRepository>();
builder.Services.AddScoped<IRoleRepository, RoleRepository>();
builder.Services.AddScoped<ICompanyRepository, CompanyRepository>();
builder.Services.AddScoped<IResourceTypeRepository, ResourceTypeRepository>();
builder.Services.AddScoped<IResourceRepository, ResourceRepository>();
builder.Services.AddScoped<IPermissionRepository, PermissionRepository>();
builder.Services.AddScoped<IBusinessMembershipRepository, BusinessMembershipRepository>();

// Services
builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IAccessAuthorizationService, AuthorizationService>();
builder.Services.AddScoped<IUserContextService, UserContextService>();
builder.Services.AddScoped<ICompanyService, CompanyService>();
builder.Services.AddScoped<IAppService, AppService>();
builder.Services.AddScoped<IResourceTypeService, ResourceTypeService>();
builder.Services.AddScoped<IResourceService, ResourceService>();
builder.Services.AddScoped<IRoleService, RoleService>();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<IUserAccessService, UserAccessService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IOffboardingService, OffboardingService>();
builder.Services.AddScoped<IComplianceService, ComplianceService>();
builder.Services.AddScoped<ISecurityConfigService, SecurityConfigService>();
builder.Services.AddScoped<IAccessReviewService, AccessReviewService>();

var app = builder.Build();

// Pre-warm the DB connection pool so the first login request doesn't pay
// the cold-start SSL handshake cost for every sequential DB call (~20s on Supabase).
var dbFactory = app.Services.GetRequiredService<IDbConnectionFactory>();
using (var c1 = await dbFactory.CreateConnectionAsync())
using (var c2 = await dbFactory.CreateConnectionAsync()) { }

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseMiddleware<SessionValidationMiddleware>();
app.UseAuthorization();

// OpenAPI spec endpoint + Scalar UI (dev-only is intentionally not enforced —
// restrict via network/infra in production instead of compile-time env checks)
app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options.Title = "FlatPlanet Security Platform API";
    options.Theme = ScalarTheme.DeepSpace;
    options.DefaultHttpClient = new(ScalarTarget.JavaScript, ScalarClient.Fetch);
    options.AddPreferredSecuritySchemes("Bearer")
           .AddHttpAuthentication("Bearer", bearer => { bearer.Token = string.Empty; });
});

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
