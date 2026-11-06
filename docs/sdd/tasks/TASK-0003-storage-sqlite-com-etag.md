<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0003 — Storage SQLite com ETag imposto e store name como parâmetro

```yaml
runId: R-20261104-01
taskId: TASK-0003
status: TASK_READY
agent: odev
phase: P-01
sequence: 1
createdAt: 2026-11-04 12:06:31
updatedAt: 2026-11-04 12:06:31
parent: SPEC-0001
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Persistence/**", "backend/tests/Squid.Tests/Storage/**"]
blocks: []
blockedBy: [TASK-0002]
inputArtifacts: ["docs/features/05-arquitetura-consolidada.md", "specs/001-squid-mvp/data-model.md"]
outputArtifacts: ["backend/src/Squid.Persistence/SqliteGrainStorage.cs", "backend/tests/Squid.Tests/Storage/SqliteGrainStorageTests.cs"]
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
nextAction: blockedBy TASK-0002 — dispatch after TASK-0002 reaches DONE
evidence: []
memoryBankFlags: []
```

## Intent

Implementa `IGrainStorage` sobre SQLite com ETag imposto (doc 05 §4.7 #1) e store name nunca
hardcoded (#2). É o que impede a escrita concorrente perdida em silêncio — o defeito que a análise
do orders-orleans apontou e que o doc 05 §4.7 transformou em regra obrigatória.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: duas escritas com o mesmo `ETag` esperado sobre a mesma chave: a primeira grava, a segunda
  lança `InconsistentStateException` e o estado em disco é o da primeira — verificado por teste que
  lê de volta depois da segunda escrita.
- AC-02: escrita com `ETag` nulo sobre linha existente lança `InconsistentStateException` e não
  sobrescreve.
- AC-03: dois stores distintos com o mesmo `grainId` guardam linhas separadas; ler o store `actors`
  nunca devolve o estado do store `containers` — teste com leitura cruzada nos dois sentidos.
- AC-04: leitura de chave inexistente devolve `State = null` e `ETag = null`, sem exceção.
- AC-05: `ClearStateAsync` respeita a regra de ETag e, após ela, a leitura volta a ser ausente.
- AC-06: um teste de arquitetura falha se o SQL de `Squid.Persistence` contiver o nome de um store
  como literal (grep scriptado sobre os `.cs` do projeto com exit 0 no estado correto).
- AC-07: a suíte de storage roda contra SQLite em arquivo temporário real (não em memória
  compartilhada), deixa o arquivo limpo ao fim e passa duas vezes seguidas.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Persistence/**`
- `backend/tests/Squid.Tests/Storage/**`

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-04 | no | `etag` = `GrainStateVersion` em base64; comparação exata em `WHERE Version = @Version` |
| GAP-05 | no | SQLite é o gate do CI; a suíte de contratos é escrita contra `IGrainStorage`, para rodar depois contra Oracle ATP sem reescrita |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`

- 2026-11-04 12:06:31 | odev | task created from SPEC-0001

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |