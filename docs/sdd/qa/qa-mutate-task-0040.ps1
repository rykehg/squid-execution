# QA-0040 - mutation driver of TASK-0040 (facet de fecho do DoD do Sprint 4). Eight cycles, one
# acceptance behaviour each: SHA256 PRE (S-16) -> QA-MUTATION edit (pattern count must equal
# `expected` BEFORE the run, checked against pristine sources) -> filtered red run ->
# classification INSIDE the log file (S-22, never the wrapper's console exit) -> byte-exact
# restore from memory + mtime bump (S-23) -> hash = PRE -> marker residue 0. After the eight:
# one green filtered run over the restored sources (GREEN = 48/48 = the Threads suite with the
# facet's 7 tests). Every file mutated is PRODUCT code the facet reads (Squid.Api/Threads/**,
# Squid.Api/Media/**, Squid.Service/**); no product file keeps a byte of this driver.
# Multiline patterns are matched in LF space and written back in the file's own line endings.
# THIS FILE MUST KEEP ITS UTF-8 BOM (QA-0034..0039 lesson, 6a recorrencia: a no-BOM edit turns
# every accented pattern into ANSI and PATTERN-COUNT dies at hits=0). Check with -VerifyOnly
# before every run.
# Targets: M1 the read marker is never written (AC-01/AC-05a/AC-06); M2 the shared 404 leaks
# traceId (AC-02/AC-04 byte-identity); M3 the GET wall off - a non-participant READS the history
# (AC-02/AC-04); M4 the seq allocation never advances - every send collides on the same number
# (AC-03, GAP-29); M5 a reply REWRITES the target row (AC-04 "o estado pre-existente ficou
# intacto"); M6 the /participants/ marker loses to the /read suffix - a seat spelled read stops
# being that participant's removal (AC-05a, ThreadRouteOf order); M7 the FR-032 notice TEXT never
# reaches the ack row (AC-05b, read at the source); M8 the message branch of GAP-25's media
# visibility removed (AC-06 - the non-uploader participant falls to 404).
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0040.ps1 [-Only M1] [-VerifyOnly]
param([string[]]$Only = @(), [switch]$VerifyOnly)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$gate = Join-Path $repo 'docs\sdd\_tools\sdd-test.ps1'
$logs = Join-Path $repo 'docs\sdd\runs\test-runs'
$out = Join-Path $PSScriptRoot 'QA-0040-mutations.log'
$filter = 'FullyQualifiedName~Squid.Tests.Threads'

$cycles = @(
    # AC-01/AC-05a/AC-06 - the FR-033 marker: the upsert is the only write, and the list of
    # GET /api/threads reads it back. Kill the write and every "avancou para N" assertion dies.
    @{ id = 'M1'; edits = @(
        @{ file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
           old = '            await mark.ExecuteNonQueryAsync();'
           new = '            await Task.CompletedTask; // QA-MUTATION-M1: o marcador de leitura nunca e gravado' }
       )
       expect = 'Two_users_create_a_direct|The_full_flow_from_creation|A_seat_spelled_read'
       why = 'AC-01/AC-05a/AC-06: o marcador nunca e gravado - a resposta ecoa o pedido e a lista devolve 0' }

    # AC-02 - the byte-identical 404 of FR-031/SC-005: withTraceId:false is what keeps the four
    # refusal bodies equal; one traceId per request breaks both the identity and the no-leak word.
    @{ id = 'M2'; edits = @(
        @{ file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
           old = '            "not_found", "thread não encontrada.", withTraceId: false);'
           new = '            "not_found", "thread não encontrada.", withTraceId: true); // QA-MUTATION-M2: o 404 da thread vaza traceId' }
       )
       expect = 'A_third_user_shares|Repeating_a_reply'
       why = 'AC-02/AC-04: o 404 ganha traceId - os corpos deixam de ser byte-identicos e a recusa ensina algo' }

    # AC-02/AC-04 - the participant wall on the READ route: with the probe dead, Carol reads the
    # history of A/B (and the absent thread answers 200 instead of the shared 404).
    @{ id = 'M3'; edits = @(
        @{ file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
           old = (@(
                  '        var reader = ContainerEndpoints.ActorId(context, current.Handle);',
                  '        if (!await store.IsParticipantAsync(id, reader))',
                  '        {',
                  '            return HiddenThread(context);',
                  '        }'
                 ) -join "`n")
           new = (@(
                  '        var reader = ContainerEndpoints.ActorId(context, current.Handle);',
                  '        if (false && !await store.IsParticipantAsync(id, reader)) // QA-MUTATION-M3: a parede de leitura do GET desligada',
                  '        {',
                  '            return HiddenThread(context);',
                  '        }'
                 ) -join "`n") }
       )
       expect = 'A_third_user_shares|Repeating_a_reply'
       why = 'AC-02/AC-04: o nao-participante LE o historico da thread alheia (GET devolve 200 em vez de 404)' }

    # AC-03 - GAP-29's total order: the CAS increments message_seq and RETURNING hands back the
    # number THIS update took. Stop the increment and every send collides on the same number -
    # the UNIQUE (thread_id, seq) of 0003 then refuses the second row.
    @{ id = 'M4'; edits = @(
        @{ file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
           old = (@(
                  '                update.CommandText = """',
                  '                    UPDATE thread',
                  '                    SET message_seq = message_seq + 1, version = version + 1',
                  '                    WHERE id = @Id AND version = @Version',
                  '                    RETURNING message_seq',
                  '                    """;'
                 ) -join "`n")
           new = (@(
                  '                update.CommandText = """',
                  '                    UPDATE thread',
                  '                    SET message_seq = message_seq, version = version + 1',
                  '                    WHERE id = @Id AND version = @Version',
                  '                    RETURNING message_seq',
                  '                    """; // QA-MUTATION-M4: a alocacao de seq nao avanca - todo envio recebe o MESMO numero (colisao)'
                 ) -join "`n") }
       )
       expect = 'Concurrent_sends_take|Two_users_create_a_direct'
       why = 'AC-03: alocacao de seq quebrada - envios concorrentes colidem no mesmo numero (GAP-29 morto)' }

    # AC-04 - "repetir a resposta nao reescreve nada que ja existe": this mutant adds the write
    # that must not exist - the reply REWRITES the target row's reply_to_id, so the original
    # stops being the null the assertion reads.
    @{ id = 'M5'; edits = @(
        @{ file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
           old = (@(
                  '                await insert.ExecuteNonQueryAsync();',
                  '            }',
                  '',
                  '            // Commit is the one step that makes the number and its row real at the same instant.'
                 ) -join "`n")
           new = (@(
                  '                await insert.ExecuteNonQueryAsync();',
                  '            }',
                  '',
                  '            if (replyToId is not null)',
                  '            {',
                  '                await using (var rewrite = connection.CreateCommand())',
                  '                {',
                  '                    rewrite.Transaction = transaction;',
                  '                    rewrite.CommandText =',
                  '                        "UPDATE message SET reply_to_id = @NewId WHERE id = @ReplyToId";',
                  '                    rewrite.Parameters.AddWithValue("@NewId", messageId);',
                  '                    rewrite.Parameters.AddWithValue("@ReplyToId", replyToId);',
                  '                    await rewrite.ExecuteNonQueryAsync();',
                  '                } // QA-MUTATION-M5: a resposta REESCREVE a mensagem alvo',
                  '            }',
                  '',
                  '            // Commit is the one step that makes the number and its row real at the same instant.'
                 ) -join "`n") }
       )
       expect = 'Repeating_a_reply|The_full_flow_from_creation'
       why = 'AC-04: o reply reescreve reply_to_id da mensagem alvo - o estado pre-existente deixa de ficar intacto' }

    # AC-05a - ThreadRouteOf's marker order: the /participants/ marker must beat the /read tail,
    # otherwise a seat spelled `read` is parsed as the read route and the DELETE answers 404.
    @{ id = 'M6'; edits = @(
        @{ file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
           old = (@(
                  '        var marker = path.LastIndexOf(ParticipantsMarker, StringComparison.Ordinal);',
                  '        if (marker > 0)'
                 ) -join "`n")
           new = (@(
                  '        var marker = path.LastIndexOf(ParticipantsMarker, StringComparison.Ordinal);',
                  '        if (marker > 0 && !path.EndsWith(ReadSuffix, StringComparison.Ordinal)) // QA-MUTATION-M6: a cadeira read perde para o sufixo /read'
                 ) -join "`n") }
       )
       expect = 'A_seat_spelled_read'
       why = 'AC-05a: o sufixo /read engole a rota - o DELETE da cadeira read vira 404 em vez de 200' }

    # AC-05b - the ack row carries the FR-032 notice TEXT itself (read at the source, as the
    # QA-0036 temp test did); an empty string leaves the row without the words FR-032 registers.
    @{ id = 'M7'; edits = @(
        @{ file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
           old = '        await store.AckNoE2eeAsync(id, actorId, NoticeNoE2ee, time.GetUtcNow());'
           new = '        await store.AckNoE2eeAsync(id, actorId, string.Empty, time.GetUtcNow()); // QA-MUTATION-M7: o texto do aviso FR-032 nao vai para a linha do aceite' }
       )
       expect = 'The_ack_row_carries'
       why = 'AC-05b: a linha do aceite nasce sem o texto do aviso - a leitura na fonte nao encontra as palavras' }

    # AC-06 - GAP-25 ampliada pela TASK-0039: the message branch is what lets bia (participant,
    # NOT uploader) read the media her conversation references; kill it and only the participant
    # half of the facet falls to 404 (ana the uploader stays green in the same run).
    @{ id = 'M8'; edits = @(
        @{ file = 'backend\src\Squid.Api\Media\MediaEndpoints.cs'; expected = 1
           old = (@(
                  '        var actorId = ContainerEndpoints.ActorId(context, current.Handle);',
                  '        if (await threads.MessageMediaVisibleAsync(record.ContentHash, actorId)) return record;'
                 ) -join "`n")
           new = '        // QA-MUTATION-M8: ramo da MENSAGEM da regra GAP-25 desligado - AC-06 vermelha' }
       )
       expect = 'The_full_flow_from_creation'
       why = 'AC-06: o participante que NAO subiu a midia cai no 404 - o ramo mensagem da visibilidade sai' }

    # M9 - OBSERVATION (the dispatch's "un-monotonic marker" suggestion): drop GAP-38's guard so
    # the marker can move BACKWARDS. The facet never marks a smaller value after a bigger one
    # (every participant marks forward from 0), so this mutant is EXPECTED to survive the facet
    # while the TASK-0036 suite reddens - the documented boundary of what AC-01..AC-06 claim.
    @{ id = 'M9'; edits = @(
        @{ file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
           old = (@(
                  '                ON CONFLICT (thread_id, actor_id) DO UPDATE',
                  '                SET last_read_seq = excluded.last_read_seq',
                  '                WHERE excluded.last_read_seq > thread_read.last_read_seq'
                 ) -join "`n")
           new = (@(
                  '                ON CONFLICT (thread_id, actor_id) DO UPDATE',
                  '                SET last_read_seq = excluded.last_read_seq',
                  '                -- QA-MUTATION-M9: guarda de monotonicidade fora - o marcador pode regredir'
                 ) -join "`n") }
       )
       expect = 'SURVIVES-FACET'
       why = 'OBSERVACAO: marcador un-monotonico - a facet nao tem caso de regressao; quem pega e a suite da TASK-0036' }
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
    $null = & powershell -NoProfile -ExecutionPolicy Bypass -File $gate test -TestFilter $RunFilter 2>&1 | Out-String
    $code = $LASTEXITCODE
    $log = Get-ChildItem -LiteralPath $logs -Filter '*.log' |
        Where-Object { $_.LastWriteTime -gt $before } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -eq $log) {
        [pscustomobject]@{ Label = $Label; Wrapper = $code; Log = '(nenhum log novo)'; LogExit = $null; Failed = -1; Approved = -1; Failing = ''; Detail = ''; Facet = ''; Constraint = 'no' }
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
    # The assertion that actually broke - what tells apart two mutations landing in the SAME test.
    $detail = @()
    $lines = $text -split "`n"
    for ($i = 0; $i -lt $lines.Count -and $detail.Count -lt 2; $i++) {
        if ($lines[$i] -match 'Assert\.(Equal|All|Contains|DoesNotContain|True|False|Single|Throws)') {
            $detail += ($lines[$i].Trim() -replace '\s+', ' ')
            if (($i + 1) -lt $lines.Count) { $detail += ($lines[$i + 1].Trim() -replace '\s+', ' ') }
        }
    }
    # The FACET's own tests that went red - the whole point of this QA: Sprint4ClosingFacetTests
    # must fail when the behaviour it claims breaks (S-22: read inside the log).
    $facet = @()
    foreach ($match in [regex]::Matches($text, '(?:Com falha|\[FAIL\])[^\r\n]*Sprint4ClosingFacetTests\.(\w+)')) {
        $facet += $match.Groups[1].Value
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
        Facet     = (($facet | Select-Object -Unique) -join ',')
        Constraint = $constraint
    }
}

# -VerifyOnly: prove every pattern count against the pristine sources, run nothing.
if ($VerifyOnly) {
    $bad = 0
    foreach ($cycle in $cycles) {
        foreach ($edit in $cycle.edits) {
            $path = Join-Path $repo $edit.file
            $text = Read-Lf $path
            $old = ($edit.old -replace "`r`n", "`n")
            $hits = ([regex]::Matches($text, [regex]::Escape($old))).Count
            $expected = if ($edit.ContainsKey('expected')) { [int]$edit.expected } else { 1 }
            $ok = ($hits -eq $expected)
            if (-not $ok) { $bad++ }
            Write-Output ("{0} hits={1} expected={2} {3} {4}" -f $cycle.id, $hits, $expected, $(if ($ok) { 'OK' } else { 'FAIL' }), $edit.file)
        }
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
    Set-Content -LiteralPath $out -Value "# QA-0040 mutations - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -Encoding utf8
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
    Write-Log ("{0} EXPECT {1}" -f $cycle.id, $cycle.expect)
    Write-Log ("{0} FILTER {1}" -f $cycle.id, $runFilter)

    $run = Invoke-ModRun $cycle.id $runFilter
    $isProductRed = ($run.Failed -gt 0)
    Write-Log ("{0} RUN wrapper={1} logExit={2} aprovado={3} falha={4} constraint={5} log={6}" -f `
        $cycle.id, $run.Wrapper, $run.LogExit, $run.Approved, $run.Failed, $run.Constraint, (Split-Path $run.Log -Leaf))
    if ($run.Failing) { Write-Log ("{0} RED-IN-LOG {1}" -f $cycle.id, $run.Failing) }
    if ($run.Detail) { Write-Log ("{0} RED-DETAIL {1}" -f $cycle.id, $run.Detail) }
    if ($run.Facet) { Write-Log ("{0} RED-FACET {1}" -f $cycle.id, $run.Facet) }
    else { Write-Log ("{0} RED-FACET NONE - a facet nao falhou neste mutante" -f $cycle.id) }
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
$green = Invoke-ModRun 'GREEN' $filter
Write-Log ("GREEN wrapper={0} logExit={1} aprovado={2} falha={3} log={4}" -f `
    $green.Wrapper, $green.LogExit, $green.Approved, $green.Failed, (Split-Path $green.Log -Leaf))
if ($green.Failed -ne 0 -or $green.Wrapper -ne 0) { $failedCycles += 'GREEN' }

# Residue: no QA-MUTATION marker left anywhere in the backend (cs + sql), no temp backup of this QA.
$residues = Get-ChildItem -LiteralPath (Join-Path $repo 'backend') -Recurse -File -Include '*.cs', '*.sql' |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    Select-String -Pattern 'QA-MUTATION' -SimpleMatch
$backups = Get-ChildItem -Path $env:TEMP -Filter 'qa0040-*' -File -ErrorAction SilentlyContinue
Write-Log ("SUMMARY cycles={0} failed={1} residue-QA-MUTATION={2} temp-backups={3}" -f `
    $selected.Count, ($failedCycles | Select-Object -Unique).Count, @($residues).Count, @($backups).Count)

if ($failedCycles.Count -gt 0) { exit 1 }
exit 0
