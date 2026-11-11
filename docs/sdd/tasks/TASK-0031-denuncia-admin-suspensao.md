<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0031 — Denúncia, fila admin e suspensão (FR-028/FR-008)

```yaml
runId: R-20261109-01
taskId: TASK-0031
status: DONE
agent: odev
phase: P-04
sequence: 2
createdAt: 2026-11-09 11:22:36
updatedAt: 2026-11-11 00:02:29
parent: SPEC-0009
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Api/Posts/**", "backend/src/Squid.Api/Admin/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/Moderation/**"]
blocks: ["TASK-0032"]
blockedBy: ["TASK-0030"]
inputArtifacts: ["docs/sdd/specs/SPEC-0009-publicacao-feed-postagem.md", "specs/001-squid-mvp/spec.md", "specs/001-squid-mvp/data-model.md", "specs/001-squid-mvp/contracts/http-api.md"]
outputArtifacts: ["backend/src/Squid.Api/Posts/**", "backend/src/Squid.Api/Admin/**", "backend/src/Squid.Service/**"]
interviewRounds: 0/6
feasibilityRounds: 0/2
correctionAttempts: 0/2
qaAttempts: 0/2
contextHandoffs: 0/4
progressDelta: 0
humanGate:
  approvedBy: Henrique
  approvedDate: 2026-11-09
  scope: FR-028 (denuncia) incluido no escopo da P-04 na entrevista de abertura
  expiresAt: NENHUM
blocker: NENHUM
nextAction: DONE - QA-0031 PASSED (5/5 ACs, gate 428/428, 5 mutacoes); PARADA POR ORDEM HUMANA 2026-11-10 23:53:57 - TASK-0032 em outra sessao
evidence: []
memoryBankFlags: []
```

## Intent

Fecha FR-028 + FR-008: `POST /api/posts/{id}/report` idempotente, fila `GET /api/admin/reports`
e `POST /api/admin/reports/{id}/resolve` (`keep|remove|ban` com justificativa) — `remove`
executa a remoção da TASK-0030, `ban` nasce `POST /api/admin/actors/{id}/suspend`
(`Actor.Status: Suspended` de §3.1, já bloqueado na publicação desde a TASK-0023).

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `POST /api/posts/{id}/report` com motivo → 201; repetir do mesmo ator no mesmo post →
  **a mesma** denúncia, sem duplicata (GAP-28); sem sessão → 401; post invisível → 404 idêntico —
  teste.
- AC-02: `GET /api/admin/reports` devolve a fila paginada do operador: sem sessão → 401,
  sessão de não-operador → `403 policy_denied`, operador (`Instance:Operators`) → 200 com
  filtros funcionando — teste (guard da P-03 reusado).
- AC-03: `POST /api/admin/reports/{id}/resolve`: `justification` vazia → `422`; `keep` fecha a
  denúncia auditada; `remove` executa a remoção do post **em nome do operador** (tombstone +
  `RemoveContent` + projeção); `ban` suspende o ator — em todos, linha na trilha com palavra
  `Resolve`, autor, alvo e justificativa (FR-008, GAP-27) — testes dos três.
- AC-04: ator `Suspended` não publica (403 + `Deny` auditado) mas **lê** normalmente — teste;
  a suspensão vemida de `resolve ban` é a mesma `Suspended` — teste de estado.
- AC-05: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`
  exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Api/Posts/**` (rota `report`)
- `backend/src/Squid.Api/Admin/**` (fila, resolve, `suspend` — guarda já existente)
- `backend/src/Squid.Service/**` (store de denúncias + estado do ator)
- `backend/tests/Squid.Tests/Moderation/**` (novo)

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-27 | no | palavra `Resolve` nova na trilha (mesma família das emendas aceitas no fecho da P-03); `reinstate` fica fora — sem rota no contrato, suspensão por API é irreversível nesta fase e isso fica registrado |
| GAP-28 | no | denúncia idempotente por `(post, ator)` — repetição devolve a existente (200/201, consistente com a primeira) |
| — | no | a fila cobre denúncias de post (MVP); denúncia de ator/contêiner sem rota no contrato não existe |
| — | no | `POST /api/admin/actors/{id}/suspend` nasce aqui como caminho do `resolve ban`; a rota direta do contrato §7 funciona com o mesmo handler |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-09 11:34:59 | odev | task created from SPEC-0009 (entrega 9 de 10; blockedBy TASK-0030; FR-028 no escopo por decisao humana 2026-11-09)
- 2026-11-10 21:58:14 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; blockedBy TASK-0030 resolve (DONE com QA-0030 PASSED, gate 419/419); baseline do gate e 419/419; FR-028/FR-008 no escopo por decisao humana 2026-11-09
- 2026-11-10 23:15:41 | dev | feasibility round 1: baseline do gate 419/419 exit 0; touches confirmado (Posts, Admin, Squid.Service, Moderation), sem duvida bloqueante - tres pontos em aberto resolvidos pela spec (GAP-28 repeticao 200; 9.12 422 reason/justification vazios; FR-008 alvo = id da denuncia)
- 2026-11-10 23:15:41 | dev | IReportStore/ReportView/ReportEndpoints criados: POST /api/posts/{id}/report idempotente por (post, ator) - 201 na primeira, 200 na repeticao com a mesma denuncia, 401 sem sessao, 404 identico para post invisivel e inexistente, 422 com reason vazio
- 2026-11-10 23:15:41 | dev | PostEndpoints: dispatch de POST /api/posts/{*target} por sufixo (report | replies | 404) e RemoveContentAsync extraido - tombstone + Activity: Delete + RemoveContent num unico caminho, agora executado tambem pelo resolve action=remove em nome do operador
- 2026-11-10 23:15:41 | dev | AdminEndpoints: Gate do operador extraido sem mudar os 401/403 existentes; GET /api/admin/reports paginado com filtros post/status; resolve keep|remove|ban com 422 de justificativa vazia, 400 de acao invalida e linha Resolve (GAP-27) com autor/alvo/justificativa; POST /api/admin/actors/{*target}/suspend com o mesmo handler de ban e linha ChangeRole
- 2026-11-10 23:15:41 | dev | SqliteReportStore em Squid.Service no padrao SqlitePolicyAuditStore (documento unico, CAS, shortcut declarado) e registro em SquidServiceHost
- 2026-11-10 23:15:41 | dev | 7 testes novos em tests/Squid.Tests/Moderation: filtro Moderation exit 0 com 7/7 apos corrigir prefixo de handle invalido (422 no registro), join em contener group (404) e uma assercao de fila que esperava 1 item onde a propria criacao fazia 2; gate completo apos a mudanca: 426/426 aprovados, exit 0 (419 da baseline + 7 novos)
- 2026-11-10 23:15:45 | odev | ODEV carimbou as 6 logLines e as 8 evidenceRows do DEV com o relogio do fluxo (DEV nao tocou o arquivo da task)
- 2026-11-10 23:15:45 | odev | ODEV verificou antes de transicionar: gate final 20261110-231218-test-gate.log lido no arquivo (exit: 0, Aprovado 426, Com falha 0 - S-22); residuos QA-MUTATION = 0; step result DONE aceito - nenhum duvida; resolve reutiliza RemoveContentAsync extraido (sem fork do tombstone), suspend dirige o IActorStatusStore da TASK-0023 conforme o parked idea, alvo da linha Resolve = id da denuncia com postId/autor/report no reason (FR-008)
- 2026-11-10 23:15:46 | odev | parked idea registrada: a fila expoe so postId - o operador nao le o conteudo denunciado em contener privado pela propria fila (snapshot de conteudo exigiria decisao de escopo); flag MUDANCA_RELEVANTE CONTRATO (4 rotas novas de report/admin com handler) para a consolidacao
- 2026-11-10 23:15:46 | odev | transicao IN_DEVELOPMENT -> AWAITING_QA; entrega 9 de 10 da P-04
- 2026-11-10 23:56:39 | qa | QA attempt 1/2: entrada pela task (status AWAITING_QA, qaAttempts 0/2); 5 ACs mapeadas em cenarios; gate de entrada 426/426 exit 0 lido no arquivo docs/sdd/runs/test-runs/20261110-232637-test-gate.log
- 2026-11-10 23:56:40 | qa | QA escreveu 2 cenarios em backend/tests/Squid.Tests/Moderation/QaTask0031Tests.cs (paridade do resolve remove com o DELETE do autor; idempotencia 201/200 com a chave do par) e corrigiu 3 erros do proprio teste (CS0246, CS0103, casing do tombstone) antes do verde 9/9 em 20261110-233755-test-filter.log
- 2026-11-10 23:56:41 | qa | 5 ciclos de mutacao M1..M5 (S-16 SHA256 PRE, S-23 restore byte-exact com bump de mtime): todos RED-IN-LOG e RESTORE-OK, GREEN 9/9, residuo QA-MUTATION 0 - cada AC provou pode-falhar
- 2026-11-10 23:56:41 | qa | gate final 20261110-234659-test-gate.log com exit: 0 e Aprovado 428/428 lido dentro do log (S-22); escopo git so artefatos QA-0031; checkers ps1 e sh 8/8; verdicto PASSED em docs/sdd/qa/QA-0031-denuncia-fila-suspensao.md
- 2026-11-11 00:02:30 | odev | ODEV verificou antes de transicionar: QA-0031-denuncia-fila-suspensao.md com verdicto PASSED; gate final 20261110-234659-test-gate.log lido no arquivo (exit: 0, Aprovado 428, Com falha 0 - S-22); residuos QA-MUTATION = 0; check exit 0 (log 16, evidencias 23); veredito aceito - 5/5 ACs, 5 ciclos can-fail (M1 idempotencia, M2 gate do operador, M3 palavra Resolve, M4 paridade do remove, M5 ban sem suspender), 0 achados de produto
- 2026-11-11 00:02:31 | odev | flags para a consolidacao do fecho da P-04: [GAP] contracts.md (report/fila/resolve/suspend com handler desde esta task), MUDANCA_RELEVANTE CONTRATO (4 rotas novas, palavra Resolve GAP-27, idempotencia GAP-28, suspensao por API irreversivel na fase); unverified registrados (Linux, corrida de denuncias simultaneas, ramo post-null do resolve)
- 2026-11-11 00:02:31 | odev | transicao AWAITING_QA -> DONE; TASK-0031 = entrega 9 de 10 da P-04 encerrada (QA-0031 PASSED); POR ORDEM HUMANA 2026-11-10 23:53:57 a sessao PARA AQUI - TASK-0032 nao dispatchada, fase 9/10 com authority SPEC, consolidacao pendente do fecho (exige a ultima entrega DONE)

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-10 23:15:41 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261110-220355-test-gate.log - Aprovado 419/419 (baseline) |
| 2026-11-10 23:15:41 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build (1a tentativa) | 1 | CS1955 em SqliteReportStore.cs (throw sem new) - corrigido em seguida |
| 2026-11-10 23:15:42 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 0 | build: exit 0, so avisos pre-existentes |
| 2026-11-10 23:15:42 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Moderation (1a) | 1 | docs/sdd/runs/test-runs/20261110-230339-test-filter.log - 2 falhas de teste (prefixo com maiuscula; join em group) |
| 2026-11-10 23:15:43 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Moderation (2a) | 1 | docs/sdd/runs/test-runs/20261110-230539-test-filter.log - 1 falha (assercao da fila com 2 itens) |
| 2026-11-10 23:15:43 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Moderation (3a) | 0 | docs/sdd/runs/test-runs/20261110-230722-test-filter.log - Aprovado 7/7 |
| 2026-11-10 23:15:44 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261110-231218-test-gate.log - Aprovado 426/426 (gate final) |
| 2026-11-10 23:15:44 | dev | git status --porcelain | 0 | escopo: mudancas apenas em Squid.Api/Posts, Squid.Api/Admin, Squid.Service (SqliteReportStore + SquidServiceHost) e tests/Squid.Tests/Moderation |
| 2026-11-10 23:15:48 | odev | Select-String do gate final + varredura QA-MUTATION em backend + sdd-state check | 0 | docs/sdd/runs/test-runs/20261110-231218-test-gate.log exit: 0 / Aprovado 426 / Com falha 0; residuos 0; DEV log 6/evidence 8 carimbados |
| 2026-11-10 23:57:09 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261110-232637-test-gate.log - exit 0 e Aprovado 426 de 426 (gate de entrada lido no arquivo S-22) |
| 2026-11-10 23:57:09 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Moderation (1a) | 1 | docs/sdd/runs/test-runs/20261110-232935-test-filter.log - CS0246 no teste da QA (using faltando) |
| 2026-11-10 23:57:10 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Moderation (2a) | 1 | docs/sdd/runs/test-runs/20261110-233041-test-filter.log - CS0103 no teste da QA (parametro renomeado) |
| 2026-11-10 23:57:11 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Moderation (3a) | 1 | docs/sdd/runs/test-runs/20261110-233326-test-filter.log - 1 falha: casing do tombstone na assercao da QA |
| 2026-11-10 23:57:11 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Moderation (4a) | 0 | docs/sdd/runs/test-runs/20261110-233755-test-filter.log - Aprovado 9 de 9 |
| 2026-11-10 23:57:12 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0031.ps1 | 0 | docs/sdd/qa/QA-0031-mutations.log - M1 a M5 RED-IN-LOG e RESTORE-OK, GREEN 9 de 9, SUMMARY cycles=5 failed=0 residue=0 |
| 2026-11-10 23:57:12 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate final AC-05) | 0 | docs/sdd/runs/test-runs/20261110-234659-test-gate.log - exit 0 e Aprovado 428 de 428 lido dentro do log (S-22) |
| 2026-11-10 23:57:13 | qa | git status --porcelain vs QA-0031-git-baseline.txt mais varredura QA-MUTATION e mtimes | 0 | docs/sdd/qa/QA-0031-git-baseline.txt - adicionadas 6 removidas 0 residuo QA-MUTATION 0 backups 0 |
| 2026-11-10 23:57:13 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0031.ps1 | 0 | docs/sdd/qa/QA-0031-check.log - C1 a C8 PASS SUMMARY total=8 fails=0 |
| 2026-11-10 23:57:14 | qa | wsl bash -lc bash docs/sdd/qa/qa-check-task-0031.sh (1a) | 1 | bugs do script da QA: profundidade do repo, TEMP nao definido e argumento do grep - corrigidos |
| 2026-11-10 23:57:14 | qa | wsl bash -lc bash docs/sdd/qa/qa-check-task-0031.sh (2a) | 0 | docs/sdd/qa/QA-0031-check-sh.log - C1 a C8 PASS SUMMARY total=8 fails=0 |
| 2026-11-10 23:58:06 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-state.ps1 check docs/sdd/tasks/TASK-0031-denuncia-admin-suspensao.md | 0 | sdd-state check OK - log 16 linhas e evidence 20 linhas |
| 2026-11-10 23:58:07 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0031.ps1 (re-execucao final) | 0 | docs/sdd/qa/QA-0031-check.log - C1 a C8 PASS SUMMARY total=8 fails=0 |
| 2026-11-10 23:58:07 | qa | wsl bash -lc bash docs/sdd/qa/qa-check-task-0031.sh (re-execucao final) | 0 | docs/sdd/qa/QA-0031-check-sh.log - C1 a C8 PASS SUMMARY total=8 fails=0 |
| 2026-11-11 00:02:32 | odev | Select-String do gate final + varredura QA-MUTATION em backend + verdicto do doc + sdd-state check | 0 | docs/sdd/runs/test-runs/20261110-234659-test-gate.log exit: 0 / Aprovado 428 / Com falha 0; residuos 0; verdicto PASSED no QA-0031-denuncia-fila-suspensao.md; check OK log 16 evidencias 23 |
