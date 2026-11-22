# QA-0055 can-fail mutation driver (TASK-0055 - BackupCoordinatorGrain, scheduler
# Backup:IntervalMinutes and the two admin backup routes). Eleven cycles: one per AC scenario
# of AC-01..AC-06 plus the three S-47/S-48 fixes this task carried. Each cycle edits ONE
# literal of PRODUCT code, runs the narrowest filter that covers the scenario, classifies the
# RED from the LOG FILE (S-22 - never from the wrapper console code), checks that the AC's own
# scenario failed, restores with git checkout (byte-exact against the index, hash compared PRE
# vs POST) and bumps the mtime (S-23). A green run over the restored tree closes the ledger.
# ASCII-only on purpose: this file runs without a UTF-8 BOM under Windows PowerShell 5.1, and
# no find string below may contain a non-ASCII byte.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0055.ps1 [-VerifyOnly]
[CmdletBinding()]
param([switch]$VerifyOnly)

$ErrorActionPreference = 'Stop'

# git is not on PATH in this session (QA-0054 hit the same trap): point at the known install.
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    $env:PATH = 'C:\Program Files\Git\cmd;' + $env:PATH
}

$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$ledgerPath = Join-Path $PSScriptRoot 'QA-0055-mutations.log'
$verifyPath = Join-Path $PSScriptRoot 'QA-0055-verify.log'
$sddTest = Join-Path $repo 'docs\sdd\_tools\sdd-test.ps1'
$fam = 'FullyQualifiedName~Squid.Tests.Backup'

$fAc01  = 'Ac01_The_coordinator_runs_the_section_3_flow_and_registers_the_envelope'
$fAc02d = 'Ac02_The_interval_defaults_to_1440_minutes_and_honours_the_configuration'
$fAc02s = 'Ac02_The_scheduler_runs_a_backup_on_its_own_after_the_interval'
$fAc034 = 'Ac03_ac04_ac05_The_routes_are_operator_only_and_the_status_answers_section_6'
$fAc05v = 'Ac05_An_unverified_envelope_counts_and_alerts_immediately'
$fAc05a = 'Ac05_A_last_success_older_than_24_h_alerts_and_a_fresh_one_does_not'
$fAc05n = 'Ac05_Any_verify_failure_alerts_on_its_own'
$fAc05w = 'Ac05_The_alert_is_emitted_as_a_warning'
$fAc06t = 'Ac06_A_destination_failure_is_reported_and_registers_nothing'
$fAc06s = 'Ac06_A_state_source_failure_is_reported_instead_of_throwing'
$fS47   = 'S47_The_destination_header_carries_the_state_the_reread_decided'
$fS48   = 'S48_A_header_it_cannot_read_is_skipped_instead_of_breaking_discovery'

$cycles = @(
    @{ id = 'M1'; ac = 'AC-01'; file = 'backend\src\Squid.Worker\Backup\BackupRunner.cs'; find = 'return sent.Max(envelope => envelope.SeqTo) + 1;'; replace = 'return 0; // QA-0055 M1 (ListAsync discovery broken)'; filter = 'FullyQualifiedName~Squid.Tests.Backup.BackupCoordinatorTests'; expect = @($fAc01); why = 'seqFrom must come from what the destination holds (contract section 1): always 0 offers the second run a full snapshot and the interval assertion fails' },
    @{ id = 'M2a'; ac = 'AC-02'; file = 'backend\src\Squid.Worker\Backup\BackupOptions.cs'; find = 'public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(1440);'; replace = 'public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(1439); // QA-0055 M2a'; filter = 'FullyQualifiedName~Ac02_The_interval_defaults'; expect = @($fAc02d); why = 'the default the specification fixes is 1440 minutes: 1439 breaks the default assertion' },
    @{ id = 'M2b'; ac = 'AC-02'; file = 'backend\src\Squid.Worker\Backup\BackupScheduler.cs'; find = 'await RunOnceAsync(cancellationToken);'; replace = 'await Task.Yield(); // QA-0055 M2b (loop never runs a backup)'; filter = 'FullyQualifiedName~Ac02_The_scheduler_runs_a_backup_on_its_own_after_the_interval'; expect = @($fAc02s); why = 'the scheduler must fire the coordinator by itself after the interval: a loop that only yields never produces an envelope and the host test times out red' },
    @{ id = 'M3'; ac = 'AC-03'; file = 'backend\src\Squid.Api\Admin\AdminEndpoints.cs'; find = 'if (!IsOperator(configuration, current.Handle))'; replace = 'if (IsOperator(configuration, current.Handle)) // QA-0055 M3 (gate inverted)'; filter = 'FullyQualifiedName~Ac03_ac04_ac05'; expect = @($fAc034); why = 'the gate must refuse handles OUTSIDE Instance:Operators: inverting it lets the stranger through and refuses the operator' },
    @{ id = 'M4'; ac = 'AC-04'; file = 'backend\src\Squid.Api\Admin\AdminEndpoints.cs'; find = 'envelopes_count = state.EnvelopesCount,'; replace = 'envelopes_count = state.EnvelopesCount + 1, // QA-0055 M4'; filter = 'FullyQualifiedName~Ac03_ac04_ac05'; expect = @($fAc034); why = 'backup.envelopes_count must be the registry count: an inflated number fails the section 6 field assertion' },
    @{ id = 'M5'; ac = 'AC-05'; file = 'backend\src\Squid.Api\Admin\BackupAlerter.cs'; find = 'if (status.LastSuccessAt is null || now - status.LastSuccessAt > StaleAfter)'; replace = 'if (status.LastSuccessAt is null || now - status.LastSuccessAt > TimeSpan.FromHours(48)) // QA-0055 M5'; filter = $fam; expect = @($fAc05a, $fAc05w, $fAc034); why = 'section 6 alerts when last_success_at is older than 24 HOURS: a 48 h window silences the 25 h staleness in the unit decision and on the route' },
    @{ id = 'M6'; ac = 'AC-05'; file = 'backend\src\Squid.Api\Admin\BackupAlerter.cs'; find = 'if (status.VerifyFailures > 0)'; replace = 'if (status.VerifyFailures > 1) // QA-0055 M6'; filter = $fam; expect = @($fAc05n, $fAc05v); why = 'ANY verify failure must alert immediately: requiring two silences the first Corrupt envelope in the unit decision and on the route' },
    @{ id = 'M7'; ac = 'AC-06'; file = 'backend\src\Squid.Grains\Backup\BackupCoordinatorGrain.cs'; find = 'return new BackupRunResult(false, null, failure.Message);'; replace = 'throw failure; // QA-0055 M7 (failure escapes instead of being reported)'; filter = 'FullyQualifiedName~Squid.Tests.Backup.BackupCoordinatorTests'; expect = @($fAc06t, $fAc06s); why = 'a target or aggregate failure must be REPORTED as a result, never escape: a rethrow takes the caller down with it' },
    @{ id = 'M8'; ac = 'S-47'; file = 'backend\src\Squid.Worker\Backup\BackupPipeline.cs'; find = 'await writer.WriteEnvelopeStateAsync(envelope, ct);'; replace = 'await Task.CompletedTask; // QA-0055 M8 (destination header rewrite removed)'; filter = 'FullyQualifiedName~Squid.Tests.Backup.BackupHeaderTests'; expect = @($fS47); why = 'S-47: the destination header must be rewritten with the state the re-read decided: without the seam the file keeps Pending/verifiedAt null' },
    @{ id = 'M9'; ac = 'S-48'; file = 'backend\src\Squid.Worker\Backup\LocalDiskTarget.cs'; find = 'when (failure is JsonException'; replace = 'when (failure is not JsonException && failure is JsonException'; filter = 'FullyQualifiedName~Squid.Tests.Backup.BackupHeaderTests'; expect = @($fS48); why = 'S-48 tolerant reader: ListAsync must SKIP a header it cannot parse: refusing JsonException lets the torn file break discovery' },
    @{ id = 'M10'; ac = 'S-48'; file = 'backend\src\Squid.Worker\Backup\LocalDiskTarget.cs'; find = 'File.Move(temporary, PathOf(envelope.Id, ".json"), overwrite: true);'; replace = '// QA-0055 M10 (move removed: the header never lands at the final path)'; filter = 'FullyQualifiedName~Squid.Tests.Backup.BackupHeaderTests'; expect = @($fS47, $fS48); why = 'S-48 tmp+move: the header only exists once the temp file is moved into place: without the move no .json is ever visible' }
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
        $lines += ('VERIFY {0} ac={1} hits={2} ok={3} filter={4}' -f $c.id, $c.ac, $hits, $ok.ToString().ToLower(), $c.filter)
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
$greenOk = ($green.logExit -eq 0 -and $green.falha -eq 0 -and $green.aprovado -eq 23)
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
