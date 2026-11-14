<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0047 — Facet de dois silos (A→B, contrato §7)

```yaml
runId: R-20261113-01
taskId: TASK-0047
status: DONE
agent: odev
phase: P-06
sequence: 2
createdAt: 2026-11-13 13:47:25
updatedAt: 2026-11-14 16:18:33
parent: SPEC-0011
authority: SPEC
parallel: false
touches: ["backend/tests/Squid.Tests/Federation/**"]
blocks: ["TASK-0048"]
blockedBy: ["TASK-0046"]
inputArtifacts: ["docs/sdd/specs/SPEC-0011-federacao-p06.md", "docs/sdd/plan/plan.md", "specs/001-squid-mvp/contracts/activitypub.md"]
outputArtifacts: ["backend/tests/Squid.Tests/Federation/**"]
interviewRounds: 2/6
feasibilityRounds: 1/2
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
nextAction: DONE - QA-0047 PASSED (8/8 ACs, 3 camadas de mutacao, gate 597/597, commits f0e3e96+b953771+89239d5); merge em develop pelo ODEV e dispatch da TASK-0048 (runbook/manual)
evidence: []
memoryBankFlags: []
```

## Intent

Entrega 7 de 9 da P-06: harness de integração com **dois silos** A→B em fluxo HTTP completo,
reusando as rotas das tasks 0041..0046, cobrindo os itens 1–9, 14 e 15 do contrato §7 (DoD do
Sprint 5 em cadeia, sem pares externos — os pares reais são da TASK-0049). É facet **tests-only**:
lacuna de produto levantada aqui vira `D-01` para o ODEV, não correção silenciosa.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: post **público** de A aparece em B com autor, mídia e data corretos (§7.1/SC-007) —
  teste.
- AC-02: post `membersOnly` **não** chega a seguidor não membro de B (§7.2/SC-005) e `private`
  não gera **nenhuma** entrega (§7.3) — teste.
- AC-03: entrega duplicada da mesma `activity.id` não duplica estado (§7.4) e assinatura inválida
  ou `Digest` divergente → `401` com estado do receptor intacto (§7.5) — teste.
- AC-04: B indisponível: A publica normalmente e a entrega fica `Pending` com backoff (§7.6);
  política alterada entre enfileiramento e envio → `Cancelled`, nada enviado (§7.7) — teste.
- AC-05: payload de 5 MB → `413`, aninhamento 100 → `400`, serviço de B continua respondendo
  (§7.8); `webfinger` de handle inexistente → `404` sem revelar atores parecidos (§7.9) — teste.
- AC-06: atributo de trace `peer.domain` presente em 100% das entregas e sem PII (§7.10/
  SC-015) — teste.
- AC-07: `Like` duplicado do mesmo ator não duplica contagem (§7.14) e resposta federada preserva
  `inReplyTo` enquanto conversa privada não gera entrega (§7.15, R-11) — teste.
- AC-08: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1
  test` exit 0; nenhum arquivo alterado fora dos `touches`. Se a facet acusar lacuna de produto,
  é `D-01` para o ODEV — o DEV desta task só escreve teste.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/tests/Squid.Tests/Federation/**` — harness de dois silos (duas instâncias em processo,
  bases distintas) e os testes das ACs
- produto **fora** dos `touches` desta task: qualquer lacuna levantada como dúvida, não corrigida aqui

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| — | no | dois silos em processo no teste (sem Docker) — Docker/compose é caminho do runbook (TASK-0048) e da interop (TASK-0049), não da facet |
| GAP-47 | no | validação com pares reais é da TASK-0049 com gate humano; aqui só pares Squid |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | como o post de A chega a B "com mídia" (AC-01/§7.1) se o `Create` sai sem `attachment` e o fetch de blob responde 401? | SPEC — contrato §1 (`GET /media/{hash}` publicado) + §7.1 (mídia exigida) + §2.1 (formato mínimo não fecha a porta ao `attachment` AS2); emendas escritas na SPEC-0011 §D/§F | (1) o `Create` de saída serializa `attachment` (AS2, simétrico ao inbound que já aceita); (2) nova rota **`GET /media/{hash}`** anônima e content-addressed (cache longo + ETag, capability por hash — `/api/media/{contentHash}` com GAP-25 **intocado**); `RemoteMediaClient` passa a buscar a rota do contrato. Correção é produto **fora dos toques** da facet → **TASK-0053** (blocks 0047); a facet re-dispatcha com feasibility 2/2 acrescentando a asserção de mídia. Adjudicada pelo ODEV 2026-11-14 12:47:22, nível especificação (o contrato publica os dois pontos; consequência blob-por-hash amarrada ao S-40) |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-13 13:47:25 | odev | task created from SPEC-0011 (entrega 7 de 9, facet dois silos tests-only, contrato §7 itens 1-9/14/15; blockedBy TASK-0046)
- 2026-11-14 11:53:10 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; runId R-20261113-01; blockedBy TASK-0046 resolve (DONE com QA-0046 PASSED) e fila limpa por 0050/0051/0052 (todos DONE com QA PASSED, merges ate 8c90cd4); FACET TESTS-ONLY - lacuna de produto e D-01 para o ODEV, nunca correcao silenciosa; contexto util: Federation:TrustedPeerHosts deve nomear 127.0.0.1/localhost no harness (GAP-49), duplo embrulho ja existe (registro HTTP real funciona), PEM blindado corrigido, shortcut do InboxPipeline.cs:159 (ator remoto x dono da keyId) e o atalho lexicografico do SqlitePeerQueue sao os riscos conhecidos; autorizacao de commit gate PHASE 2026-11-11 20:42:28 (modelo C) incluida - nova branch task/TASK-0047-facet-dois-silos desde develop; logLines e evidenceRows sem relogio para o ODEV carimbar (sem pipe em comandos)
- 2026-11-14 12:48:34 | dev | dev | feasibility r1: ordem normativa lida (SPEC-0011 H, contrato activitypub secao 7, AC-01..AC-08, memoria-bank rota implementacao), baseline do gate pre-existente 578/578 exit 0, touches confirmado - so backend/tests/Squid.Tests/Federation/**, nenhum doubt bloqueante
- 2026-11-14 12:48:35 | dev | dev | branch task/TASK-0047-facet-dois-silos criada de develop (8c90cd4) sob a autorizacao de commit gate PHASE modelo C
- 2026-11-14 12:48:36 | dev | dev | TwoSiloHarness.cs escrito: dois SquidServiceHost.Build em processo com portas Orleans livres pela costura Configure EndpointOptions, Storage e BlobRoot distintos, Federation:TrustedPeerHosts=127.0.0.1,localhost, RecoveryKey propria por silo, pump dirigido a mao e assinatura reusa o ActorSigner do produto (duplo embrulho)
- 2026-11-14 12:48:36 | dev | dev | sdd-test.ps1 build exit 0 apos correcao da expressao de colecao dos argumentos
- 2026-11-14 12:48:37 | dev | dev | filtro FullyQualifiedName~TwoSilo r1: 10/12 - Ac03 404 e Ac07 401, as duas causas eram do proprio teste (destruturacao do tuple capturou o BobActor no lugar do handle; chave {bob} literal em raw string  deixou o actor invalido), nao do produto
- 2026-11-14 12:48:37 | dev | dev | filtro FullyQualifiedName~TwoSilo r2: 11/12 - Ac03 corrigida, restava o {bob} literal do Like
- 2026-11-14 12:48:38 | dev | dev | filtro FullyQualifiedName~TwoSilo r3: 12/12 exit 0 - cadeia A->B completa em HTTP real (follow, create, dedupe, 401 com estado intacto, backoff com B parado, Cancelled por politica, 413/400, webfinger 404, peer.domain, Like idempotente, inReplyTo)
- 2026-11-14 12:48:38 | dev | dev | probe temporario da midia: 2 vermelhos capturados - o Create federado sai sem campo attachment e o fetch anonimo de /api/media responde 401 - lacuna provada vira D-01, probe removido e filtro reexecutado verde
- 2026-11-14 12:48:39 | dev | dev | gate AC-08 sdd-test.ps1 test: exit 0, 590/590 (baseline 578 + 12 novos)
- 2026-11-14 12:48:39 | dev | dev | commit unico f0e3e96 na branch task/TASK-0047-facet-dois-silos com exatamente os 2 arquivos dos touches; sem push, main intocada, nenhuma branch apagada; task file e plan.md ficaram fora (modificacoes do ODEV)
- 2026-11-14 12:48:44 | odev | step result DOUBT aceito (feasibility 1/2; gate 590/590 lido no log 20261114-124545-test-gate.log, commit f0e3e96 = so 2 arquivos de teste conferido, probe vermelho 20261114-123512-test-filter.log lido; logLines 10 + evidenceRows 8 carimbados): facet 12/12 verdes menos a metade de midia da AC-01
- 2026-11-14 12:48:45 | odev | transicao IN_DEVELOPMENT -> DOUBT_ESCALATED (D-01 lacuna de produto provada com probe)
- 2026-11-14 12:48:46 | odev | D-01 ADJUDICADA no nivel especificacao (contrato secao 1 publica GET /media/{hash} anonimo com cache longo; secao 7.1 exige midia; secao 2.1 nao fecha a porta ao attachment AS2): emendas escritas na SPEC-0011 secao D (rota de blob) e secao F (attachment na saida); correcao = nova TASK-0053 (blocks 0047); blob anonimo por hash = capability (a rota do contrato ja publicava) com a consequencia membersOnly amarrada ao S-40; nenhuma emenda de contrato nem data-model
- 2026-11-14 12:48:48 | odev | transicao DOUBT_ESCALATED -> IN_DEVELOPMENT respondida pela especificacao; redispatch adiado ate a TASK-0053 DONE (o produto precisa existir antes da assercao de midia) - attempt segue 1/2
- 2026-11-14 14:47:27 | odev | redispatch DEV (feasibility 2/2 do mesmo attempt 1/2): TASK-0053 resolve a D-01 (QA-0053 PASSED 5/5, gate 585/585, merge e9b8d61 em develop + backlog 9a701db com S-43/S-44) - a facet agora soma a assercao de midia da AC-01 (attachment via /media/{hash} anonima) ao harness que ja esta verde 12/12 no commit f0e3e96; branch da facet mantida, commit novo na mesma branch
- 2026-11-14 15:15:25 | dev | feasibility r2: ordem normativa relida (task file, SPEC-0011 secoes D e F, contrato 7.1 no AC-01, memoria-bank rota implementacao com 2 arquivos) e baseline pre-existente do gate TwoSilo 12/12 exit 0 na arvore do merge 7462612
- 2026-11-14 15:15:26 | dev | D-01 nao reaparece: adjudicada em especificacao e produto na arvore (ObjectJsonLd serializa attachment com url {origin}/media/{hash} e a rota anonima GET /media/{hash} existe em MediaEndpoints) - nenhum doubt aberto, feasibility 2/2 fecha aqui
- 2026-11-14 15:15:26 | dev | toques confirmados: so backend/tests/Squid.Tests/Federation/**, nenhuma outra task aberta os intersecta (0048 e bloqueada por esta)
- 2026-11-14 15:15:27 | dev | AC-01 acrescentada de midia em TwoSiloFacetTests.cs: upload real de JPEG na A, publish com contentHashes, attachment conferido nas duas renderizacoes (docA e docB com a rota de contrato da propria origem), blob baixado de A sem sessao pela url que o documento declara com o SHA-256 conferindo, e B servindo a copia que o inbound verificou e gravou no proprio volume
- 2026-11-14 15:15:27 | dev | filtro FullyQualifiedName~TwoSilo depois da edicao: 12/12 exit 0 - os 12 antigos seguem verdes
- 2026-11-14 15:15:28 | dev | gate AC-08 sdd-test.ps1 test: exit 0 com 597/597 (585 do develop + 12 da facet)
- 2026-11-14 15:15:28 | dev | sdd-test.ps1 build: exit 0, 0 erros e 0 avisos
- 2026-11-14 15:15:29 | dev | commit unico b953771 na branch task/TASK-0047-facet-dois-silos com exatamente o 1 arquivo dos touches (33 insercoes, 3 remocoes); sem push, main intocada, nenhuma branch apagada; plan.md, SPEC-0011 e task file seguem modificados apenas pelo ODEV
- 2026-11-14 15:15:32 | odev | ODEV verificou antes de transicionar: gate 20261114-150905-test-gate.log lido no arquivo (exit: 0, Aprovado 597, Com falha 0, 8m11s) e filtro 20261114-150022-test-filter.log (exit: 0, 12/12 com a midia); commit b953771 conferido via git show --stat (1 arquivo TwoSiloFacetTests.cs dentro dos touches); logLines (8) e evidenceRows (6) carimbados via sdd-state; feasibility 2/2 fechada sem doubt, step result DONE aceito
- 2026-11-14 15:15:34 | odev | transicao IN_DEVELOPMENT -> AWAITING_QA (attempt 1/2, feasibility 2/2) - dispatch QA a seguir
- 2026-11-14 16:21:03 | qa | QA-0047 attempt 1/2 entrada: task AWAITING_QA lida, SPEC-0011 secao H e contrato secao 7 itens 1-9/10/14/15 relidos, memoria-bank rota qa-integracao (3 arquivos) lida, AC-01..AC-08 mapeadas nos 12 testes da facet
- 2026-11-14 16:21:04 | qa | gate de ambiente sdd-test.ps1 build exit 0 com 0 erros (warnings CS0618/CS0675 pre-existentes de develop, reexibidos so na compilacao incremental pos-mutacao)
- 2026-11-14 16:21:04 | qa | baseline do facet FullyQualifiedName~TwoSilo exit 0 com 12/12 em 22s, log 20261114-152808-test-filter.log com cabecalho exit 0
- 2026-11-14 16:21:05 | qa | can-fail M1: 12 flips de expectativa (um por teste, so no arquivo de teste) exit 1 com 12 falhas e 0 aprovadas, cada falha na linha exata do flip com o valor real no lado Actual (401, Pending, Cancelled, 413, 404, 127.0.0.1, contagem 1, hash real do blob, origens distintas); log 20261114-153259-test-filter.log com cabecalho exit 1; o wrapper imprimiu a assinatura de sandbox errada (S-22 conhecido) e o cabecalho do log decide: vermelho real esperado da mutacao
- 2026-11-14 16:21:05 | qa | restauracao M1 byte-exact (git diff --stat vazio contra HEAD) e can-fail M2 (data da AC-01 linha 77, dedupe da AC-03 linha 154) exit 1 com 2 falhas e 10 aprovadas; Expected 4 Actual 3 prova que o reenvio da activity.id nao reprocessa; log 20261114-153550-test-filter.log
- 2026-11-14 16:21:06 | qa | gate intermediario sdd-test.ps1 test exit 0 com 597/597 em 8m31s sobre a arvore restaurada, log 20261114-154525-test-gate.log
- 2026-11-14 16:21:06 | qa | can-fail M3 (autor da AC-01 linha 70, Digest divergente da AC-03 linha 171, feed privado da AC-07 linha 367) exit 1 com 3 falhas e 9 aprovadas nas linhas exatas; restauracao final byte-exact e git status so com os 3 docs do ODEV; log 20261114-154710-test-filter.log
- 2026-11-14 16:21:07 | qa | gemeo Linux por podman com a imagem local mcr.microsoft.com/dotnet/sdk:10.0 (nenhuma instalacao): sdd-test.sh build exit 0; facet filtrado exit 1 com 11 de 12 - a unica vermelha e a AC-01 no fixture SKBitmap de MediaHost.cs:122 chamado por TwoSiloFacetTests.cs:52 por libfontconfig.so.1 ausente na imagem, a mesma causa de ambiente da QA-0053; AC-01 no Linux fica unverified e os outros 11 testes passam
- 2026-11-14 16:21:07 | qa | gemeo Linux gate sdd-test.sh test backend/Squid.slnx exit 1 com 546 de 597 e enumeracao separada exit 1 com 51 falhas no log docs/sdd/runs/test-runs/20261114-qa0047-linux-full.log: 13 DependencyRuleTests por cmd.exe (S-44) + 29 com raiz libfontconfig.so.1 (28 pre-existentes da QA-0053 + a AC-01 desta facet) + 9 asserts de upload/quota que cruzam o IMediaProcessor de Skia = 51 de ambiente e 0 de produto
- 2026-11-14 16:21:08 | qa | gate final da AC-08 sdd-test.ps1 test exit 0 com 597/597 em 8m34s sobre a arvore restaurada, log 20261114-161501-test-gate.log com cabecalho exit 0
- 2026-11-14 16:21:08 | qa | escopo verificado: f0e3e96 = 2 arquivos e b953771 = 1 arquivo so em backend/tests/Squid.Tests/Federation/**; merge 7462612 com pais f0e3e96 e 9a701db (develop tip 0050..0053); git diff develop...HEAD = so os 2 toques; arvore de trabalho so com plan.md, SPEC-0011 e task file do ODEV, nao commitados por QA
- 2026-11-14 16:21:09 | qa | riscos do Log avaliados contra as ACs: InboxPipeline.cs:159 (ator remoto x dono da keyId) nao quebra porque os 401 da AC-03 nascem na verificacao dos passos 2-3 antes do passo 7; SqlitePeerQueue.cs:53 (comparacao lexicografica em texto O) nao quebra porque todos os escritores usam UTC na mesma forma e a AC-04 prova Pending com backoff; S-43 (Accept descartado no HttpPeerSender) nao quebra porque Established so e asserido em OutboundFanOutTests pre-existente e a facet usa a colecao followers de A - nenhum vira finding
- 2026-11-14 16:21:09 | qa | QA doc docs/sdd/qa/QA-0047-facet-dois-silos.md escrita e commitada uma unica vez como 89239d5 na branch; nenhum finding de produto e nenhum BLOCKER; flag MUDANCA_RELEVANTE CONTRATO proposta para o ODEV
- 2026-11-14 16:21:17 | odev | ODEV verificou antes de transicionar: QA-0047-facet-dois-silos.md com verdict PASSED lido no arquivo; gate final 20261114-161501-test-gate.log exit: 0 / Aprovado 597 / Com falha 0 / 8m34s (lido no arquivo); as 3 camadas de mutacao (M1 12, M2 2, M3 3) lidas nos logs; commit 89239d5 conferido (1 arquivo docs/sdd/qa); git status -- backend = 0; logLines (13) e evidenceRows (12) carimbados via sdd-state - veredito aceito
- 2026-11-14 16:21:18 | odev | QA-0047 PASSED attempt 1/2 (FACET PRODUTO+TESTES): 8/8 ACs provadas em 3 camadas de can-fail contra o produto, restore byte-exact, gate 597/597; 0 findings de produto; os 3 riscos do Log avaliados e descartados com prova (nada vira finding); gêmeo Linux: facet 11/12 e gate 546/597 com 51 falhas 100% ambiente (13 cmd.exe S-44 + 29 fontconfig + 9 Skia), AC-01 Linux = unverified; flag MUDANCA_RELEVANTE contracts.md:21 (4a reincidente) registrada para a consolidacao
- 2026-11-14 16:21:18 | odev | transicao AWAITING_QA -> DONE; entrega 11 de 13 da P-06 encerrada - merge em develop + docs commitados pelo ODEV, dispatch da TASK-0048 (manual/runbook) a seguir

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-14 12:48:40 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261114-121848-test-gate.log (baseline 578/578) |
| 2026-11-14 12:48:40 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 0 | stdout (0 Erro(s), 2 avisos pre-existentes) |
| 2026-11-14 12:48:41 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~TwoSilo | 1 | docs/sdd/runs/test-runs/20261114-122922-test-filter.log (10/12, causas no teste) |
| 2026-11-14 12:48:42 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~TwoSilo | 1 | docs/sdd/runs/test-runs/20261114-123130-test-filter.log (11/12) |
| 2026-11-14 12:48:42 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~TwoSilo | 0 | docs/sdd/runs/test-runs/20261114-123359-test-filter.log (12/12) |
| 2026-11-14 12:48:43 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~media_probe | 1 | docs/sdd/runs/test-runs/20261114-123512-test-filter.log (2 vermelhos do probe da midia, D-01) |
| 2026-11-14 12:48:43 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261114-124545-test-gate.log (gate final 590/590, exit 0 no cabecalho do arquivo) |
| 2026-11-14 12:48:43 | dev | git status --porcelain + git show --stat HEAD | 0 | commit f0e3e96 = so TwoSiloFacetTests.cs e TwoSiloHarness.cs; arvore com apenas plan.md e task file do ODEV modificados |
| 2026-11-14 15:15:29 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~TwoSilo | 0 | docs/sdd/runs/test-runs/20261114-145519-test-filter.log (baseline 12/12 antes da edicao) |
| 2026-11-14 15:15:30 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~TwoSilo | 0 | docs/sdd/runs/test-runs/20261114-150022-test-filter.log (12/12 com a midia) |
| 2026-11-14 15:15:30 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261114-150905-test-gate.log (gate 597/597, exit 0 no cabecalho) |
| 2026-11-14 15:15:31 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 0 | stdout (0 Erro(s), 0 Aviso(s)) |
| 2026-11-14 15:15:31 | dev | git status --porcelain e git diff --stat | 0 | so TwoSiloFacetTests.cs nos toques; plan.md, SPEC-0011 e task file do ODEV |
| 2026-11-14 15:15:32 | dev | git add backend/tests/Squid.Tests/Federation/TwoSiloFacetTests.cs e git commit com a mensagem TASK-0047 da midia da AC-01 | 0 | commit b953771 na branch task/TASK-0047-facet-dois-silos, 1 file changed |
| 2026-11-14 16:21:10 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 0 | stdout (build exit 0, 0 erros, warnings pre-existentes CS0618/CS0675) |
| 2026-11-14 16:21:10 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~TwoSilo | 0 | docs/sdd/runs/test-runs/20261114-152808-test-filter.log (baseline 12 de 12, cabecalho exit 0) |
| 2026-11-14 16:21:10 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~TwoSilo | 1 | docs/sdd/runs/test-runs/20261114-153259-test-filter.log (mutacao M1: 12 falhas de 12, prova can-fail, cabecalho exit 1) |
| 2026-11-14 16:21:11 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~TwoSilo | 1 | docs/sdd/runs/test-runs/20261114-153550-test-filter.log (mutacao M2: 2 falhas e 10 aprovadas) |
| 2026-11-14 16:21:12 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261114-154525-test-gate.log (gate intermediario 597 de 597) |
| 2026-11-14 16:21:12 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~TwoSilo | 1 | docs/sdd/runs/test-runs/20261114-154710-test-filter.log (mutacao M3: 3 falhas e 9 aprovadas) |
| 2026-11-14 16:21:13 | qa | podman run --rm -v C:/dev/squid-squad-net/squid-execution:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash -lc (sdd-test.sh build backend/Squid.slnx) | 0 | stdout (gemeo Linux build exit 0) |
| 2026-11-14 16:21:13 | qa | podman run --rm ... bash -lc (dotnet test com filtro FullyQualifiedName~TwoSilo) | 1 | stdout (facet Linux 11 de 12, AC-01 ambiente libfontconfig) |
| 2026-11-14 16:21:14 | qa | podman run --rm ... bash -lc (sdd-test.sh test backend/Squid.slnx) | 1 | stdout (gate Linux 546 de 597, 51 falhas de ambiente) |
| 2026-11-14 16:21:14 | qa | podman run --rm ... bash -lc (dotnet test --no-build redirecionando para o log completo) | 1 | docs/sdd/runs/test-runs/20261114-qa0047-linux-full.log (51 falhas classificadas: 13 cmd.exe, 29 libfontconfig, 9 asserts de Skia) |
| 2026-11-14 16:21:15 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261114-161501-test-gate.log (gate final AC-08 597 de 597, cabecalho exit 0) |
| 2026-11-14 16:21:15 | qa | git status --porcelain e git diff develop...HEAD --stat e git show --stat f0e3e96 b953771 7462612 | 0 | stdout (escopo so nos toques, merge 7462612 com develop, arvore so com os 3 docs do ODEV) |
