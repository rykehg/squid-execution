<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: CONCLUIDO
- owner: qa
- lastVerified: 2026-11-21 22:07:00

# QA-0055 — BackupCoordinator, agendador e rotas admin de backup

- task: `docs/sdd/tasks/TASK-0055-coordinator-agendador-rotas.md` (status `AWAITING_QA`, qaAttempt 0/2 → esta é a 1/2)
- runId: `R-20261120-01` · branch `task/TASK-0055-coordinator-agendador-rotas` (commit `bdf1ccf` sobre `develop` `8469915`, um commit só — testado exatamente nesse estado, sem push/merge)
- critérios testados: a lista `## Acceptance criteria` do arquivo da task, como está (SPEC-0012 §C; contrato `specs/001-squid-mvp/contracts/backup-recovery.md` §3/§6) + as correções S-47/S-48 que esta task carregou
- política: ponytail-qa `full` · memory bank: rota `qa-integracao` lida (`index.md`, `build-run-test.md#Local tests without gate`, `contracts.md`) — 3 arquivos
- harness: gate do projeto `docs/sdd/_tools/sdd-test.ps1` (Windows, plataforma primária) + driver próprio `qa-mutate-task-0055.ps1` + escopo `qa-scope-task-0055.ps1`/`.sh` + gêmeo Linux `qa-linux-task-0055.sh` no contêiner local `mcr.microsoft.com/dotnet/sdk:10.0` (10.0.403 = pin do `ci.yml`, imagem já presente — nada instalado)

## AC → cenário

| AC | Cenário executado | Resultado |
| --- | --- | --- |
| AC-01 | `BackupCoordinatorTests.Ac01`: fluxo do contrato §3 por `instanceId` — `LastConsolidatedSeqAsync("i-1")` → `SnapshotAsync("i-1",0..7)` na ordem exata (assert de `state.Calls`), Worker serializa/cifra/envia/rele e registra envelope `Verified` com `seqFrom=0/seqTo=7/targetId=local-disk/contentHash sha256:` e `verifiedAt` não-nulo; 2º run descobre `seqFrom=8` pelo `ListAsync` do destino (§1, sem estado paralelo) e registra a 2ª linha | PASS — filtro `FullyQualifiedName~Squid.Tests.Backup.BackupCoordinatorTests` exit 0 (3/3) + mutação **M1** (`NextSeqFromAsync` → `return 0`) → vermelho **exatamente o Ac01**, log exit 1 |
| AC-02 | `BackupSchedulerTests` ×2: (a) `BackupOptions.From` sem config → `Interval == 1440 min` e `InstanceId == "default"`, `--Backup:IntervalMinutes=1` honra, `0` deixa ocioso; (b) host real `BackupHost.StartAsync` com `Backup:IntervalMinutes=1` e **zero requests** — logo após o boot 0 envelopes, um intervalo depois 1 envelope no registry (poll de 120 s, sem nenhuma chamada HTTP) | PASS — filtro exit 0 (2/2) + mutações **M2a** (default 1439) → vermelho o teste de default, exit 1; **M2b** (loop nunca chama `RunOnceAsync`) → vermelho o teste do host no timeout de 120 s, exit 1 |
| AC-03 | `BackupAdminRouteTests.Ac03_ac04_ac05`: sem sessão → `401` nas duas rotas; sessão fora de `Instance:Operators` → `403` + `policy_denied` nas duas; operador → `POST /api/admin/backup/run` `200` com envelope `status=Verified`, `id` e `contentHash sha256:` e `COUNT(backup_envelope)=1` | PASS — filtro exit 0 + mutação **M3** (gate invertido) → vermelho **o Ac03_ac04_ac05**, exit 1 |
| AC-04 | mesmo teste: `GET /api/admin/backup/status` devolve os **6 campos do contrato §6** — `backup.last_success_at` (ISO não-vazio), `backup.envelopes_count=1`, `backup.bytes_total>0`, `backup.verify_failures=0`, `restore.last_drill_at=null`, `restore.last_drill_result=null` (estado honesto aceito pelo ODEV: drills são TASK-0056; os campos existem e respondem null) | PASS — filtro exit 0 + mutação **M4** (`envelopes_count + 1`) → vermelho **o Ac03_ac04_ac05** na asserção do campo, exit 1 |
| AC-05 | `BackupAlerterTests` ×3 (fronteira exata: 23 h e 24 h silenciosos, 25 h alerta; nunca-ha-backup alerta; `verify_failures>0` alerta sozinho; emissão como `Warning` na ordem Stale→Verify) + fim a fim na rota: `verified_at` envelhecido para −25 h → `backup.last_success_at` refletido e `StaleAlert` no log capturado (controle positivo: sem idade, nada); envelope `Corrupt` (releitura adulterada) → `verify_failures=1` + `VerifyAlert` | PASS — filtro família exit 0 + mutações **M5** (janela 24 h → 48 h) → **3 vermelhos** (unidade stale, emissão de warning e a metade da rota), exit 1; **M6** (`> 0` → `> 1`) → **2 vermelhos** (unidade e rota verify), exit 1 |
| AC-06 | `BackupCoordinatorTests` ×2 (target em queda e state source em queda → `Succeeded=false` com mensagem, **nada registrado**, exceção não escapa) + `BackupAdminRouteTests.Ac06`: destino `BrokenTarget` → `500 backup_failed` fixo, sem caminho/`Exception` no corpo (FR-053), `/health` e `/ready` seguem `200`, registry `0` | PASS — filtro exit 0 + mutação **M7** (`return` → `throw failure`) → vermelhos **os 2 Ac06**, exit 1 |
| AC-07 | gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` exit 0 (620/620, abertura e fechamento desta QA); escopo lido do git: 25 arquivos do commit **TODOS** dentro dos `touches`, `git rev-list --count develop..HEAD = 1`, 0 linha `<PackageReference>`/`<PackageVersion>` adicionada, nenhum `.csproj`/`.props` no commit, `git status -- backend` vazio (resíduo 0, untracked 0), blobs 8/8 idênticos ao `HEAD`, exatamente os 8 arquivos mutados com mtime novo (`delta=0`) — no Windows e no Linux | PASS — gates exit 0 (logs `20261121-212318-test-gate.log` de abertura e `20261121-220634-test-gate.log` de fechamento, cabeçalho `exit: 0` nos dois) + `qa-scope-task-0055.ps1` exit 0 e gêmeo `.sh` no Linux exit 0 |
| S-47 | `BackupHeaderTests.S47`: após o run, **o arquivo `.json` do destino** (não o objeto) lê `status=Verified` com `verifiedAt` igual ao do envelope — o header é regravado pós-releitura; só 2 arquivos no volume, nenhum `.tmp` residual | PASS — filtro `BackupHeaderTests` exit 0 (2/2) + mutação **M8** (bloco `IEnvelopeStateWriter` removido do pipeline) → vermelho **o S47**, exit 1 |
| S-48 | `BackupHeaderTests.S48`: `.json` rasgado + `.tmp` órfão no volume ao lado de envelope bom → `ListAsync` devolve **só** o envelope bom (leitor tolerante) e o `File.Move` do tmp+move faz o header só existir no caminho final (mutação M10 derruba os dois cenários) | PASS — filtro exit 0 + mutações **M9** (`when` do catch deixa de pegar `JsonException`) → vermelho **o S48**, exit 1; **M10** (`File.Move` removido) → vermelhos **S47 e S48**, exit 1 |

Nenhum AC ficou `NOT-COVERED`.

## Execuções (comando → exit → cauda)

Todas executadas nesta sessão, cwd `C:\dev\squid-squad-net\squid-execution`.

1. `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` → exit 0 → `Aprovado! – Com falha: 0, Aprovado: 620, Ignorado: 0, Total: 620, Duração: 9 m 30 s` (gate de abertura; log `docs/sdd/runs/test-runs/20261121-212318-test-gate.log`, cabeçalho `exit: 0` — regra S-22: classifiquei sempre pelo log gravado, nunca pelo console)
2. `powershell … -File docs/sdd/qa/qa-mutate-task-0055.ps1 -VerifyOnly` → exit 0 → `VERIFY total=11 bad=0` (os 11 find strings batem exatamente 1× cada; ASCII limpo — `docs/sdd/qa/QA-0055-verify.log`)
3. `powershell … -File docs/sdd/qa/qa-mutate-task-0055.ps1` → exit 0 → 11 ciclos `EXPECT-OK`, `RESTORE-OK hash=PRE residue=0` em todos, fecho `GREEN wrapper=0 logExit=0 aprovado=23 falha=0`, `SUMMARY cycles=11 failed=0 residue-backend=0 green-aprovado=23 elapsed-s=583` (`docs/sdd/qa/QA-0055-mutations.log`; logs de cada ciclo em `docs/sdd/runs/test-runs/20261121-213*-test-filter.log` … `20261121-214140-test-filter.log`)
4. `powershell … -File docs/sdd/qa/qa-scope-task-0055.ps1` → exit 0 → `SUMMARY head=bdf1ccf commit-files=25 outside-touches=0 pkg-added=0 residue-backend=0 untracked-backend=0 blobs=8/8 mtime-delta=0 fails=0` (`docs/sdd/qa/QA-0055-scope.log`)
5. `podman run --rm -v …:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash docs/sdd/qa/qa-linux-task-0055.sh` (Linux real, WSL2, SDK 10.0.403 = pin do CI) → `LINUX-BUILD-EXIT=0` e `LINUX-TEST-EXIT=0` → `Passed! - Failed: 0, Passed: 23, Skipped: 0, Total: 23, Duration: 1 m 8 s` — a família inteira no Linux, hosts reais e o tick de 1 minuto do AC-02 inclusos (`docs/sdd/qa/QA-0055-linux-build.log`, `docs/sdd/qa/QA-0055-linux-test.log`)
6. `podman run … bash docs/sdd/qa/qa-scope-task-0055.sh` (mesmo gêmeo `.sh` de escopo, git do próprio Linux) → **1ª tentativa exit 1 = ambiente**: o contêiner roda como root sobre o bind mount e o git recusa o repositório (`fatal: detected dubious ownership in repository at '/src'`), o que cascateou (`commit_epoch` vazio → `find: cannot interpret '@'`); nenhum check de produto rodou. Corrigido no próprio script com `safe.directory` escopado por variáveis de ambiente do processo + guarda de abort honesto — **2ª execução exit 0** → `SUMMARY head=bdf1ccf commit-files=25 outside-touches=0 pkg-files=0 pkg-added=0 residue-backend=0 untracked-backend=0 blobs=8/8 mtime-delta=0 fails=0` (idêntico ao Windows; `docs/sdd/qa/QA-0055-scope-sh.log`)
7. gate de fechamento (após todas as execuções, árvore restaurada): `powershell … sdd-test.ps1 test` → exit 0 → `Aprovado! – Com falha: 0, Aprovado: 620, Ignorado: 0, Total: 620, Duração: 9 m 35 s` (log `docs/sdd/runs/test-runs/20261121-220634-test-gate.log`, cabeçalho `exit: 0` — a 1ª tentativa de fechamento foi morta junto com a retomada da sessão e NÃO gravou log; reexecutada do zero sobre a mesma árvore)

## Prova de que os testes podem falhar

O driver `docs/sdd/qa/qa-mutate-task-0055.ps1` aplicou 11 mutações — UM literal de código de produto por ciclo —, rodou o filtro mais estreito que cobre o cenário, classificou o vermelho PELO LOG (`exit:` do cabeçalho) e restaurou com `git checkout` comparando o blob PRE vs POST (byte-exact) + bump de mtime (S-23):

| Ciclo | Escopo | Literal quebrado | Vermelho esperado (saiu) |
| --- | --- | --- | --- |
| M1 | AC-01 | `BackupRunner.cs`: `return sent.Max(…) + 1` → `return 0` | Ac01 ✓ (1 falha) |
| M2a | AC-02 | `BackupOptions.cs`: default `1440` → `1439` | Ac02 default ✓ (1) |
| M2b | AC-02 | `BackupScheduler.cs`: `await RunOnceAsync(...)` → `await Task.Yield()` | Ac02 host ✓ (1, timeout de 120 s) |
| M3 | AC-03 | `AdminEndpoints.cs`: `if (!IsOperator(...))` → `if (IsOperator(...))` | Ac03_ac04_ac05 ✓ (1) |
| M4 | AC-04 | `AdminEndpoints.cs`: `envelopes_count = state.EnvelopesCount` → `+ 1` | Ac03_ac04_ac05 ✓ (1) |
| M5 | AC-05 | `BackupAlerter.cs`: janela `> StaleAfter` (24 h) → `> 48 h` | stale da unidade + emissão + metade da rota ✓ (3) |
| M6 | AC-05 | `BackupAlerter.cs`: `VerifyFailures > 0` → `> 1` | verify da unidade + rota ✓ (2) |
| M7 | AC-06 | `BackupCoordinatorGrain.cs`: `return new BackupRunResult(false,…)` → `throw failure` | os 2 Ac06 ✓ (2) |
| M8 | S-47 | `BackupPipeline.cs`: `await writer.WriteEnvelopeStateAsync(...)` → `Task.CompletedTask` | S47 ✓ (1) |
| M9 | S-48 | `LocalDiskTarget.cs`: `when (failure is JsonException` → `when (failure is not JsonException && failure is JsonException` | S48 ✓ (1) |
| M10 | S-48 | `LocalDiskTarget.cs`: `File.Move(temporary, …)` removido | S47 + S48 ✓ (2) |

Fecho: `GREEN … aprovado=23 falha=0` sobre a árvore restaurada, `residue-backend=0`, blobs 8/8 idênticos ao `HEAD` e os 8 arquivos mutados como único mtime novo. Os cenários, portanto, não são decoração: cada um vermelhou quando o comportamento quebrou.

## Findings

1. `docs/sdd/_tools/sdd-test.ps1:80-83` — baixo — **pré-existente** (S-22, `docs/memory-bank/build-run-test.md:54`; reproduzido de novo hoje): no ciclo **M10** a remoção do `File.Move` faz o teste falhar com `FileNotFoundException` cuja pilha contém `Microsoft.Win32.SafeHandles.SafeFileHandle` — o substring solto `handle` casa e o wrapper imprime **exit 3 (ambiente) para um vermelho REAL**; o cabeçalho `exit: 1` do log gravado classificou corretamente (regra S-22). Não bloqueia: nenhum vermelho desta QA foi classificado pelo console.
2. `backend/src/Squid.Grains/Backup/BackupInstance.cs:12` — baixo (documento, não comportamento) — **julgamento pedido pelo ODEV**: a config `Backup:InstanceId` (default `"default"`) não é nomeada na SPEC-0012 (tabela de inputs, `docs/sdd/specs/SPEC-0012-backup-rotação-p07.md:156`, lista só `Backup:IntervalMinutes`, `Backup:Local:Path` e `Backup:S3:*`). **Não é desvio de contrato**: o contrato §3 exige `BackupCoordinatorGrain (instanceId)` mas não fixa de onde o `instanceId` vem; a chave é o detalhe de implementação desse parâmetro, resolvida por um único helper usado pela rota (`AdminEndpoints.cs:407`) e pelo agendador (`BackupScheduler.cs:79`), sob a seção `Backup:` já existente, com default testado — comportamento de contrato idêntico. Recomendação não-bloqueante: ODEV enumerar a chave na tabela de inputs da spec no fecho da fase, para o doc não divergir do código.
3. `backend/src/Squid.Service/SqliteBackupStateSource.cs:20-23` — informativo — atalho **declarado no próprio código** (`shortcut:` com data de upgrade): `LastConsolidatedSeqAsync` lê o documento inteiro de atividades e pega o último número, em vez de uma leitura dedicada de “seq atual”; o comentário aponta a janela incremental de R-15/TASK-0056+. Risco aceito e rastreável — não é defeito desta task.

Nenhum BLOCKER. Nenhum MAJOR. Nenhum AC violado.

## O que não ficou coberto (unverified)

- **Suíte Linux COMPLETA verde — `unverified`, nunca assumida verde.** O que está provado no Linux: build exit 0 (SDK 10.0.403 = pin do CI) e a família `Squid.Tests.Backup` **23/23 exit 0** (inclui os hosts reais `SquidServiceHost` e o tick real de 1 minuto do AC-02 — execução 5). O que não: a suíte inteira no Linux, que a QA-0054 mostrou com 51 falhas 100% de ambiente pré-existentes (libfontconfig/Skia, `cmd.exe` S-44, asserts a jusante já vermelhos em develop) — instalar `libfontconfig1` exige gate indisponível nesta sessão.
- **Caminho do CI (`pwsh sdd-test.ps1 test` em ubuntu-latest) — `unverified`:** a imagem sdk não traz `pwsh` e instalar exige gate; executei o gêmeo `.sh`/`dotnet test` (o runner Linux disponível) com o mesmo filtro.
- **Janela de escrita rasgada do tmp+move (fault injection)** — o teste S-48 simula um `.json` rasgado escrito por terceiros e prova que o LEITOR tolera, e M10 prova que o `Move` é necessário para o header existir; nenhum teste injeta um crash no MEIO da escrita do produto para provar que o `.tmp` nunca é visível como `.json`. Risco baixo: o nome do temp não casa `*.json` e o leitor tolerante é a rede de segurança real (testada).
- **Drills, S3, `redundancy.score`, rotação** — fora do escopo desta task (TASK-0056/0057/0058/0060); `restore.last_drill_*` permanece `null` até a TASK-0056, como aceito.

## Reproduzir

1. Pré-requisito: Windows com .NET 10 SDK, checkout da branch `task/TASK-0055-coordinator-agendador-rotas` (`bdf1ccf`).
2. Comando: `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`
3. Esperado: exit 0, `Aprovado: 620, Com falha: 0` (log novo em `docs/sdd/runs/test-runs/`); filtro pontual: mesmo comando com `-TestFilter FullyQualifiedName~Squid.Tests.Backup` → `Aprovado: 23` (~1 min 20 s, o AC-02 espera um tick real de 1 minuto); prova can-fail: `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0055.ps1` → exit 0 com 11 `EXPECT-OK` e fecho `GREEN … aprovado=23`; escopo: `… qa-scope-task-0055.ps1` → exit 0 (Linux: `bash docs/sdd/qa/qa-scope-task-0055.sh` → exit 0).
