#Requires -Version 5.1
<#
qa-check-task-0006 — static half of AC-05 (TASK-0006), nothing here starts a host:
  1. ADR-25: no PackageReference carries Version= in any backend/**/*.csproj (0 hits).
  2. Scope of the human gate of 2026-11-06: the five observability packages exist as
     PackageVersion in backend/Directory.Packages.props with exactly the approved versions.
  3. The newest gate log exited 0 and counts MORE tests than the pre-change baseline
     (the new tests are in the total, as AC-05 demands).

Usage:
  powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0006.ps1
Exit: 0 = all checks green · 1 = at least one check failed
#>
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$failed = 0

function Check([bool]$Ok, [string]$Name, [string]$Detail) {
  if ($Ok) { Write-Output "PASS | $Name | $Detail" }
  else { Write-Output "FAIL | $Name | $Detail"; $script:failed++ }
}

# 1. ADR-25 — versions only in Directory.Packages.props.
$csprojs = Get-ChildItem -Path (Join-Path $repo 'backend') -Recurse -Filter *.csproj -File
$hits = @($csprojs | ForEach-Object { Select-String -LiteralPath $_.FullName -Pattern 'PackageReference[^>]*\sVersion=' })
Check ($hits.Count -eq 0) 'ADR-25 no Version= in any csproj' ("csproj={0} hits={1}" -f $csprojs.Count, $hits.Count)
foreach ($h in $hits) { Write-Output "       hit: $($h.Path):$($h.LineNumber)" }

# 2. Human-gate scope — exact package + version set, only as PackageVersion.
$propsPath = Join-Path $repo 'backend\Directory.Packages.props'
$props = Get-Content -LiteralPath $propsPath -Raw
$expected = @(
  @{ Id = 'OpenTelemetry.Extensions.Hosting';        Version = '1.20.1' },
  @{ Id = 'OpenTelemetry.Exporter.OpenTelemetryProtocol'; Version = '1.20.1' },
  @{ Id = 'OpenTelemetry.Instrumentation.AspNetCore'; Version = '1.20.1' },
  @{ Id = 'Serilog';                                  Version = '4.4.0' },
  @{ Id = 'Serilog.AspNetCore';                       Version = '10.0.0' }
)
foreach ($p in $expected) {
  $pattern = 'PackageVersion Include="{0}" Version="{1}"' -f [regex]::Escape($p.Id), [regex]::Escape($p.Version)
  Check ($props -match $pattern) 'pin present with gate version' ("{0} {1}" -f $p.Id, $p.Version)
}
Check (@($csprojs | ForEach-Object { Select-String -LiteralPath $_.FullName -Pattern 'PackageVersion' }).Count -eq 0) `
  'PackageVersion lives only in Directory.Packages.props' 'no PackageVersion inside any csproj'

# 3. Gate log — newest run green and counting the new tests.
$runFolder = Join-Path $repo 'docs\sdd\runs\test-runs'
$gateLogs = @(Get-ChildItem -LiteralPath $runFolder -Filter '*-test-gate.log' -File | Sort-Object Name -Descending)
Check ($gateLogs.Count -gt 0) 'a gate log exists' ("count={0}" -f $gateLogs.Count)
if ($gateLogs.Count -gt 0) {
  $newest = $gateLogs[0]
  $text = Get-Content -LiteralPath $newest.FullName -Raw
  $exit = if ($text -match '(?m)^exit:\s+(-?\d+)') { [int]$Matches[1] } else { $null }
  $total = if ($text -match 'Aprovado:\s+(\d+)') { [int]$Matches[1] } else { $null }
  Check ($exit -eq 0) 'newest gate log exit 0' ("{0} exit={1}" -f $newest.Name, $exit)

  $baselinePath = Join-Path $runFolder '20261106-124547-test-gate.log'
  if (Test-Path -LiteralPath $baselinePath) {
    $baselineText = Get-Content -LiteralPath $baselinePath -Raw
    $baselineTotal = if ($baselineText -match 'Aprovado:\s+(\d+)') { [int]$Matches[1] } else { $null }
    Check ($null -ne $total -and $null -ne $baselineTotal -and $total -gt $baselineTotal) `
      'new tests counted in the gate total' ("newest={0} baseline={1}" -f $total, $baselineTotal)
  }
  else {
    Check $false 'baseline gate log present' 'docs/sdd/runs/test-runs/20261106-124547-test-gate.log missing'
  }
}

if ($failed -eq 0) {
  Write-Output 'RESULT: PASS - all checks green'
  exit 0
}
Write-Output "RESULT: FAIL - $failed check(s) failed"
exit 1
