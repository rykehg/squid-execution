# QA-0045 evidence checker (static; it does not re-run the suite). Ten can-fail cycles:
# T1 AC-01 destination naming (audience), T2 AC-01 container collection, T3..T5 AC-02
# (unlisted, membersOnly, private), T6..T7 AC-03 (enqueue + session gate), T8 AC-04 (R-05
# re-evaluation), T9 AC-05 (born Pending), T10 AC-06 (Delete recipients). Every exit and
# summary is read from the LOG FILES (S-22 - never from a wrapper's console code), every
# restore from the recorded hashes (S-16) and the final tree from git.
# ASCII-only on purpose: this file runs without a UTF-8 BOM under Windows PowerShell 5.1.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0045.ps1
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$runs = Join-Path $repo 'docs\sdd\runs\test-runs'
$task = Join-Path $repo 'docs\sdd\tasks\TASK-0045-saida-fan-out-e-follow.md'
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
$base = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'QA-0045-git-baseline.txt') -Raw
Check 'BASELINE-HEAD' ($base -match 'HEAD: 3226429461b5790ece123cdae6c350b6bb2df3c3') 'baseline HEAD = 3226429'
Check 'BASELINE-BRANCH' ($base -match 'branch: task/TASK-0045-saida-fan-out-e-follow') 'branch task/TASK-0045-saida-fan-out-e-follow'
Check 'BASELINE-WSL' ($base -match 'wsl command -v dotnet => \[\] exit=1') 'dotnet ausente no WSL registrado na baseline (suite Linux = unverified)'

# The DEV commit touches only the eight glob sets of the task's touches. Anything else in
# 3226429 would break AC-07.
$inList = $false; $outside = @(); $files = @()
foreach ($line in ($base -split "`n")) {
    if ($line -match '^--- commit files') { $inList = $true; continue }
    if ($inList -and $line -match '^--- ') { break }
    if ($inList -and $line.Trim() -and $line -notmatch '^commit [0-9a-f]{7,} ') {
        $path = $line.Trim()
        $files += $path
        if ($path -notmatch '^(backend/src/Squid\.Persistence/schema/|backend/src/Squid\.Api/Posts/|backend/src/Squid\.Api/Federation/|backend/src/Squid\.Api/Containers/|backend/src/Squid\.ReadModels/|backend/src/Squid\.Protocols/|backend/src/Squid\.Service/|backend/tests/Squid\.Tests/)') {
            $outside += $path
        }
    }
}
Check 'COMMIT-TOUCHES' ($outside.Count -eq 0 -and $files.Count -eq 16) $(if ($outside.Count -eq 0) { '3226429: 16 arquivos, todos nos touches (produto + testes)' } else { 'fora dos touches: ' + ($outside -join '; ') })

# 2. This QA's baseline run of the 35 federation scenarios before any write, and the green
#    run of the 36 scenarios after the QA gap test entered the filter.
$bf = Join-Path $runs '20261114-010209-test-filter.log'
$bt = if (Test-Path -LiteralPath $bf) { Get-Content -LiteralPath $bf -Raw } else { '' }
Check 'BASELINE-FILTER' ($bt -match '(?m)^exit:\s+0\s*$' -and $bt -match 'Com falha:\s*0,\s+Aprovado:\s*35,\s+Ignorado:\s*0,\s+Total:\s*35') '20261114-010209-test-filter.log exit 0 / 35 de 35 (antes de qualquer escrita da QA)'
$qf = Join-Path $runs '20261114-010649-test-filter.log'
$qt = if (Test-Path -LiteralPath $qf) { Get-Content -LiteralPath $qf -Raw } else { '' }
Check 'QA-TEST-FILTER' ($qt -match '(?m)^exit:\s+0\s*$' -and $qt -match 'Com falha:\s*0,\s+Aprovado:\s*36,\s+Ignorado:\s*0,\s+Total:\s*36') '20261114-010649-test-filter.log exit 0 / 36 de 36 (o teste novo da QA executado e verde)'

# 3. The can-fail driver self-check recorded before the run.
$vf = Join-Path $PSScriptRoot 'QA-0045-verify.log'
$vt = if (Test-Path -LiteralPath $vf) { Get-Content -LiteralPath $vf -Raw } else { '' }
Check 'VERIFY-RUN' ($vt -match 'VERIFY total=10 bad=0') 'QA-0045-verify.log VERIFY total=10 bad=0'
Check 'VERIFY-HITS' (([regex]::Matches($vt, 'hits=1 hits2=1 ok=true')).Count -eq 10 -and ($vt -match 'non-ascii-bytes=0 ok=true')) '10/10 padroes hits=1 + ASCII do driver OK'

# 4. AC-07 gate: exit header + summary INSIDE the gate log (S-22), 538 DEV + 1 QA tests, and
#    the gate is NEWER than the GREEN of the mutation run (it ran over the restored tree).
$gate = Newest '*-test-gate.log'
$gt = if ($gate) { Get-Content -LiteralPath $gate.FullName -Raw } else { '' }
Check 'GATE-EXIT' ($gt -match '(?m)^exit:\s+0\s*$') $(if ($gate) { '{0} header exit 0' -f $gate.Name } else { 'nenhum gate log' })
Check 'GATE-SUMMARY' ($gt -match 'Com falha:\s*0,\s+Aprovado:\s*539,\s+Ignorado:\s*0,\s+Total:\s*539') 'Com falha 0 / Aprovado 539 / Total 539 (538 do DEV + 1 da QA)'

$mutLog = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'QA-0045-mutations.log') -Raw
$greenLine = [regex]::Match($mutLog, '(?m)^GREEN wrapper=0 logExit=0 aprovado=36 falha=0 log=(\S+)')
$greenOk = $greenLine.Success
if ($greenOk -and $gate) {
    $gpath = Join-Path $runs $greenLine.Groups[1].Value
    Check 'GATE-AGE' ((Test-Path -LiteralPath $gpath) -and ($gate.LastWriteTime -gt (Get-Item -LiteralPath $gpath).LastWriteTime)) ('{0} criado depois do GREEN {1}' -f $gate.Name, $greenLine.Groups[1].Value)
}
else {
    Check 'GATE-AGE' $false 'GREEN ou gate log ausente'
}

# 5. The mutation ledger (S-16/S-22/S-23): every cycle ran RED with its own AC's scenario
#    inside the log, every restore came back to PRE, the green over the restored sources.
$ids = 'T1', 'T2', 'T3', 'T4', 'T5', 'T6', 'T7', 'T8', 'T9', 'T10'
$missing = @()
foreach ($id in $ids) {
    if ($mutLog -notmatch [regex]::Escape("$id PRE ")) { $missing += $id }
}
Check 'MUT-IDS' ($missing.Count -eq 0) $(if ($missing.Count -eq 0) { 'T1..T10 (10 ciclos) todos com linha PRE' } else { 'faltando: ' + ($missing -join ',') })
Check 'MUT-HEAD-MATCH' (([regex]::Matches($mutLog, '(?m)^T\d+ PRE .* head-match=true find-hits=1')).Count -eq 10) '10/10 PRE com hash = blob do commit 3226429 (head-match=true)'

$notRed = @()
foreach ($id in $ids) {
    # S-22: the wrapper console can misreport (it prints 3 for a real red); logExit is the truth.
    $m = [regex]::Match($mutLog, ("(?m)^{0} RUN wrapper=\d+ logExit=1 aprovado=\d+ falha=(\d+)" -f [regex]::Escape($id)))
    if (-not $m.Success -or [int]$m.Groups[1].Value -lt 1) { $notRed += $id }
}
Check 'MUT-RED' ($notRed.Count -eq 0) $(if ($notRed.Count -eq 0) { 'T1..T10 todos vermelhos DENTRO do log (logExit: 1, falha >= 1)' } else { 'sem vermelho: ' + ($notRed -join ',') })

# Each cycle's EXPECT anchor must appear in that cycle's RED-AC line: the AC scenario that
# claims the broken behaviour is the one that failed (not some neighbouring suite).
$expectOf = @{
    'T1'  = 'Ac01_public_post_enqueues_one_delivery_per_remote_follower_and_names_the_destination'
    'T2'  = 'Ac01_public_post_also_enqueues_for_the_container_collection'
    'T3'  = 'Ac02_unlisted_delivers_only_to_followers'
    'T4'  = 'Ac02_members_only_delivers_only_to_active_members'
    'T5'  = 'Ac02_private_creates_no_delivery_at_all'
    'T6'  = 'Ac03_follow_route_enqueues_a_signed_follow_and_the_relation_settles_on_accept'
    'T7'  = 'Ac03_follow_route_enqueues_a_signed_follow_and_the_relation_settles_on_accept'
    'T8'  = 'Ac04_policy_change_before_send_cancels_with_a_reason_and_sends_nothing'
    'T9'  = 'Ac05_publish_returns_with_every_delivery_already_pending_and_the_wire_untouched'
    'T10' = 'Ac06_removing_the_post_propagates_a_signed_delete_to_the_recipients_that_received_the_create'
}
function Catch-Ac([string]$id) {
    $expect = $expectOf[$id]
    $acM = [regex]::Match($mutLog, ("(?m)^{0} RED-AC (?<a>.*)$" -f [regex]::Escape($id)))
    $ac = if ($acM.Success) { $acM.Groups['a'].Value.Trim() } else { '' }
    $ok = ($expect -ne '' -and $ac -ne '' -and $ac -ne 'NONE' -and ($ac -match [regex]::Escape($expect)))
    Check ("CATCH-$id") $ok $(if ($ok) { "RED-AC contem: $expect" } else { "esperado '$expect' em RED-AC '$ac'" })
}
foreach ($id in $ids) { Catch-Ac $id }
Check 'MUT-EXPECT' (([regex]::Matches($mutLog, '(?m)^T\d+ EXPECT-OK ')).Count -eq 10 -and ($mutLog -notmatch 'EXPECT-MISS')) '10/10 EXPECT-OK e nenhum EXPECT-MISS'
Check 'MUT-TOOLING' (-not ($mutLog -match 'TOOLING-FAIL|BUILD-FAIL')) 'nenhum TOOLING-FAIL ou BUILD-FAIL (todo vermelho e falha de produto)'

$restoreOk = ([regex]::Matches($mutLog, 'RESTORE-OK hash=PRE residue=0')).Count
Check 'MUT-RESTORE' ($restoreOk -ge 10) ("RESTORE-OK = {0} (esperado >= 10: dez ciclos can-fail)" -f $restoreOk)
Check 'MUT-RESTOREFAIL' (-not ($mutLog -match 'RESTORE-FAIL')) 'nenhum RESTORE-FAIL'
Check 'MUT-GREEN' ($greenOk) 'GREEN 36/36 sobre as fontes restauradas (wrapper=0 logExit=0 aprovado=36 falha=0)'
Check 'MUT-SUMMARY' ($mutLog -match 'SUMMARY cycles=10 failed=0 residue-backend=0') 'SUMMARY cycles=10 failed=0 residue-backend=0'
Check 'MUT-UNTRACKED' ($mutLog -match 'UNTRACKED backend=1') 'UNTRACKED backend=1 (so o teste novo da QA)'

# 6. Residue: no mutant token left in the product sources, no temp backup, git backend clean.
$tokens = 'qa0045-broken', '// QA-0045 T2', '// QA-0045 T3', '// QA-0045 T4', '// QA-0045 T5',
          '// QA-0045 T6', '// QA-0045 T7', '// QA-0045 T8', '// QA-0045 T9', '// QA-0045 T10',
          '_ => followers,', '.Where(_ => true)', 'VisibilityPolicy.Unlisted => [.. followers,',
          'VisibilityPolicy.Public => followers,'
$hitsResidue = 0
foreach ($token in $tokens) {
    $found = Get-ChildItem -LiteralPath (Join-Path $repo 'backend') -Recurse -File -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
        Select-String -SimpleMatch -Pattern $token
    $hitsResidue += @($found).Count
}
Check 'RESIDUE' ($hitsResidue -eq 0) ("tokens mutantes em backend/src+tests (.cs, fora de bin/obj) = {0}" -f $hitsResidue)
$backups = @(Get-ChildItem -Path $env:TEMP -Filter 'qa0045-*' -File -ErrorAction SilentlyContinue)
Check 'TEMP-BACKUPS' ($backups.Count -eq 0) ("backups qa0045-* no TEMP = {0}" -f $backups.Count)
$backendStatus = @(git -C $repo status --porcelain -uall -- backend)
$qaTestEntry = '?? backend/tests/Squid.Tests/Federation/QaTask0045FanOutGapTests.cs'
$gitBackendOk = ($backendStatus.Count -eq 1 -and (($backendStatus[0] -replace '^\s+', '') -eq $qaTestEntry))
Check 'GIT-BACKEND' $gitBackendOk ("git status backend = {0} entradas (esperado 1: so {1})" -f $backendStatus.Count, $qaTestEntry)

# 7. The test files are the AC scenario set (static markers the run above executed).
$dev = Get-Content -LiteralPath (Join-Path $repo 'backend\tests\Squid.Tests\Federation\OutboundFanOutTests.cs') -Raw
$acMissing = @()
foreach ($n in 'AC-01', 'AC-02', 'AC-03', 'AC-04', 'AC-05', 'AC-06') {
    if ($dev -notmatch ('// ' + [regex]::Escape($n))) { $acMissing += $n }
}
Check 'TEST-AC' ($acMissing.Count -eq 0 -and ([regex]::Matches($dev, '\[Fact\]')).Count -eq 8) $(if ($acMissing.Count -eq 0) { 'OutboundFanOutTests: 8 [Fact] marcados // AC-01..// AC-06' } else { 'faltando: ' + ($acMissing -join ',') })
$mig = Get-Content -LiteralPath (Join-Path $repo 'backend\tests\Squid.Tests\Federation\FederationMigrationTests.cs') -Raw
Check 'TEST-MIGRATION' (([regex]::Matches($mig, '\[Fact\]')).Count -eq 1 -and $mig -match '0008') 'FederationMigrationTests: 1 [Fact] da migration 0008'
$qtests = Get-Content -LiteralPath (Join-Path $repo 'backend\tests\Squid.Tests\Federation\QaTask0045FanOutGapTests.cs') -Raw
Check 'TEST-QA' ($qtests -match '// AC-01' -and ([regex]::Matches($qtests, '\[Fact\]')).Count -eq 1) 'QaTask0045FanOutGapTests: 1 [Fact] marcado // AC-01 (colecao do container) - 1 fato'
$objSrc = Get-Content -LiteralPath (Join-Path $repo 'backend\src\Squid.Protocols\ActivityPub\ObjectJsonLd.cs') -Raw
$svcSrc = Get-Content -LiteralPath (Join-Path $repo 'backend\src\Squid.Service\OutboundPipeline.cs') -Raw
Check 'WIRE-AUDIENCE' ($objSrc -match 'PropertyName\("audience"\)') 'o Create de saida carrega audience (GAP-48 / D-01) no produto'
Check 'WIRE-R05' ($svcSrc -match 'FederationMetrics\.DeliveryCancelled\(\)' -and $svcSrc -match 'PeerDeliveryState\.Cancelled') 'a reavaliacao R-05 cancela com motivo e conta a metrica no produto'

# 8. Scope (AC-07): the scope log proves blob identity vs HEAD, mtime, residue and touches.
$scopePath = Join-Path $PSScriptRoot 'QA-0045-scope.log'
$sc = if (Test-Path -LiteralPath $scopePath) { Get-Content -LiteralPath $scopePath -Raw } else { '' }
Check 'SCOPE-BLOB' (([regex]::Matches($sc, 'match=True')).Count -eq 3) 'blob-identity work=HEAD em 3/3 arquivos de produto mutados (git hash-object vs rev-parse)'
Check 'SCOPE-MTIME' ($sc -match 'mtime-count=4 \(esperado 4') 'mtime desde a baseline = os 3 arquivos mutados/restaurados + o teste novo da QA (4)'
Check 'SCOPE-RESIDUE' ($sc -match 'residue=0') 'scope scan: residuo mutante = 0'
Check 'SCOPE-TOUCHES' ($sc -match 'fora-dos-touches=0') 'scope scan: nenhum arquivo fora dos touches alterado'
Check 'SCOPE-GIT-SCOPE' ($sc -match 'git-backend-entries=1') 'git status backend = 1 entrada (so o teste novo da QA, dentro dos touches)'

# 9. The flow state machine accepts the task file untouched by QA (exit 0).
$state = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'docs\sdd\_tools\sdd-state.ps1') check $task 2>&1 | Out-String
Check 'SDD-STATE' ($LASTEXITCODE -eq 0) (($state -replace '\s+', ' ').Trim())

# 10. The QA doc exists with the verdict this checker is verifying, and all seven ACs in it.
$doc = Join-Path $PSScriptRoot 'QA-0045-saida-fan-out-e-follow.md'
$dt = if (Test-Path -LiteralPath $doc) { Get-Content -LiteralPath $doc -Raw } else { '' }
Check 'QA-DOC' ($dt -match '(?m)^- verdict: \**PASSED') 'QA-0045-saida-fan-out-e-follow.md com verdict PASSED'
$docAcMissing = @()
foreach ($n in 'AC-01', 'AC-02', 'AC-03', 'AC-04', 'AC-05', 'AC-06', 'AC-07') {
    if ($dt -notmatch [regex]::Escape("| $n |")) { $docAcMissing += $n }
}
Check 'QA-DOC-ACS' ($docAcMissing.Count -eq 0) $(if ($docAcMissing.Count -eq 0) { 'AC-01..AC-07 presentes na tabela do doc' } else { 'faltando: ' + ($docAcMissing -join ',') })

Write-Output "SUMMARY total=$total fails=$fails"
if ($fails -gt 0) { exit 1 }
exit 0
