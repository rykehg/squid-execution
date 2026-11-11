# QA-0031 evidence checker (TASK-0031 - denuncia, fila admin e suspensao).
# Eight checks over the artifacts of THIS run: gate log, filter log, mutation summary,
# QA-MUTATION residue, git scope vs the pre-QA baseline, control block, verdict doc, temp backups.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0031.ps1
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$out = Join-Path $PSScriptRoot 'QA-0031-check.log'
$runs = Join-Path $repo 'docs\sdd\runs\test-runs'
$fails = 0
$lines = @()

function Check([string]$Id, [bool]$Ok, [string]$Detail) {
    $script:lines += ("{0} {1} {2}" -f $Id, $(if ($Ok) { 'PASS' } else { 'FAIL' }), $Detail)
    if (-not $Ok) { $script:fails++ }
}

# C1 - the final gate of this QA: classified INSIDE the log (S-22), never by the console.
$gateLog = Get-ChildItem -LiteralPath $runs -Filter '*-test-gate.log' | Sort-Object Name -Descending | Select-Object -First 1
$gateText = if ($gateLog) { Get-Content -LiteralPath $gateLog.FullName -Raw } else { '' }
$gateExit = if ($gateText -match '(?m)^exit:\s+(\d+)') { [int]$Matches[1] } else { -1 }
$gateApproved = if ($gateText -match 'Com falha:\s*(\d+),\s*Aprovado:\s*(\d+)') { [int]$Matches[2] } else { -1 }
$gateFailed = if ($gateText -match 'Com falha:\s*(\d+),\s*Aprovado:\s*(\d+)') { [int]$Matches[1] } else { -1 }
Check 'C1' (($gateExit -eq 0) -and ($gateFailed -eq 0) -and ($gateApproved -ge 426)) `
    ("gate {0} exit={1} falha={2} aprovado={3}" -f $(if ($gateLog) { $gateLog.Name } else { '(ausente)' }), $gateExit, $gateFailed, $gateApproved)

# C2 - the Moderation filter over the restored sources (7 DEV + 2 QA tests).
$filterLog = Get-ChildItem -LiteralPath $runs -Filter '*-test-filter.log' | Sort-Object Name -Descending | Select-Object -First 1
$filterText = if ($filterLog) { Get-Content -LiteralPath $filterLog.FullName -Raw } else { '' }
$filterExit = if ($filterText -match '(?m)^exit:\s+(\d+)') { [int]$Matches[1] } else { -1 }
$filterApproved = if ($filterText -match 'Com falha:\s*(\d+),\s*Aprovado:\s*(\d+)') { [int]$Matches[2] } else { -1 }
Check 'C2' (($filterExit -eq 0) -and ($filterApproved -eq 9)) `
    ("filter {0} exit={1} aprovado={2}" -f $(if ($filterLog) { $filterLog.Name } else { '(ausente)' }), $filterExit, $filterApproved)

# C3 - mutation driver summary: five can-fail cycles, all restored, no residue.
$mutPath = Join-Path $PSScriptRoot 'QA-0031-mutations.log'
$mutText = if (Test-Path -LiteralPath $mutPath) { Get-Content -LiteralPath $mutPath -Raw } else { '' }
$summary = if ($mutText -match 'SUMMARY[^\r\n]*') { $Matches[0] } else { '(ausente)' }
Check 'C3' ($summary -match 'cycles=5 failed=0 residue-QA-MUTATION=0 temp-backups=0') $summary

# C4 - no QA-MUTATION marker left in the product sources.
$residue = @(Get-ChildItem -LiteralPath (Join-Path $repo 'backend') -Recurse -File -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    Select-String -Pattern 'QA-MUTATION' -SimpleMatch)
Check 'C4' ($residue.Count -eq 0) ("residuo QA-MUTATION={0}" -f $residue.Count)

# C5 - git scope: nothing removed, every new entry belongs to this QA (AC-05).
$baselinePath = Join-Path $PSScriptRoot 'QA-0031-git-baseline.txt'
$baseline = @(Get-Content -LiteralPath $baselinePath | Where-Object { $_.Trim() })
$current = @(git -C $repo status --porcelain | Where-Object { $_.Trim() })
$removed = @($baseline | Where-Object { $_ -notin $current })
$added = @($current | Where-Object { $_ -notin $baseline })
$allow = 'QA-0031|qa-check-task-0031|qa-mutate-task-0031|QaTask0031Tests|test-runs'
$outside = @($added | Where-Object { $_ -notmatch $allow })
Check 'C5' (($removed.Count -eq 0) -and ($outside.Count -eq 0)) `
    ("adicionadas={0} removidas={1} fora-do-escopo={2}" -f $added.Count, $removed.Count, $outside.Count)
$lines += ("  fora-do-escopo: " + $(if ($outside.Count) { $outside -join ' | ' } else { 'nenhuma' }))
$lines += ("  adicionadas: " + ($added -join ' | '))

# C6 - the control block of the task file is intact (QA writes only Log/Evidence).
$taskPath = Join-Path $repo 'docs\sdd\tasks\TASK-0031-denuncia-admin-suspensao.md'
$taskText = Get-Content -LiteralPath $taskPath -Raw
Check 'C6' (($taskText -match 'status: AWAITING_QA') -and ($taskText -match 'qaAttempts: 0/2') `
    -and ($taskText -match '- AC-05: gate')) 'status=AWAITING_QA qaAttempts=0/2 AC-05 presente'

# C7 - the verdict doc of this attempt exists and carries a verdict.
$verdict = @(Get-ChildItem -LiteralPath (Join-Path $repo 'docs\sdd\qa') -Filter 'QA-0031-*.md' -File |
    Where-Object { $_.Name -notmatch 'git-baseline' })
$verdictOk = $false
foreach ($file in $verdict) {
    if ((Get-Content -LiteralPath $file.FullName -Raw) -match '- verdict: (PASSED|FAILED|PARTIAL)') { $verdictOk = $true }
}
Check 'C7' $verdictOk ("docs: " + $(if ($verdict.Count) { ($verdict | ForEach-Object Name) -join ', ' } else { '(ausente)' }))

# C8 - no temp backup of this QA left behind.
$backups = @(Get-ChildItem -Path $env:TEMP -Filter 'qa0031-*' -File -ErrorAction SilentlyContinue)
Check 'C8' ($backups.Count -eq 0) ("backups temporarios={0}" -f $backups.Count)

$lines += ("SUMMARY total=8 fails={0}" -f $fails)
Set-Content -LiteralPath $out -Value $lines -Encoding utf8
$lines | ForEach-Object { Write-Output $_ }
if ($fails -gt 0) { exit 1 }
exit 0
