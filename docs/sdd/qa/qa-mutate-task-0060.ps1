# QA-0060 can-fail mutation driver (TASK-0060 - redundancy.score + thin page /admin/backup).
# Eleven cycles over the family FullyQualifiedName~RedundancyScore (the DEV's 5 AC facts plus
# the drill-value scenario QA-0060 added): one product edit per acceptance criterion - the
# score formatter (AC-01 suffix), the stale-verified-row rule (AC-01 honesty), the page's read
# of the state (AC-02), the operator gate of the HTML route (AC-03), an external asset tag on
# the page (AC-04), the off-site half of the score (AC-05), the configured denominator in the
# composition root (AC-01), and BOTH halves of the S-52 boot warning (presence and
# silence-in-default) - plus two SCOPE cycles that edit a file outside the touches and a
# PackageVersion line, proving qa-scope-task-0060.ps1 itself can fail (AC-06/AC-04).
# Every cycle edits PRODUCT code, runs the family filter (or the scope script), classifies the
# RED from the LOG FILE (S-22 - never from the wrapper console) together with the failing
# assertion's first message line, checks the AC's own scenario failed (and the traced greens
# stayed green), restores with git checkout HEAD (byte-exact against the index, hash compared
# PRE vs POST) and bumps the mtime (S-23). A green run over the restored tree closes the ledger.
# ASCII-only on purpose: this file runs without a UTF-8 BOM under Windows PowerShell 5.1, and no
# find string below may contain a non-ASCII byte.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0060.ps1 [-VerifyOnly]
[CmdletBinding()]
param([switch]$VerifyOnly)

$ErrorActionPreference = 'Stop'

# git is not on PATH in this session (QA-0054 hit the same trap): point at the known install.
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    $env:PATH = 'C:\Program Files\Git\cmd;' + $env:PATH
}

$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$ledgerPath = Join-Path $PSScriptRoot 'QA-0060-mutations.log'
$verifyPath = Join-Path $PSScriptRoot 'QA-0060-verify.log'
$sddTest = Join-Path $repo 'docs\sdd\_tools\sdd-test.ps1'
$scope = Join-Path $PSScriptRoot 'qa-scope-task-0060.ps1'
$fam = 'FullyQualifiedName~RedundancyScore'

$fStatus = 'Ac01_ac02_ac03_ac04_The_status_and_the_thin_page_publish_the_state_behind_the_operator_gate'
$fAc05 = 'Ac05_The_score_moves_from_1_2_to_2_2_when_the_off_site_holds_the_newest_verified_envelope'
$fFmt = 'Ac01_The_score_format_is_the_one_doc_05_section_8_fixes'
$fSrc = 'The_score_counts_the_destinations_holding_the_newest_verified_envelope'
$fQaDrill = 'Ac02_the_page_prints_the_drill_values_the_json_route_answers'
$fS52 = 'S52_A_partial_off_site_configuration_warns_at_boot_and_the_default_stays_silent'

$nl = "`n"
$endpoint = 'backend/src/Squid.Api/Admin/AdminEndpoints.cs'
$page = 'backend/src/Squid.Api/Admin/BackupPage.cs'
$redundancy = 'backend/src/Squid.Api/Admin/BackupRedundancy.cs'
$source = 'backend/src/Squid.Service/SqliteBackupRedundancySource.cs'
$hostFile = 'backend/src/Squid.Service/SquidServiceHost.cs'
$apiEdge = 'backend/src/Squid.Api/SquidApi.cs'
$packages = 'backend/Directory.Packages.props'

$m4Find = ('        var (_, refusal) = Gate(context, configuration);' + $nl +
    '        if (refusal is not null)' + $nl +
    '        {' + $nl +
    '            return refusal;' + $nl +
    '        }' + $nl +
    $nl +
    '        var state = await status.ReadAsync(context.RequestAborted);' + $nl +
    '        var score = await redundancy.ReadAsync(context.RequestAborted);' + $nl +
    '        return Results.Content(')
$m4Replace = ('        // QA-0060 M4 (the operator gate of the thin page is gone)' + $nl +
    '        var state = await status.ReadAsync(context.RequestAborted);' + $nl +
    '        var score = await redundancy.ReadAsync(context.RequestAborted);' + $nl +
    '        return Results.Content(')
$s52Find = 'if (s3Options is null && HasPartialS3Configuration(configuration))'

$cycles = @(
    @{ id = 'M1'; ac = 'AC-01'; kind = 'edit'; file = $redundancy; edits = ,@(
            @{ find = 'return OffsiteVerified ? score : score + '; replace = 'return score; // QA-0060 M1 (suffix dropped) ' }
        ); filter = $fam; expect = @($fFmt, $fSrc, $fStatus, $fAc05); expectGreen = @($fS52, $fQaDrill); why = 'AC-01 format: the disaster suffix hangs on the off-site half (SPEC-0012 G); dropping it turns red every fact that expects "Redundancia: x/y - sem recuperacao de desastre" - the doc 05 section 8 spelling itself' },
    @{ id = 'M2'; ac = 'AC-01'; kind = 'edit'; file = $source; edits = ,@(
            @{ find = 'var lastAttemptFailedThere = newest.From == windowFrom'; replace = 'var lastAttemptFailedThere = false && newest.From == windowFrom' }
        ); filter = $fam; expect = @($fSrc); expectGreen = @($fStatus, $fAc05, $fFmt, $fQaDrill, $fS52); why = 'AC-01 honesty: a stale verified row must not claim the window whose newest attempt failed; neutralizing the rule turns red the run-C scenario that expects 1/2 while a naive read answers 2/2' },
    @{ id = 'M3'; ac = 'AC-02'; kind = 'edit'; file = $page; edits = ,@(
            @{ find = 'var score = Escape(redundancy.Score);'; replace = 'var score = "0/0"; // QA-0060 M3 (the page stops reading the state) ' }
        ); filter = $fam; expect = @($fStatus, $fAc05); expectGreen = @($fFmt, $fSrc, $fQaDrill, $fS52); why = 'AC-02: the page must show the value the registry answers, not its own text - a hardcoded score leaves the JSON route and the page disagreeing, which the byte-identical assertions catch' },
    @{ id = 'M4'; ac = 'AC-03'; kind = 'edit'; file = $endpoint; edits = ,@(
            @{ find = $m4Find; replace = $m4Replace }
        ); filter = $fam; expect = @($fStatus); expectGreen = @($fAc05, $fFmt, $fSrc, $fQaDrill, $fS52); why = 'AC-03: without the gate the thin page answers the anonymous and the stranger sessions 200, so the 401/403 pair of the first fact fails at its first status-page assertion' },
    @{ id = 'M5'; ac = 'AC-04'; kind = 'edit'; file = $page; edits = ,@(
            @{ find = '<dt>redundancy.score</dt>'; replace = ('<script src="/app.js"></script>' + $nl + '            <dt>redundancy.score</dt>') }
        ); filter = $fam; expect = @($fStatus); expectGreen = @($fAc05, $fFmt, $fSrc, $fQaDrill, $fS52); why = 'AC-04: an external asset tag on the page breaks the inline-only assertions (DoesNotContain src=) - the no-front-build half of the criterion' },
    @{ id = 'M6'; ac = 'AC-05'; kind = 'edit'; file = $source; edits = ,@(
            @{ find = 'return new BackupRedundancy(verified, configured.Length, offsiteVerified);'; replace = 'return new BackupRedundancy(verified, configured.Length, false); // QA-0060 M6 ' }
        ); filter = $fam; expect = @($fAc05, $fSrc); expectGreen = @($fStatus, $fFmt, $fQaDrill, $fS52); why = 'AC-05: the off-site destination never counts as verified, so the score can never move 1/2 -> 2/2 and the suffix never leaves - every 2/2 assertion goes red while the local-only fact stays green' },
    @{ id = 'M7'; ac = 'AC-01'; kind = 'edit'; file = $hostFile; edits = ,@(
            @{ find = 'provider.GetService<S3CompatibleTarget>()?.TargetId));'; replace = 'null)); // QA-0060 M7 (off-site never configured) ' }
        ); filter = $fam; expect = @($fAc05); expectGreen = @($fStatus, $fSrc, $fFmt, $fQaDrill, $fS52); why = 'AC-01 denominator: the composition root must wire the configured off-site target - dropping it turns 1/2 into 1/1 on the S3 host, which the first score assertion of the AC-05 fact catches' },
    @{ id = 'M8'; ac = 'S-52'; kind = 'edit'; file = $hostFile; edits = ,@(
            @{ find = $s52Find; replace = ('if (s3Options is not null && HasPartialS3Configuration(configuration)) // QA-0060 M8') }
        ); filter = $fam; expect = @($fS52); expectGreen = @($fStatus, $fAc05, $fFmt, $fSrc, $fQaDrill); why = 'S-52 presence: inverting the guard silences the partial-configuration boot warning, and the Contains half of the S-52 fact fails first' },
    @{ id = 'M9'; ac = 'S-52'; kind = 'edit'; file = $hostFile; edits = ,@(
            @{ find = $s52Find; replace = ('if (true) // QA-0060 M9 (the warning fires for every boot) ') }
        ); filter = $fam; expect = @($fS52); expectGreen = @($fStatus, $fAc05, $fFmt, $fSrc, $fQaDrill); why = 'S-52 silence-in-default: warning unconditionally breaks the default local-only boot, and the DoesNotContain half of the S-52 fact fails' },
    @{ id = 'M10'; ac = 'AC-02'; kind = 'edit'; file = $page; edits = ,@(
            @{ find = 'var drillAt = Escape(Iso(state.LastDrillAt));'; replace = 'var drillAt = "-"; // QA-0060 M10 (the page stops reading the ledger instant) ' }
        ); filter = $fam; expect = @($fQaDrill); expectGreen = @($fStatus, $fAc05, $fFmt, $fSrc, $fS52); why = 'AC-02 drill value: the QA-0060 scenario reads restore.last_drill_at from the JSON route and demands the page reproduce it byte-identically - a page that prints a fixed placeholder turns only that scenario red while the DEV facts (which share the dash placeholder with drill_result) stay green' },
    @{ id = 'S1'; ac = 'AC-06'; kind = 'scope'; file = $apiEdge; edits = ,@(
            @{ find = 'public static WebApplication MapSquidApi(this WebApplication app) => app;'; replace = 'public static WebApplication MapSquidApi(this WebApplication app) => app; // QA-0060 S1 (outside the touches)' }
        ); filter = ''; expectScopeExit = 1; why = 'AC-06 can-fail of the scope scan itself: an edit in Squid.Api outside Admin/** must make qa-scope-task-0060.ps1 exit 1 (diff-files outside-touches + residue)' },
    @{ id = 'S2'; ac = 'AC-04'; kind = 'scope'; file = $packages; edits = ,@(
            @{ find = '    <PackageVersion Include="Microsoft.Data.Sqlite" Version="10.0.14" />'; replace = ('    <PackageVersion Include="Microsoft.Data.Sqlite" Version="10.0.14" />' + $nl + '    <PackageVersion Include="QA.Mutation.NotARealPackage" Version="1.0.0" />') }
        ); filter = ''; expectScopeExit = 1; why = 'AC-04/AC-06 can-fail of the package half: an added PackageVersion line must make qa-scope-task-0060.ps1 exit 1 (pkg-ref-added-lines)' }
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

# Runs one filter through sdd-test.ps1 and returns what the LOG FILE says (S-22): the summary,
# the failed test names, and the first line of each failing assertion's message - the FAILMSG
# is what tells M4's gate red from M5's asset-tag red when both fail the same fact.
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
    $result = @{ wrapper = $wrapper; logPath = $null; logExit = $null; falha = -1; aprovado = -1; total = -1; failed = @(); failMsgs = @{} }

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
    foreach ($fm in [regex]::Matches($content, 'Com falha (Squid\.Tests\.[^\s\[]+)')) {
        $name = $fm.Groups[1].Value
        $result.failed += $name
        # first line of the assertion message that follows this failure
        $tail = $content.Substring($fm.Index)
        $msg = [regex]::Match($tail, 'Mensagem de erro:\s*\r?\n\s*(.+)')
        if ($msg.Success) { $result.failMsgs[$name] = $msg.Groups[1].Value.Trim() }
        else { $result.failMsgs[$name] = '(sem mensagem)' }
    }
    return $result
}

function Invoke-Scope {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $console = (& powershell -NoProfile -ExecutionPolicy Bypass -File $scope 2>&1 | Out-String)
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }
    $summary = ''
    $sm = [regex]::Match($console, '(?m)^(SUMMARY .+)$')
    if ($sm.Success) { $summary = $sm.Groups[1].Value }
    return @{ exit = $code; summary = $summary }
}

function LogName([string]$Path) {
    if ([string]::IsNullOrEmpty($Path)) { return 'NONE' }
    return [System.IO.Path]::GetFileName($Path)
}

function FailMsg([hashtable]$Run, [string]$Name) {
    if ($Run.failMsgs.ContainsKey($Name)) {
        $v = $Run.failMsgs[$Name]
        if ($v.Length -gt 160) { $v = $v.Substring(0, 160) }
        return $v
    }
    return 'NONE'
}

# ---------------------------------------------------------------- -VerifyOnly

if ($VerifyOnly) {
    $lines = @()
    $bad = 0
    foreach ($c in $cycles) {
        $path = Join-Path $repo $c.file
        $text = [System.IO.File]::ReadAllText($path)
        $editCount = 0
        foreach ($e in $c.edits) {
            $hits = ([regex]::Matches($text, [regex]::Escape($e.find))).Count
            $editCount++
            $ok = ($hits -eq 1 -and $e.find -ne $e.replace)
            if (-not $ok) { $bad++ }
            $lines += ('VERIFY {0} ac={1} edit={2} hits={3} ok={4}' -f $c.id, $c.ac, $editCount, $hits, $ok.ToString().ToLower())
        }
        $lines += ('VERIFY-CYCLE {0} ac={1} kind={2} edits={3} why-present={4}' -f $c.id, $c.ac, $c.kind, $c.edits.Count, ([bool]$c.why).ToString().ToLower())
    }
    $bytes = [System.IO.File]::ReadAllBytes($PSCommandPath)
    $nonAscii = @($bytes | Where-Object { $_ -gt 127 }).Count
    if ($nonAscii -ne 0) { $bad++ }
    $lines += ('VERIFY-ASCII non-ascii-bytes={0} ok={1}' -f $nonAscii, ($nonAscii -eq 0).ToString().ToLower())
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

    Log ('{0} PRE at={1} ac={2} kind={3} hash={4} head-match={5} edits={6}' -f $c.id, (Get-Date -Format HHmmss), $c.ac, $c.kind, $pre, ($pre -eq $head).ToString().ToLower(), $c.edits.Count)

    $text = [System.IO.File]::ReadAllText($path)
    $toolingOk = $true
    foreach ($e in $c.edits) {
        $hits = ([regex]::Matches($text, [regex]::Escape($e.find))).Count
        if ($hits -ne 1) {
            Log ('{0} TOOLING-FAIL find-hits={1} (expected 1, nothing mutated) {2}' -f $c.id, $hits, $e.find.Substring(0, [Math]::Min(60, $e.find.Length)))
            $toolingOk = $false
        }
    }
    if (-not $toolingOk) {
        $script:failedCycles++
        continue
    }

    $mutated = $text
    foreach ($e in $c.edits) {
        $mutated = $mutated.Replace($e.find, $e.replace)
    }
    [System.IO.File]::WriteAllText($path, $mutated, (New-Object System.Text.UTF8Encoding($false)))

    if ($c.kind -eq 'scope') {
        $run = Invoke-Scope
        $ok = ($run.exit -eq $c.expectScopeExit)
        if ($ok) {
            Log ('{0} SCOPE-EXPECT-OK exit={1} (the scope scan FAILED as it must) {2}' -f $c.id, $run.exit, $run.summary)
        }
        else {
            Log ('{0} SCOPE-EXPECT-MISS exit={1} esperado={2} {3}' -f $c.id, $run.exit, $c.expectScopeExit, $run.summary)
            $script:failedCycles++
        }
    }
    else {
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
            foreach ($rn in $redNames) {
                Log ('{0} RED {1} :: {2}' -f $c.id, $rn, (FailMsg $run $rn))
            }
            if ($redNames.Count -eq 0) { Log ('{0} RED none' -f $c.id) }

            # expectGreen: tests that must STAY GREEN under this mutation - a wanted test
            # found in the red list turned red -> the expectation failed.
            $greenWanted = @()
            if ($c.expectGreen) { $greenWanted = @($c.expectGreen) }
            $greenTurnedRed = @($greenWanted | Where-Object {
                    $want = $_
                    [bool]($redNames | Where-Object { $_ -like "*$want*" })
                })
            if ($greenWanted.Count -gt 0) {
                if ($greenTurnedRed.Count -eq 0) {
                    Log ('{0} GREEN-EXPECT-OK expect={1}' -f $c.id, ($greenWanted -join '; '))
                }
                else {
                    Log ('{0} GREEN-EXPECT-MISS expect={1} turned red={2}' -f $c.id, ($greenWanted -join '; '), ($greenTurnedRed -join '; '))
                    $script:failedCycles++
                }
            }

            if ($c.expect.Count -gt 0) {
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
    }

    # restore: byte-exact against HEAD, then the mtime bump of S-23
    git -C $repo checkout HEAD -- $c.file | Out-Null
    $restoreExit = $LASTEXITCODE
    $post = Blob $c.file
    $indexClean = [string]::IsNullOrWhiteSpace((git -C $repo status --porcelain -- $c.file | Out-String))
    if ($restoreExit -ne 0 -or $post -ne $pre -or -not $indexClean) {
        Log ('{0} RESTORE-FAIL exit={1} pre={2} post={3} index-clean={4}' -f $c.id, $restoreExit, $pre, $post, $indexClean)
        $script:failedCycles++
    }
    else {
        (Get-Item -LiteralPath $path).LastWriteTime = Get-Date
        Log ('{0} RESTORE-OK hash=PRE residue=0' -f $c.id)
    }
}

# Green over the restored tree: the ledger may only close on a green filter (6 = 5 DEV facts
# plus the QA-0060 drill-value scenario).
$green = Invoke-Filter $fam
Log ('GREEN wrapper={0} logExit={1} aprovado={2} falha={3} log={4}' -f $green.wrapper, $green.logExit, $green.aprovado, $green.falha, (LogName $green.logPath))
$greenOk = ($green.logExit -eq 0 -and $green.falha -eq 0 -and $green.aprovado -eq 6)
if (-not $greenOk) { $script:failedCycles++ }

# Residue: no MUTATION may survive in the tracked product tree. The only untracked file under
# backend is QA-0060's own scenario (RedundancyScoreQaTests.cs).
$status = @(git -C $repo status --porcelain -uno -- backend | Where-Object { $_ })
$residue = $status.Count
$untracked = @(git -C $repo status --porcelain -uall -- backend | Where-Object { $_ -match '^\?\?' })
$allowedUntracked = @('?? backend/tests/Squid.Tests/Backup/RedundancyScoreQaTests.cs')
$rogueUntracked = @($untracked | Where-Object { $allowedUntracked -notcontains $_.Trim() })
if ($residue -ne 0) { $script:failedCycles++ }

$elapsed = [int]$sw.Elapsed.TotalSeconds
Log ('UNTRACKED backend={0} rogue={1} (esperado 1: so o cenario novo da QA-0060)' -f $untracked.Count, $rogueUntracked.Count)
if ($rogueUntracked.Count -ne 0) { $script:failedCycles++ }
Log ('SUMMARY cycles={0} failed={1} residue-backend={2} green-aprovado={3} elapsed-s={4}' -f $cycles.Count, $script:failedCycles, $residue, $green.aprovado, $elapsed)

if ($script:failedCycles -gt 0) { exit 1 }
exit 0
