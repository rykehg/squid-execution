# QA-0055 scope scan (TASK-0055 AC-07): nothing changed outside the task's touches, no new
# NuGet package, and every product file this QA mutated is byte-identical to HEAD again.
# Reads the git truth only - it never writes to the product tree.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-scope-task-0055.ps1
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
$ledgerPath = Join-Path $PSScriptRoot 'QA-0055-scope.log'
$lines = New-Object System.Collections.Generic.List[string]
$fails = 0

function Add-Line([string]$Line) { $script:lines.Add($Line); Write-Output $Line }

$touches = @(
    'backend/src/Squid.Grains/',
    'backend/src/Squid.Worker/',
    'backend/src/Squid.Api/Admin/',
    'backend/src/Squid.Service/',
    'backend/tests/Squid.Tests/Backup/'
)

# The product files the mutation driver touched: after restore they must be byte-identical to
# HEAD and be the ONLY .cs files newer than the HEAD commit (S-23 bumps their mtime on purpose).
$mutated = @(
    'backend/src/Squid.Worker/Backup/BackupRunner.cs',
    'backend/src/Squid.Worker/Backup/BackupOptions.cs',
    'backend/src/Squid.Worker/Backup/BackupScheduler.cs',
    'backend/src/Squid.Api/Admin/AdminEndpoints.cs',
    'backend/src/Squid.Api/Admin/BackupAlerter.cs',
    'backend/src/Squid.Grains/Backup/BackupCoordinatorGrain.cs',
    'backend/src/Squid.Worker/Backup/BackupPipeline.cs',
    'backend/src/Squid.Worker/Backup/LocalDiskTarget.cs'
)

$head = (git -C $repo rev-parse --short HEAD | Out-String).Trim()
$commits = [int]((git -C $repo rev-list --count 'develop..HEAD' | Out-String).Trim())
$commitEpoch = [long]((git -C $repo log -1 --format=%ct | Out-String).Trim())
$commitTime = [DateTimeOffset]::FromUnixTimeSeconds($commitEpoch).UtcDateTime
Add-Line ('HEAD={0} commits-over-develop={1} commit-time-utc={2:yyyy-MM-dd HH:mm:ss}Z (esperado 1 commit bdf1ccf)' -f $head, $commits, $commitTime)
if ($commits -ne 1) { $fails++ }

# 1. every committed file inside the touches set
$files = @(git -C $repo diff --name-only 'develop...HEAD' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$outside = @($files | Where-Object {
        $f = $_
        -not ($touches | Where-Object { $f.StartsWith($_) })
    })
Add-Line ('commit-files={0} outside-touches={1} {2}' -f $files.Count, $outside.Count, ($outside -join ','))
if ($outside.Count -ne 0) { $fails++ }

# 2. zero new NuGet package: no csproj/props in the commit and no added PackageReference line
$propsChanged = @(git -C $repo diff --name-only 'develop...HEAD' -- 'backend/*.csproj' 'backend/*.props' 'backend/**/*.csproj' 'backend/**/*.props' | Where-Object { $_ })
$diff = (git -C $repo diff 'develop...HEAD' | Out-String)
$pkgAdded = @([regex]::Matches($diff, '(?m)^\+.*<(PackageReference|PackageVersion)\b')).Count
$propsInCommit = @($files | Where-Object { $_ -like '*.csproj' -or $_ -like '*.props' }).Count
Add-Line ('pkg-props-changed={0} pkg-files-in-commit={1} pkg-ref-added-lines={2} (esperado 0/0/0)' -f $propsChanged.Count, $propsInCommit, $pkgAdded)
if ($propsChanged.Count -ne 0 -or $propsInCommit -ne 0 -or $pkgAdded -ne 0) { $fails++ }

# 3. working tree: no mutation residue, no untracked file under backend
$status = @(git -C $repo status --porcelain -uno -- backend | Where-Object { $_ })
$untracked = @(git -C $repo status --porcelain -uall -- backend | Where-Object { $_ -match '^\?\?' })
Add-Line ('backend-tracked-status={0} backend-untracked={1} (esperado 0/0)' -f $status.Count, $untracked.Count)
if ($status.Count -ne 0 -or $untracked.Count -ne 0) { $fails++ }

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

# 5. mtime: the restored files are the ONLY .cs newer than the HEAD commit (S-23 bump)
$newer = @(Get-ChildItem -Path (Join-Path $repo 'backend') -Recurse -Filter '*.cs' -File |
        Where-Object { $_.LastWriteTimeUtc -gt $commitTime -and $_.FullName -notmatch '\\(obj|bin)\\' } |
        ForEach-Object { $_.FullName.Substring($repo.Length + 1) -replace '\\', '/' } | Sort-Object)
$expected = @($mutated | Sort-Object)
$delta = @($newer | Where-Object { $expected -notcontains $_ }) + @($expected | Where-Object { $newer -notcontains $_ })
Add-Line ('mtime-newer-than-commit={0} delta-vs-mutated={1} {2}' -f $newer.Count, $delta.Count, ($delta -join ','))
if ($delta.Count -ne 0) { $fails++ }

# 6. the ODEV WIP in docs/ (pre-existing) plus the files this QA added: informational only
$docs = @(git -C $repo status --porcelain -- docs | ForEach-Object { $_.Trim() })
Add-Line ('docs-status={0} entries: {1}' -f $docs.Count, ($docs -join ' | '))

Add-Line ('SUMMARY head={0} commit-files={1} outside-touches={2} pkg-added={3} residue-backend={4} untracked-backend={5} blobs={6}/{7} mtime-delta={8} fails={9}' -f `
        $head, $files.Count, $outside.Count, $pkgAdded, $status.Count, $untracked.Count, $blobOk, $mutated.Count, $delta.Count, $fails)

[System.IO.File]::WriteAllLines($ledgerPath, $lines, (New-Object System.Text.UTF8Encoding($false)))
if ($fails -gt 0) { exit 1 }
exit 0
