# QA-0032 — Thumbnail no Worker, load test e facet de fecho (TASK-0032)

- runId: R-20261109-01 · attempt: **3/3** (dispatch humano 2026-11-11 18:34:07, sobrepõe o limite de redispatch único; o bloco de controle segue `qaAttempts: 0/2` — contador é do ODEV, QA não escreve nele)
- data: 2026-11-11 · QA: subagente qa-task + política ponytail-qa (full)
- leitura normativa: SPEC-0009 §J + §C + §D (facet do DoD) + SC-004 em non-functional, contra AC-01..AC-04 da task (6 cenários)
- verdict: PASSED

## STEP ZERO — restauração antes de qualquer coisa

A segunda QA-0032 morreu com **uma** mutação ativa na árvore: `backend/tests/Squid.Tests/Load/FeedLoadTests.cs:53` = `Assert.True(feed.P95 < 0, // QA-MUTATION-M3 ...)` (o clico M3 do driver). Restauração executada por esta QA antes de escrever qualquer artefato:

| prova | resultado |
| --- | --- |
| linha 53 de volta ao PRE | `Assert.True(feed.P95 < 1000,` — marcador removido |
| SHA256 vs PRE embutido no driver (S-16) | `03ADE1779D4D580BD72A805B3B191D0B58539556A65EE84F5921E1BFFB44572A` = `03ADE1779D4D` ✓ byte-exato |
| bump de mtime (S-23) | 18:41:30 (e re-bump natural nos ciclos M3/M4 do driver) |
| varredura `QA-MUTATION` em `backend/` (src+tests, sem bin/obj) | **0** |
| gate `sdd-test.ps1 test` | `20261111-185033-test-gate.log` → **exit 0, Com falha 0, Aprovado 432, Total 432** (6m46s) |
| alvos de produto (ThumbnailService, MediaEndpoints) | já restaurados pela QA morta; reprovados pelos PRE de M1/M2 na execução de hoje (`QA-0032-scope.log`) |

> **Desvio declarado (adjudicável):** o dispatch pedia "431/431 ou BLOCKED". O gate verde real é **432/432** = 431 de base (gate `20261111-112705` do DEV, 11:27) **+ 1 teste próprio da QA morta** (`QaTask0032Tests.cs`, escrito às 15:51), que o mesmo dispatch mandou verificar e reusar; o checker da própria QA codifica a conta `431 + 1 = 432`. A restauração independe da contagem (hash = PRE, resíduo 0, 0 falhas, e os 432 incluem os 3 testes da task + o da QA todos verdes) — prossegui em vez de bloquear por um número obsoleto. Se ODEV/humano lerem isso como violação do passo zero, o estado final é exatamente o exigido: árvore verde, sem mutação, sem resíduo.

## AC → cenário (4 ACs, 6 cenários)

| AC | Cenário | Execução (destaque) | Resultado |
| --- | --- | --- | --- |
| AC-01 | Worker gera thumbnail Skia de imagem/GIF a partir do blob; vídeo/áudio recebem placeholder (GAP-21); registro servido pela rota | `ThumbnailTests.Ac01` + `QaTask0032Tests.Ac01` verdes em `QA-0032-load-run4.log` (exit 0, `Aprovados: 4`, 0 FAIL) com saída própria: `J thumbnail: passada=1 gerados=4 idempotente=0 ... video=placeholder audio=placeholder` (placeholder 1081 B) e `QA-0032 thumb: jpeg=109933B 256x192 gif=97B 1x1 video=placeholder 1081B`; os asserts do teste exigem bytes **servidos pela rota** == `MediaPlaceholders.Bytes` para vídeo E áudio, e JPEG/GIF ≠ placeholder com forma derivada da fonte (640×480 → 256×192; gif 1×1 sem ampliar). Can-fail: **M1** (produto → `QaTask0032Tests` vermelho) e **M2** (produto → `ThumbnailTests` vermelho) | PASS |
| AC-02 | seed exato de **10k em AMBAS** as tabelas (`timeline_item` + `home_timeline`) + P95 < 1 s nas DUAS rotas com P50/P99 no log do teste | medida **por esta QA** com `--logger console;verbosity=detailed` → `QA-0032-load-run3.log` (exit 0): `SC-004 feed: itens=10000 n=100 P50=22,1 ms P95=27,9 ms P99=33,6 ms` e `SC-004 timeline: itens=10000 n=100 P50=16,5 ms P95=22,0 ms P99=25,4 ms`; `COUNT(*)=10000` em cada tabela é assert dentro do teste (onde a carga foi escrita). Can-fail: **M3** (limiar do P95 do feed → vermelho com a mensagem real `SC-004: feed first page P95 31 ms must stay under 1000 ms (n=100, P50 23 ms, P99 34 ms)`) e **M4** (COUNT trocado → vermelho) | PASS |
| AC-03(a) | post em comunidade chega ao assinante SSE conectado em **< 1 s** | `Sprint3ClosingFacetTests` verde em todos os runs (7 do driver + run4): Stopwatch na entrega `ReadUntilAsync` < 1 s + `Assert.Single` do frame. Prova de transitividade: **M6** altera o assert da linha 86 — para alcançá-lo as linhas 44–84 (onde vive (a)) executaram e passaram no mesmo run vermelho | PASS |
| AC-03(b) | `?since=` entrega **só** o delta, sem repetir, nas DUAS rotas (`feed` e `timeline`) | mesmo teste, linhas 75–81: item novo único, na ordem de `seq`, contendo exatamente o `deltaSeq`; sem repetir o anterior. Transit-prova idêntica via M5/M6 (asserts anteriores às linhas mutadas | PASS |
| AC-03(c) | contêiner unlisted não aparece para o não-membro **com a pré-condição de leitura pública antes da virada de política** | linhas 85–89: o outsider lê o feed público **agora** → **200 e vê o item** (pré-condição — o 404 posterior nasce do unlisted, nunca de invisibilidade pré-existente); depois `SetVisibility(unlisted)` → 404 **byte-idêntico** ao contêiner inexistente (SC-005), timeline própria do terceiro viva e sem vazamento, e nenhum frame no stream do outsider com `ping` provando que a conexão ouviu silêncio. Atribuição da troca de política: pré-condição 200 ANTES + 404 DEPOIS. Can-fail: **M6** (pré-condição 200 trocada → vermelho) e **M5** (404 pós-política trocado → vermelho) | PASS |
| AC-04 | gate `sdd-test.ps1 test` exit 0; nenhum arquivo alterado fora dos `touches` | gate final `20261111-190626-test-gate.log` → **exit 0 / 432 aprovados / 0 falhas** (5m4s); varredura final `QA-0032-scope.log`: git vs `QA-0032-git-baseline.txt` → **+12 entradas (todas artefatos QA-0032), 0 removidas, 0 fora da allowlist**; mtime desde o dispatch 18:35 em `backend/src`+`backend/tests` → **só os 4 arquivos que o driver mutou**, todos nos `touches`; resíduo `QA-MUTATION` = **0**; backups `qa0032-*` no TEMP = 0 | PASS |

## Comandos executados (todos nesta sessão, com exit real)

| # | comando | exit | artefato / saída |
| --- | --- | --- | --- |
| 1 | restauração M3 (edit) + `Get-FileHash SHA256` + varredura de resíduos + bump de mtime | 0 | `docs/sdd/qa/QA-0032-restore.log` — hash = PRE, resíduo 0, linha 53 = PRE |
| 2 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` | 0 | `docs/sdd/runs/test-runs/20261111-185033-test-gate.log` — 432/432 (prova da árvore restaurada) |
| 3 | `dotnet test backend/Squid.slnx -c Debug -m:1 --filter FullyQualifiedName~Squid.Tests.Load.FeedLoadTests --logger console;verbosity=detailed` | 0 | `docs/sdd/qa/QA-0032-load-run3.log` — P50/P95/P99 medidos por esta QA nas duas rotas |
| 4 | `dotnet test backend/Squid.slnx -c Debug -m:1 --filter FullyQualifiedName~Squid.Tests.Load --logger console;verbosity=detailed` | 0 | `docs/sdd/qa/QA-0032-load-run4.log` — 4/4 + linhas de output de thumb (vídeo e áudio) |
| 5 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0032.ps1` | 0 | `docs/sdd/qa/QA-0032-mutations.log` — M1..M6 vermelhos + restaurações + GREEN + SUMMARY |
| 6 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` (gate final) | 0 | `docs/sdd/runs/test-runs/20261111-190626-test-gate.log` — 432/432 |
| 7 | varredura final: resíduo + SHA256 dos 4 alvos + mtime desde 18:35 + git vs baseline + backups | 0 | `docs/sdd/qa/QA-0032-scope.log` — resíduo 0, 4/4 = PRE, +12/0/0 |
| 8 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0032.ps1` | 0 | `docs/sdd/qa/QA-0032-check.log` — SUMMARY total=9 fails=0 |
| 9 | `bash docs/sdd/qa/qa-check-task-0032.sh` | 0 | `docs/sdd/qa/QA-0032-check-sh.log` — SUMMARY total=9 fails=0 |

## Ciclos de mutação (can-fail; S-16 SHA256 PRE, S-22 classificação no log, S-23 restore byte-exact + mtime)

| Ciclo | Arquivo (dentro dos `touches`) | O que prova | Vermelho classificado NO LOG |
| --- | --- | --- | --- |
| M1 | `Squid.Worker/Thumbnail/ThumbnailService.cs` | AC-01: todo registro vira placeholder, o thumbnail gerado some | `QaTask0032Tests.Ac01_image... [FAIL]` (log `20261111-185558`) |
| M2 | `Squid.Api/Media/MediaEndpoints.cs` | AC-01: aro de visibilidade da rota de thumb desligado | `ThumbnailTests.Ac01_the_worker... [FAIL]` (`185632`) |
| M3 | `tests/Squid.Tests/Load/FeedLoadTests.cs` | AC-02: limiar do P95 do feed deixa de poder falhar | `FeedLoadTests.Ac02_ten_thousand... [FAIL]` (`185709`) |
| M4 | `tests/Squid.Tests/Load/FeedLoadTests.cs` | AC-02: conferência `COUNT(*)=10000` | `FeedLoadTests.Ac02_ten_thousand... [FAIL]` (`185740`) |
| M5 | `tests/Squid.Tests/Load/Sprint3ClosingFacetTests.cs` | AC-03(c): 404 pós-política trocado | facet `Ac03_the_post_reaches... [FAIL]` (`185815`) |
| M6 | `tests/Squid.Tests/Load/Sprint3ClosingFacetTests.cs` | AC-03(c): pré-condição 200-do-fora trocada | facet `Ac03_the_post_reaches... [FAIL]` (`185849`) |

Todos os 6: `RESTORE-OK hash=PRE residue=0`. GREEN final: `wrapper=0 logExit=0 aprovado=4 falha=0` (log `20261111-185927-test-filter.log`). `SUMMARY cycles=6 failed=0 residue-QA-MUTATION=0 temp-backups=0`.

## Findings

Nenhum achado de produto (0 BLOCKER, 0 MAJOR, 0 MINOR). Notas de fluxo, não são defeito do produto:

1. **Desvio 431→432 do passo zero** (detalhado acima): número obsoleto no dispatch vs o teste reusável que ele próprio mandou reusar; árvore verde e restauração provadas por hash e por 0 falhas.
2. A segunda QA morreu entre aplicar e reverter o M3 (registrado pelo ODEV em 16:14:05); esta tentativa restaurou e re-executou o driver **inteiro do zero** — nenhum resíduo sobrou.
3. O run1 legado da QA morta fechava com `temp-backups=1`; hoje TEMP = 0 e o driver novo fechou com `temp-backups=0` (o artefato `QA-0032-mutations-run.log` fica como histórico).
4. **Checker `.sh` herdado com C5 quebrado** (não é produto): `QA-0032-git-baseline.txt` foi gravado pela QA morta com **UTF-8 BOM + CRLF** e o `comm` do bash comparava contra o LF do `git status` → 100% de falsos positivos (`removidas=132 fora-do-escopo=131`, exit 1 — prova de que o check pode falhar). Corrigido no artefato da QA (`norm()` remove BOM+CR antes do `comm`), re-executado: **9/9 exit 0**, idêntico ao gêmeo ps1 que nunca falhou (`+16 / 0 / 0`).

## Não coberto / unverified

- **Suite de testes em Linux**: `sdd-test.sh` não roda neste repo (procura `*.sln`, o solution é `*.slnx` → exit 3 — memória `build-run-test.md`) e não há dotnet no WSL — segue **unverified**; o gêmeo `.sh` do **checker** foi executado (linha 9).
- **Rede real / SSE entre máquinas**: load test e facet correm in-process no gate (premissa da task: "sem rede, sem servidor externo") — a entrega < 1 s é provada no host de teste, não em produção.
- P95 medido em 100 amostras na máquina de dev: a task assume "regressões grandes ficam vermelhas, ruído fino não" (gap assunto, não é requisito de SLA).

## Reproduzir

```powershell
# pré-requisito: .NET 10 SDK + PowerShell no Windows, repo C:\dev\squid-squad-net\squid-execution
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test    # esperado: exit 0, Aprovado 432, Com falha 0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0032.ps1   # esperado: exit 0, SUMMARY total=9 fails=0
bash docs/sdd/qa/qa-check-task-0032.sh                                                   # esperado: exit 0, SUMMARY total=9 fails=0
```

memoryBankFlags: NENHUM — rota `qa-integracao` lida (`build-run-test.md`, `integrations.md`, `contracts.md`, 115 linhas); S-22 e S-23 confirmados em execução real, nenhuma divergência nova.
