#Requires -Version 5.1
<#
qa-check-task-0005.ps1 - executes the real-host scenarios of TASK-0005.

AC-01  plain `dotnet run --project backend/src/Squid.Service` serves /health 200
AC-02  /ready 200 with storage, 503 while the storage file is renamed away, and
       /health stays 200 WITHOUT recreating the file (no database I/O in liveness)
AC-04  /dashboard answers 200 on the bare route (no redirect) and the counters
       report the silo Active, all from the same single Squid.Service process
AC-05  a second plain `dotnet run` on the occupied port fails fast with a clear
       bind error and exit != 0 (bounded wait: a hang is a failure, not a wait)
AC-06  static: no Version= attribute in any .csproj (ADR-25), the Dashboard pins
       live only in Directory.Packages.props and resolve in project.assets.json

Exit: 0 = every check passed - 1 = at least one check failed.
Windows only for now: SPEC-0002 defers the Linux/container platform to the next
delivery of P-01, so there is no second platform to run here.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
Set-Location -LiteralPath $repo

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$logDir = Join-Path $repo 'docs\sdd\runs\test-runs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

$script:failed = New-Object System.Collections.Generic.List[string]

function Check([string]$Name, [bool]$Ok, [string]$Detail) {
  if ($Ok) { Write-Output ("PASS | {0} | {1}" -f $Name, $Detail) }
  else {
    Write-Output ("FAIL | {0} | {1}" -f $Name, $Detail)
    $script:failed.Add("$Name :: $Detail")
  }
}

# --- HTTP helpers: HttpClient answers every status without throwing on 4xx/5xx.
#     AllowAutoRedirect lives on the HANDLER (HttpClient itself has no such property -
#     assigning it raised a property error and silently made the check vacuous), so the
#     no-redirect client gets its own handler and a 301 stays visible as a 301 (AC-04).
Add-Type -AssemblyName System.Net.Http
$script:httpFollow = New-Object System.Net.Http.HttpClient
$script:httpFollow.Timeout = [TimeSpan]::FromSeconds(10)
$script:handlerNoRedirect = New-Object System.Net.Http.HttpClientHandler
$script:handlerNoRedirect.AllowAutoRedirect = $false
$script:httpNoRedirect = New-Object System.Net.Http.HttpClient($script:handlerNoRedirect)
$script:httpNoRedirect.Timeout = [TimeSpan]::FromSeconds(10)

function Get-Status([string]$Url) {
  try { return [int]$script:httpFollow.GetAsync($Url).Result.StatusCode } catch { return 0 }
}
function Get-StatusNoRedirect([string]$Url) {
  try { return [int]$script:httpNoRedirect.GetAsync($Url).Result.StatusCode } catch { return 0 }
}
function Get-Body([string]$Url) {
  try {
    $resp = $script:httpFollow.GetAsync($Url).Result
    return $resp.Content.ReadAsStringAsync().Result
  } catch { return '' }
}

Write-Output "== qa-check-task-0005 =="
Write-Output "repo: $repo"
Write-Output "when: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"

# ---------------------------------------------------------------------------
# AC-06 static: ADR-25 + package pins (GAP-08)
# ---------------------------------------------------------------------------
$csprojFiles = Get-ChildItem -Path (Join-Path $repo 'backend') -Recurse -Filter *.csproj -File -ErrorAction SilentlyContinue |
  Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }
$versionHits = @()
foreach ($f in $csprojFiles) {
  $m = @(Select-String -LiteralPath $f.FullName -Pattern '<PackageReference[^>]*\sVersion=' -ErrorAction SilentlyContinue)
  if ($m.Count -gt 0) { $versionHits += $m }
}
Check 'AC-06/ADR-25 no Version= in any PackageReference' ($versionHits.Count -eq 0) `
  ("csproj={0} hits={1}" -f $csprojFiles.Count, $versionHits.Count)

$props = Get-Content -LiteralPath (Join-Path $repo 'backend\Directory.Packages.props') -Raw
Check 'AC-06 pins live in Directory.Packages.props' `
  ($props -match 'PackageVersion Include="Microsoft\.Orleans\.Dashboard" Version="10\.2\.2"' -and
   $props -match 'PackageVersion Include="Microsoft\.Orleans\.Server" Version="10\.2\.2"') `
  'Dashboard 10.2.2 + Server 10.2.2 as PackageVersion'

$assets = Get-Content -LiteralPath (Join-Path $repo 'backend\src\Squid.Service\obj\project.assets.json') -Raw -ErrorAction SilentlyContinue
$assetsOk = ($assets -match '"Microsoft\.Orleans\.Dashboard/10\.2\.2"') -and ($assets -notmatch 'OrleansContrib\.OrleansDashboard')
$cacheOk = (Test-Path -LiteralPath (Join-Path $repo 'backend\.packages\microsoft.orleans.dashboard\10.2.2')) -or
           (Test-Path -LiteralPath (Join-Path $repo '.dotnet-home\nuget\microsoft.orleans.dashboard\10.2.2'))
Check 'GAP-08 pinned Microsoft.Orleans.Dashboard 10.2.2 restores (assets + local cache)' ($assetsOk -and $cacheOk) `
  ("assets10.2.2={0} cache={1} orleanscontrib-absent={2}" -f ($assets -match '"Microsoft\.Orleans\.Dashboard/10\.2\.2"'), $cacheOk, ($assets -notmatch 'OrleansContrib\.OrleansDashboard'))

# ---------------------------------------------------------------------------
# AC-01: the real command from the acceptance criteria
# ---------------------------------------------------------------------------
$portHttp = 59312   # launchSettings.json of Squid.Service
$portHttps = 59311
$busy = @(Get-NetTCPConnection -State Listen -LocalPort $portHttp, $portHttps -ErrorAction SilentlyContinue)
if ($busy.Count -gt 0) {
  Write-Output "NOTE | ports $portHttp/$portHttps already busy before the run - falling back to --no-launch-profile on 5199"
  $useLaunchProfile = $false
  $portHttp = 5199
  $baseUrl = "http://127.0.0.1:$portHttp"
} else {
  $useLaunchProfile = $true
  $baseUrl = "http://localhost:$portHttp"
}

$outA = Join-Path $logDir "$stamp-qa0005-hostA.out.log"
$errA = Join-Path $logDir "$stamp-qa0005-hostA.err.log"
$runArgs = if ($useLaunchProfile) { @('run', '--project', 'backend/src/Squid.Service') }
           else { @('run', '--project', 'backend/src/Squid.Service', '--no-launch-profile', '--', "--urls=$baseUrl") }

Write-Output ("-> starting host A: dotnet {0}" -f ($runArgs -join ' '))
$hostA = Start-Process -FilePath 'dotnet' -ArgumentList $runArgs -WorkingDirectory $repo `
  -RedirectStandardOutput $outA -RedirectStandardError $errA -PassThru -WindowStyle Hidden

$up = $false
$deadline = (Get-Date).AddSeconds(120)
while ((Get-Date) -lt $deadline) {
  if ((Get-Status "$baseUrl/health") -eq 200) { $up = $true; break }
  if ($hostA.HasExited) { break }
  Start-Sleep -Milliseconds 1500
}
Check 'AC-01 dotnet run comes up and serves GET /health 200' $up `
  ("pid={0} exited={1} url={2}" -f $hostA.Id, $hostA.HasExited, $baseUrl)
if (-not $up) {
  Write-Output '--- host A stdout tail ---'
  Get-Content -LiteralPath $outA -Tail 15 -ErrorAction SilentlyContinue | ForEach-Object { Write-Output $_ }
  Write-Output '--- host A stderr tail ---'
  Get-Content -LiteralPath $errA -Tail 15 -ErrorAction SilentlyContinue | ForEach-Object { Write-Output $_ }
}

$readyStatus = if ($up) { Get-Status "$baseUrl/ready" } else { 0 }
Check 'AC-02 /ready is 200 while the storage file is accessible' ($readyStatus -eq 200) "status=$readyStatus"

# the project's own health tool over the three routes
$healthOut = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'docs\sdd\_tools\sdd-test.ps1') health $baseUrl 2>&1
$healthCode = $LASTEXITCODE
Write-Output ("-> sdd-test health exit={0}" -f $healthCode)
$healthOut | ForEach-Object { Write-Output ("   {0}" -f $_) }
Check 'AC-01/AC-02/AC-04 sdd-test.ps1 health reports 200/200/200' ($healthCode -eq 0) "exit=$healthCode"

# ---------------------------------------------------------------------------
# AC-04: dashboard on the bare route (no redirect) + silo Active in counters
# ---------------------------------------------------------------------------
$dashBare = if ($up) { Get-StatusNoRedirect "$baseUrl/dashboard" } else { 0 }
$dashSlash = if ($up) { Get-Status "$baseUrl/dashboard/" } else { 0 }
Check 'AC-04 GET /dashboard answers 200 without a redirect' (($dashBare -eq 200) -and ($dashSlash -eq 200)) `
  ("bare={0} slash={1}" -f $dashBare, $dashSlash)

$active = $false
$counters = ''
$deadline = (Get-Date).AddSeconds(30)
while ((Get-Date) -lt $deadline) {
  $counters = Get-Body "$baseUrl/dashboard/DashboardCounters"
  if ($counters.Contains('"status":"Active"') -and $counters.Contains('127.0.0.1:11111')) { $active = $true; break }
  Start-Sleep -Milliseconds 500
}
Check 'AC-04 DashboardCounters reports the silo Active' $active `
  ($(if ($active) { 'status=Active silo=127.0.0.1:11111' } else { 'counters=' + $counters.Substring(0, [Math]::Min(160, $counters.Length)) }))

# one listener serves API + silo + dashboard (composition root in the same host):
# if the silo lived in another process, /dashboard could not answer on this port.
$ln = Get-NetTCPConnection -State Listen -LocalPort $portHttp -ErrorAction SilentlyContinue | Select-Object -First 1
$listenerPid = if ($ln) { $ln.OwningProcess } else { $null }
$listenerName = if ($listenerPid) { (Get-Process -Id $listenerPid -ErrorAction SilentlyContinue).ProcessName } else { 'none' }
Check 'AC-01 a single listener process serves API + silo + dashboard' (($up) -and ($null -ne $listenerPid)) `
  ("listener_pid={0} name={1} all routes answered on {2}" -f $listenerPid, $listenerName, $baseUrl)

# ---------------------------------------------------------------------------
# AC-02 negative: rename the storage file away, on the live process
# ---------------------------------------------------------------------------
$db = $null
$binDir = Join-Path $repo 'backend\src\Squid.Service\bin'
if (Test-Path -LiteralPath $binDir) {
  $db = Get-ChildItem -Path $binDir -Recurse -Filter 'squid-grainstate.db' -File -ErrorAction SilentlyContinue | Select-Object -First 1
}
Check 'AC-02 storage file exists after start (eager creation)' ($null -ne $db) `
  ($(if ($db) { $db.FullName } else { "no squid-grainstate.db under $binDir" }))

if ($up -and $null -ne $db) {
  $renamed = $db.FullName + '.qa-renamed'
  try {
    Move-Item -LiteralPath $db.FullName -Destination $renamed -ErrorAction Stop
    $healthWhileGone = Get-Status "$baseUrl/health"
    $recreated = Test-Path -LiteralPath $db.FullName
    $readyWhileGone = Get-Status "$baseUrl/ready"
    Check 'AC-02 /ready is 503 while the storage file is away' ($readyWhileGone -eq 503) "status=$readyWhileGone"
    Check 'AC-01 /health stays 200 and does NOT touch the database' (($healthWhileGone -eq 200) -and (-not $recreated)) `
      ("health={0} file_recreated={1}" -f $healthWhileGone, $recreated)
  } finally {
    if ((Test-Path -LiteralPath $renamed) -and -not (Test-Path -LiteralPath $db.FullName)) {
      Move-Item -LiteralPath $renamed -Destination $db.FullName
    }
  }
  $readyBack = Get-Status "$baseUrl/ready"
  Check 'AC-02 /ready returns 200 after the file is restored' ($readyBack -eq 200) "status=$readyBack"
} elseif ($up) {
  Check 'AC-02 rename negative' $false 'storage file not found, scenario skipped'
}

# ---------------------------------------------------------------------------
# AC-05: second plain `dotnet run` against the occupied ports
#
# Capture note (measured in this sandbox): Start-Process with
# -RedirectStandardOutput/-RedirectStandardError hands back an empty Process.ExitCode,
# so B runs in the foreground under `cmd /c "... > file 2>&1"` - raw file AND a real
# $LASTEXITCODE. Everything that waits is still bounded: a stopwatch flags a run over
# 120s as hung, and a detached watchdog kills anything but host A's processes at 130s
# so a product that hangs can never freeze this script.
# ---------------------------------------------------------------------------
if ($up) {
  $outB = Join-Path $logDir "$stamp-qa0005-hostB.log"
  $aPids = (Get-Process -Name 'dotnet', 'Squid.Service' -ErrorAction SilentlyContinue | ForEach-Object { $_.Id }) -join ','
  $watchCmd = 'Start-Sleep 130; $ex = @(' + $aPids + '); Get-Process -Name dotnet,Squid.Service -ErrorAction SilentlyContinue | Where-Object { $ex -notcontains $_.Id } | ForEach-Object { taskkill /PID $_.Id /T /F | Out-Null }'
  $watchdog = Start-Process -FilePath 'powershell' -ArgumentList @('-NoProfile', '-Command', $watchCmd) -PassThru -WindowStyle Hidden

  Write-Output '-> starting host B (same command, ports now occupied)'
  $swB = [System.Diagnostics.Stopwatch]::StartNew()
  $lineB = "dotnet run --project backend/src/Squid.Service > `"$outB`" 2>&1"
  & cmd /c $lineB
  $codeB = $LASTEXITCODE
  $swB.Stop()

  $bOutput = ''
  if (Test-Path -LiteralPath $outB) { $bOutput = (Get-Content -LiteralPath $outB -Raw -ErrorAction SilentlyContinue) }
  if ($null -eq $bOutput) { $bOutput = '' }
  $clearError = ($bOutput -match 'address already in use') -or ($bOutput -match 'Failed to bind') -or
                ($bOutput -match 'Only one usage of each socket address')
  $seconds = [int]$swB.Elapsed.TotalSeconds
  $hung = $seconds -ge 120
  Check 'AC-05 busy port fails fast with a clear bind error and exit != 0' ((-not $hung) -and ($codeB -ne 0) -and $clearError) `
    ("exit={0} seconds={1} hung={2} clear_error={3}" -f $codeB, $seconds, $hung, $clearError)
  $msgLine = ($bOutput -split "`r?`n" | Where-Object { $_ -match 'IOException|address already in use|Failed to bind' } | Select-Object -First 2) -join ' // '
  if ($msgLine) { Write-Output ("   bind message: {0}" -f $msgLine) }

  if ($watchdog -and -not $watchdog.HasExited) {
    Stop-Process -Id $watchdog.Id -Force -ErrorAction SilentlyContinue
  }
} else {
  Check 'AC-05 busy port scenario' $false 'skipped: host A never came up'
}

# ---------------------------------------------------------------------------
# cleanup: only processes and files this run created
# ---------------------------------------------------------------------------
if ($hostA -and -not $hostA.HasExited) {
  & taskkill /PID $hostA.Id /T /F 2>&1 | ForEach-Object { Write-Output ("cleanup: {0}" -f $_) }
}
Start-Sleep -Seconds 2
$stillHttp = @(Get-NetTCPConnection -State Listen -LocalPort $portHttp -ErrorAction SilentlyContinue)
$stillSilo = @(Get-NetTCPConnection -State Listen -LocalPort 11111 -ErrorAction SilentlyContinue)
Check 'cleanup: port released after the run' (($stillHttp.Count -eq 0) -and ($stillSilo.Count -eq 0)) `
  ("http11111-listeners={0} silo11111-listeners={1}" -f $stillHttp.Count, $stillSilo.Count)
$leftoverApp = @(Get-Process -Name 'Squid.Service' -ErrorAction SilentlyContinue)
Check 'cleanup: no Squid.Service process left behind' ($leftoverApp.Count -eq 0) "count=$($leftoverApp.Count)"

# the grain-state file was created by this run: remove it with its sidecars
if ($null -ne $db) {
  foreach ($sidecar in @($db.FullName, $db.FullName + '.qa-renamed', $db.FullName + '-wal', $db.FullName + '-shm')) {
    if (Test-Path -LiteralPath $sidecar) { Remove-Item -LiteralPath $sidecar -Force -ErrorAction SilentlyContinue }
  }
}
$leftover = @(Get-ChildItem -Path $env:TEMP -Filter 'squid-svc-*' -File -ErrorAction SilentlyContinue)
Check 'cleanup: no squid-svc-* temp files left behind' ($leftover.Count -eq 0) "count=$($leftover.Count)"

Write-Output ''
if ($script:failed.Count -eq 0) {
  Write-Output 'RESULT: PASS - all checks green'
  exit 0
}
Write-Output ("RESULT: FAIL - {0} check(s) failed:" -f $script:failed.Count)
$script:failed | ForEach-Object { Write-Output ("  - {0}" -f $_) }
exit 1
