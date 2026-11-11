#Requires -Version 5.1
<#
qa-check-task-0028.ps1 — evidence checker of QA-0028 (TASK-0028, cota de midia da instancia).
Read-only, exit 0 = all green.

S-22: every gate/run verdict is read INSIDE the log file (the `exit:` line and the summary),
never from a wrapper's console exit code. S-16/S-23: the restore hashes come from the PRE
values the mutation driver recorded before it wrote anything.
Usage: powershell -NoProfile -ExecutionPolicy Bypass -File qa-check-task-0028.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$qa = Join-Path $repo 'docs\sdd\qa'
$runs = Join-Path $repo 'docs\sdd\runs\test-runs'
$task = Join-Path $repo 'docs\sdd\tasks\TASK-0028-cota-midia.md'
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
$baseLog = Join-Path $runs '20261110-142613-test-gate.log'
Check 'GATE-BASELINE-EXIT' ((LogExit $baseLog) -eq 0) ('20261110-142613-test-gate.log exit=' + (LogExit $baseLog))
Check 'GATE-BASELINE-SUMMARY' ((LogText $baseLog) -match 'Com falha:\s+0, Aprovado:\s+343, Ignorado:\s+0, Total:\s+343') 'Com falha 0 / Aprovado 343 / Total 343 (baseline DEV, antes da QA escrever)'

# --- 2. final gate (AC-05): the NEWEST gate log, found by its timestamp name ---------------
$newestGate = Get-ChildItem -Path $runs -Filter '*-test-gate.log' -ErrorAction SilentlyContinue |
  Sort-Object Name -Descending | Select-Object -First 1
Check 'GATE-FINAL-EXISTS' ($null -ne $newestGate) $(if ($newestGate) { $newestGate.Name } else { 'nenhum log de gate' })
$finalText = if ($newestGate) { LogText $newestGate.FullName } else { '' }
$finalExit = if ($newestGate) { LogExit $newestGate.FullName } else { -1 }
Check 'GATE-FINAL-EXIT' ($finalExit -eq 0) "$($newestGate.Name) exit=$finalExit"
Check 'GATE-FINAL-SUMMARY' ($finalText -match 'Com falha:\s+0, Aprovado:\s+348, Ignorado:\s+0, Total:\s+348') 'Com falha 0 / Aprovado 348 / Total 348 (343 DEV + 5 da QA)'

# --- 3. the QA class run and the whole Quota slice ----------------------------------------
$qaFilter = Join-Path $runs '20261110-144402-test-filter.log'
Check 'QACLASS-EXIT' ((LogExit $qaFilter) -eq 0) ('20261110-144402-test-filter.log exit=' + (LogExit $qaFilter))
Check 'QACLASS-SUMMARY' ((LogText $qaFilter) -match 'Com falha:\s+0, Aprovado:\s+5, Ignorado:\s+0, Total:\s+5') 'Com falha 0 / Aprovado 5 / Total 5 (5 cenarios da QA)'
$quotaSlice = Join-Path $runs '20261110-144447-test-filter.log'
Check 'QUOTA-SLICE-EXIT' ((LogExit $quotaSlice) -eq 0) ('20261110-144447-test-filter.log exit=' + (LogExit $quotaSlice))
Check 'QUOTA-SLICE-SUMMARY' ((LogText $quotaSlice) -match 'Com falha:\s+0, Aprovado:\s+11, Ignorado:\s+0, Total:\s+11') 'Com falha 0 / Aprovado 11 / Total 11 (6 DEV + 5 QA)'

# --- 4. observations captured with the detailed logger ------------------------------------
$obs = LogText (Join-Path $qa 'QA-0028-observations.log')
$observed = @(
  'AC-01a observacao: status=507 code=quota_exceeded rowIgual=True limitBytesAposRecusa=777',
  'AC-01b observacao: linhasDoLedger=0 volumeIgual=True registrosIguais=True status=507',
  'tamanhoOriginal=9000 tamanhoGuardado=329 limiteCota=8192',
  'AC-02a observacao: real=',
  'corrigido=',
  'aposExclusao=',
  'stampAntesDoIncremento=2000-01-01T00:00:00.000Z stampAposIncremento=2000-01-01T00:00:00.000Z',
  'incrementoNaoMoveu=True',
  'Aprovados: 5')
$missing = @($observed | Where-Object { $obs -notmatch [regex]::Escape($_) })
Check 'OBSERVATIONS-9' ($missing.Count -eq 0) $(if ($missing.Count -eq 0) { 'as 9 observacoes da QA estao no artefato' } else { 'faltando: ' + ($missing -join '; ') })

# --- 5. can-fail: nine mutation cycles, each classified inside its red log ----------------
$mut = LogText (Join-Path $qa 'QA-0028-mutations.log')
$proven = @([regex]::Matches($mut, 'CYCLE [\w-]+: PROVEN')).Count
Check 'MUTATIONS-9-PROVEN' ($proven -eq 9) "$proven/9 ciclos PROVEN"
Check 'MUTATIONS-SUMMARY' ($mut -match 'SUMMARY cycles=9 failed=0 residue-QA-MUTATION=0 temp-backups=0') 'cycles=9 failed=0 residue=0 temp-backups=0'

# --- 6. live QA-MUTATION residue ----------------------------------------------------------
$residue = 0
Get-ChildItem -Path (Join-Path $repo 'backend') -Recurse -Include *.cs, *.props, *.csproj -File -ErrorAction SilentlyContinue |
  Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
  ForEach-Object { if ((Get-Content -Raw -LiteralPath $_.FullName) -match 'QA-MUTATION') { $residue++ } }
Check 'MUTATION-RESIDUE' ($residue -eq 0) "QA-MUTATION hits under backend/ = $residue"

# --- 7. byte-exact restore: current SHA256 starts with the PRE the driver recorded ---------
$pres = @([regex]::Matches($mut, 'CYCLE ([\w-]+): PROVEN PRE=([0-9A-F]{12})') |
    ForEach-Object { $_.Groups[2].Value })
# cycle order: M1 SqliteQuotaLedger, M2 MediaEndpoints, M3 QuotaReconciler,
# M4 QuotaReconcileService, M5 MediaOptions, M6 Squid.Service.csproj,
# M7 Directory.Packages.props, M8 MediaEndpoints, M9 SqliteQuotaLedger
$restoreOk = ($pres.Count -eq 9)
$restoreDetail = @()
$restoreTargets = @(
  @{ File = 'backend\src\Squid.ReadModels\Quota\SqliteQuotaLedger.cs'; Index = 0 },
  @{ File = 'backend\src\Squid.Api\Media\MediaEndpoints.cs'; Index = 1 },
  @{ File = 'backend\src\Squid.Worker\Quota\QuotaReconciler.cs'; Index = 2 },
  @{ File = 'backend\src\Squid.Worker\Quota\QuotaReconcileService.cs'; Index = 3 },
  @{ File = 'backend\src\Squid.Api\Media\MediaOptions.cs'; Index = 4 },
  @{ File = 'backend\src\Squid.Service\Squid.Service.csproj'; Index = 5 },
  @{ File = 'backend\Directory.Packages.props'; Index = 6 },
  @{ File = 'backend\src\Squid.Api\Media\MediaEndpoints.cs'; Index = 7 },
  @{ File = 'backend\src\Squid.ReadModels\Quota\SqliteQuotaLedger.cs'; Index = 8 })
foreach ($target in $restoreTargets) {
  $expect = if ($restoreOk) { $pres[$target.Index] } else { '' }
  $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $repo $target.File)).Hash
  $match = ($expect.Length -eq 12) -and $hash.StartsWith($expect)
  if (-not $match) { $restoreOk = $false }
  $restoreDetail += "$(Split-Path $target.File -Leaf)=$($hash.Substring(0,12)) PRE=$expect"
}
Check 'RESTORE-SHA256' $restoreOk ($restoreDetail -join ' ; ')

# --- 8. AC-05 scope: nothing product outside the touches changed since DEV started ---------
$cut = Get-Date '2026-11-10 13:11:00'
$hits = Get-ChildItem -Path (Join-Path $repo 'backend\src'), (Join-Path $repo 'backend\tests') -Recurse -File -Include *.cs, *.csproj, *.props |
  Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' -and $_.LastWriteTime -gt $cut }
$outside = @($hits | Where-Object { $_.FullName -notmatch '\\(Squid\.Api\\Media\\|Squid\.ReadModels\\|Squid\.Worker\\|Squid\.Service\\|Squid\.Tests\\Quota\\)' })
Check 'TOUCHES-NO-OUTSIDE' ($outside.Count -eq 0) ('mtime desde 13:11:00: ' + $hits.Count + ' arquivos, fora dos toques: ' + $outside.Count + $(if ($outside.Count) { ' -> ' + (($outside | ForEach-Object FullName) -join '; ') } else { '' }))

# --- 9. git scope: only QA/flow artifacts are new since the QA baseline capture ------------
$baselinePath = Join-Path $qa 'QA-0028-git-baseline.txt'
$baseline = @([System.IO.File]::ReadAllLines($baselinePath, [System.Text.Encoding]::UTF8) |
    ForEach-Object { $_.TrimEnd() } | Where-Object { $_.Length -gt 0 })
$current = @(git -C $repo status --porcelain -uall)
$newEntries = @($current | Where-Object { $baseline -notcontains $_ })
$badEntries = @($newEntries | Where-Object {
    $_ -notmatch 'docs/sdd/(qa/QA-0028|qa/qa-[a-z]+-task-0028|tasks/TASK-0028|runs/test-runs/)' -and
    $_ -notmatch 'backend/(src/Squid.Api/Media/|src/Squid.ReadModels/|src/Squid.Worker/|src/Squid.Service/|tests/Squid.Tests/Quota/)' })
Check 'GIT-NEW-ONLY-QA-AND-TOUCHES' ($badEntries.Count -eq 0) ('novas entradas=' + $newEntries.Count + ' fora do permitido=' + $badEntries.Count + $(if ($badEntries.Count) { ' -> ' + ($badEntries -join '; ') } else { '' }))

# --- 10. task file integrity through the flow tool (Log/Evidence append-only) --------------
$state = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'docs\sdd\_tools\sdd-state.ps1') check $task 2>&1 | Out-String
$stateExit = $LASTEXITCODE
Check 'SDD-STATE-CHECK' ($stateExit -eq 0) ((($state -split "`n" | Where-Object { $_.Trim() } | Select-Object -Last 1)) + " (exit=$stateExit)")
$taskText = LogText $task
$logRows = @([regex]::Matches($taskText, '(?m)^- [\d-]+ [\d:]+ \| qa \| ')).Count
$eviRows = @([regex]::Matches($taskText, '(?m)^\| [\d-]+ [\d:]+ \| qa \| ')).Count
Check 'TASK-ROWS' (($logRows -ge 4) -and ($eviRows -ge 8)) "linhas de log do qa=$logRows (>=4), linhas de evidencia do qa=$eviRows (>=8)"

# --- 11. the QA's own test file and verdict document --------------------------------------
$facts = @([regex]::Matches((LogText (Join-Path $repo 'backend\tests\Squid.Tests\Quota\QaTask0028Tests.cs')), '\[Fact\]')).Count
Check 'QA-TEST-FACTS' ($facts -eq 5) "[Fact] em QaTask0028Tests.cs = $facts"
$docText = LogText (Join-Path $qa 'QA-0028-cota-midia.md')
Check 'QA-DOC' (($docText.Length -gt 0) -and ($docText -match 'verdict: PASSED')) 'QA-0028-cota-midia.md existe e verdict: PASSED'

# --- 12. AC-06 declaration (the static gate of the human approval) ------------------------
$props = LogText (Join-Path $repo 'backend\Directory.Packages.props')
$csproj = LogText (Join-Path $repo 'backend\src\Squid.Service\Squid.Service.csproj')
Check 'AC06-DECLARATION' `
  (($props -match '<PackageVersion Include="SkiaSharp" Version="4.155.0" />') -and
   ($props -match '<PackageVersion Include="SkiaSharp.NativeAssets.Linux" Version="4.155.0" />') -and
   ($csproj -match '<PackageReference Include="SkiaSharp.NativeAssets.Linux" />')) `
  'SkiaSharp e NativeAssets.Linux ambos em 4.155.0 no props + PackageReference no csproj'

# --- 13. platform twin record (the .sh gate has no dotnet in this WSL - Linux unverified) --
$shText = LogText (Join-Path $qa 'QA-0028-sh-twin.log')
Check 'SH-TWIN-RECORDED' (($shText -match 'dotnet: command not found') -and ($shText -match 'exit 127')) 'sdd-test.sh: dotnet ausente no WSL - plataforma Linux registrada como unverified'

Write-Output "SUMMARY total=$($script:pass + $script:fail) fails=$($script:fail)"
Write-Output ("RESULT: " + $(if ($script:fail -eq 0) { 'PASS' } else { 'FAIL' }))
if ($script:fail -gt 0) { exit 1 }
exit 0
