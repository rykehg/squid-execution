<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: VEREDITO
- owner: qa
- lastVerified: 2026-11-10 21:45

# QA-0030 — Remoção de post com tombstone e auditoria (FR-027)

- task: TASK-0030
- spec: SPEC-0009
- runId: R-20261109-01
- verdict: PASSED
- qaAttempt: 1/2

Normative reading used: `docs/sdd/specs/SPEC-0009-publicacao-feed-postagem.md` section **H**
(soft delete com tombstone, `RemoveContent` com autor/quando/policyVersion, evento `Activity: Delete`
atualiza as projeções, 404 idêntico, quem não via continua recebendo 404) + `Rules and edge cases`
linha 190 (post removido some de toda leitura; a linha da auditoria nunca é apagada);
`data-model.md` §2.5 (`DeletedAt` + `TakedownReason` obrigatório para `Moderator+` — R-07; exclusão
nunca apaga a linha da auditoria) e §3.2 (papel só por `IPolicyEvaluator`, nunca `if` espalhado);
contrato `http-api.md` §5 (`DELETE /api/posts/{id}` — Autor ou `Moderator+`; `GET` 404 se não visível)
e §8 (404 `not_found` cobre inexistente **ou** existência protegida; 403 `policy_denied` só quando a
existência já é pública).

**Verdict: PASSED** — the five ACs are proven by executed scenarios, no BLOCKER finding, and every
DEV decision the dispatch asked me to verify was verified **by execution, not by reading**:

1. **tombstone in a separate `post-tombstone` document, `FindAsync` the single hiding gate** —
   mutation **M5** (the gate line `return tombstone.State is null ? state.State : null;` →
   `return state.State;`) turns `GET /api/posts/{id}` back into a `200` and kills 6 tests including
   the QA's 404-identity, projection and moderation tests (`20261110-213918-test-filter.log`,
   `Com falha: 6`);
2. **`takedownReason` from body OR query** — both carriers exercised for real: the moderation test
   sends it in the **body** (`new { takedownReason = reason }` → `200` echoing it back), the
   projection test sends it in the **query** (`?takedownReason=some` → `200`);
3. **DELETE answers `200 { id, deletedAt, takedownReason }`** — parsed from the live response
   (`deletedAt` non-empty in the AC-01 test, `takedownReason` echoed in the AC-02 test);
4. **the projector's `Delete` arm appends the neutral position row** — the AC-03 test pins the pump
   catch-up at the trailing removal: `LastProjectedSeqAsync() == Delete.Seq`, zero pending events,
   the `urn:squid:projection` row present **at that exact seq**, and a second pass resurrects
   nothing. Mutations **M3** (position insert removed) and **M6** (`if (false && … Delete)`) each
   kill it — M3 with `Com falha: 2`, i.e. the high-water really does drag back under the Create
   without that row;
5. **trail word `RemoveContent`** — mutation **M2** (`RemoveContent` → `RemoveContentX`) kills the
   AC-01 and AC-02 tests of both suites (the rows stop matching `Action: "RemoveContent"` queries);
6. **`422` on empty reason** — mutation **M4** (`if (false && IsNullOrWhiteSpace(reason))`) lets a
   blank reason through and kills the moderation tests of both suites;
7. **byte-identical 404** — mutation **M1** (`withTraceId: false` → `true`) kills the AC-04 test
   plus the pre-existing `QaTask0023Tests` 404 tests;
8. **StreamSession's `Delete` arm unemitted** — verified at `StreamSession.cs:112-113` (deliberate,
   comment names SPEC-D/contract §5); no AC asks for a delete frame → **not failed**;
9. **reply-cascade parked** — verified at `ActivityProjector.cs:175-176`: `RemoveTimeline`/
   `RemoveHome` filter by `post_id = <removed id>` only, so replies keep their own rows; only the
   post leaves the projections → **not failed** (parked gap of the ODEV dispatch).

## What is proven

| AC | Scenario (executed) | Script | Result |
| --- | --- | --- | --- |
| AC-01 | Author removes own post → `200` with `deletedAt`, `post-tombstone` row on disk next to the untouched `post` row (content still there = soft delete), `Activity: Delete` on the **global** seq strictly after its `Create`, and one `RemoveContent`/`Allow` trail row with author URI, `At != default`, the publish-time `PolicyVersion` and the tombstone state in `Reason` — the row then **survives** a stranger's 404 read and a repeat DELETE (SHA-256 of the trail slice before == after; the traffic only appends). DEV: `PostRemoveTests.Author_removing_own_post_writes_the_tombstone_the_event_and_the_trail_row`. Can-fail **M2** (trail word swapped → red) | `backend/tests/Squid.Tests/Posts/{QaTask0030Tests,PostRemoveTests}.cs` · filter `FullyQualifiedName~QaTask0030Tests` → `20261110-213553-test-filter.log` exit 0, 4/4 · `~Squid.Tests.Posts` → `20261110-214026-test-filter.log` 30/30 | PASS |
| AC-02 | Literal data-model §3.2 **`Moderator`** (promoted through `PATCH /api/containers/{slug}/members/{handle}`) removes a third party's post: plain active member → `403 policy_denied` with a `Deny` row (ResourceId, PolicyVersion) and the post still readable; `takedownReason: "   "` → `422 validation_failed` with **no** partial removal and **no** trail row; real reason → `200` echoing it, post then `404`, exactly one `Allow` row carrying the reason. Both carriers (body and query) exercised. DEV: `PostRemoveTests.A_non_author_is_denied_and_a_moderator_owes_a_takedown_reason`. Can-fail **M4** (422 neutralized), **M2** (trail word) | same files · red logs `20261110-213844` (M4), `20261110-213736` (M2) | PASS |
| AC-03 | Create projected FIRST, removal arrives as the TRAILING event (the pump-convergence order): after the projector runs, `feed`, `feed?since=0`, `timeline?since=0` and `GET /api/posts/{id}` all stop serving it (`404` of section H); high-water == `Delete.Seq`, `Log.ReadAsync(high-water)` empty, neutral `urn:squid:projection` position row present at that seq; a **second pass** changes nothing (no flicker back into any read). DEV: `PostRemoveTests.The_removed_post_leaves_feed_timeline_and_delta_when_the_projector_runs`. Can-fail **M3** (position row gone → the mark falls under the Create), **M6** (Delete arm ignored → post stays in feed), **M5** (tombstone gate gone) | same files · red logs `20261110-213810` (M3), `20261110-213952` (M6), `20261110-213918` (M5) | PASS |
| AC-04 | **Hard spot, byte for byte**: the 404 of a REMOVED post equals the 404 of an id that NEVER existed — compared as raw bytes, for a signed-in stranger (before and after the removal), for the author who could read it one request earlier, and for a sessionless reader; no `traceId` in the body; a repeat `DELETE` on the tombstoned id and a `DELETE` on the ghost answer the same bytes too. Audit not erased: the SHA-256 of the trail rows captured before the removal matches the surviving rows after it (in the AC-01 test). DEV: `PostRemoveTests.A_reader_who_never_saw_it_keeps_the_same_404_and_no_trail_row_is_erased`. Can-fail **M1** (traceId leaks), **M5** | same files · red logs `20261110-213701` (M1), `20261110-213918` (M5) | PASS |
| AC-05 | **Gate:** `sdd-test.ps1 test` → `20261110-213449-test-gate.log` with `exit: 0` and `Com falha: 0, Aprovado: 419, Ignorado: 0, Total: 419` read **inside** the log (S-22), 0 `[FAIL]` lines. **Scope:** git delta vs `QA-0030-git-baseline.txt` → only QA-0030 artifacts added, 0 removed; the three files the mutation driver touched are all inside `touches` and hash back to PRE; `QA-MUTATION` residue in `backend/` = **0**; temp backups `qa0030-*` = 0. Checkers: `qa-check-task-0030.ps1` and `.sh` (10 checks each) | gate + `qa-check-task-0030.ps1` / `.sh` + `QA-0030-scope.log` | PASS (suite Linux: unverified) |

Every AC's tests were killed by at least one mutation — no assertion in this task is decoration.

## Scripts

| Script | Language / runner | What it does |
| --- | --- | --- |
| `backend/tests/Squid.Tests/Posts/QaTask0030Tests.cs` | xunit via `sdd-test.ps1 test -TestFilter` | The 4 QA scenarios (inherited from the interrupted attempt 1, **re-read and re-executed** here): byte-identical 404 (removed vs never-existed vs repeat DELETE), tombstone + `Delete` event + surviving `RemoveContent` line with before/after SHA-256, pump convergence at a trailing removal, literal-Moderator ladder with `422` and audited `Deny` |
| `backend/tests/Squid.Tests/Posts/PostRemoveTests.cs` | xunit (DEV tests, re-executed) | The 4 DEV tests of the task, re-executed in the `~Squid.Tests.Posts` filter and in the gate |
| `docs/sdd/qa/qa-mutate-task-0030.ps1` | pwsh (gate `sdd-test.ps1`) | **6** can-fail cycles (M1..M6): SHA256 PRE (S-16) → one `QA-MUTATION` edit with pattern-count exactly 1 → red run classified **inside the log file** (S-22, never the wrapper console) → byte-exact restore from memory + mtime bump (S-23) → hash = PRE → residue 0 → one GREEN over the restored sources |
| `docs/sdd/qa/qa-check-task-0030.ps1` / `.sh` | pwsh / bash (WSL) | 10 evidence checks each (newest gate = 419/419 exit 0, QA baseline 415/415, 4/4 first green, mutation summary, 6 PRE + 6 red + 6 restores + GREEN, residue, git scope, control block intact, GREEN log, verdict doc); the `.sh` also records the `sdd-test.sh` platform probe |
| `docs/sdd/qa/QA-0030-mutations.log` | artefato | Output of my six cycles: `M1..M6 PRE/RUN/RED-IN-LOG/RESTORE-OK` + `GREEN wrapper=0 logExit=0 aprovado=30 falha=0` + `SUMMARY cycles=6 failed=0 residue-QA-MUTATION=0 temp-backups=0` |
| `docs/sdd/qa/QA-0030-git-baseline.txt` | artefato | `git status --porcelain -uall` captured before QA wrote (inherited; re-verified: current status = baseline + QA-0030 artifacts only) |
| `docs/sdd/qa/QA-0030-scope.log` | artefato | Final scope scan: git delta vs baseline, files with mtime since the QA dispatch, `QA-MUTATION` residue count |

## How to reproduce

Prerequisites: Windows com `dotnet` 10 e o repositório em `C:\dev\squid-squad-net\squid-execution`
(nenhuma instalação, nenhum serviço externo — os testes sobem host local em porta dinâmica e só
falsificam as costuras de rede).

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~QaTask0030Tests"   # esperado: exit 0, Aprovado 4, Total 4
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0030.ps1                                                  # esperado: exit 0, SUMMARY cycles=6 failed=0 residue-QA-MUTATION=0 temp-backups=0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test                                                    # esperado: exit 0, Com falha 0 / Aprovado 419 / Total 419
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0030.ps1                                                   # esperado: SUMMARY total=10 fails=0, exit 0
bash docs/sdd/qa/qa-check-task-0030.sh                                                                                                   # esperado: SUMMARY total=10 fails=0, exit 0 (WSL)
```

## Last run

| Date/Time | Command | Exit | Output (max 10 lines) |
| --- | --- | --- | --- |
| 2026-11-10 21:34:49 | `sdd-test.ps1 test` (gate AC-05, this QA) | 0 | `20261110-213449-test-gate.log` → `exit: 0` · `Com falha: 0, Aprovado: 419, Ignorado: 0, Total: 419` (4 m 23 s) · 0 linhas `[FAIL]` |
| 2026-11-10 21:35:53 | `sdd-test.ps1 test -TestFilter FullyQualifiedName~QaTask0030Tests` | 0 | `20261110-213553-test-filter.log` → `exit: 0` · `Com falha: 0, Aprovado: 4, Total: 4` (6 s) |
| 2026-11-10 21:37:01–21:40:26 | `qa-mutate-task-0030.ps1` (6 ciclos + GREEN) | 0 | `QA-0030-mutations.log` → `M1..M6 RED-IN-LOG` + `RESTORE-OK hash=PRE residue=0` · vermelhos nos logs `20261110-213701/213736/213810/213844/213918/213952` · `GREEN … aprovado=30 falha=0` (`20261110-214026`) · `SUMMARY cycles=6 failed=0 residue-QA-MUTATION=0 temp-backups=0` |
| 2026-11-10 21:41 | verificação de restauração (hash + resíduo + backup) | 0 | `PostEndpoints.cs 814E4A5ACDC0` · `ActivityProjector.cs CB405189EB82` · `SqliteObjectStore.cs AB09A78FCECB` — todos = PRE, mtimes 21:38–21:39 · resíduos `QA-MUTATION` = 0 · `qa0030-*` no TEMP = 0 |
| 2026-11-10 19:34:06 | baseline da QA (herdado, **re-verificado no arquivo**) | 0 | `20261110-193406-test-gate.log` → `exit: 0` · `Com falha: 0, Aprovado: 415, Total: 415` (medido antes dos 4 testes da QA existirem) |
| 2026-11-10 20:04:50 | primeiro verde dos 4 testes (herdado, **re-verificado no arquivo**) | 0 | `20261110-200450-test-filter.log` → `exit: 0` · `Com falha: 0, Aprovado: 4, Total: 4` |
| 2026-11-10 21:23:32 | sonda de saúde do ODEV (herdada, re-verificada) | 0 | `20261110-212332-test-gate.log` → `exit: 0` · `Com falha: 0, Aprovado: 419, Total: 419` |
| 2026-11-10 21:46:16 | `qa-check-task-0030.ps1` | 0 | `QA-0030-check.log` → `C1..C10 PASS` · `SUMMARY total=10 fails=0` |
| 2026-11-10 21:47:09 | `wsl bash -lc "bash docs/sdd/qa/qa-check-task-0030.sh"` | 0 | `QA-0030-check-sh.log` → `C1..C10 PASS` · `SUMMARY total=10 fails=0` · `QA-0030-sh-twin.log`: `probe exit: 0`, `test: exit 127 (real test failure)` → `test exit: 1` (achado 1; stderr do WSL só aviso do podman-machine) |
| 2026-11-10 21:48:50 | varredura de escopo (git vs baseline + mtimes desde 21:24 + resíduo + hashes PRE) | 0 | `QA-0030-scope.log` → `adicionadas=11 removidas=0 fora-do-padrao=0` · `fora-da-allowlist=0` (19 arquivos: task, logs do gate, 3 fontes dentro dos `touches` restauradas, artefatos QA) · `residuo=0` · `backups=0` · os 3 hashes = PRE |
| 2026-11-10 21:52:57 / 21:53:20 | re-execução final de `qa-check-task-0030.ps1` e `qa-check-task-0030.sh` sobre a árvore final (doc + Log/Evidence já gravados) | 0 | ambos → `C1..C10 PASS` · `C7 git: +11 entradas todas QA-0030, removidas=0, fora do padrao=0` · `SUMMARY total=10 fails=0` |

## Findings

| # | Severity | Where | What breaks | Evidence |
| --- | --- | --- | --- | --- |
| — | — | — | **Nenhum achado de produto desta task.** Os 5 ACs passaram e cada um provou pode-falhar: M1 → AC-04, M2 → AC-01/AC-02, M3/M6 → AC-03, M4 → AC-02, M5 → AC-03/AC-04 | `QA-0030-mutations.log` |
| 1 | MINOR (pré-existente, re-confirmado) | `docs/sdd/_tools/sdd-test.sh` | O gêmeo Linux do gate continua sem a guarda de ambiente do `.ps1`: sem `dotnet` no WSL ele imprime `test: exit 127 (real test failure)` e sai **1**, classificando ferramenta ausente como falha real (S-22 no traje de Linux). Mesmo achado das QA-0023..0029; não é desta task | `QA-0030-sh-twin.log` (probe exit 0 / test exit 1) |

Notas (não são defeitos):

- **Gate 419 = 415 (baseline) + 4 (testes desta QA)**: o baseline 415/415 foi medido antes dos
  testes da QA existirem e foi re-verificado no arquivo; os 419 incluem os 4 `PostRemoveTests` do
  DEV e os 4 `QaTask0030Tests` herdados da tentativa interrompida.
- **Artefatos herdados foram executados, não assumidos**: os 4 testes foram lidos linha a linha,
  re-executados (4/4 e 30/30 no filtro `~Squid.Tests.Posts`), e o driver de mutação foi re-executado
  por completo nesta tentativa — o `QA-0030-mutations.log` atual é saída **desta** execução
  (21:37–21:40), não a da tentativa que morreu. O `git-baseline` foi re-conferido contra o
  `git status` atual.
- **`takedownReason` nos dois carriers**: o contrato não fixa o transportador; a task registrou a
  decisão dupla. Testado nos dois (corpo na AC-02, query na AC-03).
- **Segunda remoção (DELETE repetido)**: responde o mesmo 404 do id fantasma e não reescreve o
  instante do tombstone (`SqliteObjectStore.cs:147`), coberto na AC-04.

## Not covered, and why

- **Suíte do produto em Linux** — o WSL desta máquina não tem `dotnet` (instalar exige gate
  humano; aprovações desativadas nesta sessão) → `unverified`, nunca assumido igual ao Windows que
  passou. O que **foi** executado no Linux: `qa-check-task-0030.sh` (10 checks) e o platform probe
  do `sdd-test.sh` (achado 1).
- **DELETE concorrente (duas remoções simultâneas)** — só a repetição sequencial foi exercitada; o
  caminho `DeleteAsync → false → 404` existe em `PostEndpoints.cs:295-300` mas não é provocado por
  corrida. Risco: baixo — o segundo escritor encontra o tombstone e responde 404, que é o
  comportamento desejado.
- **Cascata de replies (parked pelo ODEV)** — só o post sai das projeções (`ActivityProjector.cs:175-176`
  filtra por `post_id`); respostas ao removido permanecem nas leituras. Sem AC/contrato pedindo o
  contrário → não é falha; risco registrado: um leitor ainda vê a reply cujo pai sumiu.
- **Frame SSE de delete (parked pelo ODEV)** — `StreamSession.cs:112-113` emite deliberadamente
  nada para `Activity: Delete` (SPEC-D/contrato §5 não definem `event: delete`) → não é falha;
  risco: um cliente SSE conectado continua com o post no cliente até reconectar/refetch.
- **Remoção antes do pump rodar** — entre o `DELETE` e a passada do projetor o feed ainda serve a
  linha projetada (a AC diz "projeção atualizada pelo projetor"); consistência eventual assumida.
- **Compose/CI** — fora desta máquina (gate `compose` exige podman + rede); fica para o run ubuntu
  do CI.

## Memory bank (rota `qa-integracao`)

- Rota lida (3 arquivos, 115 linhas): `build-run-test.md#Local tests without gate` — **S-22 e
  S-23 re-confirmados ao vivo** (todo vermelho classificado dentro do próprio log; todo restore com
  bump de mtime e hash = PRE); `contracts.md` — ver flag abaixo; `integrations.md` — ainda template
  `RASCUNHO`, sem conteúdo a conferir.
- `[GAP]` `docs/memory-bank/contracts.md:18` — a linha ainda afirma que "publicação, timeline,
  mensageria, mídia e as demais rotas `/api/admin` seguem sem handler", mas publicação/timeline/mídia
  têm handler desde a P-03/P-04 e o `DELETE /api/posts/{id}` desta task nasce agora no §5. Proposta
  de consolidação no fecho da P-04; o arquivo só se escreve pelo consolidador.
- `[MUDANCA_RELEVANTE: CONTRATO]` — semântica adjudicada do `DELETE /api/posts/{id}`: `200` com
  `{ id, deletedAt, takedownReason }` (gap da task) e `takedownReason` aceito no corpo **e** na
  query — source: docs/sdd/tasks/TASK-0030-remocao-post-tombstone.md (Log 19:21:17);
  backend/src/Squid.Api/Posts/PostEndpoints.cs:317,325-340.
- `[PADRAO_NOVO]` — linha de posição neutra (`urn:squid:projection`) em `home_timeline` no seq do
  evento `Delete`: segura o alto-mar do pump para o `MAX(seq)` não arrastar o `Create` de volta
  (anti-flicker da remoção trailing) — source: backend/src/Squid.ReadModels/Projections/ActivityProjector.cs:64-68,177.
