<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: CONCLUIDO
- owner: qa
- lastVerified: 2026-11-08 22:55:00

# QA-0018 — Membership: join, convite, leave e listagem de membros

- task: TASK-0019
- spec: SPEC-0008 (seção E + Gaps + §A emendada), data-model §2.4/§3.3, contracts/http-api.md §4/§9.11
- runId: R-20261108-01
- qaAttempt: 1/2
- verdict: PASSED

Execução, não leitura: a área `Squid.Tests.Containers` rodou **34/34** filtrada (31 do DEV + 3 do
artefato parcial reaproveitado `Qa0018MembershipPageAndTrailTests.cs`) e o gate completo
rodou **201/201** no começo (baseline desta QA, `20261108-220945`) e **201/201 no fim**
(`20261108-223849`), ambos com `exit: 0` lido **dentro** do log gravado (S-22). O SC-006 foi
executado isolado (100 tentativas de `join` sem convite, 0 sucessos — asserções dentro do teste
`Entry_follows…`: `attempts == 100`, cada tentativa `403`, lista de membros intacta). Os 7 ACs
tiveram prova de can-fail: **10 mutações** (M1..M10), cada uma com SHA256 PRE (S-16), vermelho
classificado pelo log gravado, restauração byte-exact com bump de mtime (S-23) e verde logo em
seguida — e o vermelho da M9 cai **dentro** do laço do SC-006 (`MembershipRoutesTests.cs:108`).
A subagente anterior morreu sem veredito; o único artefato dela (3 testes) foi inspecionado,
executado e aceito — e a árvore que ela deixou está verde e intacta (hashes = PRE, resíduos
`QA-MUTATION` = 0).

## What is proven

| AC | Scenario (executed) | Script | Result |
| --- | --- | --- | --- |
| AC-01 | `Joining_an_open_community_creates_an_active_member_that_shows_up_in_the_listing` (`MembershipRoutesTests.cs:30`): `join` em comunidade `Open` → **201 Created**, `state active`, `role member`, `actorId` = origem da requisição (`:40-46`); o ator aparece na página com `state/role` (`:49-52`); `memberCount` sobe para 2 (`:55`). **M1 deixa isto vermelho** (`:40` — `Expected: Created, Actual: OK`) | filtros xunit | PASS |
| AC-02 | `Entry_follows_the_policy_and_a_container_out_of_sight_answers_the_404_of_an_absent_slug` (`:59`): contêiner invisível → **404** byte-idêntico ao slug inexistente (FR-018, `:68-72`); `Request` → **201** `requested` fora da página (`:81-89`); `Closed` → **403** `policy_denied` visível (`:94-96`); `Invite` sem convite → **100 tentativas, 0 sucessos** (10 atores × 10, `:100-113`, `Assert.Equal(100, attempts)` + cada uma `403` + só o dono na página) — **SC-006 executado isolado, 2/2 verdes**. **M1 (`:82`), M2 (`:95`), M8 (`:89`), M9 (`:108` — dentro do laço) deixam isto vermelhos** | filtros xunit | PASS |
| AC-03 | `An_admin_invites_one_target_actor_and_only_that_actor_walks_in` (`:117`): sem papel → convite **403** (`:127-129`); handle malformado → **400** `validation_failed` (`:132-135`); convite cria `invited`/`member` com `invitedBy` do dono → **201** (`:141-150`); por `actorId` canônico → **201** (`:153-155`); o convidado faz `join` → **200** `active` com `invitedBy` preservado (`:158-167`); ator sem convite segue **403** e fora da página (`:170-177`). **M2 (`:171`), M9 (`:171`), M10 (`:143` — convite 201→200) deixam isto vermelhos** | filtros xunit | PASS |
| AC-04 | `Leave_moves_active_to_left_the_last_owner_conflicts_and_reentry_follows_the_policy_in_force` (`:181`): saída do único Owner → **409** `last_owner` com a linha ainda `active` (`:189-194`); membro `join` 201 → `leave` **200** `left`, sai da página, contador 1 (`:197-210`); reentrada em `Open` → **200** `active`, contador 2 (`:213-221`); política vira `Invite`: após `Left` o `join` sem convite → **403** (`:224-228`); novo convite → **200** (transição de linha existente, `:231-233`) e `join` → **200** `active` (`:234-239`). **M5 (`:190`), M1 (`:198`), M2/M9 (`:228`) deixam isto vermelhos** | filtros xunit | PASS |
| AC-05 | filtro: `The_member_page_filters_by_the_reader_and_pages_without_repeating_or_losing` (`:243`) — anônimo vê só `Active` (`:282`), membro vê a mesma página (pendentes só `Admin`+, `:285-287`), `Admin`+ ganha `Invited/Requested` e nunca `Banned/Silenced/Left` (`:290-296`); `Left` fora da página (`:260-261`); paginas `limit/offset` sem repetir nem perder (`:298-307`); `limit=0`/`offset=x` → **400** (`:310-313`); invisível → **404** byte-idêntico (`:316-323`). paginação de contrato: `The_member_page_defaults_to_100_caps_at_1000_and_walks_every_row_exactly_once` (`Qa0018…Tests.cs:31`) — página default **100** linhas e corpo **`{ items }` só** (`:62-67`), `limit=5000` trava em **1000** (`:70-71`), `offset=1000` devolve a 1 (`:74-75`), a caminhada = exatamente os **1001** membros sem duplicata nem perda (`:77-80`). **M3 (`:67`), M4 (`:71`), M8 (`:89`/`:282`), M10 (`:265`) deixam isto vermelhos** | filtros xunit | PASS |
| AC-06 | gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` → log `docs/sdd/runs/test-runs/20261108-223849-test-gate.log` com `exit: 0` e `Com falha: 0, Aprovado: 201, Total: 201`, lido **dentro** do log (S-22); escopo: `git status --porcelain` do início (22:05) idêntico ao do fim com só os 4 artefatos desta QA novos; varredura de mtime em `backend/src` + `backend/tests` desde 19:55 → **exatamente 3 arquivos**, todos dentro dos `touches` (artefato parcial + os 2 mutados restaurados); resíduo `QA-MUTATION` em `backend/` = **0**; hash final = PRE nos 2 arquivos mutados | gate + git/mtime/hash + checker | PASS |
| AC-07 | origem = scheme + Host: `ContainerRoutesTests.cs:38` (`id` == `BaseAddress.Scheme://Authority/c/{slug}`), `:41` (`Assert.StartsWith("http://127.0.0.1:")` — o host de teste fala http), `:84`, `:115` (`actorId`), `Qa0016ContainerPatchScopeTests.cs:60`, `PolicyAuditRoutesTests.cs:340` (`ActorOf`) — as 5 asserções agora calculam a origem; estático: **0** `https://` restantes nos 3 arquivos e **5** asserções `BaseAddress!.Scheme` (checker `AC07-STATIC`); o recurso nasce com um único URI porque `Origin(HttpContext)` lê `Request.Scheme` + `Request.Host` (`ContainerEndpoints.cs:696-697`). **M6 (scheme forçado `https:`) deixa 3 testes do `ContainerRoutesTests` vermelhos** | filtros xunit + checker | PASS |

Falhas pré-existentes: **NENHUM** — baseline 201/201 verde antes de qualquer edição desta QA e
201/201 verde na árvore restaurada; as 10 rodadas vermelhas foram mutações deliberadas desta QA,
todas revertidas byte-exact.

### DEV decisions verified (not disputed — executed)

| Decision | Evidence |
| --- | --- |
| 201 quando a linha nasce / 200 em transição (`join`, `invites`) | `ContainerEndpoints.cs:328-329` e `:397-398`; vermelhos M1 (`join`) e M10 (`convite`); transições 200 em AC-03/AC-04 |
| corpo do convite aceita `handle` OU `actorId` | `TargetHandle` (`:467-477`); as duas grafias provadas em AC-03 (`:141`, `:153`) |
| paginação default 100, teto 1000, corpo `{ items }` | `DefaultLimit`/`MaxLimit` (`:54-56`), `Results.Json(new { items })` (`:252`); vermelhos M3 e M4 |
| filtro: `Active` visível / `Invited+Requested` só `Admin`+ / nada mais (`Banned` nunca) | `Listable` (`:458-460`); vermelho M8 (leitor forçado a `true`) |
| `Silenced`/`Left` fora da página (§F é TASK-0020) | `Listable` não os lista; asserções AC-05 `:294-296` + `Qa0018…Tests.cs:110-113`; `MembershipRules.JoinTarget` recusa `Silenced`/`Banned` no `join` (`MembershipRules.cs:22`) com teste `Qa0018…Tests.cs:84-114` (403 + linha intacta) |
| `409 last_owner` deixa o estado intacto | AC-04 `:189-194`; vermelho M5 |
| `Join`/`Invite` Allow+Deny na trilha, `leave` sem avaliação | `Qa0018…Tests.cs:117-163` (4 linhas na trilha + contagens iguais após `leave` + nenhuma ação `Leave`); vermelho M7 (linha `Join/Deny` removida → `:147` vermelho) |
| `IPolicyEvaluator` **não** modificado (fronteira da TASK-0020) | mtime `2026-11-06 18:02:25` e hash `94CDDFA4…` inalterados; checker `EVALUATOR-UNTOUCHED` |

## Scripts

| Script | Language / runner | What it does |
| --- | --- | --- |
| `backend/tests/Squid.Tests/Containers/MembershipRoutesTests.cs` | xunit (runner da casa) | 5 testes de rota: AC-01..AC-05 por HTTP real, incluindo as 100 tentativas do SC-006 |
| `backend/tests/Squid.Tests/Containers/MembershipRulesTests.cs` + `ContainerStoreTests.cs` + `ContainerRoutesTests.cs` + `Qa0016…` + `PolicyAuditRoutes…` | xunit | regras puras §3.3, upsert de membership, criação/origem (AC-07) e trilha — executados nos filtros e gates |
| `backend/tests/Squid.Tests/Containers/Qa0018MembershipPageAndTrailTests.cs` | xunit | artefato reaproveitado da subagente morta: 3 testes (paginação 100/1000/`{ items }`, recusa §3.3 no `join`, trilha Join/Invite) — executado 3/3 |
| `docs/sdd/_tools/sdd-test.ps1 test [-TestFilter …]` | pwsh + dotnet | runner da casa: 2 gates + 17 filtros executados nesta QA |
| `docs/sdd/qa/qa-mutate-task-0019.ps1` | pwsh | driver de mutação can-fail: PRE hash → muta exatamente 1 padrão (recusa contagem ≠ 1) → vermelho → restaura byte-exact + bump de mtime → hash == PRE → verde |
| `docs/sdd/qa/qa-check-task-0019.ps1` | pwsh | 13 checks: exit/resumo do gate e do filtro mais novos (S-22), SHA256 PRE dos 2 mutados (S-16), resíduo `QA-MUTATION`, escopo dos touches (git desde 22:05), varredura de mtime desde 19:55, AC-07 estático, marcadores do SC-006, rotas/constantes, `sdd-state check`, hash do `IPolicyEvaluator` |
| `docs/sdd/qa/qa-check-task-0019.sh` | bash (WSL) | gêmeo Linux do check acima, mesmos 13 check ids, mesmo contrato de exit |

## How to reproduce

Prerequisites: Windows com .NET SDK 10, repositório em `C:\dev\squid-squad-net\squid-execution`
(sem rede depois do restore). Sem servidor, sem banco compartilhado, sem instalação.

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test
# esperado: exit 0 · Com falha 0, Aprovado 201 · log novo em docs/sdd/runs/test-runs/

powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~Squid.Tests.Containers"
# esperado: exit 0 · Com falha 0, Aprovado 34

powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0019.ps1
# esperado: exit 0 · SUMMARY total=13 fails=0
```

Mutações (ciclo completo, precisa terminar com `RESULT: PROVEN` e exit 0 — ele restaura sozinho):

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0019.ps1 -File backend/src/Squid.Domain/Containers/MembershipRules.cs -Old "if (!entryAllowed) return null;" -Filter "FullyQualifiedName~MembershipRoutesTests"
```

## Last run

Classificação sempre pelo log gravado (S-22). As 10 mutações são vermelhos **de propósito**.
As tentativas 1 de M2/M7 (aspas quebradas na fronteira de argumento do `powershell.exe`) e de M6
(`PATTERN-COUNT 0`) e a tentativa 2 de M7 (`PATTERN-COUNT 2`) **não mutaram nada** — recusas do
próprio driver, registradas como evidência.

| When (log) | Command | Exit (log) | Output (tail) |
| --- | --- | --- | --- |
| 22:09:45 | `sdd-test.ps1 test` (baseline desta QA) | 0 | `Com falha: 0, Aprovado: 201, Total: 201` |
| 22:10:58 | filtro `~Squid.Tests.Containers` (baseline) | 0 | `Com falha: 0, Aprovado: 34` |
| 22:11:46 | filtro `~Entry_follows_the_policy` (SC-006 isolado) | 0 | `Com falha: 0, Aprovado: 2` (AC-02 + AC-04) |
| 22:12:30 | filtro `~Qa0018` (artefato parcial) | 0 | `Com falha: 0, Aprovado: 3` |
| 22:20:32 / 22:20:58 | driver **M1** (join 201→200) vermelho / verde | 1 / 0 | `Com falha: 4, Aprovado: 1` → `Aprovado: 5` |
| 22:22:09 / 22:22:35 | driver **M2** (gate `entryAllowed` removido) vermelho / verde | 1 / 0 | `Com falha: 3, Aprovado: 2` → `Aprovado: 5` |
| 22:23:35 / 22:24:06 | driver **M3** (`DefaultLimit` 100→50) vermelho / verde | 1 / 0 | `Com falha: 1, Aprovado: 2` → `Aprovado: 3` |
| 22:24:51 / 22:25:20 | driver **M4** (`MaxLimit` 1000→500) vermelho / verde | 1 / 0 | `Com falha: 1, Aprovado: 2` → `Aprovado: 3` |
| 22:25:50 / 22:26:05 | driver **M5** (last_owner `<=1`→`<1`) vermelho / verde | 1 / 0 | `Com falha: 1, Aprovado: 0` → `Aprovado: 1` |
| 22:27:21 / 22:27:39 | driver **M6** (origem `https:` fixa) vermelho / verde | 1 / 0 | `Com falha: 3, Aprovado: 2` → `Aprovado: 5` |
| 22:29:29 / 22:29:58 | driver **M7** (linha `Join/Deny` removida) vermelho / verde | 1 / 0 | `Com falha: 1, Aprovado: 2` → `Aprovado: 3` |
| 22:30:34 / 22:31:01 | driver **M8** (leitor `admin`→`true`) vermelho / verde | 1 / 0 | `Com falha: 2, Aprovado: 3` → `Aprovado: 5` |
| 22:33:07 / 22:33:32 | driver **M9** (negação de convite só em `Closed`) vermelho / verde | 1 / 0 | `Com falha: 3, Aprovado: 2` (AC-02 falha em `:108`, dentro do laço) → `Aprovado: 5` |
| 22:34:11 / 22:34:34 | driver **M10** (convite 201→200) vermelho / verde | 1 / 0 | `Com falha: 2, Aprovado: 3` → `Aprovado: 5` |
| 22:36:21 | filtro `~Squid.Tests.Containers` (final) | 0 | `Com falha: 0, Aprovado: 34` |
| 22:38:49 | `sdd-test.ps1 test` (**gate final**, AC-06) | 0 | `Com falha: 0, Aprovado: 201, Total: 201` |
| 22:47 | `bash docs/sdd/qa/qa-check-task-0019.sh` (1ª execução) | 0 | `SUMMARY total=13 fails=0` `RESULT: PASS` (git/sha256sum/find/powershell.exe todos funcionando no WSL) |
| 22:50 | `powershell … qa-check-task-0019.ps1` (1ª execução) | 1 | ParserError na linha 132 (subexpressão na string de detalhe) — bug próprio do script, corrigido |
| 22:51 | mesmo `.ps1` após o conserto | 0 | `SUMMARY total=13 fails=0` `RESULT: PASS` |
| — | self-test do check: `Aprovado>=201` → `>=9999`, depois restaurado | 1 → 0 | quebra: `CHECK GATE-NEWEST-SUMMARY FAIL … RESULT: FAIL` exit 1; restaurado: exit 0 e arquivo byte-idêntico |
| — | execução final dos dois checks (após os appends) | 0 / 0 | os dois `SUMMARY total=13 fails=0` `RESULT: PASS` |

## Mutation ledger (S-16 / S-23)

| # | File mutated | Change | PRE = POST SHA256 | Red run | Restore proof |
| --- | --- | --- | --- | --- | --- |
| M1 | `backend/src/Squid.Api/Containers/ContainerEndpoints.cs` | `join` 201→200 na criação | `4E56720255D5E50E04AFC0C76A890FB7D144893075E09D7D234C1D2DB311A979` | `20261108-222032` exit 1 (4 falhas: AC-01 `:40`, AC-02 `:82`, AC-04 `:198`, AC-05 `:255`) | hash = PRE, mtime 22:20, verde 5/5 |
| M2 | `backend/src/Squid.Domain/Containers/MembershipRules.cs` | `if (!entryAllowed) return null;` removido | `57312633E7978231D4C309913B72DCD172F90A92CC843F8F7E4D025368734803` | `20261108-222209` exit 1 (3 falhas: AC-02 `:95`, AC-03 `:171`, AC-04 `:228`) | hash = PRE, mtime 22:22, verde 5/5 |
| M3 | `ContainerEndpoints.cs` | `DefaultLimit` 100→50 | `4E5672…A979` | `20261108-222335` exit 1 (1 falha: `Qa0018…:67`) | hash = PRE, mtime 22:23, verde 3/3 |
| M4 | `ContainerEndpoints.cs` | `MaxLimit` 1000→500 | `4E5672…A979` | `20261108-222451` exit 1 (1 falha: `Qa0018…:71`) | hash = PRE, mtime 22:24, verde 3/3 |
| M5 | `ContainerEndpoints.cs` | `activeOwners <= 1` → `< 1` | `4E5672…A979` | `20261108-222550` exit 1 (1 falha: AC-04 `:190`) | hash = PRE, mtime 22:25, verde 1/1 |
| M6 | `ContainerEndpoints.cs` | `Origin`: scheme da requisição → `https:` fixo | `4E5672…A979` | `20261108-222721` exit 1 (3 falhas: id/actorId no `ContainerRoutesTests`) | hash = PRE, mtime 22:27, verde 5/5 |
| M7 | `ContainerEndpoints.cs` | bloco `Join/Deny` da trilha removido | `4E5672…A979` | `20261108-222929` exit 1 (1 falha: `Qa0018…:147`) | hash = PRE, mtime 22:29, verde 3/3 |
| M8 | `ContainerEndpoints.cs` | `Listable(member.State, admin)` → `true` | `4E5672…A979` | `20261108-223034` exit 1 (2 falhas: AC-02 `:89`, AC-05 `:282`) | hash = PRE, mtime 22:30, verde 5/5 |
| M9 | `MembershipRules.cs` | gate `!entryAllowed` só para `Closed` | `573126…4803` | `20261108-223307` exit 1 (3 falhas: AC-02 `:108` no laço do SC-006, AC-03 `:171`, AC-04 `:228`) | hash = PRE, mtime 22:33, verde 5/5 |
| M10 | `ContainerEndpoints.cs` | `invites` 201→200 | `4E5672…A979` | `20261108-223411` exit 1 (2 falhas: AC-03 `:143`, AC-05 `:265`) | hash = PRE, mtime 22:34, verde 5/5 |

Resíduo final: varredura de `QA-MUTATION` em `backend/` (src + tests, sem bin/obj) → **0** (nenhuma
mutação inseriu marcador; a restauração é por cópia byte-exact); hash final dos 2 arquivos = PRE,
confirmado também pelo `qa-check-task-0019.ps1`/`.sh` (`RESTORE-SHA256`).

## Findings

| # | Severity | Where | What breaks | Evidence |
| --- | --- | --- | --- | --- |
| F1 | MINOR | `backend/src/Squid.Api/Containers/ContainerEndpoints.cs:246-250` + `backend/src/Squid.Service/SqliteContainerStore.cs:180-191` | `GET /members` lê o documento inteiro da membership do contêiner e pagina em memória (`Skip/Take`): custo O(membros) por página, não O(página), e a NFR da SPEC-0008 pede "queries paginadas sobre índice". Não varre feed nem projeção Dapper (a cláusula concreta se mantém) e o comportamento dos ACs está correto (1001 linhas provadas), mas o limite não está marcado com `shortcut:` como a casa exige para limites conhecidos (`RecountAsync`/`CreateAsync` têm) | vermelho não observado; prova M3/M4 verdes; leitura de `ContainerEndpoints.cs:246` e `SqliteContainerStore.cs:180` |
| F2 | INFO (skip autorizado) | `ContainerEndpoints.cs:376-379` | convite de quem já é membro devolve 400 `validation_failed`, mas **nenhum teste de rota** cobre esse caminho — a tabela de gaps da task manda não cobrir em AC | gaps da task, linha `— convite de quem já é membro … não cobrir em AC` |
| F3 | INFO (skip autorizado) | `ContainerEndpoints.cs:467-477` | o convite valida só o **formato** do handle: um handle inexistente escreve `Invited` para um ator que não existe (sem checagem de existência — decisão do DEV aceita pelo dispatch) | dispatch QA: "actor-existence check on invite — não bloquear" |

Nenhum BLOCKER, nenhum MAJOR. Os três itens acima não bloqueiam: F2/F3 são desvios autorizados
por escrito, F1 é risco de escala futuro com comportamento correto hoje.

## Not covered, and why

- **Suite em Linux/WSL** — verificado nesta sessão: `wsl -e sh -lc` responde `dotnet:ABSENT`
  `pwsh:ABSENT`; instalar SDK/ferramenta é gate e não foi aprovado → **unverified**, nunca
  assumido verde. O gêmeo `docs/sdd/_tools/sdd-test.sh` do wrapper não roda neste repo (procura
  `*.sln`, a solução é `*.slnx` — fato do memory bank, não re-executado aqui) e o CI de ubuntu já
  rodou a suite via pwsh em runs anteriores (memory bank; não re-executado nesta QA). O que roda
  em Linux nesta sessão é o checker estático `.sh` (13/13 PASS no WSL). Risco: regressão
  específica de Linux só é vista no CI.
- **Convite de quem já é membro (400)** e **existência do ator no convite** — skips autorizados
  (ver F2/F3). Risco: baixo — recusa 400 sem escrita e linha `Invited` para handle inexistente.
- **CSRF nas rotas de membership** — o middleware sessão+CSRF da P-02 é o mesmo de todas as
  mutações e nenhum AC desta task pede 401/403 por CSRF; não exercitado aqui. Risco: baixo.
- **Corrida de escrita simultânea na membership** (dois `join` no mesmo instante) — só estática
  (retry `InconsistentStateException` em `SqliteContainerStore.cs:160-176`); provocar dois
  escritores não foi feito. Risco: baixo — falha alto, não perde linha.
- **`Silenced`/`Banned` como leitores e moderação via rota** — §F, fronteira da TASK-0020
  (`IPolicyEvaluator` intocado, provado por hash). Risco: nenhum para esta task.
- **Retenção/limite da trilha de auditoria** — fase dona (ver QA-0017). Risco: nenhum aqui.

## Platforms

| Platform | State | Why |
| --- | --- | --- |
| Windows (PowerShell 5.1 + .NET SDK 10) | **executado** | 2 gates + 17 filtros + 10 ciclos de mutação + checker `.ps1`, todos classificados pelos logs gravados |
| Linux / bash (WSL) | **executado (só o check estático)** | `qa-check-task-0019.sh` roda e fecha igual (13/13); a **suite** em Linux segue **unverified** (sem dotnet/pwsh no WSL) |
| Linux nativo / CI ubuntu | **não re-executado nesta QA** | histórico verde no CI via pwsh (memory bank); sem aprovação para disparar runner externo |
