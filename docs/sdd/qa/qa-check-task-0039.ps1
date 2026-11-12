# QA-0039 evidence checker (static; no tests re-run). Every exit and summary is read from the
# LOG FILES (S-22 - never from a wrapper's console code), every restore from the recorded hashes
# (S-16) and the final tree from git + an mtime scan (S-23 companions).
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0039.ps1
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$runs = Join-Path $repo 'docs\sdd\runs\test-runs'
$task = Join-Path $repo 'docs\sdd\tasks\TASK-0039-anexo-em-mensagem.md'
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
$base = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'QA-0039-git-baseline.txt') -Raw
Check 'BASELINE-HEAD' ($base -match 'HEAD: 3d8a1d99d23b96a833384c7781bac3cb8b89be56') 'baseline HEAD = 3d8a1d9'
Check 'BASELINE-BRANCH' ($base -match 'branch: task/TASK-0039-anexo-em-mensagem') 'branch task/TASK-0039-anexo-em-mensagem'
Check 'BASELINE-WSL' ($base -match 'dotnet-missing') 'dotnet ausente no WSL registrado na baseline (suite Linux = unverified)'

# 2. The AC-05 gate: exit header + summary INSIDE this QA's gate log (S-22), 473/473, and that
#    this gate is NEWER than the temp test's green run (it is the one this QA ran, tree = DEV's).
$gate = Newest '*-test-gate.log'
$gt = Get-Content -LiteralPath $gate.FullName -Raw
Check 'GATE-AGE' ($gate.LastWriteTime -gt (Get-Date '2026-11-12 09:41:00')) ("{0} criado depois do verde 094107 da QA" -f $gate.Name)
Check 'GATE-EXIT' ($gt -match '(?m)^exit:\s+0\s*$') ("{0} header exit 0" -f $gate.Name)
Check 'GATE-SUMMARY' ($gt -match 'Com falha:\s*0,\s*Aprovado:\s*473,\s*Ignorado:\s*0,\s*Total:\s*473') ("{0} Com falha 0 / Aprovado 473 / Total 473" -f $gate.Name)

# 3. Baseline filtered runs of this QA (Threads 41, Media 27) + the green WITH the QA's
#    temporary cap test (42 = 41 DEV + 1 QA) before it was removed from the tree.
Log-Ok 'BASELINE-THREADS' '20261112-093821-test-filter.log' '(?m)^exit:\s+0\s*$[\s\S]*Com falha:\s*0,\s*Aprovado:\s*41'
Log-Ok 'BASELINE-MEDIA' '20261112-093902-test-filter.log' '(?m)^exit:\s+0\s*$[\s\S]*Com falha:\s*0,\s*Aprovado:\s*27'
Log-Ok 'QA-TEMP-FILTER' '20261112-094107-test-filter.log' '(?m)^exit:\s+0\s*$[\s\S]*Com falha:\s*0,\s*Aprovado:\s*42'

# 4. The mutation ledger (S-16/S-22/S-23): every cycle ran RED (classified in its log), every
#    restore came back to PRE, the greens over the restored sources, and both SUMMARY lines
#    (the full run of 13 cycles + the corrected M2 re-run).
$mutPath = Join-Path $PSScriptRoot 'QA-0039-mutations.log'
$mut = Get-Content -LiteralPath $mutPath -Raw
$missing = @()
foreach ($id in 'M1', 'M2', 'M3', 'M4', 'M5', 'M6', 'M7', 'M8', 'M9', 'M10', 'M11', 'M12', 'M13') {
    if ($mut -notmatch [regex]::Escape("$id PRE ")) { $missing += $id }
}
Check 'MUT-IDS' ($missing.Count -eq 0) $(if ($missing.Count -eq 0) { 'M1..M13 todos com linha PRE' } else { 'faltando: ' + ($missing -join ',') })

# Every cycle except M2 is red in the FULL run; M2's first mutant died in the build
# (CS0828) - classified by the log itself - and its corrected re-run is the LAST M2 RUN.
$notRed = @()
foreach ($id in 'M1', 'M3', 'M4', 'M5', 'M6', 'M7', 'M8', 'M9', 'M10', 'M11', 'M12', 'M13') {
    $runsOfId = [regex]::Matches($mut, ("(?m)^{0} RUN wrapper=\d+ logExit=\d+ aprovado=\d+ falha=(\d+)" -f [regex]::Escape($id)))
    $red = $false
    foreach ($m in $runsOfId) { if ([int]$m.Groups[1].Value -ge 1) { $red = $true } }
    if (-not $red) { $notRed += $id }
}
Check 'MUT-RED' ($notRed.Count -eq 0) $(if ($notRed.Count -eq 0) { 'M1, M3..M13 todos com RUN vermelho classificado no log' } else { 'sem vermelho: ' + ($notRed -join ',') })

$m2runs = [regex]::Matches($mut, '(?m)^M2 RUN wrapper=\d+ logExit=\d+ aprovado=\d+ falha=(\d+)')
$m2last = if ($m2runs.Count -gt 0) { [int]($m2runs | Select-Object -Last 1).Groups[1].Value } else { -1 }
Check 'MUT-RED-M2' ($m2last -ge 1) ("ultima execucao do M2 corrigido: falha=$m2last (esperado >= 1)")
Check 'MUT-BUILD-DEAD-1' (([regex]::Matches($mut, 'CLASSIFICACAO-ERRADA')).Count -eq 1) 'exatamente 1 CLASSIFICACAO-ERRADA (M2 primitivo morreu no build, CS0828, denunciado pelo log - S-22)'

$restoreOk = ([regex]::Matches($mut, 'RESTORE-OK hash=PRE residue=0')).Count
Check 'MUT-RESTORE' ($restoreOk -ge 14) ("RESTORE-OK = {0} (esperado >= 14: 13 do run cheio + 1 do M2 corrigido)" -f $restoreOk)
Check 'MUT-RESTOREFAIL' (-not ($mut -match 'RESTORE-FAIL')) 'nenhum RESTORE-FAIL'
$greens = ([regex]::Matches($mut, 'GREEN wrapper=0 logExit=0 aprovado=42 falha=0')).Count
Check 'MUT-GREEN' ($greens -ge 2) ("GREEN 42/42 = {0} (fim do run cheio e do re-run do M2)" -f $greens)
Check 'MUT-SUMMARY-FULL' ($mut -match 'SUMMARY cycles=13 failed=1 residue-QA-MUTATION=0 temp-backups=0') 'SUMMARY cycles=13 failed=1 residue=0 temp-backups=0 (o unico failed e o M2 primitivo)'
Check 'MUT-SUMMARY-M2' ($mut -match 'SUMMARY cycles=1 failed=0 residue-QA-MUTATION=0 temp-backups=0') 'SUMMARY cycles=1 failed=0 (re-run do M2 corrigido)'

function Catch-Red([string]$id, [string]$must1, [string]$must2 = '') {
    $m = [regex]::Match($mut, ("(?m)^{0} RED-IN-LOG (?<r>.*)$" -f [regex]::Escape($id)))
    $r = if ($m.Success) { $m.Groups['r'].Value } else { '' }
    $ok = $m.Success -and $r -match [regex]::Escape($must1)
    if ($must2 -ne '') { $ok = $ok -and ($r -match [regex]::Escape($must2)) }
    Check ("CATCH-$id") $ok ("{0} vermelho com {1}{2}" -f $id, $must1, $(if ($must2) { ' + ' + $must2 } else { '' }))
}

# The dispatch's mutation targets, each proven by the suite that caught it (not by my reading).
Catch-Red 'M1' 'Send_carries_contentHashes_and_the_read_envelope_returns_the_Attachment_shape'   # AC-01 roundtrip 0007
$m2d = ([regex]::Matches($mut, "(?m)^M2 RED-DETAIL (?<d>.*)$") | Select-Object -Last 1).Groups['d'].Value
Check 'CATCH-M2' (($mut -match '(?m)^M2 RED-IN-LOG .*Send_carries_contentHashes') -and ($m2d -match 'foto da mensagem')) 'M2 (alt fora do envelope) pego pelo campo alt da AC-01 (Expected: "foto da mensagem")'
Catch-Red 'M3' 'QaTask0039AttachmentCapTests'                                                        # AC-01 cap com hash real (Fact da QA)
Catch-Red 'M4' 'Send_carries_contentHashes'                                                          # AC-01 hash inexistente 400
$m5d = ([regex]::Match($mut, "(?m)^M5 RED-DETAIL (?<d>.*)$")).Groups['d'].Value
Check 'CATCH-M5' (($mut -match '(?m)^M5 RED-IN-LOG .*Send_carries_contentHashes') -and ($m5d -match 'Expected: 1')) 'M5 (validacao depois da CAS com commit) pego pela AC-01: seq esperado 1, recebido 2'
Catch-Red 'M6' 'An_image_without_alt_is_refused_only_after_the_428_and_the_422'                     # AC-02 Alt na publicacao
$m7run = [regex]::Match($mut, '(?m)^M7 RUN wrapper=1 logExit=1 aprovado=41 falha=1')
Check 'CATCH-M7' (($mut -match '(?m)^M7 RED-IN-LOG .*ThreadAttachmentQuotaTests') -and $m7run.Success) 'M7 (envio cobra no ledger) pego pela AC-03 (41/1, so a suite de cota)'
$m8run = [regex]::Match($mut, '(?m)^M8 RUN wrapper=1 logExit=1 aprovado=68 falha=1')
Check 'CATCH-M8' (($mut -match '(?m)^M8 RED-IN-LOG .*GetMedia_serves_the_participant') -and $m8run.Success) 'M8 (ramo mensagem da GAP-25 morto) pego so pela mensagem: 68 aprovados (ramo POST verde no MESMO run) e 1 falha'
Catch-Red 'M9' 'GetMedia_serves_the_participant'                                                     # AC-04 parede de cadeiras
Catch-Red 'M10' 'An_image_without_alt_is_refused_only_after_the_428_and_the_422'                     # escada 428 antes de 400
Catch-Red 'M11' 'An_image_without_alt_is_refused_only_after_the_428_and_the_422'                     # escada 422 antes de 400
$m10d = ([regex]::Match($mut, "(?m)^M10 RED-DETAIL (?<d>.*)$")).Groups['d'].Value
$m11d = ([regex]::Match($mut, "(?m)^M11 RED-DETAIL (?<d>.*)$")).Groups['d'].Value
Check 'LADDER-428' ($m10d -match 'Expected: PreconditionRequired') 'M10 RED-DETAIL: Expected PreconditionRequired (428) - o 428 continua antes do 400 do anexo'
Check 'LADDER-422' ($m11d -match 'Expected: UnprocessableEntity') 'M11 RED-DETAIL: Expected UnprocessableEntity (422) - o 422 continua antes do 400 do anexo'
$m12run = [regex]::Match($mut, '(?m)^M12 RUN wrapper=1 logExit=1 aprovado=41 falha=1')
Check 'CATCH-M12' (($mut -match '(?m)^M12 RED-IN-LOG .*ThreadAttachmentQuotaTests') -and $m12run.Success) 'M12 (507 desligado de POST /api/media) pego pela AC-03 (41/1)'
$m13run = [regex]::Match($mut, '(?m)^M13 RUN wrapper=1 logExit=1 aprovado=39 falha=3')
Check 'CATCH-M13' (($mut -match '(?m)^M13 RED-IN-LOG .*ThreadMigrationTests') -and $m13run.Success) 'M13 (contador VALIDO na 0007) vermelho so nas 3 migration tests de message (39/3, rotas verdes)'

# 5. Residue: no QA-MUTATION marker left in the backend (cs + sql, no bin/obj), no temp backup.
$residue = Get-ChildItem -LiteralPath (Join-Path $repo 'backend') -Recurse -File -Include '*.cs', '*.sql' |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    Select-String -Pattern 'QA-MUTATION' -SimpleMatch
Check 'RESIDUE' (@($residue).Count -eq 0) ("QA-MUTATION em backend = {0}" -f @($residue).Count)
$backups = @(Get-ChildItem -Path $env:TEMP -Filter 'qa0039-*' -File -ErrorAction SilentlyContinue)
Check 'TEMP-BACKUPS' ($backups.Count -eq 0) ("backups qa0039-* no TEMP = {0}" -f $backups.Count)

# 6. The driver keeps its UTF-8 BOM (the QA-0034..0038 lesson - 5a recorrencia).
$bom = [System.IO.File]::ReadAllBytes((Join-Path $PSScriptRoot 'qa-mutate-task-0039.ps1'))[0..2]
Check 'DRIVER-BOM' ($bom[0] -eq 0xEF -and $bom[1] -eq 0xBB -and $bom[2] -eq 0xBF) 'qa-mutate-task-0039.ps1 com UTF-8 BOM'
Check 'VERIFY-RECORDED' ((Test-Path -LiteralPath (Join-Path $PSScriptRoot 'QA-0039-verify.log')) -and
    ((Get-Content -LiteralPath (Join-Path $PSScriptRoot 'QA-0039-verify.log') -Raw) -match 'VERIFY total=13 bad=0')) 'QA-0039-verify.log com VERIFY total=13 bad=0 (padroes + BOM)'

# 7. Scope (AC-05): git status backend EMPTY (the QA's temporary test is GONE from the tree, its
#    source kept as .cs.txt in docs/sdd/qa), and the scope log records blob-identity, mtime,
#    residue and touches.
$backendStatus = @(git -C $repo status --porcelain -uall -- backend)
Check 'GIT-BACKEND' ($backendStatus.Count -eq 0) ("git status backend = {0} entradas" -f $backendStatus.Count)
Check 'TEMP-TEST-GONE' (-not (Test-Path -LiteralPath (Join-Path $repo 'backend\tests\Squid.Tests\Threads\QaTask0039AttachmentCapTests.cs'))) 'teste temporario removido da arvore'
Check 'TEMP-TEST-SOURCE' (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'QaTask0039AttachmentCapTests.cs.txt')) 'fonte do teste temporario preservada em docs/sdd/qa'
$scopePath = Join-Path $PSScriptRoot 'QA-0039-scope.log'
$sc = Get-Content -LiteralPath $scopePath -Raw
Check 'SCOPE-BLOB' (([regex]::Matches($sc, 'match=True')).Count -eq 4) 'blob-identity work=HEAD em 4/4 arquivos mutados (git hash-object vs rev-parse)'
Check 'SCOPE-MTIME' ($sc -match 'mtime-count=4 \(esperado 4') 'mtime desde a baseline = exatamente os 4 arquivos mutados (restaurados)'
Check 'SCOPE-RESIDUE' ($sc -match 'residue=0') 'scope scan: residuo QA-MUTATION = 0'
Check 'SCOPE-TOUCHES' ($sc -match 'fora-dos-touches=0') 'scope scan: nenhum arquivo de produto fora dos touches alterado'

# 8. The flow state machine accepts the task file with the QA's appended lines (exit 0).
$state = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'docs\sdd\_tools\sdd-state.ps1') check $task 2>&1 | Out-String
Check 'SDD-STATE' ($LASTEXITCODE -eq 0) (($state -replace '\s+', ' ').Trim())

# 9. The QA doc exists with the verdict this checker is verifying.
$doc = Join-Path $PSScriptRoot 'QA-0039-anexo-em-mensagem.md'
Check 'QA-DOC' ((Test-Path -LiteralPath $doc) -and ((Get-Content -LiteralPath $doc -Raw) -match '(?m)^- verdict: \**PASSED')) 'QA-0039-anexo-em-mensagem.md com verdict PASSED'

Write-Output "SUMMARY total=$total fails=$fails"
if ($fails -gt 0) { exit 1 }
exit 0
