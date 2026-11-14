# qa-check-task-0048.ps1 — QA-0048 static verifier: every command/citation in the three
# runbooks of TASK-0048 checked against the repository (AC-01/03/04 static) plus the AC-02
# structural marks. PowerShell 5.1 compatible. Exit 0 = all PASS, else number of failures.
# Can fail by design: run it with -RunbooksDir pointing at a mutated copy (see QA-0048 doc).
param(
    [string]$RepoRoot  = 'C:\dev\squid-squad-net\squid-execution',
    [string]$RunbooksDir = ''
)
if ($RunbooksDir -eq '') { $RunbooksDir = Join-Path $RepoRoot 'docs\runbooks' }
$script:lines = New-Object System.Collections.Generic.List[string]
$script:fail  = 0

function Check([string]$name, [bool]$cond, [string]$detail = '') {
    if ($cond) { $script:lines.Add("PASS | $name") }
    else { $script:fail++; $script:lines.Add("FAIL | $name | $detail") }
}
function ReadRb([string]$name) {
    $p = Join-Path $RunbooksDir $name
    if (-not (Test-Path $p)) { return $null }
    Get-Content -Raw -Encoding UTF8 -LiteralPath $p
}

# ---------- collect source facts (the artifacts the runbook cites) ----------
$srcCs   = Get-ChildItem -Recurse -File (Join-Path $RepoRoot 'backend\src') -Filter *.cs
$srcAll  = Get-ChildItem -Recurse -File (Join-Path $RepoRoot 'backend\src') -Include *.cs,*.json -ErrorAction SilentlyContinue
$routeRe = 'Map(?:Get|Post|Put|Delete|Patch)\(\s*"([^"]+)"'
$routes  = @()
foreach ($f in $srcCs) {
    foreach ($m in [regex]::Matches((Get-Content -Raw -Encoding UTF8 -LiteralPath $f.FullName), $routeRe)) {
        $routes += $m.Groups[1].Value
    }
}
$routes = $routes | Sort-Object -Unique

function FindFileByName([string]$name) {
    $hits = Get-ChildItem -Recurse -File $RepoRoot -Filter $name -ErrorAction SilentlyContinue
    if ($hits) { return $hits[0] } else { return $null }
}
function MatchRoute([string]$path) {
    if ($path -eq '' -or $path -eq '/') { return $true }          # bare origin in prose: nothing to map
    if ($path -match '[…<>]') { return $true }                    # prose placeholder, not a command
    $segs = ($path.Trim('/')) -split '/'
    foreach ($r in $routes) {
        $rs = ($r.Trim('/')) -split '/'
        if ($rs.Count -ne $segs.Count) { continue }
        $ok = $true
        for ($i = 0; $i -lt $rs.Count; $i++) {
            if ($rs[$i] -match '^\{.*\}$') { continue }
            if ($rs[$i] -cne $segs[$i]) { $ok = $false; break }
        }
        if ($ok) { return $true }
    }
    return $false
}

# ---------- load runbooks ----------
$fed  = ReadRb 'federacao-interop.md'
$peer = ReadRb 'peer-entrega-travada.md'
$dlq  = ReadRb 'dlq-cheia.md'
$idx  = ReadRb 'README.md'
Check 'AC-01 federacao-interop.md existe' ($null -ne $fed) "ausente em $RunbooksDir"
Check 'AC-03 peer-entrega-travada.md existe' ($null -ne $peer) 'ausente'
Check 'AC-03 dlq-cheia.md existe' ($null -ne $dlq) 'ausente'
Check 'AC-04 README.md (indice) existe' ($null -ne $idx) 'ausente'
$all = (@($fed, $peer, $dlq, $idx) | Where-Object { $_ -ne $null }) -join "`n"

# ---------- AC-01: comandos dotnet -> projetos reais ----------
$projects = [regex]::Matches($all, 'dotnet run --project ([^\s]+)') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
foreach ($p in $projects) {
    $csproj = Join-Path $RepoRoot ($p -replace '/', '\')
    Check "projeto dotnet existe: $p" (Test-Path $csproj) "sem diretorio $p"
}
Check 'AC-01 runbook cita Squid.Service (silo A+B+dominio)' (([regex]::Matches($fed, 'dotnet run --project backend/src/Squid.Service')).Count -ge 3) 'menos de 3 boot de silo'
Check 'AC-01 runbook cita Squid.Worker (worker de projecao)' ($fed -match 'dotnet run --project backend/src/Squid\.Worker') 'ausente'
Check 'AC-01 runbook cita Squid.PeerClient (reproducir falha)' ($peer -match 'dotnet run --project backend/src/Squid\.PeerClient' -and $dlq -match 'dotnet run --project backend/src/Squid\.PeerClient') 'ausente'

# ---------- AC-01: gate tool path ----------
$m = [regex]::Matches($all, 'powershell -NoProfile -ExecutionPolicy Bypass -File ([^\s]+\.ps1)') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
foreach ($t in $m) {
    Check "ferramenta citada existe: $t" (Test-Path (Join-Path $RepoRoot ($t -replace '/', '\'))) 'ausente'
}

# ---------- AC-01: chaves de config citadas -> existem no codigo ----------
$keys = @{}
foreach ($bm in [regex]::Matches($all, '`([^`\r\n]+)`')) {
    $span = ($bm.Groups[1].Value -split '=')[0].Trim()
    if ($span -match '^[A-Za-z][A-Za-z0-9]*:[A-Z][A-Za-z0-9]*$') { $keys[$span] = $true }
}
foreach ($fm in [regex]::Matches($all, '--([A-Za-z]+:[A-Za-z0-9]+)=')) { $keys[$fm.Groups[1].Value] = $true }
foreach ($k in ($keys.Keys | Sort-Object)) {
    $hit = $false
    foreach ($f in $srcAll) { if ((Get-Content -Raw -Encoding UTF8 -LiteralPath $f.FullName).Contains('"' + $k + '"')) { $hit = $true; break } }
    Check "chave de config no codigo: $k" $hit 'nenhum backend/src cita a chave'
}

# ---------- AC-01: citacoes file:L -> arquivo existe, linha valida, claim certo ----------
$claims = @{
    'SquidServiceHost.cs:77'      = 'WithTracing'
    'SquidServiceHost.cs:93'      = 'Storage:Path'
    'SquidServiceHost.cs:227'     = 'DeliveryPumpSeconds'
    'SquidServiceHost.cs:256'     = 'UseLocalhostClustering'
    'SquidServiceHost.cs:282'     = 'Instance:ForwardedHeaders'
    'MediaOptions.cs:34'          = 'Media:BlobRoot'
    'RuntimeSigningKeyRing.cs:44' = 'FromConfiguration'
    'TrustedPeerHosts.cs:25'      = 'From(IConfiguration'
    'CanonicalOrigin.cs:28'       = 'static string Of'
    'TwoSiloHarness.cs:128'       = 'Configure<EndpointOptions>'
    'HttpPeerSender.cs:87'        = 'SendAsync'
}
$cites = [regex]::Matches($all, '`([A-Za-z0-9_\-./\\]+\.(?:cs|sql|json|yaml)):(\d+)`') |
    ForEach-Object { $_.Groups[1].Value + ':' + $_.Groups[2].Value } | Sort-Object -Unique
foreach ($c in $cites) {
    $base = ($c -split ':')[0]
    $ln   = [int]($c -split ':')[-1]
    $f    = FindFileByName ([System.IO.Path]::GetFileName($base))
    if ($null -eq $f) { Check "citacao existe: $c" $false "arquivo $base nao encontrado"; continue }
    $content = Get-Content -Encoding UTF8 -LiteralPath $f.FullName
    $inRange = $ln -le $content.Count
    Check "citacao dentro do arquivo: $c ($([System.IO.Path]::GetFileName($base)) tem $($content.Count) linhas)" $inRange "linha $ln > $($content.Count)"
    if ($inRange -and $claims.ContainsKey($c)) {
        Check "citacao confere com o claim: $c ~ '$($claims[$c])'" ($content[$ln - 1].Contains($claims[$c])) "linha $ln = '$($content[$ln - 1].Trim().Substring(0, [Math]::Min(80, $content[$ln - 1].Trim().Length)))'"
    }
}
foreach ($c in ($claims.Keys | Sort-Object)) {
    Check "claim do runbook presente no texto: $c" ($all -match [regex]::Escape($c)) 'citacao sumiu do runbook'
}

# ---------- AC-01: rotas HTTP dos curl -> mapeadas no codigo ----------
# Só endpoints de instancia (localhost/127.0.0.1/*.test): URL de fonte externa
# (github.com, friendica.net...) nao e rota do produto.
$urls = [regex]::Matches($all, 'https?://([A-Za-z0-9.\-\[\]:]+)(?::\d+)?(/[^\s"''`]*)') | ForEach-Object {
    $hostName = ($_.Groups[1].Value -replace ':\d+$', '')   # tira a porta capturada junto do host
    $p = ($_.Groups[2].Value -split '\?')[0]
    $p = $p.TrimEnd('.', ',', ')', ';', '\')
    $isLocal = ($hostName -match '^(localhost|127\.0\.0\.1|\[::1\])$') -or ($hostName -match '\.test$')
    if ($isLocal) { $p }
} | Sort-Object -Unique
foreach ($u in $urls) { Check "rota mapeada: $u" (MatchRoute $u) 'nenhum MapGet/MapPost casa com a rota' }

# ---------- AC-01: consultas SQL -> colunas da migration 0008 ----------
$schema = (Get-ChildItem -Recurse -File (Join-Path $RepoRoot 'backend\src\Squid.Persistence\schema') -Filter *.sql |
    ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }) -join "`n"
foreach ($sm in [regex]::Matches($all, '"select ([^"]+?) from ([a-z_]+)([^"]*)"')) {
    $table = $sm.Groups[2].Value
    $tail  = $sm.Groups[3].Value
    $tb = [regex]::Match($schema, "CREATE TABLE IF NOT EXISTS $table \(([^;]+)\);", 'Singleline')
    Check "tabela no schema: $table" $tb.Success 'CREATE TABLE ausente'
    if ($tb.Success) {
        $cols = @{}
        foreach ($cm in [regex]::Matches($tb.Groups[1].Value, '(?m)^\s*([a-z_]+)\s+(TEXT|INTEGER|REAL|BLOB|NUMERIC)')) { $cols[$cm.Groups[1].Value] = $true }
        $needed = @()
        foreach ($part in ($sm.Groups[1].Value -split ',')) { $t = ($part -split '\s+')[0].Trim(); if ($t -match '^[a-z_]+$' -and $t -ne 'count') { $needed += $t } }
        if ($tail -match 'group by ([a-z_]+)') { $needed += $Matches[1] }
        foreach ($wm in [regex]::Matches($tail, "where ([a-z_]+)=")) { $needed += $wm.Groups[1].Value }
        foreach ($n in ($needed | Sort-Object -Unique)) {
            Check "coluna $table.$n no schema" $cols.ContainsKey($n) 'coluna nao existe na migration'
        }
    }
}
Check 'AC-01 consulta de pares cobre colunas peer' ($all -match 'select domain, status, failure_count, backoff_until, rate_limit_per_minute from peer') 'consulta do runbook sumiu'

# ---------- AC-01: chaves/config dos passos do manual ----------
Check 'AC-01 gera RecoveryKey base64 de 32 bytes' ($fed -match 'New-Object byte\[\] 32' -and $fed -match 'ToBase64String') 'geracao de chave ausente'
Check 'AC-01 silo A porta 5601' ($fed -match '--urls http://127\.0\.0\.1:5601') 'boot do silo A ausente'
Check 'AC-01 silo B porta 5602 (distinta)' ($fed -match '--urls http://127\.0\.0\.1:5602') 'boot do silo B ausente'
Check 'AC-01 caminhos de banco/midia distintos A vs B' ($fed -match 'squid-a\.db' -and $fed -match 'squid-b\.db' -and $fed -match 'squid-a-blobs' -and $fed -match 'squid-b-blobs') 'paths nao distintos'
Check 'AC-01 Instance:Domain no boot do modo dominio' ($fed -match '--Instance:Domain=squid-a\.test') 'comando do modo dominio ausente'
Check 'AC-01 TrustedPeerHosts nomeia o par local' ($fed -match '--Federation:TrustedPeerHosts=127\.0\.0\.1,localhost') 'allowlist ausente'
Check 'AC-01 Worker com o mesmo Storage:Path do silo' ($fed -match 'Squid\.Worker -- --Storage:Path=\$env:TEMP\\squid-a\.db' -and $fed -match 'Squid\.Worker -- --Storage:Path=\$env:TEMP\\squid-b\.db') 'worker A/B ausente'
$ls = FindFileByName 'launchSettings.json'
Check 'AC-01 launchSettings.json (59311/59312) existe' ($null -ne $ls) 'ausente'
if ($ls) { $lst = Get-Content -Raw -Encoding UTF8 -LiteralPath $ls.FullName; Check 'AC-01 ports Orleans de dev citados 59311/59312' ($lst -match '59311' -and $lst -match '59312') 'portas erradas' }

# ---------- AC-01: pares de referencia (Docker) marcados + fontes oficiais ----------
Check 'AC-01 secao Mastodon/Friendica marcada GATE-HUMANO' ($fed -match 'Mastodon e Friendica em Docker local.*GATE-HUMANO') 'marca ausente'
Check 'AC-01 compose dos pares marcado EXTERNO + GATE-HUMANO' ($fed -match 'EXTERNO \+ GATE-HUMANO') 'marca ausente'
Check 'AC-01 fonte oficial Mastodon nomeada' ($fed -match 'https://github\.com/mastodon/mastodon') 'URL oficial ausente'
Check 'AC-01 fonte oficial Friendica nomeada' ($fed -match 'https://friendica\.net/') 'URL oficial ausente'
Check 'AC-01 repo nao traz compose dos pares (declarado)' ($fed -match 'não traz compose') 'afirmacao ausente'
$qs = FindFileByName 'quickstart.md'
Check 'AC-01 quickstart.md (fonte citada) existe' ($null -ne $qs) 'ausente'
if ($qs) {
    $qt = Get-Content -Raw -Encoding UTF8 -LiteralPath $qs.FullName
    Check 'quickstart cita mastodon-main 5.0.0-alpha.1' ($qt -match 'mastodon-main.*5\.0\.0-alpha\.1' -or ($qt -match '5\.0\.0-alpha\.1' -and $qt -match 'mastodon-main')) 'versao/dono errados'
    Check 'quickstart cita friendica-2026.08-rc' ($qt -match 'friendica-2026\.08-rc') 'versao ausente'
    Check 'quickstart fixa que Mastodon nao federa por localhost' ($qt -match 'não federa por `localhost`') 'regra ausente'
}
Check 'AC-01 hosts file apontando os dominios do par' ($fed -match 'squid-a\.test squid-b\.test mastodon\.test friendica\.test') 'passo de hosts ausente'

# ---------- AC-02: secao instancia publica real = gate humano ----------
Check 'AC-02 secao opcional instancia publica real' ($fed -match '### 7\. \[Opcional\] Instância pública real') 'heading ausente'
$sec7 = ''
$m7 = [regex]::Match($fed, '(?s)### 7\..*?(?=\r?\n## |\z)')
if ($m7.Success) { $sec7 = $m7.Value }
Check 'AC-02 secao 7 marcada GATE HUMANO' ($sec7 -match 'GATE HUMANO') 'marca ausente'
Check 'AC-02 secao 7 declarada gate humano no blockquote' ($sec7 -match 'Esta seção inteira é gate humano') 'frase ausente'
Check 'AC-02 secao 7 cobre dominio+TLS+tunel' ($sec7 -match 'Domínio' -and $sec7 -match 'TLS' -and $sec7 -match 'Túnel') 'item faltando'
Check 'AC-02 fora de escopo do MVP citado na secao' ($sec7 -match 'SPEC-0011 decisão 6') 'referencia a decisao ausente'

# ---------- AC-03: runbooks de fila viraram EXECUTABLE ----------
foreach ($pair in @(@('peer-entrega-travada.md', $peer), @('dlq-cheia.md', $dlq))) {
    $n = $pair[0]; $t = $pair[1]
    if ($null -eq $t) { continue }
    Check "AC-03 $n sem token PENDENTE" (-not ($t -cmatch 'PENDENTE')) 'contem marcador PENDENTE (case-sensitivo)'
    Check "AC-03 $n status EXECUTAVEL" ($t -match 'status: EXECUTÁVEL') 'linha de status nao e EXECUTAVEL'
    Check "AC-03 $n tem Sintoma/Diagnostico/Procedimento/Verificacao" (($t -match '## Sintoma') -and ($t -match '## Diagnóstico') -and ($t -match '## Procedimento') -and ($t -match '## Verificação')) 'secao faltando'
    $cmds = [regex]::Matches($t, '(?m)^\s*(python -c|dotnet run|powershell -NoProfile|curl\.exe)') | ForEach-Object { $_.Value.Trim() } | Sort-Object -Unique
    Check "AC-03 $n tem passo a passo comandos reais (>=3 tipos)" ($cmds.Count -ge 3) "apenas $($cmds.Count): $($cmds -join ', ')"
}
Check 'AC-03 peer cita PeerClient cenarios provados' ($peer -match '--scenario=500' -and $peer -match '--scenario=timeout' -and $peer -match 'slow') 'cenarios ausentes'
Check 'AC-03 dlq cita PeerClient cenario 401' ($dlq -match '--scenario=401') 'cenario ausente'
Check 'AC-03 dlq sem rota de requeue declarada' ($dlq -match 'não existe rota de requeue') 'declaracao ausente'

# ---------- AC-04: indice README ----------
$rows = @{}
foreach ($rm in [regex]::Matches($idx, '(?m)^\|\s*\[([a-z\-]+\.md)\]\([^)]*\)\s*\|([^|]*)\|([^|]*)\|([^|\r\n]*)\|')) {
    $rows[$rm.Groups[1].Value] = ($rm.Groups[3].Value + ' | ' + $rm.Groups[4].Value)
}
Check 'AC-04 peer-entrega-travada na tabela' $rows.ContainsKey('peer-entrega-travada.md') 'linha ausente'
Check 'AC-04 dlq-cheia na tabela' $rows.ContainsKey('dlq-cheia.md') 'linha ausente'
Check 'AC-04 federacao-interop na tabela' $rows.ContainsKey('federacao-interop.md') 'linha ausente'
if ($rows.ContainsKey('peer-entrega-travada.md')) {
    Check 'AC-04 peer status EXECUTAVEL + P-06' ($rows['peer-entrega-travada.md'] -match 'EXECUTÁVEL' -and $rows['peer-entrega-travada.md'] -match 'P-06') $rows['peer-entrega-travada.md']
}
if ($rows.ContainsKey('dlq-cheia.md')) {
    # "idem" no indice aponta para a linha anterior (peer), que carrega o P-06.
    $dlqOk = ($rows['dlq-cheia.md'] -match 'EXECUTÁVEL') -and
             (($rows['dlq-cheia.md'] -match 'P-06') -or
              (($rows['dlq-cheia.md'] -match 'idem') -and $rows.ContainsKey('peer-entrega-travada.md') -and ($rows['peer-entrega-travada.md'] -match 'P-06')))
    Check 'AC-04 dlq status EXECUTAVEL + ponteiro P-06 (direto ou idem)' $dlqOk $rows['dlq-cheia.md']
}
if ($rows.ContainsKey('federacao-interop.md')) {
    Check 'AC-04 federacao EXECUTAVEL local + gate humano + TASK-0049/P-06' ($rows['federacao-interop.md'] -match 'EXECUTÁVEL' -and $rows['federacao-interop.md'] -match 'gate humano' -and $rows['federacao-interop.md'] -match 'P-06') $rows['federacao-interop.md']
}
if ($rows.ContainsKey('rotacionar-chave.md')) {
    Check 'AC-04 rotacionar-chave continua PENDENTE-fase (P-07)' ($rows['rotacionar-chave.md'] -match 'PENDENTE-fase' -and $rows['rotacionar-chave.md'] -match 'P-07') $rows['rotacionar-chave.md']
}
foreach ($f in @('disco-cheio.md', 'restaurar-backup.md', 'rotacionar-chave.md', 'peer-entrega-travada.md', 'dlq-cheia.md', 'federacao-interop.md')) {
    Check "indice linka $f que existe" ($idx.Contains('](' + $f + ')') -and (Test-Path (Join-Path $RunbooksDir $f))) 'link ou arquivo ausente'
}
Check 'AC-04 README aponta Evidence da TASK-0048' ($idx -match 'TASK-0048-runbook-de-federacao\.md') 'ponteiro ausente'
Check 'AC-04 gate count citado 597/597' ($idx -match '597/597') 'contagem do gate ausente'

# ---------- resumo ----------
$summary = "qa-check-task-0048: $((($script:lines | Where-Object { $_ -like 'PASS*' })).Count) PASS / $($script:fail) FAIL"
$script:lines.Add($summary)
$script:lines | ForEach-Object { Write-Output $_ }
$log = Join-Path $PSScriptRoot 'QA-0048-static-check.log'
$script:lines | Set-Content -LiteralPath $log -Encoding UTF8
Write-Output "log: $log"
if ($script:fail -gt 255) { exit 255 }
exit $script:fail
