#Requires -Version 5.1
<#
qa-mutate-task-0025.ps1 — can-fail driver of QA-0024 (TASK-0025).

One cycle, exactly as the flow demands:
  S-16 : SHA256 PRE of the product file, recorded before anything is written
  apply: ONE unique pattern (count must be exactly 1 or the cycle is REFUSED, nothing written),
         inside the task's touches only, carrying the QA-MUTATION marker in the text
  red  : sdd-test.ps1 test -TestFilter <the AC under test>; classified by the `exit:` line and
         the summary INSIDE the log file (S-22), never by the wrapper's console exit code alone
  S-23 : byte-exact restore from the backup, mtime bump, SHA256 must equal PRE again
  green: same filtered run, exit 0 read inside its log

Nothing outside backend/src/Squid.ReadModels/** and backend/src/Squid.Api/Posts/** is ever
written, and no mutation outlives its own cycle (restore runs in a finally block).
Usage: powershell -NoProfile -ExecutionPolicy Bypass -File qa-mutate-task-0025.ps1 [-Only M3]
#>
[CmdletBinding()]
param([string]$Only = '')

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$sddTest = Join-Path $repo 'docs\sdd\_tools\sdd-test.ps1'
$utf8 = New-Object System.Text.UTF8Encoding($false)

$mutations = @(
  @{ Label = 'M1-AC01-delta-strict'; File = 'backend\src\Squid.ReadModels\Projections\SqliteFeedTimeline.cs'
     Old = 'AND seq > @After ORDER BY seq ASC LIMIT @Limit";'
     New = 'AND seq >= @After ORDER BY seq ASC LIMIT @Limit -- QA-MUTATION";'
     Filter = 'FullyQualifiedName~QaTask0025Tests.Ac01' },
  @{ Label = 'M2-AC01-page-order'; File = 'backend\src\Squid.ReadModels\Projections\SqliteFeedTimeline.cs'
     Old = 'WHERE container_id = @ContainerId ORDER BY seq DESC LIMIT @Limit";'
     New = 'WHERE container_id = @ContainerId ORDER BY seq ASC LIMIT @Limit -- QA-MUTATION";'
     Filter = 'FullyQualifiedName~QaTask0025Tests.Ac01' },
  @{ Label = 'M3-AC03-timeline-delta'; File = 'backend\src\Squid.ReadModels\Projections\SqliteFeedTimeline.cs'
     Old = 'AND h.seq > @After ORDER BY h.seq ASC LIMIT @Limit'
     New = 'AND h.seq >= @After ORDER BY h.seq ASC LIMIT @Limit -- QA-MUTATION'
     Filter = 'FullyQualifiedName~QaTask0025Tests.Ac03' },
  @{ Label = 'M4-AC03-active-gate'; File = 'backend\src\Squid.Api\Posts\FeedEndpoints.cs'
     Old = '&& membership?.State == MembershipState.Active'
     New = '&& (membership is not null || membership is null) // QA-MUTATION'
     Filter = 'FullyQualifiedName~QaTask0025Tests.Ac03' },
  @{ Label = 'M5-AC02-AC04-visibility'; File = 'backend\src\Squid.Api\Posts\PostEndpoints.cs'
     Old = 'return membership?.State is MembershipState.Active or MembershipState.Silenced;'
     New = 'return true; // QA-MUTATION'
     Filter = 'FullyQualifiedName~QaTask0025Tests.Ac0' },
  @{ Label = 'M6-AC05-banned'; File = 'backend\src\Squid.Api\Posts\PostEndpoints.cs'
     Old = 'if (forRead && membership?.State == MembershipState.Banned) return false;'
     New = 'if (forRead && membership?.State == MembershipState.Banned) { } // QA-MUTATION'
     Filter = 'FullyQualifiedName~QaTask0025Tests.Ac05' },
  @{ Label = 'M7-AC08-cap-after-sanitize'; File = 'backend\src\Squid.Api\Posts\PostEndpoints.cs'
     Old = 'return sanitized.Length > PostObject.MaxContentLength'
     New = 'return null != sanitized && sanitized.Length > int.MaxValue // QA-MUTATION'
     Filter = 'FullyQualifiedName~QaTask0025Tests.Ac08' }
)

# Runs one filtered gate and returns what the LOG FILE says (S-22), not what the console says.
function Invoke-FilterRun([string]$Filter) {
  $console = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $sddTest test -TestFilter $Filter 2>&1 | Out-String
  $wrapperExit = $LASTEXITCODE
  $logPath = [regex]::Match($console, '(?m)^log:\s+(.+)$').Groups[1].Value.Trim()
  if ([string]::IsNullOrWhiteSpace($logPath) -or -not (Test-Path -LiteralPath $logPath)) {
    return @{ WrapperExit = $wrapperExit; LogExit = 'no-log'; Summary = ''; Fails = @(); LogPath = ''; CompileError = $false }
  }

  $text = [System.IO.File]::ReadAllText($logPath)
  $logExit = [regex]::Match($text, '(?m)^exit:\s+(\d+)').Groups[1].Value
  $summary = [regex]::Match($text, '(?m)^(Aprovado!.*|Falhou!.*|Failed!.*)$').Groups[1].Value
  $fails = @([regex]::Matches($text, '(?m)^.*\b([\w.]*QaTask0025Tests\.[\w]+)\s+\[FAIL\]') |
    ForEach-Object { $_.Groups[1].Value })
  return @{
    WrapperExit  = $wrapperExit
    LogExit      = $logExit
    Summary      = $summary.Trim()
    Fails        = $fails
    LogPath      = $logPath
    CompileError = ($text -match 'error CS\d+')
  }
}

$failed = 0
$ran = 0
foreach ($m in $mutations) {
  if ($Only -and $m.Label -notlike "$Only*") { continue }
  $ran++

  $path = Join-Path $repo $m.File
  $pre = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash
  $text = [System.IO.File]::ReadAllText($path)
  $count = ([regex]::Matches($text, [regex]::Escape($m.Old))).Count
  if ($count -ne 1) {
    Write-Output "CYCLE $($m.Label): REFUSED pattern-count=$count (nothing written) - fix the driver pattern"
    $failed++
    continue
  }

  $backup = Join-Path $env:TEMP ('qa0024-' + [guid]::NewGuid().ToString('N') + '.bak')
  [System.IO.File]::Copy($path, $backup, $true)
  try {
    [System.IO.File]::WriteAllText($path, $text.Replace($m.Old, $m.New), $utf8)
    $residue = ([regex]::Matches([System.IO.File]::ReadAllText($path), 'QA-MUTATION')).Count
    $red = Invoke-FilterRun $m.Filter

    # restore FIRST (byte-exact), then bump the mtime, then prove the hash (S-23)
    [System.IO.File]::Copy($backup, $path, $true)
    (Get-Item -LiteralPath $path).LastWriteTime = Get-Date
    $post = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash
    $restored = ($post -eq $pre)

    $green = Invoke-FilterRun $m.Filter

    $redOk = ($red.LogExit -eq '1')
    $greenOk = ($green.LogExit -eq '0')
    $proven = $redOk -and $restored -and $greenOk -and ($residue -gt 0) -and (-not $red.CompileError)
    if (-not $proven) { $failed++ }

    $line = 'CYCLE {0}: {1} PRE={2} red(log exit={3} wrapper={4} fails=[{5}] compileErr={6}) restore={7} green(log exit={8} wrapper={9} {10}) => {11}' -f `
      $m.Label,
      $(if ($redOk) { 'PROVEN' } else { 'UNPROVEN' }),
      $pre.Substring(0, 12),
      $red.LogExit, $red.WrapperExit, ($red.Fails -join ', '), $red.CompileError,
      $(if ($restored) { 'HASH=PRE' } else { 'HASH-MISMATCH' }),
      $green.LogExit, $green.WrapperExit, $green.Summary,
      $(if ($proven) { 'RED classified in ' + $red.LogPath + ' | GREEN ' + $green.LogPath } else { 'CHECK MANUALLY: ' + $red.LogPath })
    Write-Output $line
  }
  finally {
    if (Test-Path -LiteralPath $backup) {
      [System.IO.File]::Copy($backup, $path, $true)
      (Get-Item -LiteralPath $path).LastWriteTime = Get-Date
      Remove-Item -LiteralPath $backup -Force
    }
  }
}

$leftover = @(Get-ChildItem -Path $env:TEMP -Filter 'qa0024-*.bak' -ErrorAction SilentlyContinue)
$residue = 0
Get-ChildItem -Path (Join-Path $repo 'backend') -Recurse -Include *.cs -File |
  Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
  ForEach-Object { if ((Get-Content -Raw -LiteralPath $_.FullName) -match 'QA-MUTATION') { $residue++ } }

Write-Output "SUMMARY cycles=$ran failed=$failed residue-QA-MUTATION=$residue temp-backups=$($leftover.Count)"
if ($failed -gt 0 -or $residue -gt 0 -or $leftover.Count -gt 0) { exit 1 }
exit 0
