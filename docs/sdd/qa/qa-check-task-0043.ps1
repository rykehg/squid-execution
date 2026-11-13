# QA-0043 evidence checker (static; it does not re-run the suite). Sixteen can-fail cycles:
# T1..T2 AC-01 actor links and #main-key, T3..T5 AC-02 collection first/next/type,
# T6..T9 AC-03 object, group kind, container collections and member visibility,
# T10 AC-04 nodeinfo, T11..T13 AC-05 the 401 gate and the 202 acceptance,
# T14..T16 AC-06 Accept negotiation (profile + federation routes) and webfinger without
# rel=profile.
# Every exit and summary is read from the LOG FILES (S-22 - never from a wrapper's console
# code), every restore from the recorded hashes (S-16) and the final tree from git.
# ASCII-only on purpose: this file runs without a UTF-8 BOM under Windows PowerShell 5.1.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0043.ps1
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$runs = Join-Path $repo 'docs\sdd\runs\test-runs'
$task = Join-Path $repo 'docs\sdd\tasks\TASK-0043-superficie-activitypub.md'
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
$base = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'QA-0043-git-baseline.txt') -Raw
Check 'BASELINE-HEAD' ($base -match 'HEAD: a658bd16e5b40e000ce6efa1d60f64d6fc865ae0') 'baseline HEAD = a658bd1'
Check 'BASELINE-BRANCH' ($base -match 'branch: task/TASK-0043-superficie-activitypub') 'branch task/TASK-0043-superficie-activitypub'
Check 'BASELINE-WSL' ($base -match 'wsl command -v dotnet => \[\] exit=1') 'dotnet ausente no WSL registrado na baseline (suite Linux = unverified)'

# The DEV commit touches only the six glob sets of the task's touches plus the task file's
# append-only Log/Evidence. Anything else in a658bd1 would break AC-07.
$inList = $false; $outside = @(); $files = @()
foreach ($line in ($base -split "`n")) {
    if ($line -match '^--- commit file list ---') { $inList = $true; continue }
    if ($inList -and $line -match '^--- ') { break }
    if ($inList -and $line.Trim()) {
        $path = $line.Trim()
        $files += $path
        if ($path -notmatch '^(backend/src/Squid\.Api/Discovery/|backend/src/Squid\.Api/Federation/|backend/src/Squid\.Protocols/|backend/src/Squid\.Service/|backend/tests/Squid\.Tests/|docs/sdd/tasks/TASK-0043-)') {
            $outside += $path
        }
    }
}
Check 'COMMIT-TOUCHES' ($outside.Count -eq 0 -and $files.Count -eq 11) $(if ($outside.Count -eq 0) { 'a658bd1: 11 arquivos, todos nos touches (9 produto + 1 teste + task file Log/Evidence)' } else { 'fora dos touches: ' + ($outside -join '; ') })

# 2. This QA's own baseline run of the eight scenarios before any write.
$bf = Join-Path $runs '20261113-194654-test-filter.log'
$bt = if (Test-Path -LiteralPath $bf) { Get-Content -LiteralPath $bf -Raw } else { '' }
Check 'BASELINE-FILTER' ($bt -match '(?m)^exit:\s+0\s*$' -and $bt -match 'Com falha:\s*0,\s+Aprovado:\s*8,\s+Ignorado:\s*0,\s+Total:\s*8') '20261113-194654-test-filter.log exit 0 / 8 de 8'

# 3. The can-fail driver self-check recorded before the run.
$vf = Join-Path $PSScriptRoot 'QA-0043-verify.log'
$vt = if (Test-Path -LiteralPath $vf) { Get-Content -LiteralPath $vf -Raw } else { '' }
Check 'VERIFY-RUN' ($vt -match 'VERIFY total=16 bad=0') 'QA-0043-verify.log VERIFY total=16 bad=0'
Check 'VERIFY-HITS' (([regex]::Matches($vt, 'hits=1 ok=true')).Count -eq 16 -and ($vt -match 'non-ascii-bytes=0 ok=true')) '16/16 padroes hits=1 + ASCII do driver OK'

# 4. AC-07 gate: exit header + summary INSIDE the gate log (S-22), 511/511, and the gate is
#    NEWER than the GREEN of the mutation run (it ran over the restored tree).
$gate = Newest '*-test-gate.log'
$gt = if ($gate) { Get-Content -LiteralPath $gate.FullName -Raw } else { '' }
Check 'GATE-EXIT' ($gt -match '(?m)^exit:\s+0\s*$') $(if ($gate) { '{0} header exit 0' -f $gate.Name } else { 'nenhum gate log' })
Check 'GATE-SUMMARY' ($gt -match 'Com falha:\s*0,\s+Aprovado:\s*512,\s+Ignorado:\s*0,\s+Total:\s*512') 'Com falha 0 / Aprovado 512 / Total 512'

$mutLog = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'QA-0043-mutations.log') -Raw
$greenLine = [regex]::Match($mutLog, '(?m)^GREEN wrapper=0 logExit=0 aprovado=9 falha=0 log=(\S+)')
$greenOk = $greenLine.Success
if ($greenOk -and $gate) {
    $gpath = Join-Path $runs $greenLine.Groups[1].Value
    Check 'GATE-AGE' ((Test-Path -LiteralPath $gpath) -and ($gate.LastWriteTime -gt (Get-Item -LiteralPath $gpath).LastWriteTime)) ('{0} criado depois do GREEN {1}' -f $gate.Name, $greenLine.Groups[1].Value)
}
else {
    Check 'GATE-AGE' $false 'GREEN ou gate log ausente'
}

# 5. The mutation ledger (S-16/S-22/S-23): every cycle ran RED with its own AC's scenario
#    inside the log, every restore came back to PRE, the green over the restored sources,
#    and the SUMMARY line of the fifteen cycles.
$ids = 'T1', 'T2', 'T3', 'T4', 'T5', 'T6', 'T7', 'T8', 'T9', 'T10', 'T11', 'T12', 'T13', 'T14', 'T15', 'T16'
$missing = @()
foreach ($id in $ids) {
    if ($mutLog -notmatch [regex]::Escape("$id PRE ")) { $missing += $id }
}
Check 'MUT-IDS' ($missing.Count -eq 0) $(if ($missing.Count -eq 0) { 'T1..T16 (16 ciclos) todos com linha PRE' } else { 'faltando: ' + ($missing -join ',') })
Check 'MUT-HEAD-MATCH' (([regex]::Matches($mutLog, '(?m)^T\d+ PRE .* head-match=true find-hits=1')).Count -eq 16) '16/16 PRE com hash = blob do commit a658bd1 (head-match=true)'

$notRed = @()
foreach ($id in $ids) {
    # S-22: the wrapper console can misreport (it prints 3 for a real red); logExit is the truth.
    $m = [regex]::Match($mutLog, ("(?m)^{0} RUN wrapper=\d+ logExit=1 aprovado=\d+ falha=(\d+)" -f [regex]::Escape($id)))
    if (-not $m.Success -or [int]$m.Groups[1].Value -lt 1) { $notRed += $id }
}
Check 'MUT-RED' ($notRed.Count -eq 0) $(if ($notRed.Count -eq 0) { 'T1..T16 todos vermelhos DENTRO do log (logExit: 1, falha >= 1)' } else { 'sem vermelho: ' + ($notRed -join ',') })

# Each cycle's EXPECT anchor must appear in that cycle's RED-AC line: the AC scenario that
# claims the broken behaviour is the one that failed (not some neighbouring suite).
function Catch-Ac([string]$id) {
    $expectM = [regex]::Match($mutLog, ("(?m)^{0} EXPECT-OK expect=(?<e>.*)$" -f [regex]::Escape($id)))
    $acM = [regex]::Match($mutLog, ("(?m)^{0} RED-AC (?<a>.*)$" -f [regex]::Escape($id)))
    $expect = if ($expectM.Success) { $expectM.Groups['e'].Value.Trim() } else { '' }
    $ac = if ($acM.Success) { $acM.Groups['a'].Value.Trim() } else { '' }
    $ok = ($expect -ne '' -and $ac -ne '' -and $ac -ne 'NONE' -and ($ac -match [regex]::Escape($expect)))
    Check ("CATCH-$id") $ok $(if ($ok) { "RED-AC contem: $expect" } else { "esperado '$expect' em RED-AC '$ac'" })
}
foreach ($id in $ids) { Catch-Ac $id }
Check 'MUT-EXPECT' (([regex]::Matches($mutLog, '(?m)^T\d+ EXPECT-OK ')).Count -eq 16 -and ($mutLog -notmatch 'EXPECT-MISS')) '16/16 EXPECT-OK e nenhum EXPECT-MISS'
Check 'MUT-TOOLING' (-not ($mutLog -match 'TOOLING-FAIL|BUILD-FAIL')) 'nenhum TOOLING-FAIL ou BUILD-FAIL (todo vermelho e falha de produto)'

$restoreOk = ([regex]::Matches($mutLog, 'RESTORE-OK hash=PRE residue=0')).Count
Check 'MUT-RESTORE' ($restoreOk -ge 16) ("RESTORE-OK = {0} (esperado >= 16: dezesseis ciclos can-fail)" -f $restoreOk)
Check 'MUT-RESTOREFAIL' (-not ($mutLog -match 'RESTORE-FAIL')) 'nenhum RESTORE-FAIL'
Check 'MUT-GREEN' ($greenOk) 'GREEN 9/9 sobre as fontes restauradas (wrapper=0 logExit=0 aprovado=9 falha=0)'
Check 'MUT-SUMMARY' ($mutLog -match 'SUMMARY cycles=16 failed=0 residue-backend=0') 'SUMMARY cycles=16 failed=0 residue-backend=0'

# 6. Residue: no mutant token left in the product sources, no temp backup, git backend clean.
$tokens = 'folloving', '#rsa-key', 'rel = "profile"', '?page=9"'
$hitsResidue = 0
foreach ($token in $tokens) {
    $found = Get-ChildItem -LiteralPath (Join-Path $repo 'backend') -Recurse -File -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
        Select-String -SimpleMatch -Pattern $token
    $hitsResidue += @($found).Count
}
Check 'RESIDUE' ($hitsResidue -eq 0) ("tokens mutantes em backend/src+tests (.cs, fora de bin/obj) = {0}" -f $hitsResidue)
$backups = @(Get-ChildItem -Path $env:TEMP -Filter 'qa0043-*' -File -ErrorAction SilentlyContinue)
Check 'TEMP-BACKUPS' ($backups.Count -eq 0) ("backups qa0043-* no TEMP = {0}" -f $backups.Count)
$backendStatus = @(git -C $repo status --porcelain -uall -- backend)
$qaTestEntry = '?? backend/tests/Squid.Tests/Federation/QaTask0043AcceptTests.cs'
$gitBackendOk = ($backendStatus.Count -eq 1 -and (($backendStatus[0] -replace '^\s+', '') -eq $qaTestEntry))
Check 'GIT-BACKEND' $gitBackendOk ("git status backend = {0} entradas (esperado 1: so {1})" -f $backendStatus.Count, $qaTestEntry)

# 7. The test file is the AC scenario set and the product still wires the behaviour (static).
$tests = Get-Content -LiteralPath (Join-Path $repo 'backend\tests\Squid.Tests\Federation\ActivityPubSurfaceTests.cs') -Raw
$acMissing = @()
foreach ($n in 'AC-01', 'AC-02', 'AC-03', 'AC-04', 'AC-05', 'AC-06') {
    if ($tests -notmatch ('// ' + [regex]::Escape($n))) { $acMissing += $n }
}
Check 'TEST-AC' ($acMissing.Count -eq 0 -and ([regex]::Matches($tests, '\[Fact\]')).Count -eq 8) $(if ($acMissing.Count -eq 0) { 'ActivityPubSurfaceTests: 8 [Fact] marcados // AC-01..// AC-06' } else { 'faltando: ' + ($acMissing -join ',') })
$qaTest = Join-Path $repo 'backend\tests\Squid.Tests\Federation\QaTask0043AcceptTests.cs'
$qt = if (Test-Path -LiteralPath $qaTest) { Get-Content -LiteralPath $qaTest -Raw } else { '' }
Check 'QA-TEST' ($qt -match '\[Fact\]' -and $qt -match '// AC-06' -and $qt -match 'The_federation_routes_negotiate_the_accept_before_any_lookup') 'QaTask0043AcceptTests: 1 [Fact] marcado // AC-06 (o cenario que T16 derruba)'
$svcSrc = Get-Content -LiteralPath (Join-Path $repo 'backend\src\Squid.Service\SquidServiceHost.cs') -Raw
Check 'WIRE-SEAMS' ($svcSrc -match 'app\.MapSquidFederation\(new FederationSeams\(' -and $svcSrc -match 'InboxSignatureGate\.VerifyAsync') 'host costura MapSquidFederation com InboxSignatureGate.VerifyAsync'
$discovery = Get-Content -LiteralPath (Join-Path $repo 'backend\src\Squid.Api\Discovery\DiscoveryEndpoints.cs') -Raw
Check 'WIRE-WEBFINGER' ($discovery -notmatch 'rel = "profile"' -and $discovery -match 'rel = "self"') 'webfinger publica so rel=self (sem rel=profile, P-08)'

# 8. Scope (AC-07): the scope log proves blob identity vs HEAD, mtime, residue and touches.
$scopePath = Join-Path $PSScriptRoot 'QA-0043-scope.log'
$sc = if (Test-Path -LiteralPath $scopePath) { Get-Content -LiteralPath $scopePath -Raw } else { '' }
Check 'SCOPE-BLOB' (([regex]::Matches($sc, 'match=True')).Count -eq 7) 'blob-identity work=HEAD em 7/7 arquivos de produto mutados (git hash-object vs rev-parse)'
Check 'SCOPE-MTIME' ($sc -match 'mtime-count=8 \(esperado 8') 'mtime desde a baseline = os 7 arquivos mutados/restaurados + o teste novo da QA (8)'
Check 'SCOPE-RESIDUE' ($sc -match 'residue=0') 'scope scan: residuo mutante = 0'
Check 'SCOPE-TOUCHES' ($sc -match 'fora-dos-touches=0') 'scope scan: nenhum arquivo fora dos touches alterado'
Check 'SCOPE-GIT-SCOPE' ($sc -match 'git-backend-entries=1') 'git status backend = 1 entrada (so o teste novo da QA, dentro dos touches)'

# 9. The flow state machine accepts the task file untouched by QA (exit 0).
$state = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'docs\sdd\_tools\sdd-state.ps1') check $task 2>&1 | Out-String
Check 'SDD-STATE' ($LASTEXITCODE -eq 0) (($state -replace '\s+', ' ').Trim())

# 10. The QA doc exists with the verdict this checker is verifying, and all seven ACs in it.
$doc = Join-Path $PSScriptRoot 'QA-0043-superficie-activitypub.md'
$dt = if (Test-Path -LiteralPath $doc) { Get-Content -LiteralPath $doc -Raw } else { '' }
Check 'QA-DOC' ($dt -match '(?m)^- verdict: \**PASSED') 'QA-0043-superficie-activitypub.md com verdict PASSED'
$docAcMissing = @()
foreach ($n in 'AC-01', 'AC-02', 'AC-03', 'AC-04', 'AC-05', 'AC-06', 'AC-07') {
    if ($dt -notmatch [regex]::Escape("| $n |")) { $docAcMissing += $n }
}
Check 'QA-DOC-ACS' ($docAcMissing.Count -eq 0) $(if ($docAcMissing.Count -eq 0) { 'AC-01..AC-07 presentes na tabela do doc' } else { 'faltando: ' + ($docAcMissing -join ',') })

Write-Output "SUMMARY total=$total fails=$fails"
if ($fails -gt 0) { exit 1 }
exit 0
