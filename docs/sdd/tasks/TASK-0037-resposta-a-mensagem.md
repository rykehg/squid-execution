<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0037 — Resposta à mensagem (`replyToId`)

```yaml
runId: R-20261111-01
taskId: TASK-0037
status: DONE
agent: odev
phase: P-05
sequence: 2
createdAt: 2026-11-11 23:45:07
updatedAt: 2026-11-11 23:45:07
parent: SPEC-0010
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Api/Threads/**", "backend/src/Squid.Service/**", "backend/src/Squid.Persistence/schema/**", "backend/tests/Squid.Tests/Threads/**"]
blocks: ["TASK-0038"]
blockedBy: ["TASK-0036"]
inputArtifacts: ["docs/sdd/specs/SPEC-0010-mensageria-p05.md", "docs/sdd/plan/plan.md", "specs/001-squid-mvp/spec.md", "specs/001-squid-mvp/contracts/http-api.md"]
outputArtifacts: ["backend/src/Squid.Api/Threads/**", "backend/src/Squid.Service/**"]
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
nextAction: DONE - QA-0037 PASSED (4/4 ACs, 10 mutacoes, gate 462/462); merge em develop + docs; dispatch DEV da TASK-0038
evidence: []
memoryBankFlags: []
```

## Intent

FR-057: responder a uma mensagem específica dentro da conversa. `replyToId` só aceita mensagem
da mesma thread (outra thread ou inexistente → `422 invalid_reply_target`, §9.9) e a referência
nunca amplia a visibilidade — não-participante segue em 404 idêntico.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `POST .../messages` com `replyToId` de mensagem da **mesma thread** cria a resposta e
  o envelope de leitura devolve a referência — teste.
- AC-02: `replyToId` de outra thread ou de mensagem inexistente → `422 invalid_reply_target`
  (§9.9) — teste.
- AC-03: resposta válida **não muda a visibilidade**: não-participante continua recebendo 404
  byte-idêntico em GET/POST da thread (FR-057) — teste.
- AC-04: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`
  exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Api/Threads/**`
- `backend/src/Squid.Service/**`
- `backend/tests/Squid.Tests/Threads/**`

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| — | no | resposta é só referência (`replyToId`), sem thread de resposta nem profundidade (diferente do post, §2.5) |
| GAP-37 | no | envio segue o gate de aceite 428 da TASK-0036 |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-11 23:45:07 | odev | task created from SPEC-0010 (entrega 5 de 8 da P-05; blockedBy TASK-0036; FR-057 + §9.9)
- 2026-11-12 05:18:23 | odev | touches emendado pelo ODEV preventivamente: backend/src/Squid.Persistence/schema/** entra no YAML (3a ocorrencia do erro do dispatch em 0034/0036 - esta task precisara de migration: coluna/tabela nova); nenhuma AC muda
- 2026-11-12 06:03:30 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; blockedBy TASK-0036 resolve (DONE com QA-0036 PASSED, gate 456/456, merge 7daa62d em develop + docs fc46b49); touches ja emendado com schema/** (migration replyToId); autorizacao de commit gate PHASE 2026-11-11 20:42:28 (modelo C) incluida - nova branch task/TASK-0037-resposta-a-mensagem desde develop
- 2026-11-12 06:14:25 | dev | feasibilidade 1/2: entrada pelo task file; unidades = ThreadEndpoints.MessageSendAsync, IThreadStore/SqliteThreadStore.AppendMessageAsync, MessageView, schema/** (nova 0005); touches correto e completo (schema/** emendado pelo ODEV), sem interseccao com task aberta; memoria banc ausente [GAP: memory bank absent]; baseline gate 456/456 exit 0 antes de qualquer edicao
- 2026-11-12 06:14:27 | dev | ordenacao decidida e registrada (D-01 nao bloqueante, fechada pelo dispatch + clausula do contrato): escada do envio 401 -> 404 -> 403 -> 400 -> 428 -> 422, ou seja 428 ANTES de 422 porque o contrato §6 diz em clausula absoluta que sem aceite o envio responde 428; envio sem aceite e com replyToId invalido responde 428, e o 422 invalid_reply_target so vale com aceite em vigor; validacao de replyToId entra DENTRO da transacao do append, junto ao gate do §9.6, para nenhuma estrada de envio escapar (padrao da TASK-0036)
- 2026-11-12 06:14:29 | dev | formato do 422 da casa confirmado: PostEndpoints.cs usa AuthErrors.Write(context, 422, invalid_reply_target, msg) para reply de post, mesma familia §9.9; envelope devolve replyToId null quando nao ha resposta (mesma convencao de PostView)
- 2026-11-12 06:23:35 | dev | branch task/TASK-0037-resposta-a-mensagem criada a partir do develop (autorizacao de commit gate PHASE 2026-11-11 20:42:28); implementacao: migration 0005_message_reply_to_id.sql (ALTER TABLE message ADD COLUMN reply_to_id, familia nova 0002/0003/0004 intactos por checksum), MessageState/AppendOutcome ganham ReplyToId e InvalidReplyTarget, validacao de replyToId DENTRO da transacao do append depois do gate 428, MessageView devolve replyToId (null quando nao ha resposta), rota POST .../messages passa request.ReplyToId e responde 422 invalid_reply_target
- 2026-11-12 06:23:37 | dev | testes novos: ThreadReplyRoutesTests (4 facts: roundtrip mesma thread com envelope, 422 de outra thread e de inexistente sem gravar nada, 428 antes de 422 na escada, 404 byte-identico do estrangeiro com replyToId valido no POST), ThreadStoreTests (gate antes do alvo + recusa sem queimar seq + referencia gravada), ThreadMigrationTests (0005 idempotente)
- 2026-11-12 06:30:45 | dev | AC-04 gate completo: 462/462 exit 0 (header exit: 0 do log do gate, 456 do baseline + 6 testes novos); diff restrito aos touches (Threads/**, Service/**, schema/**, tests/Threads/**), nada fora de escopo; commit unico na branch task/TASK-0037-resposta-a-mensagem com apenas os arquivos dos touches
- 2026-11-12 06:32:04 | dev | commit unico 6dfac85 na branch task/TASK-0037-resposta-a-mensagem (8 arquivos, todos nos touches: 4 de codigo, 1 migration 0005, 3 de teste); sem push, sem rebase, main intocada; docs/sdd/tasks ficaram fora do commit (odev os consolida, convencao das TASK-0033..0036)
- 2026-11-12 06:33:31 | odev | ODEV verificou antes de transicionar: gate final 20261112-063007-test-gate.log lido no arquivo (exit: 0, Aprovado 462, Com falha 0, 5m24s); commit 6dfac85 conferido via git show (8 files: 5 produto incl. migration 0005 + 3 teste, todos em touches, branch task/TASK-0037-resposta-a-mensagem); backend git status = 0; step result DONE aceito
- 2026-11-12 06:33:34 | odev | transicao IN_DEVELOPMENT -> AWAITING_QA
- 2026-11-12 06:34:10 | odev | step result aceito: D-01 ordem 428x422 fechada pela escada - 428 ANTES de 422 porque o contrato 6 e clausula absoluta (sem aceite o envio responde 428); 422 no formato da casa (PostEndpoints AuthErrors.Write); validacao de replyToId DENTRO da transacao do append - PADRAO_NOVO consolidado, 428 e 422 na mesma estrada, recusa nao queima seq; migration 0005 ALTER TABLE sem IF NOT EXISTS (SQLite nao tem) - idempotencia pela regra do SchemaMigrator provada pelo teste de re-aplicacao
- 2026-11-12 06:47:10 | qa | QA attempt 1/2 (runId R-20261111-01): entradas lidas (SPEC-0010 secao E, FR-057, contrato http-api 6 e 9 item 9, data-model 2.5.1, task AC-01..04) + memoria banc rota qa-integracao (index + build-run-test S-15/S-22/S-23); git baseline HEAD 6dfac85 na branch task/TASK-0037-resposta-a-mensagem; baseline filtrado Threads 30/30 exit 0
- 2026-11-12 06:52:05 | qa | 10 ciclos de mutacao executados e classificados dentro do log (S-22): M1/M2 alvo de outra thread e inexistente aceitos, M3 ordem 428-antes-do-422 invertida, M4 recusa que queima seq, M5 reply_to_id nao gravado, M6 envelope sem replyToId, M7 422 virando 409, M8 sonda de participante desligada na rota de envio, M9 404 com traceId, M10 guarda do ledger da migracao desligada - 10x RESTORE-OK hash=PRE residue=0, GREEN 30/30 exit 0, SUMMARY cycles=10 failed=0
- 2026-11-12 06:58:21 | qa | AC-04 gate completo apos o restore: docs/sdd/runs/test-runs/20261112-065700-test-gate.log com exit: 0 DENTRO do cabecalho (S-22) e Com falha 0 / Aprovado 462 / Total 462 em 4m44s; varredura de escopo QA-0037-scope.log: git status backend 0, blob-identity work=HEAD em 4/4 arquivos mutados, mtime desde a baseline = exatamente esses 4, residuo QA-MUTATION 0, fora-dos-touches 0
- 2026-11-12 07:04:24 | qa | veredito QA attempt 1/2: PASSED - 4/4 ACs provadas por cenario executado (AC-01 envelope replyToId, AC-02 422 nos dois casos + ordem 428-antes-do-422 + nada gravado, AC-03 404 byte-identico com replyToId no POST, AC-04 gate 462/462 exit 0 + escopo limpo); 10 mutacoes can-fail todas vermelhas classificadas no log e restauradas byte-exact; doc docs/sdd/qa/QA-0037-resposta-a-mensagem.md; zero achado de produto; unverified = suite Linux (WSL sem dotnet)
- 2026-11-12 07:06:55 | odev | ODEV verificou antes de transicionar: QA-0037-resposta-a-mensagem.md com verdicto PASSED lido no arquivo; gate final 20261112-065700-test-gate.log exit: 0 / Aprovado 462 / Com falha 0 / 4m44s; residuo QA-MUTATION = 0 (git grep backend); commit QA 67bec6c conferido (9 arquivos so docs/sdd/qa/, pai 6dfac85, sem push, main intacta); backend git status = 0 entradas, blob-identity 4/4; check OK - veredito aceito
- 2026-11-12 07:06:57 | odev | QA-0037 PASSED attempt 1/2: 4/4 ACs, 10 ciclos can-fail (M1 alvo de outra thread, M2 inexistente, M3 ordem 428x422 invertida, M4 recusa que queima seq, M5 reply_to_id nao gravado, M6 envelope sem campo, M7 422 virando 409, M8 sonda de participante, M9 404 com traceId, M10 ledger do migrador) todos vermelhos no log e RESTORE-OK; GREEN 30/30; unverified registrados (suite Linux/WSL sem dotnet, replyToId malformado por leitura, alvo tombstone inatingivel, concorrencia multi-cliente); flag GAP suite Linux carregada - mesmo achado das QA-0033..0036
- 2026-11-12 07:06:59 | odev | transicao AWAITING_QA -> DONE; TASK-0037 = entrega 5 de 8 da P-05 encerrada; merge em develop + docs pelo ODEV (gate modelo C); TASK-0038 desbloqueada

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-12 06:14:31 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261112-061357-test-gate.log (baseline 456/456) |
| 2026-11-12 06:23:40 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 0 | build exit 0 (avisos pre-existentes) |
| 2026-11-12 06:23:42 | dev | sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Threads | 0 | docs/sdd/runs/test-runs/20261112-062258-test-filter.log (30/30) |
| 2026-11-12 06:30:47 | dev | sdd-test.ps1 test -TestFilter (ThreadReplyRoutesTests + ThreadMigrationTests + ThreadStoreTests) | 0 | docs/sdd/runs/test-runs/20261112-062354-test-filter.log (12/12) |
| 2026-11-12 06:30:49 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261112-063007-test-gate.log (462/462, header exit: 0) |
| 2026-11-12 06:33:36 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (baseline feasibility) | 0 | docs/sdd/runs/test-runs/20261112-061357-test-gate.log (baseline 456/456) |
| 2026-11-12 06:33:37 | dev | sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Threads | 0 | docs/sdd/runs/test-runs/20261112-062258-test-filter.log (30/30) |
| 2026-11-12 06:33:39 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate final AC-04) | 0 | docs/sdd/runs/test-runs/20261112-063007-test-gate.log - exit: 0 / Aprovado 462 / Com falha 0 / 5m24s - VERIFICADO PELO ODEV lendo o arquivo |
| 2026-11-12 06:33:41 | dev | git show --stat --format= HEAD (pos-commit) | 0 | 6dfac85 - 8 files changed 460 insertions 31 deletions - 5 produto + 3 teste em touches - VERIFICADO PELO ODEV via git show |
| 2026-11-12 06:47:12 | qa | git rev-parse HEAD + git status --porcelain -uall + wsl command -v dotnet | 0 | docs/sdd/qa/QA-0037-git-baseline.txt (HEAD 6dfac85, branch task/TASK-0037, 2 task files do ODEV, dotnet no WSL = dotnet-missing) |
| 2026-11-12 06:47:14 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Threads | 0 | docs/sdd/runs/test-runs/20261112-064406-test-filter.log (exit: 0, Com falha 0, Aprovado 30, Total 30) |
| 2026-11-12 06:47:16 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0037.ps1 -VerifyOnly | 0 | stdout VERIFY total=10 bad=0 + BOM OK (UTF-8 BOM presente) |
| 2026-11-12 06:52:07 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0037.ps1 | 0 | docs/sdd/qa/QA-0037-mutations.log (10 PRE, 10 RUN vermelho classificado no log, 10 RESTORE-OK hash=PRE residue=0, GREEN 30/30 exit 0, SUMMARY cycles=10 failed=0) |
| 2026-11-12 06:58:23 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261112-065700-test-gate.log - exit: 0 / Com falha 0 / Aprovado 462 / Total 462 / 4m44s (header lido no arquivo) |
| 2026-11-12 06:58:24 | qa | pwsh varredura de escopo (git status + git hash-object vs rev-parse + mtime + residuo) | 0 | docs/sdd/qa/QA-0037-scope.log (git-backend 0, blob-identity 4/4 match=True, mtime-count=4, residue=0, fora-dos-touches=0) |
| 2026-11-12 07:04:20 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0037.ps1 | 0 | docs/sdd/qa/QA-0037-check.log (SUMMARY total=35 fails=0, inclui SDD-STATE check: OK) |
| 2026-11-12 07:04:22 | qa | wsl -e bash docs/sdd/qa/qa-check-task-0037.sh | 0 | docs/sdd/qa/QA-0037-check-sh.log (SUMMARY total=30 fails=0, gemeo Linux da parte estatica) |
