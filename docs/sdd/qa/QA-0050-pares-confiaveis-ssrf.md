<!--
templateVersion: 1.0.0
 -->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: RASCUNHO
- owner: qa
- lastVerified: (últimos artefatos: docs/sdd/qa/QA-0050-mutations.log [cycles=5 failed=0, GREEN 5/5], QA-0050-verify.log [VERIFY total=5 bad=0], gate 20261114-043833-test-gate.log [exit: 0 no cabeçalho, Com falha 0, Aprovado 559, Total 559, 6m44s], QA-0050-scope.log e QA-0050-scope-sh.log [fora-dos-touches=0, fails=0], baseline QA-0050-git-baseline.txt [fails=0])

# QA-0050 — Pares confiáveis: `Federation:TrustedPeerHosts` nas guardas de federação (TASK-0050)

- runId: R-20261113-01 · attempt: **1/2** (bloco de controle `qaAttempts: 0/2` — contador é do ODEV, QA não escreve nele)
- QA: subagente qa-task + política ponytail-qa (full) — testes EXECUTADOS nesta sessão, não escritos e deixados
- leitura normativa: SPEC-0011 **§C "Pares confiáveis"** (decisão do ODEV 2026-11-13 22:41:54) + **GAP-49** + AC-01..AC-04 do task file + memória banc rota `qa-integracao` (`build-run-test.md#Local tests without gate`, `integrations.md` [rascunho/template], `contracts.md`)
- commit sob verificação: `9758f8c` em `task/TASK-0050-pares-confiaveis-ssrf` (**8 arquivos = produto nos touches + testes; NENHUM fora**; não pushado — `git branch -r --contains` vazio; pai `b48bf17` = ponta de develop; `main` = `b9fd628` intocado). O ` M docs/sdd/tasks/TASK-0050...md` do working tree é o carimbo do ODEV — a QA não o toca nem o committa
- entrega da QA dentro dos touches: **NENHUM arquivo novo em `backend/`** — os 4 cenários que a lista de AC exige já existem nos testes do DEV (5 `[Fact]` em `TrustedPeerHostsTests.cs`, marcados `// AC-01`..`// AC-03`); a QA acrescenta apenas artefatos em `docs/sdd/qa/`
- natureza desta QA: **PRODUTO** — 4 ACs provadas por **5 mutações can-fail contra o PRODUTO**, restauradas byte-exact (hash PRE=POST), + gate completo lido dentro do log (S-22) + varredura de escopo nas duas shells
- julgamento do dispatch sobre AC-01 "byte-idêntico": **D-01 fechada no nível especificação** — SPEC-0011 §C põe a entrega ao par sob a allowlist ("só ela libera loopback/rede privada nos clientes de federação — resolução de ator, download de mídia, entrega ao par"), então o `HttpPeerSender` passando a recusar loopback/rede privada **por padrão** é in-spec; "byte-idêntico" vale para os dois clientes que já recusavam (ator e mídia) — provado por leitura do diff (só se acrescenta o release, que com lista vazia nunca dispara) + F4 verde + T1 vermelho
- verdict: **PASSED**

## AC → cenário (4 ACs; tudo executado nesta sessão)

| AC | Cenário | Execução (destaque) | Resultado |
| --- | --- | --- | --- |
| AC-01 | com a lista ausente/vazia (default), os três clientes de federação seguem recusando alvo em loopback/rede privada | Verde na baseline (`20261114-042903-test-filter.log`, exit 0, **5/5**) com `The_default_allowlist_keeps_every_federation_client_refusing_private_targets` (F1: mídia, ator e entrega recusam 127.0.0.1 e 192.168.1.9 e o tripwire `Assert.Empty` prova que nenhum socket foi tocado) e `The_allowlist_is_empty_by_default_and_matches_hosts_exactly` (F4: `From(config vazia)` → nada confiado). Can-fail: **T1** (`Contains` permissivo → **falha=4**, RED = F1 [linha 47, `Assert.Empty`, coleção com os 6 URIs vazados] + F4 [linha 122] + F2 + F3) e **T4** (perímetro do `HttpPeerSender` morto → **falha=2**, RED = F1 [`Assert.Empty` = `127.0.0.1:8080/inbox`, `private.test/inbox`] + F3) | PASS |
| AC-02 | host do par nomeado aceito em loopback/rede privada para ator e mídia; host fora da lista recusado inclusive na mesma porta; entrega ao par segue a mesma régua | Verde com `A_named_peer_is_accepted_on_loopback_and_an_unlisted_host_on_the_same_port_is_not` (F2: `127.0.0.1:8080` e `named.test:8080` aceitos — este resolve `192.168.1.9` de propósito; `127.0.0.2:8080` e `other.test:8080` recusados **na mesma porta**, handler intacto nas 3 recusas) e `The_delivery_client_sends_to_a_named_peer_and_refuses_one_outside_the_list` (F3: entrega aceita ao par nomeado, recusa `127.0.0.2:8080` com `StatusCode` nulo, `Single(handler.Requests)`). Can-fail nas QUATRO metades: **T1** (**falha=4**, RED inclui F2 [`Assert.Null`] e F3 [linha 110, `Assert.False`]), **T2** (release da mídia morto → **falha=2**, RED = F2 [`Assert.NotNull`] + F5), **T3** (release do ator morto → **falha=1**, RED = F2 [`Assert.True` esperado True, veio False]) e **T4** (**falha=2**, RED = F3 + F1) | PASS |
| AC-03 | a lista vale SÓ para os três clientes: o unfurl continua bloqueando mesmo com a lista nomeada no host de produção | Verde com `The_unfurl_keeps_blocking_private_even_with_the_allowlist_named` (F5: host real via `SquidServiceHost.Build --Federation:TrustedPeerHosts=127.0.0.1` → unfurl do **próprio host nomeado** devolve `null` sem tocar o handler, e o cliente de mídia do MESMO host aceita). Can-fail: **T5** (guarda do unfurl morta em `LinkUnfurler` → **falha=1**, RED = só F5, `Assert.Null` com `Actual: Attachment {Kind = Link ... SizeBytes = 3}` — a página de loopback foi baixada) e **T2** (metade "só clientes de federação": release da mídia morto → F5 vermelha junto com F2). Composição de leitura: `LinkUnfurler.cs` não contém `TrustedPeerHosts` (grep = 0) e não mudou no commit (8 arquivos, nenhum é o unfurl); as faixas privadas do unfurl correm verdes na suite Unfurl dentro do gate 559/559 | PASS |
| AC-04 | gate `sdd-test.ps1 test` exit 0; nenhum arquivo alterado fora dos `touches` | Gate lido **dentro do log** `20261114-043833-test-gate.log` (S-22): cabeçalho `exit:    0` + `Com falha: 0, Aprovado: 559, Ignorado: 0, Total: 559` (6m44s), sobre a árvore restaurada com os artefatos da QA no lugar. Escopo nas DUAS shells: `QA-0050-scope.log` (exit 0 — commit **8/8 nos touches**, `wip-odev=1` só do task file, `git-backend-entries=0`, `residue=0`, blob-identity **5/5**, `mtime=5`, `fora-dos-touches=0`, `fails=0`) e `QA-0050-scope-sh.log` (exit 0 sob WSL, mesmas contagens) | PASS |

## Comandos executados (todos nesta sessão, com exit real; datas vêm dos próprios logs)

| # | comando | exit | artefato / saída |
| --- | --- | --- | --- |
| 1 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-baseline-task-0050.ps1` | 0 | `docs/sdd/qa/QA-0050-git-baseline.txt` — HEAD `9758f8c`, branch da task, só o task file do ODEV modificado + artefatos da QA, 8 arquivos do commit, `branch-r-contains=EMPTY`, `develop-tip=b48bf17` (= pai do HEAD), `main=b9fd628`, sondas: `wsl-dotnet=ABSENT`, `wsl-git=/usr/sbin/git`, `gitbash-probe=/c/Program Files/dotnet ; /ucrt64/bin/git`, `windows-dotnet=10.0.403`, `fails=0` |
| 2 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0050.ps1 -VerifyOnly` | 0 | `docs/sdd/qa/QA-0050-verify.log` — 5 padrões `hits=1`, `non-ascii-bytes=0`, `VERIFY total=5 bad=0` |
| 3 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~TrustedPeerHostsTests` | 0 | `docs/sdd/runs/test-runs/20261114-042903-test-filter.log` — `exit: 0`, Com falha 0 / Aprovado 5 / Total 5 (824 ms) — baseline verde antes de qualquer mutação |
| 4 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0050.ps1` (run única) | 0 | `docs/sdd/qa/QA-0050-mutations.log` — 5 vermelhos de propósito com RED-AC da própria AC em cada um, 5× `RESTORE-OK hash=PRE residue=0`, `GREEN wrapper=0 logExit=0 aprovado=5 falha=0`, `UNTRACKED backend=0`, `SUMMARY cycles=5 failed=0 residue-backend=0 elapsed-s=81` |
| 5 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` (gate AC-04, suite inteira com BuildProof) | 0 | `docs/sdd/runs/test-runs/20261114-043833-test-gate.log` — `exit: 0` lido **dentro do log** (S-22) + `Com falha: 0, Aprovado: 559, Ignorado: 0, Total: 559` |
| 6 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-scope-task-0050.ps1` | 0 | `docs/sdd/qa/QA-0050-scope.log` — `SUMMARY commit-files=8 ... residue=0 blob=5/5 mtime=5 fails=0` |
| 7 | `wsl -e bash docs/sdd/qa/qa-scope-task-0050.sh` (gêmeo Linux do escopo) | 0 | `docs/sdd/qa/QA-0050-scope-sh.log` — mesmas contagens sob bash/WSL, `fails=0` |

As linhas `## Log` e `## Evidence` destas execuções **não** foram gravadas pela QA: o dispatch devolve `logLines`/`evidenceRows` sem relógio para o ODEV carimbar (o relógio é dele). A tabela acima é a prova; a task file permanece intacta pela QA.

## Ciclos de mutação (can-fail; S-16 hash PRE = blob do commit, S-22 classificação no log, S-23 restore byte-exact)

Todos nos arquivos de **PRODUTO** que a behaviour lê (`TrustedPeerHosts.cs`, `RemoteMediaClient.cs`, `HttpFederatedActorClient.cs`, `HttpPeerSender.cs`, `LinkUnfurler.cs`) — nenhum edit fora do escopo, e os 5 arquivos finais são byte-iguais ao `HEAD` (blob-identity 5/5; `git status -- backend` = 0).

| Ciclo | Arquivo | O que prova (AC) | Vermelho classificado NO LOG |
| --- | --- | --- | --- |
| T1 | `TrustedPeerHosts.cs:45` | AC-01: a allowlist é exata e VAZIA por padrão — um `Contains` permissivo derruba a recusa default dos três clientes e o match exato | `The_default...` [`Assert.Empty`, 6 URIs vazados] + `The_allowlist_is_empty...` [linha 122] + `A_named_peer...` [`Assert.Null`] + `The_delivery_client...` [linha 110] (`falha=4`, log `20261114-042928-test-filter.log`) |
| T2 | `RemoteMediaClient.cs:98` | AC-02: o download de mídia libera o par NOMEADO antes do perímetro (e, em F5, o mesmo host é aceito pelo cliente enquanto o unfurl bloqueia) | `A_named_peer...` + `The_unfurl_keeps...` (`falha=2`, log `20261114-042943-test-filter.log`, `Assert.NotNull` nos dois) |
| T3 | `HttpFederatedActorClient.cs:210` | AC-02: a resolução de ator libera o par NOMEADO em loopback | `A_named_peer...` (`falha=1`, log `20261114-042958-test-filter.log`, `Assert.True` esperado True, veio False) |
| T4 | `HttpPeerSender.cs:68` | AC-01/AC-02: a entrega ao par ENFORÇA o perímetro — sem ele o default não recusa loopback e caixa de fora da lista recebe POST | `The_delivery_client...` [linha 110, `Assert.False`] + `The_default...` [`Assert.Empty` = inbox loopback e privado] (`falha=2`, log `20261114-043011-test-filter.log`) |
| T5 | `LinkUnfurler.cs:123` | AC-03: o unfurl segue bloqueando o host nomeado — a guarda dele não aceita liberação nenhuma | `The_unfurl_keeps...` (`falha=1`, log `20261114-043024-test-filter.log`, `Assert.Null` com `Actual: Attachment ... SizeBytes = 3`) |

5× `PRE` com `head-match=true` (o hash de trabalho é o blob do commit `9758f8c`), 5× `RESTORE-OK hash=PRE residue=0`, 0× `RESTORE-FAIL`, 0× `TOOLING-FAIL`, 0× `BUILD-FAIL`, 5× `EXPECT-OK`, 0× `EXPECT-MISS`, 1× GREEN `5/5 exit 0` (`20261114-043036-test-filter.log`), resíduo final de tokens mutantes = 0, `UNTRACKED backend=0`, `SUMMARY cycles=5 failed=0 residue-backend=0 elapsed-s=81`.

## Afirmações do DEV a verificar (declarações do `## Log` e do commit 9758f8c)

| # | Afirmação | Julgamento desta QA | Evidência |
| --- | --- | --- | --- |
| 1 | "criado TrustedPeerHosts ... exata e vazia por padrao (escalar com virgulas ou filhos indexados)" | **CONFIRMADA em execução**: F4 verde cobre default vazio, as duas formas da chave (escalar e `:0`), grafias IPv6 (`[::1]` = `::1`), caixa DNS e a exatidão (`evil.peer-a.test` e `a.test` fora); T1 vermelho mostra que é ela que segura o default | comandos #3/#4 + ciclo T1 |
| 2 | "consultado antes do perimetro por RemoteMediaClient, HttpFederatedActorClient e HttpPeerSender" | **CONFIRMADA em leitura e execução**: release antes do literal/DNS nos três (`RemoteMediaClient.cs:98`, `HttpFederatedActorClient.cs:210`, `HttpPeerSender.cs:106`); T2/T3/T4 derrubam cada um dos três | leitura + ciclos T2/T3/T4 |
| 3 | "HttpPeerSender ganhou o perimetro SSRF que o shortcut adiava (shortcut removido)" | **CONFIRMADA**: o comentário `shortcut: no SSRF perimeter here` saiu do diff; `AllowedAsync` em `HttpPeerSender.cs:97-111` aplica esquema, release exata, literal e resolução; T4 mostra que a guarda é carga viva | diff do commit + ciclo T4 |
| 4 | "recusa fora do perimetro responde PeerSendAnswer falso, mesmo caminho de par inalcanquivel (Pending, backoff, max_attempts ate a DLQ)" | **CONFIRMADA por leitura + composição**: `HttpPeerSender.cs:73` devolve exatamente o mesmo `(false, null)` do caminho de falha `:92`; o tratamento motor desse valor (Pending → backoff → DLQ) já foi provado na QA-0046 | leitura das duas linhas + `QA-0046-peer-grain-fila-e-dlq.md` |
| 5 | "MotorHost da suite do pump nomeia 127.0.0.1 em Federation:TrustedPeerHosts" | **CONFIRMADA**: a linha `--Federation:TrustedPeerHosts=127.0.0.1` está no diff de `PeerDeliveryMotorTests.cs:45` e a suite inteira do motor corre verde dentro do gate 559/559 | diff do commit + comando #5 |
| 6 | "LinkUnfurler intocado, a lista nao chega nele" | **CONFIRMADA**: o commit tem 8 arquivos e nenhum é `LinkUnfurler.cs`; grep `TrustedPeerHosts|Federation` em `LinkUnfurler.cs` = **0**; F5 verde + T5 vermelho mostram que a guarda do unfurl é a única barreira e ela não conhece lista | `git show --stat` + grep + comandos #3/#4 |
| 7 | "novo TrustedPeerHostsTests com 5 facts cobrindo AC-01 ... AC-03" | **CONFIRMADA**: exatamente 5 `[Fact]` (`TrustedPeerHostsTests.cs:23,53,94,118,141`), todos verdes na baseline e todos vermelhos sob a mutação correspondente | comandos #3/#4 |
| 8 | "gate AC-04: sdd-test.ps1 test exit 0 com 559/559" | **CONFIRMADA de forma independente**: gate DA QA re-executado → mesmo total (559 = 554 do baseline P-06 + 5 novos), `exit: 0` e `Com falha: 0` lidos dentro do log | comando #5 |
| 9 | "commit 9758f8c ... 8 arquivos, todos nos touches, sem push" | **CONFIRMADA**: 8/8 nos três glob sets do bloco `touches`, `branch-r-contains=EMPTY`, pai = ponta de develop | comandos #1 e #6/#7 |

## Findings

Nenhum achado de **produto** que impeça o verdict (0 BLOCKER, 0 MAJOR — 559/559 no gate da QA e os 5 mutantes caem exatamente onde cada AC diz). Achados e notas:

| # | Severidade | Onde | O que | Evidência |
| --- | --- | --- | --- | --- |
| 1 | NOTA (pré-existente, plataforma) | WSL / `docs/sdd/_tools/sdd-test.sh` | `wsl -e bash -c "command -v dotnet"` → **vazio** (sondado ao vivo na baseline): a suite **não roda em Linux nesta máquina** e o gêmeo `.sh` do gate quebra sem ele (memória banc, backlog S-11/S-12; instalação exige gate humano) — Git Bash tem `dotnet`, mas é o MESMO kernel Windows: não é Linux | `QA-0050-git-baseline.txt` + `build-run-test.md:42` |
| 2 | NOTA (boundary do desenho, in-spec) | `RemoteMediaClient.cs:98`, `HttpFederatedActorClient.cs:210`, `HttpPeerSender.cs:106` | a confiança é **POR NOME e antes da resolução**: host nomeado é admitido sem julgar os endereços que ele resolver (rebind de DNS dentro de um host nomeado passa) — exatamente o que SPEC-0011 §C descreve ("a named host is trusted as it resolves"; em produção o valor é gate humano). Risco: baixo e aceito pela assumpção vigente | leitura do código + F2 (que usa `named.test` → `192.168.1.9` de propósito) |
| 3 | NOTA (fail-closed, direção segura) | `TrustedPeerHosts.cs:43-64` | entrada de config com **porta** (`127.0.0.1:8080`) nunca casa — `Contains` recebe o host sem porta e compara host exato → alvo fica recusado, nunca aceito. O doc XML da classe manda host/IP sem porta | leitura do código |
| 4 | NOTA (atalho pré-existente, fora do alcance) | `RemoteMediaClient.cs:23-25` | o `shortcut: validation without pinning` (família GAP-26, o mesmo do unfurl) permanece — não mudou no commit; nenhum AC desta task pede pinning | diff do commit (só o release foi acrescentado) |
| 5 | NOTA (boundary da AC-03) | `TrustedPeerHostsTests.cs:141-184` | F5 prova o unfurl com o **alvo igual ao host nomeado** (`127.0.0.1` nomeado → unfurl do 127.0.0.1 recusado), a forma mais estrita do vazamento: se a lista vazasse para o unfurl, F5 ficaria vermelha. A faixa 192.168.x do unfurl roda na suite Unfurl existente (verde no gate) e a composição fecha pelo grep 0 de `TrustedPeerHosts` em `LinkUnfurler.cs` | comandos #3/#4 + grep |

## Não coberto / unverified

- **Suite de testes em Linux (WSL)**: `dotnet` ausente no WSL (sondado ao vivo na baseline: `wsl-dotnet=ABSENT`; instalação exige gate humano) e o gêmeo `sdd-test.sh` quebra sem ele (S-11/S-12) → **unverified**, nunca assumido igual ao Windows. O gêmeo `.sh` **estático** do escopo roda — comando #7 sob WSL. O Git Bash tem `dotnet`, mas no mesmo kernel Windows: não acrescenta plataforma. Risco: baixo-médio (o CI ubuntu já rodou a suite em janela anterior, mas não nesta árvore).
- **Forma `appsettings.json` da chave**: os cenários leem a chave via `AddInMemoryCollection` (F1..F4) e via argumento de linha de comando no host real (F5) — o provedor JSON de arquivo não é exercitado. Risco baixo (a mesma abstração `IConfiguration` e a mesma chave).
- **Container/compose (podman)**: nenhum AC desta task nomeia o DoD de container → não executado. Risco baixo.
- **Harness de dois silos real (TASK-0047), runbook (0048) e interop Mastodon/Friendica (0049)**: tarefas futuras desta fase — os cenários desta QA usam o par sintético em loopback por desenho, que é o que 0047/0048 vão nomear na lista.
- **DNS real / rebind**: todos os cenários usam `FakeUnfurlResolver` — resolução real de nomes não é exercitada; o perímetro decide sobre o que o resolver devolve (nota 2 cobre o boundary de desenho).
- **FR-040 / rate limit / TTL do ator**: comportamentos de vizinhança já provados na QA-0044/QA-0045 e no gate (559/559), fora da lista de AC desta task — não re-executados isoladamente aqui.

## Reproduzir

```powershell
# pré-requisito: .NET 10 SDK + PowerShell no Windows, repo C:\dev\squid-squad-net\squid-execution
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~TrustedPeerHostsTests   # esperado: exit 0, Aprovado 5, Com falha 0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0050.ps1 -VerifyOnly                                       # esperado: exit 0, VERIFY total=5 bad=0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0050.ps1                                                   # run fresco: exit 0, SUMMARY cycles=5 failed=0 (5 vermelhos de proposito)
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test                                                     # gate: exit 0, Com falha 0, Aprovado 559
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-scope-task-0050.ps1                                                    # esperado: exit 0, fora-dos-touches=0
wsl -e bash docs/sdd/qa/qa-scope-task-0050.sh                                                                                             # esperado: exit 0, fora-dos-touches=0
```

## Artefatos desta QA

| Arquivo | Papel |
| --- | --- |
| `docs/sdd/qa/QA-0050-git-baseline.txt` | baseline antes de qualquer escrita: HEAD `9758f8c`, branch, status, arquivos do commit, não-push, sondas de plataforma (WSL/Git Bash/dotnet) |
| `docs/sdd/qa/qa-baseline-task-0050.ps1` | script que grava a baseline (é também o âncora de mtime do S-23) |
| `docs/sdd/qa/qa-mutate-task-0050.ps1` | driver de mutação can-fail (5 ciclos: AC-01 ×2, AC-02 ×4 contagens, AC-03 ×2 contagens; `-VerifyOnly` com checagem ASCII, S-16/S-22/S-23 embutidos, `RED-AC` = qual cena da AC caiu) |
| `docs/sdd/qa/QA-0050-verify.log` | `-VerifyOnly` gravado (5 padrões `hits=1` + ASCII OK + `VERIFY total=5 bad=0`) |
| `docs/sdd/qa/QA-0050-mutations.log` | ledger final: PRE (hash = blob do commit), vermelhos no log com RED-AC, restore, GREEN, UNTRACKED, SUMMARY |
| `docs/sdd/qa/qa-scope-task-0050.ps1` | varredura de escopo Windows: commit vs touches, classificação do `git status`, resíduo, blob-identity, mtime |
| `docs/sdd/qa/QA-0050-scope.log` | saída da varredura (8/8 touches, blob 5/5, mtime 5, residue 0, fora-dos-touches 0) |
| `docs/sdd/qa/qa-scope-task-0050.sh` | gêmeo bash da mesma varredura |
| `docs/sdd/qa/QA-0050-scope-sh.log` | saída do gêmeo sob WSL (mesmas contagens, fails=0) |
| `backend/` | **intocado pela QA** — `git status -- backend` = 0 entradas (a QA não acrescentou teste: as cenas das ACs são as do DEV) |
| `docs/sdd/qa/QA-0050-pares-confiaveis-ssrf.md` | este documento |
