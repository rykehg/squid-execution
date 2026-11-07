<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0010 — Cinco runbooks do §8 + índice com status e comandos provados

```yaml
runId: R-20261107-01
taskId: TASK-0010
status: DONE
agent: odev
phase: P-01
sequence: 2
createdAt: 2026-11-07 00:36:08
updatedAt: 2026-11-07 02:10:55
parent: SPEC-0006
authority: SPEC
parallel: true
touches: ["docs/runbooks/**"]
blocks: []
blockedBy: []
inputArtifacts: ["docs/features/05-arquitetura-consolidada.md", "docs/sdd/specs/SPEC-0006-runbooks-iniciais.md"]
outputArtifacts: ["docs/runbooks/README.md", "docs/runbooks/disco-cheio.md", "docs/runbooks/restaurar-backup.md", "docs/runbooks/rotacionar-chave.md", "docs/runbooks/peer-entrega-travada.md", "docs/runbooks/dlq-cheia.md"]
interviewRounds: 0/6
feasibilityRounds: 0/2
correctionAttempts: 0/2
qaAttempts: 0/2
contextHandoffs: 0/4
progressDelta: 0
humanGate:
  approvedBy: Henrique
  approvedDate: 2026-11-06 09:27:51
  scope: P-01 = Sprint 0 inteiro do doc 05 - inclui Runbooks iniciais (§8); nenhum pacote novo previsto (documentacao pura)
  expiresAt: NENHUM
blocker: NENHUM
nextAction: DONE - QA-0009 PASSED (5/5 ACs, qaAttempt 1/2, 61/61 comandos executados, break-it 3x vermelho, gate 97/97); ULTIMA ENTREGA DA P-01 - todas as 10 tarefas DONE
evidence: []
memoryBankFlags: [PADRAO_NOVO: runbook de operacao com status EXECUTAVEL/PARCIAL/PENDENTE-fase e marca de comando EXECUTAVEL ou NAO-executar - source: docs/runbooks/README.md]
```

## Intent

Última entrega da P-01: os runbooks mínimos do §8. A SPEC-0006 fixa a separação —
`disco-cheio.md` executável agora, `restaurar-backup.md` parcial (ciclo arquivo-volume real,
geração P-07), os outros três esboços com status PENDENTE da fase (P-02/P-06) sem passos
falsos. Runbook que mente sobre um comando que não roda é pior que ausente.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `docs/runbooks/README.md` indexa os 5 runbooks do §8 com status explícito
  (EXECUTÁVEL / PARCIAL / PENDENTE-fase) e aponta o gate local de verificação.
- AC-02: os 6 arquivos existem (índice + 5), cada um com as seções sintoma, diagnóstico,
  procedimento e verificação — runbooks PENDENTE não contêm passo a passo (só escopo +
  ponteiro da fase), verificável por leitura estrutural.
- AC-03: todo comando marcado EXECUTÁVEL em `disco-cheio.md` e no trecho executável de
  `restaurar-backup.md` foi **executado** na prova e saiu exit 0 (ou comportamento esperado
  documentado para comandos destrutivos, que nunca são executados de verdade — nesse caso o
  runbook os marca como NÃO-executar e a prova é que eles não aparecem como executáveis).
- AC-04: `compose down -v` só aparece como destruição explícita com aviso — grep no
  `docs/runbooks/` não acha `down -v` fora de aviso/contexto destrutivo; nenhum segredo,
  token ou caminho absoluto de máquina pessoal nos arquivos.
- AC-05: gate completo `sdd-test.ps1 test` sai exit 0 (nada quebra) e os arquivos novos estão
  só em `docs/runbooks/**` (fora do resto do delta já existente no working tree).

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `docs/runbooks/**`

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-18 | no | runbooks de sistemas futuros são esboços PENDENTE com ponteiro de fase — o Sprint 0 pede os arquivos, não procedimentos de sistemas não construídos |
| GAP-15, GAP-09 | no | herdados: comandos usam o stack entregue (compose dev, endpoints da SPEC-0002) |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`

- 2026-11-07 00:36:08 | odev | task created from SPEC-0006; runId R-20261107-01 (nova sessao de delivery); ultima entrega da P-01 antes da consolidacao
- 2026-11-07 00:39:59 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; sem blockedBy; parallel true (docs/runbooks/ nao existe); runId R-20261107-01
- 2026-11-07 01:19:05 | dev | feasibility 1/2: task e SPEC-0006 lidas; touches docs/runbooks/** confirmado (diretorio inexistente; nenhum outro aberto o toca); baseline gate sdd-test.ps1 test = 97/97 exit 0; nenhuma duvida bloqueante (mecanismo arquivo-volume provado antes em volume descartavel)
- 2026-11-07 01:19:05 | dev | escreveu os 6 arquivos de docs/runbooks/: README com status por runbook e gate, disco-cheio EXECUTAVEL, restaurar-backup PARCIAL (geracao P-07), rotacionar-chave / peer-entrega-travada / dlq-cheia PENDENTE-fase sem passo a passo (GAP-18)
- 2026-11-07 01:19:06 | dev | prova AC-03: todos os comandos marcados EXECUTAVEL rodaram exit 0 (podman system df, df -h via wsl, volume ls, curls Windows e WSL de /health e /ready, compose up/down com -f infra/compose.yaml, create/cp/rm do ciclo arquivo-volume); destrutivos marcados NAO-executar e nunca executados
- 2026-11-07 01:19:06 | dev | checagens AC-02/AC-04: 6 arquivos com sintoma/diagnostico/procedimento/verificacao; down -v so em aviso de destruicao; 0 ocorrencias de token e 0 de caminho absoluto C:; runbooks PENDENTE sem passo a passo
- 2026-11-07 01:22:35 | dev | gate final sdd-test.ps1 test = exit 0 (97/97); stack encerrado com down simples - portas 8080 e 16686 livres, volume infra_squid-data mantido; staging da prova removido; delta novo apenas em docs/runbooks/**
- 2026-11-07 01:27:58 | odev | ODEV verificou antes de transicionar: check exit 0 (7 logs, 24 evidencias); 6 arquivos em docs/runbooks/ com tamanhos coerentes; gate final 97/97 exit 0 lido de docs/sdd/runs/test-runs/20261107-012006-test-gate.log; stack down (nenhum container) com volume infra_squid-data mantido; delta git so ?? docs/runbooks/
- 2026-11-07 01:27:58 | odev | notas do DEV aceitas para a QA verificar: (1) compose invocado como podman compose -f infra/compose.yaml a partir da raiz (equivalente a cd infra/, mesmo projeto e volume - DEV diz ter verificado por inspect do mount); (2) 503 de /ready nao induzido (para nao mexer no volume) - exit 22 citado e comportamento documentado do curl -f, nao observacao; (3) hedge do README (4 secoes como legenda) - QA decide se atende AC-02 na leitura literal
- 2026-11-07 01:27:58 | odev | status IN_DEVELOPMENT -> AWAITING_QA
- 2026-11-07 02:05:18 | qa | qa entry: task AWAITING_QA, qaAttempts 0/2 (this is attempt 1/2); AC-01..AC-05 taken from TASK-0010 only; SPEC-0006 GAP-18 respected - PENDENTE runbooks without steps are required behavior, not a gap; memory bank route qa-integracao read (build-run-test.md)
- 2026-11-07 02:05:18 | qa | verificadores escritos e executados: docs/sdd/qa/qa-check-task-0010.ps1 e qa-check-task-0010.sh (twin Linux/WSL); estatico Windows 24/24 exit 0 e twin WSL 24/24 exit 0 sobre os 6 runbooks reais
- 2026-11-07 02:05:18 | qa | break-it em copia fora do repo (A comando falso EXECUTAVEL, B status invalido no README, C secao Verificacao removida): exit 1/1/1, controle exit 0, twin sh exit 1; SHA256 dos 6 runbooks reais identicos antes e depois - nunca mutados
- 2026-11-07 02:05:18 | qa | AC-03 executado de verdade com -Execute: exit 0, 61/61 checks, 13/13 strings EXECUTAVEIS exit 0 (podman system df, df -h e curls verbatim na WSL, wsl df -h, compose up/down com -f da raiz, ciclo restore create/cp/rm, roundtrip SHA256 identico); destrutivos nunca executados
- 2026-11-07 02:05:18 | qa | estado final da stack: down simples em todos os pontos (nunca -v), 0 containers, volume infra_squid-data mantido, portas 8080 e 16686 livres, nenhum artefato esquecido no repo
- 2026-11-07 02:05:18 | qa | gate sdd-test.ps1 test exit 0 (97/97) em docs/sdd/runs/test-runs/20261107-020119-test-gate.log; AC-04 own grep: down -v so em aviso destrutivo, 0 segredos, 0 caminhos absolutos; AC-05 delta de produto so docs/runbooks/**
- 2026-11-07 02:05:18 | qa | veredito PASSED, sem findings; QA-0009 escrita; hedge do README julgado como atende AC-02 literal (as 4 secoes existem como H2 reais em README.md:28/32/38/43); unverified declarado: ciclo podman em Linux nativo (engine WSL e vazio e diferente), gate em Linux, caso 503 de /ready nao induzido
- 2026-11-07 02:10:58 | odev | verdicto QA-0009 adjudicado: PASSED com 5/5 ACs provadas por execucao (verificadores x2 plataformas 24/24, ciclo AC-03 executado 61/61 com roundtrip SHA256 identico, break-it 3 mutacoes vermelhas + controle verde, runbooks reais nunca mutados, gate 97/97); sem findings; transicao AWAITING_QA -> DONE
- 2026-11-07 02:10:58 | odev | julgamento do QA sobre o hedge do README aceito: as 4 secoes existem como H2 reais nos 6 arquivos (README.md:28/32/38/43) - AC-02 satisfeita na leitura literal, registrado como julgamento explicito no QA-0009, nao silencioso
- 2026-11-07 02:10:58 | odev | unverified aceitos: ciclo podman em Linux nativo (engine WSL e outro engine, vazio) e caso 503 de /ready nao induzido (comportamento documentado do curl -f)
- 2026-11-07 02:10:58 | odev | TASK-0010 e a ULTIMA entrega da P-01: 10/10 tarefas DONE com QA PASSED - fase apta para consolidacao (memory bank + snapshot do plano + authority CODE)

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-07 01:22:07 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261107-004329-test-gate.log baseline 97/97 |
| 2026-11-07 01:22:07 | dev | podman system df | 0 | docs/runbooks/disco-cheio.md |
| 2026-11-07 01:22:08 | dev | wsl df -h | 0 | docs/runbooks/disco-cheio.md |
| 2026-11-07 01:22:08 | dev | podman volume ls | 0 | docs/runbooks/disco-cheio.md |
| 2026-11-07 01:22:09 | dev | curl.exe -fsS http://localhost:8080/health | 0 | docs/runbooks/disco-cheio.md |
| 2026-11-07 01:22:09 | dev | wsl curl -fsS http://localhost:8080/health | 0 | docs/runbooks/disco-cheio.md |
| 2026-11-07 01:22:10 | dev | curl.exe -fsS http://localhost:8080/ready | 0 | docs/runbooks/restaurar-backup.md |
| 2026-11-07 01:22:10 | dev | wsl curl -fsS http://localhost:8080/ready | 0 | docs/runbooks/restaurar-backup.md |
| 2026-11-07 01:22:11 | dev | podman compose -f infra/compose.yaml down | 0 | docs/runbooks/restaurar-backup.md |
| 2026-11-07 01:22:12 | dev | podman create --name squid-staging -v infra_squid-data:/data squid-service:dev | 0 | staging da prova - saida do volume fora dos runbooks |
| 2026-11-07 01:22:12 | dev | podman cp squid-staging:/data/squid-grainstate.db ./squid-grainstate.db | 0 | staging da prova - saida do volume fora dos runbooks |
| 2026-11-07 01:22:13 | dev | podman rm squid-staging | 0 | staging da prova - fora dos runbooks |
| 2026-11-07 01:22:14 | dev | podman create --name squid-restore -v infra_squid-data:/data squid-service:dev | 0 | docs/runbooks/restaurar-backup.md |
| 2026-11-07 01:22:14 | dev | podman cp ./squid-grainstate.db squid-restore:/data/squid-grainstate.db | 0 | docs/runbooks/restaurar-backup.md |
| 2026-11-07 01:22:15 | dev | podman rm squid-restore | 0 | docs/runbooks/restaurar-backup.md |
| 2026-11-07 01:22:15 | dev | podman compose -f infra/compose.yaml up -d | 0 | docs/runbooks/disco-cheio.md |
| 2026-11-07 01:22:16 | dev | podman compose -f infra/compose.yaml up -d | 0 | docs/runbooks/restaurar-backup.md apos a restauracao |
| 2026-11-07 01:22:16 | dev | podman exec infra_squid_1 ls -la /data | 0 | prova restauracao - arquivo de volta no volume |
| 2026-11-07 01:22:17 | dev | findstr /n /i token docs\runbooks\*.md | 1 | AC-04 nenhum token - exit 1 significa sem ocorrencia |
| 2026-11-07 01:22:17 | dev | findstr /n C: docs\runbooks\*.md | 1 | AC-04 nenhum caminho absoluto - exit 1 significa sem ocorrencia |
| 2026-11-07 01:22:18 | dev | findstr /n Procedimento docs\runbooks\*.md | 0 | AC-02 secao Procedimento presente nos 6 arquivos |
| 2026-11-07 01:22:18 | dev | findstr /n down docs\runbooks\*.md | 0 | AC-04 ocorrencias de down todas em contexto destrutivo |
| 2026-11-07 01:22:19 | dev | podman compose -f infra/compose.yaml down | 0 | encerramento - stack parado e portas 8080/16686 livres |
| 2026-11-07 01:22:20 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261107-012006-test-gate.log gate final 97/97 |
| 2026-11-07 02:05:37 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0010.ps1 | 0 | docs/sdd/qa/QA-0009-runbooks-iniciais.md - estatico Windows final 24/24 (AC-01 AC-02 AC-04 AC-05 e classificacao AC-03) |
| 2026-11-07 02:05:37 | qa | wsl -e bash docs/sdd/qa/qa-check-task-0010.sh | 0 | docs/sdd/qa/QA-0009-runbooks-iniciais.md - twin Linux/WSL 24/24 |
| 2026-11-07 02:05:38 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0010.ps1 -RunbooksPath C:\Users\Ryke\AppData\Local\Temp\qa0010-breakit | 1 | docs/sdd/qa/QA-0009-runbooks-iniciais.md - break-it mutacao A: comando falso EXECUTAVEL detectado (AC03-classify disco-cheio.md:50) |
| 2026-11-07 02:05:38 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0010.ps1 -RunbooksPath C:\Users\Ryke\AppData\Local\Temp\qa0010-breakit | 1 | docs/sdd/qa/QA-0009-runbooks-iniciais.md - break-it mutacao B: status invalido no README detectado (AC01-status-disco-cheio) |
| 2026-11-07 02:05:39 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0010.ps1 -RunbooksPath C:\Users\Ryke\AppData\Local\Temp\qa0010-breakit | 1 | docs/sdd/qa/QA-0009-runbooks-iniciais.md - break-it mutacao C: secao Verificacao removida detectada (AC02-sections-dlq-cheia) |
| 2026-11-07 02:05:39 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0010.ps1 -RunbooksPath C:\Users\Ryke\AppData\Local\Temp\qa0010-breakit | 0 | docs/sdd/qa/QA-0009-runbooks-iniciais.md - controle: copia integra do break-it passa |
| 2026-11-07 02:05:40 | qa | wsl -e bash docs/sdd/qa/qa-check-task-0010.sh /mnt/c/Users/Ryke/AppData/Local/Temp/qa0010-breakit | 1 | docs/sdd/qa/QA-0009-runbooks-iniciais.md - break-it mutacao A no twin Linux/WSL |
| 2026-11-07 02:05:41 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261107-020119-test-gate.log - gate final 97/97 |
| 2026-11-07 02:05:42 | qa | Get-FileHash -Path docs\runbooks\*.md -Algorithm SHA256 | 0 | docs/sdd/qa/QA-0009-runbooks-iniciais.md - 6 hashes identicos antes e depois do break-it (runbooks reais nunca mutados) |
| 2026-11-07 02:07:28 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0010.ps1 -Execute | 0 | docs/sdd/qa/QA-0009-runbooks-iniciais.md - AC-03 executado: 61/61 checks, 13/13 EXECUTAVEIS exit 0, roundtrip SHA256 identico, stack down com volume mantido |
