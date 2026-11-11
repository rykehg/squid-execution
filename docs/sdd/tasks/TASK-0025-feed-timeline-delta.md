<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0025 — Feed e timeline com delta `?since=` e visibilidade corrente

```yaml
runId: R-20261109-01
taskId: TASK-0025
status: DONE
agent: odev
phase: P-04
sequence: 2
createdAt: 2026-11-09 11:22:36
updatedAt: 2026-11-09 20:18:57
parent: SPEC-0009
authority: SPEC
parallel: false
touches: ["backend/src/Squid.ReadModels/**", "backend/src/Squid.Api/Posts/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/Feed/**"]
blocks: ["TASK-0026"]
blockedBy: ["TASK-0024"]
inputArtifacts: ["docs/sdd/specs/SPEC-0009-publicacao-feed-postagem.md", "specs/001-squid-mvp/data-model.md", "specs/001-squid-mvp/contracts/http-api.md"]
outputArtifacts: ["backend/src/Squid.ReadModels/**", "backend/src/Squid.Api/Posts/**", "backend/src/Squid.Service/**"]
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
nextAction: DONE - QA-0024 PASSED (7/7 ACs, gate 293/293, 7 mutacoes + SC-005 com 100 tentativas); proxima entrega TASK-0026
evidence: []
memoryBankFlags: []
```

## Intent

Fecha FR-023/FR-025/FR-026: `GET /api/containers/{slug}/feed?since=` e `GET /api/timeline?since=`
com delta exato sobre as projeções, ordem pura por `seq` (sem ranqueamento) e filtro de
visibilidade **corrente** da política/membership — inclui o caso do DoD "unlisted não aparece
para não-membro".

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `GET /api/containers/{slug}/feed` devolve a página cronológica (mais recente primeiro);
  com `?since=` devolve **exatamente** os itens `seq > since`, sem duplicata e na ordem de `seq`
  (§9.3) — teste de duas chamadas encadeadas e de chamada repetida (idempotente).
- AC-02: visibilidade **corrente**: após tornar a política mais restritiva, quem perdeu acesso
  não vê mais os itens (mesmo tendo visto antes) e recebe `404` idêntico ao inexistente;
  `Private`/`MembersOnly` para não-membro → 404 — teste.
- AC-03: `GET /api/timeline?since=` devolve só os itens dos contêineres em que o ator tem
  membership `Active`, com a mesma semântica de delta; entrar/sair de um contêiner muda o que
  aparece — teste.
- AC-04: contêiner `Unlisted` (não-público nesta fase): feed para não-membro → `404` idêntico e
  seus itens **nunca** aparecem na timeline de terceiros (DoD do Sprint 3) — teste.
- AC-05: membro `Banned` não lê (404 idêntico); `Silenced` lê normalmente — teste.
- AC-06: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`
  exit 0; nenhum arquivo alterado fora dos `touches`.
- AC-08 (correção adjudicada F1 da QA-0022, 2026-11-09 14:03:43): o cap de `content` é
  conferido **após a sanitização** — corpo bruto ≤ 5000 cujo conteúdo escapado ultrapassa 5000
  (ex.: 5000 `&`) → `400 validation_failed` com **nada** gravado (o data-model §2.5 manda
  `Content ≤ 5000` no campo persistido); corpo bruto > 5000 continua 400 na checagem cedo; corpo
  que escapa para ≤ 5000 publica normal — testes dos três casos.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.ReadModels/**` (queries Dapper de feed/timeline com índice em `seq`)
- `backend/src/Squid.Api/Posts/**` (rotas `feed` e `timeline`)
- `backend/src/Squid.Service/**` (registro; leitura de membership via store existente)
- `backend/tests/Squid.Tests/Feed/**` (novo)

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| — | no | a leitura usa membership/policy correntes (FR-026, §4) — para membros, a checagem pode reusar a visibilidade já existente da P-03 (`CanSeeAsync`), sem duplicar regra |
| — | no | página default 20, teto 100 (mesma casa de `?since=` do contrato); `since` fora de intervalo (maior que o máximo) devolve lista vazia, não erro |
| — | no | `Unlisted` nesta fase segue o código da P-03 (não-público exige membership) — nenhum AC depende de tratá-lo como público |
| QA-0022/F1 | no | cap de `content` pós-sanitização (AC-08): a correção mora em `Squid.Api/Posts/**` (dentro dos touches desta task); a checagem bruta é mantida |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-09 11:34:55 | odev | task created from SPEC-0009 (entrega 3 de 10; blockedBy TASK-0024; fecha FR-023/025/026 e o caso unlisted do DoD)
- 2026-11-09 14:04:59 | odev | gate do ODEV 2026-11-09 14:03:43: AC-08 adicionado antes do dispatch desta task - correcao do achado F1 da QA-0022 (cap de 5000 conferido no corpo bruto antes da sanitizacao; 5000 "&" gravavam 25000 no Content, acima do data-model 2.5); escada parou na especificacao (SPEC-0009 A manda Content <= 5000 persistido), nenhum contrato muda; toques ja cobrem Api/Posts
- 2026-11-09 17:15:32 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; blockedBy TASK-0024 resolve (DONE com QA-0023 PASSED, gate 277/277); baseline do gate e 277/277; entrega com AC-08 (cap pos-sanitizacao, gate do ODEV 2026-11-09 14:03)
- 2026-11-09 18:36:41 | odev | DEV subagent FALHOU (provavel rede, 2026-11-09 ~18:18) no MEIO da implementacao: 8 arquivos de produto escritos (FeedEndpoints, SqliteFeedTimeline, ActivityProjector, PostEndpoints, SquidServiceHost + 3 testes em tests/Feed) e nada anexado no log; varredura da arvore: 0 residuos QA-MUTATION, git sem nada fora dos toques
- 2026-11-09 18:36:41 | odev | sonda de saude executada pelo ODEV apos a falha: sdd-test.ps1 test -> exit 1 (log 20261109-183545-test-gate.log, Com falha: 6, Aprovado: 279, Total: 285) - o codigo compila e o BASELINE INTEIRO segue verde (277/277 pre-existentes); as 6 falhas sao TODAS dos testes novos da subagente morta (FeedRoutesTests AC-01..05 e ContentCapTests AC-08b) - estado = implementacao incompleta, nao regressao; sem commit de base no git, a correcao e continuar, nao reverter
- 2026-11-09 18:36:41 | odev | politica de falha: 1 redispatch da mesma task (subagente nova, attempt segue 1/2); a nova DEV recebe os 8 arquivos parciais como ponto de partida (nao como veredito) e o baseline de saude e 277/277 pre-existentes verdes com o gate da tarefa em 6 vermelhas proprias
- 2026-11-09 19:07:04 | dev | baseline reexecutado antes de editar: sdd-test.ps1 test exit 1 com exatamente as 6 falhas herdadas (FeedRoutesTests Ac01..Ac05 + ContentCapTests.Ac08b) e o baseline pre-existente inteiro verde (277/277); nenhum teste vermelho fora dos 6, implementacao seguiu adiante
- 2026-11-09 19:07:05 | dev | causa raiz 1 dos 500 (feed e timeline): Dapper nao materializava FeedTimelineRow - Depth e PolicyVersion eram int e o SQLite reporta INTEGER como Int64, exigindo assinatura de construtor exata; corrigidos para long em SqliteFeedTimeline.cs
- 2026-11-09 19:07:05 | dev | causa raiz 2 dos 500 (so a timeline): as tres consultas do ator selecionavam container_id/seq/post_id sem qualificar no JOIN home_timeline x timeline_item e o SQLite recusava a coluna ambigua; colunas agora prefixadas com t. (JoinedColumns)
- 2026-11-09 19:07:06 | dev | suite dos testes novos apos as duas correcoes: sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Feed (excluindo o diagnostico) exit 0 com 8/8; arquivo de diagnostico temporario FeedDiagTests.cs removido da arvore
- 2026-11-09 19:07:06 | dev | gate final AC-06: sdd-test.ps1 test exit 0 - Com falha: 0, Aprovado: 285, Total: 285; nada alterado fora dos touches (minha mudanca de produto limita-se a SqliteFeedTimeline.cs, dentro do escopo)
- 2026-11-09 19:14:15 | odev | ODEV verificou antes de transicionar: gate final 20261109-190324-test-gate.log lido no arquivo (exit: 0, Aprovado 285, Com falha 0 - S-22); residuos QA-MUTATION = 0; FeedDiagTests.cs removido; check exit 0 (log 11, evidencias 7); step result DONE aceito - retomada pos-falha de rede com as 2 causas-raiz diagnosticadas (Dapper Int64 e JOIN ambiguo), baseline 277 nunca regrediu
- 2026-11-09 19:14:16 | odev | nota de contadores: o step result do DEV diz correction 1/2 mas o control block segue correctionAttempts 0/2 - a morte da subagente foi falha de harness (rede), nao veredito FAILED que gera correcao; precedente QA-0015/TASK-0016 manteve o mesmo criterio; contadores so sobem em eventos de veredito
- 2026-11-09 19:14:16 | odev | parked idea do DEV registrada para a QA: contrato secao 1 preve ?before=, since_id/max_id e Link rel=next/prev - fora dos 7 ACs; se a QA-0024 levantar, vira backlog/task por adjudicacao
- 2026-11-09 19:14:17 | odev | transicao IN_DEVELOPMENT -> AWAITING_QA; entrega 3 de 10 da P-04 (7 ACs - os 6 originais + AC-08)
- 2026-11-09 20:04:52 | qa | qa attempt 1/2: 7 ACs executados com 8 cenarios proprios em backend/tests/Squid.Tests/Feed/QaTask0025Tests.cs (nao apenas re-leitura dos testes do DEV); gate de baseline 285/285 exit 0 lido DENTRO do log 20261109-192511 (S-22); classe da QA 8/8 no 20261109-194058 e observacoes em QA-0024-observations.log com logger detailed; 7 ciclos can-fail M1..M7 mutando so arquivos dos touches (SHA256 PRE antes - S-16, vermelho classificado pelo proprio log gravado - S-22, restore byte-exact com bump de mtime - S-23, verde em seguida) com SUMMARY cycles=7 failed=0 residuo=0 temp-backups=0; gate final AC-06 293/293 exit 0 (20261109-195855) e escopo limpo - mtime desde o dispatch 17:15:32 = 9 arquivos todos nos touches, git novas = so docs/sdd/qa, residuos QA-MUTATION em backend = 0, SHA256 = PRE nos 3 mutados
- 2026-11-09 20:04:52 | qa | achados da QA-0024: MINOR novo ActivityProjector.cs:95 - a timeline pessoal nao faz backfill do container entrado depois da projecao (observado False; ODEV adjudica contra data-model 4 e a B emendada da SPEC-0009); MINOR novo interacao AC-03 x AC-05 - Silenced fica fora da timeline enquanto le o feed normalmente (observado False; confirmar que a frase le normalmente do AC-05 e escopo de leitura de container); pre-existente sdd-test.sh:20 so acha *.sln e o repo tem Squid.slnx (exit 1 re-executado) e dotnet ausente no WSL vira exit 127 reportado como falha real; plataforma Linux do produto = unverified (WSL sem dotnet, instalar exige gate e a sessao tem aprovacoes desativadas); vetores before e Link do contrato 1/9.8 seguem estacionados pelo ODEV fora dos 7 ACs; veredito PASSED - 7/7 ACs, 0 BLOCKER
- 2026-11-09 20:18:59 | odev | ODEV verificou antes de transicionar: QA-0024-feed-timeline-delta.md existe; gate final 20261109-195855-test-gate.log lido no arquivo (exit: 0, Aprovado 293, Com falha 0 - S-22); residuos QA-MUTATION = 0; check exit 0 (log 17, evidencias 23); veredito aceito - 7/7 ACs, 7 ciclos de mutacao PROVEN, SC-005 com 100 tentativas e 0 vazamentos, checkers 19/19 nos dois sistemas
- 2026-11-09 20:18:59 | odev | achados adjudicados (escada: especificacao): F1 (timeline sem backfill de contêiner entrado depois) e F2 (Silenced fora da timeline mas le o contêiner) - as duas clarificacoes entraram na SPEC-0009 secao C (2026-11-09 20:17:13): sem backfill por padrao, historico vive no feed do contêiner; filtro de timeline exige Active; F1 virou S-35 no backlog; F3 (gemeo sdd-test.sh com exit 1/127) pre-existente = S-11/S-12 + flag DIVERGENCIA ja registrada para a consolidacao; [GAP: memory bank absent] de novo nao confere
- 2026-11-09 20:19:00 | odev | transicao AWAITING_QA -> DONE; TASK-0025 = entrega 3 de 10 da P-04 encerrada (QA-0024 PASSED); cadeia libera a TASK-0026

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-09 18:36:42 | odev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (sonda de saude apos falha do DEV) | 1 | docs/sdd/runs/test-runs/20261109-183545-test-gate.log (Com falha: 6, Aprovado: 279, Total: 285 - as 6 sao dos testes novos da subagente morta; baseline 277 verde) |
| 2026-11-09 19:07:32 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (baseline antes da primeira edicao) | 1 | docs/sdd/runs/test-runs/20261109-184148-test-gate.log (Com falha: 6, Aprovado: 279, Total: 285 - as 6 sao todas as herdadas) |
| 2026-11-09 19:07:33 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~FeedRoutesTests.Ac01 (reproducao isolada do 500) | 1 | docs/sdd/runs/test-runs/20261109-184528-test-filter.log (1 falha - Expected OK, Actual InternalServerError) |
| 2026-11-09 19:07:33 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~FeedDiagTests (diagnostico temporario) | 1 | docs/sdd/runs/test-runs/20261109-185252-test-filter.log (InvalidOperationException do Dapper na materializacao de FeedTimelineRow com a assinatura Int64 exigida) |
| 2026-11-09 19:07:34 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Feed (apos a correcao 1) | 1 | docs/sdd/runs/test-runs/20261109-185545-test-filter.log (5 falhas - Ac01 e Ac08 verdes; Ac02/03/04/05 ainda 500 na timeline) |
| 2026-11-09 19:07:34 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Feed&FullyQualifiedName!~FeedDiagTests (apos a correcao 2) | 0 | docs/sdd/runs/test-runs/20261109-185932-test-filter.log (Aprovado: 8, Total: 8) |
| 2026-11-09 19:07:35 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate final AC-06) | 0 | docs/sdd/runs/test-runs/20261109-190324-test-gate.log (Com falha: 0, Aprovado: 285, Total: 285) |
| 2026-11-09 19:14:17 | odev | Select-String do gate final + varredura QA-MUTATION + existencia do FeedDiagTests + sdd-state check | 0 | docs/sdd/runs/test-runs/20261109-190324-test-gate.log exit: 0 / Aprovado 285 / Com falha 0; residuos 0; FeedDiagTests removido; check OK log 11 evidencias 7 |
| 2026-11-09 20:05:23 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate de baseline da QA) | 0 | docs/sdd/runs/test-runs/20261109-192511-test-gate.log (exit: 0 lido DENTRO do log - S-22; Com falha 0 Aprovado 285 Total 285) |
| 2026-11-09 20:05:23 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~QaTask0025Tests (1a execucao: erro CS0120 no arquivo da QA, corrigido em seguida) | 1 | docs/sdd/runs/test-runs/20261109-193822-test-filter.log (error CS0120 GetAsync nao-estatico em QaTask0025Tests.cs:662; defeito do arquivo da QA, nenhum teste de produto rodou) |
| 2026-11-09 20:05:24 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~QaTask0025Tests (reexecucao apos corrigir o arquivo da QA) | 0 | docs/sdd/runs/test-runs/20261109-194058-test-filter.log (Com falha 0 Aprovado 8 Total 8) |
| 2026-11-09 20:05:24 | qa | dotnet test backend/Squid.slnx -c Debug -m:1 --filter FullyQualifiedName~QaTask0025Tests --logger console;verbosity=detailed (captura das observacoes) | 0 | docs/sdd/qa/QA-0024-observations.log (exit: 0 no cabecalho; AC-03 backfill=False; AC-05 Silenced timeline=False; SC-005 100 tentativas 0 vazamentos; AC-08 3 recusas sem gravar) |
| 2026-11-09 20:05:24 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0025.ps1 -Only M1 (smoke do driver) | 0 | docs/sdd/runs/test-runs/20261109-194837-test-filter.log (ciclo M1 PROVEN: vermelho 194837, restore HASH=PRE, verde 194851; SUMMARY cycles=1 failed=0) |
| 2026-11-09 20:05:25 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0025.ps1 (7 ciclos can-fail M1 a M7) | 0 | docs/sdd/qa/QA-0024-mutations.log (7x PROVEN; vermelhos 194951 a 195252 classificados dentro dos logs; SUMMARY cycles=7 failed=0 residuo=0 temp-backups=0) |
| 2026-11-09 20:05:25 | qa | bash docs/sdd/_tools/sdd-test.sh probe e test e test backend/Squid.slnx (gemma sh no WSL bash) | 1 | docs/sdd/qa/QA-0024-sh-twin.log (probe exit 0 sem dotnet no WSL; test sem alvo exit 1 no *.sln found pre-existente; test com alvo exit 1 dotnet command not found reportado pelo gema) |
| 2026-11-09 20:05:26 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate final AC-06) | 0 | docs/sdd/runs/test-runs/20261109-195855-test-gate.log (exit: 0 lido DENTRO do log - S-22; Com falha 0 Aprovado 293 Total 293 = 285 + 8 da QA) |
| 2026-11-09 20:05:26 | qa | varredura final de escopo: Get-FileHash dos 3 mutados + residuo QA-MUTATION em backend + git status vs baseline + mtime desde 17:15:32 + temp | 0 | hashes BBAB3FDC25DA 894AB7CEB5DD A80E83B345F1 = PRE; residuo = 0; git novas = 3 todas em docs/sdd/qa; mtime = 9 arquivos nos touches; temp qa0024 = 0 |
| 2026-11-09 20:07:17 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0025.ps1 (checker run 1) | 0 | docs/sdd/qa/QA-0024-check.log (SUMMARY total=19 fails=0 RESULT: PASS) |
| 2026-11-09 20:07:17 | qa | bash docs/sdd/qa/qa-check-task-0025.sh (checker run 1 - gema com cd de profundidade errado, corrigido em seguida) | 1 | docs/sdd/qa/QA-0024-check.log (SUMMARY total=19 fails=16 RESULT: FAIL por rodar em docs/ em vez da raiz - defeito do script da QA, nao do produto) |
| 2026-11-09 20:13:01 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0025.ps1 (checker run 2, apos os appends) | 0 | docs/sdd/qa/QA-0024-check.log (SUMMARY total=19 fails=0 RESULT: PASS) |
| 2026-11-09 20:13:01 | qa | bash docs/sdd/qa/qa-check-task-0025.sh (checker run 2 - gema li o mutations.log como UTF-16 e o baseline com BOM/CRLF, normalizacao de entrada corrigida em seguida) | 1 | docs/sdd/qa/QA-0024-check.log (SUMMARY total=19 fails=4 RESULT: FAIL - defeito do script da QA, nao do produto) |
| 2026-11-09 20:13:55 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0025.ps1 (checker run 3, execucao final apos todos os appends) | 0 | docs/sdd/qa/QA-0024-check.log (SUMMARY total=19 fails=0 RESULT: PASS; inclui SDD-STATE-CHECK OK) |
| 2026-11-09 20:13:55 | qa | bash docs/sdd/qa/qa-check-task-0025.sh (checker run 3, gema linux apos normalizacao UTF-16/BOM/CR) | 0 | docs/sdd/qa/QA-0024-check.log (SUMMARY total=19 fails=0 RESULT: PASS no WSL) |
| 2026-11-09 20:19:00 | odev | Select-String do gate final + varredura QA-MUTATION em backend + sdd-state check | 0 | docs/sdd/runs/test-runs/20261109-195855-test-gate.log exit: 0 / Aprovado 293 / Com falha 0; residuos 0; check OK log 17 evidencias 23; QA-0024 presente |
