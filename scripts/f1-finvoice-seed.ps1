<#
.SYNOPSIS
    F1 — Mirror Finvoice users into the Security Platform (V27 role model).

.DESCRIPTION
    Idempotent. Safe to re-run. Does NOT migrate passwords — users set their own
    via the SP forgot-password flow (or log in directly via Microsoft/federated path).

    SP grants for Finvoice are LOGIN ONLY. Finvoice handles its own authorization via
    local ROLE_PERMISSIONS. The script therefore grants exactly one role: 'user'
    (V27 base tier). Existing grants (any role) are preserved untouched.

    This script does NOT create roles. The finvoice app must already be registered
    in SP with the V27 role triad (owner / developer / user). Halts if the 'user'
    role is missing.

    Step 1 — Look up the Finvoice app in SP (halt if not registered).
    Step 2 — Look up the V27 'user' role id (halt if not found).
    Step 3 — Query active Finvoice users from Platform API, create SP users
             (skip existing), and grant the 'user' role to anyone without an
             existing grant on finvoice.

.PARAMETER SpAdminToken
    SP admin JWT (platform_owner role, all admin scopes: apps:read users:write grants:write).

.PARAMETER FinvoiceToken
    Platform API token for the Finvoice project (project ID: aa09bfd5-9e16-4597-a3cf-e4a9ce13f046).
    Used to query project_d20e8e40.users. Regenerate from the FlatPlanet Hub if expired.

.PARAMETER CompanyId
    UUID of the FlatPlanet company in SP. Look up via GET /api/v1/companies if unknown.

.PARAMETER DryRun
    Print what would be created/granted without making any changes.

.EXAMPLE
    .\f1-finvoice-seed.ps1 `
        -SpAdminToken "eyJ..." `
        -FinvoiceToken "eyJ..." `
        -CompanyId "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
#>

[CmdletBinding()]
param (
    [Parameter(Mandatory)] [string] $SpAdminToken,
    [Parameter(Mandatory)] [string] $FinvoiceToken,
    [Parameter(Mandatory)] [string] $CompanyId,
    [switch] $DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$SP_BASE     = 'https://flatplanet-security-api-d5cgdyhmgxcebyak.southeastasia-01.azurewebsites.net'
$PLATFORM    = 'https://flatplanet-api-freffxekdvb6hybs.southeastasia-01.azurewebsites.net'
$FV_PROJECT  = 'aa09bfd5-9e16-4597-a3cf-e4a9ce13f046'
$FV_SCHEMA   = 'project_d20e8e40'
$TARGET_ROLE = 'user'   # V27 base tier — SP grant is login-only

$spHeaders = @{
    Authorization  = "Bearer $SpAdminToken"
    'Content-Type' = 'application/json'
}
$fvHeaders = @{
    Authorization  = "Bearer $FinvoiceToken"
    'Content-Type' = 'application/json'
}

# ── Helpers ────────────────────────────────────────────────────────────────────

function Invoke-Sp {
    param ([string] $Method, [string] $Path, [hashtable] $Body = $null)
    $uri = "$SP_BASE$Path"
    $params = @{ Method = $Method; Uri = $uri; Headers = $spHeaders; ErrorAction = 'Stop' }
    if ($Body) { $params['Body'] = ($Body | ConvertTo-Json -Depth 5) }
    $resp = Invoke-RestMethod @params
    return $resp
}

function Invoke-FvQuery {
    param ([string] $Sql, [hashtable] $Parameters = @{})
    $uri = "$PLATFORM/api/projects/$FV_PROJECT/query/read"
    $body = @{ sql = $Sql; parameters = $Parameters } | ConvertTo-Json -Depth 3
    $resp = Invoke-RestMethod -Method POST -Uri $uri -Headers $fvHeaders -Body $body -ErrorAction Stop
    if (-not $resp.success) { throw "Platform API query failed: $($resp.error)" }
    return $resp.data
}

function Write-Step { param ([string] $Msg) Write-Host "`n=== $Msg ===" -ForegroundColor Cyan }
function Write-Ok   { param ([string] $Msg) Write-Host "  [OK]   $Msg" -ForegroundColor Green }
function Write-Skip { param ([string] $Msg) Write-Host "  [SKIP] $Msg" -ForegroundColor Yellow }
function Write-Dry  { param ([string] $Msg) Write-Host "  [DRY]  $Msg" -ForegroundColor Magenta }
function Write-Info { param ([string] $Msg) Write-Host "  [INFO] $Msg" }

# ── Step 1 — Look up Finvoice app ──────────────────────────────────────────────

Write-Step 'Step 1 — Look up Finvoice app'

# pageSize=500 defends against SP's default page size hiding an existing 'finvoice' app.
# CompanyId filter (client-side) ensures we don't pick another tenant's 'finvoice' slug
# if the slug is not globally unique in SP.
$appsResp = Invoke-Sp -Method GET -Path '/api/v1/apps?pageSize=500'
$finvoiceApp = $appsResp.data |
    Where-Object { $_.slug -eq 'finvoice' -and $_.companyId -eq $CompanyId } |
    Select-Object -First 1

if (-not $finvoiceApp) {
    throw "Finvoice app not found in SP for companyId=$CompanyId. Register it first (with the V27 role triad) before running this script."
}

$appId = $finvoiceApp.id
Write-Ok "Found app 'finvoice' — id=$appId"

# ── Step 2 — Look up the V27 'user' role ───────────────────────────────────────

Write-Step "Step 2 — Look up '$TARGET_ROLE' role"

$rolesResp = Invoke-Sp -Method GET -Path "/api/v1/apps/$appId/roles?pageSize=50"
$targetRole = $rolesResp.data |
    Where-Object { $_.name -and ($_.name.ToString().ToLower() -eq $TARGET_ROLE) } |
    Select-Object -First 1

if (-not $targetRole) {
    $availableNames = ($rolesResp.data | ForEach-Object { $_.name }) -join ', '
    throw "Role '$TARGET_ROLE' not found on finvoice app (available: [$availableNames]). App must be registered with V27 roles before running this script."
}

$targetRoleId = $targetRole.id
Write-Ok "Found role '$TARGET_ROLE' — id=$targetRoleId"

# ── Step 3 — Migrate active Finvoice users ──────────────────────────────────────

Write-Step 'Step 3 — Migrate active Finvoice users'

$fvUsers = Invoke-FvQuery `
    -Sql "SELECT id, email, name, role FROM $FV_SCHEMA.users WHERE is_active = true ORDER BY name" `
    -Parameters @{}

Write-Info "Found $(@($fvUsers).Count) active Finvoice users"

if (-not $DryRun) {
    # pageSize=500 defends against SP's default page size hiding existing grants,
    # which would silently create duplicates on the retry path.
    $grantsResp = Invoke-Sp -Method GET -Path "/api/v1/apps/$appId/users?pageSize=500"
    $existingGrants = $grantsResp.data   # list of UserAccessResponse
}

$created  = 0
$skipped  = 0
$granted  = 0
$grantSkipped = 0
$errors   = @()

foreach ($fvUser in @($fvUsers)) {
    $email    = $fvUser.email.Trim().ToLower()
    # Guard property access defensively for StrictMode safety even though the SELECT is explicit.
    $rawName  = if ($fvUser.PSObject.Properties['name']) { $fvUser.name } else { $null }
    $fullName = ($rawName ?? '').Trim()
    if ([string]::IsNullOrWhiteSpace($fullName)) { $fullName = $email }

    try {
        # ── Find or create SP user ──────────────────────────────────────────────
        $spUserId = $null

        if (-not $DryRun) {
            $searchResp = Invoke-Sp -Method GET -Path "/api/v1/users?search=$([Uri]::EscapeDataString($email))&pageSize=5"
            $match = $searchResp.data.items | Where-Object { $_.email.ToLower() -eq $email } | Select-Object -First 1

            if ($match) {
                $spUserId = $match.id
                $skipped++
                Write-Skip "User $email already exists — spId=$spUserId"
            } else {
                # Generate a random temporary password. User must reset via forgot-password.
                $tempPassword = [System.Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(18))

                $newUser = Invoke-Sp -Method POST -Path '/api/v1/users' -Body @{
                    companyId = $CompanyId
                    email     = $email
                    fullName  = $fullName
                    password  = $tempPassword
                }
                $spUserId = $newUser.data.id
                $created++
                Write-Ok "Created user $email — spId=$spUserId"
            }
        } else {
            Write-Dry "Would create/find user: email=$email, fullName=$fullName"
            $spUserId = '00000000-0000-0000-0000-000000000000'
        }

        # ── Grant finvoice app access ───────────────────────────────────────────
        if (-not $DryRun) {
            $alreadyGranted = $existingGrants | Where-Object { $_.userId -eq $spUserId } | Select-Object -First 1
            if ($alreadyGranted) {
                $grantSkipped++
                $existingRoleId = if ($alreadyGranted.PSObject.Properties['roleId']) { $alreadyGranted.roleId } else { $null }
                # No mismatch warning: existing grants are intentionally preserved (SP is login-only for Finvoice).
                Write-Skip "$email already has grant (roleId=$existingRoleId) — preserving existing role"
            } else {
                Invoke-Sp -Method POST -Path "/api/v1/apps/$appId/users" -Body @{
                    userId = $spUserId
                    roleId = $targetRoleId
                } | Out-Null
                $existingGrants += [pscustomobject]@{ userId = $spUserId; roleId = $targetRoleId }
                $granted++
                Write-Ok "Granted '$TARGET_ROLE' to $email"
            }
        } else {
            Write-Dry "Would grant role '$TARGET_ROLE' to $email (if no existing grant)"
        }
    } catch {
        $errors += "ERROR processing ${email}: $_"
        Write-Host "  [ERR]  $email — $_" -ForegroundColor Red
    }
}

# ── Summary ─────────────────────────────────────────────────────────────────────

Write-Step 'Summary'
if ($DryRun) {
    Write-Host '  DRY RUN — no changes were made.' -ForegroundColor Magenta
} else {
    Write-Info "Users created : $created"
    Write-Info "Users skipped : $skipped (already in SP)"
    Write-Info "Grants created: $granted"
    Write-Info "Grants skipped: $grantSkipped (already granted — role preserved)"
    if ($errors.Count -gt 0) {
        Write-Host "`n  ERRORS ($($errors.Count)):" -ForegroundColor Red
        $errors | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
    } else {
        Write-Ok 'All users migrated successfully.'
    }
}

Write-Host ''
