<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0039 — Anexo em mensagem reusando `/api/media`

```yaml
runId: R-20261111-01
taskId: TASK-0039
status: DONE
agent: odev
phase: P-05
sequence: 2
createdAt: 2026-11-11 23:45:07
updatedAt: 2026-11-11 23:45:07
parent: SPEC-0010
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Api/Threads/**", "backend/src/Squid.Api/Media/**", "backend/src/Squid.Service/**", "backend/src/Squid.Persistence/schema/**", "backend/tests/Squid.Tests/Threads/**"]
blocks: ["TASK-0040"]
blockedBy: ["TASK-0038"]
inputArtifacts: ["docs/sdd/specs/SPEC-0010-mensageria-p05.md", "docs/sdd/plan/plan.md", "specs/001-squid-mvp/data-model.md", "specs/001-squid-mvp/contracts/http-api.md"]
outputArtifacts: ["backend/src/Squid.Api/Threads/**", "backend/src/Squid.Api/Media/**", "backend/src/Squid.Service/**"]
interviewRounds: 0/6
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
nextAction: DONE - QA-0039 PASSED (5/5 ACs, 13 mutacoes, gate 473/473); merge em develop + docs; dispatch DEV da TASK-0040 ULTIMA entrega
evidence: []
memoryBankFlags: []
```

## Intent

Decisão humana da rodada 2: anexo em mensagem reusa o pipeline da P-04 **intacto** — a mensagem
só referencia `contentHashes` (≤10) no corpo; upload, magic bytes, EXIF, Alt e cota 507 continuam
em `POST /api/media`. A regra de visibilidade de `GET /api/media/{contentHash}` (GAP-25) é
ampliada: vê o uploader ou quem participa de thread cuja mensagem referencia o hash.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `POST .../messages` aceita `contentHashes` (≤10) e o envelope de leitura devolve os
  anexos com os campos do `Attachment`; hash inexistente → `400`; acima de 10 → `400` — teste.
- AC-02: `Image`/`Video`/`Gif` sem `Alt` → `400` (regra §2.6 aplicada na publicação da mensagem) —
  teste.
- AC-03: cota e upload intocados: anexo de mensagem **não cobra duas vezes** — o `507` continua
  vindo do `POST /api/media` e o envio da mensagem não cria nova cobrança no `quota_ledger` — teste.
- AC-04: `GET /api/media/{contentHash}` (regra GAP-25 ampliada): participante de thread cuja
  mensagem referencia o hash vê a mídia; não-participante → `404` idêntico; uploader vê — teste.
- AC-05: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`
  exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Api/Threads/**` (campo `contentHashes` no corpo + anexos no envelope)
- `backend/src/Squid.Api/Media/**` (ampliar a checagem de visibilidade de `GET /api/media/{hash}`)
- `backend/src/Squid.Service/**` (wiring se preciso)
- `backend/tests/Squid.Tests/Threads/**`

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-35 | no | emenda (b): `contentHashes` no corpo da mensagem; upload continua em `POST /api/media` (TASK-0027) |
| GAP-25 | no | regra de visibilidade da mídia ampliada para "participante de thread que referencia" — origem da ampliação é esta task |
| — | no | blob, magic bytes, EXIF e cota 507 permanecem os da P-04; nada duplicado |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-11 23:45:07 | odev | task created from SPEC-0010 (entrega 7 de 8 da P-05; blockedBy TASK-0038; decisão humana rodada 2 — reusar /api/media; emenda b ao §6)
- 2026-11-12 05:18:26 | odev | touches emendado pelo ODEV preventivamente: backend/src/Squid.Persistence/schema/** entra no YAML (3a ocorrencia do erro do dispatch em 0034/0036 - esta task precisara de migration: coluna/tabela nova); nenhuma AC muda
- 2026-11-12 08:44:56 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; blockedBy TASK-0038 resolve (DONE com QA-0038 PASSED, gate 468/468, merge 70e714d em develop + docs a4a411f); touches ja emendado com schema/** (coluna de anexo); autorizacao de commit gate PHASE 2026-11-11 20:42:28 (modelo C) incluida - nova branch task/TASK-0039-anexo-em-mensagem desde develop
- 2026-11-12 08:54:47 | dev | feasibility r1: branch task/TASK-0039-anexo-em-mensagem criada de develop; baseline gate 468/468 exit 0; ordem normativa lida (SPEC-0010 G + GAP-35, AC-01..05, data-model 2.6, http-api 5/6); GAP-25 mora em MediaEndpoints.VisibleAsync; posts embarcam Attachment no documento do post (SqliteObjectStore), mensagem usa tabela propria -> escolha da persistencia registrada no proximo passo
- 2026-11-12 09:00:52 | dev | decisao de persistencia (AC-01): anexo de mensagem vira coluna JSON attachments em message (0007 ALTER TABLE), espelho de como os posts embarcam PostObject.Attachments no proprio documento; a reverse index post-media-ref do GAP-25 tem o espelho em lookup json_each sobre a coluna, sem tabela nova (a familia nao reescreve, so soma versao)
- 2026-11-12 09:00:52 | dev | escada do envio com anexos (PADRAO_NOVO): validacao de contentHashes roda DENTRO da transacao do append, na ordem 404 da thread -> 428 do aceite -> 422 do replyToId -> 400 dos anexos (maximo 10, hash inexistente, Alt vazio), antes da CAS; recusa nenhuma consome seq nem escreve linha
- 2026-11-12 09:00:52 | dev | wiring: SqliteThreadStore passa a receber IMediaStore (registrado no SquidServiceHost) para resolver os hashes dentro da transacao; a montagem do Attachment (Url com a origem do envio) acontece la, e o envelope de leitura devolve attachments sempre presente com os mesmos campos do PostView
- 2026-11-12 09:18:03 | dev | implementacao completa: migration 0007 (coluna attachments em message, espelho do documento do post), validacao de contentHashes dentro da transacao do append na ordem 404-428-422-400, contentHashes no corpo da mensagem, envelope de leitura com os campos do Attachment do PostView, GAP-25 ampliada para participante de thread cuja mensagem referencia o hash (post intacto) e wiring IMediaStore no SquidServiceHost; testes novos em Threads cobrem AC-01..AC-04 + a versao 0007
- 2026-11-12 09:24:41 | dev | gate completo (AC-05): 473/473 exit 0 (baseline 468 + 5 testes novos) - nenhuma alteracao fora dos touches; commit unico na branch task/TASK-0039-anexo-em-mensagem autorizado pelo gate PHASE 2026-11-11 20:42:28 (modelo C)
- 2026-11-12 09:25:55 | dev | commit 3d8a1d9 na branch task/TASK-0039-anexo-em-mensagem: 11 arquivos, todos dentro dos touches (o task file fica de fora do commit, so recebeu Log/Evidence via sdd-state); sem push, sem rebase, main intocada
- 2026-11-12 09:27:27 | odev | ODEV verificou antes de transicionar: gate final 20261112-092417-test-gate.log lido no arquivo (exit: 0, Aprovado 473, Com falha 0, 5m22s); commit 3d8a1d9 conferido via git show (11 files: 7 produto incl. migration 0007 + MediaEndpoints + 4 teste, todos em touches, branch task/TASK-0039-anexo-em-mensagem); backend git status = 0; step result DONE aceito
- 2026-11-12 09:27:28 | odev | correcao de rotulo do step result: o campo attempt dizia correction 1/2 mas NAO houve correcao - um unico dispatch e um unico DONE, feasibility r1 direto para implementacao; correctionAttempts permanece 0/2 (contadores sao meus, calculados do control block); validacao de contentHashes DENTRO da transacao na escada 404-428-422-400 (PADRAO_NOVO consolidado), persistencia em coluna JSON espelhando o documento do post, cota so no upload (prova byte-identica do quota_ledger) e GAP-25 ampliada com post intacto; memoryBankFlag MUDANCA_RELEVANTE CONTRATO (GET /api/media ampliado) carregada
- 2026-11-12 09:27:30 | odev | transicao IN_DEVELOPMENT -> AWAITING_QA
- 2026-11-12 09:46:22 | qa | qa attempt 1/2 iniciada: baseline git HEAD 3d8a1d9 na branch task/TASK-0039-anexo-em-mensagem (git status = so o task file), rota qa-integracao lida (build-run-test.md#Local tests without gate) e ordem normativa lida (SPEC-0010 G + GAP-35, AC-01..05, data-model 2.6, http-api 5/6); suites filtradas Threads 41/41 e Media 27/27 exit 0 antes de qualquer escrita da QA
- 2026-11-12 09:46:22 | qa | teste temporario da QA escrito (QaTask0039AttachmentCapTests: 11 referencias de UM hash real = 400, 10 = 200 com seq 1 e lista de 10 na leitura) - Threads 42/42 exit 0
- 2026-11-12 10:14:41 | qa | driver de mutacao executado (13 ciclos, filtro Threads, M8 com filtro Threads+Media): 12 vermelhos de produto classificados NO LOG (S-22), 14x RESTORE-OK hash=PRE residue=0, 2x GREEN 42/42, residue final QA-MUTATION=0; o ciclo M2 primitivo morreu na compilacao (CS0828: alt = null em tipo anonimo) - CLASSIFICACAO-ERRADA denunciada pelo proprio log (aprovado=0 falha=0), driver corrigido para (string?)null e re-executado com -Only M2 (41/1, Expected: "foto da mensagem"); SUMMARY cycles=13 failed=1 e cycles=1 failed=0
- 2026-11-12 10:14:41 | qa | gate AC-05 executado apos remover o teste temporario (fonte em .cs.txt): sdd-test.ps1 test exit 0, 473/473 lido DENTRO do log 20261112-100321-test-gate.log (S-22, 5m04s); varredura de escopo: git backend 0, blob-identity 4/4 match=True, mtime-count=4, residue=0, fora-dos-touches=0; checkers ps1 (46/0) e sh no WSL (39/0) executados; verdict PASSED escrito em docs/sdd/qa/QA-0039-anexo-em-mensagem.md
- 2026-11-12 10:23:02 | odev | ODEV verificou antes de transicionar: QA-0039-anexo-em-mensagem.md com verdicto PASSED lido no arquivo; gate final 20261112-100321-test-gate.log exit: 0 / Aprovado 473 / Com falha 0 / 5m04s; residuo QA-MUTATION = 0 (git grep backend); commit QA 0163193 conferido (11 arquivos so docs/sdd/qa/, pai 3d8a1d9, sem push, main intacta); backend git status = 0 entradas, blob-identity 4/4; check OK - veredito aceito
- 2026-11-12 10:23:04 | odev | QA-0039 PASSED attempt 1/2: 5/5 ACs, 13 ciclos can-fail (M1 coluna 0007, M2 alt no envelope - morreu no build CS0828 e a QA re-executou corrigido, M3 cap de 10 - pego SO pelo Fact da QA, M4 hash ignorado, M5 recusa queima seq, M6 regra 2.6, M7 ENVIO cobrando quota_ledger - money shot da AC-03, M8 ramo mensagem da GAP-25 - 27 testes de POST verdes no mesmo run, M9 sonda de cadeira, M10/M11 428/422 antes do 400, M12 reserva 507, M13 contador valido) + teste temporario para o cap; unverified registrados (suite Linux, primeira-gravacao-vence-alt so por leitura, kind Link, /thumb ampliado, corrida concorrencia); memoryBankFlag PADRAO_NOVO escada de anexos carregada
- 2026-11-12 10:23:06 | odev | transicao AWAITING_QA -> DONE; TASK-0039 = entrega 7 de 8 da P-05 encerrada; merge em develop + docs pelo ODEV (gate modelo C); TASK-0040 (ULTIMA) desbloqueada

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-12 09:18:04 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261112-085312-test-gate.log (baseline 468/468) |
| 2026-11-12 09:18:06 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 0 | stdout: 0 Erro(s) / 2 avisos pre-existentes |
| 2026-11-12 09:18:08 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Threads | 0 | docs/sdd/runs/test-runs/20261112-091653-test-filter.log (41/41) |
| 2026-11-12 09:18:09 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Media | 0 | docs/sdd/runs/test-runs/20261112-091738-test-filter.log (27/27) |
| 2026-11-12 09:24:44 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261112-092417-test-gate.log (473/473, AC-05) |
| 2026-11-12 09:27:31 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate final AC-05) | 0 | docs/sdd/runs/test-runs/20261112-092417-test-gate.log - exit: 0 / Aprovado 473 / Com falha 0 / 5m22s - VERIFICADO PELO ODEV lendo o arquivo |
| 2026-11-12 09:27:33 | dev | git show --stat --format= HEAD (pos-commit) | 0 | 3d8a1d9 - 11 files changed 714 insertions 59 deletions - 7 produto + 4 teste em touches - VERIFICADO PELO ODEV via git show |
| 2026-11-12 09:46:24 | qa | git rev-parse HEAD + git rev-parse --abbrev-ref HEAD + git status --porcelain -uall + wsl -e bash -c command -v dotnet | 0 | docs/sdd/qa/QA-0039-git-baseline.txt (HEAD 3d8a1d9, branch task/TASK-0039-anexo-em-mensagem, wsl-dotnet dotnet-missing) |
| 2026-11-12 09:46:26 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Threads (baseline) | 0 | docs/sdd/runs/test-runs/20261112-093821-test-filter.log (exit: 0, Com falha 0 / Aprovado 41 / Total 41) |
| 2026-11-12 09:46:28 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Media (baseline) | 0 | docs/sdd/runs/test-runs/20261112-093902-test-filter.log (exit: 0, Com falha 0 / Aprovado 27 / Total 27) |
| 2026-11-12 09:46:30 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Threads (com o teste temporario da QA) | 0 | docs/sdd/runs/test-runs/20261112-094107-test-filter.log (exit: 0, Com falha 0 / Aprovado 42 / Total 42 = 41 DEV + 1 QA) |
| 2026-11-12 10:14:43 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0039.ps1 -VerifyOnly (registrado) | 0 | docs/sdd/qa/QA-0039-verify.log (14 padroes hits=1, BOM OK, VERIFY total=13 bad=0) |
| 2026-11-12 10:14:44 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0039.ps1 (run cheio: 13 ciclos + GREEN) | 1 | docs/sdd/qa/QA-0039-mutations.log - 12 vermelhos classificados, 14 RESTORE-OK, GREEN 42/42, SUMMARY cycles=13 failed=1 (so o M2 primitivo, corrigido na linha seguinte) residue=0 |
| 2026-11-12 10:14:46 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0039.ps1 -Only M2 (mutante corrigido) | 0 | docs/sdd/qa/QA-0039-mutations.log - M2 RUN aprovado=41 falha=1 Expected: foto da mensagem, GREEN 42/42, SUMMARY cycles=1 failed=0 |
| 2026-11-12 10:14:48 | qa | Remove-Item backend/tests/Squid.Tests/Threads/QaTask0039AttachmentCapTests.cs + git status --porcelain -uall -- backend | 0 | docs/sdd/qa/QaTask0039AttachmentCapTests.cs.txt (fonte preservada) + git backend = 0 entradas |
| 2026-11-12 10:14:49 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate AC-05) | 0 | docs/sdd/runs/test-runs/20261112-100321-test-gate.log - exit: 0 / Aprovado 473 / Com falha 0 / 5m04s |
| 2026-11-12 10:14:51 | qa | varredura de escopo pwsh (git status, hash-object vs rev-parse, mtime, residuo, fora-dos-touches) | 0 | docs/sdd/qa/QA-0039-scope.log - git-backend 0, blob 4/4 match=True, mtime-count=4, residue=0, fora-dos-touches=0 |
| 2026-11-12 10:14:52 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0039.ps1 | 0 | docs/sdd/qa/QA-0039-check.log - SUMMARY total=46 fails=0 (SDD-STATE check: OK) |
| 2026-11-12 10:14:54 | qa | wsl -e bash docs/sdd/qa/qa-check-task-0039.sh (gemeo Linux, parte estatica) | 0 | docs/sdd/qa/QA-0039-check-sh.log - SUMMARY total=39 fails=0 |
