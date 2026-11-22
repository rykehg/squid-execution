<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0055 — BackupCoordinator, agendador e rotas admin de backup

```yaml
runId: R-20261120-01
taskId: TASK-0055
status: DONE
agent: odev
phase: P-07
sequence: 2
createdAt: 2026-11-20 21:50:58
updatedAt: 2026-11-20 21:50:58
parent: SPEC-0012
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Grains/**", "backend/src/Squid.Worker/**", "backend/src/Squid.Api/Admin/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/Backup/**"]
blocks: ["TASK-0056"]
blockedBy: ["TASK-0054"]
inputArtifacts: ["docs/sdd/specs/SPEC-0012-backup-rotação-p07.md", "specs/001-squid-mvp/contracts/backup-recovery.md"]
outputArtifacts: ["backend/tests/Squid.Tests/Backup/**", "docs/sdd/qa/QA-*"]
interviewRounds: 2/6
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
nextAction: DONE - ODEV mergeia em develop e commita docs (gate modelo C); proxima entrega TASK-0056
evidence: []
memoryBankFlags: []
```

## Intent

SPEC-0012 §C: `BackupCoordinatorGrain` (por `instanceId`, lê o último seq consolidado e pede o
snapshot ao agregado — o Worker serializa), agendador `Backup:IntervalMinutes` (default 1440),
`POST /api/admin/backup/run` e `GET /api/admin/backup/status` do operador, com
`backup.last_success_at` alertando > 24 h e `backup.verify_failures` alertando na hora
(contrato §6). Sobe sobre o núcleo da TASK-0054.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `BackupCoordinatorGrain` roda o fluxo do contrato §3 por `instanceId` (seq consolidado
  → snapshot → Worker serializa → cifra → envia → verifica → registra `BackupEnvelope`) — teste.
- AC-02: agendador dispara backup sozinho após `Backup:IntervalMinutes` (default 1440; teste usa
  valor baixo) sem nenhuma request — teste.
- AC-03: `POST /api/admin/backup/run` com sessão de operador dispara backup imediato e devolve o
  envelope registrado; sem sessão `401`, sessão fora de `Instance:Operators` `403` — teste.
- AC-04: `GET /api/admin/backup/status` devolve `backup.last_success_at`,
  `backup.envelopes_count`, `backup.bytes_total`, `backup.verify_failures`,
  `restore.last_drill_at`, `restore.last_drill_result` (contrato §6) — teste.
- AC-05: `last_success_at` mais antigo que 24 h produz alerta (log/métrica) e
  `verify_failures` incrementa e alerta quando um envelope é marcado `Corrupt` — teste.
- AC-06: falha do target (ex.: caminho do disco indisponível) não derruba o processo nem
  corrompe o backup local — o coordinator reporta erro e o serviço segue servindo — teste.
- AC-07: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1
  test` exit 0; nenhum arquivo alterado fora dos `touches`; zero pacote NuGet novo.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Grains/**` — `BackupCoordinatorGrain` (ADR-35: sem referência a storage)
- `backend/src/Squid.Worker/**` — pump/agendador do backup
- `backend/src/Squid.Api/Admin/**` — rotas `backup/run` e `backup/status` (regra de operador existente)
- `backend/src/Squid.Service/**` — wiring do grain, do agendador e das rotas
- `backend/tests/Squid.Tests/Backup/**` — testes dos ACs
- drills são TASK-0056; S3 é TASK-0057; `redundancy.score` é TASK-0060
- produto fora dos `touches`: qualquer lacuna é `D-01` para o ODEV

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-53 | no | agendador `Backup:IntervalMinutes` (1440) + `POST /api/admin/backup/run`; alerta `last_success_at` > 24 h — default aceito pelo humano 2026-11-20 21:07:26 |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-20 21:50:58 | odev | task created from SPEC-0012 (coordinator + agendador 1440 + rotas run/status do operador; blockedBy TASK-0054)
- 2026-11-21 19:40:30 | odev | dispatch DEV (runId R-20261120-01, sequencia 1 -> 2, branch task/TASK-0055-coordinator-agendador-rotas desde develop; encadeamento automatico autorizado pelo humano 2026-11-21 19:38:38, blockedBy TASK-0054 DONE)
- 2026-11-21 21:03:26 | dev | feasibility 1/2: unit, callers e touches confirmados - BackupCoordinatorGrain e classe simples em Squid.Grains (ADR-35: sem Orleans, sem storage) sobre as ports IBackupStateSource/IBackupRunner/IBackupStatusSource; Worker fica com runner, agendador e leitor de status; rotas em Squid.Api/Admin reusam o Gate existente; wiring em SquidServiceHost; D-01 (qual RK embrulha a DK e qual keyEpoch) fechado por SPEC-0012 secao A + secao E (RK = Squid:RecoveryKey, epoch do ledger key_epoch); baseline Squid.Tests.Backup 10/10 exit 0
- 2026-11-21 21:03:26 | dev | implementacao concluida: Grains/Backup com BackupCoordinatorGrain (seq consolidado -> snapshot -> Worker), BackupInstance e as 3 ports; Worker/Backup com BackupRunner (seqFrom descoberto via ListAsync, epoch via CurrentEpochAsync, RK via IBackupKeySource), SqliteBackupStatusSource (contagem 6 do contrato), BackupOptions (Backup:IntervalMinutes default 1440, <=0 ocioso) e BackupScheduler IHostedService que dispara sozinho sem request; Api/Admin com ConfiguredBackupKeySource, BackupAlerter e as rotas POST /api/admin/backup/run + GET /api/admin/backup/status atras do Gate (401/403); Service com SqliteBackupStateSource (janela de Activity.Seq) e os registros no host
- 2026-11-21 21:03:26 | dev | S-47 e S-48 corrigidos enquanto estavam no escopo: LocalDiskTarget passa a implementar IEnvelopeStateWriter e o pipeline regrava o header depois da releitura (so metadata, ciphertext nao e reenviado, estado pos-verificacao no arquivo); escrita do .json via tmp+move e ListAsync tolerante a header ilegivel (pula em vez de lancar); 2 testes AC-style novos comprovam os dois
- 2026-11-21 21:03:26 | dev | teste: 13 testes novos em backend/tests/Squid.Tests/Backup cobrem AC-01..AC-06 e S-47/S-48; gate final sdd-test.ps1 test 620/620 exit 0 (607 de baseline + 13), zero pacote NuGet novo e nenhum arquivo de produto fora dos touches
- 2026-11-21 21:04:49 | dev | commit unico bdf1ccf na branch task/TASK-0055-coordinator-agendador-rotas (25 arquivos, todos nos touches: Grains/Backup, Worker/Backup, Api/Admin, Service, tests/Backup; sem push, rebase ou force, main intocada, nenhum branch apagado); docs do ODEV (plan, specs, tasks) ficaram fora do commit, como na TASK-0037
- 2026-11-21 21:07:02 | odev | ODEV: verificacao do step result DONE - logLines/evidenceRows ja gravados pelo DEV via sdd-state (5+5), check exit 0; gate 620/620 exit 0, 13 testes novos, 0 pacote novo, 0 arquivo fora dos touches
- 2026-11-21 21:07:02 | odev | ODEV: D-01 (qual RK embrulha a DK / origem do keyEpoch) respondida na especificacao (SPEC-0012 A+E: Squid:RecoveryKey + MAX(key_epoch)); feasibilityRounds 1/2
- 2026-11-21 21:07:02 | odev | ODEV: notDone AC-04 (restore.last_drill_* null ate a TASK-0056 criar os drills) aceito como estado honesto - os 6 campos do contrato 6 sao devolvidos, drills populam depois; DEV tambem registrou 3 parkedIdeas (snapshot semanal R-15, parse duplicado de Squid:RecoveryKey, compose sem chaves Backup) e a config Backup:InstanceId (default default) nao nomeada na SPEC - QA julga
- 2026-11-21 21:07:02 | odev | ODEV: transicao IN_DEVELOPMENT -> AWAITING_QA, dispatch QA-0055
- 2026-11-21 22:09:04 | qa | QA-0055 inicio: entrada pela task AWAITING_QA (qaAttempt 0/2), spec SPEC-0012 secao C + contrato backup-recovery 3 e 6 lidos, memory bank rota qa-integracao (3 arquivos); estado testado = branch task/TASK-0055-coordinator-agendador-rotas commit bdf1ccf (1 commit sobre develop 8469915)
- 2026-11-21 22:09:04 | qa | gate de abertura: sdd-test.ps1 test exit 0, 620/620 em 9m30s (log docs/sdd/runs/test-runs/20261121-212318-test-gate.log, cabecalho exit: 0 - regra S-22: classificado pelo log gravado, nunca pelo console)
- 2026-11-21 22:09:05 | qa | prova can-fail: 11 mutacoes de 1 literal de produto (AC-01..AC-06 + S-47 + S-48 em 2 ciclos) todas EXPECT-OK, restore byte-exact blob=8/8, GREEN final 23/23 exit 0 (docs/sdd/qa/QA-0055-mutations.log); VerifyOnly 11/11 find hits=1
- 2026-11-21 22:09:05 | qa | escopo AC-07: qa-scope-task-0055.ps1 exit 0 e gemeo .sh no Linux exit 0 (2a execucao; a 1a falhou por git dubious ownership no contenedor = ambiente, corrigido com safe.directory por env) - 25 arquivos do commit nos touches, 1 commit sobre develop, 0 linha PackageReference nova, git status -- backend vazio, residuo 0, blob 8/8, mtime 8
- 2026-11-21 22:09:06 | qa | Linux real (podman, mcr.microsoft.com/dotnet/sdk:10.0 = 10.0.403 pin do CI): build exit 0 e FAMILIA BACKUP 23/23 exit 0 (hosts reais + tick de 1 min do AC-02); suite completa Linux segue unverified (51 falhas de ambiente pre-existentes na QA-0054)
- 2026-11-21 22:09:06 | qa | achados nao-bloqueantes: 3 - pre-existente S-22 re-observado (ciclo M10: wrapper imprimiu 3 para vermelho real, SafeFileHandle casa o substring handle; log exit: 1 classificou), baixo-doc Backup:InstanceId nao enumerada na SPEC-0012:156 (julgada detalhe de implementacao aceitavel do instanceId do contrato 3, nao desvio - ODEV enumerar no fecho) e informativo atalho shortcut declarado em SqliteBackupStateSource com upgrade TASK-0056+; nenhum BLOCKER, nenhum AC NOT-COVERED
- 2026-11-21 22:09:07 | qa | gate de fechamento apos todas as execucoes: sdd-test.ps1 test exit 0, 620/620 em 9m35s (log docs/sdd/runs/test-runs/20261121-220634-test-gate.log, cabecalho exit: 0)
- 2026-11-21 22:09:07 | qa | verdito QA-0055: PASSED 7/7 ACs + S-47/S-48 provados - QA doc em docs/sdd/qa/QA-0055-coordinator-agendador-rotas.md; suite completa Linux verde segue unverified (ambiente)
- 2026-11-21 22:14:21 | odev | QA-0055 PASSED 7/7 (qaAttempt 1/2): AC-01..AC-07 todos PASS; 11 mutacoes can-fail EXPECT-OK com restore byte-exact (blobs 8/8) e GREEN 23/23; S-47 provado por M8, S-48 por M9 (leitor tolerante) e M10 (tmp+move); gates 620/620 exit 0 na abertura e no fechamento (cabecalho do log, regra S-22); familia Backup 23/23 no Linux real (podman sdk:10.0.403)
- 2026-11-21 22:14:21 | odev | QA-0055: julgamentos - Backup:InstanceId = detalhe de implementacao do instanceId do contrato 3 (nao desvio; chave ja enumerada na SPEC-0012 pelo ODEV); AC-04 = 6 campos do contrato 6 presentes, restore.last_drill_* null honesto ate a TASK-0056; achados: 1 pre-existente (S-22 re-observado), 1 documento (InstanceId, resolvido), 1 informativo (shortcut declarado em SqliteBackupStateSource com upgrade TASK-0056+)
- 2026-11-21 22:14:21 | odev | ODEV: commit gate QA e9105df na branch (1 commit, sem push); transicao AWAITING_QA -> DONE; merge em develop + commit docs conforme gate modelo C
- 2026-11-21 22:14:21 | odev | ODEV: memoryBankFlags - MUDANCA_RELEVANTE CONTRATO (Backup:InstanceId) ja resolvida na SPEC-0012 tabela de inputs; PADRAO_NOVO header pos-verificacao + tmp+move (LocalDiskTarget); ARQUITETURA ports em Squid.Grains implementadas nas raizes (ADR-35 sem aresta nova)

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-21 21:03:52 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Backup (baseline pre-edicao) | 0 | docs/sdd/runs/test-runs/20261121-201605-test-filter.log |
| 2026-11-21 21:03:53 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Backup (1a execucao: 3 falhas de teste, operador so fazia login sem registro) | 1 | docs/sdd/runs/test-runs/20261121-204608-test-filter.log |
| 2026-11-21 21:03:53 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Backup (23/23 apos correcao) | 0 | docs/sdd/runs/test-runs/20261121-205111-test-filter.log |
| 2026-11-21 21:03:54 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate AC-07: suite inteira 620/620) | 0 | docs/sdd/runs/test-runs/20261121-210128-test-gate.log |
| 2026-11-21 21:03:54 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 0 | NENHUM - console |
| 2026-11-21 22:09:36 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate de abertura, suite inteira 620/620) | 0 | docs/sdd/runs/test-runs/20261121-212318-test-gate.log |
| 2026-11-21 22:09:36 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0055.ps1 -VerifyOnly (11 find strings hits=1, ASCII) | 0 | docs/sdd/qa/QA-0055-verify.log |
| 2026-11-21 22:09:37 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0055.ps1 (11 mutacoes can-fail EXPECT-OK + GREEN 23/23) | 0 | docs/sdd/qa/QA-0055-mutations.log |
| 2026-11-21 22:09:37 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-scope-task-0055.ps1 (escopo AC-07 no Windows) | 0 | docs/sdd/qa/QA-0055-scope.log |
| 2026-11-21 22:09:38 | qa | podman run --rm -v C:/dev/squid-squad-net/squid-execution:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash docs/sdd/qa/qa-linux-task-0055.sh (Linux: build + familia Backup 23/23) | 0 | docs/sdd/qa/QA-0055-linux-test.log |
| 2026-11-21 22:09:38 | qa | podman run --rm -v C:/dev/squid-squad-net/squid-execution:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash docs/sdd/qa/qa-scope-task-0055.sh (1a tentativa: git dubious ownership no contenedor) | 1 | NENHUM - console, ambiente |
| 2026-11-21 22:09:39 | qa | podman run --rm -v C:/dev/squid-squad-net/squid-execution:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash docs/sdd/qa/qa-scope-task-0055.sh (2a apos safe.directory por env) | 0 | docs/sdd/qa/QA-0055-scope-sh.log |
| 2026-11-21 22:09:39 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate de fechamento 620/620) | 0 | docs/sdd/runs/test-runs/20261121-220634-test-gate.log |
