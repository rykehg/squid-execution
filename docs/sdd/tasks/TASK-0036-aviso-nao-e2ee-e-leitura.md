<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0036 — Aviso não-E2EE (428) e marcação de leitura

```yaml
runId: R-20261111-01
taskId: TASK-0036
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
blocks: ["TASK-0037"]
blockedBy: ["TASK-0035"]
inputArtifacts: ["docs/sdd/specs/SPEC-0010-mensageria-p05.md", "docs/sdd/plan/plan.md", "specs/001-squid-mvp/spec.md", "specs/001-squid-mvp/contracts/http-api.md"]
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
nextAction: aguarda BLOCKED por TASK-0035
evidence: []
memoryBankFlags: []
```

## Intent

Consentimento informado (FR-032): sem aceite do aviso de não-E2EE a primeira mensagem responde
`428 precondition_required` (§9.6); com aceite registrado por (ator, thread) o envio passa. E
estado de entrega (FR-033): `POST .../read` grava `lastReadSeq` monotônico, visível só a
participantes. A UI do aviso é P-08 (GAP-37).

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `POST .../ack-no-e2ee` registra o aceite por (ator, thread) e é idempotente (repetir
  não duplica nem quebra) — teste.
- AC-02: `POST .../messages` **sem aceite → `428 precondition_required`**; depois do aceite,
  mesmo envio → `200` (§9.6); aceite de um ator não libera outro ator — teste.
- AC-03: `POST .../read` grava `lastReadSeq` até `seq`; marcador **só avança** (valor menor é
  ignorado, não retrocede); `seq` acima do total da thread → `400` (GAP-38) — teste.
- AC-04: `GET /api/threads` e o envelope de mensagens expõem `lastReadSeq` por participante
  **visível só a participantes**; não-participante → 404 idêntico sem o dado — teste.
- AC-05: ator `Suspended` não envia mensagem (SPEC-0010 §Rules, data-model §3.1 — mesma família
  do post): envio negado e registrado (log estruturado), participante ativo segue enviando —
  teste. **AC acrescentada pelo ODEV 2026-11-12 02:25:21**: regra da spec sem task dona,
  reportada como `notDone` pelo DEV da TASK-0034; sem mudança de contrato.
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
| GAP-33 | no | entregue = persistido (200 + seq); lido = lastReadSeq por participante; sem estado por dispositivo |
| GAP-37 | no | registro do aceite por (ator, thread) aqui; exibição do aviso na UI entra na P-08 |
| GAP-38 | no | marcador monotônico; seq > total → 400; 404 idêntico para não-participante |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-11 23:45:07 | odev | task created from SPEC-0010 (entrega 4 de 8 da P-05; blockedBy TASK-0035; FR-032/FR-033 + §9.6; decisão humana lastReadSeq da rodada 1)
- 2026-11-12 02:27:14 | odev | AC-05 (ator Suspenso nao envia mensagem) acrescentada pelo ODEV e a antiga AC-05 (gate) virou AC-06: regra da SPEC-0010 Rules/section D sem task dona, reportada como notDone pelo DEV da TASK-0034; nenhuma AC anterior muda de sentido, so renumeracao do gate

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
