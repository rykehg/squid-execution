# QA-0050 baseline probe (TASK-0050 - Federation:TrustedPeerHosts). Records the tree identity
# BEFORE any QA write and probes which platforms can execute the suite on this machine: the
# Windows record (this shell), WSL (dotnet/git) and Git Bash (dotnet/git). The mtime anchor of
# the scope scans is this file itself (S-23), so run it first. ASCII-only on purpose.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-baseline-task-0050.ps1
$ErrorActionPreference = 'Continue'

$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$outPath = Join-Path $PSScriptRoot 'QA-0050-git-baseline.txt'
$lines = New-Object System.Collections.Generic.List[string]

function Out([string]$line) {
    $script:lines.Add($line)
    [System.IO.File]::WriteAllLines($outPath, $script:lines, (New-Object System.Text.UTF8Encoding($false)))
    Write-Output $line
}

$fails = 0

Out ('when=' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
$head = (git -C $repo rev-parse HEAD | Out-String).Trim()
$branch = (git -C $repo rev-parse --abbrev-ref HEAD | Out-String).Trim()
$parent = (git -C $repo rev-parse 'HEAD^' | Out-String).Trim()
Out ('HEAD=' + $head)
Out ('branch=' + $branch)
Out ('parent=' + $parent)
if ($branch -ne 'task/TASK-0050-pares-confiaveis-ssrf') { Out ('FAIL branch inesperado'); $fails++ }

Out ('status:')
foreach ($e in @(git -C $repo status --porcelain -uall)) {
    if ($e) { Out ('  ' + $e) }
}

Out ('commit-files:')
$commitFiles = @()
foreach ($f in @(git -C $repo show --name-only --format= HEAD)) {
    if ($f) { $commitFiles += $f; Out ('  ' + $f) }
}
Out ('commit-files-count=' + $commitFiles.Count)

$remote = @((git -C $repo branch -r --contains HEAD) | Where-Object { $_ })
Out ('branch-r-contains=' + $(if ($remote.Count -eq 0) { 'EMPTY (nothing pushed)' } else { $remote -join ' ' }))
if ($remote.Count -ne 0) { Out ('FAIL HEAD already on a remote'); $fails++ }

Out ('develop-tip=' + (git -C $repo rev-parse --short develop))
Out ('main-tip=' + (git -C $repo rev-parse --short main))
Out ('head-parent-short=' + (git -C $repo rev-parse --short 'HEAD^'))

# ---- platform probes: who can actually EXECUTE the suite here ----------------------------
$wsl = Get-Command wsl -ErrorAction SilentlyContinue
if ($wsl) {
    $wslDotnet = (& wsl -e bash -c 'command -v dotnet' 2>&1 | Out-String).Trim()
    $wslGit = (& wsl -e bash -c 'command -v git' 2>&1 | Out-String).Trim()
    Out ('wsl-dotnet=' + $(if ($wslDotnet) { $wslDotnet } else { 'ABSENT (suite cannot run in WSL; installing needs a human gate)' }))
    Out ('wsl-git=' + $(if ($wslGit) { $wslGit } else { 'ABSENT' }))
}
else {
    Out 'wsl=ABSENT'
}

$gitBash = 'C:\Program Files\Git\bin\bash.exe'
if (Test-Path -LiteralPath $gitBash) {
    $probe = (& $gitBash -lc 'command -v dotnet; command -v git' 2>&1 | Out-String).Trim()
    Out ('gitbash-probe=' + ($probe -replace "`r?`n", ' ; '))
}
else {
    Out 'gitbash=ABSENT'
}

$dotnet = (& dotnet --version 2>&1 | Out-String).Trim()
Out ('windows-dotnet=' + $dotnet)

Out ('fails=' + $fails)
if ($fails -gt 0) { exit 1 }
exit 0
