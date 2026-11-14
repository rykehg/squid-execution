<!--
templateVersion: 1.0.0
 -->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: RASCUNHO
- owner: qa
- lastVerified: (últimos artefatos: docs/sdd/runs/test-runs/20261114-012335-test-gate.log [GREEN 539/539], docs/sdd/runs/test-runs/20261114-011620-test-filter.log [GREEN 36/36], QA-0045-mutations.log [cycles=10 failed=0], QA-0045-scope.log [fora-dos-touches=0])

# QA-0045 — Saída: fan-out por visibilidade, reavaliação R-05 e rota de follow (TASK-0045)

- runId: R-20261113-01 · attempt: **1/2** (bloco de controle `qaAttempts: 0/2` — contador é do ODEV, QA não escreve nele)
- QA: subagente qa-task + política ponytail-qa (full) — testes EXECUTADOS nesta sessão, não escritos e deixados
- leitura normativa: SPEC-0011 **§E (destino nomeado D-01/GAP-48) e §F (fan-out, R-05, rota de follow)** + AC-01..AC-07 do task file + contrato `contracts/activitypub.md` **§2.1, §4, §7.2/§7.3/§7.7** + doc 05 §3.2 + FR-036/037/038 + memória banc rota `qa-integracao` (`build-run-test.md#Local tests without gate`, `integrations.md`, `contracts.md`)
- commit sob verificação: `3226429` em `task/TASK-0045-saida-fan-out-e-follow` (**16 arquivos = produto nos touches + 3 testes; NENHUM fora**); baseline DEV `538/538`
- entrega da QA dentro dos touches: `backend/tests/Squid.Tests/Federation/QaTask0045FanOutGapTests.cs` (1 cenário que a lista de AC exige e os testes do DEV não alcançavam — a cláusula da AC-01 "e para a coleção `followers` do contêiner"; ver finding 1)
- natureza desta QA: **PRODUTO + TESTES** — 10 cenários (8 DEV do fan-out + 1 DEV da migration 0008 + 1 da QA) provados por **10 mutações can-fail contra o PRODUTO**, restauradas byte-exact, + gate completo (AC-07)
- verdict: **PASSED**

## AC → cenário (7 ACs; tudo executado nesta sessão)

| AC | Cenário | Execução (destaque) | Resultado |
| --- | --- | --- | --- |
| AC-01 | post `public` → 1 `PeerDelivery` `Pending` por seguidor REMOTO (o local não cruza a fila), `to=[Public]`, `cc=[{autor}/followers]`, `audience` = URI do contêiner e `squid:slug` no objeto — **mais** o destinatário da cláusula "(e para a coleção `followers` do contêiner)" | Verde nos dois filtros: baseline `20261114-010209-test-filter.log` (**exit 0, 35/35**), cenário novo em `20261114-010649-test-filter.log` (**exit 0, 36/36**). Can-fail: **T1** (`audience` apontando para um caminho errado em `ObjectJsonLd.cs:99` → **falha=2**, RED-AC = o cenário do DEV E o da QA — o payload NOMEIA o destino) e **T2** (coleção do contêiner fora do braço `Public` em `OutboundPipeline.cs:73` → **falha=1**, RED-AC `Ac01_public_post_also_enqueues_for_the_container_collection`) | PASS |
| AC-02 | `unlisted` → só seguidores (membro ativo REMOTO não recebe); `membersOnly` → só `Membership.State = Active` (seguidor não-membro e membro `Banned` ficam de fora), `squid:policy` no payload e `cc` vazio; `private` → **nenhuma** linha | Can-fail nas TRÊS metades: **T3** (unlisted passando a entregar membros → **falha=1**, RED-AC `Ac02_unlisted...`), **T4** (membersOnly trocado para seguidores → **falha=1**, RED-AC `Ac02_members_only...`), **T5** (guarda de `private` morta + braço default vazio virando seguidores → **falha=1**, RED-AC `Ac02_private_cria...`). Verde: os 3 cenários do DEV | PASS |
| AC-03 | `POST /api/federation/follow`: sem sessão → `401`, sem CSRF → `403`, alvo local → `400`, alvo remoto → `202` com `Follow` `Pending` enfileirado (e borda `Established=false`); `Accept` remoto assenta a relação; `Follow` remoto → `202` com `Accept` no corpo; o `Follow` sai ASSINADO (`keyId=#main-key`, `rsa-sha256`) no destino `/inbox` | Verde no cenário do DEV. Can-fail nas DUAS metades da rota: **T6** (`EnqueueFollowAsync` removido em `FederationEndpoints.cs:142` → **falha=1**, RED-AC `Ac03_follow_route...`), **T7** (gate de sessão removido em `FederationEndpoints.cs:119` → o anônimo deixa de ver `401` → **falha=1**, RED-AC `Ac03_follow_route...`). As metades de caixa de entrada (`Accept` cria relação, `Follow` responde `Accept`) são da TASK-0044 e já caíram nos ciclos T9/T10 da QA-0044 — reexecutadas verdes aqui | PASS |
| AC-04 | entrega `Pending` + troca de política para negar o destinatário + envio → `Cancelled` com motivo auditável persistido (`Reason` igual no veredito e na linha), **zero** chamadas ao sender, e um segundo envio da linha resolvida é no-op | Verde no cenário do DEV. Can-fail: **T8** (negação de `membersOnly` morta em `OutboundPipeline.cs:260` → o envio era permitido → **falha=2**, RED-AC `Ac04_policy_change...` + `Ac06_...` — a mesma reavaliação cancela o não-membro do AC-06) | PASS |
| AC-05 | a resposta ao cliente chega `201` com a entrega JÁ legível `Pending` (`Attempts=0`) e o sender intocado — a publicação nunca espera a rede | Verde no cenário do DEV. Can-fail: **T9** (a linha do `Create` nascendo `Sent` em `OutboundPipeline.cs:94-96` → **falha=5**, RED-AC inclui `Ac05_publish_returns_with_every_delivery_already_pending...`) | PASS |
| AC-06 | remover o post local cria `Delete` `Pending` exatamente para quem RECEBEU o `Create` (o cancelado pela política não recebe), payload `Delete` com `object` = id do post, assinatura `keyId=#main-key` + `Digest: SHA-256=` | Verde no cenário do DEV. Can-fail: **T10** (filtro `State != Cancelled` removido em `OutboundPipeline.cs:110` → o não-membro cancelado também recebe → **falha=1**, RED-AC `Ac06_removing_the_post...`) | PASS |
| AC-07 | gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` exit 0; nenhum arquivo alterado fora dos `touches` | Gate lido **dentro do log** `20261114-012335-test-gate.log` (S-22): cabeçalho `exit: 0` + `Com falha: 0, Aprovado: 539, Total: 539` (538 do DEV + 1 da QA), 6m32s. Escopo (`QA-0045-scope.log`): blob-identity `git hash-object` = `git rev-parse HEAD:` em **3/3** arquivos mutados/restaurados; mtime desde a baseline = **exatamente os 4 esperados**; resíduo de tokens = 0; `git status -- backend` = **1** entrada (o teste novo, dentro dos touches); `fora-dos-touches=0` (o WIP do ODEV do task file contado à parte) | PASS |

## Comandos executados (todos nesta sessão, com exit real; datas vêm dos próprios logs)

| # | comando | exit | artefato / saída |
| --- | --- | --- | --- |
| 1 | `git rev-parse HEAD` + `git rev-parse --abbrev-ref HEAD` + `git status --porcelain -uall` + `git show --name-only HEAD` + sondas `wsl -e bash -c "command -v dotnet"` e `command -v git` (pwsh) | 0 | `docs/sdd/qa/QA-0045-git-baseline.txt` — HEAD `3226429`, branch `task/TASK-0045-saida-fan-out-e-follow`, 16 arquivos do commit **todos nos touches**, **dotnet ausente no WSL** (git = `/usr/sbin/git`) |
| 2 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Federation` (baseline da QA, antes de qualquer escrita) | 0 | `20261114-010209-test-filter.log` — `exit: 0`, Com falha 0 / Aprovado 35 / Total 35 (28s) |
| 3 | mesmo comando #2 depois de `QaTask0045FanOutGapTests.cs` entrar no filtro | 0 | `20261114-010649-test-filter.log` — `exit: 0`, Com falha 0 / Aprovado 36 / Total 36 (30s) — o cenário da QA executado e verde |
| 4 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0045.ps1 -VerifyOnly` (antes de cada run) | 0 | `docs/sdd/qa/QA-0045-verify.log` — 10 padrões `hits=1 hits2=1`, `non-ascii-bytes=0 ok=true`, `VERIFY total=10 bad=0` |
| 5 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0045.ps1` (run único) | 0 | `docs/sdd/qa/QA-0045-mutations.log` — 10 vermelhos com RED-AC da própria AC em cada um, 10× `RESTORE-OK hash=PRE residue=0`, `GREEN wrapper=0 logExit=0 aprovado=36 falha=0`, `UNTRACKED backend=1`, `SUMMARY cycles=10 failed=0 residue-backend=0 elapsed-s=445` |
| 6 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-scope-task-0045.ps1` (varredura de escopo) | 0 | `docs/sdd/qa/QA-0045-scope.log` — blob 3/3 `match=True`, `mtime-count=4 (esperado 4)`, `residue=0`, `git-backend-entries=1`, `fora-dos-touches=0`, WIP do ODEV = 1 |
| 7 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` (gate AC-07, suite inteira com BuildProof) | 0 | `docs/sdd/runs/test-runs/20261114-012335-test-gate.log` — `exit: 0` lido **dentro do log** (S-22) + `Com falha: 0, Aprovado: 539, Ignorado: 0, Total: 539` (6m32s) |
| 8 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0045.ps1` | 0 | `docs/sdd/qa/QA-0045-check.log` — SUMMARY **total=47 fails=0** (inclui `sdd-state check`: OK, log 13, evidence 3) |
| 9 | `wsl -e bash docs/sdd/qa/qa-check-task-0045.sh` (gêmeo Linux) | 0 | `docs/sdd/qa/QA-0045-check-sh.log` — SUMMARY **total=34 fails=0** |

As linhas `## Log` e `## Evidence` destas execuções **não** foram gravadas pela QA: o dispatch devolve `logLines`/`evidenceRows` sem relógio para o ODEV carimbar (o relógio é dele). Esta tabela acima é a prova; a task file permanece intacta pela QA.

## Ciclos de mutação (can-fail; S-16 hash PRE = blob do commit, S-22 classificação no log, S-23 restore byte-exact)

Todos nos arquivos de PRODUTO que a behaviour lê (`Squid.Protocols/ActivityPub/ObjectJsonLd.cs`, `Squid.Service/OutboundPipeline.cs`, `Squid.Api/Federation/FederationEndpoints.cs`) — nenhum edit fora do escopo, e os 3 arquivos finais são byte-iguais ao `HEAD` (blob-identity 3/3; `git status -- backend -uno` = 0).

| Ciclo | Arquivo | O que prova (AC) | Vermelho classificado NO LOG |
| --- | --- | --- | --- |
| T1 | ObjectJsonLd.cs:99 | AC-01: o `Create` NOMEIA o destino — `audience` = URI do contêiner (GAP-48/D-01) | `Ac01_public_post_enqueues...` + `Ac01_public_post_also_enqueues...` (`falha=2`) |
| T2 | OutboundPipeline.cs:73 | AC-01: a coleção do contêiner entra no fan-out de `public` (parêntese da AC) | `Ac01_public_post_also_enqueues_for_the_container_collection` (`falha=1`) |
| T3 | OutboundPipeline.cs:71 | AC-02: `unlisted` entrega SÓ aos seguidores (§4) | `Ac02_unlisted_delivers_only_to_followers` (`falha=1`) |
| T4 | OutboundPipeline.cs:72 | AC-02: `membersOnly` entrega SÓ a membros ativos (§7.2) | `Ac02_members_only_delivers_only_to_active_members` (`falha=1`) |
| T5 | OutboundPipeline.cs:63 + :74 | AC-02: `private` não cria nenhuma entrega (§7.3) — guarda morta + braço default vazio | `Ac02_private_creates_no_delivery_at_all` (`falha=1`) |
| T6 | FederationEndpoints.cs:142 | AC-03: a rota enfileira o `Follow` para o par (GAP-41) | `Ac03_follow_route_enqueues_a_signed_follow...` (`falha=1`) |
| T7 | FederationEndpoints.cs:119 | AC-03: a rota exige sessão — anônimo responde `401` | `Ac03_follow_route_enqueues_a_signed_follow...` (`falha=1`) |
| T8 | OutboundPipeline.cs:260 | AC-04: a reavaliação R-05 NO MOMENTO DO ENVIO nega o destinatário (FR-038) | `Ac04_policy_change_before_send...` + `Ac06_removing_the_post...` (`falha=2`) |
| T9 | OutboundPipeline.cs:94-96 | AC-05: a entrega nasce `Pending` registrada antes da resposta (FR-036) | 5 vermelhos incluindo `Ac05_publish_returns_with_every_delivery_already_pending...` (`falha=5`) |
| T10 | OutboundPipeline.cs:110 | AC-06: o `Delete` anda pelo que RECEBEU o `Create`, pulando o cancelado (R-08) | `Ac06_removing_the_post_propagates_a_signed_delete...` (`falha=1`) |

10× `PRE` com `head-match=true` (o hash de trabalho é o blob do commit `3226429`), 10× `RESTORE-OK hash=PRE residue=0`, 0× `RESTORE-FAIL`, 1× GREEN `36/36 exit 0`, resíduo final de tokens mutantes = 0, backups `qa0045-*` = 0, `-VerifyOnly` `bad=0` + ASCII OK, 10× `EXPECT-OK`, 0× `EXPECT-MISS`, 0× `TOOLING-FAIL`, 0× `BUILD-FAIL`, wrapper 1 em todos (log e wrapper concordaram).

## Afirmações do DEV a verificar (declarações de nível spec)

| # | Afirmação (Log do task file / commit 3226429) | Julgamento desta QA | Evidência |
| --- | --- | --- | --- |
| 1 | "9 testes novos (8 ACs + migration 0008) executados verdes" | **CONFIRMADA em execução**: `OutboundFanOutTests` tem 8 `[Fact]` com marcador `// AC-01..// AC-06` e `FederationMigrationTests` tem 1 — os 9 verdes na baseline (35/35) e depois 36/36 com o teste da QA | comandos #2/#3 + checkers `TEST-AC`/`TEST-MIGRATION` |
| 2 | "gate completo re-executado em 538/538 exit 0" | **CONFIRMADA de forma independente**: gate da QA re-executado sobre a árvore restaurada = **539/539 exit 0 lido no log** (538 do DEV + 1 da QA) | comando #7 + `GATE-EXIT`/`GATE-SUMMARY`/`GATE-AGE` |
| 3 | "commit unico 3226429 ... apenas com arquivos de backend (arquivo da task intocado, segue no working tree)" | **CONFIRMADA**: 16/16 arquivos nos glob sets dos `touches`; o task file aparece como `M` no working tree (WIP do ODEV, fora do commit) | baseline + `COMMIT-TOUCHES` + `QA-0045-scope.log` |
| 4 | "payload do Create ... audience com a URI do contêiner e squid:slug no objeto — emenda GAP-48" | **CONFIRMADA em execução e leitura**: asserts do AC-01 + o cenário da QA derivam `audience` do próprio `IContainerStore`; o ciclo **T1** derruba os dois cenários quando a URI muda | cenários AC-01 + T1 + `WIRE-AUDIENCE` |
| 5 | "SendAsync executa a reavaliação R-05 ... negada grava Cancelled com motivo auditavel ... sem consultar o sender" | **CONFIRMADA em execução**: `Ac04` prova `Cancelled` + `Reason` persistido + sender vazio + reenvio no-op; o ciclo **T8** mostra que matar a negação derruba o teste | cenário AC-04 + T8 |
| 6 | "escrita local de PeerDelivery Pending antes do 201/200, nenhum envio na request" | **CONFIRMADA em execução**: `Ac05` lê a linha `Pending` na resposta `201` com sender vazio; o ciclo **T9** (linha nascendo `Sent`) derruba | cenário AC-05 + T9 + `PostEndpoints.cs:251` |
| 7 | "rota POST /api/federation/follow valida sessão, CSRF e alvo remoto, enfileira o Follow" | **CONFIRMADA em execução**: 401/403/400/202 no cenário do DEV; ciclos **T6** (enfileira) e **T7** (sessão) derrubam as duas metades | cenário AC-03 + T6 + T7 |

## Findings

Nenhum achado de **produto** que impeça o verdict (0 BLOCKER, 0 MAJOR — 539/539 verdes e os 10 mutantes caem exatamente onde cada AC diz). Achados e notas:

| # | Severidade | Onde | O que | Evidência |
| --- | --- | --- | --- | --- |
| 1 | MINOR — **divergência de leitura, adjudicação do ODEV** | `specs/001-squid-mvp/contracts/activitypub.md:155` + `docs/features/05-arquitetura-consolidada.md:269-270` × `backend/src/Squid.Service/OutboundPipeline.cs:73` + `backend/src/Squid.Api/Federation/IOutboundPipeline.cs:17-18` | A DEV leu "coleção `followers` do contêiner" (AC-01/§4) como as **`Membership.State = Active`** do contêiner. Esta QA lê o contrato de forma DIFERENTE: a §4 dá aos membros ativos a linha própria do `membersOnly` ("apenas atores com `Membership.State = Active`") e devolve ao `public` uma "coleção `followers` do contêiner" distinta — doc 05 §3.2 também separa a regra 1 (seguidores + `followers` collection) da regra 2 (Membership ativa). Só que **essa coleção não existe no MVP**: o `Group` publicado não tem campo `followers` (`ObjectJsonLd.cs:206-218`, contrato §1.2), não há rota `/c/{slug}/followers`, e um `Follow` cujo objeto é contêiner local vira `202` sem aplicar estado (`InboxPipeline.cs:321-328`) — logo a leitura da DEV é a única implementável hoje, e é a que os `touches` da própria task indicam ("`Squid.Api/Containers/**` — destinatários `followers` do contêiner"). Risco: baixo para `public` (conteúdo público entregue a membro ativo não vaza nada restrito; nenhum destinatário existente fica de fora); o cenário da QA **fixa a leitura em vigor** e acompanha a AC — se o ODEV adjudicar ao contrário, muda o fan-out E o cenário junto | contrato §4:155 + doc05:269 + `OutboundPipeline.cs:69-76` + ciclo T2 + `QaTask0045FanOutGapTests.cs` |
| 2 | NOTA (DIVERGENCIA, já registrada pela QA-0043/QA-0044) | `docs/memory-bank/contracts.md:21` | ainda diz que `inbox`/`outbox`/`followers`/`following` ficam sem handler até P-06/P-08; a TASK-0043 entrega as rotas e a TASK-0044 o inbox de entrada (`rel=profile` continua P-08) — código vence, correção do memory bank na consolidação da fase | leitura `contracts.md:21` + cenários AC-03 |
| 3 | NOTA (pré-existente) | WSL / `docs/sdd/_tools/sdd-test.sh` | `wsl -e bash -c "command -v dotnet"` → vazio, exit 1 (**sondado ao vivo NESTA sessão**, na baseline): a suite não roda em Linux nesta máquina e o gêmeo `.sh` do gate quebra sem ele (memória banc, backlog S-11/S-12) — instalação exige gate humano | comando 1 + linha do Log |
| 4 | NOTA (fora da lista de AC, observação) | `backend/src/Squid.Api/Federation/FederationEndpoints.cs:127-143` | a rota de follow valida "URI absoluta remota", não "ator": um `targetActorUri` apontando para um contêiner REMOTO (`https://par/c/slug`) também é aceito e enfileirado — fora desta lista de AC (a AC-03 só pede sessão/CSRF/par); risco baixo, P-08 envia ator | leitura do código + cenário AC-03 |
| 5 | NOTA (boundary da AC-01) | `backend/src/Squid.Service/OutboundPipeline.cs:69-76` | SPEC-0011 §F lista **menções** como destinatário do fan-out; a lista de AC desta task não as nomeia e o produto não as entrega — a QA não testa o que a AC não pede; risco médio-baixo (menção remota não federada até task própria) | §F:138 × AC-01 |
| 6 | NOTA (boundary da AC-04) | `backend/src/Squid.Service/OutboundPipeline.cs:229-235` | a reavaliação R-05 só corre para `Create`: `Delete` e `Follow` são isentos por decisão documentada (tombstone sem conteúdo; Follow sem política de contêiner). A AC-04 cobre o `Create`; o `Delete` após política virar `private` ainda sai como tombstone — sem vazamento de conteúdo | leitura do código + ciclos T8/T10 |
| 7 | NOTA (ferramenta da QA) | `qa-check-task-0045.ps1` / `.sh` | a run 1 do checker acusou `COMMIT-TOUCHES FAIL` por causa do PRÓPRIO parser (o cabeçalho da baseline é `--- commit files (HEAD) ---` e a linha `commit <sha> <assunto>` do `git show` entrava na contagem como se fosse caminho); parser corrigido nos dois gêmeos e a run final fechou 47/0 e 34/0. O ledger da run 1 fica em `QA-0045-check-run1.log` (nada foi apagado) | `QA-0045-check-run1.log` + `QA-0045-check.log` |

## Não coberto / unverified

- **Suite de testes em Linux (WSL)**: `dotnet` ausente no WSL (sondado ao vivo nesta sessão: `command -v dotnet` → exit 1; instalação exige gate humano) e o gêmeo `sdd-test.sh` quebra sem ele → **unverified**, nunca assumido igual ao Windows. O gêmeo `.sh` do **checker** (parte estática: logs, resumos, hashes, git) roda — comando #9. O CI da fase já rodou a suite em ubuntu numa janela anterior, mas **não** nesta árvore.
- **Envio real por socket (`HttpPeerSender`)**: os cenários trocam `IPeerSender` pelo fake (seam GAP-17) por desenho — nenhuma prova desta QA abre rede; a assinatura produzida é a real (`HttpSignatureSigner` + chave do ator). Risco: baixo para as ACs (a AC-04 observa justamente "nada é enviado"); o caminho HTTP de produção entra com o motor da TASK-0046 e a facet A→B da TASK-0047.
- **Motor de entrega (backoff, DLQ, circuito, rate por par)**: TASK-0046 — as entregas desta task nascem `Pending` e são observáveis, como o gap da task define.
- **Interop com Mastodon/Friendica de verdade**: TASK-0049 (gate humano, SC-016).
- **`unlisted`/`membersOnly`/`private` em duas instâncias reais (contrato §7 itens 2/3)**: a facet de dois silos é a TASK-0047; aqui as recusas são provadas pela fila local e pela reavaliação.

## Reproduzir

```powershell
# pré-requisito: .NET 10 SDK + PowerShell no Windows, repo C:\dev\squid-squad-net\squid-execution
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~Squid.Tests.Federation"   # esperado: exit 0, Aprovado 36, Com falha 0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0045.ps1 -VerifyOnly   # esperado: exit 0, VERIFY total=10 bad=0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0045.ps1              # run fresco: exit 0, SUMMARY cycles=10 failed=0 (10 vermelhos de proposito)
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-scope-task-0045.ps1                # esperado: exit 0, fora-dos-touches=0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test                # gate: exit 0, Com falha 0, Aprovado 539
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0045.ps1               # esperado: exit 0, SUMMARY fails=0
wsl -e bash docs/sdd/qa/qa-check-task-0045.sh                                                        # esperado: exit 0, SUMMARY fails=0
```

## Artefatos desta QA

| Arquivo | Papel |
| --- | --- |
| `docs/sdd/qa/QA-0045-git-baseline.txt` | baseline antes de qualquer escrita: HEAD `3226429`, branch, status, arquivos do commit, sondas do WSL |
| `docs/sdd/qa/qa-mutate-task-0045.ps1` | driver de mutação can-fail (10 ciclos: AC-01 ×2, AC-02 ×3, AC-03 ×2, AC-04 ×1, AC-05 ×1, AC-06 ×1; `-VerifyOnly` com checagem ASCII, S-16/S-22/S-23 embutidos, `RED-AC` = qual cenário da AC caiu) |
| `docs/sdd/qa/QA-0045-mutations.log` | ledger final: PRE (hash = blob do commit), vermelhos no log com RED-AC, restore, GREEN, UNTRACKED, SUMMARY |
| `docs/sdd/qa/QA-0045-verify.log` | `-VerifyOnly` gravado (10 padrões `hits=1 hits2=1` + `ASCII OK` + `VERIFY total=10 bad=0`) |
| `docs/sdd/qa/qa-scope-task-0045.ps1` | varredura de escopo: blob-identity vs `rev-parse`, mtime desde a baseline, resíduo, classificação do `git status` |
| `docs/sdd/qa/QA-0045-scope.log` | saída da varredura (blob 3/3, mtime 4, residue 0, fora-dos-touches 0) |
| `docs/sdd/qa/qa-check-task-0045.ps1` | checker estático Windows (inclui `sdd-state check` da task file) |
| `docs/sdd/qa/qa-check-task-0045.sh` | gêmeo Linux do mesmo checker (sem o `sdd-state`, ferramenta Windows) |
| `docs/sdd/qa/QA-0045-check.log` / `QA-0045-check-sh.log` | saídas dos dois checkers (run final; a run 1 com o parser corrigido fica em `QA-0045-check-run1.log`) |
| `backend/tests/Squid.Tests/Federation/QaTask0045FanOutGapTests.cs` | o cenário da cláusula da AC-01 (coleção do contêiner), provado pelo ciclo T2 |
| `docs/sdd/qa/QA-0045-saida-fan-out-e-follow.md` | este documento |
