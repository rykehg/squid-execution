<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: CONCLUIDO
- owner: qa
- lastVerified: 2026-11-09 00:57:44

# QA-0019 — Papéis, moderação e contexto do avaliador (GAP-11)

- task: TASK-0020
- spec: SPEC-0008 (seção F + Rules), data-model §2.4/§3.2/§3.3/§2.11, contracts/http-api.md §4/§8/§9.11
- runId: R-20261108-01
- qaAttempt: 1/2
- verdict: PASSED

Execução, não leitura: o gate de baseline rodou **214/214** no começo (`20261109-000908`, `exit: 0`
lido **dentro** do log — S-22), a área `Squid.Tests.Containers` rodou **42/42**, a área
`Squid.Tests.Domain` **62/62** (matriz TASK-0007 intacta pelo overload) e o gate final rodou
**216/216** (`20261109-004537`, também `exit: 0` pelo log) = 214 do DEV + os **2 testes novos**
desta QA (`Qa0019MemberModerationEdgeTests`). Os 6 ACs tiveram prova de can-fail:
**12 ciclos de mutação** (M1..M12) + 1 recusa de driver, cada um com SHA256 **PRE** (S-16),
vermelho classificado **pelo log gravado**, restauração byte-exact com bump de mtime (S-23) e
verde logo em seguida — e as duas recusas (M11 tentativa 1) não mutaram nada. Nenhum arquivo fora
dos `touches` foi tocado: `git status` do fim = baseline do início + exatamente os 4 artefatos
desta QA (checker `TOUCHES-NO-OUTSIDE`), resíduo `QA-MUTATION` = 0, hashes = PRE.

## What is proven

| AC | Scenario (executed) | Script | Result |
| --- | --- | --- | --- |
| AC-01 | `MemberModerationRoutesTests.An_admin_changes_a_role_and_the_trail_records_author_when_and_policy_version` (`MemberModerationRoutesTests.cs:31`): Owner promove `Member`→`Moderator` → **200** com `role moderator`/`state active` (`:43-52`), a página mostra o papel novo (`:55-57`), trilha com **1** linha `ChangeRole/Allow`, `ActorId` do autor, `PolicyVersion 1`, `At != default`, `Reason` não vazia (`:60-65`). **+ o degrau Admin literal**: `Qa0019MemberModerationEdgeTests.cs:29` — um `Admin` (não-Owner) muda papel → **200**, linha no store e na página (`:60-66`), e a trilha desse Admin carrega **`PolicyVersion 2`** — a versão *vigente* depois do `PUT /policy`, não um 1 fixo (`:49-56`, `:73-80`). **M1 deixa 4 testes vermelhos** (AC-01 `:46`, AC-02, AC-05 e o Qa0019 `:60`), **M8** (version→1) deixa o Qa0019 `:79` vermelho, **M12** (ação `ChangeRole`→`View`) deixa os 2 testes de trilha vermelhos | filtros xunit + 3 mutações | PASS |
| AC-02 | `MemberModerationRoutesTests.The_moderation_verbs_follow_33_with_justifications_and_the_owner_invariants` (`:69`): `approve` (`:90-94` + contador), `reject` apaga a linha (`:97-104`), `silence`/`unsilence` com contador (`:107-112`), `ban` sem justificativa → **422** `validation_failed` com a linha intacta (`:115-119`), `ban` com → **200** `banned` (`:122-126`), **Owner banido → 403** com a linha Owner/Active intacta (`:129-135`), `unban` só-com-espaço → **422** (`:138-142`), `unban` com justificativa → **200** (`:144-147`), aresta inexistente → **400** (`:150-154`), **ban por Moderator → 403** (`:157-161`). Pura: `MembershipRulesTests.The_moderation_verbs_follow_the_machine_and_refuse_everything_else` (`:73`) — as 6 arestas + 8 recusas. **+ bordas**: `Qa0019MemberModerationEdgeTests.cs:89` — `role: "owner"` → **400** `validation_failed` (`:107-111`), demover a linha do Owner → **409** `last_owner` com a linha intacta (`:113-119`), justificativa de **1001** chars → **400** e nada escrito, **1000** → **200** `banned` (`:122-133`), **unban por Moderator → 403** com a linha ainda `banned`, unban por Owner → **200** `active` (`:136-150`). **M2** (422→só-null) vermelha no unban-de-espaço, **M6** (guarda do Owner→silence) vermelha no 403, **M7** (reject→não-remove) vermelha em 2, **M5** (Admin→Moderate) vermelha em 3, **M9** (teto→2000), **M10** (demove→Guest), **M11** (ParseRole aceita `owner`) — cada um deixa o teste correspondente vermelho | filtros xunit + 5 mutações | PASS |
| AC-03 | metade 1: `A_silenced_member_reads_a_members_only_container_but_its_writes_are_denied` (`:165`): container vira `membersOnly`, os dois membros silenciados, **GET Silenced → 200** (`:194-195`), escrita do Admin silenciado → **403** `policy_denied` e nada muda (`:198-203`). metade 2: `A_banned_member_reads_a_public_container_as_the_404_of_an_absent_slug` (`:207`): **GET Banned em público → 404 byte-idêntico** ao slug inexistente (`:223-227`) e a trilha registra `View/NotFound` (`:230-231`). Avaliador: `PolicyActorContextTests.cs:69` (Silenced negado em **todas** as 7 ações mesmo como Admin em política permissiva) e `:87` (Banned negado em **todas**, inclusive `Join`). **M3** (guarda `Banned`→`Silenced`) deixa **as duas metades** vermelhas | filtros xunit + 1 mutação | PASS |
| AC-04 | `PolicyActorContextTests.cs:35` (facet DoD): publisher nomeado ativo → **permitido** com `PolicyVersion 4`, `Member` comum → **negado** com `SinglePublisher` no motivo, e os dois caminhos só-papel (wrapper TASK-0007) continuam **negados** (`:50-51`); `:55` (a senteira do publisher não sobrevive a estado que não escreve: Silenced/Banned/sem-linha); `:99` (**Guest negado em toda escrita** com política permissiva mas `Join` permitido); `:117` (sem linha = sem membership, `Join` segue a política). GAP-11 fecha: `PolicyActor(Role, State?, IsPublisher)` existe (`IPolicyEvaluator.cs:44`), a overload é avaliada (`:65`), o wrapper só-papel responde como antes (`:76-77`) — a matriz TASK-0007 está verde nas **62/62** do Domain e nas 214 do baseline. **M4** (`return actor.IsPublisher`→`return true`) deixa a facet vermelha | filtros xunit + 1 mutação | PASS |
| AC-05 | `Patch_without_a_session_is_401_and_a_moderator_cannot_change_another_role` (`:235`): sem sessão → **401** `not_authenticated` antes de olhar linha (`:253-257`); Moderator tenta mudar papel de outro → **403** `policy_denied` e a linha continua `Member` (`:260-264`); a recusa vai para a trilha com o Moderator como autor e `PolicyVersion 1` (`:267-271`); `silence` continua do Moderator → **200** (`:274-277`). **M1** deixa este teste vermelho em 2 pontos (papel que não grava + silence negado), **M5** deixa vermelho no 403 que vira 200 | filtros xunit + 2 mutações | PASS |
| AC-06 | gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` → **`20261109-004537-test-gate.log` com `exit: 0` e `Com falha: 0, Aprovado: 216, Total: 216`**, lido **dentro** do log (S-22); escopo: `git status --porcelain -uall` capturado às 00:06 (antes da QA escrever) idêntico no fim com **só os 4 artefatos novos** desta QA; varredura de mtime em `backend/src` + `backend/tests` desde 00:05 → **exatamente 4 arquivos**, todos dentro dos `touches` (3 mutados/restaurados + o teste da QA); resíduo `QA-MUTATION` em `backend/` = **0**; hash final = PRE nos 3 mutados + `TESTFILE-INTEGRITY` no teste da QA | gate + git/mtime/hash + checkers 14/14 | PASS |

Falhas pré-existentes: **NENHUM** — baseline 214/214 verde antes de qualquer edição desta QA e
216/216 verde na árvore restaurada; todas as 24 rodadas vermelhas foram mutações deliberadas
(12 ciclos), todas revertidas byte-exact. O gap conhecido S-30 (`POST /join` em contêiner não-público
→ 404 para `Invited`, `ContainerEndpoints.cs:293,867-876`, pre-existente da TASK-0019 e corrigido
como AC-07 da TASK-0021) **não foi tocado** por nenhum cenário desta QA: todos os joins dos testes
são em contêiner público.

### DEV decisions verified (not disputed — executed)

| Decision | Evidence |
| --- | --- |
| `ban`/`unban` sem justificativa (ou só espaços) → `422 validation_failed` | `ContainerEndpoints.cs:549-554` (`Trim` + `IsNullOrEmpty`); AC-02 `:115-119` e `:138-142`; vermelho **M2** |
| teto da justificativa ≤ 1000 (`MaxJustificationLength`) | `ContainerEndpoints.cs:71,556-559` → 1001 = **400** `validation_failed`, 1000 aceito; Qa0019 `:122-133`; vermelho **M9** |
| `Owner` banido → `403 policy_denied` (SPEC Rules "Owner banido → negado") | `ContainerEndpoints.cs:563-567`; AC-02 `:129-135`; vermelho **M6** (a guarda sai e o caminho vira 409) |
| conceder `role: "owner"` → `400` | `ParseRole` sem o degrau Owner (`:1047-1053`) + guarda `:569-572`; Qa0019 `:107-111`; vermelho **M11** |
| demover a linha do Owner → `409 last_owner` | `ContainerEndpoints.cs:574-578`; Qa0019 `:113-119`; vermelho **M10** |
| `reject` apaga a linha (`§3.3 (removido)`) | `MembershipRules.cs:53` (`RemovesRow=true`) + `ContainerEndpoints.cs:598-603` (`DeleteMembershipAsync`); AC-02 `:97-104`; vermelho **M7** |
| palavra da ação na trilha = `ChangeRole` (data-model §2.11) | `IPolicyAuditStore.cs:36` + `ContainerEndpoints.cs:622,626`; filtrada nos testes de trilha AC-01/AC-05/Qa0019; vermelho **M12** (ação→`View` deixa os 2 vermelhos); `ROUTES-STATIC` lê o const do disco |
| `forRead` na visibilidade: `Silenced` GET 200, `Banned` GET 404 idêntico ao ausente, escrita só com existência | `ContainerEndpoints.cs:862-877` (`:873` é a guarda `forRead`); AC-03 nas duas metades; vermelho **M3**; os pins da QA-0018 (join de banido em público → 403, `MembershipRoutesTests`) seguem verdes nas 216 |
| `PolicyActor` + wrapper só-papel preservando a matriz TASK-0007 | `IPolicyEvaluator.cs:44,55,65,76-77`; Domain **62/62**, baseline **214** sem mudança; vermelho **M4** |

## Scripts

| Script | Language / runner | What it does |
| --- | --- | --- |
| `backend/tests/Squid.Tests/Containers/Qa0019MemberModerationEdgeTests.cs` | xunit (runner da casa) | 2 testes novos da QA: Admin literal com `policyVersion` vigente (AC-01) e as bordas do Owner/justificativa/unban (AC-02) |
| `backend/tests/Squid.Tests/Containers/MemberModerationRoutesTests.cs` + `MembershipRulesTests.cs` + `Domain/PolicyActorContextTests.cs` | xunit | os 13 testes novos do DEV (5+1+7) — executados nos filtros, gates e mutações |
| `docs/sdd/_tools/sdd-test.ps1 test [-TestFilter …]` | pwsh + dotnet | runner da casa: 3 gates + 5 filtros + 24 rodadas vermelhas/verdes desta QA |
| `docs/sdd/qa/qa-mutate-task-0019.ps1` (reaproveitado, é genérico) | pwsh | driver de mutação can-fail: PRE hash → muta **1** padrão (recusa se contagem ≠ 1) → vermelho → restaura byte-exact + mtime (S-23) → hash == PRE → verde |
| `docs/sdd/qa/qa-check-task-0020.ps1` | pwsh | 14 checks: exit/resumo do gate de baseline e do gate/filtro mais novos (S-22), SHA256 PRE dos 3 mutados (S-16), integridade do teste da QA, resíduo `QA-MUTATION`, escopo dos `touches` (git desde 00:06), varredura de mtime desde 00:05, rotas/regras estáticas, contagem de `[Fact]`, `sdd-state check` |
| `docs/sdd/qa/qa-check-task-0020.sh` | bash (WSL) | gêmeo Linux do check acima, mesmos 14 check ids, mesmo contrato de exit |

## How to reproduce

Prerequisites: Windows com .NET SDK 10, repositório em `C:\dev\squid-squad-net\squid-execution`
(sem rede depois do restore). Sem servidor, sem banco compartilhado, sem instalação.

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test
# esperado: exit 0 · Com falha 0, Aprovado 216 · log novo em docs/sdd/runs/test-runs/

powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~MemberModeration"
# esperado: exit 0 · Com falha 0, Aprovado 7 (5 DEV + 2 QA)

powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0020.ps1
# esperado: exit 0 · SUMMARY total=14 fails=0
wsl -e bash -c 'cd /mnt/c/dev/squid-squad-net/squid-execution && bash docs/sdd/qa/qa-check-task-0020.sh'
# esperado: exit 0 · SUMMARY total=14 fails=0
```

Mutações (ciclo completo, precisa terminar com `RESULT: PROVEN` e exit 0 — ele restaura sozinho):

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0019.ps1 -File backend/src/Squid.Api/Containers/ContainerEndpoints.cs -Old "if (target.Role == MembershipRole.Owner)" -New "if (target.Role == MembershipRole.Guest)" -Filter "FullyQualifiedName~Qa0019MemberModerationEdgeTests"
# esperado: RESULT: PROVEN · vermelho 1 falha (409 last_owner) · hash = PRE · verde 2/2
```

## Last run

Classificação sempre pelo log gravado (S-22). As 24 rodadas vermelhas são mutações **de propósito**.
A tentativa 1 do M11 (padrão com aspas duplas) **não mutou nada** — recusa do próprio driver
(`PATTERN-COUNT 0`), registrada como evidência.

| When (log) | Command | Exit (log) | Output (tail) |
| --- | --- | --- | --- |
| 00:09:08 | `sdd-test.ps1 test` (baseline desta QA) | 0 | `Com falha: 0, Aprovado: 214, Total: 214` |
| 00:10:39 | filtro `~Qa0019MemberModerationEdgeTests` (2 testes novos) | 0 | `Com falha: 0, Aprovado: 2` |
| 00:11:45 | filtro `~Squid.Tests.Containers` (baseline da área) | 0 | `Com falha: 0, Aprovado: 42` |
| 00:11:55 | filtro `~Squid.Tests.Domain` (matriz + contexto) | 0 | `Com falha: 0, Aprovado: 62` |
| 00:19:47 / 00:20:09 | driver **M1** (papel não grava) vermelho / verde | 1 / 0 | `Com falha: 4, Aprovado: 3` (AC-01, AC-02, AC-05, Qa0019) → `Aprovado: 7` |
| 00:20:53 / 00:21:13 | driver **M2** (422 só-null) vermelho / verde | 1 / 0 | `Com falha: 1` (unban de espaços) → `Aprovado: 5` |
| 00:21:44 / 00:22:03 | driver **M3** (guarda Banned→Silenced) vermelho / verde | 1 / 0 | `Com falha: 2` (AC-03 metade 1 e 2) → `Aprovado: 5` |
| 00:22:27 / 00:22:37 | driver **M4** (singlePublisher sempre concede) vermelho / verde | 1 / 0 | `Com falha: 1` (facet DoD) → `Aprovado: 7` |
| 00:23:13 / 00:23:37 | driver **M5** (capacidade Admin→Moderate) vermelho / verde | 1 / 0 | `Com falha: 3` (AC-02, AC-05, Qa0019) → `Aprovado: 7` |
| 00:24:13 / 00:24:34 | driver **M6** (guarda ban-Owner→silence) vermelho / verde | 1 / 0 | `Com falha: 1` (Owner banido 403) → `Aprovado: 5` |
| 00:25:07 / 00:25:26 | driver **M7** (reject não remove) vermelho / verde | 1 / 0 | `Com falha: 2` (AC-02 + máquina §3.3) → `Aprovado: 10` |
| 00:26:14 / 00:26:38 | driver **M8** (policyVersion→1) vermelho / verde | 1 / 0 | `Com falha: 1` (Qa0019 espera versão 2) → `Aprovado: 7` |
| 00:27:10 / 00:27:26 | driver **M9** (teto 1000→2000) vermelho / verde | 1 / 0 | `Com falha: 1` (Qa0019 teto) → `Aprovado: 2` |
| 00:27:57 / 00:28:13 | driver **M10** (demove Owner→Guest) vermelho / verde | 1 / 0 | `Com falha: 1` (Qa0019 409) → `Aprovado: 2` |
| — | driver **M11 tentativa 1** (padrão com aspas) | 1 | `PATTERN-COUNT 0 (need 1) - nothing mutated` — arg boundary |
| 00:29:39 / 00:29:56 | driver **M11 tentativa 2** (ParseRole aceita owner) vermelho / verde | 1 / 0 | `Com falha: 1` (Qa0019 grant 400) → `Aprovado: 2` |
| 00:36:20 | filtro `~MemberModeration` (7 testes combinados) | 0 | `Com falha: 0, Aprovado: 7` |
| 00:38:37 | `sdd-test.ps1 test` (gate pós-restauração, antes do M12) | 0 | `Com falha: 0, Aprovado: 216` |
| 00:41:29 / 00:41:50 | driver **M12** (ação ChangeRole→View) vermelho / verde | 1 / 0 | `Com falha: 2` (os 2 testes de trilha AC-01) → `Aprovado: 7` |
| 00:45:37 | `sdd-test.ps1 test` (**gate final**, AC-06) | 0 | `Com falha: 0, Aprovado: 216, Total: 216` |
| 00:47 | `powershell … qa-check-task-0020.ps1` (1ª execução) | 0 | `SUMMARY total=14 fails=0 RESULT: PASS` |
| 00:47 | `bash … qa-check-task-0020.sh` (1ª execução no WSL) | 0 | `SUMMARY total=14 fails=0 RESULT: PASS` (git/sha256sum/find/powershell.exe funcionando no WSL) |
| 00:48 | self-test do check: limiar `216` → `9999`, depois restaurado | 1 → 0 | quebra: `CHECK GATE-NEWEST-SUMMARY FAIL … RESULT: FAIL` exit 1; restaurado: exit 0, SHA256 idêntico ao PRE (`MATCH=True`) |
| 00:56 | execução final dos dois checks (pós-appends) | 0 / 0 | `SUMMARY total=14 fails=0 RESULT: PASS` nos dois |
| 00:57 | `sdd-state.ps1 check` final | 0 | `check: OK - log entries 18, evidence rows 36` |

## Mutation ledger (S-16 / S-23)

| # | File mutated | Change | PRE = POST SHA256 | Red run | Restore proof |
| --- | --- | --- | --- | --- | --- |
| M1 | `ContainerEndpoints.cs` | papel escrito: `Role = role ?? target.Role` → `Role = target.Role` | `F50555171261EBAC49A26AEF85276D9EA54DEB1813F38B47B2BDD5185E57366F` | `20261109-001947` exit 1 (4 falhas: AC-01, AC-02, AC-05, Qa0019 AC-01) | hash = PRE, mtime 00:19, verde 7/7 |
| M2 | `ContainerEndpoints.cs` | `string.IsNullOrEmpty(justification)` → `justification is null` | idem M1 | `002053` exit 1 (1 falha: AC-02 unban só-espaços) | hash = PRE, verde 5/5 |
| M3 | `ContainerEndpoints.cs` | guarda de leitura `Banned` → `Silenced` | idem M1 | `002144` exit 1 (2 falhas: AC-03 metades 1 e 2) | hash = PRE, verde 5/5 |
| M4 | `IPolicyEvaluator.cs` | `return actor.IsPublisher` → `return true` (SinglePublisher) | `BD83790F627272A5FD8509DA2BFE80EA75CB624E49F213A9C13E6FF99A402A00` | `002227` exit 1 (1 falha: facet DoD AC-04) | hash = PRE, verde 7/7 |
| M5 | `ContainerEndpoints.cs` | `? PolicyAction.Admin` → `? PolicyAction.Moderate` (capacidade) | idem M1 | `002313` exit 1 (3 falhas: AC-02, AC-05, Qa0019 unban) | hash = PRE, verde 7/7 |
| M6 | `ContainerEndpoints.cs` | guarda `Ban && Owner` → `Silence && Owner` | idem M1 | `002413` exit 1 (1 falha: Owner banido 403) | hash = PRE, verde 5/5 |
| M7 | `MembershipRules.cs` | aresta reject: `(true, null)` → `(false, Active)` | `E04CD59C9D73E79ED61D5B56AE4A69757FE11EE838CF3CC77AB233D27C87DB47` | `002507` exit 1 (2 falhas: AC-02 reject + `MembershipRulesTests:81`) | hash = PRE, verde 10/10 |
| M8 | `ContainerEndpoints.cs` | `PolicyVersion` do `ChangeRole/Allow` → `1` (regex) | idem M1 | `002614` exit 1 (1 falha: Qa0019 `:79`) | hash = PRE, verde 7/7 |
| M9 | `ContainerEndpoints.cs` | teto `> MaxJustificationLength` → `> 2000` | idem M1 | `002710` exit 1 (1 falha: Qa0019 `:128`) | hash = PRE, verde 2/2 |
| M10 | `ContainerEndpoints.cs` | `target.Role == Owner` → `== Guest` (demove) | idem M1 | `002757` exit 1 (1 falha: Qa0019 `:114`) | hash = PRE, verde 2/2 |
| M11a | `ContainerEndpoints.cs` | padrão do `ParseRole` **com aspas** | — | recusa `PATTERN-COUNT 0`, **nada mutado** (exit 1) | árvore intacta |
| M11b | `ContainerEndpoints.cs` | `ParseRole` default `_ => null` → `_ => MembershipRole.Member` | idem M1 | `002939` exit 1 (1 falha: Qa0019 `:107`) | hash = PRE, verde 2/2 |
| M12 | `ContainerEndpoints.cs` | ação do registro Allow `ChangeRole` → `View` (regex) | idem M1 | `004129` exit 1 (2 falhas: AC-01 `MemberModeration:60` + Qa0019 `:73`) | hash = PRE, verde 7/7 |

Resíduo final: varredura de `QA-MUTATION` em `backend/` (src + tests, sem bin/obj) → **0**
(nenhuma mutação inseriu marcador; a restauração é por cópia byte-exact); hash final dos 3
arquivos mutados = PRE e o teste da QA = `CB2E3BCB…` (nunca mutado), confirmado pelo
`qa-check-task-0020.ps1`/`.sh` (`RESTORE-SHA256` + `TESTFILE-INTEGRITY`).

## Findings

| # | Severity | Where | What breaks | Evidence |
| --- | --- | --- | --- | --- |
| F1 | INFO (adjudicação) | `specs/001-squid-mvp/contracts/http-api.md:201` (§9.11 "Saída/**remoção** do último Owner → 409") vs `docs/sdd/specs/SPEC-0008-containers-sociais-p03.md:162` ("`Owner` banido → negado") e `ContainerEndpoints.cs:563-567` | Banir o Owner responde **403 `policy_denied`** (SPEC, `authority: SPEC`) enquanto **demover** a linha responde **409 `last_owner`**. Se §9.11 for lido como cobrindo também o banimento ("remoção"), o código deveria ser 409. O dispatch pediu: divergência de leitura sobe como achado, não como falha — os dois decretos são plausíveis e nenhum teste da casa cobre as duas leituras ao mesmo tempo | leitura das duas fontes + M6 (guarda movida → o ban do Owner cai no 409 da demote, provando que os dois caminhos estão distintos no código) |
| F2 | MINOR (adjudicação) | `backend/src/Squid.Api/Containers/ContainerEndpoints.cs:541-547` (Deny do avaliador **vai** para a trilha) vs `:563-578` (recusas de invariant do Owner — 403 do ban, 409 da demote — retornam **sem** `AppendAsync`) e `:549-559` (422/400 da justificativa também sem linha) | Denegas por capacidade são auditadas (`ChangeRole/Deny`), mas a recusa **`403 policy_denied` de banir o Owner** — que o próprio código classifica como decisão de política — não deixa linha `Deny` na trilha; data-model §2.11 diz "`Deny`/`NotFound` também registrados (SC-011)". 400/422 são validação e não precisam de linha, mas o 403 do ban do Owner é uma negação de política sem registro | leitura + AC-02 `:129-135` (o 403 acontece e nenhum teste exige linha); nenhum vermelho — comportamento dos ACs está correto |

Nenhum BLOCKER, nenhum MAJOR. F1 sobe como dúvida de leitura para ODEV (o dispatch já adiantou
que, se §9.11 fosse lido cobrindo ban, seria achado — aqui está); F2 é uma inconsistência de
trilha dentro de SC-011 que não quebra nenhum AC.

## Not covered, and why

- **Suite em Linux/WSL** — verificado nesta sessão: `wsl -e sh -lc` responde `dotnet:ABSENT`
  `pwsh:ABSENT` (`bash:ok`); instalar SDK é gate e não foi aprovado → **unverified**, nunca
  assumido verde. O que roda em Linux nesta sessão é o checker `.sh` (14/14 PASS no WSL).
  Risco: regressão específica de Linux só é vista no CI (ubuntu já rodou a suite via pwsh em
  runs anteriores — memory bank, não re-executado aqui).
- **CSRF na rota `PATCH /members/{actorId}`** — o middleware sessão+CSRF da P-02 é o mesmo de
  toda mutação e o AC-05 só pede `401` sem sessão (coberto, `:253-257`); requisição forjada com
  sessão mas sem CSRF não foi exercitada aqui. Risco: baixo.
- **`PATCH /members/{handle inexistente}` → 404** (`ContainerEndpoints.cs:496-501`) — nenhum
  AC desta task pede esse caminho (o 404 de invisível/alvo ausente está no desenho da rota e o
  padrão `Hidden` é coberto por QA anteriores). Risco: baixo.
- **Corrida de duas escritas simultâneas na membership** (dois `PATCH` no mesmo instante) — só
  estática (retry do store); provocar dois escritores não foi feito. Risco: baixo.
- **Gap pré-existente S-30** (`POST /join` em não-público → 404 para `Invited`,
  `ContainerEndpoints.cs:293,867-876`) — fora do escopo por decisão do dispatch; nenhum cenário
  desta QA o toca (todos os `join` são em contêiner público). Já é AC-07 da TASK-0021.
  Risco: nenhum para esta task.
- **Retenção/limite da trilha de auditoria** — fase dona (QA-0017). Risco: nenhum aqui.

## Platforms

| Platform | State | Why |
| --- | --- | --- |
| Windows (PowerShell 5.1 + .NET SDK 10) | **executado** | 3 gates + 5 filtros + 12 ciclos de mutação + recusa do driver + checker `.ps1` (14/14), todos classificados pelos logs gravados |
| Linux / bash (WSL) | **executado (só o check estático)** | `qa-check-task-0020.sh` roda e fecha igual (14/14); a **suite** em Linux segue **unverified** (sem dotnet/pwsh no WSL nesta sessão) |
| Linux nativo / CI ubuntu | **não re-executado nesta QA** | histórico verde no CI via pwsh (memory bank); sem aprovação para disparar runner externo |
