# QA-0030 evidence checker - TASK-0030 (remocao de post com tombstone).
# Ten checks over the artifacts this QA actually produced; every verdict comes from a file,
# never from a terminal scrollback (S-22). Linux twin: qa-check-task-0030.sh (same checks).
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0030.ps1
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

# C1 - final gate of AC-05: read inside the newest gate log, never the wrapper console (S-22).
$gate = Get-ChildItem -LiteralPath $runDir -Filter '*-test-gate.log' | Sort-Object Name -Descending | Select-Object -First 1
$gateText = Get-Content -LiteralPath $gate.FullName -Raw
Check 'C1' (($gateText -match '(?m)^exit:\s+0') -and ($gateText -match 'Com falha:\s+0,\s+Aprovado:\s+419,\s+Ignorado:\s+0,\s+Total:\s+419')) `
    ("gate final {0}: exit 0 e 419/419" -f $gate.Name)

# C2 - the QA baseline gate (measured BEFORE QA wrote any test): 415/415 exit 0.
$baseText = Get-Content -LiteralPath (Join-Path $runDir '20261110-193406-test-gate.log') -Raw
Check 'C2' (($baseText -match '(?m)^exit:\s+0') -and ($baseText -match 'Com falha:\s+0,\s+Aprovado:\s+415')) `
    'gate baseline da QA: 415/415 exit 0'

# C3 - the four QA tests alone, first green (can-fail proven later by the mutations).
$own = Get-Content -LiteralPath (Join-Path $runDir '20261110-200450-test-filter.log') -Raw
Check 'C3' (($own -match '(?m)^exit:\s+0') -and ($own -match 'Com falha:\s+0,\s+Aprovado:\s+4,\s+Ignorado:\s+0,\s+Total:\s+4')) `
    'QaTask0030Tests: exit 0, 4/4'

# C4 - mutation driver summary: six cycles, all proven red, all restored, no residue.
$mut = Get-Content -LiteralPath (Join-Path $qaDir 'QA-0030-mutations.log') -Raw
Check 'C4' ($mut -match 'SUMMARY cycles=6 failed=0 residue-QA-MUTATION=0 temp-backups=0') `
    'SUMMARY cycles=6 failed=0 residue=0 backups=0'

# C5 - one PRE + one red-in-log + one restore per cycle, and one green after the restores.
Check 'C5' (([regex]::Matches($mut, '(?m)^M\d PRE ')).Count -eq 6 -and `
    ([regex]::Matches($mut, '(?m)^M\d RED-IN-LOG ')).Count -eq 6 -and `
    ([regex]::Matches($mut, '(?m)^M\d RESTORE-OK hash=PRE residue=0')).Count -eq 6 -and `
    ([regex]::Matches($mut, '(?m)^GREEN wrapper=0 logExit=0 aprovado=\d+ falha=0')).Count -eq 1) `
    '6 PRE + 6 vermelhos classificados no log + 6 restores byte-exact + GREEN falha=0'

# C6 - no QA-MUTATION marker left in any source file.
$residue = Get-ChildItem -LiteralPath (Join-Path $repo 'backend') -Recurse -File -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    Select-String -Pattern 'QA-MUTATION' -SimpleMatch
Check 'C6' (@($residue).Count -eq 0) ("residuos QA-MUTATION em backend = {0}" -f @($residue).Count)

# C7 - scope: the only git-visible additions are QA-0030 artifacts, nothing removed.
$old = Get-Content -LiteralPath (Join-Path $qaDir 'QA-0030-git-baseline.txt') |
    ForEach-Object { $_.Trim() } | Where-Object { $_ }
$new = (git -C $repo status --porcelain -uall) | ForEach-Object { $_.Trim() } | Where-Object { $_ }
$added = @($new | Where-Object { $old -notcontains $_ })
$removed = @($old | Where-Object { $new -notcontains $_ })
$bad = @($added | Where-Object { $_ -notmatch '^\?\?\s+(backend/tests/Squid.Tests/Posts/QaTask0030Tests\.cs|docs/sdd/qa/(QA-0030|qa-[a-z]+-task-0030))' })
Check 'C7' ($removed.Count -eq 0 -and $bad.Count -eq 0) `
    ("git: +{0} entradas todas QA-0030, removidas={1}, fora do padrao={2}" -f $added.Count, $removed.Count, $bad.Count)

# C8 - the control block QA may not write is still what ODEV left.
$task = Get-Content -LiteralPath (Join-Path $repo 'docs\sdd\tasks\TASK-0030-remocao-post-tombstone.md') -Raw
Check 'C8' (($task -match '(?m)^status: AWAITING_QA\r?$') -and ($task -match '(?m)^qaAttempts: 0/2\r?$')) `
    'control block intacto: status AWAITING_QA, qaAttempts 0/2'

# C9 - the green the mutation driver ran over the restored sources.
$greenName = if ($mut -match 'GREEN wrapper=0 logExit=0 aprovado=(\d+) falha=0 log=([\w\.-]+\.log)') { $Matches[2] } else { '' }
$greenText = if ($greenName) { Get-Content -LiteralPath (Join-Path $runDir $greenName) -Raw } else { '' }
Check 'C9' (($greenName -ne '') -and ($greenText -match '(?m)^exit:\s+0') -and ($greenText -match 'Com falha:\s+0,\s+Aprovado:\s+\d+')) `
    ("GREEN pos-restores: {0} exit 0" -f $(if ($greenName) { $greenName } else { 'AUSENTE' }))

# C10 - the verdict document exists.
$doc = Get-ChildItem -LiteralPath $qaDir -Filter 'QA-0030-*.md' | Select-Object -First 1
Check 'C10' (($null -ne $doc) -and ((Get-Content -LiteralPath $doc.FullName -Raw) -match '# QA-0030')) `
    ("doc {0}" -f $(if ($doc) { $doc.Name } else { 'AUSENTE' }))

Write-Output "SUMMARY total=$($script:total) fails=$($script:fail)"
if ($script:fail -gt 0) { exit 1 }
exit 0
