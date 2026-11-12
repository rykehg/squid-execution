<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0035 — Participantes e admin com sucessão do criador

```yaml
runId: R-20261111-01
taskId: TASK-0035
status: DONE
agent: odev
phase: P-05
sequence: 2
createdAt: 2026-11-11 23:45:07
updatedAt: 2026-11-11 23:45:07
parent: SPEC-0010
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Api/Threads/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/Threads/**"]
blocks: ["TASK-0036"]
blockedBy: ["TASK-0034"]
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
nextAction: DONE - QA-0035 PASSED (6/6 ACs, 13 mutacoes, gate 450/450); merge em develop + docs pelo ODEV; dispatch DEV da TASK-0036
evidence: []
memoryBankFlags: []
```

## Intent

Vida social da thread: adicionar participante depois da criação (rota nova, emenda GAP-31),
remover com acesso imediato perdido e `409 last_participant`, rotas de admin (promover/demover,
qualquer admin age, criador protegido) e a sucessão do criador (GAP-32): admin mais antigo
assume; sem admin, o membro mais antigo vira admin/criador.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `POST .../participants` adiciona participante a `Group` (teto 50) só sendo admin —
  participante comum → `403`, não-participante → `404` idêntico, `Direct` → `400`, acima de 50 →
  `400` — teste.
- AC-02: `DELETE .../participants/{actorId}`: admin remove qualquer participante **exceto o
  criador** (`403`); qualquer participante sai de si mesmo; **último participante →
  `409 last_participant`**; removido perde acesso imediato (404 nas rotas da thread) e o
  histórico permanece para quem fica — teste.
- AC-03: `POST`/`DELETE .../participants/{actorId}/admin` promove e remove papel: qualquer admin
  promove/demove outro admin (inclusive admins eleitos); o criador não é demovido nem removido
  por outro admin (`403`); não-admin → `403` — teste.
- AC-04: sucessão (GAP-32): quando o **criador sai**, o admin com menor `joined_at` assume o
  papel de criador; se não houver admin, o membro com menor `joined_at` é promovido a admin e
  vira criador — `Group` com ≥1 participante tem ≥1 admin — teste.
- AC-05: participante adicionado depois enxerga o histórico inteiro da thread (GAP-40) — teste.
- AC-06: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`
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
| GAP-30 | no | emenda (a) ao data-model §2.7: `CreatedBy` + papel admin por participante — original intacto |
| GAP-31 | no | emenda (b) ao contrato §6: `POST .../participants` + `POST/DELETE .../participants/{actorId}/admin` |
| GAP-32 | no | sucessão por menor `joined_at`; criador protegido de remoção/demissão por terceiros |
| GAP-38 | no | `409 last_participant` é código novo da família `last_owner` (§9.11) |
| GAP-40 | no | quem entra depois vê o histórico inteiro; `Direct` não aceita adição e sai de 2→1 com histórico legível |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-11 23:45:07 | odev | task created from SPEC-0010 (entrega 3 de 8 da P-05; blockedBy TASK-0034; decisões humanas rodadas 3/4/5 — admin, sucessão, emendas)
- 2026-11-12 03:07:59 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; blockedBy TASK-0034 resolve (DONE com QA-0034 PASSED, gate 443/443, merge ab1de75 em develop + docs bf50497); autorizacao de commit gate PHASE 2026-11-11 20:42:28 (modelo C) incluida no dispatch - nova branch task/TASK-0035-participantes-e-admin desde develop
- 2026-11-12 03:21:12 | dev | feasibilidade r1: unit=ThreadEndpoints (catch-all POST/GET + ThreadView), store=IThreadStore/SqliteThreadStore, dados=thread+thread_participant (0002 ja traz created_by/role/joined_at, sem migration nova); baseline gate 443/443 exit 0; touches confirmado e disjointo de tasks abertas; D-01..D-04 nao-bloqueantes resolvidas por SPEC-0010 C + AC-01..05
- 2026-11-12 03:39:27 | dev | implementacao: IThreadStore/SqliteThreadStore ganham FindAsync + Add/Remove (sucessao GAP-32 na mesma transacao com ORDER BY joined_at,rowid) + SetRole, ThreadEndpoints ganha os 4 dispatchers de participante no catch-all POST/DELETE, schema intacto (0002 ja traz created_by/role/joined_at, sem 0004); ListForAsync corrigido (o WHERE p.actor_id colapsava participants ao proprio chamador — AC-01/02 observaveis exigiam a lista cheia); decisoes: 400 Direct antes do 403-admin (sem ela AC-01 'Direct 400' e inalcancavel), criador protegido de demissao inclusive por si mesmo (invariante >=1 admin), 409 last_participant com traceId (familia last_owner); testes filtrados Threads 18/18 exit 0
- 2026-11-12 03:40:12 | dev | teste filtrado Threads 17/18 com 1 falha: era expectativa propria do teste (POST participants devolve 201 Created, escrevi 200) — so o teste corrigido, reexecucao 18/18 exit 0; nenhum defeito de produto no ciclo
- 2026-11-12 03:44:43 | dev | gate final AC-06: sdd-test.ps1 test → exit 0 (cabecalho exit: do log), 450/450 = baseline 443 + 7 testes novos; git status mostra apenas os 5 arquivos de touches + este task file (log append-only) — nada fora do escopo
- 2026-11-12 03:46:41 | odev | ODEV verificou antes de transicionar: gate final 20261112-034418-test-gate.log lido no arquivo (exit: 0, Aprovado 450, Com falha 0, 4m38s); commit aff9679 conferido via git show (5 files: 3 produto + 2 teste, todos em touches, branch task/TASK-0035-participantes-e-admin); backend git status = 0 entradas; touchedOutsidePlan so task file (append-only) e logs do proprio gate; step result DONE aceito
- 2026-11-12 03:46:43 | odev | DEV achado e correcao dentro dos touches: ListForAsync da TASK-0033 filtrava WHERE p.actor_id no join externo e listava so o proprio chamador em participants (nenhum teste anterior pega - Assert.Contains(caller)); corrigido para semi-join com lista cheia, exigido pela observabilidade de AC-01/02 e pelo out-row do contrato 6; D-01..D-05 fechadas pela escada (SPEC-0010 C + ACs + precedente da casa); 1 vermelho foi expectativa propria do teste (201 vs 200), so o teste corrigido
- 2026-11-12 03:46:44 | odev | transicao IN_DEVELOPMENT -> AWAITING_QA
- 2026-11-12 04:23:21 | qa | attempt 1/2 executada: baseline Threads 18/18 exit 0 (20261112-035540-test-filter.log); teste temporario da QA QaTask0035RemovedRoutesTests verde 19/19 (040053) e removido depois dos ciclos; driver 13 ciclos can-fail (12 vermelhos classificados no log; M4 v1 quebrou o BUILD do mutante CS1026 - CLASSIFICACAO-ERRADA do proprio driver, mutante redesenhado e M4 v2 vermelho na re-execucao -Only); gate final 450/450 exit 0 (20261112-041813-test-gate.log)
- 2026-11-12 04:23:21 | qa | verdict PASSED - AC-01..AC-06 cobertos por cenario executado, 0 BLOCKER (notas de QA: anomalia M4 v1 do mutante, BOM restaurado apos edicao do driver, dotnet ausente no WSL); M12 prova que o suite do DEV pega o bug do semi-join da lista; escopo backend byte-identico a aff9679 (git status vazio + blob-identity 2/2), residuo QA-MUTATION 0; doc docs/sdd/qa/QA-0035-participantes-e-admin.md
- 2026-11-12 04:32:59 | odev | ODEV verificou antes de transicionar: QA-0035-participantes-e-admin.md com verdicto PASSED lido no arquivo; gate final 20261112-041813-test-gate.log exit: 0 / Aprovado 450 / Com falha 0 / 4m37s; residuo QA-MUTATION = 0 (git grep backend); commit QA ef9431e conferido (9 arquivos so docs/sdd/qa/, pai aff9679, sem push, main intocada); backend git status = 0 entradas, blob-identity 2/2 byte-identico a aff9679; check OK - veredito aceito
- 2026-11-12 04:33:00 | odev | QA-0035 PASSED attempt 1/2: 6/6 ACs, 13 ciclos can-fail (M1 ordem 400/403, M2 teto 50, M3/M4 portoes e acesso imediato, M5/M6 remocao e criador, M7/M8 admin, M9 codigo 409, M10 penultimo, M11 sucessao ORDER BY, M12 semi-join do bug da lista - testes do DEV pegam, M13 filtro joined_at GAP-40); M4 v1 anomalo (quebrou build, CLASSIFICACAO-ERRADA) redesenhado; teste temporario da QA para rotas do removido removido depois (git 0); unverified registrados (suite Linux, contencao multi-processo, corrida add x add no teto, traceId do 409 sem assert)
- 2026-11-12 04:33:02 | odev | transicao AWAITING_QA -> DONE; TASK-0035 = entrega 3 de 8 da P-05 encerrada; merge em develop + docs pelo ODEV (gate modelo C); TASK-0036 desbloqueada

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-12 03:44:45 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261112-031950-test-gate.log — baseline 443/443 |
| 2026-11-12 03:44:46 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 0 | build exit 0 (2 execucoes, antes e depois dos testes) |
| 2026-11-12 03:44:48 | dev | sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Threads | 1 | docs/sdd/runs/test-runs/20261112-033701-test-filter.log — 17/18: expectativa propria do teste (201 vs 200) |
| 2026-11-12 03:44:50 | dev | sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Threads | 0 | docs/sdd/runs/test-runs/20261112-033831-test-filter.log — 18/18 apos corrigir o teste |
| 2026-11-12 03:44:52 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261112-034418-test-gate.log — gate final 450/450 |
| 2026-11-12 03:46:47 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate final AC-06) | 0 | docs/sdd/runs/test-runs/20261112-034418-test-gate.log - exit: 0 / Aprovado 450 / Com falha 0 / 4m38s - VERIFICADO PELO ODEV lendo o arquivo |
| 2026-11-12 03:46:48 | dev | git show --stat --format= HEAD (pos-commit) | 0 | aff9679 - 5 files changed 1184 insertions 57 deletions - 3 produto + 2 teste em touches - VERIFICADO PELO ODEV via git show |
| 2026-11-12 04:23:23 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Threads (baseline pre-QA) | 0 | docs/sdd/runs/test-runs/20261112-035540-test-filter.log (exit: 0, Com falha 0, Aprovado 18, Total 18) |
| 2026-11-12 04:23:25 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Threads (com teste temporario da QA na arvore) | 0 | docs/sdd/runs/test-runs/20261112-040053-test-filter.log (exit: 0, Com falha 0, Aprovado 19, Total 19) |
| 2026-11-12 04:23:26 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0035.ps1 -VerifyOnly | 0 | stdout VERIFY total=13 bad=0 e BOM OK (13 padroes conferidos contra a fonte pristine) |
| 2026-11-12 04:23:28 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0035.ps1 (execucao cheia, 13 ciclos) | 1 | docs/sdd/qa/QA-0035-mutations.log (12 vermelhos classificados + M4 v1 anomalo CLASSIFICACAO-ERRADA, exit 1 = failed=1 do ledger) |
| 2026-11-12 04:23:30 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0035.ps1 -Only M4 (mutante v2) | 0 | docs/sdd/qa/QA-0035-mutations.log (M4 v2 vermelho 20261112-041039 com 3 falhas + GREEN 041105 19/19, SUMMARY cycles=1 failed=0) |
| 2026-11-12 04:23:31 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate AC-06) | 0 | docs/sdd/runs/test-runs/20261112-041813-test-gate.log (exit: 0, Aprovado 450, Com falha 0, 4m37s - lido dentro do log) |
| 2026-11-12 04:23:33 | qa | varredura de escopo pwsh (git status -uall, git status backend, git hash-object vs rev-parse, mtime desde 03:54:59, residuo, teste temporario) | 0 | docs/sdd/qa/QA-0035-scope.log (GIT-BACKEND 0, blob-identity 2/2 match=True, mtime-count 2, residue 0, temp-test False) |
| 2026-11-12 04:26:49 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0035.ps1 | 0 | docs/sdd/qa/QA-0035-check.log (SUMMARY total=27 fails=0) |
| 2026-11-12 04:26:50 | qa | wsl -e bash docs/sdd/qa/qa-check-task-0035.sh | 0 | docs/sdd/qa/QA-0035-check-sh.log (SUMMARY total=24 fails=0) |
| 2026-11-12 04:28:35 | qa | git commit -m (1 commit autorizado; 9 arquivos so de docs/sdd/qa; amend apenas para reencode dos 2 check logs UTF-16 -> UTF-8 BOM da casa; sem push) | 0 | ef9431e - 9 files changed 763 insertions, git show --stat conferido, pai aff9679, branch task/TASK-0035-participantes-e-admin, main intocada |
