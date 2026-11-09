<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0018 — Política versionada (If-Match), trilha de auditoria e operador

```yaml
runId: R-20261108-01
taskId: TASK-0018
status: DONE
agent: odev
phase: P-03
sequence: 2
createdAt: 2026-11-08 11:09:06
updatedAt: 2026-11-08 18:27:23
parent: SPEC-0008
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Api/Containers/**", "backend/src/Squid.Api/Admin/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/Containers/**"]
blocks: ["TASK-0019"]
blockedBy: ["TASK-0017"]
inputArtifacts: ["docs/sdd/specs/SPEC-0008-containers-sociais-p03.md", "specs/001-squid-mvp/data-model.md", "specs/001-squid-mvp/contracts/http-api.md"]
outputArtifacts: ["backend/src/Squid.Api/Containers/**", "backend/src/Squid.Api/Admin/**", "backend/src/Squid.Service/**"]
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
nextAction: DONE - QA-0017 PASSED (6/6 ACs, gate 188/188, 6 mutacoes can-fail); proxima entrega TASK-0019
evidence: []
memoryBankFlags: []
```

## Intent

Fecha FR-013 (parte de política) e FR-019/SC-011: `PUT /api/containers/{slug}/policy` com
`If-Match`, trilha `PolicyDecision` append-only gravando Allow/Deny/NotFound de toda decisão
executada nas rotas, `GET /api/admin/audit` e o operador mínimo (`Instance:Operators`, GAP-14)
que essas rotas exigem.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `PUT /api/containers/{slug}/policy` autenticado por `Admin`+ com `If-Match: <n>` e
  payload válido devolve 200 com `version: n+1`, substitui a política (o `GET` mostra a nova)
  e grava auditoria com autor, antes, depois e versão nova — teste.
- AC-02: `If-Match` com versão defasada → `409 version_conflict` com a versão atual no corpo e
  **nada alterado** (hash do estado antes/depois); `PUT` sem `If-Match` →
  `428 precondition_required` (§9.2, GAP-16) — teste.
- AC-03: toda decisão de política executada nas rotas vira linha append-only na trilha com
  `At`, `ActorId`, `Action`, `ResourceId`, `Result`, `PolicyVersion`, `Reason` — Allow de leitura
  concedida **e** Deny/NotFound de leitura negada gravam (FR-019, SC-011) — teste dos três
  resultados.
- AC-04: `GET /api/admin/audit?actor=&action=&from=` devolve a trilha com os filtros
  funcionando e exige operador: sem sessão → 401, sessão de não-operador → `403 policy_denied`,
  operador definido em `Instance:Operators` → 200 (FR-008 mínimo, GAP-14) — teste.
- AC-05: `PUT /policy` sem sessão → 401; por `Member` (não Admin) → negado com a política
  inalterada — teste.
- AC-06: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`
  exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Api/Containers/**` (interface da trilha + `PUT /policy` + `GET /members` segue para a 0019)
- `backend/src/Squid.Api/Admin/**` (novo diretório: guard de operador + `GET /api/admin/audit`)
- `backend/src/Squid.Service/**` (store da trilha + config `Instance:Operators` + registro)
- `backend/tests/Squid.Tests/Containers/**`

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-14 | no | operador = `Instance:Operators` (lista de handles na config); sem rota de gestão |
| GAP-16 | no | sem `If-Match` → `428 precondition_required` |
| — | no | a trilha desta fase registra as decisões das rotas de contêiner; as demais rotas `/api/admin/*` do §7 pertencem às fases donas |
| — | no | ordenação da auditoria: paginação por `At` decrescente com desempate estável (id/linha) — assume-se estável para não repetir nem perder item |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-08 11:16:34 | odev | task created from SPEC-0008 (entrega 2 de 6; blockedBy TASK-0017 por toques compartilhados em Squid.Api/Containers e Squid.Service)
- 2026-11-08 13:22:19 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; blockedBy TASK-0017 resolve (DONE com QA-0016 PASSED, gate 181/181); baseline do gate e 181/181
- 2026-11-08 14:25:50 | dev | feasibility: baseline do gate 181/181 exit 0 antes de qualquer edicao; touches confirmado e completo (Squid.Api/Containers, Squid.Api/Admin, Squid.Service, tests/Squid.Tests/Containers) - nenhum toque fora; TASK-0019 segue bloqueada por este
- 2026-11-08 14:25:50 | dev | duvida D-01 (Action da trilha para a troca de politica) fechada pela SPEC-0008 C: a espec fixa o dominio de Result como Allow, Deny e NotFound e deixa Action em aberto - adotado ChangePolicy para PUT /policy e View para leitura; antes e depois no Reason em JSON porque o data-model 2.11 tem so 7 campos
- 2026-11-08 14:25:50 | dev | criados backend/src/Squid.Api/Containers/IPolicyAuditStore.cs (entrada, filtro e interface da trilha), backend/src/Squid.Api/Admin/AdminEndpoints.cs (guard de operador Instance:Operators + GET /api/admin/audit) e backend/src/Squid.Service/SqlitePolicyAuditStore.cs (append e query sobre IGrainStorage, sem tabela nova)
- 2026-11-08 14:25:50 | dev | PUT /api/containers/{slug}/policy em ContainerEndpoints: 401 sem sessao, 404 protegido, 403 decidido por IPolicyEvaluator (PolicyAction.Admin, sem if de papel), 428 sem If-Match, 400 payload, 409 com a versao atual no corpo e nada alterado, 200 com policy.version n+1; toda decisao executada nas rotas de conteneer grava linha na trilha
- 2026-11-08 14:25:50 | dev | registrados IPolicyEvaluator, IPolicyAuditStore e MapSquidAdmin em SquidServiceHost; ContainerView.Policy extraido para o before/after da auditoria; a linha Origin do https do ContainerEndpoints ficou intacta
- 2026-11-08 14:25:50 | dev | testes novos: 5 de rota (AC-01..AC-05) + 2 de store em tests/Squid.Tests/Containers; gate final 188/188 exit 0 em docs/sdd/runs/test-runs/20261108-142320-test-gate.log
- 2026-11-08 15:59:19 | odev | ODEV verificou antes de transicionar: gate final 20261108-142320-test-gate.log lido no arquivo (exit: 0, Aprovado 188, Com falha 0 - S-22); git status = so toques desta task (IPolicyAuditStore, ContainerEndpoints, ContainerView, AdminEndpoints, SqlitePolicyAuditStore, SquidServiceHost, tests/Containers) + docs de fluxo; check exit 0 (log 8, evidencias 4); step result DONE aceito - D-01 (Action ChangePolicy fora da enum do data-model 2.11) fechada no nivel 1 pela SPEC-0008 C, mas toca o MODELO DE DADOS adotado: vira pergunta ao humano com default antes da consolidacao; flag MUDANCA_RELEVANTE CONTRATO registrada, PADRAO_NOVO (trilha em documento unico IGrainStorage Append+Query) candidata a consolidacao; unverified do ramo defensivo do guard (id de ator na config) aceito
- 2026-11-08 17:40:25 | odev | gate humano 2026-11-08 17:38:56 (Henrique): emenda ao data-model 2.11 aceitando ChangePolicy como valor do enum Action da trilha - flag MUDANCA_RELEVANTE CONTRATO da D-01 resolvida na escada ate o humano (especificacao deixou Action em aberto, requisito FR-019 nao enumera, data-model enumera 7 valores); original mantido sem reescrita, emenda registrada em plan.md e vira contracts.md na consolidacao da fase; codigo permanece como esta, sem retrabalho
- 2026-11-08 17:40:25 | odev | QA-0017 subagent FALHOU antes de fechar (harness: terminou sem mensagem de fechamento e sem step result; nada escrito - check segue log 9 / evidencias 5); varredura da arvore apos a falha: 0 residuos QA-MUTATION em backend, nenhum artefato Qa0017 nem qa-check-task-0018, git status sem nada fora dos toques, backups qa0013-* no TEMP sao do suite antigo de QA-0013 e nao desta sessao - a falha nao deixou dano
- 2026-11-08 17:40:26 | odev | politica de falha: 1 redispatch da mesma task (subagente nova, qaAttempt segue 1/2); a nova QA recebe a instrucao de nao reexecutar trabalho ja concluido por nada - nao ha veredito anterior, entao ela comeca do zero
- 2026-11-08 18:21:10 | qa | QA-0017 dispatch attempt 1/2: control block AWAITING_QA lido, AC-01..AC-06 mapeados nos 7 testes novos do DEV; gate inicial 20261108-175107 exit: 0 Com falha 0 Aprovado 188 (lido no log, S-22)
- 2026-11-08 18:21:11 | qa | 6 mutacoes can-fail executadas (M1 428, M2 gate do operador, M3 linha Deny, M4 incremento de versao, M5 403 do Member, M6 desempate da loja), cada uma com SHA256 PRE=POST (S-16) e bump de mtime (S-23) e verde 7/7 em seguida; filtro baseline 7/7
- 2026-11-08 18:21:11 | qa | gate final 20261108-180433 exit: 0 Com falha 0 Aprovado 188 na arvore restaurada; AC-06: git status identico ao baseline com so os 3 artefatos QA novos, mtime apos 15:59:17 = so os 3 arquivos mutados dentro dos touches, QA-MUTATION em backend = 0
- 2026-11-08 18:21:12 | qa | verdito PASSED em docs/sdd/qa/QA-0017-politica-auditoria-operador.md; checks qa-check-task-0018.ps1 e .sh 9/9 exit 0 (o .sh teve 1 FAIL proprio por data errada no sweep, corrigido); unverified: ramo de id de ator do guard e suite em Linux (WSL sem dotnet/pwsh)
- 2026-11-08 18:27:24 | odev | ODEV verificou antes de transicionar: QA-0017-politica-auditoria-operador.md CONCLUIDO com verdicto PASSED; gate final 20261108-180433-test-gate.log lido no arquivo (exit: 0, Aprovado 188, Com falha 0 - S-22); residuos QA-MUTATION em backend = 0; git status = so toques + artefatos de QA (qa-check-task-0018.ps1/.sh) + docs de fluxo; check exit 0 (log 16, evidencias 31); veredito aceito - 6/6 ACs, 6 mutacoes can-fail vermelhas e restauradas byte-exact com bump mtime, checkers ps1 e sh 9/9, primeira subagente falhou sem dano e a redispatchada refez do zero
- 2026-11-08 18:27:25 | odev | flags e adjudicacoes finais desta task: MUDANCA_RELEVANTE CONTRATO (ChangePolicy) RESOLVIDA por gate humano 2026-11-08 17:38:56 - emenda ao data-model 2.11 registrada em plan.md, vira memory-bank/contracts.md na consolidacao; PADRAO_NOVO (trilha append-only em documento unico do IGrainStorage, Append+Query) aceita como candidata e verificada contra o codigo no fecho da fase; achado novo: NENHUM (S-28 segue pre-existente, correcao agendada TASK-0019 AC-07); unverified aceitos (ramo defensivo do guard com id de ator; suite Linux - CI ubuntu e o executor)
- 2026-11-08 18:27:25 | odev | transicao AWAITING_QA -> DONE; TASK-0018 = entrega 2 de 6 da P-03 encerrada (QA-0017 PASSED); cadeia libera a TASK-0019 (AC-07 do gate scheme+Host incluido)

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-08 14:26:14 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261108-132534-test-gate.log (baseline 181/181) |
| 2026-11-08 14:26:14 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~PolicyAudit | 0 | docs/sdd/runs/test-runs/20261108-141642-test-filter.log (7/7 novos) |
| 2026-11-08 14:26:14 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 0 | backend/Squid.slnx (0 avisos, 0 erros) |
| 2026-11-08 14:26:15 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261108-142320-test-gate.log (188/188) |
| 2026-11-08 15:59:20 | odev | Select-String do gate final + git status + sdd-state check | 0 | docs/sdd/runs/test-runs/20261108-142320-test-gate.log exit: 0 / Aprovado 188 / Com falha 0; toques fora: NENHUM; check OK log 8 evidencias 4 |
| 2026-11-08 18:21:53 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261108-175107-test-gate.log - gate inicial exit: 0 Com falha 0 Aprovado 188 Total 188 (S-22) |
| 2026-11-08 18:21:53 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~PolicyAudit | 0 | docs/sdd/runs/test-runs/20261108-175150-test-filter.log - baseline dos 7 testes novos exit: 0 Com falha 0 Aprovado 7 |
| 2026-11-08 18:21:54 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~PolicyAudit | 0 | docs/sdd/runs/test-runs/20261108-175430-test-filter.log - 1a tentativa da M1 nao aplicou a mutacao (ReadAllText no CWD do processo) exit 0 7/7 - repetida com caminho absoluto |
| 2026-11-08 18:21:54 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~PolicyAudit | 1 | docs/sdd/runs/test-runs/20261108-175512-test-filter.log - M1 428 para 400 exit: 1 Com falha 1 - AC-02 PolicyAuditRoutesTests.cs:124 |
| 2026-11-08 18:21:55 | qa | Get-FileHash SHA256 dos arquivos mutados + bump de mtime (restauracao, S-16 e S-23) | 0 | POST=PRE D0704FD83FF00EABD74B22F47F2CE7E5ECFCD006B26DEDAE85FA27031F5C14D1 ContainerEndpoints.cs mtime 17:55 |
| 2026-11-08 18:21:55 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~PolicyAudit | 1 | docs/sdd/runs/test-runs/20261108-175636-test-filter.log - M2 gate do operador nega o operador exit: 1 Com falha 3 - AC-01:76 AC-03:154 AC-04:205 |
| 2026-11-08 18:21:56 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~PolicyAudit | 0 | docs/sdd/runs/test-runs/20261108-175654-test-filter.log - pos-M2 exit: 0 Aprovado 7 |
| 2026-11-08 18:21:56 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~PolicyAudit | 1 | docs/sdd/runs/test-runs/20261108-175840-test-filter.log - M3 linha Deny deixou de ser gravada exit: 1 Com falha 1 - AC-03:173 |
| 2026-11-08 18:21:57 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~PolicyAudit | 0 | docs/sdd/runs/test-runs/20261108-175858-test-filter.log - pos-M3 exit: 0 Aprovado 7 |
| 2026-11-08 18:21:57 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~PolicyAudit | 1 | docs/sdd/runs/test-runs/20261108-175937-test-filter.log - M4 versao sem incremento exit: 1 Com falha 2 - AC-01:56 e AC-02:111 |
| 2026-11-08 18:21:58 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~PolicyAudit | 0 | docs/sdd/runs/test-runs/20261108-175955-test-filter.log - pos-M4 exit: 0 Aprovado 7 |
| 2026-11-08 18:21:58 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~PolicyAudit | 1 | docs/sdd/runs/test-runs/20261108-180047-test-filter.log - M5 403 do Member vira 200 exit: 1 Com falha 2 - AC-03:151 e AC-05:259 |
| 2026-11-08 18:21:58 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~PolicyAudit | 0 | docs/sdd/runs/test-runs/20261108-180105-test-filter.log - pos-M5 exit: 0 Aprovado 7 |
| 2026-11-08 18:21:59 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~PolicyAudit | 1 | docs/sdd/runs/test-runs/20261108-180218-test-filter.log - M6 desempate da loja invertido exit: 1 Com falha 1 - PolicyAuditStoreTests.cs:39 |
| 2026-11-08 18:21:59 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~PolicyAudit | 0 | docs/sdd/runs/test-runs/20261108-180236-test-filter.log - pos-M6 exit: 0 Aprovado 7 |
| 2026-11-08 18:22:00 | qa | Get-FileHash SHA256 dos arquivos mutados + bump de mtime (restauracao, S-16 e S-23) | 0 | 3/3 POST=PRE: D0704FD83FF00EABD74B22F47F2CE7E5ECFCD006B26DEDAE85FA27031F5C14D1 ContainerEndpoints, 754C1B69FCEC797185A4440052D3D884797E85BEF2509B90D2DD025DB4806B3B AdminEndpoints, CE485D798383E5FE79D73CBBAF1847CB06BF4FD430DBF71CA09B2BB509C26BF7 SqlitePolicyAuditStore |
| 2026-11-08 18:22:00 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261108-180433-test-gate.log - gate final exit: 0 Com falha 0 Aprovado 188 Total 188 |
| 2026-11-08 18:22:01 | qa | git status --porcelain -uall + varredura de mtime desde 15:59:17 + Select-String QA-MUTATION em backend (escopo AC-06) | 0 | escopo identico ao baseline com so os 3 artefatos QA novos; mtime = so os 3 arquivos mutados dentro dos touches; residuos QA-MUTATION em backend = 0 |
| 2026-11-08 18:22:01 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0018.ps1 | 0 | SUMMARY total=9 fails=0 RESULT: PASS (gate, filtro, hash, residuo, touches, mtime, rotas) |
| 2026-11-08 18:22:02 | qa | bash docs/sdd/qa/qa-check-task-0018.sh (git bash) | 0 | SUMMARY total=9 fails=0 RESULT: PASS - 1a execucao falhou no MTIME-SWEEP por data errada 2026-01-08, corrigida para 2026-11-08 e reexecutada |
| 2026-11-08 18:22:02 | qa | self-test do qa-check-task-0018.ps1: limiar 188 trocado por 9999 e restaurado | 1 | quebra deliberada: SUMMARY total=9 fails=1 RESULT: FAIL exit 1; restaurado: fails=0 PASS exit 0 e arquivo byte-identico |
| 2026-11-08 18:22:03 | qa | wsl -e sh -lc probe de dotnet e pwsh (plataforma Linux) | 0 | dotnet ABSENT pwsh ABSENT - suite em Linux marcada unverified, instalar e gate |
| 2026-11-08 18:22:03 | qa | bash docs/sdd/_tools/sdd-test.sh test (git bash) | 1 | sdd-test: no *.sln found within depth 3 - a solucao e .slnx, wrapper sh pre-existente (TASK-0009) nao roda no repo |
| 2026-11-08 18:23:56 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0018.ps1 (execucao final) | 0 | SUMMARY total=9 fails=0 RESULT: PASS - estado final depois dos appends de Log e Evidence |
| 2026-11-08 18:23:57 | qa | bash docs/sdd/qa/qa-check-task-0018.sh (execucao final, git bash) | 0 | SUMMARY total=9 fails=0 RESULT: PASS - twin do check no shell bash, mesmo resultado do ps1 |
| 2026-11-08 18:23:57 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-state.ps1 check + varredura QA-MUTATION em backend + git status --porcelain -uall (estado final) | 0 | check OK log 16 evidencias 28 naquele momento; residuos QA-MUTATION em backend = 0; git status = baseline 27 + 3 artefatos QA |
| 2026-11-08 18:27:26 | odev | Select-String do gate final + varredura QA-MUTATION em backend + git status + sdd-state check | 0 | docs/sdd/runs/test-runs/20261108-180433-test-gate.log exit: 0 / Aprovado 188 / Com falha 0; residuos 0; check OK log 16 evidencias 31; QA-0017 presente |
