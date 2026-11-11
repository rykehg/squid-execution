<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: CONCLUIDO
- owner: qa
- lastVerified: 2026-11-09 17:10:16

# QA-0023 — Migration versionada e projeções Dapper do feed (TASK-0024)

- task: TASK-0024
- spec: docs/sdd/specs/SPEC-0009-publicacao-feed-postagem.md (seção B + `Gaps` GAP-19/20/23), specs/001-squid-mvp/data-model.md §4/§5
- runId: R-20261109-01
- qaAttempt: 1/2
- verdict: PASSED

Execução, não leitura: gate de baseline **274/274 exit 0** (`20261109-160508`, `exit: 0` lido **dentro**
do log — S-22), os 8 testes novos do DEV **8/8** (`20261109-154055`, log do DEV conferido), os
**3 cenarios novos desta QA** fecham o filtro em **11/11 exit 0** (`20261109-161819`) e o gate final
rodou **277/277 exit 0** (`20261109-164555`) = 266 do baseline + 8 do DEV + 3 da QA.
Can-fail: **4 mutações (M1..M4) sobre 3 arquivos dentro dos `touches`**, com **4 rodadas vermelhas
de propósito** classificadas **pelo log gravado** (S-22), SHA256 **PRE** antes de cada uma (S-16),
restauração byte-exact + bump de mtime e hash = **PRE** depois (S-23), e verde logo em seguida —
**resíduo `QA-MUTATION` em `backend/` = 0**.
A costura mais arriscada (contador + append) tem prova própria: `QaTask0024Tests.Counter_and_log_…`
(`backend/tests/Squid.Tests/Projections/QaTask0024Tests.cs:45`) injeta a falha **depois** do
incremento — um trigger que aborta o **último** UPDATE do append (`:62-75`, o write da linha do log
vem depois do incremento em `SqliteActivityStore.cs:69-74`) — e mostra que o número **volta**
(`:79`), que nenhum `Seq` maior que o counter sobrevive no log (`:81`), que o retry recebe o
**mesmo** número (`:86`), que ele aparece numa **leitura delta** (`:90`) e que no fim
`counter == MAX(seq)` (`:93-94`). **M1** derruba exatamente a primeira dessas asserções.
AC-05 fecha no processo real: `qa-worker-probe-task-0024.ps1` sobe o **`Squid.Worker.exe`
construído** contra um banco preparado pelo `qa-worker-fixture-task-0024.py` (o **próprio script
versionado do repo**, byte a byte); controle negativo (`verify=1` **antes** do processo) e depois
**`PROBE verify PASS` em 4 s**: host sobe, o pump consome os 2 eventos **em ordem de Seq**, com a
linha de cada um **e só os membros `Active`** (o `Banned` não recebe) — `20261109-164624`,
exit 0. Escopo (AC-06): varredura de mtime **desde o dispatch do DEV (14:05)** = **22 arquivos de
produto, todos dentro dos `touches`**; `git status --porcelain -uall` sem nada fora deles além de
artefatos de fluxo e dos **7 artefatos desta QA**. Falhas pré-existentes: **NENHUM** (274 antes,
277 depois, nenhuma vermelha fora dos ciclos de mutação) — o único vermelho pré-existente
observado é o do **gêmeo `.sh` do gate** (finding 3), que não é desta task.

## What is proven

| AC | Scenario (executed) | Script | Result |
| --- | --- | --- | --- |
| AC-01 | `SchemaMigrationTests.Ac01_a_new_database_gets_every_table_and_the_second_apply_changes_nothing` (`backend/tests/Squid.Tests/Projections/SchemaMigrationTests.cs:20`): banco novo → `ApplyAsync` devolve `[1]` (`:27`); as 5 tabelas conferidas no `sqlite_master` (`:29-33`); `ix_timeline_item_seq` e `ix_home_timeline_seq` (`:36-39`); `schema_version` com `version=1`, `name=init_projections` e **checksum = SHA256 do texto que rodou** (`:42-47`); `activity_counter.last_seq=0` e `quota_ledger.limit_bytes=10737418240` (`:50-53`); **reaplicação** → `[]` e `COUNT(schema_version)=1` (`:56-58`). Também executado: recusa de texto alterado sob versão registrada (`:67-87`) e versão duplicada (`:90-111`). **M2** (índice `ix_home_timeline_seq` comentado no script) vermelha **em AC-01** (`SchemaMigrationTests.cs:38`) | filtro xunit + 1 mutação | PASS |
| AC-02 | `ActivityProjectorTests.Ac02_a_create_writes_the_post_summary_and_one_home_line_per_active_member` (`ActivityProjectorTests.cs:33`): função chamada **direto** (`:76`); `timeline_item` com Seq global, Object.Seq, id, autor `{origin}/users/{handle}`, conteúdo, `depth 0`, `reply_to_id null`, visibilidade **congelada** e contadores 0 (`:78-96`); `home_timeline` = **só** owner + membro `Active` (`:99-106`), `Invited` e `Banned` de fora (`:107-108`). **Borda da emenda SPEC B (2026-11-09 15:50:43)**, executada: `QaTask0024Tests.A_member_who_leaves_before_the_projection_runs_gets_no_line…` (`QaTask0024Tests.cs:98`) — membro `Active` na publicação → escrito para **`Left` entre o evento e a projeção** (`:128-137`) → projeção direta (`:138`) → a linha do post existe (`:141-143`) e o `home_timeline` = **só o owner** (`:148-149`). **M3** (`State == Active` → `!= Banned`) vermelha **em AC-02** (`ActivityProjectorTests.cs:103`) **e** na borda (`QaTask0024Tests.cs:148`) | filtro xunit + 1 mutação | PASS |
| AC-03 | `ProjectionOffWritePathTests.Ac03_publishing_projects_nothing_until_the_pump_runs_then_the_lines_appear` (`ProjectionOffWritePathTests.cs:26`): fixture nova → 0 linhas (`:29-30`); publicar → **201** (`:39`) e as duas contagens seguem **0** (`:49-52`); o pump executado pelo teste é o que faz as linhas aparecerem (`:60-71`). Reforço executado aqui: no probe de processo o **controle negativo** (`PROBE negative-control ok … verify=1`) prova que sem o pump nada é projetado num banco já populado (`20261109-164624-qa-worker-probe.log`) | filtro xunit + sonda de processo | PASS |
| AC-04 | `ProjectionPumpTests.Ac04_a_pass_failing_midway_resumes_at_the_next_event_and_never_duplicates_a_line` (`ProjectionPumpTests.cs:98`): falha no **2º** evento (`:128-131`) → só 1 linha (`:133`); passa nova reexecuta e continua de `events[1]` (`:137-143`) → 3 linhas; 3ª passa consome **0** (`:146-147`). A cláusula **"não duplica"** tinha apenas prova indireta (o high-water pula eventos projetados) — fechada aqui: `QaTask0024Tests.Projecting_the_same_event_a_second_time_leaves_exactly_the_same_lines` (`QaTask0024Tests.cs:153`) entrega o **mesmo evento duas vezes** (`:179`, `:190`) e as linhas continuam **1 e as mesmas**. **M4** (remover o `ON CONFLICT` do `InsertTimeline`) vermelha **só aqui** (`ActivityProjector.cs:124`, `QaTask0024Tests.cs:190`) | filtro xunit + 1 mutação | PASS |
| AC-05 | (a) `ProjectionPumpTests.Ac05_building_the_host_applies_the_migration_before_any_request_or_test_runs` (`ProjectionPumpTests.cs:39`): `SquidServiceHost.Build` num arquivo **novo** → as 5 tabelas + 1 linha de `schema_version` (`:49-58`); (b) `Ac05_the_pump_is_a_worker_hosted_service_and_consumes_the_log_in_seq_order` (`:69`): tipo é `IHostedService` e mora no assembly `Squid.Worker` (`:71-73`), e a passa consome **exatamente** o delta lido, em ordem de Seq (`:83-94`). A cláusula de **composição** (`Squid.Worker/Program.cs` registra o hosted service) **não tinha teste**: fechada no processo real — `docs/sdd/qa/qa-worker-probe-task-0024.ps1` → `PROBE verify PASS` (2 eventos, seq 1 e 2, inseridos nessa ordem, só membros Active), `20261109-164624`, **exit 0**. Gêmeo `.sh` executado e classificado **exit 3 = ambiente** (`20261109-164701`: `no dotnet on PATH`) | filtro xunit + sonda de processo (ps1) + gêmeo sh (ambiente) | PASS |
| AC-06 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` → **`20261109-164555-test-gate.log` com `exit: 0` e `Com falha: 0, Aprovado: 277, Total: 277`**, lido dentro do log (S-22); baseline `20261109-160508` = **274/274 `exit: 0`**. Escopo: varredura de mtime desde **14:05** → **22 arquivos de produto, todos dentro dos `touches`**; `git status --porcelain -uall` sem mudança fora deles além de artefatos de fluxo e dos 7 desta QA; resíduo `QA-MUTATION` em `backend/` = **0**; hash final = **PRE** nos 3 arquivos mutados | gate + git/mtime/hash + `sdd-state.ps1 check` | PASS |

## Mutation inventory — every can-fail, with its SHA256 and its red log

| # | Site (path:L) | Mutation | Red run → assertion that broke |
| --- | --- | --- | --- |
| M1 | `backend/src/Squid.Service/SqliteActivityStore.cs:118` (`NextSeqAsync`) | `… + 1 WHERE id = 1` → `… + 2 WHERE id = 1 -- QA-MUTATION` | `20261109-161942` (exit 1, 1 falha) → `QaTask0024Tests.cs:54` (`Assert.Equal(counter0 + 1, seed.Seq)`) |
| M2 | `backend/src/Squid.Persistence/schema/0001_init_projections.sql:58` | `CREATE INDEX … ix_home_timeline_seq …` → comentário | `20261109-162426` (exit 1, 1 falha) → `SchemaMigrationTests.cs:38` (índice ausente) |
| M3 | `backend/src/Squid.ReadModels/Projections/ActivityProjector.cs:142` | `State == MembershipState.Active` → `State != MembershipState.Banned // QA-MUTATION` | `20261109-163035` (exit 1, **2 falhas**) → `ActivityProjectorTests.cs:103` + `QaTask0024Tests.cs:148` |
| M4 | `backend/src/Squid.ReadModels/Projections/ActivityProjector.cs:34` (`InsertTimeline`) | `ON CONFLICT (container_id, seq) DO NOTHING` → comentário | `20261109-163658` (exit 1, 1 falha) → `QaTask0024Tests.cs:190` (constraint em `ActivityProjector.cs:124`) |

SHA256 **PRE** (S-16), antes de cada mutação: `SqliteActivityStore.cs = 40D877435A6D43F25456B87B78CB2F369221DB94D7969C0F9271B6D3FD0C96E8` ·
`0001_init_projections.sql = C82191A0162B3E6ECF3DA38F27FE403995C9C066D10B73C459B7134669CE1855` ·
`ActivityProjector.cs = 254C24BCE354B54B67F99A5DC43400188E88A5444968135829E028A34F43FDF1`.
Depois de cada restauração: hash conferido **= PRE**, mtime **bumpado** (S-23) e verde em seguida
(`162257`, `162815`, `163334`, `163920` — todos `exit: 0`, 11/11). Resíduo `QA-MUTATION` em `backend/` = **0**.

### DEV decisions verified (not disputed — executed)

| Decision | Evidence |
| --- | --- |
| Script único `0001_init_projections.sql` + `SchemaMigrator` criando/checkeando `schema_version` com SHA-256: **o applier** cria a tabela de versão, **o script** cria as 4 tabelas (leitura do AC-01 do dispatch) | `SchemaMigrator.cs:34-41` (ledger criado **antes** do 1º script), `:97-121` (script + registro na **mesma transação**), `:83-95` (checksum conferido; texto mudou → recusa). Executado em AC-01 (`SchemaMigrationTests.cs:26-58`) + recusa (`:67-87`) + varredura de `schema_version` no probe |
| Contador global movido para `activity_counter` com UPDATE atômico de linha única **na mesma transação** do append; `IActivityStore` sem mudança de contrato | `SqliteActivityStore.cs:59-76` (uma transação cobre read → increment → write), `:113-130` (`UPDATE … MAX(last_seq,@Floor)+1` + `SELECT`), `:168-172` (CAS da linha do log); interface `IActivityStore.cs:32-42` **não aparece** na varredura de mtime desde 14:05 (arquivo da TASK-0023, intocado). Executado por `QaTask0024Tests.cs:45-95` e derrubado por **M1** |
| Pump lê o mesmo log pela leitura Dapper de `Squid.ReadModels` (regra de dependência: Worker/ReadModels não alcançam as stores de `Squid.Api`; `Worker→Service` fecharia ciclo) | `SqliteActivityLog.cs:39-56` (mesma row `activities`, `Seq > afterSeq`), `ActivityProjectionPump.cs:42-54`; executado em AC-05(b) e no **probe de processo** — o Worker lê o documento gravado e projeta as duas linhas |
| Único pacote novo no repo = `Dapper 2.1.89` pinado em `Directory.Packages.props` (gate humano 2026-11-09) | `backend/Directory.Packages.props:17`; `git status --porcelain` lista **este único** `.props` de build modificado e a varredura de mtime desde 14:05 mostra nenhum outro arquivo de build |
| Ordem dev: `Squid.Service` aplica a migration no init e o `Squid.Worker` sobe depois | `SquidServiceHost.cs:93` (`SchemaMigrations.Apply` antes de qualquer request) executado por AC-05(a) em arquivo novo; o Worker é só leitura + projeção (o probe **pré-aplica o mesmo script do repo** pelo fixture — ver "Not covered") |

## Scripts

| Script | Language / runner | What it does |
| --- | --- | --- |
| `backend/tests/Squid.Tests/Projections/QaTask0024Tests.cs` | xunit (runner da casa) | os 3 cenarios novos desta QA: costura contador/append, borda de membership na projeção e re-projeção idempotente |
| `backend/tests/Squid.Tests/Projections/*.cs` (6 arquivos do DEV) | xunit | os 8 testes do DEV, executados em cada gate (274) e em cada filtro (11) |
| `docs/sdd/_tools/sdd-test.ps1 test [-TestFilter …]` | pwsh + dotnet | runner da casa: 1 baseline, 10 filtros (2 verdes iniciais + 4 vermelhos de mutação + 4 verdes de restauração), 2 gates |
| `docs/sdd/qa/qa-worker-fixture-task-0024.py` | python3 (std lib) | prepara o banco (script versionado do repo + `GrainState` + log de 2 eventos + 1 documento de membership) e **grava** o que o pump escreveu, artefato contra artefato |
| `docs/sdd/qa/qa-worker-probe-task-0024.ps1` + `.sh` | pwsh / bash | sobem o `Squid.Worker` (exe construído / `dotnet` no gêmeo) contra esse banco, com espera limitada de 90 s e limpeza; saída `exit 0` só com o controle negativo + `PROBE verify PASS` |
| `docs/sdd/qa/qa-check-task-0024.ps1` + `.sh` | powershell 5.1 / bash | os 17 checks: gates pelo **log** (S-22), SHA256 = PRE (S-16), mtime (S-23), resíduos, touches, facts, probe, doc e `sdd-state check` |
| `docs/sdd/_tools/sdd-state.ps1 append-log / append-evidence / check` | pwsh | escrita das linhas de `## Log` e `## Evidence` e o `check` final |
| `docs/sdd/_tools/sdd-test.sh test` (executado via Git Bash) | bash | gêmeo Linux do gate: recusa com `no *.sln found` e **`test: exit 1`** (a solution é `*.slnx`) — plataforma não coberta por aqui (finding 3) |

## How to reproduce

Prerequisites: Windows com .NET SDK 10, repositório em `C:\dev\squid-squad-net\squid-execution`
(sem rede depois do restore). Sem servidor próprio, sem banco compartilhado, sem instalação.

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test   # expected: exit 0, 277/277 (log novo em docs/sdd/runs/test-runs/)
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Projections   # expected: exit 0, 11/11
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-worker-probe-task-0024.ps1   # expected: exit 0, "PROBE verify PASS"
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0024.ps1          # expected: exit 0, SUMMARY total=17 fails=0
```

## Last run

| Date/Time | Command | Exit | Output (max 10 lines) |
| --- | --- | --- | --- |
| 2026-11-09 16:05:08 | `… sdd-test.ps1 test` (baseline) | 0 | `exit: 0` · `Aprovado! – Com falha: 0, Aprovado: 274, Total: 274` (2 m 51 s) |
| 2026-11-09 16:15:00 | `… -TestFilter FullyQualifiedName~Squid.Tests.Projections` (com 2 QA) | 0 | `Aprovado! – Com falha: 0, Aprovado: 10, Total: 10` |
| 2026-11-09 16:18:19 | `… -TestFilter …Projections` (3 QA) | 0 | `Aprovado! – Com falha: 0, Aprovado: 11, Total: 11` |
| 2026-11-09 16:19:42 | `… -TestFilter …Projections` (**mutação M1**) | 1 | `Com falha: 1` → `QaTask0024Tests.Counter_and_log…:line 54` |
| 2026-11-09 16:22:57 | `… -TestFilter …Projections` (M1 restaurado, hash=PRE) | 0 | `Aprovado: 11` |
| 2026-11-09 16:24:26 | `… -TestFilter …Projections` (**mutação M2**) | 1 | `Com falha: 1` → `SchemaMigrationTests.Ac01…:line 38` |
| 2026-11-09 16:28:15 | `… -TestFilter …Projections` (M2 restaurado) | 0 | `Aprovado: 11` |
| 2026-11-09 16:30:35 | `… -TestFilter …Projections` (**mutação M3**) | 1 | `Com falha: 2` → `ActivityProjectorTests.Ac02…:103` + `QaTask0024Tests.A_member_who_leaves…:148` |
| 2026-11-09 16:33:34 | `… -TestFilter …Projections` (M3 restaurado) | 0 | `Aprovado: 11` |
| 2026-11-09 16:36:58 | `… -TestFilter …Projections` (**mutação M4**) | 1 | `Com falha: 1` → `QaTask0024Tests.Projecting_the_same_event…:190` (`ActivityProjector.cs:124`) |
| 2026-11-09 16:39:20 | `… -TestFilter …Projections` (M4 restaurado) | 0 | `Aprovado: 11` |
| 2026-11-09 16:45:55 | `… sdd-test.ps1 test` (gate final) | 0 | `exit: 0` · `Aprovado! – Com falha: 0, Aprovado: 277, Total: 277` (2 m 59 s) |
| 2026-11-09 16:46:24 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-worker-probe-task-0024.ps1` | 0 | `PROBE negative-control ok (verify=1 …)` · `PROBE verify log_seqs=[1, 2] timeline_seqs=[1, 2] active=2 banned=1` · `PROBE PASS` |
| 2026-11-09 16:47:01 | `wsl bash -lc "… bash docs/sdd/qa/qa-worker-probe-task-0024.sh"` | 3 | `PROBE environment: no dotnet on PATH in this shell - the Linux path stays unverified here` |
| 2026-11-09 16:56:43 | `bash docs/sdd/_tools/sdd-test.sh test` (Git Bash) | 1 | `sdd-test: no *.sln found within depth 3` · `MSB1003` · `test: exit 1 (real test failure)` — pré-existente (finding 3) |
| 2026-11-09 17:06:13 | `Get-FileHash SHA256 (3 mutados) + varredura QA-MUTATION + varredura de mtime desde 14:05` | 0 | `3/3 = PRE` · `RESIDUE QA-MUTATION = 0` · `SWEEP = 22` · `SIDEEFFECT = 0` |
| 2026-11-09 17:08:39 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0024.ps1` | 0 | `SUMMARY total=17 fails=0` · `RESULT: PASS` (17 checks: gates pelo log, PRE, resíduos, touches, probe, doc) |
| 2026-11-09 17:09:25 | `wsl bash -lc "bash docs/sdd/qa/qa-check-task-0024.sh"` (gêmeo do check) | 0 | `SUMMARY total=17 fails=0` · `RESULT: PASS` — os 17 ids fecham iguais nas duas plataformas |
| 2026-11-09 17:10:16 | `… qa-check-task-0024.ps1` (reconfirmação após os `## Log`/`## Evidence`) | 0 | `STATE-CHECK check: OK - log entries 15, evidence rows 24` · `RESULT: PASS` |

## Findings

| # | Severity | Where | What breaks | Evidence |
| --- | --- | --- | --- | --- |
| 1 | INFO (borda observada — conforme a emenda, não é defeito) | `backend/src/Squid.ReadModels/Projections/ActivityProjector.cs:90-95` | Membro que sai **entre a publicação e a projeção** perde a linha da timeline pessoal: a membership é lida **no momento da projeção** (emenda da SPEC B, 2026-11-09 15:50:43), e o payload do evento não carrega snapshot. Comportamento **esperado pela spec** e agora executado (`QaTask0024Tests.cs:128-149`) — o mesmo caso já está como `S-34` no backlog (escrito pelo odev às 15:53:19) | `QaTask0024Tests.cs:148-149` (só o owner recebe) · `docs/sdd/ideas/BACKLOG.md` linha `S-34` |
| 2 | MINOR (redação do AC, para ODEV) | `docs/sdd/tasks/TASK-0024-migracao-projecoes-feed.md:58-60` (AC-02) | O AC ainda diz "membros **ativos na hora do evento**", que a emenda da SPEC B (15:50:43) substituiu por "no momento da **projeção**". Testei **a spec**, não o texto do AC (o dispatch determina a leitura normativa); se o AC for lido literalmente, a borda do finding 1 seria vermelha. O texto do AC não foi alterado por esta QA | `SPEC-0009:64-67` (emenda) vs `TASK-0024:59`; executado em `QaTask0024Tests.cs:98-149` |
| 3 | MINOR (pré-existente, não é desta task) | `docs/sdd/_tools/sdd-test.sh:20-21` | O gêmeo Linux do gate procura só `*.sln` e a solution é `*.slnx` → `no *.sln found within depth 3`, depois `MSB1003` e **`test: exit 1 (real test failure)`** (deveria ser ambiente/exit 3): **a plataforma Linux não tem gate executável neste repo**, e a classificação ainda engana (S-22/S-22 sh). Mesmo estado do baseline — nada desta task quebrou ali | `docs/sdd/runs/test-runs/20261109-165643-sdd-test-sh.log` (`exit: 1` + `test: exit 1`) · QA-0022 finding 2 · `build-run-test.md:42` |
| 4 | INFO (pré-existente / fluxo, adjudicada) | `docs/sdd/plan/plan.md`, `docs/sdd/ideas/BACKLOG.md` | Únicos arquivos fora dos `touches` no `git status`: `plan.md` tem mtime **11:34:54**, anterior ao dispatch do DEV (14:05), já adjudicado na QA-0022; `BACKLOG.md` mudou às **15:53:19** com a linha `S-34`, escrita pelo **odev** na adjudicação da emenda (o `## Log` da task a registra às 15:53:21). Nenhum dos dois é escrita de produto desta task | varredura de mtime desde 14:05 (22 arquivos, todos em `touches`) + `git diff docs/sdd/ideas/BACKLOG.md` (só `S-34` + `lastVerified`) |

## Not covered, and why

- **Linux / WSL / container** — o único distro do WSL desta máquina é `podman-machine-default` e
  `dotnet` não existe nele (o probe `.sh` sai **exit 3, ambiente**); instalar SDK exige gate humano,
  que esta QA não tem. O gêmeo do gate além disso não acha a solução (`*.slnx`, finding 3).
  **`unverified`** — nunca assumido verde; a única evidência Linux é o run histórico do CI
  (98/98, 2026-11-07, anterior a esta task). Risco: comportamento diferente de SQLite/Orleans/Dapper
  em Linux passaria despercebido.
- **Ordem de subida `Squid.Service` → `Squid.Worker` em duas processos reais** — a aplicação da
  migration **no init** está provada por AC-05(a) (`Build` em arquivo novo), mas o probe de processo
  **pré-aplica o mesmo script do repo** pelo fixture em vez de subir o `Squid.Service` primeiro.
  Risco: baixo (o Worker não escreve schema — só lê `GrainState` e grava as projeções).
- **Dois writers disputando o mesmo arquivo SQLite** — a atomicidade contador/append é provada
  **sequencialmente** (falha injetada depois do incremento + retry); execução concorrente entre
  processos não foi exercitada (o `busy_timeout` de 30 s e a transação imediata protegem, não
  testado aqui). Risco: um `SQLITE_BUSY` fora do timeout abortaria um append sem consumir seq
  (comportamento desejado), mas não foi medido.
- **Eventos que não são `Create`** (`Delete`/demais) — `ActivityProjector.cs:76` os ignora de
  propósito; projeção de remoção é a TASK-0030. Não testado.
- **Mudança de membership durante o próprio lote do pump** (a janela de segundos) — só a borda
  *antes* da projeção foi testada; uma membership que vira durante um lote em andamento cai na
  mesma leitura "no momento da projeção" e não tem teste próprio.
- **Contadores `Counts` (likes/replies/shares) refletindo reações** — nascem zerados por design
  (parked idea do ODEV, fase dona); aqui só se provou que nascem `0` (`SchemaMigrationTests.cs:50`).

## memoryBankFlags

- `[GAP: memory bank absent]` **não confere** — `docs/memory-bank/index.md` existe e foi lido
  (rota `qa-integracao`: `build-run-test.md`, `integrations.md`, `contracts.md`).
- `[DIVERGENCIA: a memoria bank (build-run-test.md:42) registra exit 3 para docs/sdd/_tools/sdd-test.sh;
  observado exit 1 com "no *.sln found within depth 3" — source: docs/sdd/runs/test-runs/20261109-165643-sdd-test-sh.log]`
  — mesma divergência já proposta na QA-0022, re-observada nesta tentativa.
- `[MUDANCA_RELEVANTE: ARQUITETURA — primeira migration versionada do projeto (ledger
  `schema_version` com SHA-256 criado pelo applier, aplicada no init do SquidServiceHost) e contador
  global de `Activity.Seq` em tabela single-row dentro da MESMA transação do append, com as
  projeções Dapper lendo o log fora do caminho de escrita — source:
  backend/src/Squid.Persistence/schema/SchemaMigrator.cs:29-125, backend/src/Squid.Service/SqliteActivityStore.cs:59-130]`
  — proposta de consolidação; quem verifica é o ODEV (o `## Log` da task já registra as flags
  `ARQUITETURA`/`PADRAO_NOVO` da sessão do DEV).
