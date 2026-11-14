# QA-0044 can-fail mutation driver (TASK-0044, inbox pipeline of contract section 3).
# Twenty cycles over the filter Squid.Tests.Federation (the 14 DEV inbox scenarios, the DEV
# rate-limit scenario, the two QA-0044 gap scenarios, the 8 TASK-0043 surface scenarios and
# the TASK-0043 Accept scenario): each cycle edits ONE literal of PRODUCT code, runs the
# filter, classifies the RED from the LOG FILE (S-22 - never from the wrapper console code),
# checks that the AC's OWN scenario failed, restores with git checkout (byte-exact against
# the index, hash compared PRE vs POST) and bumps the mtime (S-23). A green run over the
# restored tree closes the ledger.
# ASCII-only on purpose: this file runs without a UTF-8 BOM under Windows PowerShell 5.1.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0044.ps1 [-VerifyOnly]
[CmdletBinding()]
param([switch]$VerifyOnly)

$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$ledgerPath = Join-Path $PSScriptRoot 'QA-0044-mutations.log'
$verifyPath = Join-Path $PSScriptRoot 'QA-0044-verify.log'
$sddTest = Join-Path $repo 'docs\sdd\_tools\sdd-test.ps1'
$filter = 'FullyQualifiedName~Squid.Tests.Federation'

# The scenario of each AC, by method name (InboxReceiveTests.cs, InboxRateLimitTests.cs,
# QaTask0044InboxGapTests.cs).
$ac01 = 'A_remote_create_with_valid_headers_becomes_a_post_in_the_named_container'
$ac02 = 'The_same_activity_id_delivered_twice_is_accepted_without_reprocessing'
$ac03 = 'The_contract_order_refuses_each_step_with_the_state_intact'
$ac04 = 'Over_the_per_domain_quota_the_inbox_answers_429_with_retry_after'
$ac04d = 'The_default_quota_is_sixty_per_domain_without_configuration'
$ac05f = 'A_follow_creates_the_relation_answers_accept_and_undo_removes_it'
$ac05d = 'A_remote_delete_tombstones_the_post_and_it_leaves_the_feed'
$ac05l = 'The_same_actor_liking_twice_keeps_the_count_at_one'
$ac05r = 'A_flag_files_one_denunciation_and_a_repeat_files_no_second'
$ac06m = 'A_members_only_create_applies_for_a_member_and_not_for_a_stranger'
$ac06p = 'A_private_create_is_never_applied'
$ac06u = 'An_unknown_type_is_accepted_and_counted_by_the_metric'
$ac07a = 'Remote_attachments_are_verified_by_sha256_and_a_failed_one_stays_absent'
$ac07s = 'The_production_media_client_never_touches_a_blocked_address'
$d01 = 'A_create_without_any_named_destination_is_accepted_without_applying'

$cycles = @(
    @{ id = 'T1';  ac = 'AC-01'; file = 'backend\src\Squid.Service\InboxPipeline.cs'; find = 'if (obj is not null) references.AddRange(obj.Audience);'; replace = 'if (false) references.AddRange(obj.Audience);'; expect = @($ac01); why = 'a Create must reach its named destination through the object audience (D-01)' },
    @{ id = 'T2';  ac = 'AC-02'; file = 'backend\src\Squid.Service\InboxPipeline.cs'; find = 'if (!await _inbox.RememberAsync(Recipient(target), activity.Id!))'; replace = 'if (await _inbox.RememberAsync(Recipient(target), activity.Id!))'; expect = @($ac02); why = 'step 6 refuses ONLY a repeat: a first delivery must be admitted (FR-035); run1 tried the store side and it is masked by the insert-only application (QA-0044-mutations-run1.log, T2 EXPECT-MISS)' },
    @{ id = 'T3';  ac = 'AC-03'; file = 'backend\src\Squid.Api\Federation\FederationEndpoints.cs'; find = 'StatusCodes.Status401Unauthorized, "digest_invalid",'; replace = 'StatusCodes.Status400BadRequest, "digest_invalid",'; expect = @($ac03); why = 'a divergent Digest must answer 401 (contract step 2)' },
    @{ id = 'T4';  ac = 'AC-03'; file = 'backend\src\Squid.Service\InboxPipeline.cs'; find = 'private const int MaxBodyBytes = 1_048_576;'; replace = 'private const int MaxBodyBytes = 6_291_456;'; expect = @($ac03); why = 'the body ceiling is 1 MB: a 5 MB body must answer 413 (contract step 4)' },
    @{ id = 'T5';  ac = 'AC-03'; file = 'backend\src\Squid.Api\Federation\FederationEndpoints.cs'; find = 'StatusCodes.Status400BadRequest, "payload_too_deep",'; replace = 'StatusCodes.Status406NotAcceptable, "payload_too_deep",'; expect = @($ac03); why = 'a nesting past 32 levels must answer 400 (contract step 4)' },
    @{ id = 'T6';  ac = 'AC-03'; file = 'backend\src\Squid.Api\Federation\FederationEndpoints.cs'; find = '_ => AuthErrors.Write(context, StatusCodes.Status401Unauthorized,'; replace = '_ => AuthErrors.Write(context, StatusCodes.Status400BadRequest,'; expect = @($ac03); why = 'missing or invalid signature must answer 401 (contract step 1)' },
    @{ id = 'T7';  ac = 'AC-04'; file = 'backend\src\Squid.Service\FederationRateLimiter.cs'; find = 'if (window.Count >= _limit)'; replace = 'if (window.Count >= int.MaxValue)'; expect = @($ac04); why = 'the per-domain quota must refuse the over-quota delivery with 429' },
    @{ id = 'T8';  ac = 'AC-04'; file = 'backend\src\Squid.Service\FederationRateLimiter.cs'; find = 'public const int DefaultLimit = 60;'; replace = 'public const int DefaultLimit = 600;'; expect = @($ac04d); why = 'the default quota of Federation:RateLimitPerMinute is 60' },
    @{ id = 'T9';  ac = 'AC-05'; file = 'backend\src\Squid.Service\InboxPipeline.cs'; find = 'await _follows.AddFollowerAsync(handle, activity.Actor!, _time.GetUtcNow());'; replace = 'await Task.CompletedTask; // QA-0044 T9: follower never written'; expect = @($ac05f); why = 'a Follow must create the follower relation' },
    @{ id = 'T10'; ac = 'AC-05'; file = 'backend\src\Squid.Service\InboxPipeline.cs'; find = 'return Accepted(accept);'; replace = 'return Accepted();'; expect = @($ac05f); why = 'a Follow must answer with the Accept in the 202 body' },
    @{ id = 'T11'; ac = 'AC-05'; file = 'backend\src\Squid.Service\InboxPipeline.cs'; find = 'await _follows.RemoveFollowerAsync(followed, undone.Actor);'; replace = '// QA-0044 T11: Undo of a Follow never removes'; expect = @($ac05f); why = 'an Undo(Follow) must remove the relation' },
    @{ id = 'T12'; ac = 'AC-05'; file = 'backend\src\Squid.Service\InboxPipeline.cs'; find = 'await _remote.DeleteAsync(activity.Actor!, removed, cancellationToken);'; replace = 'await Task.CompletedTask; // QA-0044 T12: tombstone never written'; expect = @($ac05d); why = 'a Delete must tombstone the object out of the feed' },
    @{ id = 'T13'; ac = 'AC-05'; file = 'backend\src\Squid.Service\SqliteLikeStore.cs'; find = 'if (rows.Contains(actorUri, StringComparer.Ordinal))'; replace = 'if (false)'; expect = @($ac05l); why = 'Like is idempotent per (actor, object) - the count may not grow (7.14)' },
    @{ id = 'T14'; ac = 'AC-05'; file = 'backend\src\Squid.Service\InboxPipeline.cs'; find = 'case "flag":'; replace = 'case "flag_disabled":'; expect = @($ac05r); why = 'a Flag must file the denunciation in IReportStore' },
    @{ id = 'T15'; ac = 'AC-06'; file = 'backend\src\Squid.Service\InboxPipeline.cs'; find = 'if (visibility == VisibilityPolicy.MembersOnly && !await MemberRecipientAsync(target, destination))'; replace = 'if (visibility == VisibilityPolicy.MembersOnly && await MemberRecipientAsync(target, destination))'; expect = @($ac06m); why = 'membersOnly applies only when the local recipient is an active member' },
    @{ id = 'T16'; ac = 'AC-06'; file = 'backend\src\Squid.Service\InboxPipeline.cs'; find = 'if (visibility == VisibilityPolicy.Private) return Accepted();'; replace = '// QA-0044 T16: private gate removed'; expect = @($ac06p); why = 'private is never applied in clear (R-11)' },
    @{ id = 'T17'; ac = 'AC-06'; file = 'backend\src\Squid.Service\InboxPipeline.cs'; find = 'FederationMetrics.UnknownActivity();'; replace = '// QA-0044 T17: metric not counted'; expect = @($ac06u); why = 'an unknown type must increment federation.unknown_activity' },
    @{ id = 'T18'; ac = 'AC-07'; file = 'backend\src\Squid.Api\Posts\RemoteActivities.cs'; find = '&& !hash.Equals(spec.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))'; replace = '&& !hash.Equals("qa0044-never-matches", StringComparison.OrdinalIgnoreCase))'; expect = @($ac07a); why = 'the declared SHA-256 must gate which attachment is kept' },
    @{ id = 'T19'; ac = 'AC-07'; file = 'backend\src\Squid.Api\Posts\RemoteMediaClient.cs'; find = 'if (IPAddress.TryParse(host, out var literal)) return !SsrfGuard.Blocked(literal);'; replace = 'if (IPAddress.TryParse(host, out var literal)) return true;'; expect = @($ac07s); why = 'the SSRF guard must refuse a blocked address before any socket' },
    @{ id = 'T20'; ac = 'D-01'; file = 'backend\src\Squid.Service\InboxPipeline.cs'; find = 'var destination = routeContainer ?? await DestinationAsync(target, activity, origin);'; replace = 'var destination = routeContainer ?? await DestinationAsync(target, activity, origin) ?? (await _containers.SearchAsync("Comunidade", 1, 0)).FirstOrDefault();'; expect = @($d01); why = 'only a NAMED destination applies - no membership fallback (gate 2026-11-13)' }
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
        $ok = ($hits -eq 1 -and $c.find -ne $c.replace)
        if (-not $ok) { $bad++ }
        $lines += ('VERIFY {0} ac={1} hits={2} ok={3}' -f $c.id, $c.ac, $hits, $ok.ToString().ToLower())
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
# out of this count on purpose: the QA's own new test file is a deliverable inside the
# task's touches (classified by the scope scan and by the checkers), not a survivor.
$status = @(git -C $repo status --porcelain -uno -- backend)
$residue = @($status | Where-Object { $_ }).Count
$untracked = @(git -C $repo status --porcelain -uall -- backend | Where-Object { $_ -match '^\?\?' })
if ($residue -ne 0) { $script:failedCycles++ }

$elapsed = [int]$sw.Elapsed.TotalSeconds
Log ('UNTRACKED backend={0} (esperado 1: QaTask0044InboxGapTests.cs da QA, dentro dos touches)' -f $untracked.Count)
Log ('SUMMARY cycles={0} failed={1} residue-backend={2} temp-backups=0 elapsed-s={3}' -f $cycles.Count, $script:failedCycles, $residue, $elapsed)

if ($script:failedCycles -gt 0) { exit 1 }
exit 0
