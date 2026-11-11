<!--
templateVersion: 1.0.0
-->
Specification: the resolved reading of a requirement, precise enough to cut into tasks and to test against.
Write responsibility: ODEV.

- status: ATIVO
- owner: odev
- lastVerified: 2026-11-09 11:22:36

# SPEC-0009 — Publicação e feed: objeto, projeções, SSE, mídia e moderação de conteúdo

- requirement: REQ-0001
- phase: P-04
- dependsOn: SPEC-0008 (contêiner, membership, política versionada e `PolicyActor` servem toda leitura/escrita daqui), SPEC-0003 (motor de política), SPEC-0007 (sessão e ator)

Item do Sprint 3 do doc 05 (`#### Sprint 3 — Publicação e feed`): postar, ver e receber ao vivo.
Não reentrevista o que o doc 05 §4.2/§12.2, o data-model adotado (§2.5, §2.6, §2.8, §2.11, §2.13,
§3.1, §4, §5), os FR-020..FR-029 e o contrato HTTP §5/§7/§9 já decidem.

Decisões humanas desta abertura (entrevista 2026-11-09, 3 rodadas, Henrique):
1. **Escopo** = Sprint 3 inteiro (7 itens + DoD) **+** remoção (FR-027) **+** denúncia (FR-028)
   **+** cota (FR-029) — ~10 entregas.
2. **Migration liberada**: criar banco e migration do SQLite está liberado **para teste, dev e
   popular dados de teste** (script versionado no repositório); produção segue gate humano.
3. **Mídia** = `SkiaSharp` como pacote único de imagem (Apache-2.0), versão só em
   `Directory.Packages.props` (ADR-25); EXIF stripping e `blurHash` em código próprio.
4. **Ledger de cota** = SQL puro + Dapper na mesma migration versionada — emenda ao data-model
   §5 (`EF Core (migrations)` → `script SQL versionado + Dapper`; a razão do EF perde o motivo
   com a migration em script).
5. **Dapper** aprovado como pacote de leitura das projeções (data-model §4).

## Behaviour

### A. Publicação (FR-020 texto, data-model §2.5/§2.8)

- `POST /api/containers/{slug}/posts` (sessão + CSRF) publica `Kind: Post`: avalia `Posting` e
  `Visibility` com o contexto completo do ator (`PolicyActor`, P-03) e com a política **vigente**;
  negação grava `Deny` na trilha e responde `403` (existência pública) ou `404` (invisível) —
  regra do 404 byte-idêntica, SC-005/SC-013.
- `Content` ≤ 5000, sanitizado no servidor (GAP-24); `VisibilityAtPublish` **congelada** na
  publicação; `PolicyVersion` = a avaliada; `Id` = `{scheme}://{host}/o/{id}` (regra de origem da
  emenda P-03).
- `Object.Seq`: sequência **por contêiner**, incrementada no estado do contêiner (ETag serializa
  concorrência — duas postagens simultâneas nunca repetem seq). `GET /api/posts/{id}` devolve o
  post ou 404 idêntico quando invisível (FR-026).
- Toda publicação grava um evento `Activity` (`Type: Create`, `Payload` JSON cru, `Seq` global —
  GAP-23) em store append-only: é a fonte das projeções (R-18), do delta e do futuro outbox (P-06
  fica de fora aqui — o evento é gravado localmente, nada federado).
- Ator `Suspended` não publica (data-model §3.1) → negado e auditado.
- `POST /api/posts/{id}/replies`: comentário com `ReplyToId` dentro do mesmo contêiner,
  profundidade ≤ 3 (422 acima), mesma avaliação de política; `POST /api/posts/{id}/replies` em
  post invisível → 404 idêntico.

### B. Projeções e migration (FR-023 base, data-model §4/§5 — liberação humana 2026-11-09)

- Script SQL **versionado e idempotente** no repositório cria as tabelas `timeline_item`
  `(container_id, seq)` e `home_timeline` `(actor_id, seq)` com índice em `seq` + a tabela do
  contador global de `Activity.Seq` (GAP-23) + `quota_ledger` (seção H) — mesma migration, uma
  versão.
- Projetor: consome eventos `Activity` e escreve as linhas (post resumido, autor, contadores,
  visibilidade efetiva; e o item nas timelines dos membros) — **nunca no caminho de escrita**
  (R-18); função invocável diretamente nos testes + pump como hosted service no `Squid.Worker`.
  **Emenda do ODEV 2026-11-09 15:50:43**: os membros `Active` são lidos **no momento da
  projeção** (janela de segundos do pump), não no momento do evento — coerente com FR-026 e com
  §4 do data-model ("itens dos contêineres do ator" = estado corrente); o snapshot de membros no
  payload do evento fica como melhoria registrada (S-34).
- Consultas com Dapper em `Squid.ReadModels` (pacote aprovado); aplicação da migration no init
  de dev/teste/CI (liberado); qualquer aplicação em produção = gate humano.

### C. Feed e timeline (FR-023, FR-025, FR-026, SC-004)

- `GET /api/containers/{slug}/feed?since=` devolve a página cronológica do contêiner (mais
  recente primeiro) e, com `since`, **exatamente** o delta: só `seq > since`, sem duplicata, na
  ordem de `seq` (§9.3).
- `GET /api/timeline?since=` devolve a timeline pessoal (`home_timeline` dos contêineres em que
  o ator tem membership `Active`) com a mesma semântica de delta.
  **Clarificações do ODEV 2026-11-09 20:17:13** (adjudicação dos achados da QA-0024):
  (a) **sem backfill** — a timeline pessoal acumula os itens projetados a partir de a membership
  ficar `Active`; quem entra num contêiner depois não recebe os itens anteriores na timeline
  pessoal (o **feed do contêiner** continua com o histórico completo, que é onde o passado é
  consultado); "itens dos contêineres do ator" no data-model §4 descreve o conteúdo da tabela,
  não manda retroagir — backfill vira melhoria registrada (S-35);
  (b) o filtro de membership exige `Active`: `Silenced` fica **fora da timeline pessoal** e
  continua **lendo o contêiner** normalmente (§3.3 e AC-05 escopam "ler" à leitura do contêiner).
- **Toda leitura aplica a política vigente e a membership corrente (FR-026)**: contêiner
  `Private`/`MembersOnly` não serve itens a não-membro; membro `Banned` não lê (404); a
  visibilidade lida é a corrente, não a da publicação (edge case "política alterada").
- Sem ranqueamento: ordem pura por sequência (FR-023).

### D. SSE ao vivo (FR-024, R-16, contrato §5)

- `GET /api/stream` autenticado: `event: post` com `id` = `Activity.Seq` global e `data` com o
  resumo do item; canais em processo (`Channel<T>` + `IAsyncEnumerable`), sem broker.
- `Last-Event-ID` reconecta entregando só o delta posterior (§9.4); heartbeat `event: ping` a
  cada 15 s; a entrega filtra por visibilidade do assinante (membro ativo do contêiner).
- Façaz de fecho: post em comunidade visível chega ao assinante conectado em < 1 s (DoD).

### E. Mídia: upload (FR-020/021/022, contrato §5, decisão SkiaSharp)

- `POST /api/media` multipart com `file` + `alt`: `alt` **obrigatório e não vazio** para
  `Image|Video|Gif` (FR-022) — sem ele `400`; magic bytes decidem o tipo (extensão não confia) —
  executável disfarçado → `415` (§9.5); limite por tipo (`413`); blob **content-addressed**
  (SHA-256, volume nomeado, data-model §5).
- Pipeline de entrada, nesta ordem (invariante §2.6): decodifica (SkiaSharp) → **remove EXIF**
  (parser próprio de segmentos JPEG/TIFF) → grava blob sem metadados → calcula `width/height` e
  **`blurHash`** (encoder próprio, obrigatório para `Image`/`Gif`). Resposta do contrato:
  `{ contentHash, mimeType, width, height, durationSeconds, blurHash, sizeBytes }`.
- `durationSeconds`: só MP4/MOV (parser do box `mvhd`), demais tipos `null` (GAP-22).
- `Attachment` entra no post pela lista de `contentHash` (≤ 10) no `POST /posts`; publicar
  `Image|Video|Gif` sem `Alt` → `400` (rejeição na publicação, não só na UI — §2.6).
- `GET /api/media/{contentHash}` serve o blob: sessão obrigatória + "vê ao menos um post que
  referencia o hash, ou é o uploader" (GAP-25).

### F. Cota (FR-029, data-model §2.13, decisões 2 e 4)

- `quota_ledger` com escopo instância: `UsedBytes` (inteiro), `LimitBytes` (default 10 GiB),
  `UpdatedAt`. Upload soma no caminho otimista; exceder → `507 quota_exceeded`.
- Reconciliação diária por varredura dos blobs (função invocável em teste + hosted service) — a
  varredura é a fonte da verdade, o incremento é só o gate rápido.

### G. Link unfurl (Sprint 3 "texto, link")

- Post cujo conteúdo é uma URL ganha `Attachment.Kind: Link` com metadados OpenGraph
  (`og:title`, `og:description`, `og:image`): fetch **síncrono com timeout 3 s, fail-open**
  (falha = posta só com a URL) e guarda **SSRF** (GAP-26): só `http/https`, sem IP
  privado/loopback/link-local/CGNAT, resposta ≤ 1 MB, redirecionamentos re-validados.
- Testes usam handler falso — nenhum teste do gate toca rede real.

### H. Remoção (FR-027, contrato §5/§8)

- `DELETE /api/posts/{id}`: autor ou `Moderator`+; **soft delete com tombstone** (campos
  `DeletedAt` + `TakedownReason` obrigatório quando `Moderator`+ — R-07); grava `RemoveContent`
  na trilha com autor/quando/policyVersion; evento `Activity: Delete` atualiza as projeções —
  post removido não aparece em feed, timeline, `?since` nem em `GET /post` (404 idêntico).
- Quem não podia ver o post antes continua respondendo 404 depois (não descobre existência).

### I. Denúncia e resolução (FR-028, FR-008, contrato §5/§7)

- `POST /api/posts/{id}/report` (sessão): motivo obrigatório; idempotente por `(post, ator)` —
  repetir devolve a mesma denúncia (GAP-28).
- `GET /api/admin/reports` (operador) fila paginada; `POST /api/admin/reports/{id}/resolve`
  com `action: keep|remove|ban` + `justification` obrigatória (`422` vazia):
  `keep` fecha auditado; `remove` executa a remoção da seção H em nome do operador; `ban`
  **suspende o ator** (`POST /api/admin/actors/{id}/suspend` nasce aqui — `Actor.Status`
  `Suspended` de §3.1; suspenso não publica, regra já ativa desde a A) — tudo auditado na trilha
  com a palavra `Resolve` (GAP-27) e autor/alvo/justificativa (FR-008).
- `reinstate` fica fora: não existe rota no contrato (GAP-27) — suspensão desta fase é
  irreversível por API e isso fica registrado.

### J. Thumbnail e load test (Sprint 3, SC-004)

- Worker gera thumbnail das imagens/GIF (SkiaSharp) a partir do blob já sem EXIF; vídeo/áudio
  não decodificam (GAP-21) → sem thumbnail, serve placeholder.
- Load test: com 10k itens em `timeline_item`, `GET feed`/`timeline` primeira página
  P95 < 1 s (SC-004) e P50/P99 reportados pelo teste — executado pelo gate, sem rede.

## Inputs and outputs

| Direction | Name | Shape | Notes |
| --- | --- | --- | --- |
| in | post body | `content` ≤5000 + `contentHashes` ≤10 | sanitizado no servidor |
| in | `since` | inteiro (seq) | delta estrito `> since` |
| in | `Last-Event-ID` | inteiro (Activity.Seq) | reconexão SSE |
| in | multipart `file`+`alt` | blob + texto | alt obrigatório p/ image/video/gif |
| in | report | `reason` não vazio | idempotente por (post, ator) |
| in | resolve | `action` keep/remove/ban + `justification` | vazia → 422 |
| out | `PostView` | id, content, author, containerId, seq, createdAt, attachments, counts | 404 idêntico quando invisível |
| out | feed/timeline page | itens ordenados por seq + delta de `since` | sem ranqueamento (FR-023) |
| out | SSE frame | `event: post`, `id: seq`, resumo JSON | ping 15 s |
| out | media response | `{ contentHash, mimeType, width, height, durationSeconds, blurHash, sizeBytes }` | contrato §5 |

## Rules and edge cases

- `since` idempotente e exato: sem duplicata, na ordem, itens posteriores apenas (§9.3).
- Post invisível e post inexistente: mesmo corpo de erro, sem traceId (SC-005).
- `VisibilityAtPublish` pode ficar **mais restritiva** na leitura, nunca mais permissiva; leitura
  usa a política corrente (§4).
- Duas postagens concorrentes no mesmo contêiner: ETag do contêiner serializa — seqs únicas.
- Profundidade > 3 → `422 invalid_reply_target`? Não — §9.9 define `422 invalid_reply_target`
  para alvo de outra thread (mensageria); para profundidade de post adota-se `422` com código
  `invalid_reply_target` (assumido, ver gaps).
- Upload: magic bytes ≠ tipo declarado → `415`; `alt` vazio em imagem → `400`; acima do limite
  → `413`; cota estourada → `507`; nenhuma resposta vaza caminho interno (§9.7).
- EXIF **antes** de blurHash e thumbnail; blob gravado nunca com metadados de localização (FR-021).
- Unfurl: URL privada/loopback negada sem fetch; timeout 3 s; falha não bloqueia a publicação.
- Denúncia/resolução: `justification` vazia → `422`; sem sessão → 401; não-operador nas rotas
  admin → 403 (guard já existente da P-03).
- Ator suspenso: publicação negada e auditada; leitura segue permitida (§3.1).
- Post removido: some de toda leitura; a linha da auditoria nunca é apagada (§2.5).

## Non-functional

- performance: SC-004 — primeira página do feed de 10k posts < 1 s (P95) com P50/P99 medidos;
  delta de `since` sem varredura do histórico todo (índice em `seq`); SSE < 1 s (DoD).
- security: SC-005/SC-013 (0 vazamento de conteúdo restrito, inclusive preview/contagem),
  SC-015 (sem PII em telemetria), SSRF no unfurl, magic bytes no upload, sanitização do conteúdo,
  mídia com sessão + checagem de visibilidade, cota `507`.
- platform: `Squid.Domain` puro (sem I/O, SQUID0001); rotas em `Squid.Api`; stores SQLite +
  migration em `Squid.Persistence`/`Squid.Service`; leituras Dapper em `Squid.ReadModels`;
  `SkiaSharp` e `Dapper` com versão só em `Directory.Packages.props` (ADR-25, gates de
  2026-11-09); migration aplicada em dev/teste/CI é liberada — produção é gate humano.

## Out of scope (explicitly)

- Reações a posts (`POST /api/posts/{id}/reactions`, FR-058) — sem handler até a fase dona.
- `reinstate` de ator suspenso (sem rota no contrato); gestão de contas (FR-009) — fases donas.
- Federação/outbox: o `Activity` é gravado localmente, mas nada sai da instância (P-06).
- Notificações (projeção `notification`), cartões `actor_card`/`container_card` (fases donas),
  busca FTS (I-07), preview social (P-08).
- Relações follow/accept além do que a denúncia usa; e-mail; front (P-08).
- Qualquer arquivo fora dos `touches` das tasks listadas.

## Gaps carried from the requirement

| Gap | Status | Resolution or assumption in force |
| --- | --- | --- |
| GAP-19 | RESOLVED (HUMAN) | migration + criação de banco do SQLite liberados para teste/dev/CI com script versionado (decisão 2026-11-09); produção = gate humano |
| GAP-20 | RESOLVED (HUMAN) | ledger de cota em SQL + Dapper na mesma migration — emenda ao data-model §5 (EF Core → script versionado) |
| GAP-21 | ASSUMED | thumbnail só de `Image`/`Gif`; vídeo/áudio recebem placeholder (SkiaSharp não decodifica vídeo) |
| GAP-22 | ASSUMED | `durationSeconds` só para MP4/MOV (box `mvhd`); demais tipos `null` |
| GAP-23 | ASSUMED | duas sequências: `Object.Seq` por contêiner (incremento no estado, ETag) e `Activity.Seq` global (contador na migration) usada por timeline e `id` do SSE |
| GAP-24 | ASSUMED | sanitização = escapar HTML + remover controles C0 no servidor; o front escapa de novo |
| GAP-25 | ASSUMED | `GET /api/media/{contentHash}` (rota não listada no contrato §5): sessão + "vê algum post que referencia o hash ou é o uploader" |
| GAP-26 | ASSUMED | unfurl síncrono, timeout 3 s, fail-open, guarda SSRF (sem IP privado/loopback/link-local/CGNAT, só http/https, ≤ 1 MB, redirecionamentos re-validados) |
| GAP-27 | ASSUMED | palavra `Resolve` na trilha (mesma família das emendas Successor/Transfer/Delete já aceitas); `reinstate` fora por falta de rota |
| GAP-28 | ASSUMED | denúncia idempotente por `(post, ator)` — repetição devolve a mesma (200) |
| GAP-13..18 | ASSUMED | como estavam (SPEC-0008) |

## Tasks

| Task | Title | Parallel-safe | Status |
| --- | --- | --- | --- |
| TASK-0023 | Núcleo: `POST /posts` (texto) + `GET /post` + replies ≤3 + `Object.Seq` + evento `Activity` + suspenso negado | no — abre `Squid.Api/Posts/**` e as stores, base da fase | TASK_READY |
| TASK-0024 | Migration versionada + projeções Dapper (`timeline_item`, `home_timeline`) + pump no Worker | no — serial (mesmos arquivos de composição) | TASK_READY |
| TASK-0025 | Feed e timeline com `?since=` delta + visibilidade corrente (FR-023/025/026) | no — serial | TASK_READY |
| TASK-0026 | SSE `GET /api/stream` (Channel, `Last-Event-ID`, ping 15 s) | no — serial | TASK_READY |
| TASK-0027 | Upload `POST /api/media` (SkiaSharp, EXIF, blurHash, 415/413, blob content-addressed) + `GET /api/media/{hash}` + anexos no post | no — serial | TASK_READY |
| TASK-0028 | Cota: `quota_ledger` na migration, `507`, reconciliação diária invocável | no — serial | TASK_READY |
| TASK-0029 | Link unfurl (OpenGraph, timeout 3 s, guarda SSRF, fail-open) | no — serial | TASK_READY |
| TASK-0030 | Remoção `DELETE /posts` (tombstone + auditoria + projeção) — FR-027 | no — serial | TASK_READY |
| TASK-0031 | Denúncia + fila admin + resolve (keep/remove/ban) + `suspend` — FR-028/FR-008 | no — serial | TASK_READY |
| TASK-0032 | Thumbnail no Worker + load test P50/P99 (SC-004) + facet de fecho do DoD do Sprint 3 | no — serial; última | TASK_READY |
