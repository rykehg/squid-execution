<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0058 — Rotação de chave RK/RC da instância + kit reemitido (fecha S-39)

```yaml
runId: R-20261120-01
taskId: TASK-0058
status: TASK_READY
agent: odev
phase: P-07
sequence: 5
createdAt: 2026-11-20 21:50:58
updatedAt: 2026-11-20 21:50:58
parent: SPEC-0012
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Api/Crypto/**", "backend/src/Squid.Api/Recovery/**", "backend/src/Squid.Api/Admin/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/Crypto/**", "backend/tests/Squid.Tests/Recovery/**", "docs/runbooks/rotacionar-chave.md"]
blocks: ["TASK-0059"]
blockedBy: ["TASK-0054", "TASK-0056"]
inputArtifacts: ["docs/sdd/specs/SPEC-0012-backup-rotação-p07.md", "docs/features/05-arquitetura-consolidada.md", "specs/001-squid-mvp/research.md", "docs/runbooks/rotacionar-chave.md"]
outputArtifacts: ["backend/tests/Squid.Tests/Crypto/**", "backend/tests/Squid.Tests/Recovery/**", "docs/runbooks/rotacionar-chave.md", "docs/sdd/qa/QA-*"]
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
nextAction: TASK_READY - dispatch DEV após TASK-0054+0056 DONE (rotação RK/RC, fecha S-39)
evidence: []
memoryBankFlags: []
```

## Intent

SPEC-0012 §E — fecha o **S-39** (dono P-07 decidido pelo humano 2026-11-12): rotação de só
material de instância (decisão 6): nova `Squid:RecoveryKey` → `keyEpoch++` → reembrulho das
cópias `0009` e das `DK`s de backup → `RC` trocada → kit reemitido com as épocas antigas no
keyring (R-20: backup antigo nunca é reescrito) → runbook `rotacionar-chave.md` de
`PENDENTE-fase` para **EXECUTÁVEL**. Rotação de chave de ator fica fora (→ `S-46` no backlog).

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `POST /api/admin/recovery/rotate` (sessão de operador; sem sessão `401`, fora de
  `Instance:Operators` `403`) gera a nova época: `keyEpoch` incrementa, `RetiredAt` da época
  anterior é gravado (data-model §2.10) — teste.
- AC-02: após a rotação, as cópias `0009` e as `DK`s de backup reembrulhadas decifram com a nova
  `Squid:RecoveryKey`, e o material da época antiga **continua legível** pelo keyring do kit
  reemitido (R-20) — teste: backup da época antigo restaura depois da rotação.
- AC-03: nenhum arquivo de backup existente foi reescrito após a rotação (bytes/hash dos
  envelopes pré-rotação idênticos) — teste.
- AC-04: `RC` antiga deixa de verificar (`POST /api/recovery/kit/verify` com a RC velha falha) e
  o kit reemitido verifica e restaura a `RK` em teste (FR-006/FR-007 com o material novo) — teste.
- AC-05: cadeia derivada da nova `RK` é coerente: `HKDF("fed/ed25519-sign")`,
  `HKDF("fed/rsa-wrap")`, `HKDF("fed/backup-kek")` derivam do material novo e o runtime
  (assinatura de saída) funciona sem reinício forçado além do normal — teste.
- AC-06: a resposta da rotação entrega o kit reemitido **uma única vez**; material de chave não
  aparece em log, em segunda resposta nem em rota posterior — teste.
- AC-07: runbook `docs/runbooks/rotacionar-chave.md` **EXECUTÁVEL** (passos reais, verificados;
  nenhum valor de chave nem exemplo com formato real) e o índice `docs/runbooks/README.md`
  atualizado de `PENDENTE-fase` para o status real — revisão + evidência de execução.
- AC-08: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1
  test` exit 0; nenhum arquivo alterado fora dos `touches`; zero pacote NuGet novo.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Api/Crypto/**` — geração da nova `RK`, `keyEpoch++`, reembrulho `0009`+`DK`s, keyring
- `backend/src/Squid.Api/Recovery/**` — troca de `RC` + reemissão do kit
- `backend/src/Squid.Api/Admin/**` — rota `recovery/rotate` com regra de operador
- `backend/src/Squid.Service/**` — wiring
- `backend/tests/Squid.Tests/{Crypto,Recovery}/**` — testes dos ACs
- `docs/runbooks/rotacionar-chave.md` + `docs/runbooks/README.md` — EXECUTÁVEL (fecha S-39)
- S-42 (import com cópia `0009`) é TASK-0059; S-46 (rotação de ator) é backlog
- produto fora dos `touches`: qualquer lacuna é `D-01` para o ODEV

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-54 | no | rotação só de material de instância; rotação de chave de ator fica fora → S-46 no backlog — decisão humana 2026-11-20 21:07:26 |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-20 21:50:58 | odev | task created from SPEC-0012 (rotação só instância: keyEpoch++ + reembrulho 0009/DKs + RC nova + kit keyring; fecha S-39; GAP-54; blockedBy 0054+0056)

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
