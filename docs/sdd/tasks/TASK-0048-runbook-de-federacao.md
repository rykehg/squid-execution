<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0048 — Runbook de federação (manual dos dois silos + runbooks P-06 executáveis)

```yaml
runId: R-20261113-01
taskId: TASK-0048
status: DONE
agent: odev
phase: P-06
sequence: 2
createdAt: 2026-11-13 13:47:25
updatedAt: 2026-11-14 18:54:42
parent: SPEC-0011
authority: SPEC
parallel: false
touches: ["docs/runbooks/**"]
blocks: ["TASK-0049"]
blockedBy: ["TASK-0047"]
inputArtifacts: ["docs/sdd/specs/SPEC-0011-federacao-p06.md", "docs/sdd/plan/plan.md", "docs/runbooks/README.md", "docs/runbooks/peer-entrega-travada.md", "docs/runbooks/dlq-cheia.md"]
outputArtifacts: ["docs/runbooks/federacao-interop.md", "docs/runbooks/README.md", "docs/runbooks/peer-entrega-travada.md", "docs/runbooks/dlq-cheia.md"]
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
nextAction: DONE - QA-0048 PASSED (5/5 ACs, manual RE-EXECUTADO 52/0, gate 597/597, commits 8f06ee1 + 8cfb097); merge em develop pelo ODEV; 2 achados MINOR roteados para a AC-05 da TASK-0049
evidence: []
memoryBankFlags: []
```

## Intent

Entrega 8 de 9 da P-06 — **o manual pedido pelo humano** (decisões 3 e 5 da SPEC-0011):
`docs/runbooks/federacao-interop.md` com o passo a passo executável para subir os dois silos
Squid locais, subir Mastodon e Friendica em Docker, apontar e validar a comunicação — escrito
**antes** da TASK-0049, que executa o manual junto com o humano. Nesta entrega os runbooks
`peer-entrega-travada.md` e `dlq-cheia.md` (PENDENTE-fase desde a P-01) viram EXECUTÁVEL, com os
sistemas que a TASK-0046 construiu. Só `docs/runbooks/**` — nenhum código.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `docs/runbooks/federacao-interop.md` existe e traz comandos **reais** do repositório
  (copiados/verificados dos arquivos e configs do repo, não inventados) para: subir silo A e silo
  B em portas/domínios distintos, configurar `Instance:Domain` e `Squid:RecoveryKey`, subir
  Mastodon e Friendica em Docker, apontar cada um para o silo e validar a comunicação — teste.
- AC-02: o runbook traz uma seção opcional claramente marcada **"instância pública real"**
  (domínio, TLS, túnel) identificada como gate humano — teste.
- AC-03: `peer-entrega-travada.md` e `dlq-cheia.md` viram EXECUTÁVEL: escopo + passo a passo com
  comandos reais, sem ponteiro "PENDENTE-fase" — teste.
- AC-04: `docs/runbooks/README.md` (índice) atualizado: status dos três runbooks e ponteiros de
  fase corretos (P-06) — teste.
- AC-05: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1
  test` exit 0; nenhum arquivo alterado fora dos `touches` (só `docs/runbooks/**`).

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `docs/runbooks/federacao-interop.md` — novo: o manual
- `docs/runbooks/peer-entrega-travada.md`, `docs/runbooks/dlq-cheia.md` — de PENDENTE-fase para EXECUTÁVEL
- `docs/runbooks/README.md` — índice e status
- produto **fora** dos `touches` desta task: qualquer lacuna levantada como dúvida, não corrigida aqui

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-47 | no | manual cobre o caminho Docker local (decisão 6) como primário; "instância pública real" é seção opcional marcada como gate humano |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-13 13:47:25 | odev | task created from SPEC-0011 (entrega 8 de 9, O MANUAL pedido pelo humano - runbook federacao-interop + runbooks P-06 executaveis; blockedBy TASK-0047)
- 2026-11-14 16:23:23 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; runId R-20261113-01; blockedBy TASK-0047 resolve (DONE com QA-0047 PASSED 8/8, facet 12/12 + 3 camadas de mutacao, gate 597/597, merge 9f3ecc6 em develop + docs fb1feda); o runbook pode citar FATOS REAIS desta fase: Federation:TrustedPeerHosts=127.0.0.1,localhost (GAP-49), Squid:RecoveryKey base64 dos 32 bytes (D-01 TASK-0042) com duplo embrulho no registro (GAP-50), Instance:Domain opcional (I-11), pump Federation:DeliveryPumpSeconds, CLI do PeerClient --scenario, portas Orleans do harness da facet (Configure EndpointOptions depois de UseOrleans); autorizacao de commit gate PHASE 2026-11-11 20:42:28 (modelo C) incluida - nova branch task/TASK-0048-runbook-de-federacao desde develop; logLines e evidenceRows sem relogio para o ODEV carimbar (sem pipe em comandos)
- 2026-11-14 17:28:26 | dev | dev feasibility round 1 fechada: unit de cambio = os 4 arquivos de docs/runbooks, chamador = operador humano e a TASK-0049, dados lidos = tabelas peer_delivery e peer da migration 0008 e os logs Serilog do silo; touches confirmado sem acrescimos; baseline do gate exit 0 com 597/597 (log 20261114-171443-test-gate.log)
- 2026-11-14 17:28:26 | dev | dev ponytail full carregado: nada inventado - todo comando do runbook foi copiado de arquivo ou config do repo ou executado na prova, e sistema sem mecanismo ficou com marcador em vez de passo inventado
- 2026-11-14 17:28:27 | dev | dev prova dos dois silos locais: silo A em 5601 e silo B em 5602 com Storage:Path e Media:BlobRoot distintos, Squid:RecoveryKey gerada em base64 de 32 bytes e Federation:TrustedPeerHosts igual a 127.0.0.1,localhost; ambos subiram com health 200 e isolamento 50/50 consultas sem vazamento cruzado entre bancos
- 2026-11-14 17:28:27 | dev | dev validacao A-B com curl.exe: register 201 nos dois, comunidade de mesmo slug interop-demo 201 nos dois, follow mutuo 202 e followers totalItems 1 nos dois lados, publicacoes 201 nos dois, peer_delivery com Follow e Create em Sent e peer Healthy
- 2026-11-14 17:28:28 | dev | dev Worker de projecao ao lado de cada silo com o mesmo Storage:Path: os feeds dos dois silos passaram a exibir o post proprio e o post vindo do outro silo; sem Worker o feed devolve items vazio, observado e documentado
- 2026-11-14 17:28:28 | dev | dev prova de Instance:Domain: instancia em 5603 com Instance:Domain=squid-a.test publicou id https://squid-a.test/users/domtest e webfinger subject acct:domtest@squid-a.test mesmo consultado em 127.0.0.1:5603, confirmando origem https forcada (CanonicalOrigin.cs:28)
- 2026-11-14 17:28:29 | dev | dev prova dos runbooks de fila: PeerClient cenario 401 derrubou a linha em Failed com motivo signature_rejected e o aviso peer delivery dead-lettered na primeira tentativa; cenario 500 deixou a linha Pending com send_failed e next_attempt_at futuro, peer Down com failure_count 1; PeerClient --help exit 0 e cenario desconhecido exit 1
- 2026-11-14 17:28:29 | dev | dev comportamento suspeito registrado no manual e levado ao ODEV: no par Squid-Squid o Accept vem no corpo do 202 e o remetente le so o status, entao a colecao following fica em 0 enquanto followers e entregas funcionam (HttpPeerSender.cs:87 diante de FederationEndpoints.cs:164); nenhum codigo tocado
- 2026-11-14 17:28:30 | dev | dev primeiro gate tentado ficou bloqueado por lock de arquivo de um processo legado, ambiente e nao produto; processo legado encerrado e gate re-executado apos as edicoes com exit 0 e 597/597 (log 20261114-172351-test-gate.log)
- 2026-11-14 17:28:30 | dev | dev commit unico 8f06ee1 na branch task/TASK-0048-runbook-de-federacao criada de develop fb1feda, apenas com os 4 arquivos de docs/runbooks; push e merge ficam com o ODEV
- 2026-11-14 17:29:07 | odev | ODEV verificou antes de transicionar: gate pos-edicao 20261114-172351-test-gate.log lido no arquivo (exit: 0, Aprovado 597, Com falha 0, 8m15s); commit 8f06ee1 conferido via git show --stat (4 arquivos so docs/runbooks); federacao-interop.md existe com 236 linhas (git normaliza CRLF); grep de PENDENTE nos dois runbooks = 0 marcadores reais (o match era a palavra pendentes, falso positivo case-insensitive); logLines (10) e evidenceRows (16) carimbados - OBSERVACAO: o resumo final do DEV citou 17 evidenceRows mas as linhas verbatim entregues sao 16, carimbei as 16 que tenho; saidas sem exit inteiro registradas como NAO_COLETADO com o resultado descrito no artefato; step result DONE aceito
- 2026-11-14 17:29:08 | odev | flags do DEV acolhidas: (1) [SUSPEITO] following nunca estabelece no par Squid-Squid (Accept no corpo do 202 nao lido) - CONFIRMADO com prova following=0 x followers=1, vira S-43 ja existente no backlog com a confirmacao e risco para a 0049; (2) [GAP] FederationMetrics sem pipeline de exportacao (composition root so WithTracing) -> S-45 novo; (3) sem rota de requeue/DLQ resolvido no runbook com decisao humana registrada - tudo registrados, nenhum bloqueia a QA
- 2026-11-14 17:29:08 | odev | transicao IN_DEVELOPMENT -> AWAITING_QA (attempt 1/2) - dispatch QA a seguir
- 2026-11-14 19:00:36 | qa | qa entrou pela task file TASK-0048 status AWAITING_QA attempt 1 e leu SPEC-0011, os tres runbooks e a rota qa-integracao do memory bank
- 2026-11-14 19:00:36 | qa | verificacao estatica executada com qa-check-task-0048.ps1 exit 0 e 134 PASS 0 FAIL cobrindo projetos, chaves de config, citacoes arquivo:L com claim na linha, rotas HTTP, colunas SQL da migration 0008, marcas dos pares de referencia, AC-02, AC-03 e AC-04
- 2026-11-14 19:00:37 | qa | mutacao dos runbooks provou que os verificadores falham com 4 FAIL direcionados e exit 4 no ps1 e no sh
- 2026-11-14 19:00:38 | qa | execucao dinamica com qa-run-federation-task-0048.ps1 exit 0 e 52 PASS 0 FAIL: silos A e B no ar com health 200, verificacao 2 a 6 com 201 201 201 201 202 202 201 201, isolamento de bancos, followers 1 e 1, following 0 e 0 conforme S-43, feed vazio sem Worker e cruzado com Worker, fila Follow e Create em Sent e peer Healthy, diagnostico e contagem por estado, PeerClient help exit 0 e nope exit 1, cenario 500 com Pending send_failed e peer Down failure_count 1, cenario 401 com Failed signature_rejected na primeira tentativa e aviso peer delivery dead-lettered, consultas da DLQ, outbox do par com nota 5601, nenhum signature verification failed, silo C com Instance:Domain id https e webfinger subject, limpeza sem residuo
- 2026-11-14 19:00:38 | qa | gemeo Linux qa-check-task-0048.sh executado no WSL com exit 0 e 145 PASS 0 FAIL
- 2026-11-14 19:00:39 | qa | gate AC-05 sdd-test.ps1 test exit 0 com 597 aprovados e 0 falhas em 9m48s no log 20261114-181815-test-gate.log batendo com o 597/597 do indice
- 2026-11-14 19:00:39 | qa | escopo AC-05 confirmado com git show --stat 8f06ee1 exit 0 mostrando apenas os 4 arquivos de docs/runbooks e git status exit 0 com so os carimbos do ODEV mais os artefatos da QA
- 2026-11-14 19:00:40 | qa | pares Mastodon e Friendica verificados apenas pelas marcas GATE-HUMANO e EXTERNO com fontes oficiais e versoes do quickstart, nunca executados, conforme GAP-47
- 2026-11-14 19:00:40 | qa | commit unico 8cfb097 na branch task-TASK-0048 com os 9 artefatos da QA sem tocar task file nem BACKLOG e sem push
- 2026-11-14 19:00:41 | qa | QA doc escrito em docs/sdd/qa/QA-0048-runbook-de-federacao.md com verdict PASSED, 2 achados MINOR e lacunas declaradas
- 2026-11-14 19:00:47 | odev | ODEV verificou antes de transicionar: QA-0048-runbook-de-federacao.md com verdict PASSED lido no arquivo; gate 20261114-181815-test-gate.log exit: 0 / Aprovado 597 / Com falha 0 / 9m48s (lido no arquivo); federation-run.log com PASS reais lido; commit 8cfb097 conferido (9 arquivos so docs/sdd/qa); logLines (10) e evidenceRows (9) carimbados - veredito aceito
- 2026-11-14 19:00:48 | odev | adjudicacao dos 2 achados MINOR da QA-0048: (1) federacao-interop.md:289 Verificacao 9 sem comando e com prova exigindo ?page=1; (2) :231 curl -w sem quebra de linha cola codigos - os dois sao correcoes de passo do proprio manual e a TASK-0049 ja tem AC-05 exatamente para isso (passos que a execucao real provou); registro na tabela de gaps da 0049, sem backlog e sem rodada de correcao (veredito PASSED, nenhum BLOCKER/MAJOR)
- 2026-11-14 19:00:48 | odev | transicao AWAITING_QA -> DONE; entrega 12 de 13 da P-06 encerrada - merge em develop + docs commitados pelo ODEV; falta SÓ a TASK-0049 (interop real, gate humano com o humano presente) para a fase fechar

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-14 17:28:31 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261114-171443-test-gate.log baseline 597/597 |
| 2026-11-14 17:28:31 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261114-172351-test-gate.log gate final pos edicao 597/597 |
| 2026-11-14 17:28:32 | dev | dotnet run --project backend/src/Squid.PeerClient -- --help | 0 | docs/runbooks/peer-entrega-travada.md Diagnostico 3 |
| 2026-11-14 17:28:59 | dev | dotnet run --project backend/src/Squid.PeerClient -- --scenario=nope | 1 | docs/runbooks/peer-entrega-travada.md Diagnostico 3 (cenario desconhecido recusado, exit 1 esperado) |
| 2026-11-14 17:28:59 | dev | dotnet run --project backend/src/Squid.PeerClient -- --scenario=401 | NAO_COLETADO | docs/runbooks/dlq-cheia.md Diagnostico 4 (servidor derrubado apos a prova; linha Failed com signature_rejected gravada) |
| 2026-11-14 17:29:00 | dev | dotnet run --project backend/src/Squid.PeerClient -- --scenario=500 | NAO_COLETADO | docs/runbooks/peer-entrega-travada.md Diagnostico 3 (servidor derrubado apos a prova; linha Pending com send_failed e peer Down gravados) |
| 2026-11-14 17:29:00 | dev | curl.exe -fsS http://127.0.0.1:5601/health | 0 | docs/runbooks/federacao-interop.md Verificacao 1 |
| 2026-11-14 17:29:01 | dev | curl.exe -s -o NUL -w http_code -c $jarA -X POST http://127.0.0.1:5601/api/auth/register -H Content-Type:application/json -d registro com handle alice | 0 | docs/runbooks/federacao-interop.md Verificacao 3 (HTTP 201) |
| 2026-11-14 17:29:01 | dev | curl.exe -s -o NUL -w http_code -b $jarA -c $jarA -X POST http://127.0.0.1:5601/api/containers com corpo de mesmo slug interop-demo | 0 | docs/runbooks/federacao-interop.md Verificacao 4 (HTTP 201 nos dois silos) |
| 2026-11-14 17:29:02 | dev | curl.exe -s -o NUL -w http_code -b $jarB -c $jarB -X POST http://127.0.0.1:5602/api/federation/follow com targetActorUri do silo A | 0 | docs/runbooks/federacao-interop.md Verificacao 5 (HTTP 202) |
| 2026-11-14 17:29:02 | dev | curl.exe -s -o NUL -w http_code -b $jarA -c $jarA -X POST http://127.0.0.1:5601/api/containers/interop-demo/posts com corpo de conteudo | 0 | docs/runbooks/federacao-interop.md Verificacao 6 (HTTP 201 nos dois silos) |
| 2026-11-14 17:29:03 | dev | curl.exe -s -b $jarB http://127.0.0.1:5602/api/containers/interop-demo/feed | 0 | docs/runbooks/federacao-interop.md Verificacao 7 (post da A no feed da B e o inverso) |
| 2026-11-14 17:29:03 | dev | python -c consulta de peer_delivery e peer com argv do banco do silo A | 0 | docs/runbooks/federacao-interop.md Verificacao 8 (Sent e Healthy impressos) |
| 2026-11-14 17:29:04 | dev | curl.exe -s webfinger com resource acct:domtest em 127.0.0.1:5603 com --Instance:Domain=squid-a.test | 0 | docs/runbooks/federacao-interop.md Procedimento 4 (subject acct:domtest@squid-a.test) |
| 2026-11-14 17:29:04 | dev | python -c gerador de chave base64 de 32 bytes | 0 | docs/runbooks/federacao-interop.md Procedimento 1 (44 caracteres) |
| 2026-11-14 17:29:05 | dev | git commit unico na branch task-TASK-0048 | 0 | git show --stat HEAD (8f06ee1, 4 arquivos de docs/runbooks) |
| 2026-11-14 19:00:41 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0048.ps1 | 0 | docs/sdd/qa/QA-0048-static-check.log com 134 PASS 0 FAIL |
| 2026-11-14 19:00:42 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0048.ps1 -RunbooksDir $env:TEMP\qa0048-mut | 4 | docs/sdd/qa/QA-0048-mutation.log com 130 PASS 4 FAIL |
| 2026-11-14 19:00:42 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-run-federation-task-0048.ps1 | 0 | docs/sdd/qa/QA-0048-federation-run.log com 52 PASS 0 FAIL |
| 2026-11-14 19:00:43 | qa | wsl -e bash docs/sdd/qa/qa-check-task-0048.sh | 0 | docs/sdd/qa/QA-0048-static-check.sh.log com 145 PASS 0 FAIL |
| 2026-11-14 19:00:43 | qa | wsl -e bash docs/sdd/qa/qa-check-task-0048.sh /mnt/c/Users/Ryke/AppData/Local/Temp/qa0048-mut | 4 | docs/sdd/qa/QA-0048-mutation.log com 141 PASS 4 FAIL |
| 2026-11-14 19:00:44 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261114-181815-test-gate.log com 597 aprovados 0 falhas |
| 2026-11-14 19:00:44 | qa | git show --stat 8f06ee1 | 0 | docs/sdd/qa/QA-0048-git-scope.txt com 4 arquivos so de docs/runbooks |
| 2026-11-14 19:00:45 | qa | git status --porcelain | 0 | docs/sdd/qa/QA-0048-git-scope.txt com os 2 carimbos do ODEV e os artefatos da QA |
| 2026-11-14 19:00:45 | qa | git commit com a mensagem autorizada TASK-0048 QA-0048 PASSED | 0 | git show --stat 8cfb097 com 9 arquivos de docs/sdd/qa |
