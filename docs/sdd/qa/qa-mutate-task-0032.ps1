# QA-0032 - mutation driver of TASK-0032 (thumbnail no Worker, load test e facet de fecho).
# Six cycles, one behaviour each: SHA256 PRE (S-16) -> a single QA-MUTATION edit (the pattern
# count must be exactly 1) -> red run -> classification INSIDE the log file (S-22, never the
# wrapper's console exit) -> byte-exact restore from memory + mtime bump (S-23) -> hash = PRE
# -> marker residue 0. After the six: one green filtered run over the restored sources.
# M1/M2 mutate PRODUCT inside touches (Squid.Worker, Squid.Api/Media); M3..M6 mutate the
# expectations of the Load tests (tests/Squid.Tests/Load is in touches too) because the
# feed/timeline/SSE product paths live in Squid.Api/Posts and Squid.ReadModels - outside the
# touches this task may mutate.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0032.ps1
param([string[]]$Only = @())

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$gate = Join-Path $repo 'docs\sdd\_tools\sdd-test.ps1'
$logs = Join-Path $repo 'docs\sdd\runs\test-runs'
$out = Join-Path $PSScriptRoot 'QA-0032-mutations.log'
$filter = 'FullyQualifiedName~Squid.Tests.Load'

$cycles = @(
    @{ id = 'M1'; file = 'backend\src\Squid.Worker\Thumbnail\ThumbnailService.cs'
       old = 'var thumbnail = Attachment.KindOf(mimeType) is AttachmentKind.Image or AttachmentKind.Gif'
       new = 'var thumbnail = false && Attachment.KindOf(mimeType) is AttachmentKind.Image or AttachmentKind.Gif // QA-MUTATION-M1 imagens/GIF recebem o placeholder, nao o Skia'
       why = 'AC-01: todo registro vira o placeholder de 256x144 - o thumbnail gerado some' }
    @{ id = 'M2'; file = 'backend\src\Squid.Api\Media\MediaEndpoints.cs'
       old = 'if (record.UploaderHandle == current.Handle) return record;'
       new = 'if (current is not null) return record; // QA-MUTATION-M2 aro de visibilidade da rota de thumb desligado: qualquer sessao le qualquer hash'
       why = 'AC-01: o nao-membro lê o thumb que esta fora da vista (404 da TASK-0027 vira 200)' }
    @{ id = 'M3'; file = 'backend\tests\Squid.Tests\Load\FeedLoadTests.cs'
       old = 'Assert.True(feed.P95 < 1000,'
       new = 'Assert.True(feed.P95 < 0, // QA-MUTATION-M3 limite do P95 do feed fechado'
       why = 'AC-02: a assercao de P95 do feed deixa de poder falhar (prova que ela e avaliada)' }
    @{ id = 'M4'; file = 'backend\tests\Squid.Tests\Load\FeedLoadTests.cs'
       old = 'Assert.Equal(Items, await CountAsync(connection, "timeline_item"));'
       new = 'Assert.Equal(Items - 1, await CountAsync(connection, "timeline_item")); // QA-MUTATION-M4'
       why = 'AC-02: a conferencia COUNT(*)=10000 em timeline_item e trocada (prova que ela e avaliada)' }
    @{ id = 'M5'; file = 'backend\tests\Squid.Tests\Load\Sprint3ClosingFacetTests.cs'
       old = 'Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);'
       new = 'Assert.Equal(HttpStatusCode.OK, refused.StatusCode); // QA-MUTATION-M5 o 404 apos o unlisted e trocado'
       why = 'AC-03(c): a assercao do 404 pos-politica e trocada (prova que ela e avaliada)' }
    @{ id = 'M6'; file = 'backend\tests\Squid.Tests\Load\Sprint3ClosingFacetTests.cs'
       old = 'Assert.Equal(HttpStatusCode.OK, beforeHiding.StatusCode);'
       new = 'Assert.Equal(HttpStatusCode.NotFound, beforeHiding.StatusCode); // QA-MUTATION-M6 a pre-condicao 200-do-fora-e-trocada'
       why = 'AC-03(c): a pre-condicao de leitura publica ANTES do unlisted e trocada (prova que ela e avaliada)' }
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

Set-Content -LiteralPath $out -Value "# QA-0032 mutations - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -Encoding utf8
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
$backups = Get-ChildItem -Path $env:TEMP -Filter 'qa0032-*' -File -ErrorAction SilentlyContinue
Write-Log ("SUMMARY cycles={0} failed={1} residue-QA-MUTATION={2} temp-backups={3}" -f `
    $selected.Count, $failedCycles.Count, @($residues).Count, @($backups).Count)

if ($failedCycles.Count -gt 0) { exit 1 }
exit 0
