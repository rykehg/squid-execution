# QA-0041 evidence checker (static; no tests re-run). Twelve can-fail cycles: M1 scheme+Host
# byte-identity, M2 Instance:Domain read, M3 subject canonical, M4a/M4b the two halves of the
# D-01 domain acceptance, M5/M6 the two halves of the trust gate, M7 the FR-053 404 leak, and
# M8..M11 one origin point each (Discovery/Posts/Containers/Threads). Every exit and summary is
# read from the LOG FILES (S-22 - never from a wrapper's console code), every restore from the
# recorded hashes (S-16) and the final tree from git + an mtime scan (S-23 companions).
# ASCII-only on purpose: this file runs without a UTF-8 BOM under Windows PowerShell 5.1.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0041.ps1
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$runs = Join-Path $repo 'docs\sdd\runs\test-runs'
$task = Join-Path $repo 'docs\sdd\tasks\TASK-0041-dominio-canonico-e-proxy.md'
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
$base = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'QA-0041-git-baseline.txt') -Raw
Check 'BASELINE-HEAD' ($base -match 'HEAD: 9210469b3dec6866466f34e97babcde3adffc2c1') 'baseline HEAD = 9210469'
Check 'BASELINE-BRANCH' ($base -match 'branch: task/TASK-0041-dominio-canonico-e-proxy') 'branch task/TASK-0041-dominio-canonico-e-proxy'
Check 'BASELINE-WSL' ($base -match 'dotnet-missing') 'dotnet ausente no WSL registrado na baseline (suite Linux = unverified)'
Check 'BASELINE-TOUCHES' ($base -match 'backend/src/Squid.Api/Discovery/CanonicalOrigin.cs' -and $base -match 'backend/tests/Squid.Tests/Discovery/CanonicalOriginConfigTests.cs' -and $base -notmatch 'web/') 'commit 9210469: 7 arquivos, todos nos touches (nenhum em web/)'

# 2. This QA's own baseline run of the CanonicalOrigin filter (4/4) before any write.
$bf = Join-Path $runs '20261113-153601-test-filter.log'
$bt = if (Test-Path -LiteralPath $bf) { Get-Content -LiteralPath $bf -Raw } else { '' }
Check 'BASELINE-FILTER' ($bt -match '(?m)^exit:\s+0\s*$' -and $bt -match 'Com falha:\s*0,\s*Aprovado:\s*4,\s*Ignorado:\s*0,\s*Total:\s*4') '20261113-153601-test-filter.log exit 0 / 4 de 4'

# 3. AC-05 gate: exit header + summary INSIDE this QA's gate log (S-22), 484/484, and the gate
#    is NEWER than the last mutation cycle (it is the one this QA ran over the restored tree).
$gate = Newest '*-test-gate.log'
$gt = Get-Content -LiteralPath $gate.FullName -Raw
Check 'GATE-EXIT' ($gt -match '(?m)^exit:\s+0\s*$') ("{0} header exit 0" -f $gate.Name)
Check 'GATE-SUMMARY' ($gt -match 'Com falha:\s*0,\s*Aprovado:\s*484,\s*Ignorado:\s*0,\s*Total:\s*484') ("{0} Com falha 0 / Aprovado 484 / Total 484" -f $gate.Name)
$mutLog = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'QA-0041-mutations.log') -Raw
$greenLine = [regex]::Match($mutLog, '(?m)^GREEN wrapper=0 logExit=0 aprovado=4 falha=0 log=(\S+)')
$greenOk = $greenLine.Success
if ($greenOk) {
    $gpath = Join-Path $runs $greenLine.Groups[1].Value
    Check 'GATE-AGE' ((Test-Path -LiteralPath $gpath) -and ($gate.LastWriteTime -gt (Get-Item -LiteralPath $gpath).LastWriteTime)) ("{0} criado depois do GREEN {1}" -f $gate.Name, $greenLine.Groups[1].Value)
} else {
    Check 'GATE-AGE' $false 'linha GREEN wrapper=0 logExit=0 aprovado=4 falha=0 ausente'
}

# 4. The mutation ledger (S-16/S-22/S-23): every cycle ran RED with its own AC's config test
#    inside the log (RED-AC), every restore came back to PRE, the green over the restored
#    sources, and the SUMMARY line of the twelve cycles.
$ids = 'M1', 'M2', 'M3', 'M4a', 'M4b', 'M5', 'M6', 'M7', 'M8', 'M9', 'M10', 'M11'
$missing = @()
foreach ($id in $ids) {
    if ($mutLog -notmatch [regex]::Escape("$id PRE ")) { $missing += $id }
}
Check 'MUT-IDS' ($missing.Count -eq 0) $(if ($missing.Count -eq 0) { 'M1..M11 (12 ciclos) todos com linha PRE' } else { 'faltando: ' + ($missing -join ',') })

$notRed = @()
foreach ($id in $ids) {
    $m = [regex]::Match($mutLog, ("(?m)^{0} RUN wrapper=1 logExit=1 aprovado=\d+ falha=(\d+)" -f [regex]::Escape($id)))
    if (-not $m.Success -or [int]$m.Groups[1].Value -lt 1) { $notRed += $id }
}
Check 'MUT-RED' ($notRed.Count -eq 0) $(if ($notRed.Count -eq 0) { 'M1..M11 todos vermelhos DENTRO do log (exit: 1, falha >= 1)' } else { 'sem vermelho: ' + ($notRed -join ',') })
Check 'MUT-LOGEXIT' (([regex]::Matches($mutLog, '(?m)^M\S+ RUN wrapper=\d+ logExit=1 ')).Count -eq 12) '12/12 ciclos com logExit=1 lido no cabecalho do log (S-22)'

# Each cycle's EXPECT anchors must all appear in that cycle's RED-AC line: the config test that
# claims the broken behaviour is the one that failed (not some neighbouring suite).
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
Catch-Ac 'M1'
Catch-Ac 'M2'
Catch-Ac 'M3'
Catch-Ac 'M4a'
Catch-Ac 'M4b'
Catch-Ac 'M5'
Catch-Ac 'M6'
Catch-Ac 'M7'
Catch-Ac 'M8'
Catch-Ac 'M9'
Catch-Ac 'M10'
Catch-Ac 'M11'
Check 'MUT-EXPECT' (([regex]::Matches($mutLog, '(?m)^M\S+ EXPECT-OK ')).Count -eq 12 -and ($mutLog -notmatch 'EXPECT-MISS')) '12/12 EXPECT-OK e nenhum EXPECT-MISS'
Check 'MUT-CLASSIFICACAO' (([regex]::Matches($mutLog, 'CLASSIFICACAO-ERRADA')).Count -eq 0) 'nenhuma CLASSIFICACAO-ERRADA (todo vermelho e falha de produto)'

$restoreOk = ([regex]::Matches($mutLog, 'RESTORE-OK hash=PRE residue=0')).Count
Check 'MUT-RESTORE' ($restoreOk -ge 12) ("RESTORE-OK = {0} (esperado >= 12: doze ciclos can-fail)" -f $restoreOk)
Check 'MUT-RESTOREFAIL' (-not ($mutLog -match 'RESTORE-FAIL')) 'nenhum RESTORE-FAIL'
Check 'MUT-GREEN' ($greenOk) 'GREEN 4/4 sobre as fontes restauradas (wrapper=0 logExit=0 aprovado=4 falha=0)'
Check 'MUT-SUMMARY' ($mutLog -match 'SUMMARY cycles=12 failed=0 residue-QA-MUTATION=0 temp-backups=0') 'SUMMARY cycles=12 failed=0 residue=0 temp-backups=0'

# 5. Residue: no QA-MUTATION marker left in the backend (cs, no bin/obj), no temp backup.
$residue = Get-ChildItem -LiteralPath (Join-Path $repo 'backend') -Recurse -File -Include '*.cs' |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    Select-String -Pattern 'QA-MUTATION' -SimpleMatch
Check 'RESIDUE' (@($residue).Count -eq 0) ("QA-MUTATION em backend = {0}" -f @($residue).Count)
$backups = @(Get-ChildItem -Path $env:TEMP -Filter 'qa0041-*' -File -ErrorAction SilentlyContinue)
Check 'TEMP-BACKUPS' ($backups.Count -eq 0) ("backups qa0041-* no TEMP = {0}" -f $backups.Count)

# 6. The driver is ASCII-only (no BOM trap) and -VerifyOnly ran against pristine sources.
$bytes = [System.IO.File]::ReadAllBytes((Join-Path $PSScriptRoot 'qa-mutate-task-0041.ps1'))
$nonAscii = @($bytes | Where-Object { $_ -gt 127 }).Count
Check 'DRIVER-ASCII' ($nonAscii -eq 0) ("qa-mutate-task-0041.ps1 sem byte > 127 ({0}) - nao depende de BOM" -f $nonAscii)
Check 'VERIFY-RECORDED' ((Test-Path -LiteralPath (Join-Path $PSScriptRoot 'QA-0041-verify.log')) -and
    ((Get-Content -LiteralPath (Join-Path $PSScriptRoot 'QA-0041-verify.log') -Raw) -match 'VERIFY total=12 bad=0')) 'QA-0041-verify.log com VERIFY total=12 bad=0'

# 7. The config test file is the four AC scenarios (AC-01..AC-04) and the product still routes
#    the four origin points through the one helper (AC-05 scope: touches only).
$cfg = Get-Content -LiteralPath (Join-Path $repo 'backend\tests\Squid.Tests\Discovery\CanonicalOriginConfigTests.cs') -Raw
Check 'CFG-FACTS' (([regex]::Matches($cfg, '\[Fact\]')).Count -eq 4) 'CanonicalOriginConfigTests com 4 [Fact] (AC-01..AC-04)'
Check 'CFG-AC-MARKERS' ($cfg -match '// AC-01' -and $cfg -match '// AC-02' -and $cfg -match '// AC-03' -and $cfg -match '// AC-04') 'os quatro Fact marcados // AC-01..// AC-04'
$helperCalls = 0
foreach ($rel in 'backend\src\Squid.Api\Discovery\DiscoveryEndpoints.cs', 'backend\src\Squid.Api\Posts\PostEndpoints.cs', 'backend\src\Squid.Api\Containers\ContainerEndpoints.cs', 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs') {
    $src = Get-Content -LiteralPath (Join-Path $repo $rel) -Raw
    if ($src -match 'Origin\(HttpContext context\) => CanonicalOrigin\.Of\(context\)') { $helperCalls++ }
}
Check 'FOUR-POINTS' ($helperCalls -eq 4) ("os quatro pontos com Origin => CanonicalOrigin.Of: {0}/4" -f $helperCalls)
# $host is a read-only automatic variable in PowerShell: name the source $hostSrc.
$hostSrc = Get-Content -LiteralPath (Join-Path $repo 'backend\src\Squid.Service\SquidServiceHost.cs') -Raw
Check 'TRUST-GATE' ($hostSrc -match 'configuration\["Instance:ForwardedHeaders"\]' -and $hostSrc -match 'if \(trustProxy\)' -and $hostSrc -match 'app\.UseForwardedHeaders\(\)') 'UseForwardedHeaders instalado so dentro de if (trustProxy) com a chave Instance:ForwardedHeaders'

# 8. Scope (AC-05): git status backend EMPTY (byte-identical to 9210469), the scope log records
#    blob-identity, mtime, residue and touches, and nothing outside docs/sdd was touched.
$backendStatus = @(git -C $repo status --porcelain -uall -- backend)
Check 'GIT-BACKEND' ($backendStatus.Count -eq 0) ("git status backend = {0} entradas" -f $backendStatus.Count)
$scopePath = Join-Path $PSScriptRoot 'QA-0041-scope.log'
$sc = Get-Content -LiteralPath $scopePath -Raw
Check 'SCOPE-BLOB' (([regex]::Matches($sc, 'match=True')).Count -eq 6) 'blob-identity work=HEAD em 6/6 arquivos mutados (git hash-object vs rev-parse)'
Check 'SCOPE-MTIME' ($sc -match 'mtime-count=6 \(esperado 6') 'mtime desde a baseline = exatamente os 6 arquivos mutados (restaurados)'
Check 'SCOPE-RESIDUE' ($sc -match 'residue=0') 'scope scan: residuo QA-MUTATION = 0'
Check 'SCOPE-TOUCHES' ($sc -match 'fora-dos-touches=0') 'scope scan: nenhum arquivo de produto fora dos touches alterado'
# Nothing NEW outside docs/sdd since the baseline: every remaining entry must be either the
# pre-existing ODEV docs WIP captured at 15:41 or this QA's own artifacts in docs/sdd/qa.
$baseRaw2 = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'QA-0041-git-baseline.txt'))
$baselineSet = @()
$inStatus2 = $false
foreach ($l in $baseRaw2) {
    if ($l -match '^status-entries:') { $inStatus2 = $true; continue }
    if ($l -match '^wsl-dotnet:') { $inStatus2 = $false; continue }
    if ($inStatus2 -and $l -match '^\s{2,}\S' -and $l -match 'docs/') { $baselineSet += ($l -replace '^\s+', '') }
}
$all2 = @(git -C $repo status --porcelain -uall | ForEach-Object { $_ -replace '^\s+', '' })
$outside2 = @($all2 | Where-Object {
    $e = $_
    ($baselineSet -notcontains $e) -and ($e -notmatch 'docs/sdd/(qa/(QA-0041|qa-check-task-0041|qa-mutate-task-0041)|tasks/TASK-0041)')
})
Check 'SCOPE-GIT-SCOPE' ($outside2.Count -eq 0) ("git status fora do WIP pre-existente e dos artefatos da QA = {0} entradas (esperado 0: nem produto, nem outra task)" -f $outside2.Count)

# 9. The flow state machine accepts the task file with the QA's appended lines (exit 0).
$state = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'docs\sdd\_tools\sdd-state.ps1') check $task 2>&1 | Out-String
Check 'SDD-STATE' ($LASTEXITCODE -eq 0) (($state -replace '\s+', ' ').Trim())

# 10. The QA doc exists with the verdict this checker is verifying.
$doc = Join-Path $PSScriptRoot 'QA-0041-dominio-canonico-e-proxy.md'
Check 'QA-DOC' ((Test-Path -LiteralPath $doc) -and ((Get-Content -LiteralPath $doc -Raw) -match '(?m)^- verdict: \**PASSED')) 'QA-0041-dominio-canonico-e-proxy.md com verdict PASSED'

Write-Output "SUMMARY total=$total fails=$fails"
if ($fails -gt 0) { exit 1 }
exit 0
