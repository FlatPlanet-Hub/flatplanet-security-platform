# FlatPlanet Security Platform

Centralized Identity and Access Management (IAM) service for all FlatPlanet applications. Handles authentication, authorization, session management, audit logging, and GDPR compliance in one place — so individual apps don't need to implement any of it.

---

## Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 10 |
| Database | PostgreSQL (via Supabase or self-hosted) |
| Data access | Dapper (no EF Core) |
| Password hashing | BCrypt.Net (work factor 12) |
| Auth tokens | JWT Bearer + opaque refresh tokens |
| API docs | Scalar UI at `/scalar/v1` |

---

## Project Structure

```
src/
  FlatPlanet.Security.API/          # Controllers, middleware, DI setup
  FlatPlanet.Security.Application/  # Services, interfaces, DTOs
  FlatPlanet.Security.Domain/       # Entities, enums, domain contracts
  FlatPlanet.Security.Infrastructure/ # Repositories, DB connection, bcrypt
  FlatPlanet.Security.Tests/        # Unit tests (xUnit + Moq)
db/
  V1__initial_schema.sql
  V2__fixes.sql
  V3__rls_fixes.sql
  V4__session_idle_timeout.sql
  V5__standalone_auth.sql
  V6__remove_registered_by_fk.sql
  V11__drop_granted_by_fk.sql
  V12__view_projects_permission.sql
  V13__role_permissions_fk_drop.sql
  V14__business_membership.sql
  seed_test_data.sql
docs/
  api-reference.md                  # Complete endpoint + payload reference
  Feature.md                        # Full feature specification
  phase-*.md                        # Phase-by-phase implementation history
```

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- PostgreSQL database (Supabase or local)

### 1. Clone

```bash
git clone https://github.com/FlatPlanet-Hub/flatplanet-security-platform.git
cd flatplanet-security-platform
```

### 2. Configure

Copy `appsettings.json` and fill in your values:

```json
{
  "Database": {
    "Host": "your-db-host",
    "Port": 6543,
    "Name": "postgres",
    "User": "your-db-user",
    "Password": "your-db-password"
  },
  "Jwt": {
    "Issuer": "flatplanet-security",
    "Audience": "flatplanet-apps",
    "SecretKey": "at-least-32-characters-long-secret-key",
    "AccessTokenExpiryMinutes": 60,
    "RefreshTokenExpiryDays": 7
  },
  "ServiceToken": {
    "Token": "your-service-token-min-32-characters"
  },
  "Cors": {
    "AllowedOrigins": ["https://your-frontend.com"]
  },
  "Smtp": {
    "Host": "smtp.your-provider.com",
    "Port": 587,
    "Username": "your-smtp-username",
    "Password": "your-smtp-password",
    "FromAddress": "noreply@your-domain.com",
    "FromName": "FlatPlanet Security"
  }
}
```

For local development, use `appsettings.Development.json` to override values without touching the base config.

### 3. Run Migrations

Apply the SQL migration files in order against your database:

```
db/V1__initial_schema.sql
db/V2__fixes.sql
db/V3__rls_fixes.sql
db/V4__session_idle_timeout.sql
db/V5__standalone_auth.sql
db/V6__remove_registered_by_fk.sql
db/V11__drop_granted_by_fk.sql
db/V12__view_projects_permission.sql
db/V13__role_permissions_fk_drop.sql
db/V14__business_membership.sql
```

Optionally seed test data:

```
db/seed_test_data.sql
```

### 4. Run

```bash
cd src/FlatPlanet.Security.API
dotnet run
```

API is available at `https://localhost:5001`. Interactive docs at `https://localhost:5001/scalar/v1`.

### 5. Run Tests

```bash
dotnet test
```

25 unit tests across `AuthService`, `AuthorizationService`, `CompanyService`, and `SessionValidationMiddleware`.

---

## Authentication

The platform supports two auth methods:

**User auth (JWT)**

The platform owns the full auth stack — no external provider. Passwords are stored as bcrypt hashes (work factor 12). On login, credentials are verified directly against the database and a JWT access token + refresh token are issued.

```
POST /api/v1/auth/login
→ { accessToken, refreshToken, expiresIn, user }
```

Protected endpoints require:

```
Authorization: Bearer <accessToken>
```

Sessions are enforced by middleware on every request — idle and absolute timeouts are configurable via the security config API.

**Password self-service**

Users can change their password or reset a forgotten one without admin involvement:

```
POST /api/v1/auth/change-password   → change password (JWT required); revokes all sessions on success
POST /api/v1/auth/forgot-password   → send reset link to email (no auth required)
POST /api/v1/auth/reset-password    → consume reset token and set new password (no auth required)
```

Reset tokens expire in 15 minutes and are single-use. A SHA-256 hash is stored — the raw token is never persisted. SMTP must be configured under the `Smtp` section in `appsettings.json` for reset emails to be delivered.

**Server-to-server (Service Token)**

Backend services (e.g. HubApi) authenticate using a static bearer token configured in `appsettings.json` under `ServiceToken.Token`. The service token grants full `platform_owner` + `app_admin` access. Set a minimum 32-character secret and keep it out of source control.

---

## Business Membership

Users can belong to more than one company simultaneously. Memberships are tracked in the `user_business_memberships` table and are surfaced in the JWT.

### JWT — `business_codes` claim

Every access token now includes a `business_codes` array listing the short code of each company the user is an active member of:

```json
{
  "sub": "dc88786a-...",
  "email": "chris.moriarty@flatplanet.com",
  "business_codes": ["fp"]
}
```

Downstream services (e.g. HubApi) can use this claim to restrict file or resource access to the user's companies without an additional API call.

### Membership Endpoints

All membership endpoints require the `PlatformOwner` role.

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/v1/companies/{companyId}/members` | List all members of a company |
| `POST` | `/api/v1/companies/{companyId}/members` | Add a user to a company |
| `DELETE` | `/api/v1/companies/{companyId}/members/{userId}` | Remove a user from a company |

### Company `code` field

Companies now carry an optional short `code` identifier (e.g. `"fp"`). The `code` is returned by `GET /api/v1/companies/{id}` and accepted by `POST /api/v1/companies` and `PUT /api/v1/companies/{id}`.

---

## Documentation

- **[API Reference](docs/security-api-reference.md)** — all 49 endpoints with request/response schemas, field tables, error cases
- **[Changelog](CHANGELOG.md)** — full version history
- **[Feature Spec](docs/Feature.md)** — complete feature specification and requirements
