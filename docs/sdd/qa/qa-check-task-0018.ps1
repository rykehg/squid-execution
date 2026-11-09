# qa-check-task-0018 — static, log, restore and touches checks for TASK-0018 (QA-0017).
# Exit 0 only when every check passes: SUMMARY total=9 fails=0.
# Rules baked in: S-22 (green/red is read from the log's own exit: header and summary, never
# from the wrapper console), S-16 (SHA256 PRE of every file this QA mutated), S-23 (mtime bumped
# after restore), and the touches baseline of this attempt (git status --porcelain -uall captured
# at 2026-11-08 17:41, before QA wrote anything; only the three QA artifacts may be new).
# Pure ASCII on purpose: the PS 5.1 BOM trap of QA-0013 cannot apply.
$ErrorActionPreference = 'Continue'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
Set-Location $repo

$script:failures = 0
function Check([string]$Id, [bool]$Ok, [string]$Detail) {
  if ($Ok) { Write-Output "CHECK $Id PASS $Detail" }
  else { $script:failures++; Write-Output "CHECK $Id FAIL $Detail" }
}

# Baseline captured at QA start (git status --porcelain -uall, 2026-11-08 17:41), prefixes stripped.
$baseline = @(
  'backend/src/Squid.Service/SquidServiceHost.cs',
  'docs/sdd/ideas/BACKLOG.md',
  'docs/sdd/plan/plan.md',
  'backend/src/Squid.Api/Admin/AdminEndpoints.cs',
  'backend/src/Squid.Api/Containers/ContainerEndpoints.cs',
  'backend/src/Squid.Api/Containers/ContainerView.cs',
  'backend/src/Squid.Api/Containers/IContainerStore.cs',
  'backend/src/Squid.Api/Containers/IPolicyAuditStore.cs',
  'backend/src/Squid.Domain/Containers/Container.cs',
  'backend/src/Squid.Domain/Containers/ContainerMembership.cs',
  'backend/src/Squid.Domain/Containers/ContainerSlug.cs',
  'backend/src/Squid.Service/SqliteContainerStore.cs',
  'backend/src/Squid.Service/SqlitePolicyAuditStore.cs',
  'backend/tests/Squid.Tests/Containers/ContainerRoutesTests.cs',
  'backend/tests/Squid.Tests/Containers/ContainerStoreTests.cs',
  'backend/tests/Squid.Tests/Containers/ContainerTests.cs',
  'backend/tests/Squid.Tests/Containers/PolicyAuditRoutesTests.cs',
  'backend/tests/Squid.Tests/Containers/PolicyAuditStoreTests.cs',
  'backend/tests/Squid.Tests/Containers/Qa0016ContainerPatchScopeTests.cs',
  'docs/sdd/qa/QA-0016-nucleo-do-container.md',
  'docs/sdd/specs/SPEC-0008-containers-sociais-p03.md',
  'docs/sdd/tasks/TASK-0017-nucleo-do-container.md',
  'docs/sdd/tasks/TASK-0018-politica-auditoria-operador.md',
  'docs/sdd/tasks/TASK-0019-membership-entrada-e-listagem.md',
  'docs/sdd/tasks/TASK-0020-papeis-moderacao-avaliador.md',
  'docs/sdd/tasks/TASK-0021-sucessao-transferencia-exclusao.md',
  'docs/sdd/tasks/TASK-0022-descoberta-listagem.md'
)

$allowedNew = @(
  'docs/sdd/qa/QA-0017-politica-auditoria-operador.md',
  'docs/sdd/qa/qa-check-task-0018.ps1',
  'docs/sdd/qa/qa-check-task-0018.sh'
)

$mutated = @(
  'backend/src/Squid.Api/Containers/ContainerEndpoints.cs',
  'backend/src/Squid.Api/Admin/AdminEndpoints.cs',
  'backend/src/Squid.Service/SqlitePolicyAuditStore.cs'
)

# --- GATE-NEWEST-EXIT / GATE-NEWEST-SUMMARY (S-22: the log classifies, not the console) ---
$testRuns = Join-Path $repo 'docs\sdd\runs\test-runs'
$gate = Get-ChildItem $testRuns -Filter '*-test-gate.log' -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -Last 1
$gateText = if ($gate) { Get-Content -Raw -LiteralPath $gate.FullName } else { '' }
Check 'GATE-NEWEST-EXIT' ([bool]($gateText -match '(?m)^exit:\s+0\s*$')) $(if ($gate) { $gate.Name } else { 'no gate log found' })

$gateFalha = -1
$gateAprov = -1
if ($gateText -match 'Com falha:\s+(\d+)') { $gateFalha = [int]$Matches[1] }
if ($gateText -match 'Aprovado:\s+(\d+)') { $gateAprov = [int]$Matches[1] }
Check 'GATE-NEWEST-SUMMARY' (($gateFalha -eq 0) -and ($gateAprov -ge 188)) "Com falha=$gateFalha Aprovado=$gateAprov (need falha=0 and aprovado>=188)"

# --- FILTER-NEWEST-EXIT / FILTER-NEWEST-SUMMARY: the 7 PolicyAudit tests QA runs after restores ---
$filter = Get-ChildItem $testRuns -Filter '*-test-filter.log' -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -Last 1
$filterText = if ($filter) { Get-Content -Raw -LiteralPath $filter.FullName } else { '' }
Check 'FILTER-NEWEST-EXIT' ([bool]($filterText -match '(?m)^exit:\s+0\s*$')) $(if ($filter) { $filter.Name } else { 'no filter log found' })

$filterFalha = -1
$filterAprov = -1
if ($filterText -match 'Com falha:\s+(\d+)') { $filterFalha = [int]$Matches[1] }
if ($filterText -match 'Aprovado:\s+(\d+)') { $filterAprov = [int]$Matches[1] }
Check 'FILTER-NEWEST-SUMMARY' (($filterFalha -eq 0) -and ($filterAprov -ge 7)) "Com falha=$filterFalha Aprovado=$filterAprov (need falha=0 and aprovado>=7)"

# --- RESTORE-SHA256 (S-16): every mutated product file is byte-identical to its PRE hash ---
$preHash = @{
  'backend/src/Squid.Api/Containers/ContainerEndpoints.cs' = 'D0704FD83FF00EABD74B22F47F2CE7E5ECFCD006B26DEDAE85FA27031F5C14D1'
  'backend/src/Squid.Api/Admin/AdminEndpoints.cs'          = '754C1B69FCEC797185A4440052D3D884797E85BEF2509B90D2DD025DB4806B3B'
  'backend/src/Squid.Service/SqlitePolicyAuditStore.cs'    = 'CE485D798383E5FE79D73CBBAF1847CB06BF4FD430DBF71CA09B2BB509C26BF7'
}
$hashBad = @()
foreach ($path in $preHash.Keys) {
  $now = (Get-FileHash $path -Algorithm SHA256).Hash
  if ($now -ne $preHash[$path]) { $hashBad += "$path=$now" }
}
Check 'RESTORE-SHA256' ($hashBad.Count -eq 0) "$($preHash.Count - $hashBad.Count)/3 files match PRE"

# --- MUTATION-RESIDUE: no QA-MUTATION marker left anywhere under backend/ (S-16 leftovers) ---
$residue = 0
Get-ChildItem -Recurse -File -Path (Join-Path $repo 'backend') -Include *.cs,*.csproj,*.props,*.targets -ErrorAction SilentlyContinue |
  Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
  ForEach-Object { if ((Get-Content -Raw -LiteralPath $_.FullName) -match 'QA-MUTATION') { $residue++ } }
Check 'MUTATION-RESIDUE' ($residue -eq 0) "QA-MUTATION hits under backend/ = $residue"

# --- TOUCHES-NO-OUTSIDE (AC-06): baseline kept, only the three QA artifacts may be new ---
$porcelain = & git status --porcelain -uall
$gitOk = ($LASTEXITCODE -eq 0)
$current = @()
if ($gitOk) {
  foreach ($line in $porcelain) { if ("$line".Length -gt 3) { $current += "$line".Substring(3) } }
}
$missing = @($baseline | Where-Object { $current -notcontains $_ })
$extras = @($current | Where-Object { ($baseline -notcontains $_) -and ($allowedNew -notcontains $_) })
$newAllowed = @($current | Where-Object { ($baseline -notcontains $_) -and ($allowedNew -contains $_) })
Check 'TOUCHES-NO-OUTSIDE' ($gitOk -and ($missing.Count -eq 0) -and ($extras.Count -eq 0)) "git=$gitOk missing=$($missing.Count) out-of-scope=$($extras.Count) new-allowed=$($newAllowed.Count)/3"

# --- MTIME-SWEEP: nothing under src/tests was written after DEV closed (15:59:17) except the
# three files this QA mutated, and those three sit inside the touches of TASK-0018 ---
$cut = [datetime]'2026-11-08 15:59:17'
$touches = @('backend/src/Squid.Api/Containers/', 'backend/src/Squid.Api/Admin/', 'backend/src/Squid.Service/', 'backend/tests/Squid.Tests/Containers/')
$swept = @()
foreach ($root in @((Join-Path $repo 'backend\src'), (Join-Path $repo 'backend\tests'))) {
  $swept += Get-ChildItem -Recurse -File -Path $root -ErrorAction SilentlyContinue |
    Where-Object { ($_.FullName -notmatch '\\(bin|obj)\\') -and ($_.LastWriteTime -gt $cut) }
}
$badSweep = @($swept | Where-Object {
  $rel = $_.FullName.Substring($repo.Length + 1).Replace('\', '/')
  $inTouches = $false
  foreach ($t in $touches) { if ($rel.StartsWith($t)) { $inTouches = $true; break } }
  (-not $inTouches) -or ($mutated -notcontains $rel)
})
Check 'MTIME-SWEEP' ($badSweep.Count -eq 0) "files after 15:59:17 = $($swept.Count); not one of the 3 mutated inside touches = $($badSweep.Count)"

# --- ROUTES-STATIC: direct filesystem read (S-24: git grep on untracked paths is vacuous) ---
$container = Get-Content -Raw 'backend/src/Squid.Api/Containers/ContainerEndpoints.cs'
$admin = Get-Content -Raw 'backend/src/Squid.Api/Admin/AdminEndpoints.cs'
$hostCode = Get-Content -Raw 'backend/src/Squid.Service/SquidServiceHost.cs'
$trail = Get-Content -Raw 'backend/src/Squid.Api/Containers/IPolicyAuditStore.cs'
$routesOk = $container.Contains('app.MapPut("/api/containers/{slug}/policy", PutPolicyAsync);') `
  -and $admin.Contains('app.MapGet("/api/admin/audit", AuditAsync);') `
  -and $hostCode.Contains('app.MapSquidAdmin();') `
  -and $trail.Contains('public const string ChangePolicy = "ChangePolicy";')
Check 'ROUTES-STATIC' $routesOk 'PUT /policy + GET /api/admin/audit + MapSquidAdmin + Action ChangePolicy read from the filesystem'

Write-Output "SUMMARY total=9 fails=$($script:failures)"
if ($script:failures -eq 0) { Write-Output 'RESULT: PASS'; exit 0 }
Write-Output 'RESULT: FAIL'; exit 1
