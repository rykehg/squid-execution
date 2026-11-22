# Runbook — restaurar backup

- fase: P-07 · status: EXECUTÁVEL · origem: doc 05 §8, SPEC-0006, SPEC-0012 §D (restore e
  drills da TASK-0056; o trecho "gerar o backup" deixou de ser PENDENTE nesta task)
- executável hoje: gerar o backup cifrado, restaurar a partir de um envelope e o ciclo
  arquivo → volume do arquivo de guarda
- comandos `podman compose` rodam da raiz do repositório com `-f infra/compose.yaml`; os
  comandos `curl.exe`/`powershell`/`dotnet` também da raiz do repositório
- nenhum valor de chave ou de senha neste documento: a `RK` sai do kit salvo e a senha do
  operador sai do shell — ambas viajam por variável de ambiente

## Sintoma

O SQLite do serviço está perdido, corrompido ou trocado e existe um backup cifrado
(envelope em `Backup:Local:Path`) para devolver ao volume. Sinal: `/ready` responde 503
enquanto `/health` continua 200 — processo vivo, storage inacessível — ou o arquivo nem
existe mais.

## Diagnóstico

1. `curl -fsS http://localhost:8080/health` — **EXECUTÁVEL** — exit 0: o processo está vivo.
2. `curl -fsS http://localhost:8080/ready` — **EXECUTÁVEL** — exit 0: storage acessível.
   No caso que abre este runbook a resposta é 503 e, com `-f`, o curl termina com exit 22
   (é o comportamento documentado do `curl -f` em resposta 4xx/5xx — daí o procedimento abaixo).
   No Windows use `curl.exe -fsS http://localhost:8080/ready`; em Linux use
   `curl -fsS http://localhost:8080/ready`. Os dois formulários foram executados na prova
   (o de Linux, pela WSL).

## Procedimento

Convenção: tudo da raiz do repositório. O serviço do item A precisa estar configurado com
`Instance:Operators=<operador>`; a chave de backup é a `Squid:RecoveryKey` do kit
(SPEC-0007) e neste documento só aparece como `$env:SQUID_RK`.

### A. Gerar o backup cifrado — EXECUTÁVEL

1. Sessão do operador — **EXECUTÁVEL** (os cookies são `Secure`: em http eles viajam
   explícitos no header, do mesmo jeito que os testes fazem; a senha entra pelo `Read-Host`
   — nem o histórico do shell nem este documento a veem):
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
2. Dispare um backup imediato — **EXECUTÁVEL** (contrato §3; o envelope registrado volta no
   corpo, com `status`, `seqFrom/seqTo` e `contentHash`):
   ```powershell
   curl.exe -sS -X POST "$base/api/admin/backup/run" -H "Cookie: $cookie" -H "X-CSRF-Token: $csrf"
   ```
   Sem operador no teclado o agendador faz o mesmo a cada `Backup:IntervalMinutes`
   (default 1440).
3. Confira o estado — **EXECUTÁVEL** (contrato §6):
   ```powershell
   curl.exe -sS "$base/api/admin/backup/status" -H "Cookie: $cookie"
   ```
   `backup.last_success_at`, `envelopes_count`, `bytes_total`, `verify_failures` e os dois
   campos de prova pública: `restore.last_drill_at` e `restore.last_drill_result` — um
   restore que passou na própria verificação grava `restore: passed` ali.

### B. Restaurar a partir de um envelope — EXECUTÁVEL

Pré-condição: existe pelo menos um envelope no destino do item A, a `RK` do kit está em
`$env:SQUID_RK` (o valor é carregado do kit salvo — nunca digitado neste documento) e o
binário está compilado — `dotnet build backend/Squid.slnx` (o mesmo binário do gate).

1. Pare o serviço — o passo 1 do ciclo abaixo (EXECUTÁVEL, prova de P-01); com o serviço
   rodando direto no host, encerre o processo dele. O arquivo de destino precisa estar livre.
2. Se restou um arquivo corrompido, remova-o — **EXECUTÁVEL**:
   ```powershell
   Remove-Item ./squid-grainstate.db -ErrorAction SilentlyContinue
   ```
3. Restaure — **EXECUTÁVEL** (exit 0 = restaurado, com o resumo no stdout; exit 1 = recusado,
   com o motivo no stderr — hash divergente, época sem chave no kit, formato incompatível ou
   cadeia incompleta nunca são aceitos em silêncio). O binário roda direto, na raiz do
   repositório — é o mesmo jeito que o contêiner o executa, e os caminhos relativos resolvem
   aqui. A `RK` entra por configuração de ambiente (`__` vira `:`), nunca por argumento —
   argv é listado em qualquer `ps`:
   ```powershell
   $env:Squid__RecoveryKey = $env:SQUID_RK
   dotnet backend/src/Squid.Service/bin/Debug/net10.0/Squid.Service.dll --restore `
     --Storage:Path=./squid-grainstate.db `
     --Backup:Local:Path=./backups
   ```
   O restore segue o fluxo do contrato §4: valida hash e época de cada envelope, aplica a
   migração de `format` quando o payload é de uma versão anterior, reaplica log de
   atividade, projeções e ledger de cota, e compara contagens e hashes por agregado antes
   de responder. Uma recusa não toca no destino.
4. Suba o serviço — o passo 5 do ciclo abaixo — e confira a Verificação.

### C. Ciclo arquivo → volume (P-01) — EXECUTÁVEL

1. Pare o serviço — **EXECUTÁVEL**: `podman compose -f infra/compose.yaml down`
   (sem `-v`: o volume `infra_squid-data` é mantido).
2. Monte o contêiner de apoio sobre o volume — **EXECUTÁVEL**:
   `podman create --name squid-restore -v infra_squid-data:/data squid-service:dev`
   (resto de execução interrompida: `podman rm squid-restore` antes — é o comando do passo 4).
3. Devolva o arquivo para o volume — **EXECUTÁVEL**:
   `podman cp ./squid-grainstate.db squid-restore:/data/squid-grainstate.db`
   Atenção: isso sobrescreve o arquivo que está no volume.
4. Remova o contêiner de apoio — **EXECUTÁVEL**: `podman rm squid-restore`
5. Suba o serviço — **EXECUTÁVEL**: `podman compose -f infra/compose.yaml up -d`

## Verificação

1. `curl -fsS http://localhost:8080/health` — **EXECUTÁVEL** — exit 0.
2. `curl -fsS http://localhost:8080/ready` — **EXECUTÁVEL** — exit 0: storage acessível de novo.
3. `podman volume ls` — **EXECUTÁVEL** — `infra_squid-data` presente.
4. O status do item A.3 responde `restore.last_drill_at` preenchido e
   `restore.last_drill_result` = `restore: passed` — a prova pública do contrato §6. O
   envelope devolve o log de atividade, as projeções e o ledger de cota; contas e sessões
   não vêm do backup (cobertura da TASK-0054), então depois de um restore abra uma sessão
   nova com o item A.1 antes de consultar.

## Ponteiros

- Contrato: `specs/001-squid-mvp/contracts/backup-recovery.md` §4 (fluxo) e §5 (drills)
- Spec da fase: `docs/sdd/specs/SPEC-0012-backup-rotação-p07.md` §D
- Rotação de chave: `docs/runbooks/rotacionar-chave.md`
