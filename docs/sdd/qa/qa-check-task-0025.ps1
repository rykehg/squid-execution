#Requires -Version 5.1
<#
qa-check-task-0025.ps1 — evidence checker of QA-0024 (TASK-0025). Read-only, exit 0 = all green.

S-22: every gate/run verdict is read INSIDE the log file (the `exit:` line and the summary),
never from a wrapper's console exit code. S-16/S-23: the restore hashes come from the PRE
values the mutation driver recorded before it wrote anything.
Usage: powershell -NoProfile -ExecutionPolicy Bypass -File qa-check-task-0025.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$qa = Join-Path $repo 'docs\sdd\qa'
$runs = Join-Path $repo 'docs\sdd\runs\test-runs'
$task = Join-Path $repo 'docs\sdd\tasks\TASK-0025-feed-timeline-delta.md'
$script:pass = 0
$script:fail = 0

function Check([string]$Name, [bool]$Ok, [string]$Detail) {
  if ($Ok) { $script:pass++; Write-Output "CHECK $Name OK   $Detail" }
  else { $script:fail++; Write-Output "CHECK $Name FAIL $Detail" }
}

function LogText([string]$Path) {
  if (Test-Path -LiteralPath $Path) { [System.IO.File]::ReadAllText($Path) } else { '' }
}

function LogExit([string]$Path) {
  $m = [regex]::Match((LogText $Path), '(?m)^exit:\s+(\d+)')
  if ($m.Success) { return [int]$m.Groups[1].Value }
  return -1
}

# --- 1. baseline gate of this QA (read inside the log, S-22) -------------------------------
$baseLog = Join-Path $runs '20261109-192511-test-gate.log'
Check 'GATE-BASELINE-EXIT' ((LogExit $baseLog) -eq 0) ("20261109-192511-test-gate.log exit=" + (LogExit $baseLog))
Check 'GATE-BASELINE-SUMMARY' ((LogText $baseLog) -match 'Com falha:\s+0, Aprovado:\s+285, Ignorado:\s+0, Total:\s+285') 'Com falha 0 / Aprovado 285 / Total 285'

# --- 2. final gate (AC-06): the NEWEST gate log, found by its timestamp name ---------------
$newestGate = Get-ChildItem -Path $runs -Filter '*-test-gate.log' -ErrorAction SilentlyContinue |
  Sort-Object Name -Descending | Select-Object -First 1
Check 'GATE-FINAL-EXISTS' ($null -ne $newestGate) $(if ($newestGate) { $newestGate.Name } else { 'nenhum log de gate' })
$finalText = if ($newestGate) { LogText $newestGate.FullName } else { '' }
$finalExit = if ($newestGate) { LogExit $newestGate.FullName } else { -1 }
Check 'GATE-FINAL-EXIT' ($finalExit -eq 0) ("$($newestGate.Name) exit=$finalExit")
Check 'GATE-FINAL-SUMMARY' ($finalText -match 'Com falha:\s+0, Aprovado:\s+293, Ignorado:\s+0, Total:\s+293') 'Com falha 0 / Aprovado 293 (285 + 8 da QA)'

# --- 3. the QA class run ------------------------------------------------------------------
$qaRun = Join-Path $runs '20261109-194058-test-filter.log'
Check 'QACLASS-EXIT' ((LogExit $qaRun) -eq 0) ("20261109-194058-test-filter.log exit=" + (LogExit $qaRun))
Check 'QACLASS-SUMMARY' ((LogText $qaRun) -match 'Com falha:\s+0, Aprovado:\s+8, Ignorado:\s+0, Total:\s+8') 'Com falha 0 / Aprovado 8 / Total 8'

# --- 4. observations captured with the detailed logger ------------------------------------
$obs = LogText (Join-Path $qa 'QA-0024-observations.log')
Check 'OBSERVATIONS' (($obs -match 'AC-03 observacao: item anterior a entrada fresca presente na timeline = False') -and ($obs -match 'AC-05 observacao: item na timeline de membro Silenced = False')) 'as duas observacoes da QA registradas no artefato'
Check 'SC005-100' ($obs -match 'SC-005: 100 tentativas') 'SC-005: 100 tentativas / 0 vazamentos'

# --- 5. can-fail: seven mutation cycles, each classified inside its red log ---------------
$mut = LogText (Join-Path $qa 'QA-0024-mutations.log')
$proven = @([regex]::Matches($mut, 'CYCLE [\w-]+: PROVEN')).Count
Check 'MUTATIONS-7-PROVEN' ($proven -eq 7) "$proven/7 ciclos PROVEN"
Check 'MUTATIONS-SUMMARY' ($mut -match 'SUMMARY cycles=7 failed=0 residue-QA-MUTATION=0 temp-backups=0') 'cycles=7 failed=0 residue=0 temp-backups=0'

# --- 6. live QA-MUTATION residue (S-16 leftovers) -----------------------------------------
$residue = 0
Get-ChildItem -Path (Join-Path $repo 'backend') -Recurse -Include *.cs, *.sql, *.props, *.csproj -File -ErrorAction SilentlyContinue |
  Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
  ForEach-Object { if ((Get-Content -Raw -LiteralPath $_.FullName) -match 'QA-MUTATION') { $residue++ } }
Check 'MUTATION-RESIDUE' ($residue -eq 0) "QA-MUTATION hits under backend/ = $residue"

# --- 7. byte-exact restore: current SHA256 starts with the PRE the driver recorded ---------
$pres = @([regex]::Matches($mut, 'CYCLE ([\w-]+): PROVEN PRE=([0-9A-F]{12})') |
    ForEach-Object { $_.Groups[2].Value })
$restoreFiles = @(
  'backend\src\Squid.ReadModels\Projections\SqliteFeedTimeline.cs',
  'backend\src\Squid.Api\Posts\FeedEndpoints.cs',
  'backend\src\Squid.Api\Posts\PostEndpoints.cs')
$restoreExpects = @($pres[0], $pres[3], $pres[4])
$restoreOk = ($pres.Count -ge 5)
$restoreDetail = @()
for ($i = 0; $i -lt 3; $i++) {
  $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $repo $restoreFiles[$i])).Hash
  $match = $hash.StartsWith($restoreExpects[$i])
  if (-not $match) { $restoreOk = $false }
  $restoreDetail += "$(Split-Path $restoreFiles[$i] -Leaf)=$($hash.Substring(0,12)) PRE=$($restoreExpects[$i])"
}
Check 'RESTORE-SHA256' $restoreOk ($restoreDetail -join ' ; ')

# --- 8. AC-06 scope: nothing product outside the touches changed since the DEV dispatch ----
$cut = Get-Date '2026-11-09 17:15:32'
$hits = Get-ChildItem -Path (Join-Path $repo 'backend\src'), (Join-Path $repo 'backend\tests') -Recurse -File -Include *.cs, *.csproj, *.sql, *.props |
  Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' -and $_.LastWriteTime -gt $cut }
$outside = @($hits | Where-Object { $_.FullName -notmatch '\\(Squid\.ReadModels\\|Squid\.Api\\Posts\\|Squid\.Service\\|Squid\.Tests\\Feed\\)' })
Check 'TOUCHES-NO-OUTSIDE' ($outside.Count -eq 0) ("mtime desde o dispatch 17:15:32: " + $hits.Count + " arquivos, fora dos toques: " + $outside.Count + $(if ($outside.Count) { ' -> ' + (($outside | ForEach-Object FullName) -join '; ') } else { '' }))

# --- 9. git scope: only QA/flow artifacts are new since the QA baseline capture ------------
$baselinePath = Join-Path $qa 'QA-0024-git-baseline.txt'
$baseline = @([System.IO.File]::ReadAllLines($baselinePath, [System.Text.Encoding]::UTF8) |
    ForEach-Object { $_.TrimEnd() } | Where-Object { $_.Length -gt 0 })
$current = @(git -C $repo status --porcelain -uall)
$newEntries = @($current | Where-Object { $baseline -notcontains $_ })
$badEntries = @($newEntries | Where-Object { $_ -notmatch 'docs/sdd/(qa/|tasks/TASK-0025)' })
Check 'GIT-NEW-ONLY-QA-AND-TOUCHES' ($badEntries.Count -eq 0) ("novas entradas=" + $newEntries.Count + " fora do permitido=" + $badEntries.Count + $(if ($badEntries.Count) { ' -> ' + (($badEntries -join '; ')) } else { '' }))

# --- 10. task file integrity through the flow tool (Log/Evidence append-only) --------------
$state = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'docs\sdd\_tools\sdd-state.ps1') check $task 2>&1 | Out-String
$stateExit = $LASTEXITCODE
Check 'SDD-STATE-CHECK' ($stateExit -eq 0) ((($state -split "`n" | Where-Object { $_.Trim() } | Select-Object -Last 1)) + " (exit=$stateExit)")

# --- 11. the QA's own test file and verdict document --------------------------------------
$facts = @([regex]::Matches((LogText (Join-Path $repo 'backend\tests\Squid.Tests\Feed\QaTask0025Tests.cs')), '\[Fact\]')).Count
Check 'QA-TEST-FACTS' ($facts -eq 8) "[Fact] em QaTask0025Tests.cs = $facts"
$docText = LogText (Join-Path $qa 'QA-0024-feed-timeline-delta.md')
Check 'QA-DOC' (($docText.Length -gt 0) -and ($docText -match 'verdict: PASSED')) 'QA-0024-feed-timeline-delta.md existe e verdict: PASSED'

# --- 12. platform twin record (the .sh gate cannot find this repo's .slnx — pre-existing) --
$shText = LogText (Join-Path $qa 'QA-0024-sh-twin.log')
Check 'SH-TWIN-RECORDED' (($shText -match 'no \*\.sln found within depth 3') -and ($shText -match 'exit: 1')) 'sdd-test.sh sem alvo = exit 1 (achado pre-existente re-confirmado; dotnet ausente no WSL)'

Write-Output "SUMMARY total=$($script:pass + $script:fail) fails=$($script:fail)"
Write-Output ("RESULT: " + $(if ($script:fail -eq 0) { 'PASS' } else { 'FAIL' }))
if ($script:fail -gt 0) { exit 1 }
exit 0
