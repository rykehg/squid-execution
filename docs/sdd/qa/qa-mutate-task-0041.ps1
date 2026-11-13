# QA-0041 - mutation driver of TASK-0041 (dominio canonico e proxy). Twelve cycles, one
# acceptance behaviour each: SHA256 PRE (S-16) -> QA-MUTATION edit (pattern count must equal 1
# BEFORE the run, checked against pristine sources) -> filtered red run -> classification INSIDE
# the log file (S-22, never the wrapper's console exit) -> byte-exact restore from memory + mtime
# bump (S-23) -> hash = PRE -> marker residue 0. After the twelve: one green filtered run over the
# restored sources (GREEN = 4/4 = CanonicalOriginConfigTests). Every file mutated is PRODUCT code
# inside this task's touches; no product file keeps a byte of this driver.
# THIS FILE IS ASCII-ONLY ON PURPOSE (no BOM needed, no accented pattern to mangle under
# Windows PowerShell 5.1). -VerifyOnly rejects any byte > 127.
# Targets:
#   M1  CanonicalOrigin.Of forces https even with no config (AC-01 scheme+Host byte-identity)
#   M2  CanonicalOrigin.Domain always null - Instance:Domain ignored (AC-02)
#   M3  webfinger subject echoes the asked domain instead of the canonical (AC-02 subject, D-01)
#   M4a webfinger refuses the reached Host - only the canonical domain accepted (AC-02/D-01a)
#   M4b webfinger refuses the canonical domain - only the reached Host accepted (AC-02/D-01b)
#   M5  forwarded middleware installed WITHOUT the trust key (AC-03a default ignored)
#   M6  forwarded middleware NEVER installed even WITH the key (AC-03b honoured when enabled)
#   M7  webfinger 404 of a missing actor leaks the origin in the body (AC-04 FR-053)
#   M8  Discovery Origin bypasses the helper (AC-02 Discovery point; AC-01 stays green = proof
#       the bypass is byte-identical without config)
#   M9  Posts Origin bypasses the helper (AC-02 object point)
#   M10 Containers Origin bypasses the helper (AC-02 collection point)
#   M11 Threads Origin bypasses the helper (AC-02 thread/message point)
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0041.ps1 [-Only M1] [-VerifyOnly]
param([string[]]$Only = @(), [switch]$VerifyOnly)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$gate = Join-Path $repo 'docs\sdd\_tools\sdd-test.ps1'
$logs = Join-Path $repo 'docs\sdd\runs\test-runs'
$out = Join-Path $PSScriptRoot 'QA-0041-mutations.log'
$filter = 'FullyQualifiedName~CanonicalOriginConfigTests'

$cycles = @(
    # AC-01 - the no-config fallback must stay scheme + Host of the very request. Force https and
    # the whole canonical-id family (profile, container, post, thread) changes scheme byte-wise.
    @{ id = 'M1'; edits = @(
        @{ file = 'backend\src\Squid.Api\Discovery\CanonicalOrigin.cs'; expected = 1
           old = (@(
                  '    public static string Of(HttpContext context) =>',
                  '        Domain(context) is { } domain',
                  '            ? $"https://{domain}"',
                  '            : $"{context.Request.Scheme}://{context.Request.Host.Value}";'
                 ) -join "`n")
           new = (@(
                  '    public static string Of(HttpContext context) =>',
                  '        Domain(context) is { } domain',
                  '            ? $"https://{domain}"',
                  '            : $"https://{context.Request.Host.Value}"; // QA-MUTATION-M1: scheme da request ignorado'
                 ) -join "`n") }
       )
       expect = 'Ac01_without_configuration|Ac03_forwarded_headers'
       why = 'AC-01: sem config a origem vira https://host em vez de scheme+Host - a identidade byte-a-byte morre' }

    # AC-02 - Instance:Domain is opt-in: kill the read of the key and every published id falls
    # back to the asking host (localhost:5001) instead of https://squid.test.
    @{ id = 'M2'; edits = @(
        @{ file = 'backend\src\Squid.Api\Discovery\CanonicalOrigin.cs'; expected = 1
           old = (@(
                  '        var configured = context.RequestServices.GetService<IConfiguration>()?["Instance:Domain"]?.Trim();',
                  '        return string.IsNullOrEmpty(configured) ? null : configured;'
                 ) -join "`n")
           new = '        return null; // QA-MUTATION-M2: Instance:Domain ignorado' }
       )
       expect = 'Ac02_with_instance_domain'
       why = 'AC-02: a chave Instance:Domain deixa de ser lida - os ids nascem no Host da request' }

    # AC-02 subject (SPEC-0011 A / D-01): the JRD subject is always the canonical account, even
    # when an internal authority asked. Echo the asked domain and the second half of Ac02 dies.
    @{ id = 'M3'; edits = @(
        @{ file = 'backend\src\Squid.Api\Discovery\DiscoveryEndpoints.cs'; expected = 1
           old = '                subject = $"acct:{name}@{canonical ?? domain}",'
           new = '                subject = $"acct:{name}@{domain}", // QA-MUTATION-M3: subject ecoa o dominio pedido' }
       )
       expect = 'Ac02_with_instance_domain'
       why = 'AC-02/D-01: o subject volta acct:handle@localhost:5001 em vez do canonico @squid.test' }

    # AC-02 / D-01 half A: the reached Host is an owned domain (SPEC-0011 A clause). Drop that
    # branch and a request that arrives on its own Host gets 404 when no canonical is configured.
    @{ id = 'M4a'; edits = @(
        @{ file = 'backend\src\Squid.Api\Discovery\DiscoveryEndpoints.cs'; expected = 1
           old = (@(
                  '        var isOwn = string.Equals(domain, context.Request.Host.Value, StringComparison.OrdinalIgnoreCase)',
                  '            || (canonical is not null',
                  '                && string.Equals(domain, canonical, StringComparison.OrdinalIgnoreCase));'
                 ) -join "`n")
           new = (@(
                  '        var isOwn = canonical is not null',
                  '            && string.Equals(domain, canonical, StringComparison.OrdinalIgnoreCase); // QA-MUTATION-M4a: Host alcancado recusado'
                 ) -join "`n") }
       )
       expect = 'Ac01_without_configuration|Ac02_with_instance_domain|Ac03_forwarded_headers'
       why = 'AC-02/D-01a: o Host alcancado nao e mais aceito - sem Instance:Domain TODO webfinger vira 404' }

    # AC-02 / D-01 half B: the canonical domain is an owned domain. Drop that branch and the
    # internal authority can no longer resolve the account on the public domain.
    @{ id = 'M4b'; edits = @(
        @{ file = 'backend\src\Squid.Api\Discovery\DiscoveryEndpoints.cs'; expected = 1
           old = (@(
                  '        var isOwn = string.Equals(domain, context.Request.Host.Value, StringComparison.OrdinalIgnoreCase)',
                  '            || (canonical is not null',
                  '                && string.Equals(domain, canonical, StringComparison.OrdinalIgnoreCase));'
                 ) -join "`n")
           new = '        var isOwn = string.Equals(domain, context.Request.Host.Value, StringComparison.OrdinalIgnoreCase); // QA-MUTATION-M4b: dominio canonico recusado' }
       )
       expect = 'Ac02_with_instance_domain'
       why = 'AC-02/D-01b: o dominio canonico nao e mais aceito - acct:handle@squid.test responde 404' }

    # AC-03 half A - default: the middleware must NOT exist without Instance:ForwardedHeaders.
    # Install it unconditionally and a forged X-Forwarded-Host rewrites the origin by default.
    @{ id = 'M5'; edits = @(
        @{ file = 'backend\src\Squid.Service\SquidServiceHost.cs'; expected = 1
           old = '        var trustProxy = bool.TryParse(configuration["Instance:ForwardedHeaders"], out var forwarded) && forwarded;'
           new = '        var trustProxy = true; // QA-MUTATION-M5: X-Forwarded honrado SEM a chave de confianca' }
       )
       expect = 'Ac03_forwarded_headers'
       why = 'AC-03a: sem a chave os headers sao honrados - o Host forjado vira a origem publicada' }

    # AC-03 half B - with the key: the middleware must exist. Never install it and the trusted
    # proxy deployment silently publishes the internal authority.
    @{ id = 'M6'; edits = @(
        @{ file = 'backend\src\Squid.Service\SquidServiceHost.cs'; expected = 1
           old = '        var trustProxy = bool.TryParse(configuration["Instance:ForwardedHeaders"], out var forwarded) && forwarded;'
           new = '        var trustProxy = false; // QA-MUTATION-M6: middleware de proxy nunca instalado' }
       )
       expect = 'Ac03_forwarded_headers'
       why = 'AC-03b: com a chave o middleware nao instala - o proxy de confianca e ignorado' }

    # AC-04 / FR-053: the 404 of a missing actor must carry no body with the origin. This mutant
    # adds exactly the leak Ac04 forbids: the configured host then answers a body naming
    # squid.test while the plain host names its own authority - byte-identity AND AssertClean die.
    @{ id = 'M7'; edits = @(
        @{ file = 'backend\src\Squid.Api\Discovery\DiscoveryEndpoints.cs'; expected = 1
           old = (@(
                  '        var state = await actors.GetStateAsync(name);',
                  '        if (state is null)',
                  '        {',
                  '            return Results.NotFound();',
                  '        }',
                  '',
                  '        var origin = Origin(context);'
                 ) -join "`n")
           new = (@(
                  '        var state = await actors.GetStateAsync(name);',
                  '        if (state is null)',
                  '        {',
                  '            return Results.Text($"nao encontrado em {Origin(context)}", statusCode: StatusCodes.Status404NotFound); // QA-MUTATION-M7',
                  '        }',
                  '',
                  '        var origin = Origin(context);'
                 ) -join "`n") }
       )
       expect = 'Ac04_error_answers_carry_neither_the_configured_domain_nor_a_stack_trace'
       why = 'AC-04/FR-053: o 404 do webfinger passa a nomear a origem - o corpo deixa de ser identico e carrega o dominio' }

    # AC-02 per point - Discovery. Revert this ONE point to scheme+Host: with Instance:Domain the
    # profile id and the webfinger href fall back to localhost:5001. Without config the bypass is
    # byte-identical to the helper, so Ac01 staying green IS the AC-01 proof for this point.
    @{ id = 'M8'; edits = @(
        @{ file = 'backend\src\Squid.Api\Discovery\DiscoveryEndpoints.cs'; expected = 1
           old = '    private static string Origin(HttpContext context) => CanonicalOrigin.Of(context);'
           new = '    private static string Origin(HttpContext context) => $"{context.Request.Scheme}://{context.Request.Host.Value}"; // QA-MUTATION-M8' }
       )
       expect = 'Ac02_with_instance_domain'
       why = 'AC-02 ponto Discovery: o profile id e o href voltam ao Host da request em vez de squid.test' }

    # AC-02 per point - Posts (object id, author, containerId, activity ActorId).
    @{ id = 'M9'; edits = @(
        @{ file = 'backend\src\Squid.Api\Posts\PostEndpoints.cs'; expected = 1
           old = '    internal static string Origin(HttpContext context) => CanonicalOrigin.Of(context);'
           new = '    internal static string Origin(HttpContext context) => $"{context.Request.Scheme}://{context.Request.Host.Value}"; // QA-MUTATION-M9' }
       )
       expect = 'Ac02_with_instance_domain'
       why = 'AC-02 ponto Posts: o id do objeto nasce no Host da request em vez de squid.test' }

    # AC-02 per point - Containers (collection id).
    @{ id = 'M10'; edits = @(
        @{ file = 'backend\src\Squid.Api\Containers\ContainerEndpoints.cs'; expected = 1
           old = '    private static string Origin(HttpContext context) => CanonicalOrigin.Of(context);'
           new = '    private static string Origin(HttpContext context) => $"{context.Request.Scheme}://{context.Request.Host.Value}"; // QA-MUTATION-M10' }
       )
       expect = 'Ac02_with_instance_domain'
       why = 'AC-02 ponto Containers: o id da colecao nasce no Host da request em vez de squid.test' }

    # AC-02 per point - Threads (thread id and message id).
    @{ id = 'M11'; edits = @(
        @{ file = 'backend\src\Squid.Api\Threads\ThreadEndpoints.cs'; expected = 1
           old = '    private static string Origin(HttpContext context) => CanonicalOrigin.Of(context);'
           new = '    private static string Origin(HttpContext context) => $"{context.Request.Scheme}://{context.Request.Host.Value}"; // QA-MUTATION-M11' }
       )
       expect = 'Ac02_with_instance_domain'
       why = 'AC-02 ponto Threads: o id da thread e da mensagem nascem no Host da request em vez de squid.test' }
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
        [pscustomobject]@{ Label = $Label; Wrapper = $code; Log = '(nenhum log novo)'; LogExit = $null; Failed = -1; Approved = -1; Failing = ''; Detail = ''; Ac = ''; Constraint = 'no' }
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
        if ($lines[$i] -match 'Assert\.(Equal|All|Contains|DoesNotContain|True|False|Single|Throws|StartsWith)') {
            $detail += ($lines[$i].Trim() -replace '\s+', ' ')
            if (($i + 1) -lt $lines.Count) { $detail += ($lines[$i + 1].Trim() -replace '\s+', ' ') }
        }
    }
    # The CONFIG TESTS that went red - which AC's own scenario caught this mutant (S-22: read
    # inside the log, never from the wrapper's console code).
    $ac = @()
    foreach ($match in [regex]::Matches($text, '(?:Com falha|\[FAIL\]|Falha |Failed )[^\r\n]*CanonicalOriginConfigTests\.(\w+)')) {
        $ac += $match.Groups[1].Value
    }
    $constraint = if ($text -match 'UNIQUE constraint failed|duplicate column name|near "') { 'yes' } else { 'no' }
    [pscustomobject]@{
        Label     = $Label
        Wrapper   = $code
        Log       = $log.FullName
        LogExit   = $logExit
        Failed    = $failed
        Approved  = $approved
        Failing   = (($failing | Select-Object -First 6) -join ' | ')
        Detail    = (($detail | Select-Object -First 2) -join ' ~ ')
        Ac        = (($ac | Select-Object -Unique) -join ',')
        Constraint = $constraint
    }
}

# -VerifyOnly: prove every pattern count against the pristine sources, prove ASCII, run nothing.
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
    $bytes = [System.IO.File]::ReadAllBytes($PSCommandPath)
    $nonAscii = @($bytes | Where-Object { $_ -gt 127 }).Count
    Write-Output ("ASCII non-ascii-bytes={0} {1}" -f $nonAscii, $(if ($nonAscii -eq 0) { 'OK (no BOM needed)' } else { 'FAIL' }))
    Write-Output "VERIFY total=$($cycles.Count) bad=$bad"
    if ($bad -gt 0 -or $nonAscii -gt 0) { exit 1 }
    exit 0
}

# A selective re-run appends to the ledger; a full run starts it fresh.
if ($Only.Count -gt 0) {
    Write-Log "## re-execucao seletiva ($($Only -join ',')) - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
} else {
    Set-Content -LiteralPath $out -Value "# QA-0041 mutations - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -Encoding utf8
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
    if ($run.Ac) { Write-Log ("{0} RED-AC {1}" -f $cycle.id, $run.Ac) }
    else { Write-Log ("{0} RED-AC NONE - nenhum teste da config caiu neste mutante" -f $cycle.id) }
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

    # every cycle must redden at least the AC scenario it targets (expect anchors from the log)
    $expectOk = $true
    foreach ($anchor in ($cycle.expect -split '\|')) {
        if ($anchor -and $run.Ac -notmatch [regex]::Escape($anchor)) { $expectOk = $false }
    }
    if (-not $expectOk) {
        Write-Log ("{0} EXPECT-MISS: anchors '{1}' nao estao no RED-AC '{2}'" -f $cycle.id, $cycle.expect, $run.Ac)
        $failedCycles += $cycle.id
    } else {
        Write-Log ("{0} EXPECT-OK anchors='{1}'" -f $cycle.id, $cycle.expect)
    }
}

# One green over the restored sources: every mutation is back before the suite runs again (S-23).
$green = Invoke-ModRun 'GREEN' $filter
Write-Log ("GREEN wrapper={0} logExit={1} aprovado={2} falha={3} log={4}" -f `
    $green.Wrapper, $green.LogExit, $green.Approved, $green.Failed, (Split-Path $green.Log -Leaf))
if ($green.Failed -ne 0 -or $green.Wrapper -ne 0) { $failedCycles += 'GREEN' }

# Residue: no QA-MUTATION marker left anywhere in the backend (cs), no temp backup of this QA.
$residues = Get-ChildItem -LiteralPath (Join-Path $repo 'backend') -Recurse -File -Include '*.cs' |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    Select-String -Pattern 'QA-MUTATION' -SimpleMatch
$backups = Get-ChildItem -Path $env:TEMP -Filter 'qa0041-*' -File -ErrorAction SilentlyContinue
Write-Log ("SUMMARY cycles={0} failed={1} residue-QA-MUTATION={2} temp-backups={3}" -f `
    $selected.Count, ($failedCycles | Select-Object -Unique).Count, @($residues).Count, @($backups).Count)

if ($failedCycles.Count -gt 0) { exit 1 }
exit 0
