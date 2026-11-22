# QA-0059 can-fail mutation driver (TASK-0059 - import births the 0009 instance copy). Five
# cycles over the family FullyQualifiedName~ImportInstanceCopy (the DEV's 4 AC tests + the 2
# scenarios QA-0059 added): one product literal per AC, one save moved above the identity
# creation for the "never on the 409 path" half of AC-01, and one full REVERT of the endpoint to
# its develop version - the cycle that proves AC-02's byte-identical answer against the real
# pre-change artifact. Each cycle edits PRODUCT code, runs the family filter, classifies the RED
# from the LOG FILE (S-22 - never from the wrapper console), checks the AC's own scenario failed,
# restores with git checkout HEAD (byte-exact against the index, hash compared PRE vs POST) and
# bumps the mtime (S-23). A green run over the restored tree closes the ledger.
# ASCII-only on purpose: this file runs without a UTF-8 BOM under Windows PowerShell 5.1, and no
# find string below may contain a non-ASCII byte.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0059.ps1 [-VerifyOnly]
[CmdletBinding()]
param([switch]$VerifyOnly)

$ErrorActionPreference = 'Stop'

# git is not on PATH in this session (QA-0054 hit the same trap): point at the known install.
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    $env:PATH = 'C:\Program Files\Git\cmd;' + $env:PATH
}

$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$ledgerPath = Join-Path $PSScriptRoot 'QA-0059-mutations.log'
$verifyPath = Join-Path $PSScriptRoot 'QA-0059-verify.log'
$sddTest = Join-Path $repo 'docs\sdd\_tools\sdd-test.ps1'
$fam = 'FullyQualifiedName~ImportInstanceCopy'

$fAc01 = 'Ac01_with_the_secret_the_import_births'
$fAc02 = 'Ac02_without_the_secret_the_import_answers'
$fAc03 = 'Ac03_the_copy_never_leaves_the_table_in_clear'
$fAc04 = 'Ac04_importing_the_same_kit_twice'
$fConflict = 'Conflict_path_a_foreign_kit_409s'
$fObs = 'Obs01_after_the_in_memory_rotation'

$nl = "`n"
$endpoint = 'backend/src/Squid.Api/Recovery/RecoveryEndpoints.cs'
$store = 'backend/src/Squid.Service/SqliteInstanceSigningKeyStore.cs'

$saveBlock = ('        if (instanceCopy is not null)' + $nl +
    '        {' + $nl +
    '            await instanceCopies.SaveAsync(handle, Convert.ToBase64String(instanceCopy));' + $nl +
    '        }')
$markVerified = '        // Holding the kit with the password is the proof FR-007 asks for: the import marks it verified.'

$cycles = @(
    @{ id = 'M1'; ac = 'AC-01'; kind = 'edit'; file = $endpoint; edits = ,@(
            @{ find = 'instanceCopy = instanceKeys.WrapInstanceCopy(rsa);'; replace = 'instanceCopy = null; // QA-0059 M1 (the instance copy is never born)' }
        ); filter = $fam; expect = @($fAc01, $fAc03, $fAc04, $fConflict); why = 'SPEC-0012 F: with Squid:RecoveryKey the import must birth the 0009 copy of the actor RSA; dropping the wrap leaves no row, so Ac01 (row + federation), Ac03 (the sweep needs a copy to hide), Ac04 (COUNT 1) and the 409 scenario all go red while Ac02 stays green (no config -> same answer as before)' },
    @{ id = 'M2'; ac = 'AC-04'; kind = 'edit'; file = $store; edits = ,@(
            @{ find = ('insert.CommandText =' + $nl + '            "INSERT OR REPLACE INTO instance_signing_keys (handle, wrapped_signing_key) "'); replace = ('insert.CommandText =' + $nl + '            "INSERT INTO instance_signing_keys (handle, wrapped_signing_key) " // QA-0059 M2 (upsert degraded to plain INSERT)') }
        ); filter = $fam; expect = @($fAc04); why = 'B-14 idempotency rides the INSERT OR REPLACE upsert keyed by handle: a plain INSERT makes the second import of the same kit hit the primary key, the request fails and Ac04 (same answer twice, COUNT 1) goes red' },
    @{ id = 'M3'; ac = 'AC-01'; kind = 'edit'; file = $endpoint; edits = @(
            @{ find = ($saveBlock + $nl + $nl + $markVerified); replace = ('        // QA-0059 M3 (save moved ABOVE the identity creation)' + $nl + $markVerified) },
            @{ find = '        ActorState state;'; replace = ('        // QA-0059 M3 (the copy now lands BEFORE the identity exists: the 409 path writes too)' + $nl + $saveBlock + $nl + '        ActorState state;') }
        ); filter = $fam; expect = @($fConflict); why = 'the row must land only after the identity exists and NEVER on the 409 path: saving before CreateAsync lets a foreign kit for a taken handle overwrite the stored copy, and only the 409 scenario compares the row byte-for-byte after the refusal' },
    @{ id = 'M4'; ac = 'AC-02'; kind = 'revert'; ref = 'develop'; file = $endpoint; edits = @(); filter = $fam; expectGreen = @($fAc02); expect = @($fAc01, $fAc03, $fAc04, $fConflict); why = 'AC-02 byte-identical: run the whole family against the DEVELOP version of the endpoint (the behaviour before this task). Ac02 must stay GREEN - that is the proof its expected bytes are the real pre-change artifact - while every scenario that needs the copy goes red' },
    @{ id = 'M5'; ac = 'AC-03'; kind = 'edit'; file = $endpoint; edits = ,@(
            @{ find = ('        return Results.Json(' + $nl + '            new { handle, displayName = state.DisplayName },' + $nl + '            statusCode: StatusCodes.Status201Created);'); replace = ('        return Results.Json(' + $nl + '            new { handle, displayName = state.DisplayName, copy = instanceCopy is null ? null : Convert.ToBase64String(instanceCopy) }, // QA-0059 M5 (the wrapped copy LEAKS into the answer)' + $nl + '            statusCode: StatusCodes.Status201Created);') }
        ); filter = $fam; expect = @($fAc02, $fAc03); why = 'the copy must never be served: leaking the wrapped bytes into the import answer breaks the absence sweep (Ac03) and, with no config, appends copy:null to the answer Ac02 compares byte for byte (Ac02)' }
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
        if ($c.kind -eq 'revert') {
            $pathspec = $c.file -replace '\\', '/'
            $dev = (git -C $repo rev-parse ("{0}:{1}" -f $c.ref, $pathspec) | Out-String).Trim()
            $head = (git -C $repo rev-parse ("HEAD:{0}" -f $pathspec) | Out-String).Trim()
            $differs = ($dev -ne $head -and $dev -notlike 'error:*')
            if (-not $differs) { $bad++ }
            $lines += ('VERIFY {0} ac={1} kind=revert ref={2} differs={3} dev-blob={4}' -f $c.id, $c.ac, $c.ref, $differs.ToString().ToLower(), $dev)
            continue
        }
        $path = Join-Path $repo $c.file
        $text = [System.IO.File]::ReadAllText($path)
        $editCount = 0
        $okAll = $true
        foreach ($e in $c.edits) {
            $hits = ([regex]::Matches($text, [regex]::Escape($e.find))).Count
            $editCount++
            if ($hits -ne 1 -or $e.find -eq $e.replace) { $okAll = $false }
            $lines += ('VERIFY {0} ac={1} edit={2} hits={3} ok={4}' -f $c.id, $c.ac, $editCount, $hits, ($hits -eq 1 -and $e.find -ne $e.replace).ToString().ToLower())
        }
        if (-not $okAll) { $bad++ }
        $lines += ('VERIFY-CYCLE {0} ac={1} edits={2} ok={3} why-present={4}' -f $c.id, $c.ac, $c.edits.Count, $okAll.ToString().ToLower(), ([bool]$c.why).ToString().ToLower())
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

    Log ('{0} PRE at={1} ac={2} hash={3} head-match={4} edits={5}' -f $c.id, (Get-Date -Format HHmmss), $c.ac, $pre, ($pre -eq $head).ToString().ToLower(), $c.edits.Count)

    if ($c.kind -eq 'revert') {
        # Point the WORKING TREE (and the index) at the pre-change endpoint, run, then put HEAD
        # back: the byte-identity proof must run against the real develop artifact.
        git -C $repo checkout $c.ref -- $c.file | Out-Null
        $swapExit = $LASTEXITCODE
        if ($swapExit -ne 0) {
            Log ('{0} TOOLING-FAIL checkout-ref exit={1}' -f $c.id, $swapExit)
            $script:failedCycles++
            git -C $repo checkout HEAD -- $c.file | Out-Null
            continue
        }
        Log ('{0} REVERT ref={1} work-blob={2}' -f $c.id, $c.ref, (Blob $c.file))
    }
    else {
        $text = [System.IO.File]::ReadAllText($path)
        $toolingOk = $true
        foreach ($e in $c.edits) {
            $hits = ([regex]::Matches($text, [regex]::Escape($e.find))).Count
            if ($hits -ne 1) {
                Log ('{0} TOOLING-FAIL at={1} find-hits={2} (expected 1, nothing mutated)' -f $c.id, $e.find.Substring(0, [Math]::Min(60, $e.find.Length)), $hits)
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
    }

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

        # expectGreen: tests that must STAY GREEN under this mutation. A wanted test found in
        # the red list turned red -> the expectation failed. (run1 of this driver had this
        # predicate inverted and labelled a correct M4 as MISS; QA-0059-mutations-run1.log.)
        $greenWanted = @()
        if ($c.expectGreen) { $greenWanted = @($c.expectGreen) }
        $greenTurnedRed = @($greenWanted | Where-Object {
                $want = $_
                [bool]($redNames | Where-Object { $_ -like "*$want*" })
            })
        if ($greenWanted.Count -gt 0) {
            if ($greenTurnedRed.Count -eq 0) {
                Log ('{0} GREEN-EXPECT-OK expect={1} (stayed green under the mutation)' -f $c.id, ($greenWanted -join '; '))
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

# Green over the restored tree: the ledger may only close on a green filter.
$green = Invoke-Filter $fam
Log ('GREEN wrapper={0} logExit={1} aprovado={2} falha={3} log={4}' -f $green.wrapper, $green.logExit, $green.aprovado, $green.falha, (LogName $green.logPath))
$greenOk = ($green.logExit -eq 0 -and $green.falha -eq 0 -and $green.aprovado -eq 6)
if (-not $greenOk) { $script:failedCycles++ }

# Residue: no MUTATION may survive in the tracked product tree. The only untracked file this QA
# owns under backend is its own scenario pair (ImportInstanceCopyQaTests.cs).
$status = @(git -C $repo status --porcelain -uno -- backend | Where-Object { $_ })
$residue = $status.Count
$untracked = @(git -C $repo status --porcelain -uall -- backend | Where-Object { $_ -match '^\?\?' })
$allowedUntracked = @('?? backend/tests/Squid.Tests/Recovery/ImportInstanceCopyQaTests.cs')
$rogueUntracked = @($untracked | Where-Object { $allowedUntracked -notcontains $_ })
if ($residue -ne 0) { $script:failedCycles++ }

$elapsed = [int]$sw.Elapsed.TotalSeconds
Log ('UNTRACKED backend={0} rogue={1} (esperado 0: so o cenario novo da QA-0059)' -f $untracked.Count, $rogueUntracked.Count)
if ($rogueUntracked.Count -ne 0) { $script:failedCycles++ }
Log ('SUMMARY cycles={0} failed={1} residue-backend={2} green-aprovado={3} elapsed-s={4}' -f $cycles.Count, $script:failedCycles, $residue, $green.aprovado, $elapsed)

if ($script:failedCycles -gt 0) { exit 1 }
exit 0
