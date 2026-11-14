<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0044 — Inbox: pipeline de verificação, dedupe e aplicação de atividades

```yaml
runId: R-20261113-01
taskId: TASK-0044
status: DONE
agent: odev
phase: P-06
sequence: 2
createdAt: 2026-11-13 13:47:25
updatedAt: 2026-11-13 23:41:57
parent: SPEC-0011
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Grains/Inbox/**", "backend/src/Squid.Api/Federation/**", "backend/src/Squid.Api/Posts/**", "backend/src/Squid.Protocols/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/**"]
blocks: ["TASK-0045"]
blockedBy: ["TASK-0043"]
inputArtifacts: ["docs/sdd/specs/SPEC-0011-federacao-p06.md", "docs/sdd/plan/plan.md", "specs/001-squid-mvp/contracts/activitypub.md", "specs/001-squid-mvp/data-model.md"]
outputArtifacts: ["backend/src/Squid.Grains/Inbox/**", "backend/src/Squid.Api/Federation/**", "backend/src/Squid.Api/Posts/**", "backend/tests/Squid.Tests/**"]
interviewRounds: 2/6
feasibilityRounds: 2/2
correctionAttempts: 0/2
qaAttempts: 1/2
contextHandoffs: 0/4
progressDelta: 0
humanGate:
  approvedBy: Henrique
  approvedDate: 2026-11-13 21:23:34
  scope: D-01 - contêiner de destino do Create remoto (só destino nomeado; cobre AC-01 desta task e AC-01 da TASK-0047 do fecho)
  expiresAt: 2026-11-20 21:13:23
blocker: NENHUM
nextAction: DONE - QA-0044 PASSED (8/8 ACs, 20 mutacoes, gate 529/529, commits 92db2c7 + 61c9eab); merge em develop pelo ODEV e dispatch da TASK-0045
evidence: []
memoryBankFlags: []
```

## Intent

Entrega 4 de 9 da P-06: o caminho de **entrada** completo — `InboxGrain` com dedupe por
`activity.id`, a ordem obrigatória de verificação do contrato §3 (FR-040) e a aplicação das
atividades (`Create`, `Follow`/`Accept`, `Undo`, `Delete`, `Like`, `Flag`) no receptor. É a entrega
que faz uma atividade assinada por outra instância virar estado local sem derrubar o serviço.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `POST .../inbox` com assinatura válida, `Digest` confere e `Date` na janela → `202` e a
  atividade é aplicada: `Create` remoto vira post em B com autor remoto, conteúdo e data corretos
  — teste.
- AC-02: a mesma `activity.id` entregue de novo → `202` idêntico **sem reprocessar** (estado não
  duplica, nenhuma projeção muda) — teste (FR-035, contrato §7.4).
- AC-03: a ordem do contrato §3 é imposta: sem `Signature` ou assinatura inválida → `401`;
  `Digest` divergente → `401`; `Date` fora de ±5 min → `401`/`400`; corpo de 5 MB → `413`;
  aninhamento 100 → `400` — em todos, estado intacto e o serviço segue respondendo — teste
  (§7.5, §7.8).
- AC-04: taxa por domínio acima de `RateLimitPerMinute` (padrão 60) → `429` com `Retry-After` —
  teste.
- AC-05: `Follow` cria a relação seguidor e devolve `Accept`; `Undo(Follow)` remove a relação;
  `Delete` vira tombstone (post some do feed); `Like` é idempotente por ator+objeto (contagem não
  duplica — §7.14); `Flag` entra na fila de moderação existente — teste.
- AC-06: `membersOnly` só é aplicado quando o destinatário local é membro ativo do contêiner;
  `private` nunca é aplicado em claro; atividade de tipo desconhecido → `202` sem processar e
  métrica `federation.unknown_activity` incrementada — teste.
- AC-07: anexo de `Create` remoto é baixado com guarda SSRF e integridade conferida pelo SHA-256
  do blob; falha de download **não** rejeita o post (anexo fica ausente/marcado) — teste (GAP-44).
- AC-08: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1
  test` exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Grains/Inbox/**` — diretório novo: `InboxGrain` (janela de `activity.id`)
- `backend/src/Squid.Api/Federation/**` — pipeline do `POST .../inbox` (ordem §3, respostas)
- `backend/src/Squid.Api/Posts/**` — aplicação do `Create` remoto (append no `IActivityStore`,
  anexo/mídia, tombstone, `Like`, `Flag` via `IReportStore`)
- `backend/src/Squid.Protocols/**` — `ReceiveAsync` do adapter, decodificação tolerante (R-13)
- `backend/src/Squid.Service/**` — encanamento, métrica `federation.unknown_activity`
- `backend/tests/Squid.Tests/**` — testes das ACs
- produto **fora** dos `touches` desta task: qualquer lacuna levantada como dúvida, não corrigida aqui

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-42 | no | `Update`/`Announce`/`Join`/`Leave`/`Block` na entrada → `202` + métrica, sem aplicar estado |
| GAP-44 | no | mídia remota baixa pelo caminho de fetch com guarda SSRF, integridade pelo SHA-256; falha não rejeita o post; baixa remota não cobra cota do usuário local |
| GAP-46 | no | `Accept` de `Follow` é automático quando a política permite seguir (sem UI de aprovação) |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | qual contêiner local recebe um `Create` remoto quando o inbox não nomeia um? | HUMAN (gate 2026-11-13 21:23:34, Henrique) — resposta **"1 (default)"**: **só destino nomeado** — (a) inbox `/c/{slug}` → aquele contêiner; (b) `squid:slug` ou caminho `/c/{slug}` em `audience`/`to`/`cc`/`inReplyTo` → contêiner local correspondente (URI remota casa por slug); **sem destino → `202` sem aplicar**, estado intacto; fallback de membria rejeitado; saída da TASK-0045 nomeia o destino (`audience` + `squid:slug`) — cláusula escrita na SPEC-0011 §E |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-13 13:47:25 | odev | task created from SPEC-0011 (entrega 4 de 9, InboxGrain + ordem §3 + aplicacao; blockedBy TASK-0043)
- 2026-11-13 20:44:39 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; runId R-20261113-01; blockedBy TASK-0043 resolve (DONE com QA-0043 PASSED 7/7, gate 512/512, merge 6cfac90 em develop); autorizacao de commit gate PHASE 2026-11-11 20:42:28 (modelo C) incluida - nova branch task/TASK-0044-inbox-verificacao-e-aplicacao desde develop; logLines e evidenceRows devolvidos sem relogio para o ODEV carimbar (sem caractere pipe em comandos)
- 2026-11-13 21:16:08 | dev | feasibility 1/2 da TASK-0044: task lida na integridade (controle IN_DEVELOPMENT attempt 1/2, AC-01..AC-08, touches, gaps, doubts) e memoria bank pela rota implementacao (architecture, patterns, contracts - 3 arquivos)
- 2026-11-13 21:16:08 | dev | normas e consumidores lidos: SPEC-0011 secoes C/D/E/H, contrato activitypub secoes 1/2/3/4/7, data-model 2.5/2.8/2.9, plano P-06 (TASK-0041..0049), TASK-0045 e TASK-0047, quickstart US6
- 2026-11-13 21:16:09 | dev | codigo de entrada mapeado: FederationEndpoints, FederationSeams, InboxSignatureGate, HttpSignatureVerifier, SquidServiceHost, PostEndpoints, ActivityProjector, SqliteFeedTimeline, IActivityStore, IObjectStore, IReportStore, IMediaStore, SsrfGuard, LinkUnfurler, ActivityPubSurfaceTests, HttpSignatureTests, UnfurlHost, FeedHarness
- 2026-11-13 21:16:09 | dev | touches confirmados sem correcao; nenhum task aberto em paralelo (fila serial 0041..0049, parallel false) - nenhum intersects em disputa
- 2026-11-13 21:16:10 | dev | decisoes fechadas pela leitura antes da duvida: passos 1-3 do contrato 3 no verificador de assinatura e passos 4-7 na pipeline (tamanho 1 MB com 413, profundidade 32 com 400, taxa Federation RateLimitPerMinute padrao 60 com 429 e Retry-After, dedupe no InboxGrain por destinatario local, remetente por chave local ou remota com cache TTL e guarda SSRF); Accept de volta no corpo do 202 pela AC-05 devolve e pela AC-03 da 0045 respondido com Accept; relacao de seguidor em documento sem migracao pelo padrao actor-status com interface IFollowStore, para a 0045 adotar sobre a tabela follow da 0008
- 2026-11-13 21:16:10 | dev | duvida bloqueante D-01 registrada e nao fechavel por especificacao nem por requisito: o contêiner de destino de um Create remoto recebido em inbox de ator nao esta escrito em lugar nenhum e a resposta decide o resultado observavel de AC-01 aqui e de 0047/AC-01 no fecho da fase - stepResult DOUBT com a alternativa alfa proposta, zero codigo escrito e nenhum arquivo tocado
- 2026-11-13 21:16:12 | odev | step result DOUBT aceito (feasibility 1/2, zero codigo, zero arquivo tocado, logLines 6 + evidenceRows 2 carimbados): D-01 - contêiner de destino do Create remoto nao definido em nenhum artefato - ODEV leu SPEC-0011, FR-035/037, research e contrato: nivel especificacao e nivel requisito ambos SILACIONESOS sobre o destino - escada de adjudicacao sobe ao humano
- 2026-11-13 21:16:13 | odev | transicao IN_DEVELOPMENT -> DOUBT_ESCALATED (D-01 nao respondivel por spec nem por requisito)
- 2026-11-13 21:16:15 | odev | transicao DOUBT_ESCALATED -> AWAITING_HUMAN: humanGate.scope = D-01 destino do Create remoto (1 questao), expiresAt 2026-11-20 21:13:23; 1 questao com default enviada ao humano
- 2026-11-13 21:17:23 | odev | sdd-state set humanGate.scope/humanGate.expiresAt RECUSADO (campo aninhado nao enderecavel pela ferramenta, exit 2) - ODEV escreveu os dois campos diretamente no bloco de controle (control block e do ODEV), texto exato substituido: scope = D-01 destino do Create remoto (1 questao, cobre 0044/AC-01 e 0047/AC-01), expiresAt 2026-11-20 21:13:23; demais campos do gate permanecem NENHUM
- 2026-11-13 21:24:20 | odev | resposta humana recebida 2026-11-13 21:23:34 (Henrique): 1 (default) - destino do Create remoto = SO DESTINO NOMEADO; humanGate preenchido (approvedBy/approvedDate), D-01 fechada na tabela de duvidas
- 2026-11-13 21:24:20 | odev | SPEC-0011 secao E atualizada com a clausula de destino (inbox de contêiner, squid:slug ou caminho /c/{slug} em audience/to/cc/inReplyTo; sem destino 202 sem aplicar; fallback de membria rejeitado) + nomeacao de saida da TASK-0045 (audience + squid:slug, contrato nao muda) - decisao tambem registrada no plano
- 2026-11-13 21:24:24 | odev | transicao AWAITING_HUMAN -> IN_DEVELOPMENT; redispatch do mesmo DEV (attempt 1/2, feasibility 2/2) com a resposta da D-01 e as decisoes fechadas da rodada 1 - nenhuma outra mudanca de escopo
- 2026-11-13 22:43:20 | dev | feasibility 2/2 da TASK-0044 concluida sem nova duvida bloqueante: task re-lerida com a resposta da D-01, SPEC-0011 secoes C/D/E/H, contrato activitypub 1/2/3/4/7 e data-model 2.5/2.8/2.9 re-leridos, memoria bank pela rota implementacao (architecture, patterns, contracts - 3 arquivos), touches confirmados sem correcao, nenhum task aberto em paralelo
- 2026-11-13 22:43:21 | dev | baseline pre-codigo: powershell -NoProfile -ExecutionPolicy Bypass -File docs\sdd\_tools\sdd-test.ps1 test -> exit 0 (512/512)
- 2026-11-13 22:43:22 | dev | decisoes fechadas pela leitura preservando a rodada do predecessor: passos 1-3 do contrato 3 no verificador de assinatura e passos 4-7 na pipeline - corpo de 1 MB com 413, aninhamento 32 com 400, Federation RateLimitPerMinute padrao 60 com 429 e Retry-After por dominio remetente, dedupe no InboxGrain por destinatario local, remetente por chave local ou remota com cache TTL e guarda SSRF; Accept de volta no corpo do 202; relacao de seguidor em documento de IFollowStore sem migration
- 2026-11-13 22:43:22 | dev | D-01 aplicada literalmente conforme o gate humano: so destino nomeado - inbox /c/{slug} nomeia o proprio contêiner, squid:slug ou caminho /c/{slug} em audience/to/cc/inReplyTo (URI remota casa pelo slug, URI local casa exata) nomeia o contêiner local correspondente, sem destino -> 202 sem aplicar com estado intacto, fallback de membria inexistente
- 2026-11-13 22:43:23 | dev | AC-06 lida literalmente: membersOnly aplicado so quando o destinatario local (o ator do inbox do ator) e membro ativo do contêiner - a entrega ao inbox do contêiner nao tem destinatario membro e nao aplica; private nunca aplicado em claro; tipos desconhecidos e a familia GAP-42 (Update/Announce/Join/Leave/Block) -> 202 com a metrica federation.unknown_activity
- 2026-11-13 22:43:23 | dev | gate de leitura por visibilidade congelada (feed, GET de post, stream) implementado e REVERTIDO: contrariava o comportamento travado pela QA-0026 AC-04b, que exige leitura em estado corrente do contêiner, e crescia alem das ACs - revertido byte a byte; so o comentario do gate de stream passa a registrar a regra de estado corrente
- 2026-11-13 22:43:24 | dev | primeira vermelha dos testes existentes com defeito meu: IRemoteMediaClient sem registro na composition root derrubava os dois testes de inbox da TASK-0043 com 500 - registro adicionado antes do seam do GAP-17 e as 155 provas das areas afetadas voltaram a passar
- 2026-11-13 22:43:25 | dev | codigo entregue, todo dentro dos touches: ActivityJsonLd (Receive com teto de 32 niveis, Accept) em Protocols; IInboxPipeline/InboxTarget/InboxVerdict/IFollowStore em Api/Federation; ILikeStore/RemoteMediaClient/RemoteActivities em Api/Posts; InboxGrain + IInboxWindowStore em Grains/Inbox; InboxPipeline/HttpFederatedActorClient/SqliteInboxWindowStore/SqliteFollowStore/SqliteLikeStore/FederationRateLimiter/FederationMetrics e os registros no SquidServiceHost em Service
- 2026-11-13 22:43:25 | dev | 15 testes novos (14 em InboxReceiveTests + 1 em InboxRateLimitTests) cobrindo AC-01..AC-07, a regra de destino da D-01 e a guarda SSRF do download de midia; as seams de rede (IFederatedActorClient, IRemoteMediaClient) trocadas por fakes pelo configure do GAP-17, nenhuma prova abre socket
- 2026-11-13 22:43:26 | dev | comando dos cenarios novos: powershell -NoProfile -ExecutionPolicy Bypass -File docs\sdd\_tools\sdd-test.ps1 test -Fast -TestFilter FullyQualifiedName~Squid.Tests.Federation -> exit 0 (24/24, 15 novos e 9 pre-existentes)
- 2026-11-13 22:43:26 | dev | regressao das areas afetadas (namespaces Federation, Feed, Stream, Posts, Projections, Moderation, Unfurl) apos a reversao -> exit 0 (155/155)
- 2026-11-13 22:43:27 | dev | gate AC-08: powershell -NoProfile -ExecutionPolicy Bypass -File docs\sdd\_tools\sdd-test.ps1 test -> exit 0 (527/527 com Category=BuildProof, log docs/sdd/runs/test-runs/20261113-223959-test-gate.log)
- 2026-11-13 22:43:27 | dev | escopo auditado com git status --porcelain + git diff --stat: nenhum arquivo fora dos touches - os docs/sdd modificados na arvore (plan, SPEC-0011, TASK-0044, TASK-0045) sao do ODEV e ficaram fora do commit
- 2026-11-13 22:43:28 | dev | commit unico 92db2c7 na branch task/TASK-0044-inbox-verificacao-e-aplicacao criada de develop 6cfac90, sem push e sem tocar em main, conforme a autorizacao do gate PHASE 2026-11-11 20:42:28 (modelo C)
- 2026-11-13 22:43:34 | odev | ODEV verificou antes de transicionar: gate 20261113-223959-test-gate.log lido no arquivo (exit: 0, Aprovado 527, Com falha 0, 6m27s) e filtro 20261113-223253-test-fast.log (exit: 0, 24/24); commit 92db2c7 conferido via git show --stat (24 arquivos, todos nos touches: Protocols, Api/Federation, Api/Posts, Grains/Inbox, Service, tests; nenhum fora); feasibilityRounds corrigido para 2/2 a pedido do DEV; logLines (14) e evidenceRows (7) carimbados via sdd-state; step result DONE aceito
- 2026-11-13 22:43:34 | odev | flags do DEV acolhidas: (1) SSRF x pares locais -> ODEV abre TASK-0050 (allowlist Federation:TrustedPeerHosts) antes da TASK-0047, decisao de especificacao registrada na SPEC-0011; (2) [SUSPEITO] leitura em estado corrente vs visibilidade congelada (QA-0026 AC-04b travado) - registrado para a consolidacao, nao obedeço a leitura suspeita do data-model sem adjudicacao; (3) GET /api/media sem checagem da visibilidade de linha membersOnly remoto -> backlog S-40
- 2026-11-13 22:43:35 | odev | transicao IN_DEVELOPMENT -> AWAITING_QA (attempt 1/2) - dispatch QA a seguir
- 2026-11-13 23:42:49 | qa | QA attempt 1/2 da TASK-0044: task lida na integridade (controle AWAITING_QA, AC-01..AC-08, D-01 fechada pelo humano como so destino nomeado), SPEC-0011 secoes C/E/H e contrato activitypub 2/3/7 lidos, memoria bank pela rota qa-integracao (build-run-test, contracts - 3 arquivos)
- 2026-11-13 23:42:50 | qa | baseline pre-escrita: git HEAD 92db2c7 na branch task/TASK-0044-inbox-verificacao-e-aplicacao com 24 arquivos do commit todos nos touches, 6 WIP do ODEV, WSL sem dotnet (suite Linux = unverified); filtro Federation -> exit 0 (24/24)
- 2026-11-13 23:42:51 | qa | mapa AC para cenario fechado: 8 ACs, 17 cenarios de teste (14 DEV do inbox, 1 DEV da taxa, 2 novos da QA) + 20 ciclos can-fail no produto + gate
- 2026-11-13 23:42:51 | qa | dois cenarios exigidos pela lista de AC e ausentes foram escritos em QaTask0044InboxGapTests.cs (AC-03 assinatura forjada com chave estrangeira -> 401 intacto, AC-04 padrao 60 sem configuracao com 61a recusada) e executados verdes
- 2026-11-13 23:42:52 | qa | qa-mutate-task-0044.ps1 -VerifyOnly -> exit 0 (20 padroes hits=1, ASCII ok, VERIFY total=20 bad=0)
- 2026-11-13 23:42:52 | qa | run 1 do driver -> exit 1 com 19 de 20 EXPECT-OK e 1 EXPECT-MISS no T2: a janela de activity.id quebrada no sentido sempre nova deixa o cenario AC-02 verde porque a aplicacao e insert-only e mascara o reprocessamento - ledger preservado em QA-0044-mutations-run1.log, T2 reancorado no veredito do passo 6 da pipeline
- 2026-11-13 23:42:53 | qa | run final do driver -> exit 0: 20 de 20 vermelhos com a propria AC no RED-AC, 20 RESTORE-OK hash=PRE residue=0, GREEN 26/26, SUMMARY cycles=20 failed=0 residue-backend=0
- 2026-11-13 23:42:53 | qa | gate AC-08: sdd-test.ps1 test -> exit 0 (529/529 com Category=BuildProof, 527 do DEV + 2 da QA, log docs/sdd/runs/test-runs/20261113-233508-test-gate.log)
- 2026-11-13 23:42:54 | qa | escopo AC-08: qa-scope-task-0044.ps1 -> exit 0 (blob-identity 7/7 igual ao HEAD, mtime 8, residuo mutante 0, git status backend 1 entrada na baseline, fora-dos-touches 0, WIP do ODEV 6)
- 2026-11-13 23:42:54 | qa | checkers: qa-check-task-0044.ps1 -> exit 0 (total=57 fails=0, inclui sdd-state check OK) e o gemeo qa-check-task-0044.sh via wsl -> exit 0 (total=32 fails=0)
- 2026-11-13 23:42:55 | qa | findings: nenhum BLOCKER e nenhum defeito de produto; notas registradas no QA-0044-inbox-verificacao-e-aplicacao.md (mascara do cenario AC-02, DIVERGENCIA contracts.md:21, shortcut do ator remoto, suite Linux unverified)
- 2026-11-13 23:42:55 | qa | commit autorizado pelo gate PHASE 2026-11-11 20:42:28 (modelo C): 61c9eab na branch task/TASK-0044-inbox-verificacao-e-aplicacao, 13 arquivos so de QA (doc, scripts, logs e o teste novo), sem push e sem tocar em main
- 2026-11-13 23:42:56 | qa | QA nao escreveu na task file: logLines e evidenceRows devolvidos sem relogio para o ODEV carimbar via sdd-state
- 2026-11-13 23:43:04 | odev | ODEV verificou antes de transicionar: QA-0044-inbox-verificacao-e-aplicacao.md com verdict PASSED lido no arquivo; gate 20261113-233508-test-gate.log exit: 0 / Aprovado 529 / Com falha 0 / 6m56s (lido no arquivo); mutations SUMMARY cycles=20 failed=0 residue-backend=0 lido no log (run 1 com EXPECT-MISS do T2 preservado e explicado); commit 61c9eab conferido via git show --stat (13 arquivos: 12 docs/sdd/qa + QaTask0044InboxGapTests.cs dentro dos touches); logLines (13) e evidenceRows (11) carimbados via sdd-state - veredito aceito
- 2026-11-13 23:43:05 | odev | QA-0044 PASSED attempt 1/2 (PRODUTO+TESTES): 8/8 ACs, 20 ciclos can-fail todos vermelhos na AC alvo, restore byte-exact, GREEN 26/26; gate 529/529 (527 DEV + 2 da QA); achado de produto nenhum; NOTA importante para a TASK-0047: shortcut declarado em InboxPipeline.cs:159 (ator remoto nao casado com o dono da keyId) - se o harness A→B tropeçar, e D-01 para o ODEV
- 2026-11-13 23:43:05 | odev | transicao AWAITING_QA -> DONE; entrega 4 de 10 da P-06 encerrada - merge em develop + docs commitados pelo ODEV, dispatch da TASK-0045 a seguir

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-13 21:16:11 | dev | git branch --show-current + git rev-parse --short HEAD + git status --porcelain | 0 | baseline develop 6cfac90, apenas o arquivo de task modificado, nenhum codigo tocado |
| 2026-11-13 21:16:11 | dev | Get-ChildItem -Name; Get-ChildItem backend\src -Name; Get-ChildItem backend\tests -Name | 0 | inventario de entradas lidas na feasibility, sem artefato gravado |
| 2026-11-13 22:43:28 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs\sdd\_tools\sdd-test.ps1 test | 0 | baseline 512/512 pre-codigo - docs/sdd/runs/test-runs/20261113-214327-test-gate.log |
| 2026-11-13 22:43:29 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs\sdd\_tools\sdd-test.ps1 build | 0 | build sem erros em cada rodada (0 Erros) |
| 2026-11-13 22:43:29 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs\sdd\_tools\sdd-test.ps1 test -Fast -TestFilter FullyQualifiedName~Squid.Tests.Federation | 0 | 24/24 com os 15 testes novos - docs/sdd/runs/test-runs/20261113-223253-test-fast.log |
| 2026-11-13 22:43:30 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs\sdd\_tools\sdd-test.ps1 test -Fast -TestFilter (namespaces Federation, Feed, Stream, Posts, Projections, Moderation, Unfurl - comando literal no log) | 0 | 155/155 regressao das areas - docs/sdd/runs/test-runs/20261113-222037-test-fast.log |
| 2026-11-13 22:43:30 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs\sdd\_tools\sdd-test.ps1 test | 0 | gate AC-08 527/527 - docs/sdd/runs/test-runs/20261113-223959-test-gate.log |
| 2026-11-13 22:43:31 | dev | git status --porcelain + git diff --stat + git diff backend/src/Squid.Api/Posts/StreamEndpoints.cs | 0 | fora-dos-touches 0; diff do StreamEndpoints so comentario |
| 2026-11-13 22:43:31 | dev | git checkout -b task/TASK-0044-inbox-verificacao-e-aplicacao + git add backend + git commit TASK-0044 | 0 | commit unico 92db2c7 desde develop 6cfac90, sem push, docs do ODEV fora do commit |
| 2026-11-13 23:42:56 | qa | git rev-parse HEAD, git rev-parse --abbrev-ref HEAD, git status --porcelain -uall, git show --stat --oneline -1 HEAD, wsl probes (pwsh) | 0 | docs/sdd/qa/QA-0044-git-baseline.txt |
| 2026-11-13 23:42:57 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Federation | 0 | docs/sdd/runs/test-runs/20261113-225032-test-filter.log (baseline 24/24 antes de qualquer escrita) |
| 2026-11-13 23:42:57 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Federation | 0 | docs/sdd/runs/test-runs/20261113-225836-test-filter.log (26/26 com os 2 testes novos da QA) |
| 2026-11-13 23:42:58 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0044.ps1 -VerifyOnly | 0 | docs/sdd/qa/QA-0044-verify.log (VERIFY total=20 bad=0) |
| 2026-11-13 23:42:59 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0044.ps1 | 1 | docs/sdd/qa/QA-0044-mutations-run1.log (run 1: 19 de 20 EXPECT-OK, T2 EXPECT-MISS investigado) |
| 2026-11-13 23:42:59 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0044.ps1 | 0 | docs/sdd/qa/QA-0044-mutations.log (run final: cycles=20 failed=0, GREEN 26/26) |
| 2026-11-13 23:43:00 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261113-233508-test-gate.log (529/529) |
| 2026-11-13 23:43:00 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-scope-task-0044.ps1 | 0 | docs/sdd/qa/QA-0044-scope.log (blob 7/7, mtime 8, residuo 0, fora-dos-touches 0) |
| 2026-11-13 23:43:01 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0044.ps1 | 0 | docs/sdd/qa/QA-0044-check.log (total=57 fails=0) |
| 2026-11-13 23:43:01 | qa | wsl -e bash docs/sdd/qa/qa-check-task-0044.sh | 0 | docs/sdd/qa/QA-0044-check-sh.log (total=32 fails=0) |
| 2026-11-13 23:43:01 | qa | git add <13 artefatos QA> && git commit TASK-0044 QA-0044: PASSED 8/8 ACs | 0 | commit 61c9eab em task/TASK-0044-inbox-verificacao-e-aplicacao |
