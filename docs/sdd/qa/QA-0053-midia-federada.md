# QA-0053 — Mídia federada: `attachment` na saída + `GET /media/{hash}`

- task: `docs/sdd/tasks/TASK-0053-midia-federada-attachment-e-blob.md` (status `AWAITING_QA`, qaAttempt 1/2)
- runId: `R-20261113-01` · branch `task/TASK-0053-midia-federada-attachment-e-blob` (commit `ad883dd` sobre develop `8c90cd4`, um commit só)
- critérios testados: a lista `## Acceptance criteria` do arquivo da task, como está (SPEC-0011 §D "Mídia" + §F "Anexos na saída" + GAP-51; contrato `specs/001-squid-mvp/contracts/activitypub.md` §1 linha 23 e §7.1 linha 204)
- política: ponytail-qa `full` · memory bank: `[GAP: memory bank absent]` (`docs/memory-bank/index.md` ausente)
- harness: gate do projeto `docs/sdd/_tools/sdd-test.ps1` (Windows) + gêmeo `docs/sdd/_tools/sdd-test.sh` (Linux, contêiner local `mcr.microsoft.com/dotnet/sdk:10.0` via podman)

## AC → cenário

| AC | Cenário executado | Resultado |
| --- | --- | --- |
| AC-01 | `ObjectJsonLd.Create` de post com anexos publica `attachment` AS2 (url `{origin}/media/{hash}`, `mediaType`, `sha256`, `name`; link externo mantém a própria url; `/api/media/` nunca sai na wire) e o leitor tolerante da TASK-0044 (`ActivityJsonLd.ReceiveAsync`) decodifica de volta — round-trip saída→entrada; sem anexos, `Note` e `Create` byte-idênticos aos literais pré-task; fan-out real de publicação com upload enfileira o mesmo payload e o redecodifica | PASS — `sdd-test.ps1 test -TestFilter FullyQualifiedName~OutboundAttachment` exit 0, 3/3 (log `20261114-134527-test-filter.log`) |
| AC-02 | `GET /media/{hash}` sem sessão (host nunca logado) serve exatamente o blob (SHA-256 do corpo confere), `Cache-Control: public, max-age=31536000, immutable`, `ETag` = `"hash"`, tipo tirado dos bytes, corpo cru JPEG (não um registro JSON); hash inexistente → 404 `not_found` sem `traceId`; bytes que não respondem à chave da rota → o mesmo 404 | PASS — `... -TestFilter FullyQualifiedName~MediaBlobRoute` exit 0, 3/3 (log `20261114-134604-test-filter.log`) |
| AC-03 | Para o MESMO hash: `GET /api/media/{contentHash}` continua 401 sem sessão (GAP-25) e 200 com sessão do dono, enquanto `/media/{hash}` serve anônimo; o diff do commit não toca `GetAsync`/`GetThumbnailAsync`/escada GAP-25; regressão inteira da família `Squid.Tests.Media` verde | PASS — filtro `MediaBlobRoute` (parte AC-03) exit 0 + `... -TestFilter FullyQualifiedName~Squid.Tests.Media` exit 0, 30/30 (log `20261114-134833-test-filter.log`) |
| AC-04 | O `RemoteMediaClient` de PRODUÇÃO baixa `{origin}/media/{hash}` de um par em loopback (`http://127.0.0.1:0`, fixture real) nomeado em `Federation:TrustedPeerHosts`; a mesma allowlist vazia recusa o mesmo alvo antes do socket (GAP-49); hash sem blob (404) → `null`, nunca exceção (fail-open GAP-44); o meio-posto "post não é rejeitado" continua provado pelo pré-existente `InboxReceiveTests` AC-07 (roda na suite Federation) | PASS — `... -TestFilter FullyQualifiedName~RemoteBlobFetch` exit 0, 1/1 (log `20261114-134721-test-filter.log`) + `... -TestFilter FullyQualifiedName~Squid.Tests.Federation` exit 0, 64/64 (log `20261114-134952-test-filter.log`) |
| AC-05 | Gate do projeto inteiro (inclui `Category=BuildProof`) exit 0; escopo: `git show --stat ad883dd` = 5 arquivos, todos nos `touches`; árvore de trabalho só com o stamping do ODEV (docs) fora do commit | PASS — `sdd-test.ps1 test` exit 0, 585/585 em 7m41s (log `20261114-140038-test-gate.log`) + `git show --stat ad883dd` exit 0 |

## Execuções (comando → exit → cauda)

Todas executadas nesta sessão, cwd `C:\dev\squid-squad-net\squid-execution`.

1. `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~OutboundAttachment` → exit 0 → `Aprovado! – Com falha: 0, Aprovado: 3, Total: 3` (AC-01)
2. `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~MediaBlobRoute` → exit 0 → `Aprovado! – Com falha: 0, Aprovado: 3, Total: 3` (AC-02/AC-03)
3. `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~RemoteBlobFetch` → exit 0 → `Aprovado! – Com falha: 0, Aprovado: 1, Total: 1` (AC-04)
4. `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Media` → exit 0 → `Aprovado: 30, Total: 30` (regressão GAP-25)
5. `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Federation` → exit 0 → `Aprovado: 64, Total: 64` (regressão fail-open, inclui `InboxReceiveTests` AC-07)
6. mutação (prova de que o teste pode falhar) → mesma do item 1 com `backend/src/Squid.Protocols/ActivityPub/ObjectJsonLd.cs:181` trocado de `/media/` para `/api/media/` → exit 1 → `Com falha: 2, Aprovado: 1, Total: 3` (log `20261114-135117-test-filter.log`); restaurado e reexecutado → exit 0, 3/3 (log `20261114-135237-test-filter.log`); `git status` depois da restauração só com o stamping do ODEV
7. `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` → exit 0 → `Aprovado: 585, Com falha: 0, Total: 585, Duração: 7 m 41 s` (gate AC-05, log `20261114-140038-test-gate.log`)
8. `git show --stat ad883dd` → exit 0 → 5 arquivos: `MediaEndpoints.cs`, `ObjectJsonLd.cs` + os 3 arquivos de teste (488 inserções, 6 remoções)
9. gêmeo Linux: `podman run --rm -v C:/dev/squid-squad-net/squid-execution:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash -lc "bash docs/sdd/_tools/sdd-test.sh build backend/Squid.slnx && bash docs/sdd/_tools/sdd-test.sh test backend/Squid.slnx"` → build exit 0, teste exit 1 → `Failed: 50, Passed: 535, Total: 585` (log `20261114-141516-test-linux.log`) — causa raiz isolada nos itens 10 e 11
10. prova da causa raiz: `podman run ... bash -lc "dotnet test backend/Squid.slnx --no-build -c Debug --filter FullyQualifiedName~MediaParserTests"` → exit 1 → `System.DllNotFoundException : Unable to load shared library 'libSkiaSharp' ... libfontconfig.so.1: cannot open shared object file` em teste pré-existente (`MediaParserTests.BlurHash_*`, 2/8 falharam)
11. prova do dependente faltante: `podman run ... bash -lc "ldd .../runtimes/linux-x64/native/libSkiaSharp.so"` → `libfontconfig.so.1 => not found` (única dependência ausente; o resto resolve)
12. enumeração completa das 50 falhas: `podman run ... bash -lc "dotnet test backend/Squid.slnx --no-build -c Debug"` → exit 1 → `Failed: 50, Passed: 535, Total: 585`, saída inteira gravada em `docs/sdd/runs/test-runs/20261114-142204-test-linux-full.log` e classificada: 28× `TypeInitializationException SkiaSharp.SKImageInfo` (a raiz `DllNotFoundException ... libfontconfig.so.1`; inclui 5 dos 7 testes novos desta task, todos que passam por upload — `MediaHost.cs:120` monta o JPEG de fixture com `new SKBitmap`) + 9× `Assert.Equal` em testes de upload/quota/thread que cruzam o `IMediaProcessor` do upload (`MediaEndpoints.cs:52` → `SquidServiceHost.cs:154` → `SkiaMediaProcessor`) + 13× `Win32Exception ... process 'cmd.exe'` nos `DependencyRuleTests` (Category=BuildProof, `Architecture/BuildProbe.cs:123` spawna `cmd.exe`, Windows-only). Nenhuma das 50 passa pelo diff desta task.

## Prova de que os testes podem falhar

A mutação do item 6 quebrou o COMPORTAMENTO (url do blob virou `/api/media/`, o defeito pré-task) e os testes de AC-01 ficaram vermelhos de verdade (2 falhas, exit 1) antes de eu restaurar. O teste sem anexos continuou verde na mutação — esperado: a mutação só afetava o caminho com blob. Restaurado, 3/3 verdes; o gate final (item 7) roda sobre a árvore restaurada.

## Touches corrigidos na feasibility do DEV — julgamento

- **`backend/src/Squid.Api/Posts/**` sem código novo — correto.** `RemoteMediaClient.FetchAsync` recebe a URL inteira do payload (`backend/src/Squid.Api/Posts/RemoteMediaClient.cs:49`) e quem a passa é o pipeline inbound: `backend/src/Squid.Service/InboxPipeline.cs:311` monta o `RemoteAttachmentSpec` direto do `attachment` decodificado e `backend/src/Squid.Api/Posts/RemoteActivities.cs:200` chama `_fetch.FetchAsync(spec.Url, ...)`. Não existe construção de `/api/media/{hash}` em nenhum ponto do download; `RemoteActivities.cs:220` grava a URL `/api/media/{hash}` só DEPOIS do download, como referência local da linha (UI local, GAP-25). Logo a rota baixada é exatamente a que o par publica — e a saída (Protocols) agora publica `/media/{hash}`. O diff do commit confirma: nenhum arquivo em `Posts/**`.
- **`GET /api/media/{contentHash}` não mudou — confirmado.** O diff de `backend/src/Squid.Api/Media/MediaEndpoints.cs` em `ad883dd` só acrescenta `app.MapGet("/media/{hash}", GetBlobAsync)`, o método `GetBlobAsync` e um `using`; `MapGet("/api/media/{contentHash}", GetAsync)`, `GetAsync`, `GetThumbnailAsync` e a escada GAP-25 aparecem intactos como contexto. Regressão executada: `Squid.Tests.Media` 30/30 verde (o cenário AC-03 do teste novo ainda leu como dono para não cair no 404 correto do GAP-25).

## Findings

1. `docs/sdd/_tools/sdd-test.sh:64` e `:84` — baixo — lacuna JÁ conhecida **S-12** do backlog (`docs/sdd/ideas/BACKLOG.md:47`), reproduzida aqui: o gêmeo Linux descarta a saída do `dotnet test` (`tail -6` — os nomes dos 50 testes que falharam não ficam no log; contornei rodando `dotnet test` direto, execução 12) e classifica a recusa de ambiente (falta de `libfontconfig.so.1` na imagem) como `test: exit 1 (real test failure)` onde o `ps1` teria dito exit 3. Com isso um ambiente Linux sem fontconfig sempre parece um build vermelho.
2. `docs/sdd/_tools/sdd-test.sh:21` — baixo — lacuna JÁ conhecida **S-11** (`BACKLOG.md:46`), reproduzida aqui: `find_sln` procura só `*.sln`, mas o repositório só tem `backend/Squid.slnx`; sem argumento o gêmeo aborta com `no *.sln found within depth 3`. Tive que passar o alvo explicitamente (o `ps1` aceita `.slnx`).
3. `backend/tests/Squid.Tests/Architecture/BuildProbe.cs:123` — baixo — a prova `Category=BuildProof` spawna `cmd.exe`: as 13 teorias de `DependencyRuleTests` falham com `Win32Exception` em qualquer Linux, então a prova de arquitetura de dependências é Windows-only hoje (pré-existente, nada a ver com esta task; o gêmeo `.sh` não tem alternativa de shell).

Nenhum finding de produto. Nenhum BLOCKER.

## O que não ficou coberto (unverified)

- **Suíte Linux (gêmeo `.sh`) — `unverified`, nunca assumida verde.** Executada: build exit 0 e os 585 testes correram com 50 falhas, todas classificadas na execução 12 — 47 por `libfontconfig.so.1` ausente na imagem local `mcr.microsoft.com/dotnet/sdk:10.0` (o pacote `SkiaSharp.NativeAssets.Linux` está referenciado desde a TASK-0028 e instalado; falta o pacote do SO — prova nos itens 10 e 11) e 13 por `DependencyRuleTests` spawnar `cmd.exe` (Windows-only). Instalar `libfontconfig1` no contêiner exige gate de aprovação, indisponível nesta sessão; e mesmo com a lib, as 13 de BuildProof continuariam vermelhas no Linux. Por isso a suíte Linux fica `unverified` — não é verde e não é atribuída ao produto. Dos 7 testes novos desta task, 5 falham no Linux só pelo mesmo motivo de ambiente (passam por upload/`SKBitmap`) e 2 passam; no Windows, gate da AC-05, os 585 estão verdes. Risco: um defeito real de lógica só no Linux ficaria escondido atrás desse ruído.
- **Interop real Mastodon/Friendica (contrato §7 itens 11–13)** — fora do escopo desta task (entrega de facet/humano, TASK-0047+); não é AC aqui.

## Reproduzir

1. Pré-requisito: Windows com .NET 10 SDK, checkout da branch `task/TASK-0053-midia-federada-attachment-e-blob` (`ad883dd`).
2. Comando: `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`
3. Esperado: exit 0, `Aprovado: 585, Com falha: 0` (log novo em `docs/sdd/runs/test-runs/`); filtro pontual: mesmo comando com `-TestFilter FullyQualifiedName~OutboundAttachment` → `Aprovado: 3`.
