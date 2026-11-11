#Requires -Version 5.1
<#
qa-check-task-0027.ps1 — evidence checker of QA-0027 round 2 (TASK-0027). Read-only, exit 0 = all green.

Round-2 refresh (the round-1 numbers no longer hold after the QA-0026 BLOCKER fix):
  baseline gate 330 -> 337 (20261110-113636), final gate 335 -> 337, mutation cycles 11 -> 13
  (M13/M14 added against the fix), artifacts QA-0026-* -> QA-0027-*, git baseline re-captured.

S-22: every gate/run verdict is read INSIDE the log file (the `exit:` line and the summary),
never from a wrapper's console exit code. S-16/S-23: the restore hashes come from the PRE
values the mutation driver recorded before it wrote anything.
Usage: powershell -NoProfile -ExecutionPolicy Bypass -File qa-check-task-0027.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$qa = Join-Path $repo 'docs\sdd\qa'
$runs = Join-Path $repo 'docs\sdd\runs\test-runs'
$task = Join-Path $repo 'docs\sdd\tasks\TASK-0027-upload-midia-skia.md'
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
$baseLog = Join-Path $runs '20261110-113636-test-gate.log'
Check 'GATE-BASELINE-EXIT' ((LogExit $baseLog) -eq 0) ("20261110-113636-test-gate.log exit=" + (LogExit $baseLog))
Check 'GATE-BASELINE-SUMMARY' ((LogText $baseLog) -match 'Com falha:\s+0, Aprovado:\s+337, Ignorado:\s+0, Total:\s+337') 'Com falha 0 / Aprovado 337 / Total 337 (baseline da QA-0027, arvore corrigida)'

# --- 2. final gate (AC-06): the NEWEST gate log, found by its timestamp name ---------------
$newestGate = Get-ChildItem -Path $runs -Filter '*-test-gate.log' -ErrorAction SilentlyContinue |
  Sort-Object Name -Descending | Select-Object -First 1
Check 'GATE-FINAL-EXISTS' ($null -ne $newestGate) $(if ($newestGate) { $newestGate.Name } else { 'nenhum log de gate' })
$finalText = if ($newestGate) { LogText $newestGate.FullName } else { '' }
$finalExit = if ($newestGate) { LogExit $newestGate.FullName } else { -1 }
Check 'GATE-FINAL-EXIT' ($finalExit -eq 0) ("$($newestGate.Name) exit=$finalExit")
Check 'GATE-FINAL-SUMMARY' ($finalText -match 'Com falha:\s+0, Aprovado:\s+337, Ignorado:\s+0, Total:\s+337') 'Com falha 0 / Aprovado 337 (330 base + 5 da QA + 2 MediaCeilingTests)'

# --- 3. the QA class run: the NEWEST filtered log (the fresh green run after all edits) ----
$newestFilter = Get-ChildItem -Path $runs -Filter '*-test-filter.log' -ErrorAction SilentlyContinue |
  Sort-Object Name -Descending | Select-Object -First 1
Check 'QACLASS-EXISTS' ($null -ne $newestFilter) $(if ($newestFilter) { $newestFilter.Name } else { 'nenhum log filtrado' })
$filterText = if ($newestFilter) { LogText $newestFilter.FullName } else { '' }
$filterExit = if ($newestFilter) { LogExit $newestFilter.FullName } else { -1 }
Check 'QACLASS-EXIT' ($filterExit -eq 0) ("$($newestFilter.Name) exit=$filterExit")
Check 'QACLASS-SUMMARY' ($filterText -match 'Com falha:\s+0, Aprovado:\s+5, Ignorado:\s+0, Total:\s+5') 'Com falha 0 / Aprovado 5 / Total 5 (5 cenarios da QA)'

# --- 4. observations captured with the detailed logger ------------------------------------
$obs = LogText (Join-Path $qa 'QA-0027-observations.log')
$observed = @(
  'AC-01 observacao: contentHash=',
  'sha256Servido=',
  'AC-02 observacao: altVazio image/gif/video=[400,400,400]',
  'video12MiB=200',
  'AC-03 observacao: kinds=image/gif/video',
  'dez=201',
  'AC-04 observacao:',
  '404IdenticoAoAusente=True',
  'AC-05 observacao:',
  'igualServido=True')
$missing = @($observed | Where-Object { $obs -notmatch [regex]::Escape($_) })
Check 'OBSERVATIONS-10' ($missing.Count -eq 0) $(if ($missing.Count -eq 0) { 'as 10 observacoes da QA estao no artefato' } else { 'faltando: ' + ($missing -join '; ') })

# --- 5. can-fail: thirteen mutation cycles, each classified inside its red log ------------
$mut = LogText (Join-Path $qa 'QA-0027-mutations.log')
$proven = @([regex]::Matches($mut, 'CYCLE [\w-]+: PROVEN')).Count
Check 'MUTATIONS-13-PROVEN' ($proven -eq 13) "$proven/13 ciclos PROVEN"
Check 'MUTATIONS-SUMMARY' ($mut -match 'SUMMARY cycles=13 failed=0 residue-QA-MUTATION=0 temp-backups=0') 'cycles=13 failed=0 residue=0 temp-backups=0'

# --- 6. live QA-MUTATION residue (S-16 leftovers) -----------------------------------------
$residue = 0
Get-ChildItem -Path (Join-Path $repo 'backend') -Recurse -Include *.cs, *.props, *.csproj -File -ErrorAction SilentlyContinue |
  Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
  ForEach-Object { if ((Get-Content -Raw -LiteralPath $_.FullName) -match 'QA-MUTATION') { $residue++ } }
Check 'MUTATION-RESIDUE' ($residue -eq 0) "QA-MUTATION hits under backend/ = $residue"

# --- 7. byte-exact restore: current SHA256 starts with the PRE the driver recorded ---------
$pres = @([regex]::Matches($mut, 'CYCLE ([\w-]+): PROVEN PRE=([0-9A-F]{12})') |
    ForEach-Object { $_.Groups[2].Value })
# cycle order: M1 = ExifStripper, M2..M5 = MediaEndpoints, M6..M8 = PostEndpoints,
# M9/M10 = MediaEndpoints, M12 = SkiaMediaProcessor, M13 = MediaOptions, M14 = MediaEndpoints
$restoreOk = ($pres.Count -eq 13)
$restoreDetail = @()
$restoreTargets = @(
  @{ File = 'backend\src\Squid.Service\ExifStripper.cs'; Index = 0 },
  @{ File = 'backend\src\Squid.Api\Media\MediaEndpoints.cs'; Index = 1 },
  @{ File = 'backend\src\Squid.Api\Posts\PostEndpoints.cs'; Index = 5 },
  @{ File = 'backend\src\Squid.Service\SkiaMediaProcessor.cs'; Index = 10 },
  @{ File = 'backend\src\Squid.Api\Media\MediaOptions.cs'; Index = 11 })
foreach ($target in $restoreTargets) {
  $expect = if ($restoreOk) { $pres[$target.Index] } else { '' }
  $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $repo $target.File)).Hash
  $match = ($expect.Length -eq 12) -and $hash.StartsWith($expect)
  if (-not $match) { $restoreOk = $false }
  $restoreDetail += "$(Split-Path $target.File -Leaf)=$($hash.Substring(0,12)) PRE=$expect"
}
Check 'RESTORE-SHA256' $restoreOk ($restoreDetail -join ' ; ')

# --- 8. the M11 order-swap probe is recorded as an equivalent mutant -----------------------
$probe = LogText (Join-Path $qa 'QA-0026-mutation-probe-m11.log')
Check 'PROBE-M11-RECORDED' (($probe -match 'UNPROVEN') -and ($probe -match 'restore=HASH=PRE') -and ($probe -match 'residue-QA-MUTATION=0')) 'M11 (ordem EXIF->blurHash) sondado: UNPROVEN mutante equivalente, restore HASH=PRE, residuo 0'

# --- 9. AC-06 scope: nothing product outside the touches changed since the QA started ------
$cut = Get-Date '2026-11-10 08:00:00'
$hits = Get-ChildItem -Path (Join-Path $repo 'backend\src'), (Join-Path $repo 'backend\tests') -Recurse -File -Include *.cs, *.csproj, *.props |
  Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' -and $_.LastWriteTime -gt $cut }
$outside = @($hits | Where-Object { $_.FullName -notmatch '\\(Squid\.Api\\Media\\|Squid\.Api\\Posts\\|Squid\.Domain\\Objects\\|Squid\.Service\\|Squid\.Tests\\Media\\)' })
Check 'TOUCHES-NO-OUTSIDE' ($outside.Count -eq 0) ("mtime desde 08:00:00: " + $hits.Count + " arquivos, fora dos toques: " + $outside.Count + $(if ($outside.Count) { ' -> ' + (($outside | ForEach-Object FullName) -join '; ') } else { '' }))

# --- 10. git scope: only QA/flow artifacts are new since the QA baseline capture -----------
$baselinePath = Join-Path $qa 'QA-0027-git-baseline.txt'
$baseline = @([System.IO.File]::ReadAllLines($baselinePath, [System.Text.Encoding]::UTF8) |
    ForEach-Object { $_.TrimEnd() } | Where-Object { $_.Length -gt 0 })
$current = @(git -C $repo status --porcelain -uall)
$newEntries = @($current | Where-Object { $baseline -notcontains $_ })
$badEntries = @($newEntries | Where-Object {
    $_ -notmatch 'docs/sdd/(qa/|tasks/TASK-0027|runs/test-runs/)' -and
    $_ -notmatch 'backend/(src/Squid.Api/Media/|src/Squid.Api/Posts/|src/Squid.Domain/Objects/|src/Squid.Service/|tests/Squid.Tests/Media/)' })
Check 'GIT-NEW-ONLY-QA-AND-TOUCHES' ($badEntries.Count -eq 0) ("novas entradas=" + $newEntries.Count + " fora do permitido=" + $badEntries.Count + $(if ($badEntries.Count) { ' -> ' + (($badEntries -join '; ')) } else { '' }))

# --- 11. task file integrity through the flow tool (Log/Evidence append-only) --------------
$state = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'docs\sdd\_tools\sdd-state.ps1') check $task 2>&1 | Out-String
$stateExit = $LASTEXITCODE
Check 'SDD-STATE-CHECK' ($stateExit -eq 0) ((($state -split "`n" | Where-Object { $_.Trim() } | Select-Object -Last 1)) + " (exit=$stateExit)")
$taskText = LogText $task
$logRows = @([regex]::Matches($taskText, '(?m)^- [\d-]+ [\d:]+ \| qa \| ')).Count
$eviRows = @([regex]::Matches($taskText, '(?m)^\| [\d-]+ [\d:]+ \| qa \| ')).Count
Check 'TASK-ROWS' (($logRows -ge 6) -and ($eviRows -ge 12)) "linhas de log do qa=$logRows (>=6), linhas de evidencia do qa=$eviRows (>=12)"

# --- 12. the QA's own test file, ceiling probe and verdict document -------------------------
$facts = @([regex]::Matches((LogText (Join-Path $repo 'backend\tests\Squid.Tests\Media\QaTask0027Tests.cs')), '\[Fact\]')).Count
Check 'QA-TEST-FACTS' ($facts -eq 5) "[Fact] em QaTask0027Tests.cs = $facts"
$ceil = LogText (Join-Path $qa 'QA-0027-ceiling-probe.log')
Check 'CEILING-PROBE-3' (($ceil -match 'exit:\s+0') -and ($ceil -match 'round2 streaming.*status=413 code=payload_too_large') -and ($ceil -match 'round2 raw acima.*CL=69206017.*payload_too_large') -and ($ceil -match 'round2 raw dentro.*servidor esperou o corpo')) 'sonda de teto: 3 facts exit 0 - streaming 413 com envelope, cru 69206017 com envelope, cru 53000000 esperado'
$docText = LogText (Join-Path $qa 'QA-0027-upload-midia-skia.md')
Check 'QA-DOC' (($docText.Length -gt 0) -and ($docText -match 'verdict: PASSED')) 'QA-0027-upload-midia-skia.md existe e verdict: PASSED'

# --- 13. platform twin record (the .sh gate has no dotnet in this WSL — Linux unverified) --
$shText = LogText (Join-Path $qa 'QA-0027-sh-twin.log')
Check 'SH-TWIN-RECORDED' (($shText -match 'dotnet: command not found') -and ($shText -match 'exit 127')) 'sdd-test.sh: dotnet ausente no WSL - plataforma Linux registrada como unverified (pre-existente re-confirmado)'

Write-Output "SUMMARY total=$($script:pass + $script:fail) fails=$($script:fail)"
Write-Output ("RESULT: " + $(if ($script:fail -eq 0) { 'PASS' } else { 'FAIL' }))
if ($script:fail -gt 0) { exit 1 }
exit 0
