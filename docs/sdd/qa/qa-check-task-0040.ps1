# QA-0040 evidence checker (static; no tests re-run). Nine cycles in the ledger: eight can-fail
# mutations (M1..M8, each reddening the facet test that claims the broken behaviour) plus the M9
# observation (un-monotonic marker, EXPECTED to survive the facet). Every exit and summary is
# read from the LOG FILES (S-22 - never from a wrapper's console code), every restore from the
# recorded hashes (S-16) and the final tree from git + an mtime scan (S-23 companions).
# ASCII-only on purpose: this file runs without a UTF-8 BOM under Windows PowerShell 5.1.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0040.ps1
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$runs = Join-Path $repo 'docs\sdd\runs\test-runs'
$task = Join-Path $repo 'docs\sdd\tasks\TASK-0040-facet-do-dod-sprint4.md'
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
$base = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'QA-0040-git-baseline.txt') -Raw
Check 'BASELINE-HEAD' ($base -match 'HEAD: 01b741f1e69d061cfbe17911d5b2f6ef9912337d') 'baseline HEAD = 01b741f'
Check 'BASELINE-BRANCH' ($base -match 'branch: task/TASK-0040-facet-do-dod-sprint4') 'branch task/TASK-0040-facet-do-dod-sprint4'
Check 'BASELINE-WSL' ($base -match 'dotnet-missing') 'dotnet ausente no WSL registrado na baseline (suite Linux = unverified)'

# 2. This QA's own baseline run of the Threads filter (48 = 41 DEV + 7 facet) before any write.
Log-Ok 'BASELINE-THREADS' '20261112-112436-test-filter.log' '(?m)^exit:\s+0\s*$[\s\S]*Com falha:\s*0,\s*Aprovado:\s*48,\s*Ignorado:\s*0,\s*Total:\s*48'

# 3. AC-07 gate: exit header + summary INSIDE this QA's gate log (S-22), 480/480, and the gate
#    is NEWER than the last mutation cycle (it is the one this QA ran over the restored tree).
$gate = Newest '*-test-gate.log'
$gt = Get-Content -LiteralPath $gate.FullName -Raw
Check 'GATE-AGE' ($gate.LastWriteTime -gt (Get-Date '2026-11-12 11:44:00')) ("{0} criado depois do verde 114231 da QA" -f $gate.Name)
Check 'GATE-EXIT' ($gt -match '(?m)^exit:\s+0\s*$') ("{0} header exit 0" -f $gate.Name)
Check 'GATE-SUMMARY' ($gt -match 'Com falha:\s*0,\s*Aprovado:\s*480,\s*Ignorado:\s*0,\s*Total:\s*480') ("{0} Com falha 0 / Aprovado 480 / Total 480" -f $gate.Name)

# 4. The mutation ledger (S-16/S-22/S-23): every cycle ran RED with the FACET's own test inside
#    its log (RED-FACET), every restore came back to PRE, the green over the restored sources,
#    and the SUMMARY line of the eight cycles.
$mutPath = Join-Path $PSScriptRoot 'QA-0040-mutations.log'
$mut = Get-Content -LiteralPath $mutPath -Raw
$missing = @()
foreach ($id in 'M1', 'M2', 'M3', 'M4', 'M5', 'M6', 'M7', 'M8', 'M9') {
    if ($mut -notmatch [regex]::Escape("$id PRE ")) { $missing += $id }
}
Check 'MUT-IDS' ($missing.Count -eq 0) $(if ($missing.Count -eq 0) { 'M1..M9 todos com linha PRE' } else { 'faltando: ' + ($missing -join ',') })

$notRed = @()
foreach ($id in 'M1', 'M2', 'M3', 'M4', 'M5', 'M6', 'M7', 'M8', 'M9') {
    $m = [regex]::Match($mut, ("(?m)^{0} RUN wrapper=1 logExit=1 aprovado=\d+ falha=(\d+)" -f [regex]::Escape($id)))
    if (-not $m.Success -or [int]$m.Groups[1].Value -lt 1) { $notRed += $id }
}
Check 'MUT-RED' ($notRed.Count -eq 0) $(if ($notRed.Count -eq 0) { 'M1..M9 todos vermelhos DENTRO do log (exit: 1, falha >= 1)' } else { 'sem vermelho: ' + ($notRed -join ',') })
Check 'MUT-LOGEXIT' (([regex]::Matches($mut, '(?m)^M\d RUN wrapper=\d+ logExit=1 ')).Count -eq 9) '9/9 ciclos com logExit=1 lido no cabecalho do log (S-22)'

# Each cycle's EXPECT anchors must all appear in that cycle's RED-FACET line: the facet test
# that claims the broken behaviour is the one that failed (not some neighbouring suite).
function Catch-Facet([string]$id) {
    $expectM = [regex]::Match($mut, ("(?m)^{0} EXPECT (?<e>.*)$" -f [regex]::Escape($id)))
    $facetM = [regex]::Match($mut, ("(?m)^{0} RED-FACET (?<f>.*)$" -f [regex]::Escape($id)))
    $expect = if ($expectM.Success) { $expectM.Groups['e'].Value.Trim() } else { '' }
    $facet = if ($facetM.Success) { $facetM.Groups['f'].Value.Trim() } else { '' }
    $missingAnchors = @()
    foreach ($anchor in ($expect -split '\|')) {
        if ($anchor -and ($facet -notmatch [regex]::Escape($anchor))) { $missingAnchors += $anchor }
    }
    $ok = ($expect -ne '' -and $facet -ne '' -and $facet -notmatch 'NONE' -and $missingAnchors.Count -eq 0)
    Check ("CATCH-$id") $ok $(if ($ok) { "RED-FACET contem: $expect" } else { "esperado '$expect' em RED-FACET '$facet'" })
}
Catch-Facet 'M1'
Catch-Facet 'M2'
Catch-Facet 'M3'
Catch-Facet 'M4'
Catch-Facet 'M5'
Catch-Facet 'M6'
Catch-Facet 'M7'
Catch-Facet 'M8'
# M9 is the OBSERVATION cycle: the un-monotonic marker must SURVIVE the facet (RED-FACET NONE -
# AC-01..AC-06 never mark backwards) while the TASK-0036 suite reddens - the documented boundary.
$m9run = [regex]::Match($mut, '(?m)^M9 RUN wrapper=1 logExit=1 aprovado=\d+ falha=(\d+)')
Check 'M9-SURVIVES-FACET' (($mut -match '(?m)^M9 RED-FACET NONE') -and $m9run.Success -and ([int]$m9run.Groups[1].Value -ge 1)) 'M9 (marcador pode regredir) sobrevive a facet mas a suite da TASK-0036 fica vermelha (falha >= 1)'
Check 'M9-CAUGHT-BY-SUITE' ($mut -match '(?m)^M9 RED-IN-LOG .*The_read_marker_only_advances') 'M9 RED-IN-LOG pega em The_read_marker_only_advances (a monotonicidade e da TASK-0036, nao da facet)'
Check 'MUT-CLASSIFICACAO' (([regex]::Matches($mut, 'CLASSIFICACAO-ERRADA')).Count -eq 0) 'nenhuma CLASSIFICACAO-ERRADA (todo vermelho e falha de produto)'

$restoreOk = ([regex]::Matches($mut, 'RESTORE-OK hash=PRE residue=0')).Count
Check 'MUT-RESTORE' ($restoreOk -ge 9) ("RESTORE-OK = {0} (esperado >= 9: oito ciclos can-fail + a observacao M9)" -f $restoreOk)
Check 'MUT-RESTOREFAIL' (-not ($mut -match 'RESTORE-FAIL')) 'nenhum RESTORE-FAIL'
Check 'MUT-GREEN' ($mut -match '(?m)^GREEN wrapper=0 logExit=0 aprovado=48 falha=0 ') 'GREEN 48/48 sobre as fontes restauradas'
Check 'MUT-SUMMARY' ($mut -match 'SUMMARY cycles=8 failed=0 residue-QA-MUTATION=0 temp-backups=0') 'SUMMARY cycles=8 failed=0 residue=0 temp-backups=0 (run cheio)'
Check 'MUT-SUMMARY-M9' ($mut -match 'SUMMARY cycles=1 failed=0 residue-QA-MUTATION=0 temp-backups=0') 'SUMMARY cycles=1 failed=0 (re-execucao seletiva do M9)'

# 5. Residue: no QA-MUTATION marker left in the backend (cs + sql, no bin/obj), no temp backup.
$residue = Get-ChildItem -LiteralPath (Join-Path $repo 'backend') -Recurse -File -Include '*.cs', '*.sql' |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    Select-String -Pattern 'QA-MUTATION' -SimpleMatch
Check 'RESIDUE' (@($residue).Count -eq 0) ("QA-MUTATION em backend = {0}" -f @($residue).Count)
$backups = @(Get-ChildItem -Path $env:TEMP -Filter 'qa0040-*' -File -ErrorAction SilentlyContinue)
Check 'TEMP-BACKUPS' ($backups.Count -eq 0) ("backups qa0040-* no TEMP = {0}" -f $backups.Count)

# 6. The driver keeps its UTF-8 BOM (QA-0034..0039 lesson - 6a recorrencia) and -VerifyOnly ran.
$bom = [System.IO.File]::ReadAllBytes((Join-Path $PSScriptRoot 'qa-mutate-task-0040.ps1'))[0..2]
Check 'DRIVER-BOM' ($bom[0] -eq 0xEF -and $bom[1] -eq 0xBB -and $bom[2] -eq 0xBF) 'qa-mutate-task-0040.ps1 com UTF-8 BOM'
Check 'VERIFY-RECORDED' ((Test-Path -LiteralPath (Join-Path $PSScriptRoot 'QA-0040-verify.log')) -and
    ((Get-Content -LiteralPath (Join-Path $PSScriptRoot 'QA-0040-verify.log') -Raw) -match 'VERIFY total=9 bad=0')) 'QA-0040-verify.log com VERIFY total=9 bad=0'

# 7. The facet's concurrency test is REAL: Task.WhenAll over the two sessions, three rounds.
$facetSrc = Get-Content -LiteralPath (Join-Path $repo 'backend\tests\Squid.Tests\Threads\Sprint4ClosingFacetTests.cs') -Raw
Check 'CONCURRENCY-REAL' ($facetSrc -match 'await Task\.WhenAll\(' -and $facetSrc -match 'for \(var round = 0; round < 3; round\+\+\)') 'AC-03 usa Task.WhenAll (3 rodadas), nao envio sequencial'
Check 'FACET-FACTS' (([regex]::Matches($facetSrc, '\[Fact\]')).Count -eq 7) 'facet com 7 [Fact] (AC-01..AC-06)'

# 8. Scope (AC-07): git status backend EMPTY (byte-identical to 01b741f), the scope log records
#    blob-identity, mtime, residue and touches, and nothing outside docs/sdd was touched.
$backendStatus = @(git -C $repo status --porcelain -uall -- backend)
Check 'GIT-BACKEND' ($backendStatus.Count -eq 0) ("git status backend = {0} entradas" -f $backendStatus.Count)
$scopePath = Join-Path $PSScriptRoot 'QA-0040-scope.log'
$sc = Get-Content -LiteralPath $scopePath -Raw
Check 'SCOPE-BLOB' (([regex]::Matches($sc, 'match=True')).Count -eq 3) 'blob-identity work=HEAD em 3/3 arquivos mutados (git hash-object vs rev-parse)'
Check 'SCOPE-MTIME' ($sc -match 'mtime-count=3 \(esperado 3') 'mtime desde a baseline = exatamente os 3 arquivos mutados (restaurados)'
Check 'SCOPE-RESIDUE' ($sc -match 'residue=0') 'scope scan: residuo QA-MUTATION = 0'
Check 'SCOPE-TOUCHES' ($sc -match 'fora-dos-touches=0') 'scope scan: nenhum arquivo de produto fora dos touches alterado'
$all = @(git -C $repo status --porcelain -uall)
$outside = @($all | Where-Object { $_ -notmatch 'docs/sdd/(tasks/TASK-0040|qa/)' })
Check 'SCOPE-GIT-SCOPE' ($outside.Count -eq 0) ("git status fora de docs/sdd = {0} entradas (esperado 0: nem produto, nem outra task)" -f $outside.Count)

# 9. The flow state machine accepts the task file with the QA's appended lines (exit 0).
$state = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'docs\sdd\_tools\sdd-state.ps1') check $task 2>&1 | Out-String
Check 'SDD-STATE' ($LASTEXITCODE -eq 0) (($state -replace '\s+', ' ').Trim())

# 10. The QA doc exists with the verdict this checker is verifying.
$doc = Join-Path $PSScriptRoot 'QA-0040-facet-do-dod-sprint4.md'
Check 'QA-DOC' ((Test-Path -LiteralPath $doc) -and ((Get-Content -LiteralPath $doc -Raw) -match '(?m)^- verdict: \**PASSED')) 'QA-0040-facet-do-dod-sprint4.md com verdict PASSED'

Write-Output "SUMMARY total=$total fails=$fails"
if ($fails -gt 0) { exit 1 }
exit 0
