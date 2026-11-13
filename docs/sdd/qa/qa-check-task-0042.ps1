# QA-0042 evidence checker (static; no tests re-run). Eighteen can-fail cycles: T1 warning text,
# T2 boot without RK, T3 unwrapped-key roundtrip, T4 log channel with the secret, T5 typed failure,
# T6 no-leak message, T7..T12 the contract-5 outbound shape (line order, algorithm, #main-key,
# Digest SHA-256, Date GMT, GET half), T13..T18 the inbound acceptance/rejection of contract 3.
# Every exit and summary is read from the LOG FILES (S-22 - never from a wrapper's console code),
# every restore from the recorded hashes (S-16) and the final tree from git + an mtime scan.
# ASCII-only on purpose: this file runs without a UTF-8 BOM under Windows PowerShell 5.1.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0042.ps1
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$runs = Join-Path $repo 'docs\sdd\runs\test-runs'
$task = Join-Path $repo 'docs\sdd\tasks\TASK-0042-chave-de-assinatura-em-runtime.md'
$total = 0; $fails = 0

function Check([string]$name, [bool]$ok, [string]$detail) {
    $script:total++
    if ($ok) { Write-Output "CHECK $name OK - $detail" }
    else { $script:fails++; Write-Output "CHECK $name FAIL - $detail" }
}

function Newest([string]$filter) {
    Get-ChildItem -LiteralPath $runs -Filter $filter | Sort-Object Name | Select-Object -Last 1
}

# 1. The git baseline this QA captured before writing anything (HEAD of the DEV commit).
$base = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'QA-0042-git-baseline.txt') -Raw
Check 'BASELINE-HEAD' ($base -match 'HEAD: 0814968ece8d23a810078f3a93282c28296a407f') 'baseline HEAD = 0814968'
Check 'BASELINE-BRANCH' ($base -match 'branch: task/TASK-0042-chave-de-assinatura-em-runtime') 'branch task/TASK-0042-chave-de-assinatura-em-runtime'
Check 'BASELINE-WSL' ($base -match 'command -v dotnet => \[\] exit=1') 'dotnet ausente no WSL registrado na baseline (suite Linux = unverified)'

# The DEV commit touches only: 9 product files inside the task's touches + 3 test files + the
# task file's append-only Log/Evidence. Anything else in 0814968 would break AC-06.
$inList = $false; $outside = @()
foreach ($line in ($base -split "`n")) {
    if ($line -match '^--- commit file list ---') { $inList = $true; continue }
    if ($inList -and $line -match '^--- ') { break }
    if ($inList -and $line.Trim()) {
        if ($line -notmatch '^(backend/src/Squid\.Api/Crypto/|backend/src/Squid\.Grains/Actors/|backend/src/Squid\.Protocols/|backend/src/Squid\.Service/|backend/tests/Squid\.Tests/|docs/sdd/tasks/TASK-0042-)') {
            $outside += $line.Trim()
        }
    }
}
Check 'COMMIT-TOUCHES' ($outside.Count -eq 0) $(if ($outside.Count -eq 0) { '0814968: 13 arquivos, todos nos touches (9 produto + 3 teste + task file Log/Evidence)' } else { 'fora dos touches: ' + ($outside -join '; ') })

# 2. This QA's own baseline run of the three new classes (19/19) before any write.
$bf = Join-Path $runs '20261113-180909-test-filter.log'
$bt = if (Test-Path -LiteralPath $bf) { Get-Content -LiteralPath $bf -Raw } else { '' }
Check 'BASELINE-FILTER' ($bt -match '(?m)^exit:\s+0\s*$' -and $bt -match 'Com falha:\s*0,\s*Aprovado:\s*19,\s*Ignorado:\s*0,\s*Total:\s*19') '20261113-180909-test-filter.log exit 0 / 19 de 19'

# 3. AC-06 gate: exit header + summary INSIDE this QA's gate log (S-22), 503/503, and the gate
#    is NEWER than the last mutation cycle (it is the one this QA ran over the restored tree).
$gate = Newest '*-test-gate.log'
$gt = Get-Content -LiteralPath $gate.FullName -Raw
Check 'GATE-EXIT' ($gt -match '(?m)^exit:\s+0\s*$') ("{0} header exit 0" -f $gate.Name)
Check 'GATE-SUMMARY' ($gt -match 'Com falha:\s*0,\s*Aprovado:\s*503,\s*Ignorado:\s*0,\s*Total:\s*503') ("{0} Com falha 0 / Aprovado 503 / Total 503" -f $gate.Name)
$mutLog = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'QA-0042-mutations.log') -Raw
$greenLine = [regex]::Match($mutLog, '(?m)^GREEN wrapper=0 logExit=0 aprovado=19 falha=0 log=(\S+)')
$greenOk = $greenLine.Success
if ($greenOk) {
    $gpath = Join-Path $runs $greenLine.Groups[1].Value
    Check 'GATE-AGE' ((Test-Path -LiteralPath $gpath) -and ($gate.LastWriteTime -gt (Get-Item -LiteralPath $gpath).LastWriteTime)) ("{0} criado depois do GREEN {1}" -f $gate.Name, $greenLine.Groups[1].Value)
} else {
    Check 'GATE-AGE' $false 'linha GREEN wrapper=0 logExit=0 aprovado=19 falha=0 ausente'
}

# 4. The mutation ledger (S-16/S-22/S-23): every cycle ran RED with its own AC's test inside
#    the log (RED-AC), every restore came back to PRE, the green over the restored sources,
#    and the SUMMARY line of the eighteen cycles.
$ids = 'T1', 'T2', 'T3', 'T4', 'T5', 'T6', 'T7', 'T8', 'T9', 'T10', 'T11', 'T12', 'T13', 'T14', 'T15', 'T16', 'T17', 'T18'
$missing = @()
foreach ($id in $ids) {
    if ($mutLog -notmatch [regex]::Escape("$id PRE ")) { $missing += $id }
}
Check 'MUT-IDS' ($missing.Count -eq 0) $(if ($missing.Count -eq 0) { 'T1..T18 (18 ciclos) todos com linha PRE' } else { 'faltando: ' + ($missing -join ',') })

$notRed = @()
foreach ($id in $ids) {
    # S-22: the wrapper console can misreport (it prints 3 for a real red); logExit is the truth.
    $m = [regex]::Match($mutLog, ("(?m)^{0} RUN wrapper=\d+ logExit=1 aprovado=\d+ falha=(\d+)" -f [regex]::Escape($id)))
    if (-not $m.Success -or [int]$m.Groups[1].Value -lt 1) { $notRed += $id }
}
Check 'MUT-RED' ($notRed.Count -eq 0) $(if ($notRed.Count -eq 0) { 'T1..T18 todos vermelhos DENTRO do log (logExit: 1, falha >= 1)' } else { 'sem vermelho: ' + ($notRed -join ',') })
Check 'MUT-LOGEXIT' (([regex]::Matches($mutLog, '(?m)^T\d+ RUN wrapper=\d+ logExit=1 ')).Count -eq 18) '18/18 ciclos com logExit=1 lido no cabecalho do log (S-22)'

# Each cycle's EXPECT anchors must appear in that cycle's RED-AC line: the AC test that claims
# the broken behaviour is the one that failed (not some neighbouring suite).
function Catch-Ac([string]$id) {
    $expectM = [regex]::Match($mutLog, ("(?m)^{0} EXPECT (?<e>.*)$" -f [regex]::Escape($id)))
    $acM = [regex]::Match($mutLog, ("(?m)^{0} RED-AC (?<a>.*)$" -f [regex]::Escape($id)))
    $expect = if ($expectM.Success) { $expectM.Groups['e'].Value.Trim() } else { '' }
    $ac = if ($acM.Success) { $acM.Groups['a'].Value.Trim() } else { '' }
    $missingAnchors = @()
    foreach ($anchor in ($expect -split '\|')) {
        if ($anchor -and ($ac -notmatch [regex]::Escape($anchor))) { $missingAnchors += $anchor }
    }
    $ok = ($expect -ne '' -and $ac -ne '' -and $ac -notmatch 'NONE' -and $missingAnchors.Count -eq 0)
    Check ("CATCH-$id") $ok $(if ($ok) { "RED-AC contem: $expect" } else { "esperado '$expect' em RED-AC '$ac'" })
}
foreach ($id in $ids) { Catch-Ac $id }
Check 'MUT-EXPECT' (([regex]::Matches($mutLog, '(?m)^T\d+ EXPECT-OK ')).Count -eq 18 -and ($mutLog -notmatch 'EXPECT-MISS')) '18/18 EXPECT-OK e nenhum EXPECT-MISS'
Check 'MUT-CLASSIFICACAO' (([regex]::Matches($mutLog, 'CLASSIFICACAO-ERRADA')).Count -eq 0) 'nenhuma CLASSIFICACAO-ERRADA (todo vermelho e falha de produto)'
Check 'MUT-T16-2CASES' ($mutLog -match '(?m)^T16 EXPECT-OK .*\bfalha=2\b') 'T16 (theory draft) com falha=2: rsa-sha256 e hs2019 juntos'

$restoreOk = ([regex]::Matches($mutLog, 'RESTORE-OK hash=PRE residue=0')).Count
Check 'MUT-RESTORE' ($restoreOk -ge 18) ("RESTORE-OK = {0} (esperado >= 18: dezoito ciclos can-fail)" -f $restoreOk)
Check 'MUT-RESTOREFAIL' (-not ($mutLog -match 'RESTORE-FAIL')) 'nenhum RESTORE-FAIL'
Check 'MUT-GREEN' ($greenOk) 'GREEN 19/19 sobre as fontes restauradas (wrapper=0 logExit=0 aprovado=19 falha=0)'
Check 'MUT-SUMMARY' ($mutLog -match 'SUMMARY cycles=18 failed=0 residue-QA-MUTATION=0 temp-backups=0') 'SUMMARY cycles=18 failed=0 residue=0 temp-backups=0'

# 5. Residue: no QA-MUTATION marker left in the backend (cs, no bin/obj), no temp backup.
$residue = Get-ChildItem -LiteralPath (Join-Path $repo 'backend') -Recurse -File -Include '*.cs' |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    Select-String -Pattern 'QA-MUTATION' -SimpleMatch
Check 'RESIDUE' (@($residue).Count -eq 0) ("QA-MUTATION em backend = {0}" -f @($residue).Count)
$backups = @(Get-ChildItem -Path $env:TEMP -Filter 'qa0042-*' -File -ErrorAction SilentlyContinue)
Check 'TEMP-BACKUPS' ($backups.Count -eq 0) ("backups qa0042-* no TEMP = {0}" -f $backups.Count)

# 6. The driver is ASCII-only (no BOM trap) and -VerifyOnly ran against pristine sources.
$bytes = [System.IO.File]::ReadAllBytes((Join-Path $PSScriptRoot 'qa-mutate-task-0042.ps1'))
$nonAscii = @($bytes | Where-Object { $_ -gt 127 }).Count
Check 'DRIVER-ASCII' ($nonAscii -eq 0) ("qa-mutate-task-0042.ps1 sem byte > 127 ({0}) - nao depende de BOM" -f $nonAscii)
Check 'VERIFY-RECORDED' ((Test-Path -LiteralPath (Join-Path $PSScriptRoot 'QA-0042-verify.log')) -and
    ((Get-Content -LiteralPath (Join-Path $PSScriptRoot 'QA-0042-verify.log') -Raw) -match 'VERIFY total=18 bad=0')) 'QA-0042-verify.log com VERIFY total=18 bad=0'

# 7. The test files are the AC scenarios and the product still wires the behaviour (static).
$fed = Get-Content -LiteralPath (Join-Path $repo 'backend\tests\Squid.Tests\ServiceHost\FederationRuntimeKeyTests.cs') -Raw
Check 'FED-AC' ($fed -match '// AC-01' -and $fed -match '// AC-02' -and $fed -match '// AC-03' -and ([regex]::Matches($fed, '\[Fact\]')).Count -eq 3) 'FederationRuntimeKeyTests: 3 [Fact] marcados // AC-01..// AC-03'
$sig = Get-Content -LiteralPath (Join-Path $repo 'backend\tests\Squid.Tests\Protocols\HttpSignatureTests.cs') -Raw
Check 'SIG-AC' ($sig -match '// AC-04' -and $sig -match '// AC-05') 'HttpSignatureTests com marcadores // AC-04 e // AC-05'
$svcSrc = Get-Content -LiteralPath (Join-Path $repo 'backend\src\Squid.Service\SquidServiceHost.cs') -Raw
Check 'WIRE-RING' ($svcSrc -match 'RuntimeSigningKeyRing\.FromConfiguration\(configuration\["Squid:RecoveryKey"\]\)' -and $svcSrc -match 'federation output disabled') 'host le Squid:RecoveryKey no boot e avisa saida federada desabilitada'
$grainSrc = Get-Content -LiteralPath (Join-Path $repo 'backend\src\Squid.Grains\Actors\ActorGrain.cs') -Raw
Check 'WIRE-GRAIN' ($grainSrc -match 'ActivateSigningKeyAsync' -and $grainSrc -match 'SigningKeyActivationException') 'ActorGrain com ActivateSigningKeyAsync e SigningKeyActivationException auditavel'
$keyDer = Get-Content -LiteralPath (Join-Path $repo 'backend\src\Squid.Api\Crypto\KeyDerivation.cs') -Raw
Check 'WIRE-HKDF' ($keyDer -match 'RsaWrapInfo = "fed/rsa-wrap"') 'KeyDerivation: HKDF info fed/rsa-wrap (regra TASK-0012, AC-02)'

# 8. Scope (AC-06): git status backend EMPTY (byte-identical to 0814968), the scope log records
#    blob-identity, mtime, residue and touches, and nothing outside this QA's artifacts changed.
$backendStatus = @(git -C $repo status --porcelain -uall -- backend)
Check 'GIT-BACKEND' ($backendStatus.Count -eq 0) ("git status backend = {0} entradas" -f $backendStatus.Count)
$scopePath = Join-Path $PSScriptRoot 'QA-0042-scope.log'
$sc = Get-Content -LiteralPath $scopePath -Raw
Check 'SCOPE-BLOB' (([regex]::Matches($sc, 'match=True')).Count -eq 5) 'blob-identity work=HEAD em 5/5 arquivos mutados (git hash-object vs rev-parse)'
Check 'SCOPE-MTIME' ($sc -match 'mtime-count=5 \(esperado 5') 'mtime desde a baseline = exatamente os 5 arquivos mutados (restaurados)'
Check 'SCOPE-RESIDUE' ($sc -match 'residue=0') 'scope scan: residuo QA-MUTATION = 0'
Check 'SCOPE-TOUCHES' ($sc -match 'fora-dos-touches=0') 'scope scan: nenhum arquivo de produto fora dos touches alterado'
# Nothing NEW outside docs/sdd/qa since the baseline: every remaining entry must be either the
# pre-existing ODEV docs WIP captured at the start or this QA's own artifacts (QA-0042*).
$baseLines = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'QA-0042-git-baseline.txt'))
$baselineSet = @()
$inStatus = $false
foreach ($l in $baseLines) {
    if ($l -match '^--- status --porcelain -uall ---') { $inStatus = $true; continue }
    if ($l -match '^--- files of commit') { $inStatus = $false; continue }
    if ($inStatus -and $l.Trim()) { $baselineSet += ($l -replace '^\s+', '') }
}
$all = @(git -C $repo status --porcelain -uall | ForEach-Object { $_ -replace '^\s+', '' })
$outsideScope = @($all | Where-Object {
    $e = $_
    ($baselineSet -notcontains $e) -and ($e -notmatch 'docs/sdd/qa/.*0042')
})
Check 'SCOPE-GIT-SCOPE' ($outsideScope.Count -eq 0) ("git status fora do WIP pre-existente e dos artefatos da QA = {0} entradas (esperado 0: nem produto, nem outra task)" -f $outsideScope.Count)

# 9. The flow state machine accepts the task file untouched by QA (exit 0).
$state = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'docs\sdd\_tools\sdd-state.ps1') check $task 2>&1 | Out-String
Check 'SDD-STATE' ($LASTEXITCODE -eq 0) (($state -replace '\s+', ' ').Trim())

# 10. The QA doc exists with the verdict this checker is verifying, and all six ACs in it.
$doc = Join-Path $PSScriptRoot 'QA-0042-chave-de-assinatura-em-runtime.md'
$dt = if (Test-Path -LiteralPath $doc) { Get-Content -LiteralPath $doc -Raw } else { '' }
Check 'QA-DOC' ($dt -match '(?m)^- verdict: \**PASSED') 'QA-0042-chave-de-assinatura-em-runtime.md com verdict PASSED'
$acMissing = @()
foreach ($n in 'AC-01', 'AC-02', 'AC-03', 'AC-04', 'AC-05', 'AC-06') {
    if ($dt -notmatch [regex]::Escape("| $n |")) { $acMissing += $n }
}
Check 'QA-DOC-ACS' ($acMissing.Count -eq 0) $(if ($acMissing.Count -eq 0) { 'AC-01..AC-06 presentes na tabela do doc' } else { 'faltando: ' + ($acMissing -join ',') })

Write-Output "SUMMARY total=$total fails=$fails"
if ($fails -gt 0) { exit 1 }
exit 0
