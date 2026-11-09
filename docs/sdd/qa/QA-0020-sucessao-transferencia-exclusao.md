<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: CONCLUIDO
- owner: qa
- lastVerified: 2026-11-09 02:53:00

# QA-0020 — Sucessão, transferência, exclusão e override do operador

- task: TASK-0021
- spec: SPEC-0008 (seção G + Rules + Gaps + emenda §A + seção E), specs/001-squid-mvp/data-model.md §2.2/§2.11/§3.3, specs/001-squid-mvp/contracts/http-api.md §4/§7/§9.11/§9.12
- runId: R-20261108-01
- qaAttempt: 1/2
- verdict: PASSED

Execução, não leitura: o gate de baseline rodou **228/228** no começo (`20261109-021141`, `exit: 0`
lido **dentro** do log — S-22), os **7 testes novos** desta QA rodaram **7/7** (`20261109-023308`
e re-run final `20261109-024806`), as pinas da QA-0018 rodaram **8/8** (`20261109-023410`), a área
`Squid.Tests.Containers` **61/61** (`20261109-023540`) e o gate final rodou **235/235**
(`20261109-025153`, `exit: 0` pelo log) = 228 do DEV + os 7 desta QA. Os 8 ACs tiveram prova de
can-fail: **13 ciclos de mutação** (M1..M12, sem numerar os dois sub-ciclos do AC-08), cada um com
SHA256 **PRE** (S-16), vermelho classificado **pelo log gravado**, restauração byte-exact com bump
de mtime (S-23), hash = PRE e verde logo em seguida — 19 testes vermelhos de propósito no total —
mais o **self-test do checker** para AC-06. Nenhum arquivo fora dos `touches`: `git status` do fim
= baseline das 02:11 + exatamente os **4 artefatos** desta QA (checker `TOUCHES-NO-OUTSIDE`),
resíduo `QA-MUTATION` = 0, hashes = PRE. Duas execuções intermediárias vermelhas foram do próprio
teste da QA em construção (compilação e uma conta 409), registradas como evidência, não como
veredito.

## What is proven

| AC | Scenario (executed) | Script | Result |
| --- | --- | --- | --- |
| AC-01 | `Qa0021SectionGEdgeTests.Claim_opens_only_for_the_named_successor_and_the_owners_own_writes_restart_the_clock` (`Qa0021SuccessionEntryAndTrailEdgeTests.cs:30`): claim **sem sucessor nomeado** → 403 `policy_denied` + linha `Successor/Deny`, sem linha de membership (`:41-47`); `PUT /successor` grava o `SuccessorActorId` canônico (`:50-53`); claim de **não-sucessor** → 403 sem row (`:55-61`); dia 0 → 403 §9.12 (`:63-67`); **PATCH do Owner no dia 40 renova o relógio** → claim no dia 91 continua 403 (`:69-77`, `:69-70`); **PUT /policy do Owner no dia 121 renova de novo** → claim no dia 210 (89 dias depois) continua 403 (`:79-87`); dia 212 → **200**: herdeiro `Owner`+`Active`, antigo `Admin`, exatamente 1 Owner ativo, `OwnerActorId` = linha do herdeiro (`:89-101`); trilha com `Successor/Deny` de **dois** autores + `Successor/Allow` do herdeiro com **`PolicyVersion 2`** (a versão vigente depois do PUT) e `Reason` não vazia (`:103-115`). Relógio controlado (`FakeClock`), nenhum teste dorme. **M1** (janela → 0) vermelha em AC-01+AC-05, **M2** (PATCH não renova) vermelha em AC-01, **M3** (PUT policy não renova) vermelha em AC-01, **M12** (linha-alvo ausente não é criada) vermelha em AC-01+AC-03+AC-05 | filtros xunit + 4 mutações | PASS |
| AC-02 | `The_operator_gate_writes_no_line_and_the_override_moves_the_ownership_with_its_justification` (`:110`): contagem de linhas capturada (`:128`); sem sessão → **401** (`:131-134`); não-Operador com corpo inteiramente válido → **403 `policy_denied`**, membership intacta, **contagem de linhas idêntica ao baseline** — o gate do `/api/admin` não grava linha (precedente da rota de auditoria) (`:136-143`); Operador com justificativa só-espaços e sem justificativa → **422 ×2** com a contagem intacta (`:145-153`); bump da política para **v2** (`:155-159`); override → **200**, novo `Owner`+`Active`, antigo `Admin`, `OwnerActorId` trocado, 1 Owner ativo, e **exatamente +1 linha**: `Transfer/Allow` com autor = operador, **`PolicyVersion 2`**, justificativa + `from` + `to` no `Reason` (`:161-184`). **M4** (`IsOperator` → false) vermelha em AC-02+AC-05; **M5** (linha-alvo → `Silenced`) vermelha em AC-02+AC-05 | filtros xunit + 2 mutações | PASS |
| AC-03 | `The_owner_transfers_onto_a_target_without_a_row_and_the_refusals_stay_audited` (`:186`): anônimo → **401** (`:198-201`); não-Owner → **403** + `Transfer/Deny` com `PolicyVersion 1` e autor = tentador (`:203-210`); corpo **sem identidade** → **400** `validation_failed` e **nenhuma** linha `Transfer/Allow` (`:212-217`); o alvo **sem linha de membership** recebe a linha criada `Owner`+`Active` com o `ActorId` canônico, o antigo Owner vira `Admin` e sobra **exatamente 1 Owner ativo** (`:219-241`); `Transfer/Allow` com autor, `PolicyVersion 1` e `from`/`to` no `Reason` (`:243-248`); a **segunda grafia** da identidade (GAP-13, `actorId` no corpo) também transfere (`:250-260`); o ex-Owner (agora `Admin`) não transfere de volta → **403** + segunda linha `Transfer/Deny` (`:262-270`). **M12** vermelha em AC-03 (a linha-alvo ausente deixa de ser criada) | filtros xunit + 1 mutação | PASS |
| AC-04 | `Delete_walks_session_visibility_capability_confirm_in_order_and_keeps_the_row_for_the_grace` (`:270`): anônimo → **401** (`:281-284`); **membro sem `?confirm` → 403 + `Delete/Deny`** (capacidade antes da confirmação — a ordem da pipeline em execução) (`:286-296`); confirm errado e confirm ausente → **400** `validation_failed`, **zero** linhas `Delete/Allow`, `DeletedAt` nulo e GET ainda **200** (`:298-314`); confirm exato → **200** `{ slug, deletedAt = relógio, recoverableUntil = relógio + 30 }` (`:316-323`); store: `DeletedAt` preenchido, `IsDeleted`, **`IsSearchable = false`** (a prova de "sai do search" — a rota é da TASK-0022), `RecoverableUntil(30) = DeletedAt + 30`, 1 linha `Delete/Allow` (`:325-338`); GET responde **404 byte-idêntico** ao slug ausente (`:340-347`); `PUT /successor`, `POST /claim` e `POST /transfer` pós-delete respondem **404** (o soft delete esconde de escritor também) (`:349-358`); **dia 29 da carência** a linha continua no store com o mesmo `DeletedAt` (`:360-366`). **M6** (comparação do confirm) vermelha em AC-04+AC-05; **M7** (ação do Allow `Delete`→`View`) vermelha em AC-04 | filtros xunit + 2 mutações | PASS |
| AC-05 | `No_sequence_of_this_phase_leaves_the_container_without_one_active_owner` (`:360`) com a propriedade `NeverOrphanedAsync` (`:435`): **8 passos** — ban do Owner → 403, demote → 409, leave → 409, claim dentro da janela → 403, transfer, **ban do ex-Owner (linha `Admin`) → 200 sem orfandade**, delete por `Banned` → 403, claim pós-relógio → 200, **override do operador → 200**, soft delete → 200 — e **após cada passo** `Assert.Single(Owner+Active)` + `OwnerActorId` igual àquela linha. Sequência diferente da do teste do DEV (que é a prova do DEV). **M1, M4, M5, M6, M12** deixam este teste vermelho | filtros xunit + 5 mutações (reaproveitadas) | PASS |
| AC-06 | gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` → **`20261109-025153-test-gate.log` com `exit: 0` e `Com falha: 0, Aprovado: 235, Total: 235`**, lido **dentro** do log (S-22); baseline do início **`20261109-021141` = 228/228 `exit: 0`**; escopo: `git status --porcelain -uall` capturado às **02:11** (antes da QA escrever) idêntico no fim com **só os 4 artefatos** novos; varredura de mtime em `backend/src` + `backend/tests` desde 02:06 → **exatamente 4 arquivos**, todos dentro dos `touches` (3 mutados/restaurados + o teste da QA); resíduo `QA-MUTATION` em `backend/` = **0**; hash final = **PRE** nos 3 mutados + `TESTFILE-INTEGRITY` no teste da QA; **self-test do checker**: limiar `235` → `9999` de propósito → `CHECK GATE-NEWEST-SUMMARY FAIL` **exit 1**, restaurado → exit 0 com SHA256 idêntico ao PRE | gate + git/mtime/hash + checkers 14/14 + self-test | PASS |
| AC-07 (correção S-30) | `Qa0021EntryAndTrailEdgeTests.Entry_on_a_members_only_container_walks_invite_open_and_request_to_the_end` (`:533`): convite **sem** convite em `MembersOnly` → **404 byte-idêntico** ao slug ausente, corpo igual byte a byte (`:542-551`); o **convidado** faz `POST /join` → **200** `active`/`member` com `invitedBy` do dono e linha `Join/Allow` (`:553-571`) — é o caso do DoD "um grupo invite só entra por convite"; `Open` em não-público → **201** `active` (`:573-585`); `Request` em não-público → **201** `requested`, o leitor pendente ainda le **404**, e o `approve` completa o fluxo até `Active` (`:587-607`); voltando a `Invite`, o pino da QA-0018 continua **404 byte-idêntico** com outro atrasado (`:609-621`); `Banned` em não-público também → **404 byte-idêntico** (`:623-638`); as linhas ficam exatamente nos estados criados (`:640-646`). As pinas da QA-0018 rodam à parte: filtro `Qa0018 + MembershipRoutes` → **8/8** (`20261109-023410`). **M8** (`Invited` sai da lista de entrada admitida) vermelha em AC-07 — é o bug S-30 re-imposto; **M9** (`MembersOnly` vira visível na entrada) vermelha em AC-07 — o pino byte-idêntico quebra | filtros xunit + 2 mutações | PASS |
| AC-08 (correção F2) | `The_owner_invariant_refusals_write_deny_under_the_version_in_force_and_validations_write_nothing` (`:632`): política bumpada **2×** para **versão 3** (`:641-643`) e um segundo autor promovido a `Admin` (`:645-649`); **422** (justificativa só-espaços), **400** (`role: owner`) e **400** (corpo sem mudança) → `ChangeRole = 1` e `Deny = 0` — validação de entrada **não grava linha** (`:664-674`); **ban do Owner pelo Admin → 403 `policy_denied`** com exatamente 1 linha `ChangeRole/Deny`, autor = Admin, **`PolicyVersion 3`**, `Reason` não vazio e a linha do Owner intacta (`:676-691`); **demote da linha Owner → 409 `last_owner`** com a 2ª linha `ChangeRole/Deny` e **as duas com `PolicyVersion 3`** (`:693-705`); um 422 depois delas continua sem linha (`ChangeRole = 3`, `Deny = 2`) (`:707-715`). **M10a** (guarda do ban → `Unsilence`) vermelha (403 vira 409); **M10b** (guarda da demote → `Guest`) vermelha (409 vira 200); **M11** (422 só-`null`) vermelha (a justificativa de espaços passa e a linha proibida aparece) | filtros xunit + 3 mutações | PASS |

Falhas pré-existentes: **NENHUM** — baseline 228/228 verde antes de qualquer edição desta QA e
235/235 verde na árvore restaurada; as 19 rodadas vermelhas foram mutações deliberadas (13
ciclos), todas revertidas byte-exact. As duas vermelhas fora dos ciclos (`022858` = o teste da QA
ainda não compilava; `023028` = 1 teste, conta 409 do operador já registrado no host compartilhado)
foram corrigidas no próprio teste da QA, nunca em código de produto.

### DEV decisions verified (not disputed — executed)

| Decision | Evidence |
| --- | --- |
| claim/transfer/override criam a linha-alvo `Owner`+`Active` quando ela não existe | `SuccessionRules.cs:43-53` (`target is null` → linha nova) + AC-03 `:219-241`; vermelho **M12** |
| o move também força `State = Active` na linha existente (o override restaura um `Banned` como Owner ativo) | `SuccessionRules.cs:53`; AC-05 passo g `:414-420` (banido volta `Owner`+`Active`); vermelho **M5** |
| pipeline do `DELETE`: sessão → visibilidade → capacidade (**403 + `Deny`**) → confirm exato (**400, sem linha**) → soft delete `{ slug, deletedAt, recoverableUntil }` | `ContainerEndpoints.cs:717-760` (`OwnerCommandAsync :507-528` antes do confirm em `:743`); AC-04 `:286-323`; vermelho **M6** e **M7** |
| o gate do `/api/admin` em si não grava linha na trilha (precedente da rota de auditoria da TASK-0018) | `AdminEndpoints.cs:97-101` (gate antes de qualquer `AppendAsync`); AC-02 `:136-143` (contagem = baseline); vermelho **M4** |
| `OwnerLastActiveAt` renova **só** com escrita do Owner: `PATCH` do contêiner e `PUT /policy`, e a cada troca de Owner | `ContainerEndpoints.cs:251` (PATCH) e `:1131-1133` (PUT policy), `Ownership.cs:36` (troca); AC-01 `:69-87` e `:105`; vermelho **M2** e **M3** |
| casos de membership (moderação) ficam de fora do relógio — ideia conhecida estacionada pelo ODEV | não exercitado por decisão do dispatch; ver "Not covered" |
| corpos de successor/transfer aceitam `handle` \| `actorId` (precedência do convite, GAP-13); outra grafia → 400 | AC-03 `:250-260` (`actorId` → 200) e `:212-217` (corpo sem identidade → 400) |
| rota `search` ainda não existe (TASK-0022) — "sai do search" do AC-04 provado por `IsSearchable` no domínio + `GET 404` | `Container.cs:114`; AC-04 `:327` e `:340-347` |
| `/api/admin` sem justificativa → 422 (§9.12), não-Operador → 403 `policy_denied` (§D), sem sessão → 401 | AC-02 `:131-153` |
| `TimeProvider` + defaults `Containers:SuccessorInactivityDays=90` / `DeleteGraceDays=30` lidos da config | `SquidServiceHost.cs:45,108,118` (seam `Build(args, configure)`); AC-01 usa os 90 do default (`:63-101`) e AC-04 os 30 (`:316-323`); a prova de configuração (2/3 dias) é do DEV (`AdminOverrideAndConfiguredTimersTests.cs:94`) e rodou no gate |

## Scripts

| Script | Language / runner | What it does |
| --- | --- | --- |
| `backend/tests/Squid.Tests/Containers/Qa0021SuccessionEntryAndTrailEdgeTests.cs` | xunit (runner da casa) | os 7 testes desta QA: 5 na classe `Qa0021SectionGEdgeTests` (AC-01..AC-05) e 2 na `Qa0021EntryAndTrailEdgeTests` (AC-07, AC-08) |
| `docs/sdd/_tools/sdd-test.ps1 test [-TestFilter …]` | pwsh + dotnet | runner da casa: 2 gates (baseline e final), 5 filtros verdes e as 26 rodadas vermelhas/verdes dos ciclos |
| `docs/sdd/qa/qa-mutate-task-0019.ps1` (genérico, reaproveitado desde a QA-0018) | pwsh | driver de mutação can-fail: PRE hash → muta **1** padrão (recusa se contagem ≠ 1) → vermelho pelo log → restaura byte-exact + mtime (S-23) → hash == PRE → verde |
| `docs/sdd/qa/qa-check-task-0021.ps1` | pwsh | 14 checks: exit/resumo do gate de baseline e do gate/filtro mais novos (S-22), SHA256 PRE dos 3 mutados (S-16), integridade do teste da QA, resíduo `QA-MUTATION`, escopo dos `touches` (git desde 02:11), varredura de mtime desde 02:06, rotas/regras estáticas, contagem de `[Fact]`, `sdd-state check` |
| `docs/sdd/qa/qa-check-task-0021.sh` | bash (WSL) | gêmeo Linux do check acima, mesmos 14 check ids, mesmo contrato de exit |

## How to reproduce

Prerequisites: Windows com .NET SDK 10, repositório em `C:\dev\squid-squad-net\squid-execution`
(sem rede depois do restore). Sem servidor, sem banco compartilhado, sem instalação.

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test
# esperado: exit 0 · Com falha 0, Aprovado 235 · log novo em docs/sdd/runs/test-runs/

powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~Qa0021"
# esperado: exit 0 · Com falha 0, Aprovado 7 (5 da classe SectionG + 2 da EntryAndTrail)

powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~Qa0018|FullyQualifiedName~MembershipRoutes"
# esperado: exit 0 · Com falha 0, Aprovado 8 (as pinas da QA-0018)

powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0021.ps1
# esperado: exit 0 · SUMMARY total=14 fails=0
wsl -e bash -c 'cd /mnt/c/dev/squid-squad-net/squid-execution && bash docs/sdd/qa/qa-check-task-0021.sh'
# esperado: exit 0 · SUMMARY total=14 fails=0
```

Mutações (ciclo completo, precisa terminar com `RESULT: PROVEN` e exit 0 — ele restaura sozinho):

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0019.ps1 -File backend/src/Squid.Api/Containers/ContainerEndpoints.cs -Old 'if (membership?.State is MembershipState.Active or MembershipState.Silenced or MembershipState.Invited)' -New 'if (membership?.State is MembershipState.Active or MembershipState.Silenced)' -Filter 'FullyQualifiedName~Qa0021EntryAndTrailEdgeTests'
# esperado: RESULT: PROVEN · vermelho 1 falha (o convidado deixa de entrar = S-30) · hash = PRE · verde 2/2
```

## Last run

Classificação sempre pelo log gravado (S-22). As 26 rodadas vermelhas dos ciclos são mutações
**de propósito**.

| When (log) | Command | Exit (log) | Output (tail) |
| --- | --- | --- | --- |
| 02:11:41 | `sdd-test.ps1 test` (baseline desta QA) | 0 | `Com falha: 0, Aprovado: 228, Total: 228` |
| 02:28:58 | filtro `~Qa0021` (1ª tentativa do teste da QA) | 1 | `error CS0103 SetJoinAsync` — o teste da QA não compilava; corrigido, não é falha de produto |
| 02:30:28 | filtro `~Qa0021` (2ª tentativa) | 1 | `Com falha: 1, Aprovado: 6` — conta do operador 409 no host compartilhado; teste da QA corrigido (register-or-login) |
| 02:33:08 | filtro `~Qa0021` | 0 | `Com falha: 0, Aprovado: 7, Total: 7` |
| 02:34:10 | filtro `~Qa0018\|~MembershipRoutes` (pinas) | 0 | `Com falha: 0, Aprovado: 8` |
| 02:35:40 | filtro `~Squid.Tests.Containers` (área) | 0 | `Com falha: 0, Aprovado: 61` |
| 02:37:16 / 02:37:38 | driver **M1** (janela → 0) vermelho / verde | 1 / 0 | `Com falha: 2` (AC-01, AC-05) → `Aprovado: 5` |
| 02:38:00 / 02:38:20 | driver **M2** (PATCH não renova) | 1 / 0 | `Com falha: 1` (AC-01) → `Aprovado: 5` |
| 02:38:45 / 02:39:08 | driver **M3** (PUT policy não renova) | 1 / 0 | `Com falha: 1` (AC-01) → `Aprovado: 5` |
| 02:39:29 / 02:39:49 | driver **M5** (linha-alvo → Silenced) | 1 / 0 | `Com falha: 2` (AC-02, AC-05) → `Aprovado: 3` |
| 02:41:19 / 02:41:39 | driver **M4** (`IsOperator` → false) | 1 / 0 | `Com falha: 2` (AC-02, AC-05) → `Aprovado: 3` |
| 02:42:01 / 02:42:22 | driver **M6** (confirm comparado com `slug+slug`) | 1 / 0 | `Com falha: 2` (AC-04, AC-05) → `Aprovado: 3` |
| 02:42:42 / 02:43:04 | driver **M7** (ação do Allow `Delete`→`View`) | 1 / 0 | `Com falha: 1` (AC-04) → `Aprovado: 4` |
| 02:43:24 / 02:43:44 | driver **M12** (linha-alvo ausente não criada) | 1 / 0 | `Com falha: 3` (AC-03, AC-01, AC-05) → `Aprovado: 2` |
| 02:44:22 / 02:44:40 | driver **M8** (`Invited` fora da entrada) | 1 / 0 | `Com falha: 1` (AC-07) → `Aprovado: 1` |
| 02:44:55 / 02:45:13 | driver **M9** (`MembersOnly` admitido) | 1 / 0 | `Com falha: 1` (AC-07) → `Aprovado: 1` |
| 02:45:31 / 02:45:49 | driver **M10a** (guarda do ban → `Unsilence`) | 1 / 0 | `Com falha: 1` (AC-08) → `Aprovado: 1` |
| 02:46:07 / 02:46:26 | driver **M10b** (guarda da demote → `Guest`) | 1 / 0 | `Com falha: 1` (AC-08) → `Aprovado: 1` |
| 02:46:44 / 02:47:02 | driver **M11** (422 só-`null`) | 1 / 0 | `Com falha: 1` (AC-08) → `Aprovado: 1` |
| 02:48:06 | filtro `~Qa0021` (pós-restauração) | 0 | `Com falha: 0, Aprovado: 7` |
| 02:51:53 | `sdd-test.ps1 test` (**gate final**, AC-06) | 0 | `Com falha: 0, Aprovado: 235, Total: 235` |
| 02:47 | `wsl -e sh -lc` (probe de plataforma) | 0 | `dotnet:ABSENT` `pwsh:ABSENT` `bash:ok` — a **suite** em Linux segue **unverified** |
| ~02:59 | `powershell … qa-check-task-0021.ps1` (1ª execução) | 1 | `CHECK MTIME-SWEEP FAIL` (o checker pedia 5 arquivos e eram 4: ele listava um arquivo que nenhum ciclo mutou) → `SUMMARY total=14 fails=1 RESULT: FAIL` — defeito do checker, corrigido no checker |
| ~03:01 | `powershell … qa-check-task-0021.ps1` (lista corrigida: 3 mutados + teste = 4) | 0 | `SUMMARY total=14 fails=0 RESULT: PASS` |
| ~03:01 | `bash … qa-check-task-0021.sh` (1ª execução no WSL) | 0 | `SUMMARY total=14 fails=0 RESULT: PASS` (git/sha256sum/find/powershell.exe funcionando no WSL) |
| ~03:02 | self-test do check: limiar `235` → `9999`, depois restaurado | 1 → 0 | quebra: `CHECK GATE-NEWEST-SUMMARY FAIL … RESULT: FAIL` exit 1; restaurado: exit 0, SHA256 idêntico ao PRE (`6B1A6080…`) |
| ~03:04 | execução final dos dois checks (pós-appends) | 0 / 0 | `SUMMARY total=14 fails=0 RESULT: PASS` nos dois (Windows e WSL) |
| ~03:04 | `sdd-state.ps1 check` | 0 | `check: OK - log entries 23, evidence rows 36` |
| ~03:06 | `powershell … qa-check-task-0021.ps1` + `bash … qa-check-task-0021.sh` (estado final do repositório) | 0 / 0 | `SUMMARY total=14 fails=0 RESULT: PASS` nos dois; `sdd-state.ps1 check` final → `check: OK - log entries 24, evidence rows 39` |

## Mutation ledger (S-16 / S-23)

| # | File mutated | Change | PRE = POST SHA256 | Red run (log) | Restore proof |
| --- | --- | --- | --- | --- | --- |
| M1 | `SuccessionRules.cs` | janela do claim: `now >= ownerLastActiveAt.AddDays(inactivityDays)` → `now >= ownerLastActiveAt` | `F7D0DF3268A02421B94078F9736B4FC3D30AC97BE6F69AA322F3D8C300379410` | `20261109-023716` exit 1 — 2 falhas: AC-01 + AC-05 | hash = PRE, verde 5/5 |
| M2 | `ContainerEndpoints.cs` | relógio no PATCH: `ownerWriting ? GetUtcNow() : …` → `ownerWriting && false ? …` | `B5EFD5A0A3869A39FDCE27F9007D9CAA0C1248561BF6A9C131DEC2F5AA007C1E` | `023800` exit 1 — 1 falha: AC-01 | hash = PRE, verde 5/5 |
| M3 | `ContainerEndpoints.cs` | relógio no PUT /policy: `OwnerActorId == ActorId(...)` → `OwnerActorId is null` | idem M2 | `023845` exit 1 — 1 falha: AC-01 | hash = PRE, verde 5/5 |
| M5 | `SuccessionRules.cs` | linha-alvo existente: `State = MembershipState.Active` → `Silenced` | idem M1 | `023929` exit 1 — 2 falhas: AC-02 + AC-05 | hash = PRE, verde 5/5 |
| M4 | `AdminEndpoints.cs` | gate: `string.Equals(candidate, handle, ...) return true` → `return false` | `57A991CAFBAF642A1229B0449E5CEC76EE9312C11F04203096C620D55A8F8C74` | `024119` exit 1 — 2 falhas: AC-02 + AC-05 | hash = PRE, verde 5/5 |
| M6 | `ContainerEndpoints.cs` | confirm: comparado com `slug` → `slug + slug` (nunca casa) | idem M2 | `024201` exit 1 — 2 falhas: AC-04 + AC-05 | hash = PRE, verde 5/5 |
| M7 | `ContainerEndpoints.cs` | ação do Allow do delete: `PolicyAudit.Delete` → `PolicyAudit.View` | idem M2 | `024242` exit 1 — 1 falha: AC-04 | hash = PRE, verde 5/5 |
| M12 | `SuccessionRules.cs` | `rows.Add(target is null` → `rows.Add(false && target is null` (linha ausente nunca criada) | idem M1 | `024324` exit 1 — 3 falhas: AC-03, AC-01, AC-05 | hash = PRE, verde 5/5 |
| M8 | `ContainerEndpoints.cs` | `CanEnterAsync`: `… or MembershipState.Invited` removido (o S-30 re-imposto) | idem M2 | `024422` exit 1 — 1 falha: AC-07 | hash = PRE, verde 2/2 |
| M9 | `ContainerEndpoints.cs` | `CanEnterAsync`: `Visibility == Public` → `is Public or MembersOnly` (o pino 404 quebra) | idem M2 | `024455` exit 1 — 1 falha: AC-07 | hash = PRE, verde 2/2 |
| M10a | `ContainerEndpoints.cs` | guarda do ban do Owner: `MembershipChange.Ban` → `MembershipChange.Unsilence` | idem M2 | `024531` exit 1 — 1 falha: AC-08 (403 vira 409) | hash = PRE, verde 2/2 |
| M10b | `ContainerEndpoints.cs` | guarda da demote: `target.Role == Owner` → `== Guest` (a demote passa) | idem M2 | `024607` exit 1 — 1 falha: AC-08 (409 vira 200) | hash = PRE, verde 2/2 |
| M11 | `ContainerEndpoints.cs` | 422 da justificativa: `string.IsNullOrEmpty(justification)` → `justification is null` | idem M2 | `024644` exit 1 — 1 falha: AC-08 (a de-spaços passa e grava linha) | hash = PRE, verde 2/2 |

Todos os 13 ciclos terminaram `RESULT: PROVEN` com exit 0: `PATTERN-COUNT: 1` (o driver recusa
padrão ausente ou duplicado), `HASH-MATCH: True` e verde depois do restore. Nenhum padrão com
aspas duplas foi usado (a armadilha da M11a da QA-0019), nenhuma recusa de driver nesta rodada.
Resíduo final: varredura de `QA-MUTATION` em `backend/` (src + tests, sem bin/obj) → **0**; hash
final dos 3 arquivos mutados = PRE e o teste da QA nunca foi alvo (`TESTFILE-INTEGRITY`), confirmado
pelo `qa-check-task-0021.ps1`/`.sh`.

## Findings

| # | Severity | Where | What breaks | Evidence |
| --- | --- | --- | --- | --- |
| F1 | INFO (decisão registrada, sem quebra de AC) | `backend/src/Squid.Domain/Containers/SuccessionRules.cs:53` (`target with { Role = Owner, State = Active }`) | A troca de propriedade **força `Active` na linha-alvo existente**: um `Banned` (ou `Silenced`) nomeado/transferido volta ao contêiner como `Owner` **`Active`**. É o que a AC-03 pede ("novo `Owner` `Active`") e é o que mantém a invariante (Owner nunca pode ser `Banned`), mas o efeito colateral — transferência como desbanimento — não está escrito em SPEC-0008 nem no data-model. Nenhum AC quebra; registrado para ODEV | AC-05 passo g (`:414-420`, banido volta `Owner`+`Active`); vermelho **M5** prova que o `Active` é este código |
| F2 | INFO (estacionado pelo ODEV, confirmado) | `backend/src/Squid.Api/Containers/ContainerEndpoints.cs:920-931` (`PATCH /members` só escreve a membership) vs `:251`/`:1131-1133` (relógio só no `PATCH` do contêiner e no `PUT /policy`) | Uma **escrita de moderação do Owner** (ban/papel pela rota de membership) **não renova** o relógio de inatividade — caso que o ODEV estacionou como ideia conhecida antes do dispatch. Não exercitado de propósito, nenhum AC desta task depende disso | leitura do caminho de escrita + dispatch ("DEV parked the membership-moderation case as a known idea, do not block on it") |

Nenhum BLOCKER, nenhum MAJOR — nenhum dos 8 ACs falhou e não há contradição código × contrato.

## Not covered, and why

- **Suite em Linux/WSL** — probe desta sessão: `wsl -e sh -lc` responde `dotnet:ABSENT`
  `pwsh:ABSENT` (`bash:ok`); instalar SDK é gate e não foi aprovado → **unverified**, nunca
  assumido verde. O que roda em Linux nesta sessão é o checker `.sh` (14/14 PASS no WSL).
  Risco: regressão específica de Linux só é vista no CI (ubuntu já rodou a suite via pwsh em
  runs anteriores — memory bank, não re-executado aqui).
- **Pós-carência (após os 30 dias)** — não existe job de purge nesta fase (fora de escopo do
  dispatch): o que se prova é a **janela** (`recoverableUntil` e a linha viva no dia 29). Risco:
  nenhum para esta task; o purge é fase própria.
- **Rota `GET /api/containers/search`** — não existe ainda (TASK-0022 a possui); "sai do search"
  do AC-04 está provado por `IsSearchable = false` no domínio + `GET 404`, como o dispatch mandou.
  Risco: nenhum aqui.
- **Renovação do relógio por escrita de moderação do Owner** — estacionada (F2), não exercitada.
  Risco: baixo; se a ideia vira requirement, o teste é uma linha no AC-01.
- **`PUT /successor` por não-Owner → 403 + linha `Deny`** — o caminho é o `OwnerCommandAsync`
  compartilhado (já exercitado por AC-03/AC-04), mas a rota do sucessor com autor não-Owner não
  recebeu cenário próprio. Risco: baixo.
- **Corrida de duas escritas simultâneas** (dois `claim`/`transfer` no mesmo instante) — só
  estática (as duas escritas sem transação já declaradas como `shortcut` em `Ownership.cs:28`).
  Risco: baixo, conhecido e documentado no código.
- **CSRF nas rotas novas** — o middleware sessão+CSRF da P-02 é o mesmo de toda mutação; o AC só
  pede `401` sem sessão (coberto em AC-02/AC-03/AC-04). Risco: baixo.

## Platforms

| Platform | State | Why |
| --- | --- | --- |
| Windows (PowerShell 5.1 + .NET SDK 10) | **executado** | 2 gates (228 e 235), 5 filtros verdes, 13 ciclos de mutação (26 rodadas vermelhas/verdes) e o checker `.ps1` (14/14), todos classificados pelos logs gravados |
| Linux / bash (WSL) | **executado (só o check estático)** | `qa-check-task-0021.sh` roda e fecha igual (14/14, `git`/`sha256sum`/`find`/`powershell.exe` via interop); a **suite** em Linux segue **unverified** (sem dotnet/pwsh no WSL nesta sessão) |
| Linux nativo / CI ubuntu | **não re-executado nesta QA** | histórico verde no CI via pwsh (memory bank); sem aprovação para disparar runner externo |
