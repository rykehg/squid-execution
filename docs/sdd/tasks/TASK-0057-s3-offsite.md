<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0057 — S3CompatibleTarget off-site + drill restore-offsite

```yaml
runId: R-20261120-01
taskId: TASK-0057
status: TASK_READY
agent: odev
phase: P-07
sequence: 4
createdAt: 2026-11-20 21:50:58
updatedAt: 2026-11-20 21:50:58
parent: SPEC-0012
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Worker/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/Backup/**"]
blocks: ["TASK-0060"]
blockedBy: ["TASK-0056"]
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
nextAction: TASK_READY - dispatch DEV após TASK-0056 DONE (target S3 à mão + fake S3 em processo + drill restore-offsite)
evidence: []
memoryBankFlags: []
```

## Intent

SPEC-0012 §B + §D: `S3CompatibleTarget` (contrato §1, MVP "off-site fora do perímetro do
provedor") com client S3 **escrito à mão** (`PUT`/`GET`/`LIST`/`DELETE` + SigV4, zero pacote
NuGet novo), testado contra um **fake S3 em processo** que valida a assinatura de verdade, e o
sexto drill do contrato §5 — `restore-offsite`: backup local + off-site → apagar o disco local →
restore do off-site → estado completo, `/ready` verde. Fecha o DoD do Sprint 6 ("off-site
restaura sem o disco local").

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `S3CompatibleTarget` implementa `PutAsync`/`GetAsync`/`ListAsync`/`ExistsAsync`/
  `DeleteAsync` falando o protocolo S3 (`PUT`/`GET`/`LIST`/`DELETE` + assinatura SigV4) sobre
  `Backup:S3:*` — **nenhum pacote NuGet novo** adicionado ao solution — teste.
- AC-02: os testes rodam contra um fake S3 em processo que **valida a assinatura SigV4**
  (requisição sem assinatura ou com assinatura errada é rejeitada pelo fake) — sem rede externa
  no gate — teste.
- AC-03: `restore-offsite` (drill `Category=RestoreDrill`): backup local + off-site → apagar o
  disco local → restore do off-site → estado completo e hashes idênticos — teste.
- AC-04: falha do target off-site (resposta 500/timeout simulada) não corrompe o backup local:
  o target local segue gravando e o envelope registra o estado por destino — teste.
- AC-05: a credencial S3 vem só de `Backup:S3:*` (config/segredo externo, contrato §7): ela não
  aparece no estado do grain, em log nem na resposta de nenhuma rota — teste.
- AC-06: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1
  test` exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Worker/**` — `S3CompatibleTarget` + client SigV4 à mão
- `backend/src/Squid.Service/**` — wiring + seção `Backup:S3:*`
- `backend/tests/Squid.Tests/Backup/**` — fake S3 em processo + drill `restore-offsite`
- rota `backup/status`/`redundancy.score` é TASK-0060; rotação é TASK-0058
- produto fora dos `touches`: qualquer lacuna é `D-01` para o ODEV

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-52 | no | S3 testado contra fake S3 em processo com validação SigV4 real; client escrito à mão (zero pacote); endpoint real (`Backup:S3:*`) por config — default aceito pelo humano 2026-11-20 21:07:26 |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-20 21:50:58 | odev | task created from SPEC-0012 (S3 a mao + SigV4 + fake S3 em processo + drill restore-offsite; GAP-52; blockedBy TASK-0056)

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
