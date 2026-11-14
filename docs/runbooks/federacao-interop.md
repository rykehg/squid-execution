# Runbook — federação: dois silos locais + pares de referência (manual)

- fase: P-06 · status: EXECUTÁVEL (caminho local executado com exit 0 na prova da TASK-0048) ·
  gate humano: pares de referência (TASK-0049) e instância pública (seção final) ·
  origem: SPEC-0011 decisões 3, 5 e 6 — o manual pedido pelo humano
- comandos do repositório rodam da **raiz do repositório**, um terminal por silo
- marcas por comando: **EXECUTÁVEL** = executado nesta prova · **GATE-HUMANO** = execução conjunta
  com o humano (TASK-0049) · **EXTERNO** = ferramenta ou projeto fora deste repositório ·
  **NÃO-executar** = destrutivo, decisão consciente

## Escopo

O que este manual faz (SPEC-0011 §H — decisões 3, 5 e 6):

1. subir **silo A** e **silo B** em portas distintas, com banco, volume de mídia e chave próprios;
2. configurar `Squid:RecoveryKey` (obrigatória para a saída federada) e, no modo domínio
   canônico, `Instance:Domain`;
3. subir **Mastodon** e **Friendica** em Docker local (pares de referência, decisão 6);
4. apontar cada par para o silo e validar a comunicação;
5. opcional: instância pública real — seção final claramente marcada **GATE-HUMANO** (seção 7).

O que **não** é: produção (o valor de `Federation:TrustedPeerHosts` fora do localhost é gate
humano, GAP-49), TLS/DNS públicos sem o humano, federação de contêineres (backlog do doc 05 §12.3).

Honestidade (GAP-18): tudo que está marcado **EXECUTÁVEL** foi executado na prova desta task.
Os passos dos pares de referência e da instância pública **não** foram executados aqui — são
**GATE-HUMANO** na TASK-0049, com a fonte externa nomeada. Este repositório **não** traz compose
de Mastodon nem de Friendica; para eles vale a fonte oficial do projeto (EXTERNO).

## Pré-requisitos

| Item | Verificação | Marca |
| --- | --- | --- |
| .NET SDK 10 | `dotnet --info` | EXTERNO |
| Gate verde antes de começar | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` | EXECUTÁVEL (gate desta task) |
| `curl.exe` no Windows (o `curl` do PowerShell é alias de `Invoke-WebRequest`) | `curl.exe --version` | EXTERNO |
| `python` 3 com `sqlite3` padrão (leitura da fila/DLQ) | `python --version` | EXTERNO |
| Portas livres: 5601, 5602 (e 8080/16686 se for subir a stack dev) | `Get-NetTCPConnection -State Listen -LocalPort 5601,5602 -ErrorAction SilentlyContinue` | EXTERNO |
| Runtime de contêiner: nesta máquina é **podman** (`docker` ausente — observado na prova); onde houver Docker, `docker` troca `podman` | `podman --version` | EXTERNO |
| Coletor OTLP opcional (traces; sem ele o log repete `OTLP export failed ... Warning` — inofensivo) | `podman compose -f infra/compose.yaml up -d` — comando e marca em [disco-cheio.md](disco-cheio.md) | EXECUTÁVEL (prova TASK-0010) |

No Windows use sempre `curl.exe` e `powershell -NoProfile -ExecutionPolicy Bypass -File …`; em
Linux os mesmos comandos com `curl`/`python3`. Os formulários abaixo foram executados no Windows
PowerShell 5.1; a forma Linux não rodou nesta prova.

**Regra de aspas no PowerShell 5.1 (paga na prova):** o corpo JSON vai com aspas escapadas
(`{\"chave\":\"valor\"}`) e **sem espaços dentro dos valores** (use `InteropDemo`, nunca
`Interop Demo`) — o argumento nativo é truncado no espaço. Em bash o mesmo corpo vai entre aspas
simples sem escape.

## Procedimento

### 1. Chave de recuperação (um por silo, fora do repositório)

`Squid:RecoveryKey` é **base64 de exatamente 32 bytes** (o `RK` do kit; `RuntimeSigningKeyRing.FromConfiguration`
desliga a saída federada — com aviso no boot — se o valor não decodificar para 32 bytes).

```powershell
# EXECUTÁVEL — gera e imprime a chave (32 bytes, base64 de 44 caracteres); não grave em arquivo
$b = New-Object byte[] 32; ([Security.Cryptography.RandomNumberGenerator]::Create()).GetBytes($b); [Convert]::ToBase64String($b)
```

```bash
# forma Linux (EXTERNO — não executada nesta prova)
python3 -c "import base64,os;print(base64.b64encode(os.urandom(32)).decode())"
```

Regra de ouro (GAP-50, D-01 da TASK-0042): a chave fica em memória de processo, nunca no
repositório e nunca em log. Ator registrado **sem** a chave no boot **não** cria a cópia
embrulhada da `0009` e jamais sai da instância (limite aceito pelo humano): numa instância nova,
registe os atores **depois** de configurar a chave.

### 2. Silo A e silo B (dois terminais, portas e caminhos distintos) — EXECUTÁVEL

Terminal A:

```powershell
$b = New-Object byte[] 32; ([Security.Cryptography.RandomNumberGenerator]::Create()).GetBytes($b); $k = [Convert]::ToBase64String($b)
dotnet run --project backend/src/Squid.Service -- --urls http://127.0.0.1:5601 --Storage:Path=$env:TEMP\squid-a.db --Media:BlobRoot=$env:TEMP\squid-a-blobs --Squid:RecoveryKey=$k --Federation:TrustedPeerHosts=127.0.0.1,localhost
```

Terminal B (o mesmo comando com a chave do B):

```powershell
$b = New-Object byte[] 32; ([Security.Cryptography.RandomNumberGenerator]::Create()).GetBytes($b); $k = [Convert]::ToBase64String($b)
dotnet run --project backend/src/Squid.Service -- --urls http://127.0.0.1:5602 --Storage:Path=$env:TEMP\squid-b.db --Media:BlobRoot=$env:TEMP\squid-b-blobs --Squid:RecoveryKey=$k --Federation:TrustedPeerHosts=127.0.0.1,localhost
```

Por que cada chave existe (tudo lido do código, nada inventado):

| Chave | Fato de origem | Se faltar |
| --- | --- | --- |
| `--urls` | sobrepõe o `applicationUrl` do `launchSettings.json` (59311/59312); o log confirma `Now listening on: http://127.0.0.1:5601` | as duas instâncias brigariam pela mesma porta HTTP |
| `Storage:Path` | default é o **mesmo** arquivo para todo processo (`AppContext.BaseDirectory\squid-grainstate.db`, `SquidServiceHost.cs:93`) | os dois silos leriam e gravariam no mesmo banco |
| `Media:BlobRoot` | default também é compartilhado (`MediaOptions.cs:34`) | um volume de mídia para dois silos |
| `Squid:RecoveryKey` | base64 de 32 bytes (`RuntimeSigningKeyRing.cs:44`); duplo embrulho no registro (GAP-50, `0009_instance_signing_keys`) | aviso `federation output disabled…` e toda entrega sai com motivo `federation_output_disabled` |
| `Federation:TrustedPeerHosts=127.0.0.1,localhost` | allowlist **exata e vazia por padrão** (GAP-49, `TrustedPeerHosts.cs:25`); só ela libera loopback/rede privada nos clientes de federação | entrega a `127.0.0.1` recusada antes do socket → `send_failed`, backoff e, em 8 tentativas, DLQ `max_attempts` |

Saída esperada (prova): os dois respondem

```powershell
curl.exe -fsS http://127.0.0.1:5601/health   # EXECUTÁVEL — exit 0 (HTTP 200)
curl.exe -fsS http://127.0.0.1:5602/health   # EXECUTÁVEL — exit 0 (HTTP 200)
```

> **Portas do Orleans (caveat medido na prova).** `UseLocalhostClustering()` fixa silo/gateway em
> 11111/30000 e **não existe chave de config** para elas (`SquidServiceHost.cs:256`); o harness de
> dois silos da TASK-0047 resolve isso em processo único com `Configure<EndpointOptions>` rodando
> **depois** de `UseOrleans` (`TwoSiloHarness.cs:128`). Nesta prova, duas instâncias `dotnet run`
> no mesmo host **sobem e cada uma serve o próprio banco** (50/50 consultas `200` sem vazamento
> cruzado, bancos distintos, federação A↔B completa) — Windows aceita o segundo bind. Se aparecer
> grau de um silo respondendo com o dado do outro ou timeout de grain, suba um dos lados em
> contêiner (rede própria): `podman compose -f infra/compose.yaml up -d` põe o silo em 8080 com as
> portas Orleans só dentro do contêiner (compose/Containerfile, porta exposta só 8080) — essa
> alternativa **não** foi executada nesta prova.

### 3. Worker de projeção ao lado de cada silo — EXECUTÁVEL (necessário para ver o feed)

A projeção (feed/timeline) roda no `Squid.Worker`, fora do caminho de escrita (R-18): sem ele a
API fica de pé, mas `GET /api/containers/{slug}/feed` devolve `items: []`. Suba **depois** do
serviço (o esquema é aplicado pela inicialização do `Squid.Service`) com o **mesmo** `Storage:Path`
e `Media:BlobRoot` do silo correspondente:

```powershell
dotnet run --project backend/src/Squid.Worker -- --Storage:Path=$env:TEMP\squid-a.db --Media:BlobRoot=$env:TEMP\squid-a-blobs   # silo A
dotnet run --project backend/src/Squid.Worker -- --Storage:Path=$env:TEMP\squid-b.db --Media:BlobRoot=$env:TEMP\squid-b-blobs   # silo B
```

### 4. Modo domínio canônico — configurar `Instance:Domain` (EXECUTÁVEL)

`Instance:Domain` é opcional (I-11/GAP-45): sem ela, todo ID publicado usa scheme+Host da
própria requisição (regra byte-idêntica à anterior). Com ela, a origem canônica passa a ser
**sempre `https://{Instance:Domain}`** (`CanonicalOrigin.cs:28`) — inclusive o `subject` do
webfinger —, para trás de proxy/reverso:

```powershell
dotnet run --project backend/src/Squid.Service -- --urls http://127.0.0.1:5603 --Storage:Path=$env:TEMP\squid-dom.db --Media:BlobRoot=$env:TEMP\squid-dom-blobs --Squid:RecoveryKey=$k --Federation:TrustedPeerHosts=127.0.0.1,localhost --Instance:Domain=squid-a.test
```

Prova (registrando `domtest`): `GET /users/domtest` devolve `"id": "https://squid-a.test/users/domtest"`
e o webfinger consultado em `127.0.0.1:5603` devolve `"subject": "acct:domtest@squid-a.test"`.

Como a validação local usa `http://127.0.0.1:560x` como identidade, **o caminho executável desta
task roda SEM `Instance:Domain`**; ligue a chave só atrás de um proxy com TLS (passos 6 e 7).
Atrás de proxy confiável acrescente `--Instance:ForwardedHeaders=true`
(`SquidServiceHost.cs:282` instala o middleware só com a chave; sem ela `X-Forwarded-*` é
ignorado).

### 5. Pares de referência: Mastodon e Friendica em Docker local — GATE-HUMANO (TASK-0049)

Fontes e pré-requisitos vêm do quickstart do repositório (`specs/001-squid-mvp/quickstart.md`,
§US6 "Interop real"): Mastodon de referência `ideias-references/mastodon-main` **5.0.0-alpha.1** e
Friendica `friendica-2026.08-rc`, **ou as imagens oficiais com a mesma versão**, e a regra que o
próprio quickstart fixa: **Mastodon não federa por `localhost` nem por HTTP** — em máquina local
use túnel ou Caddy com CA instalada nos contêineres. Este repositório não traz compose deles:

```bash
# EXTERNO + GATE-HUMANO — compose oficial do projeto, versionado conforme o quickstart
# Mastodon: https://github.com/mastodon/mastodon (docker-compose.yml oficial + .env.production.sample)
# Friendica: https://friendica.net/ (distribuição oficial, compose do projeto)
podman compose -f <compose-oficial-do-projeto> up -d     # GATE-HUMANO — execução conjunta na TASK-0049
```

Onde o `.env` do par nomeia a identidade, ele precisa bater com o domínio do par (fonte externa:
`.env.production.sample` do Mastodon traz `LOCAL_DOMAIN=example.com` — "identifies your server and
cannot be changed safely later"; para domínio público distinto do host, `WEB_DOMAIN`, mesma fonte).
`SECRET_KEY_BASE`, `DB_PASS` e afins são segredo do par — nunca entram neste repositório.

### 6. Apontar cada par para o silo

1. **hosts do host** (EXTERNO — passo de sistema, executado pelo humano):
   `127.0.0.1 squid-a.test squid-b.test mastodon.test friendica.test` em
   `C:\Windows\System32\drivers\etc\hosts` (Windows) ou `/etc/hosts` (Linux).
2. **silo atrás do TLS do par** (GATE-HUMANO): no modo domínio (passo 4), o proxy local precisa
   terminar `https://squid-a.test` e reencaminhar para `http://127.0.0.1:5601`, com
   `--Instance:ForwardedHeaders=true` no silo — sem isso os IDs publicados são `https://` e o par
   não os resolve.
3. **do lado do silo**, liberar a rede do par quando ela for privada (GAP-49; produção = gate
   humano): `--Federation:TrustedPeerHosts=127.0.0.1,localhost` já cobre pares no mesmo host; um
   par em outra máquina entra nessa mesma lista, nome por nome (sem curinga).
4. **descoberta do silo pelo par**: no terminal do par, `curl https://squid-a.test/.well-known/webfinger?resource=acct:alice@squid-a.test`
   (EXTERNO) deve devolver JSON com `links.self` apontando para `https://squid-a.test/users/alice`.

### 7. [Opcional] Instância pública real — **GATE HUMANO**

> **Esta seção inteira é gate humano**: domínio, TLS e túnel/exposição à internet só são
> executados com o humano junto (SPEC-0011 decisão 6 e fora de escopo do MVP: "Exposição pública
> da instância (TLS, túnel, DNS) — seção opcional do runbook, execução humana"). Nada aqui roda
> sozinho nem em CI.

Conteúdo (GATE-HUMANO, sem comando executável por conta própria):

1. **Domínio**: registrar domínio público, apontar DNS para a máquina/túnel e usar o mesmo valor
   em `Instance:Domain` no silo.
2. **TLS**: terminar HTTPS no proxy diante do silo (Caddy/nginx/túnel com certificado válido) e
   ligar `--Instance:ForwardedHeaders=true` — atrás de proxy os IDs publicados são o domínio
   público, nunca o host interno (SPEC-0011 §A).
3. **Túnel**: expor somente o proxy (Cloudflare Tunnel, frp, port forwarding com firewall) — o
   silo continua ouvindo só loopback.
4. **Pares confiáveis = gate humano** (GAP-49): `Federation:TrustedPeerHosts` é vazia por padrão e
   em produção o valor é decisão humana registrada; unfurl continua bloqueando rede privada com a
   lista nomeada.
5. **Segredo** `Squid:RecoveryKey` injetado pelo humano no boot (decisão 2 da SPEC-0011:
   produção = gate humano); nunca em arquivo versionado.
6. Validação igual à seção Verificação, porém com os domínios públicos no lugar de
   `127.0.0.1:560x` — e os pares reais Mastodon/Friendica (TASK-0049) no lugar do silo B.

## Verificação

Sequência executada na prova desta task (Windows PowerShell; bash troca `curl.exe`→`curl` e usa
aspas simples no JSON). Marco: EXECUTÁVEL em todos os passos.

1. **Saúde** (exit 0 / HTTP 200 nos dois):

   ```powershell
   curl.exe -fsS http://127.0.0.1:5601/health
   curl.exe -fsS http://127.0.0.1:5602/health
   ```

2. **Superfície AP de cada silo** (EXIT 0; nodeinfo devolve `"software":{"name":"squid"`):

   ```powershell
   curl.exe -s http://127.0.0.1:5601/.well-known/nodeinfo
   curl.exe -s -H "Accept: application/activity+json" http://127.0.0.1:5601/users/alice
   ```

3. **Uma conta em cada silo** (esperado `201`):

   ```powershell
   $jarA = "$env:TEMP\jar-a.txt"; $jarB = "$env:TEMP\jar-b.txt"
   curl.exe -s -o NUL -w "%{http_code}" -c $jarA -X POST http://127.0.0.1:5601/api/auth/register -H "Content-Type: application/json" -d '{\"handle\":\"alice\",\"password\":\"<senha-forte>\",\"displayName\":\"Alice\"}'
   curl.exe -s -o NUL -w "%{http_code}" -c $jarB -X POST http://127.0.0.1:5602/api/auth/register -H "Content-Type: application/json" -d '{\"handle\":\"bob\",\"password\":\"<senha-forte>\",\"displayName\":\"Bob\"}'
   ```

4. **Comunidade com o MESMO `slug` nos dois lados** (esperado `201` em ambos — o destino do
   `Create` remoto é o slug: sem contêiner correspondente o par responde `202` e não aplica):

   ```powershell
   $csrfA = ((Get-Content $jarA | Where-Object { $_ -match 'squid_csrf' }) -split "`t")[-1]
   $csrfB = ((Get-Content $jarB | Where-Object { $_ -match 'squid_csrf' }) -split "`t")[-1]
   curl.exe -s -o NUL -w "%{http_code}" -b $jarA -c $jarA -X POST http://127.0.0.1:5601/api/containers -H "Content-Type: application/json" -H "X-CSRF-Token: $csrfA" -d '{\"kind\":\"community\",\"name\":\"InteropDemo\",\"slug\":\"interop-demo\",\"summary\":\"runbook-TASK-0048\"}'
   curl.exe -s -o NUL -w "%{http_code}" -b $jarB -c $jarB -X POST http://127.0.0.1:5602/api/containers -H "Content-Type: application/json" -H "X-CSRF-Token: $csrfB" -d '{\"kind\":\"community\",\"name\":\"InteropDemo\",\"slug\":\"interop-demo\",\"summary\":\"runbook-TASK-0048\"}'
   ```

5. **Aperto de mão de follow nos dois sentidos** (esperado `202`; `X-CSRF-Token` é o cookie
   `squid_csrf` — dupla submissão exigida em toda mutação):

   ```powershell
   curl.exe -s -o NUL -w "%{http_code}" -b $jarB -c $jarB -X POST http://127.0.0.1:5602/api/federation/follow -H "Content-Type: application/json" -H "X-CSRF-Token: $csrfB" -d '{\"targetActorUri\":\"http://127.0.0.1:5601/users/alice\"}'
   curl.exe -s -o NUL -w "%{http_code}" -b $jarA -c $jarA -X POST http://127.0.0.1:5601/api/federation/follow -H "Content-Type: application/json" -H "X-CSRF-Token: $csrfA" -d '{\"targetActorUri\":\"http://127.0.0.1:5602/users/bob\"}'
   Start-Sleep -Seconds 6
   ```

   Esperado: o `Follow` assinado chega ao outro silo e vira relação — nos dois lados
   `GET /users/{alvo}/followers` (com `Accept: application/activity+json`) responde
   `"totalItems":1`. **Observado na prova:** as coleções `following` (local segue remoto) seguem
   em `0` no par Squid↔Squid — o `Accept` vem no corpo do `202` e o remetente ainda não o
   processa (só o status é lido, `HttpPeerSender.cs:87`); as entregas e os `followers` funcionam.
   Comportamento registrado para o ODEV, não é falha deste runbook.

6. **Publicar nos dois lados e validar a entrega** (esperado `201`):

   ```powershell
   curl.exe -s -o NUL -w "%{http_code}" -b $jarA -c $jarA -X POST http://127.0.0.1:5601/api/containers/interop-demo/posts -H "Content-Type: application/json" -H "X-CSRF-Token: $csrfA" -d '{\"content\":\"ola-da-A-para-B-TASK0048\"}'
   curl.exe -s -o NUL -w "%{http_code}" -b $jarB -c $jarB -X POST http://127.0.0.1:5602/api/containers/interop-demo/posts -H "Content-Type: application/json" -H "X-CSRF-Token: $csrfB" -d '{\"content\":\"ola-da-B-para-A-TASK0048\"}'
   Start-Sleep -Seconds 8
   ```

7. **Feed de cada silo (com o Worker do passo 3 rodando)** — cada feed contém o **próprio** post e
   o **do outro silo** (EXIT 0):

   ```powershell
   curl.exe -s -b $jarA http://127.0.0.1:5601/api/containers/interop-demo/feed   # deve conter o-da-B-para-A-TASK0048
   curl.exe -s -b $jarB http://127.0.0.1:5602/api/containers/interop-demo/feed   # deve conter o-da-A-para-B-TASK0048
   ```

   Sem Worker o feed fica vazio (prova: `{"items":[],…}`) — não é falha de entrega.

8. **Estado da fila e dos pares** (EXIT 0; leitura direta da `0008_federation.sql`):

   ```powershell
   python -c "import sqlite3,sys;c=sqlite3.connect(sys.argv[1]);print(sys.argv[2]);[print(r) for r in c.execute(sys.argv[3])]" $env:TEMP\squid-a.db "A fila:" "select activity_type, state, attempts, reason from peer_delivery"
   python -c "import sqlite3,sys;c=sqlite3.connect(sys.argv[1]);print(sys.argv[2]);[print(r) for r in c.execute(sys.argv[3])]" $env:TEMP\squid-b.db "B pares:" "select domain, status, failure_count from peer"
   ```

   Esperado após a validação: linhas `Follow` e `Create` com `state = 'Sent'` e
   `peer.status = 'Healthy'` para o par em uso.

9. **O outro lado recebeu a entrega assinada** (EXIT 0): a outbox do par contém a nota com `id`
   de origem do silo vizinho (`http://127.0.0.1:5601/o/…` vista no silo B) — atividade aplicada,
   sem `signature verification failed` em nenhum dos dois logs (contrato §7).

10. **Interromper** — `Ctrl+C` em cada terminal (silo e worker); nada apaga banco nem volume:
    `Storage:Path` e `Media:BlobRoot` ficam nos caminhos temporários escolhidos no passo 2.

### Falha comum e diagnóstico

| Sintoma | Causa provável | Runbook |
| --- | --- | --- |
| log `federation output disabled: Squid:RecoveryKey is missing or invalid` | chave ausente/errada no boot | passo 1 |
| entrega nunca sai, `send_failed` em `127.0.0.1` | `Federation:TrustedPeerHosts` não nomeia o par | passo 2 |
| feed sempre vazio | `Squid.Worker` não rodando | passo 3 |
| `202` mas nada aplicado no par | contêiner com slug diferente (destino nomeado) | passo 4 da Verificação |
| entrega em DLQ | ver [dlq-cheia.md](dlq-cheia.md) | — |
| fila presa com backoff | ver [peer-entrega-travada.md](peer-entrega-travada.md) | — |
