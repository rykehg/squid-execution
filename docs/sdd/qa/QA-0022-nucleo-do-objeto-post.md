<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: CONCLUIDO
- owner: qa
- lastVerified: 2026-11-09 13:57:10

# QA-0022 — Núcleo do objeto: publicar, ler e responder (TASK-0023)

- task: TASK-0023
- spec: docs/sdd/specs/SPEC-0009-publicacao-feed-postagem.md (seção A + `Gaps` GAP-23 duas sequências, GAP-24 sanitização, linha `422 invalid_reply_target`), specs/001-squid-mvp/data-model.md §2.5/§2.8/§3.1, specs/001-squid-mvp/contracts/http-api.md §1/§5/§8/§9
- runId: R-20261109-01
- qaAttempt: 1/2
- verdict: PASSED

Execução, não leitura: o gate de baseline da QA rodou **259/259 exit 0** (`20261109-131352`, `exit: 0`
lido **dentro** do log — S-22), os **7 cenarios desta QA** rodaram **7/7** (`20261109-132846`), o
gate final rodou **266/266 exit 0** (`20261109-134454`) e o gate de reconfirmação na árvore limpa
repetiu **266/266 exit 0** (`20261109-135355`) = 244 do baseline do projeto + 15 do DEV + 7 da QA.
Os 7 ACs têm prova de can-fail: **10 aplicações de mutação em 9 sites** dentro dos `touches`
(M1..M9), em **3 rodadas vermelhas de propósito** classificadas **pelo log gravado**, cada uma com
SHA256 **PRE** (S-16), restauração byte-exact + bump de mtime (S-23), hash = PRE e verde logo em
seguida — **resíduo `QA-MUTATION` em `backend/` = 0**. Escopo: `git status --porcelain` e varredura
de mtime desde 11:30 mostram produto alterado **só** dentro dos `touches` (Domain/Objects,
Api/Posts, os 3 stores + `SquidServiceHost.cs` em Service, tests/Posts) + artefatos de fluxo; a
única escrita fora dos `touches` é `docs/sdd/plan/plan.md` **do odev às 11:34:54**, anterior à
sessão do DEV (11:36+), já adjudicada no `## Log`. Falhas pré-existentes: **NENHUM** — 259/259
antes e 266/266 depois, sem uma vermelha fora dos ciclos de mutação. Achado **novo** (MINOR,
executado): o cap de 5000 é conferido no corpo **bruto** antes da sanitização e o `Content`
guardado chega a 25000 caracteres.

## What is proven

| AC | Scenario (executed) | Script | Result |
| --- | --- | --- | --- |
| AC-01 | `QaTask0023Tests.Ac01_publish_answers_the_frozen_fields_and_appends_a_raw_create_event_on_one_global_sequence` (`backend/tests/Squid.Tests/Posts/QaTask0023Tests.cs:36`): 201 com `id` começando por `{origin}/o/` (`:64`); **linha `objectseq` inexistente antes** da publicação (`:56-59`) e `seq = 1` depois (`:64`); `policyVersion` e `visibilityAtPublish` **lidos do corpo do contêiner** no momento do post (`:66-69`); `PUT /policy` muda a visibilidade e sobe a versão e o post **continua com os valores antigos** (`:71-87`) = congelada; segundo contêiner volta a `seq = 1` (`:93`); o log ganha **+2 eventos com Seq global contíguo** (`logBefore.Count+1/+2`) atravessando os dois contêineres (`:95-106`); os eventos que já existiam saem **byte-idênticos** = append-only (`:100-104`); `Type = Create`, `ActorId`, `urn:squid:activity:` (`:107-111`); o `Payload` é **JSON cru** parseado com os mesmos valores (`:113-118`) e o **documento persistido no SQLite guarda exatamente o mesmo string** (`:120-128`). **M2** (visibilidade fixada em `Public`) vermelha em AC-01 (`:69`, strings diferentes); **M3** (contador de seq virado único e global) vermelha em AC-01 (`:93`) | filtro xunit + 2 mutações | PASS |
| AC-02 | `QaTask0023Tests.Ac02_denied_member_gets_403_with_a_deny_row_and_an_invisible_container_answers_the_absent_404` (`:141`): membro de `community` com `posting = moderators` → **403 `policy_denied`** (`:157-163`) e **uma linha `Deny`** da ação `Post` com a versão **2** que o corpo do contêiner reporta e motivo não vazio (`:166-175`); contêiner invisível ao autor vs slug ausente → **404 byte-idêntico**, `not_found`, sem `traceId`, sem `Exception` (`:183-193`). **M8** (linha `Deny` → `Allow`) vermelha em AC-02 (`:171`, `Assert.Single` sem item); **M7** (o 404 do publicar passa a distinguir ausente de invisível) vermelha em AC-02 (`:188`, corpos diferentes) | filtro xunit + 2 mutações | PASS |
| AC-03 | `QaTask0023Tests.Ac03_concurrent_publishes_keep_the_seq_unique_and_growing_and_containers_stay_apart` (`:195`): **4 publicações em voo juntas** (3 sessões diferentes) todas 201 (`:217-219`) com seq exatamente **`{1,2,3,4}`** sem repetir nem faltar (`:228`) e 4 ids distintos (`:229`); a seguinte é **5** (`:234-237`); o outro contêiner responde **`seq = 1`** depois de A estar em 5 (`:239-242`) = contêineres não se misturam. **M3** (chave de seq por contêiner virada para uma única `objectseq/shared`) vermelha em AC-03 (`:228`, coleções diferentes) | filtro xunit + 1 mutação | PASS |
| AC-04 | `QaTask0023Tests.Ac04_reading_answers_the_post_or_one_404_for_invisible_and_absent` (`:245`): **três grafias do mesmo recurso** — chave opaca, URI canônica e URI percent-encoded — as três 200 e os três corpos **iguais** (`:257-267`); invisível, ausente e anônimo respondem **o mesmo 404** sem `traceId` (`:277-291`). **M1** (`HiddenPost` com `withTraceId: true`) vermelha em AC-04 (`:286`); **M3** vermelha em AC-04 (`:272`) | filtro xunit + 2 mutações | PASS |
| AC-05 | `QaTask0023Tests.Ac05_three_answers_publish_the_fourth_is_422_and_out_of_sight_targets_share_the_404` (`:294`): raiz `depth 0` → respostas **1, 2 e 3 publicam** com `depth`, `replyToId`, `containerId` e seq esperada lida do corpo da raiz (`:310-321`); **4º nível → 422 `invalid_reply_target`** (`:326-331`) e **nenhuma linha nem evento novo** (`:333-338`); alvo fora da visão do leitor vs id ausente → **404 byte-idêntico** sem `traceId` (`:342-350`); meio-contêiner **público** com leitor sem membership → **403 `policy_denied`** (`:353-367`, contrato §8: existência pública não usa 404). **M4** (`MaxDepth` 3 → 4) vermelha em AC-05 (`:328`, 201 no lugar do 422); **M1** vermelha em AC-05 (`:345`) | filtro xunit + 2 mutações | PASS |
| AC-06 (a) | `QaTask0023Tests.Ac06_a_suspended_actor_is_refused_and_audited_while_reading_keeps_working` (`:369`): publica antes, `IActorStatusStore.SuspendAsync` pela rota da DI (D-02: sem rota até a TASK-0031), publicar → **403 `policy_denied`** (`:385`) com **linha `Deny` cujo motivo nomeia `suspenso`** (`:390-394`), responder → também **403** (`:397-400`) e **`GET` continua 200** (`:403`, §3.1 leitura segue). **M9** (guarda de suspenso virada para `(ActorStatus)99`) vermelha em AC-06(a) (`:385`, 201 no lugar de 403) | filtro xunit + 1 mutação | PASS |
| AC-06 (b) | `QaTask0023Tests.Ac06_content_above_the_cap_is_400_and_html_and_c0_controls_are_sanitized` (`:406`): `5001` → **400 `validation_failed`** e **contagem de linhas e de eventos não muda** (`:416-424`); `5000` → **201** (`:426-429`); `content` com `<script>`, `&`, aspas, C0 (`\u0007 \u0000 \u001b \x7F`) e acento → **201** com `<`/`>` fora, `&lt;script&gt;…`, `&amp;`, `&quot;`, `&#39;`, `olá` preservado e **nenhum caractere de controle** além de tab/LF/CR (`:431-453`); o `GET` devolve **exatamente** o mesmo texto guardado (`:455-459`). **M5** (cap 5000 → 9000) vermelha em AC-06(b) (`:416`); **M6** (sanitizador deixa de escapar `<`) vermelha em AC-06(b) (`:437`) | filtro xunit + 2 mutações | PASS |
| AC-07 | gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` → **`20261109-135355-test-gate.log` com `exit: 0` e `Com falha: 0, Aprovado: 266, Total: 266`**, lido **dentro** do log (S-22); baseline `20261109-131352` = 259/259 `exit: 0`. Escopo: `git status --porcelain` + varredura de mtime desde 11:30 → produto alterado **só** nos `touches`; fora deles só artefatos de fluxo e `docs/sdd/plan/plan.md` (**odev, 11:34:54**, anterior à sessão do DEV, adjudicado no `## Log`); resíduo `QA-MUTATION` em `backend/` = **0**; hash final = **PRE** nas 9 áreas mutadas; `sdd-state.ps1 check` → **OK, log 9, evidências 20** | gate + git/mtime/hash + check | PASS |

## Mutation inventory — every can-fail, with its SHA256 and its red log

| # | Site (path:L) | Mutation | Red run → assertion that broke |
| --- | --- | --- | --- |
| M1 | `backend/src/Squid.Api/Posts/PostEndpoints.cs:424-426` (`HiddenPost`) | `withTraceId: false` → `true` | `20261109-133204` → AC-04 `:286`, AC-05 `:345` |
| M2 | `PostEndpoints.cs:124` (`VisibilityAtPublish`) | `container.Policy.Visibility` → `VisibilityPolicy.Public` | `20261109-133204` → AC-01 `:69` |
| M3 | `backend/src/Squid.Service/SqliteObjectStore.cs:40` (`SeqRow`) | chave `slug` → `"shared"` | `20261109-133615` → AC-01 `:93`, AC-03 `:228`, AC-04 `:272` |
| M4 | `backend/src/Squid.Domain/Objects/ReplyDepth.cs:12` | `MaxDepth` 3 → 4 | `20261109-133615` → AC-05 `:328` |
| M5 | `backend/src/Squid.Domain/Objects/PostObject.cs:24` | `MaxContentLength` 5000 → 9000 | `20261109-133615` → AC-06(b) `:416` |
| M6 | `backend/src/Squid.Domain/Objects/PostSanitizer.cs:40` | escape de `<` → caractere cru | `20261109-133615` (mascarado pelo M5, registrado) e `20261109-134010` → AC-06(b) `:437` |
| M7 | `PostEndpoints.cs:80-83` (404 do publicar) | ausente e invisível passam a responder diferente | `20261109-133615` → AC-02 `:188` |
| M8 | `PostEndpoints.cs:102` (linha da política) | `PolicyAudit.Deny` → `PolicyAudit.Allow` | `20261109-134010` → AC-02 `:171` |
| M9 | `PostEndpoints.cs:89` (guarda de suspenso) | `== ActorStatus.Suspended` → `== (ActorStatus)99` | `20261109-134010` → AC-06(a) `:385` |

10 aplicações (M6 entrou em dois grupos) sobre 9 sites; antes de cada uma, SHA256 do arquivo-alvo
(`Squid.Api__Posts__PostEndpoints.cs = A383C5DE…D1F3`, `SqliteObjectStore.cs = 44FF0735…EC5`,
`ReplyDepth.cs = E7808CBC…1AB8`, `PostObject.cs = 0BCBB1B1…9A55`, `PostSanitizer.cs = 51582E8B…85DC`),
depois restauração byte-exact + bump de mtime e **hash idêntico ao PRE**.

### DEV decisions verified (not disputed — executed)

| Decision | Evidence |
| --- | --- |
| `Object.Seq` vive num documento próprio por contêiner com CAS `WHERE Version = @Version` | `SqliteObjectStore.cs:43-67` (retry de `InconsistentStateException` com ETag lido); executado por AC-03 (4 concorrentes → `{1,2,3,4}`) e derrubado por **M3**; a assinatura do CAS em si é `SqlitePersistence` de QA-0003 e fica intocada por esta task |
| `Activity.Seq` global vive no documento da própria store (movível pela migration da TASK-0024 sem mudar a interface) | `SqliteActivityStore.cs:33-65`; executado por AC-01 (Seq contíguo global `logBefore+1/+2` e documento persistido com o mesmo payload) |
| rotas aceitam id opaco, URI percent-encoded ou URI crua (`{*id}`/`{*target}`) | `PostEndpoints.cs:46-53` + `SqliteObjectStore.cs:107-116` (`Uri.UnescapeDataString`); executado em AC-04 (`:257-267`, as três grafias 200 com corpo igual) |
| visibilidade espelhada (não copiada por edição) em `Squid.Api/Posts` reutilizando membros `internal` de `ContainerEndpoints` | `git status --porcelain` **sem** `backend/src/Squid.Api/Containers/**` e varredura de mtime desde 11:30 sem nenhum arquivo daquele diretório; `PostEndpoints.cs:339-354` é o espelho de `CanSeeAsync` |
| `IActorStatusStore` em `Squid.Api/Posts`, linha ausente = Active, rota de suspenso fica para a TASK-0031 (D-02) | `SqliteActorStatusStore.cs:32-39` (`?? Active`); executado em AC-06(a) pelo `SuspendAsync` da DI — a rota `/admin/actors/{id}/suspend` **não existe** (fora do escopo desta task, contrato §7 já a lista para a TASK-0031) |
| depth escala: raiz 0, aceita até 3, 4ª resposta → `422 invalid_reply_target` (D-01) | `ReplyDepth.cs:15-18` + `PostEndpoints.cs:220-225`; executado em AC-05 (`:310-331`) e derrubado por **M4** |
| `Idempotency-Key` (contrato §1) está pendurado, não é AC | confirmado: nenhum AC cita a chave e nenhuma rota deste task a lê — registrada como não coberta abaixo |

## Scripts

| Script | Language / runner | What it does |
| --- | --- | --- |
| `backend/tests/Squid.Tests/Posts/QaTask0023Tests.cs` | xunit (runner da casa) | os 7 cenarios desta QA, um por AC, contra o host HTTP real |
| `backend/tests/Squid.Tests/Posts/PostPublishTests.cs`, `PostReadAndReplyTests.cs`, `PostSeqTests.cs`, `PostContentRulesTests.cs`, `PostRoutes.cs` | xunit | os 15 testes do DEV, executados em cada gate (259 e 266) — cobrem também a sessão/CSRF e o 404 de contêiner apagado por soft-delete |
| `docs/sdd/_tools/sdd-test.ps1 test [-TestFilter …]` | pwsh + dotnet | runner da casa: baseline, 4 filtros (1 vermelho da própria QA + 3 vermelhos de mutação), 1 verde de filtro e 2 gates |
| `docs/sdd/_tools/sdd-state.ps1 append-log / append-evidence / check` | pwsh | escrita das 1 linhas de `## Log` e 12 linhas de `## Evidence`, e o `check` final |
| `docs/sdd/_tools/sdd-test.sh test` (executado via Git Bash) | bash | gemeo Linux: recusa com `no *.sln found` (a solution é `*.slnx`) — plataforma não coberta por aqui |

## How to reproduce

Prerequisites: Windows com .NET SDK 10, repositório em `C:\dev\squid-squad-net\squid-execution`
(sem rede depois do restore). Sem servidor, sem banco compartilhado, sem instalação.

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test   # expected: exit 0, 266/266 (log novo em docs/sdd/runs/test-runs/)
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Posts.QaTask0023   # expected: exit 0, 7/7
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-state.ps1 check docs/sdd/tasks/TASK-0023-nucleo-do-objeto-post.md   # expected: check: OK
```

## Last run

| Date/Time | Command | Exit | Output (max 10 lines) |
| --- | --- | --- | --- |
| 2026-11-09 13:13:52 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` (baseline) | 0 | `exit: 0` · `Aprovado! – Com falha: 0, Aprovado: 259, Total: 259` |
| 2026-11-09 13:26:46 | `… -TestFilter FullyQualifiedName~Squid.Tests.Posts.QaTask0023` (1a) | 1 | `Com falha: 4, Aprovado: 3` — as 4 falhas eram **minhas** asserções de `traceId` em 403/422/400; o contrato §1 exige `traceId` no envelope e só o 404 compartilhado fica sem ele. Expectativas corrigidas, código intocado |
| 2026-11-09 13:28:46 | `… -TestFilter …QaTask0023` (2a) | 0 | `Aprovado! – Com falha: 0, Aprovado: 7, Total: 7` |
| 2026-11-09 13:32:04 | `… -TestFilter …QaTask0023` (mutação M1+M2) | 1 | `Com falha: 3` → AC-01 `:69`, AC-04 `:286`, AC-05 `:345` |
| 2026-11-09 13:36:15 | `… -TestFilter …QaTask0023` (mutação M3+M4+M5+M6+M7) | 1 | `Com falha: 6` → AC-01 `:93`, AC-02 `:188`, AC-03 `:228`, AC-04 `:272`, AC-05 `:328`, AC-06 `:416` |
| 2026-11-09 13:40:10 | `… -TestFilter …QaTask0023` (mutação M6+M8+M9) | 1 | `Com falha: 3` → AC-02 `:171`, AC-06(a) `:385`, AC-06(b) `:437` |
| 2026-11-09 13:44:54 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` (gate final) | 0 | `exit: 0` · `Aprovado! – Com falha: 0, Aprovado: 266, Total: 266` |
| 2026-11-09 13:49:18 | `… -TestFilter FullyQualifiedName~Squid.Tests.Posts.QaProbe0022` (sonda, arquivo removido depois) | 1 | `PROBE raw=5000 stored=25000 (sanitized text above the data-model cap)` |
| 2026-11-09 13:53:55 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` (reconfirmação, árvore limpa) | 0 | `exit: 0` · `Aprovado! – Com falha: 0, Aprovado: 266, Total: 266` · `check: OK - log entries 9, evidence rows 20` |

## Findings

| # | Severity | Where | What breaks | Evidence |
| --- | --- | --- | --- | --- |
| 1 | MINOR (novo, desta task) | `backend/src/Squid.Api/Posts/PostEndpoints.cs:408` (+ `:123`, `:263`) | O cap de 5000 é conferido no corpo **bruto** e só depois o texto é sanitizado; escapar multiplica (`&` → `&amp;`), então um corpo de exatamente 5000 metacaracteres grava **25000 caracteres** no `Content` — acima do `≤ 5000` do data-model §2.5, que fala do campo guardado. Não é vazamento nem crash (o amplificador é limitado a 5× e o texto fica escapado), mas todo consumidor futuro (feed, SSE, projeções da TASK-0024) que acreditar no teto do modelo recebe post maior. O AC-06 como está escrito continua verdadeiro (`content > 5000` do **cliente** → 400), por isso não é BLOCKER | sonda executada e depois removida: `docs/sdd/runs/test-runs/20261109-134918-test-filter.log` → `PROBE raw=5000 stored=25000`; código em `PostEndpoints.cs:408` (`content.Length > PostObject.MaxContentLength`) antes de `:123` (`PostSanitizer.Sanitize`) |
| 2 | MINOR (pré-existente) | `docs/sdd/_tools/sdd-test.sh:20-21` | O gemeo Linux procura só `*.sln` e o solution é `*.slnx` → `no *.sln found within depth 3` e exit 1 (não exit 3 como a memoria bank registra): **a plataforma Linux não tem gate executável neste repo**. Nada desta task quebrou ali — é o mesmo estado do baseline | `bash docs/sdd/_tools/sdd-test.sh test` via Git Bash → `SH-EXIT=1`; `build-run-test.md:42` (registro divergente, ver `memoryBankFlags`) |
| 3 | INFO (pré-existente, adjudicada) | `docs/sdd/plan/plan.md` | Único arquivo modificado fora dos `touches`: mtime **2026-11-09 11:34:54**, escrito pelo odev na criação da task, anterior à sessão do DEV (11:36+) e à desta QA; o `## Log` já a registra às 13:03:33 | varredura de mtime desde 11:30 + `git status --porcelain` |
| 4 | MINOR (redação do AC, para ODEV) | `docs/sdd/tasks/TASK-0023-nucleo-do-objeto-post.md:67` (AC-05) | "reply a post de **outro contêiner** ou invisível → 404 idêntico" tem duas leituras possíveis. Executada: alvo em contêiner **fora da visão** do leitor → 404 byte-idêntico (provado, `:345`); alvo em contêiner **público** sem membership → **403 `policy_denied`** (`:361-367`), que é o que SPEC-0009 §A ("403 existência pública ou 404 invisível") e contrato §8 mandam — a leitura literal de "404 sempre" contradizeria o contrato. A rota de reply não tem campo de contêiner, então a única forma do cliente expressar "outro contêiner" é mirar post que ele não enxerga | `QaTask0023Tests.cs:342-367`, `contracts/http-api.md:180-181`, SPEC-0009 §A |

## Not covered, and why

- **Linux / WSL / container** — o único distro do WSL desta máquina é `podman-machine-default` e
  `dotnet` não existe nele (`dotnet: command not found`); instalar SDK exige gate humano, que esta
  QA não tem. O gemeo `sdd-test.sh` além disso não acha a solução (`*.slnx`). **`unverified`** —
  nunca assumido verde; a única evidência Linux é o run histórico do CI (98/98, 2026-11-07,
  anterior a esta task). Risco: um comportamento diferente de SQLite/Orleans em Linux passaria
  despercebido.
- **`Idempotency-Key` (contrato §1)** — nenhum AC cobre; fica como parked idea do dispatch da fase.
- **Corrida entre processos** — AC-03 prova concorrência dentro de um host (4 requests em voo);
  duas instâncias disputando o mesmo arquivo SQLite não são testadas. Risco: o CAS depende do
  `Version` do arquivo, que protege entre processos, mas não foi exercitado.
- **Leitura com política virada depois da publicação** (visibilidade só pode ficar mais restritiva,
  §4) — AC-01 prova que `VisibilityAtPublish` e `PolicyVersion` ficam congelados; o efeito da
  virada sobre quem lê é o escopo da TASK-0025.
- **`GET /api/posts/{id}` de post apagado por remoção (tombstone)** — remoção é a TASK-0030; aqui
  o 404 idêntico foi provado com contêiner invisível, contêiner apagado (teste do DEV no gate) e
  id inexistente.
- **Reações/likes** — explicitamente fora do escopo (gap da task: não criar rota); não testado.

## memoryBankFlags

- `[GAP: memory bank absent]` **não confere** — `docs/memory-bank/index.md` existe e foi lido
  (rota `qa-integracao`: `build-run-test.md`, `integrations.md`, `contracts.md`).
- `[DIVERGENCIA: build-run-test.md diz que o sdd-test.sh sai com exit 3; observado exit 1 com
  "no *.sln found" — source: docs/sdd/_tools/sdd-test.sh:20-21]` — detalhe pequeno, proposto ao
  ODEV para o fecho da fase.
- `[MUDANCA_RELEVANTE: PADRAO — publicação de objeto com duas sequências (Object.Seq por contêiner
  com CAS ETag + Activity.Seq global no documento do log) e store de status de ator sem tabela
  (linha ausente = Active) — source: backend/src/Squid.Api/Posts/PostEndpoints.cs:114-146]` —
  proposta de consolidação na memória bank; quem verifica é o ODEV.
