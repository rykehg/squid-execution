<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0040 — Facet de fecho do DoD do Sprint 4

```yaml
runId: R-20261111-01
taskId: TASK-0040
status: TASK_READY
agent: odev
phase: P-05
sequence: 1
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
nextAction: aguarda BLOCKED por TASK-0039 - ULTIMA entrega da P-05, com QA PASSED a fase fecha
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
- AC-05: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`
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

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
