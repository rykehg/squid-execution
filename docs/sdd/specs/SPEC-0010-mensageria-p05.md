<!--
templateVersion: 1.0.0
-->
Specification: the resolved reading of a requirement, precise enough to cut into tasks and to test against.
Write responsibility: ODEV.

- status: ATIVO
- owner: odev
- lastVerified: 2026-11-11 23:33:16

# SPEC-0010 — Mensageria: thread com ordem total, participantes e admin, resposta e reação

- requirement: REQ-0001
- phase: P-05
- dependsOn: SPEC-0007 (sessão e ator), SPEC-0009 (pipeline de mídia da TASK-0027 — o anexo de mensagem reusa `POST /api/media` intacto)

Item do Sprint 4 do doc 05 (`#### Sprint 4 — Mensageria`): conversa 1-1 e em grupo, sem E2EE
(registrado). Não reentrevista o que o doc 05 §2.5/§12.2, o data-model adotado (§2.5.1, §2.7,
§2.14), os FR-030..FR-033, FR-057, FR-058 e o contrato HTTP §6/§9 já decidem.

Decisões humanas desta abertura (entrevista 2026-11-11, 5 rodadas / 13 questões, Henrique):
1. **Escopo** = Sprint 4 inteiro (7 itens + DoD) **+ anexos em mensagem** — 8 entregas (21:31:24,
   rodada 1; corte revisado e aprovado na rodada 4, 22:55:32).
2. **Ao vivo de mensagens NÃO decidido nesta fase** (21:31:24): o humano pediu só a resposta de
   custo — SSE reusando `/api/stream` é o mais barato (~1/3 de task), webhook out custa ~1–2
   entregas e duplicaria a fila que a P-06 constrói, Web Push custa ~1 task backend + front no
   P-08. Sem decisão: o DoD do Sprint 4 não exige ao vivo; **polling `?since=` cobre** (GAP-36).
3. **Estado de entrega** = `lastReadSeq` (21:31:24): "entregue" = mensagem persistida (200 com
   `seq`); "lido" = marcador por participante em `POST /api/threads/{id}/read`; sem estado por
   dispositivo, sem rota nova além do contrato §6 (FR-033).
4. **Anexo em mensagem** = reusar `POST /api/media` intacto (21:43:37): upload segue com magic
   bytes, EXIF→blurHash, SHA-256, Alt e cota 507 da P-04; a mensagem só referencia os hashes —
   emenda (b) ao contrato §6.
5. **Participantes podem ser adicionados depois da criação** (21:43:37) — rota nova na emenda (b).
6. **Whitelist de emoji em configuração da instância** (21:43:37): `Reactions:AllowedEmojis` com
   conjunto padrão; `Like` padrão 👍; fora da lista → `422 invalid_emoji`.
7. **Admin de grupo** (21:58:46 + 22:55:32): qualquer admin do grupo adiciona/remove/promove/
   demove; grupos têm mais de um admin, eleitos pelo admin original **e** admins eleitos também
   promovem outros admins; **sucessão do criador**: quando o criador sai, o admin mais antigo
   assume, e se não houver admin o membro mais antigo é promovido a admin/criador — o grupo nunca
   fica sem admin enquanto tiver participantes.
8. **Emendas aprovadas** (22:55:32): (a) data-model §2.7 ganha papéis de admin da thread;
   (b) contrato §6 ganha rota de adicionar participante, rotas de admin e campo de anexo na
   mensagem — originais mantidos sem reescrita (precedente S-27/P-03).
9. **Estado da thread = SQLite + ETag** (23:08:37): a ordem total mora no estado da thread
   serializado por ETag (regra da TASK-0003/0023), **não** em `ThreadGrain`/Orleans — ADR-1
   ("Orleans só na camada de objetos federados") vale, e thread não federa (§2.5.1).
10. **Rota de admin** = `POST`/`DELETE /api/threads/{id}/participants/{actorId}/admin`
    (23:08:37), dentro da emenda (b).

## Behaviour

### A. Núcleo da thread (FR-030 base, data-model §2.7 + emenda GAP-30)

- Migration SQL versionada e idempotente (família `0002_*.sql`, mesmo `SchemaMigrator`) cria
  `thread` `(id, kind, created_by, message_seq, created_at, version)` e `thread_participant`
  `(thread_id, actor_id, role, joined_at)` — liberação de migration para teste/dev/CI vale
  (gate 2026-11-09); produção segue gate humano.
- `POST /api/threads` (sessão + CSRF) cria `Direct` (exatamente 2 participantes) ou `Group`
  (2..50); o criador entra como participante e, em `Group`, como admin (`GAP-30`); `Id` segue a
  origem scheme+Host da emenda P-03.
- `GET /api/threads` devolve só as threads onde o ator é participante, com `messageSeq` e o
  próprio `lastReadSeq`.
- Ordem total: `MessageSeq` incrementado no estado da thread sob ETag (`GAP-29`) — dois envios
  concorrentes nunca recebem a mesma `seq`.

### B. Envio e leitura (FR-030/031, data-model §2.5.1)

- `POST /api/threads/{id}/messages` cria mensagem com `seq` alocada pelo estado da thread,
  `Content` ≤ 4000 sanitizada no servidor (escapar HTML + remover C0 — regra GAP-24 já
  adjudicada na P-04), `ReplyToId` aceito só a partir da TASK-0037.
- `GET /api/threads/{id}/messages?since=` devolve o histórico em ordem de `seq` com delta exato
  (`seq > since`, sem duplicata — §9.3) e `nextSince`.
- **404 idêntico** (byte a byte) para quem não é participante, em qualquer rota da thread — a
  resposta não revela existência nem a lista de participantes (FR-031, SC-005/SC-013).

### C. Participantes e admin (emendas GAP-30/31, decisões 5/7)

- `POST /api/threads/{id}/participants` adiciona participante a `Group` (teto 50): **só admin**
  (`403` para participante comum; `404` para não-participante); em `Direct` → `400`.
- `DELETE /api/threads/{id}/participants/{actorId}`: admin remove qualquer participante **exceto
  o criador** (`403`); qualquer participante remove a si mesmo (sair); **último participante não
  sai** → `409 last_participant` (novo código, família `last_owner` §9.11 — `GAP-38`).
- Removido perde acesso imediato (404 nas rotas da thread); histórico permanece para quem fica;
  participante adicionado depois enxerga a thread inteira (§2.7).
- `POST .../participants/{actorId}/admin` promove a admin e `DELETE` no mesmo caminho remove o
  papel: qualquer admin pode promover/demove (inclusive admins eleitos por outro admin); o
  **criador não é demovido nem removido por outro admin** enquanto participante.
- **Sucessão** (`GAP-32`): ao sair o criador, o admin mais antigo (`joined_at` menor) assume o
  papel de criador; se não houver admin, o membro mais antigo é promovido a admin e vira criador
  — invariante: `Group` com ≥1 participante tem ≥1 admin.

### D. Aviso de não-E2EE e marcação de leitura (FR-032/033, §6/§9.6)

- `POST /api/threads/{id}/ack-no-e2ee` registra o aceite do aviso por (ator, thread),
  idempotente; **envio sem aceite → `428 precondition_required`** (§9.6) — o gate entra nesta
  task (a TASK-0034 envia antes, sem o gate; dentro da fase cada entrega é funcional por si).
- O aviso em si é texto registrado no registro do aceite; **a exibição na UI fica no P-08** com o
  front (out of scope aqui — `GAP-37`).
- `POST /api/threads/{id}/read` marca `lastReadSeq` até `seq`: o marcador **só avança** (marcar
  valor menor não retrocede — `GAP-38`); `seq` acima do total da thread → `400`.
- Estado de entrega (FR-033): entregue = persistido (`200` + `seq`); lido = `lastReadSeq` de cada
  participante exposto em `GET /api/threads` e no envelope de mensagens, **visível só a
  participantes**; sem estado por dispositivo (`GAP-33`).

### E. Resposta (FR-057, §9.9)

- `replyToId` só aceita mensagem da **mesma thread**: outra thread ou inexistente →
  `422 invalid_reply_target`; a resposta exibe a referência e **não amplia a visibilidade** —
  não-participante continua recebendo 404 idêntico.

### F. Reação (FR-058, data-model §2.14, §9.10)

- `POST /api/threads/{id}/messages/{messageId}/reactions` com `{emoji}`: uma linha por
  (mensagem, ator, emoji) — repetir é idempotente; `DELETE ...?emoji=` remove (remover
  inexistente → 404).
- Emoji fora de `Reactions:AllowedEmojis` (config, `GAP-34`) → `422 invalid_emoji`; `Like`
  padrão 👍 sempre presente na lista.
- Contagem é projeção derivada (`counts.reactions`), nunca fonte; reações e contagens **só
  aparecem para participantes** — não-participante recebe 404 sem contagem (§9.10).

### G. Anexo em mensagem (decisão 4, emenda GAP-31)

- Corpo de `POST .../messages` ganha `contentHashes` (≤10, mesma forma do publish de post):
  hash inexistente → `400`; `Alt` obrigatório para `Image`/`Video`/`Gif` (regra §2.6 aplicada na
  publicação da mensagem); blob, magic bytes, EXIF e **cota 507** continuam no `POST /api/media`
  da P-04 — nada é cobrado duas vezes.
- `GET /api/media/{contentHash}` amplia a regra GAP-25: vê quem é uploader **ou** participa de
  thread cuja mensagem referencia o hash; não-participante continua 404.

## Inputs and outputs

| Direction | Name | Shape | Notes |
| --- | --- | --- | --- |
| in | `POST /api/threads` | `{kind: "direct"\|"group", participantActorIds: [...]}` | direct = 2, group = 2..50; criador automático |
| in | `POST .../messages` | `{content ≤4000, replyToId?, contentHashes?: [...]}` | sessão + CSRF; 428 sem aceite (a partir da TASK-0036) |
| in | `POST .../participants` | `{actorId}` | admin only, group only |
| in | `POST/DELETE .../participants/{actorId}/admin` | vazio | promover/remover papel de admin |
| in | `POST .../read` | `{seq}` | marcador monotônico |
| in | `POST .../reactions` | `{emoji}` | idempotente por (msg, ator, emoji) |
| out | `GET .../messages?since=` | `{items: [{id, seq, content, replyToId?, attachments?, reactions?}], nextSince}` | delta exato na ordem de `seq` |
| out | `GET /api/threads` | `{items: [{id, kind, participants, messageSeq, lastReadSeq}]}` | só threads do ator |
| out | erro | catálogo §8 + `invalid_reply_target`, `invalid_emoji`, `last_participant` (422/422/409) | 404 idêntico em rota de thread alheia |

## Rules and edge cases

- Ordem total sob concorrência: ETag do estado da thread serializa o incremento de `message_seq`
  — repetir envio concorrente nunca duplica `seq`.
- não-participante / thread inexistente / removido: **404 byte-idêntico** em toda rota da thread.
- último participante sai → `409 last_participant`; criador removido por admin → `403`;
  participante comum adiciona → `403`; `Direct` aceita adição → `400`.
- `replyToId` de outra thread → `422 invalid_reply_target`; emoji fora da lista →
  `422 invalid_emoji`; reação repetida → idempotente (mesma linha, contagem não muda).
- marcador de leitura regresso é ignorado (só avança); `seq` acima do total → `400`.
- vazio/conteúdo: `content` vazio → `400`; sanitização remove HTML/controles C0 (GAP-24).
- ator `Suspended`: regra do data-model §3.1 vale para envio de mensagem (mesma família do post)
  — negado e registrado.

## Non-functional

- performance: nenhuma meta numérica nova; paginação `since` usa índice `(thread_id, seq)`.
- security: toda rota exige sessão + CSRF; 404 sem vazamento (SC-005/SC-013); sem stack trace ou
  nome de tabela em erro (FR-053); mensagens protegidas em trânsito e em repouso, operador pode
  ler (aviso FR-032).
- platform: SQLite local; migration idempotente liberada só para teste/dev/CI.

## Out of scope (explicitly)

- E2EE de qualquer formato (I-03, fase 3); o corpo nasce payload opaco versionado (§2.5) sem cifra.
- Ao vivo de mensagens: SSE de thread, webhook para subscriber e Web Push — não decididos nesta
  fase (GAP-36); polling `?since=` cobre o DoD.
- UI do aviso de não-E2EE (front é P-08); aqui só a rota e o registro do aceite.
- Federação de mensagens (thread não federa, §2.5.1) — nada de `Activity`/projeção/feed para
  mensagem (GAP-39); inbox/outbox vêm na P-06.
- Edição/remoção de mensagem (sem rota no contrato §6); busca, notificação e typing indicator.
- Estado de entrega por dispositivo; marcador único por participante.

## Gaps carried from the requirement

| Gap | Status | Resolution or assumption in force |
| --- | --- | --- |
| GAP-29 | RESOLVED (HUMAN) | estado da thread em SQLite + ETag (decisão 9); `ThreadGrain`/Orleans rejeitado por ADR-1 |
| GAP-30 | RESOLVED (HUMAN) | emenda (a): data-model §2.7 ganha `CreatedBy` + papel admin por participante — original intacto, emenda viva aqui + `memory-bank/contracts.md` na consolidação |
| GAP-31 | RESOLVED (HUMAN) | emenda (b): contrato §6 ganha `POST .../participants`, `POST/DELETE .../participants/{actorId}/admin` e `contentHashes` no corpo da mensagem |
| GAP-32 | RESOLVED (HUMAN) | sucessão: admin mais antigo = menor `joined_at`; criador sai → assume; sem admin → membro mais antigo promovido a admin/criador |
| GAP-33 | RESOLVED (HUMAN) | estado de entrega = `lastReadSeq` por participante; entregue = persistido; sem estado por dispositivo |
| GAP-34 | RESOLVED (HUMAN) | whitelist em config `Reactions:AllowedEmojis` com defaults (👍 sempre incluído) |
| GAP-35 | RESOLVED (HUMAN) | anexo reusa `POST /api/media` da P-04; mensagem só referencia `contentHashes` |
| GAP-36 | ASSUMED | ao vivo fora do P-05 (custo respondido, humano não decidiu): polling `?since=` cobre o DoD; SSE/webhook/push viram ideia se a necessidade aparecer |
| GAP-37 | ASSUMED | aviso: rota `ack-no-e2ee` + registro por (ator, thread) nesta fase; exibição na UI entra na P-08 |
| GAP-38 | ASSUMED | `409 last_participant` (família `last_owner`); marcador de leitura monotônico; `seq` > total → 400 |
| GAP-39 | ASSUMED | mensagem não gera `Activity` nem projeção (thread não federa e não entra no feed) |
| GAP-40 | ASSUMED | participante adicionado depois vê o histórico inteiro (leitura de §2.7); `Direct` não aceita adição e sai de 2 para 1 com histórico legível pela parte restante |

## Tasks

| Task | Title | Parallel-safe | Status |
| --- | --- | --- | --- |
| TASK-0033 | Núcleo da thread: migration, store ETag, criar/listar | no — base da fase | TASK_READY |
| TASK-0034 | Envio e leitura com ordem total e 404 idêntico | no — serial | TASK_READY |
| TASK-0035 | Participantes e admin com sucessão do criador | no — serial | TASK_READY |
| TASK-0036 | Aviso não-E2EE (428) e marcação de leitura | no — serial | TASK_READY |
| TASK-0037 | Resposta à mensagem (`replyToId`) | no — serial | TASK_READY |
| TASK-0038 | Reação com whitelist e idempotência | no — serial | TASK_READY |
| TASK-0039 | Anexo em mensagem reusando `/api/media` | no — serial | TASK_READY |
| TASK-0040 | Facet de fecho do DoD do Sprint 4 | no — última | TASK_READY |
