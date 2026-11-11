#Requires -Version 5.1
<#
qa-mutate-task-0028.ps1 — can-fail driver of QA-0028 (TASK-0028, cota de midia da instancia).

Nine cycles covering every AC of the task plus the three hard spots of this QA:
  M1  AC-01  the reservation condition is neutralized (every upload passes the gate)
  M2  AC-01  the refusal itself persists blob+record before answering 507 (digest must catch it)
  M3  AC-02  the blob sweep stops writing the ledger
  M4  AC-04  the hosted service's RunOnceAsync stops running the sweep
  M5  AC-03  the 10 GiB default of the configuration layer becomes 1 MiB
  M6  AC-06  the PackageReference of SkiaSharp.NativeAssets.Linux leaves Squid.Service.csproj
  M7  AC-06  the PackageVersion line of Directory.Packages.props stops matching the declaration
  M8  QA     the gate reserves the ORIGINAL request bytes instead of the processed ones
  M9  QA     the optimistic increment moves UpdatedAt (the reconcile stamp of data-model 2.13)

One cycle, exactly as the flow demands:
  S-16 : SHA256 PRE of the product file, recorded before anything is written
  apply: ONE unique pattern (count must be exactly 1 or the cycle is REFUSED, nothing written),
         inside the task's touches only, carrying the QA-MUTATION marker in the text
  red  : sdd-test.ps1 test -TestFilter <the AC under test>; classified by the `exit:` line and
         the summary INSIDE the log file (S-22), never by the wrapper's console exit code alone
  S-23 : byte-exact restore from the backup, mtime bump, SHA256 must equal PRE again
  green: same filtered run, exit 0 read inside its log

Nothing outside the task's touches (Squid.Api/Media, Squid.ReadModels/Quota, Squid.Worker/Quota,
Squid.Service, Directory.Packages.props, Squid.Service.csproj, tests/Squid.Tests/Quota) is ever
written, and no mutation outlives its own cycle (restore runs in a finally block).
Every string in this file is ASCII on purpose: powershell.exe reads a .ps1 without BOM as ANSI and
an accented pattern would be mangled (lesson of QA-0026 run 1).
Usage: powershell -NoProfile -ExecutionPolicy Bypass -File qa-mutate-task-0028.ps1 [-Only M3]
#>
[CmdletBinding()]
param([string]$Only = '')

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$sddTest = Join-Path $repo 'docs\sdd\_tools\sdd-test.ps1'
$utf8 = New-Object System.Text.UTF8Encoding($false)

$mutations = @(
  # AC-01 — a condicao do UPDATE vira sempre verdadeira: a recusa deixa de existir
  @{ Label = 'M1-AC01-reserve-always-passes'; File = 'backend\src\Squid.ReadModels\Quota\SqliteQuotaLedger.cs'
     Old = 'WHERE id = 1 AND used_bytes + @Bytes <= @LimitBytes'
     New = 'WHERE id = 1 AND used_bytes + @Bytes <= @LimitBytes OR 1 = 1 -- QA-MUTATION'
     Filter = 'FullyQualifiedName~QuotaUploadGateTests' },
  # AC-01 hard spot — a PROPIA recusa grava blob + registro antes de responder 507:
  # o status continua certo, so o digest do volume e a contagem de registros pegam
  @{ Label = 'M2-AC01-refusal-persists-then-507'; File = 'backend\src\Squid.Api\Media\MediaEndpoints.cs'
     Old = '            return AuthErrors.Write(context, StatusCodes.Status507InsufficientStorage,'
     New = '            await store.SaveAsync(processed.Bytes, new MediaRecord(contentHash, mimeType, alt, processed.Width, processed.Height, processed.DurationSeconds, processed.BlurHash, processed.Bytes.LongLength, current.Handle, time.GetUtcNow())); return AuthErrors.Write(context, StatusCodes.Status507InsufficientStorage, // QA-MUTATION'
     Filter = 'FullyQualifiedName~QaTask0028Tests.Ac01a' },
  # AC-02 — a varredura para de escrever no ledger (o drift fica para sempre)
  @{ Label = 'M3-AC02-sweep-write-off'; File = 'backend\src\Squid.Worker\Quota\QuotaReconciler.cs'
     Old = '        await _ledger.SetUsedAsync(total, _time.GetUtcNow());'
     New = '        _ = _time; // QA-MUTATION: a varredura nao escreve mais'
     Filter = 'FullyQualifiedName~QuotaReconcileTests.Ac02' },
  # AC-04 — RunOnceAsync do servico hospedado deixa de rodar a varredura
  @{ Label = 'M4-AC04-runonce-noop'; File = 'backend\src\Squid.Worker\Quota\QuotaReconcileService.cs'
     Old = @(
       '    public Task<long> RunOnceAsync(CancellationToken cancellationToken = default) =>'
       '        _reconciler.ReconcileAsync(cancellationToken);')
     New = @(
       '    public Task<long> RunOnceAsync(CancellationToken cancellationToken = default)'
       '    {'
       '        _ = _reconciler; // QA-MUTATION'
       '        return Task.FromResult(-1L);'
       '    }')
     Filter = 'FullyQualifiedName~QuotaReconcileTests.Ac04' },
  # AC-03 — o default de 10 GiB da camada de config vira 1 MiB
  @{ Label = 'M5-AC03-default-quota-1mib'; File = 'backend\src\Squid.Api\Media\MediaOptions.cs'
     Old = '    public const long DefaultQuotaBytes = 10L * 1024 * 1024 * 1024;'
     New = '    public const long DefaultQuotaBytes = 10L * 1024 * 1024; // QA-MUTATION'
     Filter = 'FullyQualifiedName~QuotaLedgerTests' },
  # AC-06 — o PackageReference dos natives linux sai do csproj
  @{ Label = 'M6-AC06-csproj-native-ref-off'; File = 'backend\src\Squid.Service\Squid.Service.csproj'
     Old = '    <PackageReference Include="SkiaSharp.NativeAssets.Linux" />'
     New = '    <!-- QA-MUTATION: native linux removido -->'
     Filter = 'FullyQualifiedName~LinuxNativeAssetsTests' },
  # AC-06 — a linha de versao do props deixa de casar com a declaracao (o build continua igual)
  @{ Label = 'M7-AC06-props-line-mismatch'; File = 'backend\Directory.Packages.props'
     Old = '    <PackageVersion Include="SkiaSharp.NativeAssets.Linux" Version="4.155.0" />'
     New = '    <PackageVersion Include="SkiaSharp.NativeAssets.Linux" Version="4.155.0"/> <!-- QA-MUTATION -->'
     Filter = 'FullyQualifiedName~LinuxNativeAssetsTests' },
  # QA hard spot — a gate passa a contar os bytes DA REQUISICAO: o jpeg de 9000 com EXIF vira 507
  @{ Label = 'M8-QA01c-gate-counts-original'; File = 'backend\src\Squid.Api\Media\MediaEndpoints.cs'
     Old = '                processed.Bytes.LongLength, options.QuotaLimitBytes, time.GetUtcNow()))'
     New = '                original.LongLength, options.QuotaLimitBytes, time.GetUtcNow())) // QA-MUTATION'
     Filter = 'FullyQualifiedName~QaTask0028Tests.Ac01c' },
  # QA hard spot — o incremento otimista passa a carimbar updated_at (coluna da reconciliacao)
  @{ Label = 'M9-QA02b-increment-stamps-updated-at'; File = 'backend\src\Squid.ReadModels\Quota\SqliteQuotaLedger.cs'
     Old = 'SET used_bytes = used_bytes + @Bytes, limit_bytes = @LimitBytes'
     New = "SET used_bytes = used_bytes + @Bytes, limit_bytes = @LimitBytes, updated_at = strftime('%Y-%m-%dT%H:%M:%fZ','now') -- QA-MUTATION"
     Filter = 'FullyQualifiedName~QaTask0028Tests.Ac02b' }
)

# Arrays of lines become one LF pattern: the product files of this repo are LF (verified).
function Get-Pattern([object]$Value) {
  if ($Value -is [array]) { return ($Value -join "`n") }
  return [string]$Value
}

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
  $summary = [regex]::Match($text, '(?m)^(Aprovado!.*|Falhou!.*|Failed!.*|Passed!.*)$').Groups[1].Value
  $fails = @([regex]::Matches($text, '(?m)^.*\b([\w.]+)\s+\[FAIL\]') |
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
  $old = Get-Pattern $m.Old
  $new = Get-Pattern $m.New
  $count = ([regex]::Matches($text, [regex]::Escape($old))).Count
  if ($count -ne 1) {
    Write-Output "CYCLE $($m.Label): REFUSED pattern-count=$count (nothing written) - fix the driver pattern"
    $failed++
    continue
  }

  $backup = Join-Path $env:TEMP ('qa0028-' + [guid]::NewGuid().ToString('N') + '.bak')
  [System.IO.File]::Copy($path, $backup, $true)
  try {
    [System.IO.File]::WriteAllText($path, $text.Replace($old, $new), $utf8)
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

$leftover = @(Get-ChildItem -Path $env:TEMP -Filter 'qa0028-*.bak' -ErrorAction SilentlyContinue)
$residue = 0
Get-ChildItem -Path (Join-Path $repo 'backend') -Recurse -Include *.cs, *.props, *.csproj -File |
  Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
  ForEach-Object { if ((Get-Content -Raw -LiteralPath $_.FullName) -match 'QA-MUTATION') { $residue++ } }

Write-Output "SUMMARY cycles=$ran failed=$failed residue-QA-MUTATION=$residue temp-backups=$($leftover.Count)"
if ($failed -gt 0 -or $residue -gt 0 -or $leftover.Count -gt 0) { exit 1 }
exit 0
