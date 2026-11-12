<!--
templateVersion: 1.0.0
 -->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: RASCUNHO
- owner: qa
- lastVerified: (último artefato: docs/sdd/runs/test-runs/20261112-065700-test-gate.log)

# QA-0037 — Resposta à mensagem `replyToId` (TASK-0037)

- runId: R-20261111-01 · attempt: **1/2** (bloco de controle `qaAttempts: 0/2` — contador é do ODEV, QA não escreve nele)
- QA: subagente qa-task + política ponytail-qa (full) — testes EXECUTADOS nesta sessão, não escritos e deixados
- leitura normativa: SPEC-0010 §E + AC-01..AC-04 da task + FR-057 (`specs/001-squid-mvp/spec.md:231`) + contrato http-api §6 (linhas 132/139/153) e §9 item 9 (linha 199) + data-model §2.5.1 (`ReplyToId` = outra mensagem **da mesma thread**, `data-model.md:164`) + memória banc rota `qa-integracao`
- commit sob verificação: `6dfac85` em `task/TASK-0037-resposta-a-mensagem` (8 arquivos = 5 produto + 3 teste, todos nas `touches`); baseline DEV `456/456 → 462/462`
- verdict: **PASSED**

## AC → cenário (4 ACs; todos executados nesta sessão)

| AC | Cenário | Execução (destaque) | Resultado |
| --- | --- | --- | --- |
| AC-01 | `POST .../messages` com `replyToId` da **mesma thread** cria a resposta e o envelope devolve a referência (null quando não é resposta) | `ThreadReplyRoutesTests.A_reply_to_a_message_of_the_same_thread_lands_and_the_envelope_carries_the_reference` (200 seq 2 com `replyToId` no corpo, delta `[1,2]` com null na original e a referência na resposta, a par lê a MESMA referência) vermelho no baseline `20261112-064406-test-filter.log` (**exit 0, 30/30**) e no GREEN `065047`. Can-fail: **M5** (coluna sempre NULL no INSERT → `064753`, 3 vermelhas: store + envelope + parede), **M6** (`MessageView.replyToId = null` → `064824`, 3 vermelhas na rota) | PASS |
| AC-02 | `replyToId` de **outra thread** ou **inexistente** → `422 invalid_reply_target`, nada gravado, seq nenhum queimado; e a escada responde **428 antes de 422** | `ThreadReplyRoutesTests.A_target_from_another_thread_or_a_missing_message_is_422_and_writes_nothing` (422 + palavra do contrato nos DOIS casos, histórico de A = `[1]`, B vazio, e em seguida um alvo desta thread nasce em seq 2) + `A_send_missing_both_..._428_then_422` (sem aceite = 428; aceito = 422) + `ThreadStoreTests.The_append_gates_the_ack_before_the_reply_target_and_stores_a_same_thread_reference` (`AckRequired` sem alvo impossível, `InvalidReplyTarget` ×2, `message_seq==0`, histórico vazio, referência gravada). Can-fail: **M1** (alvo de outra thread aceito → `064558`, 3), **M2** (inexistente aceito → `064624`, 3), **M3** (gate pulado quando a mensagem carrega alvo → `064654`, 2 — inverte a ordem), **M4** (recusa que QUEIMA seq via commit → `064726`, 2: `message_seq != 0` e a resposta seguinte nasce em 3), **M7** (422 → 409 `version_conflict` → `064854`, 2) | PASS |
| AC-03 | Resposta válida **não muda a visibilidade**: não-participante segue em 404 **byte-idêntico** em GET/POST — inclusive no POST que CARREGA um `replyToId` válido | `ThreadReplyRoutesTests.A_valid_reply_never_widens_the_wall_and_the_stranger_keeps_the_byte_identical_404` (4 respostas 404, 3 pares byte-a-byte iguais, sem `traceId`/id/handle/`replyToId` no corpo, e dentro da parede nada mudou). Can-fail: **M8** (sonda de participante desligada na rota de envio: o POST do estrangeiro vira 428 → `064920`, 3 vermelhas em 3 suites), **M9** (404 com `traceId` → `064951`, 6 vermelhas) | PASS |
| AC-04 | `sdd-test.ps1 test` exit 0; nenhum arquivo alterado fora das `touches` | Gate `20261112-065700-test-gate.log` → **`exit: 0` DENTRO do cabeçalho e `Com falha: 0, Aprovado: 462, Total: 462`** (S-22; 4m44s). Escopo (`QA-0037-scope.log`): `git status -- backend` **0 entradas**; blob-identity `git hash-object` = `git rev-parse HEAD:` em **4/4** arquivos mutados; mtime desde a baseline `06:43:05` = **exatamente esses 4**; resíduo `QA-MUTATION` = 0; backups TEMP `qa0037-*` = 0; `git status` sobre `backend + specs + memory-bank` = **0 entradas** (fora dos touches) | PASS |

## Comandos executados (todos nesta sessão, com exit real; datas vêm dos próprios logs)

| # | comando | exit | artefato / saída |
| --- | --- | --- | --- |
| 1 | `git rev-parse HEAD` + `git rev-parse --abbrev-ref HEAD` + `git status --porcelain -uall` + `wsl -e bash -c "command -v dotnet"` (pwsh) | 0 | `docs/sdd/qa/QA-0037-git-baseline.txt` — branch `task/TASK-0037-resposta-a-mensagem`, HEAD `6dfac85`, só os 2 task files do ODEV, **dotnet ausente no WSL** |
| 2 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Threads` (baseline) | 0 | `20261112-064406-test-filter.log` — `exit: 0`, Com falha 0 / Aprovado 30 / Total 30 |
| 3 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0037.ps1 -VerifyOnly` | 0 | stdout `VERIFY total=10 bad=0` + `BOM OK (UTF-8 BOM presente)` — os 10 padrões batem `hits=1` nas fontes pristinas |
| 4 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0037.ps1` (execução cheia: 10 ciclos + GREEN) | 0 | `QA-0037-mutations.log` — 10 vermelhos classificados NO LOG (S-22), 10× `RESTORE-OK hash=PRE residue=0`, `GREEN ... aprovado=30 falha=0`, `SUMMARY cycles=10 failed=0 residue=0 temp-backups=0` |
| 5 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` (gate AC-04, árvore restaurada) | 0 | `20261112-065700-test-gate.log` — `exit: 0` lido **dentro do log** (S-22) + `Com falha: 0, Aprovado: 462, Total: 462`, 4m44s |
| 6 | varredura de escopo (pwsh): `git status`, `git hash-object` vs `git rev-parse HEAD:`, mtime desde a baseline, resíduo, backups | 0 | `docs/sdd/qa/QA-0037-scope.log` — git-backend 0, blob 4/4 `match=True`, `mtime-count=4`, `residue=0`, `fora-dos-touches=0` |
| 7 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0037.ps1` | 0 | `docs/sdd/qa/QA-0037-check.log` — SUMMARY total=35 fails=0 (SDD-STATE `check: OK - log entries 16, evidence rows 15`) |
| 8 | `wsl -e bash docs/sdd/qa/qa-check-task-0037.sh` (gêmeo Linux, parte estática) | 0 | `docs/sdd/qa/QA-0037-check-sh.log` — SUMMARY total=30 fails=0 |

## Ciclos de mutação (can-fail; S-16 SHA256 PRE, S-22 classificação no log, S-23 restore byte-exact + mtime)

Todos nos `touches` (`Squid.Api/Threads/**`, `Squid.Service/**`, `Squid.Persistence/schema/**`) — nenhum edit fora do escopo, e o arquivo final é byte-igual ao `HEAD` (blob-identity 4/4). Filtro de todos os ciclos: `FullyQualifiedName~Squid.Tests.Threads` (30 testes).

| Ciclo | Arquivo | O que prova | Vermelho classificado NO LOG |
| --- | --- | --- | --- |
| M1 | `Squid.Service/SqliteThreadStore.cs` | AC-02: alvo de OUTRA thread é recusado dentro da transação (`if` da §9.9 ligado) | store `The_append_gates...` + rota `A_target_from_another_thread...` + `A_send_missing...` (`064558`, 3) |
| M2 | idem | AC-02: mensagem INEXISTENTE (`owner` NULL) também cai — os DOIS casos andam juntos no MESMO lookup | store + `A_target_from_another_thread...` + `A_send_missing...` (`064624`, 3) |
| M3 | idem | **ordem 428-ANTES-de-422**: o gate §9.6 deixa de valer quando a mensagem carrega `replyToId` → o pedido duplamente inválido responderia 422 | store + `A_send_missing_both_the_acceptance_and_a_valid_target_answers_428_then_422` (`064654`, 2) |
| M4 | idem | AC-02: a recusa **não queima seq nem grava linha** (mutante dá commit num bump antes do retorno) | store (`message_seq != 0`) + `A_target_from_another_thread...` (a resposta seguinte nasce em seq 3) (`064726`, 2) |
| M5 | idem | AC-01: a referência é **persistida** (`reply_to_id` no INSERT, não só em memória) | store + `A_reply_to_a_message_of_the_same_thread...` + `A_valid_reply_never_widens...` (`064753`, 3) |
| M6 | `Squid.Api/Threads/MessageView.cs` | AC-01: o **envelope/200** carrega `replyToId` (null só quando não há resposta) | `A_reply_to_a_message...`, `A_target_from_another_thread...`, `A_valid_reply_never_widens...` (`064824`, 3) |
| M7 | `Squid.Api/Threads/ThreadEndpoints.cs` | AC-02: a palavra do contrato é `422 invalid_reply_target` (mutante troca por 409 `version_conflict`) | `A_target_from_another_thread...` + `A_send_missing...` (`064854`, 2) |
| M8 | idem | AC-03/FR-057: a **sonda de participante (404) vem antes** de qualquer validação/escada — com ela muda, o POST do estrangeiro com `replyToId` responde 428 e a parede cai | `A_valid_reply_never_widens...` + `ThreadParticipantRoutesTests.Removal_ends_access...` + `ThreadMessageRoutesTests.A_non_participant...` (`064920`, 3 em 3 suites) |
| M9 | idem | AC-03/SC-005: o 404 compartilhado **sem traceId** — as recusas continuam byte-idênticas | 6 falhas: `A_valid_reply_never_widens...` + `ThreadParticipantRoutesTests` ×3 (+ demais suites que usam o 404) (`064951`) |
| M10 | `Squid.Persistence/schema/SchemaMigrator.cs` | **idempotência da 0005**: SQLite não tem `IF NOT EXISTS` em `ALTER TABLE`, então o ÚNICO guarda é o ledger do `SchemaMigrator` — sem o `continue`, a re-aplicação re-executa o ALTER e estoura `duplicate column name` (`constraint=yes`) | `ThreadMigrationTests.The_0005_version_adds_the_reply_column_and_reapplying_changes_nothing` + `The_0004...` + `Ac01_the_second_migration...` (`065018`, 3) |

10× `PRE` registrado, 10× `RESTORE-OK hash=PRE residue=0`, 0× `RESTORE-FAIL`, 0× `CLASSIFICACAO-ERRADA`, 1× GREEN `30/30 exit 0` (`065047`), resíduo final `QA-MUTATION` = 0, backups TEMP `qa0037-*` = 0.

## Findings

Nenhum achado de **produto** (0 BLOCKER, 0 MAJOR, 0 MINOR). Notas de QA/fluxo:

| # | Severidade | Onde | O que | Evidência |
| --- | --- | --- | --- | --- |
| 1 | NOTA (BOM) | driver (esta QA) | `qa-mutate-task-0037.ps1` foi escrito **sem UTF-8 BOM** (mesma perda das QA-0034/0035/0036) — BOM adicionado ANTES de qualquer edição/execução e conferido em dois lugares: `-VerifyOnly` (`BOM OK`) e os checkers (`DRIVER-BOM`) | comando 3 |
| 2 | NOTA (pré-existente) | `docs/sdd/_tools/sdd-test.sh` / WSL | gêmeo Linux não roda a suite: `wsl -e bash -c "command -v dotnet"` → `dotnet-missing` (**sondado ao vivo NESTA sessão**); instalação exige gate humano — mesmo achado das QA-0033..0036 | comando 1 + linha do Log |
| 3 | NOTA (por design, documentado) | `backend/src/Squid.Persistence/schema/0005_message_reply_to_id.sql:7-10` | SQLite não oferece `ALTER TABLE ... ADD COLUMN IF NOT EXISTS`; a idempotência da 0005 descansa INTEIRAMENTE no ledger do `SchemaMigrator` (versão registrada é pulada; texto alterado sob versão registrada é recusado). É o que a migration declara no próprio cabeçalho, e o **M10** mostra que sem esse guarda a re-aplicação quebra — nenhum outro teste cobre o contrafactual | ciclo M10 (`065018`, `constraint=yes`) |
| 4 | NOTA (alcance do mutante) | `ThreadEndpoints.cs:298` (MessageSendAsync) | desligar a sonda de participante só na rota de **envio** derrubou 3 suites diferentes (`ThreadReplyRoutesTests`, `ThreadParticipantRoutesTests`, `ThreadMessageRoutesTests`) — a prova de que a recusa única do FR-031/SC-005 é estrutural, não acidental deste teste | ciclo M8 (`064920`) |

## Não coberto / unverified

- **Suite de testes em Linux (WSL)**: `dotnet` ausente no WSL (sondado ao vivo nesta sessão: `command -v dotnet` → `dotnet-missing`; instalação exige gate humano) e o gêmeo `sdd-test.sh` quebra sem ele → **unverified**, nunca assumido igual ao Windows. O gêmeo `.sh` do **checker** (parte estática: logs, resumos, hashes, git) foi executado — comando 8.
- **`replyToId` malformado** (string que não é URI válida): nenhuma AC exige tratamento distinto; por leitura do código cai no MESMO lookup sem correspondência → mesmo `422`. Risco baixo, mas **não executado** — se um dia o contrato exigir `400` para payload malformado, isso vira nova AC, não silêncio desta QA.
- **Alvo de mensagem apagada (tombstone)**: o data-model §2.5.1 tem `DeletedAt` em `Message`, mas **não existe rota de apagar mensagem de thread nesta fase** (só posts têm tombstone, SPEC-0009 §H) — cenário inatingível hoje. Risco: quando a remoção de mensagem chegar, decidir se alvo apagado é `422` ou `200` é decisão de spec nova, não desta task.
- **Corrida 428 × append concorrente (`BEGIN IMMEDIATE`)**: os testes são de cliente único; a não-queima de seq do 422 foi provada sequencialmente (M4) e por leitura da transação — mesma limitação das QA-0033/0034/0035/0036.
- **Reação/anexo sobre resposta** (TASK-0038/0039): fora do escopo desta task (`blocks` da task).

## Reproduzir

```powershell
# pré-requisito: .NET 10 SDK + PowerShell no Windows, repo C:\dev\squid-squad-net\squid-execution
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test       # esperado: exit 0, Aprovado 462, Com falha 0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0037.ps1 -VerifyOnly   # esperado: exit 0, VERIFY total=10 bad=0 + BOM OK
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0037.ps1     # run fresco: exit 0, SUMMARY cycles=10 failed=0 (10 vermelhos de propósito)
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0037.ps1      # esperado: exit 0, SUMMARY fails=0
wsl -e bash docs/sdd/qa/qa-check-task-0037.sh                                               # esperado: exit 0, SUMMARY fails=0
```

## Artefatos desta QA

| Arquivo | Papel |
| --- | --- |
| `docs/sdd/qa/qa-mutate-task-0037.ps1` | driver de mutação can-fail (10 ciclos, `-VerifyOnly` com checagem de BOM, S-16/S-22/S-23 embutidos) |
| `docs/sdd/qa/QA-0037-mutations.log` | ledger: PRE, vermelhos no log, restore, GREEN, SUMMARY |
| `docs/sdd/qa/qa-check-task-0037.ps1` / `.sh` | checkers de evidência (estáticos; gêmeo Linux sem o sdd-state, que é ferramenta Windows) |
| `docs/sdd/qa/QA-0037-check.log` / `QA-0037-check-sh.log` | execuções dos checkers |
| `docs/sdd/qa/QA-0037-git-baseline.txt` | git + HEAD + sonda WSL capturados antes da QA escrever |
| `docs/sdd/qa/QA-0037-scope.log` | varredura final de escopo (git, blob-identity, mtime, resíduo, fora-dos-touches) |

memoryBankFlags: `[GAP: suite Linux/WSL unverified - dotnet ausente na maquina, instalacao exige gate humano (pre-existente, sondado ao vivo nesta sessao; mesmo achado das QA-0033..0036)]` · restante NENHUM (novo) — rota `qa-integracao` lida (`docs/memory-bank/index.md` + `build-run-test.md#Local tests without gate`: S-15/S-22/S-23 confirmados **em execução real** nesta sessão; o contrato `http-api.md` §6/§9.9, o data-model §2.5.1 e SPEC-0010 §E foram lidos diretamente); a flag `[PADRAO_NOVO: validacao de replyToId DENTRO da transacao do append, depois do gate 428]` já vem carregada pelo ODEV no Log da task e foi **CONFIRMADA em execução** desta QA (M3 pega a ordem, M4 pega a recusa que não queima seq, M1/M2 pegam os dois casos no mesmo lookup).
