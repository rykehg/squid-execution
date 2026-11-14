<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0053 — Mídia federada: `attachment` na saída + `GET /media/{hash}`

```yaml
runId: R-20261113-01
taskId: TASK-0053
status: DONE
agent: odev
phase: P-06
sequence: 2
createdAt: 2026-11-14 12:49:25
updatedAt: 2026-11-14 14:38:24
parent: SPEC-0011
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Protocols/**", "backend/src/Squid.Api/Media/**", "backend/src/Squid.Api/Posts/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/**"]
blocks: ["TASK-0047"]
blockedBy: []
inputArtifacts: ["docs/sdd/specs/SPEC-0011-federacao-p06.md", "docs/sdd/plan/plan.md", "specs/001-squid-mvp/contracts/activitypub.md", "docs/sdd/tasks/TASK-0047-facet-dois-silos.md"]
outputArtifacts: ["backend/src/Squid.Protocols/**", "backend/src/Squid.Api/Media/**", "backend/src/Squid.Api/Posts/**", "backend/tests/Squid.Tests/**"]
interviewRounds: 2/6
feasibilityRounds: 0/2
correctionAttempts: 0/2
qaAttempts: 1/2
contextHandoffs: 0/4
progressDelta: 0
humanGate:
  approvedBy: NENHUM
  approvedDate: NENHUM
  scope: NENHUM
  expiresAt: NENHUM
blocker: NENHUM
nextAction: DONE - QA-0053 PASSED (5/5 ACs, mutacao /media/, gate 585/585, commits ad883dd + 654b851); merge em develop pelo ODEV e redispatch da TASK-0047 (feasibility 2/2)
evidence: []
memoryBankFlags: []
```

## Intent

Correção da **D-01 da TASK-0047** adjudicada pelo ODEV no nível especificação (2026-11-14
12:47:22, GAP-51): o contrato §1 publica `GET /media/{hash}` (blob content-addressed, cache
longo) e o §7.1 exige "mídia" na entrega — mas (1) o `Create` de saída sai **sem** `attachment`
(`ObjectJsonLd.NoteDocument`) e (2) `RemoteMediaClient` busca `/api/media/{contentHash}`, que
responde `401` sem sessão. Sem esta entrega, a metade de mídia da AC-01 da facet e a interop real
(TASK-0049) não passam.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: o `Create` federado de um post **com anexos** serializa o campo `attachment` (array AS2:
  url de blob por hash + tipo de mídia), e um leitor tolerante (formato da TASK-0044) decodifica
  de volta — round-trip saída→entrada — teste. Sem anexos, o documento permanece o de hoje
  byte-idêntico — teste.
- AC-02: nova rota **`GET /media/{hash}`**: serve o blob **sem sessão** (anonimamente), com
  `Cache-Control` longo e `ETag`, confere o SHA-256 do conteúdo e **não** expõe metadados; hash
  inexistente → `404` — teste.
- AC-03: `GET /api/media/{contentHash}` **não muda**: continua exigindo sessão e a regra GAP-25
  (regressão verde) — teste.
- AC-04: `RemoteMediaClient` busca agora a rota do contrato (`/media/{hash}`) e baixa mídia de um
  par em loopback nomeado em `Federation:TrustedPeerHosts`; falha de download continua **fail-open**
  (GAP-44, o post não é rejeitado) — teste.
- AC-05: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1
  test` exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Protocols/**` — `ObjectJsonLd` serializa `attachment` no `Note` (AP por
  hash + tipo), sem mudar o documento sem anexos
- `backend/src/Squid.Api/Media/**` — rota nova `GET /media/{hash}` (blob anônimo, SHA-256, cache
  longo, ETag, 404 sem vazar); `/api/media/{contentHash}` intocado
- `backend/src/Squid.Api/Posts/**` — `RemoteMediaClient` aponta para a rota do contrato
- `backend/src/Squid.Service/**` — só se a fiação do client precisar
- `backend/tests/Squid.Tests/**` — testes das ACs
- produto **fora** dos `touches` desta task: qualquer lacuna levantada como dúvida, não corrigida aqui

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-51 | no | RESOLVED (SPEC) — adjudicação D-01 da TASK-0047: blob `GET /media/{hash}` anônimo *capability por hash* (o contrato §1 já publicava cache longo); consequência membersOnly-por-hash amarrada ao **S-40** (backlog); `/api/media/{contentHash}` com GAP-25 intacto |
| GAP-44 | no | falha de download remoto segue fail-open (post aceito, anexo ausente) — mesma regra da TASK-0044 |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-14 12:49:25 | odev | task created from SPEC-0011 (correcao da D-01 da TASK-0047, GAP-51, entrega 10 de 13 da P-06; blocks TASK-0047 parada; adjudicacao de especificacao de 12:47:22)
- 2026-11-14 12:51:03 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; runId R-20261113-01; adjudicacao D-01 da TASK-0047 (especificacao) ja escrita na SPEC-0011 secoes D e F + GAP-51; blocks TASK-0047 (parada, redispatch depois); autorizacao de commit gate PHASE 2026-11-11 20:42:28 (modelo C) incluida - nova branch task/TASK-0053-midia-federada-attachment-e-blob desde develop; logLines e evidenceRows sem relogio para o ODEV carimbar (sem pipe em comandos)
- 2026-11-14 13:38:36 | dev | feasibility 1/2: units aceitos (ObjectJsonLd Note/Create, MediaEndpoints, RemoteMediaClient ja recebe a url do payload); touches corrigidos - backend/src/Squid.Api/Posts nao exige codigo novo; TASK-0047 toca so backend/tests/Squid.Tests/Federation e esta parada blockedBy esta task, sem paralelismo
- 2026-11-14 13:38:36 | dev | D-01 resolvida por SPEC-0011 secao D + contrato secao 1: o Create publica GET /media/{hash} e o RemoteMediaClient baixa a url que o payload traz - reescrita de /api/media nao consta do contrato nem da spec e nao foi inventada
- 2026-11-14 13:38:37 | dev | baseline antes das alteracoes: docs/sdd/_tools/sdd-test.ps1 test exit 0 com 578/578 (log runs/test-runs/20261114-131013-test-gate.log)
- 2026-11-14 13:38:37 | dev | ObjectJsonLd: NoteDocument ganha o campo attachment (array AS2 com type mediaType url name sha256; blob sai como {origin}/media/{hash}, link mantem a propria url, sha256 so quando ha blob) e WhenWritingNull mantem o documento sem anexos byte-identico
- 2026-11-14 13:38:38 | dev | MediaEndpoints: nova rota GET /media/{hash} sem sessao - serve so quando o SHA-256 dos bytes fecha com a chave da rota, Cache-Control public max-age 31536000 immutable, ETag do hash, tipo de midia tirado dos bytes (nenhum metadado), 404 da familia da midia sem traceId; GET /api/media intocado
- 2026-11-14 13:38:38 | dev | 7 testes novos: OutboundAttachmentTests (AC-01 round-trip saida-entrada pelo leitor tolerante da TASK-0044, Note e Create byte-identicos sem anexos, payload do fan-out real com midia), MediaBlobRouteTests (AC-02 anonimo com cache e ETag e SHA-256 conferido e 404 unico, AC-03 par /api 401 x /media 200 para o mesmo hash), RemoteBlobFetchTests (AC-04 download real da rota do contrato com Federation:TrustedPeerHosts nomeando 127.0.0.1, recusa com lista vazia e fail-open)
- 2026-11-14 13:38:39 | dev | correcao no meio da execucao: MediaBlobRouteTests AC-03 leu o blob com uma sessao que nao era a do dono e o GAP-25 respondeu 404 correto - teste ajustado para ler como dono e reexecutado verde
- 2026-11-14 13:38:39 | dev | gate final AC-05: docs/sdd/_tools/sdd-test.ps1 test exit 0 com 585/585 (578 de base + 7 novos), log runs/test-runs/20261114-133451-test-gate.log; nenhum arquivo alterado fora dos touches
- 2026-11-14 13:38:40 | dev | commit unico ad883dd na branch task/TASK-0053-midia-federada-attachment-e-blob criada de develop 8c90cd4, so com os 5 arquivos da task; sem push, main intocado, os docs modificados do ODEV seguem fora do commit
- 2026-11-14 13:38:45 | odev | ODEV verificou antes de transicionar: gate 20261114-133451-test-gate.log lido no arquivo (exit: 0, Aprovado 585, Com falha 0, 8m6s) e regressao Media 20261114-132406-test-filter.log (exit: 0, 30/30 - GAP-25 intacto); commit ad883dd conferido via git show --stat (5 arquivos: 2 produto em Protocols e Media + 3 testes, todos nos touches corrigidos na feasibility - Posts/** nao precisou de codigo, registrado no log do DEV); D-01 do DEV fechada no nivel especificacao (SPEC-0011 D + contrato 1 mandam a url publicada, nenhuma reescrita inventada) - aceita; logLines (10) e evidenceRows (10) carimbados via sdd-state; step result DONE aceito
- 2026-11-14 13:38:48 | odev | transicao IN_DEVELOPMENT -> AWAITING_QA (attempt 1/2) - dispatch QA a seguir; ao DONE desta task, redispatch da facet TASK-0047 (feasibility 2/2) para acrescentar a assercao de midia da AC-01
- 2026-11-14 14:43:25 | qa | executou AC-01: filtro FullyQualifiedName~OutboundAttachment via sdd-test.ps1 exit 0 com 3 de 3 (round-trip saida-entrada pelo leitor tolerante da TASK-0044, Note e Create byte-identicos sem anexos, payload de fan-out real com midia), log docs/sdd/runs/test-runs/20261114-134527-test-filter.log
- 2026-11-14 14:43:26 | qa | executou AC-02 e AC-03: filtro FullyQualifiedName~MediaBlobRoute exit 0 com 3 de 3 (anonimo com Cache-Control longo e ETag do hash, corpo = blob puro, SHA-256 conferido com bytes trocados, 404 unico sem traceId; par /api/media 401 sem sessao x /media 200 para o mesmo hash), log docs/sdd/runs/test-runs/20261114-134604-test-filter.log
- 2026-11-14 14:43:26 | qa | executou AC-04: filtro FullyQualifiedName~RemoteBlobFetch exit 0 com 1 de 1 (RemoteMediaClient de producao baixa a rota do contrato de par loopback nomeado em Federation:TrustedPeerHosts, lista vazia recusa o mesmo alvo, 404 vira null fail-open GAP-44), log docs/sdd/runs/test-runs/20261114-134721-test-filter.log
- 2026-11-14 14:43:27 | qa | regressao GAP-25: filtro FullyQualifiedName~Squid.Tests.Media exit 0 com 30 de 30 - GET /api/media/{contentHash} nao mudou, log docs/sdd/runs/test-runs/20261114-134833-test-filter.log
- 2026-11-14 14:43:27 | qa | regressao fail-open: filtro FullyQualifiedName~Squid.Tests.Federation exit 0 com 64 de 64 incluindo InboxReceiveTests AC-07 (o meio-posto do GAP-44), log docs/sdd/runs/test-runs/20261114-134952-test-filter.log
- 2026-11-14 14:43:27 | qa | mutacao can-fail: ObjectJsonLd.cs:181 trocada de /media/ para /api/media/, reexecucao do filtro do AC-01 exit 1 com 2 de 3 falhando, restaurada e reexecutada exit 0 com 3 de 3 e git status comprovando backend byte-identico a ad883dd, logs docs/sdd/runs/test-runs/20261114-135117-test-filter.log e 20261114-135237-test-filter.log
- 2026-11-14 14:43:28 | qa | gate AC-05: sdd-test.ps1 test exit 0 com 585 de 585 em 7m41s, log docs/sdd/runs/test-runs/20261114-140038-test-gate.log
- 2026-11-14 14:43:29 | qa | escopo AC-05: git show --stat ad883dd exit 0 com 5 arquivos (2 produto em Protocols e Media + 3 testes) todos nos touches; correcao da feasibility julgada correta - Posts/** nao precisou de codigo porque o download usa spec.Url do payload em RemoteActivities.cs:200 e RemoteActivities.cs:220 so grava a url local da linha depois do download; git status -- backend vazio
- 2026-11-14 14:43:29 | qa | gemeo Linux executado em contenedor local sdk:10.0 via podman: sdd-test.sh build exit 0 e sdd-test.sh test exit 1 com 50 falhas de ambiente classificadas (47 libfontconfig.so.1 ausente na imagem, 13 BuildProof spawnando cmd.exe), nenhuma pelo diff desta task - suite Linux registrada como unverified, logs docs/sdd/runs/test-runs/20261114-141516-test-linux.log e 20261114-142204-test-linux-full.log
- 2026-11-14 14:43:30 | qa | QA doc escrita e commitada como 654b851 so com docs/sdd/qa/QA-0053-midia-federada.md, task file intocado
- 2026-11-14 14:43:39 | odev | ODEV verificou antes de transicionar: QA-0053-midia-federada.md lido (5/5 ACs PASS, Nenhum finding de produto, Nenhum BLOCKER; OBSERVACAO: o doc nao traz o cabecalho - verdict: PASSED do padrao - aceito pelo step result completo + tabela de ACs, formato da QA registrado); gate 20261114-140038-test-gate.log exit: 0 / Aprovado 585 / Com falha 0 / 7m41s (lido no arquivo); mutacao /media/ lida no log (exit 1, 2/3); commit 654b851 conferido (1 arquivo docs/sdd/qa); git status -- backend = 0; logLines (10) e evidenceRows (15) carimbados via sdd-state - veredito aceito
- 2026-11-14 14:43:40 | odev | flags da QA: [GAP: memory bank absent] REJEITADA pelo ODEV - Test-Path docs/memory-bank/index.md = True (a rota de leitura da QA falhou, nao o arquivo); [MUDANCA_RELEVANTE rota /media capability-por-hash] ACEITA para a consolidacao; finding novo S-44 (BuildProbe spawn cmd.exe = DependencyRuleTests Windows-only) registrado no backlog; a suite Linux foi EXECUTADA em contenedor sdk:10.0 (primeira vez) - 50 falhas 100% ambiente (47 fontconfig, 13 cmd.exe), unverified mantido
- 2026-11-14 14:43:40 | odev | transicao AWAITING_QA -> DONE; entrega 10 de 13 da P-06 encerrada - merge em develop + docs commitados pelo ODEV, redispatch da TASK-0047 (feasibility 2/2) a seguir

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-14 13:38:41 | dev | docs/sdd/_tools/sdd-test.ps1 test (baseline) | 0 | docs/sdd/runs/test-runs/20261114-131013-test-gate.log (578/578) |
| 2026-11-14 13:38:41 | dev | docs/sdd/_tools/sdd-test.ps1 build | 0 | compilacao dos 2 arquivos de produto + 3 de teste |
| 2026-11-14 13:38:42 | dev | docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~OutboundAttachment | 0 | docs/sdd/runs/test-runs/20261114-132116-test-filter.log (3/3) |
| 2026-11-14 13:38:42 | dev | docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~MediaBlobRoute | 1 | docs/sdd/runs/test-runs/20261114-132141-test-filter.log (2/3, falha do proprio teste na AC-03 - dono errado - corrigido) |
| 2026-11-14 13:38:43 | dev | docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~MediaBlobRoute | 0 | docs/sdd/runs/test-runs/20261114-132254-test-filter.log (3/3) |
| 2026-11-14 13:38:43 | dev | docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~RemoteBlobFetch | 0 | docs/sdd/runs/test-runs/20261114-132322-test-filter.log (1/1) |
| 2026-11-14 13:38:44 | dev | docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Media | 0 | docs/sdd/runs/test-runs/20261114-132406-test-filter.log (30/30, regressao GAP-25 verde) |
| 2026-11-14 13:38:44 | dev | docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Federation | 0 | docs/sdd/runs/test-runs/20261114-132516-test-filter.log (64/64, inclui a prova pre-existente de fail-open InboxReceiveTests AC-07) |
| 2026-11-14 13:38:45 | dev | docs/sdd/_tools/sdd-test.ps1 test (gate AC-05) | 0 | docs/sdd/runs/test-runs/20261114-133451-test-gate.log (585/585) |
| 2026-11-14 13:38:45 | dev | git commit unico na branch task/TASK-0053 (mensagem TASK-0053) | 0 | git show ad883dd (5 arquivos, 488 insercoes) |
| 2026-11-14 14:43:30 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~OutboundAttachment | 0 | docs/sdd/runs/test-runs/20261114-134527-test-filter.log (3/3, AC-01) |
| 2026-11-14 14:43:31 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~MediaBlobRoute | 0 | docs/sdd/runs/test-runs/20261114-134604-test-filter.log (3/3, AC-02 e AC-03) |
| 2026-11-14 14:43:31 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~RemoteBlobFetch | 0 | docs/sdd/runs/test-runs/20261114-134721-test-filter.log (1/1, AC-04) |
| 2026-11-14 14:43:32 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Media | 0 | docs/sdd/runs/test-runs/20261114-134833-test-filter.log (30/30, regressao GAP-25) |
| 2026-11-14 14:43:32 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Federation | 0 | docs/sdd/runs/test-runs/20261114-134952-test-filter.log (64/64, inclui InboxReceiveTests AC-07) |
| 2026-11-14 14:43:33 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~OutboundAttachment com a mutacao ObjectJsonLd.cs:181 /media/ para /api/media/ | 1 | docs/sdd/runs/test-runs/20261114-135117-test-filter.log (2/3 falharam - prova de que o teste pode falhar) |
| 2026-11-14 14:43:33 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~OutboundAttachment depois de restaurar a mutacao | 0 | docs/sdd/runs/test-runs/20261114-135237-test-filter.log (3/3) |
| 2026-11-14 14:43:33 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261114-140038-test-gate.log (585/585, gate AC-05) |
| 2026-11-14 14:43:34 | qa | git show --stat ad883dd | 0 | 5 arquivos (MediaEndpoints.cs, ObjectJsonLd.cs e 3 arquivos de teste), todos nos touches |
| 2026-11-14 14:43:34 | qa | git status --porcelain -- backend | 0 | backend sem nenhuma alteracao - mutacao restaurada byte-identica a ad883dd, so o stamping do ODEV em docs fica modificado |
| 2026-11-14 14:43:35 | qa | podman run --rm -v C:/dev/squid-squad-net/squid-execution:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash -lc (sdd-test.sh build e depois test backend/Squid.slnx) | 1 | docs/sdd/runs/test-runs/20261114-141516-test-linux.log (build 0, teste 535/585 - ambiente, unverified) |
| 2026-11-14 14:43:36 | qa | podman run --rm ... bash -lc (dotnet test com filtro FullyQualifiedName~MediaParserTests) | 1 | causa raiz DllNotFoundException libfontconfig.so.1 em teste pre-existente (2/8) |
| 2026-11-14 14:43:36 | qa | podman run --rm ... bash -lc (ldd do libSkiaSharp.so linux-x64) | 0 | libfontconfig.so.1 => not found (unica dependencia ausente) |
| 2026-11-14 14:43:37 | qa | podman run --rm ... bash -lc (dotnet test backend/Squid.slnx --no-build -c Debug) | 1 | docs/sdd/runs/test-runs/20261114-142204-test-linux-full.log (50 falhas classificadas: 28 Skia fixture, 9 upload/quota via IMediaProcessor, 13 cmd.exe no BuildProof) |
| 2026-11-14 14:43:37 | qa | git add docs/sdd/qa/QA-0053-midia-federada.md e git commit com a mensagem TASK-0053 QA-0053 PASSED | 0 | commit 654b851 so com o artefato da QA, carimbo do ODEV ficou fora |
