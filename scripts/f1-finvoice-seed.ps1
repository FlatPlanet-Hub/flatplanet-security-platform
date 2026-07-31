<#
.SYNOPSIS
    F1 — Mirror Finvoice users into the Security Platform.

.DESCRIPTION
    Idempotent. Safe to re-run. Does NOT migrate passwords — users set their own
    via the SP forgot-password flow (or log in directly via Microsoft/federated path).
    Re-runs do NOT update existing grants — if a user's SP grant has the wrong role,
    fix it manually via PUT /api/v1/apps/{appId}/users/{userId}/role.

    Step 1 — Register the Finvoice app in SP (skip if slug 'finvoice' already exists).
    Step 2 — Seed 5 roles: admin, editor, reviewer, approver, viewer (skip existing).
    Step 3 — Query active Finvoice users from Platform API, create SP users (skip existing),
             grant finvoice app access with the matching role.

.PARAMETER SpAdminToken
    SP admin JWT (platform_owner role, all admin scopes: apps:write roles:write users:write grants:write).

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

# ── Step 1 — Register Finvoice app ─────────────────────────────────────────────

Write-Step 'Step 1 — Register Finvoice app'

# pageSize=500 defends against SP's default page size hiding an existing 'finvoice' app.
# CompanyId filter (client-side) ensures we don't pick another tenant's 'finvoice' slug
# if the slug is not globally unique in SP.
$appsResp = Invoke-Sp -Method GET -Path '/api/v1/apps?pageSize=500'
$finvoiceApp = $appsResp.data |
    Where-Object { $_.slug -eq 'finvoice' -and $_.companyId -eq $CompanyId } |
    Select-Object -First 1

if ($finvoiceApp) {
    Write-Skip "App 'finvoice' already exists — id=$($finvoiceApp.id)"
    $appId = $finvoiceApp.id
} else {
    if ($DryRun) {
        Write-Dry "Would create app: slug=finvoice, name=Finvoice, companyId=$CompanyId"
        $appId = '00000000-0000-0000-0000-000000000000'
    } else {
        $createdApp = Invoke-Sp -Method POST -Path '/api/v1/apps' -Body @{
            companyId = $CompanyId
            name      = 'Finvoice'
            slug      = 'finvoice'
            baseUrl   = 'https://fp-finvoice.netlify.app'
        }
        $appId = $createdApp.data.id
        Write-Ok "Created app 'finvoice' — id=$appId"
    }
}

# ── Step 2 — Seed roles ─────────────────────────────────────────────────────────

Write-Step 'Step 2 — Seed roles'

$roleDescriptions = @{
    admin    = 'Full edit access to all Finvoice sections'
    editor   = 'Edit access to invoices and timesheets; view-only on other sections'
    reviewer = 'View-only access to invoices, timesheets, salary increases, and calculators'
    approver = 'Edit access to invoices; view-only on analytics and timesheets'
    viewer   = 'View-only access to dashboard, analytics, and knowledge base'
}

$roleMap = @{}   # roleName -> SP role id

if (-not $DryRun) {
    $rolesResp = Invoke-Sp -Method GET -Path "/api/v1/apps/$appId/roles"
    $existingRoles = $rolesResp.data
} else {
    $existingRoles = @()
}

foreach ($roleName in $roleDescriptions.Keys) {
    $existing = $existingRoles | Where-Object { $_.name -eq $roleName } | Select-Object -First 1
    if ($existing) {
        $roleMap[$roleName] = $existing.id
        Write-Skip "Role '$roleName' already exists — id=$($existing.id)"
    } else {
        if ($DryRun) {
            Write-Dry "Would create role: name=$roleName"
            $roleMap[$roleName] = '00000000-0000-0000-0000-000000000000'
        } else {
            $created = Invoke-Sp -Method POST -Path "/api/v1/apps/$appId/roles" -Body @{
                name        = $roleName
                description = $roleDescriptions[$roleName]
            }
            $roleMap[$roleName] = $created.data.id
            Write-Ok "Created role '$roleName' — id=$($roleMap[$roleName])"
        }
    }
}

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
    $rawRole  = if ($fvUser.PSObject.Properties['role']) { $fvUser.role } else { $null }
    $fullName = ($rawName ?? '').Trim()
    $roleName = ($rawRole ?? '').Trim().ToLower()
    if ([string]::IsNullOrWhiteSpace($fullName)) { $fullName = $email }

    # Map unknown roles to viewer for safety
    if (-not $roleMap.ContainsKey($roleName)) {
        Write-Host "  [WARN] Unknown role '$roleName' for $email — mapping to viewer" -ForegroundColor Yellow
        $roleName = 'viewer'
    }

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
                # Detect role mismatch — surface it so silent skips don't hide a wrong role assignment.
                $expectedRoleId = $roleMap[$roleName]
                $existingRoleId = if ($alreadyGranted.PSObject.Properties['roleId']) { $alreadyGranted.roleId } else { $null }
                if ($existingRoleId -and $expectedRoleId -and ($existingRoleId -ne $expectedRoleId)) {
                    $existingRoleName = ($roleMap.GetEnumerator() | Where-Object { $_.Value -eq $existingRoleId } | Select-Object -First 1).Key
                    if (-not $existingRoleName) { $existingRoleName = $existingRoleId }
                    Write-Warning "Grant exists for $email but role is $existingRoleName, expected $roleName — fix manually via PUT /api/v1/apps/$appId/users/$spUserId/role"
                } else {
                    Write-Skip "Grant already exists for $email ($roleName)"
                }
            } else {
                Invoke-Sp -Method POST -Path "/api/v1/apps/$appId/users" -Body @{
                    userId = $spUserId
                    roleId = $roleMap[$roleName]
                } | Out-Null
                $existingGrants += [pscustomobject]@{ userId = $spUserId; roleId = $roleMap[$roleName] }
                $granted++
                Write-Ok "Granted $roleName to $email"
            }
        } else {
            Write-Dry "Would grant role '$roleName' to $email"
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
    Write-Info "Grants skipped: $grantSkipped (already granted)"
    if ($errors.Count -gt 0) {
        Write-Host "`n  ERRORS ($($errors.Count)):" -ForegroundColor Red
        $errors | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
    } else {
        Write-Ok 'All users migrated successfully.'
    }
}

Write-Host ''
