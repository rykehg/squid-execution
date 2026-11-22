# Runbook — rotacionar chave

- fase: P-07 · status: **EXECUTÁVEL** (TASK-0058, fecha S-39; escopo decidido pelo humano
  2026-11-12 e 2026-11-20 — rotação só de material de instância) · origem: doc 05 §5.1/§8,
  SPEC-0012 §E, R-20
- executável hoje: diagnosticar o estado da chave, rotacionar `RK`/`RC` da instância, recarregar a
  configuração com o kit reemitido e conferir que a época antiga segue restaurável — todos os
  comandos abaixo foram executados na prova, com saída em
  `docs/sdd/runs/test-runs/` (`*-runbook-rotacionar-chave.log`)
- comandos `powershell`/`curl.exe`/`python` da raiz do repositório; o serviço precisa estar
  configurado com `Instance:Operators=<operador>` e com a `Squid:RecoveryKey` vinda do kit salvo
- nenhum valor de chave ou de senha neste documento: o material novo nasce na resposta da rotação
  e viaja só em variável (`$kit`), a senha entra por `Read-Host`, a credencial digitada entra por
  variável — nada disso é impresso nem gravado aqui

## Sintoma

Período de rotação programado, ou suspeita de exposição da `Squid:RecoveryKey` de uma instância.
O mecanismo nasceu na P-07 (SPEC-0012 §E): rotacionar gera uma chave nova, avança `keyEpoch`,
reembrulha as cópias `0009` e o keyring do kit, troca a `RC` e reemite o kit com as épocas
antigas — **nenhum backup antigo é reescrito** (R-20, FR-047). Rotação de chave de ator fica fora
de escopo (S-46 no backlog).

## Diagnóstico

1. `curl.exe -fsS http://localhost:8080/health` — **EXECUTÁVEL** — exit 0: o processo está vivo.
2. `curl.exe -fsS http://localhost:8080/ready` — **EXECUTÁVEL** — exit 0: storage acessível.
3. Estado da chave — **EXECUTÁVEL** — o ledger `key_epoch` é a fonte: cada linha mostra a época,
   o fingerprint público do material (SHA-256, identifica sem expor) e quando foi aposentada.
   Passe o caminho do `Storage:Path` por argv (nunca dentro do SQL):
   ```powershell
   python -c "import sqlite3,sys;rows=list(sqlite3.connect(sys.argv[1]).execute('SELECT epoch, public_fingerprint, retired_at FROM key_epoch ORDER BY epoch'));print(*rows,sep=chr(10)) if rows else print('(vazio: nenhuma rotacao ainda - a primeira materializa a epoca 1)')" .\squid-grainstate.db
   ```
   Sem linhas = a instância nunca foi rotacionada (a época corrente é a 1 e só existe no kit).

## Procedimento

Convenção: tudo da raiz do repositório. `$base = 'http://127.0.0.1:8080'`; e o volume do backup:
`$volume = '.\backups'` — troque pelo `Backup:Local:Path` real da instância. A senha do operador
entra pelo `Read-Host` — nem o histórico do shell nem este documento a veem.

### A. Sessão do operador — EXECUTÁVEL

```powershell
$base = 'http://127.0.0.1:8080'
$env:RB_PASSWORD = Read-Host 'senha do operador'
$body = @{ handle = 'operador'; password = $env:RB_PASSWORD } | ConvertTo-Json
$register = Invoke-WebRequest -UseBasicParsing -Method Post -Uri "$base/api/auth/register" `
  -ContentType 'application/json' -Body $body
$header = ($register.Headers['Set-Cookie'] -join ',')
$session = [regex]::Match($header, 'squid_session=([^;,\s]+)').Groups[1].Value
$csrf = [regex]::Match($header, 'squid_csrf=([^;,\s]+)').Groups[1].Value
$cookie = "squid_session=$session; squid_csrf=$csrf"
```

Conta já existente: troque o POST por `/api/auth/login` com o mesmo corpo.

### B. Backup antes da rotação — EXECUTÁVEL

Um envelope verificado antes de tocar na chave: a rotação não reescreve nada, mas o estado pré
precisa estar registrado para a conferência do item E.2.

```powershell
$run = curl.exe -sS -X POST "$base/api/admin/backup/run" `
  -H "Cookie: $cookie" -H "X-CSRF-Token: $csrf" | ConvertFrom-Json
"epoch=$($run.keyEpoch) hash=$($run.contentHash)"
$antes = Get-FileHash "$volume\*" -Algorithm SHA256 | Sort-Object Path
```

Os chamados autenticados usam `curl.exe -H` (igual a `restaurar-backup.md`): o cookie `Secure` é
mandado explicitamente no header — `Invoke-WebRequest -Headers` não o entrega em http.

### C. Rotacionar — EXECUTÁVEL (a resposta é o kit, uma única vez)

```powershell
$rotate = curl.exe -sS -X POST "$base/api/admin/recovery/rotate" `
  -H "Cookie: $cookie" -H "X-CSRF-Token: $csrf"
$kit = $rotate | ConvertFrom-Json
$kit.keyEpoch        # só a época é impressa; o resto do corpo NUNCA é printado nem logado
```

O corpo É o kit reemitido — credencial, mnemônico, chave lacrada e o keyring de todas as
épocas. Guarde-o **fora do repositório** (gerenciador de segredos / mídia offline) agora: esta é
a única vez que ele é servido; nenhuma rota posterior devolve material algum dele.

### D. Recarregar a configuração — EXECUTÁVEL (reinício normal)

Os dois valores vêm do kit, sem digitação de material (só leitura da variável):

```powershell
$rk = ($kit.keyring | Where-Object epoch -eq $kit.keyEpoch).key
$ring = ($kit.keyring | ForEach-Object { "$($_.epoch):$($_.key)" }) -join ','
$env:Squid__RecoveryKey = $rk
$env:Squid__RecoveryKeyRing = $ring
```

Formato do `Squid:RecoveryKeyRing`: pares `epoch:valor` separados por vírgula, uma entrada por
época que o kit conhece — o valor é o base64 da chave daquela época, saído do kit, nunca
digitado à mão. Em seguida **pare o serviço e suba de novo** a partir desta sessão (ou escreva os
dois valores no seu gerenciador de segredos/compose): recarregar configuração é o reinício normal
— a rotação em si não reinicia nada e a assinatura de saída segue funcionando durante a troca.

### E. Conferir a rotação — EXECUTÁVEL

1. `curl.exe -fsS http://localhost:8080/health` e `curl.exe -fsS http://localhost:8080/ready` —
   exit 0 cada, com o serviço de volta.
2. Nenhum backup pré-rotação foi reescrito — **EXECUTÁVEL**: a comparação abaixo não imprime
   nada quando os hashes batem (AC-03; arquivos novos do pós-rotação não entram, porque a lista
   `$antes` só tem os caminhos que existiam antes):
   ```powershell
   $depois = @{}
   Get-FileHash "$volume\*" -Algorithm SHA256 | ForEach-Object { $depois[$_.Path] = $_.Hash }
   $antes | Where-Object { $depois[$_.Path] -ne $_.Hash }
   # (sem saída = nenhum arquivo da época antiga mudou um byte)
   ```
3. A credencial anterior parou de verificar e a nova passa — **EXECUTÁVEL** (a partir da segunda
   rotação; numa primeira rotação não há RC anterior e este passo é pulado). O corpo JSON vai por
   arquivo temporário porque `curl.exe` não recebe aspas embutidas pelo shell do Windows:
   ```powershell
   $rcFile = Join-Path $env:TEMP 'squid-kit-verify.json'
   $rcAntiga = Read-Host 'RC anterior (a que valia antes desta rotação)'
   ('{"recoveryCredential":"' + $rcAntiga + '"}') | Set-Content -Path $rcFile -Encoding ascii
   curl.exe -sS -o NUL -w "%{http_code}\n" -X POST "$base/api/recovery/kit/verify" `
     -H "Cookie: $cookie" -H "X-CSRF-Token: $csrf" -H "Content-Type: application/json" `
     --data-binary "@$rcFile"                                      # 401
   $rcNova = Read-Host 'RC nova (do kit reemitido)'
   ('{"recoveryCredential":"' + $rcNova + '"}') | Set-Content -Path $rcFile -Encoding ascii
   curl.exe -sS -o NUL -w "%{http_code}\n" -X POST "$base/api/recovery/kit/verify" `
     -H "Cookie: $cookie" -H "X-CSRF-Token: $csrf" -H "Content-Type: application/json" `
     --data-binary "@$rcFile"                                      # 200
   Remove-Item $rcFile
   ```
4. A instância segue operando na nova cadeia — **EXECUTÁVEL**: o backup abaixo nasce na época
   nova (`keyEpoch` igual ao da resposta do item C) e a saída federada continua assinando sem
   reinício além do normal do item D:
   ```powershell
   $depois1 = curl.exe -sS -X POST "$base/api/admin/backup/run" `
     -H "Cookie: $cookie" -H "X-CSRF-Token: $csrf" | ConvertFrom-Json
   $depois1.keyEpoch      # a nova época, não a antiga
   ```

## Verificação

1. `curl.exe -fsS http://localhost:8080/health` — exit 0.
2. `curl.exe -fsS http://localhost:8080/ready` — exit 0.
3. O ledger do Diagnóstico 3 mostra a época anterior com `retired_at` preenchido e a nova sem —
   e, a cada rotação seguinte, mais uma linha: a cadeia inteira fica auditável sem expor chave.
4. O item E.2 não imprimiu nada: nenhum arquivo de backup da época antiga foi reescrito.
5. O item E.3 respondeu `401` para a RC anterior e `200` para a nova (a partir da 2ª rotação).
6. O item E.4 devolveu a época nova e o status segue respondendo — **EXECUTÁVEL**:
   ```powershell
   $status = curl.exe -sS "$base/api/admin/backup/status" -H "Cookie: $cookie" | ConvertFrom-Json
   "envelopes=$($status.backup.envelopes_count) bytes=$($status.backup.bytes_total)"
   ```
7. Restaurar um envelope da época antiga: use `restaurar-backup.md` item B com os dois valores do
   item D na variável de ambiente (`Squid__RecoveryKey` + `Squid__RecoveryKeyRing`) — o keyring é
   o que mantém a época antiga legível (R-20), e nenhum arquivo do destino é tocado pelo restore
   que recusar.

## Ponteiros

- SPEC: `docs/sdd/specs/SPEC-0012-backup-rotação-p07.md` §E (rotação) e §D (restore/drills)
- Hierarquia: `docs/features/05-arquitetura-consolidada.md` §5.1; R-20 em
  `specs/001-squid-mvp/research.md`; contrato `specs/001-squid-mvp/contracts/backup-recovery.md` §7
  (deleção de envelope só em rotação/expurgo, registrada — esta rotação não apaga nada)
- Restaurar: `docs/runbooks/restaurar-backup.md`
