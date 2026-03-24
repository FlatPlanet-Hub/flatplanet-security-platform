# Phase 6 — Audit, Compliance & Security Config

## Goal
Audit log query, GDPR data export/anonymize, user offboarding, and security config management.

## Tasks

- [ ] Audit Log
  - [ ] `AuditController`
    - [ ] `GET /api/v1/audit` (filter by userId, appId, eventType, from, to — paginated)

- [ ] GDPR / Compliance
  - [ ] `IComplianceService` + `ComplianceService`
    - [ ] Export: user record + all app roles + all sessions + all audit events
    - [ ] Anonymize: replace email, full_name, role_title with anonymized values, preserve audit trail
  - [ ] `ComplianceController`
    - [ ] `GET /api/v1/users/{id}/export`
    - [ ] `POST /api/v1/users/{id}/anonymize` (platform_owner only)

- [ ] Offboarding
  - [ ] `IOffboardingService` + `OffboardingService`
    1. Set user status = inactive
    2. Revoke all active sessions
    3. Revoke all refresh tokens
    4. Suspend all user_app_roles
    5. Log user_offboarded in audit log
  - [ ] `OffboardingController`
    - [ ] `POST /api/v1/users/{id}/offboard`

- [ ] Security Config
  - [ ] `ISecurityConfigService` + `SecurityConfigService`
  - [ ] `SecurityConfigController`
    - [ ] `GET /api/v1/security/config` (platform_owner only)
    - [ ] `PUT /api/v1/security/config/{key}` (platform_owner only)

## Status: PENDING
