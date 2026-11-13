<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0048 — Runbook de federação (manual dos dois silos + runbooks P-06 executáveis)

```yaml
runId: R-20261113-01
taskId: TASK-0048
status: TASK_READY
agent: odev
phase: P-06
sequence: 1
createdAt: 2026-11-13 13:47:25
updatedAt: 2026-11-13 13:47:25
parent: SPEC-0011
authority: SPEC
parallel: false
touches: ["docs/runbooks/**"]
blocks: ["TASK-0049"]
blockedBy: ["TASK-0047"]
inputArtifacts: ["docs/sdd/specs/SPEC-0011-federacao-p06.md", "docs/sdd/plan/plan.md", "docs/runbooks/README.md", "docs/runbooks/peer-entrega-travada.md", "docs/runbooks/dlq-cheia.md"]
outputArtifacts: ["docs/runbooks/federacao-interop.md", "docs/runbooks/README.md", "docs/runbooks/peer-entrega-travada.md", "docs/runbooks/dlq-cheia.md"]
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
nextAction: TASK_READY - aguardando dispatch DEV
evidence: []
memoryBankFlags: []
```

## Intent

Entrega 8 de 9 da P-06 — **o manual pedido pelo humano** (decisões 3 e 5 da SPEC-0011):
`docs/runbooks/federacao-interop.md` com o passo a passo executável para subir os dois silos
Squid locais, subir Mastodon e Friendica em Docker, apontar e validar a comunicação — escrito
**antes** da TASK-0049, que executa o manual junto com o humano. Nesta entrega os runbooks
`peer-entrega-travada.md` e `dlq-cheia.md` (PENDENTE-fase desde a P-01) viram EXECUTÁVEL, com os
sistemas que a TASK-0046 construiu. Só `docs/runbooks/**` — nenhum código.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `docs/runbooks/federacao-interop.md` existe e traz comandos **reais** do repositório
  (copiados/verificados dos arquivos e configs do repo, não inventados) para: subir silo A e silo
  B em portas/domínios distintos, configurar `Instance:Domain` e `Squid:RecoveryKey`, subir
  Mastodon e Friendica em Docker, apontar cada um para o silo e validar a comunicação — teste.
- AC-02: o runbook traz uma seção opcional claramente marcada **"instância pública real"**
  (domínio, TLS, túnel) identificada como gate humano — teste.
- AC-03: `peer-entrega-travada.md` e `dlq-cheia.md` viram EXECUTÁVEL: escopo + passo a passo com
  comandos reais, sem ponteiro "PENDENTE-fase" — teste.
- AC-04: `docs/runbooks/README.md` (índice) atualizado: status dos três runbooks e ponteiros de
  fase corretos (P-06) — teste.
- AC-05: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1
  test` exit 0; nenhum arquivo alterado fora dos `touches` (só `docs/runbooks/**`).

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `docs/runbooks/federacao-interop.md` — novo: o manual
- `docs/runbooks/peer-entrega-travada.md`, `docs/runbooks/dlq-cheia.md` — de PENDENTE-fase para EXECUTÁVEL
- `docs/runbooks/README.md` — índice e status
- produto **fora** dos `touches` desta task: qualquer lacuna levantada como dúvida, não corrigida aqui

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-47 | no | manual cobre o caminho Docker local (decisão 6) como primário; "instância pública real" é seção opcional marcada como gate humano |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-13 13:47:25 | odev | task created from SPEC-0011 (entrega 8 de 9, O MANUAL pedido pelo humano - runbook federacao-interop + runbooks P-06 executaveis; blockedBy TASK-0047)

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
