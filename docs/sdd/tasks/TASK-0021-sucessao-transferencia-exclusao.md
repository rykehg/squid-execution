<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0021 — Sucessão, transferência, exclusão e override do operador

```yaml
runId: R-20261108-01
taskId: TASK-0021
status: DONE
agent: odev
phase: P-03
sequence: 2
createdAt: 2026-11-08 11:09:06
updatedAt: 2026-11-09 03:11:03
parent: SPEC-0008
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Domain/Containers/**", "backend/src/Squid.Api/Containers/**", "backend/src/Squid.Api/Admin/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/Containers/**"]
blocks: ["TASK-0022"]
blockedBy: ["TASK-0020"]
inputArtifacts: ["docs/sdd/specs/SPEC-0008-containers-sociais-p03.md", "specs/001-squid-mvp/data-model.md", "specs/001-squid-mvp/contracts/http-api.md"]
outputArtifacts: ["backend/src/Squid.Domain/Containers/**", "backend/src/Squid.Api/Containers/**", "backend/src/Squid.Api/Admin/**", "backend/src/Squid.Service/**"]
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
nextAction: DONE - QA-0020 PASSED (8/8 ACs, gate 235/235, 13 mutacoes can-fail); proxima entrega TASK-0022, ultima da P-03
evidence: []
memoryBankFlags: []
```

## Intent

Fecha FR-017 e o terceiro ponto do DoD do Sprint 2: sucessão com claim por inatividade,
transferência, exclusão com carência e override do operador — "contêiner nunca fica órfão,
sucessão e override cobertos por teste".

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `PUT /successor` por `Owner` nomeia o sucessor; `POST /claim` pelo sucessor **antes**
  do prazo → `403` (§9.12); com o relógio (`TimeProvider` injetado) avançado além de
  `Containers:SuccessorInactivityDays` (default 90) o sucessor assume: novo `Owner` `Active`,
  antigo vira `Admin`, tudo auditado — teste com relógio controlado, sem dormir.
- AC-02: override do operador: `POST /api/admin/containers/{slug}/transfer` com
  `justification` não vazia transfere e audita; `justification` vazia → `422` (§9.12); não
  operador → `403 policy_denied` — teste.
- AC-03: `POST /transfer` por `Owner`: novo `Owner` `Active`, antigo vira `Admin`, auditoria
  com autor/versão; sem sessão → 401; sessão de não-Owner → negado — teste.
- AC-04: `DELETE /api/containers/{slug}` sem `?confirm=<slug>` exato → `400`; com confirmação
  → soft delete: `DeletedAt` preenchido, `GET` → 404, sai do `search`, e o estado permanece
  recuperável até `Containers:DeleteGraceDays` (relógio controlado, default 30) — teste
  (GAP-17, GAP-18).
- AC-05: invariante DoD "nunca órfão": nenhuma sequência desta fase deixa o contêiner sem
  Owner ativo — saída do último Owner negada (`409 last_owner`, já provado na 0019), Owner não
  pode ser banido, e com Owner inativo o claim do sucessor ou o override do operador sempre
  restaura um Owner `Active` — teste de propriedade sobre os comandos.
- AC-06: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`
  exit 0; nenhum arquivo alterado fora dos `touches`.
- AC-07 (correção adjudicada S-30, 2026-11-08 23:56:48): entrada em contêiner **não-público**
  funciona como a SPEC-0008 §E manda — convidado (`Invited`) de um grupo `MembersOnly` faz
  `POST /join` → `Active` (é o caso do DoD "um grupo invite só entra por convite");
  `Open`/`Request` em não-público criam o fluxo (`Active`/`Requested`); e `Invite` **sem**
  convite em não-público continua `404` byte-idêntico ao slug inexistente (SC-006/FR-018 —
  as pinas da QA-0018 ficam verdes) — teste. Hoje `JoinAsync` (`ContainerEndpoints.cs:293`)
  bloqueia tudo por `CanSeeAsync` (`:867-876`), que só admite `Active`/`Silenced`.
- AC-08 (correção adjudicada F2 da QA-0019, 2026-11-09 01:01:43): as recusas de invariante do
  Owner — `403` no ban do Owner e `409 last_owner` na demote da linha Owner — gravam linha na
  trilha com `Result: Deny`, a `Action` da tentativa (`ChangeRole`) e a `PolicyVersion` vigente
  (FR-019/SC-011: toda negação de acesso restrito é auditável); já `422`/`400` de justificativa
  e payload continuam **sem** linha — validação de entrada, nenhuma política é avaliada
  (SPEC-0008 §C) — teste dos dois lados.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Domain/Containers/**` (regras puras de sucessão/transferência/soft delete)
- `backend/src/Squid.Api/Containers/**` (`PUT /successor`, `POST /claim`, `POST /transfer`, `DELETE`)
- `backend/src/Squid.Api/Admin/**` (rota de override do operador — guard já existe da 0018)
- `backend/src/Squid.Service/**` (persistência + relógio + auditoria)
- `backend/tests/Squid.Tests/Containers/**`

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-17 | no | `TimeProvider` injetável; `Containers:SuccessorInactivityDays=90`, `Containers:DeleteGraceDays=30` |
| GAP-18 | no | confirmação do `DELETE` = query `?confirm=<slug>` com o slug exato |
| — | no | inatividade do Owner = sem escrita/leitura registrada no agregado por N dias (assumido: sem atualização de `UpdatedAt` de membership/contêiner); se o campo de inatividade não existir, criá-lo nesta task |
| — | no | transferência move o antigo Owner para `Admin` (canônico: exatamente 1 Owner, data-model §2.2) |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-08 11:16:36 | odev | task created from SPEC-0008 (entrega 5 de 6; blockedBy TASK-0020; facet sucessao e override do DoD)
- 2026-11-08 23:58:04 | odev | adjudicacao do ODEV 2026-11-08 23:56:48: AC-07 adicionado antes do dispatch desta task - correcao do S-30 (join bloqueado em cont nao-publico, convidado de grupo nunca entra) junto com as rotas de sucessao; a escada parou na especificacao (SPEC-0008 secao E ja manda com convite entrar, FR-014 tambem), nenhum contrato muda e nenhum AC de task ja fechado e reescrito; toques ja cobrem ContainerEndpoints.cs e tests/Containers
- 2026-11-09 01:03:25 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; blockedBy TASK-0020 resolve (DONE com QA-0019 PASSED, gate 216/216); baseline do gate e 216/216; AC-08 (trilha das recusas de invariante do Owner) acrescentado antes do dispatch junto com o AC-07 do S-30 - typo de data do AC-08 corrigido antes do dispatch (2026-01-09 -> 2026-11-09)
- 2026-11-09 02:05:29 | dev | baseline do gate antes de escrever: sdd-test.ps1 test -> exit 0, 216/216
- 2026-11-09 02:05:29 | dev | fase 1 fechada sem duvida bloqueante: unit, chamadas e fixtures mapeados; touches confirmado; identidade de successor/transfer fechada pela precedencia do convite (GAP-13)
- 2026-11-09 02:05:29 | dev | dominio: campos SuccessorActorId/OwnerLastActiveAt/DeletedAt em Container, SuccessionRules (ClaimAllowed, Transfer) e regras de soft delete (RecoverableUntil, IsSearchable)
- 2026-11-09 02:05:29 | dev | rotas da secao G em ContainerEndpoints (PUT /successor, POST /claim, POST /transfer, DELETE com ?confirm exato) + override do operador POST /api/admin/containers/{slug}/transfer em AdminEndpoints; troca compartilhada em Ownership.MoveAsync
- 2026-11-09 02:05:29 | dev | AC-07: CanEnterAsync admite o Invited e o fluxo Open/Request em cont nao-publico na rota de join; Invite sem convite segue 404 byte-identico (pins da QA-0018 verdes)
- 2026-11-09 02:05:29 | dev | AC-08: recusas 403 (ban do Owner) e 409 last_owner (demote) gravam Deny/ChangeRole com a PolicyVersion vigente; 422/400 de validacao continuam sem linha
- 2026-11-09 02:05:29 | dev | GAP-17: TimeProvider.System registrado no composition root com seam Build(args, configure); Containers:SuccessorInactivityDays=90 e Containers:DeleteGraceDays=30 como default em codigo, lidos da config
- 2026-11-09 02:05:29 | dev | gate build -> exit 0 com 0 erros; build dos testes passou por 2 erros de compilacao (anon type misturando = e : em AdminOverrideAndConfiguredTimersTests.cs:67; List<string> em SuccessionHost.cs:52), ambos corrigidos e rebuild verde
- 2026-11-09 02:05:29 | dev | gate test (1a tentativa pos-codigo) -> exit 1, 227/228: falha no meu assert Single que contava tambem a linha Allow do PUT /successor - assert corrigido para filtrar pelo autor
- 2026-11-09 02:05:29 | dev | gate test apos a correcao -> exit 0, 228/228 (216 base + 12 novos)
- 2026-11-09 02:05:29 | dev | escopo: 12 arquivos escritos nesta rodada, todos sob touches (listagem por LastWriteTime >= baseline, sem bin/obj); os 3 logs em runs/test-runs/ sao gravados pelo proprio sdd-test.ps1
- 2026-11-09 02:05:33 | odev | ODEV carimbou as 11 logLines e as 8 evidenceRows do DEV com o relogio do fluxo (DEV nao tocou o arquivo da task, conforme devolvido no step result)
- 2026-11-09 02:05:33 | odev | ODEV verificou antes de transicionar: gate final 20261109-020041-test-gate.log lido no arquivo (exit: 0, Aprovado 228, Com falha 0 - S-22); residuos QA-MUTATION em backend = 0; git status = so toques + docs de fluxo; check exit 0; step result DONE aceito - nenhum duvida, 8 ACs; decisoes de escopo repassadas a QA: claim/transfer criam linha alvo Owner+Active, DELETE = visibilidade capacidade confirm soft-delete com {slug, deletedAt, recoverableUntil}, gate do /api/admin segue sem linha na trilha (precedente da rota de auditoria), OwnerLastActiveAt renova so com escrita do Owner
- 2026-11-09 02:05:36 | odev | transicao IN_DEVELOPMENT -> AWAITING_QA; entrega 5 de 6 da P-03 (AC-07 S-30 e AC-08 F2 entregues junto)
- 2026-11-09 02:58:05 | qa | QA-0020 attempt 1/2: baseline gate 228/228 lido no log 20261109-021141 (exit: 0 dentro do log - S-22) antes de escrever qualquer coisa; 7 testes novos em backend/tests/Squid.Tests/Containers/Qa0021SuccessionEntryAndTrailEdgeTests.cs, dentro dos touches
- 2026-11-09 02:58:05 | qa | cobertura: AC-01..AC-05 na classe Qa0021SectionGEdgeTests (SuccessionHostFixture com TimeProvider controlado), AC-07 e AC-08 na Qa0021EntryAndTrailEdgeTests (AuthHostFixture), AC-06 = gate + escopo; nenhum AC sem cenario
- 2026-11-09 02:58:05 | qa | filtros verdes: Qa0021 7/7 (20261109-023308), pinas da QA-0018 8/8 (023410), area Squid.Tests.Containers 61/61 (023540); as duas vermelhas intermediarias (022858 CS0103, 023028 conta do operador 409) eram do proprio teste da QA em construcao e foram corrigidas no teste, nunca em produto
- 2026-11-09 02:58:05 | qa | 13 ciclos de mutacao M1..M12 todos RESULT: PROVEN (S-16 SHA256 PRE, vermelho classificado pelo log gravado, restore byte-exact + mtime S-23, hash = PRE, verde em seguida) - 19 testes vermelhos de proposito; residuo QA-MUTATION em backend = 0
- 2026-11-09 02:58:05 | qa | gate final 235/235 (228 baseline + 7 da QA) com exit: 0 lido dentro do log 20261109-025153; git do fim = baseline das 02:11 + exatamente 4 artefatos da QA; varredura de mtime desde 02:06 = 5 arquivos, todos sob os touches
- 2026-11-09 02:58:05 | qa | veredito PASSED: 8 ACs com prova executada, nenhum BLOCKER e nenhuma falha pre-existente (baseline 228 verde); achados F1 (o move de propriedade forca Active na linha-alvo) e F2 (escrita de moderacao do Owner nao renova o relogio - estacionado pelo ODEV) sao INFO; suite em Linux unverified (WSL sem dotnet/pwsh nesta sessao)
- 2026-11-09 03:06:15 | qa | checkers: qa-check-task-0021.ps1 e o gemeo .sh fecham 14/14 nos dois sistemas; a 1a execucao do proprio checker acusou o limiar errado de MTIME-SWEEP (5 vs 4 arquivos) e foi corrigido nele; self-test do checker: limiar 235 -> 9999 de proposito deu exit 1 (RESULT: FAIL) e restaurado voltou a exit 0 com SHA256 = PRE
- 2026-11-09 03:11:05 | odev | ODEV verificou antes de transicionar: QA-0020-sucessao-transferencia-exclusao.md existe; gate final 20261109-025153-test-gate.log lido no arquivo (exit: 0, Aprovado 235, Com falha 0 - S-22); residuos QA-MUTATION em backend = 0; check exit 0 (log 24, evidencias 39); veredito aceito - 8/8 ACs, 13 mutacoes can-fail vermelhas e restauradas byte-exact, pinas da QA-0018 8/8 verdes, checkers ps1 e sh 14/14, propriedade never-orphan de 8 passos provada
- 2026-11-09 03:11:06 | odev | achados INFO da QA adjudicados: F1 (transfer força State=Active na linha-alvo) e F2 (moderação do Owner não renova OwnerLastActiveAt) viram S-31 no backlog com a decisao em vigor registrada - atividade = escrita no agregado, transfer ativa o alvo (ambos coerentes com invariants do data-model 2.2, nenhum AC dependia); flags MUDANCA_RELEVANTE CONTRATO (rotas da secao G) e PADRAO (TimeProvider injetavel com seam Build) ficam para a consolidacao
- 2026-11-09 03:11:06 | odev | transicao AWAITING_QA -> DONE; TASK-0021 = entrega 5 de 6 da P-03 encerrada (QA-0020 PASSED); cadeia libera a TASK-0022, ultima da fase

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-09 02:05:29 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261109-010716-test-gate.log (Aprovado 216/216 - baseline) |
| 2026-11-09 02:05:30 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 0 | 0 Aviso(s), 0 Erro(s) - build do produto e rebuild final |
| 2026-11-09 02:05:30 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 1 | CS1513/CS1026 em AdminOverrideAndConfiguredTimersTests.cs:67 (anon type com : no meio de =) - corrigido |
| 2026-11-09 02:05:31 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 1 | CS1503 em SuccessionHost.cs:52 (List<string> -> string[]) - corrigido |
| 2026-11-09 02:05:31 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 1 | docs/sdd/runs/test-runs/20261109-015704-test-gate.log (Com falha: 1, Aprovado: 227, Total: 228 - falha no assert Single do proprio DEV) |
| 2026-11-09 02:05:32 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261109-020041-test-gate.log (Aprovado 228/228) |
| 2026-11-09 02:05:32 | dev | git status --porcelain | 0 | arvore com mudancas pre-existentes e nao commitadas das TASK-0017..0020 (fora desta rodada) |
| 2026-11-09 02:05:32 | dev | Get-ChildItem -Recurse (LastWriteTime >= 01:07, sem bin/obj) | 0 | 12 arquivos escritos, todos sob os globs de touches + 3 logs do proprio gate |
| 2026-11-09 02:05:36 | odev | Select-String do gate final + varredura QA-MUTATION em backend + git status + sdd-state check | 0 | docs/sdd/runs/test-runs/20261109-020041-test-gate.log exit: 0 / Aprovado 228 / Com falha 0; residuos 0; toques fora NENHUM; check OK |
| 2026-11-09 02:58:45 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261109-021141-test-gate.log (exit: 0, Com falha 0, Aprovado 228 - baseline da QA) |
| 2026-11-09 02:58:45 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Qa0021 | 1 | docs/sdd/runs/test-runs/20261109-022858-test-filter.log (error CS0103 SetJoinAsync: o teste da QA ainda nao compilava - nao e falha de produto) |
| 2026-11-09 02:58:46 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Qa0021 | 1 | docs/sdd/runs/test-runs/20261109-023028-test-filter.log (Com falha 1, Aprovado 6: conta do operador 409 no host compartilhado - teste da QA corrigido com register-or-login) |
| 2026-11-09 02:58:46 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Qa0021 | 0 | docs/sdd/runs/test-runs/20261109-023308-test-filter.log (Com falha 0, Aprovado 7 - os 7 testes da QA) |
| 2026-11-09 02:58:46 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter (FullyQualifiedName~Qa0018)+(FullyQualifiedName~MembershipRoutes) | 0 | docs/sdd/runs/test-runs/20261109-023410-test-filter.log (Com falha 0, Aprovado 8 - pinas da QA-0018; uniao dos dois termos do vstest) |
| 2026-11-09 02:58:47 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Containers | 0 | docs/sdd/runs/test-runs/20261109-023540-test-filter.log (Com falha 0, Aprovado 61 - area da task) |
| 2026-11-09 02:58:47 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0019.ps1 -File backend/src/Squid.Domain/Containers/SuccessionRules.cs -Old 'now >= ownerLastActiveAt.AddDays(inactivityDays)' -New 'now >= ownerLastActiveAt' -Filter FullyQualifiedName~Qa0021SectionGEdgeTests | 0 | docs/sdd/runs/test-runs/20261109-023716-test-filter.log (RED exit: 1, Com falha 2 (AC-01 e AC-05); HASH-MATCH True com PRE de M1; GREEN 20261109-023738 exit: 0) |
| 2026-11-09 02:58:48 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0019.ps1 -File backend/src/Squid.Api/Containers/ContainerEndpoints.cs -Old 'OwnerLastActiveAt = ownerWriting ? time.GetUtcNow() : row.OwnerLastActiveAt,' -New 'OwnerLastActiveAt = ownerWriting && false ? time.GetUtcNow() : row.OwnerLastActiveAt,' -Filter FullyQualifiedName~Qa0021SectionGEdgeTests | 0 | docs/sdd/runs/test-runs/20261109-023800-test-filter.log (RED exit: 1, Com falha 1 (AC-01); HASH-MATCH True com PRE de M2; GREEN 20261109-023820 exit: 0) |
| 2026-11-09 02:58:48 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0019.ps1 -File backend/src/Squid.Api/Containers/ContainerEndpoints.cs -Old 'OwnerLastActiveAt = container.OwnerActorId == ActorId(context, current.Handle)' -New 'OwnerLastActiveAt = container.OwnerActorId is null' -Filter FullyQualifiedName~Qa0021SectionGEdgeTests | 0 | docs/sdd/runs/test-runs/20261109-023845-test-filter.log (RED exit: 1, Com falha 1 (AC-01); HASH-MATCH True com PRE de M3; GREEN 20261109-023908 exit: 0) |
| 2026-11-09 02:58:49 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0019.ps1 -File backend/src/Squid.Domain/Containers/SuccessionRules.cs -Old 'target with { Role = MembershipRole.Owner, State = MembershipState.Active }' -New 'target with { Role = MembershipRole.Owner, State = MembershipState.Silenced }' -Filter FullyQualifiedName~Qa0021SectionGEdgeTests | 0 | docs/sdd/runs/test-runs/20261109-023929-test-filter.log (RED exit: 1, Com falha 2 (AC-02 e AC-05); HASH-MATCH True com PRE de M5; GREEN 20261109-023949 exit: 0) |
| 2026-11-09 02:58:49 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0019.ps1 -File backend/src/Squid.Api/Admin/AdminEndpoints.cs -Old 'if (string.Equals(candidate, handle, StringComparison.OrdinalIgnoreCase)) return true;' -New 'if (string.Equals(candidate, handle, StringComparison.OrdinalIgnoreCase)) return false;' -Filter FullyQualifiedName~Qa0021SectionGEdgeTests | 0 | docs/sdd/runs/test-runs/20261109-024119-test-filter.log (RED exit: 1, Com falha 2 (AC-02 e AC-05); HASH-MATCH True com PRE de M4; GREEN 20261109-024139 exit: 0) |
| 2026-11-09 02:58:50 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0019.ps1 -File backend/src/Squid.Api/Containers/ContainerEndpoints.cs -Old 'ToString(), slug, StringComparison.Ordinal))' -New 'ToString(), slug + slug, StringComparison.Ordinal))' -Filter FullyQualifiedName~Qa0021SectionGEdgeTests | 0 | docs/sdd/runs/test-runs/20261109-024201-test-filter.log (RED exit: 1, Com falha 2 (AC-04 e AC-05); HASH-MATCH True com PRE de M6; GREEN 20261109-024222 exit: 0) |
| 2026-11-09 02:58:50 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0019.ps1 -File backend/src/Squid.Api/Containers/ContainerEndpoints.cs -Old 'PolicyAudit.Delete, container.Id,' -New 'PolicyAudit.View, container.Id,' -Filter FullyQualifiedName~Qa0021SectionGEdgeTests | 0 | docs/sdd/runs/test-runs/20261109-024242-test-filter.log (RED exit: 1, Com falha 1 (AC-04); HASH-MATCH True com PRE de M7; GREEN 20261109-024304 exit: 0) |
| 2026-11-09 02:58:51 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0019.ps1 -File backend/src/Squid.Domain/Containers/SuccessionRules.cs -Old 'rows.Add(target is null' -New 'rows.Add(false && target is null' -Filter FullyQualifiedName~Qa0021SectionGEdgeTests | 0 | docs/sdd/runs/test-runs/20261109-024324-test-filter.log (RED exit: 1, Com falha 3 (AC-03, AC-01 e AC-05); HASH-MATCH True com PRE de M12; GREEN 20261109-024344 exit: 0) |
| 2026-11-09 02:58:51 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0019.ps1 -File backend/src/Squid.Api/Containers/ContainerEndpoints.cs -Old 'if (membership?.State is MembershipState.Active or MembershipState.Silenced or MembershipState.Invited)' -New 'if (membership?.State is MembershipState.Active or MembershipState.Silenced)' -Filter FullyQualifiedName~Qa0021EntryAndTrailEdgeTests | 0 | docs/sdd/runs/test-runs/20261109-024422-test-filter.log (RED exit: 1, Com falha 1 (AC-07); HASH-MATCH True com PRE de M8; GREEN 20261109-024440 exit: 0) |
| 2026-11-09 02:58:52 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0019.ps1 -File backend/src/Squid.Api/Containers/ContainerEndpoints.cs -Old 'if (container.Policy.Visibility == VisibilityPolicy.Public) return true;' -New 'if (container.Policy.Visibility is VisibilityPolicy.Public or VisibilityPolicy.MembersOnly) return true;' -Filter FullyQualifiedName~Qa0021EntryAndTrailEdgeTests | 0 | docs/sdd/runs/test-runs/20261109-024455-test-filter.log (RED exit: 1, Com falha 1 (AC-07); HASH-MATCH True com PRE de M9; GREEN 20261109-024513 exit: 0) |
| 2026-11-09 02:58:52 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0019.ps1 -File backend/src/Squid.Api/Containers/ContainerEndpoints.cs -Old 'if (change is MembershipChange.Ban && target.Role == MembershipRole.Owner)' -New 'if (change is MembershipChange.Unsilence && target.Role == MembershipRole.Owner)' -Filter FullyQualifiedName~Qa0021EntryAndTrailEdgeTests | 0 | docs/sdd/runs/test-runs/20261109-024531-test-filter.log (RED exit: 1, Com falha 1 (AC-08); HASH-MATCH True com PRE de M10a; GREEN 20261109-024549 exit: 0) |
| 2026-11-09 02:58:53 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0019.ps1 -File backend/src/Squid.Api/Containers/ContainerEndpoints.cs -Old 'if (target.Role == MembershipRole.Owner)' -New 'if (target.Role == MembershipRole.Guest)' -Filter FullyQualifiedName~Qa0021EntryAndTrailEdgeTests | 0 | docs/sdd/runs/test-runs/20261109-024607-test-filter.log (RED exit: 1, Com falha 1 (AC-08); HASH-MATCH True com PRE de M10b; GREEN 20261109-024626 exit: 0) |
| 2026-11-09 02:58:53 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0019.ps1 -File backend/src/Squid.Api/Containers/ContainerEndpoints.cs -Old 'if ((change is MembershipChange.Ban or MembershipChange.Unban) && string.IsNullOrEmpty(justification))' -New 'if ((change is MembershipChange.Ban or MembershipChange.Unban) && justification is null)' -Filter FullyQualifiedName~Qa0021EntryAndTrailEdgeTests | 0 | docs/sdd/runs/test-runs/20261109-024644-test-filter.log (RED exit: 1, Com falha 1 (AC-08); HASH-MATCH True com PRE de M11; GREEN 20261109-024702 exit: 0) |
| 2026-11-09 02:58:53 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Qa0021 | 0 | docs/sdd/runs/test-runs/20261109-024806-test-filter.log (Com falha 0, Aprovado 7 - verde final pos-restauracao) |
| 2026-11-09 02:58:54 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261109-025153-test-gate.log (exit: 0, Com falha 0, Aprovado 235 - gate final, AC-06) |
| 2026-11-09 02:58:54 | qa | wsl -e sh -lc command -v dotnet e command -v pwsh e command -v bash | 0 | stdout do probe: dotnet:ABSENT, pwsh:ABSENT, bash:ok - suite em Linux unverified nesta sessao |
| 2026-11-09 03:04:36 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0021.ps1 (1a execucao) | 1 | stdout: CHECK MTIME-SWEEP FAIL (esperava 5 arquivos apos 02:06, eram 4 - o proprio checker tinha o IPolicyAuditStore que nenhum ciclo mutou); SUMMARY total=14 fails=1 RESULT: FAIL - checker corrigido |
| 2026-11-09 03:04:37 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0021.ps1 (apos o checker corrigir a lista de mutados: 3 arquivos + teste = 4) | 0 | stdout: SUMMARY total=14 fails=0 RESULT: PASS (14/14) |
| 2026-11-09 03:04:37 | qa | wsl -e bash -c cd /mnt/c/dev/squid-squad-net/squid-execution e bash docs/sdd/qa/qa-check-task-0021.sh | 0 | stdout no WSL: SUMMARY total=14 fails=0 RESULT: PASS (14/14, gêmeo Linux) |
| 2026-11-09 03:04:38 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0021.ps1 (self-test AC-06: limiar GATE-NEWEST-SUMMARY 235 trocado por 9999 de proposito) | 1 | stdout: CHECK GATE-NEWEST-SUMMARY FAIL ... SUMMARY total=14 fails=1 RESULT: FAIL - o checker sabe falhar |
| 2026-11-09 03:04:38 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0021.ps1 (limiar restaurado 9999 para 235) | 0 | stdout: SUMMARY total=14 fails=0 RESULT: PASS; SHA256 6B1A6080625F201601F57DF4D567FFE92EF4010D47430B554F7E685FBAC3277B igual ao PRE |
| 2026-11-09 03:06:16 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0021.ps1 (execucao definitiva pos-appends) | 0 | stdout: SUMMARY total=14 fails=0 RESULT: PASS (14/14, Windows) |
| 2026-11-09 03:06:16 | qa | wsl -e bash -c cd /mnt/c/dev/squid-squad-net/squid-execution e bash docs/sdd/qa/qa-check-task-0021.sh (execucao definitiva pos-appends) | 0 | stdout no WSL: SUMMARY total=14 fails=0 RESULT: PASS (14/14, Linux) |
| 2026-11-09 03:06:17 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-state.ps1 check docs/sdd/tasks/TASK-0021-sucessao-transferencia-exclusao.md | 0 | stdout: check: OK - log entries 23, evidence rows 36 |
| 2026-11-09 03:11:07 | odev | Select-String do gate final + varredura QA-MUTATION em backend + sdd-state check | 0 | docs/sdd/runs/test-runs/20261109-025153-test-gate.log exit: 0 / Aprovado 235 / Com falha 0; residuos 0; check OK log 24 evidencias 39; QA-0020 presente |
