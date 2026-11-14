<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0052 — PEM blindado: chave remota resolve de verdade (S-41)

```yaml
runId: R-20261113-01
taskId: TASK-0052
status: TASK_READY
agent: odev
phase: P-06
sequence: 1
createdAt: 2026-11-14 10:15:19
updatedAt: 2026-11-14 10:15:19
parent: SPEC-0011
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Protocols/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/**"]
blocks: ["TASK-0047"]
blockedBy: ["TASK-0051"]
inputArtifacts: ["docs/sdd/specs/SPEC-0011-federacao-p06.md", "docs/sdd/plan/plan.md", "specs/001-squid-mvp/contracts/activitypub.md", "docs/sdd/ideas/BACKLOG.md"]
outputArtifacts: ["backend/src/Squid.Protocols/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/**"]
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
nextAction: TASK_READY - aguardando dispatch DEV (fila: apos TASK-0051, antes da TASK-0047)
evidence: []
memoryBankFlags: []
```

## Intent

Correção focada de um defeito **provado empiricamente** na TASK-0051 e registrado como S-41:
`ProfileJsonLd.TryDecodePublicKeyPem` não decodifica PEM **blindado** multi-linha (o `Compact`
remove os espaços do armor antes do `Replace` do armor, então o base64 nunca valida) e
`HttpFederatedActorClient.KeyOf` depende dele — na inbox real, a chave pública de Mastodon/
Friendica não resolve e toda atividade assinada por elas cairia. Sem esta correção, a TASK-0047
(dois silos por HTTP real) e a TASK-0049 (interop) tropeçam no caminho de verificação de entrada.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: a decodificação aceita o PEM **blindado multi-linha** do contrato §1.1 (armor
  `-----BEGIN PUBLIC KEY-----`/`-----END PUBLIC KEY-----` com quebras de linha, como Mastodon e
  Friendica publicam) e devolve uma chave RSA utilizável — round-trip assinar→verificar com a
  chave decodificada — teste.
- AC-02: `HttpFederatedActorClient.KeyOf` resolve a chave RSA de um **documento de ator remoto
  realista** (fixture byte-realista do formato §1.1, com `publicKey.id`/`owner`/`publicKeyPem`
  blindado) e a verificação de entrada aceita a atividade assinada com ela (`202`) — teste.
- AC-03: regressões preservadas: PEM de **linha única** continua aceito e `squid:ed25519Key`
  (`publicKeyMultibase`) continua resolvendo — teste.
- AC-04: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1
  test` exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Protocols/**` — leitor de PEM (ordem do armor × compactação) e parsing do
  documento de ator
- `backend/src/Squid.Service/**` — `HttpFederatedActorClient.KeyOf` se precisar de ajuste de
  fiação (mínimo)
- `backend/tests/Squid.Tests/**` — testes das ACs (fixtures realistas do contrato §1.1)
- produto **fora** dos `touches` desta task: qualquer lacuna levantada como dúvida, não corrigida aqui

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| S-41 | no | defeito de produto pre-existente (nasceu na TASK-0044, provado na 0051) — esta task é a correção; a RCA é registrada no backlog, nenhuma AC anterior se abre |
| — | no | o import de kit sem cópia de instância (parked da 0051) é **S-42**, fora daqui |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-14 10:15:19 | odev | task created from SPEC-0011 (correcao S-41, entrega 9 de 12 da P-06; blockedBy TASK-0051, blocks TASK-0047; defeito provado pelo DEV da 0051 fora dos touches dele)

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
