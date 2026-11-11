<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0023 — Núcleo do objeto: publicar, ler e responder

```yaml
runId: R-20261109-01
taskId: TASK-0023
status: DONE
agent: odev
phase: P-04
sequence: 2
createdAt: 2026-11-09 11:22:36
updatedAt: 2026-11-09 14:03:43
parent: SPEC-0009
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Domain/Objects/**", "backend/src/Squid.Api/Posts/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/Posts/**"]
blocks: ["TASK-0024"]
blockedBy: []
inputArtifacts: ["docs/sdd/specs/SPEC-0009-publicacao-feed-postagem.md", "specs/001-squid-mvp/data-model.md", "specs/001-squid-mvp/contracts/http-api.md"]
outputArtifacts: ["backend/src/Squid.Domain/Objects/**", "backend/src/Squid.Api/Posts/**", "backend/src/Squid.Service/**"]
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
nextAction: DONE - QA-0022 PASSED (7/7 ACs, gate 266/266, 10 mutacoes); achado do cap vira AC-08 da TASK-0025; proxima entrega TASK-0024
evidence: []
memoryBankFlags: []
```

## Intent

Abre a P-04: `POST /api/containers/{slug}/posts` (texto) avaliado pelo `PolicyActor` com a
política vigente, `Object.Seq` por contêiner, evento `Activity` (fonte das projeções),
`GET /api/posts/{id}` e replies de profundidade ≤ 3 — a base funcional da qual as projeções,
o SSE e a mídia penduram.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `POST /api/containers/{slug}/posts` (sessão + CSRF, política que permite) → 201 com
  `id` = `{scheme}://{host}/o/{id}`, `seq` do contêiner, `policyVersion` avaliada e
  `visibilityAtPublish` congelada; um evento `Activity: Create` é gravado em store append-only
  com `Seq` global e `Payload` JSON cru — teste.
- AC-02: negação: `Member` com política que nega `Posting` → `403 policy_denied` com linha
  `Deny` na trilha; contêiner invisível ao autor → `404` **byte-idêntico** ao inexistente
  (SC-005) — teste dos dois corpos.
- AC-03: `Object.Seq` por contêiner é única e crescente sob **duas publicações concorrentes**
  no mesmo contêiner (ETag serializa) e contêineres diferentes não interferem — teste de
  concorrência.
- AC-04: `GET /api/posts/{id}` devolve o post visível; invisível ou inexistente → mesmo corpo
  de `404` sem traceId — teste comparando os dois.
- AC-05: `POST /api/posts/{id}/replies`: profundidade 3 funciona, 4º nível → `422
  invalid_reply_target`; reply a post de outro contêiner ou invisível → 404 idêntico — teste.
- AC-06: ator `Suspended` tenta publicar → negado e auditado (§3.1); `content` > 5000 → 400 e
  sanitização (GAP-24) remove HTML/controles C0 — testes.
- AC-07: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`
  exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Domain/Objects/**` (novo: `PostObject`, sanitização, regra de profundidade)
- `backend/src/Squid.Api/Posts/**` (novo: rotas + interfaces `IObjectStore`/`IActivityStore`)
- `backend/src/Squid.Service/**` (stores SQLite + registro)
- `backend/tests/Squid.Tests/Posts/**` (novo)

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-23 | no | duas sequências: `Object.Seq` por contêiner (incremento no estado do contêiner, ETag) e `Activity.Seq` global (contador na migration que a TASK-0024 cria — nesta task o global pode nascer como contador na store de eventos, desde que a interface não mude) |
| GAP-24 | no | sanitização = escapar HTML + remover controles C0 no servidor |
| — | no | `422 invalid_reply_target` para profundidade > 3 (mesmo código do §9.9; sem rota nova de catálogo) |
| — | no | likes/reactions não existem (fora do escopo da fase) — não criar rota |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | A escala de profundidade conta a raiz? | SPEC-0009 | não conta: raiz `depth 0`, resposta direta `1`, aceita até `3`, a 4ª resposta → `422 invalid_reply_target` — SPEC-0009 §A + AC-05 pareiam "profundidade 3 funciona / 4º nível 422"; data-model e REQ-0001 silentes; adjudicado pelo ODEV 2026-11-09 13:00:57 |
| D-02 | De onde vem o `Actor.Status: Suspended` da AC-06, sem rota existente? | SPEC-0009 | store nova `IActorStatusStore` em `Squid.Api/Posts` (linha ausente = Active, sem tabela), consumida pela escada de publicação; a TASK-0031 dirige `SuspendAsync` pelo mesmo store — não duplicar (parked idea do DEV anexada ao dispatch da 0031); adjudicado pelo ODEV 2026-11-09 13:00:57 |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-09 11:34:54 | odev | task created from SPEC-0009 (entrega 1 de 10, cadeia serial 0023..0032; abre Squid.Api/Posts e as stores de objeto/evento)
- 2026-11-09 11:35:29 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; blockedBy NENHUM (primeira task da cadeia da P-04); spec SPEC-0009 e as 10 tasks cortadas na entrevista 2026-11-09 (3 rodadas, 5 decisoes); baseline do gate e 244/244
- 2026-11-09 12:23:37 | dev | feasibility 1/2: gate baseline powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test exit 0 com 244/244; touches confirmados, zero edicao fora deles (Squid.Api/Containers apenas lido: Hidden e ActorId sao internal e reutilizados, CanSee/ActorContext espelhados em Posts com a mesma regra); Object.Seq vive em documento proprio por contêiner (GrainId=slug) com CAS ETag porque Squid.Domain/Containers esta fora de touches; Activity.Seq global vive no proprio documento de IActivityStore (contador movel para a migration TASK-0024 sem mudar a interface); AC-06 precisa de ator Suspended que nao existe em codigo: nasce IActorStatusStore em Squid.Api/Posts com SqliteActorStatusStore (linha ausente = Active), rota de suspender fica para a TASK-0031; D-01 profundidade fechada pela SPEC-0009 secao A com AC-05 (profundidade 3 funciona, 4o nivel -> 422): raiz depth 0, resposta direta depth 1, aceita ate 3, a 4a vira 422 invalid_reply_target; memoria bank ausente
- 2026-11-09 12:50:58 | dev | implementacao concluida: Dominio Objects (PostObject, PostSanitizer com escape dos 5 metacaracteres HTML + remocao de C0 preservando acentos, ReplyDepth raiz 0 ate 3, ActorStatus) + Api/Posts (IObjectStore/IActivityStore/IActorStatusStore, PostView, PostEndpoints com POST /api/containers/{slug}/posts, GET /api/posts/{*id} e POST /api/posts/{*target} para aceitar id opaco ou URI canonica) + Service (SqliteObjectStore com object-seq por contêiner em CAS ETag, SqliteActivityStore com contador global no proprio documento, SqliteActorStatusStore linha ausente = Active) + registro e MapSquidPosts no SquidServiceHost; decisao de postagem inteira por IPolicyEvaluator com PolicyActor completo, Deny/Allow/NotFound gravados na trilha IPolicyAuditStore, 404 de contêiner via ContainerEndpoints.Hidden byte-identico; testes 14/14 verdes com -Fast -TestFilter (3 falhas iniciais corrigidas: WebUtility.HtmlEncode mangulava acentos, LastSeq do activity log nao avancava, ifMatch faltando no helper); gate completo em curso
- 2026-11-09 12:58:45 | dev | gate final do gate powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test exit 0 com 259/259 (244 pre-existentes verdes + 15 novos de Posts, zero falhas); AC-07 de escopo provado por git status: modificado apenas backend/src/Squid.Service/SquidServiceHost.cs (dentro de Squid.Service) e criados Squid.Domain/Objects, Squid.Api/Posts, os tres Sqlite*Store em Squid.Service e tests/Squid.Tests/Posts; docs/sdd/plan/plan.md aparece como M mas foi gravado as 11:34:54 pelo odev antes desta sessao (11:36+); AC-01..AC-06 cobertas por 15 testes, AC-07 pelo gate
- 2026-11-09 13:03:33 | odev | ODEV verificou antes de transicionar: gate final 20261109-125726-test-gate.log lido no arquivo (exit: 0, Aprovado 259, Com falha 0 - S-22); residuos QA-MUTATION em backend = 0; git status = so toques (Domain/Objects, Api/Posts, 3 stores em Service, tests/Posts, SquidServiceHost) + docs de fluxo; check exit 0 (log 5, evidencias 7); step result DONE aceito - D-01 e D-02 resolvidas na escada da especificacao e registradas na tabela de Doubts
- 2026-11-09 13:03:33 | odev | parked ideas do DEV registradas para os dispatchs futuros: (1) Idempotency-Key do contrato secao 1 no POST /posts - nenhum AC cobre, dono da fase decide onde cair; (2) a TASK-0031 DEVE dirigir SuspendAsync do IActorStatusStore ja existente (enum ActorStatus em Domain/Objects) em vez de duplicar store; (3) flag memoryBankFlags [GAP: memory bank absent] NAO confere - docs/memory-bank existe, rejeitavel no fecho como na P-03
- 2026-11-09 13:03:34 | odev | transicao IN_DEVELOPMENT -> AWAITING_QA; entrega 1 de 10 da P-04
- 2026-11-09 13:53:40 | qa | qa attempt 1/2 executou os 7 ACs com 7 cenarios proprios em backend/tests/Squid.Tests/Posts/QaTask0023Tests.cs (nao apenas re-leu os testes do DEV): baseline 259/259 exit 0 (log 20261109-131352, exit: 0 lido DENTRO do arquivo - S-22), classe da QA 7/7 (20261109-132846), gate final 266/266 exit 0 (20261109-134454) e gate de reconfirmacao na arvore limpa; can-fail provado com 10 aplicacoes de mutacao em 9 sites dentro dos touches em 3 rodadas vermelhas classificadas pelo log gravado, SHA256 PRE antes (S-16) e restauracao byte-exact com bump de mtime depois (S-23), residuos QA-MUTATION em backend = 0; achado NOVO desta task (MINOR): o cap de 5000 e conferido no corpo bruto ANTES da sanitizacao (PostEndpoints.cs:408 e :123), entao 5000 ampersand guardam 25000 caracteres no Content acima do limite do data-model 2.5 - sonda executada, valor observado, arquivo da sonda removida (log 20261109-134918); achados pre-existentes: o gemeo docs/sdd/_tools/sdd-test.sh:20 so procura *.sln e nao roda neste repo (exit 1 observado; a memoria bank diz exit 3 - DIVERGENCIA) e docs/sdd/plan/plan.md fora dos touches escrito pelo odev as 11:34:54 antes da sessao do DEV; plataforma Linux NAO executada (WSL so tem podman-machine e dotnet ausente, instalar exige gate) = unverified; verdict PASSED com 1 achado novo nao-bloqueante e 2 pre-existentes
- 2026-11-09 14:03:45 | odev | ODEV verificou antes de transicionar: QA-0022-nucleo-do-objeto-post.md existe; gate de reconfirmacao 20261109-135355-test-gate.log lido no arquivo (exit: 0, Aprovado 266, Com falha 0 - S-22); residuos QA-MUTATION em backend = 0; check exit 0 (log 9, evidencias 20); veredito aceito - 7/7 ACs com cenarios proprios da QA, 10 aplicacoes de mutacao em 9 sites restauradas byte-exact, sonda do cap com arquivo removido depois
- 2026-11-09 14:03:45 | odev | achado F1 (MINOR NOVO) adjudicado (escada: especificacao): o cap de 5000 e conferido no corpo BRUTO antes da sanitizacao - 5000 "&" gravam Content = 25000 acima do <= 5000 do data-model 2.5 (sonda executada, PROBE raw=5000 stored=25000); SPEC-0009 A manda Content <= 5000 e a leitura correta e validar APOS a sanitizacao (mantendo a checagem bruta cedo); correcao adjudicada como AC-08 da TASK-0025 (proximo serial que toca Api/Posts), AC-06 daqui continua verde como escrito
- 2026-11-09 14:03:45 | odev | achado F4 (redacao do AC-05) adjudicado: "post de outro contenedor -> 404" le-se como "alvo fora da visao do autor -> 404 identico" (executado e verde); alvo em contenedor PUBLICO sem membership -> 403 policy_denied e o que SPEC-0009 A e contrato 8 mandam - a rota de reply nao tem campo de contenedor, entao a leitura publica so se expressa mirando post invisivel; nenhuma mudanca de AC
- 2026-11-09 14:03:46 | odev | achado F2 (gemeo sdd-test.sh sem *.slnx) e pre-existente = backlog S-11 ja registrado com a mesma sugestao; a flag DIVERGENCIA (build-run-test.md diz exit 3, observado exit 1) fica para a consolidacao da P-04 - a ferramenta vence o memory bank; flags MUDANCA_RELEVANTE PADRAO (duas sequencias + status sem tabela) candidatas a patterns.md no fecho; [GAP: memory bank absent] de novo nao confere
- 2026-11-09 14:03:46 | odev | transicao AWAITING_QA -> DONE; TASK-0023 = entrega 1 de 10 da P-04 encerrada (QA-0022 PASSED); cadeia libera a TASK-0024

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-09 12:55:43 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 0 | stdout: 0 Aviso(s) 0 Erro(s) |
| 2026-11-09 12:55:44 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (baseline antes da mudanca) | 0 | docs/sdd/runs/test-runs/20261109-113952-test-gate.log - 244/244 |
| 2026-11-09 12:55:44 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -Fast -TestFilter FullyQualifiedName~Squid.Tests.Posts (1a) | 1 | docs/sdd/runs/test-runs/20261109-124547-test-fast.log - erro de compilacao ifMatch no helper |
| 2026-11-09 12:55:45 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -Fast -TestFilter FullyQualifiedName~Squid.Tests.Posts (2a) | 1 | docs/sdd/runs/test-runs/20261109-124743-test-fast.log - 3 falhas: HtmlEncode em acentos, LastSeq do activity, assercao de conteudo |
| 2026-11-09 12:55:46 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -Fast -TestFilter FullyQualifiedName~Squid.Tests.Posts (final) | 0 | docs/sdd/runs/test-runs/20261109-125021-test-fast.log - 14/14 |
| 2026-11-09 12:55:46 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate 1, snapshot de 14 testes novos) | 0 | docs/sdd/runs/test-runs/20261109-125348-test-gate.log - 258/258 |
| 2026-11-09 12:58:45 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate final, 15 testes novos) | 0 | docs/sdd/runs/test-runs/20261109-125726-test-gate.log - 259/259 |
| 2026-11-09 13:03:34 | odev | Select-String do gate final + varredura QA-MUTATION em backend + git status + sdd-state check | 0 | docs/sdd/runs/test-runs/20261109-125726-test-gate.log exit: 0 / Aprovado 259 / Com falha 0; residuos 0; toques fora NENHUM; check OK log 5 evidencias 7 |
| 2026-11-09 13:54:19 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (baseline da QA: 244 do gate + 15 novos do DEV) | 0 | docs/sdd/runs/test-runs/20261109-131352-test-gate.log - exit: 0 - 259/259 lido no arquivo (S-22) |
| 2026-11-09 13:54:20 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Posts.QaTask0023 (1a execucao, assercoes de traceId da propria QA corrigidas) | 1 | docs/sdd/runs/test-runs/20261109-132646-test-filter.log - 3 de 7 falhas nas MINHAS assercoes (traceId em 403/422/400 e permitido pelo contrato secao 1) - corrigido |
| 2026-11-09 13:54:20 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Posts.QaTask0023 (2a execucao) | 0 | docs/sdd/runs/test-runs/20261109-132846-test-filter.log - 7/7 aprovados |
| 2026-11-09 13:54:20 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Posts.QaTask0023 (mutacao A: VisibilityAtPublish fixo e traceId no 404) | 1 | docs/sdd/runs/test-runs/20261109-133204-test-filter.log - 3 falhas propositais: AC-01 linha 69, AC-04 linha 286, AC-05 linha 345 |
| 2026-11-09 13:54:21 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Posts.QaTask0023 (mutacao B: seq por contêiner, MaxDepth, cap, sanitizador e divergencia do 404) | 1 | docs/sdd/runs/test-runs/20261109-133615-test-filter.log - 6 falhas propositais: AC-01 93, AC-02 188, AC-03 228, AC-04 272, AC-05 328, AC-06b 416 |
| 2026-11-09 13:54:22 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Posts.QaTask0023 (mutacao D: linha Deny, guarda de suspenso e sanitizador) | 1 | docs/sdd/runs/test-runs/20261109-134010-test-filter.log - 3 falhas propositais: AC-02 171, AC-06a 385, AC-06b 437 |
| 2026-11-09 13:54:22 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate final com os 7 testes da QA) | 0 | docs/sdd/runs/test-runs/20261109-134454-test-gate.log - exit: 0 - 266/266 lido no arquivo (S-22) |
| 2026-11-09 13:54:23 | qa | Get-FileHash SHA256 das 9 areas mutadas e restauracao byte-exact com bump de mtime (S-16 e S-23) | 0 | hash = PRE em todas as 9 areas; residuos QA-MUTATION em backend = 0 |
| 2026-11-09 13:54:23 | qa | git status --porcelain e varredura de mtime desde 2026-11-09 11:30 | 0 | somente toques + artefatos de fluxo; fora dos toques: docs/sdd/plan/plan.md (odev 11:34:54) e docs/sdd de fluxo |
| 2026-11-09 13:54:23 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Posts.QaProbe0022 (sonda do cap, arquivo removido depois) | 1 | docs/sdd/runs/test-runs/20261109-134918-test-filter.log - PROBE raw=5000 stored=25000 |
| 2026-11-09 13:54:24 | qa | bash docs/sdd/_tools/sdd-test.sh test (gemeo Linux executado via Git Bash) | 1 | no *.sln encontrado em depth 3 (a solution e *.slnx); WSL so tem podman-machine e dotnet ausente - Linux unverified |
| 2026-11-09 13:54:54 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate de reconfirmacao na arvore limpa apos remover a sonda) | 0 | docs/sdd/runs/test-runs/20261109-135355-test-gate.log - exit: 0 - 266/266 lido no arquivo (S-22) |
| 2026-11-09 14:03:47 | odev | Select-String do gate de reconfirmacao + varredura QA-MUTATION em backend + sdd-state check | 0 | docs/sdd/runs/test-runs/20261109-135355-test-gate.log exit: 0 / Aprovado 266 / Com falha 0; residuos 0; check OK log 9 evidencias 20; QA-0022 presente |
