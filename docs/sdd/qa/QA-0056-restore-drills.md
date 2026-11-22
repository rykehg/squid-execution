<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: CONCLUIDO
- owner: qa
- lastVerified: 2026-11-22 02:06:00

# QA-0056 — Restore do contrato §4, 5 drills no gate e runbook EXECUTÁVEL

- task: `docs/sdd/tasks/TASK-0056-restore-drills.md` (status `AWAITING_QA`, qaAttempt 0/2 → esta é a 1/2)
- runId: `R-20261120-01` · branch `task/TASK-0056-restore-drills` (commit `3fdfc69` sobre `develop` `4c217a2`, um commit só — testado exatamente nesse estado, sem push/merge, `main` intocado, nenhuma branch removida)
- critérios testados: a lista `## Acceptance criteria` da task como está (SPEC-0012 §D; contrato `specs/001-squid-mvp/contracts/backup-recovery.md` §4/§5; SC-009) + a adjudicação do ODEV no `[DIVERGENCIA]` (§D/§Out-of-scope/GAP-55 corrigidos em 2026-11-22 — verificados contra o código)
- política: ponytail-qa `full` · memory bank: rota `qa-integracao` lida (`index.md`, `build-run-test.md#Local tests without gate`, `contracts.md`) — 3 arquivos
- harness: gate do projeto `docs/sdd/_tools/sdd-test.ps1` (Windows, plataforma primária) + drivers próprios `qa-mutate-task-0056.ps1`, `qa-scope-task-0056.ps1`/`.sh`, `qa-runbook-task-0056.ps1` + gêmeo Linux `qa-linux-task-0056.sh` no contêiner local `mcr.microsoft.com/dotnet/sdk:10.0` (10.0.403 = pin do `ci.yml`, imagem já presente — nada instalado)

## AC → cenário

| AC | Cenário executado | Resultado |
| --- | --- | --- |
| AC-01 | `RestoreDrillTests.Ac01_Roundtrip_restores_100_percent_of_the_aggregate_hashes`: dados criados → backup (`seqFrom=0`) → **banco apagado** → restore → `AggregateDigests.Matches(before, after)` = 100% dos hashes idênticos por agregado (SC-009) + relatório `Events=6 / Aggregates` conferem | PASS — filtro `Category=RestoreDrill` exit 0 (7/7) + mutação **M1** (`RestoreAsync` solta 1 evento via `Skip(1)`) → vermelho **o Ac01** (com mais 5 restores que falham no passo 7), log exit 1 |
| AC-02 | `Ac02_Interrupted_backup...`: backup 1 (0..4) → `DyingTarget` morre no Put (`.bin` rasgado sem header + registro não feito, envelope continua 1) → 2 escritas → retomada `SeqFrom = first.SeqTo+1 = 5` (exatamente o `seqTo` íntegro) → restore da cadeia 6 eventos → `FlippingTarget` deixa envelope `Corrupt` → **recusa** com "corrompido" sem tocar no destino → purge registrado (§7) → restore volta a 6 eventos | PASS — exit 0 + mutação **M2** (checa `accepted[^1].SeqTo != all[^1].SeqTo` trocada por condição sempre-falsa) → vermelho **só o Ac02**; **M5** (janela exclusiva) também derruba o Ac02 |
| AC-03 | `Ac03_An_older_format_is_migrated_and_an_unknown_one_fails_explicitly`: (a) `Migrate(0, payload legado, delta)` preenche a janela pelo envelope (5..8, instanceId, eventos); (b) header reescrito com `format: 0` → restore passa; (c) header com `format: 99` → recusa explícita contendo "formato" e "99" | PASS — exit 0 + mutação **M3** (`if (format == 0)` → `format >= 0`, migra silenciosamente) → vermelho **só o Ac03** |
| AC-04 | `Ac04_A_keyEpoch_without_a_key_in_the_kit_fails_and_destroys_nothing`: header reescrito com `keyEpoch: 99` → recusa "sem chave no kit" com "99" → digests do destino inalterados **e** o arquivo byte-idêntico (SHA-256 do `.db` antes/depois) | PASS — exit 0 + mutação **M4** (checa `KeyEpoch > currentEpoch` → `KeyEpoch < 0`) → vermelho **só o Ac04** |
| AC-05 | `Ac05_The_second_backup_carries_only_the_delta...`: backup 1 (4 eventos) → 3 escritas → backup 2 com `SeqFrom = first.SeqTo+1`, payload **só o delta** (3 eventos, todos `Seq > first.SeqTo`, janela do payload igual à do envelope) → cadeia só-delta restaura 7 eventos em 2 envelopes | PASS — exit 0 + mutação **M5** (volta o bug original: `ReadAsync(seqFrom - 1)` → `ReadAsync(seqFrom)`) → vermelho **o Ac05 + o Ac02** (o defeito real que a correção do DEV fecha) |
| AC-06 | `Ac06_After_the_restore_projections_quota_and_readiness_all_confer`: ledger de cota (`used_bytes=3500` reconciliado com o volume de blobs, fora do envelope por GAP-55), projeções (contagem+hash por container + home) idênticas após restore, `StorageReadiness.IsAccessible()` = predicado do `/ready` verde; + o 7º teste: rota `GET /api/admin/backup/status` publica `restore: passed` e `GET /ready` responde **200** sobre o banco restaurado | PASS — exit 0 + mutações **M6** (passo de projeção lê nada) → vermelho **só o Ac06**; **M7** (`last_drill_result` forcado a null) → vermelho **só o teste da rota** |
| AC-07 | Runbook `docs/runbooks/restaurar-backup.md` EXECUTÁVEL: nenhum passo `PENDENTE` (única ocorrência é a nota "deixou de ser PENDENTE"); log do DEV `docs/sdd/runs/test-runs/20261122-runbook-restaurar-backup.log` cobre A.1-A.3, B.1-B.4, V.1/V.2/V.4; **re-executado por esta QA** com `qa-runbook-task-0056.ps1`: A.1 register 201 → A.2 envelope `Verified` → A.3 status 200 → B.1 para → B.2 remove → **B.3 restore exit 0** (`restore: ok — envelopes=1 eventos=0`) → **recusa com RK errada exit 1** (`o backup não pôde ser aberto com a chave do kit`) → sobe de novo → V.1/V.2 exit 0 → sessão nova 201 → V.4 `restore: passed` | PASS — script exit 0, `fails=0` (`docs/sdd/qa/QA-0056-runbook.log`); seção C (ciclo podman P-01) fora do "trecho de geração/restore" e não re-executada — ver unverified |
| AC-08 | Gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` exit 0 **na abertura e no fechamento** (627/627 nos dois); escopo lido do git: 16 arquivos do commit **TODOS** dentro dos `touches`, `git rev-list --count develop..HEAD = 1`, 0 `.csproj`/`.props`, 0 linha `<PackageReference>`/`<PackageVersion>` adicionada, resíduo backend 0 (tracked e untracked), blobs 6/6 idênticos ao `HEAD` após as mutações, mtime delta 0, nada alterado fora de `docs/`+`backend/` — **no Windows e no Linux** | PASS — gates exit 0 (logs `20261122-012756-test-gate.log` e `20261122-020227-test-gate.log`, cabeçalho `exit: 0` nos dois) + `qa-scope-task-0056.ps1` exit 0 e gêmeo `.sh` no contêiner exit 0 |

Nenhum AC ficou `NOT-COVERED`.

## Execuções (comando → exit → cauda)

Todas executadas nesta sessão, cwd `C:\dev\squid-squad-net\squid-execution`.

1. `powershell … sdd-test.ps1 test` → exit 0 → `Aprovado! – Com falha: 0, Aprovado: 627, Total: 627, Duração: 9 m 46 s` (gate de abertura; log `docs/sdd/runs/test-runs/20261122-012756-test-gate.log`, cabeçalho `exit: 0` — regra S-22: classifiquei sempre pelo log gravado, nunca pelo console)
2. `powershell … sdd-test.ps1 test -TestFilter Category=RestoreDrill` → exit 0 → `7/7, 10 s` (log `20261122-012954-test-filter.log`) — os 7 drills com `Trait Category=RestoreDrill`
3. `powershell … sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Backup` → exit 0 → `30/30, 1 m 27 s` (log `20261122-013243-test-filter.log`) — família inteira, baseline do DEV confirmada
4. `powershell … docs/sdd/qa/qa-mutate-task-0056.ps1 -VerifyOnly` → exit 0 → `VERIFY total=8 bad=0` (os 8 find strings batem exatamente 1× cada; ASCII limpo — `docs/sdd/qa/QA-0056-verify.log`)
5. `powershell … docs/sdd/qa/qa-mutate-task-0056.ps1` → exit 0 → 8 ciclos `EXPECT-OK`/`PROBE`, `RESTORE-OK hash=PRE residue=0` em todos, fecho `GREEN wrapper=0 logExit=0 aprovado=7 falha=0`, `SUMMARY cycles=8 failed=0 residue-backend=0 green-aprovado=7 elapsed-s=222` (`docs/sdd/qa/QA-0056-mutations.log`; logs de cada ciclo `20261122-013621-test-filter.log` … `20261122-013940-test-filter.log`)
6. `powershell … docs/sdd/qa/qa-runbook-task-0056.ps1` (1ª tentativa) → **exit 1** → `fails=4`: `policy_denied` em A.2/A.3/V.4 porque **o meu script** passou `--Instance:Operators=` vazio — o operador `+` dentro de `@()` tem precedência menor que a vírgula e o array partiu em 6 argumentos (provado com probe: `count=4`); bug do harness, não do runbook/produto (`docs/sdd/qa/QA-0056-runbook-attempt1.log`, preservado)
7. `powershell … docs/sdd/qa/qa-runbook-task-0056.ps1` (corrigido: `$opArg` fora do array) → **exit 0** → `SUMMARY fails=0` com todos os `OK`: register 201, envelope `Verified`, status 200, restore `exit 0`, recusa RK errada `exit 1`, health/ready exit 0, `restore: passed`, teardown limpo (`docs/sdd/qa/QA-0056-runbook.log`)
8. `powershell … docs/sdd/qa/qa-scope-task-0056.ps1` → exit 0 → `SUMMARY head=3fdfc69 commit-files=16 outside-touches=0 pkg-added=0 residue-backend=0 untracked-backend=0 blobs=6/6 mtime-delta=0 docs-unexpected=0 outside-tree=0 fails=0` (`docs/sdd/qa/QA-0056-scope.log`)
9. `podman run --rm -v <repo>:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash docs/sdd/qa/qa-scope-task-0056.sh` → exit 0 → `SUMMARY … outside-touches=0 pkg-added=0 residue-backend=0 blobs=6/6 mtime-delta=0 fails=0` (idêntico ao Windows; `docs/sdd/qa/QA-0056-scope-sh.log`)
10. `podman run … bash docs/sdd/qa/qa-linux-task-0056.sh` → `LINUX-BUILD-EXIT=0` e `LINUX-TEST-EXIT=0` → `Passed! - Failed: 0, Passed: 7, Total: 7, Duration: 4 s` — os 7 drills no Linux real (WSL2, SDK 10.0.403 = pin do CI) (`docs/sdd/qa/QA-0056-linux-build.log`, `docs/sdd/qa/QA-0056-linux-test.log`)
11. gate de fechamento (após todas as execuções, árvore restaurada): `powershell … sdd-test.ps1 test` → exit 0 → `Aprovado! – Com falha: 0, Aprovado: 627, Total: 627, Duração: 9 m 40 s` (log `docs/sdd/runs/test-runs/20261122-020227-test-gate.log`, cabeçalho `exit: 0`)

## Prova de que os testes podem falhar

O driver `docs/sdd/qa/qa-mutate-task-0056.ps1` aplicou 8 mutações — UM literal de código de produto por ciclo —, rodou o filtro dos 7 drills, classificou o vermelho PELO LOG (`exit:` do cabeçalho), exigiu que o teste do próprio AC tenha ficado vermelho, e restaurou com `git checkout` comparando o blob PRE vs POST (byte-exact) + bump de mtime (S-23):

| Ciclo | Escopo | Literal quebrado | Vermelho esperado (saiu) |
| --- | --- | --- | --- |
| M1 | AC-01 | `SqliteActivityStore.cs`: `restored = events.OrderBy(...).ToList()` → `.Skip(1)` | Ac01 ✓ (6 restores vermelhos — o passo 7 recusa) |
| M2 | AC-02 | `RestorePipeline.cs`: `if (accepted[^1].SeqTo != all[^1].SeqTo)` → `SeqTo < 0` | Ac02 ✓ (1 — a recusa do envelope Corrupt fora da cadeia) |
| M3 | AC-03 | `BackupFormatMigrations.cs`: `if (format == 0)` → `format >= 0` | Ac03 ✓ (1 — o formato 99 deixa de falhar) |
| M4 | AC-04 | `RestorePipeline.cs`: `if (envelope.KeyEpoch > currentEpoch)` → `KeyEpoch < 0` | Ac04 ✓ (1 — época sem chave é aceita) |
| M5 | AC-05 | `SqliteBackupStateSource.cs`: `ReadAsync(seqFrom - 1)` → `ReadAsync(seqFrom)` (**o bug original**) | Ac05 + Ac02 ✓ (2 — o evento da borda some) |
| M6 | AC-06 | `SqliteRestoreDestination.cs`: `since = await _projector.LastProjectedSeqAsync()` → `long.MaxValue` | Ac06 ✓ (1 — projeções não re-aplicadas) |
| M7 | §6/status | `SqliteBackupStatusSource.cs`: `drill is null ? null : $"{drill.Drill}: {drill.Result}"` → `null` | o teste da rota ✓ (1 — `restore: passed` some) |
| M8 | §4 passo 4 (PROBE) | `RestorePipeline.cs`: checagem de hash → `&& false` | **PROBE-GREEN** — nenhum drill falha (ver finding 1) |

Fecho: `GREEN … aprovado=7 falha=0` sobre a árvore restaurada, `residue-backend=0`, blobs 6/6 idênticos ao `HEAD`. Os cenários, portanto, não são decoração: cada AC vermelhou quando o comportamento quebrou.

## Os dois defeitos corrigidos pelo DEV — escopo confirmado

1. **Defeito real da borda do delta (janela inclusiva)** — `backend/src/Squid.Service/SqliteBackupStateSource.cs:39`. No `develop` o `SnapshotAsync` lia `activities.ReadAsync(seqFrom)` (exclusivo, `Seq > seqFrom`) enquanto o envelope do contrato §2 promete o intervalo **inclusivo** `[seqFrom..seqTo]` e o runner deriva `seqTo anterior + 1`: cada backup incremental perdia exatamente o evento da borda (um evento por run numa cadeia restaurada). A correção lê `seqFrom - 1` e fecha em `Seq <= seqTo`. **Dentro dos touches** (`backend/src/Squid.Service/**`); **carga real provada**: M5 (voltar ao literal antigo) derruba Ac05 e Ac02.
2. **Flake pré-existente de timestamp** — `backend/tests/Squid.Tests/Backup/BackupAdminRouteTests.cs:103-108`. No `develop` a asserção comparava `aged` como string crua contra o JSON; o serializador tira o zero final da fração de 7 dígitos do formato `O` (vermelho quando o 7º dígito era 0, ~1 em 10). A comparação passou a ser por instante. **Dentro dos touches** (`backend/tests/Squid.Tests/Backup/**`); a primeira execução do gate do DEV (`20261122-003535-test-gate.log`, 626/627) mostra exatamente `Ac03_ac04_ac05` como única falha — o flake é pré-existente da TASK-0055 (estava no `develop`), não introduzido aqui.

Escopo: os 16 arquivos do commit estão todos nos `touches` da task (`Squid.Worker/**`, `Squid.Service/**`, `Squid.Tests/Backup/**`, `docs/runbooks/restaurar-backup.md`) — `outside-touches=0` nas duas shells.

## Runbook AC-07 — cada passo EXECUTÁVEL executado

| Passo do runbook | Log do DEV (`20261122-runbook-restaurar-backup.log`) | Re-execução desta QA (`QA-0056-runbook.log`) |
| --- | --- | --- |
| A.1 sessão (register 201 + cookies) | `A1-register=201` | `OK A.1 register :: register=201` |
| A.2 `POST /api/admin/backup/run` | `A2-exit=0` envelope `Verified` | `OK A.2 … Verified=True seqFrom=True` |
| A.3 `GET /api/admin/backup/status` | `A3-exit=0` | `OK A.3 status … last_success_at=True envelopes_count=True` |
| B.1 parar o serviço | `servico parado` | `OK B.1` |
| B.2 `Remove-Item ./squid-grainstate.db` | `arquivo ausente … True` | `OK B.2 … True` |
| B.3 restore com a RK do kit | `EXIT 0 — restore: ok envelopes=1` | `OK … exit=0 restore: ok — envelopes=1 eventos=0 agregados=0 seqTo=0` |
| B.3 recusa com RK errada | `EXIT 1 — restore: falhou` | `OK … exit=1 o backup não pôde ser aberto com a chave do kit` |
| B.4 subir o serviço | `servico (2) PID=55204 health-ok=True` | `OK … PID=41688` |
| Verificação 1/2 (`/health`, `/ready`) | `V.1 exit=0`, `V.2 exit=0` | `OK V.1` + `OK V.2 exit=0` |
| Verificação 4 (sessão nova + status) | `register=201`, `restore: passed` | `OK V.4 sessao nova` + `OK … restore: passed` |
| Seção C (podman: down/create/cp/rm/up) e Verificação 3 (`podman volume ls`) | **não consta no log** | **não executado por esta QA** — stretch P-01 pré-existente (prova em `QA-0009-runbooks-iniciais.md:74`), fora do "trecho de geração/restore" que o AC-07 nomeia — unverified |

Nenhum passo do trecho de geração/restore ficou sem execução; nenhuma ocorrência de `PENDENTE` restou no runbook (a única é a nota "deixou de ser PENDENTE nesta task").

## Docs × realidade (adjudicação do ODEV conferida, não re-litigada)

- **`docs/runbooks/README.md` (linha do índice, atualizada pelo ODEV fora dos touches)**: status `**EXECUTÁVEL** (P-07, TASK-0056 — comandos executados e logados, ver runs/test-runs/20261122-runbook-restaurar-backup.log)` — **bate** com o cabeçalho do runbook (`status: EXECUTÁVEL`) e o log citado existe e contém as seções A/B/Verificação. Escopo descrito na linha ("gerar o backup cifrado e restaurar (log + projeções + ledger de cota)") = cobertura real do envelope.
- **SPEC-0012 §D / §Out-of-scope / GAP-55 (corrigidos em 2026-11-22) + linha Gaps da task**: a cobertura declarada (log de atividade + projeções + ledger de cota; contas/sessões/contêineres/membros/atores/objetos fora, re-registro/reabertura na Verificação 4; total = S-49) **bate** com o contrato §4 (passos 4-7) e com o código: `SqliteRestoreDestination.ReapplyAsync` reaplica exatamente `SqliteActivityStore.RestoreAsync` + projeção + `QuotaReconciler` — nada mais. `S-49` existe no `BACKLOG.md` (working tree do ODEV).
- **Runbook Verificação 4** ("contas e sessões não vêm do backup… abra uma sessão nova"): confirmado ao vivo — após o restore o register devolveu **201** (a conta não existia mais) e `backup.last_success_at/envelopes_count` voltaram `null/0` (o registro de envelopes não é restaurado, coerente com a cobertura).

## Findings

1. `backend/src/Squid.Worker/Backup/RestorePipeline.cs:204` — baixo — **pré-existente / não exigido por nenhum AC**: o passo 4 do contrato §4 (validação do hash do payload contra o `contentHash` do header) **não tem cenário can-fail** — a sonda M8 desligou a checagem e os 7 drills continuaram verdes (nenhum drill alimenta o restore com bytes divergentes: o `FlippingTarget` só deturpa a releitura, o `.bin` guardado é o original). Risco: uma divergência payload×header só seria barrada pelo estado `Corrupt`/seleção de cadeia, não pelo hash. Não bloqueia: nenhum AC da task pede esse caminho. Recomendação não-bloqueante: ODEV cortar um teste de recusa por hash na TASK-0057 ou saneamento próprio.
2. `docs/sdd/tasks/TASK-0056-restore-drills.md:119` — baixo — **pré-existente (linha do ODEV)**: a linha de `## Log` da adjudicação do `[DIVERGENCIA]` está **cortada no meio da frase** ("…a glossa ERRADA era minha (GAP-55 estado"). Seção append-only de propriedade do ODEV: a QA não edita — o ODEV deve acrescentar uma linha de correção. Não afeta o estado nem os ACs.
3. `docs/runbooks/restaurar-backup.md:26-27` — informativo — **pré-existente (texto do P-01, presente no `develop`)**: a nota "os dois formulários foram executados na prova (o de Linux, pela WSL)" não foi re-verificada nesta QA (o formulário Windows foi, hoje, na execução 7). Fora do escopo desta task.

Nenhum BLOCKER. Nenhum MAJOR. Nenhum AC violado.

## O que não ficou coberto (unverified)

- **Seção C do runbook (ciclo `podman compose down` → `create` → `cp` → `rm` → `up`) e Verificação 3 (`podman volume ls`) — `unverified`, nunca assumida verde.** Motivo: é o stretch P-01 pré-existente (prova histórica em `QA-0009-runbooks-iniciais.md:74`), texto não alterado por esta task, e subir a pilha de contêineres exige gate. Risco: o ciclo arquivo→volume não foi re-provado em P-07 — permanece com a prova de P-01.
- **Suíte Linux COMPLETA — `unverified`, nunca assumida verde.** Provado no Linux: build exit 0 (SDK 10.0.403 = pin do CI) + os 7 drills `Category=RestoreDrill` 7/7 exit 0 + o escopo git. Não rodado: a suíte inteira no Linux (falhas de ambiente pré-existentes conhecidas: `libfontconfig`/Skia, `cmd.exe` S-44 — memória bank `build-run-test.md:42`); instalar `libfontconfig1` exige gate indisponível nesta sessão.
- **Caminho do CI (`pwsh sdd-test.ps1 test` em ubuntu-latest) — `unverified`:** a imagem sdk não traz `pwsh` e instalar exige gate; executei o gêmeo `.sh`/`dotnet test` (o runner Linux disponível) com o mesmo filtro.
- **Envelope do spot-check do runbook com 0 eventos** — instância nova e vazia, exatamente como o proof do DEV (`eventos=0 agregados=0`); o roundtrip com dados reais é provado pelos drills (Ac01: 6 eventos, 100% dos hashes), não pelo caminho do runbook. Risco: baixo.
- **`restore-offsite`/S3 (TASK-0057) e rotação (TASK-0058)** — fora do escopo desta task (o 6º drill é da TASK-0057).

## Reproduzir

1. Pré-requisito: Windows com .NET 10 SDK, checkout da branch `task/TASK-0056-restore-drills` (`3fdfc69`).
2. Comando: `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`
3. Esperado: exit 0, `Aprovado: 627, Com falha: 0` (log novo em `docs/sdd/runs/test-runs/`); drills pontuais: mesmo comando com `-TestFilter Category=RestoreDrill` → `Aprovado: 7` (~15 s); prova can-fail: `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0056.ps1` → exit 0 com 8 ciclos e fecho `GREEN … aprovado=7`; runbook: `… qa-runbook-task-0056.ps1` → exit 0 (`fails=0`); escopo: `… qa-scope-task-0056.ps1` → exit 0 (Linux: `bash docs/sdd/qa/qa-scope-task-0056.sh` e `bash docs/sdd/qa/qa-linux-task-0056.sh` dentro do contêiner sdk → exit 0).
