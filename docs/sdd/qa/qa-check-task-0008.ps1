#Requires -Version 5.1
<#
qa-check-task-0008 - repeatable checks behind TASK-0008's acceptance criteria. Read-only:
nothing here starts or stops the stack (the stateful proofs - compose up/down cycle, span in
Jaeger, exit-3 classification, mutation - are run as commands and recorded in QA-0007 doc).

  AC-01 static : Containerfile multi-stage, last FROM is the runtime image AS runtime, non-root
                 USER after it, dotnet publish only in an earlier stage.
  AC-01 image  : built image squid-service:dev - Config.User non-root, `dotnet --list-sdks`
                 EMPTY, runtimes present, base-image env free of DOTNET_SDK_VERSION /
                 ASPNET_VERSION (derives base = runtime, not sdk, not aspnet).
  AC-02 static : compose.yaml exposes 8080:8080, collector on 16686, depends_on present.
  AC-03 static : named volume squid-data declared and mapped to /data; no `down -v` anywhere
                 in sdd-test.ps1's compose mode.
  AC-05        : no secret-shaped line in compose.yaml (comment lines excluded), and the
                 container process really runs as 10001 (id -u -g).
  AC-06        : sdd-test.ps1 declares compose mode, guards podman-absent and provider-broken
                 as exit 3, and the gate logs (QA's own newest + DEV's two claims) are exit 0
                 with 97/97.

Usage:
  powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0008.ps1
Exit: 0 = all checks green - 1 = at least one check failed
#>
# Continue, not Stop: podman's stderr redirected with 2>&1 becomes a NativeCommandError under
# Stop in PS 5.1 and would kill the script mid-run instead of reporting a check.
$ErrorActionPreference = 'Continue'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$failed = 0

foreach ($required in 'infra\Containerfile', 'infra\compose.yaml', 'docs\sdd\_tools\sdd-test.ps1') {
  if (-not (Test-Path -LiteralPath (Join-Path $repo $required))) {
    Write-Output "FAIL | prerequisite present | $required missing"
    Write-Output 'RESULT: FAIL - 1 check(s) failed'
    exit 1
  }
}

function Check([bool]$Ok, [string]$Name, [string]$Detail) {
  if ($Ok) { Write-Output "PASS | $Name | $Detail" }
  else { Write-Output "FAIL | $Name | $Detail"; $script:failed++ }
}

$cfPath   = Join-Path $repo 'infra\Containerfile'
$coPath   = Join-Path $repo 'infra\compose.yaml'
$testPath = Join-Path $repo 'docs\sdd\_tools\sdd-test.ps1'

# ---------------------------------------------------------------- AC-01 static (Containerfile)
$cf = Get-Content -LiteralPath $cfPath
$fromIdx = @()
for ($i = 0; $i -lt $cf.Count; $i++) { if ($cf[$i] -match '^\s*FROM\s+') { $fromIdx += $i } }
Check ($fromIdx.Count -ge 2) 'AC-01 multi-stage (>=2 FROM)' ("FROM count={0}" -f $fromIdx.Count)

$lastFrom = $fromIdx[-1]
$lastFromLine = $cf[$lastFrom].Trim()
# final stage base image ends in /runtime:<tag> and the stage alias is literally `runtime`.
Check ($lastFromLine -match '^\s*FROM\s+\S*/runtime:\S+\s+AS\s+runtime\s*$') `
  'AC-01 final FROM is the runtime image AS runtime' $lastFromLine
Check (-not ($lastFromLine -match '\bAS\s+sdk\b')) 'AC-01 final stage is never sdk' $lastFromLine

$tail = @($cf[($lastFrom + 1)..($cf.Count - 1)])
$userLines = @($tail | Where-Object { $_ -match '^\s*USER\s+' })
$lastUser = if ($userLines.Count -gt 0) { $userLines[-1].Trim() } else { '' }
Check ($lastUser -match '^\s*USER\s+(?!root\s*$)(?!0\s*$)(?!0:0\s*$)\S+\s*$') `
  'AC-01 non-root USER after final FROM' $(if ($lastUser) { $lastUser } else { 'no USER after final FROM' })

$publishAfterLast = @($tail | Where-Object { $_ -match 'dotnet\s+publish' })
Check ($publishAfterLast.Count -eq 0) 'AC-01 dotnet publish only in earlier stage' `
  ("dotnet publish lines after final FROM={0}" -f $publishAfterLast.Count)
Check (@($cf | Where-Object { $_ -match 'COPY\s+--from=build' }).Count -ge 1) `
  'AC-01 final image gets the published output (COPY --from=build)' 'present'

# ---------------------------------------------------------------- AC-02/AC-03/AC-05 static (compose)
$co = Get-Content -LiteralPath $coPath
$coText = ($co -join "`n")
Check ($coText -match '(?m)^\s*-\s*"?8080:8080"?\s*$') 'AC-02 API port exposed 8080:8080' 'present'
Check ($coText -match '(?m)^\s*-\s*"?16686:16686"?\s*$') 'AC-02 collector query port 16686:16686' 'present'
Check ($coText -match '(?m)^\s*-\s*collector\s*$') 'AC-02 squid depends_on collector' 'present'
Check ($coText -match '(?m)^\s*volumes:\s*$') 'AC-03 top-level named volume declared' 'volumes: block present'
Check ($coText -match '(?m)^\s*squid-data:\s*$') 'AC-03 volume named squid-data' 'declared'
Check ($coText -match '(?m)^\s*-\s*squid-data:/data\s*$') 'AC-03 squid-data mapped to /data' 'present'
Check ($coText -match '(?m)^\s*Storage__Path:\s*/data/') 'AC-03 SQLite lives under /data' 'Storage__Path under /data'
Check ($coText -match 'OTEL_EXPORTER_OTLP_ENDPOINT:\s*http://collector:4317') `
  'AC-04 OTLP endpoint points at the collector service' 'http://collector:4317'

$plain = @($co | Where-Object { $_ -notmatch '^\s*#' })
$secret = @($plain | Where-Object { $_ -match '(?i)password|passwd|secret|token|credential|api[_-]?key|private[_-]?key' })
Check ($secret.Count -eq 0) 'AC-05 no secret-shaped line in compose.yaml' ("non-comment hits={0}" -f $secret.Count)
foreach ($s in $secret) { Write-Output "       hit: $s" }

# ---------------------------------------------------------------- AC-06 static (sdd-test.ps1)
$testSrc = Get-Content -LiteralPath $testPath -Raw
Check ($testSrc -match "ValidateSet\('probe', 'build', 'test', 'health', 'compose'\)") `
  'AC-06 compose mode declared in ValidateSet' 'probe,build,test,health,compose'
Check ($testSrc -match 'Get-Command podman' -and $testSrc -match 'podman ausente - ambiente \(exit 3\)') `
  'AC-06 podman-absent classified environment exit 3' 'guard present'
Check ($testSrc -match 'podman compose indisponivel' -and $testSrc -match 'provider de compose ausente - ambiente \(exit 3\)') `
  'AC-06 broken/missing compose provider classified environment exit 3' 'guard present'
# code lines only, and only real invocations: prose about `down -v` (comments, and the gate's own
# success message "apos down/up sem -v") must not trip this. shortcut: an -v on the line after
# `compose down` would be missed; the gate itself is the stronger proof of that behaviour.
$codeLines = @(Get-Content -LiteralPath $testPath | Where-Object { $_ -notmatch '^\s*#' })
$downV = @($codeLines | Where-Object { $_ -match 'compose\s+down\b[^\r\n]*(\s-v\b|--volumes\b)' })
Check ($downV.Count -eq 0) 'AC-03 no `down -v` invocation in sdd-test.ps1' ("code-line hits={0}" -f $downV.Count)
foreach ($d in $downV) { Write-Output "       hit: $d" }

# ---------------------------------------------------------------- AC-01/AC-05 image probes
$user = ''
$sdks = ''
$runtimes = ''
$envv = ''
$idout = ''
$imgOk = $true
try {
  $user = (& podman image inspect squid-service:dev --format '{{.Config.User}}' 2>&1 | Out-String).Trim()
  if ($LASTEXITCODE -ne 0) { $imgOk = $false; $user = "inspect failed: $user" }
} catch { $imgOk = $false }
Check $imgOk 'AC-01 image inspectable' ("Config.User={0}" -f $user)
if ($imgOk) {
  Check ($user -eq '10001:10001') 'AC-01/AC-05 Config.User is 10001:10001 (non-root)' $user

  $sdks = (& podman run --rm --entrypoint dotnet squid-service:dev --list-sdks 2>&1 | Out-String).Trim()
  Check ($sdks -eq '') 'AC-01 no SDK inside the final image (dotnet --list-sdks empty)' `
    ("output='{0}'" -f ($sdks -replace "`r?`n", ' ; '))

  $runtimes = (& podman run --rm --entrypoint dotnet squid-service:dev --list-runtimes 2>&1 | Out-String)
  Check ($runtimes -match 'Microsoft\.NETCore\.App' -and $runtimes -match 'Microsoft\.AspNetCore\.App') `
    'AC-01 runtimes present (ASP.NET shared framework copied in)' (($runtimes -split "`n" | Where-Object { $_ -match 'App ' }) -join ' ; ')

  $envv = (& podman image inspect squid-service:dev --format '{{range .Config.Env}}{{.}};{{end}}' 2>&1 | Out-String).Trim()
  # Derived from the three base images on this machine: only sdk:10.0 carries DOTNET_SDK_VERSION,
  # only aspnet/sdk carry ASPNET_VERSION, runtime:10.0 carries neither.
  Check ($envv -notmatch 'DOTNET_SDK_VERSION=') 'AC-01 final base is not sdk (no DOTNET_SDK_VERSION in env)' 'absent'
  Check ($envv -notmatch 'ASPNET_VERSION=') 'AC-01 final base is not aspnet (no ASPNET_VERSION in env)' 'absent'
  Check ($envv -match 'ASPNETCORE_URLS=http://\+:8080' -and $envv -match 'HOME=/app') `
    'AC-01 Containerfile ENV applied (ASPNETCORE_URLS, HOME)' 'present'

  # GNU id refuses -u -g together ("cannot print only of more than one choice") - ask for the
  # full line and parse uid=/gid= from it instead.
  $idout = (& podman run --rm --entrypoint id squid-service:dev 2>&1 | Out-String)
  $idLine = (($idout -split "`r?`n") | Where-Object { $_ -match 'uid=' } | Select-Object -First 1)
  Check (($idLine -match 'uid=10001\b') -and ($idLine -match 'gid=10001\b')) `
    'AC-05 container process runs as uid/gid 10001' $(if ($idLine) { $idLine.Trim() } else { 'no id output' })
}

# ---------------------------------------------------------------- AC-06 gate logs
$runFolder = Join-Path $repo 'docs\sdd\runs\test-runs'
$gateLogs = @(Get-ChildItem -LiteralPath $runFolder -Filter '*-test-gate.log' -File | Sort-Object Name -Descending)
Check ($gateLogs.Count -gt 0) 'a gate log exists' ("count={0}" -f $gateLogs.Count)
if ($gateLogs.Count -gt 0) {
  $newest = $gateLogs[0]
  $text = Get-Content -LiteralPath $newest.FullName -Raw
  $exit = if ($text -match '(?m)^exit:\s+(-?\d+)') { [int]$Matches[1] } else { $null }
  $total = if ($text -match 'Aprovado:\s+(\d+)') { [int]$Matches[1] } else { $null }
  Check ($exit -eq 0 -and $total -eq 97) 'newest gate log exit 0 with 97/97' ("{0} exit={1} aprovado={2}" -f $newest.Name, $exit, $total)
}

foreach ($claim in '20261106-210143-test-gate.log', '20261106-215042-test-gate.log') {
  $p = Join-Path $runFolder $claim
  if (Test-Path -LiteralPath $p) {
    $t = Get-Content -LiteralPath $p -Raw
    $e = if ($t -match '(?m)^exit:\s+(-?\d+)') { [int]$Matches[1] } else { $null }
    $n = if ($t -match 'Aprovado:\s+(\d+)') { [int]$Matches[1] } else { $null }
    Check ($e -eq 0 -and $n -eq 97) "DEV claimed gate log $claim exit 0 with 97/97" ("exit={0} aprovado={1}" -f $e, $n)
  } else {
    Check $false "DEV claimed gate log $claim exists" "missing docs/sdd/runs/test-runs/$claim"
  }
}

if ($failed -eq 0) {
  Write-Output 'RESULT: PASS - all checks green'
  exit 0
}
Write-Output "RESULT: FAIL - $failed check(s) failed"
exit 1
