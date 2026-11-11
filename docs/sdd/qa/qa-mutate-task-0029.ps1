# QA-0029 - mutation driver of TASK-0029 (unfurl + guarda SSRF).
# Six cycles, one behaviour each: SHA256 PRE (S-16) -> a single QA-MUTATION edit (the pattern
# count must be exactly 1) -> red run -> classification INSIDE the log file (S-22, never the
# wrapper's console exit) -> byte-exact restore from memory + mtime bump (S-23) -> hash = PRE
# -> marker residue 0. After the six: one green filtered run over the restored sources.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0029.ps1
param([string[]]$Only = @())

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$gate = Join-Path $repo 'docs\sdd\_tools\sdd-test.ps1'
$logs = Join-Path $repo 'docs\sdd\runs\test-runs'
$out = Join-Path $PSScriptRoot 'QA-0029-mutations.log'
$filter = 'FullyQualifiedName~Squid.Tests.Unfurl'

$cycles = @(
    @{ id = 'M1'; file = 'backend\src\Squid.Api\Posts\SsrfGuard.cs'
       old = 'bytes[0] == 100 && (bytes[1] & 0xC0) == 64;'
       new = 'false; // QA-MUTATION-M1 faixa CGNAT removida'
       why = 'AC-03: 100.64.0.0/10 deixa de ser recusada' }
    @{ id = 'M2'; file = 'backend\src\Squid.Api\Posts\LinkUnfurler.cs'
       old = 'return SsrfGuard.Allowed(addresses);'
       new = 'return addresses.Count >= 0; // QA-MUTATION-M2 respostas do resolver ignoradas'
       why = 'AC-03: IP privado RESOLVIDO deixa de recusar o hop' }
    @{ id = 'M3'; file = 'backend\src\Squid.Api\Posts\LinkUnfurler.cs'
       old = 'if (!await AllowedAsync(current, timeout.Token)) return null;'
       new = 'if (hop == 0 && !await AllowedAsync(current, timeout.Token)) return null; // QA-MUTATION-M3 sem revalidacao por salto'
       why = 'AC-03: redirecionamento para IP privado nao e revalidado no meio da cadeia' }
    @{ id = 'M4'; file = 'backend\src\Squid.Api\Posts\LinkUnfurler.cs'
       old = 'catch (Exception)'
       new = 'catch (Exception ex) when (ex is null) // QA-MUTATION-M4 fail-open removido'
       why = 'AC-02: timeout/erro estoura no publish em vez de falhar aberto' }
    @{ id = 'M5'; file = 'backend\src\Squid.Api\Posts\LinkUnfurler.cs'
       old = 'if (declared > _options.MaxBytes) return null;'
       new = 'if (false && declared > _options.MaxBytes) return null; // QA-MUTATION-M5 teto de Content-Length removido'
       why = 'AC-04: corpo declarado acima do teto passa a ser lido' }
    @{ id = 'M6'; file = 'backend\src\Squid.Api\Posts\LinkUnfurler.cs'
       old = 'Alt = og.Title,'
       new = 'Alt = string.Empty, // QA-MUTATION-M6 og:title descartado'
       why = 'AC-01: og:title deixa de chegar no attachment' }
)

function Write-Log([string]$Line) {
    Add-Content -LiteralPath $out -Value $Line -Encoding utf8
    Write-Output $Line
}

function Get-Hash([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Invoke-UnfurlRun([string]$Label) {
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

Set-Content -LiteralPath $out -Value "# QA-0029 mutations - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -Encoding utf8
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

    $run = Invoke-UnfurlRun $cycle.id
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
$green = Invoke-UnfurlRun 'GREEN'
Write-Log ("GREEN wrapper={0} logExit={1} aprovado={2} falha={3} log={4}" -f `
    $green.Wrapper, $green.LogExit, $green.Approved, $green.Failed, (Split-Path $green.Log -Leaf))
if ($green.Failed -ne 0 -or $green.Wrapper -ne 0) { $failedCycles += 'GREEN' }

# Residue: no QA-MUTATION marker left anywhere in the backend, no temp backup of this QA.
$residues = Get-ChildItem -LiteralPath (Join-Path $repo 'backend') -Recurse -File -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    Select-String -Pattern 'QA-MUTATION' -SimpleMatch
$backups = Get-ChildItem -Path $env:TEMP -Filter 'qa0029-*' -File -ErrorAction SilentlyContinue
Write-Log ("SUMMARY cycles={0} failed={1} residue-QA-MUTATION={2} temp-backups={3}" -f `
    $selected.Count, $failedCycles.Count, @($residues).Count, @($backups).Count)

if ($failedCycles.Count -gt 0) { exit 1 }
exit 0
