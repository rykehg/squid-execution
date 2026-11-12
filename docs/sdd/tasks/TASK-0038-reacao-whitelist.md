<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0038 — Reação com whitelist e idempotência

```yaml
runId: R-20261111-01
taskId: TASK-0038
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
blocks: ["TASK-0039"]
blockedBy: ["TASK-0037"]
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
nextAction: aguarda BLOCKED por TASK-0037
evidence: []
memoryBankFlags: []
```

## Intent

FR-058 / data-model §2.14: uma linha por (mensagem, ator, emoji) — repetir é idempotente,
remover apaga, emoji fora da whitelist da instância → `422 invalid_emoji`, contagem derivada
nunca fonte, reação e contagem só aparecem para participantes (§9.10).

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `POST .../reactions` com `{emoji}` da whitelist cria a reação; repetir a mesma
  (mensagem, ator, emoji) é **idempotente** (mesma linha, contagem não muda) — teste.
- AC-02: emoji fora de `Reactions:AllowedEmojis` (config com defaults, 👍 sempre incluído) →
  `422 invalid_emoji`; mudar a config muda o conjunto aceito — teste.
- AC-03: `DELETE .../reactions?emoji=` remove a reação; remover reação inexistente → `404`;
  contagem `counts.reactions` é derivada da existência das linhas (reconciliada, não fonte) — teste.
- AC-04: não-participante recebe **404 idêntico** sem contagem em GET da mensagem e nas rotas
  de reação (§9.10) — teste.
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
| GAP-34 | no | whitelist em config `Reactions:AllowedEmojis` com conjunto padrão; 👍 (`Like`) sempre presente |
| — | no | reação de mensagem é local (thread não federa, §2.14); Kind `Like`/`Custom` sem distinção federada nesta fase |
| GAP-37 | no | rotas de reação passam pela sessão normal; gate 428 é só de envio (TASK-0036) |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-11 23:45:07 | odev | task created from SPEC-0010 (entrega 6 de 8 da P-05; blockedBy TASK-0037; FR-058 + §2.14 + §9.10; decisão humana whitelist em config rodada 2)

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
