<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0035 — Participantes e admin com sucessão do criador

```yaml
runId: R-20261111-01
taskId: TASK-0035
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
blocks: ["TASK-0036"]
blockedBy: ["TASK-0034"]
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
nextAction: aguarda BLOCKED por TASK-0034
evidence: []
memoryBankFlags: []
```

## Intent

Vida social da thread: adicionar participante depois da criação (rota nova, emenda GAP-31),
remover com acesso imediato perdido e `409 last_participant`, rotas de admin (promover/demover,
qualquer admin age, criador protegido) e a sucessão do criador (GAP-32): admin mais antigo
assume; sem admin, o membro mais antigo vira admin/criador.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `POST .../participants` adiciona participante a `Group` (teto 50) só sendo admin —
  participante comum → `403`, não-participante → `404` idêntico, `Direct` → `400`, acima de 50 →
  `400` — teste.
- AC-02: `DELETE .../participants/{actorId}`: admin remove qualquer participante **exceto o
  criador** (`403`); qualquer participante sai de si mesmo; **último participante →
  `409 last_participant`**; removido perde acesso imediato (404 nas rotas da thread) e o
  histórico permanece para quem fica — teste.
- AC-03: `POST`/`DELETE .../participants/{actorId}/admin` promove e remove papel: qualquer admin
  promove/demove outro admin (inclusive admins eleitos); o criador não é demovido nem removido
  por outro admin (`403`); não-admin → `403` — teste.
- AC-04: sucessão (GAP-32): quando o **criador sai**, o admin com menor `joined_at` assume o
  papel de criador; se não houver admin, o membro com menor `joined_at` é promovido a admin e
  vira criador — `Group` com ≥1 participante tem ≥1 admin — teste.
- AC-05: participante adicionado depois enxerga o histórico inteiro da thread (GAP-40) — teste.
- AC-06: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`
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
| GAP-30 | no | emenda (a) ao data-model §2.7: `CreatedBy` + papel admin por participante — original intacto |
| GAP-31 | no | emenda (b) ao contrato §6: `POST .../participants` + `POST/DELETE .../participants/{actorId}/admin` |
| GAP-32 | no | sucessão por menor `joined_at`; criador protegido de remoção/demissão por terceiros |
| GAP-38 | no | `409 last_participant` é código novo da família `last_owner` (§9.11) |
| GAP-40 | no | quem entra depois vê o histórico inteiro; `Direct` não aceita adição e sai de 2→1 com histórico legível |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-11 23:45:07 | odev | task created from SPEC-0010 (entrega 3 de 8 da P-05; blockedBy TASK-0034; decisões humanas rodadas 3/4/5 — admin, sucessão, emendas)

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
