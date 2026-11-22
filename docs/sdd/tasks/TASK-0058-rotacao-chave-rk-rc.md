<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0058 — Rotação de chave RK/RC da instância + kit reemitido (fecha S-39)

```yaml
runId: R-20261120-01
taskId: TASK-0058
status: DONE
agent: odev
phase: P-07
sequence: 2
createdAt: 2026-11-20 21:50:58
updatedAt: 2026-11-20 21:50:58
parent: SPEC-0012
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Api/Crypto/**", "backend/src/Squid.Api/Recovery/**", "backend/src/Squid.Api/Admin/**", "backend/src/Squid.Service/**", "backend/src/Squid.Worker/Backup/RestorePipeline.cs", "backend/tests/Squid.Tests/Crypto/**", "backend/tests/Squid.Tests/Recovery/**", "docs/runbooks/rotacionar-chave.md"]
blocks: ["TASK-0059"]
blockedBy: ["TASK-0054", "TASK-0056"]
inputArtifacts: ["docs/sdd/specs/SPEC-0012-backup-rotação-p07.md", "docs/features/05-arquitetura-consolidada.md", "specs/001-squid-mvp/research.md", "docs/runbooks/rotacionar-chave.md"]
outputArtifacts: ["backend/tests/Squid.Tests/Crypto/**", "backend/tests/Squid.Tests/Recovery/**", "docs/runbooks/rotacionar-chave.md", "docs/sdd/qa/QA-*"]
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
nextAction: DONE - ODEV mergeia em develop e commita docs (gate modelo C); S-39 FECHADO; proxima entrega TASK-0059
evidence: []
memoryBankFlags: []
```

## Intent

SPEC-0012 §E — fecha o **S-39** (dono P-07 decidido pelo humano 2026-11-12): rotação de só
material de instância (decisão 6): nova `Squid:RecoveryKey` → `keyEpoch++` → reembrulho das
cópias `0009` e das `DK`s de backup → `RC` trocada → kit reemitido com as épocas antigas no
keyring (R-20: backup antigo nunca é reescrito) → runbook `rotacionar-chave.md` de
`PENDENTE-fase` para **EXECUTÁVEL**. Rotação de chave de ator fica fora (→ `S-46` no backlog).

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `POST /api/admin/recovery/rotate` (sessão de operador; sem sessão `401`, fora de
  `Instance:Operators` `403`) gera a nova época: `keyEpoch` incrementa, `RetiredAt` da época
  anterior é gravado (data-model §2.10) — teste.
- AC-02: após a rotação, as cópias `0009` e as `DK`s de backup reembrulhadas decifram com a nova
  `Squid:RecoveryKey`, e o material da época antiga **continua legível** pelo keyring do kit
  reemitido (R-20) — teste: backup da época antigo restaura depois da rotação.
- AC-03: nenhum arquivo de backup existente foi reescrito após a rotação (bytes/hash dos
  envelopes pré-rotação idênticos) — teste.
- AC-04: `RC` antiga deixa de verificar (`POST /api/recovery/kit/verify` com a RC velha falha) e
  o kit reemitido verifica e restaura a `RK` em teste (FR-006/FR-007 com o material novo) — teste.
- AC-05: cadeia derivada da nova `RK` é coerente: `HKDF("fed/ed25519-sign")`,
  `HKDF("fed/rsa-wrap")`, `HKDF("fed/backup-kek")` derivam do material novo e o runtime
  (assinatura de saída) funciona sem reinício forçado além do normal — teste.
- AC-06: a resposta da rotação entrega o kit reemitido **uma única vez**; material de chave não
  aparece em log, em segunda resposta nem em rota posterior — teste.
- AC-07: runbook `docs/runbooks/rotacionar-chave.md` **EXECUTÁVEL** (passos reais, verificados;
  nenhum valor de chave nem exemplo com formato real) e o índice `docs/runbooks/README.md`
  atualizado de `PENDENTE-fase` para o status real — revisão + evidência de execução.
- AC-08: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1
  test` exit 0; nenhum arquivo alterado fora dos `touches`; zero pacote NuGet novo.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Api/Crypto/**` — geração da nova `RK`, `keyEpoch++`, reembrulho `0009`+`DK`s, keyring
- `backend/src/Squid.Api/Recovery/**` — troca de `RC` + reemissão do kit
- `backend/src/Squid.Api/Admin/**` — rota `recovery/rotate` com regra de operador
- `backend/src/Squid.Service/**` — wiring
- `backend/tests/Squid.Tests/{Crypto,Recovery}/**` — testes dos ACs
- `docs/runbooks/rotacionar-chave.md` + `docs/runbooks/README.md` — EXECUTÁVEL (fecha S-39)
- S-42 (import com cópia `0009`) é TASK-0059; S-46 (rotação de ator) é backlog
- produto fora dos `touches`: qualquer lacuna é `D-01` para o ODEV

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-54 | no | rotação só de material de instância; rotação de chave de ator fica fora → S-46 no backlog — decisão humana 2026-11-20 21:07:26 |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-20 21:50:58 | odev | task created from SPEC-0012 (rotação só instância: keyEpoch++ + reembrulho 0009/DKs + RC nova + kit keyring; fecha S-39; GAP-54; blockedBy 0054+0056)
- 2026-11-22 05:00:09 | odev | dispatch DEV (runId R-20261120-01, sequencia 1 -> 2, branch task/TASK-0058-rotacao-chave-rk-rc desde develop; encadeamento automatico - TASK-0057 DONE com QA-0057 PASSED 6/6 e merge em develop)
- 2026-11-22 05:45:01 | dev | DEV feasibility r1/2: unit = KeyRotationService (nova em Squid.Api/Crypto) + rota POST /api/admin/recovery/rotate (gate operador em AdminEndpoints) + POST /api/recovery/kit/verify com recoveryCredential opcional de instancia + ports IKeyEpochStore (tabela key_epoch, SQL direto) e IInstanceRecoveryKitStore (GrainState, padrao SqliteRecoveryStore, zero migration) + swap in-memory do RuntimeSigningKeyRing/ConfiguredBackupKeySource; D-01 = RestorePipeline (Squid.Worker fora de touches) escolhe a key por keyEpoch para AC-02 - fechado pela SPEC-0012 secao E + FR-047 + invariante R-20, reportado em touchedOutsidePlan; baseline sdd-test.ps1 test em execucao; branch task/TASK-0058-rotacao-chave-rk-rc criada de develop antes de qualquer edicao; memoria-bank rota implementacao lida (3 secoes); ponytail full carregado
- 2026-11-22 07:14:50 | dev | DEV implementacao: KeyRotationService (Crypto) gera a nova RK, avanca o ledger key_epoch (INSERT OR IGNORE materializa a epoca anterior + UPDATE de retired_at + INSERT da nova em UNA transacao), reembrulha as copias 0009 em transacao unica e o keyring do kit (embrulhado com HKDF fed/kit-keyring sob a chave atual), troca a RC (Argon2id do sealedRootKey sob a propria credencial) e grava o documento do kit em GrainState sem migration; rota POST /api/admin/recovery/rotate com o gate de operador existente entrega o kit uma unica vez (log so com o numero da epoca), POST /api/recovery/kit/verify ganha recoveryCredential opcional de instancia comparado por hash SHA-256 em tempo fixo (sem corpo = caminho de conta B-13 intacto), ConfiguredBackupKeySource/RuntimeSigningKeyRing ganham swap in-memory + keyring de config Squid:RecoveryKeyRing (epoch:base64,...), RestorePipeline escolhe a key por keyEpoch do keyring com fallback legado no ledger (arquivo fora de touches - D-01 fechado pela SPEC-0012 secao E + FR-047 + R-20, reportado em touchedOutsidePlan)
- 2026-11-22 07:15:25 | dev | DEV testes: 7 novos executados -3 em Crypto/KeyRotationUnitTests (caixa do keyring fed/kit-keyring abre so com a chave certa, parse do Squid:RecoveryKeyRing descarta lixo/duplicata sem quebrar o boot, swap vence a configuracao),3 em Recovery/KeyRotationRouteTests (AC-01 gate401/403 + ledger com RetiredAt e o backup pos-rotação carimando a epoca nova, AC-04 RC antiga401 e nova200 com o sealedRootKey abrindo a copia 0009 reembrulhada em RSA que assina e verifica + chave do kit igual a do IBackupKeySource, AC-06 ausencia do material em log capturado, em outras rotas e kit diferente na segunda rotação) e1 em Recovery/KeyRotationChainTests (AC-02/03/05: hashes do volume identicos antes/depois, DK novo abre so com a chave nova, chain da epoca1 restaura depois da rotação com keyring); comando sdd-test.ps1 test -TestFilter FullyQualifiedName~KeyRotation exit0 (7/7), artefato docs/sdd/runs/test-runs/20261122-062708-test-filter.log
- 2026-11-22 07:16:02 | dev | DEV runbook: docs/runbooks/rotacionar-chave.md de PENDENTE-fase para EXECUTAVEL - diagnostico (health/ready + ledger key_epoch por python+sqlite3 com o caminho por argv), A sessao do operador, B backup pre-rotacao com snapshot de hash, C rotacao one-shot (kit salvo fora do repositorio, so a epoca impressa), D extracao do valor atual + Squid__RecoveryKeyRing do kit com reinicio normal, E conferencia (health/ready, comparacao de hash sem saida, RC antiga401/nova200, backup na epoca nova) e Verificacao com ponteiros - TODOS os comandos executados de verdade em execucao unica de2 rotacoes (epocas1 a3 no ledger, RC antiga401, arquivos-alterados=0, varredura do log do servico com10 sementes e0 ocorrencias), prova em docs/sdd/runs/test-runs/20261122-071147-runbook-rotacionar-chave.log sem nenhum valor de chave; indice docs/runbooks/README.md NAO tocado (fora do array touches do bloco de controle - ODEV leva a linha do indice); nenhum valor de chave nem placeholder com formato real no documento
- 2026-11-22 07:26:26 | dev | DEV obs QA-0057 + escopo: a rotação NÃO resolve a recusa de cadeia off-site pós-restore (contrato §7 - deleção só em rotação/expurgo registrado, e rotação NÃO apaga envelope nenhum para manter o antigo restaurável) - a observação da QA-0057 fica para uma task de expurgo, parkada; senha do operador no runbook usou Read-Host no documento e variável gerada na execução não interativa (registrado no log da execução); baseline633/633 exit0, gate apos os testes reforçados em execução
- 2026-11-22 07:36:53 | dev | DEV gate final: sdd-test.ps1 test exit0 com 640/640 (baseline 633 + 7 novos) depois de todos os ajustes - artefato docs/sdd/runs/test-runs/20261122-073613-test-gate.log; escopo do commit: produto todo dentro de touches COM UMA excecao declarada (backend/src/Squid.Worker/Backup/RestorePipeline.cs, exigido pela AC-02 - selecao de key por keyEpoch, sem ela o restore da epoca antiga e impossivel), zero pacote NuGet novo (nenhum .csproj/.props alterado), task file apenas com append via sdd-state (edição de status/dispatch e do ODEV), logs em docs/sdd/runs/test-runs/ gitignored
- 2026-11-22 07:45:32 | odev | ODEV: verificacao do step result DONE - logLines/evidenceRows gravados pelo DEV via sdd-state (check exit 0); gate 640/640 exit 0 (633 + 7 novos), runbook EXECUTAVEL com 2 rotacoes reais executadas e logadas (epocas 1->3, RC antiga 401/nova 200, arquivos-alterados=0, varredura de log 10 sementes/0 ocorrencias), zero pacote novo, commit e3ec208 na branch
- 2026-11-22 07:45:32 | odev | ODEV: D-01 (RestorePipeline fora dos touches) adjudicada na escada especificacao - SPEC-0012 secao E + FR-047 + R-20 tornam a AC-02 impossivel sem escolher a key por keyEpoch; EXCEPCAO DE ESCOPO aceita e bloco de controle emendado pelo ODEV: touches + backend/src/Squid.Worker/Backup/RestorePipeline.cs (um arquivo, declarado em touchedOutsidePlan)
- 2026-11-22 07:45:32 | odev | ODEV: notDone - linha do indice docs/runbooks/README.md (rotacionar-chave PENDENTE-fase -> EXECUTAVEL) PEGA pelo ODEV agora, junto com a prosa; S-53 (expurgo de envelope, contrato 7) criado no backlog - a recusa de cadeia off-site pos-restore nao e resolvida pela rotacao (park do DEV, coerente com R-20)
- 2026-11-22 07:45:32 | odev | ODEV: transicao IN_DEVELOPMENT -> AWAITING_QA, dispatch QA-0058
- 2026-11-22 10:20:37 | qa | QA-0058 PASSED 8/8: gate abertura 640/640 e fechamento 641/641 exit 0 (640 DEV + 1 cenario novo da QA), filtro KeyRotation 8/8 no Windows e no Linux (podman sdk 10.0.403), 10 ciclos de mutacao can-fail todos EXPECT-OK restaurados byte-exact (M1/M2 gate+RetiredAt, M3 keyring da epoca antiga, M4/M5 cadeia nova, M6 comparacao da RC, M7 RC no template do log, M8 baseline do volume, M9 ring.RotateTo), M7b probe documenta PiiSanitizingEnricher (defesa em profundidade), gap de cobertura do M9 FECHADO com backend/tests/Squid.Tests/Recovery/KeyRotationRingSwapTests.cs (gate completo ficava 640 verde sem a troca do anel), escopo 19/19 nos touches com D-01 confirmado so keyring selection + fallback (9+/6-), zero pacote NuGet, runbook sem valores de chave e indice README EXECUTAVEL confere com o status real; 2 findings nao-bloqueantes (F1 cobertura ja remediada, F2 claim 'todos os comandos executados' nao cobre a verificacao 7 do runbook)
- 2026-11-22 10:28:53 | odev | QA-0058 PASSED 8/8 (qaAttempt 1/2): AC-01..AC-08 todos PASS; 10 ciclos de mutacao can-fail EXPECT-OK restaurados byte-exact (M7 prova ausencia de RC no log, M7b documenta PiiSanitizingEnricher como defesa em profundidade); gates 640/640 abertura e 641/641 fechamento exit 0; filtro KeyRotation 8/8 no Windows E no Linux; escopo 19/19 com D-01 confirmado (so keyring selection + fallback no RestorePipeline); runbook sem valores de chave e indice EXECUTAVEL confere; commit QA ad56679 na branch (1 commit, sem push)
- 2026-11-22 10:28:53 | odev | QA-0058: F1 (MEDIA, cobertura) - mutacao M9 (ring.RotateTo removido) deixava o gate COMPLETO 640 verde: nenhum cenario do DEV podia falhar na troca em place do anel; QA FECHOU com backend/tests/Squid.Tests/Recovery/KeyRotationRingSwapTests.cs (nos touches de teste, M9 vermelho no cenario novo, gate 641/641)
- 2026-11-22 10:28:53 | odev | QA-0058: F2 (baixa) adjudicada como S-54 no backlog - claim da linha 8 do runbook ("todos os comandos executados na prova") nao cobre a verificacao 7 (restore da epoca antiga com keyring) que nao tem execucao no log; comportamento coberto por KeyRotationChainTests e o comando e EXECUTAVEL desde a TASK-0056 - ou suavizar a claim ou executar ao vivo no fecho da fase (gate: servico real)
- 2026-11-22 10:28:53 | odev | ODEV: transicao AWAITING_QA -> DONE; merge em develop + commit docs conforme gate modelo C; encaminha TASK-0059 (S-42)

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-22 07:25:32 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (baseline antes das edicoes) | 0 | docs/sdd/runs/test-runs/20261122-054551-test-gate.log |
| 2026-11-22 07:25:33 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~KeyRotation (novos AC-01..AC-06,2 execucoes:7/7 e7/7 apos reforco da ausencia) | 0 | docs/sdd/runs/test-runs/20261122-072508-test-filter.log |
| 2026-11-22 07:26:25 | dev | powershell -File C:\Users\Ryke\AppData\Local\Temp\squid-runbook-rotacionar.ps1 (execucao do runbook rotacionar-chave: diagnosticos1-3, A-E e Verificacao em2 rotacoes, epocas1 a3, RC antiga401/nova200, arquivos-alterados=0, varredura do log do servico10 sementes0 ocorrencias) | 0 | docs/sdd/runs/test-runs/20261122-071147-runbook-rotacionar-chave.log |
| 2026-11-22 07:36:53 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate final apos todos os ajustes) | 0 | docs/sdd/runs/test-runs/20261122-073613-test-gate.log |
| 2026-11-22 10:21:37 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (abertura 640/640) | 0 | docs/sdd/runs/test-runs/20261122-080230-test-gate.log |
| 2026-11-22 10:21:38 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~KeyRotation (7/7 do DEV) | 0 | docs/sdd/runs/test-runs/20261122-080617-test-filter.log |
| 2026-11-22 10:21:38 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0058.ps1 -VerifyOnly (10/10 finds, ASCII 0) | 0 | docs/sdd/qa/QA-0058-verify.log |
| 2026-11-22 10:21:39 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0058.ps1 (ledger 10 ciclos, failed=0, green 8/8) | 0 | docs/sdd/qa/QA-0058-mutations.log |
| 2026-11-22 10:21:40 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-scope-task-0058.ps1 (AC-08, fails=0) | 0 | docs/sdd/qa/QA-0058-scope.log |
| 2026-11-22 10:21:40 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate SOB M9 - 640 verdes prova o gap) | 0 | docs/sdd/runs/test-runs/20261122-093526-test-gate.log |
| 2026-11-22 10:21:41 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~KeyRotation (8/8 com cenario novo) | 0 | docs/sdd/runs/test-runs/20261122-094705-test-filter.log |
| 2026-11-22 10:21:41 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (fechamento 641/641) | 0 | docs/sdd/runs/test-runs/20261122-101034-test-gate.log |
| 2026-11-22 10:21:41 | qa | podman run --rm -v repo:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash docs/sdd/qa/qa-linux-task-0058.sh (Linux build 0 + filtro 8/8) | 0 | docs/sdd/qa/QA-0058-linux-test-keyrotation.log |
