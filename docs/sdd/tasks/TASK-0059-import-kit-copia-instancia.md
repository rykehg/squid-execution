<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0059 — Import de kit com cópia de instância (fecha S-42)

```yaml
runId: R-20261120-01
taskId: TASK-0059
status: TASK_READY
agent: odev
phase: P-07
sequence: 6
createdAt: 2026-11-20 21:50:58
updatedAt: 2026-11-20 21:50:58
parent: SPEC-0012
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Api/Recovery/**", "backend/src/Squid.Api/Crypto/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/Recovery/**"]
blocks: []
blockedBy: ["TASK-0058"]
inputArtifacts: ["docs/sdd/specs/SPEC-0012-backup-rotação-p07.md", "docs/sdd/specs/SPEC-0011-federacao-p06.md"]
outputArtifacts: ["backend/tests/Squid.Tests/Recovery/**", "docs/sdd/qa/QA-*"]
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
nextAction: TASK_READY - dispatch DEV após TASK-0058 DONE (S-42: cópia 0009 no import)
evidence: []
memoryBankFlags: []
```

## Intent

SPEC-0012 §F — fecha o **S-42**: o `POST /api/recovery/import` passa a embrulhar também a cópia
`0009` da RSA do ator quando `Squid:RecoveryKey` existe (mesma regra do registro da SPEC-0011
§B), para a identidade restaurada sair federando sem novo registro. Sem a config, comportamento
byte-idêntico ao atual. Decisão humana 2026-11-20 (rodada 1): S-42 fecha junto com a rotação.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: com `Squid:RecoveryKey` configurada, importar um kit cria a cópia `0009` do ator
  importado (embrulho duplo da SPEC-0011 §B) e a identidade restaurada **federada de saída**
  funciona (assinatura desembrulhável com o segredo de boot) sem novo registro — teste.
- AC-02: sem `Squid:RecoveryKey` no boot, o import responde **byte-idêntico** ao comportamento
  atual (sem cópia, sem erro, mesma resposta JSON) — teste.
- AC-03: a cópia criada no import segue as regras da SPEC-0011 §B: nunca em claro, nunca em
  log, nunca servida por rota — teste.
- AC-04: importar o mesmo kit duas vezes é idempotente (B-14): mesma resposta, nenhuma linha
  duplicada em `0009` — teste.
- AC-05: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1
  test` exit 0; nenhum arquivo alterado fora dos `touches`; zero pacote NuGet novo.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Api/Recovery/**` — `ImportAsync` embrulha a cópia quando há `Squid:RecoveryKey`
- `backend/src/Squid.Api/Crypto/**` — reuso do embrulho duplo existente (família `0009`)
- `backend/src/Squid.Service/**` — wiring
- `backend/tests/Squid.Tests/Recovery/**` — testes dos ACs
- produto fora dos `touches`: qualquer lacuna é `D-01` para o ODEV

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| S-42 | no | fecha aqui por decisão humana 2026-11-20 (rodada 1): junto com a rotação; fora das ACs da TASK-0051 |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-20 21:50:58 | odev | task created from SPEC-0012 (S-42: copia 0009 no import quando Squid:RecoveryKey existe; fecha S-42; blockedBy TASK-0058)

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
