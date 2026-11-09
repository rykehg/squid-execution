<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0020 — Papéis, moderação e contexto do avaliador (GAP-11)

```yaml
runId: R-20261108-01
taskId: TASK-0020
status: DONE
agent: odev
phase: P-03
sequence: 2
createdAt: 2026-11-08 11:09:06
updatedAt: 2026-11-09 01:01:43
parent: SPEC-0008
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Domain/Containers/**", "backend/src/Squid.Domain/IPolicyEvaluator.cs", "backend/src/Squid.Api/Containers/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/Containers/**", "backend/tests/Squid.Tests/Domain/**"]
blocks: ["TASK-0021"]
blockedBy: ["TASK-0019"]
inputArtifacts: ["docs/sdd/specs/SPEC-0008-containers-sociais-p03.md", "docs/sdd/specs/SPEC-0003-domain-container-policy.md", "specs/001-squid-mvp/data-model.md", "specs/001-squid-mvp/contracts/http-api.md"]
outputArtifacts: ["backend/src/Squid.Domain/Containers/**", "backend/src/Squid.Domain/IPolicyEvaluator.cs", "backend/src/Squid.Api/Containers/**", "backend/src/Squid.Service/**"]
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
nextAction: DONE - QA-0019 PASSED (6/6 ACs, gate 216/216, 12 mutacoes can-fail); proxima entrega TASK-0021 com AC-07 e AC-08
evidence: []
memoryBankFlags: []
```

## Intent

Fecha FR-012 (hierarquia com herança estrita), a parte de papéis do FR-013 e o GAP-11 da
SPEC-0003: `PATCH /members/{actorId}` auditado e `IPolicyEvaluator` avaliando com o contexto
completo do ator (papel + estado de membresia + pertencimento a `PublisherIds`). É a metade
funcional restante do DoD: `singlePublisher` bloqueia `Member` e permite o publisher nomeado.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `PATCH /api/containers/{slug}/members/{actorId}` por `Admin`+ muda o papel
  (ex.: `Member`→`Moderator`) e grava auditoria com autor, quando e `policyVersion` vigente
  (FR-013) — teste de corpo + linha na trilha.
- AC-02: transições da §3.3 pela mesma rota: `approve` (`Requested`→`Active`), `reject`,
  `silence`/`unsilence`, `ban`/`unban`; `ban` exige `justification` não vazia (vazia → `422`);
  `Banned`→`Active` só por `Admin` com justificativa; `Owner` não pode ser banido — testes.
- AC-03: `Silenced` faz `GET /api/containers/{slug}` 200 mas tem escrita negada; `Banned` faz
  `GET` `404` idêntico ao slug inexistente (FR-018) — teste.
- AC-04: facet DoD `singlePublisher` com contexto do ator: política `SinglePublisher` com
  publisher nomeado **ativo** → avaliação do publisher **permitida** e de um `Member`
  comum **negada**; `Guest` negado em qualquer escrita mesmo com política permissiva —
  teste (fecha GAP-11; é o "singlePublisher bloqueia Member e permite o publisher nomeado"
  do DoD do Sprint 2).
- AC-05: `PATCH /members/{actorId}` sem sessão → 401; por `Moderator` tentando mudar papel de
  outro ator → negado (papel gerido por `Admin`+, data-model §3.2) — teste.
- AC-06: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`
  exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Domain/IPolicyEvaluator.cs` (assinatura ganha o contexto do ator —
  **mudança de contrato interno**: atualizar os chamadores existentes e os testes da matriz)
- `backend/src/Squid.Domain/Containers/**` (transições puras §3.3)
- `backend/src/Squid.Api/Containers/**` (rota `PATCH /members/{actorId}`)
- `backend/src/Squid.Service/**` (persistência da mudança + auditoria)
- `backend/tests/Squid.Tests/Domain/**` (matriz existente adaptada ao contexto)
- `backend/tests/Squid.Tests/Containers/**`

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-11 | no | fecha aqui: `Evaluate(action, actorContext, policy)` onde `actorContext` = papel + estado de membros + `isPublisher`; o papel sozinho continua sendo aceito para a matriz (overload/wrapper), preservando os testes da TASK-0007 |
| — | no | hierarquia de quem altera: `Admin`+ muda papel; `Moderator`+ aprova entrada/silencia (data-model §3.2); nada além disso |
| — | no | `justification` no `ban` é string não vazia; sem tamanho máximo definido no contrato — assumido ≤ 1000 |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-08 11:16:35 | odev | task created from SPEC-0008 (entrega 4 de 6; blockedBy TASK-0019; fecha GAP-11 e a facet singlePublisher do DoD)
- 2026-11-08 23:00:09 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; blockedBy TASK-0019 resolve (DONE com QA-0018 PASSED, gate 201/201); baseline do gate e 201/201; esta task fecha GAP-11 (contexto do ator no avaliador) e a facet singlePublisher do DoD
- 2026-11-08 23:56:26 | odev | feasibility 1/2: task, SPEC-0008 F, data-model 2.4/3.2/3.3, contrato 4/8/9 e o codigo-alvo lidos; touches confirmado completo e sem intersecao com outra task aberta
- 2026-11-08 23:56:26 | odev | baseline do gate antes de editar: sdd-test.ps1 test exit 0, 201/201
- 2026-11-08 23:56:26 | odev | D-01 e as duvidas menores fechadas pela spec/requisito (ChangeRole na trilha; reject = (removido); 422 = validation_failed; Owner banido = 403; alvo handle ou actorId) - nenhuma duvida bloqueante, nenhum round extra
- 2026-11-08 23:56:26 | odev | GAP-11 fecha: IPolicyEvaluator ganha PolicyActor (papel + estado + isPublisher) e a overload Evaluate(action, actor, policy); a avaliacao so-papel continua respondendo como antes (matriz TASK-0007 e chamadores preservados)
- 2026-11-08 23:56:26 | odev | secao 3.3 vira regra pura em MembershipRules: MembershipChange + Apply com as seis arestas validas; qualquer combinacao fora delas e recusa
- 2026-11-08 23:56:26 | odev | rota PATCH /api/containers/{slug}/members/{*actorId}: 401 sem sessao, 404 invisivel/alvo ausente, autoridade pelo avaliador com contexto completo (Admin+ papeis e ban, Moderate+ approve/reject/silence), justificativa 422, invariantes do Owner (ban = 403, pedir Owner = 400, demover Owner = 409 last_owner), aresta 3.3, escrita com recalculo do contador e linha ChangeRole Allow/Deny na trilha
- 2026-11-08 23:56:26 | odev | suporte dentro dos touches: SqliteContainerStore ganha DeleteMembershipAsync, IContainerStore a assinatura, IPolicyAuditStore a constante ChangeRole
- 2026-11-08 23:56:26 | odev | FR-018/3.3 na visibilidade so em leitura: GET com Silenced = 200 e Banned = 404 identico ao slug inexistente mesmo em publico; escrita mantem a regra de existencia (join de banido em publico segue 403, QA-0018 intacta)
- 2026-11-08 23:56:26 | odev | testes novos: 13 (7 avaliador-contexto, 1 maquina 3.3, 5 de rota AC-01/02/03/05); dois builds intermediarios com erro de compilacao corrigidos (CS0136, CS0117); gate final sdd-test.ps1 test exit 0, 214/214
- 2026-11-08 23:56:26 | odev | ODEV carimbou estas 9 linhas do DEV com o relogio do fluxo - o DEV nao tocou o arquivo da task e devolveu logLines/evidenceRows como o dispatch exige
- 2026-11-08 23:56:26 | odev | ODEV verificou antes de transicionar: gate final 20261108-234910-test-gate.log lido no arquivo (exit: 0, Aprovado 214, Com falha 0 - S-22); git status = so toques (IPolicyEvaluator.cs, Containers/**, tests) + docs de fluxo; step result DONE aceito - D-01 fechada pela spec, nenhum round extra
- 2026-11-08 23:56:26 | odev | parked idea do DEV ADJUDICADA e CONFIRMADA pelo ODEV na fonte: JoinAsync (ContainerEndpoints.cs:293) passa por DecideVisibilityAsync com forRead = false e CanSeeAsync (867-876) so admite Active/Silenced em cont nao-publico - o convidado (Invited) de um grupo MembersOnly recebe 404 no join e nunca entra, contradiz SPEC-0008 secao E (com convite entra) e FR-014, e quebraria a facet DoD da TASK-0022; o mesmo bloqueio atinge Open/Request em nao-publico; pre-existente da TASK-0019 (fora dos toques da 0020, e a QA-0018 nao cobriu porque os testes de convite usam comunidade publica); correcao + teste adjudicados como AC-07 da TASK-0021 pela escada da especificacao (a propria spec ja responde, nenhum contrato muda), registro S-30 no backlog
- 2026-11-08 23:56:26 | odev | transicao IN_DEVELOPMENT -> AWAITING_QA; entrega 4 de 6 da P-03 (GAP-11 fechado, facet singlePublisher do DoD provada no avaliador)
- 2026-11-09 00:52:31 | qa | QA-0019 attempt 1/2 (runId R-20261108-01): AC-01..AC-06 mapeados em cenarios executados; baseline gate 20261109-000908 exit 0 com 214/214 lido dentro do log (S-22); filtros Containers 42/42, Domain 62/62 e os 2 testes novos Qa0019 2/2
- 2026-11-09 00:52:31 | qa | 12 ciclos de mutacao can-fail (M1..M12, incluindo policyVersion vigente e a acao ChangeRole da trilha) + 1 recusa do driver (M11 tentativa 1: PATTERN-COUNT 0 por aspas no arg boundary); todo vermelho classificado pelo log gravado, restauracao byte-exact com bump de mtime (S-16/S-23), hash = PRE, residuo QA-MUTATION = 0
- 2026-11-09 00:52:32 | qa | gate final 20261109-004537 exit 0 com 216/216 (214 baseline + 2 da QA), checkers .ps1 e .sh com 14/14 e self-test do checker com falha provada; AC-06 confirmada (git = baseline + 4 artefatos QA); veredito PASSED na QA-0019
- 2026-11-09 01:01:44 | odev | ODEV verificou antes de transicionar: QA-0019-papeis-moderacao-avaliador.md existe; gate final 20261109-004537-test-gate.log lido no arquivo (exit: 0, Aprovado 216, Com falha 0 - S-22); residuos QA-MUTATION em backend = 0; check exit 0 (log 18, evidencias 36); veredito aceito - 6/6 ACs, 12 mutacoes can-fail vermelhas e restauradas byte-exact (inclui M4 facet singlePublisher e M6 ban de Owner), checkers ps1 e sh 14/14, artefatos novos exatamente 4, S-30 nao tocado por nenhum cenario
- 2026-11-09 01:01:45 | odev | F1 da QA adjudicado (escada: especificacao): ban do Owner responde 403 policy_denied e NAO 409 - no data-model 3.3 remove = reject/demote (a linha some, e isso ja responde 409 last_owner) enquanto ban MANTEM a linha; entao o 9.11 do contrato (remocao do ultimo Owner) nao cobre ban, e o Rules da SPEC-0008 manda Owner banido negado -> 403; comportamento provado pela M6; leitura registrada para clarificacao de contracts.md na consolidacao
- 2026-11-09 01:01:45 | odev | F2 da QA adjudicado (escada: especificacao): as recusas de invariante do Owner (403 ban, 409 demote) voltam SEM linha na trilha e isso e gap real de FR-019/SC-011 - o codigo de erro e policy_denied e a negacao acontece dentro da rota; ja 422/400 de justificativa NAO gravam mesmo (validacao de payload, nenhuma politica e avaliada, e a spec C so manda gravar DECISAO de politica); correcao adjudicada como AC-08 da TASK-0021 (mesma familia e mesmo arquivo do AC-07 do S-30), contrato nao muda
- 2026-11-09 01:01:46 | odev | transicao AWAITING_QA -> DONE; TASK-0020 = entrega 4 de 6 da P-03 encerrada (QA-0019 PASSED); flag MUDANCA_RELEVANTE CONTRATO (PolicyActor + overload fechando GAP-11) fica para a consolidacao em contracts.md; cadeia libera a TASK-0021

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-08 23:56:48 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261108-230319-test-gate.log (baseline 201/201) |
| 2026-11-08 23:56:49 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 1 | CS0136 em IPolicyEvaluator.cs:126 (variavel de padrao duplicada - corrigida) |
| 2026-11-08 23:56:49 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 1 | CS0117 em ContainerEndpoints.cs:532 (PolicyAction.Moderator inexistente - trocado por Moderate) |
| 2026-11-08 23:56:50 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 0 | 0 erros e 0 avisos |
| 2026-11-08 23:56:50 | dev | git status --porcelain | 0 | so arquivos dos touches alterados por esta task (IPolicyEvaluator.cs + alvos); SquidServiceHost.cs, plan.md e BACKLOG.md ja modificados pela fatia P-03 anterior |
| 2026-11-08 23:56:50 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261108-234910-test-gate.log (214/214 suite inteira) |
| 2026-11-08 23:56:51 | odev | Select-String do gate final + git status + sdd-state check + leitura de CanSeeAsync | 0 | docs/sdd/runs/test-runs/20261108-234910-test-gate.log exit: 0 / Aprovado 214 / Com falha 0; toques fora NENHUM; join em grupo confirmado bloqueado em ContainerEndpoints.cs:867-876 |
| 2026-11-09 00:52:32 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (baseline desta QA) | 0 | docs/sdd/runs/test-runs/20261109-000908-test-gate.log exit 0 / Com falha 0 / Aprovado 214 (lido no log, S-22) |
| 2026-11-09 00:52:33 | qa | sdd-test.ps1 test -TestFilter FullyQualifiedName~Qa0019MemberModerationEdgeTests (2 testes novos da QA) | 0 | docs/sdd/runs/test-runs/20261109-001039-test-filter.log exit 0 / Aprovado 2 |
| 2026-11-09 00:52:33 | qa | sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Containers (area da task) | 0 | docs/sdd/runs/test-runs/20261109-001145-test-filter.log exit 0 / Aprovado 42 |
| 2026-11-09 00:52:34 | qa | sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Domain (matriz TASK-0007 + contexto do ator) | 0 | docs/sdd/runs/test-runs/20261109-001155-test-filter.log exit 0 / Aprovado 62 |
| 2026-11-09 00:52:34 | qa | qa-mutate-task-0019.ps1 M1: escrita do papel ignorada (Role = role ?? target.Role virou Role = target.Role) | 0 | vermelho 20261109-001947 4 falhas (AC-01, AC-02, AC-05, Qa0019) + verde 002009 7/7; hash = PRE |
| 2026-11-09 00:52:35 | qa | qa-mutate-task-0019.ps1 M2: 422 so para null (string.IsNullOrEmpty(justification) virou justification is null) | 0 | vermelho 002053 1 falha (AC-02 unban com espacos em branco) + verde 002113 5/5; hash = PRE |
| 2026-11-09 00:52:35 | qa | qa-mutate-task-0019.ps1 M3: guarda de leitura Banned virou Silenced (forRead em CanSeeAsync) | 0 | vermelho 002144 2 falhas (AC-03 metades 1 e 2) + verde 002203 5/5; hash = PRE |
| 2026-11-09 00:52:36 | qa | qa-mutate-task-0019.ps1 M4: singlePublisher sempre concede (return actor.IsPublisher virou return true) | 0 | vermelho 002227 1 falha (facet DoD AC-04) + verde 002237 7/7; hash = PRE |
| 2026-11-09 00:52:36 | qa | qa-mutate-task-0019.ps1 M5: capacidade Admin virou Moderate na rota de membros | 0 | vermelho 002313 3 falhas (AC-02, AC-05, Qa0019 unban) + verde 002337 7/7; hash = PRE |
| 2026-11-09 00:52:37 | qa | qa-mutate-task-0019.ps1 M6: guarda de ban de Owner virou Silence (ban do Owner deixa de ser 403) | 0 | vermelho 002413 1 falha (AC-02 ban do Owner) + verde 002434 5/5; hash = PRE |
| 2026-11-09 00:52:37 | qa | qa-mutate-task-0019.ps1 M7: aresta reject da secao 3.3 nao remove a linha (true-null virou false-Active) | 0 | vermelho 002507 2 falhas (AC-02 reject + MembershipRules) + verde 002526 10/10; hash = PRE |
| 2026-11-09 00:52:38 | qa | qa-mutate-task-0019.ps1 M8: policyVersion do ChangeRole-Allow fixado em 1 (decision.PolicyVersion) | 0 | vermelho 002614 1 falha (Qa0019 espera a versao 2 vigente) + verde 002638 7/7; hash = PRE |
| 2026-11-09 00:52:38 | qa | qa-mutate-task-0019.ps1 M9: teto da justificativa 1000 virou 2000 (MaxJustificationLength) | 0 | vermelho 002710 1 falha (Qa0019 teto de 1001 chars) + verde 002726 2/2; hash = PRE |
| 2026-11-09 00:52:39 | qa | qa-mutate-task-0019.ps1 M10: guarda de democao do Owner virou Guest (409 last_owner deixa de acontecer) | 0 | vermelho 002757 1 falha (Qa0019 409 last_owner) + verde 002813 2/2; hash = PRE |
| 2026-11-09 00:52:39 | qa | qa-mutate-task-0019.ps1 M11 tentativa 1: -Old com aspas duplas no padrao do ParseRole | 1 | n/a: PATTERN-COUNT 0 (aspas quebradas no arg boundary do powershell.exe), nada mutado |
| 2026-11-09 00:52:39 | qa | qa-mutate-task-0019.ps1 M11 tentativa 2: default do ParseRole _ => null virou _ => MembershipRole.Member | 0 | vermelho 002939 1 falha (Qa0019 grant Owner 400) + verde 002956 2/2; hash = PRE |
| 2026-11-09 00:52:40 | qa | sdd-test.ps1 test -TestFilter FullyQualifiedName~MemberModeration (7 testes combinados apos restauracoes) | 0 | docs/sdd/runs/test-runs/20261109-003620-test-filter.log exit 0 / Aprovado 7 |
| 2026-11-09 00:52:40 | qa | sdd-test.ps1 test (gate pos-restauracao, antes do M12) | 0 | docs/sdd/runs/test-runs/20261109-003837-test-gate.log exit 0 / Aprovado 216 |
| 2026-11-09 00:52:41 | qa | qa-mutate-task-0019.ps1 M12: acao da trilha ChangeRole virou View no registro Allow (regex) | 0 | vermelho 004129 2 falhas (AC-01 MemberModeration + Qa0019) + verde 004150 7/7; hash = PRE |
| 2026-11-09 00:52:41 | qa | sdd-test.ps1 test (gate final desta QA, AC-06) | 0 | docs/sdd/runs/test-runs/20261109-004537-test-gate.log exit 0 / Com falha 0 / Aprovado 216 (lido no log, S-22) |
| 2026-11-09 00:52:42 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0020.ps1 (1a execucao) | 0 | console: SUMMARY total=14 fails=0 RESULT: PASS |
| 2026-11-09 00:52:43 | qa | bash docs/sdd/qa/qa-check-task-0020.sh (1a execucao no WSL) | 0 | console: SUMMARY total=14 fails=0 RESULT: PASS |
| 2026-11-09 00:52:43 | qa | qa-check-task-0020.ps1 self-test: limiar GATE-NEWEST-SUMMARY 216 trocado para 9999 | 1 | console: CHECK GATE-NEWEST-SUMMARY FAIL SUMMARY total=14 fails=1 RESULT: FAIL |
| 2026-11-09 00:52:43 | qa | qa-check-task-0020.ps1 self-test: limiar restaurado e script reexecutado (hash identico ao PRE) | 0 | console: MATCH=True SUMMARY total=14 fails=0 RESULT: PASS |
| 2026-11-09 00:52:44 | qa | sdd-test.ps1 probe + wsl -e sh -lc command -v dotnet e command -v pwsh (plataformas) | 0 | console: Windows dotnet 10.0.403; WSL dotnet ABSENT e pwsh ABSENT com bash ok (suite Linux unverified) |
| 2026-11-09 00:52:44 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-state.ps1 check docs/sdd/tasks/TASK-0020-papeis-moderacao-avaliador.md (antes dos appends) | 0 | console: check OK - log entries 15, evidence rows 7 |
| 2026-11-09 00:57:42 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0020.ps1 (execucao final pos-appends e pos-QA-0019, new-allowed 4/4) | 0 | console: SUMMARY total=14 fails=0 RESULT: PASS |
| 2026-11-09 00:57:42 | qa | bash docs/sdd/qa/qa-check-task-0020.sh (execucao final no WSL pos-appends, new-allowed 4/4) | 0 | console: SUMMARY total=14 fails=0 RESULT: PASS |
| 2026-11-09 00:57:43 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-state.ps1 check docs/sdd/tasks/TASK-0020-papeis-moderacao-avaliador.md (execucao final) | 0 | check: OK - log entries 18, evidence rows 35 |
| 2026-11-09 01:01:46 | odev | Select-String do gate final + varredura QA-MUTATION em backend + sdd-state check | 0 | docs/sdd/runs/test-runs/20261109-004537-test-gate.log exit: 0 / Aprovado 216 / Com falha 0; residuos 0; check OK log 18 evidencias 36; QA-0019 presente |
