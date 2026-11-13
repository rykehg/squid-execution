# QA-0042 - mutation driver of TASK-0042 (chave de assinatura em runtime + assinar/verificar
# HTTP). Eighteen cycles, one acceptance clause each: SHA256 PRE (S-16) -> QA-MUTATION edit
# (pattern count must equal 1 BEFORE the run, checked against pristine sources) -> filtered red
# run -> classification INSIDE the log file (S-22, never the wrapper's console exit) -> byte-exact
# restore from memory + mtime bump (S-23) -> hash = PRE -> marker residue 0. After the eighteen:
# one green run over the restored sources (GREEN = the three new classes = 19 tests). Every file
# mutated is PRODUCT code inside this task's touches; no product file keeps a byte of this driver.
# THIS FILE IS ASCII-ONLY ON PURPOSE (no BOM needed, no accented pattern to mangle under
# Windows PowerShell 5.1). -VerifyOnly rejects any byte > 127.
# Targets:
#   T1  host warning text changed (AC-01 registers what is disabled)
#   T2  ring throws at boot without Squid:RecoveryKey (AC-01 instance boots)
#   T3  ring returns a fresh RSA instead of the unwrapped key (AC-02 unwrap roundtrip)
#   T4  boot warning always fires WITH the configured secret pasted in (AC-02 log channel)
#   T5  ActorGrain rethrows the raw crypto failure (AC-03 auditable typed failure)
#   T6  ring pastes the configured secret into the failure message (AC-03 no-leak)
#   T7  signer lines diverge from the declared headers order (AC-04 independent verifier)
#   T8  algorithm label becomes hs2019 (AC-04 contract 5 format)
#   T9  #main-key fragment check disabled (AC-04 keyId fragment)
#   T10 Digest becomes SHA-512 (AC-04 Digest: SHA-256=)
#   T11 Date becomes the "u" format instead of HTTP-date GMT (AC-04 Date GMT)
#   T12 GET declares " digest" in headers (AC-04 GET half)
#   T13 date window bypassed (AC-05 reject Date out of +-5 min)
#   T14 digest check dropped (AC-05 reject divergent Digest)
#   T15 RsaVerify always true (AC-05 reject key that is not the published one)
#   T16 draft labels rsa-sha256/hs2019 rejected (AC-05 accept draft, both cases)
#   T17 case "ed25519" disabled (AC-05 accept RFC 9421 Ed25519)
#   T18 RFC 9421 routed to the draft parser (AC-05 accept RFC 9421 RSA)
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0042.ps1 [-Only T1] [-VerifyOnly]
param([string[]]$Only = @(), [switch]$VerifyOnly)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$gate = Join-Path $repo 'docs\sdd\_tools\sdd-test.ps1'
$logs = Join-Path $repo 'docs\sdd\runs\test-runs'
$out = Join-Path $PSScriptRoot 'QA-0042-mutations.log'
$greenFilter = 'FullyQualifiedName~FederationRuntimeKeyTests|FullyQualifiedName~HttpSignatureTests|FullyQualifiedName~RuntimeSigningKeyRingTests'

$hostAc01 = 'FullyQualifiedName~FederationRuntimeKeyTests.Ac01_without_a_recovery_key'
$hostAc02 = 'FullyQualifiedName~FederationRuntimeKeyTests.Ac02_with_a_valid_recovery_key'
$hostAc03 = 'FullyQualifiedName~FederationRuntimeKeyTests.Ac03_with_a_wrong_recovery_key'
$pAc04Post = 'FullyQualifiedName~HttpSignatureTests.Ac04_post_signature_matches'
$pAc04Get = 'FullyQualifiedName~HttpSignatureTests.Ac04_get_signs_request_target'
$pAc04Frag = 'FullyQualifiedName~HttpSignatureTests.Ac04_a_key_id_without'
$pAc05Draft = 'FullyQualifiedName~HttpSignatureTests.Ac05_a_peers_draft_cavage'
$pAc05Date = 'FullyQualifiedName~HttpSignatureTests.Ac05_a_date_outside_the_window'
$pAc05Digest = 'FullyQualifiedName~HttpSignatureTests.Ac05_a_digest_that_no_longer'
$pAc05Key = 'FullyQualifiedName~HttpSignatureTests.Ac05_a_signature_of_a_key_that_is_not'
$pAc05Ed = 'FullyQualifiedName~HttpSignatureTests.Ac05_an_rfc9421_signature_with_ed25519'
$pAc05Rsa = 'FullyQualifiedName~HttpSignatureTests.Ac05_an_rfc9421_signature_with_rsa'

$svc = 'backend\src\Squid.Service\SquidServiceHost.cs'
$ring = 'backend\src\Squid.Api\Crypto\RuntimeSigningKeyRing.cs'
$grain = 'backend\src\Squid.Grains\Actors\ActorGrain.cs'
$signer = 'backend\src\Squid.Protocols\ActivityPub\HttpSignatureSigner.cs'
$verifier = 'backend\src\Squid.Protocols\ActivityPub\HttpSignatureVerifier.cs'

$cycles = @(
    # AC-01 half A - the warning is what registers "only the federated output is disabled".
    # Change its wording and the host test waits 30s for a phrase that never arrives.
    @{ id = 'T1'; edits = @(
        @{ file = $svc; expected = 1
           old = '                    "federation output disabled: Squid:RecoveryKey is missing or invalid; "'
           new = '                    "federation output now disabled: Squid:RecoveryKey is missing or invalid; "' }
        )
        filter = $hostAc01
        expect = 'Ac01_without_a_recovery_key'
        why = 'AC-01: o aviso muda de texto - a captura nunca encontra a frase que registra saida federada desabilitada' }

    # AC-01 half B - the instance must BOOT with no key. Throw at the config read and StartAsync dies.
    @{ id = 'T2'; edits = @(
        @{ file = $ring; expected = 1
           old = (@(
                  '        if (string.IsNullOrWhiteSpace(value))',
                  '        {',
                  '            return new RuntimeSigningKeyRing(null, configured: false);',
                  '        }'
                 ) -join "`n")
           new = (@(
                  '        if (string.IsNullOrWhiteSpace(value))',
                  '        {',
                  '            throw new FormatException("QA-MUTATION-T2: boot cai sem Squid:RecoveryKey"); // AC-01',
                  '        }'
                 ) -join "`n") }
        )
        filter = $hostAc01
        expect = 'Ac01_without_a_recovery_key'
        why = 'AC-01: sem RK a instancia deixa de subir - a excecao estoura no Build do host' }

    # AC-02 half A - the grain must open the WRAPPED key. Hand back a fresh RSA and the
    # signature no longer verifies against the public key the profile publishes.
    @{ id = 'T3'; edits = @(
        @{ file = $ring; expected = 1
           old = '        return KeyDerivation.UnwrapRsaPrivateKey(_rootKey, Convert.FromBase64String(wrappedSigningKey));'
           new = '        return RSA.Create(2048); // QA-MUTATION-T3: chave nova em vez da desembrulhada' }
        )
        filter = $hostAc02
        expect = 'Ac02_with_a_valid_recovery_key'
        why = 'AC-02: o desembrulho nao acontece - a assinatura nao confere com a chave publicada do ator' }

    # AC-02 half B - memory only, never in a log. Fire the boot warning ALWAYS and paste the
    # configured secret into it: the log channel then carries the very secret the test forbids.
    @{ id = 'T4'; edits = @(
        @{ file = $svc; expected = 1
           old = '        if (!federationKeys.OutputEnabled)'
           new = '        if (true) // QA-MUTATION-T4: aviso sempre, com o segredo colado' }
        @{ file = $svc; expected = 1
           old = '                    + "local reads and writes keep working");'
           new = '                    + "local reads and writes keep working; key=" + configuration["Squid:RecoveryKey"]);' }
        )
        filter = $hostAc02
        expect = 'Ac02_with_a_valid_recovery_key'
        why = 'AC-02: o log de boot passa a carregar o Squid:RecoveryKey - o canal de log vaza material' }

    # AC-03 half A - the refusal must be the typed, auditable failure. Rethrow the raw crypto
    # exception and Assert.ThrowsAsync<SigningKeyActivationException> dies on the type.
    @{ id = 'T5'; edits = @(
        @{ file = $grain; expected = 1
           old = '                throw new SigningKeyActivationException(handle, failure);'
           new = '                throw; // QA-MUTATION-T5: falha crua, sem tipo auditavel' }
        )
        filter = $hostAc03
        expect = 'Ac03_with_a_wrong_recovery_key'
        why = 'AC-03: a recusa deixa de ser auditavel - a excecao tipada SigningKeyActivationException nao nasce' }

    # AC-03 half B - nothing leaks. Paste the configured secret into the inner failure the
    # grain wraps: the message channel of the test then carries the secret verbatim.
    @{ id = 'T6'; edits = @(
        @{ file = $ring; expected = 1
           old = '        return KeyDerivation.UnwrapRsaPrivateKey(_rootKey, Convert.FromBase64String(wrappedSigningKey));'
           new = (@(
                  '        try',
                  '        {',
                  '            return KeyDerivation.UnwrapRsaPrivateKey(_rootKey, Convert.FromBase64String(wrappedSigningKey));',
                  '        }',
                  '        catch (CryptographicException failure)',
                  '        {',
                  '            throw new CryptographicException(failure.Message + " key=" + Convert.ToBase64String(_rootKey!.AsSpan())); // QA-MUTATION-T6',
                  '        }'
                 ) -join "`n") }
        )
        filter = $hostAc03
        expect = 'Ac03_with_a_wrong_recovery_key'
        why = 'AC-03: a mensagem da falha passa a conter o segredo configurado - vazamento no canal da mensagem' }

    # AC-04 independent verifier - the signing string must be built in the order the headers
    # parameter DECLARES. Put the digest line before the date line and no verifier agrees.
    @{ id = 'T7'; edits = @(
        @{ file = $signer; expected = 1
           old = '            lines.Add($"digest: {digest}");'
           new = '            lines.Insert(2, $"digest: {digest}"); // QA-MUTATION-T7: linha fora da ordem declarada' }
        )
        filter = $pAc04Post
        expect = 'Ac04_post_signature_matches'
        why = 'AC-04: as linhas assinadas divergem da ordem declarada em headers - o verificador independente recusa' }

    # AC-04 format - algorithm="rsa-sha256" is what contract 5 fixes.
    @{ id = 'T8'; edits = @(
        @{ file = $signer; expected = 1
           old = '    public const string Algorithm = "rsa-sha256";'
           new = '    public const string Algorithm = "hs2019"; // QA-MUTATION-T8' }
        )
        filter = $pAc04Post
        expect = 'Ac04_post_signature_matches'
        why = 'AC-04: a saida sai com algorithm=hs2019 em vez de rsa-sha256 (contrato 5, ADR-28)' }

    # AC-04 keyId - the fragment is the contract, not the caller's choice.
    @{ id = 'T9'; edits = @(
        @{ file = $signer; expected = 1
           old = '        if (!request.KeyId.EndsWith(MainKeyFragment, StringComparison.Ordinal))'
           new = '        if (false && !request.KeyId.EndsWith(MainKeyFragment, StringComparison.Ordinal)) // QA-MUTATION-T9' }
        )
        filter = $pAc04Frag
        expect = 'Ac04_a_key_id_without'
        why = 'AC-04: keyId sem #main-key deixa de ser recusado - o signer assina com qualquer chave' }

    # AC-04 Digest - SHA-256 only; Mastodon rejects sha-512.
    @{ id = 'T10'; edits = @(
        @{ file = $signer; expected = 1
           old = '            : "SHA-256=" + Convert.ToBase64String(SHA256.HashData(request.Body));'
           new = '            : "SHA-512=" + Convert.ToBase64String(SHA256.HashData(request.Body)); // QA-MUTATION-T10' }
        )
        filter = $pAc04Post
        expect = 'Ac04_post_signature_matches'
        why = 'AC-04: a saida publica Digest: SHA-512= - o verificador independente (SHA-256) nao confere' }

    # AC-04 Date - HTTP-date in GMT ("R"), never another format.
    @{ id = 'T11'; edits = @(
        @{ file = $signer; expected = 1
           old = '        var date = request.Timestamp.ToUniversalTime().ToString("R", CultureInfo.InvariantCulture);'
           new = '        var date = request.Timestamp.ToUniversalTime().ToString("u", CultureInfo.InvariantCulture); // QA-MUTATION-T11' }
        )
        filter = $pAc04Post
        expect = 'Ac04_post_signature_matches'
        why = 'AC-04: a saida deixa de ser HTTP-date GMT - termina com Z em vez de GMT' }

    # AC-04 GET half - GET signs (request-target) host date, no digest.
    @{ id = 'T12'; edits = @(
        @{ file = $signer; expected = 1
           old = '        string? headerNames = "(request-target) host date";'
           new = '        string? headerNames = "(request-target) host date digest"; // QA-MUTATION-T12' }
        )
        filter = $pAc04Get
        expect = 'Ac04_get_signs_request_target'
        why = 'AC-04: GET passa a declarar digest em headers - o contrato exige so (request-target) host date' }

    # AC-05 rejection 1 - Date out of +-5 min. Drop the window comparison and the stale
    # message (whose signature is perfectly valid) sails through.
    @{ id = 'T13'; edits = @(
        @{ file = $verifier; expected = 1
           old = (@(
                  '        var stamped = new DateTimeOffset(DateTime.SpecifyKind(when, DateTimeKind.Utc));',
                  '        return Math.Abs((DateTimeOffset.UtcNow - stamped).TotalMinutes) > Window.TotalMinutes',
                  '            ? new(SignatureRejection.StaleDate)',
                  '            : Accepted;'
                 ) -join "`n")
           new = '        return Accepted; // QA-MUTATION-T13: janela de 5 min ignorada' }
        )
        filter = $pAc05Date
        expect = 'Ac05_a_date_outside_the_window'
        why = 'AC-05: Date fora da janela de +-5 min nao rejeita mais - replay aceito' }

    # AC-05 rejection 2 - the Digest must describe the body that arrived.
    @{ id = 'T14'; edits = @(
        @{ file = $verifier; expected = 1
           old = (@(
                  '        if (TryGetHeader(request, "digest", out var digest) && !MatchesDigest(digest, expected))',
                  '        {',
                  '            return new(SignatureRejection.DigestMismatch);',
                  '        }'
                 ) -join "`n")
           new = '        // QA-MUTATION-T14: corpo trocado nao rejeita mais (Digest nao conferido)' }
        )
        filter = $pAc05Digest
        expect = 'Ac05_a_digest_that_no_longer'
        why = 'AC-05: corpo divergente do Digest nao rejeita - corpo trocado no caminho e aceito' }

    # AC-05 rejection 3 - the signature must come from the key published at the keyId.
    @{ id = 'T15'; edits = @(
        @{ file = $verifier; expected = 1
           old = '            return rsa.VerifyData(signingString, signature, hash, padding);'
           new = '            return true; // QA-MUTATION-T15: qualquer assinatura RSA vale' }
        )
        filter = $pAc05Key
        expect = 'Ac05_a_signature_of_a_key_that_is_not'
        why = 'AC-05: assinatura de chave que nao e a publicada no keyId e aceita' }

    # AC-05 acceptance draft - rsa-sha256 AND hs2019 both accepted (theory, both cases red).
    @{ id = 'T16'; edits = @(
        @{ file = $verifier; expected = 1
           old = (@(
                  '        if (algorithm is not null',
                  '            && !algorithm.Equals("rsa-sha256", StringComparison.OrdinalIgnoreCase)',
                  '            && !algorithm.Equals("hs2019", StringComparison.OrdinalIgnoreCase))',
                  '        {'
                 ) -join "`n")
           new = (@(
                  '        if (true) // QA-MUTATION-T16: todo rotulo draft vira UnsupportedAlgorithm',
                  '        {'
                 ) -join "`n") }
        )
        filter = $pAc05Draft
        expect = 'Ac05_a_peers_draft_cavage'
        minFailed = 2
        why = 'AC-05: draft-cavage rsa-sha256 e hs2019 deixam de ser aceitos na entrada' }

    # AC-05 acceptance 9421 Ed25519 - the additional key of R-02.
    @{ id = 'T17'; edits = @(
        @{ file = $verifier; expected = 1
           old = '            case "ed25519":'
           new = '            case "ed25519-disabled": // QA-MUTATION-T17' }
        )
        filter = $pAc05Ed
        expect = 'Ac05_an_rfc9421_signature_with_ed25519'
        why = 'AC-05: Ed25519 deixa de ser aceito - cai no default UnsupportedAlgorithm' }

    # AC-05 acceptance 9421 RSA - a Signature-Input header must reach the RFC 9421 parser.
    @{ id = 'T18'; edits = @(
        @{ file = $verifier; expected = 1
           old = '            ? await VerifyRfc9421Async(request, signatureInput, resolveKey)'
           new = '            ? await VerifyDraftCavageAsync(request, resolveKey) // QA-MUTATION-T18: 9421 fora de cena' }
        )
        filter = $pAc05Rsa
        expect = 'Ac05_an_rfc9421_signature_with_rsa'
        why = 'AC-05: RFC 9421 com RSA vai parar no parser draft - o par sem keyid draft vira Malformed' }
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
        if ($lines[$i] -match 'Assert\.(Equal|All|Contains|DoesNotContain|True|False|Single|Throws|StartsWith)|SigningKeyActivationException|independent verifier') {
            $detail += ($lines[$i].Trim() -replace '\s+', ' ')
            if (($i + 1) -lt $lines.Count) { $detail += ($lines[$i + 1].Trim() -replace '\s+', ' ') }
        }
    }
    # The TESTS that went red - which AC's own scenario caught this mutant (S-22: read inside
    # the log file, never from the wrapper's console exit).
    $ac = @()
    foreach ($match in [regex]::Matches($text, '(?:Com falha|\[FAIL\])[^\r\n]*(?:FederationRuntimeKeyTests|HttpSignatureTests|RuntimeSigningKeyRingTests)\.(\w+)')) {
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
    Set-Content -LiteralPath $out -Value "# QA-0042 mutations - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -Encoding utf8
}
$failedCycles = @()
$selected = @(if ($Only.Count -gt 0) { $cycles | Where-Object { $Only -contains $_.id } } else { $cycles })

foreach ($cycle in $selected) {
    $runFilter = if ($cycle.ContainsKey('filter')) { $cycle.filter } else { $greenFilter }
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
    else { Write-Log ("{0} RED-AC NONE - nenhum teste da AC caiu neste mutante" -f $cycle.id) }
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
    $minFailed = if ($cycle.ContainsKey('minFailed')) { [int]$cycle.minFailed } else { 1 }
    if ($expectOk -and $run.Failed -lt $minFailed) { $expectOk = $false }
    if (-not $expectOk) {
        Write-Log ("{0} EXPECT-MISS: anchors '{1}' minFailed={2} vs RED-AC '{3}' falha={4}" -f $cycle.id, $cycle.expect, $minFailed, $run.Ac, $run.Failed)
        $failedCycles += $cycle.id
    } else {
        Write-Log ("{0} EXPECT-OK anchors='{1}' falha={2}" -f $cycle.id, $cycle.expect, $run.Failed)
    }
}

# One green over the restored sources: every mutation is back before the suite runs again (S-23).
$green = Invoke-ModRun 'GREEN' $greenFilter
Write-Log ("GREEN wrapper={0} logExit={1} aprovado={2} falha={3} log={4}" -f `
    $green.Wrapper, $green.LogExit, $green.Approved, $green.Failed, (Split-Path $green.Log -Leaf))
if ($green.Failed -ne 0 -or $green.Wrapper -ne 0) { $failedCycles += 'GREEN' }

# Residue: no QA-MUTATION marker left anywhere in the backend (cs), no temp backup of this QA.
$residues = Get-ChildItem -LiteralPath (Join-Path $repo 'backend') -Recurse -File -Include '*.cs' |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    Select-String -Pattern 'QA-MUTATION' -SimpleMatch
$backups = Get-ChildItem -Path $env:TEMP -Filter 'qa0042-*' -File -ErrorAction SilentlyContinue
Write-Log ("SUMMARY cycles={0} failed={1} residue-QA-MUTATION={2} temp-backups={3}" -f `
    $selected.Count, ($failedCycles | Select-Object -Unique).Count, @($residues).Count, @($backups).Count)

if ($failedCycles.Count -gt 0) { exit 1 }
exit 0
