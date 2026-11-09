<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: CONCLUIDO
- owner: qa
- lastVerified: 2026-11-09 04:53:00

# QA-0021 — Descoberta de comunidades listadas (FR-016) e facet de fecho

- task: TASK-0022
- spec: SPEC-0008 (seção H + `Gaps` GAP-15 + emenda §A + seção G + `Rules and edge cases`), specs/001-squid-mvp/spec.md FR-016/FR-018/SC-005, specs/001-squid-mvp/contracts/http-api.md §4, docs/sdd/plan/plan.md (DoD Sprint 2: grupo `invite` só por convite, `singlePublisher` bloqueia `Member` e permite o publisher nomeado, contêiner nunca órfão)
- runId: R-20261108-01
- qaAttempt: 1/2
- verdict: PASSED

Execução, não leitura: o gate de baseline rodou **242/242** no começo (`20261109-040457`, `exit: 0`
lido **dentro** do log — S-22), os **7 testes novos do DEV** rodaram **7/7** (`20261109-040703`),
os **2 testes desta QA** rodaram **2/2** (`20261109-041024`), a área `Squid.Tests.Containers`
rodou **70/70** (`20261109-042454`) e o gate final rodou **244/244** (`20261109-042748`,
`exit: 0` pelo log) = 242 do DEV + os 2 desta QA. Os 5 ACs tiveram prova de can-fail: **10 ciclos
de mutação** (M1..M9, com M6 em M6a/M6b), cada um com SHA256 **PRE** (S-16), vermelho classificado
**pelo log gravado**, restauração byte-exact com bump de mtime (S-23), hash = PRE e verde logo em
seguida — **10 rodadas vermelhas de propósito**, 9 mutando código de produto dentro dos `touches`
e 1 (M9) repetindo M1 contra o teste da QA para provar que a fuzz da SC-005 também sabe falhar.
Nenhum arquivo fora dos `touches`: `git status --porcelain -uall` do início da QA = **58 entradas**
de baseline, e no fim a árvore é esse baseline + exatamente os **4 artefatos** desta QA (checker
`TOUCHES-NO-OUTSIDE`), varredura de mtime desde 04:06 = **4 arquivos** (3 mutados/restaurados +
o teste da QA, todos dentro dos `touches`), resíduo `QA-MUTATION` = 0, hashes = PRE. Falhas
pré-existentes: **NENHUM** — baseline 242/242 verde antes de qualquer edição desta QA e 244/244
verde na árvore restaurada.

## What is proven

| AC | Scenario (executed) | Script | Result |
| --- | --- | --- | --- |
| AC-01 | `ContainerSearchRoutesTests.An_unlisted_group_never_appears_even_with_its_exact_name_authenticated_or_not` (`backend/tests/Squid.Tests/Containers/ContainerSearchRoutesTests.cs:27`): grupo `listed:false` nunca sai da busca — nome exato e slug exato, **sem sessão e com sessão** (`:37-40`); a mesma linha aparece assim que o `PATCH listed=true` grava a flag (`:43-45`). Metade da store: `ContainerStoreTests.The_search_returns_only_listed_and_living_rows_matching_name_or_slug_ignore_case` (`ContainerStoreTests.cs:133`) — a regra `IsSearchable` + substring no store. **M1** (`IsSearchable` → `true`) vermelha em AC-01 (`:37` — a linha proibida aparece) e em AC-03 (`:104`) | filtros xunit + 2 mutações (M1, M9) | PASS |
| AC-02 | `ContainerSearchRoutesTests.The_term_matches_name_and_slug_ignoring_case_and_a_blank_term_is_a_400` (`:49`): substring case-insensitive no nome (minúscula, caixa alta, acentuada) e no slug caixa alta (`:61-64`), termo sem match devolve vazio (`:65`), `q` ausente / vazio / só espaços → **400 `validation_failed`** nas três grafias (`:68-78`), `limit`/`offset` seguem as regras da casa e `limit=0` é a mesma recusa (`:82-85`). **M2** (match do nome vira sensível a caixa) vermelha em AC-02 (`:61`, `Expected ["vila-…"] Actual []`); **M3** (recusa de `q` em branco → fora) vermelha em AC-02 (`:76`, `Expected BadRequest Actual InternalServerError` — o termo vazio chega à store e explode) | filtro xunit + 2 mutações | PASS |
| AC-03 | `ContainerSearchRoutesTests.The_route_is_public_and_no_unlisted_no_deleted_nor_hidden_count_leaves_it` (`:89`): sem sessão e sem cookie a busca responde 200 com a linha listada e seu `memberCount` público (`:116-120`); nenhum não-listado e nenhum excluído sai com a mesma query, anônimo ou autenticado (`:123-126`); linha members-only listada revelada com **`memberCount = null`** para quem não a enxerga (`:130-134`). **100 tentativas da SC-005** da própria QA: `Qa0022SearchDiscoveryTests.A_hundred_sessionless_attempts_reveal_neither_the_unlisted_nor_the_deleted_row` (`Qa0022SearchDiscoveryTests.cs:31`) — 100 queries sem sessão mirando as duas linhas restritas (nome/slug exatos, caixa alta, parcial e token aleatório), cada resposta 200, nenhuma linha restrita, toda linha `listed=true` (`:74-89`), **controles positivos** para a busca não passar vazia (`:96-101`). **M4** (rota exige sessão) vermelha em AC-03 **e nos outros dois testes da classe** (`:143`, `Expected OK Actual Unauthorized`); **M5** (`memberCount` sempre revelado) vermelha em AC-03 (`:134`, `Expected Null Actual Number`); **M1** vermelha na fuzz da QA (`Qa0022:84` — `DoesNotContain` do slug restrito) | filtros xunit + 3 mutações (M4, M5, M1) | PASS |
| AC-04 (a) | `Sprint2ClosingFacetTests.The_invite_only_group_admits_nobody_in_a_hundred_attempts_and_then_its_invited_actor` (`Sprint2ClosingFacetTests.cs:31`): **100 tentativas** (10 atores × 10) sem convite → `attempts = 100`, `successes = 0`, todas **404 byte-idêntica** de slug ausente (`:40-56`) e a página de membros continua só com o Owner (`:57`); depois do convite alvo (GAP-13) o convidado entra **200 `active`/`member`** (`:60-70`) e aparece na página (`:72`). **M6a** (entrada `Invite` liberada na porta do join) vermelha (`:51`, `Expected NotFound Actual Forbidden`); **M6b** (`Invited` fora do bilhete de entrada) vermelha (`:65`, `Expected OK Actual NotFound`) | filtros xunit + 2 mutações | PASS |
| AC-04 (b) | `Sprint2ClosingFacetTests.The_single_publisher_gate_denies_a_plain_member_and_admits_the_named_active_publisher` (`:76`): `POST /policy` da rota grava `singlePublisher` com o segundo membro nomeado, versão 1 → 2, lido de volta na rota (`:91-104`); o **avaliador do host** (`IPolicyEvaluator` do DI, contexto do ator da seção F) sobre **as linhas que as rotas escreveram** nega o `Member` comum (`:112-116`, motivo cita `SinglePublisher`, `PolicyVersion 2`), **admite o publisher nomeado ativo** (`:118-121`) e nem o Owner ativo abre o portão fora da cadeira nomeada (`:124-126`). Drive pelo avaliador é a forma aceita pelo dispatch (não há rota de post nesta fase). **M7** (`join` grava `ActorId` em branco → a cadeira do policy não casa com a linha) vermelha em AC-04(b) (`:120`, `Assert.True Actual False` — o publisher nomeado deixa de ser admitido) | filtro xunit + 1 mutação | PASS |
| AC-04 (c) | `Sprint2ClosingFacetTests.The_succession_claim_and_the_operator_override_always_leave_an_active_owner` (`:130`): `NeverOrphanedAsync` (`:193-202` — exatamente 1 Owner ativo e `OwnerActorId` igual à linha) após **cada passo** da sequência: criação (`:136`), nomeação de sucessor (`:141`), claim dentro da janela → 403 §9.12 (`:145-147`), relógio +91 dias (FakeClock) → claim 200 com o herdeiro `Owner` (`:149-154`), override sem justificativa → 422 sem mover nada (`:157-163`), override justificado → 200 com o antigo Owner de volta como Owner ativo (`:167-174`). **M8** (`Ownership.MoveAsync` deixa de escrever as linhas) vermelha em AC-04(c) (`:201` — a linha ativa e o `OwnerActorId` do agregado divergem logo após o claim, `:152`) | filtro xunit + 1 mutação | PASS |
| AC-05 | gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` → **`20261109-042748-test-gate.log` com `exit: 0` e `Com falha: 0, Aprovado: 244, Total: 244`**, lido **dentro** do log (S-22); baseline do início `20261109-040457` = 242/242 `exit: 0`. Escopo: `git status --porcelain -uall` capturado às **04:06** (antes da QA escrever) idêntico no fim com **só os 4 artefatos** novos (teste + doc + 2 checkers); varredura de mtime desde 04:06 → **exatamente 4 arquivos**, todos nos `touches` (3 mutados/restaurados + o teste da QA); resíduo `QA-MUTATION` em `backend/` = **0**; hash final = **PRE** nos 3 mutados + `TESTFILE-INTEGRITY` no teste da QA; **self-test do checker**: limiar `244` → `9999` de propósito → `CHECK GATE-NEWEST-SUMMARY FAIL` **exit 1**, restaurado → exit 0 com SHA256 idêntico ao PRE | gate + git/mtime/hash + checkers 17/17 + self-test | PASS |

Falhas pré-existentes: **NENHUM** — baseline 242/242 verde antes de qualquer edição desta QA e
244/244 verde na árvore restaurada; as 10 rodadas vermelhas foram mutações deliberadas (10 ciclos),
todas revertidas byte-exact com bump de mtime. Nenhuma vermelha fora dos ciclos: o teste da QA
compilou e passou na primeira execução (`20261109-041024`, 2/2).

### DEV decisions verified (not disputed — executed)

| Decision | Evidence |
| --- | --- |
| envelope de resposta da busca = `{ items }` (estilo da casa de `GET /members` e `GET /api/admin/audit`) | `backend/src/Squid.Api/Containers/ContainerEndpoints.cs:240` (`Results.Json(new { items })`); cada teste lê `.items` e falha sem ele (M4/M5 vermelhas passam por esse caminho) |
| `q` só-brancos conta como vazio → `400 validation_failed` | `ContainerEndpoints.cs:219-223` (`.Trim()` depois `Length == 0`); executado com `q=%20%20` (`ContainerSearchRoutesTests.cs:72`); **M3** derruba exatamente esta linha (`:76` → 500) |
| `memberCount = null` na linha da busca que o leitor não enxerga (leitura da regra de metadado da SC-005) | `ContainerEndpoints.cs:236-237` + `ContainerView.cs:18-22`; a SPEC-0008 `Rules` diz literalmente "memberCount só aparece para quem já pode ver o contêiner" — leitura da QA **idêntica** à do DEV, nenhuma divergência da seção H; executado em `ContainerSearchRoutesTests.cs:130-134` e no controle da QA `Qa0022:99-101`; **M5** derruba (`Expected Null Actual Number`) |
| slug `search` sombreado pela rota literal (S-33, não é AC) | rota literal `ContainerEndpoints.cs:104` sob a rota de parâmetro `:101` (segmento literal vence na tabela de rotas); **executado**: `Qa0022SearchDiscoveryTests.The_literal_search_route_shadows_a_container_whose_slug_is_search` (`:105`) cria o contêiner `slug = search` (201), `GET /api/containers/search` sem termo responde **400 `validation_failed`** (nunca o corpo 200 do contêiner) e com termo responde a página de descoberta com o próprio `search` dentro |
| paginação depois do filtro em memória `IsSearchable` com `shortcut:` declarado (S-32, sem migration no gate) | `backend/src/Squid.Service/SqliteContainerStore.cs:231-234` (comentário `shortcut:` com o limite e o upgrade), filtro em `:246-253` e `OrderBy → Skip → Take` em `:255-259` (offset **depois** do filtro, como o comentário promete); executado pelos asserts de `limit`/`offset` (`ContainerSearchRoutesTests.cs:82-85`) |

## Scripts

| Script | Language / runner | What it does |
| --- | --- | --- |
| `backend/tests/Squid.Tests/Containers/ContainerSearchRoutesTests.cs` | xunit (runner da casa) | os 3 fatos do DEV sobre AC-01..AC-03 contra o host HTTP |
| `backend/tests/Squid.Tests/Containers/Sprint2ClosingFacetTests.cs` | xunit | os 3 fatos da facet de fecho (AC-04 a/b/c) |
| `backend/tests/Squid.Tests/Containers/ContainerStoreTests.cs` (`:133`) | xunit | a metade da store da busca (7º fato novo do DEV) |
| `backend/tests/Squid.Tests/Containers/Qa0022SearchDiscoveryTests.cs` | xunit | os 2 fatos desta QA: 100 tentativas da SC-005 da busca + sondagem do S-33 |
| `docs/sdd/_tools/sdd-test.ps1 test [-TestFilter …]` | pwsh + dotnet | runner da casa: 2 gates (242 e 244), 3 filtros verdes fora dos ciclos (7, 2 e 70) e as 20 rodadas vermelhas/verdes dos 10 ciclos |
| `docs/sdd/qa/qa-mutate-task-0019.ps1` (genérico, reaproveitado desde a QA-0018) | pwsh | driver de mutação can-fail: PRE hash → muta **1** padrão (recusa se contagem ≠ 1) → vermelho pelo log → restaura byte-exact + mtime (S-23) → hash == PRE → verde |
| `docs/sdd/qa/qa-check-task-0022.ps1` | pwsh | 17 checks: exit/resumo do gate de baseline e do gate/filtro mais novos (S-22), filtros fixos 7 e 70 e o do teste da QA (2), SHA256 PRE dos 3 mutados (S-16), integridade do teste da QA, resíduo `QA-MUTATION`, escopo dos `touches` (git desde 04:06), varredura de mtime, rotas/regras estáticas, contagem de `[Fact]`, QA-DOC e `sdd-state check` |
| `docs/sdd/qa/qa-check-task-0022.sh` | bash (WSL) | gêmeo Linux do check acima, mesmos 17 check ids, mesmo contrato de exit |

## How to reproduce

Prerequisites: Windows com .NET SDK 10, repositório em `C:\dev\squid-squad-net\squid-execution`
(sem rede depois do restore). Sem servidor, sem banco compartilhado, sem instalação.

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test
# esperado: exit 0 · Com falha 0, Aprovado 244 · log novo em docs/sdd/runs/test-runs/

powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~Squid.Tests.Containers"
# esperado: exit 0 · Com falha 0, Aprovado 70 (área da task, inclui os 2 desta QA)

powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~ContainerSearchRoutesTests|FullyQualifiedName~Sprint2ClosingFacetTests|FullyQualifiedName~The_search_returns_only_listed_and_living_rows"
# esperado: exit 0 · Com falha 0, Aprovado 7 (os 7 testes novos do DEV)

powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0022.ps1
# esperado: exit 0 · SUMMARY total=17 fails=0
wsl -e bash -c 'cd /mnt/c/dev/squid-squad-net/squid-execution && bash docs/sdd/qa/qa-check-task-0022.sh'
# esperado: exit 0 · SUMMARY total=17 fails=0
```

Mutações (ciclo completo, precisa terminar com `RESULT: PROVEN` e exit 0 — ele restaura sozinho):

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0019.ps1 -File backend/src/Squid.Api/Containers/Ownership.cs -Old 'foreach (var row in SuccessionRules.Transfer(members, container.Slug, newOwnerHandle, newOwnerActorId, now))' -New 'foreach (var row in Array.Empty<ContainerMembership>())' -Filter 'FullyQualifiedName~Sprint2ClosingFacetTests'
# esperado: RESULT: PROVEN · vermelho 1 falha (NeverOrphaned: a linha ativa e o OwnerActorId do agregado divergem) · hash = PRE · verde 3/3
```

## Last run

Classificação sempre pelo log gravado (S-22). As 10 rodadas vermelhas dos ciclos são mutações
**de propósito**.

| When (log) | Command | Exit (log) | Output (tail) |
| --- | --- | --- | --- |
| 04:04:57 | `sdd-test.ps1 test` (baseline desta QA) | 0 | `Com falha: 0, Aprovado: 242, Total: 242` |
| 04:07:03 | filtro `~ContainerSearchRoutesTests + ~Sprint2ClosingFacetTests + ~The_search_returns_only_listed_and_living_rows` | 0 | `Com falha: 0, Aprovado: 7, Total: 7` (os 7 do DEV) |
| 04:10:24 | filtro `~Qa0022` (testes desta QA) | 0 | `Com falha: 0, Aprovado: 2, Total: 2` |
| 04:13:24 / 04:13:38 | driver **M1** (`IsSearchable` → `true`) vermelho / verde | 1 / 0 | `Com falha: 2` (`:37` AC-01 e `:104` AC-03) → `Aprovado: 3` |
| 04:14:10 / 04:14:24 | driver **M2** (match do nome → sensível a caixa) | 1 / 0 | `Com falha: 1` (`Expected ["vila-…"] Actual []`, `:61`) → `Aprovado: 3` |
| 04:14:56 / 04:15:11 | driver **M3** (recusa de `q` em branco → fora) | 1 / 0 | `Com falha: 1` (`Expected BadRequest Actual InternalServerError`, `:76`) → `Aprovado: 3` |
| 04:15:50 / 04:16:06 | driver **M4** (rota de busca exige sessão) | 1 / 0 | `Com falha: 3` (os 3 testes: `Expected OK Actual Unauthorized`, `:143`) → `Aprovado: 3` |
| 04:16:32 / 04:16:47 | driver **M5** (`memberCount` sempre revelado) | 1 / 0 | `Com falha: 1` (`Expected Null Actual Number`, `:134`) → `Aprovado: 3` |
| 04:17:24 / 04:17:44 | driver **M6a** (entrada `Invite` liberada na porta do join) | 1 / 0 | `Com falha: 1` (`Expected NotFound Actual Forbidden`, facet `:51`) → `Aprovado: 3` |
| 04:18:27 / 04:18:49 | driver **M6b** (`Invited` fora do bilhete) | 1 / 0 | `Com falha: 1` (`Expected OK Actual NotFound`, facet `:65`) → `Aprovado: 3` |
| 04:19:34 / 04:19:56 | driver **M7** (`join` grava `ActorId` em branco) | 1 / 0 | `Com falha: 1` (`Assert.True Actual False`, facet `:120`) → `Aprovado: 3` |
| 04:20:36 / 04:20:59 | driver **M8** (`MoveAsync` não escreve as linhas) | 1 / 0 | `Com falha: 1` (`NeverOrphaned` `:201`, dono × agregado, facet `:152`) → `Aprovado: 3` |
| 04:23:07 / 04:23:20 | driver **M9** (M1 de novo, contra o teste da QA) | 1 / 0 | `Com falha: 1` (fuzz `Qa0022:84`) → `Aprovado: 2` |
| 04:24:54 | filtro `~Squid.Tests.Containers` (área) | 0 | `Com falha: 0, Aprovado: 70, Total: 70` |
| 04:27:48 | `sdd-test.ps1 test` (**gate final**, AC-05) | 0 | `Com falha: 0, Aprovado: 244, Total: 244` |
| ~04:29 | varredura (hashes + resíduo + git + mtime + leitura dos logs) | 0 | 3 hashes = PRE; resíduo `QA-MUTATION` = 0; mtime desde 04:06 = 4 arquivos nos `touches` |
| ~04:30 | `wsl -e sh -lc` (probe de plataforma) | 0 | `dotnet:ABSENT` `pwsh:ABSENT` `bash:ok` `git:ok` `sha256sum:ok` — a **suite** em Linux segue **unverified** |
| ~04:41 | `powershell ... qa-check-task-0022.ps1` (1a execucao) | 0 | `SUMMARY total=17 fails=0 RESULT: PASS` (17/17, Windows) |
| ~04:42 | `wsl ... bash docs/sdd/qa/qa-check-task-0022.sh` (1a execucao) | 1 | `CHECK RESTORE-SHA256 FAIL` (o proprio checker tinha o SHA256 PRE do Ownership digitado errado) -> `SUMMARY total=17 fails=1 RESULT: FAIL` - defeito do checker, corrigido |
| ~04:43 | `wsl ... bash docs/sdd/qa/qa-check-task-0022.sh` (2a execucao) | 0 | `SUMMARY total=17 fails=0 RESULT: PASS` (17/17, gemeo Linux) |
| ~04:44 | self-test do check: limiar `244` -> `9999`, depois restaurado | 1 -> 0 | quebra: `CHECK GATE-NEWEST-SUMMARY FAIL ... RESULT: FAIL` exit 1; restaurado: exit 0, SHA256 `D49AEE39...` igual ao PRE |
| ~04:48 | `qa-check-task-0022.ps1` + `qa-check-task-0022.sh` (execucao definitiva pos-appends) | 0 / 0 | `SUMMARY total=17 fails=0 RESULT: PASS` nos dois (Windows e WSL) |
| ~04:49 | `sdd-state.ps1 check` (pos-appends) | 0 | `check: OK - log entries 13, evidence rows 31` (a linha seguinte fecha em 32) |

## Mutation ledger (S-16 / S-23)

| # | File mutated | Change | PRE = POST SHA256 | Red run (log) | Restore proof |
| --- | --- | --- | --- | --- | --- |
| M1 | `backend/src/Squid.Service/SqliteContainerStore.cs` | filtro da busca: `container.IsSearchable` → `true` (listados e excluídos passam) | `7A7BA9C2CD679D7EA011F6400123AC5327616A62A9DD6C5FC3C02BCDD504F507` | `20261109-041324` exit 1 — 2 falhas: AC-01 + AC-03 | hash = PRE, verde 3/3 |
| M2 | `SqliteContainerStore.cs` | match do nome: `OrdinalIgnoreCase` → `Ordinal` | idem M1 | `041410` exit 1 — 1 falha: AC-02 | hash = PRE, verde 3/3 |
| M3 | `backend/src/Squid.Api/Containers/ContainerEndpoints.cs` | recusa do termo: `if (query.Length == 0)` → `if (query.Length < 0)` | `DAEE4CC7440C4D3C0ABE7E6D72C572EDAE1F0049F038D4759FE61B3C187725C0` | `041456` exit 1 — 1 falha: AC-02 (500 no lugar do 400) | hash = PRE, verde 3/3 |
| M4 | `ContainerEndpoints.cs` | assinatura da busca ganha a trava de sessão (`context.User.Identity?.IsAuthenticated != true` → 401) e o corpo vira `AuthedSearch` | idem M3 | `041550` exit 1 — 3 falhas: AC-01+AC-02+AC-03 | hash = PRE, verde 3/3 |
| M5 | `ContainerEndpoints.cs` | `seen ? container.MemberCount : null` → `container.MemberCount` | idem M3 | `041632` exit 1 — 1 falha: AC-03 | hash = PRE, verde 3/3 |
| M6a | `ContainerEndpoints.cs` | `CanEnterAsync`: `Join is Open or Request` → `… or JoinPolicy.Invite` (o sem-convite alcança o gate) | idem M3 | `041724` exit 1 — 1 falha: AC-04(a) (`Expected NotFound Actual Forbidden`) | hash = PRE, verde 3/3 |
| M6b | `ContainerEndpoints.cs` | `CanEnterAsync`: `… or MembershipState.Invited` removido (o convidado perde o bilhete) | idem M3 | `041827` exit 1 — 1 falha: AC-04(a) (o walk-in vira 404) | hash = PRE, verde 3/3 |
| M7 | `ContainerEndpoints.cs` | `join`: `ActorId = membership?.ActorId ?? ActorId(context, current.Handle)` → `?? string.Empty` (a cadeira do `singlePublisher` não casa com a linha) | idem M3 | `041934` exit 1 — 1 falha: AC-04(b) (`Assert.True` do publisher nomeado) | hash = PRE, verde 3/3 |
| M8 | `backend/src/Squid.Api/Containers/Ownership.cs` | `foreach (var row in SuccessionRules.Transfer(…))` → `foreach (var row in Array.Empty<ContainerMembership>())` (a troca nunca escreve as linhas) | `F35F5A56683F301A12E7F30955023FBDB5B70F3E8396EA4EFB81FD039DFBCA5A` | `042036` exit 1 — 1 falha: AC-04(c) (`NeverOrphanedAsync:201`) | hash = PRE, verde 3/3 |
| M9 | `SqliteContainerStore.cs` | M1 repetido contra `FullyQualifiedName~Qa0022` (a fuzz da QA também sabe falhar) | idem M1 | `042307` exit 1 — 1 falha: `Qa0022:84` | hash = PRE, verde 2/2 |

Todos os 10 ciclos terminaram `RESULT: PROVEN` com exit 0: `PATTERN-COUNT: 1` (o driver recusa
padrão ausente ou duplicado), `HASH-MATCH: True` e verde depois do restore. Nenhum padrão com
aspas duplas foi usado (a armadilha da M11a da QA-0019) e nenhum arquivo fora dos `touches` foi
mutado: os 10 alvos estão em `Squid.Service/**` ou `Squid.Api/Containers/**`. Resíduo final:
varredura de `QA-MUTATION` em `backend/` (src + tests, sem bin/obj) → **0**; hash final dos 3
arquivos mutados = PRE e o teste da QA nunca foi alvo (`TESTFILE-INTEGRITY`), confirmado pelo
`qa-check-task-0022.ps1`/`.sh`.

## Findings

| # | Severity | Where | What breaks | Evidence |
| --- | --- | --- | --- | --- |
| F1 | INFO (adjudicação na consolidação, nenhum AC quebra) | `backend/src/Squid.Api/Containers/ContainerEndpoints.cs:236-237` + `ContainerView.cs:31` contra `ContainerEndpoints.cs:1243-1258` | A linha da busca de um contêiner `Listed` traz o **`policy` inteiro** (inclusive `publisherIds`, `visibility` e `join`) a um leitor anônimo para quem `GET /api/containers/{slug}` responderia 404 (MembersOnly): só o `memberCount` é rebaixado a `null`. A tabela de saídas da SPEC-0008 pede exatamente `ContainerView` em `GET`, `search` e `POST`, e a regra de metadado da SC-005 que o dispatch levou à pauta (`memberCount`) está implementada como a spec escreve — por isso não é falha. Levanto para adjudicação: se a intenção é esconder o interior do contêiner, `policy` é o próximo candidato a `null`; se o descobrimento completo é a intenção, vale registrar a seção H | busca executada (`ContainerSearchRoutesTests.cs:130-134`, `Qa0022:99-101`); caminho do `GET` = **leitura estática** (`:196-207`, `:1243-1258`), não executado nesta QA |
| F2 | INFO (decisão registrada — S-33 verificado, não é AC) | `backend/src/Squid.Api/Containers/ContainerEndpoints.cs:101-104` | O slug `search` fica inalcançável por `GET /api/containers/{slug}`: o segmento literal vence na tabela de rotas, então a resposta daquele caminho é a busca (sem termo → 400) e nunca o contêiner. Comportamento **executado e fixado** pelo teste da QA | `Qa0022SearchDiscoveryTests.cs:105-129` (cria `slug = search` → 201; sem termo → 400 `validation_failed`; com termo → a página com o próprio `search` dentro) |
| F3 | INFO (decisão registrada — S-32 verificado, não é AC) | `backend/src/Squid.Service/SqliteContainerStore.cs:231-259` | A busca varre as próprias linhas da store em memória (`SELECT State WHERE StoreName`), aplica `IsSearchable` + substring e só então pagina (`OrderBy → Skip → Take`): sem índice `(Listed, Slug)` e sem migration — proibido pela porta de gate, `shortcut:` declarado no código com o limite e o gatilho de upgrade | comentário `shortcut:` (`:231-234`), ordem do filtro (`:246-259`), paginação executada (`ContainerSearchRoutesTests.cs:82-85`) |

Nenhum BLOCKER, nenhum MAJOR — nenhum dos 5 ACs falhou, não há contradição código × contrato e
as três decisões do dispatch que a QA foi encarregada de verificar (envelope, `q` em branco,
`memberCount` nulo) foram executadas e batem com a SPEC-0008.

## Not covered, and why

- **Suite em Linux/WSL** — probe desta sessão: `wsl -e sh -lc` responde `dotnet:ABSENT`
  `pwsh:ABSENT` (`bash:ok`, `git:ok`, `sha256sum:ok`); instalar SDK é gate e não foi aprovado →
  **unverified**, nunca assumido verde. O que roda em Linux nesta sessão é o checker `.sh`
  (17/17 PASS no WSL). Risco: regressão específica de Linux só é vista no CI (ubuntu já rodou a
  suite via pwsh em runs anteriores — memory bank, não re-executado aqui).
- **Índice/escala da busca (`Listed`, `Slug`)** — S-32: a varredura em memória das próprias linhas
  é proporcional ao total de contêineres da instância; nada aqui mede escala e a migration é
  proibida nesta task. Risco: baixo no volume atual, conhecido, com o `shortcut:` declarado e o
  gatilho de upgrade escrito no código.
- **Pós-carência (purge depois dos 30 dias do soft delete)** — não existe job de purge nesta fase
  (pré-existente e fora do escopo do dispatch): o que se prova aqui é que o excluído **sai da
  busca** (AC-01/AC-03 + store `IsSearchable`), como a QA-0020 já provou o `DeletedAt` e a
  janela. Risco: nenhum para esta task; o purge é fase própria.
- **CSRF/401 das rotas de mutação** — a busca é leitura pública (AC-03) e as rotas da facet
  (`join`, `invites`, `policy`, `claim`, `/api/admin/transfer`) receberam esses pinos nas
  tasks anteriores (QA-0018..QA-0020); aqui elas são reutilizadas, não re-provadas. Risco: baixo.
- **S-29 (paginação de membros em memória), S-31 (renovação do relógio do Owner)** —
  pré-existentes, fora do escopo desta task, não re-exercitados (as contagens de membros usadas
  aqui são de um host por classe de teste). Risco: nenhum novo.
- **Corrida de duas buscas/escritas simultâneas** — só estática (os dois `shortcut:` sem
  transação já declarados em `SqliteContainerStore.cs:231` e no `CreateAsync`). Risco: baixo,
  conhecido e documentado no código.

## Platforms

| Platform | State | Why |
| --- | --- | --- |
| Windows (PowerShell + .NET SDK 10) | **executado** | 2 gates (242 e 244), 3 filtros verdes fora dos ciclos (7, 2 e 70), 10 ciclos de mutação (20 rodadas vermelhas/verdes) e o checker `.ps1` (17/17 + self-test), todos classificados pelos logs gravados |
| Linux / bash (WSL) | **executado (só o check estático)** | `qa-check-task-0022.sh` roda e fecha igual (17/17, `git`/`sha256sum`/`find`/`powershell.exe` via interop); a **suite** em Linux segue **unverified** (sem dotnet/pwsh no WSL nesta sessão) |
| Linux nativo / CI ubuntu | **não re-executado nesta QA** | histórico verde no CI via pwsh (memory bank); sem aprovação para disparar runner externo |
