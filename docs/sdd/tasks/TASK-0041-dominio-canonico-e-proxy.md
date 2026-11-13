<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0041 — Domínio canônico e proxy (`Instance:Domain` + `X-Forwarded-*`)

```yaml
runId: R-20261113-01
taskId: TASK-0041
status: DONE
agent: odev
phase: P-06
sequence: 2
createdAt: 2026-11-13 13:47:25
updatedAt: 2026-11-13 16:49:36
parent: SPEC-0011
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Api/Discovery/**", "backend/src/Squid.Api/Posts/**", "backend/src/Squid.Api/Containers/**", "backend/src/Squid.Api/Threads/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/**"]
blocks: ["TASK-0042"]
blockedBy: []
inputArtifacts: ["docs/sdd/specs/SPEC-0011-federacao-p06.md", "docs/sdd/plan/plan.md", "docs/sdd/specs/SPEC-0007-identidade-ator-local-chaves-sessao-kit.md"]
outputArtifacts: ["backend/src/Squid.Api/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/**"]
interviewRounds: 2/6
feasibilityRounds: 0/2
correctionAttempts: 0/2
qaAttempts: 0/2
contextHandoffs: 0/4
progressDelta: 0
humanGate:
  approvedBy: NENHUM
  approvedDate: NENHUM
  scope: NENHUM
  expiresAt: NENHUM
blocker: NENHUM
nextAction: DONE - QA-0041 PASSED (5/5 ACs, gate 484/484, commits 9210469 + 7f0ba03); merge em develop pelo ODEV e dispatch da TASK-0042
evidence: []
memoryBankFlags: []
```

## Intent

Entrega 1 de 9 da P-06 (base da fase, `I-11` do backlog): domínio canônico configurável e
`ForwardedHeaders` de confiança, para que toda URL federada da fase nasça estável. Os quatro
pontos que hoje montam origem a partir de `Request.Scheme`/`Request.Host` passam por um helper
único. Sem config, o comportamento é byte-idêntico ao atual (GAP-45).

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: sem `Instance:Domain` e sem proxy configurado, a origem canônica dos quatro pontos
  atuais (`Discovery`, `Posts`, `Containers`, `Threads`) continua scheme+Host da request —
  comportamento idêntico ao de hoje — teste.
- AC-02: com `Instance:Domain: squid.test`, IDs canônicos publicados (ator, objeto,
  `webfinger.subject` e `links.self`) usam `https://squid.test` mesmo quando o `Host` da request é
  outro (`localhost:5001`) — teste. **Esclarecimento do ODEV (2026-11-13 15:07:04)**: o `notDone`
  do DEV está correto — o id do log interno de `Activity` é `urn:squid:activity:{token}` por design
  da P-04 (GAP-23) e **não** é escopo do I-11; a URL canônica de atividade federada vem nas
  TASK-0044/0045 (cláusula registrada na SPEC-0011 §A).
- AC-03: `X-Forwarded-Proto`/`X-Forwarded-Host` são **ignorados** por padrão e **honrados** só
  quando `Instance:ForwardedHeaders` está habilitado na config — teste.
- AC-04: nenhuma resposta de erro passa a expor o domínio configurado de forma diferente do
  formato já coberto (404 idêntico, sem stack trace — FR-053) — teste.
- AC-05: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1
  test` exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Api/Discovery/**`, `backend/src/Squid.Api/Posts/**`,
  `backend/src/Squid.Api/Containers/**`, `backend/src/Squid.Api/Threads/**` — os quatro pontos de
  origem scheme+Host viram helper único
- `backend/src/Squid.Service/**` — config `Instance:Domain`, `Instance:ForwardedHeaders`,
  encanamento de forwarded headers
- `backend/tests/Squid.Tests/**` — testes das ACs
- produto **fora** dos `touches` desta task: qualquer lacuna levantada como dúvida, não corrigida aqui

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-45 | no | `Instance:Domain` é opt-in; sem config a origem continua scheme+Host byte-idêntica; `X-Forwarded-*` só com proxy de confiança configurado |
| I-11 | no | item de backlog absorvido por esta task (decisão 4 da SPEC-0011) — nada além dele entra aqui |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | com `Instance:Domain` configurado, o webfinger aceita só o domínio canônico ou também o `Host` da request? | SPEC-0011 §A (nível especificação) + SPEC-0007 B-09a | aceita **canônico ou Host alcançado** e publica sempre o canônico (`subject = acct:{nome}@{Instance:Domain}`); domínio estrangeiro continua `404` — adjudicada pelo ODEV 2026-11-13, cláusula escrita na spec, código do DEV coerente (não muda) |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-13 13:47:25 | odev | task created from SPEC-0011 (entrega 1 de 9, base da P-06, I-11; blockedBy: nenhum)
- 2026-11-13 14:00:29 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; runId R-20261113-01; autorizacao de commit gate PHASE 2026-11-11 20:42:28 (modelo C) incluida - nova branch task/TASK-0041-dominio-canonico-e-proxy desde develop
- 2026-11-13 14:58:44 | dev | feasibility r1: quatro pontos de origem (Discovery, Posts, Containers, Threads) e a checagem B-09a do webfinger localizados no codigo; touches confirmados sem correcao; baseline do gate verde (480 testes, exit 0); D-01 (aceitacao do dominio no webfinger com Instance:Domain) fechada pela SPEC-0011 secao A sobre a regra B-09a, sem duvida bloqueante
- 2026-11-13 14:59:02 | dev | implementacao: CanonicalOrigin (Squid.Api/Discovery) vira a origem unica dos quatro pontos; Instance:Domain opt-in, sempre https, lido da configuracao da propria request (nunca estatico, para nao vazar entre hosts); ForwardedHeaders instalado so com Instance:ForwardedHeaders, e o webfinger aceita o dominio canonico ou o Host alcanca-do, publicando sempre o canonico
- 2026-11-13 14:59:02 | dev | testes: CanonicalOriginConfigTests cobre AC-01..AC-04 (4/4 aprovados, exit 0); gate completo 484/484 exit 0 (baseline 480 + 4 novos); diff restrito aos touches
- 2026-11-13 15:08:04 | odev | ODEV verificou antes de transicionar: gate 20261113-145735-test-gate.log lido no arquivo (exit: 0, Aprovado 484, Com falha 0, 5m24s); commit 9210469 conferido via git show --stat (7 arquivos: 6 de produto nos touches + 1 teste, NENHUM fora); git status = so docs WIP (plan.md, SPEC-0011, TASK-0041..0049 nao commitados); branch task/TASK-0041-dominio-canonico-e-proxy sobre develop (6b04211); sdd-state check log 5 evidence 4; step result DONE aceito com logLines/evidenceRows completos e sem timestamps inventados
- 2026-11-13 15:08:04 | odev | adjudicacoes do ODEV antes do dispatch QA: D-01 fechada no nivel especificacao (clarausula escrita na SPEC-0011 secao A: webfinger aceita canonico ou Host alcanca-do e publica sempre o canonico); AC-02 esclarecida (o notDone do DEV esta correto - id do log Activity continua urn: por design P-04/GAP-23, URL de atividade federada e das TASK-0044/0045) - nenhuma AC perde comportamento ja entregue
- 2026-11-13 15:08:06 | odev | transicao IN_DEVELOPMENT -> AWAITING_QA (attempt 1/2) - dispatch QA a seguir
- 2026-11-13 16:35:43 | qa | QA-0041 attempt 1/2: entrou pelo task file (status AWAITING_QA); SPEC-0011 secao A inteira + AC-01..AC-05 com o esclarecimento do ODEV na AC-02 lidos; rota qa-integracao do memory bank; os 4 testes da config (AC-01..AC-04) e o gate+escopo (AC-05) mapeados
- 2026-11-13 16:35:43 | qa | baseline da QA 4/4 exit 0 (20261113-153601-test-filter.log) e -VerifyOnly 12/12 padroes hits=1 + ASCII OK (QA-0041-verify.log)
- 2026-11-13 16:35:43 | qa | 12 ciclos can-fail executados contra o PRODUTO e todos vermelhos DENTRO do log com a AC alvo em cada um (RED-AC, 12/12 EXPECT-OK): M1 fallback forca https (AC-01), M2 chave Instance:Domain morta (AC-02), M3 subject ecoa o dominio pedido (D-01), M4a Host alcancado recusado (AC-01/02/03), M4b dominio canonico recusado (AC-02), M5 middleware sem a chave (AC-03a), M6 middleware nunca instalado (AC-03b), M7 404 do webfinger nomeia a origem (AC-04), M8..M11 um ponto de origem revertido cada - Discovery/Posts/Containers/Threads (AC-02); restore byte-exact 12/12 com mtime, GREEN 4/4, residuo 0
- 2026-11-13 16:35:43 | qa | gate AC-05 lido DENTRO do log 20261113-162146-test-gate.log (S-22): exit 0 / 484 de 484 / 6m1s; escopo (QA-0041-scope.log): blob 6/6 igual a 9210469, mtime 6, residuo 0, git backend 0, fora-dos-touches 0 (as 11 entradas de docs sao o WIP do ODEV da baseline, inalterado)
- 2026-11-13 16:43:47 | qa | checkers executados: qa-check-task-0041.ps1 SUMMARY total=45 fails=0 (SDD-STATE check: OK - log entries 12, evidence rows 10) e gemeo qa-check-task-0041.sh no WSL SUMMARY total=38 fails=0; verdict PASSED escrito em docs/sdd/qa/QA-0041-dominio-canonico-e-proxy.md; esclarecimento da AC-02 do ODEV aplicado como esta (activity urn: fora de escopo I-11, URL de atividade federada e TASK-0044/0045)
- 2026-11-13 16:50:59 | odev | ODEV verificou antes de transicionar: QA-0041-dominio-canonico-e-proxy.md com verdicto PASSED lido no arquivo; gate 20261113-162146-test-gate.log exit: 0 / Aprovado 484 / Com falha 0 / 6m1s (lido no arquivo); commit QA 7f0ba03 conferido via git show --stat (10 arquivos so docs/sdd/qa/, pai 9210469, sem push, main intacta); resido QA-MUTATION 0 (QA-0041-scope.log), blob 6/6 = 9210469; sdd-state check OK (log 13, evidence 12) - veredito aceito
- 2026-11-13 16:50:59 | odev | QA-0041 PASSED attempt 1/2 (PRODUTO+TESTES): 5/5 ACs, 12 ciclos can-fail contra o PRODUTO todos vermelhos com a AC alvo (RED-AC 12/12 EXPECT-OK, incluindo M8..M11 - um ponto de origem revertido cada), restore byte-exact 12/12, GREEN 4/4; gate 484/484; unverified registrados (suite Linux/WSL sem dotnet, proxy real de topologia da TASK-0047)
- 2026-11-13 16:51:01 | odev | transicao AWAITING_QA -> DONE; entrega 1 de 9 da P-06 encerrada - merge em develop + docs commitados pelo ODEV, dispatch da TASK-0042 a seguir
- 2026-11-13 16:52:09 | odev | merge em develop pelo ODEV no DONE: ad2c36e (merge --no-ff da branch task/TASK-0041-dominio-canonico-e-proxy) + 004b1be (docs da fase: SPEC-0011, TASK-0041..0049, plan.md); git status limpo, sem push, main intacta

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-13 14:59:33 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (baseline antes das alteracoes) | 0 | docs/sdd/runs/test-runs/20261113-141900-test-gate.log |
| 2026-11-13 14:59:33 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 0 | stdout do comando: build: exit 0 |
| 2026-11-13 14:59:34 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~CanonicalOriginConfigTests | 0 | docs/sdd/runs/test-runs/20261113-144932-test-filter.log |
| 2026-11-13 14:59:34 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate AC-05, suite inteira com BuildProof) | 0 | docs/sdd/runs/test-runs/20261113-145735-test-gate.log |
| 2026-11-13 16:36:08 | qa | git rev-parse HEAD + git rev-parse --abbrev-ref HEAD + git status --porcelain -uall + wsl -e bash -c command -v dotnet | 0 | docs/sdd/qa/QA-0041-git-baseline.txt (HEAD 9210469, branch task/TASK-0041-dominio-canonico-e-proxy, wsl-dotnet dotnet-missing) |
| 2026-11-13 16:36:09 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~CanonicalOriginConfigTests (baseline da QA) | 0 | docs/sdd/runs/test-runs/20261113-153601-test-filter.log (exit: 0, Com falha 0 / Aprovado 4 / Total 4) |
| 2026-11-13 16:36:09 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0041.ps1 -VerifyOnly | 0 | docs/sdd/qa/QA-0041-verify.log (12 padroes hits=1, ASCII OK, VERIFY total=12 bad=0) |
| 2026-11-13 16:36:10 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0041.ps1 (run cheio: 12 ciclos + GREEN) | 0 | docs/sdd/qa/QA-0041-mutations.log - 12 vermelhos com RED-AC da AC alvo em cada ciclo, 12 RESTORE-OK, GREEN 4/4, SUMMARY cycles=12 failed=0 residue=0 temp-backups=0 |
| 2026-11-13 16:36:10 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate AC-05) | 0 | docs/sdd/runs/test-runs/20261113-162146-test-gate.log - exit: 0 / Aprovado 484 / Com falha 0 / 6m1s (lido no arquivo) |
| 2026-11-13 16:36:11 | qa | varredura de escopo pwsh (blob-identity, mtime, residuo, diff contra a baseline do git) | 0 | docs/sdd/qa/QA-0041-scope.log - blob 6/6 match=True, mtime-count=6, residue=0, git-backend-entries=0, fora-dos-touches=0 |
| 2026-11-13 16:43:47 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0041.ps1 | 0 | docs/sdd/qa/QA-0041-check.log - SUMMARY total=45 fails=0 (SDD-STATE check: OK - log entries 12, evidence rows 10) |
| 2026-11-13 16:43:48 | qa | wsl -e bash docs/sdd/qa/qa-check-task-0041.sh (gemeo Linux, parte estatica) | 0 | docs/sdd/qa/QA-0041-check-sh.log - SUMMARY total=38 fails=0 |
