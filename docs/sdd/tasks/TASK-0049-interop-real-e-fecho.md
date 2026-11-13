<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0049 — Interop real Mastodon/Friendica + facet de fecho do DoD

```yaml
runId: R-20261113-01
taskId: TASK-0049
status: TASK_READY
agent: odev
phase: P-06
sequence: 1
createdAt: 2026-11-13 13:47:25
updatedAt: 2026-11-13 13:47:25
parent: SPEC-0011
authority: SPEC
parallel: false
touches: ["backend/tests/Squid.Tests/Federation/**", "backend/src/Squid.Protocols/**", "backend/src/Squid.Api/Federation/**", "backend/src/Squid.Grains/**", "docs/runbooks/federacao-interop.md"]
blocks: []
blockedBy: ["TASK-0048"]
inputArtifacts: ["docs/sdd/specs/SPEC-0011-federacao-p06.md", "docs/sdd/plan/plan.md", "docs/runbooks/federacao-interop.md", "specs/001-squid-mvp/contracts/activitypub.md"]
outputArtifacts: ["backend/tests/Squid.Tests/Federation/**", "docs/sdd/qa/QA-*", "docs/runbooks/federacao-interop.md"]
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
nextAction: TASK_READY - aguardando dispatch DEV (execucao com pares reais = gate humano: humano presente validando logs do par)
evidence: []
memoryBankFlags: []
```

## Intent

Entrega 9 de 9 e **última** da P-06: interop real Mastodon e Friendica em Docker local, seguindo
o manual da TASK-0048 com o humano junto (decisões 3 e 6 da SPEC-0011 — gate humano: o humano
fornece/valida os pares e os logs do par), mais a facet de fecho do DoD do Sprint 5 em cadeia.
Com QA PASSED a fase fecha (consolidação + snapshot + authority CODE). Correção de produto só
entra aqui quando **provada** pelo log real do par, cada uma registrada em `## Log`.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: **Mastodon de referência** (Docker local) exibe o post público assinado por nós e o log
  do par **não** contém `signature verification failed` (SC-016, §7.11) — evidência do log do par
  + do post exibido, coletada com o humano presente.
- AC-02: **Friendica de referência** idem: post público exibido, log sem falha de assinatura,
  `Digest` SHA-256 e `keyId` terminando em `#main-key` (§7.12) — evidência idem.
- AC-03: **inbound real**: atividade assinada por Mastodon e por Friendica aceita com `202`; a
  mesma atividade sem `Signature` recebe `401` sem processar (§7.13) — teste + evidência.
- AC-04: facet de fecho do DoD do Sprint 5 em cadeia com os pares Squid: post público de A
  aparece em B; `membersOnly` não aparece; dedupe rejeita entrega repetida; peer derrubado não
  trava o envio — teste (§7 itens 1–7 reusando a facet da TASK-0047).
- AC-05: o runbook `federacao-interop.md` é atualizado com os passos que a execução real provou
  (qualquer correção de comando/config) — artefato vivo, sem passo não verificado — teste.
- AC-06: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1
  test` exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/tests/Squid.Tests/Federation/**` — facet de fecho e testes de inbound real
- `backend/src/Squid.Protocols/**`, `backend/src/Squid.Api/Federation/**`,
  `backend/src/Squid.Grains/**` — **só** correções provadas pelo log real do par, cada uma
  registrada em `## Log`
- `docs/runbooks/federacao-interop.md` — correções de passo provadas pela execução
- produto fora dos `touches`: qualquer lacuna não provada é `D-01` para o ODEV

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-47 | no | pares reais em Docker local na máquina do humano (decisão 6); execução conjunta = gate humano registrado no `humanGate` no dispatch; instância pública é fora desta task |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-13 13:47:25 | odev | task created from SPEC-0011 (entrega 9 de 9 e ULTIMA da P-06, interop real + facet de fecho; blockedBy TASK-0048; execucao com pares reais = gate humano)

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
