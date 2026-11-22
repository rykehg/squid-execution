# QA-0059 scope scan (TASK-0059 AC-05): nothing committed outside the task's touches, no new
# NuGet package, and every product file this QA mutated is byte-identical to HEAD again.
# Reads the git truth only - it never writes to the product tree.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-scope-task-0059.ps1
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
$ledgerPath = Join-Path $PSScriptRoot 'QA-0059-scope.log'
$lines = New-Object System.Collections.Generic.List[string]
$fails = 0

function Add-Line([string]$Line) { $script:lines.Add($Line); Write-Output $Line }

# The touches of TASK-0059, as the task file declares them (control block, edited by the ODEV).
$touches = @(
    'backend/src/Squid.Api/Recovery/',
    'backend/src/Squid.Api/Crypto/',
    'backend/src/Squid.Service/',
    'backend/tests/Squid.Tests/Recovery/'
)

# The product files the mutation driver edited (M1/M3/M5 endpoint, M2 store): after restore they
# must be byte-identical to HEAD and be the ONLY .cs files newer than the HEAD commit (S-23
# bumps their mtime on purpose).
$mutated = @(
    'backend/src/Squid.Api/Recovery/RecoveryEndpoints.cs',
    'backend/src/Squid.Service/SqliteInstanceSigningKeyStore.cs'
)

# The files QA-0059 added (its own can-fail scenarios): newer than the commit and untracked
# until the QA commit - expected in the mtime and untracked checks, absent from the blob check
# (they have no HEAD blob yet).
$qaAdded = @(
    'backend/tests/Squid.Tests/Recovery/ImportInstanceCopyQaTests.cs'
)

$head = (git -C $repo rev-parse --short HEAD | Out-String).Trim()
$commits = [int]((git -C $repo rev-list --count 'develop..HEAD' | Out-String).Trim())
$commitEpoch = [long]((git -C $repo log -1 --format=%ct | Out-String).Trim())
$commitTime = [DateTimeOffset]::FromUnixTimeSeconds($commitEpoch).UtcDateTime
Add-Line ('HEAD={0} commits-over-develop={1} commit-time-utc={2:yyyy-MM-dd HH:mm:ss}Z (esperado 1 commit 77d1a44)' -f $head, $commits, $commitTime)
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

# 3. working tree: no mutation residue; the only untracked file under backend is QA-0059's own
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

# 5. mtime: the restored files PLUS QA-0059's own new scenario are the ONLY .cs files newer
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
        $_ -notmatch '^M docs/sdd/tasks/TASK-0059-import-kit-copia-instancia.md'
    })
Add-Line ('docs-status={0} entries: {1}' -f $docs.Count, ($docs -join ' | '))
Add-Line ('docs-unexpected={0} {1} (esperado 0: so WIP do ODEV + artefatos da QA)' -f $unexpectedDocs.Count, ($unexpectedDocs -join ' | '))
if ($unexpectedDocs.Count -ne 0) { $fails++ }

# 7. nothing at all changed outside docs/ and backend/
$all = @(git -C $repo status --porcelain | ForEach-Object { $_.Trim() })
$outsideTree = @($all | Where-Object { $_ -notmatch ' docs/' -and $_ -notmatch ' backend/' })
Add-Line ('tree-outside-docs-backend={0} {1} (esperado 0)' -f $outsideTree.Count, ($outsideTree -join ' | '))
if ($outsideTree.Count -ne 0) { $fails++ }

Add-Line ('SUMMARY head={0} commits={1} commit-files={2} outside-touches={3} pkg-added={4} residue-backend={5} untracked-backend={6} rogue={7} blobs={8}/{9} mtime-delta={10} docs-unexpected={11} outside-tree={12} fails={13}' -f `
        $head, $commits, $files.Count, $outside.Count, $pkgAdded, $status.Count, $untracked.Count, $rogueUntracked.Count, $blobOk, $mutated.Count, $delta.Count, $unexpectedDocs.Count, $outsideTree.Count, $fails)

[System.IO.File]::WriteAllLines($ledgerPath, $lines, (New-Object System.Text.UTF8Encoding($false)))
if ($fails -gt 0) { exit 1 }
exit 0
