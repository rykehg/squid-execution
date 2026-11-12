# QA-0035 - mutation driver of TASK-0035 (participantes e admin: adicao group-only/admin-only
# com teto 50 e ordem Direct-400-antes-do-403, remocao com 409 last_participant e acesso
# imediato perdido, promover/demover com criador protegido, sucessao GAP-32 por joined_at,
# lista cheia de participantes no GET /api/threads e historico inteiro do late joiner GAP-40).
# Thirteen cycles, one acceptance behaviour each: SHA256 PRE (S-16) -> a single QA-MUTATION edit
# (pattern count must equal `expected`, checked BEFORE the run) -> filtered red run ->
# classification INSIDE the log file (S-22, never the wrapper's console exit) -> byte-exact
# restore from memory + mtime bump (S-23) -> hash = PRE -> marker residue 0.
# After the thirteen: one green filtered run over the restored sources.
# Every file mutated is inside the task's touches (Squid.Api/Threads/**, Squid.Service/**); the
# temporary QA test QaTask0035RemovedRoutesTests.cs rides every run and is removed after.
# Multiline patterns are matched in LF space and written back in the file's own line endings,
# so CRLF sources stay byte-consistent. THIS FILE MUST KEEP ITS UTF-8 BOM (QA-0034 lesson: a
# no-BOM edit turns every accented pattern into ANSI and PATTERN-COUNT dies at hits=0).
# Targets: M1 the Direct-400-before-admin-403 ladder (AC-01); M2 the 50-seat ceiling boundary;
# M3 the membership probe of the three participant routes (outsider/removed -> shared 404);
# M4 the message-route probes (removed seat loses read+write at once, AC-02); M5 the admin gate
# of a third-party removal; M6 the creator protected from removal (ladder AC-02/04); M7 the
# admin gate of POST/DELETE admin; M8 the creator protected from demotion (AC-03); M9 the
# 409 last_participant answer itself (GAP-38, contract 9.11 family); M10 the last-seat boundary
# in the store (seats <= 1); M11 the succession ORDER BY joined_at, rowid (AC-04 GAP-32);
# M12 the semi-join of GET /api/threads collapsed back to the caller-only bug; M13 a join-time
# filter that hides the history from a late joiner (AC-05 GAP-40).
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0035.ps1 [-Only M1] [-VerifyOnly]
param([string[]]$Only = @(), [switch]$VerifyOnly)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$gate = Join-Path $repo 'docs\sdd\_tools\sdd-test.ps1'
$logs = Join-Path $repo 'docs\sdd\runs\test-runs'
$out = Join-Path $PSScriptRoot 'QA-0035-mutations.log'
$filter = 'FullyQualifiedName~Squid.Tests.Threads'

$cycles = @(
    # AC-01 - the ladder order of POST .../participants: Direct refuses with 400 BEFORE the role
    # does (nobody in a Direct holds the admin seat - swapped, "Direct -> 400" becomes 403).
    @{ id = 'M1'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
       old = (@(
              '        if (!string.Equals(snapshot.Thread.Kind, "group", StringComparison.Ordinal))',
              '        {',
              '            return Invalid(context,',
              '                "Direct não aceita participantes novos: a rota vale só para group (GAP-40).");',
              '        }',
              '',
              '        if (!IsAdmin(snapshot, caller))',
              '        {',
              '            return AuthErrors.Write(context, StatusCodes.Status403Forbidden,',
              '                "policy_denied", "só um admin do grupo pode adicionar participantes.");',
              '        }'
             ) -join "`n")
       new = (@(
              '        if (!IsAdmin(snapshot, caller))',
              '        {',
              '            return AuthErrors.Write(context, StatusCodes.Status403Forbidden,',
              '                "policy_denied", "só um admin do grupo pode adicionar participantes.");',
              '        }',
              '',
              '        if (!string.Equals(snapshot.Thread.Kind, "group", StringComparison.Ordinal))',
              '        {',
              '            return Invalid(context,',
              '                "Direct não aceita participantes novos: a rota vale só para group (GAP-40).");',
              '        }'
             ) -join "`n")
       why = 'AC-01: ordem trocada - add em Direct por nao-admin vira 403 antes do 400 (ThreadParticipantRoutesTests vermelha)' }
    # AC-01 - the 50-seat ceiling boundary: a Group with 50 seats must NOT take a fifty-first.
    @{ id = 'M2'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
       old = '        if (participants.Count >= MaxGroupParticipants)'
       new = '        if (participants.Count > MaxGroupParticipants) // QA-MUTATION-M2'
       why = 'AC-01: teto 50 vira 51 - o grupo cheio aceita o proximo participante em vez de 400 (ThreadParticipantRoutesTests vermelha)' }
    # AC-01/02/03 - the membership probe shared by add/remove/admin: without it an outsider (and a
    # removed seat) walks the participant family instead of meeting the byte-identical 404.
    @{ id = 'M3'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 3
       old = '        if (snapshot is null || !snapshot.Participants.Any(participant => participant.ActorId == caller))'
       new = '        if (snapshot is null || snapshot.Participants is null) // QA-MUTATION-M3'
       why = 'AC-01/02/03: portao de participante desligado nas 3 rotas - fora-da-thread e removido seguem em vez de 404 identico (routes da QA e do DEV vermelhas)' }
    # AC-02 - the message-route probes: the removed seat must lose read AND write at once.
    @{ id = 'M4'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 2
       old = '        if (!await store.IsParticipantAsync('
       new = '        if (false && await store.IsParticipantAsync('
       why = 'AC-02: portao de participante desligado nas rotas de mensagem - removido le e envia 200 em vez de 404 byte-identico (ThreadParticipantRoutesTests + ThreadMessageRoutesTests vermelhas)' }
    # AC-02 - a common participant must never remove a third party (403 before any write).
    @{ id = 'M5'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
       old = (@(
              '        if (!string.Equals(target, caller, StringComparison.Ordinal))',
              '        {',
              '            if (!IsAdmin(snapshot, caller))',
              '            {',
              '                return AuthErrors.Write(context, StatusCodes.Status403Forbidden,',
              '                    "policy_denied", "só um admin do grupo pode remover outros participantes.");',
              '            }'
             ) -join "`n")
       new = (@(
              '        if (!string.Equals(target, caller, StringComparison.Ordinal))',
              '        {',
              '            if (false && !IsAdmin(snapshot, caller)) // QA-MUTATION-M5',
              '            {',
              '                return AuthErrors.Write(context, StatusCodes.Status403Forbidden,',
              '                    "policy_denied", "só um admin do grupo pode remover outros participantes.");',
              '            }'
             ) -join "`n")
       why = 'AC-02: membro comum remove terceiro em vez de 403 - a remocao de `two` por `one` passa (ThreadParticipantRoutesTests vermelha)' }
    # AC-02/04 - the creator protected from removal BY ANOTHER ADMIN (succession is the only road).
    @{ id = 'M6'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
       old = '            if (string.Equals(target, snapshot.Thread.CreatedBy, StringComparison.Ordinal))'
       new = '            if (false && string.Equals(target, snapshot.Thread.CreatedBy, StringComparison.Ordinal)) // QA-MUTATION-M6'
       why = 'AC-02/04: criador removivel por outro admin - a sucessao GAP-32 dispara na mao errada (ThreadParticipantRoutesTests vermelha)' }
    # AC-03 - ANY admin changes roles; a plain member gets 403 on both verbs.
    @{ id = 'M7'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
       old = (@(
              '        if (!IsAdmin(snapshot, caller))',
              '        {',
              '            return AuthErrors.Write(context, StatusCodes.Status403Forbidden,',
              '                "policy_denied", "só um admin do grupo pode mudar papéis de participante.");',
              '        }'
             ) -join "`n")
       new = (@(
              '        if (false && !IsAdmin(snapshot, caller)) // QA-MUTATION-M7',
              '        {',
              '            return AuthErrors.Write(context, StatusCodes.Status403Forbidden,',
              '                "policy_denied", "só um admin do grupo pode mudar papéis de participante.");',
              '        }'
             ) -join "`n")
       why = 'AC-03: qualquer autenticado promove/demove - membro comum ganha 200 em vez de 403 (ThreadParticipantRoutesTests vermelha)' }
    # AC-03 - the creator's seat never changes through the admin routes, by a third party.
    @{ id = 'M8'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
       old = '        if (!promote && string.Equals(target, snapshot.Thread.CreatedBy, StringComparison.Ordinal))'
       new = '        if (!promote && false && string.Equals(target, snapshot.Thread.CreatedBy, StringComparison.Ordinal)) // QA-MUTATION-M8'
       why = 'AC-03: criador demovivel pelo DELETE admin - papel do criador vira member em vez de 403 (ThreadParticipantRoutesTests vermelha)' }
    # GAP-38 / contract 9.11 family - the last seat answers 409 last_participant, not a 400.
    @{ id = 'M9'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
       old = (@(
              '            ParticipantRemoval.LastParticipant => AuthErrors.Write(context,',
              '                StatusCodes.Status409Conflict, "last_participant",'
             ) -join "`n")
       new = (@(
              '            ParticipantRemoval.LastParticipant => AuthErrors.Write(context,',
              '                StatusCodes.Status400BadRequest, "last_owner", // QA-MUTATION-M9'
             ) -join "`n")
       why = 'GAP-38: 409 last_participant vira 400 last_owner - status e codigo do §9.11 caem (ThreadParticipantRoutesTests vermelha)' }
    # The last-seat boundary in the store: seats <= 1 refuses, seats = 2 must still leave.
    @{ id = 'M10'; file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
       old = '        if (seats <= 1)'
       new = '        if (seats <= 2) // QA-MUTATION-M10'
       why = 'AC-02: penultimo participante ja recusado - o Direct 2->1 e a saida com 2 viram 409 em vez de 200 (ThreadParticipantRoutesTests vermelha)' }
    # AC-04 / GAP-32 - succession ranks by joined_at ASC with rowid breaking the tie.
    @{ id = 'M11'; file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 2
       old = '                    ORDER BY joined_at, rowid'
       new = '                    ORDER BY joined_at DESC, rowid DESC -- QA-MUTATION-M11'
       why = 'AC-04: sucessao invertida - o membro/admin mais NOVO assume (ThreadStoreTests e ThreadParticipantRoutesTests vermelhas)' }
    # The semi-join of GET /api/threads: filter the OUTER p by caller and the list collapses to
    # the caller's own seat - the pre-existing bug DEV fixed (data-model 2.7 wants the full list).
    @{ id = 'M12'; file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
       old = '            JOIN thread_participant AS p ON p.thread_id = t.id'
       new = '            JOIN thread_participant AS p ON p.thread_id = t.id AND p.actor_id = @ActorId -- QA-MUTATION-M12'
       why = 'lista cheia: participants volta a colapsar no proprio chamador - igualdade de 3 nomes do GET /api/threads cai (ThreadParticipantRoutesTests vermelha)' }
    # AC-05 / GAP-40 - a seat added later reads the WHOLE history, not a delta from its entry.
    @{ id = 'M13'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
       old = (@(
              '        var items = await store.ListMessagesAsync(id, since);',
              '        return Results.Json(new',
              '        {',
              '            items = items.Select(MessageView.Of).ToList(),'
             ) -join "`n")
       new = (@(
              '        var items = await store.ListMessagesAsync(id, since);',
              '        var qaSnap = await store.FindAsync(id); // QA-MUTATION-M13',
              '        var qaMe = qaSnap!.Participants.Single(participant =>',
              '            participant.ActorId == ContainerEndpoints.ActorId(context, current.Handle));',
              '        items = items.Where(item => item.CreatedAt >= qaMe.JoinedAt).ToList();',
              '        return Results.Json(new',
              '        {',
              '            items = items.Select(MessageView.Of).ToList(),'
             ) -join "`n")
       why = 'AC-05/GAP-40: historico comeca no joined_at do leitor - o late joiner deixa de enxergar seq 1,2 (ThreadParticipantRoutesTests vermelha)' }
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
    Set-Content -LiteralPath $out -Value "# QA-0035 mutations - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -Encoding utf8
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
$backups = Get-ChildItem -Path $env:TEMP -Filter 'qa0035-*' -File -ErrorAction SilentlyContinue
Write-Log ("SUMMARY cycles={0} failed={1} residue-QA-MUTATION={2} temp-backups={3}" -f `
    $selected.Count, $failedCycles.Count, @($residues).Count, @($backups).Count)

if ($failedCycles.Count -gt 0) { exit 1 }
exit 0
