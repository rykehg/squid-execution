<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0043 — Superfície ActivityPub: ator completo, coleções, objeto, contêiner

```yaml
runId: R-20261113-01
taskId: TASK-0043
status: DONE
agent: odev
phase: P-06
sequence: 2
createdAt: 2026-11-13 13:47:25
updatedAt: 2026-11-13 20:43:03
parent: SPEC-0011
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Api/Discovery/**", "backend/src/Squid.Api/Federation/**", "backend/src/Squid.Protocols/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/**"]
blocks: ["TASK-0044"]
blockedBy: ["TASK-0042"]
inputArtifacts: ["docs/sdd/specs/SPEC-0011-federacao-p06.md", "docs/sdd/plan/plan.md", "specs/001-squid-mvp/contracts/activitypub.md"]
outputArtifacts: ["backend/src/Squid.Api/Discovery/**", "backend/src/Squid.Api/Federation/**", "backend/tests/Squid.Tests/**"]
interviewRounds: 2/6
feasibilityRounds: 0/2
correctionAttempts: 0/2
qaAttempts: 1/2
contextHandoffs: 0/4
progressDelta: 0
humanGate:
  approvedBy: NENHUM
  approvedDate: NENHUM
  scope: NENHUM
  expiresAt: NENHUM
blocker: NENHUM
nextAction: DONE - QA-0043 PASSED (7/7 ACs, 16 mutacoes, gate 512/512, commits a658bd1 + bc5e0ea); merge em develop pelo ODEV e dispatch da TASK-0044
evidence: []
memoryBankFlags: []
```

## Intent

Entrega 3 de 9 da P-06: a instância **expõe** o contrato `activitypub.md` §1 — documento de ator
completo (fecha a lacuna `S-26` do backlog: `inbox`/`outbox`/`followers`/`following`), coleções
paginadas, objeto, contêiner, nodeinfo e negociação de Accept. Os endpoints `POST .../inbox`
nascem aqui rejeitando sem assinatura verificável (`401`); a aplicação de atividades vem na
TASK-0044. Nenhuma UI (P-08).

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `GET /users/{name}` devolve o documento do ator do contrato §1.1 — `publicKey` objeto
  único `#main-key`, `squid:ed25519Key`, e **`inbox`, `outbox`, `followers`, `following`** — teste.
- AC-02: `GET /users/{name}/outbox`, `/followers` e `/following` devolvem `OrderedCollection`
  paginada com `first`/`next` (nunca o conjunto inteiro) — teste.
- AC-03: `GET /objects/{id}` e `GET /c/{slug}` (+ `GET /c/{slug}/outbox` primeira página e
  `GET /c/{slug}/members` com visibilidade) devolvem JSON-LD com `@context` e os campos
  `squid:kind`/`squid:slug`/`squid:policy` — teste.
- AC-04: `GET /.well-known/nodeinfo` devolve metadados de software/versão — teste.
- AC-05: `POST /users/{name}/inbox` e `POST /c/{slug}/inbox` existem e respondem `401` sem
  assinatura verificável, sem processar o corpo (estado intacto) — teste.
- AC-06: a resposta exige/aceita `Accept: application/activity+json` e
  `application/ld+json; profile="https://www.w3.org/ns/activitystreams"`; `webfinger` ganha
  `self` ao ator canônico e **não** ganha `rel=profile` (P-08) — teste.
- AC-07: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1
  test` exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Api/Discovery/**` — documento de ator ampliado, `self` do webfinger
- `backend/src/Squid.Api/Federation/**` — diretório novo: inbox (POST), outbox/followers/following
  (GET), objeto, contêiner+coleções, nodeinfo
- `backend/src/Squid.Protocols/**` — serialização JSON-LD na borda (ADR-11: JSON-LD nunca no domínio)
- `backend/src/Squid.Service/**` — mapeamento das rotas novas
- `backend/tests/Squid.Tests/**` — testes das ACs
- produto **fora** dos `touches` desta task: qualquer lacuna levantada como dúvida, não corrigida aqui

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| S-26 | no | `inbox`/`outbox`/`followers`/`following` entram **nesta task** (checagem do backlog pedida ao cortar a P-06); `rel=profile` continua P-08 e não entra aqui |
| — | no | inbox nasce rejeitando sem assinatura verificável (`401`); aplicar atividades é da TASK-0044 — a entrega é funcional pelo caminho de leitura |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-13 13:47:25 | odev | task created from SPEC-0011 (entrega 3 de 9, fecha S-26 (inbox/outbox/followers/following); blockedBy TASK-0042)
- 2026-11-13 18:35:26 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; runId R-20261113-01; blockedBy TASK-0042 resolve (DONE com QA-0042 PASSED 6/6, gate 503/503, merge 805bec5 em develop + docs 8f810c9); autorizacao de commit gate PHASE 2026-11-11 20:42:28 (modelo C) incluida - nova branch task/TASK-0043-superficie-activitypub desde develop
- 2026-11-13 19:35:24 | dev | feasibility 1/2 fechada: unidade, chamadores e dados nomeados (DiscoveryEndpoints + Federation/** novo + ProfileJsonLd + SquidServiceHost + log de atividades, objetos, conteneres e membros); touches confirmado completo, sem intersecao com outra task aberta (serial) e nenhum .csproj precisa mudar (globbing do SDK); baseline sdd-test test -Fast 490/490 exit 0 (log 20261113-185421-test-fast.log)
- 2026-11-13 19:35:24 | dev | duvidas de projeto fechadas pela SPEC-0011 secao D e pelo contrato activitypub 1/1.1/1.2/6: Accept aceita os dois tipos da secao 6, wildcard e header ausente, e recusa o resto com 406; colecao entrega first/next sem itens e os itens ficam em ?page=N; squid:kind/squid:slug/squid:policy tambem nas colecoes do contener (leitura literal do AC-03)
- 2026-11-13 19:35:25 | dev | implementado: documento de ator com inbox/outbox/followers/following (fecha S-26), 10 rotas novas (colecoes do ator, /objects/{id}, /c/{slug} + outbox + members, nodeinfo, os dois inboxes), JSON-LD de colecao, objeto e contener em Squid.Protocols costurado por 5 seams, e gate da verificacao de entrada (contrato 3 passo 1) em Squid.Service que recusa 401 antes de ler o corpo, responde 202 quando a assinatura local verifica e aplica nada
- 2026-11-13 19:35:25 | dev | testes: primeira execucao das 8 provas vermelha (PATCH ban exigindo justification respondendo 422, e base64 do wrappedSigningKey escapado pelo S-25) - corrigido no TESTE, depois 8/8 exit 0; suites pre-existentes da area Discovery, Protocols, Recovery e ServiceHost 52/52 exit 0 (comando literal no cabecalho do log, filtro com OR)
- 2026-11-13 19:35:26 | dev | gate sdd-test test exit 0 com 511/511 (log 20261113-193228-test-gate.log, cabecalho exit: 0) - AC-07; git status sem nenhum arquivo fora dos touches; commit a658bd1 em task/TASK-0043-superficie-activitypub desde 8f810c9 (develop), um commit unico autorizado pelo gate
- 2026-11-13 19:36:05 | odev | ODEV verificou antes de transicionar: gate 20261113-193228-test-gate.log lido no arquivo (exit: 0, Aprovado 511, Com falha 0, 5m46s); commit a658bd1 conferido via git show --stat (11 arquivos: 10 produto/teste nos touches + task file com o dispatch, NENHUM fora); branch task/TASK-0043-superficie-activitypub sobre develop 8f810c9, git status = so a task file com o meu carimbo; logLines (5) e evidenceRows (7) carimbados pelo ODEV via sdd-state; sdd-state check OK (log 7, evidence 7); step result DONE aceito
- 2026-11-13 19:36:06 | odev | flag [DIVERGENCIA] registrada para a consolidacao: docs/memory-bank/contracts.md:21 ainda diz que inbox/outbox/followers/following ficam sem handler ate P-06/P-08 e a TASK-0043 ja entrega os quatro (rel=profile continua P-08) - codigo vence, correcao do memory bank acontece no fecho da fase (consolidacao), nao agora
- 2026-11-13 19:36:08 | odev | transicao IN_DEVELOPMENT -> AWAITING_QA (attempt 1/2) - dispatch QA a seguir
- 2026-11-13 20:43:49 | qa | qa-task + ponytail-qa (full): entrada pela task file (status AWAITING_QA) com contrato activitypub 1/1.1/1.2/6, SPEC-0011 secao D e memoria banc rota qa-integracao (S-22 e S-23 de build-run-test.md)
- 2026-11-13 20:43:50 | qa | baseline git capturada antes de qualquer escrita: HEAD a658bd1 em task/TASK-0043-superficie-activitypub, 11 arquivos do commit todos nos touches, dotnet ausente no WSL (suite Linux unverified) - QA-0043-git-baseline.txt
- 2026-11-13 20:43:50 | qa | baseline do filtro ActivityPubSurfaceTests pre-escrita: exit 0 com 8 de 8 - log 20261113-194654-test-filter.log
- 2026-11-13 20:43:51 | qa | a QA escreveu 1 cenario novo dentro dos touches (QaTask0043AcceptTests.cs): a AC-06 no caminho federado nao tinha teste - verde 9 de 9 exit 0 - log 20261113-201417-test-filter.log
- 2026-11-13 20:43:51 | qa | qa-mutate-task-0043 -VerifyOnly: 16 padroes hits=1, ASCII OK, VERIFY total=16 bad=0, exit 0 - QA-0043-verify.log
- 2026-11-13 20:43:52 | qa | qa-mutate-task-0043 run final: 16 ciclos can-fail contra o PRODUTO (T1..T16), 16 vermelhos classificados DENTRO do log com a RED-AC da propria AC, 16 restore byte-exact (hash PRE igual ao blob de HEAD), GREEN 9/9 exit 0, SUMMARY cycles=16 failed=0 residue-backend=0, exit 0 - QA-0043-mutations.log
- 2026-11-13 20:43:52 | qa | gate AC-07 sdd-test.ps1 test: exit 0 com 512 de 512 lido dentro do log (cabecalho exit: 0) - log 20261113-203640-test-gate.log
- 2026-11-13 20:43:53 | qa | varredura de escopo final: blob-identity 7 de 7 contra HEAD, mtime 8, residuo de tokens mutantes 0, git status backend 1 entrada (so o teste novo nos touches), fora-dos-touches 0 - QA-0043-scope.log
- 2026-11-13 20:43:53 | qa | checkers executados: qa-check-task-0043.ps1 exit 0 com SUMMARY total=50 fails=0 (inclui sdd-state check OK: log 10, evidence 7) e gemeo wsl bash qa-check-task-0043.sh exit 0 com SUMMARY total=33 fails=0 - QA-0043-check.log e QA-0043-check-sh.log
- 2026-11-13 20:43:54 | qa | verdict PASSED 7/7 com commit unico autorizado bc5e0ea (11 arquivos: 10 artefatos em docs/sdd/qa + o teste novo nos touches), task file intacta - logLines e evidenceRows devolvidos sem relogio para o ODEV carimbar
- 2026-11-13 20:44:01 | odev | ODEV verificou antes de transicionar: QA-0043-superficie-activitypub.md com verdict PASSED lido no arquivo; gate 20261113-203640-test-gate.log exit: 0 / Aprovado 512 / Com falha 0 / 6m3s (lido no arquivo); mutations SUMMARY cycles=16 failed=0 residue-backend=0 lido no log; commit bc5e0ea conferido via git show --stat (11 arquivos: 10 artefatos qa + QaTask0043AcceptTests.cs dentro dos touches da QA, task file fora do commit); logLines (10) e evidenceRows (10) carimbados pelo ODEV via sdd-state; sdd-state check - veredito aceito
- 2026-11-13 20:44:01 | odev | QA-0043 PASSED attempt 1/2 (PRODUTO+TESTES): 7/7 ACs, 16 ciclos can-fail todos vermelhos na AC alvo, restore byte-exact 16/16, GREEN 9/9; gate 512/512 (511 DEV + 1 teste novo da QA na AC-06 federada); achado: 0 de produto; DIVERGENCIA contracts.md:21 confirmada pela QA - consolidacao da fase
- 2026-11-13 20:44:02 | odev | transicao AWAITING_QA -> DONE; entrega 3 de 9 da P-06 encerrada - merge em develop + docs commitados pelo ODEV, dispatch da TASK-0044 a seguir

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-13 19:35:26 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -Fast | 0 | docs/sdd/runs/test-runs/20261113-185421-test-fast.log (baseline pre-mudanca 490/490) |
| 2026-11-13 19:35:27 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 0 | console, sem log gravado (primeira tentativa saiu 1 com CS1061 em InboxSignatureGate.cs, corrigido e rebuild exit 0) |
| 2026-11-13 19:35:27 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Federation | 1 | docs/sdd/runs/test-runs/20261113-191433-test-filter.log (6/8; ban 422 e base64 escapado, corrigido no teste) |
| 2026-11-13 19:35:28 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter (Discovery, Protocols, Recovery e ServiceHost por OR - comando literal no cabecalho do log) | 0 | docs/sdd/runs/test-runs/20261113-191943-test-filter.log (52/52) |
| 2026-11-13 19:35:28 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Federation | 0 | docs/sdd/runs/test-runs/20261113-192559-test-filter.log (8/8 provas novas, estado final) |
| 2026-11-13 19:35:29 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261113-193228-test-gate.log (gate AC-07, 511/511, cabecalho exit: 0) |
| 2026-11-13 19:35:29 | dev | git switch -c task/TASK-0043-superficie-activitypub + git add -A + git commit TASK-0043 superficie ActivityPub | 0 | commit a658bd1 em task/TASK-0043-superficie-activitypub (11 arquivos, 1405 insercoes, 12 remocoes) |
| 2026-11-13 20:43:54 | qa | git rev-parse HEAD + git status --porcelain -uall + git show --name-only a658bd1 + wsl probe (pwsh) | 0 | docs/sdd/qa/QA-0043-git-baseline.txt (HEAD a658bd1, 11/11 nos touches, dotnet ausente no WSL) |
| 2026-11-13 20:43:55 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Federation.ActivityPubSurfaceTests | 0 | docs/sdd/runs/test-runs/20261113-194654-test-filter.log (baseline pre-escrita 8 de 8) |
| 2026-11-13 20:43:55 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0043.ps1 -VerifyOnly | 0 | docs/sdd/qa/QA-0043-verify.log (16 padroes hits=1, ASCII OK, VERIFY total=16 bad=0) |
| 2026-11-13 20:43:56 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Federation | 0 | docs/sdd/runs/test-runs/20261113-201417-test-filter.log (verde 9 de 9 com o teste novo da QA) |
| 2026-11-13 20:43:56 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0043.ps1 | 0 | docs/sdd/qa/QA-0043-mutations.log (16 vermelhos com RED-AC, 16 restore, GREEN 9 de 9, SUMMARY cycles=16 failed=0) |
| 2026-11-13 20:43:56 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261113-203640-test-gate.log (gate AC-07: cabecalho exit 0, Com falha 0, Aprovado 512, Total 512) |
| 2026-11-13 20:43:57 | qa | pwsh varredura de escopo (git hash-object vs rev-parse, mtime, tokens mutantes, git status) | 0 | docs/sdd/qa/QA-0043-scope.log (blob 7 de 7, mtime-count=8, residue=0, git-backend-entries=1, fora-dos-touches=0) |
| 2026-11-13 20:43:57 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0043.ps1 | 0 | docs/sdd/qa/QA-0043-check.log (SUMMARY total=50 fails=0, sdd-state check OK) |
| 2026-11-13 20:43:58 | qa | wsl -e bash docs/sdd/qa/qa-check-task-0043.sh | 0 | docs/sdd/qa/QA-0043-check-sh.log (SUMMARY total=33 fails=0, gemeo Linux) |
| 2026-11-13 20:43:58 | qa | git add dos 11 artefatos da QA + git commit TASK-0043 QA-0043: PASSED 7/7, 16 mutacoes, gate 512/512 | 0 | commit bc5e0ea em task/TASK-0043-superficie-activitypub (task file nao stageada) |
