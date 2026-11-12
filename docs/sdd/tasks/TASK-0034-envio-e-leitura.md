<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0034 — Envio e leitura com ordem total e 404 idêntico

```yaml
runId: R-20261111-01
taskId: TASK-0034
status: TASK_READY
agent: odev
phase: P-05
sequence: 1
createdAt: 2026-11-11 23:45:07
updatedAt: 2026-11-11 23:45:07
parent: SPEC-0010
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Api/Threads/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/Threads/**"]
blocks: ["TASK-0035"]
blockedBy: ["TASK-0033"]
inputArtifacts: ["docs/sdd/specs/SPEC-0010-mensageria-p05.md", "docs/sdd/plan/plan.md", "specs/001-squid-mvp/data-model.md", "specs/001-squid-mvp/contracts/http-api.md"]
outputArtifacts: ["backend/src/Squid.Api/Threads/**", "backend/src/Squid.Service/**"]
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
nextAction: aguarda BLOCKED por TASK-0033
evidence: []
memoryBankFlags: []
```

## Intent

Mensagem nasce com `seq` total alocada pelo estado da thread sob ETag (FR-030), conteúdo ≤4000
sanitizado, e leitura paginada com delta exato. Fecha FR-031: quem não é participante recebe
404 byte-idêntico em qualquer rota da thread, sem revelar existência nem participantes.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `POST /api/threads/{id}/messages` cria mensagem com `seq` crescente atribuída pelo
  estado da thread; **dois envios concorrentes nunca recebem a mesma `seq`** (ETag serializa) — teste.
- AC-02: `Content` ≤ 4000; sanitização escapa HTML e remove controles C0 no servidor (regra
  GAP-24 da P-04); vazio ou acima do limite → `400` — teste.
- AC-03: `GET .../messages?since=` devolve apenas `seq > since`, sem duplicata, na ordem de
  `seq`, com `nextSince` (§9.3) — teste.
- AC-04: não-participante (e thread inexistente) recebe **404 byte-idêntico** em GET e POST da
  thread, sem vazer existência nem a lista de participantes (FR-031/SC-005) — teste.
- AC-05: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`
  exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Api/Threads/**`
- `backend/src/Squid.Service/**`
- `backend/tests/Squid.Tests/Threads/**`

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-29 | no | seq alocada por ETag no estado da thread (mesma regra da TASK-0003/0023) |
| — | no | sanitização = escapar HTML + remover C0 (GAP-24, já adjudicado na P-04) |
| — | no | sem gate de E2EE nesta task (entra na TASK-0036); sem `replyToId` (TASK-0037); sem anexo (TASK-0039) |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-11 23:45:07 | odev | task created from SPEC-0010 (entrega 2 de 8 da P-05; blockedBy TASK-0033; FR-030/031 + §9.3)

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
