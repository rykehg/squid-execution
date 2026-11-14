<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0050 — Pares confiáveis: `Federation:TrustedPeerHosts` nas guardas de federação

```yaml
runId: R-20261113-01
taskId: TASK-0050
status: TASK_READY
agent: odev
phase: P-06
sequence: 1
createdAt: 2026-11-13 22:45:47
updatedAt: 2026-11-13 22:45:47
parent: SPEC-0011
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Api/Posts/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/**"]
blocks: ["TASK-0047"]
blockedBy: ["TASK-0044", "TASK-0046"]
inputArtifacts: ["docs/sdd/specs/SPEC-0011-federacao-p06.md", "docs/sdd/plan/plan.md", "docs/sdd/tasks/TASK-0044-inbox-verificacao-e-aplicacao.md"]
outputArtifacts: ["backend/src/Squid.Api/Posts/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/**"]
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
nextAction: TASK_READY - aguardando dispatch DEV (fila: apos TASK-0046)
evidence: []
memoryBankFlags: []
```

## Intent

Entrega extra da P-06 (cortada em 2026-11-13 22:45:47 a partir do parked do DEV da TASK-0044):
`Federation:TrustedPeerHosts` — allowlist **exata** e **vazia por padrão** que libera
loopback/rede privada **só** nos clientes de federação (resolução de ator, download de mídia,
entrega ao par). Sem ela, o harness de dois silos (TASK-0047) e o runbook/interop (0048/0049)
seriam recusados pela própria guarda SSRF da fase. Decisão do ODEV no nível especificação:
SPEC-0011 §C + GAP-49. O unfurl de link **não** muda.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: com `Federation:TrustedPeerHosts` ausente ou vazio (default), os clientes de federação
  continuam **recusando** alvo em loopback e rede privada (comportamento de hoje byte-idêntico)
  — teste.
- AC-02: com um host do par nomeado na lista, a resolução de ator e o download de mídia daquele
  par são **aceitos** mesmo em loopback/rede privada; qualquer host **fora** da lista continua
  recusado (inclusive na mesma porta) — teste.
- AC-03: a allowlist vale **só** para os clientes de federação (ator, mídia, entrega ao par): o
  unfurl de link continua bloqueando rede privada mesmo com a lista nomeada — teste.
- AC-04: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1
  test` exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Api/Posts/**` — `SsrfGuard` ganha a noção de alvo confiável sem afetar o
  unfurl; `RemoteMediaClient` usa a allowlist
- `backend/src/Squid.Service/**` — `HttpFederatedActorClient` e o cliente de entrega do par
  (pump da TASK-0046) usam a allowlist; leitura de `Federation:TrustedPeerHosts`
- `backend/tests/Squid.Tests/**` — testes das ACs
- produto **fora** dos `touches` desta task: qualquer lacuna levantada como dúvida, não corrigida aqui

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-49 | no | allowlist exata, vazia por padrão, só clientes de federação; unfurl intocado; runbook (0048) e harness (0047) nomeiam os pares locais; produção = gate humano (SPEC-0011 §C) |
| — | no | casa com o parked "escape de SSRF para pares em loopback/Docker local" do DEV da TASK-0044 (log 2026-11-13) — o restante do parked (média membersOnly em `GET /api/media`) virou S-40 no backlog |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-13 22:45:47 | odev | task created from SPEC-0011 (entrega extra 10 de 10 da P-06, cortada do parked SSRF do DEV da TASK-0044; GAP-49; blockedBy TASK-0044+TASK-0046, blocks TASK-0047; fila: apos a TASK-0046)

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
