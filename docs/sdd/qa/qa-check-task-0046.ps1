# QA-0046 evidence checker (static; it does not re-run the suite). Fourteen can-fail cycles
# over the motor (T1-T14: AC-01 x2, AC-02 x4, AC-03 x2, AC-04 x2, AC-05 x3, AC-06), the AC-05
# CLI probe (ps1 + sh twin), the scope scan (ps1 + sh twin) and the AC-07 gate. Every exit and
# summary is read from the LOG FILES (S-22 - never from a wrapper's console code), every
# restore from the recorded hashes (S-16) and the final tree from git.
# ASCII-only on purpose: this file runs without a UTF-8 BOM under Windows PowerShell 5.1.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0046.ps1
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$runs = Join-Path $repo 'docs\sdd\runs\test-runs'
$task = Join-Path $repo 'docs\sdd\tasks\TASK-0046-peer-grain-fila-e-dlq.md'
$total = 0; $fails = 0

function Check([string]$name, [bool]$ok, [string]$detail) {
    $script:total++
    if ($ok) { Write-Output "CHECK $name OK - $detail" }
    else { $script:fails++; Write-Output "CHECK $name FAIL - $detail" }
}

function Newest([string]$filter) {
    Get-ChildItem -LiteralPath $runs -Filter $filter | Sort-Object Name | Select-Object -Last 1
}

function Read-IfPresent([string]$path) {
    if (Test-Path -LiteralPath $path) { Get-Content -LiteralPath $path -Raw } else { '' }
}

# 1. The git baseline this QA captured before writing anything (HEAD of the DEV commit).
$base = Read-IfPresent (Join-Path $PSScriptRoot 'QA-0046-git-baseline.txt')
Check 'BASELINE-HEAD' ($base -match 'HEAD: 61c12cecc1e6b14e370b6a861122595a2e021348') 'baseline HEAD = 61c12ce'
Check 'BASELINE-BRANCH' ($base -match 'branch: task/TASK-0046-peer-grain-fila-e-dlq') 'branch task/TASK-0046-peer-grain-fila-e-dlq'
Check 'BASELINE-WSL' ($base -match 'wsl dotnet: \[\] exit=1') 'dotnet ausente no WSL registrado na baseline (suite Linux = unverified)'
Check 'BASELINE-CLEAN' ($base -match '(?m)^ M docs/sdd/tasks/TASK-0046-peer-grain-fila-e-dlq\.md\r?$' -and
    (@($base -split "`n" | Where-Object { $_ -match '^[ MADRC?!]{1,2}\s' }).Count -eq 1)) 'na baseline so o task file do ODEV estava modificado'

# The DEV commit touches only the five glob sets of the task's touches. Anything else in
# 61c12ce would break AC-07.
$inList = $false; $outside = @(); $files = @()
foreach ($line in ($base -split "`n")) {
    if ($line -match '^--- commit files') { $inList = $true; continue }
    if ($inList -and $line -match '^--- ') { break }
    if ($inList -and $line.Trim()) {
        $path = $line.Trim()
        $files += $path
        if ($path -notmatch '^(backend/src/Squid\.Grains/Peers/|backend/src/Squid\.PeerClient/|backend/src/Squid\.Service/|backend/src/Squid\.ReadModels/|backend/tests/Squid\.Tests/)') {
            $outside += $path
        }
    }
}
Check 'COMMIT-TOUCHES' ($outside.Count -eq 0 -and $files.Count -eq 18) $(if ($outside.Count -eq 0) { '61c12ce: 18 arquivos, todos nos touches (produto + testes)' } else { 'fora dos touches: ' + ($outside -join '; ') })

# 2. This QA's baseline run of the 51 federation scenarios before any write.
$bt = Read-IfPresent (Join-Path $runs '20261114-024950-test-filter.log')
Check 'BASELINE-FILTER' ($bt -match '(?m)^exit:\s+0\s*$' -and $bt -match 'Com falha:\s*0,\s+Aprovado:\s*51,\s+Ignorado:\s*0,\s+Total:\s*51') '20261114-024950-test-filter.log exit 0 / 51 de 51 (antes de qualquer escrita da QA)'

# 3. The can-fail driver self-check recorded before the run.
$vt = Read-IfPresent (Join-Path $PSScriptRoot 'QA-0046-verify.log')
Check 'VERIFY-RUN' ($vt -match 'VERIFY total=14 bad=0') 'QA-0046-verify.log VERIFY total=14 bad=0'
Check 'VERIFY-HITS' (([regex]::Matches($vt, 'hits=1 hits2=1 ok=true')).Count -eq 14 -and ($vt -match 'non-ascii-bytes=0 ok=true')) '14/14 padroes hits=1 + ASCII do driver OK'

# 4. The AC-05 CLI probe, both shells: five checks each, all green.
$ac05 = Read-IfPresent (Join-Path $PSScriptRoot 'QA-0046-ac05-cli.log')
Check 'AC05-CLI-SUMMARY' ($ac05 -match 'SUMMARY checks=5 fails=0') 'QA-0046-ac05-cli.log SUMMARY checks=5 fails=0'
Check 'AC05-CLI-SCENARIOS' (([regex]::Matches($ac05, 'CHECK SCENARIO-(500|TIMEOUT|SLOW) OK')).Count -eq 3 -and
    $ac05 -match 'curl-exit=28' -and $ac05 -match 'http=500' -and $ac05 -match 'elapsed=\d+ms \(esperado >= 1200') 'os tres cenarios ao vivo: 500, tempo esgotado (28) e lentidao (>= 1200 ms)'
Check 'AC05-CLI-TOKENS' ($ac05 -match 'usage declara os cenarios 500/timeout/slow') 'o --help declara 500/timeout/slow'
$ac05sh = Read-IfPresent (Join-Path $PSScriptRoot 'QA-0046-ac05-cli-sh.log')
Check 'AC05-CLI-SH-SUMMARY' ($ac05sh -match 'SUMMARY checks=5 fails=0') 'QA-0046-ac05-cli-sh.log SUMMARY checks=5 fails=0 (gêmeo bash)'

# 5. AC-07 gate: exit header + summary INSIDE the gate log (S-22), 554 tests, and the gate is
#    NEWER than the GREEN of the mutation run (it ran over the restored tree).
$gate = Newest '*-test-gate.log'
$gt = if ($gate) { Get-Content -LiteralPath $gate.FullName -Raw } else { '' }
Check 'GATE-EXIT' ($gt -match '(?m)^exit:\s+0\s*$') $(if ($gate) { '{0} header exit 0' -f $gate.Name } else { 'nenhum gate log' })
Check 'GATE-SUMMARY' ($gt -match 'Com falha:\s*0,\s+Aprovado:\s*554,\s+Ignorado:\s*0,\s+Total:\s*554') 'Com falha 0 / Aprovado 554 / Total 554 (554 do DEV, sem teste novo da QA)'

$mutLog = Read-IfPresent (Join-Path $PSScriptRoot 'QA-0046-mutations.log')
$greenLine = [regex]::Match($mutLog, '(?m)^GREEN wrapper=0 logExit=0 aprovado=51 falha=0 log=(\S+)')
$greenOk = $greenLine.Success
if ($greenOk -and $gate) {
    $gpath = Join-Path $runs $greenLine.Groups[1].Value
    Check 'GATE-AGE' ((Test-Path -LiteralPath $gpath) -and ($gate.LastWriteTime -gt (Get-Item -LiteralPath $gpath).LastWriteTime)) ('{0} criado depois do GREEN {1}' -f $gate.Name, $greenLine.Groups[1].Value)
}
else {
    Check 'GATE-AGE' $false 'GREEN ou gate log ausente'
}

# 6. The mutation ledger (S-16/S-22/S-23): every cycle ran RED with its own AC's scenario
#    inside the log, every restore came back to PRE, the green over the restored sources.
$ids = 'T1', 'T2', 'T3', 'T4', 'T5', 'T6', 'T7', 'T8', 'T9', 'T10', 'T11', 'T12', 'T13', 'T14'
$missing = @()
foreach ($id in $ids) {
    if ($mutLog -notmatch [regex]::Escape("$id PRE ")) { $missing += $id }
}
Check 'MUT-IDS' ($missing.Count -eq 0) $(if ($missing.Count -eq 0) { 'T1..T14 (14 ciclos) todos com linha PRE' } else { 'faltando: ' + ($missing -join ',') })
Check 'MUT-HEAD-MATCH' (([regex]::Matches($mutLog, '(?m)^T\d+ PRE .* head-match=true find-hits=1')).Count -eq 14) '14/14 PRE com hash = blob do commit 61c12ce (head-match=true)'

$notRed = @()
foreach ($id in $ids) {
    # S-22: the wrapper console can misreport (it prints 3 for a real red); logExit is the truth.
    $m = [regex]::Match($mutLog, ("(?m)^{0} RUN wrapper=\d+ logExit=1 aprovado=\d+ falha=(\d+)" -f [regex]::Escape($id)))
    if (-not $m.Success -or [int]$m.Groups[1].Value -lt 1) { $notRed += $id }
}
Check 'MUT-RED' ($notRed.Count -eq 0) $(if ($notRed.Count -eq 0) { 'T1..T14 todos vermelhos DENTRO do log (logExit: 1, falha >= 1)' } else { 'sem vermelho: ' + ($notRed -join ',') })

# Each cycle's primary EXPECT anchor must appear in that cycle's RED-AC line: the AC scenario
# that claims the broken behaviour is the one that failed (not some neighbouring suite).
$expectOf = @{
    'T1'  = 'Ac01_a_500_goes_back_to_pending_with_attempts_and_a_growing_backoff'
    'T2'  = 'Ac01_a_500_goes_back_to_pending_with_attempts_and_a_growing_backoff'
    'T3'  = 'Ac02_after_eight_attempts_the_delivery_hits_the_dlq_with_motive_and_alert'
    'T4'  = 'Ac02_after_eight_attempts_the_delivery_hits_the_dlq_with_motive_and_alert'
    'T5'  = 'Ac02_a_permanent_404_dead_letters_the_delivery_on_the_first_attempt'
    'T6'  = 'Ac02_a_permanent_404_dead_letters_the_delivery_on_the_first_attempt'
    'T7'  = 'Ac03_an_open_circuit_pushes_a_new_delivery_straight_to_pending'
    'T8'  = 'Backoff_doubles_with_jitter_and_hits_the_six_hour_ceiling_exactly'
    'T9'  = 'Ac04_a_blocked_peer_receives_nothing'
    'T10' = 'Ac04_the_per_pair_rate_limit_decides_who_leaves'
    'T11' = 'Ac01_a_500_goes_back_to_pending_with_attempts_and_a_growing_backoff'
    'T12' = 'Ac05_the_timeout_scenario_fails_the_send_and_keeps_the_row_scheduled'
    'T13' = 'Ac05_the_slow_scenario_loses_to_the_send_budget'
    'T14' = 'Ac06_every_send_carries_the_peer_domain_trace_attribute_without_pii'
}
function Catch-Ac([string]$id) {
    $expect = $expectOf[$id]
    $acM = [regex]::Match($mutLog, ("(?m)^{0} RED-AC (?<a>.*)$" -f [regex]::Escape($id)))
    $ac = if ($acM.Success) { $acM.Groups['a'].Value.Trim() } else { '' }
    $ok = ($expect -ne '' -and $ac -ne '' -and $ac -ne 'NONE' -and ($ac -match [regex]::Escape($expect)))
    Check ("CATCH-$id") $ok $(if ($ok) { "RED-AC contem: $expect" } else { "esperado '$expect' em RED-AC '$ac'" })
}
foreach ($id in $ids) { Catch-Ac $id }
Check 'MUT-EXPECT' (([regex]::Matches($mutLog, '(?m)^T\d+ EXPECT-OK ')).Count -eq 14 -and ($mutLog -notmatch 'EXPECT-MISS')) '14/14 EXPECT-OK e nenhum EXPECT-MISS'
Check 'MUT-TOOLING' (-not ($mutLog -match 'TOOLING-FAIL|BUILD-FAIL')) 'nenhum TOOLING-FAIL ou BUILD-FAIL (todo vermelho e falha de produto)'

$restoreOk = ([regex]::Matches($mutLog, 'RESTORE-OK hash=PRE residue=0')).Count
Check 'MUT-RESTORE' ($restoreOk -ge 14) ("RESTORE-OK = {0} (esperado >= 14: catorze ciclos can-fail)" -f $restoreOk)
Check 'MUT-RESTOREFAIL' (-not ($mutLog -match 'RESTORE-FAIL')) 'nenhum RESTORE-FAIL'
Check 'MUT-GREEN' ($greenOk) 'GREEN 51/51 sobre as fontes restauradas (wrapper=0 logExit=0 aprovado=51 falha=0)'
Check 'MUT-SUMMARY' ($mutLog -match 'SUMMARY cycles=14 failed=0 residue-backend=0') 'SUMMARY cycles=14 failed=0 residue-backend=0'
Check 'MUT-UNTRACKED' ($mutLog -match 'UNTRACKED backend=0') 'UNTRACKED backend=0 (a QA nao acrescentou arquivos em backend)'

# 7. Residue: no mutant token left in the product sources, no temp junk, git backend clean.
$hitsResidue = 0
foreach ($file in @(Get-ChildItem -LiteralPath (Join-Path $repo 'backend') -Recurse -File -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })) {
    $hitsResidue += @(Select-String -LiteralPath $file.FullName -SimpleMatch -Pattern 'QA-0046 T' -ErrorAction SilentlyContinue).Count
}
Check 'RESIDUE' ($hitsResidue -eq 0) ("tokens mutantes em backend (.cs, fora de bin/obj) = {0}" -f $hitsResidue)
$backups = @(Get-ChildItem -Path $env:TEMP -Filter 'qa0046-*' -File -ErrorAction SilentlyContinue)
Check 'TEMP-JUNK' ($backups.Count -eq 0) ("arquivos qa0046-* no TEMP = {0}" -f $backups.Count)
$backendStatus = @(git -C $repo status --porcelain -uall -- backend)
Check 'GIT-BACKEND' ($backendStatus.Count -eq 0) ("git status backend = {0} entradas (esperado 0)" -f $backendStatus.Count)

# 8. Scope (AC-07): both scans prove blob identity, mtime, residue and touches.
$sc = Read-IfPresent (Join-Path $PSScriptRoot 'QA-0046-scope.log')
Check 'SCOPE-BLOB' (([regex]::Matches($sc, 'match=TRUE')).Count -eq 4 -and $sc -match 'blob-identity=4 of 4') 'blob-identity work=HEAD em 4/4 arquivos de produto mutados (git hash-object vs rev-parse)'
Check 'SCOPE-MTIME' ($sc -match 'mtime-count=4 \(esperado 4') 'mtime desde a baseline = exatamente os 4 arquivos mutados/restaurados (S-23)'
Check 'SCOPE-RESIDUE' ($sc -match 'residue=0') 'scope scan: residuo mutante = 0'
Check 'SCOPE-TOUCHES' ($sc -match 'fora-dos-touches=0') 'scope scan: nenhum arquivo fora dos touches alterado'
Check 'SCOPE-BACKEND' ($sc -match 'git-backend-entries=0') 'git status backend = 0 entradas'
Check 'SCOPE-SUMMARY' ($sc -match 'SUMMARY commit-files=18 qa-artifacts=\d+ backend-entries=0 residue=0 blob=4/4 mtime=4 fails=0') 'scope SUMMARY fails=0'
$scSh = Read-IfPresent (Join-Path $PSScriptRoot 'QA-0046-scope-sh.log')
Check 'SCOPE-SH' ($scSh -match 'fora-dos-touches=0' -and $scSh -match 'blob-identity=4 of 4' -and $scSh -match 'residue=0' -and $scSh -match 'git-backend-entries=0') 'gêmeo bash do scope: fora-dos-touches=0, blob 4/4, residuo 0'

# 9. The scenario set the runs above executed (static markers) + the product constants of the ACs.
$motor = Read-IfPresent (Join-Path $repo 'backend\tests\Squid.Tests\Federation\PeerDeliveryMotorTests.cs')
$acMissing = @()
foreach ($n in 'AC-01', 'AC-02', 'AC-03', 'AC-04', 'AC-05', 'AC-06') {
    if ($motor -notmatch ('// ' + [regex]::Escape($n))) { $acMissing += $n }
}
Check 'TEST-MOTOR' ($acMissing.Count -eq 0 -and ([regex]::Matches($motor, '\[Fact\]')).Count -eq 11) $(if ($acMissing.Count -eq 0) { 'PeerDeliveryMotorTests: 11 [Fact] marcados // AC-01..// AC-06' } else { 'faltando: ' + ($acMissing -join ',') })
$grain = Read-IfPresent (Join-Path $repo 'backend\tests\Squid.Tests\Federation\PeerGrainTests.cs')
Check 'TEST-GRAIN' (([regex]::Matches($grain, '\[Fact\]')).Count -eq 4) 'PeerGrainTests: 4 [Fact] (backoff/circuito/bloqueio/cota)'
$grainSrc = Read-IfPresent (Join-Path $repo 'backend\src\Squid.Grains\Peers\PeerGrain.cs')
Check 'SPEC-CONSTANTS' ($grainSrc -match 'MaxAttempts = 8' -and $grainSrc -match 'BackoffCap = TimeSpan\.FromHours\(6\)') 'constantes do produto: N=8 e teto 6 h (data-model 3.4)'
$stateSrc = Read-IfPresent (Join-Path $repo 'backend\src\Squid.Grains\Peers\PeerState.cs')
Check 'SPEC-RATELIMIT' ($stateSrc -match 'RateLimitPerMinute') 'o par carrega RateLimitPerMinute (data-model 2.9)'
$scenarioSrc = Read-IfPresent (Join-Path $repo 'backend\src\Squid.PeerClient\PeerScenarioServer.cs')
$cliSrc = Read-IfPresent (Join-Path $repo 'backend\src\Squid.PeerClient\Program.cs')
Check 'PEERCLIENT-SCENARIOS' ($scenarioSrc -match 'PeerScenario\.Timeout' -and $scenarioSrc -match 'PeerScenario\.Slow' -and $scenarioSrc -match '500 Internal Server Error' -and $cliSrc -match 'case "timeout"' -and $cliSrc -match 'case "slow"') 'PeerClient expoe 500/timeout/slow (enum + CLI)'

# 10. The flow state machine accepts the task file untouched by QA (exit 0).
$state = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'docs\sdd\_tools\sdd-state.ps1') check $task 2>&1 | Out-String
Check 'SDD-STATE' ($LASTEXITCODE -eq 0) (($state -replace '\s+', ' ').Trim())

# 11. The QA doc exists with the verdict this checker is verifying, and all seven ACs in it.
$dt = Read-IfPresent (Join-Path $PSScriptRoot 'QA-0046-peer-grain-fila-e-dlq.md')
Check 'QA-DOC' ($dt -match '(?m)^- verdict: \**PASSED') 'QA-0046-peer-grain-fila-e-dlq.md com verdict PASSED'
$docAcMissing = @()
foreach ($n in 'AC-01', 'AC-02', 'AC-03', 'AC-04', 'AC-05', 'AC-06', 'AC-07') {
    if ($dt -notmatch [regex]::Escape("| $n |")) { $docAcMissing += $n }
}
Check 'QA-DOC-ACS' ($docAcMissing.Count -eq 0) $(if ($docAcMissing.Count -eq 0) { 'AC-01..AC-07 presentes na tabela do doc' } else { 'faltando: ' + ($docAcMissing -join ',') })

Write-Output "SUMMARY total=$total fails=$fails"
if ($fails -gt 0) { exit 1 }
exit 0
