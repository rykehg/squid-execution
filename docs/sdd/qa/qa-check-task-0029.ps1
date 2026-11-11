# QA-0029 evidence checker - TASK-0029 (link unfurl + guarda SSRF).
# Ten checks over the artifacts this QA actually produced; every verdict comes from a file,
# never from a terminal scrollback (S-22). Linux twin: qa-check-task-0029.sh (same checks).
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0029.ps1
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$script:total = 0
$script:fail = 0

function Check([string]$id, [bool]$ok, [string]$detail) {
    $script:total++
    if ($ok) {
        Write-Output ("CHECK {0} PASS {1}" -f $id, $detail)
    } else {
        $script:fail++
        Write-Output ("CHECK {0} FAIL {1}" -f $id, $detail)
    }
}

$runDir = Join-Path $repo 'docs\sdd\runs\test-runs'
$qaDir = Join-Path $repo 'docs\sdd\qa'

# C1 - final gate of AC-05: read inside the newest gate log, never the wrapper console.
$gate = Get-ChildItem -LiteralPath $runDir -Filter '*-test-gate.log' | Sort-Object Name -Descending | Select-Object -First 1
$gateText = Get-Content -LiteralPath $gate.FullName -Raw
Check 'C1' (($gateText -match '(?m)^exit:\s+0') -and ($gateText -match 'Com falha:\s+0,\s+Aprovado:\s+411,\s+Ignorado:\s+0,\s+Total:\s+411')) `
    ("gate final {0}: exit 0 e 411/411" -f $gate.Name)

# C2 - the QA baseline gate (before QA's own tests): 398/398 exit 0.
$baseText = Get-Content -LiteralPath (Join-Path $runDir '20261110-172026-test-gate.log') -Raw
Check 'C2' (($baseText -match '(?m)^exit:\s+0') -and ($baseText -match 'Com falha:\s+0,\s+Aprovado:\s+398')) `
    'gate baseline da QA: 398/398 exit 0'

# C3 - mutation driver summary: six cycles, all proven red, all restored, no residue.
$mut = Get-Content -LiteralPath (Join-Path $qaDir 'QA-0029-mutations.log') -Raw
Check 'C3' ($mut -match 'SUMMARY cycles=6 failed=0 residue-QA-MUTATION=0 temp-backups=0') `
    'SUMMARY cycles=6 failed=0 residue=0 backups=0'

# C4 - one PRE + one red-in-log + one restore per cycle, and one green after the restores.
Check 'C4' (([regex]::Matches($mut, '(?m)^M\d PRE ')).Count -eq 6 -and `
    ([regex]::Matches($mut, '(?m)^M\d RED-IN-LOG ')).Count -eq 6 -and `
    ([regex]::Matches($mut, '(?m)^M\d RESTORE-OK hash=PRE residue=0')).Count -eq 6 -and `
    ([regex]::Matches($mut, '(?m)^GREEN wrapper=0 logExit=0 aprovado=63 falha=0')).Count -eq 1) `
    '6 PRE + 6 vermelhos classificados no log + 6 restores byte-exact + GREEN 63/63'

# C5 - no QA-MUTATION marker left in any source file.
$residue = Get-ChildItem -LiteralPath (Join-Path $repo 'backend') -Recurse -File -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    Select-String -Pattern 'QA-MUTATION' -SimpleMatch
Check 'C5' (@($residue).Count -eq 0) ("residuos QA-MUTATION em backend = {0}" -f @($residue).Count)

# C6 - scope: the only git-visible additions are QA-0029 artifacts, nothing removed.
$old = Get-Content -LiteralPath (Join-Path $qaDir 'QA-0029-git-baseline.txt') |
    ForEach-Object { $_.Trim() } | Where-Object { $_ }
$new = (git -C $repo status --porcelain -uall) | ForEach-Object { $_.Trim() } | Where-Object { $_ }
$added = @($new | Where-Object { $old -notcontains $_ })
$removed = @($old | Where-Object { $new -notcontains $_ })
$bad = @($added | Where-Object { $_ -notmatch '^(!!|\?\?)\s+(backend/tests/Squid.Tests/Unfurl/QaTask0029Tests\.cs|docs/sdd/qa/(QA-0029|qa-[a-z]+-task-0029))' })
Check 'C6' ($removed.Count -eq 0 -and $bad.Count -eq 0) `
    ("git: +{0} entradas todas QA-0029, removidas={1}, fora do padrao={2}" -f $added.Count, $removed.Count, $bad.Count)

# C7 - the control block QA may not write is still what ODEV left.
$task = Get-Content -LiteralPath (Join-Path $repo 'docs\sdd\tasks\TASK-0029-unfurl-ssrf.md') -Raw
Check 'C7' (($task -match '(?m)^status: AWAITING_QA\r?$') -and ($task -match '(?m)^qaAttempts: 0/2\r?$')) `
    'control block intacto: status AWAITING_QA, qaAttempts 0/2'

# C8 - no test of this task can open a socket or resolve a name (AC-05).
$net = Get-ChildItem -LiteralPath (Join-Path $repo 'backend\tests\Squid.Tests\Unfurl') -File -Filter '*.cs' |
    Select-String -Pattern 'HttpClient\(|Dns\.Get|WebRequest|TcpClient|SocketsHttpHandler'
Check 'C8' (@($net).Count -eq 0) ("sem rede real nos testes da task (achados={0})" -f @($net).Count)

# C9 - the green the mutation driver ran over the restored sources.
$greenName = if ($mut -match 'GREEN wrapper=0 logExit=0 aprovado=63 falha=0 log=([\w\.-]+\.log)') { $Matches[1] } else { '' }
$greenText = if ($greenName) { Get-Content -LiteralPath (Join-Path $runDir $greenName) -Raw } else { '' }
Check 'C9' (($greenName -ne '') -and ($greenText -match '(?m)^exit:\s+0') -and ($greenText -match 'Com falha:\s+0,\s+Aprovado:\s+63')) `
    ("GREEN pos-restores: {0} exit 0 63/63" -f $greenName)

# C10 - the verdict document exists.
$doc = Get-ChildItem -LiteralPath $qaDir -Filter 'QA-0029-*.md' | Select-Object -First 1
Check 'C10' (($null -ne $doc) -and ((Get-Content -LiteralPath $doc.FullName -Raw) -match '# QA-0029')) `
    ("doc {0}" -f $(if ($doc) { $doc.Name } else { 'AUSENTE' }))

Write-Output "SUMMARY total=$($script:total) fails=$($script:fail)"
if ($script:fail -gt 0) { exit 1 }
exit 0
