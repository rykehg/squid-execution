# QA-0056 can-fail mutation driver (TASK-0056 - restore pipeline of contract section 4, the
# five blocking drills of section 5 and the drill ledger behind the status route). Eight cycles:
# one product literal per AC scenario of AC-01..AC-06, one for the section 6 public proof the
# seventh drill asserts, and one recorded PROBE of the section 4 hash step (never asserted - it
# reports coverage, it does not grade it). Each cycle edits ONE literal of PRODUCT code, runs the
# narrowest filter that covers the scenario, classifies the RED from the LOG FILE (S-22 - never
# from the wrapper console code), checks that the AC's own scenario failed, restores with git
# checkout (byte-exact against the index, hash compared PRE vs POST) and bumps the mtime (S-23).
# A green run over the restored tree closes the ledger.
# ASCII-only on purpose: this file runs without a UTF-8 BOM under Windows PowerShell 5.1, and
# no find string below may contain a non-ASCII byte.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0056.ps1 [-VerifyOnly]
[CmdletBinding()]
param([switch]$VerifyOnly)

$ErrorActionPreference = 'Stop'

# git is not on PATH in this session (QA-0054 hit the same trap): point at the known install.
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    $env:PATH = 'C:\Program Files\Git\cmd;' + $env:PATH
}

$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$ledgerPath = Join-Path $PSScriptRoot 'QA-0056-mutations.log'
$verifyPath = Join-Path $PSScriptRoot 'QA-0056-verify.log'
$sddTest = Join-Path $repo 'docs\sdd\_tools\sdd-test.ps1'
$fam = 'FullyQualifiedName~Squid.Tests.Backup.RestoreDrillTests'

$fAc01 = 'Ac01_Roundtrip_restores_100_percent_of_the_aggregate_hashes'
$fAc02 = 'Ac02_Interrupted_backup_resumes_from_the_intact_seqTo_and_corrupt_is_never_accepted'
$fAc03 = 'Ac03_An_older_format_is_migrated_and_an_unknown_one_fails_explicitly'
$fAc04 = 'Ac04_A_keyEpoch_without_a_key_in_the_kit_fails_and_destroys_nothing'
$fAc05 = 'Ac05_The_second_backup_carries_only_the_delta_and_the_chain_still_restores'
$fAc06 = 'Ac06_After_the_restore_projections_quota_and_readiness_all_confer'
$fStat = 'The_drill_a_restore_records_stops_the_status_route_answering_null'

$cycles = @(
    @{ id = 'M1'; ac = 'AC-01'; probe = $false; file = 'backend\src\Squid.Service\SqliteActivityStore.cs'; find = 'var restored = events.OrderBy(activity => activity.Seq).ToList();'; replace = 'var restored = events.OrderBy(activity => activity.Seq).Skip(1).ToList(); // QA-0056 M1 (restore drops one event)'; filter = $fam; expect = @($fAc01); why = 'step 7 compares payload events against what landed: a restore that loses one event must refuse, and SC-009 100% identical hashes must go red' },
    @{ id = 'M2'; ac = 'AC-02'; probe = $false; file = 'backend\src\Squid.Worker\Backup\RestorePipeline.cs'; find = 'if (accepted[^1].SeqTo != all[^1].SeqTo)'; replace = 'if (accepted[^1].SeqTo < 0) // QA-0056 M2 (corrupt-outside-chain refusal removed)'; filter = $fam; expect = @($fAc02); why = 'a Corrupt envelope beside the chain must refuse the whole restore: without the check the run walks over it and answers success' },
    @{ id = 'M3'; ac = 'AC-03'; probe = $false; file = 'backend\src\Squid.Worker\Backup\BackupFormatMigrations.cs'; find = 'if (format == 0)'; replace = 'if (format >= 0) // QA-0056 M3 (unknown format silently migrated)'; filter = $fam; expect = @($fAc03); why = 'an unknown format must fail out loud: migrating it silently makes the format 99 refusal disappear' },
    @{ id = 'M4'; ac = 'AC-04'; probe = $false; file = 'backend\src\Squid.Worker\Backup\RestorePipeline.cs'; find = 'if (envelope.KeyEpoch > currentEpoch)'; replace = 'if (envelope.KeyEpoch < 0) // QA-0056 M4 (epoch beyond the kit accepted)'; filter = $fam; expect = @($fAc04); why = 'a keyEpoch with no key in the kit must refuse before anything is written: skipping the gate restores with the wrong epoch and touches the destination' },
    @{ id = 'M5'; ac = 'AC-05'; probe = $false; file = 'backend\src\Squid.Service\SqliteBackupStateSource.cs'; find = 'var events = await activities.ReadAsync(seqFrom - 1);'; replace = 'var events = await activities.ReadAsync(seqFrom); // QA-0056 M5 (exclusive window - the original bug)'; filter = $fam; expect = @($fAc05); why = 'the delta window is inclusive of seqFrom: the exclusive read drops the boundary event and the second backup stops being the pure delta' },
    @{ id = 'M6'; ac = 'AC-06'; probe = $false; file = 'backend\src\Squid.Service\SqliteRestoreDestination.cs'; find = 'var since = await _projector.LastProjectedSeqAsync();'; replace = 'var since = long.MaxValue; // QA-0056 M6 (projection pass reads nothing)'; filter = $fam; expect = @($fAc06); why = 'step 6 re-applies the projections: a pass that reads nothing leaves the read side empty and AC-06 stops conferring' },
    @{ id = 'M7'; ac = 'AC-06'; probe = $false; file = 'backend\src\Squid.Worker\Backup\SqliteBackupStatusSource.cs'; find = 'drill is null ? null : $"{drill.Drill}: {drill.Result}"'; replace = 'null /* QA-0056 M7 (drill result never published) */'; filter = $fam; expect = @($fStat); why = 'contract section 6 publishes restore.last_drill_result from the ledger: hardcoding null makes the status route answer null again' },
    @{ id = 'M8'; ac = 'SECTION-4-STEP-4'; probe = $true; file = 'backend\src\Squid.Worker\Backup\RestorePipeline.cs'; find = 'if (!string.Equals(hash, envelope.ContentHash, StringComparison.OrdinalIgnoreCase))'; replace = 'if (!string.Equals(hash, envelope.ContentHash, StringComparison.OrdinalIgnoreCase) && false) // QA-0056 M8 probe'; filter = $fam; expect = @(); why = 'PROBE only: does any drill feed the restore a payload whose hash diverges from the header? green here means step 4 of section 4 has no can-fail scenario (coverage note, no AC demands it)' }
)

# ---------------------------------------------------------------- helpers

$script:ledger = New-Object System.Collections.Generic.List[string]
$script:failedCycles = 0

function Log([string]$Line) {
    $script:ledger.Add($Line)
    [System.IO.File]::WriteAllLines($ledgerPath, $script:ledger, (New-Object System.Text.UTF8Encoding($false)))
    Write-Output $Line
}

function Blob([string]$Rel) {
    # stdout only: stderr is left alone on purpose (2>&1 under EAP Stop turns a native
    # warning into a terminating ErrorRecord and would kill the run mid-cycle).
    $sha = git -C $repo hash-object (Join-Path $repo $Rel) | Out-String
    if ($LASTEXITCODE -ne 0) { return "error:$($sha.Trim())" }
    return $sha.Trim()
}

function Head-Blob([string]$Rel) {
    $pathspec = $Rel -replace '\\', '/'
    $sha = git -C $repo rev-parse "HEAD:$pathspec" | Out-String
    if ($LASTEXITCODE -ne 0) { return "error:$($sha.Trim())" }
    return $sha.Trim()
}

# Runs one filter through sdd-test.ps1 and returns what the LOG FILE says (S-22).
function Invoke-Filter([string]$TestFilter) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $console = (& powershell -NoProfile -ExecutionPolicy Bypass -File $sddTest test -TestFilter $TestFilter 2>&1 | Out-String)
        $wrapper = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }
    $result = @{ wrapper = $wrapper; logPath = $null; logExit = $null; falha = -1; aprovado = -1; total = -1; failed = @() }

    $m = [regex]::Match($console, '(?m)^log:\s+(.+)$')
    if (-not $m.Success) { return $result }
    $logPath = $m.Groups[1].Value.Trim()
    $result.logPath = $logPath
    if (-not (Test-Path -LiteralPath $logPath)) { return $result }

    $content = [System.IO.File]::ReadAllText($logPath)
    $exitMatch = [regex]::Match($content, '(?m)^exit:\s+(\d+)')
    if ($exitMatch.Success) { $result.logExit = [int]$exitMatch.Groups[1].Value }
    $sum = [regex]::Match($content, 'Com falha:\s+(\d+),\s+Aprovado:\s+(\d+),\s+Ignorado:\s+(\d+),\s+Total:\s+(\d+)')
    if ($sum.Success) {
        $result.falha = [int]$sum.Groups[1].Value
        $result.aprovado = [int]$sum.Groups[2].Value
        $result.total = [int]$sum.Groups[4].Value
    }
    $names = @()
    foreach ($fm in [regex]::Matches($content, 'Com falha (Squid\.Tests\.[^\s\[]+)')) {
        $names += $fm.Groups[1].Value
    }
    $result.failed = $names
    return $result
}

function LogName([string]$Path) {
    if ([string]::IsNullOrEmpty($Path)) { return 'NONE' }
    return [System.IO.Path]::GetFileName($Path)
}

# ---------------------------------------------------------------- -VerifyOnly

if ($VerifyOnly) {
    $lines = @()
    $bad = 0
    foreach ($c in $cycles) {
        $path = Join-Path $repo $c.file
        $text = [System.IO.File]::ReadAllText($path)
        $hits = ([regex]::Matches($text, [regex]::Escape($c.find))).Count
        $ok = ($hits -eq 1 -and $c.find -ne $c.replace)
        if (-not $ok) { $bad++ }
        $lines += ('VERIFY {0} ac={1} hits={2} ok={3} probe={4}' -f $c.id, $c.ac, $hits, $ok.ToString().ToLower(), $c.probe.ToString().ToLower())
    }
    $bytes = [System.IO.File]::ReadAllBytes($PSCommandPath)
    $nonAscii = @($bytes | Where-Object { $_ -gt 127 }).Count
    $asciiOk = $nonAscii -eq 0
    if (-not $asciiOk) { $bad++ }
    $lines += ('VERIFY-ASCII non-ascii-bytes={0} ok={1}' -f $nonAscii, $asciiOk.ToString().ToLower())
    $lines += ('VERIFY total={0} bad={1}' -f $cycles.Count, $bad)
    [System.IO.File]::WriteAllLines($verifyPath, $lines, (New-Object System.Text.UTF8Encoding($false)))
    foreach ($l in $lines) { Write-Output $l }
    if ($bad -gt 0) { exit 1 }
    exit 0
}

# ---------------------------------------------------------------- the run

$sw = [System.Diagnostics.Stopwatch]::StartNew()

foreach ($c in $cycles) {
    $path = Join-Path $repo $c.file
    $pre = Blob $c.file
    $head = Head-Blob $c.file

    $text = [System.IO.File]::ReadAllText($path)
    $hits = ([regex]::Matches($text, [regex]::Escape($c.find))).Count
    if ($hits -ne 1) {
        Log ('{0} TOOLING-FAIL at={1} find-hits={2} (expected 1, nothing mutated)' -f $c.id, (Get-Date -Format HHmmss), $hits)
        $script:failedCycles++
        continue
    }

    Log ('{0} PRE at={1} ac={2} hash={3} head-match={4} find-hits=1' -f $c.id, (Get-Date -Format HHmmss), $c.ac, $pre, ($pre -eq $head).ToString().ToLower())

    $mutated = $text.Replace($c.find, $c.replace)
    [System.IO.File]::WriteAllText($path, $mutated, (New-Object System.Text.UTF8Encoding($false)))

    $run = Invoke-Filter $c.filter
    $logName = LogName $run.logPath

    if ($null -eq $run.logPath) {
        Log ('{0} TOOLING-FAIL wrapper={1} no-log' -f $c.id, $run.wrapper)
        $script:failedCycles++
    }
    elseif ($null -eq $run.logExit -or $run.falha -lt 0) {
        Log ('{0} BUILD-FAIL wrapper={1} logExit={2} log={3}' -f $c.id, $run.wrapper, $run.logExit, $logName)
        $script:failedCycles++
    }
    else {
        Log ('{0} RUN wrapper={1} logExit={2} aprovado={3} falha={4} total={5} filter={6} log={7}' -f $c.id, $run.wrapper, $run.logExit, $run.aprovado, $run.falha, $run.total, $c.filter, $logName)

        $redNames = @($run.failed)
        Log ('{0} RED-AC {1}' -f $c.id, $(if ($redNames.Count -gt 0) { $redNames -join '; ' } else { 'NONE' }))

        if ($c.probe) {
            if ($run.logExit -eq 0) {
                Log ('{0} PROBE-GREEN expect=NONE (no drill fails without the section 4 hash step)' -f $c.id)
            }
            else {
                Log ('{0} PROBE-RED expect=NONE reds={1}' -f $c.id, ($redNames -join '; '))
            }
        }
        else {
            $redOk = ($run.logExit -eq 1 -and $run.falha -ge 1)
            $missed = @($c.expect | Where-Object {
                    $want = $_
                    -not ($redNames | Where-Object { $_ -like "*$want*" })
                })
            $caught = ($redOk -and $missed.Count -eq 0)
            if ($caught) {
                Log ('{0} EXPECT-OK expect={1}' -f $c.id, ($c.expect -join '; '))
            }
            else {
                Log ('{0} EXPECT-MISS expect={1} logExit={2} falha={3} missed={4}' -f $c.id, ($c.expect -join '; '), $run.logExit, $run.falha, ($missed -join '; '))
                $script:failedCycles++
            }
        }
    }

    # restore: byte-exact against the index, then the mtime bump of S-23
    git -C $repo checkout -- $c.file | Out-Null
    $restoreExit = $LASTEXITCODE
    $post = Blob $c.file
    if ($restoreExit -ne 0 -or $post -ne $pre) {
        Log ('{0} RESTORE-FAIL exit={1} pre={2} post={3}' -f $c.id, $restoreExit, $pre, $post)
        $script:failedCycles++
    }
    else {
        (Get-Item -LiteralPath $path).LastWriteTime = Get-Date
        Log ('{0} RESTORE-OK hash=PRE residue=0' -f $c.id)
    }
}

# Green over the restored tree: the ledger may only close on a green filter.
$green = Invoke-Filter $fam
Log ('GREEN wrapper={0} logExit={1} aprovado={2} falha={3} log={4}' -f $green.wrapper, $green.logExit, $green.aprovado, $green.falha, (LogName $green.logPath))
$greenOk = ($green.logExit -eq 0 -and $green.falha -eq 0 -and $green.aprovado -eq 7)
if (-not $greenOk) { $script:failedCycles++ }

# Residue: no MUTATION may survive in the tracked product tree. This QA added no product or
# test file (the scenarios are the DEV's own), so the expected untracked count in backend is ZERO.
$status = @(git -C $repo status --porcelain -uno -- backend)
$residue = @($status | Where-Object { $_ }).Count
$untracked = @(git -C $repo status --porcelain -uall -- backend | Where-Object { $_ -match '^\?\?' })
if ($residue -ne 0) { $script:failedCycles++ }

$elapsed = [int]$sw.Elapsed.TotalSeconds
Log ('UNTRACKED backend={0} (esperado 0: a QA nao acrescentou arquivos em backend)' -f $untracked.Count)
if ($untracked.Count -ne 0) { $script:failedCycles++ }
Log ('SUMMARY cycles={0} failed={1} residue-backend={2} green-aprovado={3} elapsed-s={4}' -f $cycles.Count, $script:failedCycles, $residue, $green.aprovado, $elapsed)

if ($script:failedCycles -gt 0) { exit 1 }
exit 0
