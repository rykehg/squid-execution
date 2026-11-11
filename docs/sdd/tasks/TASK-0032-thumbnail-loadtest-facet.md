<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0032 — Thumbnail no Worker, load test e facet de fecho do DoD

```yaml
runId: R-20261109-01
taskId: TASK-0032
status: DONE
agent: odev
phase: P-04
sequence: 2
createdAt: 2026-11-09 11:22:36
updatedAt: 2026-11-11 20:00:27
parent: SPEC-0009
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Worker/**", "backend/src/Squid.Api/Media/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/Load/**"]
blocks: []
blockedBy: ["TASK-0031"]
inputArtifacts: ["docs/sdd/specs/SPEC-0009-publicacao-feed-postagem.md", "docs/sdd/plan/plan.md", "specs/001-squid-mvp/spec.md", "specs/001-squid-mvp/data-model.md"]
outputArtifacts: ["backend/src/Squid.Worker/**", "backend/src/Squid.Api/Media/**", "backend/src/Squid.Service/**"]
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
nextAction: DONE - QA-0032 PASSED (6/6 ACs, gate 432/432, 6 mutacoes; 3 quedas de harness no caminho); ULTIMA entrega da P-04 - inicia a consolidacao da fase
evidence: []
memoryBankFlags: []
```

## Intent

Fecha o último item do Sprint 3 (thumbnail no Worker + load test) e fecha a fase: thumbnail
SkiaSharp de `Image`/`Gif` a partir do blob já sem EXIF, load test P50/P99 com 10k itens
(SC-004) e a **facet de fecho do DoD** do Sprint 3 em fluxo HTTP completo.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: o Worker gera thumbnail das imagens e GIFs a partir do blob (SkiaSharp) e o registro
  do thumbnail fica disponível ao servir a mídia; `Video`/`Audio` recebem placeholder (GAP-21) —
  teste invocando o serviço com blob sintético.
- AC-02: load test executável no gate: semeia **10k** itens em `timeline_item` e mede
  `GET /api/containers/{slug}/feed` e `GET /api/timeline` — P95 da primeira página < 1 s
  (SC-004), com P50/P99 impressos no log do teste — teste.
- AC-03: facet de fecho do DoD do Sprint 3 em fluxo HTTP completo (reutilizando as rotas das
  tasks anteriores): (a) post em comunidade chega ao assinante SSE conectado em < 1 s;
  (b) `?since=` entrega **só** o delta, sem repetir; (c) contêiner unlisted/invisível não aparece
  para não-membro — teste.
- AC-04: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`
  exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Worker/**` (serviço de thumbnail + fila de trabalho)
- `backend/src/Squid.Api/Media/**` (servir thumbnail/placeholder)
- `backend/src/Squid.Service/**` (registro/composição)
- `backend/tests/Squid.Tests/Load/**` (novo: load test + facet)

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-21 | no | thumbnail só de `Image`/`Gif`; vídeo/áudio = placeholder estático (SkiaSharp não decodifica vídeo) |
| — | no | o load test roda in-process no gate (sem rede, sem servidor externo); limites P95 < 1 s valem para a máquina de dev — regressões grandes ficam vermelhas, ruído fino não |
| — | no | facet (c) usa o fluxo real (post em contêiner não listado + leitor de fora) — se uma faceta falhar por defeito de task anterior, é achado de QA com `S-NN` no backlog, não reabertura silenciosa |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-09 11:35:00 | odev | task created from SPEC-0009 (entrega 10 de 10 e ultima da P-04; blockedBy TASK-0031; load test SC-004 + facet de fecho do DoD)
- 2026-11-11 08:26:15 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; blockedBy TASK-0031 resolve (DONE com QA-0031 PASSED, gate 428/428); baseline do gate e 428/428; ordem de retomada humana 2026-11-11 08:19:47 registrada no plan.md (parada de 2026-11-10 levantada); ULTIMA entrega da P-04 - com QA PASSED a fase fecha
- 2026-11-11 10:14:16 | odev | DEV subagent FALHOU antes de fechar (harness, 2026-11-11 ~09:46, ultima mensagem truncada "Reviewing while tests run"); deixou 10 arquivos de produto/teste escritos entre 09:21 e 09:35 (ThumbnailGenerator/ThumbnailService, MediaPlaceholders, IMediaStore/MediaEndpoints/SqliteMediaStore, Worker/Program + FeedLoadTests, Sprint3ClosingFacetTests, ThumbnailTests) e 0 residuos QA-MUTATION; nada anexado no log
- 2026-11-11 10:14:17 | odev | saude da arvore: gate baseline 20261111-083317 das 08:33 e o ultimo log existente - NAO ha evidencia de execucao apos os writes de 09:35, entao a situacao e codigo escrito sem prova (compila/verde ?); sem commit de base, redisdispatch com o trabalho parcial como ponto de partida
- 2026-11-11 10:14:17 | odev | politica de falha: 1 redispatch da mesma task (subagente nova, attempt segue 1/2); a nova DEV recebe os 10 arquivos como ponto de partida, nao como veredito
- 2026-11-11 10:46:22 | odev | sonda do ODEV apos a falha: gate 20261111-104455 exit 0 com 431/411... 431/431 (428 + 3 herdados) - o trabalho do DEV morto esta COMPLETO E VERDE; so faltava o step result; nota: gate agora leva ~30 min (load test de 10k no gate, exigido pela AC-02) - a QA-0032 deve planejar os ciclos de mutacao com filtro, nao gate inteiro
- 2026-11-11 10:46:51 | odev | correcao da linha anterior: o valor do gate da sonda e 431/431 (o trecho 431/411 e typo meu); a linha de evidencia e o log 20261111-104455 trazem o valor correto
- 2026-11-11 11:33:07 | dev | auditoria dos 3 arquivos herdados contra AC-01..04: produto completo, lacunas so nos testes (GIF so File.Exists, sem caso Audio, semente 10k nao conferida, janela de 1500 s na facet)
- 2026-11-11 11:33:07 | dev | causa real da gate de ~30 min identificada: Sprint3ClosingFacetTests lia o fio do outsider por TimeSpan.FromSeconds(1500) = 25 min por execucao; a semeacao de 10k custa segundos (FeedLoad+Thumbnail = 7 s) - a atribuicao da sonda do odev ao seed de 10k estava errada
- 2026-11-11 11:33:07 | dev | correcoes de teste: janela 1500 s -> 1500 ms (o hidden ja esta enfileirado antes da leitura; pings de 0,2 s provam a vida do fio) + pre-condicao de leitura publica do nao-membro antes do unlisted
- 2026-11-11 11:33:07 | dev | AC-01 reforcado: Audio (WAV sintetico) no passe GAP-21 com placeholder registrado e servido, GIF exige PNG decodivel com lado <= MaxDimension
- 2026-11-11 11:33:07 | dev | AC-02 reforcado: COUNT(*) = 10000 em timeline_item e home_timeline apos a semeacao
- 2026-11-11 11:33:07 | dev | impressao P50/P99 do AC-02 confirmada em log de teste (dotnet test --logger console;verbosity=detailed): feed P50=20,9 P95=25,8 P99=27,3 ms, timeline P50=16,0 P95=20,7 P99=24,1 ms; o logger normal do gate nao repassa saida de teste que passa, mesma limitacao das linhas PROBE
- 2026-11-11 11:33:07 | dev | gate final sdd-test.ps1 test exit 0 com 431/431 em 4m33s; escopo confirmado: so os 3 arquivos de backend/tests/Squid.Tests/Load alterados desde o inicio da sessao (mtime + git status)
- 2026-11-11 11:33:10 | odev | ODEV carimbou as 7 logLines e as 5 evidenceRows do DEV (a row da sonda ja estava gravada como minha); nota: a atribuicao da sonda do ODEV ao seed de 10k estava ERRADA - a culpa era a janela de 1500 s da facet, agora corrigida
- 2026-11-11 11:33:11 | odev | ODEV verificou antes de transicionar: gate final 20261111-112705-test-gate.log lido no arquivo (exit: 0, Aprovado 431, Com falha 0, 4m33s - S-22); residuos QA-MUTATION = 0; escopo = so os 3 arquivos Load da tentativa; check exit 0; step result DONE aceito - nenhum duvida, nenhum Fact novo (3 testes herdados fortalecados: Audio WAV, GIF decodivel, COUNT 10k, pre-condicao da facet); parked idea do logger detalhado no sdd-test para o backlog
- 2026-11-11 11:33:11 | odev | transicao IN_DEVELOPMENT -> AWAITING_QA; entrega 10 de 10, ULTIMA da P-04 - com QA PASSED a fase fecha
- 2026-11-11 15:06:06 | odev | QA-0032 subagent FALHOU antes de fechar (harness, 2026-11-11 ~11:46, ~11 min apos o dispatch, sem mensagem de fechamento); deixou 2 artefatos (QA-0032-git-baseline.txt 11:43, QA-0032-loadtest-p50p99.log 11:45) e NENHUM teste proprio, NENHUMA mutacao, NENHUMA linha no Log/Evidence da task
- 2026-11-11 15:06:07 | odev | saude da arvore verificada pelo ODEV: 0 residuos QA-MUTATION; 0 arquivos de produto com mtime apos o dispatch 11:33:06 - a arvore e IDENTICA ao estado do gate verde 20261111-112705 (431/431), entao sonda nova e desnecessaria (prova por mtime, nao por suposicao); ultima execucao de teste e o proprio filtro da QA morta (114421, exit 0)
- 2026-11-11 15:06:07 | odev | politica de falha: 1 redispatch da mesma task (subagente nova, qaAttempt segue 1/2); a nova QA recebe os 2 artefatos (baseline git + medicao p50p99) como ponto de partida, nao como veredito
- 2026-11-11 16:14:05 | odev | segunda QA-0032 FALHOU antes de fechar (harness, 2026-11-11 ~16:06, ~1 h apos o dispatch); DIFERENTE da primeira, esta morreu AVANCADA: escreveu QaTask0032Tests (15:51), load runs (15:26/15:52), driver qa-mutate-task-0032 (15:57), checkers (16:01-16:02) e estava no ciclo de mutacoes (logs 16:02-16:06)
- 2026-11-11 16:14:05 | odev | ESTADO DA ARVORE: 1 MUTACAO ATIVA - M3 em backend/tests/Squid.Tests/Load/FeedLoadTests.cs:53 (feed.P95 < 0 com marcador QA-MUTATION-M3); os alvos de produto (ThumbnailService 16:05, MediaEndpoints 16:06) estao RESTAURADOS (varredura: so 1 marcador na arvore inteira); o driver restaura de memoria com PRE embutido (sem backup em TEMP) e morreu entre aplicar e reverter; resido QA-MUTATION = 1
- 2026-11-11 16:14:06 | odev | politica de falha no limite: dispatch 1 (11:46) morreu cedo -> 1 redispatch usado; dispatch 2 (15:07) morreu no meio da mutacao -> o redispatch acabou; BLOCKED com decisao humana (o ODEV nao escreve codigo de produto nem de teste, entao nao restaura por conta propria)
- 2026-11-11 18:34:07 | odev | gate humano 2026-11-11 (pergunta BLOCKED): aprovado o 3o dispatch da QA-0032 com instrucao de RESTAURACAO PRIMEIRA - a decisao humana sobrepoe o limite do redispatch unico da politica de falha; status BLOCKED -> AWAITING_QA
- 2026-11-11 18:49:35 | odev | terceira QA-0032 FALHOU (harness, 2026-11-11 ~18:41, morreu AO TERMINAR a restauracao da M3 - ultima mensagem: "Restoring it to the PRE line"); RESULTADO: restauracao COMPLETA - FeedLoadTests.cs de volta ao PRE (P95 < 1000, confere com o old do driver M3), varredura da arvore = 0 marcadores QA-MUTATION, produto nao foi tocado por esta tentativa
- 2026-11-11 18:49:38 | odev | sonda de pos-restauracao em curso (job de fundo) para provar 431/431; a politica leva a BLOCKED de novo (o humano condicionou o dispatch 3 como ultimo) - decisao humana sera pedida com as opcoes
- 2026-11-11 18:51:27 | odev | sonda de pos-restauracao concluida: gate 20261111-185014-test-gate.log exit 0 com 432/432 (431 do DEV + 1 cenario que a 2a QA chegou a escrever, herdado e verde) - arvore provada restaurada, 0 marcadores; evidencia gravada
- 2026-11-11 19:14:26 | qa | STEP ZERO: mutacao M3 (FeedLoadTests.cs:53) restaurada byte-exact - SHA256 03ADE1779D4D = PRE do driver (S-16), bump de mtime (S-23), residuo QA-MUTATION em backend = 0; gate 20261111-185033 exit 0 com 432/432 (431 de base + QaTask0032Tests da QA morta - o 431 do dispatch nao contava o teste proprio que ele mandou reusar; prossegui e registrei o desvio como adjudicavel)
- 2026-11-11 19:14:26 | qa | AC-02 medida por ESTA QA com logger detalhado: feed P50=22,1 P95=27,9 P99=33,6 ms e timeline P50=16,5 P95=22,0 P99=25,4 ms (n=100, 10k itens nas duas tabelas) em QA-0032-load-run3.log exit 0; Load completo 4/4 em QA-0032-load-run4.log com thumb jpeg=109933B 256x192, gif=97B 1x1, video=placeholder audio=placeholder 1081B
- 2026-11-11 19:14:26 | qa | driver de mutacao re-executado do zero por esta QA: M1..M6 todos RED-IN-LOG + RESTORE-OK hash=PRE residue=0, GREEN 4/4 (log 20261111-185927), SUMMARY cycles=6 failed=0 residue-QA-MUTATION=0 temp-backups=0
- 2026-11-11 19:14:26 | qa | gate final 20261111-190626 exit 0 com 432/432 (5m4s); varredura final: residuo 0, 4 hashes = PRE, mtime desde 18:35 so nos 4 alvos do driver (todos nos touches), git +12 todas QA-0032 e 0 removidas; verdicto PASSED em docs/sdd/qa/QA-0032-thumbnail-loadtest-facet.md
- 2026-11-11 20:00:36 | odev | ODEV verificou antes de transicionar: QA-0032-thumbnail-loadtest-facet.md com verdicto PASSED; gate final 20261111-190626-test-gate.log lido no arquivo (exit: 0, Aprovado 432, Com falha 0 - S-22); residuos QA-MUTATION = 0; check exit 0 (log 31, evidencias 18); veredito aceito - 6/6 ACs, 6 ciclos can-fail RED-IN-LOG, P50/P99 medidos pela propria QA (feed P95=27,9 ms, timeline P95=22,0 ms), placeholders de video e audio servidos, facet com atribuicao de politica provada por M5/M6
- 2026-11-11 20:00:38 | odev | desvio 431->432 adjudicado (escada: especificacao): o +1 e o QaTask0032Tests.cs herdado da 2a tentativa, que EU mandei reusar no dispatch - o checker da propria QA codifica 431+1=432; a restauracao e prova independente (SHA256 = PRE do driver, 0 residos, gate verde); a leitura da QA foi correta e nao queimou a ultima tentativa num numero obsoleto
- 2026-11-11 20:00:40 | odev | notas de fluxo: as 3 "mortes" incluem 2 reais + 1 notificacao falsa do harness (a 3a terminou); sh-checker C5 (BOM/CRLF) corrigido no artefato da QA e reexecutado 9/9; run1 legado tinha temp-backups=1, TEMP = 0 agora; unverified padrao (Linux, SSE em rede real)
- 2026-11-11 20:00:42 | odev | transicao AWAITING_QA -> DONE; TASK-0032 = entrega 10 de 10 da P-04 encerrada (QA-0032 PASSED) - TODAS AS 10 ENTREGAS DONE COM QA PASSED, DoD do Sprint 3 provado; inicia a CONSOLIDACAO DA FASE (memory bank + snapshot 0005 + authority CODE)

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-11 10:46:21 | odev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (sonda de saude apos falha do DEV, job de fundo) | 0 | docs/sdd/runs/test-runs/20261111-104455-test-gate.log exit: 0 / Aprovado 431 / Com falha 0 (428 + 3 testes novos herdados) - duracao 29m57s porque FeedLoadTests semeia 10k itens no gate |
| 2026-11-11 11:33:08 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Load (pos-correcoes) | 0 | docs/sdd/runs/test-runs/20261111-111552-test-filter.log (3/3, 12 s - facet de ~25 min para segundos) |
| 2026-11-11 11:33:08 | dev | dotnet test backend/Squid.slnx -c Debug -m:1 --filter FullyQualifiedName~Squid.Tests.Load.FeedLoadTests --logger console;verbosity=detailed | 0 | docs/sdd/runs/test-runs/20261111-112747-loadtest-p50p99.log (SC-004 feed P50=20,9 P95=25,8 P99=27,3 ms; timeline P50=16,0 P95=20,7 P99=24,1 ms) |
| 2026-11-11 11:33:09 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261111-112705-test-gate.log (Aprovado 431 / falha 0, Duracao 4 m 33 s) |
| 2026-11-11 11:33:10 | dev | Get-ChildItem LastWriteTime > inicio da sessao + git status --porcelain | 0 | apenas backend/tests/Squid.Tests/Load/{FeedLoadTests,Sprint3ClosingFacetTests,ThumbnailTests}.cs, nenhum outro arquivo alterado por esta tentativa |
| 2026-11-11 11:33:13 | odev | Select-String do gate final + varredura QA-MUTATION + janela de arquivos + sdd-state check | 0 | docs/sdd/runs/test-runs/20261111-112705-test-gate.log exit: 0 / Aprovado 431 / Com falha 0 / 4m33s; residuos 0; escopo = 3 arquivos Load; check OK |
| 2026-11-11 11:34:00 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Load (pre-edicao: FeedLoadTests + ThumbnailTests) | 0 | docs/sdd/runs/test-runs/20261111-110906-test-filter.log (2/2, 7 s) |
| 2026-11-11 18:51:29 | odev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (sonda de pos-restauracao, job de fundo) | 0 | docs/sdd/runs/test-runs/20261111-185014-test-gate.log exit: 0 / Aprovado 432 / Com falha 0 (431 + 1 cenario herdado da 2a QA) |
| 2026-11-11 19:15:05 | qa | restauracao byte-exact de FeedLoadTests.cs:53 via edit + Get-FileHash SHA256 + varredura QA-MUTATION + bump de mtime | 0 | docs/sdd/qa/QA-0032-restore.log - SHA256 03ADE1779D4D = PRE do driver (byte-exato), residuo backend=0, linha53=PRE |
| 2026-11-11 19:15:07 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate pos-restauracao) | 0 | docs/sdd/runs/test-runs/20261111-185033-test-gate.log - exit: 0 / Aprovado 432 / Com falha 0 / 6m46s |
| 2026-11-11 19:15:09 | qa | dotnet test backend/Squid.slnx -c Debug -m:1 --filter FullyQualifiedName~Squid.Tests.Load.FeedLoadTests --logger console;verbosity=detailed | 0 | docs/sdd/qa/QA-0032-load-run3.log - SC-004 feed P50=22,1 P95=27,9 P99=33,6 ms; timeline P50=16,5 P95=22,0 P99=25,4 ms (n=100, 10k itens) |
| 2026-11-11 19:15:11 | qa | dotnet test backend/Squid.slnx -c Debug -m:1 --filter FullyQualifiedName~Squid.Tests.Load --logger console;verbosity=detailed | 0 | docs/sdd/qa/QA-0032-load-run4.log - Aprovados 4 e 0 FAIL; jpeg Skia 256x192, gif 1x1, video=placeholder audio=placeholder 1081B |
| 2026-11-11 19:15:13 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0032.ps1 | 0 | docs/sdd/qa/QA-0032-mutations.log - M1..M6 RED-IN-LOG + RESTORE-OK hash=PRE residue=0; GREEN 4/4; SUMMARY cycles=6 failed=0 residue-QA-MUTATION=0 temp-backups=0 |
| 2026-11-11 19:15:14 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate final) | 0 | docs/sdd/runs/test-runs/20261111-190626-test-gate.log - exit: 0 / Aprovado 432 / Com falha 0 / 5m4s |
| 2026-11-11 19:15:16 | qa | varredura final de escopo: residuo QA-MUTATION + SHA256 dos 4 alvos do driver + mtime desde 18:35 + git vs baseline + backups TEMP | 0 | docs/sdd/qa/QA-0032-scope.log - residuo=0; 4/4 hashes=PRE; mtime=4 arquivos nos touches; git +12 todas QA-0032 com 0 removidas e 0 fora; backups=0 |
| 2026-11-11 19:19:16 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0032.ps1 | 0 | docs/sdd/qa/QA-0032-check.log - SUMMARY total=9 fails=0 (C1 gate 432, C3 SUMMARY do driver, C5 +16/0/0) |
| 2026-11-11 19:19:18 | qa | bash docs/sdd/qa/qa-check-task-0032.sh | 0 | docs/sdd/qa/QA-0032-check-sh.log - SUMMARY total=9 fails=0; C5 corrigido no artefato QA (baseline BOM+CRLF exigia normalizacao antes do comm) e re-executado |
| 2026-11-11 19:19:43 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-state.ps1 check | 0 | docs/sdd/tasks/TASK-0032-thumbnail-loadtest-facet.md - check OK log entries 31 evidence rows 17 naquele momento |
| 2026-11-11 20:00:44 | odev | Select-String do gate final + varredura QA-MUTATION em backend + verdicto do doc + sdd-state check | 0 | docs/sdd/runs/test-runs/20261111-190626-test-gate.log exit: 0 / Aprovado 432 / Com falha 0; residuos 0; verdicto PASSED no QA-0032-thumbnail-loadtest-facet.md; check OK log 31 evidencias 18 |
