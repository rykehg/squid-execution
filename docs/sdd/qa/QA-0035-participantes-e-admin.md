<!--
templateVersion: 1.0.0
 -->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: RASCUNHO
- owner: qa
- lastVerified: (último artefato: docs/sdd/runs/test-runs/20261112-041813-test-gate.log)

# QA-0035 — Participantes e admin com sucessão do criador (TASK-0035)

- runId: R-20261111-01 · attempt: **1/2** (bloco de controle `qaAttempts: 0/2` — contador é do ODEV, QA não escreve nele)
- QA: subagente qa-task + política ponytail-qa (full) — testes EXECUTADOS nesta sessão, não escritos e deixados
- leitura normativa: SPEC-0010 §C + Gaps GAP-30/31/32/38/40 + AC-01..AC-06 da task + data-model §2.7 + contrato http-api §6/§8/§9 (item 11 da família `409 last_owner`)
- commit sob verificação: `aff9679` em `task/TASK-0035-participantes-e-admin` (5 arquivos = 3 produto + 2 teste, todos nas `touches`); baseline DEV `450/450`, gate desta QA idem
- verdict: **PASSED**

## AC → cenário (6 ACs; todos executados nesta sessão)

| AC | Cenário | Execução (destaque) | Resultado |
| --- | --- | --- | --- |
| AC-01 | `POST .../participants`: `Group` com teto 50, só admin; comum → `403`, não-participante → 404 byte-idêntico, `Direct` → `400`, >50 → `400` | `ThreadParticipantRoutesTests.Adding_a_seat...` verde no baseline `20261112-035540-test-filter.log` (**exit 0, 18/18**) e nos 2 GREENs. Can-fail: **M1** (ordem trocada: add em Direct por não-admin vira 403 antes do 400 → `040152`), **M2** (teto `>=`→`>`: grupo de 50 aceita o 51º → `040218`), **M3** (portão de participante desligado nas 3 rotas: fora-da-thread não cai mais no 404 idêntico → `040250`, 3 vermelhas) | PASS |
| AC-02 | Admin remove qualquer um **exceto o criador** (403); autônomo sai de si; último → `409 last_participant` (traceId); removido perde acesso imediato (404 nas rotas) byte-idêntico; histórico permanece | `ThreadParticipantRoutesTests.Removal_ends_access...` + `A_direct_thread_goes_from_two_to_one...` verdes + teste temporário da QA **`QaTask0035RemovedRoutesTests.A_removed_seat...`** (rota de participante: add/self-leave/admin do banco REMOVIDO = mesmo 404 byte-a-byte da rota ausente, sem traceId, sem handle) verde em `20261112-040053-test-filter.log` (**exit 0, 19/19**). Can-fail: **M5** (membro comum remove terceiro → `040328`), **M6** (criador removível por outro admin → `040354`), **M9** (409+`last_participant`→400+`last_owner` → `040510`), **M10** (fronteira `seats<=1`→`<=2`: penúltimo já recusado, Direct 2→1 e saída com 2 viram 409 → `040536`), **M4** (portão das rotas de mensagem desligado: removido lê e envia → `041039`, 3 vermelhas, inclui o 404 byte-idêntico do `ThreadMessageRoutesTests`), **M3** (rota de participante: o teste temporário da QA também cai — can-fail do teste novo) | PASS |
| AC-03 | `POST/DELETE .../admin`: qualquer admin (inclusive eleitos) promove/demove; criador não demovido nem removido por outro admin (`403`); não-admin → `403` | `ThreadParticipantRoutesTests.Any_admin_elects...` verde. Can-fail: **M7** (portão admin da rota desligado: membro comum promove → `040420`), **M8** (`!promote && false`: criador demovível em vez de 403 → `040445`); M6 acima prova o half remoção do criador | PASS |
| AC-04 | Sucessão (GAP-32): criador sai → admin de menor `joined_at` assume; sem admin → membro de menor `joined_at` vira admin+criador; `Group` com ≥1 participante tem ≥1 admin | `ThreadParticipantRoutesTests.The_creator_leave_hands...` (2 estradas, relógio empatado → desempate `rowid`) + `ThreadStoreTests.The_succession_ranks_by_joined_at...` (relógios explícitos) verdes. Can-fail: **M11** (`ORDER BY joined_at DESC, rowid DESC` nas 2 queries da sucessão → `040602`: o teste do store cai pelo `joined_at` e o road-2 das rotas cai pelo `rowid` — empate de relógio decide) + M6/M8 acima (invariante: criador protegido) | PASS |
| AC-05 | Participante adicionado depois enxerga o histórico inteiro (GAP-40) | `ThreadParticipantRoutesTests.A_seat_added_later_reads_the_whole_history...` verde (seq 1,2 antes de entrar; 1,2,3 depois). Can-fail: **M13** (filtro de `joined_at` do leitor no `GET .../messages`: late joiner deixa de ver seq 1,2 → `040657`) | PASS |
| AC-06 | `sdd-test.ps1 test` exit 0; nada alterado fora dos `touches` | Gate `20261112-041813-test-gate.log` → **`exit: 0` no cabeçalho e `Com falha: 0, Aprovado: 450, Total: 450`** (S-22: lido dentro do log; 4m37s). Escopo (`QA-0035-scope.log`): `git status -- backend` **0 entradas**; blob-identity `git hash-object` = `git rev-parse HEAD:` em **2/2** arquivos mutados (`ThreadEndpoints.cs`, `SqliteThreadStore.cs`); mtime desde a baseline `03:54:59` = **exatamente esses 2** (restaurados); resíduo `QA-MUTATION` = **0**; teste temporário da QA removido; novidades no git só em `docs/sdd/qa/*` (+ o task file já modificado pelo ODEV antes da QA) | PASS |

Extra de verificação do bug pré-existente (lista que colapsa no chamador): **M12** muta o semi-join de `GET /api/threads` de volta para `AND p.actor_id = @ActorId` — **os testes do DEV pegam** (`040627`: `Adding_a_seat` na igualdade dos 3 nomes + `A_seat_added_later` na contagem 3); nenhum teste novo foi necessário.

## Comandos executados (todos nesta sessão, com exit real; datas vêm dos próprios logs)

| # | comando | exit | artefato / saída |
| --- | --- | --- | --- |
| 1 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Threads` (baseline pré-QA) | 0 | `20261112-035540-test-filter.log` — `exit: 0`, Com falha 0 / Aprovado 18 / Total 18 |
| 2 | idem (com o teste temporário da QA na árvore — prova que o teste novo compila e passa no código limpo) | 0 | `20261112-040053-test-filter.log` — `exit: 0`, 19/19 |
| 3 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0035.ps1 -VerifyOnly` (contagem dos 13 padrões + BOM) | 0 | stdout `VERIFY total=13 bad=0` + `BOM OK` |
| 4 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0035.ps1` (execução cheia: 13 ciclos) | 1 | `QA-0035-mutations.log` — 12 vermelhos classificados no log + M4 v1 anômalo (`CLASSIFICACAO-ERRADA`: build CS1026 do mutante, não do produto); exit 1 = `failed=1` do ledger |
| 5 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0035.ps1 -Only M4` (mutante v2, sem o comentário que quebrava o build) | 0 | `QA-0035-mutations.log` — M4 vermelho `20261112-041039` (3 falhas) + GREEN `041105` 19/19 + `SUMMARY cycles=1 failed=0` |
| 6 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` (gate AC-06) | 0 | `20261112-041813-test-gate.log` — `exit: 0` + `Aprovado: 450, Com falha: 0` lidos **dentro do log** (S-22) |
| 7 | varredura de escopo (pwsh): `git status -uall`, `git status -- backend`, `git hash-object` vs `git rev-parse HEAD:`, mtime desde 03:54:59, resíduo, teste temporário, backups TEMP | 0 | `docs/sdd/qa/QA-0035-scope.log` — backend 0, blob-identity 2/2, mtime 2, resíduo 0, temp ausente |
| 8 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0035.ps1` | 0 | `docs/sdd/qa/QA-0035-check.log` — SUMMARY total=27 fails=0 |
| 9 | `wsl -e bash docs/sdd/qa/qa-check-task-0035.sh` (gêmeo Linux, parte estática) | 0 | `docs/sdd/qa/QA-0035-check-sh.log` — SUMMARY total=24 fails=0 |

## Ciclos de mutação (can-fail; S-16 SHA256 PRE, S-22 classificação no log, S-23 restore byte-exact + mtime)

Todos nos `touches` (`Squid.Api/Threads/**`, `Squid.Service/**`) — nenhum edit fora do escopo. Filtro: `FullyQualifiedName~Squid.Tests.Threads` (18 testes do DEV + 1 da QA enquanto esteve na árvore).

| Ciclo | Arquivo | O que prova | Vermelho classificado NO LOG |
| --- | --- | --- | --- |
| M1 | `Squid.Api/Threads/ThreadEndpoints.cs` | AC-01: `Direct → 400` ANTES do 403-admin (ordem da escada) | `Adding_a_seat... [FAIL]` (`040152`) |
| M2 | idem | AC-01: fronteira do teto 50 (`>=`→`>`: o 51º entra) | `Adding_a_seat... [FAIL]` (`040218`) |
| M3 | idem | AC-01/02/03: portão de participante das 3 rotas (fora-da-thread e removido) | 3 falhas: `Any_admin_elects`, `Adding_a_seat`, `QaTask0035.A_removed_seat...` (`040250`) |
| M4 v1 | idem | (quebrou o BUILD — `CS1026` no próprio mutante: `//` comeu o `)` da linha; `CLASSIFICACAO-ERRADA` do driver em vez de vermelho — S-22 funcionando, mutante descartado) | — (anômalo, documentado) |
| M4 v2 | idem | AC-02: acesso imediato perdido nas rotas de mensagem (ler + escrever) | 3 falhas: `Removal_ends...`, `Direct 2→1...`, `ThreadMessageRoutesTests.A_non_participant...` (`041039`) |
| M5 | idem | AC-02: membro comum nunca remove terceiro | `Removal_ends... [FAIL]` (`040328`) |
| M6 | idem | AC-02/04: criador protegido de remoção por outro admin | `Removal_ends...` + `The_creator_leave...` (`040354`) |
| M7 | idem | AC-03: só admin muda papel (não-admin → 403) | `Any_admin_elects... [FAIL]` (`040420`) |
| M8 | idem | AC-03: criador não é demovido nem por outro admin | `Any_admin_elects...` + `The_creator_leave...` (`040445`) |
| M9 | idem | GAP-38/§9.11: `409 last_participant` (status + código) | `Removal_ends...` + `Direct 2→1...` (`040510`) |
| M10 | `Squid.Service/SqliteThreadStore.cs` | AC-02: fronteira `seats <= 1` — o penúltimo SAI | 3 falhas: `Direct 2→1...`, `Removal_ends...`, `QaTask0035.A_removed_seat...` (`040536`) |
| M11 | idem | AC-04: sucessão `ORDER BY joined_at, rowid` (as 2 queries) | `ThreadStoreTests.The_succession_ranks...` (joined_at) + `The_creator_leave...` (rowid no empate) (`040602`) |
| M12 | idem | Lista cheia do `GET /api/threads`: semi-join de volta ao bug `p.actor_id = @ActorId` | `Adding_a_seat...` (igualdade 3 nomes) + `A_seat_added_later...` (contagem 3) (`040627`) — **o suite do DEV pega o bug** |
| M13 | `Squid.Api/Threads/ThreadEndpoints.cs` | AC-05/GAP-40: histórico do leitor começa no `joined_at` | `A_seat_added_later... [FAIL]` (`040657`) |

14× `PRE` registrado, 14× `RESTORE-OK hash=PRE residue=0`, 0× `RESTORE-FAIL`, 2× GREEN `19/19 exit 0` (`040726`, `041105`), resíduo final `QA-MUTATION` = 0, backups TEMP `qa0035-*` = 0.

## Findings

Nenhum achado de **produto** (0 BLOCKER, 0 MAJOR, 0 MINOR). Notas de QA/fluxo:

| # | Severidade | Onde | O que | Evidência |
| --- | --- | --- | --- | --- |
| 1 | NOTA (mutante da QA) | `qa-mutate-task-0035.ps1` ciclo M4 v1 | O edit inseria `// QA-MUTATION-M4` logo após `(`, o comentário comia o restante da linha e o build falhava (`CS1026` em `ThreadEndpoints.cs(256,53)`) — NÃO é vermelho de produto; o driver classificou `CLASSIFICACAO-ERRADA` (S-22) em vez de ondular; mutante redesenhado (v2, sem o comentário) e vermelho em `041039` | `QA-0035-mutations.log` + `20261112-040259-test-filter.log` |
| 2 | NOTA (BOM) | driver (esta QA) | Editar `qa-mutate-task-0035.ps1` removeu o UTF-8 BOM (mesma perda da QA-0034) — restaurado antes da re-execução; o `-VerifyOnly` agora também acusa BOM | linha 3 da tabela de comandos |
| 3 | NOTA (pré-existente) | `docs/sdd/_tools/sdd-test.sh` | Gêmeo Linux não resolve `*.slnx` + `dotnet` ausente no WSL (sondado ao vivo nesta sessão: `command -v dotnet` → missing; instalação exige gate humano) — mesmo achado das QA-0033/0034; gate segue no Windows; o gêmeo `.sh` do checker (parte estática) foi executado | comando da linha 9 abaixo |
| 4 | NOTA (cobertura) | `ThreadEndpoints.cs:500` (ramo `LastParticipant`) | O `409 last_participant` carrega `traceId` por default (`AuthErrors.cs:15` `withTraceId = true`) — nenhum teste afirma a PRESENÇA do traceId no 409 (os testes fixam status 409 + código); revisado no código | revisão de código, sem assert |

## Não coberto / unverified

- **Suite de testes em Linux (WSL)**: `dotnet` ausente no WSL (sondado ao vivo nesta sessão: `command -v dotnet` → not found; instalação exige gate humano) e o gêmeo `sdd-test.sh` quebra na busca da solution → **unverified**, nunca assumido igual ao Windows. O gêmeo `.sh` do **checker** (parte estática: logs, resumos, hashes, git) foi executado — comando 9.
- **Presença do `traceId` no corpo do 409**: comportamento revisado no código (`AuthErrors.cs:15`) mas sem assert — risco baixo (o default é da família de erros já testada).
- **Contenção multi-processo do SQLite nas rotas de participante**: remoções concorrentes disputando o `BEGIN IMMEDIATE` não rodam aqui (testes de cliente único); coberto por revisão + `busy_timeout=30s` no código — mesmo limite das QA-0033/0034.
- **Corrida add×add no teto 50**: a recusa do 51º é determinística (assertada), mas duas requests simultâneas no teto não foram executadas — risco baixo (a contagem corre sob o mesmo `FindAsync` + insert primário).
- **Fora de escopo por design**: 428 do aviso (TASK-0036), `replyToId` (0037), reações (0038), anexos (0039).

## Reproduzir

```powershell
# pré-requisito: .NET 10 SDK + PowerShell no Windows, repo C:\dev\squid-squad-net\squid-execution
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test       # esperado: exit 0, Aprovado 450, Com falha 0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0035.ps1 -VerifyOnly   # esperado: exit 0, VERIFY total=13 bad=0 + BOM OK
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0035.ps1     # run fresco: exit 0, SUMMARY cycles=13 failed=0 (13 vermelhos de propósito); o ledger ARMAZENADO guarda a anomalia M4 v1 da 1ª execução
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0035.ps1      # esperado: exit 0, SUMMARY fails=0
wsl -e bash docs/sdd/qa/qa-check-task-0035.sh                                               # esperado: exit 0, SUMMARY fails=0
```

## Artefatos desta QA

| Arquivo | Papel |
| --- | --- |
| `docs/sdd/qa/qa-mutate-task-0035.ps1` | driver de mutação can-fail (13 ciclos, `-VerifyOnly` com checagem de BOM, S-16/S-22/S-23 embutidos) |
| `docs/sdd/qa/QA-0035-mutations.log` | ledger: PRE, vermelhos no log, restore, GREENs, SUMMARYs (2 execuções) |
| `docs/sdd/qa/qa-check-task-0035.ps1` / `.sh` | checkers de evidência (estáticos; gêmeo Linux sem o sdd-state, que é ferramenta Windows) |
| `docs/sdd/qa/QA-0035-check.log` / `QA-0035-check-sh.log` | execuções dos checkers |
| `docs/sdd/qa/QA-0035-git-baseline.txt` | git + HEAD capturados antes da QA escrever (03:54:59) |
| `docs/sdd/qa/QA-0035-scope.log` | varredura final de escopo (git, blob-identity, mtime, resíduo) |
| `backend/tests/Squid.Tests/Threads/QaTask0035RemovedRoutesTests.cs` | teste temporário da QA (AC-02 nas rotas de participante) — **executado e REMOVIDO** após os ciclos, como o probe da QA-0034; prova viva nos logs `040053` (verde), `040250`/`040536` (vermelho) |

memoryBankFlags: NENHUM — rota `qa-integracao` lida (`docs/memory-bank/index.md` + `build-run-test.md#Local tests without gate`: S-15/S-22/S-23 confirmados **em execução real** nesta sessão; `contracts.md`/`integrations.md` não são normativos para os ACs — o contrato `specs/001-squid-mvp/contracts/http-api.md` §6/§8/§9 e o data-model §2.7 foram lidos); nenhum padrão novo, divergência ou suspeito observado — a flag `MUDANCA_RELEVANTE CONTRATO` (contracts.md sem mensageria) já vem carregada pelo ODEV no Log da task, e o bug do semi-join (corrigido dentro dos touches pelo DEV) está registrado no Log da task (linha do ODEV de 03:46:43).
