# QA-0033 - mutation driver of TASK-0033 (nucleo da thread: migration 0002, store ETag de
# MessageSeq, POST/GET /api/threads). Twelve cycles, one acceptance behaviour each:
# SHA256 PRE (S-16) -> a single QA-MUTATION edit (pattern count must equal `expected`, default 1)
# -> red run -> classification INSIDE the log file (S-22, never the wrapper's console exit)
# -> byte-exact restore from memory + mtime bump (S-23) -> hash = PRE -> marker residue 0.
# After the twelve: one green filtered run over the restored sources.
# Every file mutated is inside the task's touches (schema/**, Squid.Api/Threads/**,
# Squid.Service/**) - with ONE declared exception: M10 temporarily edits the CSRF gate in
# Squid.Api/Auth/AuthSessionMiddleware.cs (outside the touches) because the AC-04 CSRF assert
# can only fail there; it is restored byte-exact like every other cycle and AC-05's final git
# scope check is what judges the outcome.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0033.ps1
param([string[]]$Only = @())

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$gate = Join-Path $repo 'docs\sdd\_tools\sdd-test.ps1'
$logs = Join-Path $repo 'docs\sdd\runs\test-runs'
$out = Join-Path $PSScriptRoot 'QA-0033-mutations.log'
$filter = 'FullyQualifiedName~Squid.Tests.Threads|FullyQualifiedName~SchemaMigrationTests'

$cycles = @(
    # AC-01 / GAP-29 - the ETag CAS on message_seq: drop the version guard from the UPDATE.
    @{ id = 'M1'; file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
       old = 'WHERE id = @Id AND version = @Version'
       new = 'WHERE id = @Id // QA-MUTATION-M1 CAS de version desligado'
       why = 'AC-01/GAP-29: qualquer escrita vence o ETag - dois emissores recebem a mesma seq (ThreadStoreTests vermelha)' }
    # AC-01 - the applier's idempotence: the recorded version is no longer skipped.
    @{ id = 'M2'; file = 'backend\src\Squid.Persistence\schema\SchemaMigrator.cs'; expected = 1
       old = '                continue; // already applied: the second run of the gate''s idempotence check'
       new = '                // QA-MUTATION-M2 skip removido: a segunda aplicacao roda de novo'
       why = 'AC-01: a re-aplicacao nao e mais vazia - registro duplicado derruba a segunda aplicacao (ThreadMigrationTests vermelha)' }
    # AC-01 - the 0002 script content: the actor_id index of GET /api/threads.
    @{ id = 'M3'; file = 'backend\src\Squid.Persistence\schema\0002_threads.sql'; expected = 1
       old = 'CREATE INDEX IF NOT EXISTS ix_thread_participant_actor ON thread_participant (actor_id);'
       new = '-- QA-MUTATION-M3 indice de actor_id removido'
       why = 'AC-01: o indice que a listagem usa some - Assert IndexExistsAsync falha (ThreadMigrationTests vermelha)' }
    # AC-02 - direct is exactly 2: widen the upper bound.
    @{ id = 'M4A'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
       old = '? participants.Count == 2'
       new = '? participants.Count >= 2 // QA-MUTATION-M4A'
       why = 'AC-02: direct com 3 participantes e aceito - o 400 da faixa nao cai (ThreadRoutesTests vermelha)' }
    # AC-02 - the range gate itself: out of range must refuse before writing.
    @{ id = 'M4B'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
       old = '        if (!inRange)'
       new = '        if (false && !inRange) // QA-MUTATION-M4B faixas desligadas'
       why = 'AC-02: todo pedido fora da faixa vira 201 - group com 1 participante passa (ThreadRoutesTests vermelha)' }
    # AC-02 - the group ceiling: 50 exactly is still in range.
    @{ id = 'M4C'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
       old = 'and <= MaxGroupParticipants;'
       new = 'and < MaxGroupParticipants; // QA-MUTATION-M4C'
       why = 'AC-02: o grupo de 50 e recusado - o teto exato cai (ThreadRoutesTests vermelha)' }
    # AC-02 - the group floor: 2 exactly is still in range.
    @{ id = 'M4D'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
       old = 'is >= MinGroupParticipants'
       new = 'is > MinGroupParticipants /* QA-MUTATION-M4D */'
       why = 'AC-02: o grupo de 2 (criador + 1) e recusado - o piso exato cai (ThreadRoutesTests vermelha)' }
    # AC-02 - the creator counts toward the range (and lands in the participant set).
    @{ id = 'M5'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
       old = 'var participants = new List<string> { creator };'
       new = 'var participants = new List<string>(); // QA-MUTATION-M5 criador nao conta'
       why = 'AC-02: o criador sai do conjunto - direct com 1 convidado vira 400 em vez de 201 (ThreadRoutesTests vermelha)' }
    # AC-02 / GAP-30 - the creator of a Group lands as admin.
    @{ id = 'M6'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
       old = 'kind == "group" && actorId == creator ? AdminRole : MemberRole,'
       new = 'MemberRole, // QA-MUTATION-M6 criador do group sem admin'
       why = 'AC-02: a linha do criador nasce member - o Assert do role admin falha (ThreadRoutesTests vermelha)' }
    # AC-03 - the list starts from the caller's own participant row.
    @{ id = 'M7'; file = 'backend\src\Squid.Service\SqliteThreadStore.cs'; expected = 1
       old = 'WHERE p.actor_id = @ActorId'
       new = 'WHERE 1 = 1 // QA-MUTATION-M7 participante nao filtra'
       why = 'AC-03: a listagem devolve threads alheias - a contagem e o DoesNotContain falham (ThreadRoutesTests vermelha)' }
    # AC-04 - the thread id follows the request scheme+Host (emenda P-03).
    @{ id = 'M8'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
       old = '$"{context.Request.Scheme}://{context.Request.Host.Value}";'
       new = '"http://qa-mutated.invalid"; // QA-MUTATION-M8 origem fixa'
       why = 'AC-04: o id nasce em dominio fixo - o Assert.StartsWith da origem falha (ThreadRoutesTests vermelha)' }
    # AC-04 - both routes answer 401 without a session.
    @{ id = 'M9'; file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 2
       old = 'StatusCodes.Status401Unauthorized'
       new = 'StatusCodes.Status403Forbidden /* QA-MUTATION-M9 */'
       why = 'AC-04: sem sessao a rota responde 403 em vez de 401 - os dois asserts anonimos falham (ThreadRoutesTests vermelha)' }
    # AC-04 (CSRF) - the gate itself lives in AuthSessionMiddleware, OUTSIDE the touches: one
    # temporary byte-exact-restored edit here (S-16/S-23) so the CSRF assert of the DEV's test
    # proves it can fail too; the final git scope check is what AC-05 judges.
    @{ id = 'M10'; file = 'backend\src\Squid.Api\Auth\AuthSessionMiddleware.cs'; expected = 1
       old = '            if (IsMutation(context.Request.Method)'
       new = '            if (false && IsMutation(context.Request.Method) /* QA-MUTATION-M10 */'
       why = 'AC-04: sem header CSRF a mutacao passa - o 403 da rota nao cai (ThreadRoutesTests vermelha)' }
)

function Write-Log([string]$Line) {
    Add-Content -LiteralPath $out -Value $Line -Encoding utf8
    Write-Output $Line
}

function Get-Hash([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Invoke-ModRun([string]$Label) {
    $before = (Get-ChildItem -LiteralPath $logs -Filter '*.log' | Sort-Object LastWriteTime -Descending | Select-Object -First 1).LastWriteTime
    # The filter rides as one argv token (no spaces, so no quoting needed; the `|` reaches the
    # child's -File parser intact - the same way the DEV's own filtered run passed it).
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
    [pscustomobject]@{
        Label    = $Label
        Wrapper  = $code
        Log      = $log.FullName
        LogExit  = $logExit
        Failed   = $failed
        Approved = $approved
        Failing  = (($failing | Select-Object -First 6) -join ' | ')
    }
}

# A selective re-run appends to the ledger; a full run starts it fresh.
if ($Only.Count -gt 0) {
    Write-Log "## re-execucao seletiva ($($Only -join ',')) - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
} else {
    Set-Content -LiteralPath $out -Value "# QA-0033 mutations - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -Encoding utf8
}
$failedCycles = @()
# @() around both branches: a single selected cycle would otherwise be a bare hashtable whose
# .Count is its KEY count (the ledger once printed cycles=6 for one cycle - cosmetic, fixed).
$selected = @(if ($Only.Count -gt 0) { $cycles | Where-Object { $Only -contains $_.id } } else { $cycles })

foreach ($cycle in $selected) {
    $path = Join-Path $repo $cycle.file
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $text = [System.Text.Encoding]::UTF8.GetString($bytes)
    $expected = if ($cycle.ContainsKey('expected')) { [int]$cycle.expected } else { 1 }
    $hits = ([regex]::Matches($text, [regex]::Escape($cycle.old))).Count
    if ($hits -ne $expected) {
        Write-Log ("{0} PATTERN-COUNT={1} (esperado {2}) em {3} - ciclo abortado" -f $cycle.id, $hits, $expected, $cycle.file)
        $failedCycles += $cycle.id
        continue
    }

    $pre = Get-Hash $path
    $mutated = $text.Replace($cycle.old, $cycle.new)
    [System.IO.File]::WriteAllText($path, $mutated, (New-Object System.Text.UTF8Encoding($false)))
    Write-Log ("{0} PRE {1} | editado {2} | {3}" -f $cycle.id, $pre.Substring(0, 12), $cycle.file, $cycle.why)

    $run = Invoke-ModRun $cycle.id
    $isProductRed = ($run.Failed -gt 0)
    Write-Log ("{0} RUN wrapper={1} logExit={2} aprovado={3} falha={4} log={5}" -f `
        $cycle.id, $run.Wrapper, $run.LogExit, $run.Approved, $run.Failed, (Split-Path $run.Log -Leaf))
    if ($run.Failing) { Write-Log ("{0} RED-IN-LOG {1}" -f $cycle.id, $run.Failing) }
    if (-not $isProductRed) {
        Write-Log ("{0} CLASSIFICACAO-ERRADA: o log nao mostra falha de produto (ambiente? S-22)" -f $cycle.id)
        $failedCycles += $cycle.id
    }

    # restore byte-exact from memory + mtime bump (S-23), then hash must equal PRE
    [System.IO.File]::WriteAllBytes($path, $bytes)
    (Get-Item -LiteralPath $path).LastWriteTime = Get-Date
    $post = Get-Hash $path
    $residue = ([regex]::Matches(
        ([System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($path))),
        'QA-MUTATION')).Count
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
$backups = Get-ChildItem -Path $env:TEMP -Filter 'qa0033-*' -File -ErrorAction SilentlyContinue
Write-Log ("SUMMARY cycles={0} failed={1} residue-QA-MUTATION={2} temp-backups={3}" -f `
    $selected.Count, $failedCycles.Count, @($residues).Count, @($backups).Count)

if ($failedCycles.Count -gt 0) { exit 1 }
exit 0
