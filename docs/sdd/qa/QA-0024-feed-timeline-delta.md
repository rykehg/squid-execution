<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: VEREDITO
- owner: qa
- lastVerified: 2026-11-09 20:14:24

# QA-0024 — Feed e timeline com delta `?since=` e visibilidade corrente

- task: TASK-0025
- spec: SPEC-0009
- runId: R-20261109-01
- verdict: PASSED
- qaAttempt: 1/2

Leitura normativa usada: SPEC-0009 seção C + B emendada (membros lidos na projeção),
`data-model.md` §4/§2.5 e contrato `http-api.md` §5/§9.3. Baseline de referência 277/277
(P-04 início) + 8 testes do DEV = gate de entrada 285/285. A QA executou 8 cenários próprios
(`QaTask0025Tests`), 7 ciclos de mutação can-fail e o gate final 293/293 — nada foi apenas lido.

## What is proven

| AC | Scenario (executed) | Script | Result |
| --- | --- | --- | --- |
| AC-01 | `Ac01_qa_chained_and_repeated_since_calls_answer_the_exact_delta` — 5 publicações; página cronológica mais-recente-primeiro com `nextSince` = seq mais novo; caminhada por deltas `limit=2` (cada passo só `seq > since`, ascendente, sem duplicata, sem buraco); repetição de cada chamada **byte-idêntica**; página vazia mantém o cursor e `hasMore=false`. Can-fail: **M1** (`>` → `>=`) e **M2** (`DESC` → `ASC`) deixaram este cenário vermelho | `backend/tests/Squid.Tests/Feed/QaTask0025Tests.cs` · `sdd-test.ps1 test -TestFilter FullyQualifiedName~QaTask0025Tests.Ac01` | PASS |
| AC-02 | `Ac02_qa_tighter_policy_answers_the_404_of_absent_for_page_and_delta` — não-membro lê o público e guarda o cursor; `MembersOnly` e depois `Private`: página **e** `?since=` respondem `404` **byte-idêntico** ao contêiner inexistente com a MESMA query; timeline do leitor limpa; dono continua lendo. Can-fail: **M5** (`CanSeeAsync` → `return true`) vermelho aqui e no AC-04 | mesmo arquivo · filtro `.Ac02` | PASS |
| AC-03 | `Ac03_qa_timeline_serves_active_memberships_with_the_same_delta` — só contêineres com `Active`; delta idêntico/ascendente cruzando contêineres (seq global, GAP-23); **sair** remove mesmo depois de visto, **entrar** devolve; entrada pós-projeção traz os itens futuros. Can-fail: **M3** (`>` → `>=` no `ActorDelta`) e **M4** (gate `Active` removido do `ReachableAsync`) | mesmo arquivo · filtro `.Ac03` | PASS |
| AC-04 | `Ac04_qa_unlisted_is_404_identical_and_never_reaches_a_third_party_timeline` — `Unlisted`: feed e `?since=` do não-membro `404` idêntico ao inexistente; membro `Active` lê (código P-03); o terceiro com timeline própria carrega o próprio item e **nunca** os do unlisted (contagem e marcadores `qa0025leak-*` ausentes). Can-fail: **M5** | mesmo arquivo · filtro `.Ac04` | PASS |
| AC-05 | `Ac05_qa_banned_reads_nothing_and_silenced_reads_normally` — `Silenced` lê o feed normalmente; `Banned` (após voltar a Active, §3.3): feed `404` idêntico ao inexistente na página e no delta, e os itens somem da timeline (membership corrente). Can-fail: **M6** (checagem de `Banned` neutralizada) | mesmo arquivo · filtro `.Ac05` | PASS |
| AC-06 | Gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` → **`20261109-195855-test-gate.log` com `exit: 0` e `Com falha: 0, Aprovado: 293, Total: 293`** (285 + 8 da QA), lido **dentro** do log (S-22). Escopo: varredura de mtime desde o dispatch do DEV (17:15:32, sem bin/obj) → **9 arquivos, todos nos `touches`**; `git status` do fim vs baseline da QA → **3 entradas novas, todas em `docs/sdd/qa/`**; resíduo `QA-MUTATION` em `backend/` = **0**; SHA256 final = **PRE** nos 3 mutados (`BBAB3FDC25DA`, `894AB7CEB5DD`, `A80E83B345F1`); backups `qa0024-*` no TEMP = 0 | gate + varreduras + `qa-check-task-0025.ps1/.sh` | PASS |
| AC-08 | `Ac08_qa_the_cap_holds_after_sanitization_and_nothing_is_persisted` — bruto `MaxContentLength+1` → `400 validation_failed`; 5000 `&` (escaparia para 25000) e o degrau `cap/4+1` `&` → `400` com **contadores de `GrainState` e do log `Activity` imóveis**; reply `<`×(cap−1000) → `400` idem (segundo call site); exato `cap/5` `&` → `201` com `Content` persistido **= 5000** lido de volta pelo `GET /posts` e pelo feed; abaixo da fronteira publica (`cap−2`); feed final só os 3 itens válidos. Derivado de `PostObject.MaxContentLength` (§2.5), não de número decorado. Can-fail: **M7** (checagem pós-sanitização desligada) | mesmo arquivo · filtro `.Ac08` | PASS |

Cenários extras executados (fora dos ACs, sustentam os acima):

| Fora dos ACs | Scenario | Result |
| --- | --- | --- |
| SC-005 (rigor) | `Sc005_qa_100_attempts_of_hidden_feeds_are_byte_identical_to_absent` — **100 tentativas** (5 estados escondidos: Private, MembersOnly, Unlisted, política apertada depois de "ver antes", Banned × 3 variantes de query: pura, `?since=1`, `?limit=5`) contra o contêiner inexistente: `404` em todas, corpo **byte-idêntico**, sem `traceId`, sem marcador de conteúdo | PASS (0 vazamentos) |
| Decisão do DEV | `Pagination_default_is_20_the_cap_is_100_and_beyond_range_since_is_empty` — 101 posts: default **20** itens com `hasMore=true`; `limit=500` desce para **100**; `since` além do máximo → **lista vazia** mantendo o cursor (não erro); `limit=0` → `400 validation_failed` | PASS |

## Scripts

| Script | Language / runner | What it does |
| --- | --- | --- |
| `backend/tests/Squid.Tests/Feed/QaTask0025Tests.cs` | xunit via `sdd-test.ps1 test -TestFilter` | 8 cenários próprios da QA (7 ACs + paginação + SC-005 de 100 tentativas) |
| `docs/sdd/qa/qa-mutate-task-0025.ps1` | pwsh (gate `sdd-test.ps1`) | 7 ciclos can-fail: SHA256 PRE (S-16) → 1 mutação `QA-MUTATION` com pattern-count=1 → vermelho classificado **no log** (S-22) → restore byte-exact + bump de mtime (S-23) → hash=PRE → verde |
| `docs/sdd/qa/QA-0024-observations.log` | artefato (`--logger console;verbosity=detailed`) | saída das observações: AC-03 backfill `False`, AC-05 Silenced `False`, SC-005 100 tentativas |
| `docs/sdd/qa/QA-0024-mutations.log` | artefato | saída dos 7 ciclos (M1..M7) + `SUMMARY cycles=7 failed=0 residue=0 temp-backups=0` |
| `docs/sdd/qa/QA-0024-sh-twin.log` | artefato (WSL bash) | execução do gêmeo `sdd-test.sh` (probe/test/test com alvo) |
| `docs/sdd/qa/qa-check-task-0025.ps1` | pwsh | 19 checks de evidência (logs S-22, hashes, resíduo, escopo, git, sdd-state, doc) |
| `docs/sdd/qa/qa-check-task-0025.sh` | bash (WSL) | gêmeo com os mesmos 19 checks, executa no Linux (inclui normalização dos artefatos UTF-16/BOM/CR sem tocar os originais) |

## How to reproduce

Prerequisites: Windows com `dotnet` 10 e o repositório em `C:\dev\squid-squad-net\squid-execution` (nenhuma instalação, nenhum serviço externo — os testes sobem host local em porta dinâmica).

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~QaTask0025Tests"   # esperado: exit 0, Aprovado 8, Total 8
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0025.ps1                                                 # esperado: exit 0, SUMMARY cycles=7 failed=0 residue-QA-MUTATION=0 temp-backups=0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test                                                    # esperado: exit 0, Com falha 0 / Aprovado 293 / Total 293
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0025.ps1                                                   # esperado: RESULT: PASS, exit 0
```

## Last run

| Date/Time | Command | Exit | Output (max 10 lines) |
| --- | --- | --- | --- |
| 2026-11-09 19:25:11 | `sdd-test.ps1 test` (gate de baseline da QA) | 0 | `20261109-192511-test-gate.log` → `exit: 0` · `Com falha: 0, Aprovado: 285, Total: 285` |
| 2026-11-09 19:38:22 | `sdd-test.ps1 test -TestFilter FullyQualifiedName~QaTask0025Tests` (1ª execução) | 1 | `20261109-193822-test-filter.log` → `error CS0120` em `QaTask0025Tests.cs:662` (`GetAsync` não-estático chamado de método estático) — defeito do ARQUIVO DA QA, corrigido na hora; nenhum teste de produto rodou |
| 2026-11-09 19:40:58 | mesmo filtro (após o corrigir) | 0 | `20261109-194058-test-filter.log` → `Com falha: 0, Aprovado: 8, Total: 8` |
| 2026-11-09 19:43:45 | `dotnet test backend/Squid.slnx -c Debug -m:1 --filter FullyQualifiedName~QaTask0025Tests --logger console;verbosity=detailed` | 0 | `QA-0024-observations.log` → `AC-03 ... = False` · `AC-05 ... = False` · `SC-005: 100 tentativas ... 0 vazamentos` · `AC-08: 3 recusas sem gravar ...` |
| 2026-11-09 19:48 | `qa-mutate-task-0025.ps1 -Only M1` (smoke do driver) | 0 | `SUMMARY cycles=1 failed=0 residue=0 temp-backups=0` — M1 PROVEN |
| 2026-11-09 19:49–19:53 | `qa-mutate-task-0025.ps1` (7 ciclos M1..M7) | 0 | `QA-0024-mutations.log` → 7× `PROVEN` · vermelhos `194951..195252` classificados nos logs · `SUMMARY cycles=7 failed=0 residue-QA-MUTATION=0 temp-backups=0` |
| 2026-11-09 19:56 | `bash docs/sdd/_tools/sdd-test.sh probe` / `test` / `test backend/Squid.slnx` | 0 / 1 / 1 | `QA-0024-sh-twin.log` → WSL sem `dotnet` · `no *.sln found within depth 3` (achado pré-existente) · `exit 127 (real test failure)` reportado pelo gêmeo |
| 2026-11-09 19:58:55 | `sdd-test.ps1 test` (gate final AC-06) | 0 | `20261109-195855-test-gate.log` → `exit: 0` · `Com falha: 0, Aprovado: 293, Total: 293` (285 + 8 da QA) |
| 2026-11-09 20:13:32 | `qa-check-task-0025.ps1` + `bash qa-check-task-0025.sh` (execução final, após todos os appends) | 0 / 0 | `QA-0024-check.log` → `SUMMARY total=19 fails=0` · `RESULT: PASS` nos dois sistemas (runs 1–2 do gêmeo ficaram vermelhas por defeitos do PRÓPRIO script — `cd` de profundidade errada e leitura de UTF-16/BOM sem normalizar — corrigidos e registrados nas linhas de evidência) |
| 2026-11-09 20:14:24 | mesma dupla (confirmação de fechamento, após a última linha de evidência) | 0 / 0 | `QA-0024-check.log` run4 → `RESULT: PASS` nos dois sistemas; git delta final = 5 novas, todas em `docs/sdd/qa/`; resíduo `QA-MUTATION` = 0 |

## Findings

| # | Severity | Where | What breaks | Evidence |
| --- | --- | --- | --- | --- |
| 1 | MINOR | `backend/src/Squid.ReadModels/Projections/ActivityProjector.cs:95` (e `:113`) | A timeline pessoal **não faz backfill**: `home_timeline` só recebe linhas dos membros `Active` no instante da projeção (B emendado da SPEC-0009), então os itens de um contêiner em que o ator entra **depois** da projeção nunca aparecem na timeline dele — observado `AC-03 observacao: item anterior a entrada fresca presente na timeline = False`. O feed do contêiner continua mostrando tudo; nenhum dos 7 ACs cai (o AC-03 manda "só Active" e provar entrada/saída), mas o `data-model.md` §4 descreve o `home_timeline` como "itens dos contêineres do ator". Se o ODEV ler a frase literalmente, isto vira MAJOR e pede task (backfill no join ou janela retroativa) | `QA-0024-observations.log` + `QaTask0025Tests.Ac03_qa_timeline...` executado |
| 2 | MINOR | `backend/src/Squid.Api/Posts/FeedEndpoints.cs:143` (gate de leitura) + `ActivityProjector.cs:142` | Interação AC-03 × AC-05: membro `Silenced` é **excluído da timeline pessoal** (leitura exige `Active`; projeção só grava `Active`) enquanto o feed dele lê normalmente — observado `AC-05 observacao: item na timeline de membro Silenced = False`. O AC-03 diz "só `Active`", então o comportamento segue o AC-03; fica o alerta para o ODEV confirmar que "Silenced lê normalmente" (AC-05) é escopo de leitura de contêiner e não da timeline | `QA-0024-observations.log` + `QaTask0025Tests.Ac05_qa_banned...` executado |
| 3 | MINOR (pré-existente) | `docs/sdd/_tools/sdd-test.sh:20` (e `:64`) | O gêmeo `.sh` do gate só procura `*.sln` e o repositório tem `Squid.slnx` → `test` sem alvo sai com `exit 1` / `no *.sln found within depth 3` (mesmo achado da QA-0022, re-executado aqui); apontando o alvo explicitamente, o `dotnet` **não existe no WSL** e o gêmeo reporta `exit 127 (real test failure)` — sem guarda de ambiente como o `.ps1` tem. Pre-existente à TASK-0025 (baseline 277 verde), não bloqueia | `QA-0024-sh-twin.log` (três execuções com exit) |

Pre-existing failures on baseline 277: **NENHUM** — o gate de baseline desta QA rodou 285/285 `exit: 0` (`20261109-192511`), os 8 vermelhos reportados na log do DEV eram só os testes novos da subagente morta (conferido na leitura do arquivo `20261109-183545`) e nada regrediu depois.

## Not covered, and why

- **Plataforma Linux do produto** — o WSL desta máquina não tem `dotnet` (instalar exige gate humano; aprovações estão desativadas nesta sessão) → `unverified`, nunca assumido igual ao Windows que passou. O que **foi** executado no Linux: os gêmeos bash (`sdd-test.sh` — achado 3 — e `qa-check-task-0025.sh`).
- **`?before=`, `since_id/max_id` e `Link rel=next/prev`** (contrato §1/§9.8) — estacionados pelo ODEV como ideia registrada no `## Log` da task e fora dos 7 ACs; a QA não discorda do park e não os testou. Risco: navegação por `Link` só entra quando virar task/backlog.
- **`qa-mutate-task-0025.ps1` em Linux** — o driver orquestra `sdd-test.ps1`/`dotnet`, que não existe no WSL → gêmeo `.sh` não faz sentido e não foi escrito; a restauração byte-exact (S-16/S-23) é provada no Windows e conferida de novo pelos checkers nos dois sistemas.
- **Backfill do achado 1** — se adjudicado como exigência, nenhum teste atual cobre a semântica "entrou depois viu antes"; a cobertura precisará nascer na task que corrigir.
- **SSE (`/api/stream`), remoção e mídia** — TASK-0026/0030/0027, fora do escopo desta task.
