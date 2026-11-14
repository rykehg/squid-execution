<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: ATIVO
- owner: qa
- lastVerified: 2026-11-14

# QA-0047 — Facet de dois silos (A→B, contrato §7)

- task: `docs/sdd/tasks/TASK-0047-facet-dois-silos.md` (status `AWAITING_QA`, qaAttempt 1/2)
- spec: `docs/sdd/specs/SPEC-0011-federacao-p06.md` §H + `specs/001-squid-mvp/contracts/activitypub.md` §7 itens 1–9, 10 (AC-06), 14, 15
- runId: `R-20261113-01` · branch `task/TASK-0047-facet-dois-silos` (commits `f0e3e96` + merge `7462612` + `b953771`)
- verdict: PASSED
- política: ponytail-qa `full` · memory bank: rota `qa-integracao` lida (`build-run-test.md` §Local tests without gate, `integrations.md`, `contracts.md`)
- harness: gate do projeto `docs/sdd/_tools/sdd-test.ps1` (Windows) + gêmeo `docs/sdd/_tools/sdd-test.sh` (Linux, contêiner local `mcr.microsoft.com/dotnet/sdk:10.0` via podman, imagem já presente — nenhuma instalação)
- facet tests-only: nenhum arquivo de produto alterado; lacuna de produto vira finding, não patch

## AC → cenário

| AC | Cenário executado | Resultado |
| --- | --- | --- |
| AC-01 | `Ac01_public_post_of_a_lands_on_b_with_the_author_date_content_and_media` — cadeia A→B completa em HTTP real: upload real de JPEG na A (`MediaHarness.JpegWithExif`), publish com `contentHash`, fan-out enfileirado (`Pending`) e enviado pelo `PeerDeliveryPump` de produção (`Sent`), post projetado em B com `author` = ator de A; `docA` e `docB` byte-a-byte em `attributedTo`/`published`/`content`; `attachment` nas duas renderizações com `sha256` = hash do upload e `url` = `/media/{hash}` da própria origem; blob baixado da A sem sessão (SHA-256 confere) e a B servindo a cópia que o inbound verificou (volume próprio, não redirecionamento) | PASS (Windows) · Linux: unverified (ambiente — ver "O que não ficou coberto") |
| AC-02 | `Ac02_members_only_never_reaches_a_non_member_follower_of_b` (§7.2): post `membersOnly` com bob (seguidor não membro) → **zero** linhas de `PeerDelivery` em A, pump roda, feed de B não tem o post · `Ac02_private_generates_no_delivery_at_all` (§7.3): `private` → zero entrega, pump roda, feed de B não tem o post | PASS |
| AC-03 | `Ac03_duplicate_delivery_bad_signature_and_divergent_digest_leave_b_intact` (§7.4+§7.5): mesma `activity.id` reenviada → `202` e log/feed/outbox de B byte-idênticos (zero reprocessamento); keyId de alice com chave impostora → `401`; corpo enviado ≠ corpo assinado → `401` com `digest` no corpo — estado de B intacto após cada um | PASS |
| AC-04 | `Ac04_with_b_down_the_publish_succeeds_and_the_row_stays_pending_with_backoff` (§7.6): par próprio parado com `StopAsync`, publicação responde `200`, pump enfileira → linha `Pending`, `Attempts=1`, `Reason=send_failed`, `NextAttemptAt` no futuro (backoff) · `Ac04_policy_changed_between_enqueue_and_send_cancels_and_b_receives_nothing` (§7.7): `private` enfileirado → troca para `membersOnly` antes do envio → `Cancelled` com reason de política, nada chega em B | PASS |
| AC-05 | `Ac05_oversized_and_too_deep_payloads_are_refused_and_b_keeps_serving` (§7.8): 5 MB assinado → `413`, aninhamento 100 → `400`, log de B intacto, `/health` e webfinger seguem `200` · `Ac05_webfinger_of_an_unknown_handle_is_404_without_revealing_similar_actors` (§7.9): handle conhecido `200`; handle colado no conhecido → `404` sem o nome conhecido, sem o próprio e sem `subject` no corpo | PASS |
| AC-06 | `Ac06_every_send_carries_the_peer_domain_trace_attribute_without_pii` (§7.10/SC-015): `ActivityListener` no `FederationTrace.SourceName`; `ChainAsync` (Follow B→A) + pump do Create (A→B) → ≥2 spans `peer.send`, todos com `peer.domain` = `127.0.0.1` (sem porta, sem `users`, sem handles) e entrega `Sent` | PASS |
| AC-07 | `Ac07_a_repeated_like_from_the_same_actor_keeps_the_count_at_one` (§7.14): Like B→A com id novo, mesmo (ator, objeto) → `202` e `likeCount` segue 1 · `Ac07_federated_reply_preserves_inreplyto_and_a_private_conversation_sends_nothing` (§7.15/R-11): resposta federada chega a B com `replyToId` = id do pai byte-a-byte; conversa `private` → zero entrega e feed de B sem o post | PASS |
| AC-08 | Gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` exit 0 + escopo: `git show --stat` de `f0e3e96` (2 arquivos) e `b953771` (1 arquivo) só em `backend/tests/Squid.Tests/Federation/**`, merge `7462612` com pais `f0e3e96`+`9a701db` (develop), `git diff develop...HEAD` = só os 2 arquivos dos touches, árvore de trabalho só com o stamping do ODEV (plan.md, SPEC-0011, task file — não commitado por QA) | PASS — gate final exit 0, 597/597 (log `20261114-161501-test-gate.log`) + escopo conferido |

Premissa do harness (autoverificação): `Two_silos_live_in_one_process_with_their_own_origins_and_databases` — duas origens, dois arquivos SQLite distintos, dois blob roots distintos, conta criada em B não existe em A.

## Scripts

| Script | Language / runner | What it does |
| --- | --- | --- |
| `backend/tests/Squid.Tests/Federation/TwoSiloFacetTests.cs` | xUnit via `sdd-test.ps1` | Os 12 cenários da facet (escopo dos touches, commit `f0e3e96`+`b953771`) |
| `backend/tests/Squid.Tests/Federation/TwoSiloHarness.cs` | xUnit via `sdd-test.ps1` | Dois `SquidServiceHost.Build` em processo, portas Orleans livres, bases/blobs distintos, pump dirigido a mão |
| `docs/sdd/_tools/sdd-test.ps1` / `.sh` | pwsh / bash | Runner do projeto (gate da AC-08 e gêmeo Linux) |

Nenhum script novo foi necessário: a facet já é o harness; QA executou o runner existente e a mutação can-fail temporária (restaurada byte-a-byte, `git diff` vazio após cada camada).

## Prova de que cada AC pode falhar (ciclos can-fail)

Mutações temporárias só em EXPECTATIVA do teste (nunca em produto), em três camadas sobre o mesmo arquivo, cada camada seguida de execução filtrada e restauração byte-a-byte (`git diff --stat` vazio contra HEAD após cada camada):

| Camada | Flip (linha de `TwoSiloFacetTests.cs`) | Execução | Resultado |
| --- | --- | --- | --- |
| M1 (12 flips, um por teste) | self-check L36 `NotEqual`→`Equal`; AC-01 mídia L86; AC-02 §7.2 L116 `Undefined`→`Object`; AC-02 §7.3 L130 idem; AC-03 §7.5 assinatura L164 `Unauthorized`→`OK`; AC-04 §7.6 L192 `Pending`→`Sent`; AC-04 §7.7 L218 `Cancelled`→`Sent`; AC-05 §7.8 L241 `413`→`OK`; AC-05 §7.9 L267 `404`→`OK`; AC-06 L296 `127.0.0.1`→`wrong.example`; AC-07 §7.14 L327 contagem `1`→`2`; AC-07 §7.15 L359 `replyToId`→`parentId+"-errado"` | `sdd-test.ps1 test -TestFilter FullyQualifiedName~TwoSilo` → log `20261114-153259-test-filter.log` | exit 1 (cabeçalho do log), **12 falhas / 0 aprovadas**, cada falha na linha exata do flip com o valor real no lado `Actual` (401, Pending, Cancelled, 413, 404, `127.0.0.1`, contagem 1, hash real do blob, origens distintas) |
| M2 (segunda cláusula das duas testes multi-cláusula) | AC-01 data L77 `published` de A → `+"X"`; AC-03 §7.4 dedupe L154 `logAfterFirst` → `+1` | mesmo filtro → log `20261114-153550-test-filter.log` | exit 1, **2 falhas / 10 aprovadas**: AC-01 na L77 (`…Z` real igual nos dois lados) e AC-03 na L154 (`Expected: 4, Actual: 3` — o log de B não cresceu no reenvio, dedupe real) |
| M3 (cláusulas restantes) | AC-01 autor L70 `users/qa-wrong`; AC-03 §7.5 Digest divergente L171 `Unauthorized`→`OK`; AC-07 conversa privada L367 `Undefined`→`Object` | mesmo filtro → log `20261114-154710-test-filter.log` | exit 1, **3 falhas / 9 aprovadas**, nas linhas 70, 171 e 367 com os valores reais (`users/f01c1efb31d`, `Unauthorized`, `Undefined`) |

Após a restauração final: `git diff --stat -- backend/tests/` vazio e `git status --porcelain` só com os 3 docs do ODEV. A restauração byte-exact é a razão de o gate final (abaixo) rodar por último sobre a árvore final (armadilha S-23 do memory bank: o mtime restaurado força o rebuild incremental sobre a fonte restaurada).

## Execuções (comando → exit → cauda)

Todas executadas nesta sessão, cwd `C:\dev\squid-squad-net\squid-execution`. Classificação de ambiente/produto sempre pelo cabeçalho `exit:` e do resumo do log gravado (armadilha S-22).

1. `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build` → exit 0 → `build: exit 0` (compilação pós-mutação reexibiu os warnings CS0618/CS0675 pré-existentes de develop; 0 erros)
2. `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~TwoSilo` (baseline) → exit 0 → `Aprovado! – Com falha: 0, Aprovado: 12, Total: 12, Duração: 22 s` (log `20261114-152808-test-filter.log`, cabeçalho `exit: 0`)
3. mesmo comando, camada M1 → exit 1 → `Com falha: 12, Aprovado: 0, Total: 12` (log `20261114-153259-test-filter.log`, cabeçalho `exit: 1`; o wrapper imprimiu a assinatura de sandbox errada — S-22 conhecido — o log decide: vermelho real esperado da mutação)
4. mesmo comando, camada M2 → exit 1 → `Com falha: 2, Aprovado: 10, Total: 12` (log `20261114-153550-test-filter.log`, cabeçalho `exit: 1`)
5. `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` (gate intermediário pós-restauração M1+M2) → exit 0 → `Aprovado: 597, Com falha: 0, Total: 597, Duração: 8 m 31 s` (log `20261114-154525-test-gate.log`)
6. mesmo comando, camada M3 → exit 1 → `Com falha: 3, Aprovado: 9, Total: 12` (log `20261114-154710-test-filter.log`, cabeçalho `exit: 1`)
7. gêmeo Linux `podman run --rm -v C:/dev/squid-squad-net/squid-execution:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash -lc "bash docs/sdd/_tools/sdd-test.sh build backend/Squid.slnx"` → exit 0 → `build: exit 0` (mesmos warnings pré-existentes, em inglês)
8. gêmeo Linux, facet com nomes visíveis (contorna S-12): `podman run … bash -lc "dotnet test backend/Squid.slnx -c Debug -m:1 --filter FullyQualifiedName~TwoSilo"` → exit 1 → `Failed: 1, Passed: 11, Total: 12` — a única falha é a AC-01 no fixture `SKBitmap` (`MediaHost.cs:122` via `TwoSiloFacetTests.cs:52`) por `libfontconfig.so.1` ausente na imagem, a MESMA causa de ambiente classificada na QA-0053 (instalar exigiria gate, indisponível); os outros 11 testes da facet passam no Linux
9. gêmeo Linux, gate `podman run … bash -lc "bash docs/sdd/_tools/sdd-test.sh test backend/Squid.slnx"` → exit 1 → `Failed: 51, Passed: 546, Total: 597, Duração: 5 m 42 s` (S-12 descarta os nomes; enumeração no item 9a)
9a. enumeração com nomes (mesma imagem, `--no-build`): `podman run … bash -lc "dotnet test backend/Squid.slnx --no-build -c Debug > docs/sdd/runs/test-runs/20261114-qa0047-linux-full.log 2>&1"` → exit 1 → `Failed: 51, Passed: 546, Total: 597` (log `docs/sdd/runs/test-runs/20261114-qa0047-linux-full.log`) — classificação: **13** `DependencyRuleTests` (`Win32Exception` spawnando `cmd.exe`, S-44, Windows-only) + **29** com raiz `libfontconfig.so.1: cannot open shared object file` (28 pré-existentes da QA-0053 + a AC-01 desta facet em `MediaHost.cs:122`) + **9** asserts de upload/quota que cruzam o `IMediaProcessor` de Skia (mesma classe da QA-0053, `Expected: OK / Actual: InternalServerError` no caminho do upload) = 51 de ambiente, **0 de produto** (38 falhas sem teoria + 13 teorias = 51; o diff desta task é tests-only e não toca nenhuma dessas classes)
10. `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` (gate final da AC-08 sobre a árvore restaurada byte-a-byte) → exit 0 → `Aprovado! – Com falha: 0, Aprovado: 597, Total: 597, Duração: 8 m 34 s` (log `20261114-161501-test-gate.log`, cabeçalho `exit: 0`)
11. `git status --porcelain` + `git diff develop...HEAD --stat` + `git show --stat f0e3e96 b953771 7462612` → exit 0 → escopo conferido (ver seção abaixo)
## Escopo (AC-08, segunda cláusula)

- `git status --porcelain` (antes e depois das mutações): apenas `docs/sdd/plan/plan.md`, `docs/sdd/specs/SPEC-0011-federacao-p06.md` e `docs/sdd/tasks/TASK-0047-facet-dois-silos.md` — stamping do ODEV, fora do commit e não tocado por QA.
- `git show --stat f0e3e96` → 2 arquivos: `TwoSiloFacetTests.cs` + `TwoSiloHarness.cs`, ambos em `backend/tests/Squid.Tests/Federation/**`.
- `git show --stat b953771` → 1 arquivo: `TwoSiloFacetTests.cs` (33 inserções, 3 remoções).
- `git show -s --format='%h %p %s' 7462612` → merge com pais `f0e3e96` (facet) e `9a701db` (develop tip: 0050..0053 + backlog S-43/S-44) — a develop entrou por merge, não por trabalho da task.
- `git diff develop...HEAD --stat` → exatamente os 2 arquivos dos touches (769 inserções). Nenhum arquivo de produto alterado pela task.

## Riscos conhecidos do Log da task — análise contra as ACs

| Risco | Onde | Quebra alguma AC? | Evidência |
| --- | --- | --- | --- |
| `shortcut:` ator remoto × dono da keyId | `backend/src/Squid.Service/InboxPipeline.cs:159-161` | **Não.** O passo 7 só roda depois da verificação de assinatura/Digest (§3); os 401 da AC-03 nascem nos passos de verificação, provados em vermelho (M1 L164 `Actual: Unauthorized`, M3 L171 idem) e em verde. Nenhuma AC exige pareamento ator↔keyId para autor remoto — o §7.5 só exige assinatura inválida/Digest divergente → 401 + estado intacto | M1/M3 + baseline |
| atalho lexicográfico da fila | `backend/src/Squid.ReadModels/Federation/SqlitePeerQueue.cs:51-63` (`next_attempt_at <= @Due` em texto ISO-8601 "O") | **Não.** Os escritores (fila e `Due`) usam UTC na mesma forma de casa ("O"), em que a ordem lexicográfica = cronológica; a AC-04 exige `NextAttemptAt > UtcNow` e `Pending` com backoff — verde no baseline, vermelho real na mutação (`Actual: Pending`, M1 L192) | baseline + M1 |
| `Accept` do Follow descartado (`S-43`) | `backend/src/Squid.Service/HttpPeerSender.cs` + `OutboundPipeline` (parked `M` em `docs/sdd/ideas/BACKLOG.md:77`) | **Não.** O efeito é só a aresta `following` local de quem enviou o follow não virar `Established`; `grep Established` em `backend/tests` mostra a asserção só em `OutboundFanOutTests` (pré-existente da TASK-0045, verde no gate), nunca na facet. A `ChainAsync` da facet assera a coleção **followers** de A, criada pelo inbox de A ao receber o `Follow`, e toda a cadeia A→B (follow `Sent`, fan-out, entrega) roda verde em cada execução | grep + baseline + gate |

Nenhum dos três vira finding: nenhum quebra AC. (Se um deles quebrasse, seria finding para ODEV, não correção da QA — facet é tests-only.)

## Findings

| # | Severity | Where | What breaks | Evidence |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |

Nenhum finding de produto. Nenhum BLOCKER. Lacunas pré-existentes reproduzidas e já registradas (não são desta task): S-22 (o wrapper `ps1` chamou de "sandbox signature" o vermelho real da mutação M1 — o cabeçalho `exit:` do log gravado decidiu, regra do memory bank), S-11/S-12 do gêmeo `.sh` e o ambiente Linux `libfontconfig.so.1`/`cmd.exe` da QA-0053.

## O que não ficou coberto (unverified)

- **AC-01 no Linux — `unverified`, nunca assumida verde.** O cenário morre antes das asserções: `MediaHarness.JpegWithExif` monta o JPEG com `new SKBitmap` (`backend/tests/Squid.Tests/Media/MediaHost.cs:122`) e a imagem local `mcr.microsoft.com/dotnet/sdk:10.0` não tem `libfontconfig.so.1` (raiz provada na QA-0053 com `ldd`; instalar o pacote exigiria gate de aprovação, indisponível nesta sessão). O mesmo motivo derrubaria o upload da A (`SkiaMediaProcessor`) mesmo com fixture pré-asseada. Risco: um defeito real da cadeia de mídia só no Linux ficaria escondido atrás desse ruído de ambiente — a cadeia está verde no Windows (M1 provou que as asserções dela podem falhar).
- **Suíte Linux completa — classificada, não verde:** 51 falhas de ambiente, nenhuma atribuída ao produto: 13 `DependencyRuleTests` (`cmd.exe`, S-44) + 29 `libfontconfig.so.1` (28 pré-existentes da QA-0053 + a AC-01 desta facet) + 9 asserts de upload/quota dependentes do processador de Skia (log `20261114-qa0047-linux-full.log`, enumeração do item 9a). O gêmeo `.sh` também descarta nomes (S-11 exigiu passar o `.slnx` na mão; S-12 exigiu a enumeração separada).
- **Interop real Mastodon/Friendica (contrato §7 itens 11–13)** — fora do escopo desta task (TASK-0049, gate humano); não é AC aqui.

## Reproduzir

1. Pré-requisito: Windows com .NET 10 SDK, checkout da branch `task/TASK-0047-facet-dois-silos` (`b953771`).
2. Comando: `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~TwoSilo` → esperado: exit 0, `Aprovado: 12, Com falha: 0`.
3. Comando: `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` → esperado: exit 0, `Aprovado: 597, Com falha: 0` (log novo em `docs/sdd/runs/test-runs/`).
