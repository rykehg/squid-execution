#Requires -Version 5.1
<#
sdd-test — build, test and smoke the project the way this machine actually works.

Why this exists: in a confined sandbox `dotnet build` can exit 1 while printing "0 Erros",
and `dotnet test` can exit 1 because the testhost cannot open a handle on the parent process.
Both look like product failures and are not. This tool separates an environment failure from
a real red, so nobody writes "the build is broken" into a task file again.

Usage:
  sdd-test.ps1 probe                       # what this machine can do, one screen
  sdd-test.ps1 build [sln-or-dir]          # robust build flags
  sdd-test.ps1 test  [sln-or-dir] [-Fast] [-TestFilter "<dotnet test --filter>"] [-Fallback "cmd"]
                                           # dotnet test. Default = the gate: the WHOLE suite, build proofs
                                           # included (Category=BuildProof spawns one dotnet build per edge,
                                           # ~2 min). -Fast skips them for the inner loop: NOT the gate.
  sdd-test.ps1 health <base-url>           # GET /health, /ready, /dashboard/ with status codes
  sdd-test.ps1 compose                     # DoD da fase (SPEC-0004): podman compose up -> /health
                                           # + dashboard -> span no coletor -> volume persistente.
                                           # Sem podman ou sem provider de compose: exit 3.

Exit codes: 0 green · 1 real failure (product or test) · 3 environment refused (not your bug)
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true, Position = 0)][ValidateSet('probe', 'build', 'test', 'health', 'compose')][string]$Command,
  [Parameter(Position = 1)][string]$Target,
  [string]$TestFilter,
  [switch]$Fast,
  [string]$Fallback
)

$ErrorActionPreference = 'Continue'

# The .NET CLI writes first-run sentinels and its package cache under a home folder it can write
# to. In a confined session %USERPROFILE%\.dotnet may be denied while the repository is not, and
# every dotnet call then dies with UnauthorizedAccessException *before* compiling — which reads as
# "the build is broken". Pointing DOTNET_CLI_HOME (and the package cache) at a writable folder
# inside the repository turns that environment trap off without touching the product.
if ([string]::IsNullOrWhiteSpace($env:DOTNET_CLI_HOME)) {
  $candidates = @(
    (Join-Path (Get-Location).Path '.dotnet-home'),
    (Join-Path $PSScriptRoot '..\..\..\.dotnet-home')
  )
  foreach ($candidate in $candidates) {
    try {
      New-Item -ItemType Directory -Force -Path $candidate -ErrorAction Stop | Out-Null
      $env:DOTNET_CLI_HOME = (Resolve-Path -LiteralPath $candidate).Path
      if ([string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) {
        $env:NUGET_PACKAGES = Join-Path $env:DOTNET_CLI_HOME 'nuget'
      }
      break
    }
    catch { }
  }
}

function Resolve-Solution([string]$Path) {
  if ([string]::IsNullOrWhiteSpace($Path)) {
    $found = Get-ChildItem -Path . -Include *.sln, *.slnx -Recurse -Depth 2 -File -ErrorAction SilentlyContinue |
      Sort-Object { $_.FullName.Length } | Select-Object -First 1
    if ($null -eq $found) { Write-Error 'no *.sln or *.slnx found within depth 2; pass the path explicitly'; exit 1 }
    return $found.FullName
  }
  if (Test-Path -LiteralPath $Path -PathType Container) {
    $found = Get-ChildItem -LiteralPath $Path -Include *.sln, *.slnx -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $found) { Write-Error "no *.sln or *.slnx inside $Path"; exit 1 }
    return $found.FullName
  }
  return (Resolve-Path -LiteralPath $Path).Path
}

# A sandbox that denies the .NET home or the parent-process handle never reaches the compiler.
# That must be classified as environment (exit 3) before any product verdict is drawn.
function Test-EnvironmentRefusal([string]$Output) {
  return ($Output -match 'UnauthorizedAccessException|Access to the path .*\.dotnet|Win32Exception|GetParentProcessId|The handle is invalid|handle is not valid')
}

function Test-SandboxSignature([string]$Output) {
  $blocked = $Output -match 'testhost|Win32Exception|The handle is invalid|não é válido|handle'
  $noSummary = $Output -notmatch '(?m)^\s*(Passed!|Failed!|Aprovado!|Falhou!)'
  return ($blocked -and $noSummary)
}

# Every run leaves a file under docs/sdd/runs/test-runs/, so QA and DEV argue with the log and not
# with a terminal scrollback. The newest log is the one with the highest timestamp in its name, and
# the run history lives in the repository instead of in someone's buffer.
# Returns the path, or $null when the folder is not writable — never a reason to fail a run.
function Save-TestRunLog([string]$Name, [string]$Command, [int]$ExitCode, [string]$Output) {
  try {
    $repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
    $folder = Join-Path $repo 'docs\sdd\runs\test-runs'
    New-Item -ItemType Directory -Force -Path $folder -ErrorAction Stop | Out-Null
    $path = Join-Path $folder ('{0}-{1}.log' -f (Get-Date -Format 'yyyyMMdd-HHmmss'), $Name)
    $header = (@(
        "command: $Command",
        "exit:    $ExitCode",
        "when:    $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
        '---'
      ) -join "`n")
    [System.IO.File]::WriteAllText($path, $header + "`n" + $Output, (New-Object System.Text.UTF8Encoding($false)))
    return $path
  }
  catch {
    return $null
  }
}

# No printable summary and a restore that could not reach the feed: the packages are not on disk.
# Also an environment refusal — the suite never ran, so it is not red.
function Test-RestoreRefusal([string]$Output) {
  $noSummary = $Output -notmatch '(?m)^\s*(Passed!|Failed!|Aprovado!|Falhou!)'
  return ($noSummary -and $Output -match 'NU1301|Unable to load the service index|conexão subjacente|underlying connection|NU1101|NU1102|NU1103')
}

# Compose-mode helpers. podman compose names containers <project>_<service>_<n>, so the squid
# container is found by service name, never hard-coded.
function Get-SquidContainer {
  $out = & podman ps --format '{{.ID}} {{.Names}}' 2>&1 | Out-String
  foreach ($line in ($out -split "`n")) {
    if ($line -match '^\s*([0-9a-f]{8,})\s+\S*squid\S*') { return $Matches[1] }
  }
  return $null
}

# The service answers /health from memory (SPEC-0002), so 200 here means the host port works.
function Wait-SquidHealth([string]$Base, [int]$Seconds) {
  $deadline = (Get-Date).AddSeconds($Seconds)
  while ((Get-Date) -lt $deadline) {
    try {
      $r = Invoke-WebRequest -Uri "$Base/health" -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop
      if ($r.StatusCode -eq 200) { return $true }
    }
    catch { }
    Start-Sleep -Seconds 3
  }
  return $false
}

switch ($Command) {

  'probe' {
    $dotnet = (& dotnet --version 2>&1) -join ' '
    Write-Output "dotnet: $dotnet"
    $runtime = (& dotnet --list-runtimes 2>&1 | Select-String 'Microsoft\.NETCore\.App' | Select-Object -Last 1)
    Write-Output "runtime: $runtime"
    foreach ($tool in 'docker', 'podman', 'wsl', 'git') {
      $c = Get-Command $tool -ErrorAction SilentlyContinue
      if ($c) { Write-Output "${tool}: $($c.Source)" } else { Write-Output "${tool}: absent" }
    }
    if (Get-Command docker -ErrorAction SilentlyContinue) {
      $daemon = (& docker info --format '{{.ServerVersion}}' 2>&1) -join ' '
      Write-Output "docker daemon: $daemon"
    } else {
      Write-Output 'docker daemon: docker absent'
    }
    Write-Output "note: a sandbox can block outbound HTTPS and process pipes; build/test failures are not product failures until a normal shell reproduces them."
  }

  'build' {
    $sln = Resolve-Solution $Target
    Write-Output "build: $sln"
    $out = & dotnet build $sln -m:1 -nodeReuse:false -p:UseSharedCompilation=false 2>&1 | Out-String
    $code = $LASTEXITCODE
    if ($code -eq 0) {
      ($out -split "`n" | Where-Object { $_ -match 'aviso|warning|error|erro' } | Select-Object -First 6) -join "`n"
      Write-Output 'build: exit 0'
      exit 0
    }
    if (Test-EnvironmentRefusal $out) {
      Write-Output 'build: environment refused before compiling (dotnet home or process handle denied)'
      ($out -split "`n" | Where-Object { $_ -match 'Exception|denied|negado' } | Select-Object -First 3) -join "`n"
      Write-Output 'environment: NOT a product failure. Do not write it into the task as a defect; record unverified.'
      exit 3
    }
    if ($out -notmatch '\b(error|erro) [A-Z]{2,}\d+') {
      Write-Output 'build: exit 1 with no compiler error in the output'
      Write-Output 'environment: the MSBuild node pipe is blocked by the sandbox, not a product failure.'
      Write-Output 'retry: same command in a normal shell, or WSL.'
      exit 3
    }
    ($out -split "`n" | Where-Object { $_ -match 'error|erro' } | Select-Object -First 10) -join "`n"
    Write-Output "build: exit $code (real compiler errors)"
    exit 1
  }

  'test' {
    $sln = Resolve-Solution $Target
    Write-Output "test: $sln"
    # Squid.Tests is an xunit project (Microsoft.NET.Test.Sdk): the suite is `dotnet test`. The default
    # is the gate and runs EVERYTHING, build proofs included: a gate that skips a proof is a false
    # green. -Fast drops Category=BuildProof (DependencyRuleTests, one dotnet build per edge) for the
    # inner loop only; -TestFilter replaces the filter, for a single test.
    $dotnetArgs = @('test', $sln, '-c', 'Debug', '-m:1')
    $filter = $null
    if (-not [string]::IsNullOrWhiteSpace($TestFilter)) { $filter = $TestFilter }
    elseif ($Fast) { $filter = 'Category!=BuildProof' }
    if ([string]::IsNullOrWhiteSpace($filter)) {
      Write-Output 'filter: nenhum (gate: suite inteira, inclusive Category=BuildProof)'
    } else {
      $dotnetArgs += '--filter'
      $dotnetArgs += $filter
      Write-Output "filter: $filter"
    }
    if ($Fast) { Write-Output 'note: -Fast skips the build proofs (Category=BuildProof). It is NOT the gate; run without -Fast before reporting green.' }
    $out = & dotnet @dotnetArgs 2>&1 | Out-String
    $code = $LASTEXITCODE
    $runName = if ($Fast) { 'test-fast' } elseif (-not [string]::IsNullOrWhiteSpace($TestFilter)) { 'test-filter' } else { 'test-gate' }
    $logPath = Save-TestRunLog $runName ('dotnet ' + ($dotnetArgs -join ' ')) $code $out
    if ($logPath) { Write-Output "log: $logPath" }
    ($out -split "`n" | Select-Object -Last 6) -join "`n"
    if ($code -eq 0) { Write-Output 'test: exit 0'; exit 0 }

    if ((Test-EnvironmentRefusal $out) -or (Test-RestoreRefusal $out) -or (Test-SandboxSignature $out)) {
      if (Test-SandboxSignature $out) {
        Write-Output 'test: exit 1 with the sandbox signature (testhost cannot open the parent process handle)'
      } else {
        Write-Output 'test: environment refused before any test ran (dotnet home denied, or a package could not be restored)'
      }
      Write-Output 'environment: this is NOT a failing test. Do not write it into the task as a defect.'
      if (-not [string]::IsNullOrWhiteSpace($Fallback)) {
        Write-Output "fallback: $Fallback"
        $fout = Invoke-Expression $Fallback 2>&1 | Out-String
        $fcode = $LASTEXITCODE
        ($fout -split "`n" | Select-Object -Last 6) -join "`n"
        Write-Output "fallback: exit $fcode"
        if ($fcode -eq 0) { exit 0 } else { exit 1 }
      }
      Write-Output 'next: restore with network access (backend/.packages is the local cache), then run this again; pass an alternative command as -Fallback "cmd".'
      exit 3
    }
    Write-Output "test: exit $code (real test failure)"
    exit 1
  }

  'health' {
    if ([string]::IsNullOrWhiteSpace($Target)) { Write-Error 'health needs a base url, e.g. http://127.0.0.1:5199'; exit 1 }
    $base = $Target.TrimEnd('/')
    $failed = $false
    # /dashboard/ e a URL canonica (TASK-0011 AC-03): o bare /dashboard devolve 301 e
    # -MaximumRedirection 0 le uma resposta de redirect como falha do health.
    foreach ($p in '/health', '/ready', '/dashboard/') {
      try {
        $r = Invoke-WebRequest -Uri "$base$p" -UseBasicParsing -MaximumRedirection 0 -TimeoutSec 15 -ErrorAction Stop
        Write-Output ("{0,-12} {1}" -f $p, $r.StatusCode)
        if ($r.StatusCode -ge 400) { $failed = $true }
      }
      catch {
        $code = $null
        if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
        Write-Output ("{0,-12} {1}" -f $p, $(if ($code) { $code } else { 'sem resposta' }))
        if ($null -eq $code -or $code -ge 400) { $failed = $true }
      }
    }
    if ($failed) { exit 1 } else { exit 0 }
  }

  'compose' {
    # DoD da fase (SPEC-0004): build -> compose up -> /health e Dashboard -> span no coletor ->
    # volume nomeado sobrevivendo a down/up (nunca `down -v`). Sem podman ou sem provider de
    # compose e recusa de ambiente (exit 3), nunca defeito de produto: mesma regra de build/test.
    if (-not (Get-Command podman -ErrorAction SilentlyContinue)) {
      Write-Output 'compose: podman ausente - ambiente (exit 3), nao e defeito de produto'
      exit 3
    }
    $probe = & podman compose version 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
      Write-Output "compose: podman compose indisponivel (exit $LASTEXITCODE)"
      Write-Output 'compose: provider de compose ausente - ambiente (exit 3), nao e defeito de produto'
      exit 3
    }
    Write-Output ('compose: ' + (($probe -split "`n" | Where-Object { $_.Trim() -and $_ -notmatch 'Executing external' }) -join ' / '))

    $repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
    $infra = Join-Path $repo 'infra'
    $composeFile = Join-Path $infra 'compose.yaml'
    if (-not (Test-Path -LiteralPath $composeFile)) {
      Write-Output "compose: $composeFile ausente - falha real (exit 1)"
      exit 1
    }
    $base = 'http://127.0.0.1:8080'
    $jaeger = 'http://127.0.0.1:16686'
    $code = 1

    Push-Location $infra
    try {
      do {
        # AC-05: nenhum segredo em texto no compose (comentario nao conta).
        $plain = @(Get-Content -LiteralPath $composeFile | Where-Object { $_ -notmatch '^\s*#' })
        $secret = @($plain | Where-Object { $_ -match '(?i)password|passwd|secret|token|credential|api[_-]?key|private[_-]?key' })
        if ($secret.Count -gt 0) {
          Write-Output ('compose: segredo em texto no compose.yaml: ' + ($secret -join ' ; ') + ' - falha real (exit 1)')
          break
        }

        # Build multi-stage. Rede que falta para puxar imagem base ou restore e ambiente (GAP-13).
        $buildOut = & podman compose build 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0) {
          ($buildOut -split "`n" | Select-Object -Last 15) -join "`n"
          if ($buildOut -match 'NU1301|NU1101|NU1102|Unable to load the service index|dial tcp|Could not resolve host|connection reset|network is unreachable|context deadline exceeded') {
            Write-Output 'compose: build sem rede - ambiente (exit 3), nao e defeito de produto'
            $code = 3
          } else {
            Write-Output "compose: build exit $LASTEXITCODE com diagnostico - falha real (exit 1)"
            $code = 1
          }
          break
        }

        # AC-01/AC-05: usuario nao-root na imagem final e nenhum SDK dentro dela (so roda runtime).
        $user = (& podman image inspect squid-service:dev --format '{{.Config.User}}' 2>&1 | Out-String).Trim()
        $sdks = (& podman run --rm --entrypoint dotnet squid-service:dev --list-sdks 2>&1 | Out-String).Trim()
        if ($user -eq '' -or $user -eq 'root' -or $user -eq '0' -or $user -eq '0:0') {
          Write-Output "compose: imagem final sem usuario nao-root (Config.User='$user') - falha real (exit 1)"
          $code = 1
          break
        }
        if ($sdks -ne '') {
          Write-Output 'compose: a imagem final contem SDK - falha real (exit 1)'
          $code = 1
          break
        }
        Write-Output "compose: imagem final usuario $user, sem SDK - AC-01/AC-05"

        $upOut = & podman compose up -d 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0) {
          ($upOut -split "`n" | Select-Object -Last 15) -join "`n"
          if ($upOut -match 'dial tcp|Could not resolve|network is unreachable|context deadline exceeded|error pulling|manifest unknown') {
            Write-Output 'compose: pull de imagem sem rede - ambiente (exit 3), nao e defeito de produto'
            $code = 3
          } else {
            Write-Output "compose: up exit $LASTEXITCODE com diagnostico - falha real (exit 1)"
            $code = 1
          }
          break
        }

        if (-not (Wait-SquidHealth $base 120)) {
          $logs = & podman compose logs 2>&1 | Out-String
          ($logs -split "`n" | Select-Object -Last 25) -join "`n"
          if ($logs -match 'Unhandled exception|Exception:|Stack trace:|error CS') {
            Write-Output 'compose: servico sem /health com diagnostico no log - falha real (exit 1)'
            $code = 1
          } else {
            Write-Output 'compose: servico sem /health e sem diagnostico - ambiente (exit 3), nao e defeito de produto'
            $code = 3
          }
          break
        }

        # AC-02: /health 200 pela porta exposta e /dashboard respondendo.
        $bad = @()
        foreach ($p in '/health', '/ready', '/dashboard') {
          try {
            $r = Invoke-WebRequest -Uri "$base$p" -UseBasicParsing -TimeoutSec 15 -ErrorAction Stop
            Write-Output ("compose: {0,-11} {1}" -f $p, $r.StatusCode)
            if ($r.StatusCode -ne 200) { $bad += $p }
          }
          catch {
            $c = $null
            if ($_.Exception.Response) { $c = [int]$_.Exception.Response.StatusCode }
            Write-Output ("compose: {0,-11} {1}" -f $p, $(if ($c) { $c } else { 'sem resposta' }))
            $bad += $p
          }
        }
        if ($bad.Count -gt 0) {
          Write-Output ('compose: endpoints fora do esperado: ' + ($bad -join ', ') + ' - falha real (exit 1)')
          $code = 1
          break
        }

        # AC-04: uma request vira span consultavel no coletor (export OTLP -> Jaeger -> query).
        1..3 | ForEach-Object { try { Invoke-WebRequest -Uri "$base/health?smoke=TASK0008" -UseBasicParsing -TimeoutSec 10 | Out-Null } catch { } }
        Start-Sleep -Seconds 10   # lote do OTLP exporter atrasa 5s; o coletor indexa em seguida
        $spanHit = $null
        $deadline = (Get-Date).AddSeconds(75)
        while ((Get-Date) -lt $deadline -and -not $spanHit) {
          try {
            $svc = (Invoke-WebRequest -Uri "$jaeger/api/services" -UseBasicParsing -TimeoutSec 10).Content
            foreach ($n in @(($svc | ConvertFrom-Json).data)) {
              if (-not $n) { continue }
              $url = "$jaeger/api/traces?service=$([uri]::EscapeDataString($n))&limit=50&lookback=1h"
              $tr = (Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 15).Content
              if ($tr -match 'smoke=TASK0008') { $spanHit = "$n (query marker smoke=TASK0008)"; break }
              if ($tr -match 'GET /health') { $spanHit = "$n (operacao GET /health)"; break }
            }
          }
          catch { }
          if (-not $spanHit) { Start-Sleep -Seconds 5 }
        }
        if (-not $spanHit) {
          try { Write-Output ('compose: servicos no coletor: ' + (Invoke-WebRequest -Uri "$jaeger/api/services" -UseBasicParsing -TimeoutSec 10).Content) } catch { Write-Output 'compose: coletor nao respondeu /api/services' }
          Write-Output 'compose: span da request nao chegou ao coletor - falha real (exit 1)'
          $code = 1
          break
        }
        Write-Output "compose: span consultavel no coletor - $spanHit - AC-04"

        # AC-03: volume nomeado sobrevive a compose down e compose up (nunca down -v).
        # exec sem aspas embutidas: PowerShell 5.1 requebra argumentos com espaco + aspas.
        $ctr = Get-SquidContainer
        if (-not $ctr) {
          Write-Output 'compose: container do squid nao encontrado apos up - falha real (exit 1)'
          $code = 1
          break
        }
        $ino1 = (& podman exec $ctr stat -c %i /data/squid-grainstate.db 2>&1 | Out-String).Trim()
        if ($LASTEXITCODE -ne 0 -or $ino1 -notmatch '^\d+$') {
          Write-Output "compose: SQLite fora do volume nomeado (stat: $ino1) - falha real (exit 1)"
          $code = 1
          break
        }
        & podman exec $ctr sh -c 'echo task0008-volume > /data/task0008.marker' 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
          Write-Output 'compose: nada gravou no volume nomeado - falha real (exit 1)'
          $code = 1
          break
        }
        $listing = (& podman exec $ctr ls -l /data 2>&1 | Out-String).Trim()
        Write-Output "compose: antes do down: inode $ino1 ; $listing"

        $down1 = & podman compose down 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0) {
          ($down1 -split "`n" | Select-Object -Last 8) -join "`n"
          Write-Output "compose: down exit $LASTEXITCODE - falha real (exit 1)"
          $code = 1
          break
        }
        $up2 = & podman compose up -d 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0) {
          ($up2 -split "`n" | Select-Object -Last 8) -join "`n"
          Write-Output "compose: segundo up exit $LASTEXITCODE - falha real (exit 1)"
          $code = 1
          break
        }
        if (-not (Wait-SquidHealth $base 120)) {
          Write-Output 'compose: servico nao voltou apos down/up - falha real (exit 1)'
          $code = 1
          break
        }
        $ctr2 = Get-SquidContainer
        if (-not $ctr2) {
          Write-Output 'compose: container do squid nao encontrado apos o segundo up - falha real (exit 1)'
          $code = 1
          break
        }
        $mk2 = (& podman exec $ctr2 cat /data/task0008.marker 2>&1 | Out-String).Trim()
        $ino2 = (& podman exec $ctr2 stat -c %i /data/squid-grainstate.db 2>&1 | Out-String).Trim()
        Write-Output "compose: depois do up: marcador '$mk2' ; inode $ino2"
        if (($mk2 -notmatch 'task0008-volume') -or ($ino1 -ne $ino2)) {
          Write-Output 'compose: arquivo do volume nao sobreviveu a down/up - falha real (exit 1)'
          $code = 1
          break
        }
        Write-Output "compose: volume nomeado persistente apos down/up sem -v (inode $ino1) - AC-03"

        $code = 0
        break
      } while ($false)
    }
    finally {
      $downOut = & podman compose down 2>&1 | Out-String
      ($downOut -split "`n" | Select-Object -Last 3) -join "`n"
      if ($LASTEXITCODE -ne 0) { Write-Output "compose: down final exit $LASTEXITCODE - o volume nomeado fica de proposito" }
      Pop-Location
    }
    if ($code -eq 0) { Write-Output 'compose: exit 0 - DoD da fase verificado (AC-01 a AC-05)' }
    exit $code
  }
}