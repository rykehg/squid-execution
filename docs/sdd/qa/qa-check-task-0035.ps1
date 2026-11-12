# QA-0035 evidence checker (static; no tests re-run). Every exit and summary is read from the
# LOG FILES (S-22 - never from a wrapper's console code), every restore from the recorded hashes
# (S-16) and the final tree from git + an mtime scan (S-23 companions).
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0035.ps1
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$runs = Join-Path $repo 'docs\sdd\runs\test-runs'
$task = Join-Path $repo 'docs\sdd\tasks\TASK-0035-participantes-e-admin.md'
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

# 1. The AC-06 gate: exit header + summary INSIDE this QA's gate log (S-22), 450/450.
$gate = Newest '*-test-gate.log'
$gt = Get-Content -LiteralPath $gate.FullName -Raw
Check 'GATE-NAME' ($gate.Name -eq '20261112-041813-test-gate.log') ("{0} e o gate desta QA" -f $gate.Name)
Check 'GATE-EXIT' ($gt -match '(?m)^exit:\s+0\s*$') ("{0} header exit 0" -f $gate.Name)
Check 'GATE-SUMMARY' ($gt -match 'Com falha:\s*0,\s*Aprovado:\s*450,\s*Ignorado:\s*0,\s*Total:\s*450') ("{0} Com falha 0 / Aprovado 450 / Total 450" -f $gate.Name)

# 2. Baseline 18/18 and the green run that carried the QA's temporary test (19/19).
Log-Ok 'BASELINE' '20261112-035540-test-filter.log' '(?m)^exit:\s+0\s*$[\s\S]*Com falha:\s*0,\s*Aprovado:\s*18'
Log-Ok 'TEMPTEST-GREEN' '20261112-040053-test-filter.log' '(?m)^exit:\s+0\s*$[\s\S]*Com falha:\s*0,\s*Aprovado:\s*19'

# 3. The mutation ledger (S-16/S-22/S-23): every cycle ran RED (classified in its log), every
#    restore came back to PRE, one documented classification anomaly (M4 v1 build) + green runs.
$mutPath = Join-Path $PSScriptRoot 'QA-0035-mutations.log'
$mut = Get-Content -LiteralPath $mutPath -Raw
$missing = @()
$notRed = @()
foreach ($id in 'M1', 'M2', 'M3', 'M4', 'M5', 'M6', 'M7', 'M8', 'M9', 'M10', 'M11', 'M12', 'M13') {
    if ($mut -notmatch [regex]::Escape("$id PRE ")) { $missing += $id }
    $runsOfId = [regex]::Matches($mut, ("(?m)^{0} RUN wrapper=\d+ logExit=\d+ aprovado=\d+ falha=(\d+)" -f [regex]::Escape($id)))
    $red = $false
    foreach ($m in $runsOfId) { if ([int]$m.Groups[1].Value -ge 1) { $red = $true } }
    if (-not $red) { $notRed += $id }
}
Check 'MUT-IDS' ($missing.Count -eq 0) $(if ($missing.Count -eq 0) { 'M1..M13 todos com linha PRE' } else { 'faltando: ' + ($missing -join ',') })
Check 'MUT-RED' ($notRed.Count -eq 0) $(if ($notRed.Count -eq 0) { 'M1..M13 todos com RUN vermelho classificado no log' } else { 'sem vermelho: ' + ($notRed -join ',') })
$restoreOk = ([regex]::Matches($mut, 'RESTORE-OK hash=PRE residue=0')).Count
Check 'MUT-RESTORE' ($restoreOk -ge 14) ("RESTORE-OK = {0} (esperado >= 14: 13 ciclos + M4 v2)" -f $restoreOk)
Check 'MUT-RESTOREFAIL' (-not ($mut -match 'RESTORE-FAIL')) 'nenhum RESTORE-FAIL'
$greens = ([regex]::Matches($mut, 'GREEN wrapper=0 logExit=0 aprovado=19 falha=0')).Count
Check 'MUT-GREEN' ($greens -ge 2) ("GREEN 19/19 = {0} (fim do driver cheio + fim da re-execucao M4)" -f $greens)
Check 'MUT-ANOMALIA' (([regex]::Matches($mut, 'CLASSIFICACAO-ERRADA')).Count -eq 1) 'exatamente 1 CLASSIFICACAO-ERRADA (M4 v1 quebrou o build do mutante CS1026 - S-22 classificou em vez de ondular; mutante corrigido e re-executado)'
Check 'MUT-SUMMARY-FULL' ($mut -match 'SUMMARY cycles=13 failed=1 residue-QA-MUTATION=0 temp-backups=0') 'SUMMARY da execucao cheia: cycles=13 failed=1 (o 1 = M4 v1), residue 0'
Check 'MUT-SUMMARY-LAST' ($mut -match 'SUMMARY cycles=1 failed=0 residue-QA-MUTATION=0 temp-backups=0') 'SUMMARY final da re-execucao: cycles=1 failed=0, residue 0'
$m4run = [regex]::Match($mut, '(?m)^M4 RUN wrapper=1 logExit=1 aprovado=16 falha=3 ')
Check 'MUT-M4V2' ($m4run.Success) 'M4 v2 vermelho registrado (aprovado 16 / falha 3, log 041039)'
$m12 = [regex]::Match($mut, '(?m)^M12 RED-IN-LOG (?<r>.*)$')
Check 'MUT-M12-CATCH' ($m12.Success -and $m12.Groups['r'].Value -match 'Adding_a_seat' -and $m12.Groups['r'].Value -match 'A_seat_added_later') 'M12 (semi-join de volta ao bug) pego pelos testes do DEV: Adding_a_seat + A_seat_added_later'
$m3qa = [regex]::Match($mut, '(?m)^M3 RED-IN-LOG (?<r>.*)$')
Check 'MUT-M3-QATEST' ($m3qa.Success -and $m3qa.Groups['r'].Value -match 'QaTask0035RemovedRoutesTests') 'M3 vermelho inclui o teste temporario da QA (can-fail do teste novo)'

# 4. Residue: no QA-MUTATION marker left in the backend (cs + sql, no bin/obj), no temp backup,
#    the temporary QA test file is gone.
$residue = Get-ChildItem -LiteralPath (Join-Path $repo 'backend') -Recurse -File -Include '*.cs', '*.sql' |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    Select-String -Pattern 'QA-MUTATION' -SimpleMatch
Check 'RESIDUE' (@($residue).Count -eq 0) ("QA-MUTATION em backend = {0}" -f @($residue).Count)
$backups = @(Get-ChildItem -Path $env:TEMP -Filter 'qa0035-*' -File -ErrorAction SilentlyContinue)
Check 'TEMP-BACKUPS' ($backups.Count -eq 0) ("backups qa0035-* no TEMP = {0}" -f $backups.Count)
Check 'TEMP-FILE' (-not (Test-Path -LiteralPath (Join-Path $repo 'backend\tests\Squid.Tests\Threads\QaTask0035RemovedRoutesTests.cs'))) 'QaTask0035RemovedRoutesTests.cs ausente da arvore'

# 5. The driver keeps its UTF-8 BOM (the QA-0034 lesson, repeated in this QA after an edit).
$bom = [System.IO.File]::ReadAllBytes((Join-Path $PSScriptRoot 'qa-mutate-task-0035.ps1'))[0..2]
Check 'DRIVER-BOM' ($bom[0] -eq 0xEF -and $bom[1] -eq 0xBB -and $bom[2] -eq 0xBF) 'qa-mutate-task-0035.ps1 com UTF-8 BOM'

# 6. Scope (AC-06): git status backend empty; blob-identity with HEAD for both mutated files,
#    mtime since the QA baseline = exactly those two, residue zero, temp test gone.
$status = @(git -C $repo status --porcelain -uall -- backend)
Check 'GIT-BACKEND' ($status.Count -eq 0) ("git status backend = {0} entradas" -f $status.Count)
$scopePath = Join-Path $PSScriptRoot 'QA-0035-scope.log'
$sc = Get-Content -LiteralPath $scopePath -Raw
Check 'SCOPE-BLOB' (([regex]::Matches($sc, 'match=True')).Count -eq 2) 'blob-identity work=HEAD em 2/2 arquivos mutados (git hash-object vs rev-parse)'
Check 'SCOPE-MTIME' ($sc -match 'mtime-count=2 \(esperado 2') 'mtime desde a baseline = exatamente os 2 arquivos mutados (restaurados)'
Check 'SCOPE-RESIDUE' ($sc -match 'residue=0') 'scope scan: residuo QA-MUTATION = 0'
Check 'SCOPE-TEMPFILE' ($sc -match 'temp-test-file-present=False') 'scope scan: teste temporario da QA ausente'

# 7. The flow state machine accepts the task file with the QA's appended lines (exit 0).
$state = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'docs\sdd\_tools\sdd-state.ps1') check $task 2>&1 | Out-String
Check 'SDD-STATE' ($LASTEXITCODE -eq 0) (($state -replace '\s+', ' ').Trim())

# 8. The QA doc exists with the verdict this checker is verifying.
$doc = Join-Path $PSScriptRoot 'QA-0035-participantes-e-admin.md'
Check 'QA-DOC' ((Test-Path -LiteralPath $doc) -and ((Get-Content -LiteralPath $doc -Raw) -match '(?m)^- verdict: \**PASSED')) 'QA-0035-participantes-e-admin.md com verdict PASSED'

Write-Output "SUMMARY total=$total fails=$fails"
if ($fails -gt 0) { exit 1 }
exit 0
