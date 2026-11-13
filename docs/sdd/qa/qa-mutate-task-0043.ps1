# QA-0043 can-fail mutation driver (TASK-0043, ActivityPub surface).
# Sixteen cycles over the filter Squid.Tests.Federation (the eight DEV scenarios of
# ActivityPubSurfaceTests plus the QA-0043 federation-Accept scenario): each one edits ONE
# literal of PRODUCT code, runs the filter, classifies the RED from the LOG FILE
# (S-22 - never from the wrapper console code), checks that the AC's OWN scenario failed,
# restores with git checkout (byte-exact against the index, hash compared PRE vs POST)
# and bumps the mtime (S-23). A green run over the restored tree closes the ledger.
# ASCII-only on purpose: this file runs without a UTF-8 BOM under Windows PowerShell 5.1.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0043.ps1 [-VerifyOnly]
[CmdletBinding()]
param([switch]$VerifyOnly)

$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$ledgerPath = Join-Path $PSScriptRoot 'QA-0043-mutations.log'
$verifyPath = Join-Path $PSScriptRoot 'QA-0043-verify.log'
$sddTest = Join-Path $repo 'docs\sdd\_tools\sdd-test.ps1'
# Both AC scenario classes of Squid.Tests.Federation: the eight DEV scenarios of
# ActivityPubSurfaceTests plus the QA-0043 federation-Accept scenario.
$filter = 'FullyQualifiedName~Squid.Tests.Federation'

# The scenario of each AC, by method name (ActivityPubSurfaceTests.cs).
$ac01 = 'The_actor_document_carries_both_keys_and_the_four_endpoints'
$ac02 = 'The_three_actor_collections_answer_first_and_next_without_the_items'
$ac03o = 'The_object_and_the_container_answer_jsonld_with_the_squid_fields'
$ac03c = 'The_container_outbox_is_paged_and_the_members_page_respects_visibility'
$ac04 = 'Nodeinfo_reports_the_software_and_its_version'
$ac05u = 'Both_inboxes_refuse_without_a_verifiable_signature_and_change_nothing'
$ac05s = 'A_locally_verifiable_signature_is_accepted_without_applying_the_activity'
$ac06 = 'The_profile_negotiates_accept_and_webfinger_keeps_only_the_self_link'
$ac06f = 'The_federation_routes_negotiate_the_accept_before_any_lookup'

$selfLine = 'new { rel = "self", type = ActivityJson, href = $"{origin}/users/{name}" },'
$profileLine = '                    new { rel = "profile", type = ActivityJson, href = $"{origin}/users/{name}" },'

$cycles = @(
    @{ id = 'T1';  ac = 'AC-01'; file = 'backend\src\Squid.Protocols\ActivityPub\ProfileJsonLd.cs'; find = '$"{id}/following",'; replace = '$"{id}/folloving",'; expect = @($ac01); why = 'actor must publish the following endpoint (S-26)' },
    @{ id = 'T2';  ac = 'AC-01'; file = 'backend\src\Squid.Protocols\ActivityPub\ProfileJsonLd.cs'; find = 'new RsaKeyDocument($"{id}#main-key", id,'; replace = 'new RsaKeyDocument($"{id}#rsa-key", id,'; expect = @($ac01); why = 'publicKey id must end in #main-key (contract 1.1)' },
    @{ id = 'T3';  ac = 'AC-02'; file = 'backend\src\Squid.Api\Federation\FederationEndpoints.cs'; find = 'all.Count, $"{id}?page=1", $"{id}?page=2"'; replace = 'all.Count, $"{id}", $"{id}?page=2"'; expect = @($ac02); why = 'collection must publish first = ?page=1' },
    @{ id = 'T4';  ac = 'AC-02'; file = 'backend\src\Squid.Api\Federation\FederationEndpoints.cs'; find = 'all.Count, $"{id}?page=1", $"{id}?page=2"'; replace = 'all.Count, $"{id}?page=1", $"{id}?page=9"'; expect = @($ac02); why = 'collection must publish next = ?page=2' },
    @{ id = 'T5';  ac = 'AC-02'; file = 'backend\src\Squid.Protocols\ActivityPub\CollectionJsonLd.cs'; find = '"OrderedCollection", totalItems, first, next,'; replace = '"Collection", totalItems, first, next,'; expect = @($ac02); why = 'type must be OrderedCollection' },
    @{ id = 'T6';  ac = 'AC-03'; file = 'backend\src\Squid.Protocols\ActivityPub\ObjectJsonLd.cs'; find = '            "Note",'; replace = '            "Article",'; expect = @($ac03o); why = 'object must answer as a Note' },
    @{ id = 'T7';  ac = 'AC-03'; file = 'backend\src\Squid.Protocols\ActivityPub\ObjectJsonLd.cs'; find = 'Word(container.Kind),'; replace = '"kind",'; expect = @($ac03o); why = 'group must publish squid:kind of the container' },
    @{ id = 'T8';  ac = 'AC-03'; file = 'backend\src\Squid.Api\Federation\FederationEndpoints.cs'; find = 'context, seams, container, $"{container.Id}/outbox",'; replace = 'context, seams, null, $"{container.Id}/outbox",'; expect = @($ac03c); why = 'container outbox must carry the squid fields' },
    @{ id = 'T9';  ac = 'AC-03'; file = 'backend\src\Squid.Api\Federation\FederationEndpoints.cs'; find = 'member.State == MembershipState.Active'; replace = 'member.State != MembershipState.Left'; expect = @($ac03c); why = 'members page must carry only Active memberships' },
    @{ id = 'T10'; ac = 'AC-04'; file = 'backend\src\Squid.Api\Federation\FederationEndpoints.cs'; find = 'protocols = new[] { "activitypub" },'; replace = 'protocols = new[] { "ap" },'; expect = @($ac04); why = 'nodeinfo must report the activitypub protocol' },
    @{ id = 'T11'; ac = 'AC-05'; file = 'backend\src\Squid.Service\InboxSignatureGate.cs'; find = '            return false;'; replace = '            return true;'; expect = @($ac05u); why = 'a request without signature headers must be refused' },
    @{ id = 'T12'; ac = 'AC-05'; file = 'backend\src\Squid.Api\Federation\FederationEndpoints.cs'; find = 'StatusCodes.Status401Unauthorized,'; replace = 'StatusCodes.Status400BadRequest,'; expect = @($ac05u); why = 'the refusal status is 401' },
    @{ id = 'T13'; ac = 'AC-05'; file = 'backend\src\Squid.Service\InboxSignatureGate.cs'; find = 'return verdict.Accepted;'; replace = 'return false;'; expect = @($ac05s); why = 'a locally verifiable signature must be accepted (202)' },
    @{ id = 'T14'; ac = 'AC-06'; file = 'backend\src\Squid.Api\Federation\AcceptNegotiation.cs'; find = '|| media.Equals(LdJson, StringComparison.OrdinalIgnoreCase))'; replace = '|| false)'; expect = @($ac06); why = 'application/ld+json with the AS profile must be accepted' },
    @{ id = 'T15'; ac = 'AC-06'; file = 'backend\src\Squid.Api\Discovery\DiscoveryEndpoints.cs'; find = $selfLine; replace = $selfLine + "`n" + $profileLine; expect = @($ac06); why = 'webfinger must NOT publish rel=profile (P-08)' },
    @{ id = 'T16'; ac = 'AC-06'; file = 'backend\src\Squid.Api\Federation\FederationEndpoints.cs'; find = 'AcceptNegotiation.RefuseUnlessAccepted(context);'; replace = 'null;'; expect = @($ac06f); why = 'every federation route negotiates the Accept first (406 before the lookup)' }
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
    $pathspec = $Rel -replace '\\', '/'   # git pathspecs are slash-separated on every platform
    $sha = git -C $repo rev-parse "HEAD:$pathspec" | Out-String
    if ($LASTEXITCODE -ne 0) { return "error:$($sha.Trim())" }
    return $sha.Trim()
}

# Runs the filter through sdd-test.ps1 and returns what the LOG FILE says (S-22).
function Invoke-Filter {
    # EAP Continue while the child powershell streams: under Stop a single native stderr
    # line merged by 2>&1 would abort the whole ledger instead of being recorded as text.
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

# Residue: no MUTATION may survive in the product tree. -uno keeps untracked files out of
# this count on purpose: the QA's own new test file is a deliverable inside the task's touches
# (classified by the scope scan and by the checkers), not a mutation survivor.
$status = @(git -C $repo status --porcelain -uno -- backend)
$residue = @($status | Where-Object { $_ }).Count
$untracked = @(git -C $repo status --porcelain -uall -- backend | Where-Object { $_ -match '^\?\?' })
if ($residue -ne 0) { $script:failedCycles++ }

$elapsed = [int]$sw.Elapsed.TotalSeconds
Log ('UNTRACKED backend={0} (esperado 1: QaTask0043AcceptTests.cs da QA, dentro dos touches)' -f $untracked.Count)
Log ('SUMMARY cycles={0} failed={1} residue-backend={2} temp-backups=0 elapsed-s={3}' -f $cycles.Count, $script:failedCycles, $residue, $elapsed)

if ($script:failedCycles -gt 0) { exit 1 }
exit 0
