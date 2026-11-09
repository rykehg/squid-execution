# qa-check-task-0022 - static, log, restore and touches checks for TASK-0022 (QA-0021).
# Exit 0 only when every check passes: SUMMARY total=17 fails=0.
# Rules baked in: S-22 (green/red is read from the log's own exit: header and summary, never
# from the wrapper console), S-16 (SHA256 PRE of every file this QA mutated), S-23 (mtime bumped
# after restore), and the touches baseline of this attempt (git status --porcelain -uall
# captured at 2026-11-09 04:06, before QA wrote anything; only the four QA artifacts may be new).
# Pure ASCII on purpose: the PS 5.1 BOM trap of QA-0013 cannot apply.
$ErrorActionPreference = 'Continue'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
Set-Location $repo

$script:failures = 0
function Check([string]$Id, [bool]$Ok, [string]$Detail) {
  if ($Ok) { Write-Output "CHECK $Id PASS $Detail" }
  else { $script:failures++; Write-Output "CHECK $Id FAIL $Detail" }
}

# Baseline captured at QA start (git status --porcelain -uall, 2026-11-09 04:06), prefixes stripped.
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
  'backend/tests/Squid.Tests/Containers/ContainerSearchRoutesTests.cs',
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
  'backend/tests/Squid.Tests/Containers/Qa0021SuccessionEntryAndTrailEdgeTests.cs',
  'backend/tests/Squid.Tests/Containers/Sprint2ClosingFacetTests.cs',
  'backend/tests/Squid.Tests/Containers/SuccessionHost.cs',
  'backend/tests/Squid.Tests/Containers/SuccessionRulesTests.cs',
  'backend/tests/Squid.Tests/Containers/SuccessionTransferDeletionTests.cs',
  'backend/tests/Squid.Tests/Domain/PolicyActorContextTests.cs',
  'docs/sdd/qa/QA-0016-nucleo-do-container.md',
  'docs/sdd/qa/QA-0017-politica-auditoria-operador.md',
  'docs/sdd/qa/QA-0018-membership-entrada-e-listagem.md',
  'docs/sdd/qa/QA-0019-papeis-moderacao-avaliador.md',
  'docs/sdd/qa/QA-0020-sucessao-transferencia-exclusao.md',
  'docs/sdd/qa/qa-check-task-0018.ps1',
  'docs/sdd/qa/qa-check-task-0018.sh',
  'docs/sdd/qa/qa-check-task-0019.ps1',
  'docs/sdd/qa/qa-check-task-0019.sh',
  'docs/sdd/qa/qa-check-task-0020.ps1',
  'docs/sdd/qa/qa-check-task-0020.sh',
  'docs/sdd/qa/qa-check-task-0021.ps1',
  'docs/sdd/qa/qa-check-task-0021.sh',
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
  'backend/tests/Squid.Tests/Containers/Qa0022SearchDiscoveryTests.cs',
  'docs/sdd/qa/QA-0021-descoberta-listagem.md',
  'docs/sdd/qa/qa-check-task-0022.ps1',
  'docs/sdd/qa/qa-check-task-0022.sh'
)

# --- GATE-BASELINE-EXIT / GATE-BASELINE-SUMMARY: this QA's start gate, read inside the log (S-22) ---
$testRuns = Join-Path $repo 'docs\sdd\runs\test-runs'
$basePath = Join-Path $testRuns '20261109-040457-test-gate.log'
$baseText = if (Test-Path -LiteralPath $basePath) { Get-Content -Raw -LiteralPath $basePath } else { '' }
Check 'GATE-BASELINE-EXIT' ([bool]($baseText -match '(?m)^exit:\s+0\s*$')) '20261109-040457-test-gate.log'

$baseOk = ([bool]($baseText -match 'Com falha:\s+0,')) -and ([bool]($baseText -match 'Aprovado:\s+242,'))
Check 'GATE-BASELINE-SUMMARY' $baseOk 'need Com falha 0 and Aprovado 242 (baseline of this attempt)'

# --- GATE-NEWEST-EXIT / GATE-NEWEST-SUMMARY (S-22: the log classifies, not the console) ---
$gate = Get-ChildItem $testRuns -Filter '*-test-gate.log' -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -Last 1
$gateText = if ($gate) { Get-Content -Raw -LiteralPath $gate.FullName } else { '' }
Check 'GATE-NEWEST-EXIT' ([bool]($gateText -match '(?m)^exit:\s+0\s*$')) $(if ($gate) { $gate.Name } else { 'no gate log found' })

$gateFalha = -1
$gateAprov = -1
if ($gateText -match 'Com falha:\s+(\d+)') { $gateFalha = [int]$Matches[1] }
if ($gateText -match 'Aprovado:\s+(\d+)') { $gateAprov = [int]$Matches[1] }
$gateDetail = "Com falha=$gateFalha Aprovado=$gateAprov (need falha=0 and aprovado=244 = 242 baseline + 2 QA)"
Check 'GATE-NEWEST-SUMMARY' (($gateFalha -eq 0) -and ($gateAprov -eq 244)) $gateDetail

# --- FILTER-NEWEST-EXIT / FILTER-NEWEST-SUMMARY: the last filtered run (the Containers area) ---
$filter = Get-ChildItem $testRuns -Filter '*-test-filter.log' -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -Last 1
$filterText = if ($filter) { Get-Content -Raw -LiteralPath $filter.FullName } else { '' }
Check 'FILTER-NEWEST-EXIT' ([bool]($filterText -match '(?m)^exit:\s+0\s*$')) $(if ($filter) { $filter.Name } else { 'no filter log found' })

$filterFalha = -1
$filterAprov = -1
if ($filterText -match 'Com falha:\s+(\d+)') { $filterFalha = [int]$Matches[1] }
if ($filterText -match 'Aprovado:\s+(\d+)') { $filterAprov = [int]$Matches[1] }
$filterDetail = "Com falha=$filterFalha Aprovado=$filterAprov (need falha=0 and aprovado=70 = area of the task)"
Check 'FILTER-NEWEST-SUMMARY' (($filterFalha -eq 0) -and ($filterAprov -eq 70)) $filterDetail

# --- FILTER-DEV7: the fixed log of the seven new DEV tests (AC-01..AC-04 scenarios) ---
$dev7Path = Join-Path $testRuns '20261109-040703-test-filter.log'
$dev7Text = if (Test-Path -LiteralPath $dev7Path) { Get-Content -Raw -LiteralPath $dev7Path } else { '' }
$dev7Ok = ([bool]($dev7Text -match '(?m)^exit:\s+0\s*$')) -and ([bool]($dev7Text -match 'Com falha:\s+0,')) -and ([bool]($dev7Text -match 'Aprovado:\s+7,'))
Check 'FILTER-DEV7' $dev7Ok '20261109-040703-test-filter.log (exit 0, Com falha 0, Aprovado 7)'

# --- FILTER-QATEST: newest log of the QA's own filter must be green with the two QA facts ---
$qaFilter = Get-ChildItem $testRuns -Filter '*-test-filter.log' -ErrorAction SilentlyContinue |
  Sort-Object Name | Where-Object { (Get-Content -LiteralPath $_.FullName -TotalCount 1) -match 'FullyQualifiedName~Qa0022' } |
  Select-Object -Last 1
$qaText = if ($qaFilter) { Get-Content -Raw -LiteralPath $qaFilter.FullName } else { '' }
$qaOk = ([bool]($qaText -match '(?m)^exit:\s+0\s*$')) -and ([bool]($qaText -match 'Com falha:\s+0,')) -and ([bool]($qaText -match 'Aprovado:\s+2,'))
Check 'FILTER-QATEST' $qaOk $(if ($qaFilter) { "$($qaFilter.Name) (exit 0, Com falha 0, Aprovado 2)" } else { 'no Qa0022 filter log found' })

# --- RESTORE-SHA256 (S-16): every mutated product file is byte-identical to its PRE hash ---
$preHash = @{
  'backend/src/Squid.Service/SqliteContainerStore.cs'          = '7A7BA9C2CD679D7EA011F6400123AC5327616A62A9DD6C5FC3C02BCDD504F507'
  'backend/src/Squid.Api/Containers/ContainerEndpoints.cs'     = 'DAEE4CC7440C4D3C0ABE7E6D72C572EDAE1F0049F038D4759FE61B3C187725C0'
  'backend/src/Squid.Api/Containers/Ownership.cs'              = 'F35F5A56683F301A12E7F30955023FBDB5B70F3E8396EA4EFB81FD039DFBCA5A'
}
$hashBad = @()
foreach ($path in $preHash.Keys) {
  $now = (Get-FileHash $path -Algorithm SHA256).Hash
  if ($now -ne $preHash[$path]) { $hashBad += $path }
}
$hashDetail = "$($preHash.Count - $hashBad.Count)/3 files match PRE"
Check 'RESTORE-SHA256' ($hashBad.Count -eq 0) $hashDetail

# --- TESTFILE-INTEGRITY: the QA's own test file was never a mutation target and is intact ---
$qaTest = 'backend/tests/Squid.Tests/Containers/Qa0022SearchDiscoveryTests.cs'
$qaTestPre = '927533C3E775D0E31AFDD2047E73820C2611AB8DA912B554A4B141C70F9F2C3E'
$qaTestNow = (Get-FileHash $qaTest -Algorithm SHA256).Hash
Check 'TESTFILE-INTEGRITY' ($qaTestNow -eq $qaTestPre) $qaTestNow

# --- MUTATION-RESIDUE: no QA-MUTATION marker left anywhere under backend/ (S-16 leftovers) ---
$residue = 0
Get-ChildItem -Recurse -File -Path (Join-Path $repo 'backend') -Include *.cs,*.csproj,*.props,*.targets -ErrorAction SilentlyContinue |
  Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
  ForEach-Object { if ((Get-Content -Raw -LiteralPath $_.FullName) -match 'QA-MUTATION') { $residue++ } }
Check 'MUTATION-RESIDUE' ($residue -eq 0) "QA-MUTATION hits under backend/ = $residue"

# --- TOUCHES-NO-OUTSIDE (AC-05): baseline kept, only the four QA artifacts may be new ---
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

# --- MTIME-SWEEP: nothing under src/tests was written after QA started (04:06) except the
# three files this QA mutated (S-23 restores) and the QA test file - all inside the touches ---
$cut = [datetime]'2026-11-09 04:06:00'
$mutated = @(
  'backend/src/Squid.Api/Containers/ContainerEndpoints.cs',
  'backend/src/Squid.Api/Containers/Ownership.cs',
  'backend/src/Squid.Service/SqliteContainerStore.cs',
  'backend/tests/Squid.Tests/Containers/Qa0022SearchDiscoveryTests.cs'
)
$touches = @(
  'backend/src/Squid.Api/Containers/',
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
$sweepDetail = "files after 04:06 = $($swept.Count); unexpected (outside touches or not mutated) = $($badSweep.Count)"
Check 'MTIME-SWEEP' ($badSweep.Count -eq 0 -and $swept.Count -eq 4) $sweepDetail

# --- ROUTES-STATIC: direct filesystem read (S-24: git grep on untracked paths is vacuous) ---
$container = Get-Content -Raw 'backend/src/Squid.Api/Containers/ContainerEndpoints.cs'
$store = Get-Content -Raw 'backend/src/Squid.Service/SqliteContainerStore.cs'
$ownership = Get-Content -Raw 'backend/src/Squid.Api/Containers/Ownership.cs'
$routesOk = $container.Contains('app.MapGet("/api/containers/search", SearchAsync);') `
  -and $container.Contains('if (query.Length == 0)') `
  -and $container.Contains('return Results.Json(new { items });') `
  -and $container.Contains('seen ? container.MemberCount : null') `
  -and $container.Contains('membership?.State is MembershipState.Active or MembershipState.Silenced or MembershipState.Invited') `
  -and $container.Contains('&& container.Policy.Join is JoinPolicy.Open or JoinPolicy.Request;') `
  -and $container.Contains('ActorId = membership?.ActorId ?? ActorId(context, current.Handle),') `
  -and $container.Contains('request.PublisherIds ?? before.PublisherIds') `
  -and $store.Contains('container.IsSearchable') `
  -and $store.Contains('container.Name.Contains(query, StringComparison.OrdinalIgnoreCase)') `
  -and $ownership.Contains('SuccessionRules.Transfer(members, container.Slug, newOwnerHandle, newOwnerActorId, now)') `
  -and $ownership.Contains('OwnerActorId = newOwnerActorId,')
$routeDetail = 'search route + blank-q 400 + items envelope + memberCount gate + CanEnter Invited + join actor + publisher seat + IsSearchable + ci match + Ownership move (12 markers)'
Check 'ROUTES-STATIC' $routesOk $routeDetail

# --- FACTS-STATIC: the 7 DEV facts of this task plus the 2 QA facts exist where they claim ---
$factFiles = @{
  'backend/tests/Squid.Tests/Containers/ContainerSearchRoutesTests.cs' = 3
  'backend/tests/Squid.Tests/Containers/Sprint2ClosingFacetTests.cs'   = 3
  'backend/tests/Squid.Tests/Containers/ContainerStoreTests.cs'        = 6
  'backend/tests/Squid.Tests/Containers/Qa0022SearchDiscoveryTests.cs' = 2
}
$factBad = @()
foreach ($ff in $factFiles.Keys) {
  $n = ([regex]::Matches((Get-Content -Raw $ff), '\[Fact\]')).Count
  if ($n -ne $factFiles[$ff]) { $factBad += "$ff=$n" }
}
$factDetail = "3+3+6 DEV (7 new) + 2 QA facts where expected"
if ($factBad.Count -gt 0) { $factDetail = ($factBad -join '; ') }
Check 'FACTS-STATIC' ($factBad.Count -eq 0) $factDetail

# --- QA-DOC: the verdict document exists, names this task and carries the verdict ---
$qaDoc = 'docs/sdd/qa/QA-0021-descoberta-listagem.md'
$docOk = $false
if (Test-Path -LiteralPath $qaDoc) {
  $docText = Get-Content -Raw -LiteralPath $qaDoc
  $docOk = $docText.Contains('- task: TASK-0022') -and $docText.Contains('- verdict: PASSED')
}
Check 'QA-DOC' $docOk "$qaDoc (task id + verdict)"

# --- STATE-CHECK: the flow writer still accepts the task file after this QA's appends ---
$state = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'docs\sdd\_tools\sdd-state.ps1' check 'docs/sdd/tasks/TASK-0022-descoberta-listagem.md' 2>&1
$stateRc = $LASTEXITCODE
$stateLine = (($state | Where-Object { "$_" -match 'check:' } | Select-Object -First 1))
if ($null -eq $stateLine) { $stateLine = '(no check: line)' }
$stateDetail = "$stateLine"
Check 'STATE-CHECK' (($stateRc -eq 0) -and ("$stateLine" -match 'check: OK')) $stateDetail

Write-Output "SUMMARY total=17 fails=$($script:failures)"
if ($script:failures -eq 0) { Write-Output 'RESULT: PASS'; exit 0 }
Write-Output 'RESULT: FAIL'; exit 1
