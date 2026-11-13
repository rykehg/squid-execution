<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0044 — Inbox: pipeline de verificação, dedupe e aplicação de atividades

```yaml
runId: R-20261113-01
taskId: TASK-0044
status: TASK_READY
agent: odev
phase: P-06
sequence: 1
createdAt: 2026-11-13 13:47:25
updatedAt: 2026-11-13 13:47:25
parent: SPEC-0011
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Grains/Inbox/**", "backend/src/Squid.Api/Federation/**", "backend/src/Squid.Api/Posts/**", "backend/src/Squid.Protocols/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/**"]
blocks: ["TASK-0045"]
blockedBy: ["TASK-0043"]
inputArtifacts: ["docs/sdd/specs/SPEC-0011-federacao-p06.md", "docs/sdd/plan/plan.md", "specs/001-squid-mvp/contracts/activitypub.md", "specs/001-squid-mvp/data-model.md"]
outputArtifacts: ["backend/src/Squid.Grains/Inbox/**", "backend/src/Squid.Api/Federation/**", "backend/src/Squid.Api/Posts/**", "backend/tests/Squid.Tests/**"]
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

Entrega 4 de 9 da P-06: o caminho de **entrada** completo — `InboxGrain` com dedupe por
`activity.id`, a ordem obrigatória de verificação do contrato §3 (FR-040) e a aplicação das
atividades (`Create`, `Follow`/`Accept`, `Undo`, `Delete`, `Like`, `Flag`) no receptor. É a entrega
que faz uma atividade assinada por outra instância virar estado local sem derrubar o serviço.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `POST .../inbox` com assinatura válida, `Digest` confere e `Date` na janela → `202` e a
  atividade é aplicada: `Create` remoto vira post em B com autor remoto, conteúdo e data corretos
  — teste.
- AC-02: a mesma `activity.id` entregue de novo → `202` idêntico **sem reprocessar** (estado não
  duplica, nenhuma projeção muda) — teste (FR-035, contrato §7.4).
- AC-03: a ordem do contrato §3 é imposta: sem `Signature` ou assinatura inválida → `401`;
  `Digest` divergente → `401`; `Date` fora de ±5 min → `401`/`400`; corpo de 5 MB → `413`;
  aninhamento 100 → `400` — em todos, estado intacto e o serviço segue respondendo — teste
  (§7.5, §7.8).
- AC-04: taxa por domínio acima de `RateLimitPerMinute` (padrão 60) → `429` com `Retry-After` —
  teste.
- AC-05: `Follow` cria a relação seguidor e devolve `Accept`; `Undo(Follow)` remove a relação;
  `Delete` vira tombstone (post some do feed); `Like` é idempotente por ator+objeto (contagem não
  duplica — §7.14); `Flag` entra na fila de moderação existente — teste.
- AC-06: `membersOnly` só é aplicado quando o destinatário local é membro ativo do contêiner;
  `private` nunca é aplicado em claro; atividade de tipo desconhecido → `202` sem processar e
  métrica `federation.unknown_activity` incrementada — teste.
- AC-07: anexo de `Create` remoto é baixado com guarda SSRF e integridade conferida pelo SHA-256
  do blob; falha de download **não** rejeita o post (anexo fica ausente/marcado) — teste (GAP-44).
- AC-08: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1
  test` exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Grains/Inbox/**` — diretório novo: `InboxGrain` (janela de `activity.id`)
- `backend/src/Squid.Api/Federation/**` — pipeline do `POST .../inbox` (ordem §3, respostas)
- `backend/src/Squid.Api/Posts/**` — aplicação do `Create` remoto (append no `IActivityStore`,
  anexo/mídia, tombstone, `Like`, `Flag` via `IReportStore`)
- `backend/src/Squid.Protocols/**` — `ReceiveAsync` do adapter, decodificação tolerante (R-13)
- `backend/src/Squid.Service/**` — encanamento, métrica `federation.unknown_activity`
- `backend/tests/Squid.Tests/**` — testes das ACs
- produto **fora** dos `touches` desta task: qualquer lacuna levantada como dúvida, não corrigida aqui

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-42 | no | `Update`/`Announce`/`Join`/`Leave`/`Block` na entrada → `202` + métrica, sem aplicar estado |
| GAP-44 | no | mídia remota baixa pelo caminho de fetch com guarda SSRF, integridade pelo SHA-256; falha não rejeita o post; baixa remota não cobra cota do usuário local |
| GAP-46 | no | `Accept` de `Follow` é automático quando a política permite seguir (sem UI de aprovação) |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-13 13:47:25 | odev | task created from SPEC-0011 (entrega 4 de 9, InboxGrain + ordem §3 + aplicacao; blockedBy TASK-0043)

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
