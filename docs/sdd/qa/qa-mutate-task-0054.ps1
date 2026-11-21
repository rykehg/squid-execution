# QA-0054 can-fail mutation driver (TASK-0054 - encrypted backup core: 0010 migration with
# key_epoch NOT NULL, IBackupTarget/LocalDiskTarget over Backup:Local:Path, and the pipeline
# order cifra -> hash -> envio -> reler -> verifiedAt with Corrupt on divergence). Eight cycles
# over the filter FullyQualifiedName~Squid.Tests.Backup (one per AC scenario of AC-01..AC-06;
# AC-07 is the gate + scope scan, not a product literal): each cycle edits ONE literal of
# PRODUCT code, runs the filter, classifies the RED from the LOG FILE (S-22 - never from the
# wrapper console code), checks that the AC's OWN scenario failed, restores with git checkout
# (byte-exact against the index, hash compared PRE vs POST) and bumps the mtime (S-23). A green
# run over the restored tree closes the ledger. ASCII-only on purpose: this file runs without a
# UTF-8 BOM under Windows PowerShell 5.1, and no find string below may contain a non-ASCII byte.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0054.ps1 [-VerifyOnly]
[CmdletBinding()]
param([switch]$VerifyOnly)

$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$ledgerPath = Join-Path $PSScriptRoot 'QA-0054-mutations.log'
$verifyPath = Join-Path $PSScriptRoot 'QA-0054-verify.log'
$sddTest = Join-Path $repo 'docs\sdd\_tools\sdd-test.ps1'
$filter = 'FullyQualifiedName~Squid.Tests.Backup'

# The scenario of each AC, by method name (Backup*.cs / LocalDiskTargetTests.cs = DEV).
$fAc01  = 'Ac01_The_0010_version_creates_both_2_10_tables_and_reapplying_changes_nothing'
$fAc02t = 'Ac02_The_five_operations_run_over_the_configured_volume'
$fAc02w = 'Ac02_The_registered_target_operates_over_the_configured_backup_local_path'
$fAc03  = 'Ac03_The_destination_gets_ciphertext_and_header_never_a_plaintext_or_key_byte'
$fAc04o = 'Ac04_The_order_is_hash_then_send_then_reread_and_a_divergence_is_corrupt'
$fAc04n = 'Ac04_Without_a_surviving_reread_nothing_is_verified_or_registered'
$fAc046 = 'Ac04_ac06_Verified_run_sets_verifiedAt_and_registers_the_section_2_10_row'
$fAc05p = 'Ac05_Each_backup_wraps_its_own_dk_and_both_ciphertexts_decrypt_with_the_same_rk'

$cycles = @(
    @{ id = 'T1'; ac = 'AC-01'; file = 'backend\src\Squid.Persistence\schema\0010_backup.sql'; find = 'key_epoch    INTEGER NOT NULL,'; replace = 'key_epoch    INTEGER, -- QA-0054 T1'; expect = @($fAc01); why = 'key_epoch is NOT NULL in the migration itself: dropping the constraint lets an envelope row without an epoch be written and the AC-01 refusal assertion never fires' },
    @{ id = 'T2'; ac = 'AC-02'; file = 'backend\src\Squid.Worker\Backup\LocalDiskTarget.cs'; find = 'if (envelope.SeqTo >= fromSeq)'; replace = 'if (envelope.SeqTo >= 0 /* QA-0054 T2 */)'; expect = @($fAc02t); why = 'ListAsync must filter by fromSeq (contract section 1 discovery): ignoring fromSeq offers envelopes the caller excluded' },
    @{ id = 'T2w'; ac = 'AC-02'; file = 'backend\src\Squid.Service\SquidServiceHost.cs'; find = 'configuration["Backup:Local:Path"]'; replace = 'configuration["Backup:Local:PathQa"] /* QA-0054 T2w */'; expect = @($fAc02w); why = 'the registered target must operate over the configured Backup:Local:Path: ignoring the key writes to the default folder instead of the configured volume' },
    @{ id = 'T3'; ac = 'AC-03'; file = 'backend\src\Squid.Worker\Backup\BackupPipeline.cs'; find = 'await using (var source = new MemoryStream(blob, writable: false))'; replace = 'await using (var source = new MemoryStream(payload, writable: false)) /* QA-0054 T3 */'; expect = @($fAc03); why = 'the destination must receive ONLY ciphertext: sending the serialized payload puts the plaintext sentinel bytes on the test volume' },
    @{ id = 'T4a'; ac = 'AC-04'; file = 'backend\src\Squid.Worker\Backup\BackupPipeline.cs'; find = 'var contentHash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(blob));'; replace = 'var contentHash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(compressed)); // QA-0054 T4a'; expect = @($fAc046); why = 'contentHash must be the SHA-256 of the CIPHERTEXT: hashing the pre-encryption bytes diverges from the stored .bin and from the re-read' },
    @{ id = 'T4b'; ac = 'AC-04'; file = 'backend\src\Squid.Worker\Backup\BackupPipeline.cs'; find = 'if (string.Equals(rereadHash, envelope.ContentHash, StringComparison.OrdinalIgnoreCase))'; replace = 'if (string.Equals(rereadHash, rereadHash, StringComparison.OrdinalIgnoreCase)) // QA-0054 T4b'; expect = @($fAc04o); why = 'a divergent re-read must leave Corrupt with verifiedAt null: an always-true comparison marks the tampered backup Verified' },
    @{ id = 'T5'; ac = 'AC-05'; file = 'backend\src\Squid.Worker\Backup\BackupPipeline.cs'; find = 'var wrappedDek = KeyDerivation.WrapBackupKey(rootKey, dataKey);'; replace = 'var wrappedDek = KeyDerivation.WrapBackupKey(rootKey, new byte[DataKeySize]); // QA-0054 T5'; expect = @($fAc05p); why = 'the header must wrap the SAME DK that encrypted the payload: a foreign box means no backup opens with the RK' },
    @{ id = 'T6'; ac = 'AC-06'; file = 'backend\src\Squid.Worker\Backup\SqliteBackupEnvelopeStore.cs'; find = 'insert.Parameters.AddWithValue("@KeyEpoch", envelope.KeyEpoch);'; replace = 'insert.Parameters.AddWithValue("@KeyEpoch", 0); // QA-0054 T6'; expect = @($fAc046); why = 'the persisted row must carry the envelope KeyEpoch of data-model section 2.10: a zeroed epoch fails the row assertion' }
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

# The wiring mutation (T2w) writes the envelope into the default folder beside the test
# binaries (that IS the failure): remove it so no side effect survives this run.
$defaultVolume = Join-Path $repo 'backend\tests\Squid.Tests\bin\Debug\net10.0\backups'
if (Test-Path -LiteralPath $defaultVolume) {
    try {
        Remove-Item -LiteralPath $defaultVolume -Recurse -Force -ErrorAction Stop
        Log ('CLEANUP default-backups-folder removed')
    }
    catch {
        Log ('CLEANUP-FAIL default-backups-folder: ' + $_.Exception.Message)
        $script:failedCycles++
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
