# QA-0058 can-fail mutation driver (TASK-0058 - RK/RC key rotation, reissued kit, one-shot
# response, runbook). Nine cycles over the DEV's own 7 tests (FullyQualifiedName~KeyRotation):
# one product literal per AC scenario of AC-01..AC-06, one deliberate TEST-side expectation
# break for AC-03 (rotation has no code path that can rewrite the volume, so only breaking the
# expectation proves the hash comparison can fail), and one PROBE for AC-05's in-place ring
# swap (never asserted - it reports coverage, it does not grade it). Each cycle edits the
# literal(s) of PRODUCT code, runs the KeyRotation filter, classifies the RED from the LOG FILE
# (S-22 - never from the wrapper console), checks the AC's own scenario failed, restores with
# git checkout (byte-exact against the index, hash compared PRE vs POST) and bumps the mtime
# (S-23). A green run over the restored tree closes the ledger.
# ASCII-only on purpose: this file runs without a UTF-8 BOM under Windows PowerShell 5.1, and
# no find string below may contain a non-ASCII byte.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0058.ps1 [-VerifyOnly]
[CmdletBinding()]
param([switch]$VerifyOnly)

$ErrorActionPreference = 'Stop'

# git is not on PATH in this session (QA-0054 hit the same trap): point at the known install.
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    $env:PATH = 'C:\Program Files\Git\cmd;' + $env:PATH
}

$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$ledgerPath = Join-Path $PSScriptRoot 'QA-0058-mutations.log'
$verifyPath = Join-Path $PSScriptRoot 'QA-0058-verify.log'
$sddTest = Join-Path $repo 'docs\sdd\_tools\sdd-test.ps1'
$fam = 'FullyQualifiedName~KeyRotation'

$fAc01 = 'Ac01_rotate_is_operator_only_and_retires_the_previous_epoch'
$fAc04 = 'Ac04_the_previous_credential_stops_verifying_and_the_reissued_kit_restores_the_root_key'
$fAc06 = 'Ac06_the_reissued_kit_is_answered_once_and_reaches_no_log_route_or_second_answer'
$fChain = 'Ac02_ac03_ac05_rotation_leaves_old_backups_identical_and_readable_while_the_new_chain_works'
$fRingSwap = 'Ac05_after_rotation_the_runtime_ring_opens_the_rewrapped_copy_and_signs'

$nl = "`n"

$cycles = @(
    @{ id = 'M1'; ac = 'AC-01'; probe = $false; file = 'backend/src/Squid.Api/Admin/AdminEndpoints.cs'; edits = ,@(
            @{ find = 'if (!IsOperator(configuration, current.Handle))'; replace = 'if (false && !IsOperator(configuration, current.Handle)) // QA-0058 M1 (operator gate fails open)' }
        ); filter = $fam; expect = @($fAc01); why = 'the operator gate must refuse a session outside Instance:Operators with 403 before any key moves; failing open lets the stranger rotate and Ac01 goes red on the 403 assertion' },
    @{ id = 'M2'; ac = 'AC-01'; probe = $false; file = 'backend/src/Squid.Service/SqliteKeyEpochStore.cs'; edits = @(
            @{ find = ('+ "VALUES (@Epoch, @At, @Fingerprint, @At)";' + $nl); replace = ('+ "VALUES (@Epoch, @At, @Fingerprint, NULL)";' + $nl) },
            @{ find = '"UPDATE key_epoch SET retired_at = @At WHERE epoch = @Epoch AND retired_at IS NULL";'; replace = '"UPDATE key_epoch SET retired_at = @At WHERE epoch = -1"; // QA-0058 M2 (RetiredAt never written)' }
        ); filter = $fam; expect = @($fAc01, $fChain); why = 'data-model 2.10 requires RetiredAt of the previous epoch to be written on rotation; killing both writes (materialise NULL + dead UPDATE) leaves it null and Ac01 and the chain test go red' },
    @{ id = 'M3'; ac = 'AC-02'; probe = $false; file = 'backend/src/Squid.Api/Crypto/KeyRotationService.cs'; edits = ,@(
            @{ find = 'keyring[currentEpoch] = oldKey;'; replace = '// QA-0058 M3 (retired epoch dropped from the kit keyring)' }
        ); filter = $fam; expect = @($fChain); why = 'R-20/SPEC-0012 E: the reissued kit must keep the OLD epoch readable so a pre-rotation backup restores; dropping it makes the restore refuse epoch 1 and the AC-02 scenario goes red' },
    @{ id = 'M4'; ac = 'AC-04'; probe = $false; file = 'backend/src/Squid.Api/Crypto/KeyRotationService.cs'; edits = ,@(
            @{ find = 'KeyDerivation.WrapRsaPrivateKey(newKey, rsa)'; replace = 'KeyDerivation.WrapRsaPrivateKey(oldKey, rsa) /* QA-0058 M4 (0009 copies not re-wrapped to the new chain) */' }
        ); filter = $fam; expect = @($fAc04, $fChain); why = 'the 0009 copies must be re-wrapped under the NEW key: leaving them under the old one makes the reissued kit unable to open the copy into a signing RSA and both Ac04 and the chain test go red' },
    @{ id = 'M5'; ac = 'AC-05'; probe = $false; file = 'backend/src/Squid.Api/Crypto/KeyRotationService.cs'; edits = ,@(
            @{ find = 'keys.RotateTo(newKey, keyring);'; replace = 'keys.RotateTo(oldKey, keyring); // QA-0058 M5 (backup key source stays on the old material)' }
        ); filter = $fam; expect = @($fAc04, $fChain); why = 'the swap must move backups and signing to the NEW derived chain: keeping the old material makes the post-rotation DK unwrap fail and the kit key differ from the instance key, reding Ac04 and the chain test' },
    @{ id = 'M6'; ac = 'AC-04'; probe = $false; file = 'backend/src/Squid.Api/Recovery/RecoveryEndpoints.cs'; edits = ,@(
            @{ find = ('if (expected.Length != submitted.Length' + $nl + '            || !CryptographicOperations.FixedTimeEquals(expected, SHA256.HashData(submitted)))'); replace = ('if (false && (expected.Length != submitted.Length' + $nl + '            || !CryptographicOperations.FixedTimeEquals(expected, SHA256.HashData(submitted)))) // QA-0058 M6 (instance verify always accepts') }
        ); filter = $fam; expect = @($fAc04, $fAc06); why = 'the old RC must stop verifying after a later rotation: disabling the hash compare answers 200 to the dead credential and Ac04 (and the AC-06 second-answer refusal) go red' },
    @{ id = 'M7'; ac = 'AC-06'; probe = $false; file = 'backend/src/Squid.Api/Admin/AdminEndpoints.cs'; edits = ,@(
            @{ find = 'logger.LogInformation("instance key rotated to epoch {KeyEpoch}", kit.KeyEpoch);'; replace = 'logger.LogInformation("instance key rotated to epoch {KeyEpoch} " + kit.RecoveryCredential, kit.KeyEpoch); /* QA-0058 M7 (credential concatenated INTO the template: the name-based PII enricher cannot see it) */' }
        ); filter = $fam; expect = @($fAc06); why = 'the one-shot rule says no log line ever carries kit material: concatenating the RC into the message template (bypassing the property-name enricher) must still be caught by the captured-log absence proof, reding Ac06' },
    @{ id = 'M7b'; ac = 'AC-06'; probe = $true; file = 'backend/src/Squid.Api/Admin/AdminEndpoints.cs'; edits = ,@(
            @{ find = 'logger.LogInformation("instance key rotated to epoch {KeyEpoch}", kit.KeyEpoch);'; replace = 'logger.LogInformation("instance key rotated to epoch {KeyEpoch} {Credential}", kit.KeyEpoch, kit.RecoveryCredential); /* QA-0058 M7b probe (credential as a NAMED property) */' }
        ); filter = $fam; expect = @(); why = 'PROBE (defense-in-depth): leaking the RC as a property named Credential - green means PiiSanitizingEnricher masked it to [MASKED] before the sink, i.e. the name-based PII layer swallows this shape; the AC-06 proof itself is M7' },
    @{ id = 'M8'; ac = 'AC-03'; probe = $false; file = 'backend/tests/Squid.Tests/Recovery/KeyRotationChainTests.cs'; edits = ,@(
            @{ find = 'var before = await scenario.VolumeHashesAsync();'; replace = 'var before = (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(); // QA-0058 M8 (expectation broken: baseline emptied, rotation rewrites nothing so only an empty baseline can red this)' }
        ); filter = $fam; expect = @($fChain); why = 'TEST-side expectation break: rotation has no product path that can touch the volume, so emptying the pre-rotation baseline is what proves the AC-03 hash comparison is real (non-empty volume vs empty baseline fails)' },
    @{ id = 'M9'; ac = 'AC-05'; probe = $false; file = 'backend/src/Squid.Api/Crypto/KeyRotationService.cs'; edits = ,@(
            @{ find = 'ring.RotateTo(newKey);'; replace = '// QA-0058 M9 (in-place RuntimeSigningKeyRing swap removed)' }
        ); filter = $fam; expect = @($fRingSwap); why = 'AC-05 runtime half: without the in-place swap the ring keeps the OLD root key and cannot open the re-wrapped 0009 copy - QA-0058 added KeyRotationRingSwapTests precisely because the full 640 gate stayed green without it; this cycle reds the new scenario' }
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
        $editCount = 0
        $okAll = $true
        foreach ($e in $c.edits) {
            $hits = ([regex]::Matches($text, [regex]::Escape($e.find))).Count
            $editCount++
            if ($hits -ne 1 -or $e.find -eq $e.replace) { $okAll = $false }
            $lines += ('VERIFY {0} ac={1} edit={2} hits={3} ok={4}' -f $c.id, $c.ac, $editCount, $hits, ($hits -eq 1 -and $e.find -ne $e.replace).ToString().ToLower())
        }
        if (-not $okAll) { $bad++ }
        $lines += ('VERIFY-CYCLE {0} ac={1} edits={2} ok={3} probe={4}' -f $c.id, $c.ac, $c.edits.Count, $okAll.ToString().ToLower(), $c.probe.ToString().ToLower())
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

    Log ('{0} PRE at={1} ac={2} hash={3} head-match={4} edits={5}' -f $c.id, (Get-Date -Format HHmmss), $c.ac, $pre, ($pre -eq $head).ToString().ToLower(), $c.edits.Count)

    $mutated = $text
    foreach ($e in $c.edits) {
        $mutated = $mutated.Replace($e.find, $e.replace)
    }
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
                Log ('{0} PROBE-GREEN expect=NONE (no KeyRotation scenario fails under this mutation)' -f $c.id)
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
$greenOk = ($green.logExit -eq 0 -and $green.falha -eq 0 -and $green.aprovado -eq 8)
if (-not $greenOk) { $script:failedCycles++ }

# Residue: no MUTATION may survive in the tracked product tree. The only untracked file this
# QA owns under backend is its own can-fail scenario (KeyRotationRingSwapTests.cs) - anything
# else means a mutation leaked.
$status = @(git -C $repo status --porcelain -uno -- backend)
$residue = @($status | Where-Object { $_ }).Count
$untracked = @(git -C $repo status --porcelain -uall -- backend | Where-Object { $_ -match '^\?\?' })
$allowedUntracked = @('?? backend/tests/Squid.Tests/Recovery/KeyRotationRingSwapTests.cs')
$rogueUntracked = @($untracked | Where-Object { $allowedUntracked -notcontains $_ })
if ($residue -ne 0) { $script:failedCycles++ }

$elapsed = [int]$sw.Elapsed.TotalSeconds
Log ('UNTRACKED backend={0} rogue={1} (esperado 0: so o cenario novo da QA-0058)' -f $untracked.Count, $rogueUntracked.Count)
if ($rogueUntracked.Count -ne 0) { $script:failedCycles++ }
Log ('SUMMARY cycles={0} failed={1} residue-backend={2} green-aprovado={3} elapsed-s={4}' -f $cycles.Count, $script:failedCycles, $residue, $green.aprovado, $elapsed)

if ($script:failedCycles -gt 0) { exit 1 }
exit 0
