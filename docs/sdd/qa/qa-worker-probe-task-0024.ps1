# qa-worker-probe-task-0024 - boots the BUILT Squid.Worker process against a prepared SQLite file
# and proves the composition clause of AC-05 (TASK-0024): Squid.Worker/Program.cs registers the
# projection pump as a hosted service, the host really starts it, and the pump drains the activity
# log into the projection tables in Seq order. qa-worker-fixture-task-0024.py prepares the file and
# grades the rows the RUNNING process wrote - artifact against artifact.
# Exit 0 only when the negative control (nothing projected before the process) fails as expected
# AND the verify passes after it. Bounded wait (90 s), throwaway local process, temp dir removed
# on success and kept (with its path printed) on failure.
param([switch]$Keep)
$ErrorActionPreference = 'Continue'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
Set-Location $repo

$fixture = Join-Path $PSScriptRoot 'qa-worker-fixture-task-0024.py'
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ('qa0023-worker-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
$db = Join-Path $tmp 'worker.db'
$exe = Join-Path $repo 'backend\src\Squid.Worker\bin\Debug\net10.0\Squid.Worker.exe'
$stdout = Join-Path $tmp 'worker.stdout.log'
$stderr = Join-Path $tmp 'worker.stderr.log'
$proc = $null
$code = 1
try {
  if (-not (Test-Path -LiteralPath $exe)) {
    Write-Output "PROBE FAIL the built worker is missing: $exe (run the gate first)"
    exit 1
  }

  $prep = (& python $fixture prepare $db 2>&1) | Out-String
  if ($LASTEXITCODE -ne 0) {
    Write-Output "PROBE FAIL fixture prepare exit $LASTEXITCODE"
    Write-Output $prep.Trim()
    exit 1
  }
  Write-Output ($prep.Trim() -replace "`r?`n", ' ; ')

  # Negative control: before the process runs the projection must be empty (verify exits 1).
  (& python $fixture verify $db 2>&1) | Out-Null
  if ($LASTEXITCODE -ne 1) {
    Write-Output "PROBE FAIL negative control: verify exited $LASTEXITCODE before the process (expected 1)"
    exit 1
  }
  Write-Output 'PROBE negative-control ok (verify=1 before the process starts)'

  $proc = Start-Process -FilePath $exe `
    -ArgumentList @("--Storage:Path=$db", '--Logging:LogLevel:Default=Warning') `
    -WorkingDirectory (Split-Path $exe) `
    -RedirectStandardOutput $stdout -RedirectStandardError $stderr `
    -PassThru -WindowStyle Hidden
  $started = Get-Date
  Write-Output "PROBE started pid=$($proc.Id)"

  $deadline = (Get-Date).AddSeconds(90)
  while ((Get-Date) -lt $deadline) {
    (& python $fixture verify $db 2>&1) | Out-Null
    if ($LASTEXITCODE -eq 0) { break }
    if ($proc.HasExited) { break }
    Start-Sleep -Seconds 3
  }
  $waited = [int]((Get-Date) - $started).TotalSeconds

  $verify = (& python $fixture verify $db 2>&1) | Out-String
  $code = $LASTEXITCODE
  Write-Output ($verify.Trim() -replace "`r?`n", ' ; ')
  Write-Output "PROBE waited=${waited}s processAlive=$(-not $proc.HasExited)"

  # The process's own log is what classifies a failure (S-22 spirit): tail it, never guess.
  foreach ($log in @($stdout, $stderr)) {
    if ((Test-Path -LiteralPath $log) -and ((Get-Item -LiteralPath $log).Length -gt 0)) {
      $tail = (Get-Content -LiteralPath $log -Tail 3) -join ' | '
      Write-Output "WORKER-LOG $(Split-Path -Leaf $log): $tail"
    }
  }

  if ($code -eq 0 -and -not $Keep) {
    Write-Output "PROBE PASS (exit 0) - temp removed: $tmp"
  }
  else {
    Write-Output "PROBE FAIL (exit $code) - artifacts kept: $tmp"
  }
  exit $code
}
finally {
  if ($null -ne $proc) {
    try {
      if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    }
    catch { }
  }
  if ($code -eq 0 -and -not $Keep) {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
  }
}
