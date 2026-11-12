<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0038 — Reação com whitelist e idempotência

```yaml
runId: R-20261111-01
taskId: TASK-0038
status: DONE
agent: odev
phase: P-05
sequence: 2
createdAt: 2026-11-11 23:45:07
updatedAt: 2026-11-11 23:45:07
parent: SPEC-0010
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Api/Threads/**", "backend/src/Squid.Service/**", "backend/src/Squid.Persistence/schema/**", "backend/tests/Squid.Tests/Threads/**"]
blocks: ["TASK-0039"]
blockedBy: ["TASK-0037"]
inputArtifacts: ["docs/sdd/specs/SPEC-0010-mensageria-p05.md", "docs/sdd/plan/plan.md", "specs/001-squid-mvp/data-model.md", "specs/001-squid-mvp/contracts/http-api.md"]
outputArtifacts: ["backend/src/Squid.Api/Threads/**", "backend/src/Squid.Service/**"]
interviewRounds: 0/6
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
nextAction: DONE - QA-0038 PASSED (5/5 ACs, 11 mutacoes, gate 468/468); merge em develop + docs; dispatch DEV da TASK-0039
evidence: []
memoryBankFlags: []
```

## Intent

FR-058 / data-model §2.14: uma linha por (mensagem, ator, emoji) — repetir é idempotente,
remover apaga, emoji fora da whitelist da instância → `422 invalid_emoji`, contagem derivada
nunca fonte, reação e contagem só aparecem para participantes (§9.10).

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `POST .../reactions` com `{emoji}` da whitelist cria a reação; repetir a mesma
  (mensagem, ator, emoji) é **idempotente** (mesma linha, contagem não muda) — teste.
- AC-02: emoji fora de `Reactions:AllowedEmojis` (config com defaults, 👍 sempre incluído) →
  `422 invalid_emoji`; mudar a config muda o conjunto aceito — teste.
- AC-03: `DELETE .../reactions?emoji=` remove a reação; remover reação inexistente → `404`;
  contagem `counts.reactions` é derivada da existência das linhas (reconciliada, não fonte) — teste.
- AC-04: não-participante recebe **404 idêntico** sem contagem em GET da mensagem e nas rotas
  de reação (§9.10) — teste.
- AC-05: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`
  exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Api/Threads/**`
- `backend/src/Squid.Service/**`
- `backend/tests/Squid.Tests/Threads/**`

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-34 | no | whitelist em config `Reactions:AllowedEmojis` com conjunto padrão; 👍 (`Like`) sempre presente |
| — | no | reação de mensagem é local (thread não federa, §2.14); Kind `Like`/`Custom` sem distinção federada nesta fase |
| GAP-37 | no | rotas de reação passam pela sessão normal; gate 428 é só de envio (TASK-0036) |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-11 23:45:07 | odev | task created from SPEC-0010 (entrega 6 de 8 da P-05; blockedBy TASK-0037; FR-058 + §2.14 + §9.10; decisão humana whitelist em config rodada 2)
- 2026-11-12 05:18:25 | odev | touches emendado pelo ODEV preventivamente: backend/src/Squid.Persistence/schema/** entra no YAML (3a ocorrencia do erro do dispatch em 0034/0036 - esta task precisara de migration: coluna/tabela nova); nenhuma AC muda
- 2026-11-12 07:07:24 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; blockedBy TASK-0037 resolve (DONE com QA-0037 PASSED, gate 462/462, merge 85cab2f em develop + docs 62093aa); touches ja emendado com schema/** (tabela de reacao); autorizacao de commit gate PHASE 2026-11-11 20:42:28 (modelo C) incluida - nova branch task/TASK-0038-reacao-whitelist desde develop
- 2026-11-12 07:23:23 | dev | feasibility r1/2: baseline gate 462/462 exit 0 na branch task/TASK-0038-reacao-whitelist (criada desde develop pelo gate PHASE); touches confere (Threads/**, Squid.Service store, schema/** para a migration 0006, tests/Threads/**) e nao intersecta outra task aberta (TASK-0039 bloqueada por esta); nenhuma duvida bloqueante
- 2026-11-12 07:23:43 | dev | decisao: reacao NAO passa pelo gate 428 - SPEC-0010 secao D e contrato §6 amarram o aceite ao ENVIO (POST .../messages) e o GAP-37 fixa 'gate 428 e so de envio (TASK-0036)'; reagir nao publica conteudo, a rota vale so sessao + CSRF
- 2026-11-12 07:23:44 | dev | decisao: ator suspenso PODE reagir - a parede de participantes basta; SPEC-0010 (Rules) diz que a regra do data-model §3.1 vale para o ENVIO de mensagem e a TASK-0036 AC-05 e de envio, reagir nao publica - nenhum 403 novo nesta rota
- 2026-11-12 07:23:46 | dev | decisao: ordem de validacao nas rotas de reacao = 401 - 404 (participante + mensagem, um unico corpo byte-identico sem traceId, cobre mensagem de outra thread) - 422 invalid_emoji (tambem no DELETE e com emoji vazio) - efeito
- 2026-11-12 07:23:48 | dev | decisao: a mensagem no fio ganha reactions [{emoji,count,mine}] (contrato §6, SPEC-0010 in/out e §9.10); counts.reactions do data-model §2.14 e a contagem derivada que esse array espelha - a migration 0006 nao grava contador algum (nunca fonte)
- 2026-11-12 07:36:59 | dev | esquema e store: migration 0006_message_reaction (chave primaria message_id+actor_id+emoji, kind like/custom, sem coluna de contagem alguma) + 5 portas novas no IThreadStore implementadas no SqliteThreadStore - sonda unica MessageVisible (thread + cadeira + mensagem da MESMA thread), add/remove com a recusa da mensagem DENTRO da transacao (padrao PADRAO_NOVO) e a projecao derivada COUNT(*) lida por delta e por mensagem
- 2026-11-12 07:37:01 | dev | rotas e fio: POST/DELETE .../messages/{messageId}/reactions entram na fila de dispatch do catch-all (parser divide no ultima /messages/, GET da rota continua no 404 do contrato), emoji validado por ReactionOptions.From(IConfiguration) por requisicao (array, forma-virgula ou defaults, com 👍 sempre incluido) apos a sonda, e a mensagem no envelope de leitura ganha reactions [{emoji,count,mine}] montado depois da sonda do participante
- 2026-11-12 07:43:18 | dev | AC-05 gate completo: 468/468 exit 0 (462 de base + 6 novas: 4 de rota de reacao, 1 de config, 1 da migration 0006) e escopo limpo - nada fora dos touches alem dos anexos do proprio task file; fim da tentativa 1, branch task/TASK-0038-reacao-whitelist pronta para o UNICO commit autorizado
- 2026-11-12 07:44:15 | dev | commit unico 8903e67 na branch task/TASK-0038-reacao-whitelist com exatamente os 9 arquivos dos touches (4 em Threads, schema/0006, store, 3 de teste); sem push, main intocada, nenhuma branch removida - o task file fica modificado so pelos anexos desta task
- 2026-11-12 07:45:29 | odev | ODEV verificou antes de transicionar: gate final 20261112-074244-test-gate.log lido no arquivo (exit: 0, Aprovado 468, Com falha 0, 5m03s); commit 8903e67 conferido via git show (9 files: 6 produto incl. ReactionOptions + migration 0006 + 3 teste, todos em touches, branch task/TASK-0038-reacao-whitelist); backend git status = 0; step result DONE aceito
- 2026-11-12 07:45:31 | odev | step result aceito com 2 decisoes do DEV registradas: (1) reacao NAO passa pelo gate 428 - SPEC-0010 D e contrato 6 amarram o aceite ao ENVIO, GAP-37 fixa gate so de envio; (2) ator suspenso PODE reagir - so a parede de participantes vale, §3.1/TASK-0036 AC-05 sao regras de envio; teste proprio nao existe (exigiria host com Instance:Operators) e nenhuma AC cobra - QA decide se cobra; ordem 401-404-422 e envelope reactions [{emoji,count,mine}] com contagem derivada COUNT(*) (nunca coluna - migration 0006 sem contador)
- 2026-11-12 07:45:32 | odev | transicao IN_DEVELOPMENT -> AWAITING_QA
- 2026-11-12 08:06:24 | qa | dispatch da QA tentativa 1/2: leitura normativa SPEC-0010 secao F + GAP-34 e GAP-37, AC-01..AC-05, FR-058, data-model 2.14, contrato http-api 6 e 9.10; baseline HEAD 8903e67 com apenas o task file sujo e suite filtrada Windows 36/36 exit 0 (20261112-075557)
- 2026-11-12 08:06:26 | qa | posicao da QA sobre as duas decisoes do DEV registradas pelo ODEV: (1) gate 428 NAO alcanca a rota de reacao - SPEC-0010 D amarra o 428 ao ENVIO e a suposicao do task file fixa gate 428 so de envio, verificado em execucao na AC-01 (par sem aceite envia 428 e reage 200); (2) ator suspenso reagindo NAO e coberto - as ACs 01..05 nao pedem, a regra 3.1 e de ENVIO (SPEC-0010 Rules), posicao registrada sem inventar AC e sem executar o cenario (exigiria host com Instance:Operators)
- 2026-11-12 08:06:28 | qa | teste temporario da QA QaTask0038ReactionRereadTests (2 Facts) executado: re-read da whitelist por requisicao mutando a FONTE da config (variavel de ambiente + Reload, nao o codigo) e escada 401 sem sessao + GET em .../reactions no 404 identico - verde 38/38 em 20261112-080317; fonte preservada em QaTask0038ReactionRereadTests.cs.txt e removida da arvore depois dos ciclos
- 2026-11-12 08:35:05 | qa | driver completo qa-mutate-task-0038.ps1: 11 ciclos vermelhos classificados NO LOG (S-22), 13x RESTORE-OK hash=PRE, GREEN 38/38 (081404), SUMMARY cycles=11 failed=0 residue=0; o mutante M4 (contador armazenado) nasceu SQL invalido DUAS vezes - coluna depois da constraint de tabela e virgula dentro do comentario -- - e as 38 vermelhas dos logs 080659/081728 denunciaram o erro; driver corrigido, SQL validado com sqlite offline (SQLITE-OK) e -Only M4 re-executado: falha=1 aprovado=37 so em ThreadMigrationTests (082449)
- 2026-11-12 08:35:07 | qa | gate AC-05 + escopo: 20261112-083213-test-gate.log com exit: 0 LIDO DENTRO DO LOG (S-22) e Com falha 0 Aprovado 468 Total 468 em 4m57s; QA-0038-scope.log = git backend 0 entradas, blob 4/4 match=True, mtime=4 desde a baseline, residuo QA-MUTATION 0, fora-dos-touches 0; teste temporario da QA removido da arvore antes do gate (fonte preservada em QaTask0038ReactionRereadTests.cs.txt)
- 2026-11-12 08:41:28 | qa | checkers da evidencia: qa-check-task-0038.ps1 exit 0 com SUMMARY total=39 fails=0 (inclui SDD-STATE check OK e CATCH-M6 lendo o log de run do M6, porque o xUnit enrolou o nome comprido do Fact A na linha [FAIL]) e o gemeo wsl qa-check-task-0038.sh exit 0 com SUMMARY total=34 fails=0
- 2026-11-12 08:42:21 | qa | commit unico de QA d3e61b3 na branch task/TASK-0038-reacao-whitelist com exatamente os 10 artefatos em docs/sdd/qa (mensagem TASK-0038 QA-0038: PASSED com o resumo); sem push, main intocada, nenhuma branch criada ou removida - o task file fica FORA do commit (so anexos), git status final = somente o task file modificado
- 2026-11-12 08:44:30 | odev | ODEV verificou antes de transicionar: QA-0038-reacao-whitelist.md com verdicto PASSED lido no arquivo; gate final 20261112-083213-test-gate.log exit: 0 / Aprovado 468 / Com falha 0 / 4m57s; residuo QA-MUTATION = 0 (git grep backend); commit QA d3e61b3 conferido (10 arquivos so docs/sdd/qa/, pai 8903e67, sem push, main intacta); backend git status = 0 entradas, blob-identity 4/4; check OK - veredito aceito
- 2026-11-12 08:44:31 | odev | QA-0038 PASSED attempt 1/2: 5/5 ACs, 11 ciclos can-fail (M1 ON CONFLICT, M2 PK estreitada, M3 Allows=true, M4 contador armazenado - mutante nasceu SQL invalido 2x, QA leu o LOG e nao o console, corrigiu com SQLITE-OK e re-executou, prova invalida descartada; M5 escada 422x404, M6 config cacheada - pego pelo teste temporario da QA, M7 DELETE sem ator, M8 parede desligada, M9 traceId 9 vermelhas, M10/M11 verbos); as 2 adjudicacoes pedidas CONFIRMADAS (428 nao alcanca reacao; suspenso-rea posicao registrada sem inventar AC); unverified registrados (suite Linux, suspenso reagindo, corrida da mesma trinca, gemeo da transacao so por leitura, GET de nao-participante)
- 2026-11-12 08:44:33 | odev | transicao AWAITING_QA -> DONE; TASK-0038 = entrega 6 de 8 da P-05 encerrada; merge em develop + docs pelo ODEV (gate modelo C); TASK-0039 desbloqueada

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-12 07:37:02 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261112-072124-test-gate.log (baseline pre-codigo, 462/462) |
| 2026-11-12 07:37:04 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Threads | 0 | docs/sdd/runs/test-runs/20261112-073616-test-filter.log (36/36 com as 6 novas) |
| 2026-11-12 07:43:19 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261112-074244-test-gate.log (gate final 468/468, exit: 0 no cabecalho) |
| 2026-11-12 07:43:21 | dev | git status --porcelain | 0 | somente touches: Threads/IThreadStore,MessageView,ThreadEndpoints,ReactionOptions + schema/0006 + Squid.Service/SqliteThreadStore + tests/Threads (3 arquivos) + docs/sdd/tasks (anexos) |
| 2026-11-12 07:44:17 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 0 | compilacao sem erro (saida no console; avisos novos de ReactionOptions corrigidos antes do gate) |
| 2026-11-12 07:44:18 | dev | git commit -m TASK-0038 (branch task/TASK-0038-reacao-whitelist) | 0 | commit 8903e67 - 9 arquivos, todos em touches |
| 2026-11-12 07:45:34 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (baseline feasibility) | 0 | docs/sdd/runs/test-runs/20261112-072124-test-gate.log (baseline 462/462) |
| 2026-11-12 07:45:36 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Threads | 0 | docs/sdd/runs/test-runs/20261112-073616-test-filter.log (36/36) |
| 2026-11-12 07:45:38 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate final AC-05) | 0 | docs/sdd/runs/test-runs/20261112-074244-test-gate.log - exit: 0 / Aprovado 468 / Com falha 0 / 5m03s - VERIFICADO PELO ODEV lendo o arquivo |
| 2026-11-12 07:45:39 | dev | git show --stat --format= HEAD (pos-commit) | 0 | 8903e67 - 9 files changed 1130 insertions 24 deletions - 6 produto + 3 teste em touches - VERIFICADO PELO ODEV via git show |
| 2026-11-12 08:06:31 | qa | git rev-parse HEAD + git rev-parse --abbrev-ref HEAD + git status --porcelain -uall + wsl dotnet probe | 0 | docs/sdd/qa/QA-0038-git-baseline.txt (HEAD 8903e67, branch task/TASK-0038-reacao-whitelist, so o task file sujo, wsl dotnet-missing) |
| 2026-11-12 08:06:33 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Threads (baseline) | 0 | docs/sdd/runs/test-runs/20261112-075557-test-filter.log (exit: 0, Com falha 0, Aprovado 36, Total 36) |
| 2026-11-12 08:06:35 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Threads (verde com o teste temporario da QA) | 0 | docs/sdd/runs/test-runs/20261112-080317-test-filter.log (exit: 0, 38/38 = 36 DEV + 2 QA) |
| 2026-11-12 08:06:39 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0038.ps1 -VerifyOnly | 0 | docs/sdd/qa/qa-mutate-task-0038.ps1 (-VerifyOnly: VERIFY total=11 bad=0, BOM OK, 14 padroes hits=1) |
| 2026-11-12 08:35:08 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0038.ps1 (execucao cheia 11 ciclos + GREEN) | 0 | docs/sdd/qa/QA-0038-mutations.log - 11 vermelhos no log, 13x RESTORE-OK, GREEN 38/38 081404, SUMMARY cycles=11 failed=0 |
| 2026-11-12 08:35:10 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0038.ps1 -Only M4 (1a correcao do mutante, SQL invalido) | 0 | docs/sdd/runs/test-runs/20261112-081728-test-filter.log - 38 vermelhas por syntax error near parentese, prova REPROVADA pela propria QA e descartada |
| 2026-11-12 08:35:12 | qa | python sqlite3 executescript do SQL mutado (sonda offline) + powershell .../qa-mutate-task-0038.ps1 -Only M4 (2a correcao) | 0 | sonda SQLITE-OK e docs/sdd/runs/test-runs/20261112-082449-test-filter.log - aprovado 37 falha 1 so em ThreadMigrationTests, GREEN 38/38 082537 |
| 2026-11-12 08:35:13 | qa | Remove-Item backend/tests/Squid.Tests/Threads/QaTask0038ReactionRereadTests.cs + git status --porcelain -uall | 0 | git status = so o task file + 6 artefatos em docs/sdd/qa; fonte preservada em docs/sdd/qa/QaTask0038ReactionRereadTests.cs.txt |
| 2026-11-12 08:35:15 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate AC-05, arvore igual a do DEV) | 0 | docs/sdd/runs/test-runs/20261112-083213-test-gate.log - exit: 0 dentro do log, Com falha 0, Aprovado 468, Total 468, 4m57s |
| 2026-11-12 08:35:16 | qa | varredura de escopo pwsh (git status, git hash-object vs rev-parse, mtime, residuo, fora-dos-touches) | 0 | docs/sdd/qa/QA-0038-scope.log - git-backend 0, blob 4/4 match=True, mtime-count=4, residue=0, fora-dos-touches=0 |
| 2026-11-12 08:41:30 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0038.ps1 | 0 | docs/sdd/qa/QA-0038-check.log - SUMMARY total=39 fails=0 |
| 2026-11-12 08:41:31 | qa | wsl -e bash docs/sdd/qa/qa-check-task-0038.sh (gemeo Linux parte estatica) | 0 | docs/sdd/qa/QA-0038-check-sh.log - SUMMARY total=34 fails=0 |
| 2026-11-12 08:42:22 | qa | git add docs/sdd/qa + git commit -m (mensagem TASK-0038 QA-0038 PASSED, guarda staged só em docs/sdd/qa) | 0 | commit d3e61b3 - 10 arquivos todos em docs/sdd/qa, 1266 insercoes; git status final = somente o task file modificado |
| 2026-11-12 08:43:00 | qa | powershell ... sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Threads (1a tentativa do teste temporario da QA, nao compilou) | 1 | erro CS1929 em QaTask0038ReactionRereadTests.cs:35 - WebApplication.Configuration e IConfiguration (sem AddInMemoryCollection); teste reescrito para mutar a FONTE via variavel de ambiente + Reload - nenhuma AC afetada, ciclo de escrita do proprio teste |
