<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0033 — Núcleo da thread: migration, store ETag, criar e listar

```yaml
runId: R-20261111-01
taskId: TASK-0033
status: DONE
agent: odev
phase: P-05
sequence: 2
createdAt: 2026-11-11 23:45:07
updatedAt: 2026-11-11 23:45:07
parent: SPEC-0010
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Persistence/schema/**", "backend/src/Squid.Api/Threads/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/Threads/**", "backend/tests/Squid.Tests/Projections/SchemaMigrationTests.cs", "backend/tests/Squid.Tests/Projections/ProjectionPumpTests.cs"]
blocks: ["TASK-0034"]
blockedBy: []
inputArtifacts: ["docs/sdd/specs/SPEC-0010-mensageria-p05.md", "docs/sdd/plan/plan.md", "specs/001-squid-mvp/data-model.md", "specs/001-squid-mvp/contracts/http-api.md"]
outputArtifacts: ["backend/src/Squid.Persistence/schema/**", "backend/src/Squid.Api/Threads/**", "backend/src/Squid.Service/**"]
interviewRounds: 0/6
feasibilityRounds: 0/2
correctionAttempts: 0/2
qaAttempts: 0/2
contextHandoffs: 0/4
progressDelta: 0
humanGate:
  approvedBy: NENHUM
  approvedDate: NENHUM
  scope: NENHUM
  expiresAt: NENHUM
blocker: NENHUM
nextAction: DONE - QA-0033 PASSED (5/5 ACs, 13 mutacoes, gate 438/438); merge em develop + docs pelo ODEV; dispatch DEV da TASK-0034
evidence: []
memoryBankFlags: []
```

## Intent

Base da P-05: migration versionada cria `thread`/`thread_participant`, a store serializa
`MessageSeq` por ETag (ordem total, GAP-29) e `POST/GET /api/threads` criam e listam conversas
(direct=2, group=2..50, criador=admin no group). Nada de mensagens ainda — a base precisa estar
verde e funcional por si.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: migration SQL versionada e idempotente no padrão `SchemaMigrator` cria `thread`
  (com `message_seq` e `version`) e `thread_participant` — rodar duas vezes não falha nem duplica — teste.
- AC-02: `POST /api/threads` cria `Direct` com exatamente 2 participantes (criador incluso) e
  `Group` com 2..50; o criador do group entra como admin; fora da faixa → `400` — teste.
- AC-03: `GET /api/threads` devolve somente as threads onde o ator é participante, com
  `messageSeq` inicial 0 e o `lastReadSeq` do próprio ator — teste.
- AC-04: toda rota exige sessão + CSRF (`401` sem sessão) e `Id` da thread segue origem
  scheme+Host (emenda P-03) — teste.
- AC-05: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`
  exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Persistence/schema/**` (migration `0002_*.sql` + `SchemaMigrator` se preciso)
- `backend/src/Squid.Api/Threads/**` (novo: rotas, view, store interface)
- `backend/src/Squid.Service/**` (store SQLite + wiring)
- `backend/tests/Squid.Tests/Threads/**` (novo)
- `backend/tests/Squid.Tests/Projections/SchemaMigrationTests.cs` e
  `backend/tests/Squid.Tests/Projections/ProjectionPumpTests.cs` — **emenda do ODEV
  2026-11-12 00:44:03**: a migration `0002` derruba por causalidade os asserts `Assert.Single`
  que pinçavam a família de schema a um único script; correção mínima exigida pela AC-01/AC-05,
  nenhuma AC muda; reportada pelo DEV na feasibility (touchedOutsidePlan)

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-29 | no | estado da thread em SQLite + ETag (decisão humana 2026-11-11 23:08:37), não Orleans |
| GAP-30 | no | emenda (a): data-model §2.7 com `CreatedBy` + papel admin por participante — original intacto |
| GAP-38 | no | `lastReadSeq` nasce 0 por participante; marcador monotônico (implementado na TASK-0036, o campo existe desde aqui) |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-11 23:45:07 | odev | task created from SPEC-0010 (entrega 1 de 8 da P-05; base da fase; entrevista 5 rodadas fechada, decisões 1/9/10 do corte)
- 2026-11-11 23:51:19 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; baseline do gate a cargo do DEV na feasibility; autorizacao de commit gate PHASE 2026-11-11 20:42:28 (modelo C) incluida no dispatch
- 2026-11-12 00:24:28 | dev | feasibility 1/2: baseline do gate capturado (432 testes, exit 0); sem duvida bloqueante — D-01 lastReadSeq sem coluna (SPEC-0010 + GAP-38: campo nasce 0, marcador e da TASK-0036), D-02 faixa conta o criador (AC-02 criador incluso), D-03 participants = lista de URI (data-model 2.7), D-04 tail /t/ (familia /c/ /o/ /m/, nenhum AC depende); 0002 quebra SchemaMigrationTests (familia de um unico script) — ajuste minimo do teste reportado em touchedOutsidePlan
- 2026-11-12 00:36:59 | dev | implementacao: migration 0002_threads.sql (thread + thread_participant + indice em actor_id), Squid.Api/Threads (IThreadStore, ThreadView, POST/GET /api/threads), SqliteThreadStore com CAS de message_seq sob a coluna version (GAP-29), wiring em SquidServiceHost (DI + MapSquidThreads); SchemaMigrationTests ajustado para a familia de dois scripts
- 2026-11-12 00:43:25 | dev | gate final (1a): exit 1 — ProjectionPumpTests.Ac05 tambem fixava schema_version com uma unica linha (familia de um script, como o SchemaMigrationTests); ajustado para as duas versoes do carregador — segundo arquivo fora dos touches, reportado em touchedOutsidePlan; re-execucao do gate em seguida
- 2026-11-12 00:45:40 | odev | touches emendado pelo ODEV: SchemaMigrationTests.cs e ProjectionPumpTests.cs entram no escopo (causalidade da migration 0002 sobre asserts Assert.Single da familia de schema, reportado pelo DEV na feasibility 1/2) - nenhuma AC muda; os dois arquivos agora DEVEM entrar no commit da entrega
- 2026-11-12 00:49:25 | dev | gate final (2a): exit 0 — Aprovado 438 de 438, Com falha 0 (docs/sdd/runs/test-runs/20261112-004831-test-gate.log); touches emendado pelo ODEV incorpora SchemaMigrationTests e ProjectionPumpTests no escopo, touchedOutsidePlan NENHUM
- 2026-11-12 00:50:33 | dev | commit unico da entrega: 599d4ac em task/TASK-0033-nucleo-da-thread (11 arquivos, todos em touches emendado; 810 insercoes, 10 remocoes; sem push, sem rebase, main intocada); plan.md e os documentos do fluxo seguem fora do commit por nao serem meus
- 2026-11-12 00:54:39 | odev | ODEV verificou antes de transicionar: gate final 20261112-004831-test-gate.log lido no arquivo (exit: 0, Aprovado 438, Com falha 0, 4m32s); commit 599d4ac conferido via git show (11 files, todos em touches emendado, branch task/TASK-0033-nucleo-da-thread); touchedOutsidePlan NENHUM; step result DONE aceito - 6 testes novos (migracao idempotente, CAS do MessageSeq, rotas), 2 vermelhos intermediarios honestos (CS0104 + family pin do ProjectionPump), 4 duvidas resolvidas na feasibility sem rodada extra
- 2026-11-12 00:54:41 | odev | step result DONE aceito: D-01..D-04 resolvidas pela escada (SPEC-0010/GAP-38/data-model 2.7/familia /t/); parked idea do participante inexistente na criacao registrada pelo DEV (NENHUM no backlog por ora - ODEV avalia na consolidacao); memoryBankFlags [PADRAO_NOVO: agregado em tabela migrada com CAS na coluna version em vez de documento IGrainStorage] carregado para a consolidacao
- 2026-11-12 00:54:43 | odev | transicao IN_DEVELOPMENT -> AWAITING_QA; dispatch QA attempt 1/2 em seguida
- 2026-11-12 01:38:32 | qa | QA attempt 1/2: entrada pela task (status AWAITING_QA, qaAttempts 0/2) + SPEC-0010 secao A + data-model 2.7 + http-api 1/8/9 + GAP-29/30/38; baseline do filtro Threads+SchemaMigration 9/9 exit 0 (20261112-010646-test-filter.log); 13 ciclos can-fail executados por docs/sdd/qa/qa-mutate-task-0033.ps1 - CAS do version (M1), skip de idempotencia do SchemaMigrator (M2), conteudo da migration 0002 (M3), faixas direct/group contando o criador M4A..M4D + M5, papel admin do criador (M6), isolamento do GET (M7), origem scheme+Host (M8), 401 de sessao (M9) e portao CSRF (M10 - fora dos touches, declarado, restaurado byte-exact F2F22F2269D0)
- 2026-11-12 01:38:32 | qa | todos os vermelhos classificados dentro dos logs gravados (S-22, 0 divergencia console/log no ledger), restore SHA256 PRE=POST com bump de mtime apos cada ciclo (S-16/S-23) e GREEN 9/9 pos-restauracao; gate pos-mutacoes 438/438 e gate final 438/438 com exit: 0 lido dentro do proprio log (20261112-012642 e 20261112-013346); escopo final: git igual ao baseline do lado de backend (0 entradas), mtime = exatamente os 5 arquivos mutados restaurados, residuo QA-MUTATION 0; veredito PASSED em docs/sdd/qa/QA-0033-nucleo-da-thread.md; unverified aceito: suite Linux (WSL sem dotnet sondado ao vivo e sdd-test.sh sem achar a solution .slnx)
- 2026-11-12 01:49:48 | odev | ODEV verificou antes de transicionar: QA-0033-nucleo-da-thread.md com verdicto PASSED lido no arquivo; gate final 20261112-013346-test-gate.log exit: 0 / Aprovado 438 / Com falha 0 / 4m35s; residuo QA-MUTATION = 0 (git grep backend); commit QA 63cd221 conferido (9 arquivos, todos docs/sdd/qa/, sem push, main intocada); backend git status = 0 entradas; check OK - veredito aceito
- 2026-11-12 01:49:49 | odev | QA-0033 PASSED attempt 1/2: 5/5 ACs, 13 mutacoes can-fail vermelhas (M1 CAS, M2 idempotencia, M3 migration, M4A-D faixas, M5 criador, M6 admin, M7 isolamento, M8 origem, M9 401, M10 CSRF-fora-dos-touches declarado e restaurado byte-exact F2F22F2269D0) todas RESTORE-OK hash=PRE residue=0; 2 gates full executados (438/438 cada); unverified registrados (suite Linux - WSL sem dotnet sondado ao vivo; NextMessageSeq multi-processo); nota INFO do mutante M4D que comeu o ; (CS1002) corrigido e re-executado
- 2026-11-12 01:49:51 | odev | transicao AWAITING_QA -> DONE; TASK-0033 = entrega 1 de 8 da P-05 encerrada; ODEV faz o merge em develop e commita docs de fluxo (gate modelo C); TASK-0034 desbloqueada

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-12 00:37:03 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (baseline feasibility) | 0 | docs/sdd/runs/test-runs/20261112-001958-test-gate.log - Aprovado 432 de 432, Com falha 0 |
| 2026-11-12 00:37:23 | dev | sdd-test.ps1 test -TestFilter Threads+SchemaMigration (1a) | 1 | docs/sdd/runs/test-runs/20261112-003357-test-filter.log - CS0104 ThreadState ambiguo em SqliteThreadStore - corrigido com alias de tipo |
| 2026-11-12 00:37:24 | dev | sdd-test.ps1 test -TestFilter Threads+SchemaMigration (2a) | 0 | docs/sdd/runs/test-runs/20261112-003527-test-filter.log - Aprovado 9 de 9 (AC-01 a AC-04) |
| 2026-11-12 00:43:44 | dev | sdd-test.ps1 test (gate final 1a) | 1 | docs/sdd/runs/test-runs/20261112-004206-test-gate.log - Com falha 1 de 438: ProjectionPumpTests.Ac05 fixava a familia com uma linha unica - corrigido |
| 2026-11-12 00:49:28 | dev | sdd-test.ps1 test (gate final 2a) | 0 | docs/sdd/runs/test-runs/20261112-004831-test-gate.log - Aprovado 438 de 438, Com falha 0 (gate AC-05) |
| 2026-11-12 00:50:36 | dev | git show --stat --format= HEAD (pos-commit) | 0 | 599d4ac - 11 files changed 810 insertions 10 deletions - somente arquivos de touches, uma unica commit |
| 2026-11-12 00:51:43 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-state.ps1 check docs/sdd/tasks/TASK-0033-nucleo-da-thread.md | 0 | check OK - log 8 entries e evidence 6 rows antes desta linha |
| 2026-11-12 00:54:45 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate final 2a) | 0 | docs/sdd/runs/test-runs/20261112-004831-test-gate.log - exit: 0 / Aprovado 438 / Com falha 0 / 4m32s - VERIFICADO PELO ODEV lendo o arquivo |
| 2026-11-12 00:54:46 | dev | git show --stat --format= HEAD (pos-commit) | 0 | 599d4ac - 11 files changed 810 insertions 10 deletions - somente touches emendado - VERIFICADO PELO ODEV via git show |
| 2026-11-12 01:38:34 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter Threads+SchemaMigration (baseline da QA) | 0 | docs/sdd/runs/test-runs/20261112-010646-test-filter.log - exit: 0, Aprovado 9, Com falha 0 |
| 2026-11-12 01:38:36 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0033.ps1 (12 ciclos can-fail) | 1 | docs/sdd/qa/QA-0033-mutations.log - M1..M9 e M4A..M4D vermelhos classificados nos logs (S-22) + GREEN 9/9 + SUMMARY failed=1 = o mutante M4D quebrou o build (CS1002) e foi corrigido |
| 2026-11-12 01:38:37 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0033.ps1 -Only M4D (mutante corrigido) | 0 | docs/sdd/qa/QA-0033-mutations.log - M4D vermelho no log 20261112-011929-test-filter.log (2 falhas), RESTORE-OK hash=PRE, GREEN 20261112-011950 exit 0 |
| 2026-11-12 01:38:39 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate pos-mutacoes) | 0 | docs/sdd/runs/test-runs/20261112-012642-test-gate.log - exit: 0, Aprovado 438, Com falha 0 (lido no log, S-22) |
| 2026-11-12 01:38:40 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0033.ps1 -Only M10 (CSRF fora dos touches, declarado) | 0 | docs/sdd/qa/QA-0033-mutations.log - M10 vermelho no log 20261112-012720-test-filter.log, restore hash=PRE F2F22F2269D0 byte-exact, GREEN 20261112-012742 exit 0 |
| 2026-11-12 01:38:42 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate final AC-05) | 0 | docs/sdd/runs/test-runs/20261112-013346-test-gate.log - exit: 0 e Aprovado 438 / Com falha 0 lidos dentro do log (S-22), 4m35s |
| 2026-11-12 01:38:44 | qa | varredura de escopo QA-0033: git status -uall vs baseline, SHA256 dos 5 mutados, mtime desde 01:05:33, residuo QA-MUTATION, backups TEMP | 0 | docs/sdd/qa/QA-0033-scope.log - backend git 0, 5/5 = PRE, mtime = exatamente os 5 mutados restaurados, residuo 0, temp-backups 0 |
| 2026-11-12 01:45:21 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0033.ps1 | 0 | docs/sdd/qa/QA-0033-check.log - SUMMARY total=16 fails=0 (exit e resumo dos logs S-22, hashes S-16, mtime S-23, residuo, git, sdd-state) |
| 2026-11-12 01:45:22 | qa | wsl -e bash -lc bash docs/sdd/qa/qa-check-task-0033.sh (gêmeo Linux) | 0 | docs/sdd/qa/QA-0033-check-sh.log - SUMMARY total=13 fails=0 executado no WSL/bash |
