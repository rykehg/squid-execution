<!--
templateVersion: 1.0.0
 -->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: RASCUNHO
- owner: qa
- lastVerified: (últimos artefatos: docs/sdd/qa/QA-0046-mutations.log [cycles=14 failed=0], docs/sdd/qa/QA-0046-ac05-cli.log e QA-0046-ac05-cli-sh.log [checks=5 fails=0 nas duas shells], docs/sdd/qa/QA-0046-scope.log e QA-0046-scope-sh.log [fora-dos-touches=0], gate 20261114-033801-test-gate.log [Com falha 0, Aprovado 554, Total 554], QA-0046-check.log [total=57 fails=0] e QA-0046-check-sh.log [total=34 fails=0])

# QA-0046 — PeerGrain: fila, backoff, circuit breaker, DLQ + `PeerClient` com falha (TASK-0046)

- runId: R-20261113-01 · attempt: **1/2** (bloco de controle `qaAttempts: 0/2` — contador é do ODEV, QA não escreve nele)
- QA: subagente qa-task + política ponytail-qa (full) — testes EXECUTADOS nesta sessão, não escritos e deixados
- leitura normativa: SPEC-0011 **§G (Peer: fila, resiliência e simulação de falha)** + data-model **§2.9 (`Peer`/`PeerDelivery`) e §3.4 (`PeerDelivery.State`)** + AC-01..AC-07 do task file + memória banc rota `qa-integracao` (`build-run-test.md#Local tests without gate`, `integrations.md`, `contracts.md`)
- commit sob verificação: `61c12ce` em `task/TASK-0046-peer-grain-fila-e-dlq` (**18 arquivos = produto nos touches + testes; NENHUM fora**; não pushado — `git branch -r --contains 61c12ce` vazio, `origin` só tem develop/main); baseline DEV própria lida no log `20261114-023158-test-gate.log` (**exit 0, 554/554**)
- entrega da QA dentro dos touches: **NENHUM arquivo novo em `backend/`** — os 15 cenários que a lista de AC exige já existem nos testes do DEV (11 `[Fact]` em `PeerDeliveryMotorTests` + 4 em `PeerGrainTests`, marcados `// AC-01`..`// AC-06`); a QA acrescenta apenas artefatos em `docs/sdd/qa/`
- natureza desta QA: **PRODUTO** — 7 ACs provadas por **14 mutações can-fail contra o PRODUTO**, restauradas byte-exact, + **probe vivo do CLI do `PeerClient` nas duas shells**, + varredura de escopo nas duas shells, + gate completo (AC-07)
- verdict: **PASSED**

## AC → cenário (7 ACs; tudo executado nesta sessão)

| AC | Cenário | Execução (destaque) | Resultado |
| --- | --- | --- | --- |
| AC-01 | entrega com 500 volta para `Pending` com `attempts++`, `NextAttemptAt` no futuro e backoff crescente | Verde na baseline (`20261114-024950-test-filter.log`, exit 0, 51/51) com `Ac01_a_500_goes_back_to_pending_with_attempts_and_a_growing_backoff`. Can-fail: **T1** (`attempts` travado no pump → **falha=4**, RED-AC = `Ac01...` + `Ac02_after_eight...` + os dois `Ac05`, que também contam tentativa) e **T2** (backoff sem duplicação e sem jitter → **falha=2**, RED-AC = `Ac01...` + `Backoff_doubles...` — janelas iguais derrubam `secondWindow > firstWindow`) | PASS |
| AC-02 | após 8 tentativas ou 4xx permanente a entrega vira `Failed` na DLQ com motivo e dispara alerta | Verde com `Ac02_after_eight_attempts...` (DLQ `max_attempts` + contador `federation.delivery_dlq`), `Ac02_a_permanent_404...` (`peer_rejected`) e `Ac02_a_401...` (`signature_rejected`). Can-fail nas QUATRO metades: **T3** (`MaxAttempts` 8→9 → **falha=1**, a linha `Assert.Equal(8, dead.Attempts)` cai), **T4** (contador de alerta do pump morto → **falha=1**), **T5** (contador de alerta do pipeline morto → **falha=1**, só o 404), **T6** (guarda de 4xx morta → **falha=2**, `404` e `401` viram retry em vez de DLQ) | PASS |
| AC-03 | circuito aberto → entrega nova **direto** para `Pending` com `NextAttemptAt` futuro (nunca falha na hora); backoff com teto de 6 h e jitter | Verde com `Ac03_an_open_circuit...` (`attempts=0`, `NextAttemptAt` igual ao `BackoffUntil` do par, `peer.Requests` = 1) e `Backoff_doubles_with_jitter_and_hits_the_six_hour_ceiling_exactly` (crescente, `<= BackoffCap`, 8ª tentativa = 6 h exatos, jitter variável). Can-fail: **T7** (adimissão devolve envio quando o circuito está aberto → **falha=2**, RED-AC = `Ac03...` + `An_open_circuit_defers...`) e **T8** (clamp do teto removido → **falha=1**, `Assert.All(delay <= cap)` cai) | PASS |
| AC-04 | par `Blocked` não recebe nada e o rate limit por par (`RateLimitPerMinute`) é respeitado no envio | Verde com `Ac04_a_blocked_peer_receives_nothing` (`peer.Requests` = 0, linha intacta), `A_blocked_pair_is_never_admitted`, `Ac04_the_per_pair_rate_limit_decides_who_leaves` (cota 2 → exatamente 2 envios, linhas retidas intactas na mesma janela) e `The_per_pair_rate_limit_admits_only_the_quota_inside_one_minute`. Can-fail: **T9** (guarda `Blocked` morta → **falha=2**) e **T10** (guarda de cota morta → **falha=2**) | PASS |
| AC-05 | `Squid.PeerClient` expõe os cenários **500**, **timeout** e **lentidão** e um teste de integração usa cada um na fila | Exposição provada AO VIVO pelas duas shells: `qa-ac05-peerclient-task-0046.ps1` (**exit 0, checks=5 fails=0** — `--help` declara `500\|timeout\|slow`, POST ao vivo → HTTP **500**, timeout → **curl exit 28** sem resposta, slow `--delay=1500` → **202 em 1584 ms**) e o gêmeo `.sh` sob Git Bash (**exit 0, checks=5 fails=0**, 500 → 500, 28 em 2108 ms, 202 em 1629 ms). Integração na fila: `Ac01`/`Ac02` (cenário 500), `Ac05_the_timeout_scenario...` e `Ac05_the_slow_scenario...` verdes. Can-fail: **T11** (500 vira 202 → **falha=3**, RED-AC contém `Ac01...` e `Ac02_after_eight...`), **T12** (timeout passa a responder → **falha=1**, só `Ac05_the_timeout...`), **T13** (lentidão sem espera → **falha=1**, só `Ac05_the_slow...`) | PASS |
| AC-06 | todo envio carrega o atributo de trace `peer.domain`, sem PII (SC-015) | Verde com `Ac06_every_send_carries_the_peer_domain_trace_attribute_without_pii` (span `peer.send`, tag = `IdnHost` do destino, sem `/`, sem ator, sem handle local). Can-fail: **T14** (`SetTag("peer.domain")` removido → **falha=1**, RED-AC = `Ac06...`) | PASS |
| AC-07 | gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` exit 0; nenhum arquivo alterado fora dos `touches` | Gate lido **dentro do log** `20261114-033801-test-gate.log` (S-22): cabeçalho `exit: 0` + `Com falha: 0, Aprovado: 554, Ignorado: 0, Total: 554`, sobre a árvore restaurada e com os artefatos da QA no lugar. Escopo nas DUAS shells: `QA-0046-scope.log` (exit 0 — commit 18/18 nos touches, `git-backend-entries=0`, `residue=0`, blob-identity **4/4**, `mtime-count=4`, `fora-dos-touches=0`, `fails=0`) e `QA-0046-scope-sh.log` (exit 0, mesmas contagens sob bash/WSL) | PASS |

## Comandos executados (todos nesta sessão, com exit real; datas vêm dos próprios logs)

| # | comando | exit | artefato / saída |
| --- | --- | --- | --- |
| 1 | baseline (pwsh): `git rev-parse HEAD`, `git rev-parse --abbrev-ref HEAD`, `git status --porcelain -uall`, `git show --name-only --format= HEAD` e sondas `wsl -e bash -c "command -v dotnet"`/`command -v git` | 0 | `docs/sdd/qa/QA-0046-git-baseline.txt` — HEAD `61c12ce`, branch `task/TASK-0046-peer-grain-fila-e-dlq`, só o task file do ODEV modificado, 18 arquivos do commit, **dotnet ausente no WSL** (git = `/usr/sbin/git`) |
| 2 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Federation` (baseline da QA, antes de qualquer escrita) | 0 | `docs/sdd/runs/test-runs/20261114-024950-test-filter.log` — `exit: 0`, Com falha 0 / Aprovado 51 / Total 51 (38 s) |
| 3 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-ac05-peerclient-task-0046.ps1` (run definitiva; runs 1 e 2 preservadas abaixo) | 0 | `docs/sdd/qa/QA-0046-ac05-cli.log` — SUMMARY **checks=5 fails=0** |
| 4 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0046.ps1 -VerifyOnly` (antes do run) | 0 | `docs/sdd/qa/QA-0046-verify.log` — 14 padrões `hits=1 hits2=1`, `non-ascii-bytes=0 ok=true`, `VERIFY total=14 bad=0` |
| 5 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0046.ps1` (run único) | 0 | `docs/sdd/qa/QA-0046-mutations.log` — 14 vermelhos com RED-AC da própria AC em cada um, 14× `RESTORE-OK hash=PRE residue=0`, `GREEN wrapper=0 logExit=0 aprovado=51 falha=0`, `UNTRACKED backend=0`, `SUMMARY cycles=14 failed=0 residue-backend=0 elapsed-s=778` |
| 6 | `bash docs/sdd/qa/qa-ac05-peerclient-task-0046.sh` sob Git Bash (`C:\Program Files\Git\bin\bash.exe`) | 0 | `docs/sdd/qa/QA-0046-ac05-cli-sh.log` — SUMMARY **checks=5 fails=0** |
| 7 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-scope-task-0046.ps1` | 0 | `docs/sdd/qa/QA-0046-scope.log` — `SUMMARY commit-files=18 qa-artifacts=15 backend-entries=0 residue=0 blob=4/4 mtime=4 fails=0` |
| 8 | `wsl -e bash docs/sdd/qa/qa-scope-task-0046.sh` (gêmeo Linux do escopo) | 0 | `docs/sdd/qa/QA-0046-scope-sh.log` — `SUMMARY commit-files=18 qa-artifacts=16 backend-entries=0 residue=0 blob=4/4 mtime=4 fails=0` |
| 9 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` (gate AC-07, suite inteira com BuildProof) | 0 | `docs/sdd/runs/test-runs/20261114-033801-test-gate.log` — `exit: 0` lido **dentro do log** (S-22) + `Com falha: 0, Aprovado: 554, Ignorado: 0, Total: 554` |
| 10 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0046.ps1` | 0 | `docs/sdd/qa/QA-0046-check.log` — SUMMARY **total=57 fails=0** |
| 11 | `wsl -e bash docs/sdd/qa/qa-check-task-0046.sh` (gêmeo Linux do checker) | 0 | `docs/sdd/qa/QA-0046-check-sh.log` — SUMMARY **total=34 fails=0** |

Tentativas da QA que NÃO contam como evidência de produto (falhas da própria ferramenta da QA, preservadas para transparência — ver findings 1..4): run 1 do probe AC-05 travou em `Start-Process -Wait` (log `QA-0046-ac05-cli-run1.log`, morta por timeout aos 300 s), run 2 do probe AC-05 marcou 2 checks como FAIL por porta do cenário anterior ainda ativa (log `QA-0046-ac05-cli-run2.log`, exit 1), a primeira invocação do gêmeo `.sh` do probe saiu `checks=5 fails=5` por um `cd` que subia 2 níveis em vez de 3 (saída só no console, sem log — o `tee` não tinha diretório) e a run 1 do checker `.sh` sob WSL saiu `fails=3` por leitura de logs Windows com padding/CRLF (log `QA-0046-check-sh-run1.log`); as quatro causas estão corrigidas e as execuções finais estão nas linhas 3, 6 e 11.

As linhas `## Log` e `## Evidence` destas execuções **não** foram gravadas pela QA: o dispatch devolve `logLines`/`evidenceRows` sem relógio para o ODEV carimbar (o relógio é dele). A tabela acima é a prova; a task file permanece intacta pela QA.

## Ciclos de mutação (can-fail; S-16 hash PRE = blob do commit, S-22 classificação no log, S-23 restore byte-exact)

Todos nos arquivos de **PRODUTO** que a behaviour lê (`Squid.Grains/Peers/PeerGrain.cs`, `Squid.Service/PeerDeliveryPump.cs`, `Squid.Service/OutboundPipeline.cs`, `Squid.PeerClient/PeerScenarioServer.cs`) — nenhum edit fora do escopo, e os 4 arquivos finais são byte-iguais ao `HEAD` (blob-identity 4/4; `git status -- backend` = 0).

| Ciclo | Arquivo | O que prova (AC) | Vermelho classificado NO LOG |
| --- | --- | --- | --- |
| T1 | PeerDeliveryPump.cs:191 | AC-01: a falha conta a tentativa (`attempts++` antes do reagendamento, §3.4) | `Ac01_a_500...` + `Ac02_after_eight...` + `Ac05_the_timeout...` + `Ac05_the_slow...` (`falha=4`) |
| T2 | PeerGrain.cs:49-50 | AC-01: o backoff cresce exponencialmente COM jitter | `Ac01_a_500...` + `Backoff_doubles_with_jitter...` (`falha=2`) |
| T3 | PeerGrain.cs:17 | AC-02: N é exatamente 8 (a 9ª tentativa não existe) | `Ac02_after_eight_attempts...` (`falha=1`) |
| T4 | PeerDeliveryPump.cs:207 | AC-02: o dead-letter de `max_attempts` DISPARA O ALERTA | `Ac02_after_eight_attempts...` (`falha=1`) |
| T5 | OutboundPipeline.cs:263 | AC-02: o dead-letter de 4xx permanente TAMBÉM dispara o alerta | `Ac02_a_permanent_404...` (`falha=1`) |
| T6 | OutboundPipeline.cs:243 | AC-02: 4xx permanente vai direto a `Failed`/DLQ, nunca retry às cegas | `Ac02_a_401...` + `Ac02_a_permanent_404...` (`falha=2`) |
| T7 | PeerGrain.cs:74 | AC-03: o circuito aberto ADIAME a entrega nova — nunca falha na hora | `An_open_circuit_defers...` + `Ac03_an_open_circuit...` (`falha=2`) |
| T8 | PeerGrain.cs:51 | AC-03: nenhum jitter levanta um atraso acima do teto de 6 h | `Backoff_doubles_with_jitter_and_hits_the_six_hour_ceiling_exactly` (`falha=1`) |
| T9 | PeerGrain.cs:67 | AC-04: par `Blocked` nunca é admitido (nada é enviado) | `A_blocked_pair_is_never_admitted` + `Ac04_a_blocked_peer_receives_nothing` (`falha=2`) |
| T10 | PeerGrain.cs:149 | AC-04: a cota por par decide quem sai dentro do minuto | `The_per_pair_rate_limit_admits_only_the_quota_inside_one_minute` + `Ac04_the_per_pair_rate_limit_decides_who_leaves` (`falha=2`) |
| T11 | PeerScenarioServer.cs:120 | AC-05: o cenário 500 RESPONDE 500 (a fila vê uma falha de verdade) | `Ac01_a_500...` + `Ac02_after_eight...` + `Ac03_an_open_circuit...` (`falha=3`) |
| T12 | PeerScenarioServer.cs:129 | AC-05: o cenário timeout NUNCA responde (o orçamento de envio perde) | `Ac05_the_timeout_scenario_fails_the_send_and_keeps_the_row_scheduled` (`falha=1`) |
| T13 | PeerScenarioServer.cs:132 | AC-05: o cenário lentidão ESPERA de verdade (falha dentro do orçamento) | `Ac05_the_slow_scenario_loses_to_the_send_budget` (`falha=1`) |
| T14 | OutboundPipeline.cs:220 | AC-06: todo envio carrega o atributo `peer.domain` | `Ac06_every_send_carries_the_peer_domain_trace_attribute_without_pii` (`falha=1`) |

14× `PRE` com `head-match=true` (o hash de trabalho é o blob do commit `61c12ce`), 14× `RESTORE-OK hash=PRE residue=0`, 0× `RESTORE-FAIL`, 0× `TOOLING-FAIL`, 0× `BUILD-FAIL`, 14× `EXPECT-OK`, 0× `EXPECT-MISS`, 1× GREEN `51/51 exit 0` (`20261114-032817-test-filter.log`), resíduo final de tokens mutantes = 0, `UNTRACKED backend=0`, `SUMMARY cycles=14 failed=0 residue-backend=0 elapsed-s=778`.

## Afirmações do DEV a verificar (declarações de nível spec)

| # | Afirmação (Log do task file / commit 61c12ce) | Julgamento desta QA | Evidência |
| --- | --- | --- | --- |
| 1 | "15 testes novos: PeerGrainTests (backoff crescente com teto e jitter, circuito, bloqueio, cota) e PeerDeliveryMotorTests (AC-01 a AC-06 com os cenarios 500 timeout e lentidao, mais 404 e 401, e o loop hospedado drenando sozinho)" | **CONFIRMADA em execução e em leitura**: os dois arquivos têm 4 + 11 `[Fact]` = 15, marcados `// AC-01`..`// AC-06`; baseline do filtro 51 = 36 (QA-0045) + 15, e os 15 verdes no GREEN do mutation run | comandos #2 e #5 + checkers `TEST-MOTOR`/`TEST-GRAIN` |
| 2 | "gate AC-07 exit 0 com 554/554 (539 de base mais 15 novos)" | **CONFIRMADA de forma independente**: o log do DEV `20261114-023158-test-gate.log` tem `exit: 0` e `Com falha: 0, Aprovado: 554, Total: 554`; e o gate DA QA re-executado sobre a árvore restaurada fecha Com falha: 0, Aprovado: 554, Ignorado: 0, Total: 554 | linha 9 + `GATE-EXIT`/`GATE-SUMMARY`/`GATE-AGE` |
| 3 | "18 arquivos alterados todos dentro dos touches, fora-dos-touches 0" | **CONFIRMADA**: 18/18 nos glob sets do bloco `touches`, 0 fora, no commit E no working tree da QA (as duas varreduras) | baseline + `COMMIT-TOUCHES` + comandos #7/#8 |
| 4 | "commit unico 61c12ce na branch task/TASK-0046-peer-grain-fila-e-dlq criada de develop 8376a45, sem push e sem tocar em main" | **CONFIRMADA localmente**: HEAD = `61c12ce`, branch = `task/...`, `git branch -r --contains 61c12ce` **vazio** (nada foi pushado; `origin` só tem develop/main). A origem em `8376a45` é do log do ODEV (o pai `8376a45` é o último commit de develop no histórico local ✓ `git log` confirma) | baseline + `git branch -r --contains` |
| 5 | "Squid.PeerClient deixou de ser stub: PeerScenarioServer ... cenarios ok 500 401 404 timeout slow e CLI --scenario e --delay; smoke do binario com --scenario=500 respondeu HTTP 500" | **CONFIRMADA ao vivo, por duas shells**: probe ps1 e probe bash — `--help` declara os tokens, 500 → HTTP 500, timeout → sem resposta, slow → 202 após o `--delay` | comandos #3 e #6 (findings 1-3 registram as três tentativas defeituosas da ferramenta) |
| 6 | "o pump resolve um IOutboundPipeline novo por tentativa via scope DI porque o ActorGrain guarda a chave por instancia" | **CONFIRMADA em leitura** (`PeerDeliveryPump.cs:146-147` cria o scope a cada tentativa) e indiretamente em execução (os 15 cenários e o loop hospedado passam com o sender real `HttpPeerSender`); o risco de chave travada em instância longeva é o parked DEV que o ODEV já lançou na TASK-0051 | leitura do código + cenários verdes |
| 7 | "correcao de teste: host por fato em PeerDeliveryMotorTests ... e FakeClock ganhou Advance(TimeSpan)" | **CONFIRMADA**: cada `[Fact]` monta seu próprio `MotorHost` (isolamento por fato) e todos os avanços de relógio passam pelo relógio falso do host — os testes de backoff só passam com `Advance` funcional | leitura de `PeerDeliveryMotorTests.cs:121` + cenários `Ac01`/`Ac02` verdes |

## Findings

Nenhum achado de **produto** que impeça o verdict (0 BLOCKER, 0 MAJOR — 554/554 no gate da QA e os 14 mutantes caem exatamente onde cada AC diz). Achados e notas:

| # | Severidade | Onde | O que | Evidência |
| --- | --- | --- | --- | --- |
| 1 | NOTA (ferramenta da QA, corrigida) | `docs/sdd/qa/qa-ac05-peerclient-task-0046.ps1` | Nesta PowerShell 5.1, `Start-Process -PassThru` **com** `-RedirectStandardOutput` devolve um `Process` sem handle (`ExitCode` vazio) e o pipe redirecionado faz `-Wait` travar nos workers de node-reuse do MSBuild — a run 1 do probe travou 300 s com dois `nodemode:1` órfãos; a run 1 foi morta e o log está preservado | `QA-0046-ac05-cli-run1.log` + árvore de processos inspecionada ao vivo (2 workers `MSBuild.dll nodemode:1`, pai já saiu) |
| 2 | NOTA (ferramenta da QA, corrigida) | mesmo probe, run 2 | Os cenários não eram encerrados entre si: o servidor **500** continuava ouvindo e o poll da porta do cenário seguinte devolveu a porta dele (`timeout` e `slow` responderam `http=500` → `checks=5 fails=2`); corrigido com marcador de tempo por chamada + `Stop-Tree` após cada cenário | `QA-0046-ac05-cli-run2.log` (exit 1, 2 FAIL) × run final exit 0 |
| 3 | NOTA (ferramenta da QA, corrigida) | os três `.sh` desta QA | O `cd` inicial subia `../..` (2 níveis: parava em `docs/`) em vez de `../../..` — a primeira invocação do gêmeo do probe saiu `checks=5 fails=5` com o tee sem diretório; corrigido nos três arquivos e reexecutado (probe verde, escopo ps1+sh verdes, checker roda por último) | console da primeira invocação + execuções finais #6/#7/#8 |
| 4 | NOTA (ferramenta da QA, cosmética) | `docs/sdd/qa/qa-mutate-task-0046.ps1` (herdada da QA-0045) | A linha `RUN ... total=0` do ledger exibe o total errado: o regex lê `Groups[3]` (que é `Ignorado`) em vez de `Groups[4]`; afeta só o display — `aprovado`/`falha`/`logExit` corretos e o julgamento usa `falha` + `logExit` + os nomes do RED-AC | `QA-0046-mutations.log` (todas as 14 linhas `total=0` com `falha` coerente e `aprovado+falha` = 51) |
| 5 | NOTA (pré-existente) | WSL / `docs/sdd/_tools/sdd-test.sh` | `wsl -e bash -c "command -v dotnet"` → vazio (baseline, sondado ao vivo nesta sessão): a suite **não roda em Linux nesta máquina** e o gêmeo `.sh` do gate quebra sem ele (memória banc, backlog S-11/S-12) — instalação exige gate humano | `QA-0046-git-baseline.txt` + `build-run-test.md:42` |
| 6 | NOTA (boundary do design) | `backend/src/Squid.Grains/Peers/PeerGrain.cs:108-120` | O circuito abre na **primeira** falha transitiva (janela = backoff da falha 1 = 5 min) — SPEC §G/§3.4 não fixam limiar, dizem só que "circuito aberto manda entrega nova direto para `Pending`", que é exatamente o comportamento provado no T7/`Ac03`; risco operacional baixo (toda falha pontual adia entregas NOVAS do par por 5 min, nunca as existentes) | leitura do código + ciclo T7 |
| 7 | NOTA (boundary da AC) | `backend/src/Squid.Service/OutboundPipeline.cs:243` | `408` e `429` ficam de fora da regra do 4xx permanente (`not (408 or 429)`) e seguem o caminho `send_failed` — nenhuma AC desta task nomeia esses códigos; o caminho que eles usam é o mesmo provado pelo cenário 500 (AC-01). Risco baixo: `Retry-After` do 429 ainda não é honrado (nenhum AC pede) | leitura do código + ciclo T11 |
| 8 | NOTA (escopo) | ramos `Cancelled` da §3.4 | O ramo "política reavaliada nega → `Cancelled`" é da TASK-0045 (R-05) e não consta da lista de AC desta task — já provado na QA-0045 (ciclo T8); aqui só o `switch` do pump que o preserva sem tocar no row foi lido (`PeerDeliveryPump.cs:162-164`) | §3.4 × lista de AC + `QA-0045-saida-fan-out-e-follow.md` |
| 9 | NOTA (atalho do DEV, aceito) | `backend/src/Squid.ReadModels/Federation/SqlitePeerQueue.cs:53-55` | A consulta de vencimento compara `next_attempt_at` lexicograficamente sobre texto ISO-8601 "O" — correto enquanto todo escritor carimbar UTC nesse formato (a própria task registra o atalho com a condição de upgrade) | leitura do código + cenários com FakeClock passando |
| 10 | NOTA (ferramenta da QA, corrigida) | `docs/sdd/qa/qa-check-task-0046.sh` | A run 1 do checker sob WSL acusou 3 FAILs por ler logs escritos no Windows sem lidar com o formato deles: `^exit: 0$` contra o padding real do sdd-test (`exit:    0`) e o `\r` do ledger CRLF grudado no nome do log do GREEN; corrigido para `^exit: +0$` e `tr -d '\r'`, run final `fails=0` | `QA-0046-check-sh-run1.log` (fails=3) × `QA-0046-check-sh.log` (fails=0) |

## Não coberto / unverified

- **Suite de testes em Linux (WSL)**: `dotnet` ausente no WSL (sondado ao vivo na baseline: `command -v dotnet` → exit 1; instalação exige gate humano) e o gêmeo `sdd-test.sh` quebra sem ele (S-11/S-12) → **unverified**, nunca assumido igual ao Windows. Os gêmeos `.sh` **estáticos** (escopo e checker) rodam — comandos #8 e #11 — e o gêmeo do probe roda sob **Git Bash** (shell POSIX no mesmo Windows, `command -v dotnet` → `/c/Program Files/dotnet`) — isso NÃO é Linux: o comportamento da suite no kernel/FS Linux segue para o CI ubuntu. Risco: baixo-médio (o CI da fase já rodou a suite em ubuntu numa janela anterior, mas não nesta árvore).
- **`Squid:RecoveryKey` ausente com o motor rodando** (Gaps da task: "desabilita só o envio, aviso + métrica; a fila local continua registrando") — nenhum AC desta task pede esse cenário; o caminho existe e foi lido (`OutboundPipeline.cs:187-191` devolve `federation_output_disabled` e o pump só espera 1 min sem contar tentativa, `PeerDeliveryPump.cs:173-180`). Risco baixo — a assumpção vigente é da própria task, e o adiamento da chave é o parked da TASK-0051.
- **Interop real Mastodon/Friendica e facet de dois silos (A→B)**: TASK-0047/0049 (esta última com gate humano, SC-016) — os cenários desta QA usam o par sintético `PeerScenarioServer` em loopback por desenho.
- **Runbooks `peer-entrega-travada.md` / `dlq-cheia.md`**: TASK-0048 (assumpção do bloco Gaps: o sistema precisa existir primeiro — agora existe).
- **408/429 com `Retry-After`** — ver finding 7; nenhuma AC nomeia.
- **FR-036 "entrega nunca bloqueia a request local"** — provado na QA-0045 (AC-05 daquela task), fora da lista desta; não re-executado aqui.

## Reproduzir

```powershell
# pré-requisito: .NET 10 SDK + PowerShell no Windows, repo C:\dev\squid-squad-net\squid-execution
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~Squid.Tests.Federation"   # esperado: exit 0, Aprovado 51, Com falha 0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-ac05-peerclient-task-0046.ps1                                                  # esperado: exit 0, SUMMARY checks=5 fails=0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0046.ps1 -VerifyOnly                                               # esperado: exit 0, VERIFY total=14 bad=0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0046.ps1                                                           # run fresco: exit 0, SUMMARY cycles=14 failed=0 (14 vermelhos de proposito)
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-scope-task-0046.ps1                                                            # esperado: exit 0, fora-dos-touches=0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test                                                             # gate: exit 0, Com falha 0, Aprovado 554
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0046.ps1                                                            # esperado: exit 0, SUMMARY fails=0
bash docs/sdd/qa/qa-ac05-peerclient-task-0046.sh       # Git Bash: esperado exit 0, SUMMARY checks=5 fails=0
wsl -e bash docs/sdd/qa/qa-scope-task-0046.sh          # esperado: exit 0, fora-dos-touches=0
wsl -e bash docs/sdd/qa/qa-check-task-0046.sh          # esperado: exit 0, SUMMARY fails=0
```

## Artefatos desta QA

| Arquivo | Papel |
| --- | --- |
| `docs/sdd/qa/QA-0046-git-baseline.txt` | baseline antes de qualquer escrita: HEAD `61c12ce`, branch, status, arquivos do commit, sondas do WSL |
| `docs/sdd/qa/qa-ac05-peerclient-task-0046.ps1` | probe AO VIVO da exposição de cenários do CLI (5 checks: help, 500, timeout, lentidão, limpeza) — ProcessStartInfo puro, waits com prazo, árvores mortas no finally |
| `docs/sdd/qa/QA-0046-ac05-cli.log` / `QA-0046-ac05-cli-run1.log` / `QA-0046-ac05-cli-run2.log` | run final verde + as duas runs defeituosas preservadas (findings 1-2) |
| `docs/sdd/qa/qa-ac05-peerclient-task-0046.sh` | gêmeo bash do mesmo probe (roda sob Git Bash; WSL fica no unverified por falta de `dotnet`) |
| `docs/sdd/qa/QA-0046-ac05-cli-sh.log` | saída do gêmeo (checks=5 fails=0) |
| `docs/sdd/qa/qa-mutate-task-0046.ps1` | driver de mutação can-fail (14 ciclos: AC-01 ×2, AC-02 ×4, AC-03 ×2, AC-04 ×2, AC-05 ×3, AC-06; `-VerifyOnly` com checagem ASCII, S-16/S-22/S-23 embutidos, `RED-AC` = qual cena da AC caiu) |
| `docs/sdd/qa/QA-0046-mutations.log` | ledger final: PRE (hash = blob do commit), vermelhos no log com RED-AC, restore, GREEN, UNTRACKED, SUMMARY |
| `docs/sdd/qa/QA-0046-verify.log` | `-VerifyOnly` gravado (14 padrões `hits=1` + ASCII OK + `VERIFY total=14 bad=0`) |
| `docs/sdd/qa/qa-scope-task-0046.ps1` | varredura de escopo Windows: blob-identity vs `rev-parse`, mtime desde a baseline, resíduo, classificação do `git status` |
| `docs/sdd/qa/QA-0046-scope.log` | saída da varredura (18/18 touches, blob 4/4, mtime 4, residue 0, fora-dos-touches 0) |
| `docs/sdd/qa/qa-scope-task-0046.sh` | gêmeo bash da mesma varredura |
| `docs/sdd/qa/QA-0046-scope-sh.log` | saída do gêmeo (mesmas contagens) |
| `docs/sdd/qa/qa-check-task-0046.ps1` | checker estático Windows (inclui `sdd-state check` da task file e os markers estáticos dos testes) |
| `docs/sdd/qa/qa-check-task-0046.sh` | gêmeo Linux do mesmo checker (sem o `sdd-state`, ferramenta Windows) |
| `docs/sdd/qa/QA-0046-check.log` / `QA-0046-check-sh.log` | saídas dos dois checkers |
| `backend/` | **intocado pela QA** — `git status -- backend` = 0 entradas (a QA não acrescentou teste: as cenas das ACs são as do DEV) |
| `docs/sdd/qa/QA-0046-peer-grain-fila-e-dlq.md` | este documento |
