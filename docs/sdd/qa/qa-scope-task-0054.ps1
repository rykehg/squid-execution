# QA-0054 scope scan (AC-07 - "nenhum arquivo alterado fora dos touches; zero pacote NuGet
# novo"). Proofs, read straight from git: (1) the tested state is HEAD 28dcab9 on branch
# task/TASK-0054-nucleo-backup-cifrado; (2) the DEV commit has only files inside the five
# glob sets of the task's touches; (3) the diff develop...HEAD adds no <PackageReference> /
# <PackageVersion> line and does not touch backend/Directory.Packages.props (zero new NuGet);
# (4) the working tree after this QA's runs adds nothing outside docs/sdd (QA artifacts +
# the ODEV's own WIP recorded first in the baseline) - backend is EMPTY in git status because
# this QA added no product or test file; (5) no mutation token survived (residue), every
# mutated product file is byte-identical to its HEAD blob (S-16) and exactly those five files
# carry an mtime newer than the QA baseline (S-23). ASCII-only on purpose: runs under Windows
# PowerShell 5.1 without a UTF-8 BOM.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-scope-task-0054.ps1
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$baseline = Join-Path $PSScriptRoot 'QA-0054-git-baseline.txt'
$outPath = Join-Path $PSScriptRoot 'QA-0054-scope.log'
$expectedHead = '28dcab9aee197bd1a76a0545c4674ed0c0916c38'
$expectedBranch = 'task/TASK-0054-nucleo-backup-cifrado'
$touches = '^(backend/src/Squid\.Persistence/schema/|backend/src/Squid\.Api/Crypto/|backend/src/Squid\.Worker/|backend/src/Squid\.Service/|backend/tests/Squid\.Tests/Backup/)'
$mutated = @(
    'backend/src/Squid.Persistence/schema/0010_backup.sql',
    'backend/src/Squid.Worker/Backup/LocalDiskTarget.cs',
    'backend/src/Squid.Service/SquidServiceHost.cs',
    'backend/src/Squid.Worker/Backup/BackupPipeline.cs',
    'backend/src/Squid.Worker/Backup/SqliteBackupEnvelopeStore.cs')

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
if ($head -ne $expectedHead) { Out ('HEAD-WANT ' + $expectedHead); $script:fails++ }
if ($branch -ne $expectedBranch) { Out ('BRANCH-WANT ' + $expectedBranch); $script:fails++ }

# ---- 2. the DEV commit: files only inside the touches -------------------------------------
$commitFiles = @(git -C $repo show --name-only --format= HEAD | Where-Object { $_ })
$outsideCommit = @($commitFiles | Where-Object { $_ -notmatch $touches })
Out ('commit-files={0} commit-fora-dos-touches={1}' -f $commitFiles.Count, $outsideCommit.Count)
foreach ($f in $outsideCommit) { Out ('COMMIT-OUTSIDE ' + $f); $script:fails++ }

# ---- 3. zero new NuGet package (AC-07 second half) ----------------------------------------
$pkgPropsChanged = @(git -C $repo diff --name-only 'develop...HEAD' -- 'backend/Directory.Packages.props' |
    Where-Object { $_ })
$addedPkg = @(git -C $repo diff 'develop...HEAD' | Where-Object {
        $_ -match '^\+[^+].*(<PackageReference|<PackageVersion)' })
Out ('nuget directory-packages-props-changed={0} added-package-lines={1}' -f $pkgPropsChanged.Count, $addedPkg.Count)
foreach ($f in $pkgPropsChanged) { Out ('NUGET-PROPS ' + $f); $script:fails++ }
foreach ($l in $addedPkg) { Out ('NUGET-ADDED ' + $l); $script:fails++ }

# ---- 4. working tree after this QA's runs --------------------------------------------------
# docs/sdd outside docs/sdd/qa = the ODEV's own WIP (task file, stamping, plan/spec/backlog),
# present in the QA baseline before anything was written here; they are NOT this QA's nor the
# DEV's product change. Anything else outside the touches is a violation.
$qaArtifacts = 0
$wipOdev = 0
$outsideWorktree = @()
foreach ($entry in @(git -C $repo -c core.quotepath=false status --porcelain -uall | Where-Object { $_ })) {
    $path = ($entry -replace '^[ MADRCU?!]{1,2}\s*', '').Trim()
    if ($path -match '^docs/sdd/qa/') { $qaArtifacts++ }
    elseif ($path -match '^docs/sdd/') { $wipOdev++; Out ('WIP-ODEV ' + $path) }
    elseif ($path -notmatch $touches) { $outsideWorktree += $path }
}
Out ('git-status qa-artifacts={0} wip-odev={1} fora-dos-touches={2}' -f $qaArtifacts, $wipOdev, $outsideWorktree.Count)
foreach ($f in $outsideWorktree) { Out ('WORKTREE-OUTSIDE ' + $f); $script:fails++ }

$backendStatus = @(git -C $repo status --porcelain -uall -- backend | Where-Object { $_ })
Out ('git-backend-entries={0}' -f $backendStatus.Count)
foreach ($e in $backendStatus) { Out ('BACKEND ' + $e); $script:fails++ }

# ---- 5. no mutation token survived the restores -------------------------------------------
$residue = 0
$residueFiles = @()
foreach ($file in @(Get-ChildItem -LiteralPath (Join-Path $repo 'backend') -Recurse -File |
        Where-Object { ($_.Extension -eq '.cs' -or $_.Extension -eq '.sql') -and $_.FullName -notmatch '\\(bin|obj)\\' })) {
    $hits = @(Select-String -LiteralPath $file.FullName -SimpleMatch -Pattern 'QA-0054 T' -ErrorAction SilentlyContinue)
    if ($hits.Count -gt 0) {
        $residue += $hits.Count
        $residueFiles.Add($file.FullName.Replace($repo + '\', ''))
    }
}
Out ('residue={0}' -f $residue)
foreach ($f in $residueFiles) { Out ('RESIDUE ' + $f); $script:fails++ }

# ---- 6. every mutated product file is byte-identical to its HEAD blob (S-16) ---------------
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

# ---- 7. exactly the five mutated files carry an mtime newer than the QA baseline (S-23) ----
$baseTime = (Get-Item -LiteralPath $baseline).LastWriteTime
$newer = @(Get-ChildItem -LiteralPath (Join-Path $repo 'backend') -Recurse -File |
    Where-Object { ($_.Extension -eq '.cs' -or $_.Extension -eq '.sql') -and $_.FullName -notmatch '\\(bin|obj)\\' -and $_.LastWriteTime -gt $baseTime })
Out ('mtime-count={0} (esperado 5)' -f $newer.Count)
foreach ($f in $newer) { Out ('MTIME ' + $f.FullName.Replace($repo + '\', '')) }
if ($newer.Count -ne 5) { $script:fails++ }

# ---- summary ------------------------------------------------------------------------------
$outsideTotal = $outsideCommit.Count + $outsideWorktree.Count
Out ('fora-dos-touches={0}' -f $outsideTotal)
if ($outsideTotal -ne 0) { $script:fails++ }
Out ('SUMMARY head={0} commit-files={1} pkg-props={2} pkg-added={3} qa-artifacts={4} wip-odev={5} backend-entries={6} residue={7} blob={8}/{9} mtime={10} fails={11}' -f `
    $head.Substring(0, 7), $commitFiles.Count, $pkgPropsChanged.Count, $addedPkg.Count, $qaArtifacts, $wipOdev, $backendStatus.Count, $residue, $blobOk, $mutated.Count, $newer.Count, $script:fails)

if ($script:fails -gt 0) { exit 1 }
exit 0
