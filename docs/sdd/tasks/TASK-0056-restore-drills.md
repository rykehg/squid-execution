<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0056 — Restore e 5 drills no gate + runbook restaurar-backup

```yaml
runId: R-20261120-01
taskId: TASK-0056
status: DONE
agent: odev
phase: P-07
sequence: 2
createdAt: 2026-11-20 21:50:58
updatedAt: 2026-11-20 21:50:58
parent: SPEC-0012
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Worker/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/Backup/**", "docs/runbooks/restaurar-backup.md"]
blocks: ["TASK-0057"]
blockedBy: ["TASK-0055"]
inputArtifacts: ["docs/sdd/specs/SPEC-0012-backup-rotação-p07.md", "specs/001-squid-mvp/contracts/backup-recovery.md", "docs/runbooks/restaurar-backup.md"]
outputArtifacts: ["backend/tests/Squid.Tests/Backup/**", "docs/runbooks/restaurar-backup.md", "docs/sdd/qa/QA-*"]
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
nextAction: DONE - ODEV mergeia em develop e commita docs (gate modelo C); proxima entrega TASK-0057
evidence: []
memoryBankFlags: []
```

## Intent

SPEC-0012 §D (SC-009): o fluxo de restore do contrato §4 (decifra → valida hash → aplica
migrações de `format` → reaplica ledger/projeções → compara contagens e hashes → `/ready`)
mais os cinco drills bloqueantes no gate — `restore-roundtrip` (100% hashes idênticos),
`restore-interrupted`, `restore-schema-old`, `restore-wrong-key`, `backup-incremental` — e o
runbook `restaurar-backup.md` de `PARCIAL` para **EXECUTÁVEL**. O sexto drill
(`restore-offsite`) é da TASK-0057, que precisa do target S3.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `restore-roundtrip`: criar dados → backup → apagar o diretório → restore → **100% dos
  hashes idênticos** por agregado (SC-009) — teste `Category=RestoreDrill` no gate.
- AC-02: `restore-interrupted`: matar o backup no meio → rodar de novo — nenhum envelope
  `Corrupt` é aceito e o retomar parte do `seqTo` íntegro — teste.
- AC-03: `restore-schema-old`: envelope com `format` anterior → migração aplicada; formato
  incompatível → falha explícita, nunca silenciosa — teste.
- AC-04: `restore-wrong-key`: `keyEpoch` sem chave correspondente no kit → falha clara e
  **nada é destruído** (banco de destino intacto) — teste.
- AC-05: `backup-incremental`: backup 1 → N escritas → backup 2 contém **só o delta**
  (`SeqFrom` = seqTo anterior + 1) — teste.
- AC-06: após o restore, ledger de cota e projeções conferem (contagens e hashes por agregado) e
  `/ready` responderia verde — teste.
- AC-07: runbook `docs/runbooks/restaurar-backup.md` está **EXECUTÁVEL**: cada passo do trecho
  de geração/restore tem comando real verificado (nada de `PENDENTE`, nenhum passo não
  executado) — revisão + evidência de execução dos comandos do runbook.
- AC-08: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1
  test` exit 0 (drills incluídos); nenhum arquivo alterado fora dos `touches`; zero pacote novo.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Worker/**` — pipeline de restore (contrato §4)
- `backend/src/Squid.Service/**` — wiring do restore
- `backend/tests/Squid.Tests/Backup/**` — os 5 drills (`Category=RestoreDrill`)
- `docs/runbooks/restaurar-backup.md` — de PARCIAL para EXECUTÁVEL (passos provados)
- S3/`restore-offsite` é TASK-0057; rotação é TASK-0058
- produto fora dos `touches`: qualquer lacuna é `D-01` para o ODEV

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-55 | no | cobertura do envelope = a do contrato §4 (log de atividade + projeções + ledger de cota, provada pelos drills); mídia e estado direto de contas/sessões/contêineres/membros/atores/objetos fora — re-registro/reabertura no runbook Verificação 4; redação anterior ("estado íntegro") corrigida pelo ODEV em 2026-11-22 após o flag [DIVERGENCIA] do DEV; cobertura total = S-49 (decisão de escopo do humano) |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-20 21:50:58 | odev | task created from SPEC-0012 (restore §4 + 5 drills no gate + runbook EXECUTAVEL; blockedBy TASK-0055)
- 2026-11-21 22:15:04 | odev | dispatch DEV (runId R-20261120-01, sequencia 1 -> 2, branch task/TASK-0056-restore-drills desde develop; encadeamento automatico - TASK-0055 DONE com QA-0055 PASSED 7/7 e merge dd48cf6/4c217a2 em develop)
- 2026-11-22 00:26:15 | dev | feasibilidade 1/2 fechada: unitario e callers mapeados (pipeline, runner, state source, status, coordinator, rotas), touches confirmado sem acrescimo - portas de restore declaradas no Worker e implementadas no Service, nenhum arquivo fora dos touches; baseline da familia Backup 23/23 exit 0 (runs/test-runs/20261121-225811-test-filter.log); nenhuma duvida bloqueante - decisoes registradas: ledger de drill como uma linha no GrainState (sem migration nova, porque AC-08 proibe arquivo fora dos touches), formato 0 = payload pre-janela com instanceId e events, epochs do kit = 1..CurrentEpoch, entrypoint do restore = modo --restore do Squid.Service
- 2026-11-22 00:26:15 | dev | branch task/TASK-0056-restore-drills criada a partir de develop antes do codigo
- 2026-11-22 00:26:15 | dev | restore implementado (contrato 4): RestorePipeline com selecao de cadeia (Corrupt nunca aceito, buraco = recusa explicita), validacao de hash, epoch do kit, migracao de formato, apply e digest por agregado; SqliteRestoreDestination prepara o schema, restaura o log com Seq preservado, reaplica projecoes e reconcilia a quota; SqliteActivityStore.RestoreAsync novo; BackupFormatMigrations (0 -> 1; incompativel -> falha explicita); AggregateDigests
- 2026-11-22 00:26:15 | dev | defeito real exposto pelos drills e corrigido na raiz: SnapshotAsync lia Seq > seqFrom (exclusivo) enquanto NextSeqFromAsync devolvia seqTo anterior + 1 - cada backup incremental perdia o evento da borda (AC-02 e AC-05 nasceram vermelhos); a janela agora e inclusiva (ReadAsync de seqFrom - 1) e envelope e payload voltam a cobrir o mesmo intervalo do contrato 2
- 2026-11-22 00:26:15 | dev | ledger de drills: SqliteRestoreDrillLedger grava restore/latest no GrainState so quando o restore passa na verificacao; SqliteBackupStatusSource passa a ler as duas colunas - GET /api/admin/backup/status respondeu restore: passed ao vivo depois de um restore (evidencia abaixo)
- 2026-11-22 00:26:15 | dev | 5 drills + AC-06 + a rota de status escritos em backend/tests/Squid.Tests/Backup/RestoreDrillTests.cs com Trait Category=RestoreDrill; FlippingTarget do BackupDoubles passa a repassar a costura S-47 (no produto o alvo real e o escritor do header), que e o estado Corrupt que o restore le; familia Backup 30/30 exit 0
- 2026-11-22 00:26:15 | dev | shortcut mantido em SqliteBackupStateSource: a leitura dedicada de seq atual so faz sentido com a janela incremental de R-15 (full semanal), que esta task nao implementa - nenhuma AC pede, o marcador continua no codigo com o motivo
- 2026-11-22 00:26:15 | dev | runbook restaurar-backup.md de PARCIAL para EXECUTAVEL: secao A (sessao, backup/run e status), secao B (restore pelo binario direto, exit 0; recusa com RK errada, exit 1) e Verificacao 4 - todos os comandos executados nesta sessao; dotnet run foi descartado porque roda com CWD de projeto (criou o banco em backend/src/Squid.Service)
- 2026-11-22 00:26:15 | dev | nota para o ODEV: o envelope cobre o log de atividade (cobertura da TASK-0054) - contas, sessoes, membros e objetos nao voltam pelo restore; o runbook avisa que a sessao precisa ser reaberta e o indice docs/runbooks/README.md ainda marca PARCIAL (fora dos touches)
- 2026-11-22 00:41:33 | dev | flake pre-existente da TASK-0055 corrigido para o gate fechar: BackupAdminRouteTests comparava last_success_at como string crua e o escritor JSON tira o zero final da fracao de 7 digitos do formato O (vermelho quando o 7o digito era 0, ~1 em 10); a comparacao passou a ser por instante - primeira execucao do gate da task: 626/627 so com essa falha, filtro apos o ajuste 3/3 exit 0
- 2026-11-22 01:13:50 | odev | ODEV DONE do step result registrado: restore §4 + 7 drills Category=RestoreDrill verdes, runbook EXECUTAVEL com comandos executados, gate 627/627 exit 0, 16 arquivos nos touches, zero pacote novo, commit 3fdfc69; defeito de borda do delta (evento perdido no Seq exclusivo) corrigido na raiz pelo DEV; flake de timestamp pre-existente da TASK-0055 corrigido no caminho
- 2026-11-22 01:13:50 | odev | ODEV: adjudicacao do flag [DIVERGENCIA] do DEV - escada nivel especificacao: o contrato §4 publicado define a cobertura (log de atividade + projecoes + ledger de cota) e o codigo o implementa; a glossa ERRADA era minha (GAP-55 estado
- 2026-11-22 02:04:06 | qa | QA-0056 1/2: PASSED - AC-01..AC-08 provados por execucao; 7/7 drills Category=RestoreDrill no Windows e no Linux (container local sdk 10.0.403); 8 mutacoes can-fail vermelhas restauradas byte-exact (M8 probe verde = passo 4 do contrato §4 sem cenario can-fail, finding nao-bloqueante); runbook A/B/Verificacao re-executados pela QA (restore exit 0, recusa com RK errada exit 1, status restore: passed); escopo 16/16 nos touches e zero pacote novo; gates 627/627 exit 0 na abertura e no fechamento
- 2026-11-22 02:17:43 | odev | CORRECAO: a linha de log do ODEV anterior terminou truncada ("...GAP-55 estado"); texto completo: a glossa errada era "restore devolve estado integro do SQLite"; a correcao e: cobertura = contrato 4 (log + projecoes + ledger de cota), redacao corrigida na SPEC-0012 e nesta task em 2026-11-22; linha antiga nao e editada (secao append-only)
- 2026-11-22 02:17:43 | odev | QA-0056 PASSED 8/8 (qaAttempt 1/2): AC-01..AC-08 todos PASS; 7/7 drills Category=RestoreDrill no Windows E no Linux; 8 mutacoes can-fail restauradas byte-exact (blob 6/6, GREEN 7/7); gates 627/627 exit 0 na abertura e no fechamento; runbook A/B/Verificacao re-executado pela QA (restore exit 0, recusa RK errada exit 1, status restore: passed); escopo 16/16 nos touches, zero pacote novo; commit 07abc0b na branch (1 commit QA, sem push)
- 2026-11-22 02:17:43 | odev | QA-0056: achados - 1 baixo produto (RestorePipeline.cs:204 - passo 4 do contrato 4 validacao payload x header sem cenario can-fail, M8 verde; nenhum AC exige -> S-50), 1 pre-existente doc (runbook :26-27 claim de curl Linux via WSL do P-01 nao re-verificada -> S-51), 1 pre-existente de fluxo (linha truncada do ODEV, corrigida acima); adjudicacao da cobertura verificada ao vivo pela QA (register 201 pos-restore); memoryBankFlag contracts.md:18,23 desatualizado ("ainda sem handler") -> consolidar no fecho da fase
- 2026-11-22 02:17:43 | odev | ODEV: transicao AWAITING_QA -> DONE; merge em develop + commit docs conforme gate modelo C; encaminha TASK-0057

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-22 00:54:51 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Backup | 0 | docs/sdd/runs/test-runs/20261121-225811-test-filter.log baseline pre-codigo 23/23 |
| 2026-11-22 00:55:26 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Backup | 0 | docs/sdd/runs/test-runs/20261121-235357-test-filter.log familia Backup 30/30 apos os 7 drills |
| 2026-11-22 00:55:27 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 0 | build exit 0 sem erro de compilacao |
| 2026-11-22 00:55:27 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 1 | docs/sdd/runs/test-runs/20261122-003535-test-gate.log gate 626/627 - 1 flake pre-existente de timestamp, corrigido em seguida |
| 2026-11-22 00:55:28 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Backup.BackupAdminRouteTests | 0 | docs/sdd/runs/test-runs/20261122-004028-test-filter.log 3/3 apos comparar last_success_at por instante |
| 2026-11-22 00:55:28 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261122-005036-test-gate.log gate final 627/627 exit 0 |
| 2026-11-22 00:55:29 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter Category=RestoreDrill | 0 | docs/sdd/runs/test-runs/20261122-005121-test-filter.log 7/7 drills com Trait Category=RestoreDrill |
| 2026-11-22 00:55:29 | dev | dotnet build backend/Squid.slnx | 0 | docs/sdd/runs/test-runs/20261122-runbook-restaurar-backup.log pre-condicao do runbook |
| 2026-11-22 00:55:30 | dev | powershell -NoProfile -File task0056-runbook-a.ps1 (bloco A.1-A.3 do runbook: register, POST backup/run, GET backup/status) | 0 | docs/sdd/runs/test-runs/20261122-runbook-restaurar-backup.log A1=201 envelope Verified A3=200 |
| 2026-11-22 00:55:30 | dev | dotnet backend/src/Squid.Service/bin/Debug/net10.0/Squid.Service.dll --restore --Storage:Path=./squid-grainstate.db --Backup:Local:Path=./backups | 0 | docs/sdd/runs/test-runs/20261122-runbook-restaurar-backup.log restore: ok envelopes=1 |
| 2026-11-22 00:55:31 | dev | dotnet backend/src/Squid.Service/bin/Debug/net10.0/Squid.Service.dll --restore --Storage:Path=./squid-grainstate.db --Backup:Local:Path=./backups | 1 | docs/sdd/runs/test-runs/20261122-runbook-restaurar-backup.log recusa com RK errada: restore: falhou |
| 2026-11-22 00:55:31 | dev | curl.exe -fsS http://localhost:8080/health | 0 | docs/sdd/runs/test-runs/20261122-runbook-restaurar-backup.log Verificacao V.1 exit 0 |
| 2026-11-22 00:55:32 | dev | curl.exe -fsS http://localhost:8080/ready | 0 | docs/sdd/runs/test-runs/20261122-runbook-restaurar-backup.log Verificacao V.2 exit 0 sobre o banco restaurado |
| 2026-11-22 00:55:32 | dev | curl.exe -sS http://127.0.0.1:8080/api/admin/backup/status (sessao nova apos o restore) | 0 | docs/sdd/runs/test-runs/20261122-runbook-restaurar-backup.log V.4 restore.last_drill_result=restore: passed |
| 2026-11-22 02:05:34 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261122-012756-test-gate.log gate de abertura 627/627 exit 0 |
| 2026-11-22 02:05:35 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter Category=RestoreDrill | 0 | docs/sdd/runs/test-runs/20261122-012954-test-filter.log 7/7 drills Category=RestoreDrill exit 0 |
| 2026-11-22 02:05:35 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Backup | 0 | docs/sdd/runs/test-runs/20261122-013243-test-filter.log familia Backup 30/30 exit 0 |
| 2026-11-22 02:05:36 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0056.ps1 -VerifyOnly | 0 | docs/sdd/qa/QA-0056-verify.log total=8 bad=0 |
| 2026-11-22 02:05:56 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0056.ps1 | 0 | docs/sdd/qa/QA-0056-mutations.log 8 ciclos failed=0 green 7/7 M8 probe verde restaurado byte-exact |
| 2026-11-22 02:05:57 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-runbook-task-0056.ps1 (1a tentativa) | 1 | docs/sdd/qa/QA-0056-runbook-attempt1.log bug do harness (Instance:Operators vazio por parse do array) - nao do runbook |
| 2026-11-22 02:05:57 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-runbook-task-0056.ps1 | 0 | docs/sdd/qa/QA-0056-runbook.log A.1-A.3 B.1-B.4 V.1 V.2 V.4 fails=0 - restore exit 0 e recusa RK errada exit 1 |
| 2026-11-22 02:05:58 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-scope-task-0056.ps1 | 0 | docs/sdd/qa/QA-0056-scope.log fails=0 - 16/16 arquivos nos touches, zero pacote novo, blobs 6/6 |
| 2026-11-22 02:06:22 | qa | podman run --rm -v <repo>:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash docs/sdd/qa/qa-scope-task-0056.sh | 0 | docs/sdd/qa/QA-0056-scope-sh.log fails=0 - mesmo escopo no Linux |
| 2026-11-22 02:06:23 | qa | podman run --rm -v <repo>:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash docs/sdd/qa/qa-linux-task-0056.sh | 0 | docs/sdd/qa/QA-0056-linux-build.log build exit 0 + QA-0056-linux-test.log 7/7 drills no Linux |
| 2026-11-22 02:06:23 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261122-020227-test-gate.log gate de fechamento 627/627 exit 0 |
