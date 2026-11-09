# qa-check-task-0019 — static, log, restore and touches checks for TASK-0019 (QA-0018).
# Exit 0 only when every check passes: SUMMARY total=13 fails=0.
# Rules baked in: S-22 (green/red read from the log's own exit: header and summary, never from
# the wrapper console), S-16 (SHA256 PRE of every file this QA mutated), S-23 (mtime bumped
# after each restore), the touches baseline of this attempt (git status --porcelain captured at
# 2026-11-08 22:05, before QA wrote anything; only the four QA artifacts may be new) and the
# AC-07 origin rule (no fixed https assertion left in the three named test files).
# Pure ASCII on purpose: the PS 5.1 BOM trap of QA-0013 cannot apply.
$ErrorActionPreference = 'Continue'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
Set-Location $repo

$script:failures = 0
function Check([string]$Id, [bool]$Ok, [string]$Detail) {
  if ($Ok) { Write-Output "CHECK $Id PASS $Detail" }
  else { $script:failures++; Write-Output "CHECK $Id FAIL $Detail" }
}

# Baseline captured at QA start (git status --porcelain, 2026-11-08 22:05), prefixes stripped.
$baseline = @(
  'backend/src/Squid.Service/SquidServiceHost.cs',
  'docs/sdd/ideas/BACKLOG.md',
  'docs/sdd/plan/plan.md',
  'backend/src/Squid.Api/Admin/',
  'backend/src/Squid.Api/Containers/',
  'backend/src/Squid.Domain/Containers/',
  'backend/src/Squid.Service/SqliteContainerStore.cs',
  'backend/src/Squid.Service/SqlitePolicyAuditStore.cs',
  'backend/tests/Squid.Tests/Containers/',
  'docs/sdd/qa/QA-0016-nucleo-do-container.md',
  'docs/sdd/qa/QA-0017-politica-auditoria-operador.md',
  'docs/sdd/qa/qa-check-task-0018.ps1',
  'docs/sdd/qa/qa-check-task-0018.sh',
  'docs/sdd/specs/SPEC-0008-containers-sociais-p03.md',
  'docs/sdd/tasks/TASK-0017-nucleo-do-container.md',
  'docs/sdd/tasks/TASK-0018-politica-auditoria-operador.md',
  'docs/sdd/tasks/TASK-0019-membership-entrada-e-listagem.md',
  'docs/sdd/tasks/TASK-0020-papeis-moderacao-avaliador.md',
  'docs/sdd/tasks/TASK-0021-sucessao-transferencia-exclusao.md',
  'docs/sdd/tasks/TASK-0022-descoberta-listagem.md'
)

$allowedNew = @(
  'docs/sdd/qa/QA-0018-membership-entrada-e-listagem.md',
  'docs/sdd/qa/qa-check-task-0019.ps1',
  'docs/sdd/qa/qa-check-task-0019.sh',
  'docs/sdd/qa/qa-mutate-task-0019.ps1'
)

# Files this QA is allowed to have written inside backend/ (all inside the touches of TASK-0019:
# the partial artifact of the dead QA subagent plus the two product files this QA mutated and
# byte-exactly restored).
$expectedAfterCut = @(
  'backend/tests/Squid.Tests/Containers/Qa0018MembershipPageAndTrailTests.cs',
  'backend/src/Squid.Domain/Containers/MembershipRules.cs',
  'backend/src/Squid.Api/Containers/ContainerEndpoints.cs'
)

# --- GATE-NEWEST-EXIT / GATE-NEWEST-SUMMARY (S-22: the log classifies, not the console) -----
$testRuns = Join-Path $repo 'docs\sdd\runs\test-runs'
$gate = Get-ChildItem $testRuns -Filter '*-test-gate.log' -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -Last 1
$gateText = if ($gate) { Get-Content -Raw -LiteralPath $gate.FullName } else { '' }
Check 'GATE-NEWEST-EXIT' ([bool]($gateText -match '(?m)^exit:\s+0\s*$')) $(if ($gate) { $gate.Name } else { 'no gate log found' })

$gateFalha = -1; $gateAprov = -1
if ($gateText -match 'Com falha:\s+(\d+)') { $gateFalha = [int]$Matches[1] }
if ($gateText -match 'Aprovado:\s+(\d+)') { $gateAprov = [int]$Matches[1] }
Check 'GATE-NEWEST-SUMMARY' (($gateFalha -eq 0) -and ($gateAprov -ge 201)) "Com falha=$gateFalha Aprovado=$gateAprov (need falha=0 and aprovado>=201)"

# --- FILTER-NEWEST-EXIT / FILTER-NEWEST-SUMMARY: the 34 Containers tests QA runs last ------
$filter = Get-ChildItem $testRuns -Filter '*-test-filter.log' -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -Last 1
$filterText = if ($filter) { Get-Content -Raw -LiteralPath $filter.FullName } else { '' }
Check 'FILTER-NEWEST-EXIT' ([bool]($filterText -match '(?m)^exit:\s+0\s*$')) $(if ($filter) { $filter.Name } else { 'no filter log found' })

$filterFalha = -1; $filterAprov = -1
if ($filterText -match 'Com falha:\s+(\d+)') { $filterFalha = [int]$Matches[1] }
if ($filterText -match 'Aprovado:\s+(\d+)') { $filterAprov = [int]$Matches[1] }
Check 'FILTER-NEWEST-SUMMARY' (($filterFalha -eq 0) -and ($filterAprov -ge 34)) "Com falha=$filterFalha Aprovado=$filterAprov (need falha=0 and aprovado>=34)"

# --- RESTORE-SHA256 (S-16): both mutated product files byte-identical to their PRE hash ----
$preHash = @{
  'backend/src/Squid.Api/Containers/ContainerEndpoints.cs' = '4E56720255D5E50E04AFC0C76A890FB7D144893075E09D7D234C1D2DB311A979'
  'backend/src/Squid.Domain/Containers/MembershipRules.cs' = '57312633E7978231D4C309913B72DCD172F90A92CC843F8F7E4D025368734803'
}
$hashBad = @()
foreach ($path in $preHash.Keys) {
  $now = (Get-FileHash $path -Algorithm SHA256).Hash
  if ($now -ne $preHash[$path]) { $hashBad += "$path=$now" }
}
Check 'RESTORE-SHA256' ($hashBad.Count -eq 0) "$($preHash.Count - $hashBad.Count)/2 files match PRE"

# --- MUTATION-RESIDUE: no QA-MUTATION marker left anywhere under backend/ ------------------
$residue = 0
Get-ChildItem -Recurse -File -Path (Join-Path $repo 'backend') -Include *.cs,*.csproj,*.props,*.targets -ErrorAction SilentlyContinue |
  Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
  ForEach-Object { if ((Get-Content -Raw -LiteralPath $_.FullName) -match 'QA-MUTATION') { $residue++ } }
Check 'MUTATION-RESIDUE' ($residue -eq 0) "QA-MUTATION hits under backend/ = $residue"

# --- TOUCHES-NO-OUTSIDE (AC-06): baseline kept, only the four QA artifacts may be new ------
$porcelain = & git status --porcelain
$gitOk = ($LASTEXITCODE -eq 0)
$current = @()
if ($gitOk) {
  foreach ($line in $porcelain) { if ("$line".Length -gt 3) { $current += "$line".Substring(3) } }
}
$missing = @($baseline | Where-Object { $current -notcontains $_ })
$extras = @($current | Where-Object { ($baseline -notcontains $_) -and ($allowedNew -notcontains $_) })
$newAllowed = @($current | Where-Object { ($baseline -notcontains $_) -and ($allowedNew -contains $_) })
Check 'TOUCHES-NO-OUTSIDE' ($gitOk -and ($missing.Count -eq 0) -and ($extras.Count -eq 0)) "git=$gitOk missing=$($missing.Count) out-of-scope=$($extras.Count) new-allowed=$($newAllowed.Count)/4"

# --- MTIME-SWEEP: nothing under src/tests was written after DEV closed (19:55) except the
# partial artifact of the dead QA subagent and the two files this QA mutated + restored,
# every one of them inside the touches of TASK-0019 ---
$cut = [datetime]'2026-11-08 19:55:00'
$swept = @()
foreach ($root in @((Join-Path $repo 'backend\src'), (Join-Path $repo 'backend\tests'))) {
  $swept += Get-ChildItem -Recurse -File -Path $root -ErrorAction SilentlyContinue |
    Where-Object { ($_.FullName -notmatch '\\(bin|obj)\\') -and ($_.LastWriteTime -gt $cut) }
}
$sweptRel = @($swept | ForEach-Object { $_.FullName.Substring($repo.Length + 1).Replace('\', '/') })
$unexpected = @($sweptRel | Where-Object { $expectedAfterCut -notcontains $_ })
$absent = @($expectedAfterCut | Where-Object { $sweptRel -notcontains $_ })
Check 'MTIME-SWEEP' (($sweptRel.Count -eq 3) -and ($unexpected.Count -eq 0) -and ($absent.Count -eq 0)) "files after 19:55 = $($sweptRel.Count); unexpected = $($unexpected.Count); expected missing = $($absent.Count)"

# --- AC07-STATIC: the five obsolete https assertions are gone, the rule is asserted instead -
$routesText = Get-Content -Raw 'backend/tests/Squid.Tests/Containers/ContainerRoutesTests.cs'
$patchText = Get-Content -Raw 'backend/tests/Squid.Tests/Containers/Qa0016ContainerPatchScopeTests.cs'
$auditText = Get-Content -Raw 'backend/tests/Squid.Tests/Containers/PolicyAuditRoutesTests.cs'
$httpsLeft = ([regex]::Matches($routesText, 'https://')).Count + ([regex]::Matches($patchText, 'https://')).Count + ([regex]::Matches($auditText, 'https://')).Count
$schemeAsserts = ([regex]::Matches($routesText, 'BaseAddress!\.Scheme')).Count + ([regex]::Matches($patchText, 'BaseAddress!\.Scheme')).Count + ([regex]::Matches($auditText, 'BaseAddress!\.Scheme')).Count
$httpPrefixOk = $routesText.Contains('Assert.StartsWith("http://127.0.0.1:",')
$ac07Ok = ($httpsLeft -eq 0) -and $httpPrefixOk -and ($schemeAsserts -ge 5)
Check 'AC07-STATIC' $ac07Ok "https:// left in the 3 files = $httpsLeft; scheme+Host assertions = $schemeAsserts (need 0 and >=5); http prefix assert present = $httpPrefixOk"

# --- SC006-STATIC: the 100 invite-less attempts are really in the AC-02 test ----------------
$membershipText = Get-Content -Raw 'backend/tests/Squid.Tests/Containers/MembershipRoutesTests.cs'
$sc006Ok = $membershipText.Contains('var attempts = 0;') -and $membershipText.Contains('attempts++') `
  -and $membershipText.Contains('Assert.Equal(100, attempts)') -and $membershipText.Contains('for (var actor = 0; actor < 10; actor++)') `
  -and $membershipText.Contains('for (var attempt = 0; attempt < 10; attempt++)')
Check 'SC006-STATIC' $sc006Ok '10x10 loop + attempts=100 assertion read from MembershipRoutesTests.cs'

# --- ROUTES-STATIC: the four entry routes, the origin rule, the filter and the paging -------
$container = Get-Content -Raw 'backend/src/Squid.Api/Containers/ContainerEndpoints.cs'
$routesOk = $container.Contains('app.MapPost("/api/containers/{slug}/join", JoinAsync);') `
  -and $container.Contains('app.MapPost("/api/containers/{slug}/invites", InviteAsync);') `
  -and $container.Contains('app.MapPost("/api/containers/{slug}/leave", LeaveAsync);') `
  -and $container.Contains('app.MapGet("/api/containers/{slug}/members", MembersAsync);') `
  -and $container.Contains('$"{context.Request.Scheme}://{context.Request.Host.Value}"') `
  -and $container.Contains('private const int DefaultLimit = 100;') `
  -and $container.Contains('private const int MaxLimit = 1000;') `
  -and $container.Contains('state == MembershipState.Active')
Check 'ROUTES-STATIC' $routesOk 'join/invites/leave/members + scheme+Host origin + limit 100/1000 + Active filter read from the filesystem'

# --- STATE-CHECK: the task file still passes the writer tool's own structure check ----------
$state = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'docs\sdd\_tools\sdd-state.ps1') check 'docs/sdd/tasks/TASK-0019-membership-entrada-e-listagem.md' 2>&1 | Out-String
Check 'STATE-CHECK' (($LASTEXITCODE -eq 0) -and ($state -match 'check: OK')) ($state.Trim() -replace '\s+', ' ')

# --- EVALUATOR-UNTOUCHED: TASK-0020's boundary stayed byte-identical -----------------------
$evalHash = (Get-FileHash 'backend/src/Squid.Domain/IPolicyEvaluator.cs' -Algorithm SHA256).Hash
Check 'EVALUATOR-UNTOUCHED' ($evalHash -eq '94CDDFA4DC578B2B6655A702136177A2BDB68F25045318F8AC73A2517D6D6DCF') $evalHash

Write-Output "SUMMARY total=13 fails=$($script:failures)"
if ($script:failures -eq 0) { Write-Output 'RESULT: PASS'; exit 0 }
Write-Output 'RESULT: FAIL'; exit 1
