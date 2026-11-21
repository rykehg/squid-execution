<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0055 — BackupCoordinator, agendador e rotas admin de backup

```yaml
runId: R-20261120-01
taskId: TASK-0055
status: TASK_READY
agent: odev
phase: P-07
sequence: 2
createdAt: 2026-11-20 21:50:58
updatedAt: 2026-11-20 21:50:58
parent: SPEC-0012
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Grains/**", "backend/src/Squid.Worker/**", "backend/src/Squid.Api/Admin/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/Backup/**"]
blocks: ["TASK-0056"]
blockedBy: ["TASK-0054"]
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
nextAction: TASK_READY - dispatch DEV após TASK-0054 DONE (coordinator + agendador + rotas admin)
evidence: []
memoryBankFlags: []
```

## Intent

SPEC-0012 §C: `BackupCoordinatorGrain` (por `instanceId`, lê o último seq consolidado e pede o
snapshot ao agregado — o Worker serializa), agendador `Backup:IntervalMinutes` (default 1440),
`POST /api/admin/backup/run` e `GET /api/admin/backup/status` do operador, com
`backup.last_success_at` alertando > 24 h e `backup.verify_failures` alertando na hora
(contrato §6). Sobe sobre o núcleo da TASK-0054.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `BackupCoordinatorGrain` roda o fluxo do contrato §3 por `instanceId` (seq consolidado
  → snapshot → Worker serializa → cifra → envia → verifica → registra `BackupEnvelope`) — teste.
- AC-02: agendador dispara backup sozinho após `Backup:IntervalMinutes` (default 1440; teste usa
  valor baixo) sem nenhuma request — teste.
- AC-03: `POST /api/admin/backup/run` com sessão de operador dispara backup imediato e devolve o
  envelope registrado; sem sessão `401`, sessão fora de `Instance:Operators` `403` — teste.
- AC-04: `GET /api/admin/backup/status` devolve `backup.last_success_at`,
  `backup.envelopes_count`, `backup.bytes_total`, `backup.verify_failures`,
  `restore.last_drill_at`, `restore.last_drill_result` (contrato §6) — teste.
- AC-05: `last_success_at` mais antigo que 24 h produz alerta (log/métrica) e
  `verify_failures` incrementa e alerta quando um envelope é marcado `Corrupt` — teste.
- AC-06: falha do target (ex.: caminho do disco indisponível) não derruba o processo nem
  corrompe o backup local — o coordinator reporta erro e o serviço segue servindo — teste.
- AC-07: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1
  test` exit 0; nenhum arquivo alterado fora dos `touches`; zero pacote NuGet novo.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Grains/**` — `BackupCoordinatorGrain` (ADR-35: sem referência a storage)
- `backend/src/Squid.Worker/**` — pump/agendador do backup
- `backend/src/Squid.Api/Admin/**` — rotas `backup/run` e `backup/status` (regra de operador existente)
- `backend/src/Squid.Service/**` — wiring do grain, do agendador e das rotas
- `backend/tests/Squid.Tests/Backup/**` — testes dos ACs
- drills são TASK-0056; S3 é TASK-0057; `redundancy.score` é TASK-0060
- produto fora dos `touches`: qualquer lacuna é `D-01` para o ODEV

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-53 | no | agendador `Backup:IntervalMinutes` (1440) + `POST /api/admin/backup/run`; alerta `last_success_at` > 24 h — default aceito pelo humano 2026-11-20 21:07:26 |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-20 21:50:58 | odev | task created from SPEC-0012 (coordinator + agendador 1440 + rotas run/status do operador; blockedBy TASK-0054)

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
