<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0060 — Tela de redundância: redundancy.score + página /admin/backup

```yaml
runId: R-20261120-01
taskId: TASK-0060
status: DONE
agent: odev
phase: P-07
sequence: 2
createdAt: 2026-11-20 21:50:58
updatedAt: 2026-11-20 21:50:58
parent: SPEC-0012
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Api/Admin/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/Backup/**"]
blocks: []
blockedBy: ["TASK-0057"]
inputArtifacts: ["docs/sdd/specs/SPEC-0012-backup-rotação-p07.md", "specs/001-squid-mvp/contracts/backup-recovery.md"]
outputArtifacts: ["backend/tests/Squid.Tests/Backup/**", "docs/sdd/qa/QA-*"]
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
nextAction: DONE - 7/7 tasks novas da P-07 fechadas; resta TASK-0049 (gate humano: pares Docker com o humano presente)
evidence: []
memoryBankFlags: []
```

## Intent

SPEC-0012 §G — decisão humana 2026-11-20 (opção A): `GET /api/admin/backup/status` ganha
`redundancy.score` (targets com o envelope mais recente verificado ÷ targets configurados, no
formato `"Redundância: 1/3 — sem recuperação de desastre"` do doc 05 §8) e `GET /admin/backup`
serve uma **página HTML fina** exibindo verifiedAt, envelopes, bytes, verify_failures,
`redundancy.score` e `restore.last_drill_*` — sem build de front, sem pacote novo; a P-08
substitui só a vista, o endpoint JSON permanece.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `GET /api/admin/backup/status` devolve `redundancy.score` = nº de targets com o envelope
  mais recente **verificado** ÷ nº de targets configurados, formatado como
  `"Redundância: 1/2 — sem recuperação de desastre"` quando nenhum off-site está verificado e
  sem o sufixo quando todos estão — teste.
- AC-02: `GET /admin/backup` devolve uma página HTML que exibe: último `verifiedAt`,
  `envelopes_count`, `bytes_total`, `verify_failures`, `redundancy.score` e
  `restore.last_drill_at`/`restore.last_drill_result` — teste (página contém os valores do
  estado atual).
- AC-03: as duas rotas exigem operador: sem sessão `401`, sessão fora de `Instance:Operators`
  `403` (mesma regra das rotas admin desde a P-03) — teste.
- AC-04: a página é servida pelo próprio backend (HTML estático embutido/inline, **nenhum
  pacote NuGet novo**, nenhum artefato de build de front) — teste.
- AC-05: o score muda observavelmente quando um target off-site passa a ter o envelope mais
  recente verificado (1/2 verificado → 2/2, sufixo some) — teste.
- AC-06: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1
  test` exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Api/Admin/**` — `redundancy.score` no status + rota HTML `/admin/backup`
- `backend/src/Squid.Service/**` — wiring
- `backend/tests/Squid.Tests/Backup/**` — testes dos ACs
- S3/alvo off-site é TASK-0057 (daí o blockedBy); rotação é TASK-0058
- P-08 substituirá a vista; o endpoint JSON é contrato permanente
- produto fora dos `touches`: qualquer lacuna é `D-01` para o ODEV

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| NENHUM | — | opção A confirmada pelo humano (entrevista P-07, turno entre as rodadas) |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-20 21:50:58 | odev | task created from SPEC-0012 (opcao A: redundancy.score + pagina HTML fina /admin/backup; blockedBy TASK-0057)
- 2026-11-22 12:49:39 | odev | dispatch DEV (runId R-20261120-01, sequencia 1 -> 2, branch task/TASK-0060-tela-redundancia desde develop; encadeamento automatico - TASK-0059 DONE com QA-0059 PASSED 5/5, S-42 fechado; ULTIMA task nova antes da gate-humana TASK-0049)
- 2026-11-22 13:58:56 | dev | feasibility round 1/2: baseline gate 647/647 exit 0 em develop; touches confirmados - port IBackupRedundancySource em Squid.Api/Admin (precedente IBackupKeySource) e SQL em Squid.Service (precedente SqliteKeyEpochStore), porque tocar Squid.Grains ou Squid.Worker violaria AC-06; D-01 resolvido por SPEC-0012 G - o sufixo depende do alvo off-site segurar o envelope mais recente verificado, tambem quando so existe o local (1/1 leva o sufixo); branch task/TASK-0060-tela-redundancia criada desde develop
- 2026-11-22 13:59:11 | dev | implementado: redundancy.score no GET /api/admin/backup/status + rota GET /admin/backup servindo a pagina HTML inline (BackupPage, mesmos campos da secao 6) + SqliteBackupRedundancySource sobre o registry (numerador = alvos configurados com linha verificada na janela do envelope verificado mais recente, regra de honestidade S-47 - nunca o cabecalho do destino) + warning de boot S-52 quando Backup:S3:* existe mas nao parseia; 5 testes novos em RedundancyScoreTests, 5/5 verdes
- 2026-11-22 14:07:52 | dev | gate final: sdd-test.ps1 test exit 0 com 652/652 (647 baseline + 5 novos, 11m08s); AC-06 fechado - nenhum arquivo alterado fora dos touches, nenhum .csproj tocado e zero pacote NuGet novo; AC-01 a AC-05 todos implementados e verificados por teste executado
- 2026-11-22 14:10:32 | dev | correcao interna na verificacao: o fato AC-05 falhou so na limpeza (argumentos de Cleanup invertidos - volume x storage), as assercoes de score 1/2 -> 2/2 ja tinham passado; ordem corrigida e re-execucao do filtro com 5/5 exit 0 - nenhuma correcao veio de QA (correctionAttempts segue 0/2)
- 2026-11-22 14:13:01 | odev | ODEV: verificacao do step result DONE - logLines/evidenceRows gravados pelo DEV via sdd-state (check exit 0); gate 652/652 exit 0 (647 + 5 novos), RedundancyScoreTests 5/5, 7 arquivos nos touches, zero pacote novo, commit ba9d32a na branch; unica vermelha no caminho foi Cleanup invertido no proprio fato do DEV (corrigido, correctionAttempts segue 0/2)
- 2026-11-22 14:13:01 | odev | ODEV: D-01 (sufixo do score com so o local configurado) resolvida na especificacao - SPEC-0012 G: 1/1 local-only leva o sufixo, so off-site verificado tira; S-52 (warning de boot) implementado nesta task junto - fecha o S-52
- 2026-11-22 14:13:01 | odev | ODEV: nota - memoryBankFlags do DEV diz "[GAP: memory bank absent]" mas docs/memory-bank/index.md EXISTE (consolidacao da P-06); flag do DEV provavelmente por rota errada - descartada com o motivo, nada a consolidar aqui
- 2026-11-22 14:13:01 | odev | ODEV: transicao IN_DEVELOPMENT -> AWAITING_QA, dispatch QA-0060 (ultima task nova)
- 2026-11-22 15:46:23 | qa | QA-0060 TASK-0060: veredito PASSED 6/6 ACs - gate abertura 652/652 e fechamento 653/653 exit 0; familia RedundancyScore 6/6 e filtro do dispatch RedundancyScoreTests 5/5 no Windows; 12 mutacoes can-fail EXPECT-OK (M1-M10 + S1/S2) restauradas byte-exact + mtime bump S-23, ledger fechou green 6/6; escopo fails=0 (7 arquivos nos touches, zero pacote NuGet, blobs 6/6); Linux podman sdk 10.0: build 0, familia 6/6 exit 0 e 51 vermelhos identicos a baseline nome-a-nome (only-now=0) = pre-existente de ambiente Skia/cmd.exe; S-52 fechado com presence e silence-in-default (M8/M9); D-01 confirmada contra SPEC-0012 G (1/1 local-only leva o sufixo); cenario novo RedundancyScoreQaTests cobre o valor de restore.last_drill_at (M10); zero achado de produto
- 2026-11-22 15:52:36 | odev | QA-0060 PASSED 6/6 (qaAttempt 1/2): AC-01..AC-06 todos PASS; 12 mutacoes can-fail EXPECT-OK (M1-M10 + S1/S2) restauradas byte-exact + mtime bump, ledger green 6/6; gates 652/652 abertura e 653/653 fechamento exit 0; familia RedundancyScore 6/6 no Windows E no Linux; escopo 7/7 nos touches, zero pacote novo (S1/S2 provam que o scanner de escopo sai exit 1 na violacao); commit QA 395b4cf na branch (1 commit, sem push); ZERO achado de produto
- 2026-11-22 15:52:36 | odev | QA-0060: D-01 (sufox do 1/1 local-only) confirmada contra SPEC-0012 G - leitura do DEV e da QA concordam, testada nos dois niveis; S-52 FECHADO (presenca M8 + silencio-no-default M9); cenario novo da QA (RedundancyScoreQaTests) cobre o valor de restore.last_drill_at na pagina (M10); S-22 re-observado no M1 (classificado pelo log)
- 2026-11-22 15:52:36 | odev | ODEV: transicao AWAITING_QA -> DONE; merge em develop + commit docs conforme gate modelo C
- 2026-11-22 15:52:36 | odev | ODEV: FIM DAS TASKS AUTOMATICAS - P-07 fica 7/7 novas DONE com QA PASSED na 1a tentativa; falta so a TASK-0049 (interop real) = GATE HUMANO com o humano presente (pares Docker Mastodon/Friendica)

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-22 13:59:32 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261122-132737-test-gate.log (baseline pre-mudanca 647/647) |
| 2026-11-22 13:59:33 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 0 | backend/Squid.slnx (exit 0, nenhum warning novo nos arquivos da task) |
| 2026-11-22 13:59:33 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~RedundancyScoreTests | 0 | docs/sdd/runs/test-runs/20261122-135540-test-filter.log (5/5 novos) |
| 2026-11-22 13:59:34 | dev | git status --porcelain | 0 | 6 fontes novas/modificadas, todas nos touches, nenhum .csproj ou pacote novo |
| 2026-11-22 14:07:52 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261122-140724-test-gate.log (gate final 652/652) |
| 2026-11-22 14:07:53 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-state.ps1 check docs/sdd/tasks/TASK-0060-tela-redundancia.md | 0 | docs/sdd/tasks/TASK-0060-tela-redundancia.md |
| 2026-11-22 14:10:33 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~RedundancyScoreTests | 1 | docs/sdd/runs/test-runs/20261122-135312-test-filter.log (4/5 - so a limpeza do fato AC-05 com Cleanup invertido, corrigido na sequencia) |
| 2026-11-22 15:46:24 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261122-143930-test-gate.log (abertura 652/652) |
| 2026-11-22 15:46:24 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~RedundancyScore | 0 | docs/sdd/runs/test-runs/20261122-145642-test-filter.log (familia 6/6 = 5 DEV + 1 QA) |
| 2026-11-22 15:46:25 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~RedundancyScoreTests | 0 | docs/sdd/runs/test-runs/20261122-145832-test-filter.log (filtro do dispatch 5/5) |
| 2026-11-22 15:46:25 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0060.ps1 -VerifyOnly | 0 | docs/sdd/qa/QA-0060-verify.log (12/12 achados, ASCII 0) |
| 2026-11-22 15:46:26 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0060.ps1 | 0 | docs/sdd/qa/QA-0060-mutations-run1.log (11 ciclos failed=0) |
| 2026-11-22 15:46:26 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0060.ps1 | 0 | docs/sdd/qa/QA-0060-mutations.log (12 ciclos failed=0, green 6/6) |
| 2026-11-22 15:46:26 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-scope-task-0060.ps1 | 0 | docs/sdd/qa/QA-0060-scope.log (2 execucoes, fails=0) |
| 2026-11-22 15:46:27 | qa | podman run --rm -v C:/dev/squid-squad-net/squid-execution:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash docs/sdd/qa/qa-linux-task-0060.sh | 0 | docs/sdd/qa/QA-0060-linux-test-redundancy.log (familia 6/6; gate Linux 51 falhas = baseline, only-now=0) |
| 2026-11-22 15:46:27 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261122-154405-test-gate.log (fechamento 653/653) |
