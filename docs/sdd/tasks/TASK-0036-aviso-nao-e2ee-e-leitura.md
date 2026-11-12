<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0036 — Aviso não-E2EE (428) e marcação de leitura

```yaml
runId: R-20261111-01
taskId: TASK-0036
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
blocks: ["TASK-0037"]
blockedBy: ["TASK-0035"]
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
nextAction: DONE - QA-0036 PASSED (6/6 ACs, 15 mutacoes, gate 456/456); merge em develop + docs; dispatch DEV da TASK-0037
evidence: []
memoryBankFlags: []
```

## Intent

Consentimento informado (FR-032): sem aceite do aviso de não-E2EE a primeira mensagem responde
`428 precondition_required` (§9.6); com aceite registrado por (ator, thread) o envio passa. E
estado de entrega (FR-033): `POST .../read` grava `lastReadSeq` monotônico, visível só a
participantes. A UI do aviso é P-08 (GAP-37).

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `POST .../ack-no-e2ee` registra o aceite por (ator, thread) e é idempotente (repetir
  não duplica nem quebra) — teste.
- AC-02: `POST .../messages` **sem aceite → `428 precondition_required`**; depois do aceite,
  mesmo envio → `200` (§9.6); aceite de um ator não libera outro ator — teste.
- AC-03: `POST .../read` grava `lastReadSeq` até `seq`; marcador **só avança** (valor menor é
  ignorado, não retrocede); `seq` acima do total da thread → `400` (GAP-38) — teste.
- AC-04: `GET /api/threads` e o envelope de mensagens expõem `lastReadSeq` por participante
  **visível só a participantes**; não-participante → 404 idêntico sem o dado — teste.
- AC-05: ator `Suspended` não envia mensagem (SPEC-0010 §Rules, data-model §3.1 — mesma família
  do post): envio negado e registrado (log estruturado), participante ativo segue enviando —
  teste. **AC acrescentada pelo ODEV 2026-11-12 02:25:21**: regra da spec sem task dona,
  reportada como `notDone` pelo DEV da TASK-0034; sem mudança de contrato.
- AC-06: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`
  exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Api/Threads/**`
- `backend/src/Squid.Service/**`
- `backend/src/Squid.Persistence/schema/**` — **emenda do ODEV 2026-11-12 05:16:02**: a migration
  `0004` (tabelas de aceite e marcador) é exigida pelas AC-01/AC-03; meu dispatch declarava este
  path mas o bloco de controle não o listava (3ª ocorrência do mesmo erro do ODEV — os YAMLs de
  TASK-0037/0038/0039 também foram emendados preventivamente); nenhuma AC muda
- `backend/tests/Squid.Tests/Threads/**`

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-33 | no | entregue = persistido (200 + seq); lido = lastReadSeq por participante; sem estado por dispositivo |
| GAP-37 | no | registro do aceite por (ator, thread) aqui; exibição do aviso na UI entra na P-08 |
| GAP-38 | no | marcador monotônico; seq > total → 400; 404 idêntico para não-participante |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-11 23:45:07 | odev | task created from SPEC-0010 (entrega 4 de 8 da P-05; blockedBy TASK-0035; FR-032/FR-033 + §9.6; decisão humana lastReadSeq da rodada 1)
- 2026-11-12 02:27:14 | odev | AC-05 (ator Suspenso nao envia mensagem) acrescentada pelo ODEV e a antiga AC-05 (gate) virou AC-06: regra da SPEC-0010 Rules/section D sem task dona, reportada como notDone pelo DEV da TASK-0034; nenhuma AC anterior muda de sentido, so renumeracao do gate
- 2026-11-12 04:33:30 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; blockedBy TASK-0035 resolve (DONE com QA-0035 PASSED, gate 450/450, merge 5ea16a4 em develop + docs 538310b); AC-05 (ator Suspenso) acrescentada pelo ODEV antes do dispatch, antiga AC-05 gate virou AC-06; autorizacao de commit gate PHASE 2026-11-11 20:42:28 (modelo C) incluida - nova branch task/TASK-0036-aviso-nao-e2ee-e-leitura desde develop
- 2026-11-12 05:18:08 | odev | ODEV verificou antes de transicionar: gate final 20261112-051348-test-gate.log lido no arquivo (exit: 0, Aprovado 456, Com falha 0, 4m36s); commit 6914bb3 conferido via git show (9 files: 4 produto + 5 teste, todos em touches emendado, branch task/TASK-0036-aviso-nao-e2ee-e-leitura); backend git status = 0; step result DONE aceito - gate 428 dentro da transacao do append (PADRAO_NOVO), marcador monotonico, 403 suspenso com linha Deny ANTES da resposta e ordem 403-antes-do-428, parser reordenado sem regredir rotas da TASK-0035
- 2026-11-12 05:18:10 | odev | correcao do ODEV (3a ocorrencia do mesmo erro): touchedOutsidePlan do DEV (0004_no_e2ee_ack_and_read.sql) - meu dispatch declarava schema/** mas os touches literais nao continham; YAML + secao Touches emendados; YAMLs de TASK-0037/0038/0039 emendados PREVENTIVAMENTE (replyToId = coluna, reacao = tabela, anexo = coluna - as tres precisarao de migration); nenhuma AC muda
- 2026-11-12 05:18:11 | odev | flags do step result: memoryBankFlags [MUDANCA_RELEVANTE CONTRATO - lastReadSeq no envelope de GET messages (exemplo do contrato 6 desatualizado)] e [PADRAO_NOVO - precondicao de envio checada dentro da transacao do append] carregadas para a consolidacao; D-01/D-02 fechadas pela escada (contrato 6/8/9.6 + SPEC-0010 D); 2 vermelhos proprios do DEV (constante 428 inexistente, expectativas de teste) corrigidos no ciclo sem defeito de produto
- 2026-11-12 05:18:13 | odev | transicao IN_DEVELOPMENT -> AWAITING_QA
- 2026-11-12 05:19:34 | dev | dev | feasibility r1: unit=ThreadEndpoints (catch-all POST/GET + ThreadView) e IThreadStore/SqliteThreadStore, dados=duas tabelas novas na 0004 (0002/0003 imutaveis por checksum; schema/** emendado no dispatch); baseline sdd-test.ps1 test 450/450 exit 0; touches confirmado e disjointo de tasks abertas; D-01 e D-02 nao-bloqueantes fechadas pela escada (contrato 6/8/9.6 + SPEC-0010 D + precedente da casa); branch task/TASK-0036 criada desde develop ANTES de qualquer edicao
- 2026-11-12 05:19:36 | dev | dev | implementacao: migration 0004_no_e2ee_ack_and_read.sql (thread_no_e2ee_ack (thread,actor) com notice+acked_at; thread_read (thread,actor) last_read_seq); store ganha AckNoE2eeAsync idempotente (INSERT OR IGNORE), MarkReadAsync que so avanca, LastReadSeqAsync e ListForAsync com LEFT JOIN do marcador do chamador; AppendMessageAsync devolve AppendResult e o gate do 9.6 vive DENTRO da transacao antes de alocar seq - recusa por ator, nao queima numero, thread ausente decidida antes (404 byte-identico preservado); ThreadEndpoints ganha as rotas ack-no-e2ee e read no catch-all, 428 precondition_required, 403 policy_denied + linha Deny (action Message, policyVersion 0, motivo ator suspenso nao envia mensagem) na trilha de PolicyAudit ANTES da resposta para ator Suspended (precedente de PostEndpoints), e lastReadSeq no envelope de mensagens
- 2026-11-12 05:19:37 | dev | dev | decisoes de ordem: 401 -> 404 probe FR-031 -> 403 suspenso (antecede o 428: ator suspenso e negado como tal mesmo sem aceite) -> 400 conteudo -> 428/200 no append; no parser o marcador /participants/ foi colocado antes dos novos sufixos para nao regredir a remocao de um participante chamado read (rotas da TASK-0035 intactas)
- 2026-11-12 05:19:39 | dev | dev | testes: 6 fatos novos (4 de rota AC-01..AC-05, 1 da store, 1 da migration) e correcao dos testes que ja enviavam sem aceite - ThreadMessageRoutesTests (aceite do criador em DirectThreadAsync + aceite do par na corrida), ThreadParticipantRoutesTests (helper SendAsync aceita antes de cada envio), ThreadStoreTests (aceites antes dos appends + unwrap do AppendResult); ciclos proprios: 1 com erro de compilacao (Status428PreconditionFailed nao existe -> Status428PreconditionRequired) e 1 com 3 falhas de expectativa do proprio teste (enum 428, suspensao nao gravada no cenario, marcador esperado 1 em vez de 2) - corrigidos no ciclo, nenhum defeito de produto; filtro Threads 24/24 exit 0
- 2026-11-12 05:19:41 | dev | dev | gate final AC-06: sdd-test.ps1 test -> exit 0 (cabecalho exit: do proprio log), 456/456 = baseline 450 + 6 novos, 4m36s; um gate intermediario foi descartado e reiniciado porque editei o parser durante a execucao - o gate valido roda sobre a arvore final; git status mostra so o task file (edit do ODEV, nao commitado) fora dos 9 arquivos de touches
- 2026-11-12 05:19:43 | dev | dev | commit unico autorizado 6914bb3 na branch task/TASK-0036-aviso-nao-e2ee-e-leitura: 9 files changed 890 insertions(+), 75 deletions(-), todos em touches (git show --stat conferido); 1 commit a frente de develop, sem push, sem rebase, main intacta em b9fd628
- 2026-11-12 05:34:09 | qa | entry qa-task attempt 1/2 (control block qaAttempts 0/2, status AWAITING_QA): normativa lida (SPEC-0010 D + GAP-33/37/38, FR-032/033, http-api 6/8/9.6, data-model 3.1); branch task/TASK-0036-aviso-nao-e2ee-e-leitura HEAD 6914bb3 conferido antes de escrever; baseline git em QA-0036-git-baseline.txt (so os 4 task files do ODEV modificados); teste temporario QaTask0036RouteCollisionTests escrito (colisao /participants/ x sufixo /read + texto do aviso FR-032 na linha de aceite)
- 2026-11-12 05:39:01 | qa | ciclos de mutacao iniciados em background (job pwsh-3b9f07c453074571): 15 alvos M1..M15 (428-status, recusa-queima-seq, gate-por-ator, idempotencia do aceite, marcador monotonico, seq>total 400, marcador por participante na lista e no envelope, portao da rota read, 404 byte-identico com traceId, escada 403-antes-do-428, linha Deny da trilha, parser /read-x-/participants/, texto do aviso FR-032, seq negativo 400), filtro FullyQualifiedName~Squid.Tests.Threads, -VerifyOnly 15/15 + BOM OK antes do run
- 2026-11-12 05:48:08 | qa | ciclos concluidos: 15/15 vermelhos classificados dentro do proprio log (M1 428-status; M2 recusa-queima-seq 2 falhas; M3 gate-por-ator 2; M4 idempotencia PK 3 com constraint; M5 marcador-monotonico 2; M6 seq>total 400; M7 lista-por-participante; M8 envelope-por-participante 2; M9 portao-read; M10 404-com-traceId 5 (byte-identica cai em 3 suites); M11 escada 403-antes-do-428; M12 linha-Deny; M13 parser /read-x-participants/ pego SO pelo teste temporario da QA; M14 texto-do-aviso; M15 seq-negativo 400), 15 RESTORE-OK hash=PRE residue=0, GREEN 25/25 exit 0, driver exit 0; dotnet no WSL sonrado ao vivo (command -v dotnet -> missing)
- 2026-11-12 05:59:39 | qa | verdict PASSED (attempt 1/2): AC-01..AC-06 todos provados por cenario EXECUTADO (baseline 25/25, gate 456/456 exit: 0 dentro do log), 15 mutacoes can-fail todas vermelhas e restauradas byte-exact, checkers ps1 29/29 e sh 26/26; nenhum achado de produto; unverified = suite Linux (dotnet ausente no WSL, sonrado ao vivo); escrita: docs/sdd/qa/QA-0036-aviso-nao-e2ee-e-leitura.md + driver/checkers/logs; commit unico autorizado segue (so docs/sdd/qa/*)
- 2026-11-12 06:02:22 | odev | ODEV verificou antes de transicionar: QA-0036-aviso-nao-e2ee-e-leitura.md com verdicto PASSED lido no arquivo; gate final 20261112-055345-test-gate.log exit: 0 / Aprovado 456 / Com falha 0 / 5m05s; residuo QA-MUTATION = 0 (git grep backend); commit QA f455d50 conferido (10 arquivos so docs/sdd/qa/, pai 6914bb3, sem push, main intacta b9fd628); backend git status = 0 entradas, blob-identity 2/2; check OK - veredito aceito
- 2026-11-12 06:02:24 | odev | QA-0036 PASSED attempt 1/2: 6/6 ACs, 15 ciclos can-fail (M1 status 428, M2 recusa NAO queima seq, M3 gate por ator, M4 idempotencia PK, M5 marcador monotonico, M6 seq>total, M7/M8 marcador por participante lista+envelope, M9 portao da rota read, M10 404 byte-identica sem traceId, M11 escada 403-antes-do-428, M12 linha Deny, M13 parser /read-x/participants/, M14 texto do aviso FR-032, M15 seq negativo) - M13 e M14 pegos SO pelo teste temporario da QA; 15 RESTORE-OK hash=PRE; checkers 29/29 e 26/26; unverified registrados (suite Linux, ordem 400x403 sem assert, corrida 428xappend multi-cliente)
- 2026-11-12 06:02:26 | odev | finding 3 da QA adjudicado (escada da especificacao): colisao de parser (participante chamado read) e texto do aviso FR-032 ficaram sem teste permanente do DEV - vira AC-07 da TASK-0040 (facet, testes), NAO backlog: e cobertura de comportamento desta fase, nao item futuro
- 2026-11-12 06:02:27 | odev | transicao AWAITING_QA -> DONE; TASK-0036 = entrega 4 de 8 da P-05 encerrada; merge em develop + docs pelo ODEV (gate modelo C); TASK-0037 desbloqueada

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-12 05:18:14 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate final AC-06) | 0 | docs/sdd/runs/test-runs/20261112-051348-test-gate.log - exit: 0 / Aprovado 456 / Com falha 0 / 4m36s - VERIFICADO PELO ODEV lendo o arquivo |
| 2026-11-12 05:18:16 | dev | git show --stat --format= HEAD (pos-commit) | 0 | 6914bb3 - 9 files changed 890 insertions 75 deletions - 4 produto + 5 teste em touches emendado - VERIFICADO PELO ODEV via git show |
| 2026-11-12 05:19:44 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (baseline feasibility) | 0 | docs/sdd/runs/test-runs/20261112-045156-test-gate.log - baseline 450/450 |
| 2026-11-12 05:19:46 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Threads (1a) | 1 | docs/sdd/runs/test-runs/20261112-050212-test-filter.log - erro proprio de compilacao (constante 428) |
| 2026-11-12 05:19:48 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Threads (2a) | 1 | docs/sdd/runs/test-runs/20261112-050320-test-filter.log - 21/24, 3 falhas de expectativa do proprio teste |
| 2026-11-12 05:19:49 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Threads (3a) | 0 | docs/sdd/runs/test-runs/20261112-050506-test-filter.log - 24/24 |
| 2026-11-12 05:19:51 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Threads (4a apos reordenar parser) | 0 | docs/sdd/runs/test-runs/20261112-050829-test-filter.log - 24/24 |
| 2026-11-12 05:34:11 | qa | git rev-parse branch+HEAD e git status -uall antes da QA escrever | 0 | docs/sdd/qa/QA-0036-git-baseline.txt - branch task/TASK-0036, HEAD 6914bb3, 4 task files do ODEV |
| 2026-11-12 05:35:23 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Threads (baseline com o teste temporario da QA) | 0 | docs/sdd/runs/test-runs/20261112-053508-test-filter.log - exit: 0 no cabecalho - Com falha 0 / Aprovado 25 / Total 25 (24 DEV + 1 QA) |
| 2026-11-12 05:38:31 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0036.ps1 -VerifyOnly (contagem dos 15 padroes + BOM) | 0 | stdout: VERIFY total=15 bad=0 + BOM OK (UTF-8 BOM presente) |
| 2026-11-12 05:48:06 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0036.ps1 (15 ciclos can-fail) | 0 | docs/sdd/qa/QA-0036-mutations.log - SUMMARY cycles=15 failed=0 residue-QA-MUTATION=0 temp-backups=0 - 15 vermelhos classificados NO LOG (S-22), 15 RESTORE-OK hash=PRE, GREEN 25/25 log 20261112-054652-test-filter.log |
| 2026-11-12 05:55:29 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate AC-06, arvore restaurada sem o teste temporario) | 0 | docs/sdd/runs/test-runs/20261112-055345-test-gate.log - exit: 0 DENTRO do log (S-22) - Com falha 0 / Aprovado 456 / Total 456 (5m05s) |
| 2026-11-12 05:55:31 | qa | varredura de escopo pwsh (git status backend, git hash-object vs rev-parse, mtime desde a baseline 05:32:52, residuo, teste temporario, backups TEMP) | 0 | docs/sdd/qa/QA-0036-scope.log - git-backend-entries=0 - blob-identity 2/2 match=True - mtime-count=2 - residue=0 - temp-test-file-present=False - temp-backups=0 |
| 2026-11-12 05:59:35 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0036.ps1 | 0 | docs/sdd/qa/QA-0036-check.log - SUMMARY total=29 fails=0 (S-22, S-16, S-23, BOM, escopo, sdd-state check OK) |
| 2026-11-12 05:59:37 | qa | wsl -e bash docs/sdd/qa/qa-check-task-0036.sh (gemeo Linux, parte estatica) | 0 | docs/sdd/qa/QA-0036-check-sh.log - SUMMARY total=26 fails=0 |
| 2026-11-12 06:00:27 | qa | git commit unico autorizado (so docs/sdd/qa/*) + git show --stat | 0 | f455d50 - 10 files changed 982 insertions - branch task/TASK-0036 1 commit a frente de 6914bb3 - sem push - main intacta - task files seguem nao commitados (edicao do ODEV) |
