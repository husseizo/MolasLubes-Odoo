#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Smoke-test for the Liqui Moly replenishment pipeline.
    Covers: draft → submit → approve → execute → retry → audit reports.

.DESCRIPTION
    Runs each step sequentially, halts on the first non-2xx response, and
    prints a PASS/FAIL summary at the end.

.PARAMETER BaseUrl
    API base URL, e.g. https://localhost:7210

.PARAMETER ApiKey
    Value for the X-Api-Key header.

.PARAMETER PlannerUser
    SAP user code with Planner role (generates + submits drafts).

.PARAMETER SupervisorUser
    SAP user code with Supervisor role (approves/rejects).

.PARAMETER ExecutorUser
    SAP user code with Executor role (executes + retries).

.PARAMETER ViewerUser
    SAP user code with at least Viewer role (read-only assertions).

.PARAMETER SourceWarehouse
    MolasLubes warehouse code, e.g. "01".

.PARAMETER TargetWarehouse
    AutoHub warehouse code, e.g. "01".

.EXAMPLE
    .\liquimoly-replenishment-smoke-test.ps1 `
        -BaseUrl https://localhost:7210 `
        -ApiKey  "YOUR_API_KEY" `
        -PlannerUser    "planner1" `
        -SupervisorUser "supervisor1" `
        -ExecutorUser   "executor1" `
        -ViewerUser     "viewer1" `
        -SourceWarehouse "01" `
        -TargetWarehouse "01"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $BaseUrl,
    [Parameter(Mandatory)] [string] $ApiKey,
    [Parameter(Mandatory)] [string] $PlannerUser,
    [Parameter(Mandatory)] [string] $SupervisorUser,
    [Parameter(Mandatory)] [string] $ExecutorUser,
    [Parameter(Mandatory)] [string] $ViewerUser,
    [Parameter(Mandatory)] [string] $SourceWarehouse,
    [Parameter(Mandatory)] [string] $TargetWarehouse
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$results  = [System.Collections.Generic.List[PSCustomObject]]::new()
$apiBase  = "$BaseUrl/api/admin/liquimoly"
$headers  = @{ "X-Api-Key" = $ApiKey; "Content-Type" = "application/json" }

# ── helpers ──────────────────────────────────────────────────────────────────

function Invoke-Step {
    param(
        [string]      $Label,
        [string]      $Method,
        [string]      $Url,
        [string]      $Body      = $null,
        [hashtable]   $Query     = @{},
        [int[]]       $Expect    = @(200),
        [scriptblock] $Assert    = $null
    )

    $qs = if ($Query.Count) { "?" + (($Query.GetEnumerator() | ForEach-Object { "$($_.Key)=$([Uri]::EscapeDataString($_.Value))" }) -join "&") } else { "" }
    $uri = "$Url$qs"

    Write-Host "`n── $Label" -ForegroundColor Cyan
    Write-Host "   $Method $uri" -ForegroundColor DarkGray

    try {
        $splat = @{ Uri = $uri; Method = $Method; Headers = $headers; SkipCertificateCheck = $true }
        if ($Body) { $splat["Body"] = $Body }

        $resp = Invoke-RestMethod @splat -ResponseHeadersVariable rh -StatusCodeVariable sc
        $statusCode = [int]$sc

        if ($statusCode -notin $Expect) {
            throw "Unexpected HTTP $statusCode (expected: $($Expect -join ','))"
        }

        if ($Assert) { & $Assert $resp }

        $results.Add([PSCustomObject]@{ Step = $Label; Status = "PASS"; Detail = "HTTP $statusCode" })
        Write-Host "   PASS  HTTP $statusCode" -ForegroundColor Green
        return $resp
    }
    catch {
        $msg = $_.Exception.Message
        $results.Add([PSCustomObject]@{ Step = $Label; Status = "FAIL"; Detail = $msg })
        Write-Host "   FAIL  $msg" -ForegroundColor Red
        throw  # halt on first failure
    }
}

# ── Step 0: preflight — recommendations report ───────────────────────────────

$_ = Invoke-Step `
    -Label  "0. Recommendations report (preflight)" `
    -Method GET `
    -Url    "$apiBase/reports/recommendations" `
    -Query  @{
        actorSapUserCode = $ViewerUser
        sourceProfile    = "MolasLubes"
        targetProfile    = "AutoHub"
        sourceWarehouse  = $SourceWarehouse
        targetWarehouse  = $TargetWarehouse
        targetDays       = "30"
    } `
    -Assert {
        param($r)
        if ($r.count -lt 1) { throw "Recommendations returned 0 rows — check SAP connectivity and warehouse codes." }
        Write-Host "   $($r.count) recommendation rows returned." -ForegroundColor DarkGray
    }

# ── Step 1: generate draft ────────────────────────────────────────────────────

$draftBody = @{
    actor           = @{ sapUserCode = $PlannerUser; comment = "Smoke test draft" }
    sourceProfile   = "MolasLubes"
    targetProfile   = "AutoHub"
    sourceWarehouse = $SourceWarehouse
    targetWarehouse = $TargetWarehouse
    targetDays      = 30
} | ConvertTo-Json -Depth 4

$draft = Invoke-Step `
    -Label  "1. Generate draft" `
    -Method POST `
    -Url    "$apiBase/replenishment/generate-draft" `
    -Body   $draftBody `
    -Assert {
        param($r)
        if (-not $r.requestRef) { throw "No requestRef in response." }
        if ($r.rowCount -lt 1)  { throw "Draft has 0 lines — nothing to test." }
        Write-Host "   RequestRef: $($r.requestRef)  Lines: $($r.rowCount)" -ForegroundColor DarkGray
    }

$ref = $draft.requestRef

# ── Step 2: GET the draft ────────────────────────────────────────────────────

$_ = Invoke-Step `
    -Label  "2. GET draft by ref" `
    -Method GET `
    -Url    "$apiBase/replenishment/$ref" `
    -Query  @{ actorSapUserCode = $ViewerUser } `
    -Assert {
        param($r)
        if ($r.status -ne "DRAFT") { throw "Expected status DRAFT, got $($r.status)." }
    }

# ── Step 3: submit for approval ──────────────────────────────────────────────

$submitBody = @{
    actor = @{ sapUserCode = $PlannerUser; comment = "Smoke test submit" }
} | ConvertTo-Json -Depth 3

$_ = Invoke-Step `
    -Label  "3. Submit for approval" `
    -Method POST `
    -Url    "$apiBase/replenishment/$ref/submit" `
    -Body   $submitBody `
    -Assert {
        param($r)
        if ($r.status -ne "PENDING_APPROVAL") { throw "Expected PENDING_APPROVAL, got $($r.status)." }
    }

# ── Step 4: role guard — planner cannot approve ──────────────────────────────

$approveBody = @{
    actor          = @{ sapUserCode = $PlannerUser; comment = "Planner tries to approve (should 403)" }
    lineQuantities = @{}
} | ConvertTo-Json -Depth 4

Invoke-Step `
    -Label  "4. Role guard — planner cannot approve" `
    -Method POST `
    -Url    "$apiBase/replenishment/$ref/approve" `
    -Body   $approveBody `
    -Expect @(403) | Out-Null

# ── Step 5: supervisor approves ──────────────────────────────────────────────

$approveBody2 = @{
    actor          = @{ sapUserCode = $SupervisorUser; comment = "Smoke test approve" }
    lineQuantities = @{}   # accept all suggested quantities
} | ConvertTo-Json -Depth 4

$_ = Invoke-Step `
    -Label  "5. Supervisor approves" `
    -Method POST `
    -Url    "$apiBase/replenishment/$ref/approve" `
    -Body   $approveBody2 `
    -Assert {
        param($r)
        if ($r.status -ne "APPROVED")           { throw "Expected APPROVED, got $($r.status)." }
        if (-not $r.approvedBySapUser)          { throw "approvedBySapUser not set." }
        if (-not $r.approvedAt)                 { throw "approvedAt not set." }
    }

# ── Step 6: execute ──────────────────────────────────────────────────────────

$execBody = @{
    actor = @{ sapUserCode = $ExecutorUser; comment = "Smoke test execute" }
} | ConvertTo-Json -Depth 3

$execResult = Invoke-Step `
    -Label  "6. Execute approved request" `
    -Method POST `
    -Url    "$apiBase/replenishment/$ref/execute" `
    -Body   $execBody `
    -Assert {
        param($r)
        $validStatuses = @("EXECUTED","PARTIAL","FAILED")
        if ($r.status -notin $validStatuses) { throw "Unexpected post-execute status: $($r.status)." }
        Write-Host "   Post-execute status: $($r.status)" -ForegroundColor DarkGray
        if ($r.goodsIssueDocNum) {
            Write-Host "   GI DocNum: $($r.goodsIssueDocNum)" -ForegroundColor DarkGray
        }
    }

# ── Step 7 (conditional): retry if PARTIAL ───────────────────────────────────

if ($execResult.status -eq "PARTIAL") {
    Write-Host "`n   Request is PARTIAL — running retry path." -ForegroundColor Yellow

    $retryBody = @{
        actor = @{ sapUserCode = $ExecutorUser; comment = "Smoke test retry" }
    } | ConvertTo-Json -Depth 3

    $_ = Invoke-Step `
        -Label  "7. Retry partial execution" `
        -Method POST `
        -Url    "$apiBase/replenishment/$ref/retry" `
        -Body   $retryBody `
        -Assert {
            param($r)
            $validStatuses = @("EXECUTED","PARTIAL","FAILED")
            if ($r.status -notin $validStatuses) { throw "Unexpected post-retry status: $($r.status)." }
            Write-Host "   Post-retry status: $($r.status)" -ForegroundColor DarkGray
        }
} else {
    $results.Add([PSCustomObject]@{ Step = "7. Retry partial execution"; Status = "SKIP"; Detail = "Not PARTIAL — retry path not exercised" })
    Write-Host "`n── 7. Retry partial execution  SKIP (not PARTIAL)" -ForegroundColor DarkGray
}

# ── Step 8: approvals audit — executed record must appear ────────────────────

$_ = Invoke-Step `
    -Label  "8. Approvals report includes executed record" `
    -Method GET `
    -Url    "$apiBase/reports/approvals" `
    -Query  @{ actorSapUserCode = $ViewerUser; take = "100" } `
    -Assert {
        param($r)
        $found = $r.rows | Where-Object { $_.requestRef -eq $ref }
        if (-not $found) { throw "RequestRef $ref not found in approvals report after execution — audit trail broken." }
        Write-Host "   Found in approvals report with status: $($found.status)" -ForegroundColor DarkGray
        if (-not $found.approvedBySapUser) { throw "approvedBySapUser missing from audit row." }
        if (-not $found.approvedAt)        { throw "approvedAt missing from audit row." }
    }

# ── Step 9: executions report ────────────────────────────────────────────────

$_ = Invoke-Step `
    -Label  "9. Executions report" `
    -Method GET `
    -Url    "$apiBase/reports/executions" `
    -Query  @{ actorSapUserCode = $ViewerUser; take = "100" }

# ── Step 10: dead-stock report ───────────────────────────────────────────────

$_ = Invoke-Step `
    -Label  "10. Dead-stock report" `
    -Method GET `
    -Url    "$apiBase/reports/dead-stock" `
    -Query  @{
        actorSapUserCode = $ViewerUser
        sourceWarehouse  = $SourceWarehouse
        targetWarehouse  = $TargetWarehouse
    }

# ── Step 11: role guard — unknown user is rejected on read ───────────────────

Invoke-Step `
    -Label  "11. Role guard — unknown user rejected on list" `
    -Method GET `
    -Url    "$apiBase/replenishment" `
    -Query  @{ actorSapUserCode = "BOGUS_USER_XYZ" } `
    -Expect @(403) | Out-Null

# ── Step 12: reject path (separate draft) ────────────────────────────────────

$draftBody2 = @{
    actor           = @{ sapUserCode = $PlannerUser; comment = "Smoke test reject-path draft" }
    sourceProfile   = "MolasLubes"
    targetProfile   = "AutoHub"
    sourceWarehouse = $SourceWarehouse
    targetWarehouse = $TargetWarehouse
    targetDays      = 30
} | ConvertTo-Json -Depth 4

$draft2 = Invoke-Step `
    -Label  "12a. Generate second draft (for reject path)" `
    -Method POST `
    -Url    "$apiBase/replenishment/generate-draft" `
    -Body   $draftBody2
$ref2 = $draft2.requestRef

Invoke-Step `
    -Label  "12b. Submit second draft" `
    -Method POST `
    -Url    "$apiBase/replenishment/$ref2/submit" `
    -Body   $submitBody | Out-Null

$rejectBody = @{
    actor            = @{ sapUserCode = $SupervisorUser; comment = "Smoke test reject" }
    rejectionReason  = "Smoke test — intentional rejection"
} | ConvertTo-Json -Depth 3

$_ = Invoke-Step `
    -Label  "12c. Supervisor rejects" `
    -Method POST `
    -Url    "$apiBase/replenishment/$ref2/reject" `
    -Body   $rejectBody `
    -Assert {
        param($r)
        if ($r.status -ne "REJECTED") { throw "Expected REJECTED, got $($r.status)." }
        if (-not $r.rejectionReason)  { throw "rejectionReason not set." }
    }

$_ = Invoke-Step `
    -Label  "12d. Rejected record in approvals report" `
    -Method GET `
    -Url    "$apiBase/reports/approvals" `
    -Query  @{ actorSapUserCode = $ViewerUser; take = "100" } `
    -Assert {
        param($r)
        $found = $r.rows | Where-Object { $_.requestRef -eq $ref2 }
        if (-not $found)              { throw "Rejected ref $ref2 not found in approvals report." }
        if (-not $found.rejectedBySapUser) { throw "rejectedBySapUser missing." }
    }

# ── idempotency guard — double-execute should 400 ────────────────────────────

Invoke-Step `
    -Label  "13. Idempotency — re-execute already-executed request returns 400" `
    -Method POST `
    -Url    "$apiBase/replenishment/$ref/execute" `
    -Body   $execBody `
    -Expect @(400) | Out-Null

# ── Summary ───────────────────────────────────────────────────────────────────

Write-Host "`n═══════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  SMOKE TEST SUMMARY" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan

$pass = ($results | Where-Object Status -eq "PASS").Count
$fail = ($results | Where-Object Status -eq "FAIL").Count
$skip = ($results | Where-Object Status -eq "SKIP").Count

foreach ($r in $results) {
    $color = switch ($r.Status) { "PASS" { "Green" } "FAIL" { "Red" } default { "DarkGray" } }
    Write-Host ("  {0,-5} {1,-55} {2}" -f $r.Status, $r.Step, $r.Detail) -ForegroundColor $color
}

Write-Host ""
Write-Host "  PASS: $pass   FAIL: $fail   SKIP: $skip" -ForegroundColor $(if ($fail) { "Red" } else { "Green" })

if ($fail -gt 0) { exit 1 } else { exit 0 }
