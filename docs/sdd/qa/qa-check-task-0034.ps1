# QA-0034 evidence checker (static; no tests re-run). Every exit and summary is read from the
# LOG FILES (S-22 - never from a wrapper's console code), every restore from the recorded hashes
# (S-16) and the final tree from git + an mtime scan (S-23 companions).
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0034.ps1
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$runs = Join-Path $repo 'docs\sdd\runs\test-runs'
$task = Join-Path $repo 'docs\sdd\tasks\TASK-0034-envio-e-leitura.md'
$total = 0; $fails = 0

function Check([string]$name, [bool]$ok, [string]$detail) {
    $script:total++
    if ($ok) { Write-Output "CHECK $name OK - $detail" }
    else { $script:fails++; Write-Output "CHECK $name FAIL - $detail" }
}

function Newest([string]$filter) {
    Get-ChildItem -LiteralPath $runs -Filter $filter | Sort-Object Name | Select-Object -Last 1
}

# 1. The AC-05 gate: exit header + summary INSIDE the newest gate log (S-22), 443/443.
$gate = Newest '*-test-gate.log'
$gt = Get-Content -LiteralPath $gate.FullName -Raw
Check 'GATE-NAME' ($gate.Name -eq '20261112-025653-test-gate.log') ("{0} e o gate desta QA" -f $gate.Name)
Check 'GATE-EXIT' ($gt -match '(?m)^exit:\s+0\s*$') ("{0} header exit 0" -f $gate.Name)
Check 'GATE-SUMMARY' ($gt -match 'Com falha:\s*0,\s*Aprovado:\s*443,\s*Ignorado:\s*0,\s*Total:\s*443') ("{0} Com falha 0 / Aprovado 443 / Total 443" -f $gate.Name)

# 2. Baseline 11/11 and the two post-restore GREENs of the mutation driver (S-22).
$base = Join-Path $runs '20261112-023919-test-filter.log'
$bt = Get-Content -LiteralPath $base -Raw
Check 'BASELINE' (($bt -match '(?m)^exit:\s+0\s*$') -and ($bt -match 'Com falha:\s*0,\s*Aprovado:\s*11')) 'baseline 023919 exit 0 / 11 de 11'
$greenOk = 0
foreach ($name in '20261112-024603-test-filter.log', '20261112-025001-test-filter.log') {
    $p = Join-Path $runs $name
    if ((Test-Path -LiteralPath $p)) {
        $t = Get-Content -LiteralPath $p -Raw
        if (($t -match '(?m)^exit:\s+0\s*$') -and ($t -match 'Com falha:\s*0,\s*Aprovado:\s*11')) { $greenOk++ }
    }
}
Check 'GREENS' ($greenOk -eq 2) "GREENs 11/11 pos-restore = $greenOk de 2 (024603, 025001)"

# 3. The mutation ledger (S-16/S-22/S-23): every cycle ran RED (classified in its log), every
#    restore came back to PRE, one documented classification anomaly (M12 v1) and clean summaries.
$mutPath = Join-Path $PSScriptRoot 'QA-0034-mutations.log'
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
Check 'MUT-RESTORE' ($restoreOk -ge 14) ("RESTORE-OK = {0} (esperado >= 14: 13 ciclos + M12 v2)" -f $restoreOk)
Check 'MUT-RESTOREFAIL' (-not ($mut -match 'RESTORE-FAIL')) 'nenhum RESTORE-FAIL'
$greens = ([regex]::Matches($mut, 'GREEN wrapper=0 logExit=0 aprovado=11 falha=0')).Count
Check 'MUT-GREEN' ($greens -ge 2) ("GREEN 11/11 = {0} (fim do driver cheio + fim da re-execucao M12)" -f $greens)
Check 'MUT-ANOMALIA' (([regex]::Matches($mut, 'CLASSIFICACAO-ERRADA')).Count -eq 1) 'exatamente 1 CLASSIFICACAO-ERRADA (M12 v1 sobreviveu - ramo que as 4 requests nao alcanca; mutante corrigido e re-executado)'
Check 'MUT-SUMMARY-FULL' ($mut -match 'SUMMARY cycles=13 failed=1 residue-QA-MUTATION=0 temp-backups=0') 'SUMMARY da execucao cheia: cycles=13 failed=1 (o 1 = M12 v1), residue 0'
Check 'MUT-SUMMARY-LAST' ($mut -match 'SUMMARY cycles=1 failed=0 residue-QA-MUTATION=0 temp-backups=0') 'SUMMARY final da re-execucao: cycles=1 failed=0, residue 0'
$m3run = [regex]::Match($mut, '(?m)^M3 RUN .* constraint=(\w+)')
Check 'MUT-M3-RUN' ($m3run.Success) ("M3 RUN registrado com constraint={0} (o texto UNIQUE nao sobe ao output do host - prova nominal fica no probe)" -f $m3run.Groups[1].Value)

# 4. The UNIQUE probe: pristine green, 0003 without UNIQUE red, restore, backend clean (S-22).
$probePath = Join-Path $PSScriptRoot 'QA-0034-unique-probe.log'
$pr = Get-Content -LiteralPath $probePath -Raw
Check 'PROBE-PRISTINE' ($pr -match 'PRISTINE wrapper=0 logExit=0 aprovado=1 falha=0') 'schema original: probe verde (UNIQUE recusa a duplicata)'
Check 'PROBE-MUTATED' ($pr -match 'MUTATED wrapper=1 logExit=1 aprovado=0 falha=1') '0003 sem UNIQUE: mesmo probe vermelho (can-fail provado)'
Check 'PROBE-MUTATED-RED' ($pr -match 'MUTATED RED Squid\.Tests\.Threads\.QaTask0034UniqueProbeTests') 'vermelho nomeado no proprio teste do probe'
Check 'PROBE-RESTORE' ($pr -match 'RESTORE-OK hash=PRE residue=0') '0003 restaurado byte-exact (S-16/S-23)'
Check 'PROBE-GIT' ($pr -match 'GIT-BACKEND entradas=0') 'arquivo temporario do probe removido, git backend limpo'
Check 'PROBE-SUMMARY' ($pr -match 'SUMMARY pristine=green mutated=red restore=OK testfile-removed=yes git-backend=0 fails=0') 'SUMMARY do probe: fails=0'

# 5. Residue: no QA-MUTATION marker left in the backend (cs + sql, no bin/obj), no temp backup,
#    the probe's temporary test file is gone.
$residue = Get-ChildItem -LiteralPath (Join-Path $repo 'backend') -Recurse -File -Include '*.cs', '*.sql' |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    Select-String -Pattern 'QA-MUTATION' -SimpleMatch
Check 'RESIDUE' (@($residue).Count -eq 0) ("QA-MUTATION em backend = {0}" -f @($residue).Count)
$backups = @(Get-ChildItem -Path $env:TEMP -Filter 'qa0034-*' -File -ErrorAction SilentlyContinue)
Check 'TEMP-BACKUPS' ($backups.Count -eq 0) ("backups qa0034-* no TEMP = {0}" -f $backups.Count)
Check 'PROBE-FILE' (-not (Test-Path -LiteralPath (Join-Path $repo 'backend\tests\Squid.Tests\Threads\QaTask0034UniqueProbeTests.cs'))) 'QaTask0034UniqueProbeTests.cs ausente da arvore'

# 6. Scope (AC-05): git status backend empty; the scope log records blob-identity with HEAD for
#    the three mutated files, mtime since the QA baseline = exactly those three, residue zero.
$status = @(git -C $repo status --porcelain -uall -- backend)
Check 'GIT-BACKEND' ($status.Count -eq 0) ("git status backend = {0} entradas" -f $status.Count)
$scopePath = Join-Path $PSScriptRoot 'QA-0034-scope.log'
$sc = Get-Content -LiteralPath $scopePath -Raw
Check 'SCOPE-BLOB' (([regex]::Matches($sc, 'match=True')).Count -eq 3) 'blob-identity work=HEAD em 3/3 arquivos mutados (git hash-object vs rev-parse)'
Check 'SCOPE-MTIME' ($sc -match 'mtime-count=3 \(esperado 3\)') 'mtime desde a baseline = exatamente os 3 arquivos mutados'
Check 'SCOPE-RESIDUE' ($sc -match 'residue=0') 'scope scan: residuo QA-MUTATION = 0'

# 7. The flow state machine accepts the task file with the QA's appended lines (exit 0).
$state = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'docs\sdd\_tools\sdd-state.ps1') check $task 2>&1 | Out-String
Check 'SDD-STATE' ($LASTEXITCODE -eq 0) (($state -replace '\s+', ' ').Trim())

# 8. The QA doc exists with the verdict this checker is verifying.
$doc = Join-Path $PSScriptRoot 'QA-0034-envio-e-leitura.md'
Check 'QA-DOC' ((Test-Path -LiteralPath $doc) -and ((Get-Content -LiteralPath $doc -Raw) -match '(?m)^- verdict: \**PASSED')) 'QA-0034-envio-e-leitura.md com verdict PASSED'

Write-Output "SUMMARY total=$total fails=$fails"
if ($fails -gt 0) { exit 1 }
exit 0
