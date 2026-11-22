<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0059 — Import de kit com cópia de instância (fecha S-42)

```yaml
runId: R-20261120-01
taskId: TASK-0059
status: DONE
agent: odev
phase: P-07
sequence: 2
createdAt: 2026-11-20 21:50:58
updatedAt: 2026-11-20 21:50:58
parent: SPEC-0012
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Api/Recovery/**", "backend/src/Squid.Api/Crypto/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/Recovery/**"]
blocks: []
blockedBy: ["TASK-0058"]
inputArtifacts: ["docs/sdd/specs/SPEC-0012-backup-rotação-p07.md", "docs/sdd/specs/SPEC-0011-federacao-p06.md"]
outputArtifacts: ["backend/tests/Squid.Tests/Recovery/**", "docs/sdd/qa/QA-*"]
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
nextAction: DONE - ODEV mergeia em develop e commita docs (gate modelo C); S-42 FECHADO; proxima entrega TASK-0060 (ultima nova)
evidence: []
memoryBankFlags: []
```

## Intent

SPEC-0012 §F — fecha o **S-42**: o `POST /api/recovery/import` passa a embrulhar também a cópia
`0009` da RSA do ator quando `Squid:RecoveryKey` existe (mesma regra do registro da SPEC-0011
§B), para a identidade restaurada sair federando sem novo registro. Sem a config, comportamento
byte-idêntico ao atual. Decisão humana 2026-11-20 (rodada 1): S-42 fecha junto com a rotação.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: com `Squid:RecoveryKey` configurada, importar um kit cria a cópia `0009` do ator
  importado (embrulho duplo da SPEC-0011 §B) e a identidade restaurada **federada de saída**
  funciona (assinatura desembrulhável com o segredo de boot) sem novo registro — teste.
- AC-02: sem `Squid:RecoveryKey` no boot, o import responde **byte-idêntico** ao comportamento
  atual (sem cópia, sem erro, mesma resposta JSON) — teste.
- AC-03: a cópia criada no import segue as regras da SPEC-0011 §B: nunca em claro, nunca em
  log, nunca servida por rota — teste.
- AC-04: importar o mesmo kit duas vezes é idempotente (B-14): mesma resposta, nenhuma linha
  duplicada em `0009` — teste.
- AC-05: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1
  test` exit 0; nenhum arquivo alterado fora dos `touches`; zero pacote NuGet novo.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Api/Recovery/**` — `ImportAsync` embrulha a cópia quando há `Squid:RecoveryKey`
- `backend/src/Squid.Api/Crypto/**` — reuso do embrulho duplo existente (família `0009`)
- `backend/src/Squid.Service/**` — wiring
- `backend/tests/Squid.Tests/Recovery/**` — testes dos ACs
- produto fora dos `touches`: qualquer lacuna é `D-01` para o ODEV

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| S-42 | no | fecha aqui por decisão humana 2026-11-20 (rodada 1): junto com a rotação; fora das ACs da TASK-0051 |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-20 21:50:58 | odev | task created from SPEC-0012 (S-42: copia 0009 no import quando Squid:RecoveryKey existe; fecha S-42; blockedBy TASK-0058)
- 2026-11-22 10:30:00 | odev | dispatch DEV (runId R-20261120-01, sequencia 1 -> 2, branch task/TASK-0059-import-kit-copia-instancia desde develop; encadeamento automatico - TASK-0058 DONE com QA-0058 PASSED 8/8, S-39 fechado)
- 2026-11-22 11:14:28 | dev | feasibility round 1 fechada sem duvida bloqueante: ImportAsync reusa RuntimeSigningKeyRing.WrapInstanceCopy + IInstanceSigningKeyStore (upsert INSERT OR REPLACE, PK handle) - toques confirmados (Crypto/Service ja com wiring da TASK-0051, sem mudanca necessaria); baseline pre-existente Recovery+DoubleWrap+Ring 31/31 exit 0
- 2026-11-22 11:14:28 | dev | branch task/TASK-0059-import-kit-copia-instancia criada desde develop antes do trabalho (gate 2026-11-11 20:42:28 modelo C)
- 2026-11-22 11:14:29 | dev | AC-01..AC-04 implementados em RecoveryEndpoints.ImportAsync: com Squid:RecoveryKey a copia 0009 da RSA do ator importado e embrulhada (bytes so em memoria, linha gravada so depois que a identidade existe, nunca no caminho 409); sem a config instanceCopy nulo e a resposta fica byte-identica
- 2026-11-22 11:14:29 | dev | 4 testes novos em backend/tests/Squid.Tests/Recovery/ImportInstanceCopyTests.cs (AC-01 copia+federacao de saida, AC-02 byte-identico sem config, AC-03 nunca em claro/log/rota, AC-04 idempotencia B-14) - filtro FullyQualifiedName~ImportInstanceCopyTests exit 0 (4/4)
- 2026-11-22 11:15:37 | dev | gate AC-05: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test exit 0 - 645/645 (641 pre-existentes + 4 novos), escopo so em toques (RecoveryEndpoints.cs + Recovery/ImportInstanceCopyTests.cs), zero pacote NuGet novo
- 2026-11-22 11:19:32 | dev | commit unico 77d1a44 na branch task/TASK-0059-import-kit-copia-instancia (2 arquivos de produto; o task file fica para o ODEV commitar no DONE, como na TASK-0058) - sem push, main intacta, nenhuma branch apagada
- 2026-11-22 11:22:16 | odev | ODEV: verificacao do step result DONE - logLines/evidenceRows gravados pelo DEV via sdd-state (check exit 0); gate 645/645 exit 0 (641 + 4 novos), ImportInstanceCopyTests 4/4, baseline Recovery+DoubleWrap+Ring 31/31, 2 arquivos de produto nos touches, zero pacote novo, commit 77d1a44 na branch
- 2026-11-22 11:22:16 | odev | ODEV: feasibility 1/2 sem duvida bloqueante (reusa WrapInstanceCopy + upsert 0009 ja existentes desde a TASK-0051); S-42 fechado no codigo - aguarda verificacao da QA
- 2026-11-22 11:22:16 | odev | ODEV: transicao IN_DEVELOPMENT -> AWAITING_QA, dispatch QA-0059
- 2026-11-22 12:43:24 | qa | QA-0059 abertura: gate sdd-test.ps1 test exit 0 - 645/645 (641 baseline + 4 da TASK-0059); familia FullyQualifiedName~ImportInstanceCopy exit 0 - 6/6 (4 do DEV + 2 novos da QA: caminho 409 + OBS-01 rotacao)
- 2026-11-22 12:43:24 | qa | QA-0059 filtro da familia ImportInstanceCopy exit 0 - 6/6 (4 do DEV + 2 cenarios novos da QA executados e verdes)
- 2026-11-22 12:43:25 | qa | QA-0059 verificacao do driver de mutacao: -VerifyOnly exit 0 - 5/5 achados com hits=1 e ASCII 0
- 2026-11-22 12:43:26 | qa | QA-0059 mutacoes execucao 1: exit 1 - 5/5 EXPECT-OK na substancia, porem o rotulo GREEN do proprio driver estava invertido (ferramenta da QA, corrigida antes do ledger fechar; run1 preservada)
- 2026-11-22 12:43:27 | qa | QA-0059 mutacoes execucao 2 (ledger valido): exit 0 - SUMMARY cycles=5 failed=0 green-aprovado=6; M4 com Ac02 VERDE prova o byte-identico do AC-02 contra o artefato do develop
- 2026-11-22 12:43:28 | qa | QA-0059 AC-05 escopo: qa-scope-task-0059.ps1 exit 0 - fails=0 (1 commit 77d1a44, 2/2 arquivos nos touches, zero pacote NuGet, blobs 2/2 identicos, sem residuo)
- 2026-11-22 12:43:29 | qa | QA-0059 Linux (podman sdk 10.0.403): build exit 0 e familia 6/6 exit 0; a etapa de gate completo saiu 1 com 51 falhas de ambiente
- 2026-11-22 12:43:30 | qa | QA-0059 Linux classificacao: exit 0 - suite completa 51 falhas / 596 pass / 647, conjunto identico a baseline pre-task (only-now=0, only-baseline=0), zero falha em Recovery/Import, familia 6/6
- 2026-11-22 12:43:31 | qa | QA-0059 fechamento: gate sdd-test.ps1 test exit 0 - 647/647 (645 do DEV + 2 novos da QA); veredito PASSED 5/5 ACs, nenhum achado de produto
- 2026-11-22 12:49:05 | odev | QA-0059 PASSED 5/5 (qaAttempt 1/2): AC-01..AC-05 todos PASS; 5 mutacoes can-fail EXPECT-OK (M1 copia+federa, M3 caminho 409, M4 byte-identico provado contra o artefato do develop, M5 varredura de segredos, M2 B-14) + 2 cenarios novos da QA (Conflict_path + OBS-01 rotacao x import, verdes); gates 645/645 abertura e 647/647 fechamento exit 0; familia 6/6 no Windows E no Linux; escopo 2/2 nos touches, zero pacote novo; commit QA c6100ef na branch (1 commit, sem push)
- 2026-11-22 12:49:05 | odev | QA-0059: 1 finding MINOR so de ferramenta da QA (rotulo GREEN invertido no driver na run1, corrigido antes do ledger fechar; run1 preservada em QA-0059-mutations-run1.log) - NENHUM achado de produto; Linux 51 falhas = baseline pre-task identica nome a nome (only-now=0, only-baseline=0)
- 2026-11-22 12:49:05 | odev | QA-0059: OBS-01 (rotacao x import) executada e verde - apos RotateTo em memoria o import embrulha com a chave corrente; memoryBankFlag proposto: contracts.md:22 descreve so o registro e precisa citar o import na consolidacao
- 2026-11-22 12:49:05 | odev | ODEV: S-42 FECHADO; transicao AWAITING_QA -> DONE; merge em develop + commit docs conforme gate modelo C; encaminha TASK-0060 (ultima nova)

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-22 11:17:38 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter (Recovery + DoubleWrap + RuntimeSigningKeyRing por OR - comando literal no cabecalho do log) | 0 | docs/sdd/runs/test-runs/20261122-104719-test-filter.log (baseline pre-existente 31/31 antes de qualquer edicao) |
| 2026-11-22 11:17:38 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~ImportInstanceCopyTests | 0 | docs/sdd/runs/test-runs/20261122-110245-test-filter.log (4/4 novos: AC-01 a AC-04) |
| 2026-11-22 11:17:39 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261122-111402-test-gate.log (AC-05: 645/645, gate inteiro com Category=BuildProof) |
| 2026-11-22 11:17:39 | dev | git status --short | 0 | produto so em toques: backend/src/Squid.Api/Recovery/RecoveryEndpoints.cs + backend/tests/Squid.Tests/Recovery/ImportInstanceCopyTests.cs; nenhum csproj alterado = zero pacote NuGet novo (AC-05) |
| 2026-11-22 11:19:33 | dev | git commit (unico, 2 arquivos de produto, mensagem TASK-0059: <resumo>) | 0 | commit 77d1a44 na branch task/TASK-0059-import-kit-copia-instancia desde develop |
| 2026-11-22 12:43:24 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261122-114334-test-gate.log (abertura 645/645 exit 0) |
| 2026-11-22 12:43:25 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~ImportInstanceCopy | 0 | docs/sdd/runs/test-runs/20261122-114602-test-filter.log (6/6 exit 0) |
| 2026-11-22 12:43:26 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0059.ps1 -VerifyOnly | 0 | docs/sdd/qa/QA-0059-verify.log (5/5 achados, ASCII 0) |
| 2026-11-22 12:43:27 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0059.ps1 | 1 | docs/sdd/qa/QA-0059-mutations-run1.log (5/5 EXPECT-OK; rotulo GREEN invertido no driver, corrigido) |
| 2026-11-22 12:43:28 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0059.ps1 | 0 | docs/sdd/qa/QA-0059-mutations.log (cycles=5 failed=0 green-aprovado=6) |
| 2026-11-22 12:43:29 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-scope-task-0059.ps1 | 0 | docs/sdd/qa/QA-0059-scope.log (fails=0) |
| 2026-11-22 12:43:30 | qa | podman run --rm -v C:/dev/squid-squad-net/squid-execution:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash docs/sdd/qa/qa-linux-task-0059.sh | 1 | docs/sdd/qa/QA-0059-linux-test-importcopy.log (build 0, familia 6/6; gate completo 1 = 51 pre-existentes) |
| 2026-11-22 12:43:31 | qa | podman run --rm -v C:/dev/squid-squad-net/squid-execution:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash docs/sdd/qa/qa-linux2-task-0059.sh | 0 | docs/sdd/qa/QA-0059-linux2-test-importcopy.log (gate classificado: 51 = baseline pre-task, only-now=0; familia 6/6 exit 0) |
| 2026-11-22 12:43:32 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261122-122607-test-gate.log (647/647 exit 0) |
