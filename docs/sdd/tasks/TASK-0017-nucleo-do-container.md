<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0017 — Núcleo do contêiner: agregado, presets, criar/ler/editar

```yaml
runId: R-20261108-01
taskId: TASK-0017
status: DONE
agent: odev
phase: P-03
sequence: 2
createdAt: 2026-11-08 11:09:06
updatedAt: 2026-11-08 13:21:21
parent: SPEC-0008
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Domain/Containers/**", "backend/src/Squid.Api/Containers/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/Containers/**"]
blocks: ["TASK-0018"]
blockedBy: []
inputArtifacts: ["docs/sdd/specs/SPEC-0008-containers-sociais-p03.md", "specs/001-squid-mvp/data-model.md", "specs/001-squid-mvp/contracts/http-api.md", "docs/sdd/specs/SPEC-0003-domain-container-policy.md"]
outputArtifacts: ["backend/src/Squid.Domain/Containers/**", "backend/src/Squid.Api/Containers/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/Containers/**"]
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
nextAction: DONE - QA-0016 PASSED (6/6 ACs, gate 181/181); achado MINOR adjudicado (S-28 + pergunta ao humano); proxima entrega TASK-0018
evidence: []
memoryBankFlags: []
```

## Intent

Abre a P-03: agregado `Container` puro no Domain com os dois presets do data-model §2.2,
store SQLite própria com concorrência otimista (mesma regra da TASK-0003) e as rotas
`POST /api/containers`, `GET /api/containers/{slug}` e `PATCH /api/containers/{slug}`.
Entrega funcional sozinha: dá para criar um grupo e uma comunidade, lê-los e editá-los.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `POST /api/containers` autenticado (sessão + CSRF) com `kind: group` devolve 201 com
  `id` = `https://{host}/c/{slug}`, `listed: false` e `policy.version: 1`, aplicando os presets
  do data-model §2.2: `Visibility: MembersOnly`, `Join: Invite`, `Posting: Members`,
  `Interaction` todo `Members` — teste de corpo.
- AC-02: mesmo fluxo com `kind: community` devolve `listed: true`, `visibility: public`,
  `join: open`, `posting: members` — teste (os dois presets no mesmo arquivo de teste).
- AC-03: o criador é gravado como `Membership` `Owner`+`Active` e aparece como tal em
  `GET /api/containers/{slug}/members` já nesta task; slug duplicado **do mesmo dono** →
  `400 validation_failed` com nada criado (GAP-16) — teste.
- AC-04: `GET /api/containers/{slug}` devolve contêiner + política vigente quando visível;
  contêiner `MembersOnly` sem membership do leitor **ou** slug inexistente → `404` com corpo
  de erro **idêntico** nos dois casos (FR-018, SC-005) — teste comparando os dois corpos.
- AC-05: `PATCH /api/containers/{slug}` altera somente `name`, `summary` e `listed` (responde
  200) e a store rejeita escrita com versão de estado defasada → `409 version_conflict` com o
  estado inalterado (hash antes/depois) — teste de unidade na store + teste da rota.
- AC-06: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`
  exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Domain/Containers/**` (novo diretório: agregado e presets — `ContainerPolicy`
  e `IPolicyEvaluator` existentes ficam onde estão)
- `backend/src/Squid.Api/Containers/**` (novo diretório: interface da store + rotas)
- `backend/src/Squid.Service/**` (store SQLite + registro das rotas — padrão `SqliteAuthStore`)
- `backend/tests/Squid.Tests/Containers/**` (novo diretório)

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-16 | no | slug duplicado do dono → `400 validation_failed` (catálogo §8 não tem código próprio) |
| — | no | versão de estado (ETag interno da store) e `ContainerPolicy.Version` são campos distintos; PATCH incrementa a primeira, `PUT /policy` incrementa a segunda |
| — | no | `GET /members` nesta task devolve só o caso do dono recém-criado (a listagem completa é da TASK-0019) |
| — | no | membership nasce nesta task só para o Owner; os demais estados são da TASK-0019 |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-08 11:16:34 | odev | task created from SPEC-0008 (entrega 1 de 6, cadeia serial 0017..0022; abre Squid.Api/Containers e a store com ETag)
- 2026-11-08 11:21:00 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; blockedBy NENHUM (primeira task da cadeia da P-03); spec SPEC-0008 e as 6 tasks cortadas na entrevista 2026-11-08
- 2026-11-08 12:14:00 | dev | feasibility 1/2: baseline docs/sdd/_tools/sdd-test.ps1 test = 167/167 exit 0; touches confirmado (cadeia serial, nenhuma task aberta em paralelo); D-01 id e slug fechada por SPEC-0008 secao A + data-model 2.2 + quickstart US2/US4 (id = https://{host}/c/{slug}, slug vem no corpo do POST), D-02 caminho de 409 do PATCH fechada por AC-05 + contrato 4 (ETag de estado no GET, If-Match opcional no PATCH), D-03 404 identico sem traceId fechada por AC-04 + precedente do login — nenhuma duvida bloqueante
- 2026-11-08 12:14:00 | dev | dominio e borda escritos: agregado Container puro com presets Group/Community (MembersOnly+Invite+nao listado, Public+Open+listado, interacao toda Members, policy.version 1) e regra de slug em Squid.Domain/Containers; IContainerStore, ContainerView e rotas POST/GET/PATCH/members em Squid.Api/Containers com sessao+CSRF pelo middleware existente
- 2026-11-08 12:14:00 | dev | store e composicao: SqliteContainerStore em Squid.Service sobre IGrainStorage no padrao SqliteAuthStore (sem nova tabela, sem migracao), escrita guardada por WHERE Version = @Version e recusa em vez de retry quando o If-Match esta defasado; store registrada em SquidServiceHost e rotas mapeadas apos MapSquidAuth
- 2026-11-08 12:14:00 | dev | build corrigido em duas rodadas (StatusBadRequest para Status400BadRequest, using Squid.Domain para VisibilityPolicy) e 13 testes novos escritos em Squid.Tests/Containers: 4 de dominio, 4 de store com hash SHA-256 antes/depois do AC-05 e 5 de rota cobrindo AC-01 a AC-05
- 2026-11-08 12:14:00 | dev | gate final docs/sdd/_tools/sdd-test.ps1 test = 180/180 exit 0 (167 pre-existentes + 13 novos); git status mostra as escritas desta task todas em touches, e docs/sdd/plan/plan.md com modificacao anterior de ODEV, nao escrita aqui
- 2026-11-08 12:21:29 | odev | ODEV verificou antes de transicionar: gate 20261108-120801-test-gate.log lido no arquivo (exit: 0, Aprovado 180, Com falha 0 — leitura pelo log gravado, S-22); git status = so toques desta task (SquidServiceHost.cs, Domain/Containers, Api/Containers, SqliteContainerStore.cs, tests/Containers) + documentacao do ODEV; check exit 0 (log 7, evidencias 2: baseline 167/167 e final 180/180); step result DONE aceito (D-01..D-03 fechadas por spec/data-model/contrato, nenhuma bloqueante); flag memoryBankFlags [GAP: memory bank absent] NAO confere — docs/memory-bank existe no repo, rejeitavel na consolidacao com motivo; publish NENHUM (DEV escreveu direto no repo, sem staging); parkedIdeas (shortcut de escrita atomica em ContainerEndpoints) registrada para o ledger de debito
- 2026-11-08 13:10:48 | qa | QA-0016 attempt 1/2 na TASK-0017: entrada pelo arquivo da task (status AWAITING_QA, qaAttempts 0/2); AC-01..AC-06 mapeados para os 13 testes novos do DEV em Squid.Tests/Containers; baseline desta QA gate 20261108-123532 exit 0 com Aprovado 180 Com falha 0 e filtro Containers 13/13 (20261108-123610), classificados pelo exit:/summary dos logs gravados (S-22)
- 2026-11-08 13:10:48 | qa | 4 mutacoes can-fail (S-16), uma por comportamento, so em arquivos dos toques: M1 troca o preset de Community por Group e zera o Listed do Container.Create - 4 falhas (AC-01 rota linha 42 Actual True e dominio Actual MembersOnly, AC-02 rota e dominio); M2 abre o VisibleAsync para MembersOnly e grava a membership como Admin - 2 falhas (AC-03 linha 110 Actual admin, AC-04 linha 160 Actual OK); M3 desliga a guarda de versao do SqliteContainerStore - 2 falhas (AC-05 store linha 49 com a escrita velha gravada e rota linha 209 Actual OK); M4 deixa o slug do corpo entrar no PATCH - 1 falha no teste novo da QA (linha 58 Actual slug-roubado)
- 2026-11-08 13:10:48 | qa | cada restauracao foi byte-exact SHA256 PRE=POST com bump de mtime (S-23) e verde logo em seguida: Container.cs 4DE4ED58C87F2148606A3170D5E0F75B6AF9ADBA4DD0D62A3B1C109F1C653A8E, ContainerEndpoints.cs E8F30250BC0D9F2AEA6E38D64DFE41646D3ADCBEAA5E4E7DCE400DB6D6AD846F, SqliteContainerStore.cs E8FFE12D30C0E3179BB35DA1AC6BF990808813847EFA95061D935B90ED84DF0F; residuo final QA-MUTATION na arvore = 0 desta sessao (so 3 mencoes pre-existentes em QA-0015, TASK-0014 e TASK-0016)
- 2026-11-08 13:10:48 | qa | teste novo da QA Qa0016ContainerPatchScopeTests fecha a palavra somente do AC-05: PATCH com slug/kind/policy no corpo nao move nada alem de name/summary/listed, confirmado por releitura; a primeira execucao ficou vermelha por bug proprio do teste (handle com hifen viola ActorHandle.Rule [a-z0-9_]{3,30}) - corrigido o teste, nunca o produto - e as seguintes 14/14
- 2026-11-08 13:10:48 | qa | gate final 20261108-130113 exit 0 com Aprovado 181 Com falha 0 (167 baseline + 13 DEV + 1 QA) na arvore restaurada; escopo AC-06: git status --porcelain -uall e varredura de mtime desde o dispatch 11:21:00 sem nenhum produto fora dos touches - as entradas fora sao docs de fluxo do ODEV com mtime anterior ao dispatch (plan.md 11:20:29, SPEC-0008 11:09:06, TASK-0018..0022 11:16-11:17) e o append da propria task pela ferramenta 12:21:30; QA-0016-nucleo-do-container.md gravado, veredito PASSED sem BLOCKER e sem falhas pre-existentes
- 2026-11-08 13:10:48 | qa | achado MINOR para adjudicacao do ODEV (nao falha nenhum AC): ContainerEndpoints.cs:210 fixa a origem em https:// enquanto DiscoveryEndpoints.cs:114 usa scheme+Host, e o mesmo ator fica com dois URIs sobre http (membership ContainerRoutesTests:112 vs profile WebFingerProfileTests:223) - em producao atras de TLS nao aparece, risco em TASK-0019+/P-06; plataformas: suite so executada em Windows, WSL2 sem dotnet e sem pwsh (instalar e gate) e Linux unverified
- 2026-11-08 13:21:23 | odev | ODEV verificou antes de transicionar: QA-0016-nucleo-do-container.md existe; gate final 20261108-130113-test-gate.log lido no arquivo (exit: 0, Aprovado 181, Com falha 0 - S-22); residuos QA-MUTATION em backend/src e backend/tests = 0; check exit 0 (log 14, evidencias 21); veredito PASSED aceito - 6/6 ACs, 4 mutacoes can-fail vermelhas e restauradas byte-exact com bump mtime (S-16/S-23), escopo AC-06 limpo, 1 teste novo da QA dentro dos toques
- 2026-11-08 13:21:24 | odev | achado MINOR adjudicado (escada: especificacao): ContainerEndpoints.cs:210 fixa origem https:// e contracts.md:34 registra scheme+Host ESCOPADO a Descoberta (SPEC-0007 B-09a, CONFIRMADO); SPEC-0008 secao A tem as duas leituras e o AC-01 literal foi testado e passou - mudar AC ja testado e gate humano, entao vira S-28 no backlog + pergunta ao humano com default, sem reabrir a task
- 2026-11-08 13:21:24 | odev | flags de memory bank do step result: SUSPEITO (origem) vira S-28 com a pergunta em curso; PADRAO_NOVO (store de container sobre IGrainStorage sem tabela nova, conflito recusado em vez de reexecutado) aceita como candidata e verificada contra o codigo na consolidacao da P-03; GAP memory bank absent NAO confere - docs/memory-bank existe no repo - rejeitada no fecho com motivo; unverified Linux aceito (WSL sem dotnet/pwsh, instalar e gate; CI ubuntu e o executor)
- 2026-11-08 13:21:25 | odev | transicao AWAITING_QA -> DONE; TASK-0017 = entrega 1 de 6 da P-03 encerrada (QA-0016 PASSED); cadeia libera a TASK-0018
- 2026-11-08 16:02:16 | odev | gate humano 2026-11-08 15:57:34 (Henrique): o AC-01 literal https://{host} desta task (DONE, QA-0016 PASSED) e substituido pela regra scheme+Host da SPEC-0008 secao A emendada; a correcao fica registrada como AC-07 da TASK-0019 - task encerrada nao e reaberta, veredito da QA-0016 permanece valido para o que foi testado

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-08 12:14:13 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261108-112516-test-gate.log (baseline 167/167 antes de qualquer edicao) |
| 2026-11-08 12:14:34 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261108-120801-test-gate.log (final 180/180 com os 13 testes novos) |
| 2026-11-08 12:21:30 | odev | git status --porcelain + Select-String do gate final + sdd-state check | 0 | docs/sdd/runs/test-runs/20261108-120801-test-gate.log exit: 0 / Aprovado 180 / Com falha 0; check: OK log 7 evidencias 2; escritas fora dos toques: NENHUM |
| 2026-11-08 13:12:15 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261108-123532-test-gate.log - gate inicial exit: 0 Aprovado 180 Com falha 0 (lido no log, S-22) |
| 2026-11-08 13:12:15 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Containers | 0 | docs/sdd/runs/test-runs/20261108-123610-test-filter.log - baseline dos 13 testes novos exit: 0 Aprovado 13 Com falha 0 |
| 2026-11-08 13:12:15 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Containers | 1 | docs/sdd/runs/test-runs/20261108-123849-test-filter.log - M1 presets exit: 1 Com falha 4 - AC-01 rota:42 Actual True e dominio Actual MembersOnly, AC-02 rota e dominio |
| 2026-11-08 13:12:15 | qa | Get-FileHash SHA256 Container.cs + bump de mtime (restauracao M1, S-16 e S-23) | 0 | POST=PRE 4DE4ED58C87F2148606A3170D5E0F75B6AF9ADBA4DD0D62A3B1C109F1C653A8E mtime 2026-11-08 12:39:26 |
| 2026-11-08 13:12:15 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Containers | 0 | docs/sdd/runs/test-runs/20261108-123951-test-filter.log - pos-M1 exit: 0 Aprovado 13 Com falha 0 |
| 2026-11-08 13:12:15 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Containers | 1 | docs/sdd/runs/test-runs/20261108-124035-test-filter.log - M2 visibilidade e papel exit: 1 Com falha 2 - AC-03:110 Actual admin e AC-04:160 Actual OK |
| 2026-11-08 13:12:45 | qa | Get-FileHash SHA256 ContainerEndpoints.cs + bump de mtime (restauracao M2, S-16 e S-23) | 0 | POST=PRE E8F30250BC0D9F2AEA6E38D64DFE41646D3ADCBEAA5E4E7DCE400DB6D6AD846F mtime 2026-11-08 12:41:17 |
| 2026-11-08 13:12:45 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Containers | 1 | docs/sdd/runs/test-runs/20261108-124212-test-filter.log - M3 guarda de versao exit: 1 Com falha 2 - AC-05 store:49 com a escrita velha gravada e rota:209 Actual OK |
| 2026-11-08 13:12:45 | qa | Get-FileHash SHA256 dos 3 arquivos mutados + bump de mtime + varredura QA-MUTATION (restauracao M3, S-16 e S-23) | 0 | 3/3 POST=PRE (4DE4ED58 Container.cs, E8F30250 ContainerEndpoints.cs, E8FFE12D SqliteContainerStore.cs) residuos QA-MUTATION desta sessao = 0 |
| 2026-11-08 13:12:45 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261108-124714-test-gate.log - gate pos-restauracao exit: 0 Aprovado 180 Com falha 0 |
| 2026-11-08 13:12:45 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Containers | 1 | docs/sdd/runs/test-runs/20261108-125446-test-filter.log - 1a execucao do teste da QA exit: 1 Com falha 1 Qa0016:28 Actual UnprocessableEntity - bug proprio do teste, handle com hifen |
| 2026-11-08 13:12:45 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Containers | 0 | docs/sdd/runs/test-runs/20261108-125606-test-filter.log - teste da QA corrigido exit: 0 Aprovado 14 Com falha 0 |
| 2026-11-08 13:13:06 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Containers | 1 | docs/sdd/runs/test-runs/20261108-125730-test-filter.log - M4 slug gravavel no PATCH exit: 1 Com falha 1 Qa0016:58 Actual slug-roubado |
| 2026-11-08 13:13:06 | qa | Get-FileHash SHA256 ContainerEndpoints.cs + bump de mtime (restauracao M4, S-16 e S-23) | 0 | POST=PRE E8F30250BC0D9F2AEA6E38D64DFE41646D3ADCBEAA5E4E7DCE400DB6D6AD846F mtime 2026-11-08 12:58:00 |
| 2026-11-08 13:13:06 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Containers | 0 | docs/sdd/runs/test-runs/20261108-125927-test-filter.log - pos-M4 exit: 0 Aprovado 14 Com falha 0 |
| 2026-11-08 13:13:06 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261108-130113-test-gate.log - gate final exit: 0 Aprovado 181 Com falha 0 (167 baseline + 13 DEV + 1 QA) |
| 2026-11-08 13:13:06 | qa | git status --porcelain -uall + varredura de mtime desde o dispatch 11:21:00 (escopo AC-06) | 0 | produto fora dos touches: NENHUM - entradas fora sao docs de fluxo do ODEV com mtime anterior ao dispatch e o append da ferramenta na task |
| 2026-11-08 13:13:06 | qa | wsl -e sh -lc probe de dotnet e pwsh (plataforma Linux) | 0 | dotnet ABSENT pwsh ABSENT - suite em Linux marcada unverified, instalar e gate |
| 2026-11-08 13:21:25 | odev | Select-String do gate final + varredura QA-MUTATION em backend + sdd-state check | 0 | docs/sdd/runs/test-runs/20261108-130113-test-gate.log exit: 0 / Aprovado 181 / Com falha 0; residuos 0; check OK log 14 evidencias 21; QA-0016 presente |
