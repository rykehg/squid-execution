<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: RASCUNHO
- owner: qa
- lastVerified: (último artefato: docs/sdd/runs/test-runs/20261112-025653-test-gate.log)

# QA-0034 — Envio e leitura com ordem total e 404 idêntico (TASK-0034)

- runId: R-20261111-01 · attempt: **1/2** (bloco de controle `qaAttempts: 0/2` — contador é do ODEV, QA não escreve nele)
- QA: subagente qa-task + política ponytail-qa (full) — testes EXECUTADOS nesta sessão, não escritos e deixados
- leitura normativa: SPEC-0010 §B + Gaps (GAP-29, nota GAP-38) + AC-01..AC-05 da task + data-model §2.5.1 + contrato http-api §6/§9.3 e §9.7
- commit sob verificação: `f6ba1b2` em `task/TASK-0034-envio-e-leitura` (7 arquivos = 5 produto + 2 teste, todos nas `touches` emendadas com `schema/**`); baseline DEV `443/443`, gate desta QA idem
- verdict: **PASSED**

## AC → cenário (5 ACs; todos executados nesta sessão)

| AC | Cenário | Execução (destaque) | Resultado |
| --- | --- | --- | --- |
| AC-01 | `seq` crescente alocada pelo estado da thread; dois envios concorrentes nunca compartilham `seq`; número alocado e linha da mensagem nascem juntos (GAP-29, mesma transação) | `ThreadMessageRoutesTests.Two_sends_racing...` (2 rodadas de corrida HTTP reais → exatamente 1..4, delta devolve os 4) + `ThreadStoreTests.The_message_seq...` (versão velha perde, exatamente um vence) + `ThreadStoreTests.The_append...` (4 linhas = `message_seq` 4) verdes no baseline `20261112-023919-test-filter.log` (**exit 0, 11/11**) e nos 2 GREENs. Can-fail: **M1** (INSERT para outro `thread_id`: número some do delta → 5 vermelhas `024132`), **M2** (CAS do `version` de `NextMessageSeqAsync` desligado → `024154`), **M3** (sem incremento: mesma `seq` reutilizada → o `UNIQUE(thread_id,seq)` do 0003 derruba os envios → 4 vermelhas `024214`) + **probe UNIQUE** (abaixo) | PASS |
| AC-02 | `Content` ≤ 4000; sanitização escapa HTML e remove C0 no servidor (GAP-24); vazio ou acima → 400; nada recusado é gravado | `ThreadMessageRoutesTests.Content_is_sanitized...` verde: vazio→400, `\u0001\u0002`→400, 4001→400, **4000 exatos→200**, `&`×4000 cru cabe mas pos-sanitização estoura→400, HTML/C0 sanitizados no servidor e o histórico final tem **exatamente** as 2 aceitas. Can-fail: **M4** (`>`→`>=` no teto cru: 4000 exatos viram 400 → `024236`), **M5** (teto pós-sanitização desligado → `024257`), **M6** (chamada do `PostSanitizer` trocada por texto cru: C0 e HTML não caem → `024319`) | PASS |
| AC-03 | `GET ?since=` devolve só `seq > since`, sem duplicata, ordenado, com `nextSince`; `since` não numérico → 400 | `ThreadMessageRoutesTests.The_delta...` verde: since 0/3/5 (no topo: vazio + cursor parado), re-poll `since=2` **byte a byte idêntico**, `since=abc`→400. Can-fail: **M7** (`>`→`>=`: `since=3` devolve 3 repetido, `since=5` devolve `[5]` → `024340` + vermelha do store), **M8** (`since=abc` vira 200 com histórico inteiro → `024400`), **M9** (poll vazio avança cursor `+1` → `024421`) | PASS |
| AC-04 | Não-participante **e** thread inexistente recebem 404 byte-idêntico em GET e POST, sem `traceId` nem vazamento (FR-031/SC-005, contrato §9.1/§9.7) | `ThreadMessageRoutesTests.A_non_participant...` verde: 4 responses (estranho×{GET,POST} na thread alheia e na ausente) = mesmos bytes, `not_found` presente, `traceId`/`participants`/handle/dono/id da thread ausentes, e o dono continua lendo e enviando. Can-fail: **M10** (portão de participante desligado nas 2 rotas → `024442`), **M11** (`withTraceId:false`→`true`: corpos deixam de casar → `024502`), **M12 v2** (recusa do POST com corpo próprio → pares GET×POST quebram → `024941`), **M13** (404→403 → `024542`); M12 v1 sobreviveu e está classificado como anômalo (finding 1) | PASS |
| AC-05 | `sdd-test.ps1 test` exit 0 e nenhum arquivo alterado fora dos `touches` | Gate `20261112-025653-test-gate.log` → **`exit: 0` no cabeçalho e `Com falha: 0, Aprovado: 443, Total: 443`** (S-22: lido dentro do log; 4m36s). Escopo (`QA-0034-scope.log`): `git status -- backend` **vazio**; blob-identity `git hash-object` = `git rev-parse HEAD:` em **3/3** arquivos mutados (byte-exact); mtime desde a baseline `02:38:45` = **exatamente os 3** (`ThreadEndpoints.cs`, `SqliteThreadStore.cs`, `0003_messages.sql`, todos restaurados); resíduo `QA-MUTATION` = **0**; probe temporário removido; novidades no git só em `docs/sdd/qa/*` (+ os 2 task files já modificados pelo ODEV antes da QA) | PASS |

## Comandos executados (todos nesta sessão, com exit real; datas vêm dos próprios logs)

| # | comando | exit | artefato / saída |
| --- | --- | --- | --- |
| 1 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Threads` (baseline pré-QA) | 0 | `20261112-023919-test-filter.log` — `exit: 0`, Com falha 0 / Aprovado 11 / Total 11 |
| 2 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0034.ps1` (execução cheia: 13 ciclos) | 1 | `QA-0034-mutations.log` — 12 vermelhos classificados no log + M12 v1 sobrevivente (`CLASSIFICACAO-ERRADA`); exit 1 é o próprio ledger marcando `failed=1` (finding 1) |
| 3 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0034.ps1 -VerifyOnly` (contagem dos 13 padrões, 2 passagens) | 0 | stdout `VERIFY total=13 bad=0` (a 1ª passagem falhou em M5/M8 por BOM removido por edição — corrigido e re-executado) |
| 4 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0034.ps1 -Only M12` (mutante v2) | 0 | `QA-0034-mutations.log` — M12 v2 vermelho `20261112-024941` + GREEN `025001` + `SUMMARY cycles=1 failed=0` |
| 5 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-probe-task-0034-unique.ps1` (diferencial UNIQUE, can-fail embutido) | 0 | `QA-0034-unique-probe.log` — PRISTINE verde `025136`, 0003 sem UNIQUE → mesmo probe vermelho `025148`, restore OK, git backend 0 |
| 6 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` (gate AC-05) | 0 | `20261112-025653-test-gate.log` — `exit: 0` + `Aprovado: 443, Com falha: 0` lidos **dentro do log** (S-22) |
| 7 | varredura de escopo (pwsh): `git status -uall` vs baseline + `git status -- backend` + mtime desde 02:38:45 + resíduo + `git hash-object` vs `git rev-parse HEAD:` | 0 | `docs/sdd/qa/QA-0034-scope.log` — backend 0, 3/3 blob-identity, mtime 3, resíduo 0; 1ª tentativa de hash usou caminho com `\` e o git recusou — seção corrigida com `hash-object` |
| 8 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0034.ps1` | 0 | `docs/sdd/qa/QA-0034-check.log` — SUMMARY total=29 fails=0 |
| 9 | `wsl -e bash docs/sdd/qa/qa-check-task-0034.sh` (gêmeo Linux, parte estática) | 0 | `docs/sdd/qa/QA-0034-check-sh.log` — SUMMARY total=24 fails=0 |

## Ciclos de mutação (can-fail; S-16 SHA256 PRE, S-22 classificação no log, S-23 restore byte-exact + mtime)

Todos nos `touches` (`Squid.Api/Threads/**`, `Squid.Service/**`, `schema/**`) — nenhum edit fora do escopo. Filtro: `FullyQualifiedName~Squid.Tests.Threads` (11 testes).

| Ciclo | Arquivo | O que prova | Vermelho classificado NO LOG |
| --- | --- | --- | --- |
| M1 | `Squid.Service/SqliteThreadStore.cs` | AC-01/GAP-29: número alocado com a mensagem fora da thread (delta pula seq; e o thread falso acumula seq repetida → 500) | 5 falhas: as 4 rotas + `ThreadStoreTests.The_append...` (`20261112-024132`) |
| M2 | idem | AC-01: CAS do `version` em `NextMessageSeqAsync` — versão velha não pode mover nada | `ThreadStoreTests.The_message_seq... [FAIL]` (`024154`) |
| M3 | idem | AC-01: sem o incremento a mesma `seq` volta — o `UNIQUE(thread_id,seq)` é quem recusa (500 no envio) | 4 falhas: content/delta/corrida + store append (`024214`) |
| M4 | `Squid.Api/Threads/ThreadEndpoints.cs` | AC-02: 4000 cru exatos ainda cabem (`>` do teto) | `Content_is_sanitized... [FAIL]` (`024236`) |
| M5 | idem | AC-02: teto do que é PERSISTIDO após sanitizar (`&`×4000 → 20000) | `Content_is_sanitized... [FAIL]` (`024257`) |
| M6 | idem | AC-02: o `PostSanitizer` é chamado no servidor (C0 + HTML) | `Content_is_sanitized... [FAIL]` (`024319`) |
| M7 | `Squid.Service/SqliteThreadStore.cs` | AC-03/§9.3: fronteira exata `seq > since` | delta da rota + store append (`024340`) |
| M8 | `Squid.Api/Threads/ThreadEndpoints.cs` | AC-03: `since` não numérico é 400, não 200 com histórico inteiro | `The_delta... [FAIL]` (`024400`) |
| M9 | idem | AC-03: poll vazio devolve o MESMO `nextSince` (cursor parado) | `The_delta... [FAIL]` (`024421`) |
| M10 | idem | AC-04: portão de participante nas DUAS rotas (esperado=2) | `A_non_participant... [FAIL]` (`024442`) |
| M11 | idem | AC-04: 404 sem `traceId` (par byte-idêntico por request) | `A_non_participant... [FAIL]` (`024502`) |
| M12 v1 | idem | (sobreviveu) edit no ramo de tail malformado que as 4 requests não alcançam — **não é produto**: `CLASSIFICACAO-ERRADA` `024522`, mutante descartado | — (anômalo, documentado) |
| M12 v2 | idem | AC-04: recusa do POST com corpo próprio → pares GET×POST e ausente×alheio quebram | `A_non_participant... [FAIL]` (`024941`) |
| M13 | idem | AC-04/§9.1: recurso restrito nunca 403 | `A_non_participant... [FAIL]` (`024542`) |

14× `PRE` registrado, 14× `RESTORE-OK hash=PRE residue=0`, 0× `RESTORE-FAIL`, 2× GREEN `11/11 exit 0` (`024603`, `025001`).

## Probe UNIQUE (última linha de defesa do GAP-29)

Nenhum teste do suite consegue falhar quando **só** o `UNIQUE` do 0003 some (o CAS nunca oferece uma duplicata) — o probe fecha a prova ponta a ponta, com can-fail embutido e arquivo temporário removido no fim:

1. `qa-probe-task-0034-unique.ps1` escreve `QaTask0034UniqueProbeTests.cs` (temporário) e roda no schema original → **verde** (`025136`, 1/1): `PRAGMA index_list` mostra `unique=1` e o INSERT duplicado levanta `SqliteException 19` com `message.thread_id, message.seq` no texto;
2. troca `CREATE UNIQUE INDEX` → `CREATE INDEX` no 0003 (S-16) e roda o **mesmo teste** → **vermelho** (`025148`, 0/1, can-fail provado);
3. restaura 0003 byte-exact + mtime (S-23) → `RESTORE-OK hash=PRE`, apaga o arquivo temporário, `git status -- backend` volta vazio.

## Findings

Nenhum achado de **produto** (0 BLOCKER, 0 MAJOR, 0 MINOR). Notas de QA/fluxo:

| # | Severidade | Onde | O que | Evidência |
| --- | --- | --- | --- | --- |
| 1 | NOTA (mutante da QA) | `qa-mutate-task-0034.ps1` ciclo M12 v1 | O 1º edit trocava o corpo do ramo `id is null`, que as 4 requests do AC-04 **não alcançam** (thread ausente tem id bem-formado e cai no probe) — o ciclo sobreviveu e o driver classificou `CLASSIFICACAO-ERRADA` em vez de ondular vermelho (S-22 funcionando); mutante redesenhado (v2) e vermelho em `024941` | `QA-0034-mutations.log` |
| 2 | NOTA (medição) | ciclo M3 | O texto `UNIQUE constraint failed` **não sobe** ao output do teste (o host responde 500 genérico) — ledger `constraint=no`; a prova nominal do índice fica no probe (item acima: `SqliteException 19` com as duas colunas) | `QA-0034-mutations.log` + `QA-0034-unique-probe.log` |
| 3 | NOTA (pré-existente) | `docs/sdd/_tools/sdd-test.sh` | O gêmeo Linux não resolve `*.slnx` — mesmo achado da QA-0033, não é desta task; gate segue sendo executo no Windows | finding 4 da QA-0033 |
| 4 | NOTA (BOM) | driver (esta QA) | Uma edição de arquivo removeu o BOM UTF-8 do `.ps1` e M5/M8 (padrões com `inválido`) caíram para `hits=0` no `-VerifyOnly` — BOM restaurado, `VERIFY total=13 bad=0`; flag que qualquer script PS 5.1 com acento sofre | linha 3 da tabela de comandos |

## Não coberto / unverified

- **Suite de testes em Linux (WSL)**: `dotnet` ausente no WSL (sondado ao vivo nesta sessão: `command -v dotnet` → not found; instalação exige gate humano) e o gêmeo `sdd-test.sh` quebra na busca da solution → **unverified**, nunca assumido igual ao Windows. O gêmeo `.sh` do **checker** (parte estática: logs, resumos, hashes, git) foi executado — comando 9.
- **`since` acima do topo** (`since` > `messageSeq` da thread): não tem assert próprio do DEV — o caminho é literalmente o mesmo do `since == topo` (assertado: vazio + cursor parado, `items.Count > 0 ? last : since`) — risco baixo, comportamento revisado no código (`ThreadEndpoints.cs:275-279`).
- **Corpo do 404 no ramo de tail malformado** (`id is null` do POST): M12 v1 provou que nenhum teste compara esse corpo — mas esse ramo não é rota de thread (fora do texto do AC-04) e usa o mesmo `HiddenThread` (revisão de código `ThreadEndpoints.cs:170-175`) — risco baixo.
- **Contenção multi-processo do SQLite**: mesmo limite da QA-0033 — CAS exercitado por chamadas concorrentes no mesmo processo + SQL revisado; dois processos disputando o lock de arquivo não rodam aqui — risco coberto por `busy_timeout=30s` no código.
- **Fora de escopo por design**: 428 do aviso (TASK-0036), `replyToId` (0037), reações (0038), anexos (0039), ator `Suspended` em envio (AC nova da TASK-0036 pelo ODEV).

## Reproduzir

```powershell
# pré-requisito: .NET 10 SDK + PowerShell no Windows, repo C:\dev\squid-squad-net\squid-execution
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test       # esperado: exit 0, Aprovado 443, Com falha 0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0034.ps1 -VerifyOnly   # esperado: exit 0, VERIFY total=13 bad=0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0034.ps1     # run fresco: exit 0, SUMMARY cycles=13 failed=0 (13 vermelhos de propósito); o ledger ARMAZENADO guarda a anomalia M12 v1 da 1ª execução
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-probe-task-0034-unique.ps1  # esperado: exit 0, SUMMARY ... fails=0 (PRISTINE green + MUTATED red)
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0034.ps1      # esperado: exit 0, SUMMARY fails=0
wsl -e bash -lc bash docs/sdd/qa/qa-check-task-0034.sh                                      # esperado: exit 0, SUMMARY fails=0
```

## Artefatos desta QA

| Arquivo | Papel |
| --- | --- |
| `docs/sdd/qa/qa-mutate-task-0034.ps1` | driver de mutação can-fail (13 ciclos, `-VerifyOnly`, S-16/S-22/S-23 embutidos) |
| `docs/sdd/qa/QA-0034-mutations.log` | ledger: PRE, vermelhos no log, restore, GREENs, SUMMARYs (2 execuções) |
| `docs/sdd/qa/qa-probe-task-0034-unique.ps1` | probe diferencial do `UNIQUE(thread_id,seq)` com can-fail embutido |
| `docs/sdd/qa/QA-0034-unique-probe.log` | execução do probe: pristine green / mutado red / restore OK |
| `docs/sdd/qa/qa-check-task-0034.ps1` / `.sh` | checkers de evidência (estáticos; gêmeo Linux sem o sdd-state, que é ferramenta Windows) |
| `docs/sdd/qa/QA-0034-check.log` / `QA-0034-check-sh.log` | execuções dos checkers |
| `docs/sdd/qa/QA-0034-git-baseline.txt` | git + HEAD capturados antes da QA escrever (02:38:45) |
| `docs/sdd/qa/QA-0034-scope.log` | varredura final de escopo (git, blob-identity, mtime, resíduo) |

memoryBankFlags: NENHUM — rota `qa-integracao` lida (`docs/memory-bank/index.md` + `build-run-test.md` via grep dirigido: S-15/S-22/S-23 confirmados **em execução real** nesta sessão; `contracts.md`/`integrations.md` não são normativos para os ACs — o contrato `specs/001-squid-mvp/contracts/http-api.md` §6/§9 é que foi lido); nenhum padrão novo, divergência ou suspeito observado — a flag `MUDANCA_RELEVANTE CONTRATO` (contracts.md ## Endpoints sem mensageria) já vem carregada pelo ODEV no Log da task.
