# QA-0033 evidence checker (static; no tests re-run). Every exit and summary is read from the
# LOG FILES (S-22 - never from a wrapper's console code), every restore from the recorded hashes
# (S-16) and the final tree from git + an mtime scan (S-23 companions).
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0033.ps1
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$runs = Join-Path $repo 'docs\sdd\runs\test-runs'
$task = Join-Path $repo 'docs\sdd\tasks\TASK-0033-nucleo-da-thread.md'
$total = 0; $fails = 0

function Check([string]$name, [bool]$ok, [string]$detail) {
    $script:total++
    if ($ok) { Write-Output "CHECK $name OK - $detail" }
    else { $script:fails++; Write-Output "CHECK $name FAIL - $detail" }
}

function Newest([string]$filter) {
    Get-ChildItem -LiteralPath $runs -Filter $filter | Sort-Object Name | Select-Object -Last 1
}

# 1. The AC-05 gate: exit header + summary INSIDE the newest gate log (S-22), 438/438.
$gate = Newest '*-test-gate.log'
$gt = Get-Content -LiteralPath $gate.FullName -Raw
Check 'GATE-EXIT' ($gt -match '(?m)^exit:\s+0\s*$') ("{0} header exit 0" -f $gate.Name)
Check 'GATE-SUMMARY' ($gt -match 'Com falha:\s*0,\s*Aprovado:\s*438,\s*Ignorado:\s*0,\s*Total:\s*438') ("{0} Com falha 0 / Aprovado 438 / Total 438" -f $gate.Name)

# 2. The newest filtered run (the green over restored sources after M10) is green 9/9 (S-22).
$filt = Newest '*-test-filter.log'
$ft = Get-Content -LiteralPath $filt.FullName -Raw
Check 'FILTER-GREEN' (($filt.Name -ge '20261112-012742') -and ($ft -match '(?m)^exit:\s+0\s*$') -and ($ft -match 'Com falha:\s*0,\s*Aprovado:\s*9')) ("{0} exit 0 / 9 de 9" -f $filt.Name)

# 3. The mutation ledger (S-16/S-22/S-23): every cycle restored, no RESTORE-FAIL, both SUMMARY
#    lines residue 0, all eleven ids present, and the one documented classification anomaly.
$mutPath = Join-Path $PSScriptRoot 'QA-0033-mutations.log'
$mut = Get-Content -LiteralPath $mutPath -Raw
$restoreOk = ([regex]::Matches($mut, 'RESTORE-OK hash=PRE residue=0')).Count
Check 'MUT-RESTORE' ($restoreOk -ge 14) ("RESTORE-OK = {0} (esperado >= 14: 12 + M4D re-run + M10)" -f $restoreOk)
Check 'MUT-RESTOREFAIL' (-not ($mut -match 'RESTORE-FAIL')) 'nenhum RESTORE-FAIL'
Check 'MUT-SUMMARYS' (([regex]::Matches($mut, 'SUMMARY cycles=\d+ failed=\d+ residue-QA-MUTATION=0 temp-backups=0')).Count -ge 3) 'toda SUMMARY com residue=0 e temp-backups=0'
$missing = @()
foreach ($id in 'M1','M2','M3','M4A','M4B','M4C','M4D','M5','M6','M7','M8','M9','M10') {
    if ($mut -notmatch [regex]::Escape("$id PRE ")) { $missing += $id }
}
Check 'MUT-IDS' ($missing.Count -eq 0) $(if ($missing.Count -eq 0) { 'M1..M10 todos com linha PRE' } else { 'faltando: ' + ($missing -join ',') })
$greens = ([regex]::Matches($mut, 'GREEN wrapper=0 logExit=0 aprovado=9 falha=0')).Count
Check 'MUT-GREEN' ($greens -ge 3) ("GREEN 9/9 = {0} (fim do driver, apos M4D, apos M10)" -f $greens)
Check 'MUT-ANOMALIA' (([regex]::Matches($mut, 'CLASSIFICACAO-ERRADA')).Count -eq 1) 'exatamente 1 CLASSIFICACAO-ERRADA (M4D quebrou o build - mutante corrigido e re-executado)'

# 4. Residue: no QA-MUTATION marker left in the backend (cs + sql, sem bin/obj).
$residue = Get-ChildItem -LiteralPath (Join-Path $repo 'backend') -Recurse -File -Include '*.cs', '*.sql' |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    Select-String -Pattern 'QA-MUTATION' -SimpleMatch
Check 'RESIDUE' (@($residue).Count -eq 0) ("QA-MUTATION em backend = {0}" -f @($residue).Count)
$backups = @(Get-ChildItem -Path $env:TEMP -Filter 'qa0033-*' -File -ErrorAction SilentlyContinue)
Check 'TEMP-BACKUPS' ($backups.Count -eq 0) ("backups qa0033-* no TEMP = {0}" -f $backups.Count)

# 5. Scope (AC-05): backend byte-identical to HEAD 599d4ac; the five mutated files at their
#    recorded PRE hashes; no backend path anywhere in git status.
$expected = @{
    'backend\src\Squid.Service\SqliteThreadStore.cs'            = '86679F604F0A03A9CA2BA86ADA108AE58E3C6687C8EC32012858057EA31F002A'
    'backend\src\Squid.Persistence\schema\SchemaMigrator.cs'    = '7B51779F621A054B497A2E2550AAFC57F178861D0D594DDE9ACB4843CE5EAF07'
    'backend\src\Squid.Persistence\schema\0002_threads.sql'     = 'EA1D8BE3BF489BE94B452B34A04E631A40A8CA254FE340769650B8CF48F923A5'
    'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'          = '758FDCDE5A01DE42EF04E8F1FD9002A7FD4D049ED952E9621EA69B1B40104CA6'
    'backend\src\Squid.Api\Auth\AuthSessionMiddleware.cs'       = 'F2F22F2269D011ED6DB70978382A0EFF515424492C6A8002F4966324A37EE948'
}
$hashBad = @()
foreach ($k in $expected.Keys) {
    $h = (Get-FileHash -LiteralPath (Join-Path $repo $k) -Algorithm SHA256).Hash
    if ($h -ne $expected[$k]) { $hashBad += $k }
}
Check 'HASH-PRE' ($hashBad.Count -eq 0) $(if ($hashBad.Count -eq 0) { '5 arquivos mutados = PRE/HEAD (S-16)' } else { 'divergente: ' + ($hashBad -join ',') })
$status = @(git -C $repo status --porcelain -uall -- backend)
Check 'GIT-BACKEND' ($status.Count -eq 0) ("git status backend = {0} entradas" -f $status.Count)

# 6. Only the five mutated backend files moved since the QA baseline (mtime, S-23 companion).
$stamp = Get-Date '2026-11-12 01:05:33'
$moved = Get-ChildItem -LiteralPath (Join-Path $repo 'backend\src'), (Join-Path $repo 'backend\tests') -Recurse -File |
    Where-Object { ($_.FullName -notmatch '\\(bin|obj)\\') -and $_.LastWriteTime -gt $stamp } |
    ForEach-Object { $_.FullName.Substring($repo.Length + 1) } | Sort-Object
$allowed = @($expected.Keys | Sort-Object)
Check 'MTIME-SCOPE' (@(Compare-Object $moved $allowed).Count -eq 0) ("mtime desde 01:05:33 = {0} arquivos (esperado exatamente os 5 mutados): {1}" -f $moved.Count, ($moved -join '; '))

# 7. The flow state machine accepts the task file with the QA's appended lines (exit 0).
$state = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'docs\sdd\_tools\sdd-state.ps1') check $task 2>&1 | Out-String
Check 'SDD-STATE' ($LASTEXITCODE -eq 0) (($state -replace '\s+', ' ').Trim())

# 8. The QA doc exists with the verdict this checker is verifying.
$doc = Join-Path $PSScriptRoot 'QA-0033-nucleo-da-thread.md'
Check 'QA-DOC' ((Test-Path -LiteralPath $doc) -and ((Get-Content -LiteralPath $doc -Raw) -match '(?m)^- verdict: \**PASSED')) 'QA-0033-nucleo-da-thread.md com verdict PASSED'

Write-Output "SUMMARY total=$total fails=$fails"
if ($fails -gt 0) { exit 1 }
exit 0
