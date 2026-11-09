<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: CONCLUIDO
- owner: qa
- lastVerified: 2026-11-08 13:07:10

# QA-0016 — Núcleo do contêiner: agregado, presets, criar/ler/editar

- task: TASK-0017
- spec: SPEC-0008 (seção A + Rules), data-model §2.2 (os presets do data-model prevalecem sobre o doc 05 §2.3)
- runId: R-20261108-01
- qaAttempt: 1/2
- verdict: PASSED

Execução, não leitura: os 13 testes novos do DEV em `Squid.Tests/Containers` + 1 teste novo desta QA
rodaram 12× no runner do projeto (`sdd-test.ps1` — 3 gates e 9 filtros), entre eles **4 mutações
can-fail** — uma por comportamento — cada uma restaurada byte-exact (SHA256 PRE = POST, S-16) com
bump de mtime (S-23) e verde logo em seguida. Toda classificação ambiente vs. produto vem do cabeçalho
`exit:` e do resumo **dentro** do log gravado em `docs/sdd/runs/test-runs/` (S-22), nunca do código de
saída do wrapper: nas 5 rodadas vermelhas o console devolveu exit 1 e os próprios logs dizem `exit: 1`
com `Com falha N` — vermelho real de produto. Linha de base conhecida verde: gate `20261108-120801`
(167/167) antes desta QA; gate inicial desta QA `20261108-123532` 180/180.

## What is proven

| AC | Scenario (executed) | Script | Result |
| --- | --- | --- | --- |
| AC-01 | `A_group_is_created_with_the_members_only_preset_and_needs_session_and_csrf` (`ContainerRoutesTests.cs:25`) + domínio `Group_is_born_members_only_invite_unlisted_at_policy_version_one` (`ContainerTests.cs:17`): POST autenticado → **201**, ETag `"1"`, `id` = `https://{authority}/c/{slug}`, `listed` false, `memberCount` 1, `policy.version` 1, `visibility` membersOnly, `join` invite, `posting` members e `interaction` canComment/canReact/canShare/canAttach **todos** members (corpo, como o AC pede); sem sessão → 401 e sessão sem CSRF → 403. **A mutação M1 deixa isto vermelho** (rota `:42` `Actual: True` para listed; domínio `Actual: MembersOnly` para o preset trocado) | filtros xunit | PASS |
| AC-02 | `A_community_is_created_with_the_public_preset_in_the_same_file_as_the_group` (`:69`) + `Community_is_born_public_open_listed_at_policy_version_one` (`ContainerTests.cs:42`): mesmo arquivo do AC-01, → **201**, `listed` true, `visibility` public, `join` open, `posting` members, `version` 1; e GET **anônimo** do contêiner recém-criado → 200 (público é público). **M1 deixa vermelho** (`Actual: MembersOnly`) | filtros xunit | PASS |
| AC-03 | `The_creator_is_the_active_owner_and_a_duplicated_slug_creates_nothing` (`:97`): `GET /members` devolve 1 item com `role` owner, `state` active e `actorId` = `https://{authority}/users/{handle}`; slug duplicado **do mesmo dono** → **400** com `validation_failed` no corpo **e** a contagem de linhas de `containers` e `container-members` idêntica antes/depois (nada criado, GAP-16); o contêiner original segue legível com exatamente 1 membro. **M2 deixa vermelho** (`:110` `Actual: "admin"`) | filtros xunit | PASS |
| AC-04 | `An_unseen_container_answers_the_same_404_as_one_that_does_not_exist` (`:140`): dono lê → 200 com `visibility` membersOnly e `version` 1; leitor sem membership → **404**, slug inexistente → **404**, e os dois corpos são **byte-idênticos** (`Assert.Equal` entre eles), com o anônimo caindo no mesmo corpo e `not_found` presente (FR-018, SC-005). **M2 deixa vermelho** (`:160` `Actual: OK`) | filtros xunit | PASS |
| AC-05 | store `A_stale_version_write_is_refused_and_the_state_stays_byte_identical` (`ContainerStoreTests.cs:33` — SHA-256 do agregado antes/depois, versão velha → `null` com hash igual) + rota `Patch_edits_only_the_editable_fields_and_a_stale_version_conflicts_untouched` (`:176` — 200 e ETag `"2"` só na versão de estado, `policy.version` continua 1, If-Match velho → **409 version_conflict** com ETag atual `"2"` e GET antes == depois byte a byte, If-Match novo → 200 ETag `"3"`) + **teste novo desta QA** `Qa0016ContainerPatchScopeTests.A_patch_that_also_carries_slug_kind_and_policy_moves_only_the_three_writable_fields` (o "somente" do AC: o corpo também manda `slug`, `kind` e `policy` e nada além de name/summary/listed se move, confirmado por releitura). **M3 deixa vermelho** (store `:49` `Actual: ContainerState { … Name = Ninguem vê isto … }`, rota `:209` `Actual: OK`) e **M4 deixa vermelho** (QA `:58` `Actual: "slug-roubado"`) | filtros xunit | PASS |
| AC-06 | gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` → log `docs/sdd/runs/test-runs/20261108-130113-test-gate.log` com `exit: 0` e `Aprovado! … Com falha: 0, Aprovado: 181, Total: 181` (167 da linha de base + 13 do DEV + 1 desta QA), lido **dentro** do log (S-22); escopo: `git status --porcelain -uall` + varredura de mtime desde o dispatch do DEV (11:21:00) → **nenhum arquivo de produto fora dos `touches`**; as entradas fora são docs de fluxo do ODEV com mtime **anterior** ao dispatch (`plan.md` 11:20:29, `SPEC-0008` 11:09:06, `TASK-0018..0022` 11:16–11:17) + o append de Log/Evidence da própria task pela ferramenta (12:21:30); resíduo `QA-MUTATION` desta sessão = **0** | gate + git/mtime | PASS |

Falhas pré-existentes: **NENHUM** — linha de base 167/167 verde antes de qualquer edição desta QA e
181/181 verde na árvore restaurada; as 5 rodadas vermelhas foram mutações deliberadas desta QA,
todas revertidas.

## Scripts

| Script | Language / runner | What it does |
| --- | --- | --- |
| `backend/tests/Squid.Tests/Containers/ContainerTests.cs` | xunit (runner do projeto) | 4 testes de domínio: presets AC-01/AC-02, regra do slug, kind fora do domínio |
| `backend/tests/Squid.Tests/Containers/ContainerStoreTests.cs` | xunit | 4 testes de store: hash antes/depois do AC-05, slug duplicado, convergência incondicional, membership do dono |
| `backend/tests/Squid.Tests/Containers/ContainerRoutesTests.cs` | xunit | 5 testes de rota: AC-01 a AC-05 por HTTP real contra `SquidServiceHost` |
| `backend/tests/Squid.Tests/Containers/Qa0016ContainerPatchScopeTests.cs` | xunit | **QA-0016**: fecha a palavra "somente" do AC-05 (1 teste novo, escrito e executado aqui) |
| `docs/sdd/_tools/sdd-test.ps1 test [-TestFilter …]` | pwsh + dotnet | runner da casa: 3 gates e 9 filtros executados nesta QA |

## How to reproduce

Prerequisites: Windows com .NET SDK 10, repositório em `C:\dev\squid-squad-net\squid-execution`
(sem rede depois do restore). Sem servidor, sem banco compartilhado, sem instalação.

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test
# esperado: exit 0 · Aprovado 181, Com falha 0 · log novo em docs/sdd/runs/test-runs/

powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~Squid.Tests.Containers"
# esperado: exit 0 · Aprovado 14, Com falha 0
```

## Last run

Classificação sempre pelo log gravado (S-22). As 4 mutações são vermelhos **de propósito**; o 1º run
do teste da QA ficou vermelho por bug próprio do teste, não do produto.

| When (log) | Command | Exit (log) | Output (tail) |
| --- | --- | --- | --- |
| 12:35:32 | `sdd-test.ps1 test` (inicial) | 0 | `Aprovado! … Com falha: 0, Aprovado: 180, Total: 180` |
| 12:36:10 | `sdd-test.ps1 test -TestFilter "FullyQualifiedName~Squid.Tests.Containers"` | 0 | `Aprovado: 13, Total: 13` |
| 12:38:49 | mesmo filtro **sob M1** | 1 | `Com falha: 4, Aprovado: 9` — AC-01 rota `:42` `Actual: True`, domínio `Actual: MembersOnly`, AC-02 rota e domínio |
| 12:39:26 | `Get-FileHash SHA256` + bump de mtime (restauração M1) | 0 | `RESTORE_OK sha256=4DE4ED58…653A8E` = PRE |
| 12:39:51 | mesmo filtro (pós-M1) | 0 | `Aprovado: 13, Total: 13` |
| 12:40:35 | mesmo filtro **sob M2** | 1 | `Com falha: 2, Aprovado: 11` — AC-03 `:110` `Actual: "admin"`, AC-04 `:160` `Actual: OK` |
| 12:41:17 | `Get-FileHash SHA256` + bump de mtime (restauração M2) | 0 | `RESTORE_OK sha256=E8F30250…6AD846F` = PRE |
| 12:42:12 | mesmo filtro **sob M3** | 1 | `Com falha: 2, Aprovado: 11` — store `:49` `Actual: ContainerState { … Ninguem vê isto … }`, rota `:209` `Actual: OK` (+ o `CS0162` da própria mutação) |
| 12:42:55 | `Get-FileHash SHA256` dos 3 arquivos + bump de mtime + varredura `QA-MUTATION` | 0 | 3/3 = PRE; resíduos desta sessão = 0 |
| 12:47:14 | `sdd-test.ps1 test` (pós-restauração) | 0 | `Aprovado: 180, Total: 180` |
| 12:54:46 | mesmo filtro **1ª execução do teste da QA** | 1 | `Com falha: 1` — `Qa0016…:28` `Actual: UnprocessableEntity` (bug próprio do teste: handle com hífen, `ActorHandle.Rule` = `[a-z0-9_]{3,30}`) |
| 12:56:06 | mesmo filtro (teste corrigido) | 0 | `Aprovado: 14, Total: 14` |
| 12:57:30 | mesmo filtro **sob M4** | 1 | `Com falha: 1` — `Qa0016…:58` `Actual: "slug-roubado"` |
| 12:59:27 | mesmo filtro (pós-M4) | 0 | `Aprovado: 14, Total: 14` |
| 13:01:13 | `sdd-test.ps1 test` (**gate final**) | 0 | `Aprovado! … Com falha: 0, Aprovado: 181, Total: 181` |

## Mutation ledger (S-16 / S-23)

| # | File mutated | Change | PRE = POST SHA256 | Red run | Restore proof |
| --- | --- | --- | --- | --- | --- |
| M1 | `backend/src/Squid.Domain/Containers/Container.cs` | preset de `Community` trocado por `Group` + `Listed = true` | `4DE4ED58C87F2148606A3170D5E0F75B6AF9ADBA4DD0D62A3B1C109F1C653A8E` | `20261108-123849-test-filter.log` exit 1 (4 falhas: AC-01 e AC-02) | hash = PRE, mtime 12:39:26, verde 13/13 em seguida |
| M2 | `backend/src/Squid.Api/Containers/ContainerEndpoints.cs` | `VisibleAsync` passa a aceitar `MembersOnly` + membership gravada como `Admin` | `E8F30250BC0D9F2AEA6E38D64DFE41646D3ADCBEAA5E4E7DCE400DB6D6AD846F` | `20261108-124035-test-filter.log` exit 1 (2 falhas: AC-03 e AC-04) | hash = PRE, mtime 12:41:17 |
| M3 | `backend/src/Squid.Service/SqliteContainerStore.cs` | guarda de concorrência desligada (`if (false)` no lugar de `expectedVersion.Value != Version(read.ETag)`) | `E8FFE12D30C0E3179BB35DA1AC6BF990808813847EFA95061D935B90ED84DF0F` | `20261108-124212-test-filter.log` exit 1 (2 falhas: AC-05 store + rota) | hash = PRE, mtime 12:42:55, gate 180/180 em seguida |
| M4 | `backend/src/Squid.Api/Containers/ContainerEndpoints.cs` | `PatchContainerRequest` ganha `Slug` e o `PATCH` passa a gravá-lo | `E8F30250BC0D9F2AEA6E38D64DFE41646D3ADCBEAA5E4E7DCE400DB6D6AD846F` | `20261108-125730-test-filter.log` exit 1 (1 falha: o teste desta QA) | hash = PRE, mtime 12:58:00, verde 14/14 em seguida |

Resíduo final: varredura de `QA-MUTATION` em toda a árvore → 3 ocorrências **pré-existentes**
(`docs/sdd/qa/QA-0015-kit-recuperacao.md:36`, `docs/sdd/tasks/TASK-0014-registro-login-sessao.md:109`,
`docs/sdd/tasks/TASK-0016-kit-recuperacao.md:113`, mtimes 2026-11-07/08 01:15–01:19, anteriores a
esta sessão) e **0 desta sessão**; nenhum arquivo mutado ficou alterado.

## Findings

| # | Severity | Where | What breaks | Evidence |
| --- | --- | --- | --- | --- |
| 1 | MINOR | `backend/src/Squid.Api/Containers/ContainerEndpoints.cs:210` | A origem dos contêineres é fixada em `https://` enquanto o resto da API deriva `scheme + Host` (`DiscoveryEndpoints.cs:114`): sobre o host de teste `http://127.0.0.1` o **mesmo ator** fica com dois URIs — `actorId` da membership `https://…/users/{h}` (`ContainerRoutesTests.cs:112`) e id do perfil `http://…/users/{h}` (`WebFingerProfileTests.cs:223`), ambos verdes no mesmo gate. Nenhum AC falha (AC-01/AC-03 pedem `https://{host}` literal, e em produção atrás de TLS os dois coincidem); o risco aparece em TASK-0019+/P-06, quando `actorId` de membership for comparado com o id do perfil. É desta task (arquivo novo), não pré-existente | gate `20261108-130113` + os dois asserts citados |

Nenhum BLOCKER, nenhum MAJOR.

## Not covered, and why

- **Suite em Linux/WSL** — o WSL2 existe nesta máquina mas sem `dotnet` e sem `pwsh`
  (`wsl -e sh -lc '…'` → `dotnet: ABSENT`, `pwsh: ABSENT`); instalar SDK ou ferramenta é gate e não
  foi aprovado → **unverified**, nunca assumido verde. `sdd-test.sh` também não roda neste repo
  (procura `*.sln`, a solução é `*.slnx` — pré-existente, TASK-0009). Risco: regressão específica de
  Linux só será vista no CI (`ubuntu-26.04`), que roda o mesmo `.ps1`.
- **Slug duplicado entre donos diferentes** — fora dos ACs: a store chaveia o row pelo slug isolado
  (`SqliteContainerStore.cs:38`), então o segundo dono também recebe 400. Mais estrito que
  data-model §2.2 ("único entre contêineres do dono"), porém coerente com o id global `/c/{slug}`.
  Risco: nenhum para os ACs; se algum dia quiser slug por dono, muda o contrato.
- **Rotas das próximas tasks** — `PUT /policy`, `join/invite/leave`, `search`, `DELETE`,
  transfer/successor (TASK-0018..0022): não testadas aqui, não são desta task.
- **`If-Match` malformado no PATCH** — a rota responde 400 `validation_failed`
  (`ContainerEndpoints.cs:134-138`), exercitado só de passagem; não é AC desta task.
- **Duas escritas realmente simultâneas** — a guarda de versão é provada com versão defasada e o
  caminho de retry incondicional só pelo teste de convergência sequencial; corrida real entre dois
  processos não foi provocada. Risco baixo: a recusa (em vez de retry) já é o comportamento exigido.

## Platforms

| Platform | State | Why |
| --- | --- | --- |
| Windows (PowerShell 5.1 + .NET SDK 10) | **executed** | 2 gates + 13 filtros acima, todos classificados pelos logs |
| Linux / WSL2 | **unverified** | WSL2 presente, `dotnet` e `pwsh` ausentes; instalar é gate (não aprovado) |
