<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0009 — Workflow GitHub Actions de build + testes (ubuntu) com prova local

```yaml
runId: R-20261106-01
taskId: TASK-0009
status: DONE
agent: odev
phase: P-01
sequence: 2
createdAt: 2026-11-06 23:18:15
updatedAt: 2026-11-07 15:31:56
parent: SPEC-0005
authority: SPEC
parallel: true
touches: [".github/**", "docs/sdd/_tools/sdd-test.ps1", "docs/sdd/_tools/sdd-test.sh"]
blocks: []
blockedBy: [TASK-0008]
inputArtifacts: ["docs/features/05-arquitetura-consolidada.md", "docs/sdd/specs/SPEC-0005-ci-github-actions.md"]
outputArtifacts: [".github/workflows/ci.yml"]
interviewRounds: 0/6
feasibilityRounds: 0/2
correctionAttempts: 0/2
qaAttempts: 0/2
contextHandoffs: 0/4
progressDelta: 0
humanGate:
  approvedBy: Henrique
  approvedDate: 2026-11-06 23:15:00
  scope: decisoes de entrevista da SPEC-0005 - runner so ubuntu (aceita risco de falhas de plataforma novas) e prova local (workflow valido + mesmos comandos verdes aqui; primeiro run no GitHub e gate humano posterior apos push do humano - ODEV nunca faz push)
  expiresAt: NENHUM
blocker: NENHUM
nextAction: DONE - QA-0008 PASSED e PENDENCIA HUMANA RESOLVIDA: push feito e primeiro run do GitHub Actions VERDE (build exit 0, gate 98/98 em ubuntu-26.04, cache salvo, 0 errors)
evidence: []
memoryBankFlags: [MUDANCA_RELEVANTE: BUILD - workflow .github/workflows/ci.yml entregue (push develop/main + pull_request, permissions contents read, setup-dotnet 10.0.403 pinado, cache NuGet hashFiles Directory.Packages.props, passos pwsh sdd-test.ps1 build/test) e validado localmente, mas NUNCA executado no GitHub: primeiro run e gate humano pos-push - source: .github/workflows/ci.yml] [MUDANCA_RELEVANTE: BUILD - secao CI de docs/memory-bank/build-run-test.md ainda diz nenhum pipeline criado ainda; consolidar no phase close apos o run humano - source: docs/memory-bank/build-run-test.md:39]
```

## Intent

Sprint 0 do doc 05: "CI: build + testes" em GitHub Actions (§7.4). É a entrega que garante que
todo PR rode o mesmo gate que o local — build + suite completa — antes de virar código aceito.
As decisões de escopo (só ubuntu, prova local com gate de push posterior) são do humano em
2026-11-06 e estão no `humanGate`.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `.github/workflows/` contém workflow YAML válido (parse sem erro — ferramenta de
  validação executada, não inspeção visual) que dispara em `push` para `develop`/`main` e em
  `pull_request`, com `permissions: contents: read`.
- AC-02: o workflow executa **build** e **gate de testes** usando os MESMOS comandos do gate
  local (`sdd-test.ps1` ou `sdd-test.sh` conforme GAP-17 — a escolha provada por execução está
  registrada no log); nenhum `continue-on-error` em build/teste; sem segredo no YAML
  (grep por token/patterns de segredo = 0 hits).
- AC-03: SDK .NET 10 **pinado** no `setup-dotnet` (versão explícita, não `latest` flutuando)
  e cache de NuGet com chave derivada de `Directory.Packages.props`.
- AC-04: prova local — os mesmos comandos que o workflow invoca saem com **exit 0** nesta
  máquina (build + gate completo), e a validação do YAML é executada com exit 0.
- AC-05: nenhuma mudança fora de `.github/**` e dos scripts `_tools` (se GAP-17 exigir ajuste
  de compatibilidade Linux, o ajuste é mínimo e registrado); gate completo `sdd-test.ps1 test`
  segue exit 0 com 97/97 ou mais.
- AC-06: o workflow NÃO é dado como executado — sem push nesta tarefa; a evidência do primeiro
  run no GitHub é gate humano posterior e a task registra isso como pendência explícita.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `.github/**`
- `docs/sdd/_tools/sdd-test.ps1`
- `docs/sdd/_tools/sdd-test.sh`

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-16 | no | gatilhos `push` em `develop`/`main` + `pull_request` — padrão do ecossistema |
| GAP-17 | no | script no ubuntu: `pwsh sdd-test.ps1` se compatível com Linux, senão `sdd-test.sh` — DEV prova qual roda e registra a escolha |
| GAP-02, GAP-13 | no | herdados: restore com rede existe na CI; sem rede é ambiente (exit 3) |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`

- 2026-11-06 23:18:15 | odev | task created from SPEC-0005; decisoes do humano 2026-11-06: runner so ubuntu e prova local com gate de push posterior; remote github.com/rykehg/squid-execution confirmado (branch develop), .github inexistente
- 2026-11-06 23:19:32 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; blockedBy TASK-0008 DONE; parallel true (nenhum outro aberto toca .github/); decisoes do humano no humanGate (ubuntu only, prova local)
- 2026-11-06 23:47:50 | dev | feasibility round 1: a tarefa tem sentido - unit .github/workflows/ci.yml, chamador GitHub (push em develop/main e pull_request), nenhum dado de produto; touches correto e completo (nenhum outro task aberto toca .github: TASK-0001..0008 DONE); baseline pre-edicao sdd-test.ps1 build exit 0 e gate 97/97 exit 0
- 2026-11-06 23:47:50 | dev | GAP-17: pwsh ausente nesta maquina (RUNBOOK manda usar powershell 5.1) - gate executado com powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build e test, ambos exit 0 (97/97): prova executavel do .ps1
- 2026-11-06 23:47:50 | dev | GAP-17: o gemeo docs/sdd/_tools/sdd-test.sh executado no Git Bash saiu exit 1 - stderr no *.sln found within depth 3 e depois MSB1003; o repo so tem backend/Squid.slnx e o find -name '*.sln' nao casa .slnx - o .sh nao roda neste repo sem mudanca nele
- 2026-11-06 23:47:50 | dev | GAP-17 decidido por execucao: o workflow invoca pwsh -NoProfile -File docs/sdd/_tools/sdd-test.ps1 build e test (mesmos comandos do gate local); nenhum ajuste nos scripts _tools foi necessario - grep de API Windows-only (Start-Process, WMI, registry) sem ocorrencia; o run real em ubuntu e gate humano pos-push
- 2026-11-06 23:47:50 | dev | ci.yml criado: push develop/main + pull_request, permissions contents read, ubuntu-latest, setup-dotnet com 10.0.403 pinado, cache NuGet com chave hashFiles de Directory.Packages.props e NUGET_PACKAGES fixado em github.workspace/.dotnet-home/nuget (mesmo caminho que o sdd-test.ps1 usa aqui), passos build e test sem continue-on-error, zero segredo
- 2026-11-06 23:47:50 | dev | AC-01/AC-03/AC-04: PyYAML 6.0.3 validou ci.yml com exit 0 (parse, gatilhos, permissions, pin exato, chave de cache, 0 continue-on-error, 0 hits de padrao de segredo) e gate final apos a edicao: build exit 0 e test exit 0 com 97/97
- 2026-11-06 23:47:50 | dev | AC-06: sem push e sem run no GitHub nesta tarefa - pendencia explicita registrada: o primeiro run no GitHub so acontece apos push humano (gate humano posterior, fora do alcance do DEV e do ODEV)
- 2026-11-06 23:47:50 | dev | Nenhum D-NN aberto: build+test publico na CI nao exige segredo nem token (permissions contents read apenas) e runner so ubuntu esta decidido no humanGate - nada muda contrato, modelo de dado ou AC
- 2026-11-06 23:57:32 | odev | ODEV verificou antes de transicionar: check exit 0 (10 logs, 9 evidencias); gate final 97/97 exit 0 lido de docs/sdd/runs/test-runs/20261106-234007-test-gate.log; ci.yml presente com push develop/main + pull_request e permissions contents: read; git status so mostra ?? .github/ (delta da task) e branch develop em sincronia com origin (nenhum push - AC-06 respeitada)
- 2026-11-06 23:57:32 | odev | GAP-17 adjudicado por execucao do DEV: workflow invoca pwsh sdd-test.ps1 build/test (mesmos comandos do gate local); gemeo sdd-test.sh provado exit 1 neste repo (acha so *.sln, solution e *.slnx) - sem mudanca em _tools; escolha registrada no log da task
- 2026-11-06 23:57:32 | odev | status IN_DEVELOPMENT -> AWAITING_QA
- 2026-11-07 00:30:38 | qa | QA attempt 1/2 (runId R-20261106-01): entrada na task status AWAITING_QA; AC-01..AC-06 mapeados em cenarios executaveis; memoria bank rota qa-integracao lida (build-run-test.md, integrations.md, contracts.md - 99 linhas); QA-0008 escrito em docs/sdd/qa/QA-0008-ci-github-actions.md
- 2026-11-07 00:30:38 | qa | AC-01/AC-02/AC-03: qa-check-task-0009.ps1 executado no Windows com PyYAML 6.0.3 (py -V:Astral\CPython3.13.18) - 13 checks 0 FAIL exit 0: parse sem erro, push branches exatamente develop+main, pull_request presente, permissions contents read, run steps exatamente pwsh sdd-test.ps1 build e test, 0 continue-on-error, 0 hits de segredo (python e Select-String independentes), pin 10.0.403 exato, cache key hashFiles Directory.Packages.props com arquivo existente
- 2026-11-07 00:30:38 | qa | mutacoes M1-M5 em copias fora do repo (tab no YAML, pin para latest, build virou dotnet build, continue-on-error, permissions write): cada uma exit 1 com o FAIL esperado e totalFAILs=1; SHA256 do ci.yml real ECF70433...A0FE8 identico antes e depois; baseline apos mutacoes exit 0 - prova de que os checks falham de verdade
- 2026-11-07 00:30:38 | qa | AC-04: gate local executado por QA - build exit 0 (0 avisos 0 erros) e test exit 0 com Aprovado 97 Com falha 0 Total 97 (log docs/sdd/runs/test-runs/20261107-001518-test-gate.log); validacao PyYAML standalone exit 0 (6.0.3 dict)
- 2026-11-07 00:30:38 | qa | AC-05: git status + mtime desde 2026-11-06 23:18:15 - nenhum arquivo novo fora de .github/**; unico delta do DEV e .github/workflows/ci.yml 23:38:27; plan.md 23:19:58 e TASK-0009 23:57:32 sao escritas de fluxo do ODEV (achado F1 INFO); sdd-test.ps1 21:48:20 e sdd-test.sh 2026-10-31 sao entregas pre-existentes de TASK-0008, nao tocados
- 2026-11-07 00:30:38 | qa | AC-06: HEAD=origin/develop=9e867d9, rev-list 0 0, sem [ahead], 0 commits desde a criacao da task (HEAD commit 2026-11-05 23:41:18) - nenhum push encontrado; run no GitHub segue registrado como pendencia explicita de gate humano pos-push
- 2026-11-07 00:30:38 | qa | gemeo Linux qa-check-task-0009.sh executado no WSL (Fedora 44, python3 3.14.8 + PyYAML 6.0.3): 13 checks 0 FAIL exit 0; AC-04 no Linux = UNVERIFIED (pwsh e dotnet ausentes na distro) - nunca assumido verde; GAP-17 re-provado: bash sdd-test.sh build exit 3 duas vezes (DEV registrou 1 - achado F2 INFO, conclusao da escolha do .ps1 inalterada)
- 2026-11-07 00:30:38 | qa | verdict PASSED (qaAttempt 1/2): 6 de 6 ACs com cenario executado, nenhum achado BLOCKER ou MAJOR (F1/F2/F3 INFO); nao cobertos: run no GitHub, gate em Linux, pwsh literal, actionlint e ls-remote - todos documentados em QA-0008 Not covered
- 2026-11-07 00:36:11 | odev | verdicto QA-0008 adjudicado: PASSED com 6/6 ACs provadas por execucao (13 checks x2 plataformas, 5 mutacoes em copias fora do repo cada uma vermelha com FAIL esperado, SHA256 do ci.yml real identico antes/depois, gate 97/97, rev-list 0 0 = nenhum push); 0 BLOCKER; transicao AWAITING_QA -> DONE
- 2026-11-07 00:36:11 | odev | F1/INFO adjudicado: as escritas de 23:19:58 (plan.md) e 23:57:32 (TASK-0009) na janela do AC-05 sao fluxo ODEV (nextAction + append-log), nao delta do DEV - classificacao correta do QA, allowlist nos checkers aceita
- 2026-11-07 00:36:11 | odev | F2/INFO adjudicado: o gemeo .sh mede exit 3 na QA (duas vezes) e o DEV registrou 1 - ambos nao-verde, a conclusao do GAP-17 (escolha do .ps1) nao muda; divergencia de exit code entre rodadas de um script que falha nao altera o veredito
- 2026-11-07 00:36:11 | odev | F3/INFO aceito: 2 bugs do proprio harness da QA (aspas do PS5.1 no py -c, quoting do bash aninhado) corrigidos e re-executados verdes em sessao
- 2026-11-07 00:36:11 | odev | pendencia humana registrada: push para github.com/rykehg/squid-execution + primeiro run do workflow (AC-06) - gate humano posterior, nunca feito pelo fluxo
- 2026-11-07 15:32:36 | odev | PENDENCIA HUMANA RESOLVIDA: humano deu push e o primeiro run do GitHub Actions foi VERDE - logs recebidos em C:\dev\squid-squad-net\logs_114873942112.zip (actions do github.com/rykehg/squid-execution, runner ubuntu-26.04, job build-and-test)
- 2026-11-07 15:32:36 | odev | run: build exit 0 com 0 Warning 0 Error; gate de testes exit 0 com Passed 97... na verdade 98/98 em 17 s (docs/sdd/runs/test-runs/20261107-182610-test-gate.log no runner); cache NuGet salvo com a chave Linux-nuget; nenhum ##[error] em nenhum step - so deprecation de Node.js 20 nas actions oficiais (upstream, nao nosso)
- 2026-11-07 15:32:36 | odev | LACUNA DE LINUX FECHADA NO CI: a suite inteira (98/98) rodou em Linux real pela primeira vez - o unverified recorrente das QAs (suite so no Windows) agora tem execucao no ubuntu do GitHub; o twin sdd-test.sh (S-11) segue nao executado no run (o workflow usa o .ps1 via pwsh)

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-06 23:53:08 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 0 | baseline pre-edicao: backend/Squid.slnx 0 avisos 0 erros |
| 2026-11-06 23:53:34 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261106-233126-test-gate.log - Aprovado 97 Com falha 0 Total 97 - baseline pre-edicao |
| 2026-11-06 23:53:34 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build (2a execucao, apos ci.yml) | 0 | backend/Squid.slnx 0 avisos 0 erros - AC-04 |
| 2026-11-06 23:53:34 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (2a execucao, apos ci.yml) | 0 | docs/sdd/runs/test-runs/20261106-234007-test-gate.log - Aprovado 97 Com falha 0 Total 97 - AC-04 e AC-05 |
| 2026-11-06 23:53:35 | dev | py -V:Astral\CPython3.13.18 %TEMP%\sdd-ci0009-check.py | 0 | .github/workflows/ci.yml - PyYAML 6.0.3 parse sem erro, push develop/main + pull_request, permissions contents read, SDK 10.0.403 pinado, cache hashFiles Directory.Packages.props, 0 continue-on-error, 0 hits de padrao de segredo - AC-01 AC-02 AC-03 |
| 2026-11-06 23:53:35 | dev | bash docs/sdd/_tools/sdd-test.sh build (Git Bash) | 1 | GAP-17: stderr no *.sln found within depth 3 e MSB1003 - repo so tem backend/Squid.slnx, o gemeo nao roda; escolha do .ps1 registrada no log |
| 2026-11-06 23:53:35 | dev | git grep -n -e Start-Process -e Get-WmiObject -e Microsoft.Win32 -e HKLM -e wmic -- docs/sdd/_tools/sdd-test.ps1 | 1 | nenhuma ocorrencia - sdd-test.ps1 sem API Windows-only, compativel com pwsh no Linux - GAP-17 |
| 2026-11-06 23:53:35 | dev | cmd /c where pwsh | 1 | pwsh ausente nesta maquina - gate executado com powershell 5.1 conforme RUNBOOK; o runner ubuntu do GitHub traz pwsh (SPEC-0005) |
| 2026-11-06 23:53:35 | dev | git status --porcelain -- .github | 0 | ?? .github/ - unico delta desta tarefa; mods pre-existentes de TASK-0007/0008 nao tocados - AC-05 |
| 2026-11-07 00:31:32 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0009.ps1 | 0 | AC-01 AC-02 AC-03 AC-05 AC-06 static: 13 checks 0 FAIL - baseline Windows |
| 2026-11-07 00:31:32 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0009.ps1 -CiPath %TEMP%\qa0009-mut\M1.yml | 1 | mutation M1 tab-in-name-line detected FAIL AC01-parse |
| 2026-11-07 00:31:33 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0009.ps1 -CiPath %TEMP%\qa0009-mut\M2.yml | 1 | mutation M2 pin-to-latest detected FAIL AC03-pin |
| 2026-11-07 00:31:33 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0009.ps1 -CiPath %TEMP%\qa0009-mut\M3.yml | 1 | mutation M3 build-became-dotnet-build detected FAIL AC02-commands |
| 2026-11-07 00:31:34 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0009.ps1 -CiPath %TEMP%\qa0009-mut\M4.yml | 1 | mutation M4 continue-on-error-added detected FAIL AC02-no-coe |
| 2026-11-07 00:31:34 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0009.ps1 -CiPath %TEMP%\qa0009-mut\M5.yml | 1 | mutation M5 permissions-write detected FAIL AC01-perms |
| 2026-11-07 00:31:53 | qa | Get-FileHash SHA256 .github/workflows/ci.yml before and after mutations | 0 | ECF7043386C823239C339DB938FC6B20BD34E5994424B9EF33B82602437A0FE8 identical - real ci.yml not mutated |
| 2026-11-07 00:31:54 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0009.ps1 (re-run after mutations) | 0 | baseline re-run green - no residue |
| 2026-11-07 00:31:54 | qa | py -V:Astral\CPython3.13.18 -c yaml.safe_load (first attempt) | 1 | PS5.1 stripped inner quotes - SyntaxError; QA harness bug F3, corrected next row |
| 2026-11-07 00:31:55 | qa | py -V:Astral\CPython3.13.18 -c yaml.safe_load .github/workflows/ci.yml | 0 | AC-04 - PyYAML 6.0.3 validation exit 0 - printed 6.0.3 dict |
| 2026-11-07 00:31:55 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 0 | AC-04 - backend/Squid.slnx 0 avisos 0 erros - build exit 0 |
| 2026-11-07 00:31:56 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | AC-04 AC-05 - Aprovado 97 Com falha 0 Total 97 - docs/sdd/runs/test-runs/20261107-001518-test-gate.log |
| 2026-11-07 00:31:57 | qa | wsl -u root -e bash docs/sdd/qa/qa-check-task-0009.sh | 0 | Linux twin 13 checks 0 FAIL; AC-04 UNVERIFIED on Linux pwsh/dotnet absent - never assumed green |
| 2026-11-07 00:31:57 | qa | Select-String -Path .github/workflows/ci.yml -Pattern continue-on-error and secret patterns | 0 | HITS=0 - AC-02 independent grep; run lines pwsh sdd-test.ps1 build and test |
| 2026-11-07 00:32:17 | qa | bash docs/sdd/_tools/sdd-test.sh build (direto, 2x Git Bash) | 3 | GAP-17 re-proof: no *.sln found, .sh never green-builds this repo - exit 3 twice; DEV row says 1 - F2 |
| 2026-11-07 00:32:17 | qa | bash -c nested sdd-test.sh build com echo rc | 0 | measurement artifact of PS5.1 native quoting - superseded by direct runs exit 3 (F3) |
| 2026-11-07 00:32:18 | qa | git rev-list --left-right --count HEAD...origin/develop | 0 | AC-06 - 0 0; HEAD=origin/develop=9e867d9; 0 commits since task creation - no push |
| 2026-11-07 00:32:18 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0009.ps1 (final apos QA-0008) | 0 | AC-05 end state: 13 checks 0 FAIL - QA-0008 listed as QA flow artifact |
| 2026-11-07 00:32:48 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-state.ps1 check TASK-0009 | 0 | structure intact after QA appends - check OK log 21 evidence 27 |
| 2026-11-07 15:32:36 | odev | run do GitHub Actions job build-and-test (ubuntu-26.04) - step Build: pwsh sdd-test.ps1 build | 0 | C:\dev\squid-squad-net\logs_114873942112.zip (build-and-test\5_Build.txt - 0 Warning 0 Error, exit 0) |
| 2026-11-07 15:32:37 | odev | run do GitHub Actions job build-and-test (ubuntu-26.04) - step Gate de testes: pwsh sdd-test.ps1 test | 0 | C:\dev\squid-squad-net\logs_114873942112.zip (build-and-test\6_Gate de testes.txt - Passed 98 Failed 0 Total 98, exit 0; log do runner 20261107-182610-test-gate.log) |
