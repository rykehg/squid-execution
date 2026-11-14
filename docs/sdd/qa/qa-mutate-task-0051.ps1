# QA-0051 can-fail mutation driver (TASK-0051 - double wrap: registration persists only the
# instance-wrapped copy of the actor RSA in 0009_*, activation opens THAT copy, kit untouched,
# no clear copy anywhere, no-copy output disabled with an auditable warning). Seven cycles over
# the filter FullyQualifiedName~DoubleWrapTests (the 7 DEV facts of the AC-01..AC-05
# scenarios): each cycle edits ONE literal of PRODUCT code, runs the filter, classifies the RED
# from the LOG FILE (S-22 - never from the wrapper console code), checks that the AC's OWN
# scenario failed, restores with git checkout (byte-exact against the index, hash compared PRE
# vs POST) and bumps the mtime (S-23). A green run over the restored tree closes the ledger.
# ASCII-only on purpose: this file runs without a UTF-8 BOM under Windows PowerShell 5.1, and
# no find string below may contain a non-ASCII byte (the sources around them do, in comments).
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0051.ps1 [-VerifyOnly]
[CmdletBinding()]
param([switch]$VerifyOnly)

$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$ledgerPath = Join-Path $PSScriptRoot 'QA-0051-mutations.log'
$verifyPath = Join-Path $PSScriptRoot 'QA-0051-verify.log'
$sddTest = Join-Path $repo 'docs\sdd\_tools\sdd-test.ps1'
$filter = 'FullyQualifiedName~DoubleWrapTests'

# The scenario of each AC, by method name (DoubleWrapTests.cs = DEV).
$fAc01w = 'Ac01_with_a_secret_the_registration_persists_only_the_wrapped_instance_copy'
$fAc01n = 'Ac01_without_the_secret_registration_births_no_copy_and_answers_as_before'
$fAc02  = 'Ac02_a_registered_actor_federates_signed_and_an_independent_verifier_accepts'
$fAc03  = 'Ac03_the_kit_path_is_untouched_and_a_restore_devolves_the_same_identity'
$fAc04c = 'Ac04_the_copy_never_leaves_the_table_and_no_route_or_log_carries_it'
$fAc04n = 'Ac04_without_the_secret_activation_stays_auditably_disabled'
$fAc05  = 'Ac05_without_a_copy_the_output_stays_disabled_while_a_later_actor_federates'

$cycles = @(
    @{ id = 'T1'; ac = 'AC-01'; file = 'backend\src\Squid.Api\Auth\AuthEndpoints.cs'; find = 'await instanceCopies.SaveAsync(handle, Convert.ToBase64String(instanceCopy));'; replace = 'await Task.CompletedTask; // QA-0051 T1 the copy is never persisted'; expect = @($fAc01w, $fAc02, $fAc04c, $fAc05); why = 'registration PERSISTS the wrapped copy in 0009: without the row no copy is born and every dependent AC loses its material (AC-01)' },
    @{ id = 'T2'; ac = 'AC-02'; file = 'backend\src\Squid.Api\Crypto\RuntimeSigningKeyRing.cs'; find = 'return KeyDerivation.UnwrapRsaPrivateKey(_rootKey, Convert.FromBase64String(wrapped));'; replace = 'return RSA.Create(2048); // QA-0051 T2 activation hands out a foreign key'; expect = @($fAc02); why = 'activation hands out the ACTOR key opened from the copy: a foreign RSA makes the independent verifier reject the wire signature (AC-02)' },
    @{ id = 'T3'; ac = 'AC-03'; file = 'backend\src\Squid.Api\Crypto\KeyState.cs'; find = 'WrappedRsaPrivateKey = KeyDerivation.WrapRsaPrivateKey(rootKey, rsa),'; replace = 'WrappedRsaPrivateKey = KeyDerivation.WrapRsaPrivateKey(RootKey.Create(), rsa), // QA-0051 T3 kit under a foreign RK'; expect = @($fAc03); why = 'the KIT wrap still uses the actors own RK: wrapping it under another root key breaks restore (AC-03, GAP-01)' },
    @{ id = 'T4'; ac = 'AC-04'; file = 'backend\src\Squid.Api\Crypto\KeyState.cs'; find = 'InstanceWrappedRsaPrivateKey = instanceKeys?.WrapInstanceCopy(rsa),'; replace = 'InstanceWrappedRsaPrivateKey = instanceKeys is null ? null : rsa.ExportPkcs8PrivateKey(), // QA-0051 T4 clear copy'; expect = @($fAc01w, $fAc02, $fAc04c, $fAc05); why = 'the table holds ONLY wrapped material: a clear private key in the copy breaks every opener and is exactly what AC-04 forbids' },
    @{ id = 'T5'; ac = 'AC-04'; file = 'backend\src\Squid.Api\Auth\AuthEndpoints.cs'; find = 'encryptedRootKey = account.Kit!.EncryptedRootKey,'; replace = 'encryptedRootKey = account.Kit!.EncryptedRootKey, instanceCopy = Convert.ToBase64String(keys.InstanceWrappedRsaPrivateKey ?? Array.Empty<byte>()), // QA-0051 T5'; expect = @($fAc04c); why = 'no route serves the copy: leaking it into the register response trips the DoesNotContain over the wire body (AC-04)' },
    @{ id = 'T6'; ac = 'AC-04'; file = 'backend\src\Squid.Api\Crypto\RuntimeSigningKeyRing.cs'; find = 'return null; // nothing configured at all: output disabled, no lookup, nothing attempted'; replace = 'return RSA.Create(2048); // QA-0051 T6 a missing secret still activates'; expect = @($fAc04n); why = 'without Squid:RecoveryKey at boot the activation returns null auditably: a signer here defeats the disabled state of TASK-0042 AC-01/03 (AC-04)' },
    @{ id = 'T7'; ac = 'AC-05'; file = 'backend\src\Squid.Service\OutboundPipeline.cs'; find = 'absent at boot, or no instance signing copy for this actor'; replace = 'absent at boot QA-0051 T7'; expect = @($fAc05); why = 'the held output of an actor WITHOUT a copy is auditable: gutting the warning tail the assertion reads (instance signing copy) leaves the console without any record of which actor never leaves (AC-05)' }
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

# Residue: no MUTATION may survive in the tracked product tree. This QA added no test file
# (the scenarios of the ACs are the DEV's own), so the expected untracked count in backend
# is ZERO.
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
