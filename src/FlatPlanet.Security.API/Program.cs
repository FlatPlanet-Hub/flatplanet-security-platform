using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using FlatPlanet.Security.API.Authentication;
using FlatPlanet.Security.API.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using FlatPlanet.Security.API.Middleware;
using FlatPlanet.Security.Application.Common.Options;
using FlatPlanet.Security.Application.Interfaces;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Application.Services;
using FlatPlanet.Security.Infrastructure.BackgroundServices;
using FlatPlanet.Security.Infrastructure.Email;
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
builder.Services.Configure<AppOptions>(builder.Configuration.GetSection(AppOptions.Section));
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.Section));
builder.Services.Configure<MfaOptions>(builder.Configuration.GetSection(MfaOptions.Section));

// Database
builder.Services.AddSingleton<IDbConnectionFactory>(
    new NpgsqlConnectionFactory(dbOptions.BuildConnectionString()));

// CORS — all 28 active project origins are listed in appsettings.json.
// No DB query at startup — avoids connection hangs on cold restarts.
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

    // AdminAccess admits user JWTs with platform_owner/app_admin role
    // AND per-service tokens (which are gated separately by [RequireScope] on each
    // admin endpoint). Without the service_token allowance here, narrow-scoped
    // service tokens would 403 at the policy check before reaching the scope handler.
    options.AddPolicy("AdminAccess", policy =>
        policy.AddAuthenticationSchemes("ServiceToken", JwtBearerDefaults.AuthenticationScheme)
              .RequireRole("platform_owner", "app_admin", "service_token"));
});

// Per-service token scope-based authorization. RequireScope("...") attributes
// resolve via this provider; the ScopeAuthorizationHandler does the actual check.
builder.Services.AddSingleton<IAuthorizationPolicyProvider, ScopePolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, ScopeAuthorizationHandler>();

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
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();

// Rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Return our standard JSON envelope instead of ASP.NET Core's empty 429 body.
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            "{\"success\":false,\"message\":\"Too many requests. Please try again later.\"}",
            token);
    };

    // forgot-password: 3 per 15 min per IP
    options.AddPolicy("forgot-password", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromMinutes(15)
            }));

    // change-password: 5 per 15 min per user (JWT sub claim)
    options.AddPolicy("change-password", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(15)
            }));

    // authorize: 60 per min per user
    options.AddPolicy("authorize", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1)
            }));

    // update-profile: 10 per 15 min per user (prevents authenticated email enumeration via 409)
    options.AddPolicy("update-profile", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(15)
            }));

    // mfa-verify: 5 per min per IP (TOTP and email OTP login/enrol verify endpoints)
    options.AddPolicy("mfa-verify", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1)
            }));
});

builder.Services.AddHealthChecks();

// Repositories
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ISessionRepository, SessionRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<ILoginAttemptRepository, LoginAttemptRepository>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
builder.Services.AddScoped<IAdminAuditLogRepository, AdminAuditLogRepository>();
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
builder.Services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();
builder.Services.AddScoped<IMfaChallengeRepository, MfaChallengeRepository>();
builder.Services.AddScoped<IMfaBackupCodeRepository, MfaBackupCodeRepository>();
builder.Services.AddScoped<IIdentityVerificationRepository, IdentityVerificationRepository>();
builder.Services.AddScoped<IServiceTokenRepository, ServiceTokenRepository>();

// Services
builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<ILoginService, LoginService>();
builder.Services.AddScoped<IPasswordService, PasswordService>();
builder.Services.AddScoped<IProfileService, ProfileService>();
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
builder.Services.AddScoped<IBusinessMembershipService, BusinessMembershipService>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddSingleton<ITotpSecretEncryptor, TotpSecretEncryptor>();
builder.Services.AddSingleton<ITotpVerifier, TotpVerifier>();
builder.Services.AddScoped<IMfaService, MfaService>();
builder.Services.AddScoped<IIdentityVerificationService, IdentityVerificationService>();
builder.Services.AddScoped<IServiceTokenService, ServiceTokenService>();
builder.Services.AddHostedService<AuditLogCleanupService>();

var app = builder.Build();

// Fail boot if any AdminAccess-gated action is missing [RequireScope].
// See AdminAccessScopeInvariant for rationale.
AdminAccessScopeInvariant.Verify(typeof(Program).Assembly);

// DB pre-warm intentionally removed:
// Opening connections before app.Run() blocks Azure's warmup probe → 503/504 on cold start.
// Pool Size=0 (default) — connections are opened on first request and released when idle.

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
// UseHttpsRedirection() intentionally removed:
// Azure App Service terminates TLS at the load balancer — the container receives plain HTTP on port 8080.
// Enabling this causes an infinite 301 redirect loop that Azure LB follows until 240s timeout → 504.
app.UseCors();
app.UseAuthentication();
app.UseMiddleware<SessionValidationMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();

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
