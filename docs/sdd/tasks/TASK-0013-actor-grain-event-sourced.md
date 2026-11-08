<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0013 — ActorGrain event-sourced com snapshot e truncamento

```yaml
runId: R-20261107-02
taskId: TASK-0013
status: DONE
agent: odev
phase: P-02
sequence: 2
createdAt: 2026-11-07 16:19:35
updatedAt: 2026-11-07 16:19:35
parent: SPEC-0007
authority: SPEC
parallel: true
touches: ["backend/src/Squid.Grains/Actors/**", "backend/tests/Squid.Tests/Actors/**"]
blocks: ["TASK-0014"]
blockedBy: []
inputArtifacts: ["docs/sdd/specs/SPEC-0007-identidade-ator-local-chaves-sessao-kit.md", "specs/001-squid-mvp/data-model.md"]
outputArtifacts: ["backend/src/Squid.Grains/Actors/**"]
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
nextAction: DONE - QA-0011 PASSED (5/5 ACs, qaAttempt 1/2); TASK-0013 encerrada
evidence: []
memoryBankFlags: [DIVERGENCIA: sdd-test.ps1 classificou vermelho real como ambiente - Test-SandboxSignature casa substring handle e o detector de resumo nao reconhece o Com falha! do xunit pt-BR, exit 1 (3 falhas) virou exit 3 - variante mais grave que S-13, codigo do exit errado - source: docs/sdd/_tools/sdd-test.ps1:80, docs/sdd/runs/test-runs/20261107-172502-test-filter.log:2 - virou S-22 no backlog] [GAP: caminho Linux nativo nao executavel - WSL sem dotnet SDK (instalacao exige gate); gemeo .sh so com caminho explicito, ja backlogado S-11/S-12 - source: docs/sdd/_tools/sdd-test.sh:18]
```

## Intent

Entrega o agregado de identidade da fase (SPEC-0007 B-08): `ActorGrain` com log de eventos,
snapshot periódico e **truncamento** do log após o snapshot, mais a regra de `handle` único —
é o alvo do registro da TASK-0014.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: criar ator com `handle` válido persiste eventos (`ActorCreated` etc.) e o estado
  reconstruído dos eventos é idêntico ao estado direto — teste de round-trip (replay).
- AC-02: `handle` com `[a-z0-9_]{3,30}` malformado é rejeitado com erro tipado; duplicidade de
  `handle` é rejeitada pelo índice/estado — nada é persistido nos dois casos — teste positivo e
  negativo.
- AC-03: após atingir o limiar de eventos, um snapshot é gravado e o log anterior é **truncado**
  (o log deixa de conter os eventos antigos ao snapshot) — teste afirma a truncagem observável.
- AC-04: reconstruir o estado depois do truncamento (snapshot + eventos novos) produz estado
  idêntico ao estado direto — teste que falharia se o truncamento perdesse informação.
- AC-05: o estado persistido contém somente material embrulhado/público (sem `RK`/privada em
  claro) — teste de inspeção da serialização; e gate `sdd-test.ps1 test` exit 0 sem arquivo
  alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Grains/Actors/**` (novo diretório; `GrainsAssembly.cs` só se precisar registrar)
- `backend/tests/Squid.Tests/Actors/**` (novo diretório)

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-09 | no | o grain persiste só material embrulhado/público; destravar `RK` em runtime não é desta fase |
| — | no | storage é o `IGrainStorage` SQLite com ETag da TASK-0003; o formato do log/snapshot (tabela, colunas) é decisão de implementação do DEV dentro desse storage, registrada no log |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`

- 2026-11-07 16:19:35 | odev | task created from SPEC-0007 (P-02, lote paralelo inicial com TASK-0012)
- 2026-11-07 16:27:06 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; lote paralelo com TASK-0012 (touches disjuntos verificados)
- 2026-11-07 17:12:51 | dev | baseline antes de qualquer edicao: sdd-test.ps1 test -> exit 0 com 98 testes aprovados
- 2026-11-07 17:12:51 | dev | feasibility 1/2: unit = backend/src/Squid.Grains/Actors/** (diretorio novo), caller = TASK-0014 (Squid.Api/Auth, registro em Squid.Service), dados = stream de eventos por ator no IGrainStorage SQLite com ETag da TASK-0003; touches confirmado e disjunto de TASK-0012; GrainsAssembly.cs nao precisa de alteracao
- 2026-11-07 17:12:51 | dev | decisao: ActorGrain nasce como agregado event-sourced puro (classe comum), nao grain Orleans - Squid.Grains nao tem pacote Orleans e herdar Grain exigiria PackageReference fora dos touches (AC-05 proibe alterar arquivo fora deles); o wrapper Orleans fica para a composicao
- 2026-11-07 17:12:51 | dev | decisao de formato (assumida do task): um documento JSON por ator em GrainState, StoreName "actors", GrainId "actor/<handle>", forma { Snapshot: ActorState?, SnapshotVersion: long, Events: ActorEvent[] }; ao cruzar o limiar grava Snapshot e Events.Clear() na mesma escrita, e o log deixa de conter os eventos ja cobertos (doc 05 4.7 #3)
- 2026-11-07 17:12:51 | dev | decisao: unicidade de handle = a linha do proprio stream (chave = handle); CreateAsync valida o formato e recusa stream existente antes de qualquer escrita, com erro tipado InvalidActorHandleException (mensagem cita [a-z0-9_]{3,30}) ou ActorHandleTakenException - nada persistido nos dois casos
- 2026-11-07 17:12:51 | dev | decisao: limiar DefaultSnapshotThreshold = 8 (const publica; o construtor aceita outro valor) para que AC-03 e AC-04 sejam alcancaveis pela API publica padrao
- 2026-11-07 17:12:51 | dev | decisao: dois tipos de evento - ActorCreated (handle, displayName, material) e ActorDisplayNameChanged, evento repetivel que existe para o log cruzar o limiar exigido por AC-03/AC-04; material persistido so como base64 embrulhado + publico, sem slot para RK ou privada em claro (GAP-09)
- 2026-11-07 17:12:51 | dev | escrita: 7 arquivos em backend/src/Squid.Grains/Actors/** e 4 em backend/tests/Squid.Tests/Actors/**; o adaptador IActorEventStore -> SqliteGrainStorage mora nos testes porque ADR-35 proibe Squid.Grains -> Squid.Persistence - o exemplar de producao entra em Squid.Service no wiring da TASK-0014
- 2026-11-07 17:12:51 | dev | ajuste 1 (erro de compilacao real): o default do construtor nao enxergava a const da propria classe (CS0103) -> dois construtores explicitos; ajuste 2 (defeito do teste, nao do produto): a inspecao de AC-05 comparava o base64 cru, mas o System.Text.Json escapa '+' e '/' - a assercao passou a usar a forma serializada
- 2026-11-07 17:12:51 | dev | gate final: sdd-test.ps1 test -> exit 0 com 113 testes aprovados (98 pre-existentes + 15 novos); git status mostra apenas backend/src/Squid.Grains/Actors/ e backend/tests/Squid.Tests/Actors/ novos, alem das mudancas de docs do odev ja presentes no baseline - nenhum arquivo alterado fora dos touches
- 2026-11-07 17:13:47 | odev | odev | ODEV verificou antes de transicionar: check exit 0 (12 logs, 6 evidencias); gate final lido no proprio log docs/sdd/runs/test-runs/20261107-170827-test-gate.log com Aprovado 113 / Com falha 0 / exit 0; os 7 arquivos de produto existem em backend/src/Squid.Grains/Actors/; Squid.Grains.csproj sem PackageReference (flag BUILD confirmada no arquivo)
- 2026-11-07 17:13:47 | odev | odev | memoryBankFlags do step result gravados no control block; o GAP do adaptador de producao (existe so nos testes) e encaminhado como contexto de wiring para a TASK-0014
- 2026-11-07 17:13:47 | odev | odev | status IN_DEVELOPMENT -> AWAITING_QA; step result DONE aceito (logLines e evidenceRows completos, sem timestamps inventados)
- 2026-11-07 17:37:48 | qa | qa attempt 1/2: 5 ACs verificados por execucao propria - gate full 119/119 exit 0 (baseline 20261107-172124 e pos-restauracao 20261107-172854) e filtro Actors 15/15 exit 0; mutacoes S-16 M1 (truncamento desligado) e M2 (snapshot descartado no limiar) vermelhas com exit real 1 cada, restauracao byte-exata SHA256 PRE=POST em 5 checkpoints (33FFE862...5440); veredito PASSED sem BLOCKER, QA-0011 gravado
- 2026-11-07 17:37:48 | qa | qa findings: F1 MAJOR harness - sdd-test.ps1:79-83 imprime exit 3 (ambiente) para vermelho real (casa handle e nao reconhece o resumo Com falha!), variante pior que S-13; F2 = S-11/S-12 ja backlogados (.sh nao descobre .slnx); F3 caminhos Crypto sujos sao da TASK-0012 paralela e estavam verdes nos dois gates (119 = 113 + 6) sem falha de interferencia; WSL sem dotnet SDK - caminho Linux unverified
- 2026-11-07 17:43:34 | odev | odev | ODEV verificou o finding F1 na fonte antes de adjudicar: docs/sdd/_tools/sdd-test.ps1:79-83 casa substring "handle" e a regex de resumo nao inclui "Com falha!"; no log da mutacao 20261107-172502-test-filter.log a linha 56 diz Com falha! e o cabecalho diz exit: 1 - o wrapper imprimiu exit 3 (ambiente) para vermelho real. Pre-existente (variante pior do S-13, ferramenta do fluxo, nao produto desta task): virou S-22 no backlog, nao bloqueia a entrega
- 2026-11-07 17:43:34 | odev | odev | F2 = S-11/S-12 ja backlogados (reproduzido); F3 = escritas da TASK-0012 paralela fora do escopo da QA e verdes nos dois gates (119 = 113 + 6), nenhuma interferencia; caminho Linux nativo registrado unverified (WSL sem dotnet SDK, instalar exige gate)
- 2026-11-07 17:43:34 | odev | odev | verdicto QA-0011 adjudicado: PASSED com 5/5 ACs provadas por execucao propria da QA (15/15 no filtro, duas mutacoes can-fail vermelhas com exit real 1, restauracao byte-exact SHA256 5 checkpoints PRE=POST, gate 119/119 apos restauracao); 0 BLOCKER; transicao AWAITING_QA -> DONE

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-07 17:12:52 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261107-164719-test-gate.log - baseline, 98 aprovados antes de qualquer edicao |
| 2026-11-07 17:12:52 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Actors | 1 | docs/sdd/runs/test-runs/20261107-170329-test-filter.log - CS0103 no construtor; corrigido antes do retry |
| 2026-11-07 17:12:53 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Actors | 1 | docs/sdd/runs/test-runs/20261107-170456-test-filter.log - 1 falha na inspecao AC-05 (base64 escapado pelo serializador); corrigido antes do retry |
| 2026-11-07 17:12:53 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Actors | 0 | docs/sdd/runs/test-runs/20261107-170713-test-filter.log - 15 casos novos aprovados |
| 2026-11-07 17:12:54 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261107-170827-test-gate.log - gate final: 113 aprovados, suite inteira com BuildProof |
| 2026-11-07 17:12:54 | dev | git status --porcelain | 0 | apenas backend/src/Squid.Grains/Actors/ e backend/tests/Squid.Tests/Actors/ novos; docs/sdd ja alterados antes do primeiro edit do DEV - do odev |
| 2026-11-07 17:38:08 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261107-172124-test-gate.log - baseline da QA antes das mutacoes: 119/119 aprovados |
| 2026-11-07 17:38:09 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Actors | 0 | docs/sdd/runs/test-runs/20261107-172302-test-filter.log - 15/15 aprovados (AC-01 a AC-05 executados) |
| 2026-11-07 17:38:09 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Actors | 1 | docs/sdd/runs/test-runs/20261107-172502-test-filter.log - mutacao M1 truncamento desligado: exit real 1 no cabecalho, 3 falhas AC-03 :117 AC-04 :139 AC-05 :60; wrapper imprimiu exit 3 = finding F1 |
| 2026-11-07 17:38:10 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Actors | 1 | docs/sdd/runs/test-runs/20261107-172653-test-filter.log - mutacao M2 snapshot descartado: exit real 1 no cabecalho, 3 falhas AC-04 :139 AC-03 :115 AC-05 :59; wrapper imprimiu exit 3 = finding F1 |
| 2026-11-07 17:38:10 | qa | Get-FileHash -Algorithm SHA256 backend/src/Squid.Grains/Actors/ActorGrain.cs (S-16, 5 checkpoints) | 0 | docs/sdd/qa/QA-0011-actor-grain-event-sourced.md - PRE = POST = 33FFE8627BA83435B2B9E1FEB44BC4F149E89E03886D8793FE8B296BB2015440 byte-exact |
| 2026-11-07 17:38:30 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261107-172854-test-gate.log - gate final pos-restauracao byte-exata: 119/119 aprovados |
| 2026-11-07 17:38:31 | qa | bash docs/sdd/_tools/sdd-test.sh test backend/Squid.slnx (Git Bash + dotnet 10.0.403) | 0 | gemeo .sh executado: SH_EXIT=0 com 119/119; WSL sem dotnet SDK permanece unverified |
| 2026-11-07 17:38:31 | qa | bash docs/sdd/_tools/sdd-test.sh test (Git Bash, sem target) | 1 | BARE_SH_EXIT=1 - stderr no *.sln found + MSB1003; reproducao do S-11 ja backlogado = finding F2 |
| 2026-11-07 17:38:32 | qa | dotnet test backend/Squid.slnx -c Debug --no-build --filter FullyQualifiedName~Squid.Tests.Actors --list-tests | 0 | 15 nomes Squid.Tests.Actors + 6 nomes Squid.Tests.Crypto = delta 113 para 119 do gate explicado (TASK-0012 paralela) |
| 2026-11-07 17:38:32 | qa | git status --porcelain | 0 | escopo: escritas da TASK-0013 apenas nos touches Actors; Crypto + Squid.Api + Directory.Packages.props = TASK-0012 fora de escopo e verdes; docs = ODEV baseline - finding F3 |
