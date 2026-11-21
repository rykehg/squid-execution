<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0054 — Núcleo do backup cifrado

```yaml
runId: R-20261120-01
taskId: TASK-0054
status: DONE
agent: odev
phase: P-07
sequence: 2
createdAt: 2026-11-20 21:50:58
updatedAt: 2026-11-20 21:50:58
parent: SPEC-0012
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Persistence/schema/**", "backend/src/Squid.Api/Crypto/**", "backend/src/Squid.Worker/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/Backup/**"]
blocks: ["TASK-0055", "TASK-0058"]
blockedBy: []
inputArtifacts: ["docs/sdd/specs/SPEC-0012-backup-rotação-p07.md", "specs/001-squid-mvp/contracts/backup-recovery.md", "specs/001-squid-mvp/data-model.md"]
outputArtifacts: ["backend/src/Squid.Persistence/schema/0010_*.sql", "backend/tests/Squid.Tests/Backup/**", "docs/sdd/qa/QA-*"]
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
nextAction: DONE - ODEV mergeia em develop e commita docs (gate modelo C); proxima entrega TASK-0055
evidence: []
memoryBankFlags: []
```

## Intent

Base da P-07 (SPEC-0012 §A/§B): migration `0010_*` com `backup_envelope`/`key_epoch`
(data-model §2.10), contrato `IBackupTarget` + `LocalDiskTarget` sobre volume configurado, e o
pipeline de cifra na origem na ordem obrigatória cifrar → hash → enviar → verificar
(ChaCha20-Poly1305, `DK` por backup, `HKDF(RK,"fed/backup-kek")`). Sem coordinator, agendador,
drills ou S3 — esses vêm nas tasks seguintes.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: migration `0010_*.sql` idempotente cria `backup_envelope` e `key_epoch` com as colunas
  do data-model §2.10 (`KeyEpoch` obrigatória em todo envelope) e é aplicada pelo mecanismo de
  migration existente — teste.
- AC-02: `IBackupTarget` implementa `PutAsync`/`GetAsync`/`ListAsync`/`ExistsAsync`/`DeleteAsync`
  (contrato §1) e `LocalDiskTarget` opera sobre `Backup:Local:Path`; `ListAsync` devolve os
  envelopes do destino — teste.
- AC-03: `PutAsync` recebe só ciphertext + envelope; nenhum byte de plaintext ou de chave chega
  ao destino (varredura dos bytes gravados no disco de teste) — teste.
- AC-04: ordem obrigatória do contrato §3: cifra → `contentHash` (SHA-256 do ciphertext) →
  envio → releitura e conferência do hash → `verifiedAt`; envelope com hash divergente após
  releitura fica `Corrupt` — teste.
- AC-05: cada backup usa `DK` aleatória própria embrulhada por `HKDF(RK,"fed/backup-kek")`;
  dois backups do mesmo estado geram ciphertexts diferentes (IV/`DK` distintos) e ambos decifram
  com a mesma `RK` — teste.
- AC-06: envelope persistido registra `Id`, `KeyEpoch`, `SeqFrom`/`SeqTo`, `ContentHash`,
  `SizeBytes`, `TargetId`, `CreatedAt` (§2.10) — teste.
- AC-07: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1
  test` exit 0; nenhum arquivo alterado fora dos `touches`; **zero pacote NuGet novo**.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Persistence/schema/**` — migration `0010_backup.sql` (liberada teste/dev/CI)
- `backend/src/Squid.Api/Crypto/**` — `HKDF("fed/backup-kek")` + embrulho de `DK` (família do `KeyDerivation` existente)
- `backend/src/Squid.Worker/**` — pipeline serializa → comprime → cifra → hash (Worker serializa, nunca o scheduler — contrato §3)
- `backend/src/Squid.Service/**` — registro/wiring do target e do caminho de escrita
- `backend/tests/Squid.Tests/Backup/**` — testes dos ACs
- `Squid.Grains` **não** entra aqui (coordinator é TASK-0055); S3 é TASK-0057; drills são TASK-0056
- produto fora dos `touches`: qualquer lacuna é `D-01` para o ODEV

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-55 | no | blobs de mídia fora do envelope (SPEC-0012 §Out of scope) — backup cobre o estado do SQLite |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-20 21:50:58 | odev | task created from SPEC-0012 (base da P-07: schema 0010 + IBackupTarget/LocalDiskTarget + cifra na origem; zero pacote novo)
- 2026-11-21 10:17:10 | odev | dispatch DEV (runId R-20261120-01, sequencia 1 -> 2, branch task/TASK-0054-nucleo-backup-cifrado desde develop)
- 2026-11-21 11:07:09 | dev | feasibility 1/2: unit, callers e touches confirmados (migration 0010 em schema/, HKDF fed/backup-kek em Api/Crypto, nucleo em Worker/Backup, wiring em Service por raiz->raiz permitida pela regra); D-01 (onde vive a DK embrulhada) fechado por SPEC-0012 secao A (relê o header) + AC-05 (ambos decifram com a mesma RK); baseline gate 597/597 exit 0
- 2026-11-21 11:30:37 | dev | implementacao concluida: 0010_backup.sql (backup_envelope + key_epoch, colunas 2.10, key_epoch NOT NULL), Wrap/UnwrapBackupKey com HKDF fed/backup-kek em KeyDerivation, Worker/Backup com IBackupTarget, LocalDiskTarget, BackupPipeline (serializa-comprime-cifra-hash-envia-relê-registra) e SqliteBackupEnvelopeStore, wiring Backup:Local:Path em SquidServiceHost via ProjectReference Service->Worker (raiz->raiz permitida), 10 testes novos em tests/Squid.Tests/Backup
- 2026-11-21 11:40:40 | dev | gate final AC-07: suite inteira 607/607 aprovados, exit 0 (baseline 597 + 10 novos de Backup), zero pacote NuGet novo e nenhum arquivo de produto fora dos touches
- 2026-11-21 11:43:08 | dev | commit unico 28dcab9 na branch task/TASK-0054-nucleo-backup-cifrado (15 arquivos, todos nos touches; sem push, main intocada, nenhum branch apagado); docs do ODEV (plan, BACKLOG, specs, tasks) intocados
- 2026-11-21 11:45:14 | odev | DEV DONE (feasibility 1/2, correction 0/2): migration 0010_backup.sql (backup_envelope + key_epoch, colunas data-model 2.10), Wrap/UnwrapBackupKey HKDF fed/backup-kek, Worker/Backup (IBackupTarget, LocalDiskTarget, BackupPipeline ordem obrigatoria, SqliteBackupEnvelopeStore), wiring Backup:Local:Path em SquidServiceHost via ProjectReference Service->Worker (raiz->raiz permitida), 10 testes novos
- 2026-11-21 11:45:14 | odev | DEV DONE: D-01 (onde vive a DK embrulhada) fechado na especificacao (SPEC-0012 secao A + AC-05 + contrato 1) - feasibilityRounds 1/2
- 2026-11-21 11:45:14 | odev | DEV DONE: gate final 607/607 exit 0 (baseline 597 + 10 novos Backup), zero pacote NuGet novo, nenhum arquivo de produto fora dos touches
- 2026-11-21 11:45:14 | odev | DEV DONE: commit unico 28dcab9 na branch task/TASK-0054-nucleo-backup-cifrado (15 arquivos nos touches; sem push, main intocada, nenhum branch apagado)
- 2026-11-21 11:45:14 | odev | DEV DONE: parkedIdeas - compose/infra nao declara Backup:Local:Path (fora dos touches; default backups/ cobre dev/test; considerar chave no compose ainda na P-07)
- 2026-11-21 11:45:14 | odev | DEV DONE: memoryBankFlags - MUDANCA_RELEVANTE ARQUITETURA (Squid.Service referencia Squid.Worker pela primeira vez, source Squid.Service.csproj:16); PADRAO_NOVO (destino guarda <id>.bin ciphertext||tag + <id>.json envelope, JSON escrito por ultimo como marcador, source LocalDiskTarget.cs:36)
- 2026-11-21 11:45:14 | odev | ODEV: transicao IN_DEVELOPMENT -> AWAITING_QA, dispatch QA-0054
- 2026-11-21 11:48:24 | odev | CORRECAO do ODEV: a append-evidence minha duplicou as 3 linhas que o DEV ja tinha gravado (mesmos comandos/artefatos, timestamps meus 11:45); linhas 11:45 removidas - as linhas 11:07/11:30/11:40 do DEV sao as canonicas; nenhum comando rodou 2x
- 2026-11-21 13:04:30 | qa | QA-0054 inicio: entrada pela task AWAITING_QA (qaAttempt 0/2), specs SPEC-0012 A/B + contrato backup-recovery 1-3 + data-model 2.10 lidos, memory bank rota qa-integracao (3 arquivos); estado testado = branch task/TASK-0054-nucleo-backup-cifrado commit 28dcab9 (1 commit sobre develop 1e0cd33)
- 2026-11-21 13:04:30 | qa | gate de abertura: sdd-test.ps1 test exit 0, 607/607 em 8m52s (log docs/sdd/runs/test-runs/20261121-120707-test-gate.log, cabecalho exit: 0 - regra S-22: classificado pelo log, nunca pelo console)
- 2026-11-21 13:04:30 | qa | prova can-fail: 8 mutacoes de 1 literal de produto (AC-01..AC-06) todas EXPECT-OK, restore byte-exact blob=5/5 e GREEN final 10/10 exit 0 (docs/sdd/qa/QA-0054-mutations.log); 1a tentativa do driver abortou por git fora do PATH ANTES de qualquer mutacao
- 2026-11-21 13:04:30 | qa | escopo AC-07: qa-scope-task-0054.ps1 exit 0 e o gemeo .sh no Linux exit 0 - 15 arquivos do commit nos touches, 0 linha PackageReference nova, Directory.Packages.props intocado, git status -- backend vazio, residuo 0, blob 5/5, mtime 5
- 2026-11-21 13:04:30 | qa | Linux real (podman, mcr.microsoft.com/dotnet/sdk:10.0 = 10.0.403 pin do CI): build exit 0; suíte 556/607 exit 1 com 51 falhas 100% pre-existentes (29 Skia/libfontconfig + 9 asserts a jusante, todas presentes no log pre-task 20261114-142204 + 13 cmd.exe S-44; delta TwoSiloFacetTests.Ac01 = TASK-0047 b953771 em develop); FAMILIA BACKUP NO LINUX 10/10 exit 0
- 2026-11-21 13:04:30 | qa | achados nao-bloqueantes: 4 - pre-existentes S-11 (sdd-test.sh:21 find_sln nao propaga o exit 1) e S-22 (sdd-test.ps1:80 gatilho novo SafeKeyHandle + sumario pt-BR Com falha!), 2 produto baixos em LocalDiskTarget (header do destino sempre status Pending/verifiedAt null; header nao atomica rasgada derruba ListAsync em vez de pular); nenhum BLOCKER, nenhum AC NOT-COVERED
- 2026-11-21 13:04:30 | qa | gate de fechamento apos todas as execucoes: sdd-test.ps1 test exit 0, 607/607 em 8m48s (log docs/sdd/runs/test-runs/20261121-130327-test-gate.log, cabecalho exit: 0)
- 2026-11-21 13:04:30 | qa | verdito QA-0054: PASSED 7/7 - QA doc em docs/sdd/qa/QA-0054-nucleo-backup-cifrado.md; suíte completa LINUX verde segue unverified (ambiente, gate de libfontconfig indisponivel)
- 2026-11-21 13:19:27 | odev | QA-0054 PASSED 7/7 (qaAttempt 1/2): AC-01..AC-07 todos PASS; can-fail provado com 8 mutacoes de literal de produto, cada uma vermelha no proprio cenario, restauradas byte-exact (blob 5/5) e GREEN final 10/10; gates 607/607 exit 0 na abertura e no fechamento (lido pelo cabecalho do log, regra S-22)
- 2026-11-21 13:19:27 | odev | QA-0054: escopo AC-07 provado nas duas shells (15 arquivos do commit nos touches, 0 PackageReference nova, Directory.Packages.props intocado); familia Backup 10/10 verde no Linux real (podman sdk:10.0 = pin do CI); suite Linux COMPLETA fica unverified (51 falhas de ambiente pre-existentes: 29 libfontconfig + 9 asserts a jusante + 13 BuildProof S-44, todas no log pre-task 20261114-142204)
- 2026-11-21 13:19:27 | odev | QA-0054: 4 achados nao-bloqueantes - 2 pre-existentes (S-11 sdd-test.sh find_sln; S-22 sdd-test.ps1 com gatilho NOVO SafeKeyHandle+Com

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-21 11:07:09 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261121-105301-test-gate.log |
| 2026-11-21 11:30:37 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Backup | 0 | docs/sdd/runs/test-runs/20261121-112954-test-filter.log |
| 2026-11-21 11:40:41 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261121-113946-test-gate.log |
| 2026-11-21 13:05:09 | qa | bash docs/sdd/qa/qa-scope-task-0054.sh (Linux podman-machine) | 0 | docs/sdd/qa/QA-0054-scope-sh.log |
| 2026-11-21 13:05:09 | qa | bash docs/sdd/_tools/sdd-test.sh test (Linux, sem target - S-11) | 1 | docs/sdd/qa/QA-0054-sh-default-probe.log |
| 2026-11-21 13:05:10 | qa | podman run sdk:10.0 bash docs/sdd/qa/qa-linux-task-0054.sh (build + suíte Linux) | 1 | docs/sdd/qa/QA-0054-linux-test.log |
| 2026-11-21 13:05:10 | qa | podman run sdk:10.0 bash docs/sdd/qa/qa-linux2-task-0054.sh (classificação das 51 falhas Linux) | 1 | docs/sdd/runs/test-runs/20261121-154231-test-linux-full.log |
| 2026-11-21 13:05:11 | qa | podman run sdk:10.0 bash docs/sdd/qa/qa-linux2-task-0054.sh (filtro Backup Linux) | 0 | docs/sdd/qa/QA-0054-linux-backup-filter.log |
| 2026-11-21 13:10:22 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261121-120707-test-gate.log |
| 2026-11-21 13:10:22 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0054.ps1 -VerifyOnly | 0 | docs/sdd/qa/QA-0054-verify.log |
| 2026-11-21 13:10:22 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0054.ps1 (1a tentativa, git fora do PATH) | 1 | NENHUM - aborto antes de qualquer mutacao, git status -- backend vazio confirmado |
| 2026-11-21 13:10:22 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0054.ps1 | 0 | docs/sdd/qa/QA-0054-mutations.log |
| 2026-11-21 13:10:22 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-scope-task-0054.ps1 (1a execucao, falso positivo core.quotepath) | 1 | docs/sdd/qa/QA-0054-scope.log - regravado pela execucao corrigida do item seguinte |
| 2026-11-21 13:10:22 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-scope-task-0054.ps1 | 0 | docs/sdd/qa/QA-0054-scope.log |
| 2026-11-21 13:10:22 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (fechamento) | 0 | docs/sdd/runs/test-runs/20261121-130327-test-gate.log |
