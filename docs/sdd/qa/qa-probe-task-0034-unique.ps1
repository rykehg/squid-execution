# QA-0034 - UNIQUE(thread_id, seq) differential probe of TASK-0034 (0003_messages.sql is the
# last line of defence behind the CAS of GAP-29: the same number can never be stored twice for a
# thread). No suite test can fail when the index alone disappears (the CAS never offers a
# duplicate), so this probe proves it end-to-end with a built-in can-fail:
#   1. write a TEMPORARY xUnit probe under backend/tests/.../Threads/ (ASCII, deleted at the end)
#   2. run it on the PRISTINE schema -> must PASS (SQLite itself refuses the duplicate row: the
#      pragma shows unique=1 and the INSERT raises SQLITE_CONSTRAINT 19 on message.thread_id, seq)
#   3. mutate 0003_messages.sql: CREATE UNIQUE INDEX -> CREATE INDEX (S-16: SHA256 PRE, count=1)
#   4. run the SAME probe -> must be RED (the duplicate goes in silently = the probe can fail)
#   5. restore 0003 byte-exact from memory + mtime bump (S-23) -> hash = PRE, marker residue 0
#   6. delete the temporary test file; `git status -- backend` must come back empty
# Every exit is judged by the `exit:` header INSIDE the produced log (S-22), never the wrapper.
# Run it AFTER qa-mutate-task-0034.ps1 finishes - both drive dotnet builds over the same tree.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-probe-task-0034-unique.ps1
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$gate = Join-Path $repo 'docs\sdd\_tools\sdd-test.ps1'
$logs = Join-Path $repo 'docs\sdd\runs\test-runs'
$ledger = Join-Path $PSScriptRoot 'QA-0034-unique-probe.log'
$filter = 'FullyQualifiedName~QaTask0034UniqueProbe'
$testFile = Join-Path $repo 'backend\tests\Squid.Tests\Threads\QaTask0034UniqueProbeTests.cs'
$sqlFile = Join-Path $repo 'backend\src\Squid.Persistence\schema\0003_messages.sql'
$uniqueLine = 'CREATE UNIQUE INDEX IF NOT EXISTS ix_message_thread_seq ON message (thread_id, seq);'
$plainLine = 'CREATE INDEX IF NOT EXISTS ix_message_thread_seq ON message (thread_id, seq);'

$probeTest = @'
using Microsoft.Data.Sqlite;
using Squid.Persistence;
using Squid.Service;
using Squid.Tests.Projections;

namespace Squid.Tests.Threads;

/// <summary>
/// QA-0034 probe (TASK-0034): the UNIQUE(thread_id, seq) of 0003_messages.sql is the last line
/// of defence behind the CAS of GAP-29 - the same number can never be stored twice for a thread.
/// On the shipped schema this test PASSES (the duplicate is refused); the driver then removes
/// the UNIQUE from 0003 and runs this same test again, where it must FAIL.
/// Written by docs/sdd/qa/qa-probe-task-0034-unique.ps1 and deleted by it - not a repo file.
/// </summary>
public sealed class QaTask0034UniqueProbeTests
{
    [Fact]
    public async Task The_unique_of_thread_and_seq_refuses_a_duplicate_row()
    {
        var file = ProjectionDb.NewFile();
        try
        {
            await SchemaMigrator.ApplyAsync(ProjectionDb.ConnectionOf(file), SchemaMigrations.Load());

            await using (var connection = new SqliteConnection(ProjectionDb.ConnectionOf(file)))
            {
                await connection.OpenAsync();
                await using var seed = connection.CreateCommand();
                seed.CommandText = """
                    INSERT INTO message (id, thread_id, author_actor_id, content, seq, created_at)
                    VALUES ('m/1', 't/probe', 'u/a', 'primeira', 1, '2026-11-12T00:00:00.0000000+00:00');
                    """;
                await seed.ExecuteNonQueryAsync();

                // The index exists AND SQLite reports it unique - the flag, never the script text.
                await using var pragma = connection.CreateCommand();
                pragma.CommandText = "PRAGMA index_list('message')";
                var seen = false;
                await using (var reader = await pragma.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        if (string.Equals(reader.GetString(1), "ix_message_thread_seq",
                                StringComparison.Ordinal))
                        {
                            seen = true;
                            Assert.Equal(1L, reader.GetInt64(2)); // 1 = UNIQUE
                        }
                    }
                }
                Assert.True(seen, "ix_message_thread_seq nao existe na tabela message");

                // The duplicate number is refused: SQLITE_CONSTRAINT (19) naming both columns.
                await using var dup = connection.CreateCommand();
                dup.CommandText = """
                    INSERT INTO message (id, thread_id, author_actor_id, content, seq, created_at)
                    VALUES ('m/2', 't/probe', 'u/b', 'duplicada', 1, '2026-11-12T00:00:01.0000000+00:00');
                    """;
                var error = await Assert.ThrowsAsync<SqliteException>(
                    () => dup.ExecuteNonQueryAsync());
                Assert.Equal(19, error.SqliteErrorCode);
                Assert.Contains("message.thread_id, message.seq", error.Message,
                    StringComparison.Ordinal);
            }
        }
        finally
        {
            ProjectionDb.TryDelete(file);
        }
    }
}
'@

function Write-Log([string]$Line) {
    Add-Content -LiteralPath $ledger -Value $Line -Encoding utf8
    Write-Output $Line
}

function Get-Hash([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

# One probe run over the gate; the verdict comes from the log the run wrote (S-22).
function Invoke-Probe([string]$Label) {
    $before = (Get-ChildItem -LiteralPath $logs -Filter '*.log' | Sort-Object LastWriteTime -Descending | Select-Object -First 1).LastWriteTime
    $null = & powershell -NoProfile -ExecutionPolicy Bypass -File $gate test -TestFilter $filter 2>&1 | Out-String
    $code = $LASTEXITCODE
    $log = Get-ChildItem -LiteralPath $logs -Filter '*.log' |
        Where-Object { $_.LastWriteTime -gt $before } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -eq $log) {
        [pscustomobject]@{ Label = $Label; Wrapper = $code; Log = '(nenhum log novo)'; LogExit = $null; Failed = -1; Approved = -1; Failing = '' }
        return
    }
    $text = Get-Content -LiteralPath $log.FullName -Raw
    $logExit = if ($text -match '(?m)^exit:\s+(\d+)') { [int]$Matches[1] } else { $null }
    $failed = -1
    $approved = -1
    if ($text -match 'Com falha:\s*(\d+),\s*Aprovado:\s*(\d+)') {
        $failed = [int]$Matches[1]; $approved = [int]$Matches[2]
    }
    $failing = @()
    foreach ($line in ($text -split "`n")) {
        if ($line -match '\[FAIL\]') { $failing += ($line.Trim() -replace '\s+', ' ') }
    }
    [pscustomobject]@{
        Label   = $Label
        Wrapper = $code
        Log     = $log.FullName
        LogExit = $logExit
        Failed  = $failed
        Approved = $approved
        Failing = (($failing | Select-Object -First 3) -join ' | ')
    }
}

Set-Content -LiteralPath $ledger -Value "# QA-0034 UNIQUE probe - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -Encoding utf8
$failures = @()

# 0. Preconditions: no leftover probe file, the UNIQUE line appears exactly once in 0003.
if (Test-Path -LiteralPath $testFile) {
    Write-Log 'PRECONDITION FAIL: QaTask0034UniqueProbeTests.cs ja existe - nada executado'
    exit 1
}
$sqlText = [System.IO.File]::ReadAllText($sqlFile)
$uniqueHits = ([regex]::Matches($sqlText, [regex]::Escape($uniqueLine))).Count
if ($uniqueHits -ne 1) {
    Write-Log ("PRECONDITION FAIL: linha UNIQUE em 0003 encontrada {0}x (esperado 1) - nada executado" -f $uniqueHits)
    exit 1
}

# 1. The temporary probe test (deleted in step 5, whatever happens before).
[System.IO.File]::WriteAllText($testFile, $probeTest, (New-Object System.Text.UTF8Encoding($false)))
Write-Log ("TEST-FILE written ({0} bytes)" -f (Get-Item -LiteralPath $testFile).Length)

# 2. Pristine schema: the probe must PASS (the duplicate row is refused).
$pristine = Invoke-Probe 'PRISTINE'
Write-Log ("PRISTINE wrapper={0} logExit={1} aprovado={2} falha={3} log={4}" -f `
    $pristine.Wrapper, $pristine.LogExit, $pristine.Approved, $pristine.Failed, (Split-Path $pristine.Log -Leaf))
if ($pristine.Failing) { Write-Log ("PRISTINE RED {0}" -f $pristine.Failing) }
if ($pristine.LogExit -ne 0 -or $pristine.Failed -ne 0) { $failures += 'PRISTINE-not-green' }

# 3. Can-fail: drop the UNIQUE from 0003 (S-16 - hash first, pattern count 1).
$bytes = [System.IO.File]::ReadAllBytes($sqlFile)
$pre = Get-Hash $sqlFile
$mutated = $sqlText.Replace($uniqueLine, $plainLine + ' -- QA-MUTATION-PROBE')
[System.IO.File]::WriteAllText($sqlFile, $mutated, (New-Object System.Text.UTF8Encoding($false)))
Write-Log ("0003-MUTATE PRE {0} | UNIQUE -> plain INDEX (can-fail)" -f $pre.Substring(0, 12))

$mutatedRun = Invoke-Probe 'MUTATED'
Write-Log ("MUTATED wrapper={0} logExit={1} aprovado={2} falha={3} log={4}" -f `
    $mutatedRun.Wrapper, $mutatedRun.LogExit, $mutatedRun.Approved, $mutatedRun.Failed, (Split-Path $mutatedRun.Log -Leaf))
if ($mutatedRun.Failing) { Write-Log ("MUTATED RED {0}" -f $mutatedRun.Failing) }
if ($mutatedRun.Failed -lt 1) { $failures += 'MUTATED-not-red' }

# 4. Restore byte-exact + mtime bump (S-23), then hash must equal PRE and no marker may remain.
[System.IO.File]::WriteAllBytes($sqlFile, $bytes)
(Get-Item -LiteralPath $sqlFile).LastWriteTime = Get-Date
$post = Get-Hash $sqlFile
$residue = ([regex]::Matches([System.IO.File]::ReadAllText($sqlFile), 'QA-MUTATION')).Count
if ($post -ne $pre -or $residue -ne 0) {
    Write-Log ("RESTORE-FAIL hash={0} residue={1}" -f $post.Substring(0, 12), $residue)
    $failures += 'RESTORE'
} else {
    Write-Log 'RESTORE-OK hash=PRE residue=0'
}

# 5. The temporary test file leaves, and the backend comes back to a clean git.
Remove-Item -LiteralPath $testFile -Force
if (Test-Path -LiteralPath $testFile) { $failures += 'TEST-FILE-not-deleted' }
$status = @(git -C $repo status --porcelain -uall -- backend)
if ($status.Count -ne 0) { $failures += "GIT-BACKEND($($status.Count))" }
Write-Log ("GIT-BACKEND entradas={0}" -f $status.Count)

Write-Log ("SUMMARY pristine={0} mutated={1} restore={2} testfile-removed={3} git-backend={4} fails={5}" -f `
    $(if ($pristine.LogExit -eq 0 -and $pristine.Failed -eq 0) { 'green' } else { 'red' }),
    $(if ($mutatedRun.Failed -ge 1) { 'red' } else { 'green' }),
    $(if ($post -eq $pre -and $residue -eq 0) { 'OK' } else { 'FAIL' }),
    $(if (Test-Path -LiteralPath $testFile) { 'no' } else { 'yes' }),
    $status.Count, $failures.Count)

if ($failures.Count -gt 0) { exit 1 }
exit 0
