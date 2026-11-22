# QA-0060 scope scan (TASK-0060 AC-06): nothing changed outside the task's touches, no new
# NuGet package (AC-04's package half), and every product file this QA mutated is byte-identical
# to HEAD again. Reads the git truth only - it never writes to the product tree. The committed
# side is the diff develop..HEAD; the working-tree side is the diff develop..worktree, so an
# uncommitted edit outside the touches (the driver's S1) or a PackageVersion line (the driver's
# S2) makes this script exit 1 - that is its can-fail proof.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-scope-task-0060.ps1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    $env:PATH = 'C:\Program Files\Git\cmd;' + $env:PATH
}
$env:GIT_CONFIG_COUNT = '1'
$env:GIT_CONFIG_KEY_0 = 'core.quotepath'
$env:GIT_CONFIG_VALUE_0 = 'false'

$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$ledgerPath = Join-Path $PSScriptRoot 'QA-0060-scope.log'
$lines = New-Object System.Collections.Generic.List[string]
$fails = 0

function Add-Line([string]$Line) { $script:lines.Add($Line); Write-Output $Line }

# The touches of TASK-0060, as the task file declares them (control block, edited by the ODEV),
# plus the task file itself - the flow artifact DEV/ODEV/QA append to under the template's own
# write rule, counted among the 7 files of AC-06 by the dispatch.
$touches = @(
    'backend/src/Squid.Api/Admin/',
    'backend/src/Squid.Service/',
    'backend/tests/Squid.Tests/Backup/'
)
$taskFile = 'docs/sdd/tasks/TASK-0060-tela-redundancia.md'

# The product files the mutation driver edited (M1..M9 + the S1/S2 scope cycles): after restore
# they must be byte-identical to HEAD and be the ONLY .cs files newer than the HEAD commit
# (S-23 bumps their mtime on purpose).
$mutated = @(
    'backend/src/Squid.Api/Admin/BackupRedundancy.cs',
    'backend/src/Squid.Api/Admin/BackupPage.cs',
    'backend/src/Squid.Api/Admin/AdminEndpoints.cs',
    'backend/src/Squid.Service/SqliteBackupRedundancySource.cs',
    'backend/src/Squid.Service/SquidServiceHost.cs',
    'backend/src/Squid.Api/SquidApi.cs'
)

# The files QA-0060 added (its own can-fail scenario): newer than the commit and untracked
# until the QA commit - expected in the mtime and untracked checks, absent from the blob check
# (they have no HEAD blob yet).
$qaAdded = @(
    'backend/tests/Squid.Tests/Backup/RedundancyScoreQaTests.cs'
)

$head = (git -C $repo rev-parse --short HEAD | Out-String).Trim()
$commits = [int]((git -C $repo rev-list --count 'develop..HEAD' | Out-String).Trim())
$commitEpoch = [long]((git -C $repo log -1 --format=%ct | Out-String).Trim())
$commitTime = [DateTimeOffset]::FromUnixTimeSeconds($commitEpoch).UtcDateTime
Add-Line ('HEAD={0} commits-over-develop={1} commit-time-utc={2:yyyy-MM-dd HH:mm:ss}Z (esperado 1 commit ba9d32a)' -f $head, $commits, $commitTime)
if ($commits -ne 1) { $fails++ }

# 1. every changed file (committed OR working tree) inside the touches set + the task file
$files = @(git -C $repo diff --name-only develop | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$outside = @($files | Where-Object {
        $f = $_
        -not ($touches | Where-Object { $f.StartsWith($_) }) -and $f -ne $taskFile
    })
Add-Line ('diff-files={0} outside-touches={1} {2}' -f $files.Count, $outside.Count, ($outside -join ','))
if ($outside.Count -ne 0) { $fails++ }

# 2. zero new NuGet package: no csproj/props in the diff and no added PackageReference/PackageVersion
$propsChanged = @(git -C $repo diff --name-only develop -- 'backend/*.csproj' 'backend/*.props' 'backend/**/*.csproj' 'backend/**/*.props' | Where-Object { $_ })
$diff = (git -C $repo diff develop | Out-String)
$pkgAdded = @([regex]::Matches($diff, '(?m)^\+.*<(PackageReference|PackageVersion)\b')).Count
$propsInFiles = @($files | Where-Object { $_ -like '*.csproj' -or $_ -like '*.props' }).Count
Add-Line ('pkg-props-changed={0} pkg-files-in-diff={1} pkg-ref-added-lines={2} (esperado 0/0/0)' -f $propsChanged.Count, $propsInFiles, $pkgAdded)
if ($propsChanged.Count -ne 0 -or $propsInFiles -ne 0 -or $pkgAdded -ne 0) { $fails++ }

# 3. working tree: no mutation residue; the only untracked file under backend is QA-0060's own
# scenario (a mutation leak would show as a tracked modification or a rogue untracked file).
$status = @(git -C $repo status --porcelain -uno -- backend | Where-Object { $_ })
$untracked = @(git -C $repo status --porcelain -uall -- backend | Where-Object { $_ -match '^\?\?' })
$rogueUntracked = @($untracked | Where-Object {
        $u = $_.Trim()
        -not ($qaAdded | Where-Object { $u -like "?? $_" -or $u -like "*$_" })
    })
Add-Line ('backend-tracked-status={0} backend-untracked={1} rogue-untracked={2} (esperado 0/1/0)' -f $status.Count, $untracked.Count, $rogueUntracked.Count)
if ($status.Count -ne 0 -or $rogueUntracked.Count -ne 0) { $fails++ }

# 4. every mutated file byte-identical to its HEAD blob
$blobOk = 0
foreach ($m in $mutated) {
    $work = (git -C $repo hash-object (Join-Path $repo $m) | Out-String).Trim()
    $idx = (git -C $repo rev-parse ('HEAD:' + ($m -replace '\\', '/')) | Out-String).Trim()
    if ($work -eq $idx -and $work -notlike 'error:*') { $blobOk++ }
    else { Add-Line ('BLOB-MISMATCH file={0} work={1} head={2}' -f $m, $work, $idx) }
}
Add-Line ('blobs-identical={0}/{1} (esperado {1}/{1})' -f $blobOk, $mutated.Count)
if ($blobOk -ne $mutated.Count) { $fails++ }

# 5. mtime: the restored files PLUS QA-0060's own new scenario are the ONLY .cs files newer
# than the HEAD commit (S-23 bump on the restored ones; the new file is simply newer).
$newer = @(Get-ChildItem -Path (Join-Path $repo 'backend') -Recurse -Filter '*.cs' -File |
        Where-Object { $_.LastWriteTimeUtc -gt $commitTime -and $_.FullName -notmatch '\\(obj|bin)\\' } |
        ForEach-Object { $_.FullName.Substring($repo.Length + 1) -replace '\\', '/' } | Sort-Object)
$expected = @($mutated + $qaAdded | Sort-Object)
$delta = @($newer | Where-Object { $expected -notcontains $_ }) + @($expected | Where-Object { $newer -notcontains $_ })
Add-Line ('mtime-newer-than-commit={0} delta-vs-mutated={1} {2}' -f $newer.Count, $delta.Count, ($delta -join ','))
if ($delta.Count -ne 0) { $fails++ }

# 6. the ODEV/DEV WIP in docs/ (task file) plus the files this QA added: informational only,
# but every entry outside docs/sdd/qa + the task file is reported loudly.
# Entries arrive TRIMMED, so the status letter has no leading space: ^M, not '^ M'.
$docs = @(git -C $repo status --porcelain -- docs | ForEach-Object { $_.Trim() })
$unexpectedDocs = @($docs | Where-Object {
        $_ -notmatch '^\?\? docs/sdd/qa/' -and
        $_ -notmatch '^M docs/sdd/tasks/TASK-0060-tela-redundancia.md'
    })
Add-Line ('docs-status={0} entries: {1}' -f $docs.Count, ($docs -join ' | '))
Add-Line ('docs-unexpected={0} {1} (esperado 0: so WIP do ODEV + artefatos da QA)' -f $unexpectedDocs.Count, ($unexpectedDocs -join ' | '))
if ($unexpectedDocs.Count -ne 0) { $fails++ }

# 7. nothing at all changed outside docs/ and backend/
$all = @(git -C $repo status --porcelain | ForEach-Object { $_.Trim() })
$outsideTree = @($all | Where-Object { $_ -notmatch ' docs/' -and $_ -notmatch ' backend/' })
Add-Line ('tree-outside-docs-backend={0} {1} (esperado 0)' -f $outsideTree.Count, ($outsideTree -join ' | '))
if ($outsideTree.Count -ne 0) { $fails++ }

Add-Line ('SUMMARY head={0} commits={1} diff-files={2} outside-touches={3} pkg-added={4} residue-backend={5} untracked-backend={6} rogue={7} blobs={8}/{9} mtime-delta={10} docs-unexpected={11} outside-tree={12} fails={13}' -f `
        $head, $commits, $files.Count, $outside.Count, $pkgAdded, $status.Count, $untracked.Count, $rogueUntracked.Count, $blobOk, $mutated.Count, $delta.Count, $unexpectedDocs.Count, $outsideTree.Count, $fails)

[System.IO.File]::WriteAllLines($ledgerPath, $lines, (New-Object System.Text.UTF8Encoding($false)))
if ($fails -gt 0) { exit 1 }
exit 0
