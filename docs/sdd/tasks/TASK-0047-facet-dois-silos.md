<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0047 — Facet de dois silos (A→B, contrato §7)

```yaml
runId: R-20261113-01
taskId: TASK-0047
status: TASK_READY
agent: odev
phase: P-06
sequence: 1
createdAt: 2026-11-13 13:47:25
updatedAt: 2026-11-13 13:47:25
parent: SPEC-0011
authority: SPEC
parallel: false
touches: ["backend/tests/Squid.Tests/Federation/**"]
blocks: ["TASK-0048"]
blockedBy: ["TASK-0046"]
inputArtifacts: ["docs/sdd/specs/SPEC-0011-federacao-p06.md", "docs/sdd/plan/plan.md", "specs/001-squid-mvp/contracts/activitypub.md"]
outputArtifacts: ["backend/tests/Squid.Tests/Federation/**"]
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

Entrega 7 de 9 da P-06: harness de integração com **dois silos** A→B em fluxo HTTP completo,
reusando as rotas das tasks 0041..0046, cobrindo os itens 1–9, 14 e 15 do contrato §7 (DoD do
Sprint 5 em cadeia, sem pares externos — os pares reais são da TASK-0049). É facet **tests-only**:
lacuna de produto levantada aqui vira `D-01` para o ODEV, não correção silenciosa.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: post **público** de A aparece em B com autor, mídia e data corretos (§7.1/SC-007) —
  teste.
- AC-02: post `membersOnly` **não** chega a seguidor não membro de B (§7.2/SC-005) e `private`
  não gera **nenhuma** entrega (§7.3) — teste.
- AC-03: entrega duplicada da mesma `activity.id` não duplica estado (§7.4) e assinatura inválida
  ou `Digest` divergente → `401` com estado do receptor intacto (§7.5) — teste.
- AC-04: B indisponível: A publica normalmente e a entrega fica `Pending` com backoff (§7.6);
  política alterada entre enfileiramento e envio → `Cancelled`, nada enviado (§7.7) — teste.
- AC-05: payload de 5 MB → `413`, aninhamento 100 → `400`, serviço de B continua respondendo
  (§7.8); `webfinger` de handle inexistente → `404` sem revelar atores parecidos (§7.9) — teste.
- AC-06: atributo de trace `peer.domain` presente em 100% das entregas e sem PII (§7.10/
  SC-015) — teste.
- AC-07: `Like` duplicado do mesmo ator não duplica contagem (§7.14) e resposta federada preserva
  `inReplyTo` enquanto conversa privada não gera entrega (§7.15, R-11) — teste.
- AC-08: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1
  test` exit 0; nenhum arquivo alterado fora dos `touches`. Se a facet acusar lacuna de produto,
  é `D-01` para o ODEV — o DEV desta task só escreve teste.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/tests/Squid.Tests/Federation/**` — harness de dois silos (duas instâncias em processo,
  bases distintas) e os testes das ACs
- produto **fora** dos `touches` desta task: qualquer lacuna levantada como dúvida, não corrigida aqui

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| — | no | dois silos em processo no teste (sem Docker) — Docker/compose é caminho do runbook (TASK-0048) e da interop (TASK-0049), não da facet |
| GAP-47 | no | validação com pares reais é da TASK-0049 com gate humano; aqui só pares Squid |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-13 13:47:25 | odev | task created from SPEC-0011 (entrega 7 de 9, facet dois silos tests-only, contrato §7 itens 1-9/14/15; blockedBy TASK-0046)

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
