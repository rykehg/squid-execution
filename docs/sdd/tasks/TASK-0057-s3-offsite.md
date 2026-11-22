<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0057 — S3CompatibleTarget off-site + drill restore-offsite

```yaml
runId: R-20261120-01
taskId: TASK-0057
status: DONE
agent: odev
phase: P-07
sequence: 2
createdAt: 2026-11-20 21:50:58
updatedAt: 2026-11-20 21:50:58
parent: SPEC-0012
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Worker/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/Backup/**"]
blocks: ["TASK-0060"]
blockedBy: ["TASK-0056"]
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
nextAction: DONE - ODEV mergeia em develop e commita docs (gate modelo C); proxima entrega TASK-0058
evidence: []
memoryBankFlags: []
```

## Intent

SPEC-0012 §B + §D: `S3CompatibleTarget` (contrato §1, MVP "off-site fora do perímetro do
provedor") com client S3 **escrito à mão** (`PUT`/`GET`/`LIST`/`DELETE` + SigV4, zero pacote
NuGet novo), testado contra um **fake S3 em processo** que valida a assinatura de verdade, e o
sexto drill do contrato §5 — `restore-offsite`: backup local + off-site → apagar o disco local →
restore do off-site → estado completo, `/ready` verde. Fecha o DoD do Sprint 6 ("off-site
restaura sem o disco local").

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `S3CompatibleTarget` implementa `PutAsync`/`GetAsync`/`ListAsync`/`ExistsAsync`/
  `DeleteAsync` falando o protocolo S3 (`PUT`/`GET`/`LIST`/`DELETE` + assinatura SigV4) sobre
  `Backup:S3:*` — **nenhum pacote NuGet novo** adicionado ao solution — teste.
- AC-02: os testes rodam contra um fake S3 em processo que **valida a assinatura SigV4**
  (requisição sem assinatura ou com assinatura errada é rejeitada pelo fake) — sem rede externa
  no gate — teste.
- AC-03: `restore-offsite` (drill `Category=RestoreDrill`): backup local + off-site → apagar o
  disco local → restore do off-site → estado completo e hashes idênticos — teste.
- AC-04: falha do target off-site (resposta 500/timeout simulada) não corrompe o backup local:
  o target local segue gravando e o envelope registra o estado por destino — teste.
- AC-05: a credencial S3 vem só de `Backup:S3:*` (config/segredo externo, contrato §7): ela não
  aparece no estado do grain, em log nem na resposta de nenhuma rota — teste.
- AC-06: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1
  test` exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Worker/**` — `S3CompatibleTarget` + client SigV4 à mão
- `backend/src/Squid.Service/**` — wiring + seção `Backup:S3:*`
- `backend/tests/Squid.Tests/Backup/**` — fake S3 em processo + drill `restore-offsite`
- rota `backup/status`/`redundancy.score` é TASK-0060; rotação é TASK-0058
- produto fora dos `touches`: qualquer lacuna é `D-01` para o ODEV

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-52 | no | S3 testado contra fake S3 em processo com validação SigV4 real; client escrito à mão (zero pacote); endpoint real (`Backup:S3:*`) por config — default aceito pelo humano 2026-11-20 21:07:26 |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-20 21:50:58 | odev | task created from SPEC-0012 (S3 a mao + SigV4 + fake S3 em processo + drill restore-offsite; GAP-52; blockedBy TASK-0056)
- 2026-11-22 02:20:51 | odev | dispatch DEV (runId R-20261120-01, sequencia 1 -> 2, branch task/TASK-0057-s3-offsite desde develop; encadeamento automatico - TASK-0056 DONE com QA-0056 PASSED 8/8 e merge 1c6e145/5d84ed3 em develop)
- 2026-11-22 02:58:33 | dev | feasibility 1/2 fechada: fanout por destino dentro de BackupRunner (primario local primeiro com semantica atual, off-site degrada com registro verified_at NULL = estado por destino, descoberta de seqFrom so no primario); D-01 resolvido por SPEC-0012 secao B + contrato 1/6 + FR-050; branch task/TASK-0057-s3-offsite criada desde develop; baseline gate 627/627 exit 0
- 2026-11-22 03:52:08 | dev | produto escrito: BackupS3Options (Backup:S3:* so de config), S3SigV4 (HMAC chain mao, zero pacote), S3CompatibleTarget (PUT/GET/LIST paginado/HEAD/DELETE + seam S-47/S-48 do header), fanout off-site em BackupRunner (primario falha = run falha como antes; off-site 500/timeout = degrada + registro verified_at NULL = estado por destino + warning estruturado), onPrepared opcional no BackupPipeline, wiring em SquidServiceHost; build exit 0
- 2026-11-22 03:52:26 | dev | vermelho 1 da familia Backup (6/6): fake S3 canonicalizava a barreira do path como caractere (encode por segmento corrigido) e BackupOffsiteTests.Cleanup recebeu os argumentos invertidos; vermelho 2 (2/2): Replay do AC-02 copiava a Authorization original junto da adulterada (fake lia a assinatura valida) e a chamada de Cleanup de AC-05 ficou invertida apos o rename; ambas as rodadas corrigidas na raiz do teste, familia Backup 36/36 exit 0 e filtro Category=RestoreDrill 8/8 exit 0
- 2026-11-22 04:02:56 | dev | gate final 633/633 exit 0 (627 pre-existentes + 6 novos: AC-01 cinco operacoes, AC-02 assinatura validada pelo fake, AC-04 x2 degradacao 500/timeout, AC-05 credencial ausente de estado/log/rota, drill restore-offsite); filtro Category=RestoreDrill 8/8 exit 0; familia Backup 36/36 exit 0; escopo 11 arquivos de codigo nos touches, csproj e Directory.Packages.props intocados (zero pacote NuGet novo)
- 2026-11-22 04:07:13 | odev | ODEV: verificacao do step result DONE - logLines/evidenceRows ja gravados pelo DEV via sdd-state, check exit 0; gate 633/633 exit 0 (627 + 6 novos), familia Backup 36/36, drills 8/8, 11 arquivos de codigo nos touches, csproj e Directory.Packages.props intocados (zero pacote novo)
- 2026-11-22 04:07:13 | odev | ODEV: D-01 (coexistencia dos dois destinos com um IBackupTarget no DI) resolvida na especificacao (SPEC-0012 secao B + contrato 1/6 + FR-050: fanout por destino no BackupRunner, off-site degrada com verified_at NULL = estado por destino); 2 rodadas de vermelho foram do proprio DEV na autoria dos testes - correctionAttempts segue 0/2
- 2026-11-22 04:07:13 | odev | ODEV: transicao IN_DEVELOPMENT -> AWAITING_QA, dispatch QA-0057
- 2026-11-22 04:53:57 | qa | QA-0057: gate de abertura 633/633 exit 0 (log 20261122-042134) e fechamento 633/633 exit 0 (log 20261122-045245); filtros Backup 36/36, Category=RestoreDrill 8/8 e S3CompatibleTargetTests 2/2 exit 0 no Windows; escopo 11 arquivos nos touches, csproj e Directory.Packages.props intocados, 1 commit sobre develop
- 2026-11-22 04:53:57 | qa | QA-0057: 5 mutacoes can-fail vermelhas nos proprios ACs (M1 Ac02:123, M2 Ac02:141, M3 Ac01:52, M4 Ac04x2+Ac05, M5 so o drill restore-offsite) e restauradas byte-exact SHA-256 pre/post identico com git status limpo; gemeo Linux qa-linux-task-0057.sh no contêiner sdk:10.0 exit 0 (build 0, Backup 36/36, drills 8/8); verdicto PASSED, qaDoc docs/sdd/qa/QA-0057-s3-offsite.md
- 2026-11-22 04:53:57 | qa | QA-0057: findings 3 nao-bloqueantes (arrasto QA-0056 hash-payload sem cenario, shortcut Backup:S3 typo documentado, recusa de cadeia pos-restore = escopo TASK-0058); unverified declarado: suite Linux completa, caminho CI pwsh, endpoint S3 real (GAP-52)
- 2026-11-22 04:59:09 | odev | QA-0057 PASSED 6/6 (qaAttempt 1/2): AC-01..AC-06 todos PASS; 5 mutacoes can-fail vermelhas nos ACs proprios e restauradas byte-exact (SHA-256 pre/post identico, git status limpo); gates 633/633 exit 0 na abertura e no fechamento; validacao do fake S3 prova-se independente (grep: S3SigV4 so no produto, FakeS3 remonta o canonical request com copia propria, 4 adulteracoes recusadas); familia Backup 36/36 e drills 8/8 no Windows E no Linux; escopo 11/11 nos touches, zero pacote novo; commit QA b17d6a2 na branch (1 commit, sem push)
- 2026-11-22 04:59:09 | odev | QA-0057: adjudicacao dos 3 findings nao-bloqueantes - (1) recusa payload x contentHash sem cenario can-fail = mesmo alvo do S-50 ja registrado (nao abre numero novo); (2) typo em Backup:S3:* desliga off-site silenciosamente = S-52 novo no backlog (upgrade para warning de boot junto do painel da TASK-0060); (3) recusa ruidosa da cadeia off-site apos restore (seqFrom 0) = observacao, escopo do expurgo/rotacao da TASK-0058 - nenhum AC contrariado; unverified declarados: suite Linux completa, CI pwsh, endpoint S3 real (GAP-52), sequencia restore-then-backup
- 2026-11-22 04:59:09 | odev | ODEV: transicao AWAITING_QA -> DONE; merge em develop + commit docs conforme gate modelo C; encaminha TASK-0058 (rotação, fecha S-39)

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-22 02:58:33 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261122-025508-test-gate.log |
| 2026-11-22 04:02:57 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Backup | 0 | docs/sdd/runs/test-runs/20261122-035048-test-filter.log |
| 2026-11-22 04:02:57 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter Category=RestoreDrill | 0 | docs/sdd/runs/test-runs/20261122-035133-test-filter.log |
| 2026-11-22 04:02:58 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261122-040200-test-gate.log |
| 2026-11-22 04:02:58 | dev | git diff --name-only develop (escopo) + git diff develop -- csproj e Directory.Packages.props (vazio = zero pacote novo) | 0 | NENHUM |
| 2026-11-22 04:40:56 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261122-042134-test-gate.log |
| 2026-11-22 04:40:56 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Backup | 0 | docs/sdd/runs/test-runs/20261122-042433-test-filter.log |
| 2026-11-22 04:40:57 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter Category=RestoreDrill | 0 | docs/sdd/runs/test-runs/20261122-042501-test-filter.log |
| 2026-11-22 04:40:57 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~S3CompatibleTargetTests (mutacao M1: fake S3 sem validacao de assinatura) | 1 | docs/sdd/runs/test-runs/20261122-042625-test-filter.log |
| 2026-11-22 04:40:58 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~S3CompatibleTargetTests (mutacao M2: checagem de payload hash neutra) | 1 | docs/sdd/runs/test-runs/20261122-042756-test-filter.log |
| 2026-11-22 04:40:58 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~S3CompatibleTargetTests (mutacao M3: cliente assinando errado) | 1 | docs/sdd/runs/test-runs/20261122-043016-test-filter.log |
| 2026-11-22 04:40:58 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~BackupOffsiteTests (mutacao M4: degradacao off-site removida) | 1 | docs/sdd/runs/test-runs/20261122-043210-test-filter.log |
| 2026-11-22 04:40:59 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter Category=RestoreDrill (mutacao M5: restore do disco local apagado) | 1 | docs/sdd/runs/test-runs/20261122-043354-test-filter.log |
| 2026-11-22 04:40:59 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Backup (apos restauracao byte-exact) | 0 | docs/sdd/runs/test-runs/20261122-043732-test-filter.log |
| 2026-11-22 04:41:00 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter Category=RestoreDrill (apos restauracao byte-exact) | 0 | docs/sdd/runs/test-runs/20261122-043800-test-filter.log |
| 2026-11-22 04:41:00 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~S3CompatibleTargetTests (apos restauracao byte-exact) | 0 | docs/sdd/runs/test-runs/20261122-043813-test-filter.log |
| 2026-11-22 04:41:01 | qa | git diff --name-only develop...HEAD (escopo 11 arquivos nos touches) + git diff develop -- csproj e Directory.Packages.props (vazio) | 0 | NENHUM |
| 2026-11-22 04:47:08 | qa | podman run --rm -v C:/dev/squid-squad-net/squid-execution:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash docs/sdd/qa/qa-linux-task-0057.sh (Linux: build + familia Backup 36/36 + drills 8/8) | 0 | docs/sdd/qa/QA-0057-linux-build.log + QA-0057-linux-test-backup.log + QA-0057-linux-test-drills.log |
| 2026-11-22 04:53:19 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate de fechamento apos restauracao das 5 mutacoes) | 0 | docs/sdd/runs/test-runs/20261122-045245-test-gate.log |
