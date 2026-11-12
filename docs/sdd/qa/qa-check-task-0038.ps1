# QA-0038 evidence checker (static; no tests re-run). Every exit and summary is read from the
# LOG FILES (S-22 - never from a wrapper's console code), every restore from the recorded hashes
# (S-16) and the final tree from git + an mtime scan (S-23 companions).
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0038.ps1
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$runs = Join-Path $repo 'docs\sdd\runs\test-runs'
$task = Join-Path $repo 'docs\sdd\tasks\TASK-0038-reacao-whitelist.md'
$total = 0; $fails = 0

function Check([string]$name, [bool]$ok, [string]$detail) {
    $script:total++
    if ($ok) { Write-Output "CHECK $name OK - $detail" }
    else { $script:fails++; Write-Output "CHECK $name FAIL - $detail" }
}

function Newest([string]$filter) {
    Get-ChildItem -LiteralPath $runs -Filter $filter | Sort-Object Name | Select-Object -Last 1
}

function Log-Ok([string]$name, [string]$file, [string]$pattern) {
    $p = Join-Path $runs $file
    $ok = (Test-Path -LiteralPath $p) -and ((Get-Content -LiteralPath $p -Raw) -match $pattern)
    Check $name $ok $file
}

# 1. The git baseline this QA captured before writing anything (HEAD of the DEV commit).
$base = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'QA-0038-git-baseline.txt') -Raw
Check 'BASELINE-HEAD' ($base -match 'HEAD: 8903e675c5f9a3855f7e87ebca4dd12202886edb') 'baseline HEAD = 8903e67'
Check 'BASELINE-BRANCH' ($base -match 'branch: task/TASK-0038-reacao-whitelist') 'branch task/TASK-0038-reacao-whitelist'
Check 'BASELINE-WSL' ($base -match 'dotnet-missing') 'dotnet ausente no WSL registrado na baseline (suite Linux = unverified)'

# 2. The AC-05 gate: exit header + summary INSIDE this QA's gate log (S-22), 468/468, and that
#    this gate is NEWER than the baseline capture (it is the one this QA ran, not DEV's old one).
$gate = Newest '*-test-gate.log'
$gt = Get-Content -LiteralPath $gate.FullName -Raw
Check 'GATE-AGE' ($gate.LastWriteTime -gt (Get-Date '2026-11-12 07:52:00')) ("{0} criado depois da baseline 07:52" -f $gate.Name)
Check 'GATE-EXIT' ($gt -match '(?m)^exit:\s+0\s*$') ("{0} header exit 0" -f $gate.Name)
Check 'GATE-SUMMARY' ($gt -match 'Com falha:\s*0,\s*Aprovado:\s*468,\s*Ignorado:\s*0,\s*Total:\s*468') ("{0} Com falha 0 / Aprovado 468 / Total 468" -f $gate.Name)

# 3. Baseline filtered run of this QA (36/36) and the green WITH the QA's temporary test (38/38).
Log-Ok 'BASELINE-FILTER' '20261112-075557-test-filter.log' '(?m)^exit:\s+0\s*$[\s\S]*Com falha:\s*0,\s*Aprovado:\s*36'
Log-Ok 'QA-TEMP-FILTER' '20261112-080317-test-filter.log' '(?m)^exit:\s+0\s*$[\s\S]*Com falha:\s*0,\s*Aprovado:\s*38'

# 4. The mutation ledger (S-16/S-22/S-23): every cycle ran RED (classified in its log), every
#    restore came back to PRE, no classification anomaly, greens over the restored sources, and
#    both SUMMARY lines (the full run of 11 cycles + the corrected M4 re-run).
$mutPath = Join-Path $PSScriptRoot 'QA-0038-mutations.log'
$mut = Get-Content -LiteralPath $mutPath -Raw
$missing = @()
$notRed = @()
foreach ($id in 'M1', 'M2', 'M3', 'M4', 'M5', 'M6', 'M7', 'M8', 'M9', 'M10', 'M11') {
    if ($mut -notmatch [regex]::Escape("$id PRE ")) { $missing += $id }
    $runsOfId = [regex]::Matches($mut, ("(?m)^{0} RUN wrapper=\d+ logExit=\d+ aprovado=\d+ falha=(\d+)" -f [regex]::Escape($id)))
    $red = $false
    foreach ($m in $runsOfId) { if ([int]$m.Groups[1].Value -ge 1) { $red = $true } }
    if (-not $red) { $notRed += $id }
}
Check 'MUT-IDS' ($missing.Count -eq 0) $(if ($missing.Count -eq 0) { 'M1..M11 todos com linha PRE' } else { 'faltando: ' + ($missing -join ',') })
Check 'MUT-RED' ($notRed.Count -eq 0) $(if ($notRed.Count -eq 0) { 'M1..M11 todos com RUN vermelho classificado no log' } else { 'sem vermelho: ' + ($notRed -join ',') })
$restoreOk = ([regex]::Matches($mut, 'RESTORE-OK hash=PRE residue=0')).Count
Check 'MUT-RESTORE' ($restoreOk -ge 14) ("RESTORE-OK = {0} (esperado >= 14: 13 do run cheio + 1 do M4 corrigido)" -f $restoreOk)
Check 'MUT-RESTOREFAIL' (-not ($mut -match 'RESTORE-FAIL')) 'nenhum RESTORE-FAIL'
$greens = ([regex]::Matches($mut, 'GREEN wrapper=0 logExit=0 aprovado=38 falha=0')).Count
Check 'MUT-GREEN' ($greens -ge 2) ("GREEN 38/38 = {0} (fim do run cheio e do re-run do M4)" -f $greens)
Check 'MUT-ANOMALIA' (([regex]::Matches($mut, 'CLASSIFICACAO-ERRADA')).Count -eq 0) 'nenhuma CLASSIFICACAO-ERRADA (S-22)'
Check 'MUT-SUMMARY-FULL' ($mut -match 'SUMMARY cycles=11 failed=0 residue-QA-MUTATION=0 temp-backups=0') 'SUMMARY cycles=11 failed=0 residue=0 temp-backups=0'
Check 'MUT-SUMMARY-M4' ($mut -match 'SUMMARY cycles=1 failed=0 residue-QA-MUTATION=0 temp-backups=0') 'SUMMARY cycles=1 failed=0 (re-run do M4 corrigido)'

function Catch-Red([string]$id, [string]$must1, [string]$must2 = '') {
    $m = [regex]::Match($mut, ("(?m)^{0} RED-IN-LOG (?<r>.*)$" -f [regex]::Escape($id)))
    $r = if ($m.Success) { $m.Groups['r'].Value } else { '' }
    $ok = $m.Success -and $r -match [regex]::Escape($must1)
    if ($must2 -ne '') { $ok = $ok -and ($r -match [regex]::Escape($must2)) }
    Check ("CATCH-$id") $ok ("{0} vermelho com {1}{2}" -f $id, $must1, $(if ($must2) { ' + ' + $must2 } else { '' }))
}

# The dispatch's mutation targets, each proven by the suite that caught it (not by my reading).
Catch-Red 'M1' 'The_same_message_actor_emoji_is_one_row_and_repeating_it_changes_nothing'   # AC-01 idempotencia da trinca
Catch-Red 'M2' 'The_same_message_actor_emoji_is_one_row_and_repeating_it_changes_nothing'   # AC-01 segunda emoji diferente
Catch-Red 'M3' 'An_emoji_outside_Reactions_AllowedEmojis_is_422_and_never_reaches_a_row'    # AC-02 422 invalid_emoji
# M4 (counter column) - the LATEST M4 RED-IN-LOG is the corrected run: only the migration test.
$m4red = ([regex]::Matches($mut, "(?m)^M4 RED-IN-LOG (?<r>.*)$") | Select-Object -Last 1)
$m4ok = $m4red.Success -and $m4red.Groups['r'].Value -match 'ThreadMigrationTests' -and
    $mut -match '(?m)^M4 RUN wrapper=1 logExit=1 aprovado=37 falha=1'
Check 'CATCH-M4' $m4ok 'M4 (contador armazenado VALIDO) vermelho so na migration 0006, rotas verdes (37/1)'
Catch-Red 'M5' 'A_stranger_gets_one_404_body_on_read_and_on_both_reaction_routes'           # AC-04 escada 404 antes de 422
# CATCH-M6 reads M6's own RUN LOG (S-22): the Fact A name is 90 chars and xUnit wrapped it
# inside the [FAIL] line, and the driver keeps only the first 8 failing lines of a run - the
# summary line "Com falha ... <full name>" in the raw log is the contiguous proof.
$m6run = [regex]::Match($mut, '(?m)^M6 RUN .*log=(\S+)')
$m6logName = if ($m6run.Success) { $m6run.Groups[1].Value } else { '' }
$m6text = if ($m6logName -and $m6logName -ne '(nenhum') { Get-Content -LiteralPath (Join-Path $runs $m6logName) -Raw } else { '' }
Check 'CATCH-M6' (($mut -match '(?m)^M6 RED-IN-LOG ') -and ($m6text -match 'Com falha Squid\.Tests\.Threads\.QaTask0038ReactionRereadTests\.Mutating_the_config_source_changes_the_accepted_set_without_a_restart')) ("M6 (cache de config) pego pelo Fact A no log {0}" -f $m6logName)
Catch-Red 'M7' 'Deleting_takes_the_actor_row_the_count_follows_the_rows_and_a_missing_one_is_404' # AC-03 so a linha do ator
Catch-Red 'M8' 'A_stranger_gets_one_404_body_on_read_and_on_both_reaction_routes'           # AC-04 parede de participantes
Catch-Red 'M9' 'ThreadReactionRoutesTests'                                                  # AC-04 byte-idade com traceId
$m10d = ([regex]::Match($mut, "(?m)^M10 RED-DETAIL (?<d>.*)$")).Groups['d'].Value
$m11d = ([regex]::Match($mut, "(?m)^M11 RED-DETAIL (?<d>.*)$")).Groups['d'].Value
Check 'CATCH-M10' (($mut -match '(?m)^M10 RED-IN-LOG .*The_reaction_ladder_answers_401_first_and_GET_stays_the_shared_404') -and ($m10d -match 'Expected: NotFound')) 'M10 (GET em .../reactions aberto) pego pelo Fact B: Expected NotFound'
Check 'CATCH-M11' (($mut -match '(?m)^M11 RED-IN-LOG .*The_reaction_ladder_answers_401_first_and_GET_stays_the_shared_404') -and ($m11d -match 'Expected: Unauthorized')) 'M11 (401 virou 404) pego pelo Fact B: Expected Unauthorized'

# 5. Residue: no QA-MUTATION marker left in the backend (cs + sql, no bin/obj), no temp backup.
$residue = Get-ChildItem -LiteralPath (Join-Path $repo 'backend') -Recurse -File -Include '*.cs', '*.sql' |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    Select-String -Pattern 'QA-MUTATION' -SimpleMatch
Check 'RESIDUE' (@($residue).Count -eq 0) ("QA-MUTATION em backend = {0}" -f @($residue).Count)
$backups = @(Get-ChildItem -Path $env:TEMP -Filter 'qa0038-*' -File -ErrorAction SilentlyContinue)
Check 'TEMP-BACKUPS' ($backups.Count -eq 0) ("backups qa0038-* no TEMP = {0}" -f $backups.Count)

# 6. The driver keeps its UTF-8 BOM (the QA-0034..0037 lesson).
$bom = [System.IO.File]::ReadAllBytes((Join-Path $PSScriptRoot 'qa-mutate-task-0038.ps1'))[0..2]
Check 'DRIVER-BOM' ($bom[0] -eq 0xEF -and $bom[1] -eq 0xBB -and $bom[2] -eq 0xBF) 'qa-mutate-task-0038.ps1 com UTF-8 BOM'

# 7. Scope (AC-05): git status backend EMPTY (the QA's temporary test is GONE from the tree, its
#    source kept as .cs.txt in docs/sdd/qa), and the scope log records blob-identity, mtime,
#    residue and touches.
$backendStatus = @(git -C $repo status --porcelain -uall -- backend)
Check 'GIT-BACKEND' ($backendStatus.Count -eq 0) ("git status backend = {0} entradas" -f $backendStatus.Count)
Check 'TEMP-TEST-GONE' (-not (Test-Path -LiteralPath (Join-Path $repo 'backend\tests\Squid.Tests\Threads\QaTask0038ReactionRereadTests.cs'))) 'teste temporario removido da arvore'
Check 'TEMP-TEST-SOURCE' (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'QaTask0038ReactionRereadTests.cs.txt')) 'fonte do teste temporario preservada em docs/sdd/qa'
$scopePath = Join-Path $PSScriptRoot 'QA-0038-scope.log'
$sc = Get-Content -LiteralPath $scopePath -Raw
Check 'SCOPE-BLOB' (([regex]::Matches($sc, 'match=True')).Count -eq 4) 'blob-identity work=HEAD em 4/4 arquivos mutados (git hash-object vs rev-parse)'
Check 'SCOPE-MTIME' ($sc -match 'mtime-count=4 \(esperado 4') 'mtime desde a baseline = exatamente os 4 arquivos mutados (restaurados)'
Check 'SCOPE-RESIDUE' ($sc -match 'residue=0') 'scope scan: residuo QA-MUTATION = 0'
Check 'SCOPE-TOUCHES' ($sc -match 'fora-dos-touches=0') 'scope scan: nenhum arquivo de produto fora dos touches alterado'

# 8. The flow state machine accepts the task file with the QA's appended lines (exit 0).
$state = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'docs\sdd\_tools\sdd-state.ps1') check $task 2>&1 | Out-String
Check 'SDD-STATE' ($LASTEXITCODE -eq 0) (($state -replace '\s+', ' ').Trim())

# 9. The QA doc exists with the verdict this checker is verifying.
$doc = Join-Path $PSScriptRoot 'QA-0038-reacao-whitelist.md'
Check 'QA-DOC' ((Test-Path -LiteralPath $doc) -and ((Get-Content -LiteralPath $doc -Raw) -match '(?m)^- verdict: \**PASSED')) 'QA-0038-reacao-whitelist.md com verdict PASSED'

Write-Output "SUMMARY total=$total fails=$fails"
if ($fails -gt 0) { exit 1 }
exit 0
