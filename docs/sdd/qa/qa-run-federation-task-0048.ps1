# qa-run-federation-task-0048.ps1 — QA-0048 dynamic run: re-executes the reproducible local
# parts of the TASK-0048 runbooks (federacao-interop.md + peer-entrega-travada.md + dlq-cheia.md)
# against the repo. Commands inside fragments are the runbook's own lines, verbatim; [QA] markers
# are instrumentation only. Windows PowerShell 5.1. Exit = number of failed checks (0 = all green).
param([string]$RepoRoot = 'C:\dev\squid-squad-net\squid-execution')

$ErrorActionPreference = 'Continue'
$work = Join-Path $env:TEMP 'qa0048'
if (Test-Path $work) { Remove-Item -Recurse -Force $work }
New-Item -ItemType Directory -Path $work | Out-Null
$artifact = Join-Path $RepoRoot 'docs\sdd\qa\QA-0048-federation-run.log'

$script:fail = 0
$script:log  = New-Object System.Collections.Generic.List[string]
function Note([string]$m) { $script:log.Add($m); Write-Output $m; Add-Content -LiteralPath $artifact -Value $m -Encoding UTF8 }
function Check([string]$name, [bool]$cond, [string]$detail = '') {
    if ($cond) { Note "PASS | $name" }
    else { $script:fail++; Note "FAIL | $name | $detail" }
}
Set-Content -LiteralPath $artifact -Value ("qa-run-federation-task-0048 — " + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')) -Encoding UTF8

$ps5 = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
$procs = @()

function WriteFrag([string]$name, [string[]]$lines) {
    $p = Join-Path $work $name
    # marcadores [QA] viram Write-Output: a linha crua '[QA] ...' quebraria o parse do fragmento
    $body = foreach ($l in $lines) {
        if ($l -match '^\[QA\]') { "Write-Output '" + $l + "'" } else { $l }
    }
    Set-Content -LiteralPath $p -Value ($body -join "`r`n") -Encoding UTF8
    return $p
}
function StartFrag([string]$frag, [string]$tag) {
    $out = Join-Path $work "$tag.out.log"
    $err = Join-Path $work "$tag.err.log"
    $p = Start-Process -FilePath $ps5 -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $frag) `
        -WorkingDirectory $RepoRoot -RedirectStandardOutput $out -RedirectStandardError $err `
        -PassThru -WindowStyle Hidden
    $script:procs += ,@{ P = $p; Tag = $tag; Out = $out; Err = $err }
    return $p
}
function StopAll {
    foreach ($e in $script:procs) {
        try { if (-not $e.P.HasExited) { taskkill /PID $e.P.Id /T /F | Out-Null } } catch { }
    }
}
function WaitHealth([string]$url, [int]$sec) {
    $deadline = (Get-Date).AddSeconds($sec)
    while ((Get-Date) -lt $deadline) {
        & curl.exe -fsS -m 3 $url 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) { return $true }
        Start-Sleep -Milliseconds 1200
    }
    return $false
}
function Seg([string]$text, [string]$from, [string]$to = [string]::Empty) {
    $i = $text.IndexOf($from)
    if ($i -lt 0) { return $null }
    $rest = $text.Substring($i + $from.Length)
    if ($to -eq [string]::Empty) { return $rest }
    $j = $rest.IndexOf($to)
    if ($j -lt 0) { return $rest }
    return $rest.Substring(0, $j)
}
function GetOut([string]$tag) {
    $e = $script:procs | Where-Object { $_.Tag -eq $tag } | Select-Object -First 1
    if (-not $e) { return '' }
    $t = ''
    try {
        if (Test-Path $e.Out) { $t += Get-Content -Raw -Encoding UTF8 -LiteralPath $e.Out }
        if (Test-Path $e.Err) { $t += "`n" + (Get-Content -Raw -Encoding UTF8 -LiteralPath $e.Err) }
    } catch { }
    return $t
}
function Snip([string]$s, [int]$n = 200) {
    if ($null -eq $s) { return '<nulo>' }
    $t = ($s -replace '\s+', ' ')
    if ($t.Length -le $n) { return $t }
    return $t.Substring(0, $n)
}
function Py([string]$db, [string]$label, [string]$sql) {
    $o = & python -c "import sqlite3,sys;c=sqlite3.connect(sys.argv[1]);print(sys.argv[2]);[print(r) for r in c.execute(sys.argv[3])]" $db $label $sql 2>&1
    return (@($o) -join "`n")
}
# curl -w "%{http_code}" nao termina em \n: os codigos colam (201201). Reconstrói em blocos de 3.
function CodeChunks([string]$seg) {
    if ($null -eq $seg) { return @() }
    $d = ($seg -replace '\D', '')
    $res = @()
    for ($i = 0; $i + 3 -le $d.Length; $i += 3) { $res += $d.Substring($i, 3) }
    return $res
}

try {
    # ---------- fragments: runbook lines, verbatim ----------
    $fSiloA = WriteFrag 'silo-a.ps1' @(
        '$b = New-Object byte[] 32; ([Security.Cryptography.RandomNumberGenerator]::Create()).GetBytes($b); $k = [Convert]::ToBase64String($b)'
        'dotnet run --project backend/src/Squid.Service -- --urls http://127.0.0.1:5601 --Storage:Path=$env:TEMP\squid-a.db --Media:BlobRoot=$env:TEMP\squid-a-blobs --Squid:RecoveryKey=$k --Federation:TrustedPeerHosts=127.0.0.1,localhost'
    )
    $fSiloB = WriteFrag 'silo-b.ps1' @(
        '$b = New-Object byte[] 32; ([Security.Cryptography.RandomNumberGenerator]::Create()).GetBytes($b); $k = [Convert]::ToBase64String($b)'
        'dotnet run --project backend/src/Squid.Service -- --urls http://127.0.0.1:5602 --Storage:Path=$env:TEMP\squid-b.db --Media:BlobRoot=$env:TEMP\squid-b-blobs --Squid:RecoveryKey=$k --Federation:TrustedPeerHosts=127.0.0.1,localhost'
    )
    $fSiloC = WriteFrag 'silo-c.ps1' @(
        '$b = New-Object byte[] 32; ([Security.Cryptography.RandomNumberGenerator]::Create()).GetBytes($b); $k = [Convert]::ToBase64String($b)'
        'dotnet run --project backend/src/Squid.Service -- --urls http://127.0.0.1:5603 --Storage:Path=$env:TEMP\squid-dom.db --Media:BlobRoot=$env:TEMP\squid-dom-blobs --Squid:RecoveryKey=$k --Federation:TrustedPeerHosts=127.0.0.1,localhost --Instance:Domain=squid-a.test'
    )
    $fWrkA = WriteFrag 'worker-a.ps1' @(
        'dotnet run --project backend/src/Squid.Worker -- --Storage:Path=$env:TEMP\squid-a.db --Media:BlobRoot=$env:TEMP\squid-a-blobs   # silo A'
    )
    $fWrkB = WriteFrag 'worker-b.ps1' @(
        'dotnet run --project backend/src/Squid.Worker -- --Storage:Path=$env:TEMP\squid-b.db --Media:BlobRoot=$env:TEMP\squid-b-blobs   # silo B'
    )
    # federacao-interop.md Verificacao 2..6 (linhas do runbook, verbatim; marcadores [QA] sao instrumentacao)
    $fVerify = WriteFrag 'verify.ps1' @(
        '[QA] step2-superficie-AP'
        'curl.exe -s http://127.0.0.1:5601/.well-known/nodeinfo'
        'curl.exe -s -H "Accept: application/activity+json" http://127.0.0.1:5601/users/alice'
        '[QA] step3-registro'
        '$jarA = "$env:TEMP\jar-a.txt"; $jarB = "$env:TEMP\jar-b.txt"'
        'curl.exe -s -o NUL -w "%{http_code}" -c $jarA -X POST http://127.0.0.1:5601/api/auth/register -H "Content-Type: application/json" -d ''{\"handle\":\"alice\",\"password\":\"<senha-forte>\",\"displayName\":\"Alice\"}'''
        'curl.exe -s -o NUL -w "%{http_code}" -c $jarB -X POST http://127.0.0.1:5602/api/auth/register -H "Content-Type: application/json" -d ''{\"handle\":\"bob\",\"password\":\"<senha-forte>\",\"displayName\":\"Bob\"}'''
        '[QA] step4-comunidades'
        '$csrfA = ((Get-Content $jarA | Where-Object { $_ -match ''squid_csrf'' }) -split "`t")[-1]'
        '$csrfB = ((Get-Content $jarB | Where-Object { $_ -match ''squid_csrf'' }) -split "`t")[-1]'
        'curl.exe -s -o NUL -w "%{http_code}" -b $jarA -c $jarA -X POST http://127.0.0.1:5601/api/containers -H "Content-Type: application/json" -H "X-CSRF-Token: $csrfA" -d ''{\"kind\":\"community\",\"name\":\"InteropDemo\",\"slug\":\"interop-demo\",\"summary\":\"runbook-TASK-0048\"}'''
        'curl.exe -s -o NUL -w "%{http_code}" -b $jarB -c $jarB -X POST http://127.0.0.1:5602/api/containers -H "Content-Type: application/json" -H "X-CSRF-Token: $csrfB" -d ''{\"kind\":\"community\",\"name\":\"InteropDemo\",\"slug\":\"interop-demo\",\"summary\":\"runbook-TASK-0048\"}'''
        '[QA] step5-follow'
        'curl.exe -s -o NUL -w "%{http_code}" -b $jarB -c $jarB -X POST http://127.0.0.1:5602/api/federation/follow -H "Content-Type: application/json" -H "X-CSRF-Token: $csrfB" -d ''{\"targetActorUri\":\"http://127.0.0.1:5601/users/alice\"}'''
        'curl.exe -s -o NUL -w "%{http_code}" -b $jarA -c $jarA -X POST http://127.0.0.1:5601/api/federation/follow -H "Content-Type: application/json" -H "X-CSRF-Token: $csrfA" -d ''{\"targetActorUri\":\"http://127.0.0.1:5602/users/bob\"}'''
        'Start-Sleep -Seconds 6'
        '[QA] step6-publicacoes'
        'curl.exe -s -o NUL -w "%{http_code}" -b $jarA -c $jarA -X POST http://127.0.0.1:5601/api/containers/interop-demo/posts -H "Content-Type: application/json" -H "X-CSRF-Token: $csrfA" -d ''{\"content\":\"ola-da-A-para-B-TASK0048\"}'''
        'curl.exe -s -o NUL -w "%{http_code}" -b $jarB -c $jarB -X POST http://127.0.0.1:5602/api/containers/interop-demo/posts -H "Content-Type: application/json" -H "X-CSRF-Token: $csrfB" -d ''{\"content\":\"ola-da-B-para-A-TASK0048\"}'''
        'Start-Sleep -Seconds 8'
        '[QA] fim'
    )
    $fEmptyFeed = WriteFrag 'empty-feed.ps1' @(
        '$jarA = "$env:TEMP\jar-a.txt"; $jarB = "$env:TEMP\jar-b.txt"'
        '[QA] feeds-sem-worker'
        'curl.exe -s -b $jarA http://127.0.0.1:5601/api/containers/interop-demo/feed   # deve conter o-da-B-para-A-TASK0048'
        'curl.exe -s -b $jarB http://127.0.0.1:5602/api/containers/interop-demo/feed   # deve conter o-da-A-para-B-TASK0048'
        '[QA] fim'
    )
    # federacao-interop.md Verificacao 8 (leitura da fila) — verbatim
    $fQueue = WriteFrag 'queue.ps1' @(
        'python -c "import sqlite3,sys;c=sqlite3.connect(sys.argv[1]);print(sys.argv[2]);[print(r) for r in c.execute(sys.argv[3])]" $env:TEMP\squid-a.db "A fila:" "select activity_type, state, attempts, reason from peer_delivery"'
        'python -c "import sqlite3,sys;c=sqlite3.connect(sys.argv[1]);print(sys.argv[2]);[print(r) for r in c.execute(sys.argv[3])]" $env:TEMP\squid-b.db "B pares:" "select domain, status, failure_count from peer"'
    )
    # peer-entrega-travada.md Diagnostico 2 — verbatim
    $fPeerDiag = WriteFrag 'peer-diag.ps1' @(
        'python -c "import sqlite3,sys;c=sqlite3.connect(sys.argv[1]);print(sys.argv[2]);[print(r) for r in c.execute(sys.argv[3])]" $env:TEMP\squid-a.db "fila:" "select activity_type, state, attempts, reason, next_attempt_at from peer_delivery"'
        'python -c "import sqlite3,sys;c=sqlite3.connect(sys.argv[1]);print(sys.argv[2]);[print(r) for r in c.execute(sys.argv[3])]" $env:TEMP\squid-a.db "pares:" "select domain, status, failure_count, backoff_until, rate_limit_per_minute from peer"'
    )
    # peer-entrega-travada.md Verificacao 1 — verbatim
    $fPeerEst = WriteFrag 'peer-estados.ps1' @(
        'python -c "import sqlite3,sys;c=sqlite3.connect(sys.argv[1]);print(sys.argv[2]);[print(r) for r in c.execute(sys.argv[3])]" $env:TEMP\squid-a.db "estados:" "select state, count(*) from peer_delivery group by state"'
    )
    # dlq-cheia.md Diagnostico 1 e 2 — verbatim
    $fDlq = WriteFrag 'dlq.ps1' @(
        'python -c "import sqlite3,sys;c=sqlite3.connect(sys.argv[1]);print(sys.argv[2]);[print(r) for r in c.execute(sys.argv[3])]" $env:TEMP\squid-a.db "DLQ por motivo:" "select reason, count(*) from peer_delivery where state=''Failed'' group by reason"'
        'python -c "import sqlite3,sys;c=sqlite3.connect(sys.argv[1]);print(sys.argv[2]);[print(r) for r in c.execute(sys.argv[3])]" $env:TEMP\squid-a.db "DLQ:" "select activity_type, attempts, reason, recipient_actor_id from peer_delivery where state=''Failed''"'
    )
    # dlq-cheia.md Verificacao 1 — verbatim
    $fDlqEst = WriteFrag 'dlq-estados.ps1' @(
        'python -c "import sqlite3,sys;c=sqlite3.connect(sys.argv[1]);print(sys.argv[2]);[print(r) for r in c.execute(sys.argv[3])]" $env:TEMP\squid-a.db "estados:" "select state, count(*) from peer_delivery group by state"'
    )
    $fPeer500 = WriteFrag 'peer-500.ps1' @('dotnet run --project backend/src/Squid.PeerClient -- --scenario=500')
    $fPeer401 = WriteFrag 'peer-401.ps1' @('dotnet run --project backend/src/Squid.PeerClient -- --scenario=401')

    $jarA = Join-Path $env:TEMP 'jar-a.txt'
    $jarB = Join-Path $env:TEMP 'jar-b.txt'

    # ---------- 1. silo A ----------
    Note '== 1. boot do silo A (runbook passo 2, verbatim) =='
    StartFrag $fSiloA 'siloA' | Out-Null
    $okA = WaitHealth 'http://127.0.0.1:5601/health' 180
    Check 'silo A health 200 (curl -fsS exit 0)' $okA '5601 nao respondeu em 180s'
    & curl.exe -fsS http://127.0.0.1:5601/health 2>$null | Out-Null
    Check 'comando do runbook curl -fsS /health silo A exit 0' ($LASTEXITCODE -eq 0) "exit=$LASTEXITCODE"

    # ---------- 2. silo B ----------
    Note '== 2. boot do silo B (runbook passo 2, verbatim) =='
    StartFrag $fSiloB 'siloB' | Out-Null
    $okB = WaitHealth 'http://127.0.0.1:5602/health' 120
    Check 'silo B health 200 (curl -fsS exit 0)' $okB '5602 nao respondeu em 120s'
    $outA = GetOut 'siloA'
    Check 'log A: Now listening on: http://127.0.0.1:5601' ($outA -match 'Now listening on: .{0,4}http://127\.0\.0\.1:5601') 'ausente no log do silo A'
    Check 'log A: sem aviso federation output disabled (chave valida)' (-not ($outA -match 'federation output disabled')) 'aviso indevido no boot'
    Check 'log A: sem Orleans ja em uso que derrube o silo' (-not ($outA -match 'AddressInUseException|already in use')) 'conflito de porta Orleans'

    # ---------- 3. Verificacao 2..6 (fragmento verbatim) ----------
    Note '== 3. Verificacao passos 2-6 (comandos verbatim do runbook) =='
    $pv = StartFrag $fVerify 'verify'
    Wait-Process -Id $pv.Id -Timeout 120 -ErrorAction SilentlyContinue
    $vOut = GetOut 'verify'
    $seg2 = Seg $vOut '[QA] step2-superficie-AP' '[QA] step3-registro'
    Check 'step2 executou' ($null -ne $seg2) 'marcador step2 ausente'
    if ($null -ne $seg2) {
        Check 'step2: nodeinfo devolve software name squid' ($seg2 -match '"name"\s*:\s*"squid"') (Snip $seg2 220)
    }
    $seg3 = Seg $vOut '[QA] step3-registro' '[QA] step4-comunidades'
    $codes3 = CodeChunks $seg3
    Check 'step3: registro alice 201' ($codes3.Count -ge 1 -and $codes3[0] -eq '201') ("codes=" + ($codes3 -join ','))
    Check 'step3: registro bob 201' ($codes3.Count -ge 2 -and $codes3[1] -eq '201') ("codes=" + ($codes3 -join ','))
    $seg4 = Seg $vOut '[QA] step4-comunidades' '[QA] step5-follow'
    $codes4 = CodeChunks $seg4
    Check 'step4: container A 201' ($codes4.Count -ge 1 -and $codes4[0] -eq '201') ("codes=" + ($codes4 -join ','))
    Check 'step4: container B 201' ($codes4.Count -ge 2 -and $codes4[1] -eq '201') ("codes=" + ($codes4 -join ','))
    $seg5 = Seg $vOut '[QA] step5-follow' '[QA] step6-publicacoes'
    $codes5 = CodeChunks $seg5
    Check 'step5: follow B->A 202' ($codes5.Count -ge 1 -and $codes5[0] -eq '202') ("codes=" + ($codes5 -join ','))
    Check 'step5: follow A->B 202' ($codes5.Count -ge 2 -and $codes5[1] -eq '202') ("codes=" + ($codes5 -join ','))
    $seg6 = Seg $vOut '[QA] step6-publicacoes' '[QA] fim'
    $codes6 = CodeChunks $seg6
    Check 'step6: post A 201' ($codes6.Count -ge 1 -and $codes6[0] -eq '201') ("codes=" + ($codes6 -join ','))
    Check 'step6: post B 201' ($codes6.Count -ge 2 -and $codes6[1] -eq '201') ("codes=" + ($codes6 -join ','))

    # ---------- 4. isolamento entre bancos + colecoes ----------
    Note '== 4. isolamento A/B e colecoes de follow =='
    $r = & curl.exe -s -o NUL -w '%{http_code}' -H 'Accept: application/activity+json' http://127.0.0.1:5601/users/alice 2>$null
    Check 'alice existe no silo A (200)' ("$r" -eq '200') "code=$r"
    $r = & curl.exe -s -o NUL -w '%{http_code}' -H 'Accept: application/activity+json' http://127.0.0.1:5602/users/alice 2>$null
    Check 'alice NAO existe no silo B (404, bancos distintos)' ("$r" -eq '404') "code=$r"
    $b = & curl.exe -s -H 'Accept: application/activity+json' http://127.0.0.1:5601/users/alice/followers 2>$null
    $bTxt = (@($b) -join ' ')
    Check 'alice followers totalItems 1' ($bTxt -match '"totalItems"\s*:\s*1') (Snip $bTxt 160)
    $b = & curl.exe -s -H 'Accept: application/activity+json' http://127.0.0.1:5602/users/bob/followers 2>$null
    $bTxt = (@($b) -join ' ')
    Check 'bob followers totalItems 1' ($bTxt -match '"totalItems"\s*:\s*1') (Snip $bTxt 160)
    $bA = & curl.exe -s -H 'Accept: application/activity+json' http://127.0.0.1:5601/users/alice/following 2>$null
    $bB = & curl.exe -s -H 'Accept: application/activity+json' http://127.0.0.1:5602/users/bob/following 2>$null
    Note ("OBS | following A=" + (Snip (@($bA) -join ' ') 200) + " | following B=" + (Snip (@($bB) -join ' ') 200))

    # ---------- 5. feeds SEM worker (runbook: sem Worker feed vazio) ----------
    Note '== 5. feeds sem o Worker (claim: items vazio) =='
    $pe = StartFrag $fEmptyFeed 'emptyfeed'
    Wait-Process -Id $pe.Id -Timeout 60 -ErrorAction SilentlyContinue
    $eOut = GetOut 'emptyfeed'
    $segE = Seg $eOut '[QA] feeds-sem-worker' '[QA] fim'
    $vazio = ($null -ne $segE) -and (([regex]::Matches($segE, '"items"\s*:\s*\[\]')).Count -ge 2) -and ($segE -notmatch 'ola-da-')
    Check 'feeds A+B vazios sem Worker (claim do runbook)' $vazio (Snip $segE 240)

    # ---------- 6. Workers + feeds com projecao ----------
    Note '== 6. Workers de projecao (runbook passo 3, verbatim) =='
    StartFrag $fWrkA 'workerA' | Out-Null
    StartFrag $fWrkB 'workerB' | Out-Null
    Start-Sleep -Seconds 5
    $wA = $script:procs | Where-Object { $_.Tag -eq 'workerA' }
    $wB = $script:procs | Where-Object { $_.Tag -eq 'workerB' }
    Check 'worker A segue vivo apos 5s' (-not $wA.P.HasExited) 'worker A morreu'
    Check 'worker B segue vivo apos 5s' (-not $wB.P.HasExited) 'worker B morreu'
    $feedA = ''; $feedB = ''
    $deadline = (Get-Date).AddSeconds(90)
    while ((Get-Date) -lt $deadline) {
        $feedA = (@(& curl.exe -s -b $jarA http://127.0.0.1:5601/api/containers/interop-demo/feed 2>$null) -join ' ')
        $feedB = (@(& curl.exe -s -b $jarB http://127.0.0.1:5602/api/containers/interop-demo/feed 2>$null) -join ' ')
        if (($feedA -match 'ola-da-A-para-B-TASK0048') -and ($feedA -match 'ola-da-B-para-A-TASK0048') -and
            ($feedB -match 'ola-da-A-para-B-TASK0048') -and ($feedB -match 'ola-da-B-para-A-TASK0048')) { break }
        Start-Sleep -Seconds 3
    }
    Check 'feed A: proprio post + post da B' (($feedA -match 'ola-da-A-para-B-TASK0048') -and ($feedA -match 'ola-da-B-para-A-TASK0048')) $feedA
    Check 'feed B: proprio post + post da A' (($feedB -match 'ola-da-A-para-B-TASK0048') -and ($feedB -match 'ola-da-B-para-A-TASK0048')) $feedB

    # ---------- 7. leitura da fila (Verificacao 8, verbatim) ----------
    Note '== 7. leitura da fila e dos pares (Verificacao 8, verbatim) =='
    $pq = StartFrag $fQueue 'queue'
    Wait-Process -Id $pq.Id -Timeout 60 -ErrorAction SilentlyContinue
    $qOut = GetOut 'queue'
    Check 'fila A: linhas Follow e Create em Sent' (($qOut -match "'Follow'") -and ($qOut -match "'Create'") -and ($qOut -match "'Sent'")) (($qOut -replace '\s+', ' '))
    Check 'pares B: peer Healthy' ($qOut -match "'Healthy'") (($qOut -replace '\s+', ' '))

    # ---------- 8. diagnostico do runbook peer-entrega-travada (verbatim) ----------
    Note '== 8. peer-entrega-travada Diagnostico 2 e Verificacao 1 (verbatim) =='
    $pd = StartFrag $fPeerDiag 'peerdiag'
    Wait-Process -Id $pd.Id -Timeout 60 -ErrorAction SilentlyContinue
    $pdOut = GetOut 'peerdiag'
    Check 'diagnostico 2: fila legivel (linha de entrega na saida)' ($pdOut -match "\('(Follow|Create)'") (Snip $pdOut 240)
    Check 'diagnostico 2: pares legiveis (linha de peer na saida)' ($pdOut -match "\('127\.0\.0\.1") (Snip $pdOut 240)
    $pe2 = StartFrag $fPeerEst 'peerest'
    Wait-Process -Id $pe2.Id -Timeout 60 -ErrorAction SilentlyContinue
    $peOut = GetOut 'peerest'
    Check 'verificacao 1: contagem por estado mostra Sent' ($peOut -match "'Sent'") (($peOut -replace '\s+', ' '))

    # ---------- 9. PeerClient --help e cenario desconhecido ----------
    Note '== 9. PeerClient --help e cenario invalido =='
    & dotnet run --project backend/src/Squid.PeerClient -- --help 2>$null | Out-Null
    Check 'PeerClient --help exit 0' ($LASTEXITCODE -eq 0) "exit=$LASTEXITCODE"
    & dotnet run --project backend/src/Squid.PeerClient -- --scenario=nope 2>$null | Out-Null
    Check 'PeerClient --scenario=nope exit 1' ($LASTEXITCODE -eq 1) "exit=$LASTEXITCODE"

    # ---------- 10. cenario 500 -> Pending/send_failed/peer Down ----------
    Note '== 10. cenario 500 (peer-entrega-travada Diagnostico 3) =='
    StartFrag $fPeer500 'peer500' | Out-Null
    $port = $null; $deadline = (Get-Date).AddSeconds(120)
    while ((Get-Date) -lt $deadline) {
        $t500 = GetOut 'peer500'
        if ($t500 -match "scenario '500' listening on http://127\.0\.0\.1:(\d+)") { $port = $Matches[1]; break }
        Start-Sleep -Milliseconds 1200
    }
    Check 'PeerClient 500 imprime origem dinamica (log do runbook)' ($null -ne $port) (($t500 -replace '\s+', ' ').Substring(0, [Math]::Min(200, ($t500 -replace '\s+', ' ').Length)))
    if ($null -ne $port) {
        $csrfA = ((Get-Content $jarA | Where-Object { $_ -match 'squid_csrf' }) -split "`t")[-1]
        $body = '{\"targetActorUri\":\"http://127.0.0.1:' + $port + '/users/peer-test\"}'
        $r = & curl.exe -s -o NUL -w '%{http_code}' -b $jarA -c $jarA -X POST http://127.0.0.1:5601/api/federation/follow -H 'Content-Type: application/json' -H "X-CSRF-Token: $csrfA" -d $body 2>$null
        Check 'follow para o par sintetico 500 responde 202 (enfileira)' ("$r" -eq '202') "code=$r"
        Start-Sleep -Seconds 10
        $rows = Py (Join-Path $env:TEMP 'squid-a.db') 'send_failed:' "select activity_type, state, attempts, reason, next_attempt_at from peer_delivery where reason='send_failed'"
        Check 'linha Pending com send_failed e next_attempt_at futuro' (($rows -match "'Pending'") -and ($rows -match "'send_failed'") -and ($rows -match 'T\d\d:')) (Snip $rows 240)
        $peers = Py (Join-Path $env:TEMP 'squid-a.db') 'pares:' "select domain, status, failure_count from peer"
        $peerLine = "'" + '127.0.0.1:' + $port + "', 'Down', 1)"
        Check 'peer do cenario 500: Down com failure_count 1 (mesma linha)' ($peers -match [regex]::Escape($peerLine)) (Snip $peers 240)
    }

    # ---------- 11. cenario 401 -> Failed/signature_rejected + DLQ ----------
    Note '== 11. cenario 401 (dlq-cheia Diagnostico 4) =='
    $e500 = $script:procs | Where-Object { $_.Tag -eq 'peer500' }
    if ($e500 -and -not $e500.P.HasExited) { taskkill /PID $e500.P.Id /T /F | Out-Null }
    StartFrag $fPeer401 'peer401' | Out-Null
    $port2 = $null; $deadline = (Get-Date).AddSeconds(120)
    while ((Get-Date) -lt $deadline) {
        $t401 = GetOut 'peer401'
        if ($t401 -match "scenario '401' listening on http://127\.0\.0\.1:(\d+)") { $port2 = $Matches[1]; break }
        Start-Sleep -Milliseconds 1200
    }
    Check 'PeerClient 401 imprime origem dinamica' ($null -ne $port2) ''
    if ($null -ne $port2) {
        $csrfA = ((Get-Content $jarA | Where-Object { $_ -match 'squid_csrf' }) -split "`t")[-1]
        $body = '{\"targetActorUri\":\"http://127.0.0.1:' + $port2 + '/users/peer-401\"}'
        $r = & curl.exe -s -o NUL -w '%{http_code}' -b $jarA -c $jarA -X POST http://127.0.0.1:5601/api/federation/follow -H 'Content-Type: application/json' -H "X-CSRF-Token: $csrfA" -d $body 2>$null
        Check 'follow para o par sintetico 401 responde 202' ("$r" -eq '202') "code=$r"
        Start-Sleep -Seconds 10
        $dlqRows = Py (Join-Path $env:TEMP 'squid-a.db') 'Failed:' "select activity_type, state, attempts, reason from peer_delivery where state='Failed'"
        Check '401 derruba a linha em Failed com signature_rejected na 1a tentativa' (($dlqRows -match "'Failed'") -and ($dlqRows -match "'signature_rejected'") -and ($dlqRows -match ', 1,')) (Snip $dlqRows 240)
        $eA = $script:procs | Where-Object { $_.Tag -eq 'siloA' }
        $siloLog = ''
        if (Test-Path $eA.Out) { $siloLog += Get-Content -Raw -Encoding UTF8 -LiteralPath $eA.Out }
        if (Test-Path $eA.Err) { $siloLog += "`n" + (Get-Content -Raw -Encoding UTF8 -LiteralPath $eA.Err) }
        Check 'log do silo A: peer delivery dead-lettered' ($siloLog -match 'peer delivery dead-lettered') 'aviso ausente'
        Check 'log do silo A: reason signature_rejected no aviso' ($siloLog -match 'signature_rejected') ''
    }
    # dlq-cheia.md Diagnostico 1/2 e Verificacao 1 (verbatim)
    $dq = StartFrag $fDlq 'dlq'
    Wait-Process -Id $dq.Id -Timeout 60 -ErrorAction SilentlyContinue
    $dqOut = GetOut 'dlq'
    Check 'dlq diagnostico 1: contagem por motivo mostra signature_rejected' ($dqOut -match "'signature_rejected'") (($dqOut -replace '\s+', ' '))
    Check 'dlq diagnostico 2: linhas com recipient_actor_id' ($dqOut -match 'recipient|5601|5602|127\.0\.0\.1') ''
    $de = StartFrag $fDlqEst 'dlqest'
    Wait-Process -Id $de.Id -Timeout 60 -ErrorAction SilentlyContinue
    $deOut = GetOut 'dlqest'
    Check 'dlq verificacao 1: estados com Failed' ($deOut -match "'Failed'") (($deOut -replace '\s+', ' '))

    # ---------- 12. outbox do par e ausencia de signature verification failed ----------
    Note '== 12. Verificacao 9: outbox do par + logs sem falha de assinatura =='
    $ob = & curl.exe -s -H 'Accept: application/activity+json' http://127.0.0.1:5602/c/interop-demo/outbox 2>$null
    $ob1 = & curl.exe -s -H 'Accept: application/activity+json' 'http://127.0.0.1:5602/c/interop-demo/outbox?page=1' 2>$null
    $obTxt = ((@($ob) -join ' ') + ' ' + (@($ob1) -join ' '))
    Check 'outbox do contener B cita nota de origem do silo A (5601/o/)' ($obTxt -match '127\.0\.0\.1:5601/o/') (Snip $obTxt 300)
    $allLogs = ''
    foreach ($tag in @('siloA', 'siloB', 'workerA', 'workerB')) {
        $e = $script:procs | Where-Object { $_.Tag -eq $tag }
        if ($e) {
            if (Test-Path $e.Out) { $allLogs += Get-Content -Raw -Encoding UTF8 -LiteralPath $e.Out }
            if (Test-Path $e.Err) { $allLogs += Get-Content -Raw -Encoding UTF8 -LiteralPath $e.Err }
        }
    }
    Check 'nenhum log com signature verification failed (contrato §7)' (-not ($allLogs -match 'signature verification failed')) 'assimetrica rejeitada em algum log'

    # ---------- 13. Instance:Domain (runbook passo 4, verbatim) ----------
    Note '== 13. modo dominio canonico Instance:Domain (5603) =='
    StartFrag $fSiloC 'siloC' | Out-Null
    $okC = WaitHealth 'http://127.0.0.1:5603/health' 150
    Check 'silo C (Instance:Domain) health 200' $okC '5603 nao respondeu'
    $r = & curl.exe -s -o NUL -w '%{http_code}' -X POST http://127.0.0.1:5603/api/auth/register -H 'Content-Type: application/json' -d '{\"handle\":\"domtest\",\"password\":\"<senha-forte>\",\"displayName\":\"Dom\"}' 2>$null
    Check 'registro domtest no silo C 201' ("$r" -eq '201') "code=$r"
    $b = (@(& curl.exe -s -H 'Accept: application/activity+json' http://127.0.0.1:5603/users/domtest 2>$null) -join ' ')
    Check 'id publicado usa https://squid-a.test (CanonicalOrigin)' ($b -match 'https://squid-a\.test/users/domtest') (($b -replace '\s+', ' ').Substring(0, [Math]::Min(200, ($b -replace '\s+', ' ').Length)))
    $w = (@(& curl.exe -s 'http://127.0.0.1:5603/.well-known/webfinger?resource=acct:domtest@squid-a.test' 2>$null) -join ' ')
    Check 'webfinger subject acct:domtest@squid-a.test' ($w -match 'acct:domtest@squid-a\.test') (($w -replace '\s+', ' ').Substring(0, [Math]::Min(200, ($w -replace '\s+', ' ').Length)))
}
finally {
    Note '== limpeza: encerrando processos e apagando temporarios =='
    StopAll
    $freed = $true
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
        $busy = Get-NetTCPConnection -State Listen -LocalPort 5601,5602,5603 -ErrorAction SilentlyContinue
        if (-not $busy) { break }
        Start-Sleep -Milliseconds 800
        $freed = $false
    }
    $busy = Get-NetTCPConnection -State Listen -LocalPort 5601,5602,5603 -ErrorAction SilentlyContinue
    Check 'portas 5601-5603 liberadas apos limpeza' (-not $busy) 'porta ainda escutando'
    foreach ($n in @('squid-a.db', 'squid-a.db-shm', 'squid-a.db-wal', 'squid-b.db', 'squid-b.db-shm', 'squid-b.db-wal',
                     'squid-dom.db', 'squid-dom.db-shm', 'squid-dom.db-wal', 'squid-a-blobs', 'squid-b-blobs', 'squid-dom-blobs',
                     'jar-a.txt', 'jar-b.txt')) {
        $p = Join-Path $env:TEMP $n
        if (Test-Path $p) { Remove-Item -Recurse -Force $p -ErrorAction SilentlyContinue }
    }
    $left = @('squid-a.db', 'squid-b.db', 'squid-dom.db', 'jar-a.txt', 'jar-b.txt', 'squid-a-blobs', 'squid-b-blobs', 'squid-dom-blobs') |
        Where-Object { Test-Path (Join-Path $env:TEMP $_) }
    Check 'sem residuo temporario da execucao' (@($left).Count -eq 0) ($left -join ',')
    $summary = "qa-run-federation-task-0048: $($script:fail) FAIL"
    Note $summary
}
exit [Math]::Min($script:fail, 255)
