<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0003 — Storage SQLite com ETag imposto e store name como parâmetro

```yaml
runId: R-20261104-01
taskId: TASK-0003
status: DONE
agent: odev
phase: P-01
sequence: 2
createdAt: 2026-11-04 12:06:31
updatedAt: 2026-11-06 09:07:42
parent: SPEC-0001
authority: SPEC
parallel: false
touches: [backend/src/Squid.Persistence/**, backend/tests/Squid.Tests/Storage/**, backend/Directory.Packages.props]
blocks: []
blockedBy: [TASK-0002]
inputArtifacts: ["docs/features/05-arquitetura-consolidada.md", "specs/001-squid-mvp/data-model.md"]
outputArtifacts: ["backend/src/Squid.Persistence/SqliteGrainStorage.cs", "backend/tests/Squid.Tests/Storage/SqliteGrainStorageTests.cs"]
interviewRounds: 0/6
feasibilityRounds: 1/2
correctionAttempts: 0/2
qaAttempts: 0/2
contextHandoffs: 0/4
progressDelta: 0
humanGate:
  approvedBy: Henrique
  approvedDate: 2026-11-06 07:35:15
  scope: D-01 aprovado: instalar Microsoft.Orleans.* (linha 10.2) + Microsoft.Data.Sqlite (linha 10.0) com versao fixada em backend/Directory.Packages.props (ADR-25), PackageReference sem Version=; contrato local sem Orleans rejeitado
  expiresAt: NENHUM
blocker: NENHUM
nextAction: DONE - QA-0003 PASSED (7/7 ACs, qaAttempt 1/2); ultima entrega da primeira carga da P-01 - cortar entregas seguintes da fase (silo + /health + /ready + Orleans Dashboard, OTel + Serilog)
evidence: []
memoryBankFlags: [MUDANCA_RELEVANTE: BUILD - primeiro par de dependencias de runtime do backend: Microsoft.Orleans.Core 10.2.2 + Microsoft.Data.Sqlite 10.0.14 em Directory.Packages.props sob humanGate D-01; Squid.Persistence ganha IGrainStorage sobre SQLite com ETag imposto - sources: backend/Directory.Packages.props:L12-L16, backend/src/Squid.Persistence/SqliteGrainStorage.cs]
```

## Intent

Implementa `IGrainStorage` sobre SQLite com ETag imposto (doc 05 §4.7 #1) e store name nunca
hardcoded (#2). É o que impede a escrita concorrente perdida em silêncio — o defeito que a análise
do orders-orleans apontou e que o doc 05 §4.7 transformou em regra obrigatória.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: duas escritas com o mesmo `ETag` esperado sobre a mesma chave: a primeira grava, a segunda
  lança `InconsistentStateException` e o estado em disco é o da primeira — verificado por teste que
  lê de volta depois da segunda escrita.
- AC-02: escrita com `ETag` nulo sobre linha existente lança `InconsistentStateException` e não
  sobrescreve.
- AC-03: dois stores distintos com o mesmo `grainId` guardam linhas separadas; ler o store `actors`
  nunca devolve o estado do store `containers` — teste com leitura cruzada nos dois sentidos.
- AC-04: leitura de chave inexistente devolve `State = null` e `ETag = null`, sem exceção.
- AC-05: `ClearStateAsync` respeita a regra de ETag e, após ela, a leitura volta a ser ausente.
- AC-06: um teste de arquitetura falha se o SQL de `Squid.Persistence` contiver o nome de um store
  como literal (grep scriptado sobre os `.cs` do projeto com exit 0 no estado correto).
- AC-07: a suíte de storage roda contra SQLite em arquivo temporário real (não em memória
  compartilhada), deixa o arquivo limpo ao fim e passa duas vezes seguidas.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Persistence/**`
- `backend/tests/Squid.Tests/Storage/**`

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-04 | no | `etag` = `GrainStateVersion` em base64; comparação exata em `WHERE Version = @Version` |
| GAP-05 | no | SQLite é o gate do CI; a suíte de contratos é escrita contra `IGrainStorage`, para rodar depois contra Oracle ATP sem reescrita |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`

- 2026-11-04 12:06:31 | odev | task created from SPEC-0001
- 2026-11-06 00:37:43 | odev | dispatch DEV: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; blockedBy TASK-0002 (DONE) satisfeito; touches disjointas das demais tarefas da P-01
- 2026-11-06 01:02:22 | odev | primeiro dispatch de DEV falhou antes de terminar (subagente morreu sem closing message, zero escritas no repositorio - confirmado por git status); redispatch unico permitido pela politica de falha
- 2026-11-06 01:27:13 | dev | DEV rodada de viabilidade 1/2: baseline verde antes de qualquer edicao - powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test exit 0, Aprovado 16, Com falha 0, Total 16 em 26 s (log docs/sdd/runs/test-runs/20261106-011938-test-gate.log)
- 2026-11-06 01:27:13 | dev | DEV confirmou touches com uma correcao: falta backend/Directory.Packages.props (ADR-25 vive la, fora do glob atual); nenhuma task aberta intersecta - TASK-0001, TASK-0002 e TASK-0004 estao DONE
- 2026-11-06 01:27:13 | dev | DEV viabilidade: o que as AC pedem ainda nao existe - git grep -n GrainId -- backend exit 1 (o tipo do contrato nao esta em lugar nenhum do solution); git grep -n PackageReference -- backend/src so achou comentario (Squid.Domain.csproj:4, Squid.Worker.csproj:6); git grep -n -e Sqlite -e IGrainStorage -- backend/src so achou comentario (PersistenceAssembly.cs:5, Squid.Persistence.csproj:3)
- 2026-11-06 01:27:13 | dev | DEV viabilidade: cache offline backend/.packages nao tem microsoft.data.sqlite nem microsoft.orleans.core.abstractions (Test-Path exit 1 nos dois); nuget.org respondeu HTTP 200, entao o bloqueio nao e de rede - e de autorizacao
- 2026-11-06 01:27:13 | dev | DEV D-01 bloqueante (ladder spec -> requirement -> humano): SPEC-0001 tipa o contrato como GrainId e REQ-0001 fixa a stack (Orleans 10.2 + SQLite), mas nenhum dos dois autoriza DEV a instalar pacote - instalar dependencia e gate humano no fluxo e o humanGate desta task e NENHUM; TASK-0004 so acrescentou pacotes sob humanGate aprovado por Henrique
- 2026-11-06 01:27:13 | dev | DEV retorna BLOCKED no gate sem escrever codigo de produto - pedido ao humano: autorizar Microsoft.Data.Sqlite e Microsoft.Orleans.* com a versao em backend/Directory.Packages.props (ADR-25), ou ditar que o contrato IGrainStorage sera local (isso muda o contrato publicado: GrainId deixaria de ser um tipo do Orleans)
- 2026-11-06 01:35:12 | odev | DEV tentativa 1/2 retornou BLOCKED com D-01 no gate de dependencia: os pacotes que as AC de TASK-0003 exigem (Microsoft.Data.Sqlite, Microsoft Orleans para GrainId/IGrainStorage/InconsistentStateException) nao existem no solution nem no cache backend/.packages, e instalar dependencia e gate humano - humanGate era NENHUM, nenhum codigo de produto escrito
- 2026-11-06 01:35:12 | odev | ladder esgotada: SPEC-0001 define a forma do contrato e exige versao centralizada em Directory.Packages.props mas nao autoriza instalacao; REQ-0001:52 fixa a stack (Orleans 10.2 + SQLite) responde o que, nao o quem instala - sobe para o nivel 3 (humano)
- 2026-11-06 01:35:12 | odev | correcao de touches aplicada por ODEV: backend/Directory.Packages.props entrou na lista (ADR-25 mora la, DEV confirmou na viabilidade, nenhuma task aberta intersecta)
- 2026-11-06 01:35:12 | odev | status IN_DEVELOPMENT -> AWAITING_HUMAN; humanGate.scope = D-01, expiresAt 2026-11-13 01:30:00; feasibilityRounds 0/2 -> 1/2 (contado do step result do DEV)
- 2026-11-06 01:35:12 | odev | baseline do DEV antes de editar: gate verde 16/16 exit 0 (docs/sdd/runs/test-runs/20261106-011938-test-gate.log); zero arquivos de produto escritos
- 2026-11-06 07:35:20 | odev | humano aprovou D-01: instalar Microsoft.Orleans.* (linha 10.2) + Microsoft.Data.Sqlite (linha 10.0) com versao fixada em backend/Directory.Packages.props (ADR-25) e PackageReference sem Version=; alternativa de contrato local sem Orleans rejeitada - registro em humanGate com approvedBy Henrique, approvedDate 2026-11-06 07:35:15, expiresAt NENHUM
- 2026-11-06 07:35:20 | odev | humanGate preenchido e status AWAITING_HUMAN -> IN_DEVELOPMENT; resposta humana nao consome rodada de feasibility (segue 1/2); redispatch DEV attempt 2/2
- 2026-11-06 08:28:17 | dev | DEV viabilidade 2/2 sem duvida nova: contrato Orleans 10.2 verificado por probe da API real fora do repo (scratch _api-probe, apagado ao fim) - IGrainStorage generico com IGrainState<T> State/ETag/RecordExists, GrainId em Orleans.Runtime, InconsistentStateException (errorMsg, storedEtag, currentEtag); D-01 resolvido pelo humanGate e baseline 16/16 exit 0 da rodada 1 valendo
- 2026-11-06 08:28:17 | dev | DEV instalou as pacotes autorizadas pelo humanGate D-01: PackageVersion Microsoft.Orleans.Core 10.2.2 e Microsoft.Data.Sqlite 10.0.14 em backend/Directory.Packages.props (ADR-25) e PackageReference sem Version= em Squid.Persistence.csproj; dotnet restore exit 0 com os dois pacotes no cache offline backend/.packages
- 2026-11-06 08:28:17 | dev | DEV implementou backend/src/Squid.Persistence/SqliteGrainStorage.cs: IGrainStorage sobre arquivo SQLite, tabela GrainState com chave (StoreName, GrainId), store name sempre parametro, CAS exato em WHERE Version = @Version (GAP-04, etag = versao monotonica em base64) e InconsistentStateException em escrita ou clear divergentes sem nada gravado
- 2026-11-06 08:28:17 | dev | DEV escreveu backend/tests/Squid.Tests/Storage: 10 testes de contrato para AC-01 a AC-05 e AC-07 (arquivo temporario por teste com Pooling=False e delete no Dispose) mais 1 teste de arquitetura que falha se algum .cs do projeto trouxer nome de store literal (AC-06)
- 2026-11-06 08:28:17 | dev | DEV verificou tudo depois da mudanca: build exit 0 com 0 avisos e 0 erros; suite de storage duas vezes seguidas 11/11 exit 0; gate completo 27/27 exit 0 (16 pre-existentes + 11 novas); grep do AC-06 hits=0 exit 0; leftovers=0 em %TEMP%
- 2026-11-06 08:32:52 | odev | ODEV verificou antes de transicionar: check exit 0 (21 logs, 13 evidencias); gate 27/27 exit 0 confirmado lendo o cabecalho e o resumo de docs/sdd/runs/test-runs/20261106-082300-test-gate.log; arvore so com os arquivos do touches (Directory.Packages.props, Squid.Persistence/**, Squid.Tests/Storage/**) mais os ja modificados pelo fluxo
- 2026-11-06 08:32:52 | odev | status IN_DEVELOPMENT -> AWAITING_QA; touchedOutsidePlan do DEV (scratch _api-probe fora do repo, removido; cache %USERPROFILE%\.nuget do probe) aceito como efeito colateral fora do repositorio, nenhuma escrita fora do toques alem disso
- 2026-11-06 09:00:08 | qa | QA-0003 aberta: tarefa lida em AWAITING_QA, AC-01..AC-07, gaps GAP-04/GAP-05 e rota qa-integracao do memory bank (build-run-test.md)
- 2026-11-06 09:00:08 | qa | gate re-executado por QA antes de qualquer escrita: sdd-test.ps1 test exit 0 com Aprovado 27 Com falha 0 Total 27 em docs/sdd/runs/test-runs/20261106-083953-test-gate.log - reinvindicacao do DEV confirmada
- 2026-11-06 09:00:08 | qa | AC-07: storage duas vezes seguidas no mesmo processo, 11/11 exit 0 em ambas - logs 20261106-084156-test-filter.log e 20261106-084205-test-filter.log
- 2026-11-06 09:00:08 | qa | AC-06 + ADR-25 + GAP-04 estatico: qa-check-task-0003.ps1 exit 0 com 9 verificacoes, incluindo probe que nao pode passar errado
- 2026-11-06 09:00:08 | qa | gem .sh executado no WSL2: bash docs/sdd/qa/qa-check-task-0003.sh exit 0 em Linux 6.18.54.1 - mesmas 9 verificacoes
- 2026-11-06 09:00:08 | qa | GAP-04 dinamico: sonda Qa0003EtagFormatTests le a coluna Version direto do arquivo SQLite e compara com o ETag da API - storage com a sonda 12/12 exit 0 (log 20261106-084624-test-filter.log)
- 2026-11-06 09:00:08 | qa | mutacao M1: removido AND Version = @Version do UPDATE em SqliteGrainStorage.cs - storage Com falha 1 em SqliteGrainStorageTests.cs:62, exit 1 (log 20261106-084741-test-filter.log); restaurado, SHA256 identico ao anterior
- 2026-11-06 09:00:08 | qa | mutacao M2: literal actors injetado em PersistenceAssembly.cs - StoreNameLiteralTests Com falha 1 em StoreNameLiteralTests.cs:44, exit 1 (log 20261106-084903-test-filter.log); restaurado, SHA256 identico ao anterior
- 2026-11-06 09:00:08 | qa | gate final apos as restauracoes: sdd-test.ps1 test exit 0 com 28/28 (27 do DEV + 1 sonda do QA) em docs/sdd/runs/test-runs/20261106-085028-test-gate.log; qa-check-task-0003.ps1 exit 0
- 2026-11-06 09:00:08 | qa | ADR-25 mantida: 0 PackageReference com Version= em 10 csproj e PackageVersion Microsoft.Data.Sqlite 10.0.14 e Microsoft.Orleans.Core 10.2.2 em backend/Directory.Packages.props - dentro do escopo do humanGate D-01
- 2026-11-06 09:00:08 | qa | relatorio docs/sdd/qa/QA-0003-storage-sqlite-etag.md escrito - verdicto PASSED com AC-01..AC-07 cobertas, 1 finding MINOR, unverified: suite xunit em Linux por falta de dotnet SDK no WSL2
- 2026-11-06 09:07:45 | odev | verdicto QA-0003 adjudicado: PASSED com 7/7 ACs provadas por execucao (gate 27/27 re-executado pelo QA, storage 2x seguidas 11/11, GAP-04 provado em 3 camadas com sonda que le a coluna Version do arquivo SQLite, 2 mutacoes vermelhas restauradas com SHA256 identico, gate final 28/28 exit 0); transicao AWAITING_QA -> DONE
- 2026-11-06 09:07:45 | odev | finding MINOR do QA-0003 registrado como S-14 em ideas/BACKLOG.md: o detector do AC-06 e regex fixa dos stores de hoje (actors/containers), store novo precisa entrar na lista a mao - nao bloqueia
- 2026-11-06 09:07:45 | odev | unverified do QA-0003 aceito e registrado: suite xunit nunca rodou fora do Windows (WSL2 sem .NET SDK, instalar e gate humano); verificacoes portaveis rodaram no WSL2 via qa-check-task-0003.sh exit 0

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-06 01:27:42 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261106-011938-test-gate.log - Aprovado 16, Com falha 0, Total 16 |
| 2026-11-06 01:27:42 | dev | git grep -n GrainId -- backend | 1 | nenhuma ocorrencia - o tipo do contrato pedido por SPEC-0001 nao existe no solution |
| 2026-11-06 01:27:42 | dev | git grep -n PackageReference -- backend/src | 0 | so comentarios em Squid.Domain.csproj:4 e Squid.Worker.csproj:6 - nenhum PackageReference real em src |
| 2026-11-06 01:27:43 | dev | git grep -n -e Sqlite -e IGrainStorage -- backend/src | 0 | so comentarios em PersistenceAssembly.cs:5 e Squid.Persistence.csproj:3 - sem implementacao e sem contrato |
| 2026-11-06 01:27:43 | dev | powershell -NoProfile -Command if (Test-Path backend/.packages/microsoft.data.sqlite) { exit 0 } else { exit 1 } | 1 | pacote ausente do cache offline; microsoft.orleans.core.abstractions devolveu exit 1 tambem |
| 2026-11-06 01:27:44 | dev | Invoke-WebRequest https://api.nuget.org/v3/index.json -Method Head -TimeoutSec 10 -UseBasicParsing | 0 | HTTP 200 - rede disponivel; o gate e de autorizacao, nao de rede |
| 2026-11-06 08:29:05 | dev | dotnet restore backend\Squid.slnx | 0 | backend/.packages com microsoft.data.sqlite 10.0.14 e microsoft.orleans.core 10.2.2 - restore offline |
| 2026-11-06 08:29:06 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs\sdd\_tools\sdd-test.ps1 build | 0 | 0 Aviso(s) 0 Erro(s) - build do Squid.slnx |
| 2026-11-06 08:29:06 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs\sdd\_tools\sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Storage | 0 | docs/sdd/runs/test-runs/20261106-082117-test-filter.log - Aprovado 11 Com falha 0 - storage 1a execucao |
| 2026-11-06 08:29:07 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs\sdd\_tools\sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Storage | 0 | docs/sdd/runs/test-runs/20261106-082205-test-filter.log - Aprovado 11 Com falha 0 - storage 2a execucao seguida |
| 2026-11-06 08:29:07 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs\sdd\_tools\sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261106-082300-test-gate.log - Aprovado 27 Com falha 0 - 16 pre-existentes + 11 novas |
| 2026-11-06 08:29:07 | dev | $hits = @(Select-String -Path (Get-ChildItem -Recurse -File -Filter *.cs -Path 'backend/src/Squid.Persistence').FullName -Pattern 'actors','containers'); Write-Output ('hits=' + $hits.Count); if ($hits.Count -gt 0) { exit 1 }; exit 0 | 0 | hits=0 em backend/src/Squid.Persistence - nenhum nome de store literal nos .cs |
| 2026-11-06 08:29:08 | dev | $f = @(Get-ChildItem -Path $env:TEMP -Filter 'squid-grainstate-*' -File -ErrorAction SilentlyContinue); Write-Output ('leftovers=' + $f.Count); if ($f.Count -eq 0) { exit 0 }; exit 1 | 0 | leftovers=0 em %TEMP% - a suite nao deixa arquivo temporario para tras |
| 2026-11-06 09:01:04 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261106-083953-test-gate.log - Aprovado 27 Com falha 0 Total 27 (gate re-executado por QA) |
| 2026-11-06 09:01:05 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Storage | 0 | docs/sdd/runs/test-runs/20261106-084156-test-filter.log - Aprovado 11 Com falha 0 - run A do AC-07 |
| 2026-11-06 09:01:05 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Storage | 0 | docs/sdd/runs/test-runs/20261106-084205-test-filter.log - Aprovado 11 Com falha 0 - run B seguida do AC-07 |
| 2026-11-06 09:01:06 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0003.ps1 | 0 | 9 verificacoes ok - AC-06 positivo e can-fail, ADR-25, GAP-04 SQL, leftovers do AC-07 |
| 2026-11-06 09:01:06 | qa | bash docs/sdd/qa/qa-check-task-0003.sh | 0 | WSL2 Linux 6.18.54.1 - gem .sh com as mesmas 9 verificacoes ok |
| 2026-11-06 09:01:06 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Storage | 0 | docs/sdd/runs/test-runs/20261106-084624-test-filter.log - Aprovado 12 Com falha 0 - storage com a sonda GAP-04 |
| 2026-11-06 09:01:07 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Storage | 1 | docs/sdd/runs/test-runs/20261106-084741-test-filter.log - Com falha 1 em SqliteGrainStorageTests.cs:62 com a mutacao M1 aplicada; restaurada e SHA256 conferido |
| 2026-11-06 09:01:07 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~StoreNameLiteralTests | 1 | docs/sdd/runs/test-runs/20261106-084903-test-filter.log - Com falha 1 em StoreNameLiteralTests.cs:44 com a mutacao M2 aplicada; restaurada e SHA256 conferido |
| 2026-11-06 09:01:08 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261106-085028-test-gate.log - Aprovado 28 Com falha 0 Total 28 (27 DEV + 1 sonda QA) |
| 2026-11-06 09:01:08 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0003.ps1 | 0 | summary all checks behaved as specified - exit 0; backups em TEMP removidos e leftovers 0 |
| 2026-11-06 09:01:33 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-state.ps1 check docs/sdd/tasks/TASK-0003-storage-sqlite-com-etag.md | 0 | check OK - log entries 34 evidence rows 23 - relatorio docs/sdd/qa/QA-0003-storage-sqlite-etag.md escrito com verdicto PASSED |
