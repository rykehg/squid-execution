<!--
templateVersion: 1.0.0
 -->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: RASCUNHO
- owner: qa
- lastVerified: (últimos artefatos: docs/sdd/runs/test-runs/20261113-225836-test-filter.log [GREEN 26/26], 20261113-233508-test-gate.log [GREEN 529/529], QA-0044-check.log [total=57 fails=0] e QA-0044-check-sh.log [total=32 fails=0])

# QA-0044 — Inbox: verificação §3, dedupe e aplicação de atividades (TASK-0044)

- runId: R-20261113-01 · attempt: **1/2** (bloco de controle `qaAttempts: 0/2` — contador é do ODEV, QA não escreve nele)
- QA: subagente qa-task + política ponytail-qa (full) — testes EXECUTADOS nesta sessão, não escritos e deixados
- leitura normativa: SPEC-0011 **§C (ordem de verificação), §E (inbox, dedupe, aplicação e a cláusula D-01 "só destino nomeado" gravada pelo gate humano 2026-11-13 21:23:34), §H** + AC-01..AC-08 do task file + contrato `contracts/activitypub.md` **§2, §3, §7.4/§7.5/§7.8/§7.14** + memória banc rota `qa-integracao` (`build-run-test.md#Local tests without gate`, `contracts.md`)
- commit sob verificação: `92db2c7` em `task/TASK-0044-inbox-verificacao-e-aplicacao` (**24 arquivos = 20 produto nos touches + 4 testes; NENHUM fora**); baseline DEV `527/527`
- entrega da QA dentro dos touches: `backend/tests/Squid.Tests/Federation/QaTask0044InboxGapTests.cs` (2 cenários que a lista de AC exige e os testes do DEV não alcançavam — ver nota 4 das findings: AC-03 "assinatura inválida" e AC-04 "padrão 60")
- natureza desta QA: **PRODUTO + TESTES** — 16 cenários (14 DEV do inbox + 1 DEV da taxa + 2 da QA) provados por **20 mutações can-fail contra o PRODUTO**, restauradas byte-exact, + gate completo (AC-08)
- verdict: **PASSED**

## AC → cenário (8 ACs; tudo executado nesta sessão)

| AC | Cenário | Execução (destaque) | Resultado |
| --- | --- | --- | --- |
| AC-01 | `POST .../inbox` assinado com `Digest` e `Date` válidos → `202` e o `Create` remoto vira post no contêiner NOMEADO com autor remoto, conteúdo sanitizado e `published` do par | Verde no baseline pré-escrita `20261113-225032-test-filter.log` (**exit 0, 24/24**) e no GREEN. Can-fail: **T1** (referência de destino via `audience` do objeto removida em `InboxPipeline.cs:352` → nada aplicado → **falha=1**, RED-AC `A_remote_create_with_valid_headers...` = o destino nomeado é obrigatório) | PASS |
| AC-02 | a mesma `activity.id` entregue de novo → `202` idêntico byte a byte, log sem evento novo, projeção sem mudar, outbox com 1 item | Can-fail: **T2** (veredito do passo 6 invertido na `InboxPipeline.cs:152` → a primeira entrega é recusada e a repetida aplica → **falha=11**, RED-AC inclui `The_same_activity_id_delivered_twice...`); a direção "janela sempre nova" é mascarada pela aplicação insert-only — **run1**, `QA-0044-mutations-run1.log` T2 `EXPECT-MISS`, vira nota 3 das findings | PASS |
| AC-03 | ordem do contrato §3 imposta: sem `Signature` → `401`; assinatura forjada → `401`; `Digest` divergente → `401`; `Date` ±5 min → `401`; 5 MB → `413`; aninhamento 100 → `400`; estado intacto e `/health` 200 | Can-fail nas QUATRO metades: **T3** (digest mapeado p/ 400 → **falha=1**), **T4** (teto de 1 MB virando 6 MB → 5 MB aplicado em vez de 413 → **falha=1**), **T5** (profundidade mapeada p/ 406 → **falha=1**), **T6** (assinatura inválida mapeada p/ 400 → **falha=3**: o cenário do DEV, o novo da QA e o da QA-0043 — a recusa é uma só no mapa de veredictos). Verde: as 5 recusas + estado intacto + `health` 200, e o cenário novo da QA (`A_signature_made_with_a_foreign_key...`) | PASS |
| AC-04 | taxa por domínio acima de `Federation:RateLimitPerMinute` → `429` com `Retry-After`, estado intocado; **padrão 60** sem configuração | Verde: host próprio a 2/min (3ª entrega → `429` + `Retry-After` ≥ 1, log +2) e o teste novo da QA a 60 com config vazia. Can-fail: **T7** (comparação da cota nunca dispara → **falha=2**, RED-AC inclui `Over_the_per_domain_quota...`), **T8** (`DefaultLimit` 60 → 600 → **falha=1**, RED-AC `The_default_quota_is_sixty_per_domain...`) | PASS |
| AC-05 | `Follow` cria relação e devolve `Accept`; `Accept` de saída assenta a relação; `Undo(Follow)` remove; `Delete` → tombstone (some do feed); `Like` idempotente por ator+objeto; `Flag` na fila `IReportStore` | Can-fail nas SEIS metades: **T9** relação não gravada, **T10** `Accept` fora do corpo do 202, **T11** `Undo` sem efeito, **T12** tombstone não escrito, **T13** like duplicando (contagem 2), **T14** `Flag` fora do `switch` → **6 vermelhos de falha=1**, cada um com o cenário da própria AC no RED-AC | PASS |
| AC-06 | `membersOnly` só com destinatário local membro ativo (não membro → `202` sem aplicar); `private` nunca em claro; tipo desconhecido → `202` + métrica `federation.unknown_activity` | Can-fail nas TRÊS metades: **T15** (gate `membersOnly` invertido → **falha=1**), **T16** (gate `private` removido → post visível → **falha=1**), **T17** (métrica não incrementada → contador 0 ≠ 3 → **falha=1**). Verde: membro aplica com `visibilityAtPublish`, estranho 404, `private` 404, Warp/Update/Announce → 202 com contador 3 | PASS |
| AC-07 | anexo baixado com guarda SSRF e integridade pelo SHA-256; falha de download não rejeita o post | Can-fail nas DUAS metades: **T18** (comparação do hash declarado trocada → anexo bom recusado, `Assert.Single` cai → **falha=1**), **T19** (literal bloqueado liberado no `RemoteMediaClient` → o handler de teste é chamado → **falha=1**, RED-AC `The_production_media_client_never_touches...`). Verde: 1 anexo mantido com `contentHash`/`alt`/`url` de mídia, 2 ausentes, entrega `202`, e as 4 URLs bloqueadas recusadas com 0 chamadas ao handler | PASS |
| AC-08 | gate `sdd-test.ps1 test` exit 0; nenhum arquivo alterado fora dos `touches` | Gate lido **dentro do log** `*-test-gate.log` (S-22): cabeçalho `exit: 0` + `Com falha: 0, Aprovado: 529, Total: 529` (527 do DEV + 2 da QA). Escopo (`QA-0044-scope.log`): blob-identity `git hash-object` = `git rev-parse HEAD:` em **7/7** arquivos mutados/restaurados; mtime desde a baseline = **exatamente os 8 esperados**; resíduo de tokens = 0; `git status -- backend` = **1** entrada (o teste novo, dentro dos touches); `fora-dos-touches=0` (WIP do ODEV da baseline contado à parte). Commit `92db2c7`: 24/24 dentro dos `touches` (checker `COMMIT-TOUCHES`) | PASS |

## Comandos executados (todos nesta sessão, com exit real; datas vêm dos próprios logs)

| # | comando | exit | artefato / saída |
| --- | --- | --- | --- |
| 1 | `git rev-parse HEAD` + `git rev-parse --abbrev-ref HEAD` + `git status --porcelain -uall` + `git show --stat --oneline -1 HEAD` + `git show --name-only` + sondas `wsl -e bash -c "command -v dotnet"` e `command -v git` (pwsh) | 0 | `docs/sdd/qa/QA-0044-git-baseline.txt` — HEAD `92db2c7`, branch `task/TASK-0044-inbox-verificacao-e-aplicacao`, 6 WIP do ODEV, **24 arquivos do commit todos nos touches**, **dotnet ausente no WSL** (git = `/usr/sbin/git`) |
| 2 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~Squid.Tests.Federation"` (baseline da QA, antes de qualquer escrita) | 0 | `20261113-225032-test-filter.log` — `exit: 0`, Com falha 0 / Aprovado 24 / Total 24 (20s) |
| 3 | mesmo comando #2 depois de `QaTask0044InboxGapTests.cs` entrar no filtro | 0 | `20261113-225836-test-filter.log` — `exit: 0`, Com falha 0 / Aprovado 26 / Total 26 (23s) — os 2 cenários da QA executados e verdes |
| 4 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0044.ps1 -VerifyOnly` (antes de cada run) | 0 | `docs/sdd/qa/QA-0044-verify.log` — 20 padrões `hits=1`, `non-ascii-bytes=0 ok=true`, `VERIFY total=20 bad=0` |
| 5 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0044.ps1` (run 1) | 1 | `docs/sdd/qa/QA-0044-mutations-run1.log` — 19/20 `EXPECT-OK` + **1 `EXPECT-MISS` (T2)**; ledger preservado como prova da investigação (nota 3) |
| 6 | mesmo comando #5 (run final, após reancorar T2) | 0 | `docs/sdd/qa/QA-0044-mutations.log` — 20 vermelhos com RED-AC da própria AC em cada um, 20× `RESTORE-OK hash=PRE residue=0`, `GREEN wrapper=0 logExit=0 aprovado=26 falha=0`, `UNTRACKED backend=1`, `SUMMARY cycles=20 failed=0 residue-backend=0` |
| 7 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` (gate AC-08, suite inteira com BuildProof) | 0 | `docs/sdd/runs/test-runs/20261113-233508-test-gate.log` — `exit: 0` lido **dentro do log** (S-22) + `Com falha: 0, Aprovado: 529, Ignorado: 0, Total: 529` (6m56s) |
| 8 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-scope-task-0044.ps1` (varredura de escopo) | 0 | `docs/sdd/qa/QA-0044-scope.log` — blob 7/7 `match=True`, `mtime-count=8 (esperado 8)`, `residue=0`, `git-backend-entries=1`, `fora-dos-touches=0`, WIP do ODEV = 6 |
| 9 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0044.ps1` | 0 | `docs/sdd/qa/QA-0044-check.log` — SUMMARY total=57 fails=0 (inclui `sdd-state check`: OK, log 32, evidence 9) |
| 10 | `wsl -e bash docs/sdd/qa/qa-check-task-0044.sh` (gêmeo Linux) | 0 | `docs/sdd/qa/QA-0044-check-sh.log` — SUMMARY total=32 fails=0 |

As linhas `## Log` e `## Evidence` destas execuções **não** foram gravadas pela QA: o dispatch devolve `logLines`/`evidenceRows` sem relógio para o ODEV carimbar (o relógio é dele). Esta tabela acima é a prova; a task file permanece intacta pela QA.

## Ciclos de mutação (can-fail; S-16 hash PRE = blob do commit, S-22 classificação no log, S-23 restore byte-exact + mtime)

Todos nos arquivos de PRODUTO que a behaviour lê (`Squid.Service/InboxPipeline.cs`, `Squid.Service/FederationRateLimiter.cs`, `Squid.Service/SqliteLikeStore.cs`, `Squid.Api/Federation/FederationEndpoints.cs`, `Squid.Api/Posts/RemoteActivities.cs`, `Squid.Api/Posts/RemoteMediaClient.cs`) — nenhum edit fora do escopo, e os 7 arquivos finais são byte-iguais ao `HEAD` (blob-identity 7/7; `git status -- backend -uno` = 0).

| Ciclo | Arquivo | O que prova (AC) | Vermelho classificado NO LOG |
| --- | --- | --- | --- |
| T1 | InboxPipeline.cs:352 | AC-01: o `Create` resolve o destino pelo `audience` nomeado do objeto (D-01) | `A_remote_create_with_valid_headers...` (`falha=1`) |
| T2 | InboxPipeline.cs:152 | AC-02: o veredito do passo 6 recusa só a REPETIÇÃO — a primeira entrega aplica | `The_same_activity_id_delivered_twice...` entre 11 (`falha=11`) |
| T3 | FederationEndpoints.cs:134 | AC-03: `Digest` divergente responde `401` (passo 2) | `The_contract_order_refuses_each_step...` (`falha=1`) |
| T4 | InboxPipeline.cs:35 | AC-03: teto de 1 MB — 5 MB responde `413` (passo 4) | `The_contract_order_refuses_each_step...` (`falha=1`) |
| T5 | FederationEndpoints.cs:127 | AC-03: aninhamento > 32 responde `400` (passo 4) | `The_contract_order_refuses_each_step...` (`falha=1`) |
| T6 | FederationEndpoints.cs:142 | AC-03: assinatura ausente/inválida responde `401` (passo 1) | `The_contract_order_refuses...` + `A_signature_made_with_a_foreign_key...` + `Both_inboxes_refuse...` (`falha=3`) |
| T7 | FederationRateLimiter.cs:57 | AC-04: a cota por domínio recusa com `429` (passo 5) | `Over_the_per_domain_quota...` + `The_default_quota_is_sixty...` (`falha=2`) |
| T8 | FederationRateLimiter.cs:22 | AC-04: o padrão é 60 | `The_default_quota_is_sixty_per_domain...` (`falha=1`) |
| T9 | InboxPipeline.cs:330 | AC-05: `Follow` grava a relação seguidor | `A_follow_creates_the_relation...` (`falha=1`) |
| T10 | InboxPipeline.cs:332 | AC-05: `Follow` devolve o `Accept` no corpo do 202 | `A_follow_creates_the_relation...` (`falha=1`) |
| T11 | InboxPipeline.cs:232 | AC-05: `Undo(Follow)` remove a relação | `A_follow_creates_the_relation...` (`falha=1`) |
| T12 | InboxPipeline.cs:240 | AC-05: `Delete` escreve o tombstone (some do feed) | `A_remote_delete_tombstones...` (`falha=1`) |
| T13 | SqliteLikeStore.cs:56 | AC-05: `Like` idempotente por ator+objeto (§7.14) | `The_same_actor_liking_twice...` (`falha=1`) |
| T14 | InboxPipeline.cs:253 | AC-05: `Flag` entra na fila `IReportStore` | `A_flag_files_one_denunciation...` (`falha=1`) |
| T15 | InboxPipeline.cs:294 | AC-06: `membersOnly` só com destinatário membro ativo | `A_members_only_create_applies...` (`falha=1`) |
| T16 | InboxPipeline.cs:293 | AC-06: `private` nunca aplicado em claro (R-11) | `A_private_create_is_never_applied` (`falha=1`) |
| T17 | InboxPipeline.cs:268 | AC-06: métrica `federation.unknown_activity` incrementada | `An_unknown_type_is_accepted...` (`falha=1`) |
| T18 | RemoteActivities.cs:205 | AC-07: o SHA-256 declarado gateia o anexo mantido | `Remote_attachments_are_verified...` (`falha=1`) |
| T19 | RemoteMediaClient.cs:93 | AC-07: a guarda SSRF recusa antes de qualquer socket | `The_production_media_client_never_touches...` (`falha=1`) |
| T20 | InboxPipeline.cs:289 | D-01 (gate humano): só destino nomeado — fallback rejeitado | `A_create_without_any_named_destination...` (`falha=1`) |

20× `PRE` com `head-match=true` (o hash de trabalho é o blob do commit `92db2c7`), 20× `RESTORE-OK hash=PRE residue=0`, 0× `RESTORE-FAIL`, 1× GREEN `26/26 exit 0`, resíduo final de tokens mutantes = 0, backups `qa0044-*` = 0, `-VerifyOnly` `bad=0` + ASCII OK, 20× `EXPECT-OK`, 0× `EXPECT-MISS` (run final), 0× `TOOLING-FAIL`, 0× `BUILD-FAIL`, wrapper 1 em todos (log e wrapper concordaram).

## Afirmações do DEV a verificar (declarações de nível spec)

| # | Afirmação (Log do task file / commit 92db2c7) | Julgamento desta QA | Evidência |
| --- | --- | --- | --- |
| 1 | "15 testes novos (14 em InboxReceiveTests + 1 em InboxRateLimitTests) cobrindo AC-01..AC-07, a regra de destino da D-01 e a guarda SSRF do download de midia" | **CONFIRMADA em execução** com um acréscimo desta QA: os 15 existem com marcador `// AC-01..// AC-07`/`// D-01` e passam; mas a lista de AC pedia mais dois cenários que não existiam — assinatura INVÁLIDA (forjada, não ausente) e o padrão 60 — escritos pela QA (nota 4) | filtro verde `225836` + marcadores (checker `TEST-AC`) + ciclos T6/T8 |
| 2 | "gate AC-08: sdd-test test -> exit 0 (527/527)" | **CONFIRMADA de forma independente**: gate da QA re-executado sobre a árvore restaurada = **529/529 exit 0 lido no log** (527 do DEV + 2 da QA) | comando #7 + `GATE-EXIT`/`GATE-SUMMARY`/`GATE-AGE` |
| 3 | "escopo auditado... nenhum arquivo fora dos touches" | **CONFIRMADA** para o commit (24/24 nos touches) e para a árvore de trabalho (`fora-dos-touches=0`, WIP do ODEV da baseline contado à parte, artefatos da QA em `docs/sdd/qa/`) | baseline + `QA-0044-scope.log` + `COMMIT-TOUCHES` |
| 4 | "D-01 aplicada literalmente... so destino nomeado... sem destino -> 202 sem aplicar" | **CONFIRMADA em execução e leitura**: `DestinationAsync` só aceita inbox de contêiner, `squid:slug` e caminho `/c/{slug}` (URI local casa exata, remota por slug) — `InboxPipeline.cs:341-376`; o cenário D-01 prova o `202` sem aplicar e o ciclo **T20** mostra que um fallback derruba o teste | cenário D-01 + ciclo T20 + leitura |
| 5 | "seams de rede trocadas por fakes pelo configure do GAP-17, nenhuma prova abre socket" | **CONFIRMADA por leitura e execução**: `InboxHostFixture` registra `IFederatedActorClient`/`IRemoteMediaClient` por último (last-one-wins) e nenhum teste do filtro resolve DNS; a guarda do caminho de produção é provada isoladamente pelo **T19** (cliente de mídia) | `InboxHost.cs:56-62` + ciclo T19 |
| 6 | "Accept de volta no corpo do 202" e "relacao de seguidor em documento de IFollowStore sem migration" | **CONFIRMADAS em execução**: o `Accept` vem no corpo do `202` (ciclo **T10** derruba) e a relação aparece nas coleções `followers`/`following` (ciclos T9/T11) | ciclos T9/T10/T11 + cenários AC-05 |

## Findings

Nenhum achado de **produto** (0 BLOCKER, 0 MAJOR, 0 MINOR — 529/529 verdes e os 20 mutantes caem exatamente onde cada AC diz). Notas de QA/fluxo:

| # | Severidade | Onde | O que | Evidência |
| --- | --- | --- | --- | --- |
| 1 | NOTA (pré-existente) | `docs/sdd/_tools/sdd-test.sh` / WSL | gêmeo Linux não roda a suite: `wsl -e bash -c "command -v dotnet"` → vazio, exit 1 (**sondado ao vivo NESTA sessão**, na baseline); instalação exige gate humano — mesmo achado das QA-0033..0043 | comando 1 + linha do Log |
| 2 | NOTA (DIVERGENCIA, já registrada pela QA-0043 e pelo ODEV) | `docs/memory-bank/contracts.md:21` | ainda diz que `inbox`/`outbox`/`followers`/`following` ficam sem handler até P-06/P-08; a TASK-0043 entrega as rotas e a TASK-0044 entrega o inbox de entrada e o conteúdo das relações (`rel=profile` continua P-08) — código vence, correção do memory bank na consolidação da fase | leitura `contracts.md:21` + ciclos T9/T11 |
| 3 | NOTA (força do cenário, decisão da QA) | `backend/src/Squid.Api/Posts/RemoteActivities.cs:131` (+ `SqliteFollowStore.cs:112`, `SqliteLikeStore.cs:56`) | o cenário AC-02 do DEV fica **verde** com a janela quebrada no sentido "sempre nova" (run 1 do driver, `T2 EXPECT-MISS`): a aplicação tem idempotência própria — objeto insert-only, relação um-par-par-linha, like por (ator,objeto), denúncia por (post,ator) — então "estado não duplica" se mantém sem a janela. O AC como está escrito é satisfeito (o observável é o parêntese dele); a janela é provada pelo outro lado (T2 final, veredito invertido → nada aplica → vermelho). Risco: baixo — "sem reprocessar" (custo) não é observável pela superfície | `QA-0044-mutations-run1.log` T2 + ciclo T2 final |
| 4 | NOTA (decisão da QA, dentro dos touches) | `backend/tests/Squid.Tests/Federation/QaTask0044InboxGapTests.cs` | a AC-03 nomeia "assinatura inválida" e os testes do DEV cobriam só assinatura AUSENTE + digest divergente; a AC-04 nomeia o padrão 60 e o teste do DEV só exercita a 429 com cota baixada para 2. A QA escreveu **2 cenários** (assinatura forjada com chave estrangeira → 401 intacto; cota padrão 60 sem configuração, 61ª recusada, taxa por domínio) e provou os dois verdes + os ciclos **T6** e **T8** | comandos #3/#9 + ciclos T6/T8 |
| 5 | NOTA (boundary da AC-01) | `InboxPipeline.cs:159-161` | o `shortcut:` declarado pelo DEV: o ator remoto não é casado com o dono da `keyId` (§3 passo 7 não o exige) — a entrega é aceita com o `actor` do payload desde que a assinatura verifique na chave publicada. Fora da lista de AC desta task; risco médio para o harness A→B da TASK-0047 | leitura do código + cenário AC-01 |
| 6 | NOTA (ferramenta da QA) | `qa-mutate-task-0044.ps1` | run 1 abortou só no T2 (EXPECT-MISS, causa investigada e virou a nota 3); o driver foi reancorado e o run final fechou 20/20. O ledger do run 1 fica em `QA-0044-mutations-run1.log` (nada foi apagado) | `QA-0044-mutations-run1.log` + ledger final |

## Não coberto / unverified

- **Suite de testes em Linux (WSL)**: `dotnet` ausente no WSL (sondado ao vivo nesta sessão: `command -v dotnet` → exit 1; instalação exige gate humano) e o gêmeo `sdd-test.sh` quebra sem ele → **unverified**, nunca assumido igual ao Windows. O gêmeo `.sh` do **checker** (parte estática: logs, resumos, hashes, git) roda — comando #10.
- **RFC 9421 (`Signature-Input`) na entrada**: a §3 passo 1 o aceita, mas nenhuma AC desta task o nomeia e os cenários do inbox são draft-cavage — cobertura do verificador mora na TASK-0042 (`HttpSignatureTests`, fora do filtro). Risco: baixo.
- **429 real no padrão 60**: a recusa do endpoint só é exercitada com cota 2/min (60 entregas por teste seriam lentas); o valor 60 é provado diretamente no limiter (teste da QA + T8). Risco: baixo.
- **Resolução DNS viva do remetente** (`HttpFederatedActorClient`, passo 7): as provas do filtro usam o fake de seams por desenho (GAP-17); a guarda SSRF do caminho de produção é provada no cliente de mídia (T19) e na QA-0029. Risco: baixo.
- **Interop com Mastodon/Friendica de verdade**: TASK-0049 (gate humano, SC-016). Risco: médio.
- **`Accept: */*`, `Update` federado, `Announce`**: fora desta lista de AC (GAP-42 → só 202 + métrica, provado no cenário AC-06).

## Reproduzir

```powershell
# pré-requisito: .NET 10 SDK + PowerShell no Windows, repo C:\dev\squid-squad-net\squid-execution
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~Squid.Tests.Federation"   # esperado: exit 0, Aprovado 26, Com falha 0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0044.ps1 -VerifyOnly   # esperado: exit 0, VERIFY total=20 bad=0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0044.ps1              # run fresco: exit 0, SUMMARY cycles=20 failed=0 (20 vermelhos de proposito)
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test                # gate: exit 0, Com falha 0, Aprovado 529
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-scope-task-0044.ps1                # esperado: exit 0, fora-dos-touches=0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0044.ps1                # esperado: exit 0, SUMMARY fails=0
wsl -e bash docs/sdd/qa/qa-check-task-0044.sh                                                          # esperado: exit 0, SUMMARY fails=0
```

## Artefatos desta QA

| Arquivo | Papel |
| --- | --- |
| `docs/sdd/qa/QA-0044-git-baseline.txt` | baseline antes de qualquer escrita: HEAD `92db2c7`, branch, status, arquivos do commit, sondas do WSL |
| `docs/sdd/qa/qa-mutate-task-0044.ps1` | driver de mutação can-fail (20 ciclos: AC-01 ×1, AC-02 ×1, AC-03 ×4, AC-04 ×2, AC-05 ×6, AC-06 ×3, AC-07 ×2, D-01 ×1; `-VerifyOnly` com checagem ASCII, S-16/S-22/S-23 embutidos, `RED-AC` = qual cenário da AC caiu) |
| `docs/sdd/qa/QA-0044-mutations.log` | ledger final: PRE (hash = blob do commit), vermelhos no log com RED-AC, restore, GREEN, UNTRACKED, SUMMARY |
| `docs/sdd/qa/QA-0044-mutations-run1.log` | ledger do run 1 preservado: 19/20 + o `EXPECT-MISS` do T2 que virou a nota 3 |
| `docs/sdd/qa/QA-0044-verify.log` | `-VerifyOnly` gravado (20 padrões `hits=1` + `ASCII OK` + `VERIFY total=20 bad=0`) |
| `docs/sdd/qa/qa-scope-task-0044.ps1` | varredura de escopo: blob-identity vs `rev-parse`, mtime desde a baseline, resíduo, classificação do `git status` |
| `docs/sdd/qa/QA-0044-scope.log` | saída da varredura (blob 7/7, mtime 8, residue 0, fora-dos-touches 0) |
| `docs/sdd/qa/qa-check-task-0044.ps1` | checker estático Windows (inclui `sdd-state check` da task file) |
| `docs/sdd/qa/qa-check-task-0044.sh` | gêmeo Linux do mesmo checker (sem o `sdd-state`, ferramenta Windows) |
| `backend/tests/Squid.Tests/Federation/QaTask0044InboxGapTests.cs` | os 2 cenários que a lista de AC exige (nota 4), provados pelos ciclos T6 e T8 |
| `docs/sdd/qa/QA-0044-inbox-verificacao-e-aplicacao.md` | este documento |
