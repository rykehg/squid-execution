# QA-0038 - mutation driver of TASK-0038 (reaction: one row per (message, actor, emoji), repeat
# idempotent, instance whitelist 422 invalid_emoji re-read per request, delete takes the actor's
# own row, count derived from the rows, byte-identical 404 for the outsider on read AND both
# reaction routes, migration 0006 without any counter column).
# Eleven cycles, one acceptance behaviour each: SHA256 PRE (S-16) -> QA-MUTATION edit(s) (pattern
# count must equal `expected` on EVERY edit BEFORE the run, checked against pristine sources) ->
# filtered red run -> classification INSIDE the log file (S-22, never the wrapper's console exit)
# -> byte-exact restore from memory + mtime bump (S-23) -> hash = PRE -> marker residue 0.
# After the eleven: one green filtered run over the restored sources (the QA's temporary test is
# still in the tree at that point: GREEN = 38/38 = 36 DEV + 2 QA).
# Every file mutated is inside the task's touches (Squid.Api/Threads/**, Squid.Service/**,
# Squid.Persistence/schema/**); no product file keeps a byte of this driver after the run.
# Multiline patterns are matched in LF space and written back in the file's own line endings.
# THIS FILE MUST KEEP ITS UTF-8 BOM (QA-0034..0037 lesson: a no-BOM edit turns every accented
# pattern into ANSI and PATTERN-COUNT dies at hits=0). Check with -VerifyOnly before every run.
# Targets: M1 ON CONFLICT DO NOTHING neutralized (AC-01 repeat of the triple); M2 the key narrowed
# from the TRIPLE to (message, actor) - a second DIFFERENT emoji of the same actor must be its own
# row (AC-01, two files); M3 Allows never refuses (422 invalid_emoji gone, AC-02); M4 a STORED
# counter column added to 0006 - the migration test must catch any counter (AC-03 never source);
# M5 the 422 whitelist before the 404 probe (AC-04 ladder byte-identity); M6 the config read cached
# per process instead of per request (AC-02 second half, caught by the QA's own test); M7 the
# DELETE drops actor_id and eats somebody else's row (AC-03 delete removes OWN row only); M8 the
# participant wall off in MessageVisible (AC-04 outsider inside the routes); M9 the shared 404
# carrying traceId (AC-04 byte-identity); M10 GET opened on .../reactions (contract §6 gives the
# route no GET - QA Fact B); M11 the no-session answer moved off 401 (ladder first rung - Fact B).
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0038.ps1 [-Only M1] [-VerifyOnly]
param([string[]]$Only = @(), [switch]$VerifyOnly)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$gate = Join-Path $repo 'docs\sdd\_tools\sdd-test.ps1'
$logs = Join-Path $repo 'docs\sdd\runs\test-runs'
$out = Join-Path $PSScriptRoot 'QA-0038-mutations.log'
$filter = 'FullyQualifiedName~Squid.Tests.Threads'

$cycles = @(
    # AC-01 - the idempotence of the SAME triple lives in the ON CONFLICT DO NOTHING of the
    # insert: this mutant writes straight, so the repeat of (message, actor, emoji) hits the
    # primary key of 0006 and the second POST answers 500 instead of the same projection.
    @{ id = 'M1'; edits = @(
        @{ file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
           old = (@(
                  '                INSERT INTO message_reaction (message_id, actor_id, emoji, kind, created_at)',
                  '                VALUES (@MessageId, @ActorId, @Emoji, @Kind, @CreatedAt)',
                  '                ON CONFLICT (message_id, actor_id, emoji) DO NOTHING'
                 ) -join "`n")
           new = (@(
                  '                INSERT INTO message_reaction (message_id, actor_id, emoji, kind, created_at)',
                  '                VALUES (@MessageId, @ActorId, @Emoji, @Kind, @CreatedAt)',
                  '                -- QA-MUTATION-M1: idempotencia da trinca neutralizada (sem ON CONFLICT DO NOTHING)'
                 ) -join "`n") }
       )
       why = 'AC-01: repetir a MESMA trinca grava de novo e estoura a chave - a segunda POST vira 500 em vez de 200 idempotente' }

    # AC-01 - uniqueness IS the triple: narrowing the key of 0006 (and the ON CONFLICT target
    # that must match it) to (message, actor) makes a second DIFFERENT emoji of the same actor
    # a conflict: the row is never born and the projection shows only the first emoji.
    @{ id = 'M2'; edits = @(
        @{ file = 'backend\src\Squid.Persistence\schema\0006_message_reaction.sql'; expected = 1
           old = '    PRIMARY KEY (message_id, actor_id, emoji)'
           new = '    PRIMARY KEY (message_id, actor_id) -- QA-MUTATION-M2: chave ja nao e a trinca' },
        @{ file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
           old = '                ON CONFLICT (message_id, actor_id, emoji) DO NOTHING'
           new = '                ON CONFLICT (message_id, actor_id) DO NOTHING -- QA-MUTATION-M2' }
       )
       why = 'AC-01: unica deixou de ser a trinca - o SEGUNDO emoji diferente do mesmo ator e recusado em silencio (sem linha nova)' }

    # AC-02 - the house refusal itself: Allows() is what turns an emoji outside
    # Reactions:AllowedEmojis into 422 invalid_emoji on POST and DELETE (and on the empty value).
    @{ id = 'M3'; edits = @(
        @{ file = 'backend\src\Squid.Api\Threads\ReactionOptions.cs'; expected = 1
           old = '        !string.IsNullOrEmpty(emoji) && AllowedEmojis.Contains(emoji, StringComparer.Ordinal);'
           new = '        true; // QA-MUTATION-M3: whitelist nunca recusa - o 422 invalid_emoji some' }
       )
       why = 'AC-02: qualquer emoji (e o vazio) passa - 422 invalid_emoji nao existe mais em POST nem em DELETE' }

    # AC-03 / data-model 2.14 "nunca fonte" - the schema guard: this mutant gives the reaction
    # table a STORED counter (a VALID column: the comma must sit OUTSIDE the `--` comment and the
    # column def must come BEFORE the table constraint - the first two attempts got one of those
    # wrong, the syntax error reddened all 38 both times, the driver was corrected and the cycle
    # re-run with -Only M4). 0006's pragma_table_info list in the migration test is exact, so any
    # counter column added anywhere in this family turns THAT test red while the routes stay
    # green (the INSERT names its columns).
    @{ id = 'M4'; edits = @(
        @{ file = 'backend\src\Squid.Persistence\schema\0006_message_reaction.sql'; expected = 1
           old = (@(
                  '    PRIMARY KEY (message_id, actor_id, emoji)',
                  ');'
                 ) -join "`n")
           new = (@(
                  '    reaction_count INTEGER NOT NULL DEFAULT 0, -- QA-MUTATION-M4: contador armazenado (nunca fonte)',
                  '    PRIMARY KEY (message_id, actor_id, emoji)',
                  ');'
                 ) -join "`n") }
       )
       why = 'AC-03: contador ARMAZENADO na tabela de reacao - a migration tem que pegar qualquer coluna de contagem' }

    # AC-04 - the ladder 401 -> 404 -> 422: the single probe decides before the whitelist, so an
    # outsider with an INVALID emoji still gets the byte-identical 404 (it may not learn what the
    # instance allows). This mutant validates first, which teaches the outsider the whitelist.
    @{ id = 'M5'; edits = @(
        @{ file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
           old = (@(
                  '        var actorId = ContainerEndpoints.ActorId(context, current.Handle);',
                  '        if (!await store.MessageVisibleAsync(id, messageId, actorId))',
                  '        {',
                  '            return HiddenThread(context);',
                  '        }',
                  '',
                  '        var emoji = request?.Emoji?.Trim();',
                  '        if (!ReactionOptions.From(configuration).Allows(emoji))',
                  '        {',
                  '            return InvalidEmoji(context);',
                  '        }'
                 ) -join "`n")
           new = (@(
                  '        var actorId = ContainerEndpoints.ActorId(context, current.Handle);',
                  '        var emoji = request?.Emoji?.Trim(); // QA-MUTATION-M5: 422 ANTES da sonda de 404',
                  '        if (!ReactionOptions.From(configuration).Allows(emoji))',
                  '        {',
                  '            return InvalidEmoji(context);',
                  '        }',
                  '',
                  '        if (!await store.MessageVisibleAsync(id, messageId, actorId))',
                  '        {',
                  '            return HiddenThread(context);',
                  '        }'
                 ) -join "`n") }
       )
       why = 'AC-04: o estrangeiro com emoji FORA da lista aprende 422 invalid_emoji em vez do 404 identico (escada invertida)' }

    # AC-02 second half - ReactionOptions.From(IConfiguration) runs on EVERY request precisely
    # so a changed config source changes the accepted set without a restart. This mutant caches
    # the first read for the life of the process; the QA's own test mutates the SOURCE and must
    # see the route follow it (it will not).
    @{ id = 'M6'; edits = @(
        @{ file = 'backend\src\Squid.Api\Threads\ReactionOptions.cs'; expected = 1
           old = '    public const string Like = "👍";'
           new = (@(
                  '    public const string Like = "👍";',
                  '',
                  '    /// <summary>QA-MUTATION-M6: cache por processo - a config deixa de ser lida por requisicao.</summary>',
                  '    private static ReactionOptions? _qaCache;'
                 ) -join "`n") },
        @{ file = 'backend\src\Squid.Api\Threads\ReactionOptions.cs'; expected = 1
           old = '        ArgumentNullException.ThrowIfNull(configuration);'
           new = (@(
                  '        ArgumentNullException.ThrowIfNull(configuration);',
                  '        if (_qaCache is not null) { return _qaCache; } // QA-MUTATION-M6'
                 ) -join "`n") },
        @{ file = 'backend\src\Squid.Api\Threads\ReactionOptions.cs'; expected = 1
           old = '        return new ReactionOptions(allowed);'
           new = (@(
                  '        _qaCache = new ReactionOptions(allowed); // QA-MUTATION-M6',
                  '        return _qaCache;'
                 ) -join "`n") }
       )
       why = 'AC-02: whitelist lida UMA vez por processo - mutar a FONTE da config nao muda mais o conjunto aceito' }

    # AC-03 - delete removes the ACTOR'S OWN row for that emoji; another actor's row of the same
    # emoji is a different row and must survive. This mutant drops actor_id from the WHERE.
    @{ id = 'M7'; edits = @(
        @{ file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
           old = (@(
                  '                DELETE FROM message_reaction',
                  '                WHERE message_id = @MessageId AND actor_id = @ActorId AND emoji = @Emoji'
                 ) -join "`n")
           new = (@(
                  '                DELETE FROM message_reaction',
                  '                WHERE message_id = @MessageId AND emoji = @Emoji -- QA-MUTATION-M7: apaga a linha de qualquer ator'
                 ) -join "`n") }
       )
       why = 'AC-03: o DELETE do dono come TAMBEM a linha do par - a contagem cai para zero em vez de 1' }

    # AC-04 / 9.10 - MessageVisible is the ONE probe: thread + seat + message of THAT thread.
    # This mutant drops the seat, so a non-participant passes the wall and gets his reaction
    # written (200) instead of the byte-identical 404.
    @{ id = 'M8'; edits = @(
        @{ file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
           old = (@(
                  '                  AND EXISTS(',
                  '                      SELECT 1 FROM thread_participant',
                  '                      WHERE thread_id = @ThreadId AND actor_id = @ActorId',
                  '                  )'
                 ) -join "`n")
           new = '                  AND 1 = 1 /* QA-MUTATION-M8: parede de participantes desligada */' }
       )
       why = 'AC-04: o nao-participante PASSA a parede e reage com 200 - o 404 identico das rotas de reacao some' }

    # AC-04 - SC-005 byte-identity: HiddenThread answers without traceId so every refusal of the
    # thread family is byte-a-byte the same body. This mutant stamps traceId on it.
    @{ id = 'M9'; edits = @(
        @{ file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
           old = (@(
                  '    private static IResult HiddenThread(HttpContext context) =>',
                  '        AuthErrors.Write(context, StatusCodes.Status404NotFound,',
                  '            "not_found", "thread não encontrada.", withTraceId: false);'
                 ) -join "`n")
           new = (@(
                  '    private static IResult HiddenThread(HttpContext context) =>',
                  '        AuthErrors.Write(context, StatusCodes.Status404NotFound,',
                  '            "not_found", "thread não encontrada.", withTraceId: true); // QA-MUTATION-M9'
                 ) -join "`n") }
       )
       why = 'AC-04: 404 com traceId - a identidade byte-a-byte das seis recusas cai (cada corpo ganha um traceId proprio)' }

    # Contract 6 gives the reaction route POST and DELETE only: a GET on .../reactions must stay
    # the shared 404 of a tail no method of the contract owns. This mutant routes GET into the
    # message read, which answers 200 for a participant.
    @{ id = 'M10'; edits = @(
        @{ file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
           old = '        if (route is null || route.Tail != ThreadTail.Messages)'
           new = '        if (route is null || (route.Tail != ThreadTail.Messages && route.Tail != ThreadTail.Reactions)) // QA-MUTATION-M10' }
       )
       why = 'GET em .../reactions vira 200 (leitura de mensagem) em vez do 404 do contrato (Fact B da QA)' }

    # The ladder's first rung: no session on POST .../reactions answers 401 not_authenticated
    # BEFORE the probe or the whitelist (an emoji outside the list may not teach an outsider
    # anything, and a stranger may not learn the wall either). This mutant answers 404 instead.
    @{ id = 'M11'; edits = @(
        @{ file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
           old = (@(
                  '        var current = AuthSessions.Current(context);',
                  '        if (current is null)',
                  '        {',
                  '            return AuthErrors.Write(context, StatusCodes.Status401Unauthorized,',
                  '                "not_authenticated", "sessão inválida ou expirada.");',
                  '        }',
                  '',
                  '        var actorId = ContainerEndpoints.ActorId(context, current.Handle);',
                  '        if (!await store.MessageVisibleAsync(id, messageId, actorId))',
                  '        {',
                  '            return HiddenThread(context);',
                  '        }',
                  '',
                  '        var emoji = request?.Emoji?.Trim();'
                 ) -join "`n")
           new = (@(
                  '        var current = AuthSessions.Current(context);',
                  '        if (current is null)',
                  '        {',
                  '            return AuthErrors.Write(context, StatusCodes.Status404NotFound, // QA-MUTATION-M11: 401 vira 404',
                  '                "not_found", "sessão inválida ou expirada.");',
                  '        }',
                  '',
                  '        var actorId = ContainerEndpoints.ActorId(context, current.Handle);',
                  '        if (!await store.MessageVisibleAsync(id, messageId, actorId))',
                  '        {',
                  '            return HiddenThread(context);',
                  '        }',
                  '',
                  '        var emoji = request?.Emoji?.Trim();'
                 ) -join "`n") }
       )
       why = 'roda de 401 trocada: sem sessao a rota de reacao responde 404 em vez de 401 not_authenticated (Fact B da QA)' }
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
    # The filter rides as one argv token (no spaces, so no quoting needed).
    $null = & powershell -NoProfile -ExecutionPolicy Bypass -File $gate test -TestFilter $filter 2>&1 | Out-String
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
    # is what tells apart two mutations that land in the SAME test (M10 vs M11).
    $detail = @()
    $lines = $text -split "`n"
    for ($i = 0; $i -lt $lines.Count -and $detail.Count -lt 2; $i++) {
        if ($lines[$i] -match 'Assert\.(Equal|All|Contains|DoesNotContain|True|False|Single|Throws)') {
            $detail += ($lines[$i].Trim() -replace '\s+', ' ')
            if (($i + 1) -lt $lines.Count) { $detail += ($lines[$i + 1].Trim() -replace '\s+', ' ') }
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
    Set-Content -LiteralPath $out -Value "# QA-0038 mutations - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -Encoding utf8
}
$failedCycles = @()
$selected = @(if ($Only.Count -gt 0) { $cycles | Where-Object { $Only -contains $_.id } } else { $cycles })

foreach ($cycle in $selected) {
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

    $run = Invoke-ModRun $cycle.id
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
# The QA's temporary test is still in the tree here: GREEN = 38/38 (36 DEV + 2 QA).
$green = Invoke-ModRun 'GREEN'
Write-Log ("GREEN wrapper={0} logExit={1} aprovado={2} falha={3} log={4}" -f `
    $green.Wrapper, $green.LogExit, $green.Approved, $green.Failed, (Split-Path $green.Log -Leaf))
if ($green.Failed -ne 0 -or $green.Wrapper -ne 0) { $failedCycles += 'GREEN' }

# Residue: no QA-MUTATION marker left anywhere in the backend (cs + sql), no temp backup of this QA.
$residues = Get-ChildItem -LiteralPath (Join-Path $repo 'backend') -Recurse -File -Include '*.cs', '*.sql' |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    Select-String -Pattern 'QA-MUTATION' -SimpleMatch
$backups = Get-ChildItem -Path $env:TEMP -Filter 'qa0038-*' -File -ErrorAction SilentlyContinue
Write-Log ("SUMMARY cycles={0} failed={1} residue-QA-MUTATION={2} temp-backups={3}" -f `
    $selected.Count, ($failedCycles | Select-Object -Unique).Count, @($residues).Count, @($backups).Count)

if ($failedCycles.Count -gt 0) { exit 1 }
exit 0
