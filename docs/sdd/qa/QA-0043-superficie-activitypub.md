<!--
templateVersion: 1.0.0
 -->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: RASCUNHO
- owner: qa
- lastVerified: (últimos artefatos: docs/sdd/runs/test-runs/20261113-203002-test-filter.log [GREEN] e o gate da Comandos #6)

# QA-0043 — Superfície ActivityPub: ator completo, coleções, objeto, contêiner (TASK-0043)

- runId: R-20261113-01 · attempt: **1/2** (bloco de controle `qaAttempts: 0/2` — contador é do ODEV, QA não escreve nele)
- QA: subagente qa-task + política ponytail-qa (full) — testes EXECUTADOS nesta sessão, não escritos e deixados
- leitura normativa: SPEC-0011 **§D (superfície exposta, fecha S-26)** + AC-01..AC-07 do task file + contrato `contracts/activitypub.md` **§1, §1.1, §1.2 e §6** + memória banc rota `qa-integracao` (`build-run-test.md` S-22/S-23, `contracts.md:21`)
- commit sob verificação: `a658bd1` em `task/TASK-0043-superficie-activitypub` (**11 arquivos = 9 produto nos touches + 1 teste + task file com Log/Evidence do dispatch; NENHUM fora**); baseline DEV `511/511`
- entrega da QA dentro dos touches: `backend/tests/Squid.Tests/Federation/QaTask0043AcceptTests.cs` (1 cenário novo — leia a nota 5 das findings: a AC-06 no caminho federado não tinha teste)
- natureza desta QA: **PRODUTO + TESTES** — as behaviours das AC-01..AC-06 provadas por **16 mutações can-fail contra o PRODUTO**, restauradas byte-exact, + gate completo (AC-07)
- verdict: **PASSED**

## AC → cenário (7 ACs; tudo executado nesta sessão)

| AC | Cenário | Execução (destaque) | Resultado |
| --- | --- | --- | --- |
| AC-01 | `/users/{name}` entrega o documento do ator do contrato §1.1: `publicKey` **objeto único** `#main-key`, `squid:ed25519Key`, `inbox`/`outbox`/`followers`/`following` | verde no baseline pré-escrita `20261113-194654-test-filter.log` (**exit 0, 8/8**) e no GREEN. Can-fail: **T1** (`following` publicado como `folloving` → `202332`, **falha=1**, RED-AC `The_actor_document_carries_both_keys_and_the_four_endpoints`), **T2** (`#main-key` virar `#rsa-key` → `202355`, **falha=1**, mesma RED-AC = o `keyId` do contrato é contrato, não escolha) | PASS |
| AC-02 | `/outbox`, `/followers`, `/following` devolvem `OrderedCollection` com `first`/`next` e **nunca** o conjunto inteiro; `?page=N` atrás dos links | Can-fail nas TRÊS metades: **T3** (`first` sem `?page=1` → `202419`, **falha=2** — a mesma serialização serve a coleção do contêiner), **T4** (`next` apontando `page=9` → `202443`, **falha=1**), **T5** (`OrderedCollection` → `Collection` → `202508`, **falha=2**). Verde: coleção sem `orderedItems`, página 1 com `partOf` + itens, 404 de ator desconhecido, `page=abc` → 400, `page=999` → página vazia | PASS |
| AC-03 | `/objects/{id}`, `/c/{slug}`, `/c/{slug}/outbox` (primeira página) e `/c/{slug}/members` (visibilidade) em JSON-LD com `@context` + `squid:kind`/`squid:slug`/`squid:policy` | Can-fail nas QUATRO metades: **T6** (`Note` → `Article` no objeto → `202532`, **falha=1**), **T7** (`squid:kind` do `Group` trocado por literal → `202555`, **falha=1**), **T8** (coleção do contêiner publicada sem contêiner → `202619`, **falha=1**, RED-AC `The_container_outbox_is_paged_...`), **T9** (filtro `Active` virar `!= Left` → `202644`, **falha=1** = o `Banned` volta a federar). Verde: membros antes do ban = os 2 ativos, depois do ban = só o dono, contêiner `membersOnly` → 404 nas 3 rotas | PASS |
| AC-04 | `/.well-known/nodeinfo` entrega metadados de software/versão | Verde: `version 2.0`, `software.name/version` não vazios, `protocols` contém `activitypub`. Can-fail: **T10** (`activitypub` → `ap` → `202709`, **falha=1**, RED-AC `Nodeinfo_reports_the_software_and_its_version`) | PASS |
| AC-05 | `POST /users/{name}/inbox` e `POST /c/{slug}/inbox` existem e respondem `401` sem assinatura verificável, sem processar o corpo (estado intacto) | Can-fail nas TRÊS metades: **T11** (recusa sem header virar `true` → `202734`, **falha=1** = aceitação silenciosa de anônimo), **T12** (status `401` → `400` → `202758`, **falha=1**), **T13** (assinatura local que confere recusada → `202824`, **falha=1**, RED-AC `A_locally_verifiable_...` = a metade 202). Verde: corpo idêntico em todas as recusas, sem `traceId`, membros do contêiner e outbox do ator intactos, assinatura local → **202** aplicando nada | PASS |
| AC-06 | Resposta exige/aceita `Accept: application/activity+json` e `application/ld+json; profile="https://www.w3.org/ns/activitystreams"`; `webfinger` ganha `self` ao ator canônico e **não** ganha `rel=profile` | Can-fail nas TRÊS metades: **T14** (`application/ld+json` recusado no `AcceptNegotiation` → `202849`, **falha=2** — as duas cenas AC-06 caem juntas: perfil e rota federada), **T15** (link `rel=profile` injetado no `webfinger` → `202913`, **falha=1** = o `DoesNotContain` do P-08), **T16** (negociação das rotas federadas desligada em `FederationEndpoints.Negotiate` → `202937`, **falha=1**, RED-AC `The_federation_routes_negotiate_the_accept_before_any_lookup` = o teste novo da QA). Verde: os dois tipos → 200 `application/activity+json` no perfil **e** na coleção federada, `text/html` → 406 **antes do lookup** (ator existente e inexistente), `self.href` = ator canônico | PASS |
| AC-07 | gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` exit 0; nenhum arquivo alterado fora dos `touches` | **Lido DENTRO do log** `20261113-203640-test-gate.log` (S-22): cabeçalho `exit: 0` + `Com falha: 0, Aprovado: 512, Ignorado: 0, Total: 512` — reproduzido de forma independente do gate do DEV (511/511 + o teste novo da QA). Escopo (`QA-0043-scope.log`): blob-identity `git hash-object` = `git rev-parse HEAD:` em **7/7** arquivos de produto mutados; mtime desde a baseline = **exatamente os 8 esperados** (7 mutados/restaurados + o teste novo); resíduo de tokens mutantes = 0; `git status -- backend` = **1**, e a 1 entrada é o teste novo dentro dos `touches`; `fora-dos-touches=0` (1 WIP do ODEV já na baseline + 11 artefatos desta QA — 10 em `docs/sdd/qa/` e o teste novo nos touches; varredura final fechada depois dos checkers). Commit `a658bd1`: 11/11 arquivos dentro dos `touches` (checker `COMMIT-TOUCHES`) | PASS |

## Comandos executados (todos nesta sessão, com exit real; datas vêm dos próprios logs)

| # | comando | exit | artefato / saída |
| --- | --- | --- | --- |
| 1 | `git rev-parse HEAD` + `--abbrev-ref` + `status --porcelain -uall` + `show --stat a658bd1` + `show --name-only a658bd1` + probes `wsl -e bash -c "command -v dotnet"` e `command -v git` (pwsh) | 0 | `docs/sdd/qa/QA-0043-git-baseline.txt` — HEAD `a658bd1`, branch `task/TASK-0043-superficie-activitypub`, 1 WIP do ODEV, **11 arquivos do commit todos nos touches**, **dotnet ausente no WSL** (git = `/usr/sbin/git`) |
| 2 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~Squid.Tests.Federation.ActivityPubSurfaceTests"` (baseline da QA, antes de qualquer escrita) | 0 | `20261113-194654-test-filter.log` — `exit: 0`, Com falha 0 / Aprovado 8 / Total 8 (5s) |
| 3 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0043.ps1 -VerifyOnly` (depois de T16 entrar na tabela) | 0 | `docs/sdd/qa/QA-0043-verify.log` — 16 padrões `hits=1`, `non-ascii-bytes=0 ok=true`, `VERIFY total=16 bad=0` |
| 4 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~Squid.Tests.Federation"` (prova de verde do teste novo da QA) | 0 | `20261113-201417-test-filter.log` — `exit: 0`, Com falha 0 / Aprovado 9 / Total 9 (6s) |
| 5 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0043.ps1` (run final: 16 ciclos + GREEN) | 0 | `docs/sdd/qa/QA-0043-mutations.log` — 16 vermelhos com RED-AC da AC alvo em cada um (T3, T5 e T14 com falha=2 — cenas que dividem a serialização/aceitação), 16× `RESTORE-OK hash=PRE residue=0`, `GREEN wrapper=0 logExit=0 aprovado=9 falha=0` (`203002`), `UNTRACKED backend=1` (o teste da QA), `SUMMARY cycles=16 failed=0 residue-backend=0 temp-backups=0 elapsed-s=413` |
| 6 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` (gate AC-07, suite inteira com BuildProof) | 0 | `20261113-203640-test-gate.log` — `exit: 0` lido **dentro do log** (S-22) + `Com falha: 0, Aprovado: 512, Ignorado: 0, Total: 512`, 6m3s |
| 7 | varredura de escopo (pwsh): blob-identity vs `rev-parse HEAD:`, mtime desde a baseline, resíduo de tokens mutantes, diff contra a baseline do git | 0 | `docs/sdd/qa/QA-0043-scope.log` — blob 7/7 `match=True`, `mtime-count=8 (esperado 8)`, `residue=0`, `git-backend-entries=1` (só o teste da QA), `pre-existing-odev-wip=1`, `qa-artifacts=12`, `fora-dos-touches=0` (varredura final, fechada depois dos checkers) |
| 8 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0043.ps1` | 0 | `docs/sdd/qa/QA-0043-check.log` — SUMMARY total=50 fails=0 (inclui `sdd-state check`: OK, log 10, evidence 7) |
| 9 | `wsl -e bash docs/sdd/qa/qa-check-task-0043.sh` | 0 | `docs/sdd/qa/QA-0043-check-sh.log` — SUMMARY total=33 fails=0 (gêmeo Linux, primeira execução já verde: o parser nasceu com `tr -d '\r'`, a armadilha da QA-0042) |

As linhas `## Log` e `## Evidence` destas execuções **não** foram gravadas pela QA: o dispatch devolve `logLines`/`evidenceRows` sem relógio para o ODEV carimbar (o relógio é dele). Esta tabela acima é a prova; a task file permanece intacta pela QA.

## Ciclos de mutação (can-fail; S-16 hash PRE = blob do commit, S-22 classificação no log, S-23 restore byte-exact + mtime)

Todos nos arquivos de PRODUTO que a behaviour lê (`Squid.Protocols/ActivityPub/*.cs`, `Squid.Api/Federation/*.cs`, `Squid.Api/Discovery/DiscoveryEndpoints.cs`, `Squid.Service/InboxSignatureGate.cs`) — nenhum edit fora do escopo, e os 7 arquivos finais são byte-iguais ao `HEAD` (blob-identity 7/7; `git status -- backend -uno` = 0).

| Ciclo | Arquivo | O que prova (AC) | Vermelho classificado NO LOG |
| --- | --- | --- | --- |
| T1 | ProfileJsonLd.cs | AC-01: o ator publica `following` (S-26 fecha aqui) | `The_actor_document_carries...` (`202332`, falha=1) |
| T2 | ProfileJsonLd.cs | AC-01: `publicKey.id` termina em `#main-key` (contrato §1.1) | `The_actor_document_carries...` (`202355`, falha=1) |
| T3 | FederationEndpoints.cs | AC-02: coleção publica `first = ?page=1` | `The_three_actor_collections...` + `The_container_outbox...` (`202419`, falha=2) |
| T4 | FederationEndpoints.cs | AC-02: coleção publica `next = ?page=2` | `The_three_actor_collections...` (`202443`, falha=1) |
| T5 | CollectionJsonLd.cs | AC-02: o tipo é `OrderedCollection` | `The_three_actor_collections...` + `The_container_outbox...` (`202508`, falha=2) |
| T6 | ObjectJsonLd.cs | AC-03: o objeto responde como `Note` | `The_object_and_the_container...` (`202532`, falha=1) |
| T7 | ObjectJsonLd.cs | AC-03: o `Group` publica o `squid:kind` do contêiner | `The_object_and_the_container...` (`202555`, falha=1) |
| T8 | FederationEndpoints.cs | AC-03: a coleção do contêiner carrega os três `squid:` | `The_container_outbox_is_paged...` (`202619`, falha=1) |
| T9 | FederationEndpoints.cs | AC-03: `/members` só publica `Active` (visibilidade) | `The_container_outbox_is_paged...` (`202644`, falha=1) |
| T10 | FederationEndpoints.cs | AC-04: nodeinfo reporta o protocolo `activitypub` | `Nodeinfo_reports...` (`202709`, falha=1) |
| T11 | InboxSignatureGate.cs | AC-05: sem header de assinatura a recusa acontece (401, corpo não lido) | `Both_inboxes_refuse...` (`202734`, falha=1) |
| T12 | FederationEndpoints.cs | AC-05: o status da recusa é `401` | `Both_inboxes_refuse...` (`202758`, falha=1) |
| T13 | InboxSignatureGate.cs | AC-05: assinatura local verificável → `202` aplicando nada | `A_locally_verifiable...` (`202824`, falha=1) |
| T14 | AcceptNegotiation.cs | AC-06: `application/ld+json; profile` é aceito (perfil **e** rotas federadas) | `The_profile_negotiates...` + `The_federation_routes_negotiate...` (`202849`, falha=2) |
| T15 | DiscoveryEndpoints.cs | AC-06: `webfinger` **não** publica `rel=profile` (P-08) | `The_profile_negotiates...` (`202913`, falha=1) |
| T16 | FederationEndpoints.cs | AC-06: toda rota federada negocia o `Accept` **antes** do lookup (406) | `The_federation_routes_negotiate...` (`202937`, falha=1) |

16× `PRE` com `head-match=true` (o hash de trabalho é o blob do commit `a658bd1`), 16× `RESTORE-OK hash=PRE residue=0`, 0× `RESTORE-FAIL`, 1× GREEN `9/9 exit 0` (`203002`), resíduo final de tokens mutantes = 0, backups `/tmp|TEMP` `qa0043-*` = 0, `-VerifyOnly` `bad=0` + ASCII OK (`QA-0043-verify.log`), 16× `EXPECT-OK`, 0× `EXPECT-MISS`, 0× `TOOLING-FAIL`, 0× `BUILD-FAIL`, wrapper **1** em todos os 16 (o log e o wrapper concordaram — nenhuma leitura de ambiente errada nesta vez).

## Afirmações do DEV a verificar (declarações de nível spec)

| # | Afirmação (Log do task file / commit a658bd1) | Julgamento desta QA | Evidência |
| --- | --- | --- | --- |
| 1 | "documento de ator com inbox/outbox/followers/following (fecha S-26), 10 rotas novas" | **CONFIRMADA em execução**: as 10 rotas existem (`FederationEndpoints.cs:43-56` + `DiscoveryEndpoints.cs:36-37`) e cada AC bateu no próprio cenário; T1 derruba exatamente o link que a lacuna S-26 abria | ciclos T1/T2 + cenários AC-01..AC-06 |
| 2 | "JSON-LD de colecao, objeto e contener em Squid.Protocols costurado por 5 seams" | **CONFIRMADA em execução e leitura**: `FederationSeams` declara os 5 delegates (`FederationSeams.cs:28-33`) e T5/T6/T7/T8 derrubam, um a um, coleção, objeto, `Group` e campos do contêiner — todos em `Squid.Protocols`, nenhum JSON-LD no domínio (ADR-11) | ciclos T5..T8 + leitura |
| 3 | "gate da verificacao de entrada (contrato 3 passo 1) em Squid.Service que recusa 401 antes de ler o corpo, responde 202 quando a assinatura local verifica e aplica nada" | **CONFIRMADA em execução**: T11 mostra a recusa nascendo no passo 1 (sem header → falso), T12 o status 401, T13 a aceitação 202 — e o cenário verde prova estado intacto (membros e outbox não mudam, corpo idêntico e sem `traceId`) | ciclos T11/T12/T13 + cenário AC-05 |
| 4 | "Accept aceita os dois tipos da secao 6, wildcard e header ausente, e recusa o resto com 406" | **CONFIRMADA em execução** para os dois tipos (perfil **e** rota federada, ciclo T14) e para o header ausente (todo request do cliente de teste vai sem `Accept` e é servido); **CONFIRMADA por leitura** para wildcard (`AcceptNegotiation.cs:52-54`) — `*/*` não tem cenário, ver "não coberto"; `text/html` → 406 verde nos dois caminhos | ciclos T14/T16 + cenários AC-06 + leitura |
| 5 | "colecao entrega first/next sem itens e os itens ficam em ?page=N; squid:kind/squid:slug/squid:policy tambem nas colecoes do contener" | **CONFIRMADA em execução**: T3/T4/T5 derrubam `first`/`next`/tipo e T8 derruba os `squid:` da coleção; verde: coleção sem `orderedItems`, página com `partOf`+itens e os três campos no contêiner | ciclos T3/T4/T5/T8 + cenários AC-02/AC-03 |
| 6 | "gate sdd-test test exit 0 com 511/511 + git status sem nenhum arquivo fora dos touches" | **CONFIRMADA de forma independente**: gate re-executado pela QA = 512/512 exit 0 lido no log (511 do DEV + o teste novo desta QA); commit 11/11 nos touches; árvore final byte-igual (blob 7/7, `git status -- backend -uno` = 0, `fora-dos-touches=0`) | Comandos #6 + `QA-0043-scope.log` + checkers |

## Findings

Nenhum achado de **produto** (0 BLOCKER, 0 MAJOR, 0 MINOR — 512/512 verdes e os 16 mutantes caem exatamente onde cada AC diz). Notas de QA/fluxo:

| # | Severidade | Onde | O que | Evidência |
| --- | --- | --- | --- | --- |
| 1 | NOTA (pré-existente) | `docs/sdd/_tools/sdd-test.sh` / WSL | gêmeo Linux não roda a suite: `wsl -e bash -c "command -v dotnet"` → vazio, exit 1 (**sondado ao vivo NESTA sessão**, na baseline); instalação exige gate humano — mesmo achado das QA-0033..0042 | Comando 1 + linha do Log |
| 2 | NOTA (DIVERGENCIA, já registrada pelo ODEV) | `docs/memory-bank/contracts.md:21` | ainda diz que `inbox`/`outbox`/`followers`/`following` ficam sem handler até P-06/P-08; a TASK-0043 já entrega os quatro (`rel=profile` continua P-08) — código vence, correção do memory bank na consolidação da fase | leitura `contracts.md:21` + ciclos T1/T3 |
| 3 | NOTA (boundary da AC-06) | `FederationEndpoints.cs:213-230` (nodeinfo) | `/.well-known/nodeinfo` responde `application/json` **sem** gate de Accept: é documento de descoberta do `/.well-known` (cliente nodeinfo não fala ActivityPub), não um documento AP do contrato §6 — leitura da AC-06 como aplicável aos documentos AP; se ODEV ler a AC ao pé da letra, isto vira dúvida de fluxo, não defeito provado | leitura do handler + cenário AC-04 verde |
| 4 | NOTA (boundary da AC-02) | `FederationEndpoints.cs:81-92` | as páginas de `followers`/`following` nascem vazias por construção (as linhas de follow são da TASK-0044) — a AC-02 pede o formato paginado, não o conteúdo, e o cenário prova `Assert.Empty` no lugar certo | cenário AC-02 + leitura |
| 5 | NOTA (decisão da QA, dentro dos touches) | `backend/tests/Squid.Tests/Federation/QaTask0043AcceptTests.cs` | a AC-06 diz "a resposta exige/aceita", mas o teste do DEV cobria a negociação só no perfil (`/users/{name}`) — as 10 rotas novas da task não tinham teste de `Accept`. A QA escreveu **1 cenário novo** (406 antes do lookup, os dois tipos, 404 da escada) e o provou com o ciclo **T16** (desligar `Negotiate` em `FederationEndpoints` derruba só ele). Arquivo novo dentro do glob `backend/tests/Squid.Tests/**` do touches; por isso o gate fecha 512 e não 511 | Comandos #4 + ciclo T16 |
| 6 | NOTA (ferramenta da QA) | `qa-mutate-task-0043.ps1` | driver corrigido 2× durante a sessão: (a) 1ª execução abortou **antes do T1** (`git rev-parse HEAD:` não aceita pathspec com `\` e o `2>&1` sob EAP Stop virou ErrorRecord terminal — **nenhum produto tocado**, árvore verificada limpa); (b) a checagem de resíduo contava o teste novo da QA como resíduo (`SUMMARY failed=1` falso-positivo) → resíduo agora é só `--porcelain -uno` (modificados rastreados) e o não-rastreado vira linha própria `UNTRACKED backend=1`. Rótulo cosmetico restante: a linha `RUN ... total=` do ledger imprime o valor de **Ignorado** (grupo errado do parse) — `falha`/`aprovado`, que é o que os checkers validam, corretos | ledger final + este relato |

## Não coberto / unverified

- **Suite de testes em Linux (WSL)**: `dotnet` ausente no WSL (sondado ao vivo nesta sessão: `command -v dotnet` → exit 1; instalação exige gate humano) e o gêmeo `sdd-test.sh` quebra sem ele → **unverified**, nunca assumido igual ao Windows. O gêmeo `.sh` do **checker** (parte estática: logs, resumos, hashes, git) roda — Comando 9.
- **`Accept: */*` (wildcard)**: aceito pelo código (`AcceptNegotiation.cs:52-54`) mas sem cenário de teste — não é uma AC (a AC-06 nomeia os dois tipos + `webfinger`); o header ausente está coberto por todo request do cliente de teste. Risco: baixo.
- **Conteúdo real das coleções `followers`/`following`**: as linhas de follow são da TASK-0044 — só o formato foi provado aqui. Risco: baixo (a shape não pode derivar, é justamente o que T3/T4/T5 protegem).
- **Contrato §3 passos 2–7 no inbox** (tamanho, profundidade, rate limit, dedupe, remetente remoto): fora das AC desta task (TASK-0044/0046) — a AC-05 cobre o passo 1 e a integridade de estado.
- **Interop com Mastodon/Friendica de verdade**: TASK-0049 (gate humano, SC-016). Risco: médio.

## Reproduzir

```powershell
# pré-requisito: .NET 10 SDK + PowerShell no Windows, repo C:\dev\squid-squad-net\squid-execution
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~Squid.Tests.Federation"   # esperado: exit 0, Aprovado 9, Com falha 0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0043.ps1 -VerifyOnly   # esperado: exit 0, VERIFY total=16 bad=0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0043.ps1              # run fresco: exit 0, SUMMARY cycles=16 failed=0 (16 vermelhos de proposito)
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0043.ps1                # esperado: exit 0, SUMMARY fails=0
wsl -e bash docs/sdd/qa/qa-check-task-0043.sh                                                          # esperado: exit 0, SUMMARY fails=0
```

## Artefatos desta QA

| Arquivo | Papel |
| --- | --- |
| `docs/sdd/qa/QA-0043-git-baseline.txt` | baseline antes de qualquer escrita: HEAD `a658bd1`, branch, status, arquivos do commit, probes do WSL |
| `docs/sdd/qa/qa-mutate-task-0043.ps1` | driver de mutação can-fail (16 ciclos: AC-01 ×2, AC-02 ×3, AC-03 ×4, AC-04 ×1, AC-05 ×3, AC-06 ×3; `-VerifyOnly` com checagem ASCII, S-16/S-22/S-23 embutidos, `RED-AC` = qual cenário da AC caiu) |
| `docs/sdd/qa/QA-0043-mutations.log` | ledger: PRE (hash = blob do commit), vermelhos no log com RED-AC, restore, GREEN, UNTRACKED, SUMMARY |
| `docs/sdd/qa/QA-0043-verify.log` | `-VerifyOnly` gravado (16 padrões `hits=1` + `ASCII OK` + `VERIFY total=16 bad=0`) |
| `docs/sdd/qa/QA-0043-scope.log` | varredura de escopo: blob-identity 7/7, mtime 8, resíduo 0, classificação do `git status` |
| `docs/sdd/qa/qa-check-task-0043.ps1` | checker estático Windows (inclui `sdd-state check` da task file) |
| `docs/sdd/qa/qa-check-task-0043.sh` | gêmeo Linux do mesmo checker (sem o `sdd-state`, ferramenta Windows) |
| `backend/tests/Squid.Tests/Federation/QaTask0043AcceptTests.cs` | cenário AC-06 no caminho federado (nota 5), provado pelo ciclo T16 |
| `docs/sdd/qa/QA-0043-superficie-activitypub.md` | este documento |
