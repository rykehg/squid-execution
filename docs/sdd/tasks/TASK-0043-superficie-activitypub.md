<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0043 — Superfície ActivityPub: ator completo, coleções, objeto, contêiner

```yaml
runId: R-20261113-01
taskId: TASK-0043
status: TASK_READY
agent: odev
phase: P-06
sequence: 1
createdAt: 2026-11-13 13:47:25
updatedAt: 2026-11-13 13:47:25
parent: SPEC-0011
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Api/Discovery/**", "backend/src/Squid.Api/Federation/**", "backend/src/Squid.Protocols/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/**"]
blocks: ["TASK-0044"]
blockedBy: ["TASK-0042"]
inputArtifacts: ["docs/sdd/specs/SPEC-0011-federacao-p06.md", "docs/sdd/plan/plan.md", "specs/001-squid-mvp/contracts/activitypub.md"]
outputArtifacts: ["backend/src/Squid.Api/Discovery/**", "backend/src/Squid.Api/Federation/**", "backend/tests/Squid.Tests/**"]
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

Entrega 3 de 9 da P-06: a instância **expõe** o contrato `activitypub.md` §1 — documento de ator
completo (fecha a lacuna `S-26` do backlog: `inbox`/`outbox`/`followers`/`following`), coleções
paginadas, objeto, contêiner, nodeinfo e negociação de Accept. Os endpoints `POST .../inbox`
nascem aqui rejeitando sem assinatura verificável (`401`); a aplicação de atividades vem na
TASK-0044. Nenhuma UI (P-08).

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `GET /users/{name}` devolve o documento do ator do contrato §1.1 — `publicKey` objeto
  único `#main-key`, `squid:ed25519Key`, e **`inbox`, `outbox`, `followers`, `following`** — teste.
- AC-02: `GET /users/{name}/outbox`, `/followers` e `/following` devolvem `OrderedCollection`
  paginada com `first`/`next` (nunca o conjunto inteiro) — teste.
- AC-03: `GET /objects/{id}` e `GET /c/{slug}` (+ `GET /c/{slug}/outbox` primeira página e
  `GET /c/{slug}/members` com visibilidade) devolvem JSON-LD com `@context` e os campos
  `squid:kind`/`squid:slug`/`squid:policy` — teste.
- AC-04: `GET /.well-known/nodeinfo` devolve metadados de software/versão — teste.
- AC-05: `POST /users/{name}/inbox` e `POST /c/{slug}/inbox` existem e respondem `401` sem
  assinatura verificável, sem processar o corpo (estado intacto) — teste.
- AC-06: a resposta exige/aceita `Accept: application/activity+json` e
  `application/ld+json; profile="https://www.w3.org/ns/activitystreams"`; `webfinger` ganha
  `self` ao ator canônico e **não** ganha `rel=profile` (P-08) — teste.
- AC-07: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1
  test` exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Api/Discovery/**` — documento de ator ampliado, `self` do webfinger
- `backend/src/Squid.Api/Federation/**` — diretório novo: inbox (POST), outbox/followers/following
  (GET), objeto, contêiner+coleções, nodeinfo
- `backend/src/Squid.Protocols/**` — serialização JSON-LD na borda (ADR-11: JSON-LD nunca no domínio)
- `backend/src/Squid.Service/**` — mapeamento das rotas novas
- `backend/tests/Squid.Tests/**` — testes das ACs
- produto **fora** dos `touches` desta task: qualquer lacuna levantada como dúvida, não corrigida aqui

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| S-26 | no | `inbox`/`outbox`/`followers`/`following` entram **nesta task** (checagem do backlog pedida ao cortar a P-06); `rel=profile` continua P-08 e não entra aqui |
| — | no | inbox nasce rejeitando sem assinatura verificável (`401`); aplicar atividades é da TASK-0044 — a entrega é funcional pelo caminho de leitura |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-13 13:47:25 | odev | task created from SPEC-0011 (entrega 3 de 9, fecha S-26 (inbox/outbox/followers/following); blockedBy TASK-0042)

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
