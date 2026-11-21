<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0056 — Restore e 5 drills no gate + runbook restaurar-backup

```yaml
runId: R-20261120-01
taskId: TASK-0056
status: TASK_READY
agent: odev
phase: P-07
sequence: 3
createdAt: 2026-11-20 21:50:58
updatedAt: 2026-11-20 21:50:58
parent: SPEC-0012
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Worker/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/Backup/**", "docs/runbooks/restaurar-backup.md"]
blocks: ["TASK-0057"]
blockedBy: ["TASK-0055"]
inputArtifacts: ["docs/sdd/specs/SPEC-0012-backup-rotação-p07.md", "specs/001-squid-mvp/contracts/backup-recovery.md", "docs/runbooks/restaurar-backup.md"]
outputArtifacts: ["backend/tests/Squid.Tests/Backup/**", "docs/runbooks/restaurar-backup.md", "docs/sdd/qa/QA-*"]
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
nextAction: TASK_READY - dispatch DEV após TASK-0055 DONE (restore + drills Category=RestoreDrill + runbook)
evidence: []
memoryBankFlags: []
```

## Intent

SPEC-0012 §D (SC-009): o fluxo de restore do contrato §4 (decifra → valida hash → aplica
migrações de `format` → reaplica ledger/projeções → compara contagens e hashes → `/ready`)
mais os cinco drills bloqueantes no gate — `restore-roundtrip` (100% hashes idênticos),
`restore-interrupted`, `restore-schema-old`, `restore-wrong-key`, `backup-incremental` — e o
runbook `restaurar-backup.md` de `PARCIAL` para **EXECUTÁVEL**. O sexto drill
(`restore-offsite`) é da TASK-0057, que precisa do target S3.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `restore-roundtrip`: criar dados → backup → apagar o diretório → restore → **100% dos
  hashes idênticos** por agregado (SC-009) — teste `Category=RestoreDrill` no gate.
- AC-02: `restore-interrupted`: matar o backup no meio → rodar de novo — nenhum envelope
  `Corrupt` é aceito e o retomar parte do `seqTo` íntegro — teste.
- AC-03: `restore-schema-old`: envelope com `format` anterior → migração aplicada; formato
  incompatível → falha explícita, nunca silenciosa — teste.
- AC-04: `restore-wrong-key`: `keyEpoch` sem chave correspondente no kit → falha clara e
  **nada é destruído** (banco de destino intacto) — teste.
- AC-05: `backup-incremental`: backup 1 → N escritas → backup 2 contém **só o delta**
  (`SeqFrom` = seqTo anterior + 1) — teste.
- AC-06: após o restore, ledger de cota e projeções conferem (contagens e hashes por agregado) e
  `/ready` responderia verde — teste.
- AC-07: runbook `docs/runbooks/restaurar-backup.md` está **EXECUTÁVEL**: cada passo do trecho
  de geração/restore tem comando real verificado (nada de `PENDENTE`, nenhum passo não
  executado) — revisão + evidência de execução dos comandos do runbook.
- AC-08: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1
  test` exit 0 (drills incluídos); nenhum arquivo alterado fora dos `touches`; zero pacote novo.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Worker/**` — pipeline de restore (contrato §4)
- `backend/src/Squid.Service/**` — wiring do restore
- `backend/tests/Squid.Tests/Backup/**` — os 5 drills (`Category=RestoreDrill`)
- `docs/runbooks/restaurar-backup.md` — de PARCIAL para EXECUTÁVEL (passos provados)
- S3/`restore-offsite` é TASK-0057; rotação é TASK-0058
- produto fora dos `touches`: qualquer lacuna é `D-01` para o ODEV

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-55 | no | blobs de mídia fora do envelope (SPEC-0012 §Out of scope) — o restore devolve estado íntegro do SQLite, mídia segue no volume |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-20 21:50:58 | odev | task created from SPEC-0012 (restore §4 + 5 drills no gate + runbook EXECUTAVEL; blockedBy TASK-0055)

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
