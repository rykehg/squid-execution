<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: RASCUNHO
- owner: qa
- lastVerified: (último artefato: docs/sdd/runs/test-runs/20261112-013346-test-gate.log)

# QA-0033 — Núcleo da thread: migration, store ETag, criar e listar (TASK-0033)

- runId: R-20261111-01 · attempt: **1/2** (bloco de controle `qaAttempts: 0/2` — contador é do ODEV, QA não escreve nele)
- QA: subagente qa-task + política ponytail-qa (full)
- leitura normativa: SPEC-0010 §A (seção normativa) + data-model §2.7 (+ emenda GAP-30) + contrato http-api §1/§8/§9 + GAP-29/30/38, contra AC-01..AC-05 da task
- commit sob verificação: `599d4ac` em `task/TASK-0033-nucleo-da-thread` — `git show --stat` nesta sessão: **11 arquivos = 6 produto** (`IThreadStore.cs`, `ThreadEndpoints.cs`, `ThreadView.cs`, `0002_threads.sql`, `SqliteThreadStore.cs`, `SquidServiceHost.cs`) **+ 5 teste** (`ProjectionPumpTests.cs`, `SchemaMigrationTests.cs` + os 3 novos de `Threads/`), todos dentro do `touches` emendado (a nota do dispatch "7 produto + 6 teste" não bate: são 6+5)
- verdict: **PASSED**

## AC → cenário (5 ACs; todos executados nesta sessão)

| AC | Cenário | Execução (destaque) | Resultado |
| --- | --- | --- | --- |
| AC-01 | migration `0002_threads.sql` versionada e idempotente: `thread` (`id,kind,created_by,message_seq,created_at,version`) + `thread_participant` + índice `actor_id`; rodar duas vezes não falha nem duplica; **e** `MessageSeq` só avança sob o CAS do `version` (GAP-29/ETag) | `ThreadMigrationTests.Ac01` (colunas lidas de `pragma_table_info`, índice, linhas-semente entre as duas aplicações, re-aplica vazia, contagens 1/1) + `ThreadStoreTests` (dois emissores com a mesma versão → exatamente um vence; versão velha não move nada; `message_seq`/`version` conferidos no banco) verdes no baseline `20261112-010646-test-filter.log` (**exit 0, 9/9**) e em todos os GREENs. Can-fail: **M1** (CAS desligado → `ThreadStoreTests` vermelha, log `011118`), **M2** (skip de idempotência removido → 3 vermelhas `011142`), **M3** (índice removido → `ThreadMigrationTests` vermelha `011205`) | PASS |
| AC-02 | `POST /api/threads`: direct = exatamente 2 **contando o criador**; group = 2..50 **contando o criador**; criador do group = `admin`; fora da faixa → `400` antes de gravar | `ThreadRoutesTests.A_direct...` + `A_group...` verdes (201/400 + leitura do papel direto no SQLite via `RoleAsync`). Can-fail: **M4A** (direct `==2`→`>=2`, direct 3 aceito → `011228`), **M4B** (portão `!inRange` desligado → 400 vira 201 → `011250`, 2 vermelhas), **M4C** (teto `<=50`→`<50` → grupo de 50 recusado → `011312`), **M4D** (piso `>=2`→`>2` → grupo de 2 recusado → `011929`, 2 vermelhas), **M5** (criador fora do conjunto → `011349`, 4 vermelhas), **M6** (criador do group nasce `member` → `011413`) | PASS |
| AC-03 | `GET /api/threads` devolve só as threads do ator, `messageSeq` 0 e `lastReadSeq` 0 (GAP-38) | `ThreadRoutesTests.The_list...` verde: Alice vê as 2 dela, Bob vê as 2 dele, `DoesNotContain` da thread alheia nos dois sentidos, ambos contadores 0 em todo item. Can-fail: **M7** (`WHERE p.actor_id` removido → threads alheias entram → `011438`) | PASS |
| AC-04 | sem sessão → `401` nas duas rotas; mutação sem CSRF → `403`; `Id` nasce do scheme+Host da própria requisição (emenda P-03) | `ThreadRoutesTests.Both_routes...` verde (anon POST/GET 401, sessão sem header 403, id começa com `http://127.0.0.1:<porta>/t/`). Can-fail: **M9** (401 → 403 nos dois guards → `011522`), **M8** (origem fixa no lugar de scheme+Host → `011501`), **M10** (portão CSRF do `AuthSessionMiddleware` desligado → 403 vira 201 → `012720`) | PASS |
| AC-05 | gate `sdd-test.ps1 test` exit 0 e nenhum arquivo alterado fora dos `touches` | gate final `20261112-013346-test-gate.log` → **`exit: 0` no cabeçalho do log e `Com falha: 0, Aprovado: 438, Total: 438`** (S-22: lido dentro do log, não do wrapper; 4m35s); gate intermediário `20261112-012642` também 438/438. Escopo (`QA-0033-scope.log`): git baseline (`QA-0033-git-baseline.txt`, 10 entradas) vs final → **+10 artefatos desta QA/fluxo, 0 fora da allowlist, backend = 0 entradas**; mtime desde 01:05:33 em `backend/src`+`backend/tests` → **exatamente os 5 arquivos mutados, todos restaurados**; resíduo `QA-MUTATION` = **0**; SHA256 dos 5 = PRE do ledger | PASS |

## Comandos executados (todos nesta sessão, com exit real; datas vêm dos próprios logs)

| # | comando | exit | artefato / saída |
| --- | --- | --- | --- |
| 1 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~Squid.Tests.Threads\|FullyQualifiedName~SchemaMigrationTests"` (baseline; na linha de evidência do task file o separador aparece como `+` porque o `sdd-state` recusa `\|` em argumento — o log gravado traz o comando real) | 0 | `20261112-010646-test-filter.log` — `exit: 0`, Aprovado 9, Com falha 0 |
| 2 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0033.ps1` (12 ciclos can-fail) | 1 | `QA-0033-mutations.log` — M1..M9+M4A..M4D vermelhos **classificados dentro dos logs**, GREEN 9/9; exit 1 é o próprio ledger marcando `failed=1` (M4D quebrou o **build**, ver finding 1) |
| 3 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0033.ps1 -Only M4D` (mutante corrigido) | 0 | `QA-0033-mutations.log` — M4D vermelho no log `20261112-011929` (2 falhas) + GREEN `011950` |
| 4 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` (gate pós-mutações) | 0 | `20261112-012642-test-gate.log` — 438/438 |
| 5 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0033.ps1 -Only M10` (CSRF, fora dos touches, restaurado byte-exact) | 0 | `QA-0033-mutations.log` — M10 vermelho no log `20261112-012720` + restore `hash=PRE` + GREEN `012742` |
| 6 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` (gate final AC-05) | 0 | `20261112-013346-test-gate.log` — `exit: 0` + `Aprovado: 438, Com falha: 0` lidos **dentro do log** (S-22) |
| 7 | varredura de escopo: `git status --porcelain -uall` vs baseline + SHA256 dos 5 mutados + mtime desde 01:05:33 + resíduo `QA-MUTATION` + TEMP | 0 | `docs/sdd/qa/QA-0033-scope.log` — backend 0, 5/5 = PRE, mtime = os 5, resíduo 0, backups 0 |
| 8 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0033.ps1` | 0 | `docs/sdd/qa/QA-0033-check.log` — SUMMARY total=16 fails=0 |
| 9 | `wsl -e bash -lc bash docs/sdd/qa/qa-check-task-0033.sh` (gêmeo Linux, parte estática) | 0 | `docs/sdd/qa/QA-0033-check-sh.log` — SUMMARY total=13 fails=0 |

## Ciclos de mutação (can-fail; S-16 SHA256 PRE, S-22 classificação no log, S-23 restore byte-exact + mtime)

| Ciclo | Arquivo (12 nos `touches`; M10 declaradamente fora) | O que prova | Vermelho classificado NO LOG |
| --- | --- | --- | --- |
| M1 | `Squid.Service/SqliteThreadStore.cs` | AC-01/GAP-29: CAS do `version` removido → dois emissores tomam a mesma `seq` | `ThreadStoreTests.The_message_seq... [FAIL]` (`20261112-011118`) |
| M2 | `Squid.Persistence/schema/SchemaMigrator.cs` | AC-01: o skip da versão registrada some → a re-aplicação não é vazia | 3 falhas: `ThreadMigrationTests.Ac01`, `SchemaMigrationTests.Ac01`, `SchemaMigrationTests.A_text_that_changed...` (`011142`) |
| M3 | `Squid.Persistence/schema/0002_threads.sql` | AC-01: conteúdo da migration (índice que a listagem usa) é lido do artefato | `ThreadMigrationTests.Ac01 [FAIL]` (`011205`) |
| M4A | `Squid.Api/Threads/ThreadEndpoints.cs` | AC-02: `direct` exatamente 2 (teto) | `A_direct_thread... [FAIL]` (`011228`) |
| M4B | idem | AC-02: portão da faixa (piso — 400 antes de gravar) | `A_group...` + `A_direct... [FAIL]` (`011250`) |
| M4C | idem | AC-02: teto 50 exato ainda cabe | `A_group... [FAIL]` (`011312`) |
| M4D | idem | AC-02: piso 2 exato (criador + 1) | 1ª tentativa quebrou o **build** (`011327`, CS1002 — finding 1); corrigido → `A_group...` + `The_list... [FAIL]` (`011929`) |
| M5 | idem | AC-02: criador conta no conjunto | 4 falhas das 4 rotas (`011349`) |
| M6 | idem | AC-02/GAP-30: criador do group = `admin` | `A_group... [FAIL]` (`011413`) |
| M7 | `Squid.Service/SqliteThreadStore.cs` | AC-03: isolamento por participante no `JOIN` | `The_list_answers_only... [FAIL]` (`011438`) |
| M8 | `Squid.Api/Threads/ThreadEndpoints.cs` | AC-04: id segue scheme+Host da requisição | `Both_routes_demand... [FAIL]` (`011501`) |
| M9 | idem | AC-04: 401 de sessão nas duas rotas | `Both_routes_demand... [FAIL]` (`011522`) |
| M10 | **`Squid.Api/Auth/AuthSessionMiddleware.cs` (fora dos `touches` — declarado)** | AC-04: portão CSRF (só ele faz o 403) | `Both_routes_demand... [FAIL]` (`012720`) |

Todos: `PRE` registrado antes, `RESTORE-OK hash=PRE residue=0` depois (14 no ledger), mtime bumpado (S-23) e verde logo em seguida — 13 ciclos vermelhos de propósito + 1 mutante descartado e re-executado. GREENs: `011545`, `011950`, `012742` (9/9, exit 0). O run nº 2 do driver executou os 12 primeiros ciclos; **M10 foi acrescentado ao script depois** e rodou isolado por `-Only M10` (por isso o ledger tem dois `SUMMARY` — o primeiro registra `failed=1`, o defeito do mutante M4D corrigido na linha seguinte).

## Findings

Nenhum achado de **produto** (0 BLOCKER, 0 MAJOR, 0 MINOR). Notas de fluxo/medição, não são defeito do entregue:

1. **M4D, 1ª tentativa: defeito do mutante da QA, não do produto** — o comentário `//` comeu o `;` da linha (CS1002 no build, log `20261112-011327`, `aprovado=0 falha=0`): o driver classificou `CLASSIFICACAO-ERRADA` em vez de ondular vermelho (comportamento correto, S-22). Mutante trocado por comentário de bloco, re-executado: vermelho real de produto (`011929`).
2. **M10 é a única edição fora dos `touches`** (o portão CSRF vive em `AuthSessionMiddleware.cs`, fora do escopo da task): declarado no driver, restaurado byte-exact (`F2F22F2269D0…` = o mesmo SHA256 registrado na TASK-0014), backend com **0** entradas no git e mtime batendo nos 5 arquivos esperados — AC-05 julgado pelo estado final, como manda o critério.
3. **S-22 monitorado, não disparou**: todos os pares wrapper/log do ledger com o mesmo exit (`wrapper=1 logExit=1`, `wrapper=0 logExit=0`); classificação feita sempre pelo cabeçalho `exit:` e resumo dentro do log gravado.
4. Pré-existente conhecido e **não** desta task: `docs/sdd/_tools/sdd-test.sh` não roda nesta solution (procura `*.sln`, o arquivo é `*.slnx`) — backlog S-22/S-11 já registrado.

## Não coberto / unverified

- **Suite de testes em Linux (WSL)**: `dotnet` ausente no WSL (sondado ao vivo nesta sessão: `command -v dotnet` → `no-dotnet`; instalação exige gate humano) e o gêmeo `sdd-test.sh` quebra na busca da solution → **unverified**, nunca assumido igual ao Windows. O gêmeo `.sh` do **checker** (parte estática: logs, hashes, resíduo, git) foi executado — linha 9.
- **CSRF sob carga/reativo**: o 403 é provado por 1 request sem header (M10 prova que o assert cai se o portão cair); não testo handshake alternativo/`SameSite` de navegador — risco baixo, já fixado pela TASK-0014.
- **`NextMessageSeqAsync` concorrente multi-processo**: o CAS é provado por duas chamadas concorrentes no mesmo processo + revisão do SQL (`UPDATE … WHERE version=@v RETURNING`); dois processos separados disputando o lock de arquivo não são exercitados aqui — risco: trava de arquivo do SQLite sob contenção real, coberto por `busy_timeout=30s` (código) e sem teste de carga nesta task.

## Reproduzir

```powershell
# pré-requisito: .NET 10 SDK + PowerShell no Windows, repo C:\dev\squid-squad-net\squid-execution
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test    # esperado: exit 0, Aprovado 438, Com falha 0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0033.ps1  # esperado: exit 0, SUMMARY residue-QA-MUTATION=0 (13 vermelhos de propósito)
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0033.ps1   # esperado: exit 0, SUMMARY total=16 fails=0
wsl -e bash -lc bash docs/sdd/qa/qa-check-task-0033.sh                                   # esperado: exit 0, SUMMARY total=13 fails=0
```

## Artefatos desta QA

| Arquivo | Papel |
| --- | --- |
| `docs/sdd/qa/qa-mutate-task-0033.ps1` | driver de mutação can-fail (13 ciclos, S-16/S-22/S-23 embutidos) |
| `docs/sdd/qa/QA-0033-mutations.log` | ledger: PRE, vermelhos no log, restore, GREENs, SUMMARYs |
| `docs/sdd/qa/QA-0033-git-baseline.txt` | git + mtime capturados antes da QA escrever |
| `docs/sdd/qa/QA-0033-scope.log` | varredura final de escopo (git, hashes, mtime, resíduo) |
| `docs/sdd/qa/qa-check-task-0033.ps1` / `.sh` | checkers de evidência (16 checks no ps1 / 13 checks no gêmeo Linux) |
| `docs/sdd/qa/QA-0033-check.log` / `QA-0033-check-sh.log` | execuções dos checkers |

memoryBankFlags: NENHUM — rota `qa-integracao` lida (`docs/memory-bank/index.md` + `build-run-test.md` via grep dirigido, regras S-22/S-23 confirmadas em execução real nesta sessão; `integrations.md`/`contracts.md` não são normativos para os ACs desta task e não foram relidos); nenhum padrão novo, divergência ou suspeito observado — o `[PADRAO_NOVO: agregado em tabela migrada com CAS na coluna version]` já vem carregado pelo ODEV da DEV.
