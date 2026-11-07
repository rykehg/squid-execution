<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0007 — ContainerPolicy, IPolicyEvaluator e testes da matriz no Domain

```yaml
runId: R-20261106-01
taskId: TASK-0007
status: DONE
agent: odev
phase: P-01
sequence: 2
createdAt: 2026-11-06 17:12:45
updatedAt: 2026-11-06 18:40:29
parent: SPEC-0003
authority: SPEC
parallel: true
touches: ["backend/src/Squid.Domain/**", "backend/tests/Squid.Tests/**"]
blocks: []
blockedBy: []
inputArtifacts: ["docs/features/05-arquitetura-consolidada.md", "specs/001-squid-mvp/data-model.md", "docs/sdd/specs/SPEC-0003-domain-container-policy.md"]
outputArtifacts: ["backend/src/Squid.Domain/ContainerPolicy.cs", "backend/src/Squid.Domain/IPolicyEvaluator.cs", "backend/tests/Squid.Tests/Domain/"]
interviewRounds: 0/6
feasibilityRounds: 0/2
correctionAttempts: 0/2
qaAttempts: 0/2
contextHandoffs: 0/4
progressDelta: 0
humanGate:
  approvedBy: Henrique
  approvedDate: 2026-11-06 18:40:29
  scope: F1 adjudicado: clausula de descoberta do AC-01 (nao listado/listada) FORA DO ESCOPO agora - Listed e campo de Container (data-model 2.2), FR-016 entra na P-03 com o ContainerGrain; AC-01 corrigido pelo ODEV com a correcao logada
  expiresAt: NENHUM
blocker: NENHUM
nextAction: DONE - QA-0006 PARTIAL resolvido pelo gate F1 (fora do escopo, AC-01 corrigido, lacuna FR-016 registrada para P-03); 6/6 ACs cobertas; proxima entrega da P-01: Containerfile+compose
evidence: []
memoryBankFlags: [MUDANCA_RELEVANTE: SEGURANCA - Guest negado em escrita mesmo com politica permissiva e SinglePublisher falha fechado so-papel; matriz 5x8 coberta - source: backend/src/Squid.Domain/IPolicyEvaluator.cs:82] [PADRAO_NOVO: PolicyDecision(Permitted, Reason, PolicyVersion) + IPolicyEvaluator como unico caminho de checagem de papel - source: backend/src/Squid.Domain/IPolicyEvaluator.cs:33]
```

## Intent

Item do Sprint 0 do doc 05: o motor de políticas é o coração do controle de acesso (§2.4) e a
base da P-03. `Squid.Domain` já existe no solution mas não tem código de produto — esta entrega
cria `ContainerPolicy` (registro versionado com as quatro políticas), `IPolicyEvaluator` com a
matriz papel × ação, e os presets de grupo/comunidade. Toda checagem futura de papel passa por
aqui (data-model §3.2, regra dura).

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `ContainerPolicy` existe como registro imutável com `Posting`, `Visibility`, `Join`,
  `Interaction` (comentário, reação, compartilhamento, anexo — cada um com o enum de Posting),
  `PublisherIds` e `Version`; presets de política de grupo (`membersOnly`/`invite`) e comunidade
  (`public`/`open|request`), ambos com postagem padrão `membersOnly` (FR-010, doc 05 §2.3).
  Corrigido em 2026-11-06 pelo humano (gate F1): a cláusula original "não listado/listada" saiu —
  `Listed` é campo de `Container` (data-model §2.2), não de `ContainerPolicy` (§2.3); descoberta
  é FR-016 e entra na P-03 com o `ContainerGrain` (SPEC-0003 §Out of scope).
- AC-02: `SinglePublisher` sem `PublisherIds` não vazio é rejeitado como política inválida
  (construir a política lança, não avalia em falso) — FR-015.
- AC-03: `IPolicyEvaluator.Evaluate(acao, papel, politica)` devolve `PolicyDecision` com
  permitido/negado, motivo e a `PolicyVersion` avaliada; nenhuma decisão silenciosa.
- AC-04: matriz de capacidades completa e testada: para cada papel (`Owner`, `Admin`,
  `Moderator`, `Member`, `Guest`) × cada ação (publicar, comentar, reagir, compartilhar, anexar,
  entrar, moderar, admin) o teste afirma o resultado esperado pela hierarquia estrita
  (FR-012, data-model §3.2) — `Guest` negado em toda escrita mesmo com política permissiva.
- AC-05: política permissiva não concede o que o papel não tem: `Guest` com `Posting=Everyone`
  continua negado para publicar (data-model §2.4) — caso de canto explícito no teste.
- AC-06: `Squid.Domain` continua sem nenhuma referência a Orleans, JSON-LD ou I/O (testes de
  arquitetura existentes permanecem verdes) e o gate completo `sdd-test.ps1 test` sai com
  exit 0 com os testes novos contando no total.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Domain/**`
- `backend/tests/Squid.Tests/**`

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-11 | no | `Evaluate` recebe o papel (`ContainerRole`), não o agregado `Actor` completo — o `Actor` da spec entra na P-03 com o grain |
| GAP-12 | no | nomes de enum seguem o data-model §2.3 (`Everyone..SinglePublisher`, `Public..Private`, `Open..Closed`); o doc 05 usa minúsculas na tabela §2.4, prevalece o data-model por ser o contrato de dados |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`

- 2026-11-06 17:12:45 | odev | task created from SPEC-0003; instância ODEV única confirmada pelo humano (fork do Telegram arquivado) — numeração TASK-0007 alocada por esta sessão
- 2026-11-06 17:17:52 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; sem blockedBy; parallel true (touches disjuntos de qualquer tarefa aberta); instancia ODEV unica confirmada pelo humano em 2026-11-06 17:12
- 2026-11-06 17:44:08 | dev | feasibilidade round 1: tarefa, SPEC-0003 e fontes lidas (doc 05 §2.4, data-model §2.3/2.4/3.2/3.3, FR-010..019); touches confirmado — só backend/src/Squid.Domain/** e backend/tests/Squid.Tests/**; TASK-0001..0006 DONE, nenhuma tarefa aberta em paralelo; baseline gate 42/42 exit 0
- 2026-11-06 17:44:08 | dev | D-01 resolved by SPEC-0003: SinglePublisher na trilha só-papel não confirma publisher (GAP-11) e falha fechada — regra de segurança da spec: negação é o default e caminho não mapeado nega; o caminho com Actor entra na P-03
- 2026-11-06 17:44:08 | dev | D-02 resolved by SPEC-0003: entrar não é ação de escrita — Guest entra com Join=Open|Request (FR-014, senão a entrada aberta não existe); Invite exige estado de Membership (P-03) e aqui falha fechado
- 2026-11-06 17:44:08 | dev | D-03 resolved by SPEC-0003: PolicyDecision do avaliador é o registro do AC-03 (permitido, motivo, PolicyVersion); a trilha de auditoria completa com Actor/ResourceId (data-model §2.11, FR-019) é P-03
- 2026-11-06 17:44:08 | dev | criados backend/src/Squid.Domain/ContainerPolicy.cs (enums de data-model §2.3 por GAP-12, InteractionPolicy, registro imutável validado no construtor, presets Group/Community, NextVersion) e backend/src/Squid.Domain/IPolicyEvaluator.cs (PolicyAction, ContainerRole, PolicyDecision, IPolicyEvaluator, PolicyEvaluator)
- 2026-11-06 17:44:08 | dev | criados backend/tests/Squid.Tests/Domain/ContainerPolicyTests.cs (6 facts: AC-01, AC-02, regra de incremento) e Domain/PolicyEvaluatorTests.cs (49 casos: AC-03, AC-04 matriz 40 células, AC-05, ramo SinglePublisher, Join, portas de interação, entradas inválidas); fast 84/84 exit 0
- 2026-11-06 17:44:08 | dev | gate completo powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test: 97/97 exit 0 (42 pré-existentes + 55 novos); testes de arquitetura do Domain verdes (AC-06)
- 2026-11-06 17:48:04 | odev | ODEV verificou antes de transicionar: check exit 0 (9 logs, 3 evidencias); gate final 97/97 exit 0 confirmado lendo cabecalho e resumo de docs/sdd/runs/test-runs/20261106-174304-test-gate.log; arvore so com os arquivos do touches (Squid.Domain/**, Squid.Tests/Domain/); nenhum leftover .logtest em docs/sdd/tasks
- 2026-11-06 17:48:04 | odev | D-01..D-03 do DEV adjudicados por ODEV: todos fechados na propria SPEC-0003 (nivel 1 da ladder) - falha fechada para SinglePublisher/Invite, entrar nao e escrita (FR-014), PolicyDecision do AC-03 basta; nenhum subiu para humano
- 2026-11-06 17:48:04 | odev | status IN_DEVELOPMENT -> AWAITING_QA
- 2026-11-06 18:17:44 | qa | QA attempt 1/2: entry via task file; status AWAITING_QA confirmed; SPEC-0003, data-model 2.3/2.4/3.2, doc 05 2.2/2.3/2.4 and FR-010..019 read; memory bank route qa-integracao read (index PARCIAL, presente)
- 2026-11-06 18:17:44 | qa | QA re-ran the full gate: sdd-test.ps1 test -> exit 0, 97/97 (42 pre-existing + 55 new), log 20261106-175244-test-gate.log; DEV claim verified against its own log 20261106-174304 (exit 0, 97/97) and baseline log 20261106-173120 (42/42)
- 2026-11-06 18:17:44 | qa | Domain filter baseline exit 0 55/55 (20261106-175844); static qa-check-task-0007.ps1 exit 0 with 26 PASS (GAP-12 enums equal data-model 2.3, GAP-11 signature, 40 matrix cells equal the hierarchy oracle, AC-06 scan clean, gate logs 97/97); Linux twin .sh under WSL exit 0 with 25 PASS
- 2026-11-06 18:17:44 | qa | mutation battery executed and restored byte-exact with SHA256 captured BEFORE breaking: M1 remove SinglePublisher ctor guard -> exit 1 (1 failure AC-02, 175956); M2 Guest passes on write -> exit 1 (7 failures: 5 Guest cells + AC-05 + AC-03, 180054); M3 Moderate weakened to Member -> exit 1 (1 failure cell Member/Moderate, 180213); restored hashes C6521ACF/94CDDFA4 match pre-mutation, QA-MUT markers 0
- 2026-11-06 18:17:44 | qa | final gate after all restores exit 0 97/97 (180400); Category=Architecture filter exit 0 17/17 (180612); QA report written to docs/sdd/qa/QA-0006-container-policy-evaluator.md; verdict PASSED with finding F1 (AC-01 discovery clause nao listado/listada has no code path here: FR-016 is P-03 per SPEC-0003 out-of-scope and data-model 2.3 has no discovery field - ODEV adjudicates) and F2 (static scan scope corrected: obj/GlobalUsings.g.cs is SDK-generated, product sources 0 hits)
- 2026-11-06 18:17:44 | qa | xunit suite execution on Linux remains unverified: WSL has no dotnet (dotnet-absent); only the static checks ran there
- 2026-11-06 18:22:57 | qa | correction to the previous log line (append-only, earlier line not edited): final verdict is PARTIAL, not PASSED - every executable scenario is green (gate 97/97, 3 mutations red then restored byte-exact, static checks green), but AC-01 discovery clause (nao listado/listada) has no reachable code path here (FR-016 is P-03 per SPEC-0003, data-model 2.3 has no field) so that clause is NOT-COVERED and goes back to ODEV as finding F1 of docs/sdd/qa/QA-0006-container-policy-evaluator.md
- 2026-11-06 18:32:02 | odev | verdicto QA-0006 registrado: PARTIAL (qaAttempt 1/2) - 5 de 6 ACs PASS com provas executadas (gate 97/97 tres vezes, 3 mutacoes vermelhas restauradas byte-exact SHA256 capturado antes, oraculo independente da matriz 40 celulas 0 divergencias, arquitetura 17/17); AC-01 PARTIAL so na clausula de descoberta (F1)
- 2026-11-06 18:32:02 | odev | F1 adjudicado na ladder spec: causa e redacao do ODEV no AC-01 - copiei a linha de descoberta da tabela 2.3 do doc 05 para ContainerPolicy, mas o data-model adotado coloca Listed em Container (2.2, preset Group=false/Community=true) e a SPEC-0003 ja poe FR-016/descoberta fora do escopo (P-03); o DEV nao tem o que corrigir - mudar o AC apos teste e gate humano, entao sobe para o humano
- 2026-11-06 18:32:02 | odev | F2/INFO aceito: o primeiro scan estatico pegou obj/GlobalUsings.g.cs gerado pelo SDK; escopo corrigido para fontes de produto, sem defeito de produto
- 2026-11-06 18:32:02 | odev | status AWAITING_QA -> AWAITING_HUMAN com humanGate.scope = F1 e expiresAt 2026-11-13 18:30
- 2026-11-06 18:40:34 | odev | humano respondeu F1: FORA DO ESCOPO AGORA (2026-11-06, gate registrado com approvedBy Henrique, approvedDate 2026-11-06 18:40:29, expiresAt NENHUM)
- 2026-11-06 18:40:34 | odev | ODEV corrigiu o AC-01 (clausula de descoberta removida, correcao explicita no proprio AC, nada editado em linha de log/evidencia antiga); lacuna FR-016/descoberta registrada para a P-03 na SPEC-0003 §Out of scope
- 2026-11-06 18:40:34 | odev | status AWAITING_HUMAN -> DONE; veredito QA-0006 PARTIAL resolvido: as 5/6 ACs executadas verdes continuam validas, a 6a clausula saiu do escopo por decisao humana - 6/6 cobertas

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-06 17:44:23 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261106-173120-test-gate.log (baseline 42/42) |
| 2026-11-06 17:44:24 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -Fast | 0 | docs/sdd/runs/test-runs/20261106-174155-test-fast.log (84/84) |
| 2026-11-06 17:44:24 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261106-174304-test-gate.log (97/97 com55 novos) |
| 2026-11-06 18:18:11 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261106-175244-test-gate.log (QA re-run: 97/97, 42+55) |
| 2026-11-06 18:18:11 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Domain | 0 | docs/sdd/runs/test-runs/20261106-175844-test-filter.log (baseline dos 55 novos: 55/55) |
| 2026-11-06 18:18:12 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0007.ps1 | 0 | docs/sdd/qa/qa-check-task-0007.ps1 (26 PASS, RESULT: PASS - GAP-12, GAP-11, oraculo da matriz, AC-06, gate logs) |
| 2026-11-06 18:19:51 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Domain (mutacao M1: guarda SinglePublisher removida) | 1 | docs/sdd/runs/test-runs/20261106-175956-test-filter.log (1 falha AC-02 em ContainerPolicyTests.cs:72; restaurado SHA256 C6521ACF byte-exact) |
| 2026-11-06 18:19:52 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Domain (mutacao M2: Guest passa em escrita) | 1 | docs/sdd/runs/test-runs/20261106-180054-test-filter.log (7 falhas: 5 celulas Guest + AC-05 PolicyEvaluatorTests.cs:94 + AC-03; restaurado SHA256 94CDDFA4 byte-exact) |
| 2026-11-06 18:19:52 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Domain (mutacao M3: Moderate enfraquecido para Member) | 1 | docs/sdd/runs/test-runs/20261106-180213-test-filter.log (1 falha: celula Member/Moderate em PolicyEvaluatorTests.cs:85; restaurado SHA256 94CDDFA4 byte-exact) |
| 2026-11-06 18:19:53 | qa | Get-FileHash SHA256 ContainerPolicy.cs IPolicyEvaluator.cs + scan QA-MUT | 0 | hashes C6521ACF e 94CDDFA4 iguais as pre-mutacao; MARCADORES DE MUTACAO: NENHUM |
| 2026-11-06 18:19:53 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261106-180400-test-gate.log (gate final apos restores: 97/97) |
| 2026-11-06 18:19:54 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter Category=Architecture | 0 | docs/sdd/runs/test-runs/20261106-180612-test-filter.log (17/17 testes de arquitetura verdes, AC-06) |
| 2026-11-06 18:19:54 | qa | wsl -e bash docs/sdd/qa/qa-check-task-0007.sh | 0 | docs/sdd/qa/qa-check-task-0007.sh (25 PASS sob WSL; suite xunit no Linux permanece unverified, sem dotnet) |
