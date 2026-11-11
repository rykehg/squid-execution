<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0026 — SSE `GET /api/stream` ao vivo

```yaml
runId: R-20261109-01
taskId: TASK-0026
status: DONE
agent: odev
phase: P-04
sequence: 2
createdAt: 2026-11-09 11:22:36
updatedAt: 2026-11-09 22:27:33
parent: SPEC-0009
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Api/Posts/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/Stream/**"]
blocks: ["TASK-0027"]
blockedBy: ["TASK-0025"]
inputArtifacts: ["docs/sdd/specs/SPEC-0009-publicacao-feed-postagem.md", "specs/001-squid-mvp/spec.md", "specs/001-squid-mvp/contracts/http-api.md"]
outputArtifacts: ["backend/src/Squid.Api/Posts/**", "backend/src/Squid.Service/**"]
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
nextAction: DONE - QA-0025 PASSED (6/6 ACs, gate 310/310, 7 mutacoes, latencia 140/67/207 ms); proxima entrega TASK-0027
evidence: []
memoryBankFlags: []
```

## Intent

Fecha FR-024 (e R-16): `GET /api/stream` autenticado com `Channel<T>` + `IAsyncEnumerable`,
`event: post` com `id` = `Activity.Seq`, reconexão por `Last-Event-ID` entregando só o delta e
heartbeat `ping` de 15 s — sem broker, tudo em processo.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: assinante conectado em `GET /api/stream` recebe `event: post` com `id` = seq global e
  `data` resumido quando um post é publicado em contêiner que ele **vê**, em < 1 s (medido no
  teste — é o primeiro ponto do DoD do Sprint 3) — teste.
- AC-02: `Last-Event-ID` na reconexão entrega apenas eventos posteriores: nada perdido, nada
  repetido (§9.4) — teste publicando entre desconexão e reconexão.
- AC-03: heartbeat `event: ping` a cada 15 s (com o intervalo injetável para o teste não
  dormir) — teste.
- AC-04: filtro de visibilidade do assinante: post em contêiner que ele não vê (não-membro em
  `MembersOnly`, `Banned`) **nunca** chega — teste.
- AC-05: sem sessão → `401 not_authenticated`; o fluxo derrubado/assinante desconectado não
  trava o publicador (o canal é best-effort, sem backpressure no autor) — teste.
- AC-06: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`
  exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Api/Posts/**` (hub do canal + rota SSE; publicação da TASK-0023 publica no hub)
- `backend/src/Squid.Service/**` (registro do hub no composition root)
- `backend/tests/Squid.Tests/Stream/**` (novo)

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| — | no | o filtro de visibilidade do assinante pode reusar a checagem da P-03 (membresia corrente); custo por evento é aceitável no MVP (canal em processo) |
| — | no | eventos do `Activity` de outras ações (Delete etc.) entram no mesmo hub quando as rotas existirem (TASK-0030) — aqui só `post` |
| — | no | intervalo de ping configurável (`Stream:PingSeconds`, default 15) só para os testes não dormirem |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-09 11:34:56 | odev | task created from SPEC-0009 (entrega 4 de 10; blockedBy TASK-0025; fecha FR-024 com o primeiro ponto do DoD)
- 2026-11-09 20:19:03 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; blockedBy TASK-0025 resolve (DONE com QA-0024 PASSED, gate 293/293); baseline do gate e 293/293
- 2026-11-09 21:01:36 | dev | - 2026-11-09 | dev | feasibility: baseline gate verde 293/293 exit 0 (runs/test-runs/20261109-202327-test-gate.log); touches confirmados sem acrescimos; D-01 (forma do data do quadro SSE) resolvido por SPEC-0009 sec D data resumido + R-13 payload bruto do PostView, sem duvida bloqueante
- 2026-11-09 21:01:36 | dev | - 2026-11-09 | dev | implementado: hub em processo (Channel bounded 1, TryWrite sem backpressure, melhor-esforco), StreamSession (IAsyncEnumerable de quadros, delta por IActivityStore.ReadAsync, ping por deadline Stream:PingSeconds default 15), rota GET /api/stream (401 sem sessao, cursor Last-Event-ID, filtro de visibilidade na entrega), publica no hub apos gravação durable em publish e reply, registro no SquidServiceHost
- 2026-11-09 21:15:57 | dev | testes novos 11/11 verdes (filtro FullyQualifiedName~Squid.Tests.Stream, exit 0); prova can-fail da AC-04 com o filtro de visibilidade mutado: 2/2 vermelhos exit 1, linha restaurada byte identica; gate completo 304/304 exit 0 (baseline 293 + 11 novos); escopo conferido por LastWriteTime: 11 arquivos de produto, todos dentro dos touches
- 2026-11-09 21:20:15 | odev | ODEV verificou antes de transicionar: gate final 20261109-211322-test-gate.log lido no arquivo (exit: 0, Aprovado 304, Com falha 0 - S-22); residuo QA-MUTATION em backend = 0; git = so toques (Posts/Stream novos + SquidServiceHost); check exit 0 (log 5, evidencias 6); step result DONE aceito - D-01 (forma do data do quadro SSE) fechada na especificacao (payload bruto do PostView, id = Activity.Seq, R-13 sem reserializar)
- 2026-11-09 21:20:15 | odev | notas: (1) as entradas 1-2 do Log carregam prefixo "- data | role |" duplicado porque o DEV passou o prefixo no -Text e a ferramenta carimba o seu - append-only, nao se edita, registro aqui; (2) flag DIVERGENCIA contracts.md (diz que publicacao/timeline seguem sem handler - nao seguem mais desde as TASK-0023/0025/0026) fica para a consolidacao da P-04, o arquivo so se escreve no fecho; (3) parked idea do StreamSession (ReadAsync parseia o log inteiro por wake) e da mesma familia do S-32
- 2026-11-09 21:20:16 | odev | transicao IN_DEVELOPMENT -> AWAITING_QA; entrega 4 de 10 da P-04 (FR-024 + primeiro ponto do DoD: post chega em menos de 1s)
- 2026-11-09 21:35:41 | qa | QA attempt 1/2: baseline gate 304/304 exit 0 (20261109-213326-test-gate.log, exit: 0 e Aprovado 304 lidos no arquivo - S-22); git baseline da QA capturado em QA-0025-git-baseline.txt com 83 entradas
- 2026-11-09 21:42:42 | qa | 5 cenarios proprios escritos em backend/tests/Squid.Tests/Stream/QaTask0026Tests.cs e EXECUTADOS: 5/5 verdes exit 0 (20261109-214136-test-filter.log, exit: 0 e Aprovado 5 no arquivo)
- 2026-11-09 21:52:26 | qa | prova can-fail dos seis ACs: 6 ciclos de mutacao (evento do quadro, cursor da reconexao, cadencia do ping, gate de visibilidade na entrega, 401, hub refem do assinante) - todos PROVEN, vermelhos lidos dentro dos logs, SHA256 de volta ao PRE (S-16/S-23), residuo QA-MUTATION 0
- 2026-11-09 21:53:13 | qa | gêmeo bash do gate executado no WSL: probe mostra dotnet ausente no Linux desta maquina, test sem alvo da no *.sln found within depth 3 e test com alvo da exit 127 - suite Linux fica unverified, achado pre-existente da QA-0024 re-confirmado
- 2026-11-09 22:01:31 | qa | 6 cenarios executados depois de incluir o espelho do AC-04 (post que ficou visivel durante a desconexao entrega na reconexao): 6/6 verdes exit 0 (20261109-220026-test-filter.log) e 6 observacoes gravadas com o logger detailed
- 2026-11-09 22:13:09 | qa | can-fail final com 7 ciclos (inclui M7 fechando o gate para todo mundo, que deixa o cenario espelho Ac04b vermelho na assercao de entrega): 7/7 PROVEN, vermelhos classificados dentro dos logs (S-22), SHA256 de volta ao PRE e residuo QA-MUTATION 0
- 2026-11-09 22:17:26 | qa | gate final AC-06: 310/310 exit 0 (20261109-221654-test-gate.log, exit: 0 e Aprovado 310 lidos no arquivo - S-22); baseline 304 + 6 cenarios da QA
- 2026-11-09 22:19:32 | qa | checker ps1 rodou 18/19 na primeira passada (so TASK-ROWS abaixo do minimo, por ordem: as linhas de evidencia do checker ainda nao existiam) - re-executado apos os appends
- 2026-11-09 22:20:11 | qa | gêmeo bash do checker executado no WSL: 19/19 checks OK, RESULT: PASS - os mesmos S-16/S-22/S-23 conferidos no Linux
- 2026-11-09 22:20:50 | qa | checker ps1 re-executado depois dos appends: 19/19 checks OK, RESULT: PASS (gate final 310 no log, 7/7 ciclos, SHA256=PRE, residuo 0, escopo 0 fora dos touches, git delta so artefatos da QA)
- 2026-11-09 22:22:24 | qa | confirmacao final da dupla de checkers depois de todos os appends: ps1 e sh 19/19 RESULT: PASS; verdicto da QA-0025 = PASSED (6/6 ACs provados por cenario executado, 7 ciclos can-fail, gate 310/310)
- 2026-11-09 22:27:35 | odev | ODEV verificou antes de transicionar: QA-0025-sse-stream-ao-vivo.md existe; gate final 20261109-221654-test-gate.log lido no arquivo (exit: 0, Aprovado 310, Com falha 0 - S-22); residuos QA-MUTATION = 0; check exit 0 (log 19, evidencias 19); veredito aceito - 6/6 ACs, 7 ciclos PROVEN, reconexao apos 6 eventos perdidos com replay exato, gate de visibilidade na entrega nos dois sentidos, checkers 19/19 nos dois sistemas
- 2026-11-09 22:27:35 | odev | achados adjudicados: F1 (gemeo sdd-test.sh sem *.slnx e sem guarda de dotnet - exit 127 como falha real) pre-existente = S-11/S-12 + flag DIVERGENCIA ja em consolidacao; F2 (hub sem teto de assinantes, R-16) vira S-36 no backlog - nenhum AC/FR pedia, endurecimento para antes de exposicao publica; F3 (exemplo author:{...} do contrato 5 vs string do PostView) - leitura adjudicada em nivel de especificacao: {...} e elise, nao forma fixa; o shape aprovado na QA-0022 (GET /api/posts) e o mesmo do SSE, entao sem conflito de contrato; registro para contracts.md na consolidacao - se o humano ler o exemplo como literal, e emenda de contrato e eu reabro
- 2026-11-09 22:27:36 | odev | transicao AWAITING_QA -> DONE; TASK-0026 = entrega 4 de 10 da P-04 encerrada (QA-0025 PASSED); cadeia libera a TASK-0027

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-09 21:16:34 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261109-202327-test-gate.log (baseline pre-edit 293/293) |
| 2026-11-09 21:16:35 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Stream -Fast | 0 | docs/sdd/runs/test-runs/20261109-210750-test-fast.log (novos 11/11) |
| 2026-11-09 21:16:35 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Stream.StreamVisibilityTests -Fast | 1 | docs/sdd/runs/test-runs/20261109-210856-test-fast.log (mutacao can-fail intencional do gate de visibilidade, restaurada: 2/2 vermelhos) |
| 2026-11-09 21:16:36 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261109-211322-test-gate.log (gate final 304/304) |
| 2026-11-09 21:16:36 | dev | git status --porcelain | 0 | console (working tree carrega as entregas anteriores nao commitadas; regra do fluxo impede commit) |
| 2026-11-09 21:16:37 | dev | Get-ChildItem backend e docs/sdd -Recurse por LastWriteTime apos 2026-11-09 20:19:00, sem bin e obj | 0 | console (11 arquivos de produto: 3 novos em Squid.Api/Posts + PostEndpoints + SquidServiceHost, 6 em tests/Squid.Tests/Stream; fora dos touches apenas logs do gate, o Log desta task e TASK-0025 escrita pelo odev no dispatch) |
| 2026-11-09 21:20:16 | odev | Select-String do gate final + varredura QA-MUTATION em backend + git status + sdd-state check | 0 | docs/sdd/runs/test-runs/20261109-211322-test-gate.log exit: 0 / Aprovado 304 / Com falha 0; residuos QA-MUTATION 0; toques fora NENHUM; check OK log 5 evidencias 6 |
| 2026-11-09 21:35:40 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261109-213326-test-gate.log (baseline da QA 304/304 lido dentro do log) |
| 2026-11-09 21:42:41 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~QaTask0026Tests | 0 | docs/sdd/runs/test-runs/20261109-214136-test-filter.log (5 cenarios proprios da QA, exit 0 lido no log) |
| 2026-11-09 21:52:25 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0026.ps1 | 0 | docs/sdd/qa/QA-0025-mutations.log (6 ciclos can-fail PROVEN, red classificado no log, HASH=PRE, residuo 0) |
| 2026-11-09 21:53:13 | qa | wsl bash docs/sdd/_tools/sdd-test.sh probe e test (gêmeo Linux do gate) | 1 | docs/sdd/qa/QA-0025-sh-twin.log (WSL sem dotnet; no *.sln found dentro do repo .slnx - achado pre-existente) |
| 2026-11-09 22:01:30 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~QaTask0026Tests | 0 | docs/sdd/runs/test-runs/20261109-220026-test-filter.log (6 cenarios proprios da QA, exit 0 lido no log, 5 antes do espelho do AC-04 em 20261109-214136) |
| 2026-11-09 22:01:30 | qa | dotnet test backend/Squid.slnx -c Debug -m:1 --filter FullyQualifiedName~QaTask0026Tests --logger console;verbosity=detailed | 0 | docs/sdd/qa/QA-0025-observations.log (6 observacoes: latencias 140/67/207 ms, delta [18,19,21,22,23], ping 204 ms, replay [16], replay espelho [26,27], 8 publicacoes 1564 ms) |
| 2026-11-09 22:13:08 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0026.ps1 | 0 | docs/sdd/qa/QA-0025-mutations.log (execucao final: 7 ciclos M1..M7 PROVEN, SUMMARY cycles=7 failed=0 residue=0, HASH=PRE em todos; reds: 220845 Ac01, 220914 Ac02, 220948 Ac03, 221017 Ac04, 221047 Ac05, 221126 familia 11/17, 221215 Ac04b) |
| 2026-11-09 22:17:25 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261109-221654-test-gate.log (gate final AC-06: 310/310, exit 0 lido dentro do log) |
| 2026-11-09 22:19:32 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0026.ps1 | 1 | console (18/19: TASK-ROWS 7 de 8 porque as linhas proprias do checker ainda nao existiam - ordem dos passos, nao defeito do artefato; re-executado depois dos appends) |
| 2026-11-09 22:20:10 | qa | wsl bash docs/sdd/qa/qa-check-task-0026.sh (gêmeo Linux do checker) | 0 | docs/sdd/qa/QA-0025-check.log run1 (19/19 checks, RESULT: PASS no Linux) |
| 2026-11-09 22:20:50 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0026.ps1 | 0 | docs/sdd/qa/QA-0025-check.log run2 (19/19 checks, RESULT: PASS - gates S-22, hashes S-16/S-23, ressduo, escopo, git, sdd-state) |
| 2026-11-09 22:22:23 | qa | wsl bash docs/sdd/qa/qa-check-task-0026.sh e powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0026.ps1 (confirmacao final) | 0 | docs/sdd/qa/QA-0025-check.log runs 3-4 (19/19 e 19/19, RESULT: PASS nos dois sistemas, apos a ultima linha de evidencia) |
| 2026-11-09 22:27:37 | odev | Select-String do gate final + varredura QA-MUTATION em backend + sdd-state check | 0 | docs/sdd/runs/test-runs/20261109-221654-test-gate.log exit: 0 / Aprovado 310 / Com falha 0; residuos 0; check OK log 19 evidencias 19; QA-0025 presente |
