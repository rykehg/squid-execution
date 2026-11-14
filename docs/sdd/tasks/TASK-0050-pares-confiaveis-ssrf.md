<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0050 — Pares confiáveis: `Federation:TrustedPeerHosts` nas guardas de federação

```yaml
runId: R-20261113-01
taskId: TASK-0050
status: DONE
agent: odev
phase: P-06
sequence: 2
createdAt: 2026-11-13 22:45:47
updatedAt: 2026-11-14 04:46:21
parent: SPEC-0011
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Api/Posts/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/**"]
blocks: ["TASK-0047"]
blockedBy: ["TASK-0044", "TASK-0046"]
inputArtifacts: ["docs/sdd/specs/SPEC-0011-federacao-p06.md", "docs/sdd/plan/plan.md", "docs/sdd/tasks/TASK-0044-inbox-verificacao-e-aplicacao.md"]
outputArtifacts: ["backend/src/Squid.Api/Posts/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/**"]
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
nextAction: DONE - QA-0050 PASSED (4/4 ACs, 5 mutacoes, gate 559/559, commits 9758f8c + c10b741); merge em develop pelo ODEV - fila restante 0047/0048/0049 aguarda a TASK-0051 (gate humano pendente)
evidence: []
memoryBankFlags: []
```

## Intent

Entrega extra da P-06 (cortada em 2026-11-13 22:45:47 a partir do parked do DEV da TASK-0044):
`Federation:TrustedPeerHosts` — allowlist **exata** e **vazia por padrão** que libera
loopback/rede privada **só** nos clientes de federação (resolução de ator, download de mídia,
entrega ao par). Sem ela, o harness de dois silos (TASK-0047) e o runbook/interop (0048/0049)
seriam recusados pela própria guarda SSRF da fase. Decisão do ODEV no nível especificação:
SPEC-0011 §C + GAP-49. O unfurl de link **não** muda.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: com `Federation:TrustedPeerHosts` ausente ou vazio (default), os clientes de federação
  continuam **recusando** alvo em loopback e rede privada (comportamento de hoje byte-idêntico)
  — teste.
- AC-02: com um host do par nomeado na lista, a resolução de ator e o download de mídia daquele
  par são **aceitos** mesmo em loopback/rede privada; qualquer host **fora** da lista continua
  recusado (inclusive na mesma porta) — teste.
- AC-03: a allowlist vale **só** para os clientes de federação (ator, mídia, entrega ao par): o
  unfurl de link continua bloqueando rede privada mesmo com a lista nomeada — teste.
- AC-04: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1
  test` exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Api/Posts/**` — `SsrfGuard` ganha a noção de alvo confiável sem afetar o
  unfurl; `RemoteMediaClient` usa a allowlist
- `backend/src/Squid.Service/**` — `HttpFederatedActorClient` e o cliente de entrega do par
  (pump da TASK-0046) usam a allowlist; leitura de `Federation:TrustedPeerHosts`
- `backend/tests/Squid.Tests/**` — testes das ACs
- produto **fora** dos `touches` desta task: qualquer lacuna levantada como dúvida, não corrigida aqui

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-49 | no | allowlist exata, vazia por padrão, só clientes de federação; unfurl intocado; runbook (0048) e harness (0047) nomeiam os pares locais; produção = gate humano (SPEC-0011 §C) |
| — | no | casa com o parked "escape de SSRF para pares em loopback/Docker local" do DEV da TASK-0044 (log 2026-11-13) — o restante do parked (média membersOnly em `GET /api/media`) virou S-40 no backlog |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-13 22:45:47 | odev | task created from SPEC-0011 (entrega extra 10 de 10 da P-06, cortada do parked SSRF do DEV da TASK-0044; GAP-49; blockedBy TASK-0044+TASK-0046, blocks TASK-0047; fila: apos a TASK-0046)
- 2026-11-14 03:45:40 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; runId R-20261113-01; blockedBy resolve (TASK-0044 DONE com QA-0044 PASSED e TASK-0046 DONE com QA-0046 PASSED, gate 554/554, merge 855293b em develop + docs b48bf17); autorizacao de commit gate PHASE 2026-11-11 20:42:28 (modelo C) incluida - nova branch task/TASK-0050-pares-confiaveis-ssrf desde develop; logLines e evidenceRows sem relogio para o ODEV carimbar (sem pipe em comandos)
- 2026-11-14 04:16:03 | dev | DEV feasibility 1/2: task lida, toques conferidos contra SsrfGuard, RemoteMediaClient, HttpFederatedActorClient, HttpPeerSender, SquidServiceHost e os testes; baseline pre-existente 554/554 exit 0; D-01 fechado pela SPEC-0011 C - a allowlist libera tambem a entrega ao par e o shortcut do HttpPeerSender aplica o gate a esta task
- 2026-11-14 04:16:04 | dev | DEV: criado TrustedPeerHosts em backend/src/Squid.Api/Posts/TrustedPeerHosts.cs - Federation:TrustedPeerHosts exata e vazia por padrao (escalar com virgulas ou filhos indexados); consultado antes do perimetro por RemoteMediaClient, HttpFederatedActorClient e HttpPeerSender; registro na raiz de composicao SquidServiceHost
- 2026-11-14 04:16:04 | dev | DEV: HttpPeerSender ganhou o perimetro SSRF que o shortcut adiava (shortcut removido) - recusa fora do perimetro responde PeerSendAnswer falso, mesmo caminho de par inalcanhavel (Pending, backoff, max_attempts ate a DLQ); LinkUnfurler intocado, a lista nao chega nele
- 2026-11-14 04:16:05 | dev | DEV: MotorHost da suite do pump nomeia 127.0.0.1 em Federation:TrustedPeerHosts - o cenario do PeerScenarioServer e loopback, do mesmo jeito que o harness da TASK-0047 e o runbook da TASK-0048 vao nomear os pares locais
- 2026-11-14 04:16:05 | dev | DEV testes: novo TrustedPeerHostsTests com 5 facts cobrindo AC-01 (default recusa nos tres clientes), AC-02 (par nomeado aceito, host fora da lista recusado na mesma porta), entrega ao par, parser exato sem curinga nem sufixo, e AC-03 (unfurl segue bloqueando com a lista nomeada no host de producao)
- 2026-11-14 04:16:06 | dev | DEV gate AC-04: comando sdd-test.ps1 test exit 0 com 559/559 (log runs/test-runs/20261114-041319-test-gate.log, exit 0 no cabecalho); build exit 0; areas Federation 56/56 e Unfurl 63/63 exit 0; commit 9758f8c na branch task/TASK-0050-pares-confiaveis-ssrf criada de develop b48bf17, sem push
- 2026-11-14 04:16:10 | odev | ODEV verificou antes de transicionar: gate 20261114-041319-test-gate.log lido no arquivo (exit: 0, Aprovado 559, Com falha 0, 6m46s) e filtro 20261114-040610-test-fast.log (exit: 0, 5/5); commit 9758f8c conferido via git show --stat (8 arquivos, todos nos touches); D-01 do DEV fechada no nivel especificacao (SPEC-0011 C ja declara os tres clientes sob a allowlist) - aceita; logLines (6) e evidenceRows (7) carimbados via sdd-state; step result DONE aceito
- 2026-11-14 04:16:12 | odev | transicao IN_DEVELOPMENT -> AWAITING_QA (attempt 1/2) - checkout da branch da task pelo ODEV e dispatch QA a seguir
- 2026-11-14 04:47:25 | qa | qa-task + ponytail-qa (full): task file lida (AWAITING_QA, qaAttempts 0/2), SPEC-0011 C + GAP-49 e rota qa-integracao lidas; baseline powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-baseline-task-0050.ps1 exit 0 - HEAD 9758f8c, 8 arquivos do commit, nada pushado, WSL sem dotnet (QA-0050-git-baseline.txt)
- 2026-11-14 04:47:25 | qa | qa: verify dos padroes de mutacao - powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0050.ps1 -VerifyOnly exit 0 - VERIFY total=5 bad=0 (QA-0050-verify.log)
- 2026-11-14 04:47:26 | qa | qa: baseline verde dos cenarios das ACs - powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~TrustedPeerHostsTests exit 0 - 5/5 em 20261114-042903-test-filter.log
- 2026-11-14 04:47:26 | qa | qa: 5 mutacoes can-fail sobre o PRODUTO (T1 AC-01 falha=4, T2 AC-02 falha=2, T3 AC-02 falha=1, T4 AC-01 falha=2, T5 AC-03 falha=1), RESTORE-OK 5/5 hash=PRE, GREEN 5/5 - powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0050.ps1 exit 0 (QA-0050-mutations.log, cycles=5 failed=0)
- 2026-11-14 04:47:27 | qa | qa: gate AC-04 - powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test exit 0 - 20261114-043833-test-gate.log lido dentro do arquivo (exit: 0, Com falha 0, Aprovado 559, Total 559, 6m44s)
- 2026-11-14 04:47:27 | qa | qa: escopo AC-04 Windows - powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-scope-task-0050.ps1 exit 0 - commit 8/8 nos touches, fora-dos-touches=0, residue=0, blob 5/5, mtime=5, fails=0 (QA-0050-scope.log)
- 2026-11-14 04:47:27 | qa | qa: escopo AC-04 gemeo Linux - wsl -e bash docs/sdd/qa/qa-scope-task-0050.sh exit 0 - mesmas contagens, fails=0 (QA-0050-scope-sh.log)
- 2026-11-14 04:47:28 | qa | qa: verdict PASSED 4/4 - doc docs/sdd/qa/QA-0050-pares-confiaveis-ssrf.md escrita e artefatos commitados em c10b741 na branch task/TASK-0050-pares-confiaveis-ssrf (10 arquivos so de docs/sdd/qa, task file intocado); unverified: suite Linux/WSL por dotnet ausente e sdd-test.sh S-11/S-12
- 2026-11-14 04:47:34 | odev | ODEV verificou antes de transicionar: QA-0050-pares-confiaveis-ssrf.md com verdict PASSED lido no arquivo; gate 20261114-043833-test-gate.log exit: 0 / Aprovado 559 / Com falha 0 / 6m44s (lido no arquivo); mutations SUMMARY cycles=5 failed=0 residue-backend=0 lido no log; commit c10b741 conferido via git show --stat (10 arquivos so docs/sdd/qa); logLines (8) e evidenceRows (7) carimbados via sdd-state - veredito aceito
- 2026-11-14 04:47:34 | odev | QA-0050 PASSED attempt 1/2 (PRODUTO+TESTES): 4/4 ACs, 5 ciclos can-fail (T5 prova que o unfurl de loopback e barrado com a lista nomeada - probe mais estrito), RESTORE byte-exact, GREEN 5/5; gate 559/559; 0 BLOCKER/MAJOR; 5 NOTA (suite Linux, trust-by-name = gate humano, porta na chave = fail-closed, GAP-26 pre-existente)
- 2026-11-14 04:47:35 | odev | transicao AWAITING_QA -> DONE; entrega 7 de 10 da P-06 encerrada - merge em develop + docs commitados pelo ODEV; PROXIMO DA FILA e a TASK-0047 mas ela deve nascer depois da TASK-0051 (assinatura de ator registrado), que aguarda o gate humano pendente

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-14 04:16:06 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261114-040118-test-gate.log (baseline pre-existente 554/554) |
| 2026-11-14 04:16:07 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 0 | console (build: exit 0) |
| 2026-11-14 04:16:07 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -Fast -TestFilter FullyQualifiedName~Squid.Tests.Federation | 0 | docs/sdd/runs/test-runs/20261114-040505-test-fast.log (56/56) |
| 2026-11-14 04:16:08 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -Fast -TestFilter FullyQualifiedName~Squid.Tests.Unfurl | 0 | docs/sdd/runs/test-runs/20261114-040547-test-fast.log (63/63) |
| 2026-11-14 04:16:08 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -Fast -TestFilter FullyQualifiedName~TrustedPeerHostsTests | 0 | docs/sdd/runs/test-runs/20261114-040610-test-fast.log (5/5 novos) |
| 2026-11-14 04:16:08 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261114-041319-test-gate.log (gate AC-04: 559/559, exit 0 no cabecalho do log) |
| 2026-11-14 04:16:09 | dev | git commit na branch task/TASK-0050-pares-confiaveis-ssrf criada de develop b48bf17 (8 arquivos, apenas toques) | 0 | 9758f8c |
| 2026-11-14 04:47:28 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-baseline-task-0050.ps1 | 0 | docs/sdd/qa/QA-0050-git-baseline.txt |
| 2026-11-14 04:47:29 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0050.ps1 -VerifyOnly | 0 | docs/sdd/qa/QA-0050-verify.log |
| 2026-11-14 04:47:29 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~TrustedPeerHostsTests | 0 | docs/sdd/runs/test-runs/20261114-042903-test-filter.log |
| 2026-11-14 04:47:30 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0050.ps1 | 0 | docs/sdd/qa/QA-0050-mutations.log |
| 2026-11-14 04:47:30 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261114-043833-test-gate.log |
| 2026-11-14 04:47:31 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-scope-task-0050.ps1 | 0 | docs/sdd/qa/QA-0050-scope.log |
| 2026-11-14 04:47:31 | qa | wsl -e bash docs/sdd/qa/qa-scope-task-0050.sh | 0 | docs/sdd/qa/QA-0050-scope-sh.log |
