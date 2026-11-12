<!--
templateVersion: 1.0.0
 -->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: RASCUNHO
- owner: qa
- lastVerified: (último artefato: docs/sdd/runs/test-runs/20261112-115022-test-gate.log)

# QA-0040 — Facet de fecho do DoD do Sprint 4 (TASK-0040)

- runId: R-20261111-01 · attempt: **1/2** (bloco de controle `qaAttempts: 0/2` — contador é do ODEV, QA não escreve nele)
- QA: subagente qa-task + política ponytail-qa (full) — testes EXECUTADOS nesta sessão, não escritos e deixados
- leitura normativa: SPEC-0010 **inteiro** (DoD do Sprint 4: dois conversam; terceiro não lê; ordem estável sob concorrência; resposta e reação só participantes e idempotentes) + AC-01..AC-07 do task file + contrato http-api §6 (`http-api.md:125-154`) e §9 (`http-api.md:189-204`) + memória banc rota `qa-integracao` (`index.md:50` → `build-run-test.md#Local tests without gate`: S-15/S-22/S-23 relidos)
- commit sob verificação: `01b741f` em `task/TASK-0040-facet-do-dod-sprint4` (**1 arquivo = só teste**: `backend/tests/Squid.Tests/Threads/Sprint4ClosingFacetTests.cs`, 613 linhas, 7 testes; NENHUM produto tocado pelo DEV); baseline DEV `473/473 → 480/480`
- natureza desta QA: **TESTS-ONLY** — o produto já foi provado pelas tasks 0033..0039; o trabalho aqui é provar que os testes da facet **FALHAM quando o comportamento que eles dizem cobrir quebra** (mutações can-fail contra o PRODUTO, restauradas byte-exact)
- verdict: **PASSED**

## AC → cenário (7 ACs; todos executados nesta sessão)

| AC | Cenário | Execução (destaque) | Resultado |
| --- | --- | --- | --- |
| AC-01 | dois conversam: direct criado, aviso aceito pelos DOIS, mensagens trocadas e lidas na ordem de `seq`, marcador por participante exposto na lista | verde no baseline `20261112-112436-test-filter.log` (**exit 0, 48/48**) e no GREEN `114231` (**48/48**). Can-fail: **M1** (marcador nunca gravado → `113630`, **falha=6**, RED-FACET `Two_users_create_a_direct...` + `A_seat_spelled_read...` + `The_full_flow...` — a lista devolve 0 onde a AC lê 3), **M4** (alocação de `seq` nunca avança → `113851`, `Expected: 1` na primeira mensagem) | PASS |
| AC-02 | terceiro não lê: C leva o MESMO 404 byte-idêntico em GET e POST da thread de A/B e da thread inexistente, sem `traceId`, sem `participants`, sem os handles, e a lista de C não carrega a thread | Can-fail: **M2** (o 404 ganha `traceId` → `113719`, **falha=10**, RED-FACET `A_third_user_shares...` + `Repeating_a_reply...` — os corpos deixam de ser idênticos), **M3** (parede do GET desligada → `113807`, **falha=10**, RED-DETAIL `Assert.All ... Item: StatusCode: 200` — C LEU o histórico). Verde nos mesmos momentos | PASS |
| AC-03 | ordem estável sob concorrência: envios das DUAS sessões no mesmo instante recebem `seq` únicas 1..6, leitura completa, ordenada, sem duplicata, delta do meio exato | **Concorrência REAL confirmada na fonte**: `Sprint4ClosingFacetTests.cs:154-158` usa `await Task.WhenAll(...)` com A e B disparando juntos, **3 rodadas** (`for round in 0..2`) — não é sequencial disfarçado. Can-fail: **M4** (a CAS para de incrementar `message_seq` → `113851`, **falha=31 constraint=yes**, RED-FACET inclui `Concurrent_sends_take_unique_seqs...` e `Two_users_create_a_direct...` — colisão no mesmo número, recusada pelo UNIQUE `(thread_id, seq)` da 0003) | PASS |
| AC-04 | repetir resposta/reação não muda nada; não-participante não vê resposta nem reação (404 idêntico em 6 rotas) e as recusas não tocam nada dentro da parede | Can-fail: **M5** (o `reply` REESCREVE `reply_to_id` da mensagem alvo → `113933`, **falha=4**, RED-DETAIL `Expected: Null`, RED-FACET `Repeating_a_reply...` + `The_full_flow...` — a "original segue SEM resposta" morre), **M2/M3** (a parede do terceiro: MESMOS dois testes vermelhos). Verde nos mesmos momentos | PASS |
| AC-05 | (a) participante com handle `read` continua sendo REMOÇÃO (marcador `/participants/` vence o sufixo `/read`) e a rota `/read` segue viva; (b) o TEXTO do aviso FR-032 existe na linha do aceite, lido na FONTE (tabela), idempotente | Can-fail cirúrgico: **M6** (o sufixo `/read` engole a rota → `114018`, **falha=1, SÓ a AC-05a**, RED-DETAIL `Expected: OK` — o DELETE virou 404), **M7** (texto do aviso fora da linha → `114101`, **falha=1, SÓ a AC-05b**, leitura na fonte não encontra as palavras). Verde nos mesmos momentos | PASS |
| AC-06 | facet ponta a ponta 0033..0039 na MESMA thread: criação, aceite, envio, resposta, reação e anexo, com contagens e referências íntegras dos DOIS lados | Can-fail: **M8** (ramo MENSAGEM da visibilidade de mídia da GAP-25 morto → `114147`, **falha=2**, RED-FACET `The_full_flow_from_creation...` — a participante que NÃO subiu a mídia cai no 404; junto, o teste de rota da TASK-0039). Verde nos mesmos momentos | PASS |
| AC-07 | gate `sdd-test.ps1 test` exit 0; nenhum arquivo alterado fora dos `touches` | **Lido DENTRO do log** `20261112-115022-test-gate.log` (S-22): cabeçalho `exit: 0` + `Com falha: 0, Aprovado: 480, Ignorado: 0, Total: 480` (5m27s). Escopo (`QA-0040-scope.log`): `git status -- backend` **0 entradas**; blob-identity `git hash-object` = `git rev-parse HEAD:` em **3/3** arquivos mutados (work = `f9585eb8b303`, `2ea88b9a9370`, `d78757281d23`); mtime desde a baseline = **exatamente esses 3**; resíduo `QA-MUTATION` = 0; `fora-dos-touches` = **0** (só task file + artefatos `docs/sdd/qa/`); nenhum `D-01` (nenhuma lacuna de produto acusada) | PASS |

## Observação executada (M9): onde a facet PARA de cobrir

O dispatch sugeriu "des-monotonizar o marcador (AC-01/AC-06 red?)". Executei como ciclo-observação **M9** (guarda `WHERE excluded.last_read_seq > thread_read.last_read_seq` removida, marcador pode regredir):

- **A facet NÃO cai**: `M9 RED-FACET NONE` — por leitura do próprio teste, nenhuma AC-01..AC-06 marca um valor MENOR depois de um maior (cada participante avança a partir de 0: `Sprint4ClosingFacetTests.cs:84-87`, `:326`, `:432-434`). A monotonicidade (GAP-38) não é comportamento reivindicado pela facet;
- **A suite cai**: `M9 RUN ... falha=2` (`120124`), RED-IN-LOG em `ThreadAckAndReadRoutesTests.The_read_marker_only_advances_and_a_seq_above_the_thread_total_is_400` e `ThreadStoreTests.The_append_gates_on_the_ack_of_each_sender_and_the_read_marker_only_ever_advances` — a TASK-0036 cobre a regressão.

Boundary registrado como NOTA, não como lacuna de AC: o M9 é vermelho DENTRO do seu log (S-22) e restaurado como os outros (`RESTORE-OK`, GREEN `120209` 48/48).

## Afirmações do DEV a verificar (declarações de nível spec)

| # | Afirmação (Log do task file / commit 01b741f) | Julgamento desta QA | Evidência |
| --- | --- | --- | --- |
| 1 | "7 testes: AC-01..AC-06; concorrência com seqs únicas" | **CONFIRMADA em execução e leitura**: 7 `[Fact]` na fonte (checker `FACET-FACTS`); a corrida é `Task.WhenAll` real de duas sessões em 3 rodadas (`Sprint4ClosingFacetTests.cs:154-158`), e o M4 derruba EXATAMENTE o `Concurrent_sends_take...` | checkers + ciclo M4 |
| 2 | "nenhum D-01 (nenhuma lacuna de produto acusada)" | **CONFIRMADA**: nas 9 execuções desta QA nenhum teste acusou defeito do produto — todos os vermelhos foram os mutantes que EU introduzi e restaurei; o produto verde é o gate 480/480 | `20261112-115022-test-gate.log` + ledger |
| 3 | "gate final 480/480 exit 0 (DEV, 20261112-105828) e escopo limpo pós-commit 01b741f" | **CONFIRMADA**: baseline do git desta QA = `HEAD 01b741f`, branch correta, `git status` só o task file; e o MEU gate sobre a mesma árvore também é 480/480 exit 0 | `QA-0040-git-baseline.txt` + gate `115022` |

## Comandos executados (todos nesta sessão, com exit real; datas vêm dos próprios logs)

| # | comando | exit | artefato / saída |
| --- | --- | --- | --- |
| 1 | `git rev-parse HEAD` + `git rev-parse --abbrev-ref HEAD` + `git status --porcelain -uall` + `wsl -e bash -c "command -v dotnet"` (pwsh) | 0 | `docs/sdd/qa/QA-0040-git-baseline.txt` — HEAD `01b741f`, branch `task/TASK-0040-facet-do-dod-sprint4`, só o task file, **dotnet ausente no WSL** |
| 2 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Threads` (baseline) | 0 | `20261112-112436-test-filter.log` — `exit: 0`, Com falha 0 / Aprovado 48 / Total 48 |
| 3 | `powershell ... docs/sdd/qa/qa-mutate-task-0040.ps1 -VerifyOnly` (2×: 8 ciclos antes do run cheio, 9 ciclos depois de acrescentar a observação M9) | 0 | `docs/sdd/qa/QA-0040-verify.log` — 9 padrões `hits=1`, `BOM OK`, `VERIFY total=9 bad=0` |
| 4 | `powershell ... docs/sdd/qa/qa-mutate-task-0040.ps1` (execução cheia: 8 ciclos can-fail + GREEN) | 0 | `QA-0040-mutations.log` — 8 vermelhos com **RED-FACET da facet em cada um**, 8× `RESTORE-OK hash=PRE residue=0`, `GREEN ... aprovado=48 falha=0` (`114231`), `SUMMARY cycles=8 failed=0 residue=0 temp-backups=0` |
| 5 | `powershell ... docs/sdd/qa/qa-mutate-task-0040.ps1 -Only M9` (observação: marcador sem guarda de monotonicidade) | 0 | ledger: `M9 RUN ... aprovado=46 falha=2` (`120124`), `M9 RED-FACET NONE` (facet sobrevive), restore OK, `GREEN 48/48` (`120209`), `SUMMARY cycles=1 failed=0` |
| 6 | `powershell ... docs/sdd/_tools/sdd-test.ps1 test` (gate AC-07, árvore igual à do DEV) | 0 | `20261112-115022-test-gate.log` — `exit: 0` lido **dentro do log** (S-22) + `Com falha: 0, Aprovado: 480, Total: 480`, 5m27s |
| 7 | varredura de escopo (pwsh): `git status`, `git hash-object` vs `git rev-parse HEAD:`, mtime desde a baseline, resíduo, fora-dos-touches | 0 | `docs/sdd/qa/QA-0040-scope.log` — git-backend 0, blob 3/3 `match=True`, `mtime-count=3`, `residue=0`, `fora-dos-touches=0`, `temp-backups=0` |
| 8 | `powershell ... docs/sdd/qa/qa-check-task-0040.ps1` | 0 | `docs/sdd/qa/QA-0040-check.log` — SUMMARY total/fails (SDD-STATE `check` incluso) |
| 9 | `wsl -e bash docs/sdd/qa/qa-check-task-0040.sh` (gêmeo Linux, parte estática) | 0 | `docs/sdd/qa/QA-0040-check-sh.log` — SUMMARY total/fails (o `sdd-state` é ferramenta Windows, fica só no ps1) |

## Ciclos de mutação (can-fail; S-16 SHA256 PRE, S-22 classificação no log, S-23 restore byte-exact + mtime)

Todos nos arquivos de PRODUTO que a facet lê (`Squid.Api/Threads/**`, `Squid.Api/Media/**`, `Squid.Service/**`) — nenhum edit fora do escopo, e os 3 arquivos finais são byte-iguais ao `HEAD` (blob-identity 3/3). Filtro de todos os ciclos: `FullyQualifiedName~Squid.Tests.Threads`.

| Ciclo | Arquivo | O que prova (AC) | Vermelho classificado NO LOG |
| --- | --- | --- | --- |
| M1 | `Squid.Service/SqliteThreadStore.cs` | AC-01/05a/06: sem GRAVAR o marcador, a resposta ecoa o pedido e a lista devolve 0 — "avancou para N" é escrita, não leitura | `Two_users_create_a_direct...` + `A_seat_spelled_read...` + `The_full_flow...` (`113630`, **falha=6**) |
| M2 | `Squid.Api/Threads/ThreadEndpoints.cs` | AC-02/04: o 404 compartilhado é SEM `traceId` — um id por request racha a identidade byte-a-byte e ensina algo na recusa | `A_third_user_shares...` + `Repeating_a_reply...` (`113719`, **falha=10**) |
| M3 | `Squid.Api/Threads/ThreadEndpoints.cs` | AC-02/04: a parede do GET — sem a sonda, o não-participante LÊ o histórico (e a thread ausente responde 200) | MESMOS dois + `A_seat_spelled_read...` (`113807`, **falha=10**, `RED-DETAIL StatusCode: 200`) |
| M4 | `Squid.Service/SqliteThreadStore.cs` | AC-03: a alocação de `seq` é o incremento da CAS — sem ele todo envio recebe o MESMO número (colisão), recusado pelo UNIQUE da 0003 | `Concurrent_sends_take...` + `Two_users_create_a_direct...` (+ 4 da facet) (`113851`, **falha=31 constraint=yes**) |
| M5 | `Squid.Service/SqliteThreadStore.cs` (1 edição que ADICIONA a escrita que não pode existir) | AC-04: "repetir a resposta não reescreve nada que já existe" — o `reply` passa a reescrever o alvo e a original deixa de ser `null` | `Repeating_a_reply...` + `The_full_flow...` (`113933`, **falha=4**, `RED-DETAIL Expected: Null`) |
| M6 | `Squid.Api/Threads/ThreadEndpoints.cs` | AC-05a: ordem do parser — o marcador `/participants/` tem que vencer o sufixo `/read`, senão a cadeira `read` não é remoção | **SÓ** `A_seat_spelled_read...` (`114018`, **falha=1**, `Expected: OK` → 404) |
| M7 | `Squid.Api/Threads/ThreadEndpoints.cs` | AC-05b: o texto do aviso FR-032 é o que a linha do aceite carrega — lido na FONTE (SQL), não na resposta da rota | **SÓ** `The_ack_row_carries...` (`114101`, **falha=1**) |
| M8 | `Squid.Api/Media/MediaEndpoints.cs` | AC-06: o ramo MENSAGEM da visibilidade (GAP-25 ampliada) — sem ele o participante que não subiu a mídia cai no 404 | `The_full_flow...` + `GetMedia_serves_the_participant...` (`114147`, **falha=2**, `Expected: OK`) |
| M9 (observação) | `Squid.Service/SqliteThreadStore.cs` | boundary: marcador SEM a guarda de monotonicidade — a facet não tem caso de regressao (RED-FACET NONE); quem pega é a suite da TASK-0036 | `The_read_marker_only_advances...` + `The_append_gates_on_the_ack...` (`120124`, **falha=2**, facet sobrevive) |

9× `PRE` registrado, 9× `RESTORE-OK hash=PRE residue=0`, 0× `RESTORE-FAIL`, 2× GREEN `48/48 exit 0` (`114231`, `120209`), resíduo final `QA-MUTATION` = 0, backups TEMP `qa0040-*` = 0, `-VerifyOnly` `bad=0` + `BOM OK` (`QA-0040-verify.log`).

## Findings

Nenhum achado de **produto** (0 BLOCKER, 0 MAJOR, 0 MINOR — esta QA não acusou nenhuma lacuna; o DEV também não levantou `D-01`). Notas de QA/fluxo:

| # | Severidade | Onde | O que | Evidência |
| --- | --- | --- | --- | --- |
| 1 | NOTA (boundary de cobertura) | facet `Sprint4ClosingFacetTests.cs` x GAP-38 | a facet **não** cobre regressão do marcador de leitura (nenhuma AC pede; cada participante avança só para frente a partir de 0) — provado em EXECUÇÃO pelo M9, que sobrevive à facet (`RED-FACET NONE`) e é vermelho na suite da TASK-0036. Se o ODEV quiser a monotonicidade DENTRO do DoD, é nova AC, não é lacuna silenciosa | ciclo M9 (`120124`) + leitura `:84-87`, `:326`, `:432-434` |
| 2 | NOTA (BOM, 6ª recorrência) | driver (esta QA) | `qa-mutate-task-0040.ps1` nasceu **sem UTF-8 BOM** e a edição que acrescentou o M9 **derrubou o BOM de novo** — re-posto antes de qualquer execução e reconferido em três lugares: `-VerifyOnly` (`BOM OK`), `QA-0040-verify.log` e os dois checkers (`DRIVER-BOM`) | comando 3 + `DRIVER-BOM` |
| 3 | NOTA (pré-existente) | `docs/sdd/_tools/sdd-test.sh` / WSL | gêmeo Linux não roda a suite: `wsl -e bash -c "command -v dotnet"` → `dotnet-missing` (**sondado ao vivo NESTA sessão**); instalação exige gate humano — mesmo achado das QA-0033..0039 | comando 1 + linha do Log |
| 4 | NOTA (alcance do mutante) | `SqliteThreadStore.cs` alocação de seq | quebrar a alocação (M4) derrubou **31 testes** (quase toda a família de envio) — a defesa da ordem total é estrutural, e a facet está explícita entre elas (`Concurrent_sends_take...` no RED-FACET); o ciclo é conservador: colisão + UNIQUE da 0003, sem estado parcial | ciclo M4 (`113851`) |

## Não coberto / unverified

- **Suite de testes em Linux (WSL)**: `dotnet` ausente no WSL (sondado ao vivo nesta sessão: `command -v dotnet` → `dotnet-missing`; instalação exige gate humano) e o gêmeo `sdd-test.sh` quebra sem ele → **unverified**, nunca assumido igual ao Windows. O gêmeo `.sh` do **checker** (parte estática: logs, resumos, hashes, git) foi executado — comando 9.
- **Corrida real de envios sob carga** (BEGIN IMMEDIATE + CAS): a facet dispara `Task.WhenAll` de duas sessões e o M4 prova que a alocação quebrada é DETECTADA, mas a corrida em si (quem vence a escrita) não foi instrumentada — mesma limitação das QA-0033..0039. Risco: baixo (o serializador é o próprio SQLite, provado pelas tasks anteriores).
- **`GET /api/media/{hash}/thumb` e demais rotas fora da facet**: a facet cobre o caminho pedido pelo DoD; rotas vizinhas continuam cobertas pelas suites 0027/0039 (não re-executadas aqui fora do gate completo, que as inclui).

## Reproduzir

```powershell
# pré-requisito: .NET 10 SDK + PowerShell no Windows, repo C:\dev\squid-squad-net\squid-execution
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test       # esperado: exit 0, Aprovado 480, Com falha 0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0040.ps1 -VerifyOnly   # esperado: exit 0, VERIFY total=9 bad=0 + BOM OK
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0040.ps1     # run fresco: exit 0, SUMMARY cycles=8 failed=0 (8 vermelhos de propósito)
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0040.ps1      # esperado: exit 0, SUMMARY fails=0
wsl -e bash docs/sdd/qa/qa-check-task-0040.sh                                               # esperado: exit 0, SUMMARY fails=0
```

## Artefatos desta QA

| Arquivo | Papel |
| --- | --- |
| `docs/sdd/qa/qa-mutate-task-0040.ps1` | driver de mutação can-fail (8 ciclos + observação M9, `-VerifyOnly` com checagem de BOM, S-16/S-22/S-23 embutidos, captura de `RED-FACET` = quais testes da facet caíram) |
| `docs/sdd/qa/QA-0040-mutations.log` | ledger: PRE, vermelhos no log (com RED-FACET), restore, GREEN, SUMMARY (full + seletivo do M9) |
| `docs/sdd/qa/QA-0040-verify.log` | `-VerifyOnly` gravado (9 padrões `hits=1` + `BOM OK` + `VERIFY total=9 bad=0`) |
| `docs/sdd/qa/qa-check-task-0040.ps1` / `.sh` | checkers de evidência (estáticos; gêmeo Linux sem o sdd-state, que é ferramenta Windows) |
| `docs/sdd/qa/QA-0040-check.log` / `QA-0040-check-sh.log` | execuções dos checkers |
| `docs/sdd/qa/QA-0040-git-baseline.txt` | git + HEAD + sonda WSL capturados antes da QA escrever |
| `docs/sdd/qa/QA-0040-scope.log` | varredura final de escopo (gate, git, blob-identity, mtime, resíduo, fora-dos-touches) |

memoryBankFlags: `[GAP: suite Linux/WSL unverified - dotnet ausente na maquina (sondado ao vivo nesta sessao), instalacao exige gate humano (pre-existente, mesmo achado das QA-0033..0039)]` · `[SUSPEITO: nao - nenhuma contradicao spec x codigo encontrada; a facet e 7 testes verdes em 48/48 e 480/480, e os 9 mutantes caem exatamente onde cada AC diz (RED-FACET por ciclo)]` · `[PADRAO_NOVO: facet de fecho de DoD - prova que os TESTES falham quando o comportamento quebra (mutacao can-fail por AC) em vez de re-provar o produto; o boundary do marcador monotonico (GAP-38) fica FORA da facet e e da suite da TASK-0036 - M9 RED-FACET NONE + falha=2 - source: backend/tests/Squid.Tests/Threads/Sprint4ClosingFacetTests.cs:84]` · `[MUDANCA_RELEVANTE: nao - NENHUM arquivo de produto alterado (git backend = 0; blob 3/3 igual a 01b741f)]` · rota `qa-integracao` lida (`docs/memory-bank/index.md` + `build-run-test.md#Local tests without gate`: S-15/S-22/S-23 confirmados **em execução real** nesta sessão; SPEC-0010 inteiro e o contrato §6/§9 foram lidos diretamente das fontes normativas).
