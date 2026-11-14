# QA-0044 evidence checker (static; it does not re-run the suite). Twenty can-fail cycles:
# T1 AC-01 named destination, T2 AC-02 id window, T3..T6 AC-03 contract order (digest, size,
# depth, signature), T7..T8 AC-04 quota and its 60 default, T9..T14 AC-05 (Follow, Accept,
# Undo, Delete, Like, Flag), T15..T17 AC-06 (membersOnly, private, metric), T18..T19 AC-07
# (SHA-256 gate, SSRF guard) and T20 D-01 (named destination only, no fallback).
# Every exit and summary is read from the LOG FILES (S-22 - never from a wrapper's console
# code), every restore from the recorded hashes (S-16) and the final tree from git.
# ASCII-only on purpose: this file runs without a UTF-8 BOM under Windows PowerShell 5.1.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0044.ps1
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$runs = Join-Path $repo 'docs\sdd\runs\test-runs'
$task = Join-Path $repo 'docs\sdd\tasks\TASK-0044-inbox-verificacao-e-aplicacao.md'
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
$base = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'QA-0044-git-baseline.txt') -Raw
Check 'BASELINE-HEAD' ($base -match 'HEAD: 92db2c7413c3a1a4c92c63b09bbbffe0c7dbba1a') 'baseline HEAD = 92db2c7'
Check 'BASELINE-BRANCH' ($base -match 'branch: task/TASK-0044-inbox-verificacao-e-aplicacao') 'branch task/TASK-0044-inbox-verificacao-e-aplicacao'
Check 'BASELINE-WSL' ($base -match 'wsl command -v dotnet => \[\] exit=1') 'dotnet ausente no WSL registrado na baseline (suite Linux = unverified)'

# The DEV commit touches only the six glob sets of the task's touches (the task file's own
# Log/Evidence rows of the dispatch were stamped by ODEV outside the commit). Anything else
# in 92db2c7 would break AC-08.
$inList = $false; $outside = @(); $files = @()
foreach ($line in ($base -split "`n")) {
    if ($line -match '^--- commit files ---') { $inList = $true; continue }
    if ($inList -and $line -match '^--- ') { break }
    if ($inList -and $line.Trim()) {
        $path = $line.Trim()
        $files += $path
        if ($path -notmatch '^(backend/src/Squid\.Grains/Inbox/|backend/src/Squid\.Api/Federation/|backend/src/Squid\.Api/Posts/|backend/src/Squid\.Protocols/|backend/src/Squid\.Service/|backend/tests/Squid\.Tests/)') {
            $outside += $path
        }
    }
}
Check 'COMMIT-TOUCHES' ($outside.Count -eq 0 -and $files.Count -eq 24) $(if ($outside.Count -eq 0) { '92db2c7: 24 arquivos, todos nos touches (produto + testes)' } else { 'fora dos touches: ' + ($outside -join '; ') })

# 2. This QA's own baseline run of the 24 federation scenarios before any write, and the
#    green run of the 26 scenarios after the QA gap tests entered the filter.
$bf = Join-Path $runs '20261113-225032-test-filter.log'
$bt = if (Test-Path -LiteralPath $bf) { Get-Content -LiteralPath $bf -Raw } else { '' }
Check 'BASELINE-FILTER' ($bt -match '(?m)^exit:\s+0\s*$' -and $bt -match 'Com falha:\s*0,\s+Aprovado:\s*24,\s+Ignorado:\s*0,\s+Total:\s*24') '20261113-225032-test-filter.log exit 0 / 24 de 24 (antes de qualquer escrita da QA)'
$qf = Join-Path $runs '20261113-225836-test-filter.log'
$qt = if (Test-Path -LiteralPath $qf) { Get-Content -LiteralPath $qf -Raw } else { '' }
Check 'QA-TEST-FILTER' ($qt -match '(?m)^exit:\s+0\s*$' -and $qt -match 'Com falha:\s*0,\s+Aprovado:\s*26,\s+Ignorado:\s*0,\s+Total:\s*26') '20261113-225836-test-filter.log exit 0 / 26 de 26 (2 testes novos da QA executados)'

# 3. The can-fail driver self-check recorded before the run.
$vf = Join-Path $PSScriptRoot 'QA-0044-verify.log'
$vt = if (Test-Path -LiteralPath $vf) { Get-Content -LiteralPath $vf -Raw } else { '' }
Check 'VERIFY-RUN' ($vt -match 'VERIFY total=20 bad=0') 'QA-0044-verify.log VERIFY total=20 bad=0'
Check 'VERIFY-HITS' (([regex]::Matches($vt, 'hits=1 ok=true')).Count -eq 20 -and ($vt -match 'non-ascii-bytes=0 ok=true')) '20/20 padroes hits=1 + ASCII do driver OK'

# 4. AC-08 gate: exit header + summary INSIDE the gate log (S-22), 527 DEV + 2 QA tests, and
#    the gate is NEWER than the GREEN of the mutation run (it ran over the restored tree).
$gate = Newest '*-test-gate.log'
$gt = if ($gate) { Get-Content -LiteralPath $gate.FullName -Raw } else { '' }
Check 'GATE-EXIT' ($gt -match '(?m)^exit:\s+0\s*$') $(if ($gate) { '{0} header exit 0' -f $gate.Name } else { 'nenhum gate log' })
Check 'GATE-SUMMARY' ($gt -match 'Com falha:\s*0,\s+Aprovado:\s*529,\s+Ignorado:\s*0,\s+Total:\s*529') 'Com falha 0 / Aprovado 529 / Total 529 (527 do DEV + 2 da QA)'

$mutLog = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'QA-0044-mutations.log') -Raw
$greenLine = [regex]::Match($mutLog, '(?m)^GREEN wrapper=0 logExit=0 aprovado=26 falha=0 log=(\S+)')
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
$ids = 'T1', 'T2', 'T3', 'T4', 'T5', 'T6', 'T7', 'T8', 'T9', 'T10', 'T11', 'T12', 'T13', 'T14', 'T15', 'T16', 'T17', 'T18', 'T19', 'T20'
$missing = @()
foreach ($id in $ids) {
    if ($mutLog -notmatch [regex]::Escape("$id PRE ")) { $missing += $id }
}
Check 'MUT-IDS' ($missing.Count -eq 0) $(if ($missing.Count -eq 0) { 'T1..T20 (20 ciclos) todos com linha PRE' } else { 'faltando: ' + ($missing -join ',') })
Check 'MUT-HEAD-MATCH' (([regex]::Matches($mutLog, '(?m)^T\d+ PRE .* head-match=true find-hits=1')).Count -eq 20) '20/20 PRE com hash = blob do commit 92db2c7 (head-match=true)'

$notRed = @()
foreach ($id in $ids) {
    # S-22: the wrapper console can misreport (it prints 3 for a real red); logExit is the truth.
    $m = [regex]::Match($mutLog, ("(?m)^{0} RUN wrapper=\d+ logExit=1 aprovado=\d+ falha=(\d+)" -f [regex]::Escape($id)))
    if (-not $m.Success -or [int]$m.Groups[1].Value -lt 1) { $notRed += $id }
}
Check 'MUT-RED' ($notRed.Count -eq 0) $(if ($notRed.Count -eq 0) { 'T1..T20 todos vermelhos DENTRO do log (logExit: 1, falha >= 1)' } else { 'sem vermelho: ' + ($notRed -join ',') })

# Each cycle's EXPECT anchor must appear in that cycle's RED-AC line: the AC scenario that
# claims the broken behaviour is the one that failed (not some neighbouring suite).
$expectOf = @{
    'T1' = 'A_remote_create_with_valid_headers_becomes_a_post_in_the_named_container'
    'T2' = 'The_same_activity_id_delivered_twice_is_accepted_without_reprocessing'
    'T3' = 'The_contract_order_refuses_each_step_with_the_state_intact'
    'T4' = 'The_contract_order_refuses_each_step_with_the_state_intact'
    'T5' = 'The_contract_order_refuses_each_step_with_the_state_intact'
    'T6' = 'The_contract_order_refuses_each_step_with_the_state_intact'
    'T7' = 'Over_the_per_domain_quota_the_inbox_answers_429_with_retry_after'
    'T8' = 'The_default_quota_is_sixty_per_domain_without_configuration'
    'T9' = 'A_follow_creates_the_relation_answers_accept_and_undo_removes_it'
    'T10' = 'A_follow_creates_the_relation_answers_accept_and_undo_removes_it'
    'T11' = 'A_follow_creates_the_relation_answers_accept_and_undo_removes_it'
    'T12' = 'A_remote_delete_tombstones_the_post_and_it_leaves_the_feed'
    'T13' = 'The_same_actor_liking_twice_keeps_the_count_at_one'
    'T14' = 'A_flag_files_one_denunciation_and_a_repeat_files_no_second'
    'T15' = 'A_members_only_create_applies_for_a_member_and_not_for_a_stranger'
    'T16' = 'A_private_create_is_never_applied'
    'T17' = 'An_unknown_type_is_accepted_and_counted_by_the_metric'
    'T18' = 'Remote_attachments_are_verified_by_sha256_and_a_failed_one_stays_absent'
    'T19' = 'The_production_media_client_never_touches_a_blocked_address'
    'T20' = 'A_create_without_any_named_destination_is_accepted_without_applying'
}
function Catch-Ac([string]$id) {
    $expect = $expectOf[$id]
    $acM = [regex]::Match($mutLog, ("(?m)^{0} RED-AC (?<a>.*)$" -f [regex]::Escape($id)))
    $ac = if ($acM.Success) { $acM.Groups['a'].Value.Trim() } else { '' }
    $ok = ($expect -ne '' -and $ac -ne '' -and $ac -ne 'NONE' -and ($ac -match [regex]::Escape($expect)))
    Check ("CATCH-$id") $ok $(if ($ok) { "RED-AC contem: $expect" } else { "esperado '$expect' em RED-AC '$ac'" })
}
foreach ($id in $ids) { Catch-Ac $id }
Check 'MUT-EXPECT' (([regex]::Matches($mutLog, '(?m)^T\d+ EXPECT-OK ')).Count -eq 20 -and ($mutLog -notmatch 'EXPECT-MISS')) '20/20 EXPECT-OK e nenhum EXPECT-MISS'
Check 'MUT-TOOLING' (-not ($mutLog -match 'TOOLING-FAIL|BUILD-FAIL')) 'nenhum TOOLING-FAIL ou BUILD-FAIL (todo vermelho e falha de produto)'

$restoreOk = ([regex]::Matches($mutLog, 'RESTORE-OK hash=PRE residue=0')).Count
Check 'MUT-RESTORE' ($restoreOk -ge 20) ("RESTORE-OK = {0} (esperado >= 20: vinte ciclos can-fail)" -f $restoreOk)
Check 'MUT-RESTOREFAIL' (-not ($mutLog -match 'RESTORE-FAIL')) 'nenhum RESTORE-FAIL'
Check 'MUT-GREEN' ($greenOk) 'GREEN 26/26 sobre as fontes restauradas (wrapper=0 logExit=0 aprovado=26 falha=0)'
Check 'MUT-SUMMARY' ($mutLog -match 'SUMMARY cycles=20 failed=0 residue-backend=0') 'SUMMARY cycles=20 failed=0 residue-backend=0'
Check 'MUT-UNTRACKED' ($mutLog -match 'UNTRACKED backend=1') 'UNTRACKED backend=1 (so o teste novo da QA)'

# 6. Residue: no mutant token left in the product sources, no temp backup, git backend clean.
$tokens = 'if (false) references.AddRange', 'if (await _inbox.RememberAsync(Recipient(target), activity.Id!))',
          'Status400BadRequest, "digest_invalid"', '6_291_456', 'Status406NotAcceptable, "payload_too_deep"',
          '_ => AuthErrors.Write(context, StatusCodes.Status400BadRequest,', 'window.Count >= int.MaxValue',
          'DefaultLimit = 600', '// QA-0044 T9', '// QA-0044 T11', '// QA-0044 T12', 'case "flag_disabled"',
          'MembersOnly && await MemberRecipientAsync', '// QA-0044 T16', '// QA-0044 T17',
          'qa0044-never-matches', 'IPAddress.TryParse(host, out var literal)) return true;',
          'SearchAsync("Comunidade", 1, 0)'
$hitsResidue = 0
foreach ($token in $tokens) {
    $found = Get-ChildItem -LiteralPath (Join-Path $repo 'backend') -Recurse -File -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
        Select-String -SimpleMatch -Pattern $token
    $hitsResidue += @($found).Count
}
Check 'RESIDUE' ($hitsResidue -eq 0) ("tokens mutantes em backend/src+tests (.cs, fora de bin/obj) = {0}" -f $hitsResidue)
$backups = @(Get-ChildItem -Path $env:TEMP -Filter 'qa0044-*' -File -ErrorAction SilentlyContinue)
Check 'TEMP-BACKUPS' ($backups.Count -eq 0) ("backups qa0044-* no TEMP = {0}" -f $backups.Count)
$backendStatus = @(git -C $repo status --porcelain -uall -- backend)
$qaTestEntry = '?? backend/tests/Squid.Tests/Federation/QaTask0044InboxGapTests.cs'
$gitBackendOk = ($backendStatus.Count -eq 1 -and (($backendStatus[0] -replace '^\s+', '') -eq $qaTestEntry))
Check 'GIT-BACKEND' $gitBackendOk ("git status backend = {0} entradas (esperado 1: so {1})" -f $backendStatus.Count, $qaTestEntry)

# 7. The test files are the AC scenario set (static markers the run above executed).
$tests = Get-Content -LiteralPath (Join-Path $repo 'backend\tests\Squid.Tests\Federation\InboxReceiveTests.cs') -Raw
$acMissing = @()
foreach ($n in 'AC-01', 'AC-02', 'AC-03', 'AC-05', 'AC-06', 'AC-07', 'D-01') {
    if ($tests -notmatch ('// ' + [regex]::Escape($n))) { $acMissing += $n }
}
Check 'TEST-AC' ($acMissing.Count -eq 0 -and ([regex]::Matches($tests, '\[Fact\]')).Count -eq 14) $(if ($acMissing.Count -eq 0) { 'InboxReceiveTests: 14 [Fact] marcados // AC-01..// AC-07 + // D-01' } else { 'faltando: ' + ($acMissing -join ',') })
$rl = Get-Content -LiteralPath (Join-Path $repo 'backend\tests\Squid.Tests\Federation\InboxRateLimitTests.cs') -Raw
Check 'TEST-AC04' ($rl -match '// AC-04' -and ([regex]::Matches($rl, '\[Fact\]')).Count -eq 1) 'InboxRateLimitTests: 1 [Fact] marcado // AC-04'
$qtests = Get-Content -LiteralPath (Join-Path $repo 'backend\tests\Squid.Tests\Federation\QaTask0044InboxGapTests.cs') -Raw
Check 'TEST-QA' ($qtests -match '// AC-03' -and $qtests -match '// AC-04' -and ([regex]::Matches($qtests, '\[Fact\]')).Count -eq 2) 'QaTask0044InboxGapTests: 2 [Fact] marcados // AC-03 (assinatura forjada) e // AC-04 (padrao 60)'
$svcSrc = Get-Content -LiteralPath (Join-Path $repo 'backend\src\Squid.Service\InboxPipeline.cs') -Raw
Check 'WIRE-ORDER' ($svcSrc.IndexOf('InboxSignatureGate.HasSignatureHeaders') -lt $svcSrc.IndexOf('_rate.TryAcquire') -and $svcSrc.IndexOf('_rate.TryAcquire') -lt $svcSrc.IndexOf('_inbox.RememberAsync')) 'ordem 1 assinatura -> 5 taxa -> 6 janela no InboxPipeline.ReceiveAsync'
Check 'WIRE-DESTINATIONS' ($svcSrc -match 'sem destino nomeado' -and $svcSrc -match 'MemberRecipientAsync') 'guarda de destino nomeado (D-01) e gate membersOnly presentes no produto'

# 8. Scope (AC-08): the scope log proves blob identity vs HEAD, mtime, residue and touches.
$scopePath = Join-Path $PSScriptRoot 'QA-0044-scope.log'
$sc = if (Test-Path -LiteralPath $scopePath) { Get-Content -LiteralPath $scopePath -Raw } else { '' }
Check 'SCOPE-BLOB' (([regex]::Matches($sc, 'match=True')).Count -eq 7) 'blob-identity work=HEAD em 7/7 arquivos de produto mutados (git hash-object vs rev-parse)'
Check 'SCOPE-MTIME' ($sc -match 'mtime-count=8 \(esperado 8') 'mtime desde a baseline = os 7 arquivos mutados/restaurados + o teste novo da QA (8)'
Check 'SCOPE-RESIDUE' ($sc -match 'residue=0') 'scope scan: residuo mutante = 0'
Check 'SCOPE-TOUCHES' ($sc -match 'fora-dos-touches=0') 'scope scan: nenhum arquivo fora dos touches alterado'
Check 'SCOPE-GIT-SCOPE' ($sc -match 'git-backend-entries=1') 'git status backend = 1 entrada (so o teste novo da QA, dentro dos touches)'

# 9. The flow state machine accepts the task file untouched by QA (exit 0).
$state = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'docs\sdd\_tools\sdd-state.ps1') check $task 2>&1 | Out-String
Check 'SDD-STATE' ($LASTEXITCODE -eq 0) (($state -replace '\s+', ' ').Trim())

# 10. The QA doc exists with the verdict this checker is verifying, and all eight ACs in it.
$doc = Join-Path $PSScriptRoot 'QA-0044-inbox-verificacao-e-aplicacao.md'
$dt = if (Test-Path -LiteralPath $doc) { Get-Content -LiteralPath $doc -Raw } else { '' }
Check 'QA-DOC' ($dt -match '(?m)^- verdict: \**PASSED') 'QA-0044-inbox-verificacao-e-aplicacao.md com verdict PASSED'
$docAcMissing = @()
foreach ($n in 'AC-01', 'AC-02', 'AC-03', 'AC-04', 'AC-05', 'AC-06', 'AC-07', 'AC-08') {
    if ($dt -notmatch [regex]::Escape("| $n |")) { $docAcMissing += $n }
}
Check 'QA-DOC-ACS' ($docAcMissing.Count -eq 0) $(if ($docAcMissing.Count -eq 0) { 'AC-01..AC-08 presentes na tabela do doc' } else { 'faltando: ' + ($docAcMissing -join ',') })

Write-Output "SUMMARY total=$total fails=$fails"
if ($fails -gt 0) { exit 1 }
exit 0
