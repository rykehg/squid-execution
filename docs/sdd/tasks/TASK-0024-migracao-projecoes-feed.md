<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0024 — Migration versionada e projeções Dapper do feed

```yaml
runId: R-20261109-01
taskId: TASK-0024
status: DONE
agent: odev
phase: P-04
sequence: 2
createdAt: 2026-11-09 11:22:36
updatedAt: 2026-11-09 17:15:05
parent: SPEC-0009
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Persistence/schema/**", "backend/src/Squid.ReadModels/**", "backend/src/Squid.Worker/**", "backend/src/Squid.Service/**", "backend/Directory.Packages.props", "backend/tests/Squid.Tests/Projections/**"]
blocks: ["TASK-0025"]
blockedBy: ["TASK-0023"]
inputArtifacts: ["docs/sdd/specs/SPEC-0009-publicacao-feed-postagem.md", "specs/001-squid-mvp/data-model.md", "specs/001-squid-mvp/contracts/http-api.md"]
outputArtifacts: ["backend/src/Squid.Persistence/schema/**", "backend/src/Squid.ReadModels/**", "backend/src/Squid.Worker/**", "backend/src/Squid.Service/**"]
interviewRounds: 0/6
feasibilityRounds: 0/2
correctionAttempts: 0/2
qaAttempts: 0/2
contextHandoffs: 0/4
progressDelta: 0
humanGate:
  approvedBy: Henrique
  approvedDate: 2026-11-09
  scope: criacao de banco e migration do SQLite liberados para teste/dev/CI com script versionado no repo; producao segue gate humano (decisao 2026-11-09); Dapper aprovado como pacote das projecoes com versao em Directory.Packages.props (decisao 2026-11-09)
  expiresAt: NENHUM
blocker: NENHUM
nextAction: DONE - QA-0023 PASSED (6/6 ACs, gate 277/277, 4 mutacoes + probe de processo real); proxima entrega TASK-0025 com AC-08
evidence: []
memoryBankFlags: []
```

## Intent

Primeira migration do projeto (gate humano concedido no bloco): script SQL versionado e
idempotente com `timeline_item`, `home_timeline`, contador global de `Activity.Seq` e
`quota_ledger`, mais o projetor Dapper (evento → linhas, nunca no caminho de escrita — R-18) e
o pump no `Squid.Worker`.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: o script versionado aplica as tabelas `timeline_item` `(container_id, seq)`,
  `home_timeline` `(actor_id, seq)` com índice em `seq`, o contador global de atividades e
  `quota_ledger`; aplicar **duas vezes** é idempotente e `schema_version` registra a versão —
  teste (banco novo + reaplicação).
- AC-02: o projetor transforma um `Activity: Create` em linha de `timeline_item` (post resumido,
  autor, contadores, visibilidade efetiva) e nos `home_timeline` dos membros `Active` **no
  momento da projeção** (emenda da SPEC-0009 §B de 2026-11-09 15:50:43 — texto original "na hora
  do evento" alinhado após a QA-0023, que testou a versão emendada e passou) — teste invocando a
  função direto.
- AC-03: projeção fora do caminho de escrita: publicar com o projetor não executado → post OK e
  projeção vazia; executar o projetor → as linhas surgem — teste.
- AC-04: retomada idempotente: projetor reexecutado (inclusive após falha no meio) não duplica
  linhas e continua do último `seq` global processado — teste.
- AC-05: o pump como hosted service do `Squid.Worker` consome eventos na ordem (o teste invoca
  o serviço) e a migration é aplicada no init de dev/teste antes dos testes — teste.
- AC-06: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`
  exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Persistence/schema/**` (novo: scripts versionados + aplicação idempotente)
- `backend/src/Squid.ReadModels/**` (novo: projetor Dapper — `Dapper` aprovado, versão em `Directory.Packages.props` **se** o csproj do ReadModels ainda não o tiver — instalar foi gate concedido 2026-11-09)
- `backend/src/Squid.Worker/**` (pump/hosted service)
- `backend/src/Squid.Service/**` (init da migration + registro)
- `backend/tests/Squid.Tests/Projections/**` (novo)

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-19 | no | migration/banco liberado para teste/dev/CI (gate humano 2026-11-09); produção = gate |
| GAP-20 | no | `quota_ledger` em SQL puro + Dapper na mesma migration (emenda ao data-model §5) |
| GAP-23 | no | o contador global de `Activity.Seq` nasce nesta migration (tabela de contador single-row, incremento atômico em SQL) |
| — | no | ordem de aplicação: scripts numerados (`0001_*`, `0002_*`...) com checksum em `schema_version` — a da TASK-0023 (store de eventos) pode preceder se DEV criar a store com contador próprio; a interface `IActivityStore` não muda depois |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-09 11:34:55 | odev | task created from SPEC-0009 (entrega 2 de 10; blockedBy TASK-0023; PRIMEIRA migration do projeto com gate humano registrado no humanGate)
- 2026-11-09 14:05:02 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; blockedBy TASK-0023 resolve (DONE com QA-0022 PASSED, gate 266/266); baseline do gate e 266/266; PRIMEIRA migration do projeto com o humanGate ja registrado no bloco (liberada para teste/dev/CI)
- 2026-11-09 14:06:05 | odev | ODDV check: toque backend/Directory.Packages.props acrescentado ao control block antes do dispatch (faltava para o Dapper em central package management) e humanGate.scope completo com os dois gates de 2026-11-09 (migration dev/test/CI + Dapper)
- 2026-11-09 15:02:21 | dev | feasibility 1/2: baseline gate verde 266/266 exit 0; touches confirmado e completo; D-01 (seq da projecao = Activity.Seq global) resolvido por SPEC-0009 GAP-23 + contrato HTTP 5; D-02 (origem dos eventos do pump) resolvida pela regra de dependencia: ReadModels/Worker nao alcancam a store de Squid.Api e Worker->Squid.Service fecharia ciclo no probe Squid.Service->Squid.Worker, entao o pump le o mesmo log pela leitura Dapper de Squid.ReadModels com a semantica ReadAsync(afterSeq)
- 2026-11-09 15:41:31 | dev | implementacao: migration 0001 (timeline_item, home_timeline + indices em seq, activity_counter, quota_ledger) com aplica idempotente e checksum em schema_version aplicada no init do SquidServiceHost; contador global movido para a tabela single-row em transacao unica no SqliteActivityStore (IActivityStore e ReadAsync sem mudanca de contrato); projetor Dapper em Squid.ReadModels (pump em Squid.Worker como hosted service, le a partir do proprio high-water das projecoes); pacote novo no repo = somente Dapper (pino em Directory.Packages.props); 8 testes novos em tests/Squid.Tests/Projections verdes (filtro FullyQualifiedName~Squid.Tests.Projections -> exit 0)
- 2026-11-09 15:48:03 | dev | gate final: 274/274 aprovados (266 de baseline + 8 novos) exit 0; probe do processo Squid.Worker: host sobe (Application started) e o pump fica em polling, contendo a ausencia do schema como warning (ordem dev: subir o Squid.Service antes, que aplica a migration no init); git status: nenhum arquivo alterado fora dos touches (plan.md modificado e de sessao anterior, mtime 11:34)
- 2026-11-09 15:53:21 | odev | ODEV verificou antes de transicionar: gate final 20261109-154442-test-gate.log lido no arquivo (exit: 0, Aprovado 274, Com falha 0 - S-22); residuos QA-MUTATION em backend = 0; git status = so toques (schema/, ReadModels, Worker, Service, Packages.props, tests/Projections) + docs de fluxo; check exit 0 (log 6, evidencias 5); step result DONE aceito - D-01 (seq global nas projecoes) e D-02 (pump le o log pela leitura Dapper) resolvidas na escada da especificacao/regra de dependencia
- 2026-11-09 15:53:21 | odev | adjudicacoes do ODEV: (1) membros lidos no momento da PROJECAO e nao do evento - a SPEC-0009 B foi emendada (2026-11-09 15:50:43) porque FR-026 e o data-model 4 mandam estado corrente; o snapshot por evento virou S-34 no backlog; (2) parked idea dos Counts zerados registrada como conhecida (reconciliacao e de fase dona); (3) flags MUDANCA_RELEVANTE ARQUITETURA (ReadModels com Dapper+Orleans.Core) e PADRAO_NOVO (leitor deriva a chave GrainId, 1 ocorrencia OBSERVADO) ficam para a consolidacao
- 2026-11-09 15:53:22 | odev | transicao IN_DEVELOPMENT -> AWAITING_QA; entrega 2 de 10 da P-04 (primeira migration do projeto, gate humano registrado no humanGate)
- 2026-11-09 17:07:06 | qa | QA-0023 inicio: ACs de TASK-0024 lidos contra SPEC-0009 secao B + Gaps (GAP-19/20/23) e data-model 4/5; rota qa-integracao da memoria bank lida; gate de baseline reproduzido 274/274 exit 0 lido DENTRO do log 20261109-160508 (S-22)
- 2026-11-09 17:07:06 | qa | QA escreve 3 cenarios em backend/tests/Squid.Tests/Projections/QaTask0024Tests.cs (costura contador+append com falha injetada DEPOIS do incremento; borda de membership na projecao; re-projecao idempotente) - filtro 11/11 exit 0 em 20261109-161819
- 2026-11-09 17:07:06 | qa | can-fail: 4 mutacoes dentro dos touches (M1 contador +1 -> +2 em SqliteActivityStore.cs:118, M2 indice ix_home_timeline_seq do 0001, M3 filtro Active -> != Banned, M4 ON CONFLICT do InsertTimeline); SHA256 PRE antes de cada uma, 4 vermelhos classificados pelo log gravado (161942, 162426, 163035, 163658), restauracao byte-exact + bump de mtime + hash = PRE e verde em seguida (162257, 162815, 163334, 163920); residuo QA-MUTATION em backend = 0
- 2026-11-09 17:07:06 | qa | AC-05 no processo real: qa-worker-probe-task-0024.ps1 sobe o Squid.Worker.exe construido com controle negativo (verify = 1 antes do processo) e depois PROBE verify PASS em 4 s (2 eventos projetados na ordem de Seq, so membros Active) - 20261109-164624 exit 0; gemeo .sh executado e classificado exit 3 ambiente sem dotnet no WSL (20261109-164701) = Linux unverified
- 2026-11-09 17:07:06 | qa | AC-06: gate final 277/277 exit 0 (20261109-164555 = 266 baseline + 8 DEV + 3 QA, lido dentro do log); varredura de mtime desde o dispatch 14:05 = 22 arquivos de produto todos dentro dos touches; git status sem nada fora deles alem de artefatos de fluxo e dos 7 desta QA; sdd-test.sh gemeo do gate segue pre-existente exit 1 (20261109-165643)
- 2026-11-09 17:09:49 | qa | QA-0023 fechamento: qa-check-task-0024.ps1 e o gemeo .sh (WSL) fecham iguais - SUMMARY total=17 fails=0 RESULT: PASS, exit 0 (20261109-170839 e 20261109-170925); 6/6 ACs por cenario executado, 0 BLOCKER - verdict PASSED
- 2026-11-09 17:15:07 | odev | ODEV verificou antes de transicionar: QA-0023-migracao-projecoes-feed.md existe; gate final 20261109-164555-test-gate.log lido no arquivo (exit: 0, Aprovado 277, Com falha 0 - S-22); residuos QA-MUTATION em backend = 0; check exit 0 (log 15, evidencias 24); veredito aceito - 6/6 ACs, 4 mutacoes restauradas byte-exact, costura contador/append provada com falha injetada DEPOIS do incremento (counter volta, retry recebe o mesmo numero, counter == MAX(seq)), probe de processo real do Worker com controle negativo (AC-05), ON CONFLICT exercitado pela QA
- 2026-11-09 17:15:08 | odev | achado F1 da QA adjudicado: o texto do AC-02 ainda dizia "na hora do evento" - alinhado a emenda da SPEC-0009 B (15:50:43) sem mudanca de comportamento (a QA testou a versao emendada e passou); F2 = S-34 ja registrado; F3 (gemeo sdd-test.sh exit 1 vs memoria bank exit 3) segue como S-11 + flag DIVERGENCIA para a consolidacao; F4 = arquivos do odev, nada de produto
- 2026-11-09 17:15:08 | odev | transicao AWAITING_QA -> DONE; TASK-0024 = entrega 2 de 10 da P-04 encerrada (QA-0023 PASSED); cadeia libera a TASK-0025

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-09 15:48:04 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261109-141153-test-gate.log (baseline 266/266) |
| 2026-11-09 15:48:04 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 0 | stdout: 0 Aviso(s), 0 Erro(s) |
| 2026-11-09 15:48:05 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Projections | 0 | docs/sdd/runs/test-runs/20261109-154055-test-filter.log (8/8 novos) |
| 2026-11-09 15:48:05 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261109-154442-test-gate.log (274/274) |
| 2026-11-09 15:48:06 | dev | dotnet run --project backend/src/Squid.Worker --no-build -- --Storage:Path=<temp> (taskkill apos 14s) | 1 | stdout: Application started + warning contido do pump em banco sem schema |
| 2026-11-09 15:53:22 | odev | Select-String do gate final + varredura QA-MUTATION em backend + git status + sdd-state check | 0 | docs/sdd/runs/test-runs/20261109-154442-test-gate.log exit: 0 / Aprovado 274 / Com falha 0; residuos 0; toques fora NENHUM; check OK log 6 evidencias 5 |
| 2026-11-09 17:07:50 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (baseline desta QA) | 0 | docs/sdd/runs/test-runs/20261109-160508-test-gate.log (exit 0, Aprovado 274, Com falha 0) |
| 2026-11-09 17:07:50 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Projections (8 DEV + 3 QA) | 0 | docs/sdd/runs/test-runs/20261109-161819-test-filter.log (exit 0, Aprovado 11) |
| 2026-11-09 17:07:51 | qa | sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Projections (mutacao M1: SqliteActivityStore.cs:118 + 1 vira + 2) | 1 | docs/sdd/runs/test-runs/20261109-161942-test-filter.log (Com falha 1: QaTask0024Tests.cs:54) |
| 2026-11-09 17:07:51 | qa | sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Projections (mutacao M2: indice ix_home_timeline_seq comentado no 0001) | 1 | docs/sdd/runs/test-runs/20261109-162426-test-filter.log (Com falha 1: SchemaMigrationTests.cs:38) |
| 2026-11-09 17:07:52 | qa | sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Projections (mutacao M3: State == Active vira != Banned) | 1 | docs/sdd/runs/test-runs/20261109-163035-test-filter.log (Com falha 2: ActivityProjectorTests.cs:103 e QaTask0024Tests.cs:148) |
| 2026-11-09 17:07:52 | qa | sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Projections (mutacao M4: ON CONFLICT do InsertTimeline comentado) | 1 | docs/sdd/runs/test-runs/20261109-163658-test-filter.log (Com falha 1: QaTask0024Tests.cs:190) |
| 2026-11-09 17:07:53 | qa | sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Projections (apos restauracao M1, hash = PRE, mtime bump) | 0 | docs/sdd/runs/test-runs/20261109-162257-test-filter.log (exit 0, Aprovado 11) |
| 2026-11-09 17:07:54 | qa | sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Projections (apos restauracao M2, hash = PRE, mtime bump) | 0 | docs/sdd/runs/test-runs/20261109-162815-test-filter.log (exit 0, Aprovado 11) |
| 2026-11-09 17:07:54 | qa | sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Projections (apos restauracao M3, hash = PRE, mtime bump) | 0 | docs/sdd/runs/test-runs/20261109-163334-test-filter.log (exit 0, Aprovado 11) |
| 2026-11-09 17:07:54 | qa | sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Projections (apos restauracao M4, hash = PRE, mtime bump) | 0 | docs/sdd/runs/test-runs/20261109-163920-test-filter.log (exit 0, Aprovado 11) |
| 2026-11-09 17:07:55 | qa | Get-FileHash SHA256 dos 3 arquivos mutados + varredura QA-MUTATION em backend + varredura de mtime desde 14:05 | 0 | docs/sdd/runs/test-runs/20261109-170613-qa-restore-verify.log (3/3 = PRE, residuos 0, sweep 22, temp 0) |
| 2026-11-09 17:07:55 | qa | python docs/sdd/qa/qa-worker-fixture-task-0024.py prepare e verify antes de subir o Worker (controle negativo) | 1 | docs/sdd/runs/test-runs/20261109-164624-qa-worker-probe.log (negative-control ok; exit 1 esperado: nada projetado antes do processo) |
| 2026-11-09 17:07:56 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-worker-probe-task-0024.ps1 | 0 | docs/sdd/runs/test-runs/20261109-164624-qa-worker-probe.log (PROBE verify PASS em 4 s, 2 eventos na ordem) |
| 2026-11-09 17:08:17 | qa | wsl bash -lc bash docs/sdd/qa/qa-worker-probe-task-0024.sh (gemeo Linux do probe) | 3 | docs/sdd/runs/test-runs/20261109-164701-qa-worker-probe-linux.log (ambiente: sem dotnet no WSL, Linux unverified) |
| 2026-11-09 17:08:17 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate final) | 0 | docs/sdd/runs/test-runs/20261109-164555-test-gate.log (exit 0, Aprovado 277, Com falha 0) |
| 2026-11-09 17:08:18 | qa | bash docs/sdd/_tools/sdd-test.sh test via Git Bash (gemeo Linux do gate) | 1 | docs/sdd/runs/test-runs/20261109-165643-sdd-test-sh.log (pre-existente: no *.sln found, a solution e *.slnx) |
| 2026-11-09 17:09:50 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0024.ps1 | 0 | docs/sdd/runs/test-runs/20261109-170839-qa-check.log (SUMMARY total=17 fails=0, RESULT: PASS) |
| 2026-11-09 17:09:51 | qa | wsl bash -lc bash docs/sdd/qa/qa-check-task-0024.sh (gemeo Linux do check) | 0 | docs/sdd/runs/test-runs/20261109-170925-qa-check-linux.log (SUMMARY total=17 fails=0, RESULT: PASS) |
| 2026-11-09 17:15:08 | odev | Select-String do gate final + varredura QA-MUTATION em backend + sdd-state check | 0 | docs/sdd/runs/test-runs/20261109-164555-test-gate.log exit: 0 / Aprovado 277 / Com falha 0; residuos 0; check OK log 15 evidencias 24; QA-0023 presente |
