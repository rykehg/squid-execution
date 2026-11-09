<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: CONCLUIDO
- owner: qa
- lastVerified: 2026-11-08 18:23:57

# QA-0017 — Política versionada (If-Match), trilha de auditoria e operador

- task: TASK-0018
- spec: SPEC-0008 (seções B, C, D + Gaps), data-model §2.11, contracts/http-api.md §4/§7/§9
- runId: R-20261108-01
- qaAttempt: 1/2
- verdict: PASSED

Execução, não leitura: os 7 testes novos do DEV em `Squid.Tests/Containers` rodaram **15×** no
runner do projeto (`sdd-test.ps1` — 2 gates + 13 filtros), entre eles **6 mutações can-fail** —
uma por comportamento (AC-01..AC-05 + o desempate estável da loja) — cada uma restaurada
byte-exact (SHA256 PRE = POST, S-16) com bump de mtime (S-23) e verde logo em seguida. Toda
classificação ambiente vs. produto vem do cabeçalho `exit:` e do resumo **dentro** do log gravado
em `docs/sdd/runs/test-runs/` (S-22), nunca do código de saída do wrapper. Linha de base desta
QA: gate `20261108-175107` **188/188 exit 0** (181 da QA-0016 + 7 novos do DEV); gate final
`20261108-180433` 188/188 exit 0 na árvore restaurada.

## What is proven

| AC | Scenario (executed) | Script | Result |
| --- | --- | --- | --- |
| AC-01 | `A_policy_put_replaces_it_and_the_trail_records_author_before_after_and_new_version` (`PolicyAuditRoutesTests.cs:31`): PUT `If-Match: 1` autenticado (dono) → **200** com `policy.version` **2** (n+1, `:56`) e os campos novos (`posting admins`, `visibility public`, `join open`, `interaction.canComment everyone`); `GET` mostra a política nova na versão 2 (`:64-72`); a linha da trilha `ChangePolicy` (lida pelo operador em `GET /api/admin/audit?action=ChangePolicy`) tem `result Allow`, `policyVersion 2`, `actorId` do autor e `Reason` JSON com `before` (posting members, version 1) e `after` (posting admins, version 2) + `reason` não vazio (`:76-91`). **M4 deixa isto vermelho** (`:56` `Actual: 1`) e **M2 deixa vermelho** (`:76` — o operador não é mais admitido na rota de auditoria) | filtros xunit | PASS |
| AC-02 | `A_stale_if_match_conflicts_with_the_current_version_and_a_missing_one_is_428` (`:95`): `If-Match: 1` reenviado após a política já estar na versão 2 → **409** com `error.code version_conflict` **e** `version: 2` (a vigente) no corpo (`:111-116`); hash do estado: `GET` antes == `GET` depois byte a byte (`:119`); `PUT` **sem** `If-Match` → **428** com `precondition_required` no corpo (`:124-125`) e estado inalterado de novo (`:126`). **M1 deixa isto vermelho** (`:124` — o 428 vira 400) e **M4 deixa vermelho** (`:111` — sem n+1 o conflito deixa de existir) | filtros xunit | PASS |
| AC-03 | `The_trail_carries_allow_deny_and_not_found_of_the_decisions_the_routes_execute` (`:130`): os **três resultados** na mesma trilha, filtrados por recurso — **Allow**: `View` do dono lendo o próprio contêiner (actorId e `policyVersion 1`, `:160-165`); **NotFound**: leitura de estranho ao contêiner não-público, `View/NotFound` com `policyVersion 1` e `Reason` não vazio (`:167-171`); **Deny**: `Member` tentando `PUT /policy` grava `ChangePolicy/Deny` com o actorId do membro e `policyVersion 1` antes do 403 (`:173-178`). Os 7 campos de data-model §2.11 (`At/ActorId/Action/ResourceId/Result/PolicyVersion/Reason`) fecham no round-trip da loja (`PolicyAuditStoreTests.cs:48-54`). **M3 deixa isto vermelho** (`:173` — a linha Deny deixa de ser gravada), **M5 deixa vermelho** (`:151`) e **M6 deixa vermelho** (loja `:39` — o desempate estável vira reverso) | filtros xunit | PASS |
| AC-04 | `The_audit_route_demands_an_operator_and_its_three_filters_work` (`:182`): sem sessão → **401** `not_authenticated` (`:186-188`); sessão de handle fora de `Instance:Operators` → **403** `policy_denied` (`:191-194`); operador nascido na config do host → **200** com itens ordenados por `At` decrescente (`:205-208`); os três filtros do contrato §7 funcionam — `actor` pela id canônica **e** pelo handle devolvem as mesmas linhas (`:211-216`), `action` só as linhas com o valor (`:219-221`), `from` inclusivo no passado e vazio no futuro (`:224-227`); paginação `limit=1` sem repetir a linha entre páginas (`:230-234`). **M2 deixa isto vermelho** (`:205` — o gate passa a negar o próprio operador) | filtros xunit | PASS |
| AC-05 | `Put_policy_without_session_is_401_and_a_member_is_denied_with_the_policy_untouched` (`:238`): `PUT /policy` anônimo → **401** `not_authenticated` (`:248-252`); `Member` ativo (linha de membership escrita direto na store, rota de join é da TASK-0019) → **403** `policy_denied` (`:257-260`) e o `GET` do estado é byte-idêntico antes/depois — política inalterada (`:263`). **M5 deixa isto vermelho** (`:259` — o 403 vira 200) | filtros xunit | PASS |
| AC-06 | gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` → log `docs/sdd/runs/test-runs/20261108-180433-test-gate.log` com `exit: 0` e `Com falha: 0, Aprovado: 188, Total: 188`, lido **dentro** do log (S-22); escopo: `git status --porcelain -uall` capturado no início desta QA (17:41) idêntico ao do fim, com apenas os artefatos desta QA novos permitidos; varredura de mtime em `backend/src` + `backend/tests` desde o fim do DEV (15:59:17) → **só os 3 arquivos mutados por esta QA**, todos dentro dos `touches`; resíduo `QA-MUTATION` em `backend/` = **0** | gate + git/mtime/hash | PASS |

Falhas pré-existentes: **NENHUM** — linha de base 188/188 verde antes de qualquer edição desta QA
e 188/188 verde na árvore restaurada; as 6 rodadas vermelhas foram mutações deliberadas desta QA,
todas revertidas byte-exact.

## Scripts

| Script | Language / runner | What it does |
| --- | --- | --- |
| `backend/tests/Squid.Tests/Containers/PolicyAuditRoutesTests.cs` | xunit (runner do projeto) | 5 testes de rota contra `SquidServiceHost`: AC-01 a AC-05 por HTTP real, host próprio com `Instance:Operators` |
| `backend/tests/Squid.Tests/Containers/PolicyAuditStoreTests.cs` | xunit | 2 testes de loja SQLite: append-only + ordenação com desempate estável, e os três filtros do contrato §7 |
| `docs/sdd/_tools/sdd-test.ps1 test [-TestFilter …]` | pwsh + dotnet | runner da casa: 2 gates e 13 filtros executados nesta QA |
| `docs/sdd/qa/qa-check-task-0018.ps1` | pwsh | 9 checks estáticos: exit/resumo do gate e do filtro mais novos (S-22), SHA256 de restauração dos 3 arquivos mutados (S-16), resíduo `QA-MUTATION` em `backend/`, escopo dos touches, varredura de mtime, rotas estáticas |
| `docs/sdd/qa/qa-check-task-0018.sh` | bash (git bash) | gêmeo Linux do check acima, mesmos 9 check ids, mesmo contrato de exit |

## How to reproduce

Prerequisites: Windows com .NET SDK 10, repositório em `C:\dev\squid-squad-net\squid-execution`
(sem rede depois do restore). Sem servidor, sem banco compartilhado, sem instalação.

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test
# esperado: exit 0 · Com falha 0, Aprovado 188 · log novo em docs/sdd/runs/test-runs/

powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~PolicyAudit"
# esperado: exit 0 · Com falha 0, Aprovado 7

powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0018.ps1
# esperado: exit 0 · SUMMARY total=9 fails=0
```

## Last run

Classificação sempre pelo log gravado (S-22). As 6 mutações são vermelhos **de propósito**; a
primeira tentativa da M1 não mutou nada (o `ReadAllText` do .NET leu o CWD do processo, não o da
locação do PowerShell) e por isso foi verde — repetida com caminho absoluto, vermelhou como
esperado.

| When (log) | Command | Exit (log) | Output (tail) |
| --- | --- | --- | --- |
| 17:51:07 | `sdd-test.ps1 test` (inicial) | 0 | `Com falha: 0, Aprovado: 188, Total: 188` |
| 17:51:50 | `sdd-test.ps1 test -TestFilter "FullyQualifiedName~PolicyAudit"` (baseline) | 0 | `Com falha: 0, Aprovado: 7, Total: 7` |
| 17:54:30 | mesmo filtro (1ª tentativa da M1 — mutação não aplicada, caminho relativo) | 0 | `Com falha: 0, Aprovado: 7` — sem mutação, não vale como vermelho |
| 17:55:12 | mesmo filtro **sob M1** (428 → 400) | 1 | `Com falha: 1, Aprovado: 6` — `…is_428` `PolicyAuditRoutesTests.cs:124` |
| 17:56:36 | mesmo filtro **sob M2** (gate do operador nega o próprio operador) | 1 | `Com falha: 3, Aprovado: 4` — `:76`, `:154`, `:205` |
| 17:56:54 | mesmo filtro (pós-M2) | 0 | `Com falha: 0, Aprovado: 7` |
| 17:58:40 | mesmo filtro **sob M3** (linha `Deny` deixou de ser gravada) | 1 | `Com falha: 1, Aprovado: 6` — `:173` |
| 17:58:58 | mesmo filtro (pós-M3) | 0 | `Com falha: 0, Aprovado: 7` |
| 17:59:37 | mesmo filtro **sob M4** (versão da política não incrementa) | 1 | `Com falha: 2, Aprovado: 5` — `:56` e `:111` |
| 17:59:55 | mesmo filtro (pós-M4) | 0 | `Com falha: 0, Aprovado: 7` |
| 18:00:47 | mesmo filtro **sob M5** (403 do Member vira 200) | 1 | `Com falha: 2, Aprovado: 5` — `:259` e `:151` |
| 18:01:05 | mesmo filtro (pós-M5) | 0 | `Com falha: 0, Aprovado: 7` |
| 18:02:18 | mesmo filtro **sob M6** (desempate `ThenByDescending` → `ThenBy`) | 1 | `Com falha: 1, Aprovado: 6` — loja `PolicyAuditStoreTests.cs:39` |
| 18:02:36 | mesmo filtro (pós-M6) | 0 | `Com falha: 0, Aprovado: 7` |
| 18:04:33 | `sdd-test.ps1 test` (**gate final**) | 0 | `Com falha: 0, Aprovado: 188, Total: 188` |
| 18:14 | `powershell … docs/sdd/qa/qa-check-task-0018.ps1` | 0 | `SUMMARY total=9 fails=0` `RESULT: PASS` |
| 18:15 | `bash docs/sdd/qa/qa-check-task-0018.sh` (1ª execução) | 1 | `CHECK MTIME-SWEEP FAIL … = 105 … = 102` — bug próprio do script: data `2026-01-08` no `find` em vez de `2026-11-08` |
| 18:16 | mesmo `.sh` após o conserto | 0 | `SUMMARY total=9 fails=0` `RESULT: PASS` — mesmo resultado do `.ps1` |
| 18:17 | self-test do check: limiar do gate `188` → `9999`, depois restaurado | 1 → 0 | quebra: `fails=1` `RESULT: FAIL` exit 1; restaurado: `fails=0` exit 0 e arquivo byte-identico |
| 18:18 | `wsl -e sh -lc` probe + `bash docs/sdd/_tools/sdd-test.sh test` | 0 / 1 | `dotnet:ABSENT` `pwsh:ABSENT`; `sdd-test: no *.sln found within depth 3` (a solução é `.slnx`, wrapper `.sh` pré-existente TASK-0009) |
| 18:23 | `qa-check-task-0018.ps1` + `qa-check-task-0018.sh` (**execução final**, após os appends) | 0 / 0 | os dois `SUMMARY total=9 fails=0` `RESULT: PASS`; `sdd-state check` → `OK - log entries 16, evidence rows 28` (naquele momento); `QA-MUTATION` em `backend/` = 0; `git status` = baseline 27 + 3 artefatos QA |

## Mutation ledger (S-16 / S-23)

| # | File mutated | Change | PRE = POST SHA256 | Red run | Restore proof |
| --- | --- | --- | --- | --- | --- |
| M1 | `backend/src/Squid.Api/Containers/ContainerEndpoints.cs` | `StatusCodes.Status428PreconditionRequired` → `StatusCodes.Status400BadRequest` | `D0704FD83FF00EABD74B22F47F2CE7E5ECFCD006B26DEDAE85FA27031F5C14D1` | `20261108-175512-test-filter.log` exit 1 (1 falha: AC-02 `:124`) | hash = PRE, mtime 17:55, verde 7/7 em seguida |
| M2 | `backend/src/Squid.Api/Admin/AdminEndpoints.cs` | `IsOperator`: `return true` → `return false` no match do handle | `754C1B69FCEC797185A4440052D3D884797E85BEF2509B90D2DD025DB4806B3B` | `20261108-175636-test-filter.log` exit 1 (3 falhas: AC-01 `:76`, AC-03 `:154`, AC-04 `:205`) | hash = PRE, mtime 17:56, verde 7/7 em seguida |
| M3 | `backend/src/Squid.Api/Containers/ContainerEndpoints.cs` | bloco `AppendAsync(… PolicyAudit.Deny …)` do `PUT /policy` removido | `D0704FD83FF00EABD74B22F47F2CE7E5ECFCD006B26DEDAE85FA27031F5C14D1` | `20261108-175840-test-filter.log` exit 1 (1 falha: AC-03 `:173`) | hash = PRE, mtime 17:58, verde 7/7 em seguida |
| M4 | `backend/src/Squid.Api/Containers/ContainerEndpoints.cs` | `before.Version + 1` → `before.Version` na nova política | `D0704FD83FF00EABD74B22F47F2CE7E5ECFCD006B26DEDAE85FA27031F5C14D1` | `20261108-175937-test-filter.log` exit 1 (2 falhas: AC-01 `:56`, AC-02 `:111`) | hash = PRE, mtime 17:59, verde 7/7 em seguida |
| M5 | `backend/src/Squid.Api/Containers/ContainerEndpoints.cs` | `StatusCodes.Status403Forbidden` do `policy_denied` do `PUT /policy` → `StatusCodes.Status200OK` | `D0704FD83FF00EABD74B22F47F2CE7E5ECFCD006B26DEDAE85FA27031F5C14D1` | `20261108-180047-test-filter.log` exit 1 (2 falhas: AC-05 `:259`, AC-03 `:151`) | hash = PRE, mtime 18:00, verde 7/7 em seguida |
| M6 | `backend/src/Squid.Service/SqlitePolicyAuditStore.cs` | `.ThenByDescending(match => match.line)` → `.ThenBy(...)` (desempate estável) | `CE485D798383E5FE79D73CBBAF1847CB06BF4FD430DBF71CA09B2BB509C26BF7` | `20261108-180218-test-filter.log` exit 1 (1 falha: loja `:39`) | hash = PRE, mtime 18:02, verde 7/7 em seguida |

Resíduo final: varredura de `QA-MUTATION` em `backend/` (src + tests, sem bin/obj) → **0**; a
árvore tem só menções em prosa dos documentos de QA/task antigos (QA-0015, QA-0016, TASK-0014,
TASK-0016, TASK-0017, TASK-0018) e nenhuma em arquivo de produto; nenhum arquivo mutado ficou
alterado (hash final = PRE nos 3 arquivos, verificado também por `qa-check-task-0018.ps1`).

## Findings

| # | Severity | Where | What breaks | Evidence |
| --- | --- | --- | --- | --- |
| — | — | — | Nenhum achado novo desta task | — |

Nenhum BLOCKER, nenhum MAJOR, nenhum MINOR novo. Dois itens conhecidos ficam registrados, nenhum
deles bloqueia:

- **Pré-existente conhecida (S-28, fora do escopo)** — `backend/src/Squid.Api/Containers/ContainerEndpoints.cs:425`
  (`Origin` fixo em `https://`): a origem do `ResourceId`/`ActorId` da trilha segue o padrão já
  apontado na QA-0016 e adjudicado — a correção é AC-07 da TASK-0019. Nenhum AC desta task falha
  por isso (os testes constroem a esperança com a mesma regra). Não é desta task, não bloqueia.
- **Unverified declarado pelo DEV (aceito)** — o ramo defensivo de `IsOperator` que aceita um id
  de ator na configuração (`AdminEndpoints.cs:89`, `candidate.EndsWith("/users/" + handle)`) não é
  exercitado: a fixture configura um handle simples e o caminho exigido pelo AC-04/GAP-14 é o do
  handle, que está provado. Ramo não exercitado = unverified, não bloqueia.

## Not covered, and why

- **Suite em Linux/WSL** — verificado nesta sessão: WSL2 existe mas `dotnet` e `pwsh` estão
  `ABSENT` (`wsl -e sh -lc …`); instalar SDK/ferramenta é gate e não foi aprovado →
  **unverified**, nunca assumido verde. O gêmeo `docs/sdd/_tools/sdd-test.sh` também não roda
  neste repo (procura `*.sln`, a solução é `*.slnx` — `sdd-test.sh:20`, pré-existente TASK-0009);
  o gate é o `.ps1`, que roda no CI de ubuntu via pwsh (fato do memory bank, não re-executado
  aqui). Risco: regressão específica de Linux só é vista no CI.
- **Ramo de id de ator em `Instance:Operators`** — `AdminEndpoints.cs:89` nunca avaliado em teste
  (ver Findings). Risco: baixo — configuração com id de ator completo é cenário não exigido pelo
  AC-04, que manda o handle.
- **CSRF do `PUT /policy`** — o padrão sessão+CSRF da P-02 é de TASK-0017; os ACs desta task
  pedem 401 sem sessão e 403 por papel, ambos provados. Risco: baixo, o middleware de sessão é o
  mesmo de todas as mutações.
- **`If-Match` malformado e `If-Match: *` em `PUT /policy`** — o caminho existe
  (`ContainerEndpoints.cs:262-266`, `:451-466`) mas nenhum AC desta task o exige; exercitado só
  indiretamente. Risco: baixo (400 de validação, sem escrita).
- **Retenção de 6 meses da trilha** (invariante data-model §2.11) — nenhum AC desta task a cobra e
  não há job de retenção; fase dona futura. Risco: nenhum para o escopo desta task.
- **Volume da trilha em um único documento** — `SqlitePolicyAuditStore.cs:41` documenta o
  `shortcut:` (o append reescreve o documento inteiro; `IGrainStorage` não tem scan). Suficiente
  para o volume desta fase; o upgrade é para quando o documento crescer, e a interface não muda.
- **Escritas realmente simultâneas na trilha** — a corrida de ETag é coberta pela recusa
  `InconsistentStateException` com 3 tentativas (`:36-60`) só estaticamente; provocar dois
  escritores ao mesmo tempo não foi feito. Risco baixo: falhar alto (IOException) é o comportamento
  de uma trilha que não pode perder linha.

## Platforms

| Platform | State | Why |
| --- | --- | --- |
| Windows (PowerShell 5.1 + .NET SDK 10) | **executed** | 2 gates + 13 filtros acima, todos classificados pelos logs gravados; check `.ps1` executado |
| Linux / bash (git bash) | **executed (só o check estático)** | `qa-check-task-0018.sh` roda e fecha igual; a **suite** em Linux continua **unverified** (sem dotnet/pwsh na máquina) |
| Linux / WSL2 (suite) | **unverified** | `dotnet` e `pwsh` ausentes no WSL desta máquina; instalar é gate (não aprovado) |
