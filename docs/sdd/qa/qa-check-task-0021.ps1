# qa-check-task-0021 - static, log, restore and touches checks for TASK-0021 (QA-0020).
# Exit 0 only when every check passes: SUMMARY total=14 fails=0.
# Rules baked in: S-22 (green/red is read from the log's own exit: header and summary, never
# from the wrapper console), S-16 (SHA256 PRE of every file this QA mutated), S-23 (mtime bumped
# after restore), and the touches baseline of this attempt (git status --porcelain -uall
# captured at 2026-11-09 02:11, before QA wrote anything; only the four QA artifacts may be new).
# Pure ASCII on purpose: the PS 5.1 BOM trap of QA-0013 cannot apply.
$ErrorActionPreference = 'Continue'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
Set-Location $repo

$script:failures = 0
function Check([string]$Id, [bool]$Ok, [string]$Detail) {
  if ($Ok) { Write-Output "CHECK $Id PASS $Detail" }
  else { $script:failures++; Write-Output "CHECK $Id FAIL $Detail" }
}

# Baseline captured at QA start (git status --porcelain -uall, 2026-11-09 02:11), prefixes stripped.
$baseline = @(
  'backend/src/Squid.Domain/IPolicyEvaluator.cs',
  'backend/src/Squid.Service/SquidServiceHost.cs',
  'docs/sdd/ideas/BACKLOG.md',
  'docs/sdd/plan/plan.md',
  'backend/src/Squid.Api/Admin/AdminEndpoints.cs',
  'backend/src/Squid.Api/Containers/ContainerEndpoints.cs',
  'backend/src/Squid.Api/Containers/ContainerView.cs',
  'backend/src/Squid.Api/Containers/IContainerStore.cs',
  'backend/src/Squid.Api/Containers/IPolicyAuditStore.cs',
  'backend/src/Squid.Api/Containers/Ownership.cs',
  'backend/src/Squid.Domain/Containers/Container.cs',
  'backend/src/Squid.Domain/Containers/ContainerMembership.cs',
  'backend/src/Squid.Domain/Containers/ContainerSlug.cs',
  'backend/src/Squid.Domain/Containers/MembershipRules.cs',
  'backend/src/Squid.Domain/Containers/SuccessionRules.cs',
  'backend/src/Squid.Service/SqliteContainerStore.cs',
  'backend/src/Squid.Service/SqlitePolicyAuditStore.cs',
  'backend/tests/Squid.Tests/Containers/AdminOverrideAndConfiguredTimersTests.cs',
  'backend/tests/Squid.Tests/Containers/ContainerRoutesTests.cs',
  'backend/tests/Squid.Tests/Containers/ContainerStoreTests.cs',
  'backend/tests/Squid.Tests/Containers/ContainerTests.cs',
  'backend/tests/Squid.Tests/Containers/EntryAndOwnerInvariantTrailTests.cs',
  'backend/tests/Squid.Tests/Containers/MemberModerationRoutesTests.cs',
  'backend/tests/Squid.Tests/Containers/MembershipRoutesTests.cs',
  'backend/tests/Squid.Tests/Containers/MembershipRulesTests.cs',
  'backend/tests/Squid.Tests/Containers/PolicyAuditRoutesTests.cs',
  'backend/tests/Squid.Tests/Containers/PolicyAuditStoreTests.cs',
  'backend/tests/Squid.Tests/Containers/Qa0016ContainerPatchScopeTests.cs',
  'backend/tests/Squid.Tests/Containers/Qa0018MembershipPageAndTrailTests.cs',
  'backend/tests/Squid.Tests/Containers/Qa0019MemberModerationEdgeTests.cs',
  'backend/tests/Squid.Tests/Containers/SuccessionHost.cs',
  'backend/tests/Squid.Tests/Containers/SuccessionRulesTests.cs',
  'backend/tests/Squid.Tests/Containers/SuccessionTransferDeletionTests.cs',
  'backend/tests/Squid.Tests/Domain/PolicyActorContextTests.cs',
  'docs/sdd/qa/QA-0016-nucleo-do-container.md',
  'docs/sdd/qa/QA-0017-politica-auditoria-operador.md',
  'docs/sdd/qa/QA-0018-membership-entrada-e-listagem.md',
  'docs/sdd/qa/QA-0019-papeis-moderacao-avaliador.md',
  'docs/sdd/qa/qa-check-task-0018.ps1',
  'docs/sdd/qa/qa-check-task-0018.sh',
  'docs/sdd/qa/qa-check-task-0019.ps1',
  'docs/sdd/qa/qa-check-task-0019.sh',
  'docs/sdd/qa/qa-check-task-0020.ps1',
  'docs/sdd/qa/qa-check-task-0020.sh',
  'docs/sdd/qa/qa-mutate-task-0019.ps1',
  'docs/sdd/specs/SPEC-0008-containers-sociais-p03.md',
  'docs/sdd/tasks/TASK-0017-nucleo-do-container.md',
  'docs/sdd/tasks/TASK-0018-politica-auditoria-operador.md',
  'docs/sdd/tasks/TASK-0019-membership-entrada-e-listagem.md',
  'docs/sdd/tasks/TASK-0020-papeis-moderacao-avaliador.md',
  'docs/sdd/tasks/TASK-0021-sucessao-transferencia-exclusao.md',
  'docs/sdd/tasks/TASK-0022-descoberta-listagem.md'
)

$allowedNew = @(
  'backend/tests/Squid.Tests/Containers/Qa0021SuccessionEntryAndTrailEdgeTests.cs',
  'docs/sdd/qa/QA-0020-sucessao-transferencia-exclusao.md',
  'docs/sdd/qa/qa-check-task-0021.ps1',
  'docs/sdd/qa/qa-check-task-0021.sh'
)

# --- GATE-BASELINE-EXIT / GATE-BASELINE-SUMMARY: this QA's start gate, read inside the log (S-22) ---
$testRuns = Join-Path $repo 'docs\sdd\runs\test-runs'
$basePath = Join-Path $testRuns '20261109-021141-test-gate.log'
$baseText = if (Test-Path -LiteralPath $basePath) { Get-Content -Raw -LiteralPath $basePath } else { '' }
Check 'GATE-BASELINE-EXIT' ([bool]($baseText -match '(?m)^exit:\s+0\s*$')) '20261109-021141-test-gate.log'

$baseOk = ([bool]($baseText -match 'Com falha:\s+0,')) -and ([bool]($baseText -match 'Aprovado:\s+228,'))
Check 'GATE-BASELINE-SUMMARY' $baseOk 'need Com falha 0 and Aprovado 228 (baseline of this attempt)'

# --- GATE-NEWEST-EXIT / GATE-NEWEST-SUMMARY (S-22: the log classifies, not the console) ---
$gate = Get-ChildItem $testRuns -Filter '*-test-gate.log' -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -Last 1
$gateText = if ($gate) { Get-Content -Raw -LiteralPath $gate.FullName } else { '' }
Check 'GATE-NEWEST-EXIT' ([bool]($gateText -match '(?m)^exit:\s+0\s*$')) $(if ($gate) { $gate.Name } else { 'no gate log found' })

$gateFalha = -1
$gateAprov = -1
if ($gateText -match 'Com falha:\s+(\d+)') { $gateFalha = [int]$Matches[1] }
if ($gateText -match 'Aprovado:\s+(\d+)') { $gateAprov = [int]$Matches[1] }
$gateDetail = "Com falha=$gateFalha Aprovado=$gateAprov (need falha=0 and aprovado=235 = 228 baseline + 7 QA)"
Check 'GATE-NEWEST-SUMMARY' (($gateFalha -eq 0) -and ($gateAprov -eq 235)) $gateDetail

# --- FILTER-NEWEST-EXIT / FILTER-NEWEST-SUMMARY: the last filtered run this QA made (the 7 QA facts) ---
$filter = Get-ChildItem $testRuns -Filter '*-test-filter.log' -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -Last 1
$filterText = if ($filter) { Get-Content -Raw -LiteralPath $filter.FullName } else { '' }
Check 'FILTER-NEWEST-EXIT' ([bool]($filterText -match '(?m)^exit:\s+0\s*$')) $(if ($filter) { $filter.Name } else { 'no filter log found' })

$filterFalha = -1
$filterAprov = -1
if ($filterText -match 'Com falha:\s+(\d+)') { $filterFalha = [int]$Matches[1] }
if ($filterText -match 'Aprovado:\s+(\d+)') { $filterAprov = [int]$Matches[1] }
$filterDetail = "Com falha=$filterFalha Aprovado=$filterAprov (need falha=0 and aprovado=7 = the QA-0020 facts)"
Check 'FILTER-NEWEST-SUMMARY' (($filterFalha -eq 0) -and ($filterAprov -eq 7)) $filterDetail

# --- RESTORE-SHA256 (S-16): every mutated product file is byte-identical to its PRE hash ---
$preHash = @{
  'backend/src/Squid.Api/Containers/ContainerEndpoints.cs'  = 'B5EFD5A0A3869A39FDCE27F9007D9CAA0C1248561BF6A9C131DEC2F5AA007C1E'
  'backend/src/Squid.Api/Admin/AdminEndpoints.cs'           = '57A991CAFBAF642A1229B0449E5CEC76EE9312C11F04203096C620D55A8F8C74'
  'backend/src/Squid.Domain/Containers/SuccessionRules.cs'  = 'F7D0DF3268A02421B94078F9736B4FC3D30AC97BE6F69AA322F3D8C300379410'
}
$hashBad = @()
foreach ($path in $preHash.Keys) {
  $now = (Get-FileHash $path -Algorithm SHA256).Hash
  if ($now -ne $preHash[$path]) { $hashBad += $path }
}
$hashDetail = "$($preHash.Count - $hashBad.Count)/3 files match PRE"
Check 'RESTORE-SHA256' ($hashBad.Count -eq 0) $hashDetail

# --- TESTFILE-INTEGRITY: the QA's own test file was never a mutation target and is intact ---
$qaTest = 'backend/tests/Squid.Tests/Containers/Qa0021SuccessionEntryAndTrailEdgeTests.cs'
$qaTestPre = '7423D8FEC6F557EA293C8489FD19DB71431D4E85272B197893C0ECB1C678AA97'
$qaTestNow = (Get-FileHash $qaTest -Algorithm SHA256).Hash
Check 'TESTFILE-INTEGRITY' ($qaTestNow -eq $qaTestPre) $qaTestNow

# --- MUTATION-RESIDUE: no QA-MUTATION marker left anywhere under backend/ (S-16 leftovers) ---
$residue = 0
Get-ChildItem -Recurse -File -Path (Join-Path $repo 'backend') -Include *.cs,*.csproj,*.props,*.targets -ErrorAction SilentlyContinue |
  Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
  ForEach-Object { if ((Get-Content -Raw -LiteralPath $_.FullName) -match 'QA-MUTATION') { $residue++ } }
Check 'MUTATION-RESIDUE' ($residue -eq 0) "QA-MUTATION hits under backend/ = $residue"

# --- TOUCHES-NO-OUTSIDE (AC-06): baseline kept, only the four QA artifacts may be new ---
$porcelain = & git status --porcelain -uall
$gitOk = ($LASTEXITCODE -eq 0)
$current = @()
if ($gitOk) {
  foreach ($line in $porcelain) { if ("$line".Length -gt 3) { $current += "$line".Substring(3) } }
}
$missing = @($baseline | Where-Object { $current -notcontains $_ })
$extras = @($current | Where-Object { ($baseline -notcontains $_) -and ($allowedNew -notcontains $_) })
$newAllowed = @($current | Where-Object { ($baseline -notcontains $_) -and ($allowedNew -contains $_) })
$touchDetail = "git=$gitOk missing=$($missing.Count) out-of-scope=$($extras.Count) new-allowed=$($newAllowed.Count)/4"
Check 'TOUCHES-NO-OUTSIDE' ($gitOk -and ($missing.Count -eq 0) -and ($extras.Count -eq 0)) $touchDetail

# --- MTIME-SWEEP: nothing under src/tests was written after QA started (02:06) except the
# three files this QA mutated (S-23 restores) and the QA test file - all inside the touches ---
$cut = [datetime]'2026-11-09 02:06:00'
$mutated = @(
  'backend/src/Squid.Api/Containers/ContainerEndpoints.cs',
  'backend/src/Squid.Api/Admin/AdminEndpoints.cs',
  'backend/src/Squid.Domain/Containers/SuccessionRules.cs',
  'backend/tests/Squid.Tests/Containers/Qa0021SuccessionEntryAndTrailEdgeTests.cs'
)
$touches = @(
  'backend/src/Squid.Domain/Containers/',
  'backend/src/Squid.Api/Containers/',
  'backend/src/Squid.Api/Admin/',
  'backend/src/Squid.Service/',
  'backend/tests/Squid.Tests/Containers/'
)
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
$sweepDetail = "files after 02:06 = $($swept.Count); unexpected (outside touches or not mutated) = $($badSweep.Count)"
Check 'MTIME-SWEEP' ($badSweep.Count -eq 0 -and $swept.Count -eq 4) $sweepDetail

# --- ROUTES-STATIC: direct filesystem read (S-24: git grep on untracked paths is vacuous) ---
$container = Get-Content -Raw 'backend/src/Squid.Api/Containers/ContainerEndpoints.cs'
$admin = Get-Content -Raw 'backend/src/Squid.Api/Admin/AdminEndpoints.cs'
$rules = Get-Content -Raw 'backend/src/Squid.Domain/Containers/SuccessionRules.cs'
$trail = Get-Content -Raw 'backend/src/Squid.Api/Containers/IPolicyAuditStore.cs'
$routesOk = $container.Contains('app.MapPost("/api/containers/{slug}/claim", ClaimAsync);') `
  -and $container.Contains('app.MapDelete("/api/containers/{slug}", DeleteAsync);') `
  -and $admin.Contains('app.MapPost("/api/admin/containers/{slug}/transfer", TransferAsync);') `
  -and $container.Contains('if (membership?.State is MembershipState.Active or MembershipState.Silenced or MembershipState.Invited)') `
  -and $container.Contains('ToString(), slug, StringComparison.Ordinal))') `
  -and $container.Contains('if (change is MembershipChange.Ban && target.Role == MembershipRole.Owner)') `
  -and $container.Contains('if (target.Role == MembershipRole.Owner)') `
  -and $rules.Contains('now >= ownerLastActiveAt.AddDays(inactivityDays)') `
  -and $rules.Contains('target with { Role = MembershipRole.Owner, State = MembershipState.Active }') `
  -and $trail.Contains('public const string Successor = "Successor";')
$routeDetail = 'claim+delete+admin-transfer routes + CanEnter Invited + exact confirm + Owner guards + ClaimAllowed + Transfer Active + Successor const'
Check 'ROUTES-STATIC' $routesOk $routeDetail

# --- FACTS-STATIC: the 12 DEV facts of this task plus the 7 QA facts exist where they claim ---
$factFiles = @{
  'backend/tests/Squid.Tests/Containers/SuccessionTransferDeletionTests.cs'    = 4
  'backend/tests/Squid.Tests/Containers/AdminOverrideAndConfiguredTimersTests.cs' = 2
  'backend/tests/Squid.Tests/Containers/EntryAndOwnerInvariantTrailTests.cs'   = 2
  'backend/tests/Squid.Tests/Containers/SuccessionRulesTests.cs'               = 4
  'backend/tests/Squid.Tests/Containers/Qa0021SuccessionEntryAndTrailEdgeTests.cs' = 7
}
$factBad = @()
foreach ($ff in $factFiles.Keys) {
  $n = ([regex]::Matches((Get-Content -Raw $ff), '\[Fact\]')).Count
  if ($n -ne $factFiles[$ff]) { $factBad += "$ff=$n" }
}
$factDetail = "4+2+2+4 DEV + 7 QA facts where expected"
if ($factBad.Count -gt 0) { $factDetail = ($factBad -join '; ') }
Check 'FACTS-STATIC' ($factBad.Count -eq 0) $factDetail

# --- STATE-CHECK: the flow writer still accepts the task file after this QA's appends ---
$state = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'docs\sdd\_tools\sdd-state.ps1' check 'docs/sdd/tasks/TASK-0021-sucessao-transferencia-exclusao.md' 2>&1
$stateRc = $LASTEXITCODE
$stateLine = (($state | Where-Object { "$_" -match 'check:' } | Select-Object -First 1))
if ($null -eq $stateLine) { $stateLine = '(no check: line)' }
$stateDetail = "$stateLine"
Check 'STATE-CHECK' (($stateRc -eq 0) -and ("$stateLine" -match 'check: OK')) $stateDetail

Write-Output "SUMMARY total=14 fails=$($script:failures)"
if ($script:failures -eq 0) { Write-Output 'RESULT: PASS'; exit 0 }
Write-Output 'RESULT: FAIL'; exit 1
