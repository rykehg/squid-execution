# QA-0037 - mutation driver of TASK-0037 (replyToId: same-thread reference only, 422
# invalid_reply_target AFTER the 428 gate inside the append transaction, refusal burns no seq and
# writes no row, envelope carries the reference or null, outsider keeps the byte-identical 404 even
# with a replyToId in the POST, migration 0005 idempotent through the SchemaMigrator ledger).
# Ten cycles, one acceptance behaviour each: SHA256 PRE (S-16) -> a single QA-MUTATION edit
# (pattern count must equal `expected`, checked BEFORE the run) -> filtered red run ->
# classification INSIDE the log file (S-22, never the wrapper's console exit) -> byte-exact restore
# from memory + mtime bump (S-23) -> hash = PRE -> marker residue 0.
# After the ten: one green filtered run over the restored sources.
# Every file mutated is inside the task's touches (Squid.Api/Threads/**, Squid.Service/**,
# Squid.Persistence/schema/**); no product file keeps a byte of this driver after the run.
# Multiline patterns are matched in LF space and written back in the file's own line endings, so
# CRLF sources stay byte-consistent (the four targets are LF today). THIS FILE MUST KEEP ITS UTF-8
# BOM (QA-0034/0035/0036 lesson: a no-BOM edit turns every accented pattern into ANSI and
# PATTERN-COUNT dies at hits=0).
# Targets: M1 a cross-thread target accepted instead of 422 (AC-02); M2 a target that does not
# exist accepted instead of 422 (AC-02); M3 the 428 gate skipped when the send carries a replyToId
# - inverts the documented order 428-before-422 (contract 6 absolute clause); M4 a refusal that
# BURNS a sequence number (commit of a bump before the InvalidReplyTarget return); M5 the
# reference never PERSISTED (reply_to_id always written NULL); M6 the envelope never CARRIES the
# reference (MessageView replyToId null); M7 the 422 becoming the catalog neighbour 409
# version_conflict (status + word of 9.9); M8 the participant probe off in the SEND route (the
# 404 no longer fires before validation, AC-03); M9 the shared 404 carrying traceId (byte-identity
# breaks, AC-03); M10 the SchemaMigrator ledger skip removed (0005 re-applied -> duplicate column:
# the idempotence of migration 0005 has no other guard).
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0037.ps1 [-Only M1] [-VerifyOnly]
param([string[]]$Only = @(), [switch]$VerifyOnly)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$gate = Join-Path $repo 'docs\sdd\_tools\sdd-test.ps1'
$logs = Join-Path $repo 'docs\sdd\runs\test-runs'
$out = Join-Path $PSScriptRoot 'QA-0037-mutations.log'
$filter = 'FullyQualifiedName~Squid.Tests.Threads'

$cycles = @(
    # AC-02 - a message of ANOTHER thread must be refused with 422 inside the append attempt: this
    # mutant drops the refusal, so the foreign target is stored like a same-thread one.
    @{ id = 'M1'; file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
       old = (@(
              '                    if (!string.Equals(owner as string, threadId, StringComparison.Ordinal))',
              '                    {',
              '                        return new AppendResult(AppendOutcome.InvalidReplyTarget, null);',
              '                    }'
             ) -join "`n")
       new = (@(
              '                    if (!string.Equals(owner as string, threadId, StringComparison.Ordinal))',
              '                    {',
              '                        // QA-MUTATION-M1: alvo de OUTRA thread aceito em vez de 422',
              '                    }'
             ) -join "`n")
       why = 'AC-02: alvo de OUTRA thread aceito (200 + linha gravada em vez de 422) - ThreadReplyRoutesTests + ThreadStoreTests vermelhas' }
    # AC-02 - the "does not exist" half of 9.9 shares ONE lookup with the cross-thread half: this
    # mutant lets a NULL owner answer as if it were this very thread, so a ghost target lands.
    @{ id = 'M2'; file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
       old = (@(
              '                    var owner = await target.ExecuteScalarAsync();',
              '                    if (!string.Equals(owner as string, threadId, StringComparison.Ordinal))'
             ) -join "`n")
       new = (@(
              '                    var owner = await target.ExecuteScalarAsync();',
              '                    if (!string.Equals(owner as string ?? threadId, threadId, StringComparison.Ordinal)) // QA-MUTATION-M2'
             ) -join "`n")
       why = 'AC-02: mensagem INEXISTENTE aceita em vez de 422 (só o fantasma passa) - ThreadReplyRoutesTests + ThreadStoreTests vermelhas' }
    # The ladder: contract section 6 makes 428 absolute for every send, so a send missing BOTH the
    # acceptance and the target answers 428. This mutant exempts a send that carries a replyToId
    # from the gate, so the 422 wins - the documented order inverts.
    @{ id = 'M3'; file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
       old = (@(
              '                if (!accepted)',
              '                {',
              '                    return new AppendResult(AppendOutcome.AckRequired, null);',
              '                }'
             ) -join "`n")
       new = (@(
              '                if (!accepted && replyToId is null) // QA-MUTATION-M3: 428 so sem alvo - ordem 422 antes de 428',
              '                {',
              '                    return new AppendResult(AppendOutcome.AckRequired, null);',
              '                }'
             ) -join "`n")
       why = 'ordem da escada: sem aceite E com alvo ruim vira 422 em vez de 428 (contrato 6) - ThreadReplyRoutesTests + ThreadStoreTests vermelhas' }
    # The refusal must write NO row and burn NO sequence number: the check lives inside the
    # attempt before the CAS. This mutant commits a bump first, so the next real send skips a
    # number and the store reads message_seq != 0 after two refusals.
    @{ id = 'M4'; file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
       old = (@(
              '                    if (!string.Equals(owner as string, threadId, StringComparison.Ordinal))',
              '                    {',
              '                        return new AppendResult(AppendOutcome.InvalidReplyTarget, null);',
              '                    }'
             ) -join "`n")
       new = (@(
              '                    if (!string.Equals(owner as string, threadId, StringComparison.Ordinal))',
              '                    {',
              '                        await using (var burn = connection.CreateCommand())',
              '                        {',
              '                            burn.Transaction = transaction;',
              '                            burn.CommandText =',
              '                                "UPDATE thread SET message_seq = message_seq + 1, version = version + 1 WHERE id = @Id";',
              '                            burn.Parameters.AddWithValue("@Id", threadId);',
              '                            await burn.ExecuteNonQueryAsync();',
              '                        }',
              '',
              '                        await transaction.CommitAsync(); // QA-MUTATION-M4',
              '                        return new AppendResult(AppendOutcome.InvalidReplyTarget, null);',
              '                    }'
             ) -join "`n")
       why = 'AC-02: recusa que QUEIMA numero - message_seq avanca no 422 e a resposta seguinte nasce em seq errado - ThreadStoreTests + ThreadReplyRoutesTests vermelhas' }
    # AC-01 - the reference must be PERSISTED: reply_to_id is what ListMessagesAsync (and the
    # read envelope through it) returns, so writing NULL buries the reference at the source.
    @{ id = 'M5'; file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
       old = '                insert.Parameters.AddWithValue("@ReplyToId", (object?)replyToId ?? DBNull.Value);'
       new = '                insert.Parameters.AddWithValue("@ReplyToId", DBNull.Value); // QA-MUTATION-M5'
       why = 'AC-01: reply_to_id sempre gravado NULL - o historico e o envelope devolvem null - ThreadStoreTests + ThreadReplyRoutesTests vermelhas' }
    # AC-01 - the wire shape itself: MessageView is the 200 body AND every item of the delta, so
    # nulling the field hides a stored reference from the reader.
    @{ id = 'M6'; file = 'backend\src\Squid.Api\Threads\MessageView.cs'; expected = 1
       old = '        replyToId = message.ReplyToId,'
       new = '        replyToId = (string?)null, // QA-MUTATION-M6: envelope nunca devolve a referencia'
       why = 'AC-01: envelope/200 devolvem replyToId null mesmo com resposta gravada - ThreadReplyRoutesTests vermelha' }
    # AC-02 - section 9.9 names the answer: 422 invalid_reply_target. This mutant answers with a
    # catalog neighbour, so both the status and the word of the contract fall.
    @{ id = 'M7'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
       old = (@(
              '            AppendOutcome.InvalidReplyTarget => AuthErrors.Write(context,',
              '                StatusCodes.Status422UnprocessableEntity, "invalid_reply_target",',
              '                "replyToId deve apontar para uma mensagem desta mesma thread (FR-057, §9.9)."),'
             ) -join "`n")
       new = (@(
              '            AppendOutcome.InvalidReplyTarget => AuthErrors.Write(context,',
              '                StatusCodes.Status409Conflict, "version_conflict", // QA-MUTATION-M7',
              '                "replyToId deve apontar para uma mensagem desta mesma thread (FR-057, §9.9)."),'
             ) -join "`n")
       why = 'AC-02: 422 invalid_reply_target vira 409 version_conflict (status e palavra do contrato caem) - ThreadReplyRoutesTests vermelha' }
    # AC-03 / FR-057 - the participant probe decides the 404 BEFORE anything else is learned, so
    # the replyToId in the POST of a stranger can never reveal which case it was. This mutant
    # leaves the probe body empty: the stranger falls through to the append and gets 428 instead.
    @{ id = 'M8'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
       old = (@(
              '        var actorId = ContainerEndpoints.ActorId(context, current.Handle);',
              '        if (!await store.IsParticipantAsync(id, actorId))',
              '        {',
              '            return HiddenThread(context);',
              '        }',
              '',
              '        // data-model §3.1 (the rule Posts enforces on publish and reply): a suspended actor does'
             ) -join "`n")
       new = (@(
              '        var actorId = ContainerEndpoints.ActorId(context, current.Handle);',
              '        if (!await store.IsParticipantAsync(id, actorId))',
              '        {',
              '            // QA-MUTATION-M8: rota de envio sem o 404 de FR-031 - estrangeiro cai na escada',
              '        }',
              '',
              '        // data-model §3.1 (the rule Posts enforces on publish and reply): a suspended actor does'
             ) -join "`n")
       why = 'AC-03: sonda de participante desligada na rota de envio - o POST do estrangeiro com replyToId vira 428 em vez do 404 identico - ThreadReplyRoutesTests vermelha' }
    # AC-03 / SC-005 - the shared 404 answers without traceId so the four refusals stay
    # byte-identical (GET/POST of the real thread x GET/POST of the absent one).
    @{ id = 'M9'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
       old = (@(
              '    private static IResult HiddenThread(HttpContext context) =>',
              '        AuthErrors.Write(context, StatusCodes.Status404NotFound,',
              '            "not_found", "thread não encontrada.", withTraceId: false);'
             ) -join "`n")
       new = (@(
              '    private static IResult HiddenThread(HttpContext context) =>',
              '        AuthErrors.Write(context, StatusCodes.Status404NotFound,',
              '            "not_found", "thread não encontrada.", withTraceId: true); // QA-MUTATION-M9'
             ) -join "`n")
       why = 'AC-03: 404 com traceId - a igualdade byte-a-byte das 4 recusas cai - ThreadReplyRoutesTests vermelha' }
    # Migration 0005 idempotence: SQLite has no IF NOT EXISTS on ALTER TABLE, so the ONLY guard is
    # the SchemaMigrator ledger skip. This mutant removes it: the re-apply re-runs the ALTER and
    # the duplicate column kills the second ApplyAsync.
    @{ id = 'M10'; file = 'backend\src\Squid.Persistence\schema\SchemaMigrator.cs'; expected = 1
       old = '                continue; // already applied: the second run of the gate''s idempotence check'
       new = '                // QA-MUTATION-M10: guarda do ledger desligada - a re-aplicacao roda 0005 de novo'
       why = 'migration 0005: sem o ledger a re-aplicacao re-executa o ALTER TABLE e estoura duplicate column - ThreadMigrationTests vermelha' }
)

function Write-Log([string]$Line) {
    Add-Content -LiteralPath $out -Value $Line -Encoding utf8
    Write-Output $Line
}

function Get-Hash([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

# Read a source in LF space (multiline patterns are authored with LF) and remember whether the
# file itself is CRLF, so the mutated write keeps the original line endings.
function Read-Lf([string]$Path) {
    $raw = [System.IO.File]::ReadAllText($Path)
    $script:isCrlf = $raw.Contains("`r`n")
    return ($raw -replace "`r`n", "`n")
}

function Invoke-ModRun([string]$Label) {
    $before = (Get-ChildItem -LiteralPath $logs -Filter '*.log' | Sort-Object LastWriteTime -Descending | Select-Object -First 1).LastWriteTime
    # The filter rides as one argv token (no spaces, so no quoting needed) - the same way the
    # DEV's own filtered run passed it.
    $null = & powershell -NoProfile -ExecutionPolicy Bypass -File $gate test -TestFilter $filter 2>&1 | Out-String
    $code = $LASTEXITCODE
    $log = Get-ChildItem -LiteralPath $logs -Filter '*.log' |
        Where-Object { $_.LastWriteTime -gt $before } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -eq $log) {
        [pscustomobject]@{ Label = $Label; Wrapper = $code; Log = '(nenhum log novo)'; LogExit = $null; Failed = -1; Approved = -1; Failing = ''; Constraint = 'no' }
        return
    }
    $text = Get-Content -LiteralPath $log.FullName -Raw
    $logExit = if ($text -match '(?m)^exit:\s+(\d+)') { [int]$Matches[1] } else { $null }
    $failed = 0
    $approved = 0
    if ($text -match 'Com falha:\s*(\d+),\s*Aprovado:\s*(\d+)') {
        $failed = [int]$Matches[1]; $approved = [int]$Matches[2]
    }
    $failing = @()
    foreach ($line in ($text -split "`n")) {
        if ($line -match '\[FAIL\]' -or $line -match '(?i)com falha Squid\.Tests' -or $line -match '(?i)^\s*(falha|failed)\s+Squid\.Tests') {
            $failing += ($line.Trim() -replace '\s+', ' ')
        }
    }
    $constraint = if ($text -match 'UNIQUE constraint failed|duplicate column name') { 'yes' } else { 'no' }
    [pscustomobject]@{
        Label     = $Label
        Wrapper   = $code
        Log       = $log.FullName
        LogExit   = $logExit
        Failed    = $failed
        Approved  = $approved
        Failing   = (($failing | Select-Object -First 8) -join ' | ')
        Constraint = $constraint
    }
}

# -VerifyOnly: prove every pattern count against the pristine sources, run nothing.
if ($VerifyOnly) {
    $bad = 0
    foreach ($cycle in $cycles) {
        $path = Join-Path $repo $cycle.file
        $text = Read-Lf $path
        $old = ($cycle.old -replace "`r`n", "`n")
        $hits = ([regex]::Matches($text, [regex]::Escape($old))).Count
        $ok = ($hits -eq [int]$cycle.expected)
        if (-not $ok) { $bad++ }
        Write-Output ("{0} hits={1} expected={2} {3} {4}" -f $cycle.id, $hits, $cycle.expected, $(if ($ok) { 'OK' } else { 'FAIL' }), $cycle.file)
    }
    $bom = [System.IO.File]::ReadAllBytes($PSCommandPath)[0..2]
    $bomOk = ($bom[0] -eq 0xEF -and $bom[1] -eq 0xBB -and $bom[2] -eq 0xBF)
    Write-Output ("BOM {0}" -f $(if ($bomOk) { 'OK (UTF-8 BOM presente)' } else { 'FALTA - editar antes de rodar' }))
    Write-Output "VERIFY total=$($cycles.Count) bad=$bad"
    if ($bad -gt 0 -or -not $bomOk) { exit 1 }
    exit 0
}

# A selective re-run appends to the ledger; a full run starts it fresh.
if ($Only.Count -gt 0) {
    Write-Log "## re-execucao seletiva ($($Only -join ',')) - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
} else {
    Set-Content -LiteralPath $out -Value "# QA-0037 mutations - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -Encoding utf8
}
$failedCycles = @()
$selected = @(if ($Only.Count -gt 0) { $cycles | Where-Object { $Only -contains $_.id } } else { $cycles })

foreach ($cycle in $selected) {
    $path = Join-Path $repo $cycle.file
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $text = Read-Lf $path
    $old = ($cycle.old -replace "`r`n", "`n")
    $new = ($cycle.new -replace "`r`n", "`n")
    $expected = if ($cycle.ContainsKey('expected')) { [int]$cycle.expected } else { 1 }
    $hits = ([regex]::Matches($text, [regex]::Escape($old))).Count
    if ($hits -ne $expected) {
        Write-Log ("{0} PATTERN-COUNT={1} (esperado {2}) em {3} - ciclo abortado" -f $cycle.id, $hits, $expected, $cycle.file)
        $failedCycles += $cycle.id
        continue
    }

    $pre = Get-Hash $path
    $mutated = $text.Replace($old, $new)
    if ($script:isCrlf) { $mutated = $mutated -replace "`n", "`r`n" }
    [System.IO.File]::WriteAllText($path, $mutated, (New-Object System.Text.UTF8Encoding($false)))
    Write-Log ("{0} PRE {1} | editado {2} | {3}" -f $cycle.id, $pre.Substring(0, 12), $cycle.file, $cycle.why)

    $run = Invoke-ModRun $cycle.id
    $isProductRed = ($run.Failed -gt 0)
    Write-Log ("{0} RUN wrapper={1} logExit={2} aprovado={3} falha={4} constraint={5} log={6}" -f `
        $cycle.id, $run.Wrapper, $run.LogExit, $run.Approved, $run.Failed, $run.Constraint, (Split-Path $run.Log -Leaf))
    if ($run.Failing) { Write-Log ("{0} RED-IN-LOG {1}" -f $cycle.id, $run.Failing) }
    if (-not $isProductRed) {
        Write-Log ("{0} CLASSIFICACAO-ERRADA: o log nao mostra falha de produto (ambiente? S-22)" -f $cycle.id)
        $failedCycles += $cycle.id
    }

    # restore byte-exact from memory + mtime bump (S-23), then hash must equal PRE
    [System.IO.File]::WriteAllBytes($path, $bytes)
    (Get-Item -LiteralPath $path).LastWriteTime = Get-Date
    $post = Get-Hash $path
    $residue = ([regex]::Matches((Read-Lf $path), 'QA-MUTATION')).Count
    if ($post -ne $pre -or $residue -ne 0) {
        Write-Log ("{0} RESTORE-FAIL hash={1} residue={2}" -f $cycle.id, $post.Substring(0, 12), $residue)
        $failedCycles += $cycle.id
    } else {
        Write-Log ("{0} RESTORE-OK hash=PRE residue=0" -f $cycle.id)
    }
}

# One green over the restored sources: every mutation is back before the suite runs again (S-23).
$green = Invoke-ModRun 'GREEN'
Write-Log ("GREEN wrapper={0} logExit={1} aprovado={2} falha={3} log={4}" -f `
    $green.Wrapper, $green.LogExit, $green.Approved, $green.Failed, (Split-Path $green.Log -Leaf))
if ($green.Failed -ne 0 -or $green.Wrapper -ne 0) { $failedCycles += 'GREEN' }

# Residue: no QA-MUTATION marker left anywhere in the backend (cs + sql), no temp backup of this QA.
$residues = Get-ChildItem -LiteralPath (Join-Path $repo 'backend') -Recurse -File -Include '*.cs', '*.sql' |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    Select-String -Pattern 'QA-MUTATION' -SimpleMatch
$backups = Get-ChildItem -Path $env:TEMP -Filter 'qa0037-*' -File -ErrorAction SilentlyContinue
Write-Log ("SUMMARY cycles={0} failed={1} residue-QA-MUTATION={2} temp-backups={3}" -f `
    $selected.Count, $failedCycles.Count, @($residues).Count, @($backups).Count)

if ($failedCycles.Count -gt 0) { exit 1 }
exit 0
