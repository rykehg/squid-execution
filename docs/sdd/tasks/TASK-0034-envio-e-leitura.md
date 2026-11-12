<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0034 — Envio e leitura com ordem total e 404 idêntico

```yaml
runId: R-20261111-01
taskId: TASK-0034
status: DONE
agent: odev
phase: P-05
sequence: 2
createdAt: 2026-11-11 23:45:07
updatedAt: 2026-11-11 23:45:07
parent: SPEC-0010
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Api/Threads/**", "backend/src/Squid.Service/**", "backend/src/Squid.Persistence/schema/**", "backend/tests/Squid.Tests/Threads/**"]
blocks: ["TASK-0035"]
blockedBy: ["TASK-0033"]
inputArtifacts: ["docs/sdd/specs/SPEC-0010-mensageria-p05.md", "docs/sdd/plan/plan.md", "specs/001-squid-mvp/data-model.md", "specs/001-squid-mvp/contracts/http-api.md"]
outputArtifacts: ["backend/src/Squid.Api/Threads/**", "backend/src/Squid.Service/**"]
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
nextAction: DONE - QA-0034 PASSED (5/5 ACs, 13 mutacoes + probe UNIQUE, gate 443/443); merge em develop + docs pelo ODEV; dispatch DEV da TASK-0035
evidence: []
memoryBankFlags: []
```

## Intent

Mensagem nasce com `seq` total alocada pelo estado da thread sob ETag (FR-030), conteúdo ≤4000
sanitizado, e leitura paginada com delta exato. Fecha FR-031: quem não é participante recebe
404 byte-idêntico em qualquer rota da thread, sem revelar existência nem participantes.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `POST /api/threads/{id}/messages` cria mensagem com `seq` crescente atribuída pelo
  estado da thread; **dois envios concorrentes nunca recebem a mesma `seq`** (ETag serializa) — teste.
- AC-02: `Content` ≤ 4000; sanitização escapa HTML e remove controles C0 no servidor (regra
  GAP-24 da P-04); vazio ou acima do limite → `400` — teste.
- AC-03: `GET .../messages?since=` devolve apenas `seq > since`, sem duplicata, na ordem de
  `seq`, com `nextSince` (§9.3) — teste.
- AC-04: não-participante (e thread inexistente) recebe **404 byte-idêntico** em GET e POST da
  thread, sem vazer existência nem a lista de participantes (FR-031/SC-005) — teste.
- AC-05: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`
  exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Api/Threads/**`
- `backend/src/Squid.Service/**`
- `backend/src/Squid.Persistence/schema/**` — **emenda do ODEV 2026-11-12 02:25:21**: a tabela
  `message` não existe e SPEC-0010 §B exige persistência com `seq`; meu dispatch declarava este
  path mas o bloco de controle não o listava (erro do ODEV, reportado como touchedOutsidePlan
  pelo DEV) — `0003_messages.sql` é a manifestação (regra da SchemaMigrator: script registrado é
  imutável, D-01 do DEV); nenhuma AC muda
- `backend/tests/Squid.Tests/Threads/**`

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-29 | no | seq alocada por ETag no estado da thread (mesma regra da TASK-0003/0023) |
| — | no | sanitização = escapar HTML + remover C0 (GAP-24, já adjudicado na P-04) |
| — | no | sem gate de E2EE nesta task (entra na TASK-0036); sem `replyToId` (TASK-0037); sem anexo (TASK-0039) |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-11 23:45:07 | odev | task created from SPEC-0010 (entrega 2 de 8 da P-05; blockedBy TASK-0033; FR-030/031 + §9.3)
- 2026-11-12 01:51:00 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; blockedBy TASK-0033 resolve (DONE com QA-0033 PASSED, gate 438/438, merge 5988e74 em develop); autorizacao de commit gate PHASE 2026-11-11 20:42:28 (modelo C) incluida no dispatch - nova branch task/TASK-0034-envio-e-leitura desde develop
- 2026-11-12 02:22:26 | dev | feasibility 1/2: baseline pre-edicao 438/438 exit 0 no gate com o mesmo comando do AC-05; unit e callers = ThreadEndpoints (MapSquidThreads ja ligado no SquidServiceHost), IThreadStore/SqliteThreadStore e o schema; touches confirmada e acrescida de backend/src/Squid.Persistence/schema/** pelo dispatch; nenhuma task aberta intersecta as touches (fase serial, TASK-0035 bloqueada por esta)
- 2026-11-12 02:22:28 | dev | D-01 fechado pela propria regra da SchemaMigrator: script registrado e imutavel, portanto a tabela message entra como nova versao 0003_messages.sql em vez de editar 0002; a familia de tres embute o .sql pelo wildcard que o Squid.Service.csproj ja declara
- 2026-11-12 02:22:41 | dev | implementacao: POST e GET /api/threads/{id}/messages pelo dispatcher catch-all na mesma forma de /api/posts/{*target}; seq alocada pelo CAS da thread com o INSERT na MESMA transacao (GAP-29: numero alocado nunca fica sem mensagem, entao o delta nao pode pular nenhuma); content de 4000 sanitizado no servidor com o PostSanitizer da P-04 (vazio ou acima do teto, cru ou pos-sanitizacao, le a 400); 404 byte-identico sem traceId para nao-participante e para thread inexistente (FR-031/SC-005, nao vaza existencia nem participantes); delta ?since= com nextSince (§9.3)
- 2026-11-12 02:22:42 | dev | 5 testes novos em backend/tests/Squid.Tests/Threads/**: seq em duas corridas de envio HTTP reais, cadeia de conteudo (4000 e sanitizacao), delta exato incluindo repeticao byte a byte, par de 404 identico em GET e POST, e rodada do append no store; fatia Threads verde 11/11 em duas execucoes e gate final 443/443 exit 0 (AC-05)
- 2026-11-12 02:24:08 | dev | commit unico autorizado f6ba1b2 no branch task/TASK-0034-envio-e-leitura (criado de develop conforme o gate de autorizacao): 7 arquivos, todos das touches; nenhuma interacao com main, nenhum push, nenhum rebase
- 2026-11-12 02:26:20 | odev | ODEV verificou antes de transicionar: gate final 20261112-022110-test-gate.log lido no arquivo (exit: 0, Aprovado 443, Com falha 0, 4m36s); commit f6ba1b2 conferido via git show (7 files: 5 produto + 2 teste, branch task/TASK-0034-envio-e-leitura); step result DONE aceito - 5 testes novos (2 corridas HTTP de seq, cadeia de conteudo, delta com re-poll byte-identico, par de 404 identico sem traceId, append no store), D-01 resolvida pela regra da propria SchemaMigrator
- 2026-11-12 02:26:22 | odev | correcao do ODEV: touchedOutsidePlan do DEV (0003_messages.sql) e erro do bloco de controle - meu dispatch declarava backend/src/Squid.Persistence/schema/** mas os touches literais nao o continham; touches emendado (arquivo + secao Touches), nenhuma AC muda; a escolha 0003 vs estender 0002 (D-01 do DEV) esta correta: script registrado e imutavel por checksum
- 2026-11-12 02:26:24 | odev | flags do step result: memoryBankFlag MUDANCA_RELEVANTE CONTRATO (contracts.md ## Endpoints ainda dice que mensageria nao tem handler - source ThreadEndpoints.cs:58-59) carregada para a consolidacao; notDone (ator Suspenso nao envia mensagem, SPEC-0010 Rules) vira AC nova da TASK-0036 pelo ODEV - regra de spec sem task dona; parkedIdea ?limit/Link de paginacao avaliada na consolidacao
- 2026-11-12 02:26:25 | odev | transicao IN_DEVELOPMENT -> AWAITING_QA
- 2026-11-12 03:02:27 | qa | attempt 1/2 executada: baseline Threads 11/11 exit 0 (20261112-023919-test-filter.log); driver com 13 ciclos can-fail (12 vermelhos na execucao cheia; M12 v1 sobreviveu - CLASSIFICACAO-ERRADA do proprio driver, mutante redesenhado e M12 v2 vermelho na re-execucao -Only); probe diferencial UNIQUE: pristine green, 0003 sem UNIQUE red, restore OK; gate final 443/443 exit 0 (20261112-025653-test-gate.log)
- 2026-11-12 03:02:28 | qa | verdict PASSED - AC-01..AC-05 cobertos por cenario executado, 0 BLOCKER (notas de QA: anomalia M12 v1, texto do constraint nao sobe ao output do host, BOM do driver); escopo backend byte-identico a f6ba1b2 (git status vazio + blob-identity 3/3), residuo QA-MUTATION 0; doc docs/sdd/qa/QA-0034-envio-e-leitura.md
- 2026-11-12 03:07:29 | odev | ODEV verificou antes de transicionar: QA-0034-envio-e-leitura.md com verdicto PASSED lido no arquivo; gate final 20261112-025653-test-gate.log exit: 0 / Aprovado 443 / Com falha 0 / 4m36s; residuo QA-MUTATION = 0 (git grep backend); commit QA dcda2db conferido (11 arquivos so docs/sdd/qa/, pai f6ba1b2, sem push, main intocada); backend git status = 0 entradas; check OK - veredito aceito
- 2026-11-12 03:07:31 | odev | QA-0034 PASSED attempt 1/2: 5/5 ACs, 13 ciclos can-fail (M1 CAS+INSERT, M2 version, M3 incremento, M4/M5/M6 sanitizacao, M7/M8/M9 delta, M10/M11/M12v2/M13 404) + probe diferencial do UNIQUE(thread_id,seq) que nenhum teste do suite alcancava sozinho; anomalia M12 v1 documentada no ledger (CLASSIFICACAO-ERRADA, redesenhado e vermelho); BOM do driver restaurado; unverified registrados (suite Linux/WSL sem dotnet, since acima do topo, tail malformado do POST, contencao multi-processo)
- 2026-11-12 03:07:33 | odev | transicao AWAITING_QA -> DONE; TASK-0034 = entrega 2 de 8 da P-05 encerrada; merge em develop + docs pelo ODEV (gate modelo C); TASK-0035 desbloqueada

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-12 02:23:01 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261112-020517-test-gate.log (baseline 438/438) |
| 2026-11-12 02:23:03 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 0 | stdout: build exit 0 (apenas avisos pre-existentes) |
| 2026-11-12 02:23:04 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Threads | 0 | docs/sdd/runs/test-runs/20261112-021429-test-filter.log (11/11) |
| 2026-11-12 02:23:23 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Threads | 0 | docs/sdd/runs/test-runs/20261112-021603-test-filter.log (11/11, 2a execucao) |
| 2026-11-12 02:23:25 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261112-022110-test-gate.log (gate AC-05: 443/443) |
| 2026-11-12 02:26:27 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate final AC-05) | 0 | docs/sdd/runs/test-runs/20261112-022110-test-gate.log - exit: 0 / Aprovado 443 / Com falha 0 / 4m36s - VERIFICADO PELO ODEV lendo o arquivo |
| 2026-11-12 02:26:28 | dev | git show --stat --format= HEAD (pos-commit) | 0 | f6ba1b2 - 7 files changed 761 insertions - 5 produto + 2 teste, todos em touches emendado - VERIFICADO PELO ODEV via git show |
| 2026-11-12 03:02:30 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Threads (baseline) | 0 | docs/sdd/runs/test-runs/20261112-023919-test-filter.log (exit 0, 11/11) |
| 2026-11-12 03:02:32 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0034.ps1 (execucao cheia, 13 ciclos) | 1 | docs/sdd/qa/QA-0034-mutations.log (12 vermelhos + M12 v1 anomalo CLASSIFICACAO-ERRADA, exit 1 = failed=1 do ledger) |
| 2026-11-12 03:02:33 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0034.ps1 -VerifyOnly | 0 | stdout VERIFY total=13 bad=0 (2a passagem, BOM restaurado) |
| 2026-11-12 03:02:35 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0034.ps1 -Only M12 | 0 | docs/sdd/qa/QA-0034-mutations.log (M12 v2 vermelho 024941 + GREEN 025001) |
| 2026-11-12 03:02:36 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-probe-task-0034-unique.ps1 | 0 | docs/sdd/qa/QA-0034-unique-probe.log (pristine green, mutado red, restore OK, git backend 0) |
| 2026-11-12 03:02:38 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate AC-05) | 0 | docs/sdd/runs/test-runs/20261112-025653-test-gate.log (exit 0, Aprovado 443, Com falha 0, 4m36s) |
| 2026-11-12 03:02:40 | qa | varredura de escopo pwsh (git status -uall, git status backend, mtime desde 02:38:45, residuo, git hash-object vs rev-parse) | 0 | docs/sdd/qa/QA-0034-scope.log (backend 0, blob-identity 3/3, mtime 3, residuo 0) |
| 2026-11-12 03:04:36 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0034.ps1 | 0 | docs/sdd/qa/QA-0034-check.log (SUMMARY total=29 fails=0) |
| 2026-11-12 03:04:37 | qa | wsl -e bash docs/sdd/qa/qa-check-task-0034.sh | 0 | docs/sdd/qa/QA-0034-check-sh.log (SUMMARY total=24 fails=0) |
| 2026-11-12 03:05:50 | qa | git commit -m (1 commit autorizado, 11 arquivos so de docs/sdd/qa, branch task/TASK-0034-envio-e-leitura, sem push) | 0 | dcda2db - 11 files changed 961 insertions, git show --stat conferido, pai f6ba1b2, main intocada |
