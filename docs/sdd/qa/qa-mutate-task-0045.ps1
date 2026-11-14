# QA-0045 can-fail mutation driver (TASK-0045 - outbound fan-out, R-05 re-evaluation, follow
# route). Ten cycles over the filter Squid.Tests.Federation (the 8 DEV OutboundFanOut scenarios,
# the 0008 migration scenario, the QA-0045 gap scenario and the inherited inbox/surface suites):
# each cycle edits ONE literal of PRODUCT code (T5 edits two literals of the same private guard
# and switch, because the guard alone never lets a private post reach the switch), runs the
# filter, classifies the RED from the LOG FILE (S-22 - never from the wrapper console code),
# checks that the AC's OWN scenario failed, restores with git checkout (byte-exact against the
# index, hash compared PRE vs POST) and bumps the mtime (S-23). A green run over the restored
# tree closes the ledger.
# ASCII-only on purpose: this file runs without a UTF-8 BOM under Windows PowerShell 5.1, and
# no find string below may contain a non-ASCII byte (the sources around them do, in comments).
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0045.ps1 [-VerifyOnly]
[CmdletBinding()]
param([switch]$VerifyOnly)

$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$ledgerPath = Join-Path $PSScriptRoot 'QA-0045-mutations.log'
$verifyPath = Join-Path $PSScriptRoot 'QA-0045-verify.log'
$sddTest = Join-Path $repo 'docs\sdd\_tools\sdd-test.ps1'
$filter = 'FullyQualifiedName~Squid.Tests.Federation'

# The scenario of each AC, by method name (OutboundFanOutTests.cs = DEV, QaTask0045FanOutGapTests.cs = QA).
$ac01  = 'Ac01_public_post_enqueues_one_delivery_per_remote_follower_and_names_the_destination'
$ac01c = 'Ac01_public_post_also_enqueues_for_the_container_collection'
$ac02u = 'Ac02_unlisted_delivers_only_to_followers'
$ac02m = 'Ac02_members_only_delivers_only_to_active_members'
$ac02p = 'Ac02_private_creates_no_delivery_at_all'
$ac03  = 'Ac03_follow_route_enqueues_a_signed_follow_and_the_relation_settles_on_accept'
$ac04  = 'Ac04_policy_change_before_send_cancels_with_a_reason_and_sends_nothing'
$ac05  = 'Ac05_publish_returns_with_every_delivery_already_pending_and_the_wire_untouched'
$ac06  = 'Ac06_removing_the_post_propagates_a_signed_delete_to_the_recipients_that_received_the_create'

# The three-line enqueue block of EnqueueCreateAsync (file is LF-only, verified by probe).
$enqueueCreate = @(
    '            await _deliveries.AddAsync(new PeerDelivery(',
    '                activityId, recipient, PeerActivity.Create, post.Id, payload,',
    '                container.Policy.Version, 0, now, PeerDeliveryState.Pending, null));'
) -join "`n"
$enqueueCreateBornSent = @(
    '            await _deliveries.AddAsync(new PeerDelivery(',
    '                activityId, recipient, PeerActivity.Create, post.Id, payload,',
    '                container.Policy.Version, 0, now, PeerDeliveryState.Sent, null)); // QA-0045 T9 born Sent'
) -join "`n"

$cycles = @(
    @{ id = 'T1';  ac = 'AC-01'; file = 'backend\src\Squid.Protocols\ActivityPub\ObjectJsonLd.cs'; find = 'container.Id,'; replace = 'origin + "/c/qa0045-broken",'; expect = @($ac01, $ac01c); why = 'the outbound Create must NAME the destination: audience = the container URI (GAP-48/D-01)' },
    @{ id = 'T2';  ac = 'AC-01'; file = 'backend\src\Squid.Service\OutboundPipeline.cs'; find = 'VisibilityPolicy.Public => [.. followers, .. members.Where(uri => Remote(uri, origin))],'; replace = 'VisibilityPolicy.Public => followers, // QA-0045 T2: container collection dropped'; expect = @($ac01c); why = 'a public post must also enqueue for the container collection (AC-01 parenthetical)' },
    @{ id = 'T3';  ac = 'AC-02'; file = 'backend\src\Squid.Service\OutboundPipeline.cs'; find = 'VisibilityPolicy.Unlisted => followers,'; replace = 'VisibilityPolicy.Unlisted => [.. followers, .. members.Where(uri => Remote(uri, origin))], // QA-0045 T3'; expect = @($ac02u); why = 'unlisted delivers ONLY to followers (contract section 4)' },
    @{ id = 'T4';  ac = 'AC-02'; file = 'backend\src\Squid.Service\OutboundPipeline.cs'; find = 'VisibilityPolicy.MembersOnly => members.Where(uri => Remote(uri, origin)).ToList(),'; replace = 'VisibilityPolicy.MembersOnly => followers, // QA-0045 T4'; expect = @($ac02m); why = 'membersOnly delivers ONLY to Active memberships (7.2)' },
    @{ id = 'T5';  ac = 'AC-02'; file = 'backend\src\Squid.Service\OutboundPipeline.cs'; find = 'if (post.VisibilityAtPublish == NoFederation) return;'; replace = 'if (false) return; // QA-0045 T5'; find2 = '_ => [],'; replace2 = '_ => followers, // QA-0045 T5'; expect = @($ac02p); why = 'private creates NO delivery at all (7.3) - dead guard plus a non-empty default arm' },
    @{ id = 'T6';  ac = 'AC-03'; file = 'backend\src\Squid.Api\Federation\FederationEndpoints.cs'; find = 'await outbound.EnqueueFollowAsync(origin, current.Handle, target);'; replace = 'await Task.CompletedTask; // QA-0045 T6: Follow never enqueued'; expect = @($ac03); why = 'the follow route must enqueue the signed Follow for the peer (GAP-41)' },
    @{ id = 'T7';  ac = 'AC-03'; file = 'backend\src\Squid.Api\Federation\FederationEndpoints.cs'; find = 'if (current is null)'; replace = 'if (false) // QA-0045 T7: session gate removed'; expect = @($ac03); why = 'the follow route requires a session: anonymous must answer 401' },
    @{ id = 'T8';  ac = 'AC-04'; file = 'backend\src\Squid.Service\OutboundPipeline.cs'; find = 'if (effective == VisibilityPolicy.MembersOnly'; replace = 'if (false && effective == VisibilityPolicy.MembersOnly // QA-0045 T8'; expect = @($ac04); why = 'R-05 re-evaluation at send time must cancel a now-denied recipient' },
    @{ id = 'T9';  ac = 'AC-05'; file = 'backend\src\Squid.Service\OutboundPipeline.cs'; find = $enqueueCreate; replace = $enqueueCreateBornSent; expect = @($ac05); why = 'the publish answer carries the deliveries registered as Pending (FR-036)' },
    @{ id = 'T10'; ac = 'AC-06'; file = 'backend\src\Squid.Service\OutboundPipeline.cs'; find = '.Where(row => row.State != PeerDeliveryState.Cancelled)'; replace = '.Where(_ => true) // QA-0045 T10'; expect = @($ac06); why = 'the Delete walks the Create recipients, skipping the ones that never received it (R-08)' }
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
# out of this count on purpose: the QA's own new test file is a deliverable inside the
# task's touches (classified by the scope scan and by the checkers), not a survivor.
$status = @(git -C $repo status --porcelain -uno -- backend)
$residue = @($status | Where-Object { $_ }).Count
$untracked = @(git -C $repo status --porcelain -uall -- backend | Where-Object { $_ -match '^\?\?' })
if ($residue -ne 0) { $script:failedCycles++ }

$elapsed = [int]$sw.Elapsed.TotalSeconds
Log ('UNTRACKED backend={0} (esperado 1: QaTask0045FanOutGapTests.cs da QA, dentro dos touches)' -f $untracked.Count)
Log ('SUMMARY cycles={0} failed={1} residue-backend={2} temp-backups=0 elapsed-s={3}' -f $cycles.Count, $script:failedCycles, $residue, $elapsed)

if ($script:failedCycles -gt 0) { exit 1 }
exit 0
