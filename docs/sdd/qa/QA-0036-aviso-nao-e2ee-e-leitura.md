<!--
templateVersion: 1.0.0
 -->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: RASCUNHO
- owner: qa
- lastVerified: (último artefato: docs/sdd/runs/test-runs/20261112-055345-test-gate.log)

# QA-0036 — Aviso não-E2EE (428) e marcação de leitura (TASK-0036)

- runId: R-20261111-01 · attempt: **1/2** (bloco de controle `qaAttempts: 0/2` — contador é do ODEV, QA não escreve nele)
- QA: subagente qa-task + política ponytail-qa (full) — testes EXECUTADOS nesta sessão, não escritos e deixados
- leitura normativa: SPEC-0010 §D + Gaps GAP-33/37/38 + AC-01..AC-06 da task + FR-032/FR-033 (`specs/001-squid-mvp/spec.md`) + contrato http-api §6 (`ack-no-e2ee`/`read`), §8 (428), §9 item 6 + data-model §3.1 (`Suspended`)
- commit sob verificação: `6914bb3` em `task/TASK-0036-aviso-nao-e2ee-e-leitura` (9 arquivos = 4 produto + 5 teste, todos nas `touches` emendadas); baseline DEV `456/456`, gate desta QA idem `456/456`
- verdict: **PASSED**

## AC → cenário (6 ACs; todos executados nesta sessão)

| AC | Cenário | Execução (destaque) | Resultado |
| --- | --- | --- | --- |
| AC-01 | `POST .../ack-no-e2ee` registra o aceite **por (ator, thread)** e é idempotente (repetir não duplica nem quebra) | `ThreadAckAndReadRoutesTests.Sending_without...` (2º ack `200` + `COUNT(*)==1` na fonte) e `ThreadStoreTests.The_append_gates...` (repetição mantém a PRIMEIRA linha) verdes no baseline `20261112-053508-test-filter.log` (**exit 0, 25/25**) e no GREEN `054652`. Can-fail: **M4** (PK sem `DO NOTHING`: segundo aceite estoura → `054044`, 3 vermelhas + `constraint=yes`), **M14** (linha gravada SEM o texto do aviso → `054549`, teste temporário da QA) | PASS |
| AC-02 | Sem aceite → `428 precondition_required`; depois do aceite o MESMO envio → `200` com `seq` que **não foi queimada**; aceite de um ator não libera outro | `ThreadAckAndReadRoutesTests.Sending_without...` (428 + história vazia → ack → `200 seq 1` → par segue `428` → ack dela → `seqs {1,2}`) + `ThreadStoreTests.The_append_gates...` (`AckRequired` sem linha e `message_seq==0`). Can-fail: **M1** (428→409 `version_conflict` → `053916`), **M2** (recusa que QUEIMA número via commit antes do retorno → `053946`: store cai em `message_seq==0` e rota cai no `seq 1`), **M3** (gate por thread em vez de por ator → `054015`, 2 vermelhas) | PASS |
| AC-03 | `POST .../read`: marcador **só avança**; `seq` acima do total → `400` (GAP-38); vazio/negativo → `400`; `seq == total` é lido | `ThreadAckAndReadRoutesTests.The_read_marker_only_advances...` (nasce 0 → avança 2 → pedir 1 mantém 2 → acima de 3 é `400` com marcador intacto → `{}` e `-1` são `400` → `3` no limite) + store idem. Can-fail: **M5** (upsert sem `WHERE`: marcador REGRIDE → `054117`, 2 vermelhas), **M6** (portão `seq > total` desligado → `054148`), **M15** (seq negativo aceito → `054621`) | PASS |
| AC-04 | `lastReadSeq` em `GET /api/threads` e no envelope de mensagens, **por participante**; não-participante → 404 **idêntico** sem o dado | `ThreadAckAndReadRoutesTests.The_last_read_seq_reaches...` (lista e envelope do dono = 2; par = 0 nos dois; estranho fora da lista; GET/read/ack recusados com 4 corpos byte-a-byte iguais, sem `lastReadSeq`/`traceId`/id; marcador do dono intacto). Can-fail: **M7** (join do marcador sem o ator → `054218`), **M8** (envelope lê marcador de qualquer um → `054246`, 2 vermelhas), **M9** (portão da rota `read` desligado: estranho grava marcador alheio → `054320`), **M10** (404 ganha `traceId`: igualdade byte-a-byte cai em 3 suites → `054354`, 5 vermelhas) | PASS |
| AC-05 | Ator `Suspended` não envia: `403 policy_denied` **+ linha Deny na trilha ANTES da resposta**, checado **ANTES do 428**; participante ativo segue enviando; suspenso segue LENDO | `ThreadAckAndReadRoutesTests.A_suspended_actor_is_denied...` (suspenso com aceite → 403; suspenso SEM aceite → **403, não 428**; exatamente **2 linhas Deny** com "suspenso" e os dois `ActorId`; suspenso lê `200`; ativo envia `seq 2`, `seqs {1,2}`). Can-fail: **M11** (escada invertida: append+428 antes da checagem → `054422`), **M12** (linha Deny nunca gravada → `054452`) | PASS |
| AC-06 | `sdd-test.ps1 test` exit 0; nenhum arquivo alterado fora dos `touches` | Gate `20261112-055345-test-gate.log` → **`exit: 0` DENTRO do cabeçalho e `Com falha: 0, Aprovado: 456, Total: 456`** (S-22; 5m05s). Escopo (`QA-0036-scope.log`): `git status -- backend` **0 entradas**; blob-identity `git hash-object` = `git rev-parse HEAD:` em **2/2** arquivos mutados; mtime desde a baseline `05:32:52` = **exatamente esses 2**; resíduo `QA-MUTATION` = 0; teste temporário da QA removido; no repo inteiro só os 4 task files do ODEV + artefatos novos em `docs/sdd/qa/*` | PASS |

Extra de verificação (fora das ACs, pedido do dispatch): **regressão de parser** — o marcador `/participants/` precede os novos sufixos (`/ack-no-e2ee`, `/read`), e a **colisão real** (participante com handle `read`, que a regra `[a-z0-9_]{3,30}` permite) é coberta **apenas** pelo teste temporário desta QA: **M13** (sufixo `/read` movido na frente do marcador → remoção vira 404) foi pego por `QaTask0036RouteCollisionTests` em `054520` — nenhum teste do DEV tem essa forma. O texto do aviso FR-032 no registro (`M14`, `054549`) também só é asserted lá.

## Comandos executados (todos nesta sessão, com exit real; datas vêm dos próprios logs)

| # | comando | exit | artefato / saída |
| --- | --- | --- | --- |
| 1 | `git rev-parse branch+HEAD` + `git status -uall` antes da QA escrever (pwsh) | 0 | `docs/sdd/qa/QA-0036-git-baseline.txt` — branch `task/TASK-0036-aviso-nao-e2ee-e-leitura`, HEAD `6914bb3`, só os 4 task files do ODEV |
| 2 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Threads` (baseline com o teste temporário da QA) | 0 | `20261112-053508-test-filter.log` — `exit: 0`, Com falha 0 / Aprovado 25 / Total 25 (24 DEV + 1 QA) |
| 3 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0036.ps1 -VerifyOnly` (contagem dos 15 padrões + BOM) | 0 | stdout `VERIFY total=15 bad=0` + `BOM OK (UTF-8 BOM presente)` |
| 4 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0036.ps1` (execução cheia: 15 ciclos) | 0 | `QA-0036-mutations.log` — 15 vermelhos classificados NO LOG (S-22), 15× `RESTORE-OK hash=PRE residue=0`, `GREEN ... aprovado=25 falha=0`, `SUMMARY cycles=15 failed=0 residue=0 temp-backups=0` |
| 5 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` (gate AC-06, árvore restaurada sem o teste temporário) | 0 | `20261112-055345-test-gate.log` — `exit: 0` lido **dentro do log** (S-22) + `Com falha: 0, Aprovado: 456, Total: 456` |
| 6 | varredura de escopo (pwsh): `git status -- backend`, `git hash-object` vs `git rev-parse HEAD:`, mtime desde 05:32:52, resíduo, teste temporário, backups TEMP | 0 | `docs/sdd/qa/QA-0036-scope.log` — backend 0, blob-identity 2/2, mtime 2, resíduo 0, temp ausente |
| 7 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0036.ps1` | 0 | `docs/sdd/qa/QA-0036-check.log` — SUMMARY total=29 fails=0 |
| 8 | `wsl -e bash docs/sdd/qa/qa-check-task-0036.sh` (gêmeo Linux, parte estática) | 0 | `docs/sdd/qa/QA-0036-check-sh.log` — SUMMARY total=26 fails=0 |

## Ciclos de mutação (can-fail; S-16 SHA256 PRE, S-22 classificação no log, S-23 restore byte-exact + mtime)

Todos nos `touches` (`Squid.Api/Threads/**`, `Squid.Service/**`) — nenhum edit fora do escopo. Filtro: `FullyQualifiedName~Squid.Tests.Threads` (24 testes do DEV + 1 da QA enquanto esteve na árvore).

| Ciclo | Arquivo | O que prova | Vermelho classificado NO LOG |
| --- | --- | --- | --- |
| M1 | `Squid.Api/Threads/ThreadEndpoints.cs` | AC-02: a resposta do §9.6 é `428 precondition_required` (não vizinha do catálogo) | `Sending_without... [FAIL]` (`053916`, 1 falha) |
| M2 | `Squid.Service/SqliteThreadStore.cs` | AC-02: a recusa **não queima seq** (mutante dá commit num bump antes de recusar) | store `The_append_gates...` + rota `Sending_without...` (`053946`, 2) |
| M3 | idem | AC-02: gate **por (thread, ator)** — aceite de A não libera B | store + rota (`054015`, 2) |
| M4 | idem | AC-01: idempotência do aceite (PK + `DO NOTHING`) | 3 falhas + `constraint=yes`: store, rota e `ThreadParticipantRoutesTests.A_direct_thread...` (helper acka antes de cada envio) (`054044`) |
| M5 | idem | AC-03: marcador **só avança** (upsert sem o `WHERE`) | store + `The_read_marker_only_advances...` (`054117`, 2) |
| M6 | `Squid.Api/Threads/ThreadEndpoints.cs` | AC-03/GAP-38: `seq` acima do total → `400` | `The_read_marker_only_advances... [FAIL]` (`054148`, 1) |
| M7 | `Squid.Service/SqliteThreadStore.cs` | AC-04: `lastReadSeq` da **lista** é por participante (join sem o ator) | `The_last_read_seq_reaches...` (`054218`, 1) |
| M8 | idem | AC-04: `lastReadSeq` do **envelope** é por participante | store + `The_last_read_seq_reaches...` (`054246`, 2) |
| M9 | `Squid.Api/Threads/ThreadEndpoints.cs` | AC-04: portão de participante da rota `read` (estranho não grava marcador alheio) | `The_last_read_seq_reaches...` (`054320`, 1) |
| M10 | idem | AC-04/SC-005: o 404 compartilhado **sem traceId** — as 4 recusas byte-idênticas | 5 falhas em **3 suites** (rota + participantes × 2) (`054354`) |
| M11 | idem | AC-05: escada `401 → 404 → 403 suspenso → 400 → 428/200` (428 respondido ANTES do 403 no mutante) | `A_suspended_actor_is_denied...` (`054422`, 1) |
| M12 | idem | AC-05: linha Deny escrita na trilha ANTES da resposta | `A_suspended_actor_is_denied...` (`054452`, 1) |
| M13 | idem | Regressão de parser: sufixo `/read` antes do marcador `/participants/` | **`QaTask0036RouteCollisionTests`** (`054520`, 1) — só o teste da QA pega |
| M14 | `Squid.Service/SqliteThreadStore.cs` | FR-032: o registro do aceite guarda o **texto do aviso** | **`QaTask0036RouteCollisionTests`** (`054549`, 1) — só o teste da QA pega |
| M15 | `Squid.Api/Threads/ThreadEndpoints.cs` | AC-03: `seq` negativo → `400` (ramo de validação da rota `read`) | `The_read_marker_only_advances...` (`054621`, 1) |

15× `PRE` registrado, 15× `RESTORE-OK hash=PRE residue=0`, 0× `RESTORE-FAIL`, 0× `CLASSIFICACAO-ERRADA`, 1× GREEN `25/25 exit 0` (`054652`), resíduo final `QA-MUTATION` = 0, backups TEMP `qa0036-*` = 0.

## Findings

Nenhum achado de **produto** (0 BLOCKER, 0 MAJOR, 0 MINOR). Notas de QA/fluxo:

| # | Severidade | Onde | O que | Evidência |
| --- | --- | --- | --- | --- |
| 1 | NOTA (BOM) | driver (esta QA) | o arquivo escrito saiu **sem UTF-8 BOM** (mesma perda das QA-0034/0035) — BOM adicionado ANTES de qualquer edição/execução; o `-VerifyOnly` e o checker agora também acusam BOM | comando 3 (`BOM OK`) + check `DRIVER-BOM` |
| 2 | NOTA (pré-existente) | `docs/sdd/_tools/sdd-test.sh` / WSL | gêmeo Linux não roda a suite: `wsl -e bash -c "command -v dotnet"` → `dotnet-missing` (sondado ao vivo NESTA sessão; instalação exige gate humano) — mesmo achado das QA-0033/0034/0035; gate segue no Windows; o gêmeo `.sh` do checker (parte estática) foi executado | comando 8 + linha do Log |
| 3 | NOTA (cobertura herdada) | `ThreadEndpoints.cs:490` (ThreadRouteOf) | a ordem `/participants/` antes dos sufixos da §D não tinha NENHUM teste do DEV (handle gerado nunca termina em `read`) — a forma só é afirmada pelo teste temporário da QA, **executado e removido** após os ciclos (fonte preservada em `QaTask0036RouteCollisionTests.cs.txt`); se a casa quiser cobertura permanente, o teste vira task de DEV | ciclo M13 (`054520`) |

## Não coberto / unverified

- **Suite de testes em Linux (WSL)**: `dotnet` ausente no WSL (sondado ao vivo: `command -v dotnet` → missing; instalação exige gate humano) e o gêmeo `sdd-test.sh` quebra sem ele → **unverified**, nunca assumido igual ao Windows. O gêmeo `.sh` do **checker** (parte estática: logs, resumos, hashes, git) foi executado — comando 8.
- **Ordem `400 conteúdo` × `403 suspenso`**: nenhum teste envia conteúdo vazio a um ator suspenso (a AC-05 não pede) — o M11 mostra que mover a checagem muda a escada, mas esse degrau em específico não tem assert; risco baixo (400 é do contrato §8 e o mutante M11 é o único que o toca).
- **Corrida 428 × append concorrente (BEGIN IMMEDIATE)**: os testes são de cliente único; a não-queima de seq foi provada sequencialmente (M2) e por revisão da transação — mesma limitação das QA-0033/0034/0035.
- **Texto do aviso na UI**: exibição do aviso é P-08 (GAP-37, out of scope por design); aqui só o registro.
- **Cobertura permanente da colisão de parser**: teste temporário removido após os ciclos (fonte em `docs/sdd/qa/`) — ver finding 3.

## Reproduzir

```powershell
# pré-requisito: .NET 10 SDK + PowerShell no Windows, repo C:\dev\squid-squad-net\squid-execution
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test       # esperado: exit 0, Aprovado 456, Com falha 0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0036.ps1 -VerifyOnly   # esperado: exit 0, VERIFY total=15 bad=0 + BOM OK
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0036.ps1     # run fresco: exit 0, SUMMARY cycles=15 failed=0 (15 vermelhos de propósito)
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0036.ps1      # esperado: exit 0, SUMMARY fails=0
wsl -e bash docs/sdd/qa/qa-check-task-0036.sh                                               # esperado: exit 0, SUMMARY fails=0
```

## Artefatos desta QA

| Arquivo | Papel |
| --- | --- |
| `docs/sdd/qa/qa-mutate-task-0036.ps1` | driver de mutação can-fail (15 ciclos, `-VerifyOnly` com checagem de BOM, S-16/S-22/S-23 embutidos) |
| `docs/sdd/qa/QA-0036-mutations.log` | ledger: PRE, vermelhos no log, restore, GREEN, SUMMARY |
| `docs/sdd/qa/qa-check-task-0036.ps1` / `.sh` | checkers de evidência (estáticos; gêmeo Linux sem o sdd-state, que é ferramenta Windows) |
| `docs/sdd/qa/QA-0036-check.log` / `QA-0036-check-sh.log` | execuções dos checkers |
| `docs/sdd/qa/QA-0036-git-baseline.txt` | git + HEAD capturados antes da QA escrever (05:32:52) |
| `docs/sdd/qa/QA-0036-scope.log` | varredura final de escopo (git, blob-identity, mtime, resíduo) |
| `docs/sdd/qa/QaTask0036RouteCollisionTests.cs.txt` | fonte do teste temporário da QA (colisão parser `/read` + texto do aviso) — **executado e REMOVIDO** da árvore após os ciclos; prova viva nos logs `053508` (verde), `054520`/`054549` (vermelho) |

memoryBankFlags: NENHUM (novo) — rota `qa-integracao` lida (`docs/memory-bank/index.md` + `build-run-test.md#Local tests without gate`: S-15/S-22/S-23 confirmados **em execução real** nesta sessão; `contracts.md` não é normativo para os ACs — o contrato `http-api.md` §6/§8/§9, o data-model §3.1 e SPEC-0010 §D foram lidos); as flags `[MUDANCA_RELEVANTE CONTRATO - lastReadSeq no envelope]` e `[PADRAO_NOVO - precondicao de envio dentro da transacao do append]` já vêm carregadas pelo ODEV no Log da task e foram **CONFIRMADAS em execução** desta QA (M8 pega o envelope, M2 pega a recusa que não queima seq).
