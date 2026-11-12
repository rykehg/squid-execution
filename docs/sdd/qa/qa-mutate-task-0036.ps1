# QA-0036 - mutation driver of TASK-0036 (aviso nao-E2EE: 428 por aceite (ator,thread)
# idempotente com o gate DENTRO da transacao do append, marcador de leitura monotonico
# visivel so a participantes em GET /api/threads e no envelope, ator suspenso negado 403 com
# linha Deny na trilha ANTES da resposta e ANTES do 428, parser com /participants/ antes dos
# novos sufixos). Fifteen cycles, one acceptance behaviour each: SHA256 PRE (S-16) -> a single
# QA-MUTATION edit (pattern count must equal `expected`, checked BEFORE the run) -> filtered red
# run -> classification INSIDE the log file (S-22, never the wrapper's console exit) -> byte-exact
# restore from memory + mtime bump (S-23) -> hash = PRE -> marker residue 0.
# After the fifteen: one green filtered run over the restored sources.
# Every file mutated is inside the task's touches (Squid.Api/Threads/**, Squid.Service/**); the
# temporary QA test QaTask0036RouteCollisionTests.cs rides every run and is removed after.
# Multiline patterns are matched in LF space and written back in the file's own line endings,
# so CRLF sources stay byte-consistent. THIS FILE MUST KEEP ITS UTF-8 BOM (QA-0034/0035 lesson:
# a no-BOM edit turns every accented pattern into ANSI and PATTERN-COUNT dies at hits=0).
# Targets: M1 the 428 status+code itself (AC-02); M2 a refusal that BURNS a sequence number
# (commit of a bump before the AckRequired return - AC-02); M3 the per-actor keying of the gate
# (any ack unlocks everybody - AC-02); M4 the idempotent ack (PK without DO NOTHING - AC-01);
# M5 the monotonic marker (upsert without the WHERE - AC-03); M6 seq above total answered 200
# instead of 400 (GAP-38, AC-03); M7 GET /api/threads marker joined for anybody (AC-04); M8 the
# envelope marker read for anybody (AC-04); M9 the read-route participant probe off (outsider
# writes a marker on a foreign thread - AC-04); M10 the shared 404 carrying traceId (breaks the
# byte-identical pair - AC-04); M11 the ladder: 428 answered BEFORE the suspended check (AC-05);
# M12 the Deny trail row never written (AC-05); M13 the /read tail ahead of the /participants/
# marker (parser reordering - seat called "read" stops being a removal); M14 the ack row storing
# an empty notice (FR-032 text at the source); M15 a negative seq accepted (read 400 branch).
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0036.ps1 [-Only M1] [-VerifyOnly]
param([string[]]$Only = @(), [switch]$VerifyOnly)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$gate = Join-Path $repo 'docs\sdd\_tools\sdd-test.ps1'
$logs = Join-Path $repo 'docs\sdd\runs\test-runs'
$out = Join-Path $PSScriptRoot 'QA-0036-mutations.log'
$filter = 'FullyQualifiedName~Squid.Tests.Threads'

$cycles = @(
    # AC-02 - the answer of contract 8/9.6 itself: without acceptance the send must be
    # 428 precondition_required, never a catalog neighbour like 409.
    @{ id = 'M1'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
       old = (@(
              '            AppendOutcome.AckRequired => AuthErrors.Write(context,',
              '                StatusCodes.Status428PreconditionRequired, "precondition_required",',
              '                "aceite o aviso de não-E2EE antes de enviar (POST /api/threads/{id}/ack-no-e2ee)."),'
             ) -join "`n")
       new = (@(
              '            AppendOutcome.AckRequired => AuthErrors.Write(context,',
              '                StatusCodes.Status409Conflict, "version_conflict", // QA-MUTATION-M1',
              '                "aceite o aviso de não-E2EE antes de enviar (POST /api/threads/{id}/ack-no-e2ee)."),'
             ) -join "`n")
       why = 'AC-02: 428 precondition_required vira 409 version_conflict (ThreadAckAndReadRoutesTests vermelha)' }
    # AC-02 - the refusal must burn NO sequence number: the gate lives inside the append
    # transaction BEFORE the number is taken. This mutant takes a number and COMMITS it on the
    # refusal path, so the send after the ack lands on the second free number instead of the first.
    @{ id = 'M2'; file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
       old = (@(
              '                if (!accepted)',
              '                {',
              '                    return new AppendResult(AppendOutcome.AckRequired, null);',
              '                }'
             ) -join "`n")
       new = (@(
              '                if (!accepted)',
              '                {',
              '                    await using (var burn = connection.CreateCommand())',
              '                    {',
              '                        burn.Transaction = transaction;',
              '                        burn.CommandText =',
              '                            "UPDATE thread SET message_seq = message_seq + 1, version = version + 1 WHERE id = @Id";',
              '                        burn.Parameters.AddWithValue("@Id", threadId);',
              '                        await burn.ExecuteNonQueryAsync();',
              '                    }',
              '',
              '                    await transaction.CommitAsync(); // QA-MUTATION-M2',
              '                    return new AppendResult(AppendOutcome.AckRequired, null);',
              '                }'
             ) -join "`n")
       why = 'AC-02: recusa que QUEIMA numero - message_seq avanca no 428 e o envio pos-aceite nasce em 2 (ThreadAckAndReadRoutesTests + ThreadStoreTests vermelhas)' }
    # AC-02 - the gate is keyed per (thread, SENDER): A's acceptance must never unlock B.
    @{ id = 'M3'; file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
       old = '                        SELECT 1 FROM thread_no_e2ee_ack WHERE thread_id = @ThreadId AND actor_id = @ActorId'
       new = '                        SELECT 1 FROM thread_no_e2ee_ack WHERE thread_id = @ThreadId AND (@ActorId = @ActorId OR actor_id = @ActorId) -- QA-MUTATION-M3'
       why = 'AC-02: gate por thread - o aceite de UM ator libera o outro (ThreadAckAndReadRoutesTests + ThreadStoreTests vermelhas)' }
    # AC-01 - the ack is idempotent because 0004's PRIMARY KEY is enforced by DO NOTHING.
    @{ id = 'M4'; file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
       old = (@(
              '            INSERT INTO thread_no_e2ee_ack (thread_id, actor_id, acked_at, notice)',
              '            VALUES (@ThreadId, @ActorId, @AckedAt, @Notice)',
              '            ON CONFLICT (thread_id, actor_id) DO NOTHING'
             ) -join "`n")
       new = (@(
              '            INSERT INTO thread_no_e2ee_ack (thread_id, actor_id, acked_at, notice)',
              '            VALUES (@ThreadId, @ActorId, @AckedAt, @Notice)',
              '            -- QA-MUTATION-M4'
             ) -join "`n")
       why = 'AC-01: segundo aceite estoura a PK em vez de 200-idempotente (ThreadAckAndReadRoutesTests + ThreadStoreTests vermelhas)' }
    # AC-03 - the marker only advances: drop the WHERE and a smaller seq overwrites the high-water mark.
    @{ id = 'M5'; file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
       old = (@(
              '                ON CONFLICT (thread_id, actor_id) DO UPDATE',
              '                SET last_read_seq = excluded.last_read_seq',
              '                WHERE excluded.last_read_seq > thread_read.last_read_seq'
             ) -join "`n")
       new = (@(
              '                ON CONFLICT (thread_id, actor_id) DO UPDATE',
              '                SET last_read_seq = excluded.last_read_seq -- QA-MUTATION-M5'
             ) -join "`n")
       why = 'AC-03: marcador REGRIDE - pedir 1 depois de 2 passa a gravar 1 (ThreadAckAndReadRoutesTests + ThreadStoreTests vermelhas)' }
    # AC-03 / GAP-38 - seq above the thread total is 400, nothing written.
    @{ id = 'M6'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
       old = '        if (seq > snapshot.Thread.MessageSeq)'
       new = '        if (false && seq > snapshot.Thread.MessageSeq) // QA-MUTATION-M6'
       why = 'AC-03: seq acima do total aceito em vez de 400 - o marcador la em cima vira 4 (ThreadAckAndReadRoutesTests vermelha)' }
    # AC-04 - GET /api/threads carries the READER's own marker; without the actor key any
    # participant's row answers for everybody (peer would read the owner's high-water mark).
    @{ id = 'M7'; file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
       old = '            LEFT JOIN thread_read AS r ON r.thread_id = t.id AND r.actor_id = @ActorId'
       new = '            LEFT JOIN thread_read AS r ON r.thread_id = t.id -- QA-MUTATION-M7'
       why = 'AC-04: lastReadSeq da lista deixa de ser POR PARTICIPANTE - a par le o marcador do dono (ThreadAckAndReadRoutesTests vermelha)' }
    # AC-04 - the envelope's lastReadSeq is read per actor for the same reason.
    @{ id = 'M8'; file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
       old = (@(
              '        await using var command = connection.CreateCommand();',
              '        command.CommandText =',
              '            "SELECT last_read_seq FROM thread_read WHERE thread_id = @ThreadId AND actor_id = @ActorId";'
             ) -join "`n")
       new = (@(
              '        await using var command = connection.CreateCommand();',
              '        command.CommandText =',
              '            "SELECT last_read_seq FROM thread_read WHERE thread_id = @ThreadId AND (@ActorId = @ActorId OR actor_id = @ActorId) -- QA-MUTATION-M8";'
             ) -join "`n")
       why = 'AC-04: envelope deixa de ser POR PARTICIPANTE - a par enxerga lastReadSeq do dono (ThreadAckAndReadRoutesTests vermelha)' }
    # AC-04 - POST .../read keeps the FR-031 participant probe: an outsider must meet the
    # byte-identical 404, not write a marker on a foreign thread.
    @{ id = 'M9'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
       old = '        if (snapshot is null || !snapshot.Participants.Any(participant => participant.ActorId == reader))'
       new = '        if (snapshot is null) // QA-MUTATION-M9'
       why = 'AC-04: portao de participante da rota read desligado - nao-participante grava marcador em vez de 404 (ThreadAckAndReadRoutesTests vermelha)' }
    # AC-04 / SC-005 - the shared 404 answers without traceId so the four refusals stay byte-identical.
    @{ id = 'M10'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
       old = (@(
              '    private static IResult HiddenThread(HttpContext context) =>',
              '        AuthErrors.Write(context, StatusCodes.Status404NotFound,',
              '            "not_found", "thread não encontrada.", withTraceId: false);'
             ) -join "`n")
       new = (@(
              '    private static IResult HiddenThread(HttpContext context) =>',
              '        AuthErrors.Write(context, StatusCodes.Status404NotFound,',
              '            "not_found", "thread não encontrada.", withTraceId: true); // QA-MUTATION-M10'
             ) -join "`n")
       why = 'AC-04: 404 ganha traceId - a igualdade byte-a-byte das 4 recusas cai (ThreadAckAndReadRoutesTests vermelha)' }
    # AC-05 - the ladder 401 -> 404 -> 403 suspended -> 400 -> 428/200: this mutant appends and
    # answers 428 FIRST, so a suspended actor WITHOUT the acceptance is refused as "faltou o
    # aviso" instead of as suspended - and the acked suspension WRITES the denied message.
    @{ id = 'M11'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
       old = (@(
              '        // data-model §3.1 (the rule Posts enforces on publish and reply): a suspended actor does',
              '        // not send — the decision is recorded like any other and reaches the trail before the 403.',
              '        if (await statuses.GetAsync(current.Handle) == ActorStatus.Suspended)',
              '        {',
              '            await audit.AppendAsync(new PolicyAuditEntry(',
              '                time.GetUtcNow(), actorId, MessageDecision, id, PolicyAudit.Deny, 0,',
              '                "ator suspenso não envia mensagem (data-model §3.1)"));',
              '            return AuthErrors.Write(context, StatusCodes.Status403Forbidden,',
              '                "policy_denied", "o ator está suspenso e não pode enviar mensagens (data-model §3.1).");',
              '        }',
              '',
              '        var refusal = ContentRefusal(context, request?.Content, out var content);',
              '        if (refusal is not null)',
              '        {',
              '            return refusal;',
              '        }',
              '',
              '        var result = await store.AppendMessageAsync(',
              '            id, $"{Origin(context)}/m/{NewToken()}", actorId, content, time.GetUtcNow());',
              '',
              '        // Three answers of §6/§8/§9.6: the absent thread keeps FR-031''s one body, the missing',
              '        // acceptance keeps the precondition the contract asks for, and the stored row is the 200.',
              '        return result.Outcome switch',
              '        {',
              '            AppendOutcome.NoSuchThread => HiddenThread(context), // defensive twin of the probe',
              '            AppendOutcome.AckRequired => AuthErrors.Write(context,',
              '                StatusCodes.Status428PreconditionRequired, "precondition_required",',
              '                "aceite o aviso de não-E2EE antes de enviar (POST /api/threads/{id}/ack-no-e2ee)."),',
              '            _ => Results.Json(MessageView.Of(result.Message!)),',
              '        };'
             ) -join "`n")
       new = (@(
              '        var refusal = ContentRefusal(context, request?.Content, out var content);',
              '        if (refusal is not null)',
              '        {',
              '            return refusal;',
              '        }',
              '',
              '        var result = await store.AppendMessageAsync(',
              '            id, $"{Origin(context)}/m/{NewToken()}", actorId, content, time.GetUtcNow());',
              '',
              '        // QA-MUTATION-M11: o 428 do 9.6 respondido ANTES da checagem suspenso do 3.1.',
              '        if (result.Outcome == AppendOutcome.AckRequired)',
              '        {',
              '            return AuthErrors.Write(context,',
              '                StatusCodes.Status428PreconditionRequired, "precondition_required",',
              '                "aceite o aviso de não-E2EE antes de enviar (POST /api/threads/{id}/ack-no-e2ee).");',
              '        }',
              '',
              '        // data-model §3.1 (the rule Posts enforces on publish and reply): a suspended actor does',
              '        // not send — the decision is recorded like any other and reaches the trail before the 403.',
              '        if (await statuses.GetAsync(current.Handle) == ActorStatus.Suspended)',
              '        {',
              '            await audit.AppendAsync(new PolicyAuditEntry(',
              '                time.GetUtcNow(), actorId, MessageDecision, id, PolicyAudit.Deny, 0,',
              '                "ator suspenso não envia mensagem (data-model §3.1)"));',
              '            return AuthErrors.Write(context, StatusCodes.Status403Forbidden,',
              '                "policy_denied", "o ator está suspenso e não pode enviar mensagens (data-model §3.1).");',
              '        }',
              '',
              '        return result.Outcome switch',
              '        {',
              '            AppendOutcome.NoSuchThread => HiddenThread(context), // defensive twin of the probe',
              '            _ => Results.Json(MessageView.Of(result.Message!)),',
              '        };'
             ) -join "`n")
       why = 'AC-05: escada invertida - suspenso SEM aceite recebe 428 em vez de 403, e a suspensao com aceite grava a mensagem negada (ThreadAckAndReadRoutesTests vermelha)' }
    # AC-05 - the refusal reaches the trail BEFORE the response: without the row the test sees
    # zero Deny entries for action Message.
    @{ id = 'M12'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
       old = (@(
              '            await audit.AppendAsync(new PolicyAuditEntry(',
              '                time.GetUtcNow(), actorId, MessageDecision, id, PolicyAudit.Deny, 0,',
              '                "ator suspenso não envia mensagem (data-model §3.1)"));'
             ) -join "`n")
       new = (@(
              '            if (false && audit is not null) // QA-MUTATION-M12',
              '            await audit.AppendAsync(new PolicyAuditEntry(',
              '                time.GetUtcNow(), actorId, MessageDecision, id, PolicyAudit.Deny, 0,',
              '                "ator suspenso não envia mensagem (data-model §3.1)"));'
             ) -join "`n")
       why = 'AC-05: linha Deny nao gravada na trilha - a contagem de linhas cai de 2 para 0 (ThreadAckAndReadRoutesTests vermelha)' }
    # Parser reordering regression: /read tail ahead of the /participants/ marker turns the
    # removal of a seat called `read` into the read route (shared 404, seat survives).
    @{ id = 'M13'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
       old = (@(
              '        var marker = path.LastIndexOf(ParticipantsMarker, StringComparison.Ordinal);',
              '        if (marker > 0)',
              '        {',
              '            var threadId = path[..marker];',
              '            var actor = path[(marker + ParticipantsMarker.Length)..];',
              '            if (actor.Length == 0)',
              '            {',
              '                return null;',
              '            }',
              '',
              '            if (actor.EndsWith(AdminSuffix, StringComparison.Ordinal))',
              '            {',
              '                var candidate = actor[..^AdminSuffix.Length];',
              '                if (ContainerEndpoints.IdentityOf(null, candidate) is not null)',
              '                {',
              '                    return new ThreadRoute(threadId, ThreadTail.SetAdmin, candidate);',
              '                }',
              '            }',
              '',
              '            return new ThreadRoute(threadId, ThreadTail.RemoveParticipant, actor);',
              '        }',
              '',
              '        if (path.EndsWith(AckNoE2eeSuffix, StringComparison.Ordinal))',
              '        {',
              '            var id = path[..^AckNoE2eeSuffix.Length];',
              '            return string.IsNullOrWhiteSpace(id) ? null : new ThreadRoute(id, ThreadTail.AckNoE2ee, null);',
              '        }',
              '',
              '        if (path.EndsWith(ReadSuffix, StringComparison.Ordinal))',
              '        {',
              '            var id = path[..^ReadSuffix.Length];',
              '            return string.IsNullOrWhiteSpace(id) ? null : new ThreadRoute(id, ThreadTail.Read, null);',
              '        }'
             ) -join "`n")
       new = (@(
              '        if (path.EndsWith(ReadSuffix, StringComparison.Ordinal)) // QA-MUTATION-M13',
              '        {',
              '            var id = path[..^ReadSuffix.Length];',
              '            return string.IsNullOrWhiteSpace(id) ? null : new ThreadRoute(id, ThreadTail.Read, null);',
              '        }',
              '',
              '        if (path.EndsWith(AckNoE2eeSuffix, StringComparison.Ordinal))',
              '        {',
              '            var id = path[..^AckNoE2eeSuffix.Length];',
              '            return string.IsNullOrWhiteSpace(id) ? null : new ThreadRoute(id, ThreadTail.AckNoE2ee, null);',
              '        }',
              '',
              '        var marker = path.LastIndexOf(ParticipantsMarker, StringComparison.Ordinal);',
              '        if (marker > 0)',
              '        {',
              '            var threadId = path[..marker];',
              '            var actor = path[(marker + ParticipantsMarker.Length)..];',
              '            if (actor.Length == 0)',
              '            {',
              '                return null;',
              '            }',
              '',
              '            if (actor.EndsWith(AdminSuffix, StringComparison.Ordinal))',
              '            {',
              '                var candidate = actor[..^AdminSuffix.Length];',
              '                if (ContainerEndpoints.IdentityOf(null, candidate) is not null)',
              '                {',
              '                    return new ThreadRoute(threadId, ThreadTail.SetAdmin, candidate);',
              '                }',
              '            }',
              '',
              '            return new ThreadRoute(threadId, ThreadTail.RemoveParticipant, actor);',
              '        }'
             ) -join "`n")
       why = 'parser: sufixo /read antes do marcador /participants/ - a remocao do participante `read` vira 404 e a cadeira sobrevive (QaTask0036RouteCollisionTests vermelha)' }
    # FR-032 section D - the notice text itself is stored in the ack record.
    @{ id = 'M14'; file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
       old = '        insert.Parameters.AddWithValue("@Notice", notice);'
       new = '        insert.Parameters.AddWithValue("@Notice", string.Empty); // QA-MUTATION-M14'
       why = 'AC-01/FR-032: linha de aceite gravada SEM o texto do aviso - o assert do notice cai (QaTask0036RouteCollisionTests vermelha)' }
    # AC-03 - a negative seq is a payload this route cannot take (400).
    @{ id = 'M15'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
       old = '        if (seq is null || seq < 0)'
       new = '        if (seq is null) // QA-MUTATION-M15'
       why = 'AC-03: seq negativo aceito em vez de 400 na rota read (ThreadAckAndReadRoutesTests vermelha)' }
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
    $constraint = if ($text -match 'UNIQUE constraint failed') { 'yes' } else { 'no' }
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
    Set-Content -LiteralPath $out -Value "# QA-0036 mutations - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -Encoding utf8
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
$backups = Get-ChildItem -Path $env:TEMP -Filter 'qa0036-*' -File -ErrorAction SilentlyContinue
Write-Log ("SUMMARY cycles={0} failed={1} residue-QA-MUTATION={2} temp-backups={3}" -f `
    $selected.Count, $failedCycles.Count, @($residues).Count, @($backups).Count)

if ($failedCycles.Count -gt 0) { exit 1 }
exit 0
