<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0045 — Saída: fan-out por visibilidade, reavaliação R-05 e rota de follow

```yaml
runId: R-20261113-01
taskId: TASK-0045
status: DONE
agent: odev
phase: P-06
sequence: 2
createdAt: 2026-11-13 13:47:25
updatedAt: 2026-11-14 01:35:17
parent: SPEC-0011
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Persistence/schema/**", "backend/src/Squid.Api/Posts/**", "backend/src/Squid.Api/Federation/**", "backend/src/Squid.Api/Containers/**", "backend/src/Squid.ReadModels/**", "backend/src/Squid.Protocols/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/**"]
blocks: ["TASK-0046"]
blockedBy: ["TASK-0044"]
inputArtifacts: ["docs/sdd/specs/SPEC-0011-federacao-p06.md", "docs/sdd/plan/plan.md", "specs/001-squid-mvp/contracts/activitypub.md", "specs/001-squid-mvp/data-model.md", "specs/001-squid-mvp/research.md"]
outputArtifacts: ["backend/src/Squid.Persistence/schema/0008_federation.sql", "backend/src/Squid.Api/Posts/**", "backend/src/Squid.Api/Federation/**", "backend/src/Squid.ReadModels/**", "backend/tests/Squid.Tests/**"]
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
nextAction: DONE - QA-0045 PASSED (7/7 ACs, 10 mutacoes, gate 539/539, commits 3226429 + bfe4052); merge em develop pelo ODEV e dispatch da TASK-0046
evidence: []
memoryBankFlags: []
```

## Intent

Entrega 5 de 9 da P-06: o caminho de **saída** — fan-out do log `Activity` por visibilidade
(contrato §4, §3.2 do doc 05, FR-037), reavaliação da política **no momento do envio** (R-05,
FR-038 → `Cancelled` auditável), fila `PeerDelivery` em migration versionada `0008_federation.sql`
e a rota local `POST /api/federation/follow` (GAP-41) que nasce a relação de seguidor. O motor de
entrega (backoff/DLQ/circuito) é da TASK-0046: aqui as entregas nascem `Pending` e são observáveis.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: publicar post `public` cria `PeerDelivery` para cada seguidor remoto do autor (e para a
  coleção `followers` do contêiner) com `to`/`cc` do contrato §2.1 (`Public` em `to`, `followers`
  em `cc`), e o payload **nomeia o destino** do lado de fora: `audience` com a URI do contêiner e
  `squid:slug` no objeto — é o que a entrada remota resolve pela D-01 (SPEC-0011 §E, decisão
  humana 2026-11-13) — teste.
- AC-02: `unlisted` entrega **só** a seguidores; `membersOnly` entrega **só** a atores com
  `Membership.State = Active` com `squid:policy` no payload e nada em `cc`; `private` não cria
  **nenhuma** `PeerDelivery` — teste (§7.2, §7.3).
- AC-03: `POST /api/federation/follow` (sessão + CSRF) gera `Follow` assinado enfileirado para o
  par; `Accept` remoto cria a relação seguidor local; `Follow` remoto respondido com `Accept` —
  teste (GAP-41/GAP-46).
- AC-04: reavaliação R-05 — com entrega `Pending`, mudar a política para negar o destinatário e
  disparar o envio → entrega vira `Cancelled` com motivo auditável e **nada** é enviado; sem erro,
  sem retry — teste (FR-038, §7.7).
- AC-05: publicar retorna ao cliente **sem esperar** a entrega remota: a resposta local chega com
  as entregas já registradas em estado `Pending` (assíncrono, FR-036) — teste.
- AC-06: remover o post local propaga `Delete` assinado a todos os destinatários que receberam o
  `Create` — teste (R-08).
- AC-07: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1
  test` exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Persistence/schema/**` — `0008_federation.sql` idempotente (`follow`,
  `peer`, `peer_delivery`; liberação teste/dev/CI vale, produção é gate humano)
- `backend/src/Squid.Api/Posts/**` — hook de fan-out na publicação e no `DELETE`
- `backend/src/Squid.Api/Federation/**` — `POST /api/federation/follow`
- `backend/src/Squid.Api/Containers/**` — destinatários `followers` do contêiner
- `backend/src/Squid.ReadModels/**` — leitura de seguidores remotos para o fan-out
- `backend/src/Squid.Protocols/**` — montagem do envelope de saída (Deliver)
- `backend/src/Squid.Service/**` — encanamento
- `backend/tests/Squid.Tests/**` — testes das ACs
- produto **fora** dos `touches` desta task: qualquer lacuna levantada como dúvida, não corrigida aqui

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-41 | no | rota local `POST /api/federation/follow` sem UI (P-08 consume) — sem ela o DoD dos dois silos não tem por onde nascer a relação |
| GAP-48 | no | D-01 respondida pelo humano (2026-11-13 21:23:34): entrada só aplica `Create` com destino nomeado — por isso esta task **nomeia o destino na saída** (`audience` + `squid:slug`, SPEC-0011 §E); contrato não muda (campos extras preservados pela leitura tolerante §6) |
| GAP-43 | no | saída limitada a `Create`, `Follow`, `Accept`, `Undo(Follow)`, `Delete`; `Like`/`Announce`/`Flag` de saída fora |
| — | no | motor de entrega (backoff, circuito, DLQ, rate por par) é da TASK-0046; aqui a entrega nasce `Pending` e é observável — cada entrega é funcional por si |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-13 13:47:25 | odev | task created from SPEC-0011 (entrega 5 de 9, fan-out + R-05 + migration 0008 + rota de follow; blockedBy TASK-0044)
- 2026-11-13 23:44:06 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; runId R-20261113-01; blockedBy TASK-0044 resolve (DONE com QA-0044 PASSED 8/8, gate 529/529, merge 28c72b4 em develop + docs 68db56e); AC-01 emendada pelo ODEV antes do dispatch (nomeacao de destino audience + squid:slug, GAP-48 - gate humano D-01 2026-11-13 21:23:34); autorizacao de commit gate PHASE 2026-11-11 20:42:28 (modelo C) incluida - nova branch task/TASK-0045-saida-fan-out-e-follow desde develop; logLines e evidenceRows sem relogio para o ODEV carimbar (sem pipe em comandos)
- 2026-11-14 00:49:56 | dev | feasibility 1/2: task, control block e toques conferidos contra SPEC-0011 secao F, contrato activitypub 2.1/4/7, data-model 2.9/3.4 e R-05; baseline da suite registrada em 529/529 exit 0 antes do primeiro edit; sem duvida bloqueante - a regra de destinatarios do secao 4 fecha a leitura
- 2026-11-14 00:49:56 | dev | migration 0008_federation.sql criada idempotente (peer_delivery, peer, follow); IFollowStore do TASK-0044 adotada como porta da relacao sem mudar nenhum chamador, a tabela follow nasce reservada sem escritor
- 2026-11-14 00:49:57 | dev | portas novas na borda (IPeerDeliveryStore, IOutboundPipeline, IPeerSender) e adaptadores em Squid.Service, com IPeerSender registrada antes da seam de teste para o fake substituir a rede
- 2026-11-14 00:49:57 | dev | fan-out acoplado na publicacao e na resposta de post e reply, e na remocao local: escrita local de PeerDelivery Pending antes do 201/200, nenhum envio na request (FR-036), motor de drenagem continua sendo a TASK-0046
- 2026-11-14 00:49:58 | dev | payload do Create montado em ObjectJsonLd.Create com to/cc da visibilidade (Public em to e followers em cc; unlisted followers em to; membersOnly membros em to e cc vazio com squid:policy), audience com a URI do contêiner e squid:slug no objeto - emenda GAP-48 da AC-01 aplicada na saida
- 2026-11-14 00:49:58 | dev | SendAsync executa a reavaliacao R-05 com a visibilidade mais restritiva entre a congelada e a vigente: negada grava Cancelled com motivo auditavel e incrementa federation.delivery_cancelled sem consultar o sender; permitida assina draft-cavage na hora e faz o POST pelo seam
- 2026-11-14 00:49:59 | dev | rota POST /api/federation/follow valida sessao, CSRF e alvo remoto, enfileira o Follow e grava a borda pending em IFollowStore; Accept remoto estabelece e Follow remoto responde Accept (aplicacao da TASK-0044)
- 2026-11-14 00:49:59 | dev | 9 testes novos (8 ACs + migration 0008) executados verdes; gate completo re-executado em 538/538 exit 0; commit unico 3226429 na branch task/TASK-0045-saida-fan-out-e-follow criada de develop 68db56e, apenas com arquivos de backend (arquivo da task intocado, segue no working tree para o ODEV carimbar)
- 2026-11-14 00:50:01 | odev | ODEV verificou antes de transicionar: gate 20261114-004534-test-gate.log lido no arquivo (exit: 0, Aprovado 538, Com falha 0, 6m31s) e filtro 20261114-003830-test-fast.log (exit: 0, 38/38); commit 3226429 conferido via git show --stat (16 arquivos, todos nos touches - Containers/** e ReadModels/** so lidos, fora do commit); logLines (8) e evidenceRows (3) carimbados via sdd-state; step result DONE aceito
- 2026-11-14 00:50:02 | odev | flag [SUSPEITO] do DEV VERIFICADA NO CODIGO pelo ODEV: AuthEndpoints.cs:68 cria RootKey.Create() por ator (RK aleatoria por usuario), logo a Squid:RecoveryKey de boot NAO desembrulha a RSA de usuarios registrados por HTTP - contradicao SPEC-0011 secao B x codigo P-02, toca data-model (campo extra embrulhado) = gate humano; QUESTAO enviada ao humano nesta sessao, resposta vira a TASK-0051 antes da TASK-0047; testes desta task contornam escrevendo o ator direto, ACs nao cobrem ator registrado
- 2026-11-14 00:50:04 | odev | transicao IN_DEVELOPMENT -> AWAITING_QA (attempt 1/2) - dispatch QA a seguir
- 2026-11-14 01:37:06 | qa | baseline da QA capturada antes de qualquer escrita: docs/sdd/qa/QA-0045-git-baseline.txt - HEAD 3226429 na branch task/TASK-0045-saida-fan-out-e-follow, 16 arquivos do commit todos nos touches, WSL sem dotnet (suite Linux = unverified)
- 2026-11-14 01:37:06 | qa | filtro Federation antes de qualquer escrita da QA: exit 0, 35/35 (docs/sdd/runs/test-runs/20261114-010209-test-filter.log); depois de QaTask0045FanOutGapTests.cs entrar no filtro: exit 0, 36/36 (20261114-010649-test-filter.log)
- 2026-11-14 01:37:07 | qa | cenario novo QaTask0045FanOutGapTests.cs escrito dentro dos touches para a clausula da AC-01 (colecao followers do contêiner) e executado verde, com expectativas derivadas das stores
- 2026-11-14 01:37:08 | qa | qa-mutate-task-0045.ps1 -VerifyOnly: exit 0, VERIFY total=10 bad=0 (10 padroes hits=1 hits2=1, ASCII ok) em docs/sdd/qa/QA-0045-verify.log
- 2026-11-14 01:37:08 | qa | qa-mutate-task-0045.ps1: exit 0 - 10 ciclos can-fail vermelhos com o RED-AC da propria AC (falhas 1 a 5), 10x RESTORE-OK hash=PRE residue=0, GREEN 36/36 exit 0, SUMMARY cycles=10 failed=0 residue-backend=0, ledger em docs/sdd/qa/QA-0045-mutations.log
- 2026-11-14 01:37:09 | qa | qa-scope-task-0045.ps1: exit 0 - blob 3/3 match, mtime-count=4, residue=0, git-backend-entries=1, fora-dos-touches=0 em docs/sdd/qa/QA-0045-scope.log
- 2026-11-14 01:37:09 | qa | gate AC-07 sdd-test.ps1 test: exit 0 lido dentro do log 20261114-012335-test-gate.log - Com falha 0, Aprovado 539, Total 539 (538 DEV + 1 QA), 6m32s
- 2026-11-14 01:37:10 | qa | checkers finais: qa-check-task-0045.ps1 exit 0 (SUMMARY total=47 fails=0, inclui sdd-state check OK log 13 evidence 3) e wsl -e bash qa-check-task-0045.sh exit 0 (SUMMARY total=34 fails=0); run 1 do checker com o parser do cabecalho da baseline corrigido preservada em QA-0045-check-run1.log
- 2026-11-14 01:37:10 | qa | verdict PASSED 7/7 com nenhum BLOCKER; commit unico bfe4052 na branch task/TASK-0045-saida-fan-out-e-follow com os 13 artefatos da QA, task file fora do commit (WIP do ODEV) para o carimbo
- 2026-11-14 01:37:11 | qa | finding de adjudicacao registrado na QA-0045 finding 1: contrato secao 4 e doc 05 3.2 separam a colecao followers do contêiner de Membership.State=Active, o produto so tem a segunda e a leitura do DEV e a unica implementavel no MVP - AC nao foi alterada
- 2026-11-14 01:37:18 | odev | ODEV verificou antes de transicionar: QA-0045-saida-fan-out-e-follow.md com verdict PASSED lido no arquivo; gate 20261114-012335-test-gate.log exit: 0 / Aprovado 539 / Com falha 0 / 6m32s (lido no arquivo); mutations SUMMARY cycles=10 failed=0 residue-backend=0 lido no log; commit bfe4052 conferido via git show --stat (13 arquivos: 12 docs/sdd/qa + QaTask0045FanOutGapTests.cs dentro dos touches); logLines (10) e evidenceRows (10) carimbados via sdd-state - veredito aceito
- 2026-11-14 01:37:19 | odev | finding 1 da QA-0045 ADJUDICADO no nivel especificacao: colecao followers do contêiner = membrias Active remotas do contêiner no MVP (a relacao propria nao existe - federaçao de contêineres e backlog, nota D4-e); clausula escrita na SPEC-0011 secao F; leitura do DEV confirmada, AC intacta, QaTask0045FanOutGapTests fixa a leitura; substituicao pela relacao propria quando a federaçao de contêineres existir
- 2026-11-14 01:37:20 | odev | QA-0045 PASSED attempt 1/2 (PRODUTO+TESTES): 7/7 ACs, 10 ciclos can-fail todos vermelhos na AC alvo, restore byte-exact, GREEN 36/36; gate 539/539; achado de produto nenhum; notas: alvo follow aceita URI de contêiner remoto, meniçoes fora das AC, R-05 so no Create (decisao documentada) - todas registradas para consolidacao
- 2026-11-14 01:37:20 | odev | transicao AWAITING_QA -> DONE; entrega 5 de 10 da P-06 encerrada - merge em develop + docs commitados pelo ODEV, dispatch da TASK-0046 a seguir

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-14 00:50:00 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261114-001119-test-gate.log (baseline 529/529 pre-edit) |
| 2026-11-14 00:50:00 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -Fast -TestFilter FullyQualifiedName~Federation | 0 | docs/sdd/runs/test-runs/20261114-003830-test-fast.log (38/38, filtro Federation) |
| 2026-11-14 00:50:01 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261114-004534-test-gate.log (gate AC-07, 538/538) |
| 2026-11-14 01:37:11 | qa | git rev-parse HEAD + git rev-parse --abbrev-ref HEAD + git status --porcelain -uall + git show --name-only + sondas wsl command -v dotnet e command -v git (pwsh) | 0 | docs/sdd/qa/QA-0045-git-baseline.txt |
| 2026-11-14 01:37:12 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Federation | 0 | docs/sdd/runs/test-runs/20261114-010209-test-filter.log (baseline 35/35) |
| 2026-11-14 01:37:12 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Federation | 0 | docs/sdd/runs/test-runs/20261114-010649-test-filter.log (36/36 com o teste da QA) |
| 2026-11-14 01:37:13 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0045.ps1 -VerifyOnly | 0 | docs/sdd/qa/QA-0045-verify.log |
| 2026-11-14 01:37:14 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0045.ps1 | 0 | docs/sdd/qa/QA-0045-mutations.log |
| 2026-11-14 01:37:14 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-scope-task-0045.ps1 | 0 | docs/sdd/qa/QA-0045-scope.log |
| 2026-11-14 01:37:14 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261114-012335-test-gate.log (gate AC-07, 539/539) |
| 2026-11-14 01:37:15 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0045.ps1 | 0 | docs/sdd/qa/QA-0045-check.log |
| 2026-11-14 01:37:15 | qa | wsl -e bash docs/sdd/qa/qa-check-task-0045.sh | 0 | docs/sdd/qa/QA-0045-check-sh.log |
| 2026-11-14 01:37:16 | qa | git add dos 13 artefatos da QA + git commit TASK-0045 QA-0045 PASSED + git show --stat -1 HEAD (pwsh) | 0 | commit bfe4052 |
