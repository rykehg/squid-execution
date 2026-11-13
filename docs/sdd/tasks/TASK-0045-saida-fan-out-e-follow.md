<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0045 — Saída: fan-out por visibilidade, reavaliação R-05 e rota de follow

```yaml
runId: R-20261113-01
taskId: TASK-0045
status: TASK_READY
agent: odev
phase: P-06
sequence: 1
createdAt: 2026-11-13 13:47:25
updatedAt: 2026-11-13 13:47:25
parent: SPEC-0011
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Persistence/schema/**", "backend/src/Squid.Api/Posts/**", "backend/src/Squid.Api/Federation/**", "backend/src/Squid.Api/Containers/**", "backend/src/Squid.ReadModels/**", "backend/src/Squid.Protocols/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/**"]
blocks: ["TASK-0046"]
blockedBy: ["TASK-0044"]
inputArtifacts: ["docs/sdd/specs/SPEC-0011-federacao-p06.md", "docs/sdd/plan/plan.md", "specs/001-squid-mvp/contracts/activitypub.md", "specs/001-squid-mvp/data-model.md", "specs/001-squid-mvp/research.md"]
outputArtifacts: ["backend/src/Squid.Persistence/schema/0008_federation.sql", "backend/src/Squid.Api/Posts/**", "backend/src/Squid.Api/Federation/**", "backend/src/Squid.ReadModels/**", "backend/tests/Squid.Tests/**"]
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

Entrega 5 de 9 da P-06: o caminho de **saída** — fan-out do log `Activity` por visibilidade
(contrato §4, §3.2 do doc 05, FR-037), reavaliação da política **no momento do envio** (R-05,
FR-038 → `Cancelled` auditável), fila `PeerDelivery` em migration versionada `0008_federation.sql`
e a rota local `POST /api/federation/follow` (GAP-41) que nasce a relação de seguidor. O motor de
entrega (backoff/DLQ/circuito) é da TASK-0046: aqui as entregas nascem `Pending` e são observáveis.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: publicar post `public` cria `PeerDelivery` para cada seguidor remoto do autor (e para a
  coleção `followers` do contêiner) com `to`/`cc` do contrato §2.1 (`Public` em `to`, `followers`
  em `cc`) — teste.
- AC-02: `unlisted` entrega **só** a seguidores; `membersOnly` entrega **só** a atores com
  `Membership.State = Active` com `squid:policy` no payload e nada em `cc`; `private` não cria
  **nenhuma** `PeerDelivery` — teste (§7.2, §7.3).
- AC-03: `POST /api/federation/follow` (sessão + CSRF) gera `Follow` assinado enfileirado para o
  par; `Accept` remoto cria a relação seguidor local; `Follow` remoto respondido com `Accept` —
  teste (GAP-41/GAP-46).
- AC-04: reavaliação R-05 — com entrega `Pending`, mudar a política para negar o destinatário e
  disparar o envio → entrega vira `Cancelled` com motivo auditável e **nada** é enviado; sem erro,
  sem retry — teste (FR-038, §7.7).
- AC-05: publicar retorna ao cliente **sem esperar** a entrega remota: a resposta local chega com
  as entregas já registradas em estado `Pending` (assíncrono, FR-036) — teste.
- AC-06: remover o post local propaga `Delete` assinado a todos os destinatários que receberam o
  `Create` — teste (R-08).
- AC-07: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1
  test` exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Persistence/schema/**` — `0008_federation.sql` idempotente (`follow`,
  `peer`, `peer_delivery`; liberação teste/dev/CI vale, produção é gate humano)
- `backend/src/Squid.Api/Posts/**` — hook de fan-out na publicação e no `DELETE`
- `backend/src/Squid.Api/Federation/**` — `POST /api/federation/follow`
- `backend/src/Squid.Api/Containers/**` — destinatários `followers` do contêiner
- `backend/src/Squid.ReadModels/**` — leitura de seguidores remotos para o fan-out
- `backend/src/Squid.Protocols/**` — montagem do envelope de saída (Deliver)
- `backend/src/Squid.Service/**` — encanamento
- `backend/tests/Squid.Tests/**` — testes das ACs
- produto **fora** dos `touches` desta task: qualquer lacuna levantada como dúvida, não corrigida aqui

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-41 | no | rota local `POST /api/federation/follow` sem UI (P-08 consume) — sem ela o DoD dos dois silos não tem por onde nascer a relação |
| GAP-43 | no | saída limitada a `Create`, `Follow`, `Accept`, `Undo(Follow)`, `Delete`; `Like`/`Announce`/`Flag` de saída fora |
| — | no | motor de entrega (backoff, circuito, DLQ, rate por par) é da TASK-0046; aqui a entrega nasce `Pending` e é observável — cada entrega é funcional por si |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-13 13:47:25 | odev | task created from SPEC-0011 (entrega 5 de 9, fan-out + R-05 + migration 0008 + rota de follow; blockedBy TASK-0044)

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
