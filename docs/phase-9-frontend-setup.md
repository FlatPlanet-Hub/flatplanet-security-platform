# Phase 9 — Frontend Setup

## Goal

Make the API consumable by a browser-based frontend:
- CORS so browsers can call the API without being blocked
- OpenAPI spec endpoint so tooling can introspect the API
- Scalar UI so developers can explore and test every endpoint interactively

---

## Tasks

- [x] Add CORS middleware driven by `Cors:AllowedOrigins` config
- [x] Add `Microsoft.AspNetCore.OpenApi` — serves spec at `GET /openapi/v1.json`
- [x] Add `Scalar.AspNetCore` — interactive explorer at `GET /scalar/v1`
- [x] Configure Scalar with Bearer JWT auth pre-wired
- [x] `appsettings.json` — `Cors:AllowedOrigins: []` (production sets real origins)
- [x] `appsettings.Development.json` — localhost:3000, :5173, :4200 for local dev
- [x] Ignore `Agents/` directory in `.gitignore`

---

## Endpoints Added

| Endpoint | Description |
|---|---|
| `GET /openapi/v1.json` | OpenAPI 3 spec — import into Postman or use to codegen a typed client |
| `GET /scalar/v1` | Interactive API explorer — test every endpoint from the browser |

---

## CORS Configuration

Origins are loaded from `appsettings.json`:

```json
"Cors": {
  "AllowedOrigins": ["https://your-frontend.com"]
}
```

Development override in `appsettings.Development.json`:
```json
"Cors": {
  "AllowedOrigins": [
    "http://localhost:3000",
    "http://localhost:5173",
    "http://localhost:4200"
  ]
}
```

All headers and methods are allowed. Credentials (`Authorization` header, cookies) are included.

---

## Using Scalar

1. Start the API
2. Open `http://localhost:PORT/scalar/v1`
3. Call `POST /api/v1/auth/login` with credentials
4. Copy the `accessToken` from the response
5. Click the lock icon in Scalar → paste the token
6. All authenticated endpoints are now unlocked for testing

---

## Packages Added

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.AspNetCore.OpenApi` | 10.0.5 | Generates OpenAPI spec from controllers |
| `Scalar.AspNetCore` | 2.13.14 | Interactive API explorer UI |

---

## Status: COMPLETE
