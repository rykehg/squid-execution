<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0060 — Tela de redundância: redundancy.score + página /admin/backup

```yaml
runId: R-20261120-01
taskId: TASK-0060
status: TASK_READY
agent: odev
phase: P-07
sequence: 7
createdAt: 2026-11-20 21:50:58
updatedAt: 2026-11-20 21:50:58
parent: SPEC-0012
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Api/Admin/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/Backup/**"]
blocks: []
blockedBy: ["TASK-0057"]
inputArtifacts: ["docs/sdd/specs/SPEC-0012-backup-rotação-p07.md", "specs/001-squid-mvp/contracts/backup-recovery.md"]
outputArtifacts: ["backend/tests/Squid.Tests/Backup/**", "docs/sdd/qa/QA-*"]
interviewRounds: 2/6
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
nextAction: TASK_READY - dispatch DEV após TASK-0057 DONE (opção A: endpoint JSON + página fina; última entrega de backend antes da 0049)
evidence: []
memoryBankFlags: []
```

## Intent

SPEC-0012 §G — decisão humana 2026-11-20 (opção A): `GET /api/admin/backup/status` ganha
`redundancy.score` (targets com o envelope mais recente verificado ÷ targets configurados, no
formato `"Redundância: 1/3 — sem recuperação de desastre"` do doc 05 §8) e `GET /admin/backup`
serve uma **página HTML fina** exibindo verifiedAt, envelopes, bytes, verify_failures,
`redundancy.score` e `restore.last_drill_*` — sem build de front, sem pacote novo; a P-08
substitui só a vista, o endpoint JSON permanece.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `GET /api/admin/backup/status` devolve `redundancy.score` = nº de targets com o envelope
  mais recente **verificado** ÷ nº de targets configurados, formatado como
  `"Redundância: 1/2 — sem recuperação de desastre"` quando nenhum off-site está verificado e
  sem o sufixo quando todos estão — teste.
- AC-02: `GET /admin/backup` devolve uma página HTML que exibe: último `verifiedAt`,
  `envelopes_count`, `bytes_total`, `verify_failures`, `redundancy.score` e
  `restore.last_drill_at`/`restore.last_drill_result` — teste (página contém os valores do
  estado atual).
- AC-03: as duas rotas exigem operador: sem sessão `401`, sessão fora de `Instance:Operators`
  `403` (mesma regra das rotas admin desde a P-03) — teste.
- AC-04: a página é servida pelo próprio backend (HTML estático embutido/inline, **nenhum
  pacote NuGet novo**, nenhum artefato de build de front) — teste.
- AC-05: o score muda observavelmente quando um target off-site passa a ter o envelope mais
  recente verificado (1/2 verificado → 2/2, sufixo some) — teste.
- AC-06: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1
  test` exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Api/Admin/**` — `redundancy.score` no status + rota HTML `/admin/backup`
- `backend/src/Squid.Service/**` — wiring
- `backend/tests/Squid.Tests/Backup/**` — testes dos ACs
- S3/alvo off-site é TASK-0057 (daí o blockedBy); rotação é TASK-0058
- P-08 substituirá a vista; o endpoint JSON é contrato permanente
- produto fora dos `touches`: qualquer lacuna é `D-01` para o ODEV

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| NENHUM | — | opção A confirmada pelo humano (entrevista P-07, turno entre as rodadas) |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-20 21:50:58 | odev | task created from SPEC-0012 (opcao A: redundancy.score + pagina HTML fina /admin/backup; blockedBy TASK-0057)

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
