<!--
templateVersion: 1.0.0
 -->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: RASCUNHO
- owner: qa
- lastVerified: (último artefato: docs/sdd/runs/test-runs/20261113-162146-test-gate.log)

# QA-0041 — Domínio canônico e proxy (TASK-0041)

- runId: R-20261113-01 · attempt: **1/2** (bloco de controle `qaAttempts: 0/2` — contador é do ODEV, QA não escreve nele)
- QA: subagente qa-task + política ponytail-qa (full) — testes EXECUTADOS nesta sessão, não escritos e deixados
- leitura normativa: SPEC-0011 **§A inteiro** (I-11, decisão 4 + cláusula D-01 + nota do Activity `urn:`) + AC-01..AC-05 do task file (com o esclarecimento do ODEV 2026-11-13 15:07:04 na AC-02) + SPEC-0007 B-09a via `contracts.md` + memória banc rota `qa-integracao` (`index.md:50` → `build-run-test.md`: S-22/S-23 relidos)
- commit sob verificação: `9210469` em `task/TASK-0041-dominio-canonico-e-proxy` (**7 arquivos = 6 produto nos touches + 1 teste**; NENHUM fora); baseline DEV `480/480 → 484/484`
- natureza desta QA: **PRODUTO + TESTES** — a behaviour nova (origem canônico única, `Instance:Domain` opt-in, `ForwardedHeaders` só com chave) é provada por mutações can-fail contra o PRODUTO, restauradas byte-exact
- verdict: **PASSED**

## AC → cenário (5 ACs; todos executados nesta sessão)

| AC | Cenário | Execução (destaque) | Resultado |
| --- | --- | --- | --- |
| AC-01 | sem `Instance:Domain` e sem proxy: os quatro pontos (Discovery, Posts, Containers, Threads) continuam scheme+Host da request, byte-idêntico | verde no baseline `20261113-153601-test-filter.log` (**exit 0, 4/4**). Can-fail: **M1** (fallback força https → `160513`, **falha=2**, RED-AC `Ac01_without...` + `Ac03_forwarded...` — a string do profile id muda de esquema, `Assert.Equal Strings differ pos 4`), **M4a** (webfinger só aceita o canônico → sem config TODO webfinger vira 404 → `160720`, **falha=3**, RED-AC inclui `Ac01_without...`, `Expected: OK`). Os pontos M8..M11 **não** derrubam a AC-01 (bypass byte-idêntico sem config) — é a prova de que o helper é o caminho único sem comportamento novo | PASS |
| AC-02 | com `Instance:Domain: squid.test`, IDs canônicos (ator, objeto, `webfinger.subject`, `links.self`) usam `https://squid.test` mesmo com `Host: localhost:5001`; Activity `urn:` fora de escopo (esclarecimento ODEV) | verde no baseline e no GREEN `161344` (4/4). Can-fail: **M2** (chave ignorada → `160558`, `Ac02...`), **M3** (subject ecoa o domínio pedido → `160643`, pos 18 = a arroba), **M4b** (domínio canônico recusado → `160804`, `Expected: OK` = 404), **M8** Discovery (`161053`), **M9** Posts (`161136`, RED-DETAIL `StartsWith "http://localhost:5001/o/..."`), **M10** Containers (`161218`), **M11** Threads (`161301`, `"http://localhost:5001/t/..."`) — **cada um dos quatro pontos cai sozinho** na própria AC; todos `EXPECT-OK`, restore 7/7 | PASS |
| AC-03 | `X-Forwarded-Proto/Host` ignorados por padrão e honrados só com `Instance:ForwardedHeaders` | Can-fail nas DUAS metades: **M5** (middleware instala SEM a chave → `160843`, só `Ac03...`, `Strings differ pos 4` = o Host forjado virou origem), **M6** (middleware NUNCA instala mesmo COM a chave → `160926`, só `Ac03...`). Verde nos dois momentos sem a mutação | PASS |
| AC-04 | nenhuma resposta de erro expõe o domínio configurado fora do formato já coberto (404 idêntico, sem stack trace — FR-053) | Can-fail: **M7** (o 404 do webfinger passa a nomear a origem → `161010`, **falha=1, SÓ a AC-04**, RED-DETAIL `Strings differ pos 22` — o corpo deixa de ser byte-idêntico e carrega `squid.test`). O teste compara plain × configurado byte-a-byte e varre `squid.test`/`stack`/`System.`/`   at ` nos dois corpos | PASS |
| AC-05 | gate `sdd-test.ps1 test` exit 0; nenhum arquivo alterado fora dos `touches` | **Lido DENTRO do log** `20261113-162146-test-gate.log` (S-22): cabeçalho `exit: 0` + `Com falha: 0, Aprovado: 484, Ignorado: 0, Total: 484` (6m1s). Escopo (`QA-0041-scope.log`): blob-identity `git hash-object` = `git rev-parse HEAD:` em **6/6** arquivos mutados; mtime desde a baseline = **exatamente esses 6**; resíduo `QA-MUTATION` = 0; `git status -- backend` = **0**; `fora-dos-touches` = **0** (as 11 entradas de docs são o WIP do ODEV já presente na baseline das 15:41, inalterado; os 7 artefatos novos são desta QA em `docs/sdd/qa/`) | PASS |

## O esclarecimento da AC-02 (ODEV 2026-11-13 15:07:04) aplicado como está

O `notDone` do DEV está correto e a AC-02 foi testada **como escrita**: o id do log interno de `Activity` continua `urn:squid:activity:{token}` (design P-04/GAP-23) e a URL canônica de atividade federada é das TASK-0044/0045 (cláusula SPEC-0011 §A). A QA **não** cobre `activity.id` como URL canônica — não é escopo do I-11. O teste lê a linha da atividade pelo `IActivityStore` e assere só `ActorId`/`ObjectId` canônicos (`CanonicalOriginConfigTests.cs:171-173`), que é exatamente o que a AC-02 pede.

## Comandos executados (todos nesta sessão, com exit real; datas vêm dos próprios logs)

| # | comando | exit | artefato / saída |
| --- | --- | --- | --- |
| 1 | `git rev-parse HEAD` + `--abbrev-ref` + `status --porcelain -uall` + `wsl -e bash -c command -v dotnet` (pwsh) | 0 | `docs/sdd/qa/QA-0041-git-baseline.txt` — HEAD `9210469`, branch `task/TASK-0041-dominio-canonico-e-proxy`, 11 entradas de WIP do ODEV, **dotnet ausente no WSL**, 7 arquivos do commit todos nos touches |
| 2 | `powershell ... docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~CanonicalOriginConfigTests` (baseline da QA) | 0 | `20261113-153601-test-filter.log` — `exit: 0`, Com falha 0 / Aprovado 4 / Total 4 |
| 3 | `powershell ... docs/sdd/qa/qa-mutate-task-0041.ps1 -VerifyOnly` | 0 | `docs/sdd/qa/QA-0041-verify.log` — 12 padrões `hits=1`, `ASCII non-ascii-bytes=0 OK`, `VERIFY total=12 bad=0` |
| 4 | `powershell ... docs/sdd/qa/qa-mutate-task-0041.ps1` (run cheio: 12 ciclos + GREEN) | 0 | `docs/sdd/qa/QA-0041-mutations.log` — 12 vermelhos com RED-AC da AC alvo em cada um, 12× `RESTORE-OK hash=PRE residue=0`, `GREEN ... aprovado=4 falha=0` (`161344`), `SUMMARY cycles=12 failed=0 residue-QA-MUTATION=0 temp-backups=0` |
| 5 | `powershell ... docs/sdd/_tools/sdd-test.ps1 test` (gate AC-05, suite inteira com BuildProof) | 0 | `20261113-162146-test-gate.log` — `exit: 0` lido **dentro do log** (S-22) + `Com falha: 0, Aprovado: 484, Total: 484`, 6m1s |
| 6 | varredura de escopo (pwsh): blob-identity vs `rev-parse HEAD:`, mtime desde a baseline, resíduo, diff contra a baseline do git | 0 | `docs/sdd/qa/QA-0041-scope.log` — blob 6/6 `match=True`, `mtime-count=6`, `residue=0`, `git-backend-entries=0`, `fora-dos-touches=0`, `pre-existing-odev-wip=11` |
| 7 | `powershell ... docs/sdd/qa/qa-check-task-0041.ps1` | 0 | `docs/sdd/qa/QA-0041-check.log` — SUMMARY total/fails (SDD-STATE `check` incluso) |
| 8 | `wsl -e bash docs/sdd/qa/qa-check-task-0041.sh` (gêmeo Linux, parte estática) | 0 | `docs/sdd/qa/QA-0041-check-sh.log` — SUMMARY total/fails (o `sdd-state` é ferramenta Windows, fica só no ps1) |

## Ciclos de mutação (can-fail; S-16 SHA256 PRE, S-22 classificação no log, S-23 restore byte-exact + mtime)

Todos nos arquivos de PRODUTO que a behaviour lê (`Squid.Api/Discovery/**`, `Squid.Api/Posts/**`, `Squid.Api/Containers/**`, `Squid.Api/Threads/**`, `Squid.Service/**`) — nenhum edit fora do escopo, e os 6 arquivos finais são byte-iguais ao `HEAD` (blob-identity 6/6). Filtro de todos os ciclos: `FullyQualifiedName~CanonicalOriginConfigTests`.

| Ciclo | Arquivo | O que prova (AC) | Vermelho classificado NO LOG |
| --- | --- | --- | --- |
| M1 | `Squid.Api/Discovery/CanonicalOrigin.cs` | AC-01: sem config o fallback tem que ser scheme+Host — forçar https muda a string de todo id publicado | `Ac03_forwarded...` + `Ac01_without...` (`160513`, **falha=2**, `Strings differ pos 4`) |
| M2 | `Squid.Api/Discovery/CanonicalOrigin.cs` | AC-02: `Instance:Domain` é a chave opt-in — morta, os ids nascem no Host pedido | `Ac02_with_instance...` (`160558`, **falha=1**) |
| M3 | `Squid.Api/Discovery/DiscoveryEndpoints.cs` | AC-02 + D-01: `subject` é sempre a conta canônica, mesmo com autoridade interna perguntando | MESMO `Ac02...` (`160643`, **falha=1**, pos 18 = `@`) |
| M4a | `Squid.Api/Discovery/DiscoveryEndpoints.cs` | AC-01/02/03 + D-01a: o Host alcançado é domínio próprio — sem canônico configurado, TODO webfinger vira 404 | `Ac03...` + `Ac01_without...` + `Ac02...` (`160720`, **falha=3**, `Expected: OK`) |
| M4b | `Squid.Api/Discovery/DiscoveryEndpoints.cs` | AC-02 + D-01b: o domínio canônico também é domínio próprio — `acct:handle@squid.test` tem que resolver | SÓ `Ac02...` (`160804`, **falha=1**, `Expected: OK` = 404 no webfinger) |
| M5 | `Squid.Service/SquidServiceHost.cs` | AC-03a: sem `Instance:ForwardedHeaders` o middleware NEM EXISTE — instalar sempre deixa o Host forjado virar origem | SÓ `Ac03...` (`160843`, **falha=1**) |
| M6 | `Squid.Service/SquidServiceHost.cs` | AC-03b: com a chave o middleware tem que instalar — nunca instalar ignora o proxy de confiança | SÓ `Ac03...` (`160926`, **falha=1**) |
| M7 | `Squid.Api/Discovery/DiscoveryEndpoints.cs` | AC-04/FR-053: o 404 não pode nomear a origem — o corpo deixa de ser idêntico e carrega o domínio | SÓ `Ac04_error_answers...` (`161010`, **falha=1**, pos 22) |
| M8 | `Squid.Api/Discovery/DiscoveryEndpoints.cs` | AC-02 ponto Discovery (ator + `links.self`): revertido ao scheme+Host o profile id volta a `localhost:5001` | SÓ `Ac02...` (`161053`, **falha=1**) |
| M9 | `Squid.Api/Posts/PostEndpoints.cs` | AC-02 ponto Posts (objeto + author + containerId + `ActorId` da atividade) | SÓ `Ac02...` (`161136`, **falha=1**, `StartsWith "http://localhost:5001/o/..."`) |
| M10 | `Squid.Api/Containers/ContainerEndpoints.cs` | AC-02 ponto Containers (coleção `/c/{slug}`) | SÓ `Ac02...` (`161218`, **falha=1**) |
| M11 | `Squid.Api/Threads/ThreadEndpoints.cs` | AC-02 ponto Threads (thread `/t/` + mensagem `/m/`) | SÓ `Ac02...` (`161301`, **falha=1**, `"http://localhost:5001/t/..."`) |

12× `PRE` registrado, 12× `RESTORE-OK hash=PRE residue=0`, 0× `RESTORE-FAIL`, 1× GREEN `4/4 exit 0` (`161344`), resíduo final `QA-MUTATION` = 0, backups TEMP `qa0041-*` = 0, `-VerifyOnly` `bad=0` + `ASCII OK` (`QA-0041-verify.log`), 12× `EXPECT-OK`, 0× `EXPECT-MISS`, 0× `CLASSIFICACAO-ERRADA`.

**Nota de coerência AC-01 × M8..M11**: reverter UM ponto ao scheme+Host (M8..M11) **não** derruba a AC-01 — sem config o bypass produz a mesma string do helper — e derruba a AC-02 naquele ponto específico. É exatamente a dupla que a AC-01/AC-02 pedem: sem config nada muda, com config tudo muda junto.

## Afirmações do DEV a verificar (declarações de nível spec)

| # | Afirmação (Log do task file / commit 9210469) | Julgamento desta QA | Evidência |
| --- | --- | --- | --- |
| 1 | "CanonicalOrigin vira a origem unica dos quatro pontos; Instance:Domain opt-in, sempre https, lido da configuracao da propria request (nao estatico)" | **CONFIRMADA em execução e leitura**: os quatro pontos com `Origin => CanonicalOrigin.Of` (checker `FOUR-POINTS` 4/4); a chave é lida por request (`CanonicalOrigin.cs:23`); M1..M11 todos caem onde a AC diz | checkers + ledger |
| 2 | "ForwardedHeaders instalado so com Instance:ForwardedHeaders" | **CONFIRMADA em execução**: M5 (instalar sempre) e M6 (nunca instalar) derrubam a AC-03 pelas duas metades | ciclos M5/M6 |
| 3 | "o webfinger aceita o dominio canonico ou o Host alcanca-do, publicando sempre o canonico" | **CONFIRMADA em execução**: M4a derruba a aceitação do Host alcançado (falha=3), M4b a do canônico (falha=1), M3 o publicar sempre o canônico (pos 18) — as três partes da cláusula D-01 têm seu próprio vermelho | ciclos M3/M4a/M4b |
| 4 | "diff restrito aos touches" (7 arquivos) | **CONFIRMADA**: commit `9210469` = 6 produto + 1 teste, todos nos `touches`; `git status -- backend` = 0 na QA inteira; `fora-dos-touches=0` | `QA-0041-git-baseline.txt` + `QA-0041-scope.log` |
| 5 | "AC-02: o notDone do DEV esta correto - id do Activity continua urn: por design P-04/GAP-23" | **CONFIRMADA como escopo**: a QA leu a cláusula e testou a AC-02 como escrita (ator, objeto, subject, links.self); `activity.id` como URL canônica NÃO é coberto aqui — é TASK-0044/0045 | leitura SPEC-0011 §A + `CanonicalOriginConfigTests.cs:171-173` |

## Findings

Nenhum achado de **produto** (0 BLOCKER, 0 MAJOR, 0 MINOR — 484/484 verdes e os 12 mutantes caem exatamente onde cada AC diz). Notas de QA/fluxo:

| # | Severidade | Onde | O que | Evidência |
| --- | --- | --- | --- | --- |
| 1 | NOTA (pré-existente) | `docs/sdd/_tools/sdd-test.sh` / WSL | gêmeo Linux não roda a suite: `wsl -e bash -c "command -v dotnet"` → `dotnet-missing` (**sondado ao vivo NESTA sessão**, na baseline); instalação exige gate humano — mesmo achado das QA-0033..0040 | comando 1 + linha do Log |
| 2 | NOTA (boundary de escopo) | AC-02 × `Activity.id` | o esclarecimento do ODEV mantém `urn:squid:activity:{token}` fora do I-11; a QA não inventa cobertura para a URL de atividade federada (TASK-0044/0045) — se o ODEV quiser isso num DoD, é nova AC | leitura SPEC-0011 §A + teste `:171-173` |
| 3 | NOTA (WIP pré-existente) | `docs/sdd/plan/plan.md` + SPEC-0011 + TASK-0042..0049 | 11 entradas de docs não commitadas pelo ODEV já estavam na baseline 15:41, **inalteradas** pela QA (mesmo conjunto) — o gate do AC-05 é sobre o produto, e o produto está 0 | `QA-0041-scope.log` (`pre-existing-odev-wip=11`) |
| 4 | NOTA (driver) | `qa-mutate-task-0041.ps1` | nasceu ASCII-only para matar de vez a armadilha do BOM (6ª recorrência nas QA-0034..0040): `-VerifyOnly` rejeita qualquer byte > 127 e os dois checkers conferem | comando 3 (`ASCII non-ascii-bytes=0 OK`) |

## Não coberto / unverified

- **Suite de testes em Linux (WSL)**: `dotnet` ausente no WSL (sondado ao vivo nesta sessão: `command -v dotnet` → `dotnet-missing`; instalação exige gate humano) e o gêmeo `sdd-test.sh` quebra sem ele → **unverified**, nunca assumido igual ao Windows. O gêmeo `.sh` do **checker** (parte estática: logs, resumos, hashes, git) foi executado — comando 8.
- **`Instance:Domain` com valor malformado** (vazio, com esquema embutido, com barra final): a AC não pede validação de formato — o helper faz `Trim()` e usa o valor como autoridade. Risco: baixo (config de operador, falha visível imediata em qualquer id publicado).
- **Comportamento atrás de um proxy REAL (Caddy)**: o AC-03 prova o contrato `X-Forwarded-*` com headers forjados no cliente de teste; a topologia com dois salos e TLS da fase (TASK-0047) não existe ainda → coberta pelas tasks da fase, não aqui.

## Reproduzir

```powershell
# pré-requisito: .NET 10 SDK + PowerShell no Windows, repo C:\dev\squid-squad-net\squid-execution
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test       # esperado: exit 0, Aprovado 484, Com falha 0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0041.ps1 -VerifyOnly   # esperado: exit 0, VERIFY total=12 bad=0 + ASCII OK
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0041.ps1     # run fresco: exit 0, SUMMARY cycles=12 failed=0 (12 vermelhos de proposito)
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0041.ps1      # esperado: exit 0, SUMMARY fails=0
wsl -e bash docs/sdd/qa/qa-check-task-0041.sh                                               # esperado: exit 0, SUMMARY fails=0
```

## Artefatos desta QA

| Arquivo | Papel |
| --- | --- |
| `docs/sdd/qa/qa-mutate-task-0041.ps1` | driver de mutação can-fail (12 ciclos: AC-01, AC-02 núcleo + 2 metades da D-01 + os 4 pontos, AC-03 2 metades, AC-04; `-VerifyOnly` com checagem ASCII, S-16/S-22/S-23 embutidos, captura de `RED-AC` = qual cenário da config caiu) |
| `docs/sdd/qa/QA-0041-mutations.log` | ledger: PRE, vermelhos no log (com RED-AC), restore, GREEN, SUMMARY |
| `docs/sdd/qa/QA-0041-verify.log` | `-VerifyOnly` gravado (12 padrões `hits=1` + `ASCII OK` + `VERIFY total=12 bad=0`) |
| `docs/sdd/qa/qa-check-task-0041.ps1` / `.sh` | checkers de evidência (estáticos; gêmeo Linux sem o sdd-state, que é ferramenta Windows) |
| `docs/sdd/qa/QA-0041-check.log` / `QA-0041-check-sh.log` | execuções dos checkers |
| `docs/sdd/qa/QA-0041-git-baseline.txt` | git + HEAD + sonda WSL capturados antes da QA escrever |
| `docs/sdd/qa/QA-0041-scope.log` | varredura final de escopo (blob-identity, mtime, resíduo, diff contra a baseline do git) |

memoryBankFlags: `[GAP: suite Linux/WSL unverified - dotnet ausente na maquina (sondado ao vivo nesta sessao), instalacao exige gate humano (pre-existente, mesmo achado das QA-0033..0040)]` · `[SUSPEITO: nao - nenhuma contradicao spec x codigo encontrada; 484/484 verdes e os 12 mutantes caem exatamente onde cada AC diz (RED-AC por ciclo, 12/12 EXPECT-OK)]` · `[MUDANCA_RELEVANTE: CONTRATO - a regra B-09a de contracts.md ("o dominio da instancia e o Host da propria requisicao, sem chave; X-Forwarded-* nao e lido, parked I-11") agora tem a chave opt-in Instance:Domain + Instance:ForwardedHeaders - contracts.md:36 ficou para tras da TASK-0041 e deve ser atualizado na consolidacao da fase - source: docs/memory-bank/contracts.md:36]` · `[PADRAO_NOVO: helper unico de origem canonica (CanonicalOrigin) lido POR REQUEST na configuracao da propria request, com o middleware de forwarded instalado condicionalmente na composition root - os quatro pontos de origem viram uma linha cada - source: backend/src/Squid.Api/Discovery/CanonicalOrigin.cs:18]` · rota `qa-integracao` lida (`docs/memory-bank/index.md` + `build-run-test.md`: S-22/S-23 confirmados **em execução real** nesta sessão; SPEC-0011 §A e a AC-02 com esclarecimento lidos diretamente das fontes normativas).
