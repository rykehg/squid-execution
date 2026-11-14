# QA-0046 scope scan (AC-07 second half - "nenhum arquivo alterado fora dos touches").
# Three proofs, read straight from git: (1) the DEV commit 61c12ce has only files inside the
# five glob sets of the task's touches; (2) the working tree after this QA's runs adds nothing
# outside docs/sdd/qa (QA artifacts + logs) and the ODEV's own task-file WIP - backend is
# expected to be EMPTY in git status because this QA added no product or test file; (3) no
# mutation token survived (residue), every mutated product file is byte-identical to its HEAD
# blob (S-16) and exactly those four files carry an mtime newer than the QA baseline (S-23).
# ASCII-only on purpose: runs under Windows PowerShell 5.1 without a UTF-8 BOM.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-scope-task-0046.ps1
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$baseline = Join-Path $PSScriptRoot 'QA-0046-git-baseline.txt'
$outPath = Join-Path $PSScriptRoot 'QA-0046-scope.log'
$taskFile = 'docs/sdd/tasks/TASK-0046-peer-grain-fila-e-dlq.md'
$touches = '^(backend/src/Squid\.Grains/Peers/|backend/src/Squid\.PeerClient/|backend/src/Squid\.Service/|backend/src/Squid\.ReadModels/|backend/tests/Squid\.Tests/)'
$mutated = @(
    'backend/src/Squid.Grains/Peers/PeerGrain.cs',
    'backend/src/Squid.Service/PeerDeliveryPump.cs',
    'backend/src/Squid.Service/OutboundPipeline.cs',
    'backend/src/Squid.PeerClient/PeerScenarioServer.cs')

$lines = New-Object System.Collections.Generic.List[string]
$script:fails = 0

function Out([string]$line) {
    $script:lines.Add($line)
    [System.IO.File]::WriteAllLines($outPath, $script:lines, (New-Object System.Text.UTF8Encoding($false)))
    Write-Output $line
}

# ---- 1. identity of the tree --------------------------------------------------------------
$head = (git -C $repo rev-parse HEAD | Out-String).Trim()
$branch = (git -C $repo rev-parse --abbrev-ref HEAD | Out-String).Trim()
Out ('HEAD=' + $head)
Out ('branch=' + $branch)

# ---- 2. the DEV commit: files only inside the touches -------------------------------------
$commitFiles = @(git -C $repo show --name-only --format= HEAD | Where-Object { $_ })
$outsideCommit = @($commitFiles | Where-Object { $_ -notmatch $touches })
Out ('commit-files={0} commit-fora-dos-touches={1}' -f $commitFiles.Count, $outsideCommit.Count)
foreach ($f in $outsideCommit) { Out ('COMMIT-OUTSIDE ' + $f); $script:fails++ }

# ---- 3. working tree after this QA's runs --------------------------------------------------
$qaArtifacts = 0
$wipOdev = 0
$outsideWorktree = @()
foreach ($entry in @(git -C $repo status --porcelain -uall | Where-Object { $_ })) {
    $path = ($entry -replace '^[ MADRCU?!]{1,2}\s*', '').Trim()
    if ($path -match '^docs/sdd/qa/') { $qaArtifacts++ }
    elseif ($path -eq $taskFile) { $wipOdev++ }
    elseif ($path -notmatch $touches) { $outsideWorktree += $path }
}
Out ('git-status qa-artifacts={0} wip-odev={1} fora-dos-touches={2}' -f $qaArtifacts, $wipOdev, $outsideWorktree.Count)
foreach ($f in $outsideWorktree) { Out ('WORKTREE-OUTSIDE ' + $f); $script:fails++ }

$backendStatus = @(git -C $repo status --porcelain -uall -- backend | Where-Object { $_ })
Out ('git-backend-entries={0}' -f $backendStatus.Count)
foreach ($e in $backendStatus) { Out ('BACKEND ' + $e); $script:fails++ }

# ---- 4. no mutation token survived the restores -------------------------------------------
$residue = 0
$residueFiles = @()
foreach ($file in @(Get-ChildItem -LiteralPath (Join-Path $repo 'backend') -Recurse -File -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })) {
    $hits = @(Select-String -LiteralPath $file.FullName -SimpleMatch -Pattern 'QA-0046 T' -ErrorAction SilentlyContinue)
    if ($hits.Count -gt 0) {
        $residue += $hits.Count
        $residueFiles.Add($file.FullName.Replace($repo + '\', ''))
    }
}
Out ('residue={0}' -f $residue)
foreach ($f in $residueFiles) { Out ('RESIDUE ' + $f); $script:fails++ }

# ---- 5. every mutated product file is byte-identical to its HEAD blob (S-16) ---------------
$blobOk = 0
foreach ($f in $mutated) {
    $pathspec = $f -replace '\\', '/'
    $work = (git -C $repo hash-object (Join-Path $repo $f) | Out-String).Trim()
    $headBlob = (git -C $repo rev-parse ('HEAD:' + $pathspec) | Out-String).Trim()
    $match = ($work -eq $headBlob)
    if ($match) { $blobOk++ }
    Out ('blob {0} match={1}' -f $f, $match.ToString().ToUpper())
}
Out ('blob-identity={0} of {1}' -f $blobOk, $mutated.Count)
if ($blobOk -ne $mutated.Count) { $script:fails++ }

# ---- 6. exactly the four mutated files carry an mtime newer than the QA baseline (S-23) ----
$baseTime = (Get-Item -LiteralPath $baseline).LastWriteTime
$newer = @(Get-ChildItem -LiteralPath (Join-Path $repo 'backend') -Recurse -File -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' -and $_.LastWriteTime -gt $baseTime })
Out ('mtime-count={0} (esperado 4)' -f $newer.Count)
foreach ($f in $newer) { Out ('MTIME ' + $f.FullName.Replace($repo + '\', '')) }
if ($newer.Count -ne 4) { $script:fails++ }

# ---- summary ------------------------------------------------------------------------------
$outsideTotal = $outsideCommit.Count + $outsideWorktree.Count
Out ('fora-dos-touches={0}' -f $outsideTotal)
if ($outsideTotal -ne 0) { $script:fails++ }
Out ('SUMMARY commit-files={0} qa-artifacts={1} backend-entries={2} residue={3} blob={4}/{5} mtime={6} fails={7}' -f `
    $commitFiles.Count, $qaArtifacts, $backendStatus.Count, $residue, $blobOk, $mutated.Count, $newer.Count, $script:fails)

if ($script:fails -gt 0) { exit 1 }
exit 0
