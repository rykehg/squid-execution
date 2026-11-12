# QA-0039 - mutation driver of TASK-0039 (anexo em mensagem reusando /api/media intacta).
# Thirteen cycles, one acceptance behaviour each: SHA256 PRE (S-16) -> QA-MUTATION edit(s) (pattern
# count must equal `expected` on EVERY edit BEFORE the run, checked against pristine sources) ->
# filtered red run -> classification INSIDE the log file (S-22, never the wrapper's console exit)
# -> byte-exact restore from memory + mtime bump (S-23) -> hash = PRE -> marker residue 0.
# After the thirteen: one green filtered run over the restored sources (the QA's temporary cap test
# is still in the tree at that point: GREEN = 42/42 = 41 DEV + 1 QA).
# Every file mutated is inside the task's touches (Squid.Api/Threads/**, Squid.Api/Media/**,
# Squid.Service/**, Squid.Persistence/schema/**); no product file keeps a byte of this driver.
# Multiline patterns are matched in LF space and written back in the file's own line endings.
# THIS FILE MUST KEEP ITS UTF-8 BOM (QA-0034..0038 lesson: a no-BOM edit turns every accented
# pattern into ANSI and PATTERN-COUNT dies at hits=0). Check with -VerifyOnly before every run.
# Targets: M1 the 0007 list never persisted (AC-01 roundtrip); M2 the wire envelope loses `alt`
# (AC-01 attachment fields); M3 the cap >10 TRUNCATED instead of refused (AC-01, caught ONLY by the
# QA's own cap test with real hashes); M4 an unknown hash ignored instead of 400 (AC-01); M5 the
# attachment validation moved AFTER the CAS with the refusal committing (AC-01: a refused send
# must consume no seq); M6 the 2.6 Alt rule off at publication (AC-02); M7 the SEND charges
# quota_ledger (AC-03 money shot); M8 the message branch of GAP-25's ladder removed (AC-04: message
# tests red while the POST half stays green - this cycle filters Threads|Media); M9 the participant
# seat off the ampliation (AC-04 outsider 404); M10 the 428 gate skipped when hashes ride (ladder
# 428 before 400); M11 the 422 gate skipped when hashes ride (ladder 422 before 400); M12 the 507
# reservation off POST /api/media (AC-03 the 507 still comes from the upload route); M13 a stored
# counting column added to migration 0007 (AC-01/AC-05 scope: the migration test must catch it,
# routes stay green).
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0039.ps1 [-Only M1] [-VerifyOnly]
param([string[]]$Only = @(), [switch]$VerifyOnly)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$gate = Join-Path $repo 'docs\sdd\_tools\sdd-test.ps1'
$logs = Join-Path $repo 'docs\sdd\runs\test-runs'
$out = Join-Path $PSScriptRoot 'QA-0039-mutations.log'
$filter = 'FullyQualifiedName~Squid.Tests.Threads'
# M8 alone runs BOTH families: the proof is that the POST half of GAP-25 stays green (Media
# aprovados) while only the message test reddens under the same mutant.
$filterBoth = 'FullyQualifiedName~Squid.Tests.Threads|FullyQualifiedName~Squid.Tests.Media'

$cycles = @(
    # AC-01 - the roundtrip itself: the send answer comes from the in-memory list, so the ONLY
    # proof that 0007 persists the list is the READ envelope. This mutant always writes NULL.
    @{ id = 'M1'; edits = @(
        @{ file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
           old = (@(
                  '                insert.Parameters.AddWithValue("@Attachments",',
                  '                    attachments.Count > 0 ? JsonSerializer.Serialize(attachments) : DBNull.Value);'
                 ) -join "`n")
           new = (@(
                  '                insert.Parameters.AddWithValue("@Attachments",',
                  '                    DBNull.Value); // QA-MUTATION-M1: a lista de anexos nunca e persistida (roundtrip 0007 morto)'
                 ) -join "`n") }
       )
       why = 'AC-01: a coluna 0007 nasce sempre NULL - a leitura devolve anexos vazios (send continua com a lista em memoria)' }

    # AC-01 - the wire shape of data-model 2.6: MessageView is what spells the attachment fields
    # word for word; dropping `alt` from it hides the field every reader of the envelope checks.
    @{ id = 'M2'; edits = @(
        @{ file = 'backend\src\Squid.Api\Threads\MessageView.cs'; expected = 1
           old = '        alt = attachment.Alt,'
           # typed null: `alt = null` alone is CS0828 (null cannot be assigned to an anonymous
           # property) - the first version of this mutant died in the BUILD, the log itself
           # classified it (S-22: aprovado=0 falha=0 -> CLASSIFICACAO-ERRADA) and the cycle was
           # fixed and re-run with -Only M2. Not counted as proof until it reddens a test.
           new = '        alt = (string?)null, // QA-MUTATION-M2: campo alt fora do envelope de leitura' }
       )
       why = 'AC-01: o envelope de leitura deixa de devolver `alt` (campo obrigatorio de 2.6 para image/video/gif)' }

    # AC-01 - the cap: the DEV test's eleven hashes are fabricated hex strings (the per-hash 400
    # would refuse them too), so only the QA's temp test - ELEVEN COPIES OF ONE REAL HASH - fails
    # when the count guard truncates instead of refusing.
    @{ id = 'M3'; edits = @(
        @{ file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
           old = (@(
                  '            if (contentHashes is { Count: > Attachment.MaxPerPost })',
                  '            {',
                  '                return new AppendResult(AppendOutcome.InvalidAttachment, null)',
                  '                {',
                  '                    Reason = $"máximo de {Attachment.MaxPerPost} anexos por mensagem",',
                  '                };',
                  '            }'
                 ) -join "`n")
           new = (@(
                  '            if (contentHashes is { Count: > Attachment.MaxPerPost })',
                  '            {',
                  '                contentHashes = contentHashes.Take(Attachment.MaxPerPost).ToList(); // QA-MUTATION-M3: acima de 10 e truncado, nunca recusado',
                  '            }'
                 ) -join "`n") }
       )
       why = 'AC-01: lista de 11 referencias de um hash REAL vira 10 e o envio responde 200 em vez de 400 (Fact da QA)' }

    # AC-01 - unknown hash: the guard inside the loop IS the 400 of "hash inexistente".
    @{ id = 'M4'; edits = @(
        @{ file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
           old = (@(
                  '                var record = await _media.FindAsync(sent ?? string.Empty);',
                  '                if (record is null)',
                  '                {',
                  '                    return new AppendResult(AppendOutcome.InvalidAttachment, null)',
                  '                    {',
                  '                        Reason = "contentHash inexistente: faça o upload da mídia antes de anexá-la",',
                  '                    };',
                  '                }'
                 ) -join "`n")
           new = (@(
                  '                var record = await _media.FindAsync(sent ?? string.Empty);',
                  '                if (record is null)',
                  '                {',
                  '                    continue; // QA-MUTATION-M4: hash desconhecido e ignorado, nunca recusado',
                  '                }'
                 ) -join "`n") }
       )
       why = 'AC-01: hash inexistente e ignorado - a mensagem aceita hash fantasma com 200 em vez de 400' }

    # AC-01 - "recusa nenhuma consome seq": validation must run BEFORE the CAS and the attempt
    # rolls back. This mutant moves the whole block AFTER the CAS and COMMITS the ghost-hash
    # refusal, so the refused send really burns the number the next accepted send then reports.
    @{ id = 'M5'; edits = @(
        @{ file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
           old = (@(
                  '            var attachments = new List<Attachment>();',
                  '            if (contentHashes is { Count: > Attachment.MaxPerPost })',
                  '            {',
                  '                return new AppendResult(AppendOutcome.InvalidAttachment, null)',
                  '                {',
                  '                    Reason = $"máximo de {Attachment.MaxPerPost} anexos por mensagem",',
                  '                };',
                  '            }',
                  '',
                  '            foreach (var sent in contentHashes ?? [])',
                  '            {',
                  '                var record = await _media.FindAsync(sent ?? string.Empty);',
                  '                if (record is null)',
                  '                {',
                  '                    return new AppendResult(AppendOutcome.InvalidAttachment, null)',
                  '                    {',
                  '                        Reason = "contentHash inexistente: faça o upload da mídia antes de anexá-la",',
                  '                    };',
                  '                }',
                  '',
                  '                var kind = Attachment.KindOf(record.MimeType);',
                  '                if (Attachment.NeedsAlt(kind) && string.IsNullOrWhiteSpace(record.Alt))',
                  '                {',
                  '                    return new AppendResult(AppendOutcome.InvalidAttachment, null)',
                  '                    {',
                  '                        Reason = "alt obrigatório e não vazio para imagem, vídeo e gif (FR-022)",',
                  '                    };',
                  '                }',
                  '',
                  '                attachments.Add(new Attachment',
                  '                {',
                  '                    Kind = kind,',
                  '                    ContentHash = record.ContentHash,',
                  '                    MimeType = record.MimeType,',
                  '                    Url = $"{origin}/api/media/{record.ContentHash}",',
                  '                    BlurHash = record.BlurHash,',
                  '                    Alt = record.Alt,',
                  '                    Width = record.Width,',
                  '                    Height = record.Height,',
                  '                    DurationSeconds = record.DurationSeconds,',
                  '                    SizeBytes = record.SizeBytes,',
                  '                });',
                  '            }'
                 ) -join "`n")
           new = '            var attachments = new List<Attachment>(); // QA-MUTATION-M5: validacao movada para DEPOIS da CAS' },
        @{ file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
           old = (@(
                  '                seq = Convert.ToInt64(value, CultureInfo.InvariantCulture);',
                  '            }',
                  '',
                  '            await using (var insert = connection.CreateCommand())'
                 ) -join "`n")
           new = (@(
                  '                seq = Convert.ToInt64(value, CultureInfo.InvariantCulture);',
                  '            }',
                  '',
                  '            if (contentHashes is { Count: > Attachment.MaxPerPost })',
                  '            {',
                  '                await transaction.CommitAsync();',
                  '                return new AppendResult(AppendOutcome.InvalidAttachment, null)',
                  '                {',
                  '                    Reason = $"máximo de {Attachment.MaxPerPost} anexos por mensagem",',
                  '                };',
                  '            }',
                  '',
                  '            foreach (var sent in contentHashes ?? [])',
                  '            {',
                  '                var record = await _media.FindAsync(sent ?? string.Empty);',
                  '                if (record is null)',
                  '                {',
                  '                    await transaction.CommitAsync(); // QA-MUTATION-M5: a recusa de anexo ja gravou o numero',
                  '                    return new AppendResult(AppendOutcome.InvalidAttachment, null)',
                  '                    {',
                  '                        Reason = "contentHash inexistente: faça o upload da mídia antes de anexá-la",',
                  '                    };',
                  '                }',
                  '',
                  '                var kind = Attachment.KindOf(record.MimeType);',
                  '                if (Attachment.NeedsAlt(kind) && string.IsNullOrWhiteSpace(record.Alt))',
                  '                {',
                  '                    return new AppendResult(AppendOutcome.InvalidAttachment, null)',
                  '                    {',
                  '                        Reason = "alt obrigatório e não vazio para imagem, vídeo e gif (FR-022)",',
                  '                    };',
                  '                }',
                  '',
                  '                attachments.Add(new Attachment',
                  '                {',
                  '                    Kind = kind,',
                  '                    ContentHash = record.ContentHash,',
                  '                    MimeType = record.MimeType,',
                  '                    Url = $"{origin}/api/media/{record.ContentHash}",',
                  '                    BlurHash = record.BlurHash,',
                  '                    Alt = record.Alt,',
                  '                    Width = record.Width,',
                  '                    Height = record.Height,',
                  '                    DurationSeconds = record.DurationSeconds,',
                  '                    SizeBytes = record.SizeBytes,',
                  '                });',
                  '            }',
                  '',
                  '            await using (var insert = connection.CreateCommand())'
                 ) -join "`n") }
       )
       why = 'AC-01: a recusa de hash inexistente acontece DEPOIS da CAS e grava o numero - o envio limpo seguinte nasce no seq 2' }

    # AC-02 - data-model 2.6 at publication: NeedsAlt + empty Alt is the refusal, and the test's
    # record was seeded straight into the store precisely so the send path cannot lean on the
    # upload route's own check.
    @{ id = 'M6'; edits = @(
        @{ file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
           old = '                if (Attachment.NeedsAlt(kind) && string.IsNullOrWhiteSpace(record.Alt))'
           new = '                if (Attachment.NeedsAlt(kind) && string.IsNullOrWhiteSpace(record.Alt) && record.Alt.Length < 0) // QA-MUTATION-M6: regra 2.6 desligada na publicacao' }
       )
       why = 'AC-02: mensagem com midia sem Alt e aceita (200) em vez do 400 da publicacao - o upload ja bloqueia, a store direta prova o caminho do envio' }

    # AC-03 - the money shot: the SEND writes a quota_ledger row change (same DB, same table the
    # upload route charges). Anexo de mensagem NAO cobra duas vezes - this mutant makes it.
    @{ id = 'M7'; edits = @(
        @{ file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
           old = (@(
                  '            }',
                  '',
                  '            // The same version-guarded CAS NextMessageSeqAsync runs (GAP-29): message_seq and'
                 ) -join "`n")
           new = (@(
                  '            }',
                  '',
                  '            if (attachments.Count > 0)',
                  '            {',
                  '                // QA-MUTATION-M7: o ENVIO cobra de novo - AC-03 diz que so POST /api/media mexe no ledger',
                  '                await using var charge = connection.CreateCommand();',
                  '                charge.Transaction = transaction;',
                  '                charge.CommandText =',
                  '                    "UPDATE quota_ledger SET used_bytes = used_bytes + 1, updated_at = @Now WHERE id = 1";',
                  '                charge.Parameters.AddWithValue("@Now",',
                  '                    createdAt.ToString("O", CultureInfo.InvariantCulture));',
                  '                await charge.ExecuteNonQueryAsync();',
                  '            }',
                  '',
                  '            // The same version-guarded CAS NextMessageSeqAsync runs (GAP-29): message_seq and'
                 ) -join "`n") }
       )
       why = 'AC-03: enviar mensagem com anexo SOMA bytes no quota_ledger (a linha byte-identica da QA vira diferente)' }

    # AC-04 - GAP-25's ladder: the message branch is the NEW third rung. This mutant removes it,
    # so the participant of the thread falls to 404 while the post branch above decides exactly
    # as before (the Media family must stay GREEN in the same run).
    @{ id = 'M8'; filter = $filterBoth; edits = @(
        @{ file = 'backend\src\Squid.Api\Media\MediaEndpoints.cs'; expected = 1
           old = (@(
                  '        // Section G (TASK-0039): the participant of a conversation carrying this hash reads it —',
                  '        // the post rule above decides nothing here, and a reader in no such thread still gets null.',
                  '        var actorId = ContainerEndpoints.ActorId(context, current.Handle);',
                  '        if (await threads.MessageMediaVisibleAsync(record.ContentHash, actorId)) return record;'
                 ) -join "`n")
           new = '        // QA-MUTATION-M8: ramo da MENSAGEM da regra GAP-25 desligado - so upload e post decidem' }
       )
       why = 'AC-04: o participante da thread que referencia o hash cai no 404 (ramo mensagem morto) - e o ramo POST segue verde no MESMO run' }

    # AC-04 - the ampliation keeps FR-031's wall: without the seat probe every reader of the
    # instance would see every conversation's media.
    @{ id = 'M9'; edits = @(
        @{ file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
           old = (@(
                  '                  AND EXISTS(',
                  '                      SELECT 1 FROM thread_participant AS p',
                  '                      WHERE p.thread_id = m.thread_id AND p.actor_id = @ActorId',
                  '                  )'
                 ) -join "`n")
           new = '                  AND 1 = 1 /* QA-MUTATION-M9: parede de participantes fora da ampliacao GAP-25 */' }
       )
       why = 'AC-04: o estrangeiro (participante de OUTRA thread) le a midia da conversa alheia com 200 em vez do 404 identico' }

    # Ladder order 428 BEFORE 400: contract 6 makes the acceptance absolute for every send, so a
    # send missing BOTH the ack and a valid attachment answers 428 first.
    @{ id = 'M10'; edits = @(
        @{ file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
           old = '                if (!accepted)'
           new = '                if (!accepted && contentHashes is null) // QA-MUTATION-M10: 428 some quando a recusa envia anexo' }
       )
       why = 'escada 428-400: sem aceite + anexo ruim o envio responde 400 (anexo) em vez de 428 (aceite)' }

    # Ladder order 422 BEFORE 400: a reply target outside the thread must stay 422 even when the
    # same send also carries a bad attachment.
    @{ id = 'M11'; edits = @(
        @{ file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
           old = '            if (replyToId is not null)'
           new = '            if (replyToId is not null && contentHashes is null) // QA-MUTATION-M11: 422 some quando a recusa envia anexo' }
       )
       why = 'escada 422-400: replyToId de fora + anexo ruim o envio responde 400 (anexo) em vez de 422 (resposta)' }

    # AC-03 second half - the 507 lives in POST /api/media: this mutant never refuses the
    # reservation, so the oversized upload answers 200 instead of 507 quota_exceeded.
    @{ id = 'M12'; edits = @(
        @{ file = 'backend\src\Squid.Api\Media\MediaEndpoints.cs'; expected = 1
           old = (@(
                  '        if (!await quota.TryReserveAsync(',
                  '                processed.Bytes.LongLength, options.QuotaLimitBytes, time.GetUtcNow()))',
                  '        {'
                 ) -join "`n")
           new = (@(
                  '        if (false) // QA-MUTATION-M12: a cota 507 de POST /api/media desligada',
                  '        {'
                 ) -join "`n") }
       )
       why = 'AC-03: o 507 some de POST /api/media - o upload acima da cota passa (200) e ainda muda o ledger' }

    # AC-01/AC-05 - migration 0007: the pragma_table_info list of the migration test is exact and
    # says the new column is TEXT, never a count; a VALID counting column reddens that test while
    # every route stays green (the INSERT and the SELECT name their columns).
    @{ id = 'M13'; edits = @(
        @{ file = 'backend\src\Squid.Persistence\schema\0007_message_attachments.sql'; expected = 1
           old = 'ALTER TABLE message ADD COLUMN attachments TEXT;'
           new = (@(
                  'ALTER TABLE message ADD COLUMN attachments TEXT;',
                  'ALTER TABLE message ADD COLUMN attachment_count INTEGER NOT NULL DEFAULT 0; -- QA-MUTATION-M13: contador armazenado nos anexos'
                 ) -join "`n") }
       )
       why = 'AC-01: coluna de CONTAGEM somada a familia 0007 - so a ThreadMigrationTests vermelha, rotas verdes' }
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

function Invoke-ModRun([string]$Label, [string]$RunFilter) {
    $before = (Get-ChildItem -LiteralPath $logs -Filter '*.log' | Sort-Object LastWriteTime -Descending | Select-Object -First 1).LastWriteTime
    # The filter rides as one argv token (no spaces, so no quoting needed).
    $null = & powershell -NoProfile -ExecutionPolicy Bypass -File $gate test -TestFilter $RunFilter 2>&1 | Out-String
    $code = $LASTEXITCODE
    $log = Get-ChildItem -LiteralPath $logs -Filter '*.log' |
        Where-Object { $_.LastWriteTime -gt $before } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -eq $log) {
        [pscustomobject]@{ Label = $Label; Wrapper = $code; Log = '(nenhum log novo)'; LogExit = $null; Failed = -1; Approved = -1; Failing = ''; Detail = ''; Constraint = 'no' }
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
    # One line of failure DETAIL after a failing test (the assertion that actually broke) - this
    # is what tells apart two mutations that land in the SAME test.
    $detail = @()
    $lines = $text -split "`n"
    for ($i = 0; $i -lt $lines.Count -and $detail.Count -lt 2; $i++) {
        if ($lines[$i] -match 'Assert\.(Equal|All|Contains|DoesNotContain|True|False|Single|Throws)') {
            $detail += ($lines[$i].Trim() -replace '\s+', ' ')
            if (($i + 1) -lt $lines.Count) { $detail += ($lines[$i + 1].Trim() -replace '\s+', ' ') }
        }
    }
    $constraint = if ($text -match 'UNIQUE constraint failed|duplicate column name|near "') { 'yes' } else { 'no' }
    [pscustomobject]@{
        Label     = $Label
        Wrapper   = $code
        Log       = $log.FullName
        LogExit   = $logExit
        Failed    = $failed
        Approved  = $approved
        Failing   = (($failing | Select-Object -First 8) -join ' | ')
        Detail    = (($detail | Select-Object -First 2) -join ' ~ ')
        Constraint = $constraint
    }
}

# -VerifyOnly: prove every pattern count against the pristine sources, run nothing.
if ($VerifyOnly) {
    $bad = 0
    foreach ($cycle in $cycles) {
        $cycleBad = 0
        foreach ($edit in $cycle.edits) {
            $path = Join-Path $repo $edit.file
            $text = Read-Lf $path
            $old = ($edit.old -replace "`r`n", "`n")
            $hits = ([regex]::Matches($text, [regex]::Escape($old))).Count
            $expected = if ($edit.ContainsKey('expected')) { [int]$edit.expected } else { 1 }
            $ok = ($hits -eq $expected)
            if (-not $ok) { $bad++; $cycleBad++ }
            Write-Output ("{0} hits={1} expected={2} {3} {4}" -f $cycle.id, $hits, $expected, $(if ($ok) { 'OK' } else { 'FAIL' }), $edit.file)
        }
        if ($cycleBad -gt 0) { $null = $cycleBad }
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
    Set-Content -LiteralPath $out -Value "# QA-0039 mutations - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -Encoding utf8
}
$failedCycles = @()
$selected = @(if ($Only.Count -gt 0) { $cycles | Where-Object { $Only -contains $_.id } } else { $cycles })

foreach ($cycle in $selected) {
    $runFilter = if ($cycle.ContainsKey('filter')) { $cycle.filter } else { $filter }
    # Group the cycle's edits by file: one read, all pattern checks, one write, one restore.
    $files = @($cycle.edits | ForEach-Object { $_.file } | Select-Object -Unique)
    $bytesByFile = @{}
    $preByFile = @{}
    $mutated = $true
    foreach ($file in $files) {
        $path = Join-Path $repo $file
        $bytesByFile[$file] = [System.IO.File]::ReadAllBytes($path)
        $preByFile[$file] = Get-Hash $path
        $text = Read-Lf $path
        foreach ($edit in ($cycle.edits | Where-Object { $_.file -eq $file })) {
            $old = ($edit.old -replace "`r`n", "`n")
            $hits = ([regex]::Matches($text, [regex]::Escape($old))).Count
            $expected = if ($edit.ContainsKey('expected')) { [int]$edit.expected } else { 1 }
            if ($hits -ne $expected) {
                Write-Log ("{0} PATTERN-COUNT={1} (esperado {2}) em {3} - ciclo abortado" -f $cycle.id, $hits, $expected, $file)
                $mutated = $false
            }
        }
        if (-not $mutated) { break }
        foreach ($edit in ($cycle.edits | Where-Object { $_.file -eq $file })) {
            $text = $text.Replace(($edit.old -replace "`r`n", "`n"), ($edit.new -replace "`r`n", "`n"))
        }
        if ($script:isCrlf) { $text = $text -replace "`n", "`r`n" }
        [System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding($false)))
        Write-Log ("{0} PRE {1} | editado {2}" -f $cycle.id, $preByFile[$file].Substring(0, 12), $file)
    }
    if (-not $mutated) {
        # Abort means RESTORE too: put back anything already written (nothing should be, but be sure).
        foreach ($file in $files) {
            if ($bytesByFile.ContainsKey($file)) {
                $path = Join-Path $repo $file
                if ((Get-Hash $path) -ne $preByFile[$file]) {
                    [System.IO.File]::WriteAllBytes($path, $bytesByFile[$file])
                    (Get-Item -LiteralPath $path).LastWriteTime = Get-Date
                }
            }
        }
        $failedCycles += $cycle.id
        continue
    }
    Write-Log ("{0} WHY {1}" -f $cycle.id, $cycle.why)
    Write-Log ("{0} FILTER {1}" -f $cycle.id, $runFilter)

    $run = Invoke-ModRun $cycle.id $runFilter
    $isProductRed = ($run.Failed -gt 0)
    Write-Log ("{0} RUN wrapper={1} logExit={2} aprovado={3} falha={4} constraint={5} log={6}" -f `
        $cycle.id, $run.Wrapper, $run.LogExit, $run.Approved, $run.Failed, $run.Constraint, (Split-Path $run.Log -Leaf))
    if ($run.Failing) { Write-Log ("{0} RED-IN-LOG {1}" -f $cycle.id, $run.Failing) }
    if ($run.Detail) { Write-Log ("{0} RED-DETAIL {1}" -f $cycle.id, $run.Detail) }
    if (-not $isProductRed) {
        Write-Log ("{0} CLASSIFICACAO-ERRADA: o log nao mostra falha de produto (ambiente? S-22)" -f $cycle.id)
        $failedCycles += $cycle.id
    }

    # restore byte-exact from memory + mtime bump (S-23), then hash must equal PRE
    foreach ($file in $files) {
        $path = Join-Path $repo $file
        [System.IO.File]::WriteAllBytes($path, $bytesByFile[$file])
        (Get-Item -LiteralPath $path).LastWriteTime = Get-Date
        $post = Get-Hash $path
        $residue = ([regex]::Matches((Read-Lf $path), 'QA-MUTATION')).Count
        if ($post -ne $preByFile[$file] -or $residue -ne 0) {
            Write-Log ("{0} RESTORE-FAIL file={1} hash={2} residue={3}" -f $cycle.id, $file, $post.Substring(0, 12), $residue)
            $failedCycles += $cycle.id
        } else {
            Write-Log ("{0} RESTORE-OK hash=PRE residue=0 file={1}" -f $cycle.id, $file)
        }
    }
}

# One green over the restored sources: every mutation is back before the suite runs again (S-23).
# The QA's temporary cap test is still in the tree here: GREEN = 42/42 (41 DEV + 1 QA).
$green = Invoke-ModRun 'GREEN' $filter
Write-Log ("GREEN wrapper={0} logExit={1} aprovado={2} falha={3} log={4}" -f `
    $green.Wrapper, $green.LogExit, $green.Approved, $green.Failed, (Split-Path $green.Log -Leaf))
if ($green.Failed -ne 0 -or $green.Wrapper -ne 0) { $failedCycles += 'GREEN' }

# Residue: no QA-MUTATION marker left anywhere in the backend (cs + sql), no temp backup of this QA.
$residues = Get-ChildItem -LiteralPath (Join-Path $repo 'backend') -Recurse -File -Include '*.cs', '*.sql' |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    Select-String -Pattern 'QA-MUTATION' -SimpleMatch
$backups = Get-ChildItem -Path $env:TEMP -Filter 'qa0039-*' -File -ErrorAction SilentlyContinue
Write-Log ("SUMMARY cycles={0} failed={1} residue-QA-MUTATION={2} temp-backups={3}" -f `
    $selected.Count, ($failedCycles | Select-Object -Unique).Count, @($residues).Count, @($backups).Count)

if ($failedCycles.Count -gt 0) { exit 1 }
exit 0
