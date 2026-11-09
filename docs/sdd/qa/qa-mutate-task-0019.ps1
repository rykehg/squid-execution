#Requires -Version 5.1
<#
qa-mutate-task-0019 — can-fail proof driver for QA-0018 (TASK-0019).

One cycle, in this exact order (backlog rules S-16 and S-23):
  1. SHA256 of the target file BEFORE anything  (S-16) and a backup outside the repo;
  2. mutate exactly ONE pattern — the driver refuses to run if the pattern is absent
     or appears more than once, so a mutation can never land in the wrong place;
  3. red run: sdd-test.ps1 test -TestFilter <filter> (the runner of the house);
  4. byte-exact restore from the backup + mtime bump, then hash must equal the PRE hash
     (S-23: without the bump the incremental build runs the mutated binary over the
     restored source);
  5. green run of the same filter.

Classification always comes from the `exit:` header and the summary INSIDE the log file
the wrapper wrote (backlog S-22) — never from the wrapper's console exit code.

Exit codes: 0 = proven (red log product-red, green log exit 0, hash restored);
1 = not proven (read the RESULT lines); 8 = restore hash mismatch (tree damaged!);
9 = pattern count != 1 (nothing mutated).

Usage:
  powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0019.ps1 `
    -File backend/src/Squid.Api/Containers/ContainerEndpoints.cs `
    -Old "<literal or regex>" -New "<replacement>" -Filter "FullyQualifiedName~..." [-Regex]
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$File,
  [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Old,
  # Omit -New to REMOVE the pattern (an explicit empty string does not survive the
  # powershell.exe argument boundary, so deletion is the default of this switch-less form).
  [string]$New = '',
  [Parameter(Mandatory = $true)][string]$Filter,
  [switch]$Regex
)

$ErrorActionPreference = 'Stop'
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$testTool = Join-Path $repo 'docs\sdd\_tools\sdd-test.ps1'
$target = if ([System.IO.Path]::IsPathRooted($File)) { $File } else { Join-Path $repo $File }
if (-not (Test-Path -LiteralPath $target)) { Write-Output "DRIVER: target not found: $target"; exit 9 }

function Get-LogFacts([string[]]$Lines, [string]$Label) {
  $logLine = $Lines | Where-Object { "$_" -match '^\s*log:\s+' } | Select-Object -First 1
  if (-not $logLine) { return @{ Log = '(no log path printed)'; Exit = 'NA'; Summary = '(none)' } }
  $logPath = ("$logLine" -replace '^\s*log:\s+', '').Trim()
  $content = [System.IO.File]::ReadAllLines($logPath, [System.Text.Encoding]::UTF8)
  $exitLine = $content | Where-Object { $_ -match '^exit:\s+' } | Select-Object -First 1
  $exit = if ($exitLine) { ($exitLine -replace '^exit:\s+', '').Trim() } else { 'NA' }
  $summary = $content | Where-Object { $_ -match 'Com falha:' } | Select-Object -Last 1
  return @{ Log = $logPath; Exit = $exit; Summary = if ($summary) { $summary.Trim() } else { '(no summary)' } }
}

function Invoke-Gate([string]$Label) {
  $lines = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $testTool test -TestFilter $Filter 2>&1 |
    ForEach-Object { "$_" }
  $lines | ForEach-Object { Write-Output "[$Label] $_" }
  return Get-LogFacts -Lines $lines -Label $Label
}

# --- 1. PRE hash + backup outside the repo -------------------------------------------------
$pre = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
$backup = Join-Path $env:TEMP ('qa0018-{0}-{1}.bak' -f ([System.IO.Path]::GetFileNameWithoutExtension($target)), ([guid]::NewGuid().ToString('N')))
Copy-Item -LiteralPath $target -Destination $backup -Force
Write-Output "MUT-FILE: $File"
Write-Output "PRE-SHA256: $pre"

# --- 2. mutate exactly one place -----------------------------------------------------------
$text = [System.IO.File]::ReadAllText($target)
if ($Regex) {
  $count = [regex]::Matches($text, $Old).Count
  if ($count -ne 1) { Remove-Item -LiteralPath $backup -Force; Write-Output "PATTERN-COUNT: $count (need 1) - nothing mutated"; exit 9 }
  $mutated = [regex]::Replace($text, $Old, $New)
}
else {
  $count = ([regex]::Matches($text, [regex]::Escape($old))).Count
  if ($count -ne 1) { Remove-Item -LiteralPath $backup -Force; Write-Output "PATTERN-COUNT: $count (need 1) - nothing mutated"; exit 9 }
  $mutated = $text.Replace($Old, $New)
}
[System.IO.File]::WriteAllText($target, $mutated, $utf8NoBom)
Write-Output "PATTERN-COUNT: 1 - mutated"
Write-Output "OLD: $Old"
Write-Output "NEW: $(if ($New.Length -eq 0) { '(removed)' } else { $New })"

try {
  # --- 3. red run --------------------------------------------------------------------------
  $red = Invoke-Gate -Label 'RED'
  Write-Output "RED-LOG: $($red.Log)"
  Write-Output "RED-EXIT(log): $($red.Exit)"
  Write-Output "RED-SUMMARY(log): $($red.Summary)"

  # --- 4. byte-exact restore + mtime bump (S-23) -------------------------------------------
  Copy-Item -LiteralPath $backup -Destination $target -Force
  (Get-Item -LiteralPath $target).LastWriteTime = Get-Date
  $post = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
  $match = $pre -eq $post
  Write-Output "POST-SHA256: $post"
  Write-Output "HASH-MATCH: $match"
  if (-not $match) { Write-Output "RESULT: FAIL-RESTORE (tree damaged)"; exit 8 }

  # --- 5. green run ------------------------------------------------------------------------
  $green = Invoke-Gate -Label 'GREEN'
  Write-Output "GREEN-LOG: $($green.Log)"
  Write-Output "GREEN-EXIT(log): $($green.Exit)"
  Write-Output "GREEN-SUMMARY(log): $($green.Summary)"

  $redProduct = $red.Exit -ne '0' -and $red.Exit -ne '3' -and $red.Summary -match 'Com falha:\s+[1-9]'
  $greenOk = $green.Exit -eq '0' -and $green.Summary -match 'Com falha:\s+0,'
  if ($redProduct -and $greenOk) {
    Write-Output 'RESULT: PROVEN (red is a product red inside the log, green after restore, hash matched)'
    Remove-Item -LiteralPath $backup -Force
    exit 0
  }

  Write-Output "RESULT: FAIL (redProduct=$redProduct greenOk=$greenOk)"
  Remove-Item -LiteralPath $backup -Force
  exit 1
}
catch {
  # never leave a mutation behind, whatever failed mid-cycle
  Copy-Item -LiteralPath $backup -Destination $target -Force
  (Get-Item -LiteralPath $target).LastWriteTime = Get-Date
  $rescue = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
  Remove-Item -LiteralPath $backup -Force
  Write-Output "EXCEPTION: $($_.Exception.Message)"
  Write-Output "RESCUED-SHA256: $rescue HASH-MATCH: $($pre -eq $rescue)"
  exit 7
}
