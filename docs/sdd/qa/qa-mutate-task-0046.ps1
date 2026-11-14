# QA-0046 can-fail mutation driver (TASK-0046 - PeerGrain queue, backoff N=8, circuit breaker,
# DLQ, per-pair quota and the PeerClient failure scenarios). Fourteen cycles over the filter
# FullyQualifiedName~Squid.Tests.Federation (the 11 DEV PeerDeliveryMotor scenarios, the 4
# DEV PeerGrain scenarios, the fan-out/inbox/migration suites of the neighbours): each cycle
# edits ONE literal of PRODUCT code (T2 edits two literals of the same Backoff formula - the
# growth and the jitter are one behaviour), runs the filter, classifies the RED from the LOG
# FILE (S-22 - never from the wrapper console code), checks that the AC's OWN scenario failed,
# restores with git checkout (byte-exact against the index, hash compared PRE vs POST) and
# bumps the mtime (S-23). A green run over the restored tree closes the ledger.
# ASCII-only on purpose: this file runs without a UTF-8 BOM under Windows PowerShell 5.1, and
# no find string below may contain a non-ASCII byte (the sources around them do, in comments).
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0046.ps1 [-VerifyOnly]
[CmdletBinding()]
param([switch]$VerifyOnly)

$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$ledgerPath = Join-Path $PSScriptRoot 'QA-0046-mutations.log'
$verifyPath = Join-Path $PSScriptRoot 'QA-0046-verify.log'
$sddTest = Join-Path $repo 'docs\sdd\_tools\sdd-test.ps1'
$filter = 'FullyQualifiedName~Squid.Tests.Federation'

# The scenario of each AC, by method name (PeerDeliveryMotorTests.cs and PeerGrainTests.cs = DEV).
$ac01   = 'Ac01_a_500_goes_back_to_pending_with_attempts_and_a_growing_backoff'
$ac02e  = 'Ac02_after_eight_attempts_the_delivery_hits_the_dlq_with_motive_and_alert'
$ac02f  = 'Ac02_a_permanent_404_dead_letters_the_delivery_on_the_first_attempt'
$ac02s  = 'Ac02_a_401_is_dead_lettered_as_signature_rejected'
$ac03   = 'Ac03_an_open_circuit_pushes_a_new_delivery_straight_to_pending'
$ac04b  = 'Ac04_a_blocked_peer_receives_nothing'
$ac04r  = 'Ac04_the_per_pair_rate_limit_decides_who_leaves'
$ac05t  = 'Ac05_the_timeout_scenario_fails_the_send_and_keeps_the_row_scheduled'
$ac05s  = 'Ac05_the_slow_scenario_loses_to_the_send_budget'
$ac06   = 'Ac06_every_send_carries_the_peer_domain_trace_attribute_without_pii'
$gBack  = 'Backoff_doubles_with_jitter_and_hits_the_six_hour_ceiling_exactly'
$gCirc  = 'An_open_circuit_defers_to_its_window_and_a_success_heals_the_peer'
$gBloc  = 'A_blocked_pair_is_never_admitted'
$gQuota = 'The_per_pair_rate_limit_admits_only_the_quota_inside_one_minute'

$cycles = @(
    @{ id = 'T1';  ac = 'AC-01'; file = 'backend\src\Squid.Service\PeerDeliveryPump.cs'; find = 'Attempts = attempts,'; replace = 'Attempts = delivery.Attempts, // QA-0046 T1'; expect = @($ac01, $ac02e); why = 'a failed send counts the attempt: attempts++ before the reschedule (3.4)' },
    @{ id = 'T2';  ac = 'AC-01'; file = 'backend\src\Squid.Grains\Peers\PeerGrain.cs'; find = 'BackoffBase.Ticks * Math.Pow(2, Math.Min(exponent, 30))'; replace = 'BackoffBase.Ticks'; find2 = 'Random.Shared.NextDouble() * 0.2'; replace2 = '0.0 * Random.Shared.NextDouble()'; expect = @($ac01, $gBack); why = 'the backoff grows exponentially WITH jitter: flat and unrandomized delays break AC-01' },
    @{ id = 'T3';  ac = 'AC-02'; file = 'backend\src\Squid.Grains\Peers\PeerGrain.cs'; find = 'public const int MaxAttempts = 8;'; replace = 'public const int MaxAttempts = 9; // QA-0046 T3'; expect = @($ac02e); why = 'N of 3.4 is exactly 8: a ninth attempt before the DLQ breaks the boundary' },
    @{ id = 'T4';  ac = 'AC-02'; file = 'backend\src\Squid.Service\PeerDeliveryPump.cs'; find = 'FederationMetrics.DeliveryDlq();'; replace = '// QA-0046 T4: DLQ alert counter removed'; expect = @($ac02e); why = 'max_attempts dead-letter must FIRE THE ALERT (AC-02 alerta)' },
    @{ id = 'T5';  ac = 'AC-02'; file = 'backend\src\Squid.Service\OutboundPipeline.cs'; find = 'FederationMetrics.DeliveryDlq();'; replace = '// QA-0046 T5: DLQ alert counter removed'; expect = @($ac02f); why = 'a permanent 4xx dead-letter must FIRE THE ALERT too' },
    @{ id = 'T6';  ac = 'AC-02'; file = 'backend\src\Squid.Service\OutboundPipeline.cs'; find = 'if (answer.StatusCode is >= 400 and < 500 and not (408 or 429))'; replace = 'if (false && answer.StatusCode is >= 400 and < 500 and not (408 or 429)) // QA-0046 T6'; expect = @($ac02f, $ac02s); why = 'a permanent 4xx goes straight to Failed/DLQ - never a blind retry' },
    @{ id = 'T7';  ac = 'AC-03'; file = 'backend\src\Squid.Grains\Peers\PeerGrain.cs'; find = 'return new PeerAdmission(false, until);'; replace = 'return new PeerAdmission(true, null); // QA-0046 T7'; expect = @($ac03, $gCirc); why = 'an open circuit DEFERS the new delivery - it must never go out now (nunca falha na hora)' },
    @{ id = 'T8';  ac = 'AC-03'; file = 'backend\src\Squid.Grains\Peers\PeerGrain.cs'; find = 'return TimeSpan.FromTicks(Math.Min(BackoffCap.Ticks, jittered));'; replace = 'return TimeSpan.FromTicks(jittered); // QA-0046 T8'; expect = @($gBack); why = 'no jitter may lift a delay above the 6h ceiling, and the 8th delay IS the cap' },
    @{ id = 'T9';  ac = 'AC-04'; file = 'backend\src\Squid.Grains\Peers\PeerGrain.cs'; find = 'if (state.Status == PeerStatus.Blocked)'; replace = 'if (state.Status == PeerStatus.Blocked && false) // QA-0046 T9'; expect = @($ac04b, $gBloc); why = 'a Blocked pair is never admitted - nothing may be sent to it' },
    @{ id = 'T10'; ac = 'AC-04'; file = 'backend\src\Squid.Grains\Peers\PeerGrain.cs'; find = 'if (window.Count >= limitPerMinute)'; replace = 'if (window.Count >= limitPerMinute && false) // QA-0046 T10'; expect = @($ac04r, $gQuota); why = 'RateLimitPerMinute decides who leaves inside the minute' },
    @{ id = 'T11'; ac = 'AC-05'; file = 'backend\src\Squid.PeerClient\PeerScenarioServer.cs'; find = 'await ReplyAsync(stream, "500 Internal Server Error");'; replace = 'await ReplyAsync(stream, "202 Accepted"); // QA-0046 T11'; expect = @($ac01, $ac02e); why = 'the 500 scenario really answers 500 - the queue must see a failure' },
    @{ id = 'T12'; ac = 'AC-05'; file = 'backend\src\Squid.PeerClient\PeerScenarioServer.cs'; find = 'await Task.Delay(Timeout.InfiniteTimeSpan, _stop.Token); // the answer that never comes'; replace = 'await ReplyAsync(stream, "202 Accepted"); // QA-0046 T12'; expect = @($ac05t); why = 'the timeout scenario really never answers - the send budget must lose' },
    @{ id = 'T13'; ac = 'AC-05'; file = 'backend\src\Squid.PeerClient\PeerScenarioServer.cs'; find = 'await Task.Delay(_slowDelay, _stop.Token);'; replace = 'await Task.Delay(_slowDelay > TimeSpan.Zero ? TimeSpan.Zero : _slowDelay, _stop.Token); // QA-0046 T13'; expect = @($ac05s); why = 'the slow scenario really waits - slowness must fail inside the budget' },
    @{ id = 'T14'; ac = 'AC-06'; file = 'backend\src\Squid.Service\OutboundPipeline.cs'; find = 'span?.SetTag("peer.domain", inbox.IdnHost);'; replace = '// QA-0046 T14: peer.domain tag removed'; expect = @($ac06); why = 'every send carries the peer.domain trace attribute (SC-015)' }
)

# ---------------------------------------------------------------- helpers

$script:ledger = New-Object System.Collections.Generic.List[string]
$script:failedCycles = 0

function Log([string]$Line) {
    $script:ledger.Add($Line)
    [System.IO.File]::WriteAllLines($script:ledgerPath, $script:ledger, (New-Object System.Text.UTF8Encoding($false)))
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

# Runs the filter through sdd-test.ps1 and returns what the LOG FILE says (S-22).
function Invoke-Filter {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $console = (& powershell -NoProfile -ExecutionPolicy Bypass -File $sddTest test -TestFilter $filter 2>&1 | Out-String)
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
        $result.total = [int]$sum.Groups[3].Value
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
        $hits2 = 1
        if ($c.find2) { $hits2 = ([regex]::Matches($text, [regex]::Escape($c.find2))).Count }
        $ok = ($hits -eq 1 -and $hits2 -eq 1 -and $c.find -ne $c.replace)
        if (-not $ok) { $bad++ }
        $lines += ('VERIFY {0} ac={1} hits={2} hits2={3} ok={4}' -f $c.id, $c.ac, $hits, $hits2, $ok.ToString().ToLower())
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
    $hits2 = 1
    if ($c.find2) { $hits2 = ([regex]::Matches($text, [regex]::Escape($c.find2))).Count }
    if ($hits -ne 1 -or $hits2 -ne 1) {
        Log ('{0} TOOLING-FAIL at={1} find-hits={2} find2-hits={3} (expected 1/1, nothing mutated)' -f $c.id, (Get-Date -Format HHmmss), $hits, $hits2)
        $script:failedCycles++
        continue
    }

    Log ('{0} PRE at={1} ac={2} hash={3} head-match={4} find-hits=1' -f $c.id, (Get-Date -Format HHmmss), $c.ac, $pre, ($pre -eq $head).ToString().ToLower())

    $mutated = $text.Replace($c.find, $c.replace)
    if ($c.find2) { $mutated = $mutated.Replace($c.find2, $c.replace2) }
    [System.IO.File]::WriteAllText($path, $mutated, (New-Object System.Text.UTF8Encoding($false)))

    $run = Invoke-Filter
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
        Log ('{0} RUN wrapper={1} logExit={2} aprovado={3} falha={4} total={5} log={6}' -f $c.id, $run.wrapper, $run.logExit, $run.aprovado, $run.falha, $run.total, $logName)

        $redNames = @($run.failed)
        Log ('{0} RED-AC {1}' -f $c.id, $(if ($redNames.Count -gt 0) { $redNames -join '; ' } else { 'NONE' }))

        $redOk = ($run.logExit -eq 1 -and $run.falha -ge 1)
        $missed = @($c.expect | Where-Object {
                $want = $_
                -not ($redNames | Where-Object { $_ -like "*$want" })
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
$green = Invoke-Filter
Log ('GREEN wrapper={0} logExit={1} aprovado={2} falha={3} log={4}' -f $green.wrapper, $green.logExit, $green.aprovado, $green.falha, (LogName $green.logPath))
$greenOk = ($green.logExit -eq 0 -and $green.falha -eq 0)
if (-not $greenOk) { $script:failedCycles++ }

# Residue: no MUTATION may survive in the tracked product tree. -uno keeps untracked files
# out of this count on purpose: this QA added no test file (the scenarios of the ACs are the
# DEV's own), so the expected untracked count in backend is ZERO.
$status = @(git -C $repo status --porcelain -uno -- backend)
$residue = @($status | Where-Object { $_ }).Count
$untracked = @(git -C $repo status --porcelain -uall -- backend | Where-Object { $_ -match '^\?\?' })
if ($residue -ne 0) { $script:failedCycles++ }

$elapsed = [int]$sw.Elapsed.TotalSeconds
Log ('UNTRACKED backend={0} (esperado 0: a QA nao acrescentou arquivos em backend)' -f $untracked.Count)
if ($untracked.Count -ne 0) { $script:failedCycles++ }
Log ('SUMMARY cycles={0} failed={1} residue-backend={2} temp-backups=0 elapsed-s={3}' -f $cycles.Count, $script:failedCycles, $residue, $elapsed)

if ($script:failedCycles -gt 0) { exit 1 }
exit 0
