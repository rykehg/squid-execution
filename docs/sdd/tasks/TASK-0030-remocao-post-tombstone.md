<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0030 — Remoção de post com tombstone e auditoria (FR-027)

```yaml
runId: R-20261109-01
taskId: TASK-0030
status: DONE
agent: odev
phase: P-04
sequence: 2
createdAt: 2026-11-09 11:22:36
updatedAt: 2026-11-10 21:58:07
parent: SPEC-0009
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Api/Posts/**", "backend/src/Squid.ReadModels/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/Posts/**"]
blocks: ["TASK-0031"]
blockedBy: ["TASK-0029"]
inputArtifacts: ["docs/sdd/specs/SPEC-0009-publicacao-feed-postagem.md", "specs/001-squid-mvp/spec.md", "specs/001-squid-mvp/data-model.md", "specs/001-squid-mvp/contracts/http-api.md"]
outputArtifacts: ["backend/src/Squid.Api/Posts/**", "backend/src/Squid.ReadModels/**", "backend/src/Squid.Service/**"]
interviewRounds: 0/6
feasibilityRounds: 0/2
correctionAttempts: 0/2
qaAttempts: 0/2
contextHandoffs: 0/4
progressDelta: 0
humanGate:
  approvedBy: Henrique
  approvedDate: 2026-11-09
  scope: FR-027 (remocao com auditoria) incluido no escopo da P-04 na entrevista de abertura
  expiresAt: NENHUM
blocker: NENHUM
nextAction: DONE - QA-0030 PASSED (5/5 ACs, gate 419/419, 6 mutacoes; 1 queda de QA recuperada no caminho); proxima entrega TASK-0031
evidence: []
memoryBankFlags: []
```

## Intent

Fecha FR-027: `DELETE /api/posts/{id}` por autor ou `Moderator`+ com soft delete + tombstone
(`TakedownReason` obrigatório em remoção de terceiros — R-07), trilha `RemoveContent`,
evento `Activity: Delete` e projeções atualizadas — o post some de toda leitura sem apagar a
auditoria.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: autor remove o próprio post → soft delete com `DeletedAt`, evento `Activity: Delete`
  gravado e linha `RemoveContent` na trilha com autor, quando e `PolicyVersion` — teste.
- AC-02: `Moderator`+ remove post de terceiro exigindo `takedownReason` não vazio (vazio →
  `422`); quem não é autor nem `Moderator` → negado com `Deny` auditado — testes.
- AC-03: o removido some de **toda** leitura: `GET /api/posts/{id}` → 404 idêntico, `feed`,
  `timeline` e as respostas com `?since=` não o devolvem mais (projeção atualizada pelo
  projetor) — teste.
- AC-04: quem não via o post antes continua recebendo 404 com o mesmo corpo (a remoção não
  revela existência) e a linha da auditoria **não** é apagada — teste (hash da trilha antes/depois).
- AC-05: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`
  exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Api/Posts/**` (rota DELETE + trilha)
- `backend/src/Squid.ReadModels/**` (projetor trata `Activity: Delete`)
- `backend/src/Squid.Service/**` (tombstone na store)
- `backend/tests/Squid.Tests/Posts/**`

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| — | no | tombstone federado (§2.5) é o campo `DeletedAt` local aqui; a publicação federada do tombstone é P-06 |
| — | no | comentário removido usa o mesmo caminho (é um `Object`); replies do removido seguem o mesmo destino (somem) |
| — | no | `DELETE` responde `200` com o estado (contrato não fixa status de sucesso; 404 para quem não podia ver) |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-09 11:34:58 | odev | task created from SPEC-0009 (entrega 8 de 10; blockedBy TASK-0029; FR-027 no escopo por decisao humana 2026-11-09)
- 2026-11-10 18:13:31 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; blockedBy TASK-0029 resolve (DONE com QA-0029 PASSED, gate 411/411); baseline do gate e 411/411; FR-027 no escopo por decisao humana 2026-11-09
- 2026-11-10 19:21:17 | dev | baseline do gate 411/411 exit 0 antes de qualquer edicao; feasibility fechada sem duvida bloqueante; marcador [GAP: memory bank absent] do DEV NAO confere - docs/memory-bank existe (rejeitado no fecho)
- 2026-11-10 19:21:17 | dev | decidido: takedownReason aceito no CORPO e na QUERY (contrato nao fixa carrier) e DELETE responde 200 com {id, deletedAt, takedownReason} (gap 200 com o estado)
- 2026-11-10 19:21:17 | dev | implementado DELETE /api/posts/{id}: 401 -> 404 identico (ausente/invisivel) -> autor OU Moderate via avaliador com contexto completo (Deny auditado) -> 422 sem takedownReason em remocao de terceiros -> tombstone DeletedAt+TakedownReason, evento Activity: Delete na sequencia global, trilha RemoveContent Allow com autor/quando/PolicyVersion
- 2026-11-10 19:21:17 | dev | SqliteObjectStore ganhou o doc post-tombstone (sem migracao, sem Squid.Domain) e FindAsync e o gate unico que esconde o removido; ActivityProjector trata Activity: Delete apagando timeline_item e home_timeline e gravando linha de posicao neutra para o alto-mar do pump (sem ela o Create seria re-projetado e o post reapareceria)
- 2026-11-10 19:21:17 | dev | StreamSession: braço Delete segue sem emitir frame (SPEC-D/contrato 5 nao definem event: delete) - so o comentario foi atualizado
- 2026-11-10 19:21:17 | dev | gate final 415/415 exit 0 (411 base + 4 novos PostRemoveTests), build exit 0, nenhum arquivo alterado fora de touches
- 2026-11-10 19:21:20 | odev | ODEV carimbou as 6 logLines e as 5 evidenceRows do DEV com o relogio do fluxo (DEV nao tocou o arquivo da task)
- 2026-11-10 19:21:20 | odev | ODEV verificou antes de transicionar: gate final 20261110-191749-test-gate.log lido no arquivo (exit: 0, Aprovado 415, Com falha 0 - S-22); residuos QA-MUTATION = 0; step result DONE aceito - duas decisoes de forma aceitas (carrier do takedownReason duplo, 200 com estado - gap da task); parked ideas registradas: cascata de replies NAO implementada (nenhum AC exige, so o post sai das projecoes) e frame SSE de delete nao emitido (SPEC-D nao define)
- 2026-11-10 19:21:21 | odev | transicao IN_DEVELOPMENT -> AWAITING_QA; entrega 8 de 10 da P-04
- 2026-11-10 21:24:17 | odev | QA-0030 subagent FALHOU antes de fechar (harness, ~2026-11-10 20:18-20:22, sem mensagem de fechamento); deixou artefatos parciais: QaTask0030Tests.cs (4 testes), qa-mutate-task-0030.ps1, checkers .ps1/.sh, mutations.log, git-baseline - mas NENHUMA linha no Log/Evidence da task
- 2026-11-10 21:24:18 | odev | saude da arvore verificada pelo ODEV: 0 residuos QA-MUTATION; 3 arquivos de produto reescritos na janela 20:12-20:14 (PostEndpoints, SqliteObjectStore, ActivityProjector) = mutacoes em curso na morte; sonda sdd-test.ps1 test -> exit 0 com 419/419 (415 + os 4 testes da QA, todos verdes) em docs/sdd/runs/test-runs/20261110-212332-test-gate.log - arvore RESTAURADA, mutacoes revertidas antes de morrer; sem commit de base, entao a continuacao e o caminho
- 2026-11-10 21:24:19 | odev | politica de falha: 1 redispatch da mesma task (subagente nova, qaAttempt segue 1/2); a nova QA recebe os artefatos parciais (testes verdes, driver, checkers) como ponto de partida, nao como veredito; baseline de saude 419/419
- 2026-11-10 21:51:52 | qa | QA-0030 attempt 1/2 iniciada; artefatos herdados da tentativa morta verificados e RE-EXECUTADOS (4 testes QaTask0030 4/4 no filtro proprio, driver de mutacao re-executado integralmente, git-baseline re-conferido contra o git status atual)
- 2026-11-10 21:51:52 | qa | gate AC-05 executado: sdd-test.ps1 test exit 0 com 419/419 lido DENTRO do log 20261110-213449-test-gate.log (S-22); baseline da QA 415/415 do log 20261110-193406 re-verificado no arquivo
- 2026-11-10 21:51:52 | qa | 6 ciclos de mutacao M1..M6 todos vermelhos classificados no proprio log + restore byte-exact hash=PRE com bump de mtime + residuo QA-MUTATION 0 + GREEN 30/30; checkers ps1 e sh 10/10; escopo git +11 todas QA-0030, fora-da-allowlist=0
- 2026-11-10 21:51:52 | qa | veredito PASSED nos 5 ACs; 1 achado pre-existente (sdd-test.sh classifica dotnet ausente como falha real, mesmo das QA-0023..0029); suite Linux unverified; nao-falhas confirmadas por leitura: cascata de replies parked (ActivityProjector.cs:175-176 remove so o post) e frame SSE de delete nao emitido (StreamSession.cs:112-113); veredito em docs/sdd/qa/QA-0030-remocao-post-tombstone.md
- 2026-11-10 21:58:09 | odev | ODEV verificou antes de transicionar: QA-0030-remocao-post-tombstone.md com verdicto PASSED; gate final 20261110-213449-test-gate.log lido no arquivo (exit: 0, Aprovado 419, Com falha 0 - S-22); residuos QA-MUTATION = 0; check exit 0 (log 18, evidencias 13); veredito aceito - 5/5 ACs, 6 ciclos can-fail, 404 byte-identico por comparacao crua, trilha sobrevivendo com SHA256 antes==depois, pump convergente apos remocao trailing (linha neutra anti-flicker provada por M3)
- 2026-11-10 21:58:10 | odev | flags para a consolidacao do fecho: [GAP] contracts.md sem DELETE /api/posts (e sem midia/publicacao/stream), MUDANCA_RELEVANTE CONTRATO (semantica do DELETE: 200 com estado, takedownReason em corpo E query), PADRAO_NOVO (linha de posicao neutra urn:squid:projection segurando o alto-mar do pump); achado unico o pre-existente sdd-test.sh (S-11/S-12); marcador [GAP: memory bank absent] do DEV segue nao confirmando
- 2026-11-10 21:58:10 | odev | transicao AWAITING_QA -> DONE; TASK-0030 = entrega 8 de 10 da P-04 encerrada (QA-0030 PASSED apos redispatch por queda de harness); cadeia libera a TASK-0031

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-10 19:21:18 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261110-181918-test-gate.log (baseline 411/411) |
| 2026-11-10 19:21:18 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 0 | saida do build (0 erros; so warnings pre-existentes) |
| 2026-11-10 19:21:19 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~PostRemoveTests | 0 | docs/sdd/runs/test-runs/20261110-191059-test-filter.log (4/4 novos) |
| 2026-11-10 19:21:19 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261110-191749-test-gate.log (415/415) |
| 2026-11-10 19:21:19 | dev | git status --porcelain | 0 | working tree; os 6 arquivos desta task estao todos em Squid.Api/Posts, Squid.ReadModels, Squid.Service e tests/Squid.Tests/Posts |
| 2026-11-10 19:21:23 | odev | Select-String do gate final + varredura QA-MUTATION em backend + sdd-state check | 0 | docs/sdd/runs/test-runs/20261110-191749-test-gate.log exit: 0 / Aprovado 415 / Com falha 0; residuos 0; DEV log 6/evidence 5 carimbados |
| 2026-11-10 21:24:20 | odev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (sonda de saude apos falha da QA-0030) | 0 | docs/sdd/runs/test-runs/20261110-212332-test-gate.log exit: 0 / Aprovado 419 / Com falha 0 (415 + 4 testes da QA) |
| 2026-11-10 21:52:11 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261110-213449-test-gate.log exit: 0 e 419/419 (gate AC-05) |
| 2026-11-10 21:52:11 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~QaTask0030Tests | 0 | docs/sdd/runs/test-runs/20261110-213553-test-filter.log exit: 0 e 4/4 |
| 2026-11-10 21:52:12 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0030.ps1 | 0 | docs/sdd/qa/QA-0030-mutations.log SUMMARY cycles=6 failed=0 residue-QA-MUTATION=0 temp-backups=0 e GREEN 30/30 |
| 2026-11-10 21:52:12 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0030.ps1 | 0 | docs/sdd/qa/QA-0030-check.log SUMMARY total=10 fails=0 |
| 2026-11-10 21:52:13 | qa | wsl bash -lc bash docs/sdd/qa/qa-check-task-0030.sh | 0 | docs/sdd/qa/QA-0030-check-sh.log SUMMARY total=10 fails=0 e docs/sdd/qa/QA-0030-sh-twin.log (probe exit 0, test exit 1) |
| 2026-11-10 21:52:13 | qa | varredura de escopo (git status vs QA-0030-git-baseline, mtimes desde 21:24, residuo QA-MUTATION, hashes PRE) | 0 | docs/sdd/qa/QA-0030-scope.log adicionadas=11 removidas=0 fora-da-allowlist=0 residuo=0 |
| 2026-11-10 21:58:11 | odev | Select-String do gate final + varredura QA-MUTATION em backend + verdicto do doc + sdd-state check | 0 | docs/sdd/runs/test-runs/20261110-213449-test-gate.log exit: 0 / Aprovado 419 / Com falha 0; residuos 0; verdicto PASSED no QA-0030-remocao-post-tombstone.md; check OK log 18 evidencias 13 |
