<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0046 — PeerGrain: fila, backoff, circuit breaker, DLQ + `PeerClient` com falha

```yaml
runId: R-20261113-01
taskId: TASK-0046
status: TASK_READY
agent: odev
phase: P-06
sequence: 1
createdAt: 2026-11-13 13:47:25
updatedAt: 2026-11-13 13:47:25
parent: SPEC-0011
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Grains/Peers/**", "backend/src/Squid.PeerClient/**", "backend/src/Squid.Service/**", "backend/src/Squid.ReadModels/**", "backend/tests/Squid.Tests/**"]
blocks: ["TASK-0047"]
blockedBy: ["TASK-0045"]
inputArtifacts: ["docs/sdd/specs/SPEC-0011-federacao-p06.md", "docs/sdd/plan/plan.md", "specs/001-squid-mvp/data-model.md", "specs/001-squid-mvp/contracts/activitypub.md"]
outputArtifacts: ["backend/src/Squid.Grains/Peers/**", "backend/src/Squid.PeerClient/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/**"]
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

Entrega 6 de 9 da P-06: o motor de entrega assíncrona — `PeerGrain` por domínio remoto com fila,
backoff exponencial com jitter (`N = 8`, teto 6 h), circuito aberto, DLQ com motivo e alerta,
rate limit por par (FR-036, data-model §2.9/§3.4) — e o `Squid.PeerClient` (stub desde a
TASK-0002) com cenários de falha 500/timeout/lentidão para exercitar tudo sem par real.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: entrega que falha com 500 volta para `Pending` com backoff crescente e `attempts++`
  (`NextAttemptAt` no futuro) — teste (§3.4).
- AC-02: após 8 tentativas ou diante de 4xx permanente a entrega vira `Failed` na DLQ com motivo
  e dispara alerta — teste.
- AC-03: com o circuito aberto para o par, entrega nova vai **direto** para `Pending` com
  `NextAttemptAt` futuro (nunca falha na hora); backoff respeita teto de 6 h com jitter — teste.
- AC-04: par `Blocked` não recebe **nada** (nenhuma entrega é enviada) e o rate limit por par
  (`RateLimitPerMinute`) é respeitado no envio — teste.
- AC-05: `Squid.PeerClient` expõe cenários de falha **500**, **timeout** e **lentidão**, e um
  teste de integração usa cada cenário para exercitar a fila (AC-01/02/03) — teste.
- AC-06: todo envio carrega o atributo de trace `peer.domain`, sem PII (SC-015) — teste.
- AC-07: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1
  test` exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Grains/Peers/**` — diretório novo: `PeerGrain` (saúde, fila, backoff,
  circuito, DLQ)
- `backend/src/Squid.PeerClient/**` — cenários de falha (500, timeout, lentidão) no stub atual
- `backend/src/Squid.Service/**` — pump que consome `PeerDelivery` pendente e dispara o envio
- `backend/src/Squid.ReadModels/**` — consulta da fila/DLQ para o pump e para as métricas
- `backend/tests/Squid.Tests/**` — testes das ACs
- produto **fora** dos `touches` desta task: qualquer lacuna levantada como dúvida, não corrigida aqui

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| — | no | `Squid:RecoveryKey` ausente desabilita só o envio (aviso + métrica); a fila local continua registrando pendências — a instância nunca cai por falta da chave |
| — | no | runbooks `peer-entrega-travada.md`/`dlq-cheia.md` são da TASK-0048 (o sistema precisa existir primeiro) |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-13 13:47:25 | odev | task created from SPEC-0011 (entrega 6 de 9, PeerGrain + pump + PeerClient com falha; blockedBy TASK-0045)

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
