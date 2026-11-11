# qa-check-task-0024 - static, log, restore and touches checks for TASK-0024 (QA-0023).
# Exit 0 only when every check passes: SUMMARY total=17 fails=0.
# Rules baked in: S-22 (green/red is read from the log's own exit: header and summary, never from
# the wrapper console), S-16 (SHA256 PRE of every file this QA mutated), S-23 (mtime after restore),
# the touches baseline of this attempt (git status --porcelain -uall captured at 2026-11-09 15:54,
# before QA wrote anything; only the seven QA artifacts may be new).
# Pure ASCII on purpose: the PS 5.1 BOM trap of QA-0013 cannot apply.
$ErrorActionPreference = 'Continue'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
Set-Location $repo

$script:failures = 0
function Check([string]$Id, [bool]$Ok, [string]$Detail) {
  if ($Ok) { Write-Output "CHECK $Id PASS $Detail" }
  else { $script:failures++; Write-Output "CHECK $Id FAIL $Detail" }
}

$runs = Join-Path $repo 'docs\sdd\runs\test-runs'

# --- GATE-BASELINE-EXIT / GATE-BASELINE-SUMMARY: this QA's start gate (S-22) ---
$basePath = Join-Path $runs '20261109-160508-test-gate.log'
$baseText = if (Test-Path -LiteralPath $basePath) { Get-Content -Raw -LiteralPath $basePath } else { '' }
Check 'GATE-BASELINE-EXIT' ([bool]($baseText -match '(?m)^exit:\s+0\s*$')) '20261109-160508-test-gate.log'
Check 'GATE-BASELINE-SUMMARY' (($baseText -match 'Com falha:\s+0,') -and ($baseText -match 'Aprovado:\s+274,')) `
  'need Com falha 0 and Aprovado 274 (baseline of this attempt = DEV gate)'

# --- GATE-NEWEST-EXIT / GATE-NEWEST-SUMMARY (S-22) ---
$gate = Get-ChildItem $runs -Filter '*-test-gate.log' -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -Last 1
$gateText = if ($gate) { Get-Content -Raw -LiteralPath $gate.FullName } else { '' }
Check 'GATE-NEWEST-EXIT' ([bool]($gateText -match '(?m)^exit:\s+0\s*$')) $(if ($gate) { $gate.Name } else { 'no gate log found' })
$gateOk = ($gateText -match 'Com falha:\s+0,') -and ($gateText -match 'Aprovado:\s+277,')
Check 'GATE-NEWEST-SUMMARY' $gateOk "need Com falha 0 and Aprovado 277 (274 baseline + 3 QA facts)"

# --- FILTER-DEV8: DEV's own eight new tests, run by DEV before QA (fixed log) ---
$dev8Path = Join-Path $runs '20261109-154055-test-filter.log'
$dev8Text = if (Test-Path -LiteralPath $dev8Path) { Get-Content -Raw -LiteralPath $dev8Path } else { '' }
$dev8Ok = ($dev8Text -match '(?m)^exit:\s+0\s*$') -and ($dev8Text -match 'Com falha:\s+0,') -and ($dev8Text -match 'Aprovado:\s+8,')
Check 'FILTER-DEV8' $dev8Ok '20261109-154055-test-filter.log (exit 0, Com falha 0, Aprovado 8)'

# --- FILTER-QATEST: newest Projections filter (8 DEV + 3 QA) must be green ---
$filter = Get-ChildItem $runs -Filter '*-test-filter.log' -ErrorAction SilentlyContinue |
  Sort-Object Name | Where-Object { (Get-Content -LiteralPath $_.FullName -TotalCount 1) -match 'FullyQualifiedName~Squid.Tests.Projections' } |
  Select-Object -Last 1
$filterText = if ($filter) { Get-Content -Raw -LiteralPath $filter.FullName } else { '' }
$filterOk = ($filterText -match '(?m)^exit:\s+0\s*$') -and ($filterText -match 'Com falha:\s+0,') -and ($filterText -match 'Aprovado:\s+11,')
Check 'FILTER-QATEST' $filterOk $(if ($filter) { "$($filter.Name) (exit 0, Com falha 0, Aprovado 11 = 8 DEV + 3 QA)" } else { 'no Projections filter log found' })

# --- MUTATION-RED: the four deliberately broken runs must be RED in their own log (S-22) and
# must name the test that is supposed to catch them (a red for another reason proves nothing).
$mutations = @(
  @{ Log = '20261109-161942-test-filter.log'; Must = @('Counter_and_log_never_diverge'); Id = 'M1 counter +1->+2' },
  @{ Log = '20261109-162426-test-filter.log'; Must = @('Ac01_a_new_database_gets_every_table'); Id = 'M2 seq index removed' },
  @{ Log = '20261109-163035-test-filter.log'; Must = @('Ac02_a_create_writes_the_post_summary', 'A_member_who_leaves_before_the_projection'); Id = 'M3 Active filter widened' },
  @{ Log = '20261109-163658-test-filter.log'; Must = @('Projecting_the_same_event_a_second_time'); Id = 'M4 ON CONFLICT removed' }
)
$mutBad = @()
foreach ($m in $mutations) {
  $path = Join-Path $runs $m.Log
  $text = if (Test-Path -LiteralPath $path) { Get-Content -Raw -LiteralPath $path } else { '' }
  $red = $text -match '(?m)^exit:\s+1\s*$'
  $named = $true
  foreach ($needle in $m.Must) { if (-not $text.Contains($needle)) { $named = $false } }
  if (-not ($red -and $named)) { $mutBad += $m.Id }
}
Check 'MUTATION-RED' ($mutBad.Count -eq 0) "4/4 red with the expected test named$(if ($mutBad.Count) { ' - bad: ' + ($mutBad -join ', ') })"

# --- MUTATION-RESTORE (S-16): every mutated product file is byte-identical to its PRE hash ---
$preHash = @{
  'backend/src/Squid.Service/SqliteActivityStore.cs'          = '40D877435A6D43F25456B87B78CB2F369221DB94D7969C0F9271B6D3FD0C96E8'
  'backend/src/Squid.Persistence/schema/0001_init_projections.sql' = 'C82191A0162B3E6ECF3DA38F27FE403995C9C066D10B73C459B7134669CE1855'
  'backend/src/Squid.ReadModels/Projections/ActivityProjector.cs' = '254C24BCE354B54B67F99A5DC43400188E88A5444968135829E028A34F43FDF1'
}
$hashBad = @()
foreach ($path in $preHash.Keys) {
  $now = (Get-FileHash $path -Algorithm SHA256).Hash
  if ($now -ne $preHash[$path]) { $hashBad += $path }
}
Check 'MUTATION-RESTORE' ($hashBad.Count -eq 0) "$($preHash.Count - $hashBad.Count)/3 files match PRE$(if ($hashBad.Count) { ' - bad: ' + ($hashBad -join ', ') })"

# --- MUTATION-RESIDUE: no QA-MUTATION marker left anywhere under backend/ ---
$residue = 0
Get-ChildItem -Recurse -File -Path (Join-Path $repo 'backend') -Include *.cs,*.csproj,*.props,*.targets,*.sql -ErrorAction SilentlyContinue |
  Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
  ForEach-Object { if ((Get-Content -Raw -LiteralPath $_.FullName) -match 'QA-MUTATION') { $residue++ } }
Check 'MUTATION-RESIDUE' ($residue -eq 0) "QA-MUTATION hits under backend/ = $residue"

# --- MTIME-SWEEP (S-23 + AC-06): every product file written since the DEV dispatch (14:05) is
# inside the task's touches - 22 files, of which 4 are this QA's (3 mutated/restored + its test).
# bin/obj excluded: the build writes them on every run. ---
$cut = [datetime]'2026-11-09 14:05:00'
$expected = @(
  'backend/Directory.Packages.props',
  'backend/src/Squid.Persistence/schema/0001_init_projections.sql',
  'backend/src/Squid.Persistence/schema/SchemaMigrator.cs',
  'backend/src/Squid.ReadModels/Projections/ActivityLogEntry.cs',
  'backend/src/Squid.ReadModels/Projections/ActivityProjector.cs',
  'backend/src/Squid.ReadModels/Projections/IActivityProjector.cs',
  'backend/src/Squid.ReadModels/Projections/SqliteActivityLog.cs',
  'backend/src/Squid.ReadModels/Squid.ReadModels.csproj',
  'backend/src/Squid.Service/SchemaMigrations.cs',
  'backend/src/Squid.Service/SqliteActivityStore.cs',
  'backend/src/Squid.Service/Squid.Service.csproj',
  'backend/src/Squid.Service/SquidServiceHost.cs',
  'backend/src/Squid.Worker/Program.cs',
  'backend/src/Squid.Worker/Projection/ActivityProjectionPump.cs',
  'backend/src/Squid.Worker/Squid.Worker.csproj',
  'backend/tests/Squid.Tests/Projections/ActivityProjectorTests.cs',
  'backend/tests/Squid.Tests/Projections/ProjectionDb.cs',
  'backend/tests/Squid.Tests/Projections/ProjectionDoubles.cs',
  'backend/tests/Squid.Tests/Projections/ProjectionOffWritePathTests.cs',
  'backend/tests/Squid.Tests/Projections/ProjectionPumpTests.cs',
  'backend/tests/Squid.Tests/Projections/QaTask0024Tests.cs',
  'backend/tests/Squid.Tests/Projections/SchemaMigrationTests.cs'
)
$swept = Get-ChildItem -Recurse -File -Path (Join-Path $repo 'backend') -ErrorAction SilentlyContinue |
  Where-Object { ($_.FullName -notmatch '\\(bin|obj)\\') -and ($_.LastWriteTime -gt $cut) }
$sweepBad = @()
foreach ($f in $swept) {
  $rel = $f.FullName.Substring($repo.Length + 1).Replace('\', '/')
  if ($expected -notcontains $rel) { $sweepBad += $rel }
}
$sweepMissing = @($expected | Where-Object {
  $want = $_
  -not ($swept | Where-Object { $_.FullName.Substring($repo.Length + 1).Replace('\', '/') -eq $want })
})
Check 'MTIME-SWEEP' (($swept.Count -eq 22) -and ($sweepBad.Count -eq 0) -and ($sweepMissing.Count -eq 0)) `
  "product files after 14:05 = $($swept.Count)/22 (need 22, all inside the touches); unexpected = $($sweepBad.Count); missing = $($sweepMissing.Count)$(if ($sweepBad.Count) { ' - ' + ($sweepBad -join ', ') })"

# --- TOUCHES-NO-OUTSIDE (AC-06): baseline kept, only the seven QA artifacts may be new ---
$baseline = @(
  'backend/Directory.Packages.props',
  'backend/src/Squid.ReadModels/Squid.ReadModels.csproj',
  'backend/src/Squid.Service/Squid.Service.csproj',
  'backend/src/Squid.Service/SquidServiceHost.cs',
  'backend/src/Squid.Worker/Program.cs',
  'backend/src/Squid.Worker/Squid.Worker.csproj',
  'docs/sdd/ideas/BACKLOG.md',
  'docs/sdd/plan/plan.md',
  'backend/src/Squid.Api/Posts/IActivityStore.cs',
  'backend/src/Squid.Api/Posts/IActorStatusStore.cs',
  'backend/src/Squid.Api/Posts/IObjectStore.cs',
  'backend/src/Squid.Api/Posts/PostEndpoints.cs',
  'backend/src/Squid.Api/Posts/PostView.cs',
  'backend/src/Squid.Domain/Objects/ActorStatus.cs',
  'backend/src/Squid.Domain/Objects/PostObject.cs',
  'backend/src/Squid.Domain/Objects/PostSanitizer.cs',
  'backend/src/Squid.Domain/Objects/ReplyDepth.cs',
  'backend/src/Squid.Persistence/schema/0001_init_projections.sql',
  'backend/src/Squid.Persistence/schema/SchemaMigrator.cs',
  'backend/src/Squid.ReadModels/Projections/ActivityLogEntry.cs',
  'backend/src/Squid.ReadModels/Projections/ActivityProjector.cs',
  'backend/src/Squid.ReadModels/Projections/IActivityProjector.cs',
  'backend/src/Squid.ReadModels/Projections/SqliteActivityLog.cs',
  'backend/src/Squid.Service/SchemaMigrations.cs',
  'backend/src/Squid.Service/SqliteActivityStore.cs',
  'backend/src/Squid.Service/SqliteActorStatusStore.cs',
  'backend/src/Squid.Service/SqliteObjectStore.cs',
  'backend/src/Squid.Worker/Projection/ActivityProjectionPump.cs',
  'backend/tests/Squid.Tests/Posts/PostContentRulesTests.cs',
  'backend/tests/Squid.Tests/Posts/PostPublishTests.cs',
  'backend/tests/Squid.Tests/Posts/PostReadAndReplyTests.cs',
  'backend/tests/Squid.Tests/Posts/PostRoutes.cs',
  'backend/tests/Squid.Tests/Posts/PostSeqTests.cs',
  'backend/tests/Squid.Tests/Posts/QaTask0023Tests.cs',
  'backend/tests/Squid.Tests/Projections/ActivityProjectorTests.cs',
  'backend/tests/Squid.Tests/Projections/ProjectionDb.cs',
  'backend/tests/Squid.Tests/Projections/ProjectionDoubles.cs',
  'backend/tests/Squid.Tests/Projections/ProjectionOffWritePathTests.cs',
  'backend/tests/Squid.Tests/Projections/ProjectionPumpTests.cs',
  'backend/tests/Squid.Tests/Projections/SchemaMigrationTests.cs',
  'docs/sdd/qa/QA-0022-nucleo-do-objeto-post.md',
  'docs/sdd/specs/SPEC-0009-publicacao-feed-postagem.md',
  'docs/sdd/tasks/TASK-0023-nucleo-do-objeto-post.md',
  'docs/sdd/tasks/TASK-0024-migracao-projecoes-feed.md',
  'docs/sdd/tasks/TASK-0025-feed-timeline-delta.md',
  'docs/sdd/tasks/TASK-0026-sse-stream.md',
  'docs/sdd/tasks/TASK-0027-upload-midia-skia.md',
  'docs/sdd/tasks/TASK-0028-cota-midia.md',
  'docs/sdd/tasks/TASK-0029-unfurl-ssrf.md',
  'docs/sdd/tasks/TASK-0030-remocao-post-tombstone.md',
  'docs/sdd/tasks/TASK-0031-denuncia-admin-suspensao.md',
  'docs/sdd/tasks/TASK-0032-thumbnail-loadtest-facet.md'
)
$allowedNew = @(
  'backend/tests/Squid.Tests/Projections/QaTask0024Tests.cs',
  'docs/sdd/qa/QA-0023-migracao-projecoes-feed.md',
  'docs/sdd/qa/qa-check-task-0024.ps1',
  'docs/sdd/qa/qa-check-task-0024.sh',
  'docs/sdd/qa/qa-worker-fixture-task-0024.py',
  'docs/sdd/qa/qa-worker-probe-task-0024.ps1',
  'docs/sdd/qa/qa-worker-probe-task-0024.sh'
)
$porcelain = & git status --porcelain -uall
$gitOk = ($LASTEXITCODE -eq 0)
$current = @()
if ($gitOk) { foreach ($line in $porcelain) { if ("$line".Length -gt 3) { $current += "$line".Substring(3) } } }
$missing = @($baseline | Where-Object { $current -notcontains $_ })
$extras = @($current | Where-Object { ($baseline -notcontains $_) -and ($allowedNew -notcontains $_) })
$newAllowed = @($current | Where-Object { ($baseline -notcontains $_) -and ($allowedNew -contains $_) })
Check 'TOUCHES-NO-OUTSIDE' ($gitOk -and ($missing.Count -eq 0) -and ($extras.Count -eq 0)) `
  "git=$gitOk missing=$($missing.Count) out-of-scope=$($extras.Count) new-allowed=$($newAllowed.Count)/7$(if ($extras.Count) { ' - ' + ($extras -join ', ') })"

# --- FACTS-STATIC: 8 DEV facts + 3 QA facts where they claim to be ---
$factFiles = @{
  'backend/tests/Squid.Tests/Projections/SchemaMigrationTests.cs'         = 3
  'backend/tests/Squid.Tests/Projections/ActivityProjectorTests.cs'       = 1
  'backend/tests/Squid.Tests/Projections/ProjectionPumpTests.cs'          = 3
  'backend/tests/Squid.Tests/Projections/ProjectionOffWritePathTests.cs'  = 1
  'backend/tests/Squid.Tests/Projections/QaTask0024Tests.cs'              = 3
}
$factBad = @()
foreach ($ff in $factFiles.Keys) {
  $n = ([regex]::Matches((Get-Content -Raw $ff), '\[Fact\]')).Count
  if ($n -ne $factFiles[$ff]) { $factBad += "$ff=$n" }
}
Check 'FACTS-STATIC' ($factBad.Count -eq 0) "3+1+3+1 DEV (8) + 3 QA facts where expected$(if ($factBad.Count) { ' - ' + ($factBad -join '; ') })"

# --- WORKER-PROBE: the built Squid.Worker process really pumped the log (AC-05 composition) ---
$probePath = Join-Path $runs '20261109-164624-qa-worker-probe.log'
$probeText = if (Test-Path -LiteralPath $probePath) { Get-Content -Raw -LiteralPath $probePath } else { '' }
$probeOk = ($probeText -match '(?m)^exit:\s+0\s*$') -and $probeText.Contains('PROBE verify PASS') -and $probeText.Contains('negative-control ok')
Check 'WORKER-PROBE' $probeOk '20261109-164624-qa-worker-probe.log (exit 0, negative control 1 then PROBE verify PASS)'

# --- WORKER-PROBE-LINUX: the .sh twin ran and was CLASSIFIED, never assumed equal to Windows ---
$linuxPath = Join-Path $runs '20261109-164701-qa-worker-probe-linux.log'
$linuxText = if (Test-Path -LiteralPath $linuxPath) { Get-Content -Raw -LiteralPath $linuxPath } else { '' }
$linuxOk = ($linuxText -match '(?m)^exit:\s+3\s*$') -and ($linuxText -match 'no dotnet on PATH')
Check 'WORKER-PROBE-LINUX' $linuxOk '20261109-164701-qa-worker-probe-linux.log (exit 3 environment: Linux stays unverified)'

# --- SIDE-EFFECTS: no qa0023 temp directory survived the probe ---
$leftovers = @(Get-ChildItem -Path ([System.IO.Path]::GetTempPath()) -Filter 'qa0023*' -ErrorAction SilentlyContinue)
Check 'SIDE-EFFECTS' ($leftovers.Count -eq 0) "qa0023* entries left in TEMP = $($leftovers.Count)"

# --- QA-DOC: the verdict document exists, names this task and carries the verdict ---
$qaDoc = 'docs/sdd/qa/QA-0023-migracao-projecoes-feed.md'
$docOk = $false
if (Test-Path -LiteralPath $qaDoc) {
  $docText = Get-Content -Raw -LiteralPath $qaDoc
  $docOk = $docText.Contains('- task: TASK-0024') -and $docText.Contains('- verdict: PASSED')
}
Check 'QA-DOC' $docOk "$qaDoc (task id + verdict)"

# --- STATE-CHECK: the flow writer still accepts the task file after this QA's appends ---
$state = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'docs\sdd\_tools\sdd-state.ps1' check 'docs/sdd/tasks/TASK-0024-migracao-projecoes-feed.md' 2>&1
$stateRc = $LASTEXITCODE
$stateLine = (($state | Where-Object { "$_" -match 'check:' } | Select-Object -First 1))
if ($null -eq $stateLine) { $stateLine = '(no check: line)' }
Check 'STATE-CHECK' (($stateRc -eq 0) -and ("$stateLine" -match 'check: OK')) "$stateLine"

Write-Output "SUMMARY total=17 fails=$($script:failures)"
if ($script:failures -eq 0) { Write-Output 'RESULT: PASS'; exit 0 }
Write-Output 'RESULT: FAIL'; exit 1
