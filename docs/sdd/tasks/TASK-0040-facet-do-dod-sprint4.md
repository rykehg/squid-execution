<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0040 — Facet de fecho do DoD do Sprint 4

```yaml
runId: R-20261111-01
taskId: TASK-0040
status: DONE
agent: odev
phase: P-05
sequence: 2
createdAt: 2026-11-11 23:45:07
updatedAt: 2026-11-11 23:45:07
parent: SPEC-0010
authority: SPEC
parallel: false
touches: ["backend/tests/Squid.Tests/Threads/**"]
blocks: []
blockedBy: ["TASK-0039"]
inputArtifacts: ["docs/sdd/specs/SPEC-0010-mensageria-p05.md", "docs/sdd/plan/plan.md", "specs/001-squid-mvp/contracts/http-api.md"]
outputArtifacts: ["backend/tests/Squid.Tests/Threads/**"]
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
nextAction: DONE - QA-0040 PASSED (7/7 ACs, 8 mutacoes RED-FACET, gate 480/480); ULTIMA entrega - inicia a consolidacao da fase
evidence: []
memoryBankFlags: []
```

## Intent

Facet de fecho do DoD do Sprint 4 em fluxo HTTP completo, reusando as rotas das tasks
anteriores: dois usuários conversam; terceiro não lê; ordem estável sob envio concorrente;
resposta e reação visíveis só aos participantes e idempotentes. Última entrega da fase — com QA
PASSED a P-05 fecha (consolidação + snapshot + authority CODE).

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: facet "dois conversam": A e B criam direct, aceitam o aviso, trocam mensagens e as
  leem na ordem de `seq` — fluxo HTTP completo — teste.
- AC-02: facet "terceiro não lê": C (não-participante) recebe 404 byte-idêntico em GET e POST
  da thread de A/B, e não recebe a lista de participantes — teste.
- AC-03: facet "ordem estável sob concorrência": envios concorrentes na mesma thread recebem
  `seq` únicas e a leitura devolve todas na ordem, sem duplicata — teste.
- AC-04: facet "resposta e reação idempotentes e só participantes": repetir resposta/reação não
  muda contagem nem estado; não-participante não vê resposta nem reação (404 idêntico) — teste.
- AC-05: **regressões que só a QA cobriu com teste temporário viram teste permanente** (finding 3
  da QA-0036, adjudicado pelo ODEV 2026-11-12 06:02:34): (a) colisão de parser — participante com
  handle `read` (ou outro sufixo de rota) não quebra remoção/leitura; (b) o texto do aviso FR-032
  gravado na linha de aceite existe — teste.
- AC-06: facet "dois conversam" completa da TASK-0033 até a TASK-0039 em fluxo: criação, aceite,
  envio, resposta, reação e anexo na mesma thread — teste.
- AC-07: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`
  exit 0; nenhum arquivo alterado fora dos `touches` (se a facet acusar lacuna de produto, é
  `D-01` para o ODEV — o DEV desta task só escreve teste).

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/tests/Squid.Tests/Threads/**` (facet DoD — testes novos reusando rotas existentes)
- produto **fora** dos `touches` desta task: qualquer lacuna levantada como dúvida, não corrigida aqui

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| — | no | facet usa só as rotas já entregues (0033..0039); defeito de task anterior é achado de QA com `S-NN` no backlog, não correção silenciosa aqui |
| GAP-36 | no | sem asserção de ao vivo (SSE/webhook/push fora do P-05 — polling cobre o DoD) |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-11 23:45:07 | odev | task created from SPEC-0010 (entrega 8 de 8 e ULTIMA da P-05; blockedBy TASK-0039; facet DoD do Sprint 4)
- 2026-11-12 06:03:08 | odev | AC renumerada pelo ODEV: a antiga AC-05 (gate) virou AC-07; acrescentadas AC-05 (testes permanentes das duas regressoes que a QA-0036 so cobriu com teste temporario - colisao de parser e texto do aviso FR-032, finding 3 adjudicado) e AC-06 (facet ponta a ponta 0033..0039); nenhuma AC anterior perde sentido
- 2026-11-12 10:23:41 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; blockedBy TASK-0039 resolve (DONE com QA-0039 PASSED, gate 473/473, merge a24af0d em develop + docs 0c329d8); AC-05/06/07 (regressoes da QA-0036 + facet ponta a ponta + gate) acrescentadas pelo ODEV antes do dispatch; ULTIMA entrega da P-05 - com QA PASSED a fase fecha; autorizacao de commit gate PHASE 2026-01-11 20:42:28 (modelo C) incluida - nova branch task/TASK-0040-facet-do-dod-sprint4 desde develop
- 2026-11-12 10:24:49 | odev | correcao da linha anterior: a data do gate de autorizacao de commit e 2026-11-11 20:42:28 (o typo 2026-01-11 na linha de dispatch e meu, digito errado - o gate e o mesmo Modelo C de 2026-11-11)
- 2026-11-12 10:36:44 | dev | feasibility 1/2: task faz sentido (facet HTTP reusando rotas 0033..0039; unit sob teste = Suites de backend/tests/Squid.Tests/Threads + PostRoutes/MediaHarness helpers); touches confirmado (só backend/tests/Squid.Tests/Threads/**); sem duvida bloqueante (AC-05a/05b tem fonte na QA-0036 preservada); baseline 473/473 exit 0 em 20261112-103510-test-gate.log
- 2026-11-12 10:52:45 | dev | Sprint4ClosingFacetTests.cs escrito (7 testes: AC-01 fluxo completo dois conversam, AC-02 404 identico do terceiro, AC-03 concorrencia com seqs unicas, AC-04 resposta/reacao idempotentes + parede, AC-05a colisao handle read, AC-05b texto do aviso FR-032 na linha do aceite, AC-06 facet ponta a ponta 0033..0039 com anexo); filtro Threads 48/48 exit 0
- 2026-11-12 10:59:14 | dev | gate final AC-07: 480/480 (473 baseline + 7 novos) exit 0 lido DENTRO do log 20261112-105828-test-gate.log (S-22); escopo limpo - git status so o arquivo de teste novo em touches + este task file (log/evidence do DEV)
- 2026-11-12 10:59:54 | dev | commit autorizado (gate PHASE 2026-11-11 20:42:28, modelo C): branch task/TASK-0040-facet-do-dod-sprint4 desde develop, UM commit 01b741f so com backend/tests/Squid.Tests/Threads/Sprint4ClosingFacetTests.cs (613 linhas); sem push, sem rebase, main intocado; AC-01..AC-07 todas cobertas, nenhum D-01 (nenhuma lacuna de produto acusada)
- 2026-11-12 11:02:37 | odev | ODEV verificou antes de transicionar: gate final 20261112-105828-test-gate.log lido no arquivo (exit: 0, Aprovado 480, Com falha 0, 5m02s); commit 01b741f conferido via git show (1 arquivo de teste, 613 linhas, NENHUM produto tocado, branch task/TASK-0040-facet-do-dod-sprint4); backend git status = 0; touchedOutsidePlan NENHUM; step result DONE aceito - 7 testes (AC-01..AC-06), nenhuma lacuna de produto acusada (D-01 nao levantado), AC-05a/05b usaram a fonte preservada da QA-0036
- 2026-11-12 11:02:39 | odev | step result aceito: facet de fecho do DoD do Sprint 4 completa - dois conversam, terceiro 404 identico (4 corpos iguais), 6 envios concorrentes Task.WhenAll com seqs 1..6, resposta/reacao idempotentes, handle read (colisao de parser), notice FR-032 na linha do aceite, fluxo ponta a ponta 0033..0039 com anexo e marcadores por participante; baseline 473/473 -> gate 480/480
- 2026-11-12 11:02:41 | odev | transicao IN_DEVELOPMENT -> AWAITING_QA; ULTIMA entrega da P-05 em QA - com QA PASSED a fase fecha (consolidacao + snapshot + authority CODE)
- 2026-11-12 11:54:55 | qa | QA-0040 attempt 1/2: entrou pelo task file (status AWAITING_QA); SPEC-0010 inteiro + contrato http-api secoes 6 e 9 lidos; facet = Sprint4ClosingFacetTests.cs com 7 testes cobrindo AC-01..AC-06, concorrencia REAL confirmada na fonte (Task.WhenAll, 3 rodadas, duas sessoes)
- 2026-11-12 11:54:56 | qa | baseline do filtro Threads 48/48 exit 0 (20261112-112436-test-filter.log) e -VerifyOnly do driver 8/8 padroes + BOM (QA-0040-verify.log)
- 2026-11-12 11:54:56 | qa | 8 ciclos can-fail executados contra o PRODUTO e todos vermelhos DENTRO do log com a facet de vermelha em cada um: M1 marcador nunca gravado (AC-01/05a/06), M2 traceId no 404 (AC-02/04), M3 nao-participante le o GET (AC-02/04), M4 seq colidindo (AC-03), M5 reply reescreve o alvo (AC-04), M6 cadeira read engolida (AC-05a), M7 texto do aviso fora da linha (AC-05b), M8 ramo mensagem da midia morto (AC-06); restore byte-exact 8/8 com mtime, GREEN 48/48, residuo 0
- 2026-11-12 11:54:57 | qa | gate AC-07 lido DENTRO do log 20261112-115022-test-gate.log (S-22): exit 0 / 480 de 480 / 5m27s; git status backend = 0 vs 01b741f; escopo so docs/sdd (task file + artefatos QA)
- 2026-11-12 12:15:52 | qa | checkers executados: qa-check-task-0040.ps1 SUMMARY total=40 fails=0 (SDD-STATE check: OK) e gemeo qa-check-task-0040.sh no WSL SUMMARY total=35 fails=0; verdict PASSED escrito em docs/sdd/qa/QA-0040-facet-do-dod-sprint4.md; observacao M9 registrada (marcador un-monotonico sobrevive a facet, vermelho na suite da TASK-0036)
- 2026-11-12 12:19:24 | qa | -VerifyOnly re-executado depois de acrescentar a observacao M9: 9/9 padroes hits=1 + BOM OK (QA-0040-verify.log VERIFY total=9 bad=0 - a linha anterior registrava o estado de 8 ciclos, antes do M9); commit unico da QA 13d6a70 com os 10 artefatos docs/sdd/qa/QA-0040* e scripts *-0040 (task file fora do commit, sem push, sem rebase, main intocado)
- 2026-11-12 12:25:06 | odev | ODEV verificou antes de transicionar: QA-0040-facet-do-dod-sprint4.md com verdicto PASSED lido no arquivo; gate final 20261112-115022-test-gate.log exit: 0 / Aprovado 480 / Com falha 0 / 5m27s; residuo QA-MUTATION = 0 (git grep backend); commit QA 13d6a70 conferido (10 arquivos so docs/sdd/qa/, pai 01b741f, sem push, main intacta); backend git status = 0 entradas, blob 3/3 = 01b741f; check OK - veredito aceito
- 2026-11-12 12:25:06 | odev | QA-0040 PASSED attempt 1/2 (TESTS-ONLY): 7/7 ACs, 8 ciclos can-fail contra o PRODUTO com RED-FACET da propria facet em cada um (M1 marcador, M2 traceId, M3 parede de leitura, M4 colisao de seq, M5 reply reescreve alvo, M6 cadeira read, M7 texto do aviso, M8 ramo mensagem da midia) + observacao M9 (marcador un-monotono sobrevive a facet mas derruba a suite da TASK-0036 - boundary documentado, NAO e lacuna: o comportamento tem teste); concorrencia Task.WhenAll confirmada na fonte; unverified registrados (suite Linux, carga real)
- 2026-11-12 12:25:07 | odev | transicao AWAITING_QA -> DONE; TASK-0040 = entrega 8 de 8 e ULTIMA da P-05 encerrada - TODAS AS 8 ENTREGAS DONE COM QA PASSED NA 1a TENTATIVA; DoD do Sprint 4 provado; inicia a CONSOLIDACAO DA FASE (memory bank + snapshot 0006 + authority CODE)

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-12 10:52:49 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Threads | 0 | docs/sdd/runs/test-runs/20261112-105144-test-filter.log (48/48, exit 0) |
| 2026-11-12 10:53:13 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261112-103510-test-gate.log (baseline 473/473, exit 0) |
| 2026-11-12 10:59:16 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261112-105828-test-gate.log (exit: 0 no cabecalho, 480/480) |
| 2026-11-12 11:02:42 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate final AC-07) | 0 | docs/sdd/runs/test-runs/20261112-105828-test-gate.log - exit: 0 / Aprovado 480 / Com falha 0 / 5m02s - VERIFICADO PELO ODEV lendo o arquivo |
| 2026-11-12 11:02:44 | dev | git show --stat --format= HEAD (pos-commit) | 0 | 01b741f - 1 file changed 613 insertions - so teste em touches - VERIFICADO PELO ODEV via git show |
| 2026-11-12 11:56:48 | qa | git rev-parse HEAD + git rev-parse --abbrev-ref HEAD + git status --porcelain -uall + wsl -e bash -c command -v dotnet | 0 | docs/sdd/qa/QA-0040-git-baseline.txt (HEAD 01b741f, branch task/TASK-0040-facet-do-dod-sprint4, wsl-dotnet dotnet-missing) |
| 2026-11-12 11:56:49 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Threads (baseline da QA) | 0 | docs/sdd/runs/test-runs/20261112-112436-test-filter.log (exit: 0, Com falha 0 / Aprovado 48 / Total 48) |
| 2026-11-12 11:56:49 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0040.ps1 -VerifyOnly | 0 | docs/sdd/qa/QA-0040-verify.log (8 padroes hits=1, BOM OK, VERIFY total=8 bad=0) |
| 2026-11-12 11:56:49 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0040.ps1 (run cheio: 8 ciclos + GREEN) | 0 | docs/sdd/qa/QA-0040-mutations.log - 8 vermelhos com RED-FACET da facet em cada ciclo, 8 RESTORE-OK, GREEN 48/48, SUMMARY cycles=8 failed=0 residue=0 temp-backups=0 |
| 2026-11-12 11:56:50 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate AC-07) | 0 | docs/sdd/runs/test-runs/20261112-115022-test-gate.log - exit: 0 / Aprovado 480 / Com falha 0 / 5m27s (lido no arquivo) |
| 2026-11-12 11:56:50 | qa | varredura de escopo pwsh (git status, hash-object vs rev-parse, mtime, residuo, fora-dos-touches) | 0 | docs/sdd/qa/QA-0040-scope.log - git-backend 0, blob 3/3 match=True, mtime-count=3, residue=0, fora-dos-touches=0 |
| 2026-11-12 12:15:52 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0040.ps1 | 0 | docs/sdd/qa/QA-0040-check.log - SUMMARY total=40 fails=0 (SDD-STATE check: OK - log entries 15, evidence rows 11) |
| 2026-11-12 12:15:53 | qa | wsl -e bash docs/sdd/qa/qa-check-task-0040.sh (gemeo Linux, parte estatica) | 0 | docs/sdd/qa/QA-0040-check-sh.log - SUMMARY total=35 fails=0 |
| 2026-11-12 12:19:24 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0040.ps1 -VerifyOnly (re-executado pos-M9) | 0 | docs/sdd/qa/QA-0040-verify.log (9 padroes hits=1, BOM OK, VERIFY total=9 bad=0) |
| 2026-11-12 12:21:24 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0040.ps1 -Only M9 (observacao: marcador sem guarda de monotonicidade) | 0 | docs/sdd/qa/QA-0040-mutations.log - M9 RUN wrapper=1 logExit=1 aprovado=46 falha=2 (120124), RED-FACET NONE (facet sobrevive), RESTORE-OK, GREEN 48/48 (120209), SUMMARY cycles=1 failed=0 |
