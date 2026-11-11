# QA-0031 - mutation driver of TASK-0031 (denuncia, fila admin e suspensao).
# Five cycles, one behaviour each: SHA256 PRE (S-16) -> a single QA-MUTATION edit (the pattern
# count must be exactly 1) -> red run -> classification INSIDE the log file (S-22, never the
# wrapper's console exit) -> byte-exact restore from memory + mtime bump (S-23) -> hash = PRE
# -> marker residue 0. After the five: one green filtered run over the restored sources.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0031.ps1
param([string[]]$Only = @())

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$gate = Join-Path $repo 'docs\sdd\_tools\sdd-test.ps1'
$logs = Join-Path $repo 'docs\sdd\runs\test-runs'
$out = Join-Path $PSScriptRoot 'QA-0031-mutations.log'
$filter = 'FullyQualifiedName~Squid.Tests.Moderation'

$cycles = @(
    @{ id = 'M1'; file = 'backend\src\Squid.Service\SqliteReportStore.cs'
       old = 'if (existing is not null)'
       new = 'if (existing is not null && false) // QA-MUTATION-M1 GAP-28 desligado: o par vira uma segunda linha'
       why = 'AC-01: a repeticao do mesmo par cria uma segunda denuncia (201 + fila duplicada)' }
    @{ id = 'M2'; file = 'backend\src\Squid.Api\Admin\AdminEndpoints.cs'
       old = 'if (!IsOperator(configuration, current.Handle))'
       new = 'if (!IsOperator(configuration, current.Handle) && false) // QA-MUTATION-M2 gate do operador desligado'
       why = 'AC-02: a nao-operador a fila responde 200 em vez de 403 policy_denied' }
    @{ id = 'M3'; file = 'backend\src\Squid.Api\Admin\AdminEndpoints.cs'
       old = 'private const string ResolveDecision = "Resolve";'
       new = 'private const string ResolveDecision = "ResolveX"; // QA-MUTATION-M3 palavra da trilha trocada'
       why = 'AC-03: a linha Resolve some da trilha (FR-008, GAP-27)' }
    @{ id = 'M4'; file = 'backend\src\Squid.Api\Admin\AdminEndpoints.cs'
       old = 'if (post is not null)'
       new = 'if (post is not null && false) // QA-MUTATION-M4 resolve remove nao executa a remocao'
       why = 'AC-03: o resolve remove fecha a denuncia sem tombstone - o post continua lido' }
    @{ id = 'M5'; file = 'backend\src\Squid.Api\Admin\AdminEndpoints.cs'
       old = 'await statuses.SuspendAsync(authorHandle);'
       new = '// QA-MUTATION-M5 ban nao suspende o autor'
       why = 'AC-04: resolve ban nao muda o estado - a proxima publicacao do autor passa' }
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

Set-Content -LiteralPath $out -Value "# QA-0031 mutations - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -Encoding utf8
$failedCycles = @()
$selected = if ($Only.Count -gt 0) { $cycles | Where-Object { $Only -contains $_.id } } else { $cycles }

foreach ($cycle in $selected) {
    $path = Join-Path $repo $cycle.file
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $text = [System.Text.Encoding]::UTF8.GetString($bytes)
    $hits = ([regex]::Matches($text, [regex]::Escape($cycle.old))).Count
    if ($hits -ne 1) {
        Write-Log ("{0} PATTERN-COUNT={1} (esperado 1) em {2} - ciclo abortado" -f $cycle.id, $hits, $cycle.file)
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

# Residue: no QA-MUTATION marker left anywhere in the backend, no temp backup of this QA.
$residues = Get-ChildItem -LiteralPath (Join-Path $repo 'backend') -Recurse -File -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    Select-String -Pattern 'QA-MUTATION' -SimpleMatch
$backups = Get-ChildItem -Path $env:TEMP -Filter 'qa0031-*' -File -ErrorAction SilentlyContinue
Write-Log ("SUMMARY cycles={0} failed={1} residue-QA-MUTATION={2} temp-backups={3}" -f `
    $selected.Count, $failedCycles.Count, @($residues).Count, @($backups).Count)

if ($failedCycles.Count -gt 0) { exit 1 }
exit 0
