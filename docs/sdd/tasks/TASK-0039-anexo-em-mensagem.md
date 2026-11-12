<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0039 — Anexo em mensagem reusando `/api/media`

```yaml
runId: R-20261111-01
taskId: TASK-0039
status: TASK_READY
agent: odev
phase: P-05
sequence: 1
createdAt: 2026-11-11 23:45:07
updatedAt: 2026-11-11 23:45:07
parent: SPEC-0010
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Api/Threads/**", "backend/src/Squid.Api/Media/**", "backend/src/Squid.Service/**", "backend/src/Squid.Persistence/schema/**", "backend/tests/Squid.Tests/Threads/**"]
blocks: ["TASK-0040"]
blockedBy: ["TASK-0038"]
inputArtifacts: ["docs/sdd/specs/SPEC-0010-mensageria-p05.md", "docs/sdd/plan/plan.md", "specs/001-squid-mvp/data-model.md", "specs/001-squid-mvp/contracts/http-api.md"]
outputArtifacts: ["backend/src/Squid.Api/Threads/**", "backend/src/Squid.Api/Media/**", "backend/src/Squid.Service/**"]
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
nextAction: aguarda BLOCKED por TASK-0038
evidence: []
memoryBankFlags: []
```

## Intent

Decisão humana da rodada 2: anexo em mensagem reusa o pipeline da P-04 **intacto** — a mensagem
só referencia `contentHashes` (≤10) no corpo; upload, magic bytes, EXIF, Alt e cota 507 continuam
em `POST /api/media`. A regra de visibilidade de `GET /api/media/{contentHash}` (GAP-25) é
ampliada: vê o uploader ou quem participa de thread cuja mensagem referencia o hash.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `POST .../messages` aceita `contentHashes` (≤10) e o envelope de leitura devolve os
  anexos com os campos do `Attachment`; hash inexistente → `400`; acima de 10 → `400` — teste.
- AC-02: `Image`/`Video`/`Gif` sem `Alt` → `400` (regra §2.6 aplicada na publicação da mensagem) —
  teste.
- AC-03: cota e upload intocados: anexo de mensagem **não cobra duas vezes** — o `507` continua
  vindo do `POST /api/media` e o envio da mensagem não cria nova cobrança no `quota_ledger` — teste.
- AC-04: `GET /api/media/{contentHash}` (regra GAP-25 ampliada): participante de thread cuja
  mensagem referencia o hash vê a mídia; não-participante → `404` idêntico; uploader vê — teste.
- AC-05: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`
  exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Api/Threads/**` (campo `contentHashes` no corpo + anexos no envelope)
- `backend/src/Squid.Api/Media/**` (ampliar a checagem de visibilidade de `GET /api/media/{hash}`)
- `backend/src/Squid.Service/**` (wiring se preciso)
- `backend/tests/Squid.Tests/Threads/**`

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-35 | no | emenda (b): `contentHashes` no corpo da mensagem; upload continua em `POST /api/media` (TASK-0027) |
| GAP-25 | no | regra de visibilidade da mídia ampliada para "participante de thread que referencia" — origem da ampliação é esta task |
| — | no | blob, magic bytes, EXIF e cota 507 permanecem os da P-04; nada duplicado |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-11 23:45:07 | odev | task created from SPEC-0010 (entrega 7 de 8 da P-05; blockedBy TASK-0038; decisão humana rodada 2 — reusar /api/media; emenda b ao §6)
- 2026-11-12 05:18:26 | odev | touches emendado pelo ODEV preventivamente: backend/src/Squid.Persistence/schema/** entra no YAML (3a ocorrencia do erro do dispatch em 0034/0036 - esta task precisara de migration: coluna/tabela nova); nenhuma AC muda

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
