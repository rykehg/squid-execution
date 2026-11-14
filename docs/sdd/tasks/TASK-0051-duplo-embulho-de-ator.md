<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0051 — Assinatura de ator registrado: duplo embrulho com `Squid:RecoveryKey`

```yaml
runId: R-20261113-01
taskId: TASK-0051
status: DONE
agent: odev
phase: P-06
sequence: 2
createdAt: 2026-11-14 09:10:08
updatedAt: 2026-11-14 11:04:15
parent: SPEC-0011
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Api/Auth/**", "backend/src/Squid.Api/Crypto/**", "backend/src/Squid.Grains/Actors/**", "backend/src/Squid.Persistence/schema/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/**"]
blocks: ["TASK-0047"]
blockedBy: ["TASK-0050"]
inputArtifacts: ["docs/sdd/specs/SPEC-0011-federacao-p06.md", "docs/sdd/plan/plan.md", "docs/sdd/tasks/TASK-0042-chave-de-assinatura-em-runtime.md", "docs/sdd/tasks/TASK-0045-saida-fan-out-e-follow.md"]
outputArtifacts: ["backend/src/Squid.Persistence/schema/0009_instance_signing_keys.sql", "backend/src/Squid.Api/Auth/**", "backend/src/Squid.Grains/Actors/**", "backend/tests/Squid.Tests/**"]
interviewRounds: 2/6
feasibilityRounds: 0/2
correctionAttempts: 0/2
qaAttempts: 1/2
contextHandoffs: 0/4
progressDelta: 0
humanGate:
  approvedBy: Henrique
  approvedDate: 2026-11-14 09:10:08
  scope: GAP-50 - duplo embrulho da RSA do ator com a Squid:RecoveryKey de instancia (opcao 1 escolhida; cobre registro, runtime e limite de atores antigos a config)
  expiresAt: NENHUM
blocker: NENHUM
nextAction: DONE - QA-0051 PASSED (6/6 ACs, 7 mutacoes, gate 573/573, commits 0a7cdf9 + 68340b6); merge em develop pelo ODEV e dispatch da TASK-0052
evidence: []
memoryBankFlags: []
```

## Intent

Entrega extra da P-06 (cortada 2026-11-14 09:10:08 pelo gate humano do GAP-50): fecha a
contradição **spec × código** levantada como `[SUSPEITO]` na TASK-0045 — o registro cria `RK`
própria por ator (`AuthEndpoints.cs:68`), então a `Squid:RecoveryKey` de boot não desembrulhava
a RSA de **nenhum** usuário registrado por HTTP e a interop real (TASK-0049) falharia para
qualquer conta criada pela API. Com o **duplo embrulho**, o runtime assina com a chave do ator
desembrulhando a cópia de instância (contrato §5 intacto, kit/GAP-01 intactos).

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: com `Squid:RecoveryKey` configurada, o registro (`POST /api/auth/register`) embrulha a
  **mesma RSA do ator** também com `HKDF(RK_instância, "fed/rsa-wrap")` e persiste **só a cópia
  embrulhada** em tabela própria (`0009_*` idempotente); sem a config, o registro é o de hoje e
  nenhuma cópia nasce — teste.
- AC-02: um ator **registrado pela API** tem a saída federada funcionando **sem contornos**: o
  caminho de ativação desembrulha a cópia de instância, assina com a chave RSA **própria do
  ator** (`keyId` `#main-key` do ator) e um verificador independente aceita o par
  assinar→verificar — nenhum teste precisa mais "escrever o ator direto" — teste.
- AC-03: kit e continuidade (GAP-01) intactos: o embrulho do kit (`EncryptedRootKey` + wrapped
  da RK do ator) **não muda** — restaurar o kit devolve a mesma identidade e as regressões da
  P-02 continuam verdes — teste.
- AC-04: a cópia de instância nunca aparece em claro: a tabela guarda só material embrulhado,
  nenhuma rota/log devolve a cópia, e sem `Squid:RecoveryKey` no boot a ativação de assinatura
  falha auditável como hoje (AC-01/03 da TASK-0042 preservadas) — teste.
- AC-05: ator registrado **antes** de configurar `Squid:RecoveryKey` → sem cópia: a saída dele
  fica desabilitada com aviso auditável (limite aceito pelo humano no gate GAP-50), e registrar
  **depois** da config nasce com cópia — teste.
- AC-06: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1
  test` exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Persistence/schema/**` — `0009_instance_signing_keys.sql` idempotente
  (liberação teste/dev/CI vale; produção gate humano)
- `backend/src/Squid.Api/Auth/**` — registro embrulha a cópia quando `Squid:RecoveryKey` existe
- `backend/src/Squid.Api/Crypto/**` — helper de embrulho da cópia (RK de instância) sobre a mesma
  chave bruta
- `backend/src/Squid.Grains/Actors/**` — ativação usa a cópia de instância quando existe (e
  preserva o caminho do kit como fallback de recuperação)
- `backend/src/Squid.Service/**` — store da tabela `0009` + wiring
- `backend/tests/Squid.Tests/**` — testes das ACs (inclui provar AC-02 SEM o contorno atual)
- produto **fora** dos `touches` desta task: qualquer lacuna levantada como dúvida, não corrigida aqui

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-50 | no | RESOLVED (HUMAN) — decisão 2026-11-14 09:10:08 (opção 1): duplo embrulho, cópia em `0009_*`, runtime com segredo de boot, kit intacto; atores antigos à config não federam (limite aceito); alternativa "embrulhar a RK do ator" rejeitada por privilégio mínimo |
| — | no | o latch de chave do `ActorGrain` (parked da TASK-0046: instância longeva assinaria o segundo ator com a chave do primeiro) entra na mesma leitura de código — se a correção fugir dos `touches`, é `D-01` |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-14 09:10:08 | odev | task created from SPEC-0011 (entrega extra 8 de 11 da P-06, gate humano GAP-50 opcao 1 - duplo embrulho; blockedBy TASK-0050, blocks TASK-0047; humanGate registrado com a decisao de 09:10:08)
- 2026-11-14 09:12:24 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; runId R-20261113-01; blockedBy TASK-0050 resolve (DONE com QA-0050 PASSED 4/4, gate 559/559, merge c58bd6e em develop + docs 2c66912); gate humano GAP-50 registrado no bloco (Henrique, 2026-11-14 09:10:08, opcao 1 duplo embrulho); autorizacao de commit gate PHASE 2026-11-11 20:42:28 (modelo C) incluida - nova branch task/TASK-0051-duplo-embulho-de-ator desde develop; logLines e evidenceRows sem relogio para o ODEV carimbar (sem pipe em comandos)
- 2026-11-14 10:16:29 | dev | DEV feasibility r1/2: unit = AuthEndpoints.RegisterAsync, KeyState/RuntimeSigningKeyRing (Squid.Api/Crypto), ActorGrain.ActivateSigningKeyAsync, nova tabela 0009_instance_signing_keys com SqliteInstanceSigningKeyStore; callers = OutboundPipeline.SendAsync e SquidServiceHost; dados = so a nova tabela (event-sourcing do ator e embrulho do kit intactos); ordem normativa lida = SPEC-0011 secao B (duplo embrulho GAP-50) + ACs das TASK-0042/0045; baseline sdd-test.ps1 test 559/559 exit 0 em runs/test-runs/20261114-093550-test-gate.log; touches confirmado completo e disjointo (blockedBy TASK-0050 DONE, blocks TASK-0047 nao iniciada); latch do ActorGrain parked da TASK-0046 corrigido DENTRO de touches, logo nenhum D-01; memoria-bank rota implementacao lida (3 arquivos); branch task/TASK-0051-duplo-embulho-de-ator criada de develop 2c66912 antes de qualquer edicao
- 2026-11-14 10:16:30 | dev | DEV implementacao: migration 0009_instance_signing_keys.sql idempotente (handle PK, wrapped_signing_key, liberacao teste/dev/CI igual a familia), porta IInstanceSigningKeyStore em Squid.Api/Crypto + store SqliteInstanceSigningKeyStore em Squid.Service, RuntimeSigningKeyRing embrulha a MESMA RSA com HKDF(RK_instancia, fed/rsa-wrap) no registro e abre a copia por handle na ativacao, AuthEndpoints grava a copia depois da linha de credencial (sem segredo a copia e nula e o registro e byte a byte o de sempre), ActorGrain ativa por handle com cache por ator (latch 0046: o segundo ator nunca reusa a chave do primeiro) e OutboundPipeline emite warning auditavel quando a saida de um ator fica desabilitada
- 2026-11-14 10:16:30 | dev | DEV testes: 14 novos - 7 em ServiceHost/DoubleWrapTests (AC-01 com e sem segredo, AC-02 ator registrado federando assinado aceito por verificador independente, AC-03 kit intacto e restauracao da mesma identidade, AC-04 copia nunca em claro em tabela rota log e sem segredo desabilitacao auditavel, AC-05 ator antigo sem copia desabilitado com aviso e outro registrado depois federationando), 1 da migration 0009, 4 de ativacao e latch, +2 do RuntimeSigningKeyRingTests reescrito; primeira execucao da classe apontou 4 vermelhos (CSRF da rota de kit, mnemonic como array, leitor de PEM) - corrigidos e a classe re-executada 7/7 exit 0; setups ajustados em FederationRuntimeKeyTests (copia nas AC-02/AC-03 da TASK-0042), OutboundFanOutTests (SignableAsync = registro HTTP real, contorno removido) e MotorHost (nasce com copia)
- 2026-11-14 10:16:31 | dev | DEV gate AC-06: sdd-test.ps1 test exit 0 com 573/573 (559 de baseline + 14 novos) em runs/test-runs/20261114-101234-test-gate.log com exit 0 no cabecalho; areas tocadas 116/116 (Federation, Crypto, Actors, ServiceHost) em 20261114-100417-test-fast.log; build exit 0 sem erro novo
- 2026-11-14 10:16:31 | dev | DEV escopo: git status --porcelain mostra 18 arquivos de backend todos dentro dos touches (6 novos), fora-dos-touches 0 alem dos docs/sdd pre-existentes do ODEV (plan, SPEC-0011, task file nao commitado); commit unico autorizado 0a7cdf9 na branch task/TASK-0051-duplo-embulho-de-ator desde develop 2c66912, sem push, sem rebase, main intacta em b9fd628
- 2026-11-14 10:16:32 | dev | DEV achado fora dos touches (nao corrigido aqui): ProfileJsonLd.TryDecodePublicKeyPem em Squid.Protocols nao decodifica PEM blindado - Compact remove o espaco do armor antes do Replace do armor, entao HttpFederatedActorClient.KeyOf (chave remota do inbox) recebe null; provado no teste Ac02 do DoubleWrapTests, que passou a ler o documento com ProfileJsonLd.Materialize; vira item da ODEV antes da TASK-0047/0049
- 2026-11-14 10:16:37 | odev | ODEV verificou antes de transicionar: gate 20261114-101234-test-gate.log lido no arquivo (exit: 0, Aprovado 573, Com falha 0, 7m48s) e filtro 20261114-100146-test-fast.log (exit: 0, 7/7); commit 0a7cdf9 conferido via git show --stat (18 arquivos, todos nos touches); latch do ActorGrain corrigido dentro dos touches; logLines (6) e evidenceRows (9) carimbados via sdd-state; step result DONE aceito
- 2026-11-14 10:16:37 | odev | achado do DEV FORA dos touches acolhido: ProfileJsonLd.TryDecodePublicKeyPem nao decodifica PEM blindado (prova empirical no teste Ac02) e HttpFederatedActorClient.KeyOf depende dele - vai morder em 0047/0049; ODEV corta a TASK-0052 (correcao focada) entre esta e a 0047 e registra S-41 no backlog; o parked de kit-import sem copia vira S-42
- 2026-11-14 10:16:39 | odev | transicao IN_DEVELOPMENT -> AWAITING_QA (attempt 1/2) - dispatch QA a seguir
- 2026-11-14 11:05:02 | qa | qa-task + ponytail-qa (full): entrada pelo task file (status AWAITING_QA, qaAttempts 0/2), SPEC-0011 secao B (duplo embrulho, GAP-50 opcao 1) e memoria-bank rota qa-integracao lidas; AC-01..AC-06 mapeadas para os 7 Facts de DoubleWrapTests + gate + escopo
- 2026-11-14 11:05:02 | qa | baseline verde da QA antes de qualquer mutacao: filtro FullyQualifiedName~DoubleWrapTests 7/7 exit 0 em docs/sdd/runs/test-runs/20261114-102845-test-filter.log
- 2026-11-14 11:05:03 | qa | 7 mutacoes can-fail no PRODUTO (T1 copia nao persistida em AuthEndpoints, T2 chave estranha na ativacao, T3 kit sob RK estranha, T4 copia em claro na tabela, T5 copia vazada no corpo de registro, T6 sem segredo devolvendo chave, T7 cauda do aviso auditavel cortada), cada uma derrubando a cena da propria AC, restauradas byte-exact hash PRE=POST com blob-identity 4/4; ledger docs/sdd/qa/QA-0051-mutations.log SUMMARY cycles=7 failed=0
- 2026-11-14 11:05:04 | qa | ciclo T7 na PRIMEIRA tentativa nao derrubou o teste: a mutacao trocou so a cabeca do aviso e a frase asserted de Ac05 permaneceu intacta (prova em 20261114-103613-test-filter.log e 20261114-104011-test-filter.log, ambos exit 0, com o mutante confirmado no binario via busca UTF-16 no Squid.Service.dll); T7 redesenhado para cortar a frase asserted e o vermelho provado em 20261114-104943-test-filter.log - achado registrado como NOTA-1 no qaDoc
- 2026-11-14 11:05:04 | qa | gate AC-06 re-executado pela QA: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test exit 0 com 573/573 em docs/sdd/runs/test-runs/20261114-105858-test-gate.log (559 baseline + 14 novos)
- 2026-11-14 11:05:05 | qa | escopo AC-06 nas DUAS shells: commit 0a7cdf9 com 18/18 arquivos dentro dos seis glob sets dos touches, git status de backend 0, residuo de mutante 0, blob-identity 4/4, mtime 4, fora-dos-touches 0 (as 5 entradas docs/sdd sao o WIP do ODEV gravado no baseline) - gemeo .sh executado sob WSL
- 2026-11-14 11:05:05 | qa | commit unico autorizado de QA: 68340b6 na branch task/TASK-0051-duplo-embulho-de-ator so com os 10 artefatos de docs/sdd/qa, sem push, task file e docs do ODEV fora do commit
- 2026-11-14 11:05:06 | qa | verdict PASSED 6/6 ACs sem BLOCKER; unverified declarado: suite Linux (wsl-dotnet=ABSENT) e forma appsettings.json da chave
- 2026-11-14 11:05:13 | odev | ODEV verificou antes de transicionar: QA-0051-duplo-embulho-de-ator.md com verdict PASSED lido no arquivo; gate 20261114-105858-test-gate.log exit: 0 / Aprovado 573 / Com falha 0 / 7m46s (lido no arquivo); mutations SUMMARY cycles=7 failed=0 residue-backend=0 lido no log; commit 68340b6 conferido via git show --stat (10 arquivos so docs/sdd/qa); git status -- backend = 0 (produto intocado pela QA); logLines (8) e evidenceRows (10) carimbados via sdd-state - veredito aceito
- 2026-11-14 11:05:14 | odev | QA-0051 PASSED attempt 1/2 (PRODUTO+TESTES): 6/6 ACs, 7 ciclos can-fail todos vermelhos na AC alvo, restore byte-exact, GREEN 7/7; gate 573/573; NOTA-1 (mutante T7 v1 nao derrubou - mutante de teste, redesenhado e provado); NOTA-3 confirma o defeito PEM ja roteado a TASK-0052/S-41; achado de produto nenhum
- 2026-11-14 11:05:14 | odev | transicao AWAITING_QA -> DONE; entrega 8 de 12 da P-06 encerrada - merge em develop + docs commitados pelo ODEV, dispatch da TASK-0052 a seguir

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-14 10:16:32 | dev | git checkout -b task/TASK-0051-duplo-embulho-de-ator | 0 | branch criada de develop 2c66912 antes de qualquer edicao |
| 2026-11-14 10:16:33 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | baseline 559/559 em docs/sdd/runs/test-runs/20261114-093550-test-gate.log |
| 2026-11-14 10:16:33 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 0 | 0 erros, 2 avisos pre-existentes (QaTask0027Tests, ThreadStoreTests), sem log proprio |
| 2026-11-14 10:16:34 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -Fast -TestFilter FullyQualifiedName~DoubleWrapTests | 0 | 7/7 em docs/sdd/runs/test-runs/20261114-100146-test-fast.log |
| 2026-11-14 10:16:34 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -Fast -TestFilter (quatro areas: Federation, Crypto, Actors, ServiceHost - comando literal no cabecalho do log) | 0 | 116/116 em docs/sdd/runs/test-runs/20261114-100417-test-fast.log |
| 2026-11-14 10:16:35 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | gate AC-06 573/573 com exit 0 no cabecalho em docs/sdd/runs/test-runs/20261114-101234-test-gate.log |
| 2026-11-14 10:16:35 | dev | git status --porcelain | 0 | 18 arquivos de backend todos em touches, fora-dos-touches 0 (docs/sdd plan, SPEC-0011 e task file = pre-existentes do ODEV, fora do commit) |
| 2026-11-14 10:16:36 | dev | git add backend + git commit TASK-0051 na branch task/TASK-0051-duplo-embulho-de-ator | 0 | commit 0a7cdf9 com 18 files changed, 1053 insertions, 110 deletions, todos em touches, sem push, main em b9fd628 |
| 2026-11-14 10:16:36 | dev | git show --stat --format=%h HEAD | 0 | 0a7cdf9 - 6 arquivos novos e 12 emendados, todos dentro dos touches |
| 2026-11-14 11:05:06 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-baseline-task-0051.ps1 | 0 | docs/sdd/qa/QA-0051-git-baseline.txt (HEAD 0a7cdf9, 18 arquivos do commit, branch-r-contains EMPTY, wsl-dotnet ABSENT, windows-dotnet 10.0.403, fails=0) |
| 2026-11-14 11:05:07 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0051.ps1 -VerifyOnly | 0 | docs/sdd/qa/QA-0051-verify.log (7 padroes hits=1, VERIFY total=7 bad=0) |
| 2026-11-14 11:05:07 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~DoubleWrapTests | 0 | docs/sdd/runs/test-runs/20261114-102845-test-filter.log (baseline verde 7/7) |
| 2026-11-14 11:05:08 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0051.ps1 | 0 | docs/sdd/qa/QA-0051-mutations.log (7 vermelhos de proposito, 7x RESTORE-OK hash=PRE, GREEN 7/7, SUMMARY cycles=7 failed=0) |
| 2026-11-14 11:05:08 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261114-105858-test-gate.log (gate AC-06: exit 0 no cabecalho, Com falha 0, Aprovado 573, Total 573) |
| 2026-11-14 11:05:09 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-scope-task-0051.ps1 | 0 | docs/sdd/qa/QA-0051-scope.log (commit-files=18 fora-dos-touches=0, blob=4/4, mtime=4, residue=0, fails=0) |
| 2026-11-14 11:05:09 | qa | wsl -e bash docs/sdd/qa/qa-scope-task-0051.sh | 0 | docs/sdd/qa/QA-0051-scope-sh.log (gemeo bash sob WSL, mesmas contacoes, fails=0) |
| 2026-11-14 11:05:10 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~DoubleWrapTests.Ac05_without_a_copy | 0 | docs/sdd/runs/test-runs/20261114-104011-test-filter.log (investigacao do T7 ciclo 1: verde com a mutacao antiga, prova da NOTA-1) |
| 2026-11-14 11:05:10 | qa | git checkout -- backend/src/Squid.Service/OutboundPipeline.cs | 0 | mutante manual da investigacao restaurado byte-exact antes das execucoes seguintes (git status de backend limpo) |
| 2026-11-14 11:05:11 | qa | git add docs/sdd/qa + git commit QA-0051 PASSED na branch task/TASK-0051-duplo-embulho-de-ator | 0 | 68340b6 com 10 arquivos de docs/sdd/qa, sem push, main e ODEV intocados |
