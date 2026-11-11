<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: VEREDITO
- owner: qa
- lastVerified: 2026-11-09 22:13

# QA-0025 — SSE `GET /api/stream` ao vivo

- task: TASK-0026
- spec: SPEC-0009
- runId: R-20261109-01
- verdict: PASSED
- qaAttempt: 1/2

Leitura normativa usada: SPEC-0009 seção D + contrato `http-api.md` §5 (quadro `event: post`,
`id` = `Activity.Seq` global, `Last-Event-ID` = só o delta, `event: ping` a cada 15 s) e §9.4,
`spec.md` FR-024, `research.md` R-16, e o primeiro ponto do DoD do Sprint 3 (post chega em
menos de 1 s). Baseline do DEV **304/304** (293 + 11 novos) foi a porta de entrada desta QA. A
QA executou **6 cenários próprios** (`QaTask0026Tests`), **7 ciclos de mutação can-fail**, o
gêmeo bash do gate no WSL e o gate final — nada foi apenas lido.

## What is proven

| AC | Scenario (executed) | Script | Result |
| --- | --- | --- | --- |
| AC-01 | `Ac01_qa_publish_reaches_the_subscriber_in_under_a_second_with_global_seq_and_raw_payload` — 3 rondas de publicação com cronômetro no fio: **140 / 67 / 207 ms**, todas < 1 s (DoD do Sprint 3); `event: post`; `id` = `Activity.Seq` **global** lido do log; `data` **byte-idêntico** ao payload do `IActivityStore` (R-13, sem re-serializar) e texto-idêntico ao `PostView` da resposta 201, com `content`/`author`/`containerId`. Can-fail **M1** (`"Create" => "post"` → `posts`): vermelho `20261109-220845` (`Expected: "post" / Actual: "posts"`) | `backend/tests/Squid.Tests/Stream/QaTask0026Tests.cs` · `sdd-test.ps1 test -TestFilter FullyQualifiedName~QaTask0026Tests` | PASS |
| AC-02 | `Ac02_qa_reconnect_after_six_missed_events_delivers_exactly_the_visible_delta` — reconexão com `Last-Event-ID` depois de **SEIS** eventos perdidos (5 visíveis + 1 invisível de um MembersOnly no meio): o replay traz exatamente `[18,19,21,22,23]`, em ordem de seq, cada um uma vez, **sem** o cursor `17`, **sem** o invisível `20`, **sem** repetição; a mesma conexão continua entregando ao vivo depois do backlog. Can-fail **M2** (`afterSeq` → `afterSeq - 1`): vermelho `20261109-220914` | mesmo arquivo · filtro `.Ac02` | PASS |
| AC-03 | `Ac03_qa_idle_connection_pings_on_the_injected_interval_without_an_id` — dois `event: ping` com **gap de 204 ms** sob `Stream:PingSeconds=0.2` (o contrato manda 15 s e a janela do teste é 5 s), `id` **nulo** (ping nunca move o `Last-Event-ID` do cliente) e `data: {}`. O default de 15 s é coberto pelo teste do DEV `StreamConfigTests.Ping_interval_defaults...` no gate. Can-fail **M3** (configurabilidade desligada → 15 s): vermelho `20261109-220948` dentro da janela | mesmo arquivo · filtro `.Ac03` | PASS |
| AC-04 | `Ac04_qa_posts_that_became_forbidden_while_the_subscriber_was_away_never_replay` — **gate lido na ENTREGA**: com o assinante desconectado, três posts proibidos são escritos — (1) MembersOnly desde sempre, (2) publicado em contêiner **Público** e rebaixado para MembersOnly **depois**, (3) publicado com o leitor **Active** e banido **depois** — e na reconexão o replay entrega **só o controle** (`ids no replay=[16]`), nenhum dos três (`nomembro=13 politica=14 ban=15 controle=16`, todos antes do controle na seq). Espelho `Ac04b_qa_a_post_that_became_visible_while_the_subscriber_was_away_is_delivered_on_replay` (extra): o post de um contêiner que **afrouxou** para Público durante a desconexão **entrega** (`[26,27]`) — a leitura é corrente nos dois sentidos, como a do feed. Can-fail: **M4** (gate da entrega removido) vermelho `20261109-221017` (Ac04) e **M7** (gate nega tudo) vermelho `20261109-221215` (Ac04b) | mesmo arquivo · filtros `.Ac04` e `.Ac04b` | PASS |
| AC-05 | `Ac05_qa_sessionless_connect_is_401_and_no_subscriber_can_block_or_fail_a_publish` — sem sessão: `401 not_authenticated` com `application/json` (nunca `text/event-stream`); publicador livre: **8 publicações** (5 com assinante registrado no hub e **nunca lendo**, 3 depois dele sair) todas `201` em **1564 ms**, cada uma com trava de 10 s. Can-fail: **M5** (401 → 403) vermelho `20261109-221047`; **M6** (hub falha quando existe assinante) derruba a família `Squid.Tests.Stream` inteira — **11 falhas em 17**, incluindo o teste de 10 000 publicações do DEV (`StreamHubTests`) e as rotas de stream — vermelho `20261109-221126` | mesmo arquivo · filtros `.Ac05` e `~Squid.Tests.Stream` | PASS |
| AC-06 | Gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` → **log final com `exit: 0` e `Com falha: 0, Aprovado: 310, Total: 310`** (304 + 6 da QA), lido **dentro** do log (S-22). Escopo por mtime desde o dispatch do DEV (20:19:00, sem bin/obj) → **12 arquivos, todos dentro dos `touches`** (`Squid.Api/Posts/**`, `Squid.Service/**`, `Squid.Tests/Stream/**`); delta do git contra `QA-0025-git-baseline.txt` → só `docs/sdd/qa/`, `docs/sdd/runs/test-runs/` e o teste novo em `tests/Squid.Tests/Stream/`; resíduo `QA-MUTATION` em `backend/` = **0**; SHA256 dos 3 arquivos mutados = **PRE** | gate + varreduras + `qa-check-task-0026.ps1` / `.sh` | PASS |

Cenários extras executados (fora dos ACs, sustentam os de cima):

| Fora dos ACs | Scenario | Result |
| --- | --- | --- |
| id global × Object.Seq (GAP-23) | no espelho do AC-04, o primeiro post de um contêiner nascente vale `Object.Seq = 1` (`SqliteObjectStore.cs:43`) e chega ao fio com **id global 13** — o quadro fala o `Activity.Seq`, não a seq do contêiner | PASS (asserção executada) |
| durabilidade antes do wake | no AC-01, ao ler o quadro o evento **já está** em `IActivityStore` (`Single(ObjectId == postId)` do payload byte-idêntico falharia se o wake viesse antes do append) + o caminho do código (`PostEndpoints.cs:138-144` e `:282-288`: `AppendAsync` → `hub.Publish()`) | PASS |
| só `event: post` (Delete reservado à TASK-0030) | `StreamSessionTests` do DEV verdes no gate e vermelhos sob M6 — a leitura do log e o mapa de eventos são exercitados | PASS |

## Scripts

| Script | Language / runner | What it does |
| --- | --- | --- |
| `backend/tests/Squid.Tests/Stream/QaTask0026Tests.cs` | xunit via `sdd-test.ps1 test -TestFilter` | 6 cenários próprios da QA (AC-01 a AC-05 + espelho do AC-04) |
| `docs/sdd/qa/qa-mutate-task-0026.ps1` | pwsh (gate `sdd-test.ps1`) | 7 ciclos can-fail: SHA256 PRE (S-16) → 1 mutação `QA-MUTATION` com pattern-count=1 → vermelho classificado **no log** (S-22) → restore byte-exact + bump de mtime (S-23) → hash=PRE → verde |
| `docs/sdd/qa/QA-0025-observations.log` | artefato (`--logger console;verbosity=detailed`) | saída das observações: latências, ids do delta, gap dos pings, reels de replay, tempo das publicações |
| `docs/sdd/qa/QA-0025-mutations.log` | artefato | saída dos 7 ciclos (M1..M7) + `SUMMARY cycles=7 failed=0 residue-QA-MUTATION=0 temp-backups=0` |
| `docs/sdd/qa/QA-0025-sh-twin.log` | artefato (WSL bash) | execução do gêmeo `sdd-test.sh` (probe/test/test com alvo) no Linux |
| `docs/sdd/qa/qa-check-task-0026.ps1` | pwsh | 19 checks de evidência (logs S-22, hashes, resíduo, escopo, git, sdd-state, doc) |
| `docs/sdd/qa/qa-check-task-0026.sh` | bash (WSL) | gêmeo com os mesmos 19 checks, executado no Linux (inclui normalização dos artefatos CRLF/BOM sem tocar os originais) |

## How to reproduce

Prerequisites: Windows com `dotnet` 10 e o repositório em `C:\dev\squid-squad-net\squid-execution` (nenhuma instalação, nenhum serviço externo — os testes sobem host local em porta dinâmica).

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~QaTask0026Tests"   # esperado: exit 0, Aprovado 6, Total 6
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0026.ps1                                                 # esperado: exit 0, SUMMARY cycles=7 failed=0 residue-QA-MUTATION=0 temp-backups=0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test                                                    # esperado: exit 0, Com falha 0 / Aprovado 310 / Total 310
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0026.ps1                                                   # esperado: RESULT: PASS, exit 0
bash docs/sdd/qa/qa-check-task-0026.sh                                                                                                  # esperado: RESULT: PASS, exit 0 (WSL)
```

## Last run

| Date/Time | Command | Exit | Output (max 10 lines) |
| --- | --- | --- | --- |
| 2026-11-09 21:35:40 | `sdd-test.ps1 test` (gate de baseline da QA) | 0 | `20261109-213326-test-gate.log` → `exit: 0` · `Com falha: 0, Aprovado: 304, Total: 304` |
| 2026-11-09 21:41:36 | `sdd-test.ps1 test -TestFilter FullyQualifiedName~QaTask0026Tests` (1ª versão, 5 cenários) | 0 | `20261109-214136-test-filter.log` → `Com falha: 0, Aprovado: 5, Total: 5` |
| 2026-11-09 ~21:44 | `dotnet test ... --logger console;verbosity=detailed` (1ª coleta, 5 observações) | 0 | artefato substituído pela coleta das 6 observações abaixo |
| 2026-11-09 21:52:25 | `qa-mutate-task-0026.ps1` (1ª execução, 6 ciclos) | 0 | `QA-0025-mutations.log` → 6× PROVEN (artefato substituído pelas execuções seguintes) |
| 2026-11-09 21:52:45 | `wsl bash docs/sdd/_tools/sdd-test.sh probe / test / test backend/Squid.slnx` | 0 / 1 / 1 | `QA-0025-sh-twin.log` → `dotnet: command not found` · `no *.sln found within depth 3` · `test: exit 127 (real test failure)` |
| 2026-11-09 22:00:26 | `sdd-test.ps1 test -TestFilter FullyQualifiedName~QaTask0026Tests` (6 cenários) | 0 | `20261109-220026-test-filter.log` → `Com falha: 0, Aprovado: 6, Total: 6` |
| 2026-11-09 22:01:30 | `dotnet test ... --logger console;verbosity=detailed` | 0 | `QA-0025-observations.log` → `Aprovados: 6` · latências `140/67/207 ms` · delta `[18,19,21,22,23]` · ping `204 ms` · replay `[16]` · espelho `[26,27]` · `8 publicacoes ... 1564 ms` |
| 2026-11-09 22:02–22:05 | `qa-mutate-task-0026.ps1` (2ª execução, 6 ciclos, com Ac04b) | 0 | 6× PROVEN (artefato substituído pela 3ª execução) |
| 2026-11-09 22:12:30 | `qa-mutate-task-0026.ps1` (**execução final, 7 ciclos**) | 0 | `QA-0025-mutations.log` → M1..M7 `PROVEN` · reds `220845/220914/220948/221017/221047/221126/221215` · `SUMMARY cycles=7 failed=0 residue=0 temp-backups=0` |
| 2026-11-09 22:16:54 | `sdd-test.ps1 test` (gate final AC-06) | 0 | `20261109-221654-test-gate.log` → `exit: 0` · `Com falha: 0, Aprovado: 310, Total: 310` (304 + 6 da QA) |
| 2026-11-09 22:19 | `qa-check-task-0026.ps1` (1ª passada) | 1 | console → 18/19; `TASK-ROWS` 7 de 8 porque as linhas próprias do checker ainda não existiam (ordem dos passos, não defeito do artefato) |
| 2026-11-09 22:21 | `wsl bash docs/sdd/qa/qa-check-task-0026.sh` (gêmeo Linux) | 0 | `QA-0025-check.log` run1 → `SUMMARY total=19 fails=0` · `RESULT: PASS` |
| 2026-11-09 22:23 | `qa-check-task-0026.ps1` (re-executado após os appends) | 0 | `QA-0025-check.log` run2 → `SUMMARY total=19 fails=0` · `RESULT: PASS` |
| 2026-11-09 22:25 | `wsl bash docs/sdd/qa/qa-check-task-0026.sh` + `qa-check-task-0026.ps1` (confirmação final) | 0 / 0 | `QA-0025-check.log` runs 3-4 → `SUMMARY total=19 fails=0` · `RESULT: PASS` **nos dois sistemas**, depois da última linha de evidência |

## Findings

| # | Severity | Where | What breaks | Evidence |
| --- | --- | --- | --- | --- |
| 1 | MINOR (pré-existente) | `docs/sdd/_tools/sdd-test.sh:20-21` (e `:64`) | O gêmeo Linux do gate só procura `*.sln` enquanto o solution é `*.slnx`, e **não tem a guarda de ambiente** que o `.ps1` tem: sem `dotnet` no WSL ele imprime `test: exit 127 (real test failure)` — classifica ferramenta ausente como falha real (S-22 no traje de Linux). Re-executado aqui; mesmo achado da QA-0024 | `QA-0025-sh-twin.log` (três execuções com exit 0/1/1) |
| 2 | MINOR (fora dos ACs) | `backend/src/Squid.Api/Posts/StreamHub.cs:19` | O hub não põe teto em assinantes simultâneos: `research.md:334-339` (R-16) menciona "Limite de assinantes por conexão", enquanto a SPEC-0009 D e os 6 ACs não pedem nenhum — se a leitura do R-16 for literal, falta uma regra (e um cliente pode abrir conexões sem parar, cada uma com o seu `Channel` e a sua varredura do log) | leitura de `StreamHub.cs` + `research.md:334-339`; nenhum AC cobre o teto |
| 3 | MINOR (ambíguo) | `specs/001-squid-mvp/contracts/http-api.md:120` | O exemplo do `data` no §5 traz `"author":{...}` (objeto) enquanto o quadro entrega a **string** do `PostView` (`PostView.cs:20`) — que é o mesmo shape de `GET /api/posts/{id}` aprovado na QA-0022. Se o exemplo for literal, o contrato pede emenda; como `{...}` é élide, a QA não bloqueia | `QaTask0026Tests.Ac01` executado (`author` string não vazia) + contrato §5 |

Notas (não são defeitos): as entradas 1-2 do `## Log` da task carregam o prefixo `- data | role |` duplicado (artefato append-only já registrado pelo ODEV — não editado, não cobrado aqui); o `shortcut:` de `StreamSession.cs:60` (uma varredura do log inteiro por wake) é a ideia estacionada pelo ODEV, da família S-32.

**Pre-existing failures on baseline 304: NENHUM** — o gate de baseline desta QA rodou 304/304 `exit: 0` (`20261109-213326`), lido dentro do log, e nada regrediu depois (gate final abaixo).

## Not covered, and why

- **Suíte do produto em Linux** — o WSL desta máquina (`podman-machine-default`) não tem `dotnet` (instalar exige gate humano; aprovações estão desativadas nesta sessão) → `unverified`, nunca assumido igual ao Windows que passou. O que **foi** executado no Linux: os gêmeos bash (`sdd-test.sh` — achado 1 — e `qa-check-task-0026.sh`).
- **`qa-mutate-task-0026.ps1` em Linux** — o driver orquestra `sdd-test.ps1`/`dotnet`, indisponíveis no WSL → gêmeo `.sh` não faz sentido e não foi escrito; a restauração byte-exact (S-16/S-23) é provada no Windows e conferida de novo pelos checkers nos dois sistemas.
- **Tipos de `Activity` além de `Create` (`Delete` etc.)** — o braço é reservado à TASK-0030 e nenhum AC desta task pede outro `event:`; só `post` foi testado.
- **Latência sob proxy/carga real** — o < 1 s do DoD foi medido em host em processo (é o que o DoD do Sprint 3 pede); uma regressão atrás de proxy só apareceria no load test da SPEC-0009 J, que é task própria.
- **Teto de assinantes** (achado 2) — se o ODEV adjudicar o R-16 literalmente, a regra nasce na task que o implementar; nenhum teste atual cobre o limite.
- **Reconexão trocando de banco/instância** — fora do escopo (MVP em processo, um único `IActivityStore`).

## Memory bank (rota `qa-integracao`)

- `[DIVERGENCIA]` `docs/memory-bank/contracts.md:18` — diz que publicação, timeline e demais rotas "seguem sem handler"; desde as TASK-0023/0025/0026 (inclui `GET /api/stream`) seguem com handler. Já registrado pelo ODEV para a consolidação da P-04 (Log da task, 21:20:15) e re-confirmado nesta leitura; o arquivo só se escreve no fecho.
