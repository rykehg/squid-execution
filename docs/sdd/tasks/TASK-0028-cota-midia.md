<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0028 — Cota de mídia da instância (`507`)

```yaml
runId: R-20261109-01
taskId: TASK-0028
status: DONE
agent: odev
phase: P-04
sequence: 2
createdAt: 2026-11-09 11:22:36
updatedAt: 2026-11-10 15:27:14
parent: SPEC-0009
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Api/Media/**", "backend/src/Squid.ReadModels/**", "backend/src/Squid.Worker/**", "backend/src/Squid.Service/**", "backend/Directory.Packages.props", "backend/src/Squid.Service/Squid.Service.csproj", "backend/tests/Squid.Tests/Quota/**"]
blocks: ["TASK-0029"]
blockedBy: ["TASK-0027"]
inputArtifacts: ["docs/sdd/specs/SPEC-0009-publicacao-feed-postagem.md", "specs/001-squid-mvp/data-model.md", "specs/001-squid-mvp/contracts/http-api.md"]
outputArtifacts: ["backend/src/Squid.Api/Media/**", "backend/src/Squid.ReadModels/**", "backend/src/Squid.Worker/**", "backend/src/Squid.Service/**"]
interviewRounds: 0/6
feasibilityRounds: 0/2
correctionAttempts: 0/2
qaAttempts: 0/2
contextHandoffs: 0/4
progressDelta: 0
humanGate:
  approvedBy: Henrique
  approvedDate: 2026-11-09
  scope: ledger de cota em SQL puro + Dapper na migration versionada (emenda ao data-model 5, EF Core substituido) - decisao da entrevista de abertura da P-04; SkiaSharp.NativeAssets.Linux 4.155.0 aprovado em 2026-11-10 08:00:15 para CI ubuntu e compose (vira AC-06)
  expiresAt: NENHUM
blocker: NENHUM
nextAction: DONE - QA-0028 PASSED (6/6 ACs, gate 348/348, 9 mutacoes); proxima entrega TASK-0029
evidence: []
memoryBankFlags: []
```

## Intent

Fecha FR-029 + data-model §2.13: `quota_ledger` (escopo instância, bytes inteiros, default
10 GiB) já criado na migration da TASK-0024, gate rápido no upload → `507 quota_exceeded`, e
reconciliação diária por varredura dos blobs como fonte da verdade.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: com `Quota:LimitBytes` pequeno na config, upload que estoura → `507 quota_exceeded`
  com **nada** gravado (blob ausente, ledger inalterado — hash antes/depois); upload seguinte
  dentro do limite → `UsedBytes` soma o tamanho — teste.
- AC-02: reconciliação: com `UsedBytes` dessincronizado artificialmente, a varredura dos blobs
  corrige o valor para o real e atualiza `UpdatedAt` — teste invocando a função diretamente.
- AC-03: `UsedBytes`/`LimitBytes` são **inteiro** (R-19); default 10 GiB quando não configurado;
  a linha nasce no primeiro uso — teste.
- AC-04: o hosted service de reconciliação roda a varredura (o teste invoca o serviço) sem
  tocar o caminho de escrita do upload — teste.
- AC-05: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`
  exit 0; nenhum arquivo alterado fora dos `touches`.
- AC-06 (gate humano 2026-11-10 08:00:15): `SkiaSharp.NativeAssets.Linux` na mesma versão do
  `SkiaSharp` (4.155.0) entra em `Directory.Packages.props` + `Squid.Service.csproj` — sem ele a
  CI ubuntu e o compose não decodificam imagem (`DllNotFoundException`); um teste estático
  verifica que o pacote está declarado (a execução do decode em Linux continua `unverified`
  nesta máquina) — teste.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Api/Media/**` (gate `507` antes de gravar blob)
- `backend/src/Squid.ReadModels/**` (ledger com Dapper — mesma migration da TASK-0024)
- `backend/src/Squid.Worker/**` (hosted service de reconciliação)
- `backend/src/Squid.Service/**` (registro + config + `PackageReference` dos natives)
- `backend/Directory.Packages.props` (versão do `SkiaSharp.NativeAssets.Linux` — gate 2026-11-10)
- `backend/tests/Squid.Tests/Quota/**` (novo)

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-20 | no | ledger em SQL/Dapper (emenda ao data-model §5, gate humano 2026-11-09) |
| — | no | a reconciliação roda por `Quota:ReconcileHours` (default 24) e sob demanda no teste; drift entre varreduras é aceito (o gate rápido é otimista, a varredura corrige) |
| — | no | `507` responde no formato do catálogo §8; nenhuma rota lista a cota no contrato — visibilidade da cota fica para fase de admin |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-09 11:34:57 | odev | task created from SPEC-0009 (entrega 6 de 10; blockedBy TASK-0027; emenda do ledger ao data-model 5 no humanGate)
- 2026-11-10 08:03:17 | odev | gate humano 2026-11-10 08:00:15 (Henrique): SkiaSharp.NativeAssets.Linux aprovado - o DEV da TASK-0027 descobriu que o SkiaSharp traz natives so de Win32+macOS e o decode em Linux (CI ubuntu + compose) quebraria com DllNotFoundException; pacote acrescentado ao touches e ao humanGate, correcao vira AC-06 desta task (que e a proxima da cadeia)
- 2026-11-10 13:11:12 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; blockedBy TASK-0027 resolve (DONE com QA-0027 PASSED na tentativa 2/2, gate 337/337); baseline do gate e 337/337; AC-06 (SkiaSharp.NativeAssets.Linux) com gate humano 2026-11-10 registrado
- 2026-11-10 13:58:21 | dev | feasibility: baseline do gate 337/337 (exit 0); touches confirmado no codigo (MediaEndpoints, MediaOptions, SqliteMediaStore, SquidServiceHost, ActivityProjectionPump) e quota_ledger ja existe na migration 0001 da TASK-0024; nenhuma duvida bloqueante - SPEC-0009 secao F, data-model 2.13 e contrato 5/8 decidem o resto
- 2026-11-10 13:58:21 | dev | ledger Dapper em Squid.ReadModels/Quota: IQuotaLedger + SqliteQuotaLedger com um unico UPDATE condicional atomico; a recusa nao toca a linha e a linha nasce no primeiro uso; gate 507 quota_exceeded em MediaEndpoints antes do blob, com nada gravado
- 2026-11-10 13:58:22 | dev | reconciliacao: QuotaReconciler varre Media:BlobRoot e reescreve used_bytes e updated_at (a varredura e a fonte da verdade); QuotaReconcileService hosted no Squid.Worker com Quota:ReconcileHours default 24; IQuotaLedger registrado no SquidServiceHost e o upload roda sem servico hospedado
- 2026-11-10 13:58:22 | dev | AC-06 gate humano 2026-11-10 08:00:15: SkiaSharp.NativeAssets.Linux 4.155.0 em Directory.Packages.props e PackageReference em Squid.Service.csproj; dotnet restore exit 0 com o pacote disponivel
- 2026-11-10 13:58:23 | dev | testes novos em backend/tests/Squid.Tests/Quota: dotnet test com filtro Quota saiu exit 0 com 6/6 (AC-01, AC-02, AC-03 em dois testes, AC-04, AC-06)
- 2026-11-10 13:58:23 | dev | gate final: sdd-test.ps1 test exit 0 com 343/343 (baseline 337 + 6 novos)
- 2026-11-10 14:03:14 | odev | ODEV verificou antes de transicionar: gate final 20261110-135429-test-gate.log lido no arquivo (exit: 0, Aprovado 343, Com falha 0 - S-22); residuos QA-MUTATION = 0; check exit 0 (log 9, evidencias 6, carimbadas pelo proprio DEV); step result DONE aceito - nenhum duvida, 6 ACs com 1 teste por AC; decisoes internas do DEV repassadas a QA: gate sobre o tamanho PROCESSADO, incremento otimista nao move updated_at (coluna e da reconciliacao, data-model 2.13), recusa nao toca limit_bytes
- 2026-11-10 14:03:15 | odev | transicao IN_DEVELOPMENT -> AWAITING_QA; entrega 6 de 10 da P-04 (AC-06 natives Linux com restore exit 0 - execucao em Linux segue unverified ate a CI)
- 2026-11-10 15:13:05 | qa | attempt 1/2: leitura normativa SPEC-0009 secao F + Gaps (GAP-19/20), data-model 2.13 e 5 (ledger em SQL+Dapper - emenda humana GAP-20), contrato http-api 5 e 8, humanGate da task; baseline do gate 343/343 capturado antes de escrever (log 20261110-142613 com exit: 0 lido dentro)
- 2026-11-10 15:13:05 | qa | 5 cenarios proprios escritos em backend/tests/Squid.Tests/Quota/QaTask0028Tests.cs e executados: a 1a execucao saiu vermelha por defeito do fixture da QA (APP1 length 2 bytes grande - o ExifStripper manteve o arquivo por nao parsear, comportamento documentado em ExifStripper.cs:8), fixture corrigido e verde 5/5 (log 20261110-144402)
- 2026-11-10 15:13:05 | qa | decisoes do DEV verificadas por execucao: gate conta os bytes PROCESSADOS (9000 pedidos, 329 guardados, resposta 200), recusa nao toca nada nem o limit_bytes sentinela 777 (hash da linha inteira + digest do volume com SHA-256 por arquivo + contagem de registros), incremento otimista nao move updated_at, varredura corrige drift nas duas direcoes e segue blob apagado as costas
- 2026-11-10 15:13:05 | qa | 9 ciclos can-fail M1..M9 (M1 e M2 AC-01, M3 AC-02, M4 AC-04, M5 AC-03, M6 e M7 AC-06, M8 e M9 os cenarios da QA) todos PROVEN com o vermelho classificado dentro do proprio log (S-22), restore HASH=PRE com bump de mtime (S-23), residuo QA-MUTATION 0 e backups 0
- 2026-11-10 15:13:05 | qa | AC-06: dotnet restore do Squid.Service exit 0 com SkiaSharp.NativeAssets.Linux 4.155.0 no cache (mesma versao do SkiaSharp); execucao em Linux segue unverified - WSL sem dotnet (sdd-test.sh probe exit 0 e test exit 1 com exit 127)
- 2026-11-10 15:13:05 | qa | gate final sdd-test.ps1 test exit 0 com 348/348 lido dentro do log 20261110-150722 (343 do DEV + 5 da QA); escopo 15 arquivos desde 13:11 todos nos touches, git +7 apenas artefatos QA e o teste da QA; veredito PASSED em docs/sdd/qa/QA-0028-cota-midia.md
- 2026-11-10 15:27:16 | odev | ODEV verificou antes de transicionar: QA-0028-cota-midia.md com verdicto PASSED; gate final 20261110-150722-test-gate.log lido no arquivo (exit: 0, Aprovado 348, Com falha 0 - S-22); residuos QA-MUTATION = 0; check exit 0 (log 17, evidencias 21); veredito aceito - 6/6 ACs, 9 ciclos can-fail, 507 provado com digest de volume inteiro, reconciliacao corrigindo drift nas duas direcoes, AC-06 com restore do pacote exit 0
- 2026-11-10 15:27:16 | odev | flags para a consolidacao do fecho: [GAP] contracts.md sem rotas de midia/507 (re-levantado nas QA-0026/0027/0028) e MUDANCA_RELEVANTE PADRAO (ledger SQL+Dapper com gate otimista + varredura como fonte da verdade, emenda GAP-20); achado unico restante segue o pre-existente sdd-test.sh (S-11/S-12)
- 2026-11-10 15:27:17 | odev | transicao AWAITING_QA -> DONE; TASK-0028 = entrega 6 de 10 da P-04 encerrada (QA-0028 PASSED); cadeia libera a TASK-0029

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-10 13:58:45 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test - baseline antes das mudancas | 0 | docs/sdd/runs/test-runs/20261110-131629-test-gate.log - 337/337 |
| 2026-11-10 13:58:46 | dev | dotnet restore backend/src/Squid.Service/Squid.Service.csproj | 0 | Squid.Service restaurado com SkiaSharp.NativeAssets.Linux 4.155.0 |
| 2026-11-10 13:58:46 | dev | dotnet build backend/tests/Squid.Tests/Squid.Tests.csproj -nologo | 0 | build sem erros - apenas warnings pre-existentes do repo |
| 2026-11-10 13:58:47 | dev | dotnet test backend/tests/Squid.Tests/Squid.Tests.csproj --no-build --filter FullyQualifiedName~Squid.Tests.Quota | 0 | 6/6 testes novos aprovados em 5 s |
| 2026-11-10 13:58:47 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test - gate final | 0 | docs/sdd/runs/test-runs/20261110-135429-test-gate.log - 343/343 |
| 2026-11-10 13:58:48 | dev | git status --porcelain | 0 | escrita minha restrita aos touches - Media, ReadModels/Quota, Worker/Quota, Service, props, csproj e tests/Quota |
| 2026-11-10 14:03:15 | odev | Select-String do gate final + varredura QA-MUTATION em backend + sdd-state check | 0 | docs/sdd/runs/test-runs/20261110-135429-test-gate.log exit: 0 / Aprovado 343 / Com falha 0; residuos 0; check OK log 9 evidencias 6 |
| 2026-11-10 15:16:55 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test - baseline da QA antes de escrever | 0 | docs/sdd/runs/test-runs/20261110-142613-test-gate.log - exit 0 - Com falha 0 / Aprovado 343 / Total 343 |
| 2026-11-10 15:16:55 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~QaTask0028Tests - 1a execucao do fixture | 1 | docs/sdd/runs/test-runs/20261110-143449-test-filter.log - Ac01c vermelha por defeito do fixture da QA com APP1 length 2 bytes grande - corrigido depois |
| 2026-11-10 15:16:55 | qa | dotnet test backend/tests/Squid.Tests/Squid.Tests.csproj --filter FullyQualifiedName~QaTask0028Tests --logger console;verbosity=detailed | 0 | docs/sdd/qa/QA-0028-observations.log - Aprovados 5 com as 5 observacoes (9000 pedidos e 329 guardados) |
| 2026-11-10 15:16:56 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~QaTask0028Tests | 0 | docs/sdd/runs/test-runs/20261110-144402-test-filter.log - Com falha 0 / Aprovado 5 / Total 5 |
| 2026-11-10 15:16:56 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Quota | 0 | docs/sdd/runs/test-runs/20261110-144447-test-filter.log - Com falha 0 / Aprovado 11 / Total 11 (6 do DEV e 5 da QA) |
| 2026-11-10 15:16:57 | qa | dotnet restore backend/src/Squid.Service/Squid.Service.csproj | 0 | cache NUGET_PACKAGES com skiasharp.nativeassets.linux 4.155.0 igual ao SkiaSharp - AC-06 |
| 2026-11-10 15:16:57 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0028.ps1 - 9 ciclos can-fail | 0 | docs/sdd/qa/QA-0028-mutations.log - 9/9 PROVEN - SUMMARY cycles=9 failed=0 residue-QA-MUTATION=0 temp-backups=0 |
| 2026-11-10 15:16:58 | qa | bash docs/sdd/_tools/sdd-test.sh probe no WSL | 0 | docs/sdd/qa/QA-0028-sh-twin.log - dotnet command not found - plataforma Linux unverified |
| 2026-11-10 15:16:58 | qa | bash docs/sdd/_tools/sdd-test.sh test backend/Squid.slnx no WSL | 1 | docs/sdd/qa/QA-0028-sh-twin.log - o gemeo classifica exit 127 como falha real sem dotnet - achado pre-existente |
| 2026-11-10 15:16:59 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test - gate final AC-05 | 0 | docs/sdd/runs/test-runs/20261110-150722-test-gate.log - exit 0 - Com falha 0 / Aprovado 348 / Total 348 (343 do DEV e 5 da QA) |
| 2026-11-10 15:16:59 | qa | varredura de escopo: mtimes desde 2026-11-10 13:11 em backend e git status contra QA-0028-git-baseline | 0 | 15 arquivos com mtime na janela todos nos touches - git +7 so artefatos QA e QaTask0028Tests.cs - residuo QA-MUTATION 0 |
| 2026-11-10 15:17:00 | qa | Select-String de Quota ReconcileHours em backend/src/Squid.Worker/Program.cs | 0 | Program.cs:38 le a chave e Program.cs:41 cai no fallback 24 - default do gap da task |
| 2026-11-10 15:22:36 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0028.ps1 | 0 | docs/sdd/qa/QA-0028-check.log - 22 checks - RESULT PASS - exit 0 |
| 2026-11-10 15:22:37 | qa | bash docs/sdd/qa/qa-check-task-0028.sh no WSL | 0 | docs/sdd/qa/QA-0028-check.log - 22 checks - RESULT PASS - exit 0 no Linux |
| 2026-11-10 15:27:17 | odev | Select-String do gate final + varredura QA-MUTATION em backend + verdicto do doc + sdd-state check | 0 | docs/sdd/runs/test-runs/20261110-150722-test-gate.log exit: 0 / Aprovado 348 / Com falha 0; residuos 0; verdicto PASSED no QA-0028-cota-midia.md; check OK log 17 evidencias 21 |
