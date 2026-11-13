<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0042 — Chave de assinatura em runtime (GAP-09) + assinar/verificar HTTP

```yaml
runId: R-20261113-01
taskId: TASK-0042
status: TASK_READY
agent: odev
phase: P-06
sequence: 1
createdAt: 2026-11-13 13:47:25
updatedAt: 2026-11-13 13:47:25
parent: SPEC-0011
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Api/Crypto/**", "backend/src/Squid.Grains/Actors/**", "backend/src/Squid.Protocols/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/**"]
blocks: ["TASK-0043"]
blockedBy: ["TASK-0041"]
inputArtifacts: ["docs/sdd/specs/SPEC-0011-federacao-p06.md", "docs/sdd/plan/plan.md", "specs/001-squid-mvp/contracts/activitypub.md", "specs/001-squid-mvp/data-model.md"]
outputArtifacts: ["backend/src/Squid.Api/Crypto/**", "backend/src/Squid.Grains/Actors/**", "backend/src/Squid.Protocols/**", "backend/tests/Squid.Tests/**"]
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

Entrega 2 de 9 da P-06: fecha o **GAP-09** (decisão 2 da SPEC-0011, humana) — `RK` como segredo
de ambiente no boot, desembrulho da RSA privada do ator só em memória no `ActorGrain` — e entrega
a biblioteca de assinatura HTTP: saída draft-cavage `rsa-sha256` (ADR-28, contrato §5) e verificação
de entrada `rsa-sha256`/`hs2019` + RFC 9421 (contrato §3, ordem fixa). Nenhuma rota de inbox ainda:
aqui é chave + biblioteca com round-trip provado.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: sem `Squid:RecoveryKey` a instância **sobe**, segue servindo leitura e escrita local, e
  registra aviso/métrica de que a saída federada está desabilitada — teste.
- AC-02: com `Squid:RecoveryKey` válida, o `ActorGrain` desembrulha a RSA privada do ator
  (`HKDF(RK, "fed/rsa-wrap")`) na ativação e a mantém só em memória: nenhuma rota, log ou
  persistência devolve material privado — teste.
- AC-03: `Squid:RecoveryKey` errada → falha auditável na ativação do grain, sem vazar material
  em mensagem, log ou resposta — teste.
- AC-04: requisição de saída assinada bate com o formato do contrato §5 — `keyId` terminando em
  `#main-key`, `algorithm="rsa-sha256"`, `headers="(request-target) host date digest"`,
  `Digest: SHA-256=<base64>`, `Date` em HTTP-date GMT, GET assinando `(request-target) host date`
  — e um verificador independente aceita o par assinar→verificar — teste.
- AC-05: a verificação de entrada aceita draft-cavage `rsa-sha256` e `hs2019` e RFC 9421 (RSA e
  Ed25519), e rejeita: `Date` fora de ±5 min, `Digest` divergente, assinatura de chave que não é a
  publicada no `keyId` — teste.
- AC-06: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1
  test` exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Api/Crypto/**` — carga de `Squid:RecoveryKey`, desembrulho de `RK`/RSA
- `backend/src/Squid.Grains/Actors/**` — desembrulho na ativação, chave só em memória
- `backend/src/Squid.Protocols/**` — assinar (saída) e verificar (entrada) draft-cavage + RFC 9421
- `backend/src/Squid.Service/**` — config/encanamento de `Squid:RecoveryKey`, aviso de saída desabilitada
- `backend/tests/Squid.Tests/**` — testes das ACs
- produto **fora** dos `touches` desta task: qualquer lacuna levantada como dúvida, não corrigida aqui

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-09 | no | RESOLVED (HUMAN) — decisão 2 da SPEC-0011 (2026-11-13 11:48:04): `Squid:RecoveryKey` no boot, desembrulho em memória, sem rota de reidratação, kit = recuperação, produção = gate humano |
| — | no | sem `RK` a saída fica desabilitada e a instância continua funcional localmente (AC-01) — nunca é motivo de queda |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-13 13:47:25 | odev | task created from SPEC-0011 (entrega 2 de 9, fecha GAP-09 por decisao humana; blockedBy TASK-0041)

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
