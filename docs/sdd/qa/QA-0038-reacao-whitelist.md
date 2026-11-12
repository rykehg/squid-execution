<!--
templateVersion: 1.0.0
 -->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: RASCUNHO
- owner: qa
- lastVerified: (último artefato: docs/sdd/runs/test-runs/20261112-083213-test-gate.log)

# QA-0038 — Reação com whitelist e idempotência (TASK-0038)

- runId: R-20261111-01 · attempt: **1/2** (bloco de controle `qaAttempts: 0/2` — contador é do ODEV, QA não escreve nele)
- QA: subagente qa-task + política ponytail-qa (full) — testes EXECUTADOS nesta sessão, não escritos e deixados
- leitura normativa: SPEC-0010 §F + GAP-34 e suposição do GAP-37 do task file + AC-01..AC-05 + FR-058 (`specs/001-squid-mvp/spec.md:232`) + data-model §2.14 (`data-model.md:289-302`) + contrato http-api §6 (linhas 133/134/154) e §9 item 10 (linha 200) + memória banc rota `qa-integracao`
- commit sob verificação: `8903e67` em `task/TASK-0038-reacao-whitelist` (9 arquivos = 6 produto + 3 teste, todos nas `touches`); baseline DEV `462/462 → 468/468`
- verdict: **PASSED**

## AC → cenário (5 ACs; todos executados nesta sessão)

| AC | Cenário | Execução (destaque) | Resultado |
| --- | --- | --- | --- |
| AC-01 | `POST .../reactions` da whitelist cria a reação; repetir a MESMA (mensagem, ator, emoji) é idempotente (mesma linha, contagem não muda); um segundo emoji DIFERENTE do mesmo ator é outra linha | `ThreadReactionRoutesTests.The_same_message_actor_emoji_is_one_row_and_repeating_it_changes_nothing` verde no baseline `20261112-075557-test-filter.log` (**exit 0, 36/36**), no verde com o teste da QA `080317` (**38/38**), no GREEN do driver `081404` e no gate `083213` (468/468). O mesmo teste prova de passagem a decisão (1) do DEV: o par **sem aceite** envia `428` e reage `200` (§D amarra o 428 ao ENVIO). Can-fail: **M1** (sem `ON CONFLICT DO NOTHING` → a repetição estoura a chave e responde 500 → `080439`, 3 vermelhas), **M2** (chave da 0006 reduzida a `(message_id, actor_id)` + `ON CONFLICT` correspondente → o segundo emoji do mesmo ator não nasce → `080533`, 5 vermelhas incl. rota e config) | PASS |
| AC-02 | emoji fora de `Reactions:AllowedEmojis` → `422 invalid_emoji` (POST, DELETE e valor vazio), nada gravado; **mudar a config muda o conjunto aceito**; 👍 sempre presente | `ThreadReactionRoutesTests.An_emoji_outside_Reactions_AllowedEmojis_is_422_and_never_reaches_a_row` (422 com `traceId` no corpo da casa, recusa não envenena, DELETE na mesma escada, `?emoji=` vazio = a mesma palavra, linhas intactas) + `ThreadReactionWhitelistConfigTests.Changing_Reactions_AllowedEmojis_changes_what_the_reaction_route_accepts` (host com `Reactions:AllowedEmojis:0=🚀`: 🚀 200 / 🎉 422 / 👍 200 e leitura derivada) verdes nos mesmos quatro momentos. **Re-read por requisição** — o fato que os testes do DEV NÃO cobriam — coberto pelo teste temporário da QA: `QaTask0038ReactionRereadTests.Mutating_the_config_source_changes_the_accepted_set_without_a_restart` muta a **FONTE** (variável de ambiente `Reactions:AllowedEmojis:0` + `Reload()`, nunca o código nem restart): 🚀 200 → fonte vira 🎉 → 🚀 422 e 🎉 200 na PRÓXIMA requisição, 👍 200 sempre (GAP-34). Can-fail: **M3** (`Allows` sempre `true` → o 422 some → `080627`, 3 vermelhas), **M6** (config cacheada por processo → a mutação da fonte não surte efeito → `080852`, 4 vermelhas incl. o Fact A) | PASS |
| AC-03 | `DELETE ...?emoji=` remove; remover inexistente → `404`; contagem é DERIVADA da existência das linhas (reconciliada, nunca fonte) | `ThreadReactionRoutesTests.Deleting_takes_the_actor_row_the_count_follows_the_rows_and_a_missing_one_is_404` (DELETE apaga **só a linha do dono**, a do par sobrevive e `mine` troca, contagem cai 2→1; remover de novo → 404 sem `traceId`; nunca reagido → 404 byte-idêntico) + `ThreadMigrationTests.The_0006_version_creates_the_reaction_table_and_reapplying_changes_nothing` (colunas exatas da 0006 **sem coluna de contagem alguma**, `message` segue com os 7 campos da 0005, tríade repetida recusada pela própria chave, re-aplicação idempotente) verdes nos mesmos quatro momentos. Can-fail: **M7** (DELETE sem `actor_id` come a linha do par → `080949`, 1 vermelha), **M4** (contador `reaction_count` VÁLIDO somado à 0006 → a migration pega qualquer contador: última execução `082449` **aprovado=37 falha=1** só em `ThreadMigrationTests` com `RED-DETAIL Collections differ`; as duas primeiras execuções do mutante foram SQL INVÁLIDO — ver Findings 3) | PASS |
| AC-04 | não-participante recebe **404 idêntico** sem contagem em GET da mensagem e nas DUAS rotas de reação (§9.10) | `ThreadReactionRoutesTests.A_stranger_gets_one_404_body_on_read_and_on_both_reaction_routes` (6 casos — GET/POST/DELETE do estrangeiro, POST com emoji FORA da lista, GET/POST de thread que nunca existiu — um corpo só byte-a-byte, sem `traceId`/ids/handle/`reactions`/`count`/`emoji`; dono reagindo à mensagem de OUTRA thread recebe o MESMO corpo; dentro da parede nada muda) verdes nos mesmos quatro momentos. **GET na rota de reação fica no 404** (fato do dispatch que nenhum teste do DEV cobria) — coberto pelo Fact B da QA: `The_reaction_ladder_answers_401_first_and_GET_stays_the_shared_404` (GET `.../reactions` do participante = 404 byte-idêntico ao GET de thread ausente, sem `traceId`; e **401 primeiro**: sessão nula com emoji 🚀 responde `401 not_authenticated`, nunca 422/404). Can-fail: **M5** (branco validado ANTES da sonda → o estrangeiro com 🚀 aprende 422 → `080756`, `Assert.All` acusa o item [3] = 422), **M8** (parede de participantes desligada na sonda → o estrangeiro reage `200` → `081038`, item [1] = 200), **M9** (404 com `traceId` → `081130`, 9 vermelhas em 3 suites), **M10** (GET aberto na rota → `081222`, Fact B `Expected: NotFound`), **M11** (401 trocada por 404 → `081312`, Fact B `Expected: Unauthorized`) | PASS |
| AC-05 | `sdd-test.ps1 test` exit 0; nenhum arquivo alterado fora das `touches` | Gate `20261112-083213-test-gate.log` → **`exit: 0` DENTRO do cabeçalho e `Com falha: 0, Aprovado: 468, Total: 468`** (S-22; 4m57s). Escopo (`QA-0038-scope.log`): `git status -- backend` **0 entradas** (o teste temporário da QA REMOVIDO, fonte em `.cs.txt`); blob-identity `git hash-object` = `git rev-parse HEAD:` em **4/4** arquivos mutados; mtime desde a baseline `07:52` = **exatamente esses 4**; resíduo `QA-MUTATION` = 0; backups TEMP `qa0038-*` = 0; `fora-dos-touches` = **0** (só task file + artefatos `docs/sdd/qa/`) | PASS |

## As duas decisões do DEV (reclamações de nível spec tratadas como afirmações a VERIFICAR)

| # | Afirmação | Julgamento desta QA | Evidência |
| --- | --- | --- | --- |
| 1 | Reação **não** passa pelo gate 428 | **CONFIRMADA.** Normativo: SPEC-0010 §D diz "**envio** sem aceite → 428" e a suposição em força do task file (GAP-37 da lista de Gaps) fixa "gate 428 é só de envio (TASK-0036)"; §6 do contrato amarra o 428 a `POST .../messages`. Em execução: a AC-01 do DEV envia o par sem aceite → `428`, e o MESMO par reage → `200` (verde nos 4 momentos + gate) | `ThreadReactionRoutesTests.cs:57-65`; verdes `075557`/`080317`/`081404`/`083213` |
| 2 | Ator suspenso **PODE** reagir | **POSICÇÃO REGISTRADA, não coberta por teste — e as ACs não a exigem.** A regra §3.1 do data-model e a linha "ator Suspended" de SPEC-0010 (Rules) valem para o **envio** de mensagem ("negado e registrado" no ENVIO); TASK-0036 AC-05 é de envio. Nenhuma AC-01..05 de TASK-0038 pede teste de suspensão e a rota de reação sequer lê `IActorStatusStore`. Decisão da QA: **não inventar AC** e não executar o cenário (exigiria host com `Instance:Operators` + rota admin) — fica em "Não coberto" com o risco declarado | SPEC-0010 Rules (§3.1 linha 156); `ThreadEndpoints.cs:510-537` (nenhum status check); task AC list |

## Comandos executados (todos nesta sessão, com exit real; datas vêm dos próprios logs)

| # | comando | exit | artefato / saída |
| --- | --- | --- | --- |
| 1 | `git rev-parse HEAD` + `git rev-parse --abbrev-ref HEAD` + `git status --porcelain -uall` + `wsl -e bash -c "command -v dotnet"` (pwsh) | 0 | `docs/sdd/qa/QA-0038-git-baseline.txt` — branch `task/TASK-0038-reacao-whitelist`, HEAD `8903e67`, só o task file do ODEV/DEV, **dotnet ausente no WSL** |
| 2 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Threads` (baseline) | 0 | `20261112-075557-test-filter.log` — `exit: 0`, Com falha 0 / Aprovado 36 / Total 36 |
| 3 | `Copy-Item docs/sdd/qa/QaTask0038ReactionRereadTests.cs.txt backend/tests/Squid.Tests/Threads/` + mesmo filtro (verde COM o teste temporário da QA) | 0 | `20261112-080317-test-filter.log` — `exit: 0`, 38/38 (36 DEV + 2 QA) |
| 4 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0038.ps1 -VerifyOnly` (2×, antes de cada versão corrigida do driver) | 0 | stdout `VERIFY total=11 bad=0` + `BOM OK (UTF-8 BOM presente)` — os 14 padrões batem `hits=1` nas fontes pristinas |
| 5 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0038.ps1` (execução cheia: 11 ciclos + GREEN) | 0 | `QA-0038-mutations.log` — 11 vermelhos classificados NO LOG (S-22), 13× `RESTORE-OK hash=PRE residue=0`, `GREEN ... aprovado=38 falha=0` (`081404`), `SUMMARY cycles=11 failed=0 residue=0 temp-backups=0` |
| 6 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0038.ps1 -Only M4` (1ª correção — vírgula ainda dentro do `--`) | 0 | ledger: 38 vermelhas SQL inválido no `20261112-081728-test-filter.log` (`near "("`) — mutante da QA corrigido, não contado como prova |
| 7 | `python sqlite3 executescript` do SQL mutado (sonda offline) + `powershell ... -Only M4` (2ª correção, SQL válido) | 0 | sonda `SQLITE-OK`; `20261112-082449-test-filter.log` — **aprovado=37 falha=1**, só `ThreadMigrationTests.The_0006_version...` (`RED-DETAIL Collections differ`), `GREEN 38/38` (`082537`), `SUMMARY cycles=1 failed=0` |
| 8 | `Remove-Item backend/tests/Squid.Tests/Threads/QaTask0038ReactionRereadTests.cs` + `git status --porcelain -uall` | 0 | teste temporário fora da árvore (fonte preservada no artefato `.cs.txt`); git = só task file + 6 artefatos `docs/sdd/qa/` |
| 9 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` (gate AC-05, árvore igual à do DEV) | 0 | `20261112-083213-test-gate.log` — `exit: 0` lido **dentro do log** (S-22) + `Com falha: 0, Aprovado: 468, Total: 468`, 4m57s |
| 10 | varredura de escopo (pwsh): `git status`, `git hash-object` vs `git rev-parse HEAD:`, mtime desde a baseline, resíduo, fora-dos-touches | 0 | `docs/sdd/qa/QA-0038-scope.log` — git-backend 0, blob 4/4 `match=True`, `mtime-count=4`, `residue=0`, `fora-dos-touches=0` |
| 11 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0038.ps1` | 0 | `docs/sdd/qa/QA-0038-check.log` — SUMMARY total=39 fails=0 (SDD-STATE `check: OK - log entries 20, evidence rows 20`; CATCH-M6 lê o log de run do M6 porque o xUnit enrolou o nome comprido do Fact A) |
| 12 | `wsl -e bash docs/sdd/qa/qa-check-task-0038.sh` (gêmeo Linux, parte estática) | 0 | `docs/sdd/qa/QA-0038-check-sh.log` — SUMMARY total=34 fails=0 (o `sdd-state` é ferramenta Windows, fica só no ps1) |

## Ciclos de mutação (can-fail; S-16 SHA256 PRE, S-22 classificação no log, S-23 restore byte-exact + mtime)

Todos nos `touches` (`Squid.Api/Threads/**`, `Squid.Service/**`, `Squid.Persistence/schema/**`) — nenhum edit fora do escopo, e os 4 arquivos finais são byte-iguais ao `HEAD` (blob-identity 4/4). Filtro de todos os ciclos: `FullyQualifiedName~Squid.Tests.Threads` (38 testes com o temporário da QA).

| Ciclo | Arquivo(s) | O que prova | Vermelho classificado NO LOG |
| --- | --- | --- | --- |
| M1 | `Squid.Service/SqliteThreadStore.cs` | AC-01: a idempotência da MESMA tríade vive no `ON CONFLICT DO NOTHING` — sem ele a repetição estoura a chave da 0006 | `The_same_message_actor_emoji...` + `A_stranger_gets_one_404...` (repete no fim) + Fact A (`080439`, 3) |
| M2 | `schema/0006_message_reaction.sql` + `SqliteThreadStore.cs` (2 arquivos, um ciclo) | AC-01: **a única é a TRINCA** — chave reduzida a `(message_id, actor_id)` + `ON CONFLICT` correspondente → o segundo emoji DIFERENTE do mesmo ator é engolido em silêncio | config `Changing_Reactions_AllowedEmojis...` + `Deleting_takes_the_actor_row...` + `The_same_message_actor_emoji...` (`080533`, 5) |
| M3 | `Squid.Api/Threads/ReactionOptions.cs` | AC-02: o `422 invalid_emoji` é o próprio `Allows()` — sem ele POST/DELETE/vazio passam | `An_emoji_outside_Reactions_AllowedEmojis...` + config + Fact A (`080627`, 3) |
| M4 | `schema/0006_message_reaction.sql` | AC-03/nunca fonte: **qualquer coluna de contagem** somada à 0006 morre na migration (lista `pragma_table_info` exata), com as rotas verdes (o INSERT nomeia colunas) | `ThreadMigrationTests.The_0006_version_creates_the_reaction_table_and_reapplying_changes_nothing` — execução corrigida `082449`, **37/1**, `RED-DETAIL Collections differ` |
| M5 | `Squid.Api/Threads/ThreadEndpoints.cs` | AC-04/escada: a sonda de 404 vem ANTES da 422 — o estrangeiro com emoji inválido não pode aprender a whitelist | `A_stranger_gets_one_404...` — `Assert.All` falha no item [3] = **422** (`080756`, 1) |
| M6 | `ReactionOptions.cs` (3 edits) | AC-02/meio: `ReactionOptions.From(IConfiguration)` roda POR REQUISIÇÃO — cache por processo mata o re-read da fonte | Fact A `Mutating_the_config_source_changes_the_accepted_set_without_a_restart` + 3 rotas (`080852`, 4) |
| M7 | `SqliteThreadStore.cs` | AC-03: o DELETE leva **a linha do próprio ator** — sem `actor_id` ele come a linha do par e a contagem zera | `Deleting_takes_the_actor_row_the_count_follows_the_rows_and_a_missing_one_is_404` (`080949`, 1) |
| M8 | `SqliteThreadStore.cs` | AC-04/§9.10: `MessageVisible` é a parede (thread + cadeira + mensagem) — sem a cadeira o não-participante reage `200` | `A_stranger_gets_one_404...` — `Assert.All` falha no item [1] = **200** (`081038`, 1) |
| M9 | `ThreadEndpoints.cs` | AC-04/SC-005: o 404 compartilhado **sem traceId** — as seis recusas continuam byte-idênticas | 9 falhas em 3 suites: `A_stranger_gets_one_404...` + `Deleting_takes...` (reação) + `ThreadReplyRoutesTests.A_valid_reply_never_widens...` + `ThreadParticipantRoutesTests...` (`081130`) |
| M10 | `ThreadEndpoints.cs` | contrato §6: o GET da rota de reação não tem método — fica no 404 do catch-all | Fact B `The_reaction_ladder_answers_401_first_and_GET_stays_the_shared_404` — `RED-DETAIL Expected: NotFound` (`081222`, 1) |
| M11 | `ThreadEndpoints.cs` | escada: **401 not_authenticated** é o primeiro degrau da rota de reação | mesma Fact B — `RED-DETAIL Expected: Unauthorized` (`081312`, 1) |

14× `PRE` registrado, 14× `RESTORE-OK hash=PRE residue=0`, 0× `RESTORE-FAIL`, 0× `CLASSIFICACAO-ERRADA`, 2× GREEN `38/38 exit 0` (`081404`, `082537`), resíduo final `QA-MUTATION` = 0, backups TEMP `qa0038-*` = 0.

## Findings

Nenhum achado de **produto** (0 BLOCKER, 0 MAJOR, 0 MINOR). Notas de QA/fluxo:

| # | Severidade | Onde | O que | Evidência |
| --- | --- | --- | --- | --- |
| 1 | NOTA (BOM) | driver (esta QA) | `qa-mutate-task-0038.ps1` foi escrito **sem UTF-8 BOM** (5ª ocorrência do mesmo defecto nas QA-0034..0037) — BOM adicionado ANTES de qualquer edição/execução, re-conferido após **cada** edição do arquivo e checado em dois lugares: `-VerifyOnly` (`BOM OK`) e os checkers (`DRIVER-BOM`) | comando 4 + `DRIVER-BOM` |
| 2 | NOTA (pré-existente) | `docs/sdd/_tools/sdd-test.sh` / WSL | gêmeo Linux não roda a suite: `wsl -e bash -c "command -v dotnet"` → `dotnet-missing` (**sondado ao vivo NESTA sessão**); instalação exige gate humano — mesmo achado das QA-0033..0037 | comando 1 + linha do Log |
| 3 | NOTA (auto-catch do mutante, S-22) | `qa-mutate-task-0038.ps1` ciclo M4 | O mutante de contador nasceu **SQL inválido DUAS vezes**: (a) coluna depois da `PRIMARY KEY` de tabela (0006 é constraint no fim) e (b) vírgula **dentro** do comentário `--` (a coluna ficou sem vírgula → `DEFAULT 0 PRIMARY KEY (` → `near "("`). As duas execuções vermelhas por completo (`080659`, `081728`) foram LIDAS no log (não no console) e denunciaram o erro; o driver foi corrigido, o SQL validado offline com `sqlite3` (`SQLITE-OK`) e o ciclo re-executado: agora a prova é cirúrgica — 1 vermelha só na migration, rotas verdes. Os ciclos inválidos NÃO contam como prova e ficam registrados no ledger | ciclos M4 (`080659`, `081728` → `082449`) |
| 4 | NOTA (alcance do mutante) | `ThreadEndpoints.cs` HiddenThread | desligar o `withTraceId: false` do 404 compartilhado derrubou **9 testes em 3 suites** (reação, resposta, participantes) — a identidade byte-a-byte do SC-005 é estrutural, não acidental deste teste de reação | ciclo M9 (`081130`) |
| 5 | NOTA (profundidade defensiva, código) | `SqliteThreadStore.cs:659-674` (`AddReactionAsync`) | a recusa da mensagem DENTRO da transação de add/remove (o "geméo defensivo" da sonda) **não foi isolada por mutação**: os ciclos M5/M8 mexem na sonda externa/ordem, e o geméo nunca chega a ser o único guarda que falha (o cross-thread ainda cai pela sonda). Cobertura por leitura + o fato de a AC-04 passar pelos dois — se a casa quiser prova de contrafactual, é mutante dedicado em task futura | código lido; nenhum ciclo |

## Não coberto / unverified

- **Suite de testes em Linux (WSL)**: `dotnet` ausente no WSL (sondado ao vivo nesta sessão: `command -v dotnet` → `dotnet-missing`; instalação exige gate humano) e o gêmeo `sdd-test.sh` quebra sem ele → **unverified**, nunca assumido igual ao Windows. O gêmeo `.sh` do **checker** (parte estática: logs, resumos, hashes, git) foi executado — comando 12.
- **Ator suspenso reagindo**: nenhuma AC exige (posição registrada acima); o cenário **não foi executado** — exigiria host com `Instance:Operators` + rota admin para suspender. Risco: se uma spec futura cobrar 403 de suspensão na rota de reação, isso vira AC nova, não silêncio desta QA.
- **Corrida de duas requisições da MESMA tríade ao mesmo tempo** (`BEGIN IMMEDIATE` + `ON CONFLICT`): os testes são de cliente único; a idempotência sequencial está provada (AC-01 + M1/M2) e a unicidade é da chave do esquema (migration test) — a corrida em si não foi executada (mesma limitação das QA-0033..0037).
- **Geméo defensivo da transação** (recusa da mensagem dentro de `AddReactionAsync`/`RemoveReactionAsync`): só leitura de código — ver Finding 5.
- **GET `.../reactions` de um não-participante**: o Fact B executou o do PARTICIPANTE (que já é 404) e o byte-a-byte com a thread ausente; o do estrangeiro cai no MESMO caminho (`GetTargetAsync` decide `route.Tail != Messages` antes de qualquer identidade) — por leitura, não separadamente executado. Risco: baixíssimo.

## Reproduzir

```powershell
# pré-requisito: .NET 10 SDK + PowerShell no Windows, repo C:\dev\squid-squad-net\squid-execution
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test       # esperado: exit 0, Aprovado 468, Com falha 0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0038.ps1 -VerifyOnly   # esperado: exit 0, VERIFY total=11 bad=0 + BOM OK
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0038.ps1     # run fresco: exit 0, SUMMARY cycles=11 failed=0 (11 vermelhos de propósito)
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0038.ps1      # esperado: exit 0, SUMMARY fails=0
wsl -e bash docs/sdd/qa/qa-check-task-0038.sh                                               # esperado: exit 0, SUMMARY fails=0
```

## Artefatos desta QA

| Arquivo | Papel |
| --- | --- |
| `docs/sdd/qa/qa-mutate-task-0038.ps1` | driver de mutação can-fail (11 ciclos, `-VerifyOnly` com checagem de BOM, S-16/S-22/S-23 embutidos, ciclo M4 corrigido documentado no próprio código) |
| `docs/sdd/qa/QA-0038-mutations.log` | ledger: PRE, vermelhos no log, restore, GREEN, SUMMARY (full + re-run do M4) |
| `docs/sdd/qa/QaTask0038ReactionRereadTests.cs.txt` | fonte do teste temporário da QA (re-read da FONTE de config + escada 401/GET-404) — **executado e REMOVIDO** da árvore após os ciclos; prova viva nos logs `080317` (verde), `080852`/`081222`/`081312` (vermelho) |
| `docs/sdd/qa/qa-check-task-0038.ps1` / `.sh` | checkers de evidência (estáticos; gêmeo Linux sem o sdd-state, que é ferramenta Windows) |
| `docs/sdd/qa/QA-0038-check.log` / `QA-0038-check-sh.log` | execuções dos checkers |
| `docs/sdd/qa/QA-0038-git-baseline.txt` | git + HEAD + sonda WSL capturados antes da QA escrever |
| `docs/sdd/qa/QA-0038-scope.log` | varredura final de escopo (gate, git, blob-identity, mtime, resíduo, fora-dos-touches) |

memoryBankFlags: `[GAP: suite Linux/WSL unverified - dotnet ausente na maquina, instalacao exige gate humano (pre-existente, sondado ao vivo nesta sessao; mesmo achado das QA-0033..0037)]` · `[PADRAO_NOVO: parede de reacao em duas camadas - sonda unica MessageVisible (thread + cadeira + mensagem) E recusa da mensagem DENTRO da transacao de add/remove; whitelist lida de IConfiguration POR REQUISICAO na rota - confirmados em execucao pelo M8/M6/Fact A, o geméo da transacao so por leitura - source: backend/src/Squid.Service/SqliteThreadStore.cs:620]` · restante NENHUM (novo) — rota `qa-integracao` lida (`docs/memory-bank/index.md` + `build-run-test.md#Local tests without gate`: S-15/S-22/S-23 confirmados **em execução real** nesta sessão; SPEC-0010 §D/§F/Rules, o data-model §2.14 e o contrato §6/§9.10 foram lidos diretamente das fontes normativas).
