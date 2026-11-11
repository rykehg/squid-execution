<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: VEREDITO
- owner: qa
- lastVerified: 2026-11-10 23:50

# QA-0031 — Denúncia, fila admin e suspensão (FR-028/FR-008)

- task: TASK-0031
- spec: SPEC-0009
- runId: R-20261109-01
- verdict: PASSED
- qaAttempt: 1/2

Normative reading used: `docs/sdd/specs/SPEC-0009-publicacao-feed-postagem.md` section **I**
(idempotente por `(post, ator)`, fila paginada do operador, `resolve keep|remove|ban` com
`justification` obrigatória `422`, `remove` executa a remoção da seção H em nome do operador,
`ban` suspende o ator, tudo auditado com a palavra `Resolve` e autor/alvo/justificativa;
`reinstate` fora — sem rota no contrato, suspensão por API irreversível nesta fase) + `Gaps`
GAP-27 (palavra `Resolve`; `reinstate` OUT) e GAP-28 (repetição devolve a mesma denúncia) +
`Rules and edge cases` (justificação vazia 422; sem sessão 401; não-operador 403; ator suspenso
não publica e lê; post removido some de toda leitura); `data-model.md` §3.1 (`Suspended` não
publica, leitura segue permitida) e §2.11 (`RemoveContent`/`ChangeRole`); `spec.md` FR-008
(autor, alvo e justificativa em toda ação administrativa) e FR-028; contrato `http-api.md` §5
(`POST /api/posts/{id}/report`) e §7 (fila, `resolve`, `/suspend`, operador-only, `422`).

**Verdict: PASSED** — the five ACs are proven by executed scenarios, no BLOCKER finding, and
every DEV decision the dispatch asked me to verify was verified **by execution, not by reading**:

1. **`resolve remove` reuses the extracted `RemoveContentAsync` (no fork of the tombstone)** —
   my parity scenario runs the operator's `resolve action=remove` and the author's own `DELETE`
   side by side and compares the effects: both 404s are the **raw bytes** of a never-existing id
   (no `traceId`), both tombstones carry `DeletedAt`/`TakedownReason` in the same document, both
   `Activity: Delete` events follow their `Create` on the global sequence, both `RemoveContent`/
   `Allow` rows carry the tombstone JSON with the same `PolicyVersion` — the only intended
   difference being the actor (operator URI vs author URI) — and both posts leave the projected
   feed. Static: `AdminEndpoints.cs:272` calls `PostEndpoints.RemoveContentAsync`
   (`PostEndpoints.cs:371`), the single path `RemoveAsync` itself uses. Mutation **M4**
   (the `if (post is not null)` of the remove branch neutralized) kills the DEV remove test **and**
   my parity test (`20261110-234057-test-filter.log`, `Com falha: 2`);
2. **`ban` and the direct `POST /api/admin/actors/{*target}/suspend` both drive
   `IActorStatusStore.SuspendAsync` — no duplicate store** — grep finds exactly one
   implementation (`SqliteActorStatusStore.cs:42`) and two call sites
   (`AdminEndpoints.cs:281` ban, `:352` direct); executed: after `resolve ban` the state is
   `ActorStatus.Suspended`, the author's **next** publish answers `403 policy_denied` with a
   `Deny` row under `Action: Post`, and reading the very reported post still answers **200**
   (§3.1). Mutation **M5** (`await statuses.SuspendAsync(authorHandle);` removed) kills
   `ReportResolveTests.Ban_suspends...` (`20261110-234125-test-filter.log`, `Com falha: 1`);
3. **trail words** — `Resolve` for the resolution with the **report id** as `resourceId` and
   `action/justification/postId/reporter/author` inside the reason JSON (FR-008),
   `ChangeRole` for both suspension paths — all executed by DEV's tests querying the live trail;
   mutation **M3** (`ResolveDecision` → `ResolveX`) kills the three `ReportResolveTests`
   (`20261110-234028-test-filter.log`, `Com falha: 3`);
4. **report ladder 401 → 404 byte-identical → 422 empty reason → 201 first / 200 repeat** —
   executed in DEV's `ReportRouteTests` (raw byte comparison against the read route's 404, for
   invisible **and** absent ids) plus my independent idempotency scenario; mutation **M1**
   (the `(post, reporter)` check of `SqliteReportStore.CreateAsync` neutralized → GAP-28 off)
   kills both (`20261110-233932-test-filter.log`, `Com falha: 2`);
5. **queue: `post`/`status` filters, `400` on invalid input, first resolution wins, one
   ETag-guarded document with a declared `shortcut:`** — filters/paging/400 executed by
   `AdminReportsQueueTests`; first-resolution-wins executed by `ReportResolveTests.Keep_...`
   (a repeat `remove` answers the stored `keep` and writes no second `Resolve` row);
   the single-document CAS store with the declared `shortcut:` is `SqliteReportStore.cs:41-44,60-67`;
   mutation **M2** (the `Instance:Operators` gate neutralized) kills three tests including the
   queue's `403 policy_denied` (`20261110-233958-test-filter.log`, `Com falha: 3`);
6. **queue exposure = `postId` only** — `ReportView.cs:11-23` carries id/postId/reporter/author/
   reason/status and never post content: the operator cannot read a private-container post from
   the queue (the parked idea of the dispatch, verified, **not** a defect).

## What is proven

| AC | Scenario (executed) | Script | Result |
| --- | --- | --- | --- |
| AC-01 | First `POST /api/posts/{id}/report` → `201` with id/postId/reporter/author/reason/status; repeat by the same actor → `200` with the **same** id, the first reason untouched and the queue count for the post unmoved; a **different** reporter on the same post → `201` with a second row (the key is the pair); no session → `401 not_authenticated`; empty/whitespace reason → `422 validation_failed` with no row; invisible post → `404` **raw-byte equal** to the read route's 404 and to an absent id, no `traceId`. DEV: `ReportRouteTests` (2). QA: `QaTask0031Tests.The_repeat_of_a_denunciation_is_the_same_report_and_the_pair_key_stops_a_duplicate`. Can-fail **M1** | `backend/tests/Squid.Tests/Moderation/{ReportRouteTests,QaTask0031Tests}.cs` · filter `~Squid.Tests.Moderation` → `20261110-233755-test-filter.log` exit 0, 9/9 · red `20261110-233932` (M1, 2 falhas) | PASS |
| AC-02 | Queue behind the P-03 gate: no session → `401`; non-operator session → `403 policy_denied`; operator → `200` with `post` filter matching the canonical id **and** its opaque tail, `status=open/resolved` selecting correctly, `limit/offset` paging without repeating a row, and `?status=bogus` / `?limit=0` / `?offset=-1` → `400 validation_failed` (never a silent match-all). DEV: `AdminReportsQueueTests`. Can-fail **M2** | same directory · red `20261110-233958` (M2, 3 falhas) | PASS |
| AC-03 | Ladder: gate 401/403 → unknown report `404` → absent/empty/whitespace `justification` → `422` (no row, report still `open`) → unknown `action` → `400`; `keep` closes with **one** `Resolve` row carrying operator as author, report id as target and `action/justification/postId/reporter/author` in the reason; repeat answers the stored resolution (first wins) with no second row and the post untouched; `remove` executes the section-H removal **in the operator's name** (tombstone + `Activity: Delete` + `RemoveContent`/`Allow` + projection drop) beside its `Resolve` row; `ban` resolves with its `Resolve` row. **Parity hard spot:** the `remove` effects are compared raw against the author's own `DELETE` (404 bytes == ghost bytes, same tombstone shape, same trail shape, same projection drop). DEV: `ReportResolveTests` (3). QA: `QaTask0031Tests.Resolve_remove_leaves_the_very_same_tombstone_effects_as_the_authors_own_delete`. Can-fail **M3** (trail word), **M4** (remove path) | same directory · reds `20261110-234028` (M3, 3 falhas), `20261110-234057` (M4, 2 falhas) | PASS |
| AC-04 | State test: `resolve ban` leaves `IActorStatusStore` = `Suspended`; the author's next `POST /api/containers/{slug}/posts` → `403 policy_denied` with a `Deny` row under `Action: Post` on the trail; `GET /api/posts/{id}` still `200` (§3.1). Direct route `POST /api/admin/actors/{handle\|percent-encoded-id}/suspend`: gate 401/403, ghost actor `404`, malformed id `400`, empty justification `422`, `200 {id,status:"suspended"}` both spellings, one `ChangeRole` row per call with author/target/justification. DEV: `ReportResolveTests.Ban_suspends...`, `ActorSuspendRouteTests`. Can-fail **M5** | same directory · red `20261110-234125` (M5, 1 falha) | PASS |
| AC-05 | **Gate:** `sdd-test.ps1 test` → `20261110-234659-test-gate.log` with `exit: 0` and `Com falha: 0, Aprovado: 428, Total: 428` read **inside** the log (S-22), 0 `[FAIL]` lines — 426 pre-QA + 2 QA tests. **Scope:** git delta vs `QA-0031-git-baseline.txt` → only QA-0031 artifacts added, 0 removed, nothing outside `touches`; the two files the mutation driver edited (`AdminEndpoints.cs`, `SqliteReportStore.cs`) hash back to PRE with bumped mtimes; `QA-MUTATION` residue in `backend/` = **0**; temp backups `qa0031-*` = 0. Checkers: `qa-check-task-0031.ps1` and `.sh` (8 checks each) | gate + scope scan + `qa-check-task-0031.ps1` / `.sh` | PASS (suite Linux: unverified) |

Every AC's tests were killed by at least one mutation — no assertion in this task is decoration.

## Scripts

| Script | Language / runner | What it does |
| --- | --- | --- |
| `backend/tests/Squid.Tests/Moderation/QaTask0031Tests.cs` | xunit via `sdd-test.ps1 test -TestFilter` | The 2 QA scenarios: the `resolve remove` vs author's `DELETE` parity (404 bytes, tombstone, trail, event, projection) and GAP-28 idempotency with the pair key (201 → 200 same id, count unmoved, second reporter = second row) |
| `backend/tests/Squid.Tests/Moderation/*.cs` (DEV) | xunit (re-executed, never assumed) | The 7 DEV tests of the task, executed in the filter (9/9) and in the final gate (428/428) |
| `docs/sdd/qa/qa-mutate-task-0031.ps1` | pwsh (gate `sdd-test.ps1`) | **5** can-fail cycles (M1..M5): SHA256 PRE (S-16) → one `QA-MUTATION` edit with pattern-count exactly 1 → red run classified **inside the log file** (S-22) → byte-exact restore from memory + mtime bump (S-23) → hash = PRE → residue 0 → one GREEN over the restored sources |
| `docs/sdd/qa/qa-check-task-0031.ps1` / `.sh` | pwsh / bash (WSL) | 8 evidence checks each (gate log, filter log, mutation summary, residue, git scope, control block, verdict doc, temp backups) |
| `docs/sdd/qa/QA-0031-mutations.log` | artefato | Output of the five cycles: `M1..M5 PRE/RUN/RED-IN-LOG/RESTORE-OK` + `GREEN wrapper=0 logExit=0 aprovado=9 falha=0` + `SUMMARY cycles=5 failed=0 residue-QA-MUTATION=0 temp-backups=0` |
| `docs/sdd/qa/QA-0031-git-baseline.txt` | artefato | `git status --porcelain` captured at QA start (before QA wrote; the QA-0030 block re-verified by file mtimes after the first transcription dropped it) |

## How to reproduce

Prerequisites: Windows com `dotnet` 10 e o repositório em `C:\dev\squid-squad-net\squid-execution`
(nenhuma instalação, nenhum serviço externo — os testes sobem host local em porta dinâmica e só
falsificam as costuras de rede).

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~Squid.Tests.Moderation"   # esperado: exit 0, Aprovado 9, Total 9
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0031.ps1                                                  # esperado: exit 0, SUMMARY cycles=5 failed=0 residue-QA-MUTATION=0 temp-backups=0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test                                                    # esperado: exit 0, Com falha 0 / Aprovado 428 / Total 428
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0031.ps1                                                   # esperado: SUMMARY total=8 fails=0, exit 0
bash docs/sdd/qa/qa-check-task-0031.sh                                                                                                   # esperado: SUMMARY total=8 fails=0, exit 0 (WSL)
```

## Last run

| Date/Time | Command | Exit | Output (max 10 lines) |
| --- | --- | --- | --- |
| 2026-11-10 23:26:37 | `sdd-test.ps1 test` (gate de entrada, antes da QA escrever) | 0 | `20261110-232637-test-gate.log` → `exit: 0` · `Com falha: 0, Aprovado: 426, Total: 426` (4 m 36 s) · 0 linhas `[FAIL]` |
| 2026-11-10 23:29:35 | `sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Moderation` (1a) | 1 | `20261110-232935-test-filter.log` → `CS0246 SqliteActivityLog` — erro do teste da QA (using faltando), corrigido |
| 2026-11-10 23:30:41 | idem (2a) | 1 | `20261110-233041-test-filter.log` → `CS0103 owner` — erro do teste da QA (parâmetro renomeado), corrigido |
| 2026-11-10 23:33:26 | idem (3a) | 1 | `20261110-233326-test-filter.log` → `Com falha: 1, Aprovado: 8` — asserção da QA errada (`KeyNotFoundException` no casing do tombstone); valor derivado do artefato (`SqliteGrainStorage.cs:94` serializa com opções padrão → `DeletedAt`/`TakedownReason`), corrigido |
| 2026-11-10 23:37:55 | idem (4a) | 0 | `20261110-233755-test-filter.log` → `exit: 0` · `Com falha: 0, Aprovado: 9, Total: 9` (16 s) |
| 2026-11-10 23:39:32–23:41:52 | `qa-mutate-task-0031.ps1` (5 ciclos + GREEN) | 0 | `QA-0031-mutations.log` → `M1..M5 RED-IN-LOG` + `RESTORE-OK hash=PRE residue=0` · vermelhos `20261110-233932` (2) / `233958` (3) / `234028` (3) / `234057` (2) / `234125` (1) · `GREEN … aprovado=9 falha=0` (`234152`) · `SUMMARY cycles=5 failed=0 residue-QA-MUTATION=0 temp-backups=0` |
| 2026-11-10 23:46:59 | `sdd-test.ps1 test` (gate final, AC-05) | 0 | `20261110-234659-test-gate.log` → `exit: 0` · `Com falha: 0, Aprovado: 428, Ignorado: 0, Total: 428` (4 m 36 s) · 0 linhas `[FAIL]` |
| 2026-11-10 23:45 / 23:57 | varredura de escopo (git vs baseline + resíduo + backups + mtimes) | 0 | `adicionadas=8` (todos artefatos QA-0031) `removidas=0` · resíduo `QA-MUTATION` = 0 · backups `qa0031-*` = 0 · hashes `AdminEndpoints.cs 56C976366CD4` e `SqliteReportStore.cs BAFDA5773615` = PRE do driver (restaurados com bump de mtime) |
| 2026-11-10 23:54 | `wsl bash -lc "bash docs/sdd/qa/qa-check-task-0031.sh"` (1a) | 1 | bugs do **script da QA**: profundidade do `repo` (`../..` em vez de `../../..`), `TEMP` indefinido com `set -u` e `grep --` com argumento errado — corrigidos |
| 2026-11-10 23:55 | `wsl bash -lc "bash docs/sdd/qa/qa-check-task-0031.sh"` (2a) | 0 | `QA-0031-check-sh.log` → `C1..C8 PASS` · `SUMMARY total=8 fails=0` |
| 2026-11-10 23:57 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-state.ps1 check docs/sdd/tasks/TASK-0031-denuncia-admin-suspensao.md` | 0 | `check: OK - log entries 16, evidence rows 20` (23 após as últimas 3 linhas) |
| 2026-11-10 23:57 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0031.ps1` (re-execução final) | 0 | `QA-0031-check.log` → `C1..C8 PASS` · `SUMMARY total=8 fails=0` |
| 2026-11-10 23:57 | `wsl bash -lc "bash docs/sdd/qa/qa-check-task-0031.sh"` (re-execução final) | 0 | `QA-0031-check-sh.log` → `C1..C8 PASS` · `SUMMARY total=8 fails=0` (stderr do WSL só o aviso do podman-machine) |

## Findings

| # | Severity | Where | What breaks | Evidence |
| --- | --- | --- | --- | --- |
| — | — | — | **Nenhum achado de produto desta task.** Os 5 ACs passaram e cada um provou pode-falhar: M1 → AC-01, M2 → AC-02, M3/M4 → AC-03, M5 → AC-04 | `QA-0031-mutations.log` |

Notas (não são defeitos):

- **Gate 428 = 426 (pré-QA) + 2 (testes desta QA)**: o gate de entrada (426/426) foi medido
  antes de qualquer arquivo da QA existir e está lido no log; o gate final inclui os 2
  `QaTask0031Tests`.
- **Três iterações vermelhas minhas (23:29/23:30/23:33)**: erros do próprio teste da QA
  (using, variável, casing), corrigidos antes do verde — registrados, não escondidos; nenhum
  produto foi alterado por eles.
- **`reason` da denúncia vs conteúdo do post**: a fila expõe o texto do motivador (`reason`),
  nunca o conteúdo do post — `ReportView.cs:11-23` (parked idea do dispatch confirmada como
  comportamento, não defeito).
- **Fila em documento único**: `shortcut:` declarado em `SqliteReportStore.cs:41-44` com o mesmo
  estilo do trail; CAS com `ETag` em create e update (`:60-67`, `:105-111`).

## Not covered, and why

- **Suíte do produto em Linux** — o WSL desta máquina não tem `dotnet` (instalar exige gate
  humano; aprovações desativadas nesta sessão) → `unverified`, nunca assumido igual ao Windows
  que passou. O que **foi** executado no Linux: `qa-check-task-0031.sh` (8 checks).
- **Corrida de duas denúncias simultâneas do mesmo par** — o caminho CAS
  (`InconsistentStateException` → re-leitura) existe em `SqliteReportStore.cs:64-68` mas não é
  provocado por um teste de concorrência; a idempotência sequencial está provada. Risco: baixo —
  dois escritores no mesmo `ETag` re-leem e o segundo encontra o par.
- **`POST /api/admin/reports/{id}/resolve` sobre denúncia cujo post já saiu do ar** — o ramo
  `post is null` (`AdminEndpoints.cs:269`) fecha a denúncia sem remoção, mas só o caminho
  sequencial (autor removeu antes) foi exercitado pelos testes do DEV (comentário do próprio
  código); corrida não provocada. Risco: baixo.
- **Compose/CI** — fora desta máquina (gate `compose` exige podman + rede); fica para o run
  ubuntu do CI.

## Memory bank (rota `qa-integracao`)

- Rota lida (3 arquivos, ~115 linhas): `build-run-test.md#Local tests without gate` — **S-22 e
  S-23 re-confirmados ao vivo** (todo vermelho classificado dentro do próprio log; todo restore
  com bump de mtime e hash = PRE); `contracts.md` — ver flag abaixo; `integrations.md` — ainda
  template `RASCUNHO`, sem conteúdo a conferir.
- `[GAP]` `docs/memory-bank/contracts.md:18` — a linha ainda afirma que "publicação, timeline,
  mensageria, mídia e **as demais rotas `/api/admin`** seguem sem handler"; desde esta task a
  `POST /api/posts/{id}/report`, `GET /api/admin/reports`,
  `POST /api/admin/reports/{id}/resolve` e `POST /api/admin/actors/{id}/suspend` têm handler.
  Proposta de consolidação no fecho da P-04; o arquivo só se escreve pelo consolidador.
- `[MUDANCA_RELEVANTE: CONTRATO]` — 4 rotas novas com handler (`report`, fila, `resolve`,
  `suspend`), palavra de trilha `Resolve` (GAP-27) e `ChangeRole` para suspensão direta,
  idempotência de denúncia por `(post, ator)` (GAP-28: 201/200) e suspensão por API
  irreversível nesta fase (`reinstate` fora) — source: backend/src/Squid.Api/Posts/ReportEndpoints.cs:11-20;
  backend/src/Squid.Api/Admin/AdminEndpoints.cs:45,166-206,208-305,307-359.
