# FlatPlanet Security Platform

Centralized Identity and Access Management (IAM) / SSO service for all FlatPlanet applications. Handles authentication, MFA, authorization, session management, audit logging, and GDPR compliance in one place — so individual apps don't need to implement any of it.

---

## Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 10 |
| Database | PostgreSQL (via Supabase or self-hosted) |
| Data access | Dapper (no EF Core) |
| Password hashing | BCrypt.Net (work factor 12) |
| Auth tokens | JWT Bearer + opaque refresh tokens |
| MFA | TOTP (authenticator app) + email OTP fallback |
| Email | MailKit 4.x via SMTP (StartTLS port 587) |
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
  ...
  V25__mfa_challenge_email.sql      # Latest migration
docs/
  security-api-reference.md        # Complete endpoint + payload reference (v1.7.0)
  frontend-integration-guide.md    # Frontend integration walkthrough (v1.7.0)
  Feature.md                       # Full feature specification
  phase-*.md                       # Phase-by-phase implementation history
```

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- PostgreSQL database (Supabase or local)
- SMTP server for email delivery (MFA OTPs + password reset links)

### 1. Clone

```bash
git clone https://github.com/FlatPlanet-Hub/flatplanet-security-platform.git
cd flatplanet-security-platform
```

### 2. Configure

Fill in your values in `appsettings.json` (or override via environment variables / Azure App Config):

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
  "App": {
    "BaseUrl": "https://your-frontend.com"
  },
  "Cors": {
    "AllowedOrigins": ["https://your-frontend.com"]
  },
  "Smtp": {
    "Host": "smtp.your-provider.com",
    "Port": 587,
    "Username": "your-smtp-username",
    "Password": "your-smtp-password",
    "FromEmail": "noreply@your-domain.com",
    "FromName": "FlatPlanet Security"
  },
  "Mfa": {
    "TotpEncryptionKey": "32-byte-base64-encoded-AES256-key="
  }
}
```

> **`App.BaseUrl`** — the base URL of the frontend that hosts the `/reset-password` page. Password reset emails link to `{App.BaseUrl}/reset-password?token=...`. Since this is an SSO service, this is one central URL regardless of which app initiated the reset.

For local development, use `appsettings.Development.json` to override values without touching the base config.

### 3. Run Migrations

Apply the SQL migration files in order against your database. The latest migration is `V25__mfa_challenge_email.sql`.

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

---

## Authentication

The platform is a full SSO — no external auth provider. Apps delegate all auth to this service.

**User auth (JWT)**

Passwords are stored as bcrypt hashes (work factor 12). On login, credentials are verified against the database and a JWT access token + refresh token are issued.

```
POST /api/v1/auth/login
→ { accessToken, refreshToken, expiresIn, user, requiresMfa, mfaMethod, mfaEnrolmentPending }
```

Protected endpoints require:

```
Authorization: Bearer <accessToken>
```

Sessions are enforced by middleware on every request — idle and absolute timeouts are configurable via the security config API.

**MFA (TOTP + email OTP)**

Two supported methods — mutually exclusive per user:

| Method | Flow |
|---|---|
| `totp` | User scans a QR code in Microsoft/Google Authenticator. Login requires the 6-digit code. Email OTP fallback available if authenticator is lost. |
| `email_otp` | One-time code sent to the user's registered email address on each login. |

When `requiresMfa: true` is returned from login, no tokens are issued until the challenge is completed. Backup codes (8 per generation, single-use) provide a last-resort recovery path.

If `mfaEnrolmentPending: true`, MFA is required but not yet set up. A short-lived enrollment-only token (10 min, no refresh) is returned — valid only for the enrollment endpoints and logout.

**Profile self-service**

```
PATCH /api/v1/auth/me         → change display name and/or email (email change revokes all sessions)
POST /api/v1/auth/change-password   → change password (revokes all sessions on success)
POST /api/v1/auth/forgot-password   → send reset link to email (no auth required)
POST /api/v1/auth/reset-password    → consume reset token and set new password (no auth required)
```

Reset tokens expire in 15 minutes and are single-use. A SHA-256 hash is stored — the raw token is never persisted. The reset link always points to `App.BaseUrl/reset-password?token=...` — no per-app routing.

**Server-to-server (Service Token)**

Backend services authenticate using a static bearer token configured in `appsettings.json` under `ServiceToken.Token`. The service token grants full `platform_owner` + `app_admin` access. Set a minimum 32-character secret and keep it out of source control.

---

## Admin MFA Management

Admins (`app_admin` or `platform_owner`) can manage user MFA without user involvement:

| Endpoint | Description |
|---|---|
| `POST /api/v1/admin/mfa/{userId}/disable` | Disable MFA for a user |
| `POST /api/v1/admin/mfa/{userId}/reset` | Clear TOTP secret + backup codes — forces re-enrollment |
| `POST /api/v1/admin/mfa/{userId}/set-method` | Switch user between `totp` and `email_otp` |
| `POST /api/v1/admin/users/{userId}/force-reset-password` | Send password reset email on behalf of a user |

---

## Business Membership

Users can belong to more than one company simultaneously. Memberships are tracked in the `user_business_memberships` table and surfaced in the JWT.

The JWT includes parallel `business_codes` and `business_ids` claims. Single-membership users get a plain string; multiple memberships serialize as an array — always normalize before use:

```js
const codes = Array.isArray(jwt.business_codes) ? jwt.business_codes : jwt.business_codes ? [jwt.business_codes] : [];
```

---

## Documentation

- **[API Reference](docs/security-api-reference.md)** — all endpoints with request/response schemas, field tables, error cases (v1.7.0)
- **[Frontend Integration Guide](docs/frontend-integration-guide.md)** — step-by-step guide for frontend teams (v1.7.0)
- **[Feature Spec](docs/Feature.md)** — complete feature specification and requirements
