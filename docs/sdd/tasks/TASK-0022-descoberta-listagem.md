<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0022 — Descoberta de comunidades listadas (FR-016) e facet de fecho

```yaml
runId: R-20261108-01
taskId: TASK-0022
status: DONE
agent: odev
phase: P-03
sequence: 2
createdAt: 2026-11-08 11:09:06
updatedAt: 2026-11-09 04:55:35
parent: SPEC-0008
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Api/Containers/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/Containers/**"]
blocks: []
blockedBy: ["TASK-0021"]
inputArtifacts: ["docs/sdd/specs/SPEC-0008-containers-sociais-p03.md", "specs/001-squid-mvp/spec.md", "specs/001-squid-mvp/contracts/http-api.md"]
outputArtifacts: ["backend/src/Squid.Api/Containers/**", "backend/src/Squid.Service/**"]
interviewRounds: 0/6
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
nextAction: DONE - QA-0021 PASSED (5/5 ACs, gate 244/244, 10 mutacoes can-fail); ULTIMA entrega da P-03 - consolidacao da fase
evidence: []
memoryBankFlags: []
```

## Intent

Fecha FR-016/R-10 (a lacuna de descoberta sinalizada no fecho da P-02) e entrega a última
peça funcional da fase: `GET /api/containers/search?q=` listando só contêineres `Listed=true`.
Com ela o DoD do Sprint 2 fecha e a fase vai para consolidação.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `GET /api/containers/search?q=` devolve somente contêineres `Listed=true` e não
  excluídos; um grupo com `listed: false` **nunca** aparece — nem com o `q` igual ao nome
  exato, nem autenticado — teste (FR-016).
- AC-02: matching = substring case-insensitive em `name` e `slug` (sem FTS, GAP-15); `q`
  ausente ou vazio → `400 validation_failed` — teste.
- AC-03: a rota é pública (sem sessão devolve os listados) e nenhum contêiner não listado ou
  excluído vaza em resposta (SC-005) — teste.
- AC-04: facet de fecho do DoD do Sprint 2 em fluxo HTTP completo, reutilizando as rotas das
  tasks anteriores: (a) grupo `invite` só entra por convite — 100 tentativas, 0 sucessos;
  (b) `singlePublisher` nega `Member` e permite o publisher nomeado; (c) sucessão + override:
  contêiner com Owner inativo restaura Owner ativo e nenhum caso deixa órfão — teste.
- AC-05: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`
  exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Api/Containers/**` (rota `search`)
- `backend/src/Squid.Service/**` (query na store)
- `backend/tests/Squid.Tests/Containers/**` (testes + facet de fecho do DoD)

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-15 | no | substring case-insensitive em `name`/`slug`; sem FTS, ranking ou paginação por relevância (I-07 pós-MVP) |
| — | no | facet de fecho roda na suíte desta task; se uma faceta falhar por defeito de task anterior, é achado de QA com `S-NN` no backlog, não motivo para reabrir a fase |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-08 11:16:36 | odev | task created from SPEC-0008 (entrega 6 de 6 e ultima da P-03; blockedBy TASK-0021; FR-016 + facet de fecho do DoD)
- 2026-11-09 03:11:10 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; blockedBy TASK-0021 resolve (DONE com QA-0020 PASSED, gate 235/235); baseline do gate e 235/235; entrega final da fase - com ela fecham FR-016 e a facet de fecho do DoD do Sprint 2
- 2026-11-09 03:27:46 | dev | feasibility 1/2: baseline gate 235/235 exit 0 antes de editar; SPEC-0008 H + Gaps, contrato 4/9 e FR-016/FR-018 lidos; touches confirmados (Containers, Service, tests/Containers); D-01 envelope da busca e D-02 q em branco fechados pela SPEC-0008 + casa; sem duvida bloqueante
- 2026-11-09 03:44:24 | dev | implementacao: rota GET /api/containers/search (publica, q em branco 400, itens paginados) sobre IContainerStore.SearchAsync com filtro IsSearchable + substring case-insensitive em name/slug; memberCount da linha so para quem ve o contenedor (SC-005); construtor do SqliteContainerStore ganhou a connection string (8 sites de teste ajustados, todos em touches); testes novos: store half, ContainerSearchRoutesTests (AC-01..03) e Sprint2ClosingFacetTests (AC-04 a/b/c); build exit 0
- 2026-11-09 03:49:29 | dev | gate final 242/242 exit 0 (235 do baseline + 7 testes novos); escrita desde o dispatch inteira dentro dos touches; AC-01..AC-05 verdes - DEV entrega a ultima entrega da P-03 ao ODEV
- 2026-11-09 03:52:39 | odev | ODEV verificou antes de transicionar: gate final 20261109-034706-test-gate.log lido no arquivo (exit: 0, Aprovado 242, Com falha 0 - S-22); residuos QA-MUTATION em backend = 0; git status = so toques (ContainerEndpoints, ContainerView, IContainerStore, SqliteContainerStore, SquidServiceHost, tests/Containers) + docs de fluxo; check exit 0 (log 5, evidencias 5 gravadas pelo proprio DEV); step result DONE aceito - D-01 e D-02 fechadas na feasibility, nenhum duvida
- 2026-11-09 03:52:40 | odev | adjudicacoes do ODEV: notDone (NFR de indice Listed/Slug impossivel sem migration - proibida) aceito com o shortcut declarado no codigo e vira S-32 no backlog junto com o S-29; parked idea do slug search vira S-33 (reservar ou aceitar a colisao, task de saneamento); flags: ARQUITETURA (SQL direto na store do Squid.Service vs responsabilidade gravada do memory bank) fica para a consolidacao - o codigo ja vinha assim desde a P-01 (SqliteAuthStore), e PADRAO_NOVO (shortcut com limite declarado) tambem para o fecho; risco apontado pelo DEV (memberCount null em linha que o leitor nao ve) entra na pauta da QA-0021
- 2026-11-09 03:52:40 | odev | transicao IN_DEVELOPMENT -> AWAITING_QA; TASK-0022 = entrega 6 de 6, ULTIMA da P-03 - com QA PASSED a fase fecha
- 2026-11-09 04:44:21 | qa | dispatch attempt 1/2: gate de baseline 242/242 exit 0 lido dentro do log 20261109-040457 (S-22); AC-01..AC-05 mapeados no arquivo, SPEC-0008 secao H + Gaps + emenda A + secao G, FR-016/FR-018/SC-005, contrato 4 e DoD do Sprint 2; touches confirmados (Containers, Service, tests/Containers)
- 2026-11-09 04:44:21 | qa | cenarios executados: 7 testes novos do DEV 7/7 (20261109-040703), 2 testes da QA 2/2 (20261109-041024: 100 tentativas da SC-005 na busca + sondagem do S-33), area Squid.Tests.Containers 70/70 (20261109-042454), gate final 244/244 exit 0 (20261109-042748) lido dentro do log
- 2026-11-09 04:44:21 | qa | can-fail: 10 ciclos de mutacao M1..M9 (M6 em M6a/M6b) - 10 vermelhos de proposito, todos dentro dos touches, cada um com SHA256 PRE, restauracao byte-exact + bump de mtime (S-23) e verde em seguida; residuo QA-MUTATION = 0; hash final = PRE nos 3 arquivos mutados
- 2026-11-09 04:44:21 | qa | decisoes do DEV verificadas por execucao: envelope { items }, q so-brancos 400, memberCount null na linha fora de vista (regra de metadado da SC-005 - leitura identica a da SPEC-0008 Rules), S-32 (shortcut declarado, filtro antes da paginao) e S-33 (slug search sombreado, executado); WSL sem dotnet/pwsh -> suite Linux unverified
- 2026-11-09 04:47:45 | qa | veredito PASSED (QA-0021, attempt 1/2): 5/5 ACs com cenario executado e prova de falha; achados INFO F1 (policy na linha da busca, para adjudicacao na consolidacao), F2 (S-33 verificado), F3 (S-32 verificado); BLOCKER nenhum, falhas pre-existentes NENHUM; docs/sdd/qa/QA-0021-descoberta-listagem.md + qa-check-task-0022.ps1/.sh (17 checks) escritos e verdes nos dois sistemas - com isso o ultimo entregavel da P-03 esta pronto para a consolidacao
- 2026-11-09 04:55:36 | odev | ODEV verificou antes de transicionar: QA-0021-descoberta-listagem.md existe; gate final 20261109-042748-test-gate.log lido no arquivo (exit: 0, Aprovado 244, Com falha 0 - S-22); residuos QA-MUTATION em backend = 0; check exit 0 (log 13, evidencias 32); veredito aceito - 5/5 ACs, 10 mutacoes can-fail vermelhas e restauradas byte-exact (inclui M6a/M6b da facet de convite, M7 do singlePublisher e M8 never-orphan), checkers ps1 e sh 17/17 nos dois sistemas
- 2026-11-09 04:55:37 | odev | F1 da QA adjudicado (escada: especificacao): a linha da busca devolve ContainerView com a policy inteira (inclui publisherIds) porque a tabela de IO da SPEC-0008 manda ContainerView em GET/search/POST e o SC-005 da secao H so exige rebaixar memberCount - leitura da QA identica a da spec; ator e URI publica (descoberta P-02), cont nao-listado nunca aparece (provado por fuzz 100 tentativas), entao sem vazamento; F2 (S-33) e F3 (S-32) ja estavam registrados - nenhum AC reaberto
- 2026-11-09 04:55:37 | odev | transicao AWAITING_QA -> DONE; TASK-0022 = entrega 6 de 6, ULTIMA da P-03 (QA-0021 PASSED) - TODAS AS 6 ENTREGAS DONE COM QA PASSED, DoD do Sprint 2 provado em cadeia; inicia a consolidacao de fecho da fase

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-09 03:48:23 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261109-031605-test-gate.log |
| 2026-11-09 03:48:24 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 1 | backend/src/Squid.Tests build - CS7036 nos 8 sites do construtor, corrigido |
| 2026-11-09 03:48:24 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 0 | backend/Squid.slnx - 0 erros 0 avisos |
| 2026-11-09 03:48:25 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261109-034706-test-gate.log |
| 2026-11-09 03:48:25 | dev | Get-ChildItem -Recurse (arquivos escritos desde o dispatch) | 0 | NENHUM |
| 2026-11-09 03:52:41 | odev | Select-String do gate final + varredura QA-MUTATION em backend + git status + sdd-state check | 0 | docs/sdd/runs/test-runs/20261109-034706-test-gate.log exit: 0 / Aprovado 242 / Com falha 0; residuos 0; toques fora NENHUM; check OK log 5 evidencias 5 |
| 2026-11-09 04:45:20 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (baseline da QA) | 0 | docs/sdd/runs/test-runs/20261109-040457-test-gate.log (exit: 0, Com falha 0, Aprovado 242 - baseline lido dentro do log) |
| 2026-11-09 04:45:20 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter (FullyQualifiedName~ContainerSearchRoutesTests)+(FullyQualifiedName~Sprint2ClosingFacetTests)+(FullyQualifiedName~The_search_returns_only_listed_and_living_rows) | 0 | docs/sdd/runs/test-runs/20261109-040703-test-filter.log (exit: 0, Com falha 0, Aprovado 7 - os 7 testes novos do DEV; uniao vstest dos 3 termos) |
| 2026-11-09 04:45:21 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Qa0022 | 0 | docs/sdd/runs/test-runs/20261109-041024-test-filter.log (exit: 0, Com falha 0, Aprovado 2 - os 2 testes da QA) |
| 2026-11-09 04:45:21 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0019.ps1 -File backend/src/Squid.Service/SqliteContainerStore.cs -Old 'container.IsSearchable' -New 'true' -Filter FullyQualifiedName~ContainerSearchRoutesTests | 0 | docs/sdd/runs/test-runs/20261109-041324-test-filter.log (RED exit: 1, Com falha 2 = AC-01:37 e AC-03:104) e 20261109-041338-test-filter.log (GREEN exit: 0, Aprovado 3); HASH-MATCH True, PRE 7A7BA9C2 - M1 |
| 2026-11-09 04:45:22 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0019.ps1 -File backend/src/Squid.Service/SqliteContainerStore.cs -Old 'container.Name.Contains(query, StringComparison.OrdinalIgnoreCase)' -New 'container.Name.Contains(query, StringComparison.Ordinal)' -Filter FullyQualifiedName~ContainerSearchRoutesTests | 0 | docs/sdd/runs/test-runs/20261109-041410-test-filter.log (RED exit: 1, Com falha 1 = AC-02:61) e 20261109-041424-test-filter.log (GREEN exit: 0, Aprovado 3); HASH-MATCH True, PRE 7A7BA9C2 - M2 |
| 2026-11-09 04:45:22 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0019.ps1 -File backend/src/Squid.Api/Containers/ContainerEndpoints.cs -Old 'if (query.Length == 0)' -New 'if (query.Length < 0)' -Filter FullyQualifiedName~ContainerSearchRoutesTests | 0 | docs/sdd/runs/test-runs/20261109-041456-test-filter.log (RED exit: 1, Com falha 1 = AC-02:76 Expected BadRequest Actual InternalServerError) e 20261109-041511-test-filter.log (GREEN exit: 0, Aprovado 3); HASH-MATCH True, PRE DAEE4CC7 - M3 |
| 2026-11-09 04:45:23 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0019.ps1 -File backend/src/Squid.Api/Containers/ContainerEndpoints.cs -Old 'private static async Task<IResult> SearchAsync(HttpContext context, IContainerStore store)' -New 'private static async Task<IResult> SearchAsync(HttpContext context, IContainerStore store) { if (context.User.Identity?.IsAuthenticated != true) return Results.Unauthorized(); return await AuthedSearch(context, store); } private static async Task<IResult> AuthedSearch(HttpContext context, IContainerStore store)' -Filter FullyQualifiedName~ContainerSearchRoutesTests | 0 | docs/sdd/runs/test-runs/20261109-041550-test-filter.log (RED exit: 1, Com falha 3 = AC-01, AC-02, AC-03 Expected OK Actual Unauthorized) e 20261109-041606-test-filter.log (GREEN exit: 0, Aprovado 3); HASH-MATCH True, PRE DAEE4CC7 - M4 |
| 2026-11-09 04:45:23 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0019.ps1 -File backend/src/Squid.Api/Containers/ContainerEndpoints.cs -Old 'seen ? container.MemberCount : null' -New 'container.MemberCount' -Filter FullyQualifiedName~ContainerSearchRoutesTests | 0 | docs/sdd/runs/test-runs/20261109-041632-test-filter.log (RED exit: 1, Com falha 1 = AC-03:134 Expected Null Actual Number) e 20261109-041647-test-filter.log (GREEN exit: 0, Aprovado 3); HASH-MATCH True, PRE DAEE4CC7 - M5 |
| 2026-11-09 04:45:53 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0019.ps1 -File backend/src/Squid.Api/Containers/ContainerEndpoints.cs -Old '&& container.Policy.Join is JoinPolicy.Open or JoinPolicy.Request;' -New '&& container.Policy.Join is JoinPolicy.Open or JoinPolicy.Request or JoinPolicy.Invite;' -Filter FullyQualifiedName~Sprint2ClosingFacetTests | 0 | docs/sdd/runs/test-runs/20261109-041724-test-filter.log (RED exit: 1, Com falha 1 = AC-04a:51 Expected NotFound Actual Forbidden) e 20261109-041744-test-filter.log (GREEN exit: 0, Aprovado 3); HASH-MATCH True, PRE DAEE4CC7 - M6a |
| 2026-11-09 04:45:53 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0019.ps1 -File backend/src/Squid.Api/Containers/ContainerEndpoints.cs -Old 'membership?.State is MembershipState.Active or MembershipState.Silenced or MembershipState.Invited' -New 'membership?.State is MembershipState.Active or MembershipState.Silenced' -Filter FullyQualifiedName~Sprint2ClosingFacetTests | 0 | docs/sdd/runs/test-runs/20261109-041827-test-filter.log (RED exit: 1, Com falha 1 = AC-04a:65 Expected OK Actual NotFound) e 20261109-041849-test-filter.log (GREEN exit: 0, Aprovado 3); HASH-MATCH True, PRE DAEE4CC7 - M6b |
| 2026-11-09 04:45:54 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0019.ps1 -File backend/src/Squid.Api/Containers/ContainerEndpoints.cs -Old 'ActorId = membership?.ActorId ?? ActorId(context, current.Handle),' -New 'ActorId = membership?.ActorId ?? string.Empty,' -Filter FullyQualifiedName~Sprint2ClosingFacetTests | 0 | docs/sdd/runs/test-runs/20261109-041934-test-filter.log (RED exit: 1, Com falha 1 = AC-04b:120 Assert.True Actual False) e 20261109-041956-test-filter.log (GREEN exit: 0, Aprovado 3); HASH-MATCH True, PRE DAEE4CC7 - M7 |
| 2026-11-09 04:45:54 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0019.ps1 -File backend/src/Squid.Api/Containers/Ownership.cs -Old 'foreach (var row in SuccessionRules.Transfer(members, container.Slug, newOwnerHandle, newOwnerActorId, now))' -New 'foreach (var row in Array.Empty<ContainerMembership>())' -Filter FullyQualifiedName~Sprint2ClosingFacetTests | 0 | docs/sdd/runs/test-runs/20261109-042036-test-filter.log (RED exit: 1, Com falha 1 = AC-04c:201 NeverOrphaned na linha 152) e 20261109-042059-test-filter.log (GREEN exit: 0, Aprovado 3); HASH-MATCH True, PRE F35F5A56 - M8 |
| 2026-11-09 04:45:55 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0019.ps1 -File backend/src/Squid.Service/SqliteContainerStore.cs -Old 'container.IsSearchable' -New 'true' -Filter FullyQualifiedName~Qa0022 | 0 | docs/sdd/runs/test-runs/20261109-042307-test-filter.log (RED exit: 1, Com falha 1 = fuzz da QA Qa0022:84) e 20261109-042320-test-filter.log (GREEN exit: 0, Aprovado 2); HASH-MATCH True, PRE 7A7BA9C2 - M9 |
| 2026-11-09 04:45:55 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Containers | 0 | docs/sdd/runs/test-runs/20261109-042454-test-filter.log (exit: 0, Com falha 0, Aprovado 70 - area da task) |
| 2026-11-09 04:45:55 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate final) | 0 | docs/sdd/runs/test-runs/20261109-042748-test-gate.log (exit: 0, Com falha 0, Aprovado 244 = 242 do DEV + 2 da QA) |
| 2026-11-09 04:45:56 | qa | wsl -e sh -lc command -v dotnet e command -v pwsh e command -v bash e command -v git e command -v sha256sum | 0 | stdout do probe: dotnet:ABSENT, pwsh:ABSENT, bash:ok, git:ok, sha256sum:ok - suite em Linux unverified nesta sessao |
| 2026-11-09 04:46:17 | qa | Get-FileHash dos 3 mutados + varredura QA-MUTATION em backend + git status --porcelain -uall + Get-ChildItem mtime desde 04:06 + leitura dentro dos logs do gate e do filtro | 0 | hashes = PRE (7A7BA9C2, DAEE4CC7, F35F5A56); residuos 0; git = baseline 58 + 4 artefatos da QA; mtime = 4 arquivos nos touches; 042748 exit: 0 Aprovado 244 e 042454 exit: 0 Aprovado 70 |
| 2026-11-09 04:46:18 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0022.ps1 (1a execucao) | 0 | stdout: SUMMARY total=17 fails=0 RESULT: PASS (17/17, Windows) |
| 2026-11-09 04:46:19 | qa | wsl -e bash -c cd /mnt/c/dev/squid-squad-net/squid-execution e bash docs/sdd/qa/qa-check-task-0022.sh (1a execucao) | 1 | stdout no WSL: CHECK RESTORE-SHA256 FAIL (Ownership=1 - o proprio checker tinha o SHA256 PRE digitado errado); SUMMARY total=17 fails=1 RESULT: FAIL - defeito do checker, corrigido no checker |
| 2026-11-09 04:46:19 | qa | wsl -e bash -c cd /mnt/c/dev/squid-squad-net/squid-execution e bash docs/sdd/qa/qa-check-task-0022.sh (2a execucao, apos corrigir o hash no checker) | 0 | stdout no WSL: SUMMARY total=17 fails=0 RESULT: PASS (17/17, gemeo Linux) |
| 2026-11-09 04:46:20 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0022.ps1 (self-test AC-05: limiar GATE-NEWEST-SUMMARY 244 trocado por 9999 de proposito) | 1 | stdout: CHECK GATE-NEWEST-SUMMARY FAIL ... SUMMARY total=17 fails=1 RESULT: FAIL - o checker sabe falhar |
| 2026-11-09 04:46:20 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0022.ps1 (limiar restaurado 9999 para 244) | 0 | stdout: SUMMARY total=17 fails=0 RESULT: PASS; SHA256 D49AEE3996B7FA77BE22BA1315E7214C913F9ED7D79C4DAAEEED78443CBA0B51 igual ao PRE |
| 2026-11-09 04:47:45 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0022.ps1 (execucao definitiva pos-appends) | 0 | stdout: SUMMARY total=17 fails=0 RESULT: PASS (17/17, Windows) |
| 2026-11-09 04:47:45 | qa | wsl -e bash -c cd /mnt/c/dev/squid-squad-net/squid-execution e bash docs/sdd/qa/qa-check-task-0022.sh (execucao definitiva pos-appends) | 0 | stdout no WSL: SUMMARY total=17 fails=0 RESULT: PASS (17/17, Linux) |
| 2026-11-09 04:47:46 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-state.ps1 check docs/sdd/tasks/TASK-0022-descoberta-listagem.md | 0 | stdout: check: OK - log entries 12, evidence rows 28 (antes destas proprias linhas) |
| 2026-11-09 04:48:12 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-state.ps1 check docs/sdd/tasks/TASK-0022-descoberta-listagem.md (estado final apos as proprias linhas) | 0 | stdout: check: OK - log entries 13, evidence rows 31 (a propria linha seguinte fecha em 32) |
| 2026-11-09 04:55:38 | odev | Select-String do gate final + varredura QA-MUTATION em backend + sdd-state check | 0 | docs/sdd/runs/test-runs/20261109-042748-test-gate.log exit: 0 / Aprovado 244 / Com falha 0; residuos 0; check OK log 13 evidencias 32; QA-0021 presente |
