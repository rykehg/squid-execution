# QA-0034 - mutation driver of TASK-0034 (envio e leitura: seq by CAS with the INSERT in the
# same transaction, delta ?since= with nextSince, content 4000 sanitized, byte-identical 404).
# Thirteen cycles, one acceptance behaviour each: SHA256 PRE (S-16) -> a single QA-MUTATION edit
# (pattern count must equal `expected`, checked BEFORE the run) -> filtered red run ->
# classification INSIDE the log file (S-22, never the wrapper's console exit) -> byte-exact
# restore from memory + mtime bump (S-23) -> hash = PRE -> marker residue 0.
# After the thirteen: one green filtered run over the restored sources.
# Every file mutated is inside the task's touches (Squid.Api/Threads/**, Squid.Service/**,
# schema/**) - this QA needs no edit outside the touches. Multiline patterns are matched in LF
# space and written back in the file's own line endings, so CRLF sources stay byte-consistent.
# Targets: M1 a seq allocated without its message in the thread's delta (GAP-29); M2 the version
# CAS of NextMessageSeqAsync; M3 the message_seq increment (the UNIQUE of 0003 fires - the
# differential lives in qa-probe-task-0034-unique.ps1); M4..M6 the content ladder of data-model
# 2.5.1 (raw 4000, post-sanitize 4000, the sanitizer call); M7..M9 the delta boundary of contract
# 9.3 (exact since, non-numeric since, nextSince on an empty poll); M10..M13 the byte-identical
# 404 of FR-031 (participant guard, traceId, absent-vs-foreign body, 404 status).
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0034.ps1 [-Only M1] [-VerifyOnly]
param([string[]]$Only = @(), [switch]$VerifyOnly)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$gate = Join-Path $repo 'docs\sdd\_tools\sdd-test.ps1'
$logs = Join-Path $repo 'docs\sdd\runs\test-runs'
$out = Join-Path $PSScriptRoot 'QA-0034-mutations.log'
$filter = 'FullyQualifiedName~Squid.Tests.Threads'

$cycles = @(
    # AC-01/GAP-29 - the INSERT of AppendMessageAsync lands under another thread_id: the number
    # is allocated and committed, but the thread's own delta never sees the message.
    @{ id = 'M1'; file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
       old = 'insert.Parameters.AddWithValue("@ThreadId", threadId);'
       new = 'insert.Parameters.AddWithValue("@ThreadId", "http://qa-mutated.invalid/t/sumida"); // QA-MUTATION-M1'
       why = 'AC-01/GAP-29: seq alocada comits mas a mensagem nao aparece no delta da thread (ThreadStoreTests + ThreadMessageRoutesTests vermelhas)' }
    # AC-01/GAP-29 - NextMessageSeqAsync loses the version guard: a stale expectation wins.
    @{ id = 'M2'; file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
       old = (@(
              '            WHERE id = @Id AND version = @Version',
              '            RETURNING message_seq',
              '            """;',
              '        update.Parameters.AddWithValue("@Id", id);',
              '        update.Parameters.AddWithValue("@Version", expectedVersion);'
             ) -join "`n")
       new = (@(
              '            WHERE id = @Id /* QA-MUTATION-M2 */',
              '            RETURNING message_seq',
              '            """;',
              '        update.Parameters.AddWithValue("@Id", id);'
             ) -join "`n")
       why = 'AC-01: CAS do version desligado - versao velha vence, as duas corridas recebem numero (ThreadStoreTests vermelha)' }
    # AC-01 - the increment itself in AppendMessageAsync: RETURNING hands back the OLD number,
    # the second append collides and the UNIQUE(thread_id,seq) of 0003 is what refuses it.
    @{ id = 'M3'; file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
       old = '                    SET message_seq = message_seq + 1, version = version + 1'
       new = '                    SET message_seq = message_seq, version = version + 1 /* QA-MUTATION-M3 */'
       why = 'AC-01: sem incremento todo append devolve a mesma seq - o UNIQUE de 0003 dispara (constraint no log) e o resto vermelho' }
    # AC-02 - the raw cap of data-model 2.5.1: 4000 exactly is accepted, 4001 is not.
    @{ id = 'M4'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
       old = 'if (content.Length > MaxContentLength)'
       new = 'if (content.Length >= MaxContentLength) // QA-MUTATION-M4'
       why = 'AC-02: 4000 caracteres exatos viram 400 - o teste atCap cai (ThreadMessageRoutesTests vermelha)' }
    # AC-02 - the post-sanitization cap: escaping multiplies the text, 2.5.1 caps what persists.
    @{ id = 'M5'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
       old = (@(
              '        return sanitized.Length > MaxContentLength',
              '            ? Invalid(context,',
              '                $"content inválido: máximo de {MaxContentLength} caracteres após a sanitização")',
              '            : null;'
             ) -join "`n")
       new = '        return null; // QA-MUTATION-M5 limite pos-sanitizacao desligado'
       why = 'AC-02: 4000 ampersands explodem para 20000 pos-sanitizacao e sao aceitos - o 400 pos-cap cai (ThreadMessageRoutesTests vermelha)' }
    # AC-02/GAP-24 - the sanitizer call itself: raw text is stored (C0 kept, HTML unescaped).
    @{ id = 'M6'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
       old = 'sanitized = PostSanitizer.Sanitize(content);'
       new = 'sanitized = content; // QA-MUTATION-M6 sanitizer da rota desligado'
       why = 'AC-02: sem sanitizacao controles C0 nao vazam para 400, HTML cru e gravado - 3 asserts do teste de conteudo caem' }
    # AC-03 - the delta operator of contract 9.3: seq > since, never >= (the exact boundary).
    @{ id = 'M7'; file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
       old = 'WHERE thread_id = @ThreadId AND seq > @Since'
       new = 'WHERE thread_id = @ThreadId AND seq >= @Since /* QA-MUTATION-M7 */'
       why = 'AC-03: since exato deixa repetir o proprio item - since=3 devolve 3,4,5 e since=5 devolve [5] (ThreadMessageRoutesTests vermelha)' }
    # AC-03 - a since that is not a number must be 400, not a silent restart at 0.
    @{ id = 'M8'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
       old = '            return Invalid(context, "since inválido: use um inteiro maior ou igual a zero");'
       new = '            // QA-MUTATION-M8 since nao-numerico aceito (o out deixa 0)'
       why = 'AC-03: ?since=abc vira 200 com o historico inteiro em vez de 400 - o assert final do delta cai (ThreadMessageRoutesTests vermelha)' }
    # AC-03 - nextSince of an empty poll keeps the cursor where it was.
    @{ id = 'M9'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
       old = 'nextSince = items.Count > 0 ? items[^1].Seq : since,'
       new = 'nextSince = items.Count > 0 ? items[^1].Seq : since + 1, // QA-MUTATION-M9'
       why = 'AC-03: polling sem novidade avanca o cursor - since=5 devolve nextSince 6 (ThreadMessageRoutesTests vermelha)' }
    # AC-04/FR-031 - the participant probe of BOTH routes: without it a stranger reads and sends.
    @{ id = 'M10'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 2
       old = 'if (!await store.IsParticipantAsync('
       new = 'if (false && await store.IsParticipantAsync('
       why = 'AC-04: portao de participante desligado nas duas rotas - nao-participante le e envia 200 em vez de 404 (ThreadMessageRoutesTests vermelha)' }
    # AC-04 - the 404 body without traceId: a per-request id breaks the byte-identical pair.
    @{ id = 'M11'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
       old = 'withTraceId: false);'
       new = 'withTraceId: true); // QA-MUTATION-M11'
       why = 'AC-04: traceId por request no 404 - os corpos nao sao mais byte-identicos e o DoesNotContain cai (ThreadMessageRoutesTests vermelha)' }
    # AC-04 - the POST refusal of a foreign thread answers a body of its own, so the GET vs POST
    # pair stops matching byte for byte (v2 - the v1 edit hit the malformed-tail branch that an
    # absent-but-well-formed thread id never reaches: survived, CLASSIFICACAO-ERRADA, see doc).
    @{ id = 'M12'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
       old = (@(
              '        var actorId = ContainerEndpoints.ActorId(context, current.Handle);',
              '        if (!await store.IsParticipantAsync(id, actorId))',
              '        {',
              '            return HiddenThread(context);',
              '        }'
             ) -join "`n")
       new = (@(
              '        var actorId = ContainerEndpoints.ActorId(context, current.Handle);',
              '        if (!await store.IsParticipantAsync(id, actorId))',
              '        {',
              '            return Results.Json(',
              '                new { error = new { code = "not_found", message = "nao participante desta thread." } },',
              '                statusCode: StatusCodes.Status404NotFound); // QA-MUTATION-M12 POST alheio com corpo proprio',
              '        }'
             ) -join "`n")
       why = 'AC-04: recusa do POST com corpo proprio - bodies[0] vs bodies[1] e bodies[2] vs bodies[3] deixam de ser byte-identicos (ThreadMessageRoutesTests vermelha)' }
    # AC-04 - the status of the refusal: restricted resource answers 404, never 403 (9.1).
    @{ id = 'M13'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
       old = 'AuthErrors.Write(context, StatusCodes.Status404NotFound,'
       new = 'AuthErrors.Write(context, StatusCodes.Status403Forbidden, /* QA-MUTATION-M13 */'
       why = 'AC-04: 403 no lugar do 404 - os quatro asserts de status caem (ThreadMessageRoutesTests vermelha)' }
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
    # The UNIQUE of 0003 announcing itself in the failure output is the direct evidence that the
    # last line of defence exists and fires (probed end-to-end by qa-probe-task-0034-unique.ps1).
    $constraint = if ($text -match 'UNIQUE constraint failed') { 'yes' } else { 'no' }
    [pscustomobject]@{
        Label     = $Label
        Wrapper   = $code
        Log       = $log.FullName
        LogExit   = $logExit
        Failed    = $failed
        Approved  = $approved
        Failing   = (($failing | Select-Object -First 6) -join ' | ')
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
    Write-Output "VERIFY total=$($cycles.Count) bad=$bad"
    if ($bad -gt 0) { exit 1 }
    exit 0
}

# A selective re-run appends to the ledger; a full run starts it fresh.
if ($Only.Count -gt 0) {
    Write-Log "## re-execucao seletiva ($($Only -join ',')) - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
} else {
    Set-Content -LiteralPath $out -Value "# QA-0034 mutations - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -Encoding utf8
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
$backups = Get-ChildItem -Path $env:TEMP -Filter 'qa0034-*' -File -ErrorAction SilentlyContinue
Write-Log ("SUMMARY cycles={0} failed={1} residue-QA-MUTATION={2} temp-backups={3}" -f `
    $selected.Count, $failedCycles.Count, @($residues).Count, @($backups).Count)

if ($failedCycles.Count -gt 0) { exit 1 }
exit 0
