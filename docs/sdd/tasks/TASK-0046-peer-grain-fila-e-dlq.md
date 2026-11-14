<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0046 — PeerGrain: fila, backoff, circuit breaker, DLQ + `PeerClient` com falha

```yaml
runId: R-20261113-01
taskId: TASK-0046
status: DONE
agent: odev
phase: P-06
sequence: 2
createdAt: 2026-11-13 13:47:25
updatedAt: 2026-11-14 03:43:35
parent: SPEC-0011
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Grains/Peers/**", "backend/src/Squid.PeerClient/**", "backend/src/Squid.Service/**", "backend/src/Squid.ReadModels/**", "backend/tests/Squid.Tests/**"]
blocks: ["TASK-0047"]
blockedBy: ["TASK-0045"]
inputArtifacts: ["docs/sdd/specs/SPEC-0011-federacao-p06.md", "docs/sdd/plan/plan.md", "specs/001-squid-mvp/data-model.md", "specs/001-squid-mvp/contracts/activitypub.md"]
outputArtifacts: ["backend/src/Squid.Grains/Peers/**", "backend/src/Squid.PeerClient/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/**"]
interviewRounds: 2/6
feasibilityRounds: 0/2
correctionAttempts: 0/2
qaAttempts: 1/2
contextHandoffs: 0/4
progressDelta: 0
humanGate:
  approvedBy: NENHUM
  approvedDate: NENHUM
  scope: NENHUM
  expiresAt: NENHUM
blocker: NENHUM
nextAction: DONE - QA-0046 PASSED (7/7 ACs, 14 mutacoes, gate 554/554, commits 61c12ce + d4c20cf); merge em develop pelo ODEV e dispatch da TASK-0050
evidence: []
memoryBankFlags: []
```

## Intent

Entrega 6 de 9 da P-06: o motor de entrega assíncrona — `PeerGrain` por domínio remoto com fila,
backoff exponencial com jitter (`N = 8`, teto 6 h), circuito aberto, DLQ com motivo e alerta,
rate limit por par (FR-036, data-model §2.9/§3.4) — e o `Squid.PeerClient` (stub desde a
TASK-0002) com cenários de falha 500/timeout/lentidão para exercitar tudo sem par real.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: entrega que falha com 500 volta para `Pending` com backoff crescente e `attempts++`
  (`NextAttemptAt` no futuro) — teste (§3.4).
- AC-02: após 8 tentativas ou diante de 4xx permanente a entrega vira `Failed` na DLQ com motivo
  e dispara alerta — teste.
- AC-03: com o circuito aberto para o par, entrega nova vai **direto** para `Pending` com
  `NextAttemptAt` futuro (nunca falha na hora); backoff respeita teto de 6 h com jitter — teste.
- AC-04: par `Blocked` não recebe **nada** (nenhuma entrega é enviada) e o rate limit por par
  (`RateLimitPerMinute`) é respeitado no envio — teste.
- AC-05: `Squid.PeerClient` expõe cenários de falha **500**, **timeout** e **lentidão**, e um
  teste de integração usa cada cenário para exercitar a fila (AC-01/02/03) — teste.
- AC-06: todo envio carrega o atributo de trace `peer.domain`, sem PII (SC-015) — teste.
- AC-07: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1
  test` exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Grains/Peers/**` — diretório novo: `PeerGrain` (saúde, fila, backoff,
  circuito, DLQ)
- `backend/src/Squid.PeerClient/**` — cenários de falha (500, timeout, lentidão) no stub atual
- `backend/src/Squid.Service/**` — pump que consome `PeerDelivery` pendente e dispara o envio
- `backend/src/Squid.ReadModels/**` — consulta da fila/DLQ para o pump e para as métricas
- `backend/tests/Squid.Tests/**` — testes das ACs
- produto **fora** dos `touches` desta task: qualquer lacuna levantada como dúvida, não corrigida aqui

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| — | no | `Squid:RecoveryKey` ausente desabilita só o envio (aviso + métrica); a fila local continua registrando pendências — a instância nunca cai por falta da chave |
| — | no | runbooks `peer-entrega-travada.md`/`dlq-cheia.md` são da TASK-0048 (o sistema precisa existir primeiro) |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-13 13:47:25 | odev | task created from SPEC-0011 (entrega 6 de 9, PeerGrain + pump + PeerClient com falha; blockedBy TASK-0045)
- 2026-11-14 01:38:08 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; runId R-20261113-01; blockedBy TASK-0045 resolve (DONE com QA-0045 PASSED 7/7, gate 539/539, merge 037a47b em develop + docs 8376a45); usar as portas IPeerDeliveryStore/IPeerSender/IOutboundPipeline e a tabela peer_delivery da 0008 ja entregues na 0045; autorizacao de commit gate PHASE 2026-11-11 20:42:28 (modelo C) incluida - nova branch task/TASK-0046-peer-grain-fila-e-dlq desde develop; logLines e evidenceRows sem relogio para o ODEV carimbar (sem pipe em comandos)
- 2026-11-14 02:39:09 | dev | dev feasibility round 1 fechada: unit PeerGrain em Squid.Grains/Peers, callers PostEndpoints e FederationEndpoints enfileiram via IOutboundPipeline, dados nas tabelas peer_delivery e peer da migration 0008; touches confirmado sem acrescimos (pump em Squid.Service porque o Squid.Worker nao referencia a implementacao e nao consta em touches); baseline 539/539 exit 0
- 2026-11-14 02:39:09 | dev | ponytail full carregado e design fechado: backoff base 5 min dobrando com jitter so inflando de 0 a 20 por cento ate o teto de 6 h exato na oitava tentativa, circuito abre na primeira falha transitiva e fecha com sucesso, janela de cota por par em memoria dentro do PeerGrain
- 2026-11-14 02:39:10 | dev | AC-02 trazida para dentro dos touches: IPeerStatusSender em Squid.Service com precedente IFederatedActorClient devolve PeerSendAnswer com o status, 4xx exceto 408 e 429 vira Failed com motivo peer_rejected ou signature_rejected para 401 e 403, o bool da porta IPeerSender continua como piso e nenhum arquivo de Squid.Api foi alterado
- 2026-11-14 02:39:10 | dev | Squid.PeerClient deixou de ser stub: PeerScenarioServer em sockets crus com cenarios ok 500 401 404 timeout slow e CLI --scenario e --delay; smoke do binario com --scenario=500 respondeu HTTP 500 no POST do inbox
- 2026-11-14 02:39:11 | dev | correcao de projeto: o pump resolve um IOutboundPipeline novo por tentativa via scope DI porque o ActorGrain guarda a chave ativada por instancia e uma instancia longeva assinaria o segundo ator com a chave do primeiro
- 2026-11-14 02:39:11 | dev | correcao de teste: host por fato em PeerDeliveryMotorTests (o host compartilhado fazia a passada contar linhas vencidas de outros fatores) e FakeClock ganhou Advance(TimeSpan) para os minutos de backoff
- 2026-11-14 02:39:13 | dev | duas execucoes de teste em paralelo colidiram no arquivo do testhost enquanto o gate de fundo corria; gate reexecutado em serie sem nova falha
- 2026-11-14 02:39:13 | dev | 15 testes novos: PeerGrainTests (backoff crescente com teto e jitter, circuito, bloqueio, cota) e PeerDeliveryMotorTests (AC-01 a AC-06 com os cenarios 500 timeout e lentidao do PeerClient, mais 404 e 401, e o loop hospedado drenando sozinho)
- 2026-11-14 02:39:14 | dev | gate AC-07 exit 0 com 554/554 (539 de base mais 15 novos); 18 arquivos alterados todos dentro dos touches, fora-dos-touches 0; commit unico 61c12ce na branch task/TASK-0046-peer-grain-fila-e-dlq criada de develop 8376a45, sem push e sem tocar em main
- 2026-11-14 02:39:19 | odev | ODEV verificou antes de transicionar: gate 20261114-023158-test-gate.log lido no arquivo (exit: 0, Aprovado 554, Com falha 0, 6m36s) e filtro 20261114-022001-test-fast.log (exit: 0, 50/50); commit 61c12ce conferido via git show --stat (18 arquivos, todos nos touches - SuccessionHost.cs e InboxHost.cs de teste dentro de backend/tests); logLines (9) e evidenceRows (10) carimbados via sdd-state; step result DONE aceito
- 2026-11-14 02:39:19 | odev | parked do DEV acolhido: latch de chave do ActorGrain (instancia longeva assinaria o segundo ator com a chave do primeiro - contornado por scope DI no pump) + a contradicao RK por ator x Squid:RecoveryKey - ambos caem na TASK-0051, que redesenha o caminho de chave junto com a resposta do humano ainda pendente
- 2026-11-14 02:39:21 | odev | transicao IN_DEVELOPMENT -> AWAITING_QA (attempt 1/2) - dispatch QA a seguir
- 2026-11-14 03:44:27 | qa | baseline da QA antes de qualquer escrita: HEAD 61c12ce na branch task/TASK-0046-peer-grain-fila-e-dlq, 18 arquivos do commit todos nos touches, somente o task file do ODEV modificado, wsl sem dotnet registrado (suite Linux unverified) - QA-0046-git-baseline.txt
- 2026-11-14 03:44:28 | qa | baseline do filtro Federation exit 0 com 51 de 51 antes de qualquer escrita da QA - 20261114-024950-test-filter.log
- 2026-11-14 03:44:28 | qa | probe ao vivo da exposicao de cenarios do PeerClient pelo CLI: exit 0 com checks 5 fails 0 - help declara 500 timeout slow, ao vivo 500 respondeu HTTP 500, timeout sem resposta com curl exit 28, lentidao respondeu 202 apos 1584 ms - QA-0046-ac05-cli.log
- 2026-11-14 03:44:29 | qa | qa-mutate-task-0046.ps1 -VerifyOnly exit 0 com 14 padroes hits 1 e ASCII ok - QA-0046-verify.log
- 2026-11-14 03:44:29 | qa | 14 ciclos can-fail executados contra o produto, todos vermelhos com a cena da propria AC no RED-AC, 14 restores byte-exact, GREEN 51 de 51, UNTRACKED backend 0 e SUMMARY cycles 14 failed 0 residue-backend 0 em 778 s - QA-0046-mutations.log
- 2026-11-14 03:44:30 | qa | gemeo bash do probe AC-05 executado sob Git Bash com checks 5 fails 0 (500 em 164 ms, curl 28 em 2108 ms, 202 em 1629 ms) - QA-0046-ac05-cli-sh.log
- 2026-11-14 03:44:30 | qa | varredura de escopo executada nas duas shells: commit 18 de 18 nos touches, git-backend-entries 0, residuo 0, blob-identity 4 de 4, mtime 4, fora-dos-touches 0 - QA-0046-scope.log e QA-0046-scope-sh.log
- 2026-11-14 03:44:31 | qa | gate AC-07 executado sobre a arvore restaurada com exit 0 e 554 de 554 lido dentro do proprio log - 20261114-033801-test-gate.log
- 2026-11-14 03:44:31 | qa | checker estatico Windows SUMMARY total 57 fails 0, incluindo sdd-state check OK com log 14 e evidence 10 - QA-0046-check.log
- 2026-11-14 03:44:32 | qa | gemeo Linux do checker sob WSL SUMMARY total 34 fails 0 na run final, com a run 1 de 3 fails por padding e CRLF preservada - QA-0046-check-sh.log
- 2026-11-14 03:44:32 | qa | tentativas defeituosas da propria ferramenta da QA preservadas e documentadas como findings 1 a 4: probe run 1 travada em Start-Process Wait, probe run 2 com porta de cenario anterior, sh do probe com cd de 2 niveis e checker sh run 1 com padding de exit
- 2026-11-14 03:44:33 | qa | verdict PASSED em QA-0046-peer-grain-fila-e-dlq.md com 7 de 7 ACs PASS e 0 BLOCKER; commit unico d4c20cf na branch com os 20 artefatos da QA, task file nao tocado pela QA, git branch -r --contains 61c12ce vazio (sem push, main intocado)
- 2026-11-14 03:44:41 | odev | ODEV verificou antes de transicionar: QA-0046-peer-grain-fila-e-dlq.md com verdict PASSED lido no arquivo; gate 20261114-033801-test-gate.log exit: 0 / Aprovado 554 / Com falha 0 / 6m40s (lido no arquivo); mutations SUMMARY cycles=14 failed=0 residue-backend=0 lido no log; commit d4c20cf conferido via git show --stat (20 artefatos so docs/sdd/qa); logLines (12) e evidenceRows (12) carimbados via sdd-state - veredito aceito
- 2026-11-14 03:44:41 | odev | QA-0046 PASSED attempt 1/2 (PRODUTO+TESTES): 7/7 ACs, 14 ciclos can-fail todos vermelhos na AC alvo, restore byte-exact, GREEN 51/51; probe AO VIVO do PeerClient nas duas shells (500, curl 28, 202 apos delay); gate 554/554; 0 BLOCKER/MAJOR; 10 NOTA (4 ferramenta da QA, 1 cosmética, 5 boundary) - flags de contrato/ponteiro contracts.md:26 e :21 registradas para a consolidacao
- 2026-11-14 03:44:42 | odev | transicao AWAITING_QA -> DONE; entrega 6 de 10 da P-06 encerrada - merge em develop + docs commitados pelo ODEV, dispatch da TASK-0050 (pares confiaveis) a seguir - fila: 0050 -> 0047 -> 0048 -> 0049, com a TASK-0051 (assinatura de ator registrado) apos o gate humano pendente

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-14 02:39:14 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261114-015808-test-gate.log (baseline antes das mudancas, 539/539) |
| 2026-11-14 02:39:15 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 0 | NENHUM (compilador: 0 erros) |
| 2026-11-14 02:39:15 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -Fast -TestFilter FullyQualifiedName~Squid.Tests.Federation | 0 | docs/sdd/runs/test-runs/20261114-021752-test-fast.log (3 falhas reais do teste compartilhado, corrigidas com host por fato) |
| 2026-11-14 02:39:15 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -Fast -TestFilter FullyQualifiedName~Squid.Tests.Federation | 0 | docs/sdd/runs/test-runs/20261114-022001-test-fast.log (50/50) |
| 2026-11-14 02:39:16 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -Fast -TestFilter FullyQualifiedName~PeerDeliveryMotorTests | 0 | docs/sdd/runs/test-runs/20261114-022456-test-fast.log (11/11 com o loop hospedado) |
| 2026-11-14 02:39:16 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261114-023158-test-gate.log (AC-07: 554/554) |
| 2026-11-14 02:39:17 | dev | curl.exe -s -o NUL -w http_code -X POST -H Content-Type:application/activity+json --data {} http://127.0.0.1:50202/users/alice/inbox | 0 | HTTP 500 via dotnet run --project backend/src/Squid.PeerClient -- --scenario=500 (CLI do PeerClient) |
| 2026-11-14 02:39:17 | dev | git checkout -b task/TASK-0046-peer-grain-fila-e-dlq | 0 | branch criada de develop 8376a45 |
| 2026-11-14 02:39:18 | dev | git show --stat --format=%h 61c12ce | 0 | commit 61c12ce com 18 arquivos, todos dentro dos touches |
| 2026-11-14 02:39:18 | dev | git -C C:\dev\squid-squad-net\squid-execution status --porcelain | 0 | resta apenas o edit de dispatch do ODEV no arquivo da task, nao commitado por mim |
| 2026-11-14 03:44:33 | qa | git rev-parse HEAD + git rev-parse --abbrev-ref HEAD + git status --porcelain -uall + git show --name-only HEAD + sondas wsl command -v dotnet e command -v git | 0 | docs/sdd/qa/QA-0046-git-baseline.txt |
| 2026-11-14 03:44:33 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Federation | 0 | docs/sdd/runs/test-runs/20261114-024950-test-filter.log (baseline 51 de 51) |
| 2026-11-14 03:44:34 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-ac05-peerclient-task-0046.ps1 | 0 | docs/sdd/qa/QA-0046-ac05-cli.log (checks 5 fails 0) |
| 2026-11-14 03:44:34 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0046.ps1 -VerifyOnly | 0 | docs/sdd/qa/QA-0046-verify.log (VERIFY total 14 bad 0) |
| 2026-11-14 03:44:35 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0046.ps1 | 0 | docs/sdd/qa/QA-0046-mutations.log (cycles 14 failed 0) |
| 2026-11-14 03:44:36 | qa | bash docs/sdd/qa/qa-ac05-peerclient-task-0046.sh sob Git Bash | 0 | docs/sdd/qa/QA-0046-ac05-cli-sh.log (checks 5 fails 0) |
| 2026-11-14 03:44:36 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-scope-task-0046.ps1 | 0 | docs/sdd/qa/QA-0046-scope.log (fora-dos-touches 0) |
| 2026-11-14 03:44:37 | qa | wsl -e bash docs/sdd/qa/qa-scope-task-0046.sh | 0 | docs/sdd/qa/QA-0046-scope-sh.log (fora-dos-touches 0) |
| 2026-11-14 03:44:37 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261114-033801-test-gate.log (554 de 554 lido dentro do log) |
| 2026-11-14 03:44:37 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0046.ps1 | 0 | docs/sdd/qa/QA-0046-check.log (SUMMARY total 57 fails 0) |
| 2026-11-14 03:44:38 | qa | wsl -e bash docs/sdd/qa/qa-check-task-0046.sh | 0 | docs/sdd/qa/QA-0046-check-sh.log (SUMMARY total 34 fails 0) |
| 2026-11-14 03:44:38 | qa | git commit com a mensagem autorizada TASK-0046 QA-0046 PASSED | 0 | d4c20cf com 20 artefatos em docs/sdd/qa, task file fora do commit |
