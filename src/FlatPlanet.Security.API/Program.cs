using System.Text;
using FlatPlanet.Security.API.Authentication;
using Microsoft.AspNetCore.Authentication;
using FlatPlanet.Security.API.Middleware;
using FlatPlanet.Security.Application.Common.Options;
using FlatPlanet.Security.Application.Interfaces;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Application.Services;
using FlatPlanet.Security.Infrastructure.BackgroundServices;
using FlatPlanet.Security.Infrastructure.ExternalServices;
using FlatPlanet.Security.Infrastructure.Persistence;
using FlatPlanet.Security.Infrastructure.Repositories;
using FlatPlanet.Security.Infrastructure.Security;
using FlatPlanet.Security.Infrastructure.Services;
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
builder.Services.Configure<SmsOptions>(builder.Configuration.GetSection(SmsOptions.Section));

// Database
builder.Services.AddSingleton<IDbConnectionFactory>(
    new NpgsqlConnectionFactory(dbOptions.BuildConnectionString()));

// CORS
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
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
builder.Services.AddScoped<IAdminAuditLogRepository, AdminAuditLogRepository>();
builder.Services.AddScoped<IMfaChallengeRepository, MfaChallengeRepository>();

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
builder.Services.AddScoped<IMfaService, MfaService>();
builder.Services.AddScoped<IIdentityVerificationService, IdentityVerificationServiceStub>();
if (builder.Environment.IsDevelopment())
    builder.Services.AddSingleton<ISmsSender, ConsoleSmsSender>();
else
    builder.Services.AddSingleton<ISmsSender, TwilioSmsSender>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHostedService<AuditLogCleanupService>();

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
