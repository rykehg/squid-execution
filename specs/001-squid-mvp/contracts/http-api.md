# Contract: HTTP API (cliente web/mobile)

**Feature**: `001-squid-mvp` | **Versão**: v1 | **Base**: `/api`

Superfície REST/JSON vista pelo cliente próprio. A superfície federada está em
[`activitypub.md`](./activitypub.md) e é **separada** desta — o cliente nunca fala JSON-LD.

## 1. Convenções

- JSON em UTF-8; `Content-Type: application/json`; datas em ISO-8601 UTC (`2026-11-02T23:30:00Z`).
- Identidade de recurso é URI canônica quando pública (`id`); IDs opacos em rotas internas.
- Erros sempre no mesmo envelope, **sem** detalhe interno (FR-053):

```json
{ "error": { "code": "policy_denied", "message": "Ação não permitida.", "traceId": "00-..." } }
```

- **Regra do 404**: quando revelar a existência do recurso já viola a visibilidade, a resposta é
  `404 not_found` — nunca `403` com o nome do contêiner, contagem ou preview (FR-018, SC-005).
- **Idempotência**: `POST` de publicação aceita `Idempotency-Key`; repetição com a mesma chave
  devolve a primeira resposta.
- **Paginação**: `?limit` (padrão 20, máx 100) + `?since=<seq>` para delta (FR-025); a resposta traz `nextSince` e `hasMore`; `before=<seq>` para histórico. `since_id` e `max_id` são aceitos como sinônimos de `since`/`before`, e toda coleção paginada devolve `Link` com `rel="next"` (e `rel="prev"` no histórico) — quem só entende `Link` navega sem conhecer o nome do cursor (padrão `Api::Pagination` do Mastodon, D4-a).

## 2. Autenticação e sessão

| Método | Rota | Descrição |
|--------|------|-----------|
| POST | `/api/auth/register` | Cria ator local; devolve sessão + kit uma única vez |
| POST | `/api/auth/login` | Senha (Argon2id) → cookie `HttpOnly` + CSRF token |
| POST | `/api/auth/logout` | Revoga a sessão atual |
| POST | `/api/auth/logout-all` | Revoga todas as sessões do ator |
| GET | `/api/auth/me` | Ator autenticado, papéis e avisos pendentes (ex.: kit não verificado) |

Cookie: `squid_session`, `HttpOnly`, `Secure`, `SameSite=Lax`, 30 dias deslizantes (R-03).
Mutações exigem `X-CSRF-Token` igual ao cookie `squid_csrf` (dupla submissão, R-09).

Não há e-mail, telefone ou reset de senha por e-mail no MVP: a recuperação de identidade é o kit
(FR-005/FR-062). Passkey (fase 2) e OIDC de instância (fase 3) são métodos adicionais reservados —
nunca substituem o kit.

## 3. Identidade e recuperação

| Método | Rota | Descrição |
|--------|------|-----------|
| GET | `/api/actors/{handle}` | Perfil público (sem sessão), com chaves públicas |
| POST | `/api/actors/{handle}/follow` | Segue ator local ou remoto (resolve handle e envia `Follow`) |
| POST | `/api/actors/{handle}/unfollow` | Envia `Undo(Follow)` |
| PATCH | `/api/actors/me` | Atualiza perfil (`displayName`, `summary`, `avatarHash`, `headerHash`); `handle` imutável → `422` |
| POST | `/api/actors/me/keys/rotate` | Inicia rotação de época de chave (fora do MVP core, contrato reservado) |
| POST | `/api/recovery/kit` | Gera kit; resposta contém mnemônico **uma vez** |
| POST | `/api/recovery/kit/verify` | Marca kit como verificado (usuário provou que guardou) |
| POST | `/api/recovery/import` | Importa kit em instância limpa; restaura ator e chaves |
| DELETE | `/api/actors/me` | Exclusão de conta com aviso de cópias federadas (FR-009) |

```json
POST /api/recovery/kit
→ 201 { "recoveryCredential": "RC-XXXX-...", "mnemonic": ["24", "palavras", "..."],
         "encryptedRootKey": "base64...", "createdAt": "..." }
```

`mnemonic` nunca é recuperável depois desta resposta; a `RK` em claro nunca aparece em resposta
(FR-004).

## 4. Contêineres e política

| Método | Rota | Descrição |
|--------|------|-----------|
| POST | `/api/containers` | Cria com `kind: group\|community` (presets aplicados) |
| GET | `/api/containers/{slug}` | Contêiner + política vigente (se visível) |
| PATCH | `/api/containers/{slug}` | Nome/summary/listed |
| PUT | `/api/containers/{slug}/policy` | Substitui política (exige `If-Match: <version>`) |
| GET | `/api/containers/{slug}/members` | Membros paginados (filtrado por papel do leitor) |
| POST | `/api/containers/{slug}/invites` | Cria convite (papel `Admin`+) |
| POST | `/api/containers/{slug}/join` | Entra (open) ou solicita (request) |
| POST | `/api/containers/{slug}/leave` | Sai |
| PATCH | `/api/containers/{slug}/members/{actorId}` | Muda papel/estado (auditado) |
| DELETE | `/api/containers/{slug}` | Exclui (Owner; confirmação obrigatória) |
| POST | `/api/containers/{slug}/transfer` | Transfere propriedade |
| PUT | `/api/containers/{slug}/successor` | Nomeia sucessor (Owner) |
| POST | `/api/containers/{slug}/claim` | Sucessor assume após inatividade do Owner (auditado) |
| GET | `/api/containers/search?q=` | Busca comunidades listadas (R-10) |

Política no fio:

```json
{ "posting": "singlePublisher", "publisherIds": ["https://d/a/alice"],
  "visibility": "membersOnly", "join": "invite",
  "interaction": { "canComment": "members", "canReact": "members",
                   "canShare": "members", "canAttach": "members" },
  "version": 7 }
```

`PUT` com versão defasada → `409 version_conflict` com a versão atual (edge case de edição
concorrente).

## 5. Publicação, feed e mídia

| Método | Rota | Descrição |
|--------|------|-----------|
| POST | `/api/media` | Upload `multipart`; devolve `contentHash` + metadados |
| POST | `/api/containers/{slug}/posts` | Publica em contêiner |
| POST | `/api/posts/{id}/replies` | Comentário (profundidade ≤ 3) |
| GET | `/api/posts/{id}` | Post (404 se não visível) |
| DELETE | `/api/posts/{id}` | Autor ou `Moderator`+; tombstone federado |
| POST | `/api/posts/{id}/reactions` | Reagir (conforme política) |
| GET | `/api/containers/{slug}/feed?since=` | Feed do contêiner |
| GET | `/api/timeline?since=` | Timeline pessoal |
| GET | `/api/stream` | **SSE** de eventos do ator autenticado |
| POST | `/api/posts/{id}/report` | Denúncia (FR-028) |

Upload (`POST /api/media`): campos `file`, `alt` (obrigatório para imagem/vídeo/gif — FR-022).
Resposta: `{ contentHash, mimeType, width, height, durationSeconds, blurHash, sizeBytes }`.
Rejeições: `415 unsupported_media_type` (magic bytes), `413 payload_too_large`, `507 quota_exceeded`.

SSE (`GET /api/stream`):

```text
event: post
id: 1042
data: {"seq":1042,"containerId":"https://d/c/rust","author":{...},"content":"..."}
```

`Last-Event-ID` reconecta do delta (R-16); heartbeat `event: ping` a cada 15 s.

## 6. Mensageria

| Método | Rota | Descrição |
|--------|------|-----------|
| POST | `/api/threads` | Cria conversa direct/group |
| GET | `/api/threads` | Lista conversas do ator |
| GET | `/api/threads/{id}/messages?since=` | Histórico paginado (404 para não participante — FR-031) |
| POST | `/api/threads/{id}/messages` | Envia mensagem; `replyToId` opcional (mesma thread) |
| POST | `/api/threads/{id}/messages/{messageId}/reactions` | Reage (`{emoji}`, idempotente) |
| DELETE | `/api/threads/{id}/messages/{messageId}/reactions?emoji=` | Remove a reação |
| POST | `/api/threads/{id}/read` | Marca leitura até `seq` |
| DELETE | `/api/threads/{id}/participants/{actorId}` | Remove participante (perde acesso imediato) |

Obrigatório antes da primeira mensagem: `POST /api/threads/{id}/ack-no-e2ee` — registra o aceite do
aviso (FR-032). Sem aceite, o envio responde `428 precondition_required`.

```json
POST /api/threads/{id}/messages
{ "content": "fechado então", "replyToId": "https://dominio/m/01H..." }

GET /api/threads/{id}/messages?since=0
{ "items": [
    { "id": "https://dominio/m/01J...", "content": "fechado então",
      "replyToId": "https://dominio/m/01H...",
      "reactions": [ { "emoji": "👍", "count": 2, "mine": true } ] } ],
  "nextSince": 42 }
```

`replyToId` só aceita mensagem da mesma thread (`422 invalid_reply_target` caso contrário); lista de
`reactions` só existe para participante; emoji fora da whitelist → `422 invalid_emoji`.

## 7. Administração da instância

| Método | Rota | Descrição |
|--------|------|-----------|
| GET | `/api/admin/reports` | Fila de denúncias (R-07) |
| POST | `/api/admin/reports/{id}/resolve` | Ação: manter, remover, banir (com justificativa) |
| POST | `/api/admin/actors/{id}/suspend` | Suspender ator |
| POST | `/api/admin/containers/{slug}/transfer` | Operador força transferência, com justificativa (auditado) |
| POST | `/api/admin/peers/{domain}/block` | Bloquear par |
| GET | `/api/admin/audit?actor=&action=&from=` | Consulta de auditoria (SC-011) |
| GET | `/api/admin/backup/status` | Estado de redundância (FR-050) |
| POST | `/api/admin/backup/run` | Dispara backup |
| POST | `/api/admin/restore/dry-run` | Valida envelope sem restaurar |
| GET | `/health`, `/ready` | Liveness e prontidão (FR-051) |

Toda rota `/api/admin/*` exige papel global de operador e grava `PolicyDecision`/ação administrativa
com autor e justificativa.

## 8. Catálogo de erros

| HTTP | `code` | Quando |
|------|--------|--------|
| 400 | `validation_failed` | Payload inválido |
| 401 | `not_authenticated` | Sem sessão válida |
| 403 | `policy_denied` | Autenticado, mas política nega — só quando a existência do recurso já é pública |
| 404 | `not_found` | Inexistente **ou** existência protegida (regra do 404) |
| 409 | `version_conflict` | Política/papel alterado concorrentemente |
| 413 / 415 | `payload_too_large` / `unsupported_media_type` | Upload |
| 428 | `precondition_required` | Aceite de aviso de E2EE ausente |
| 429 | `rate_limited` | Limite por ator/IP |
| 507 | `quota_exceeded` | Cota de mídia (R-06) |
| 503 | `degraded` | Dependência indisponível, com `Retry-After` |

## 9. Testes de contrato obrigatórios

1. Recurso restrito nunca retorna `403` com metadado — sempre `404` sem vazamento (SC-005, SC-013).
2. `PUT /policy` com versão antiga falha e não altera nada.
3. `?since=` devolve exatamente o delta, sem duplicata e na ordem de `seq`.
4. SSE reconectado com `Last-Event-ID` não repete item já entregue.
5. Upload com conteúdo executável disfarçado de imagem retorna `415`.
6. `POST /messages` sem aceite de aviso retorna `428`.
7. Nenhuma resposta de erro contém stack trace, caminho interno ou nome de tabela (FR-053).
8. `since_id`/`max_id` e o `Link` devolvem a mesma página que `since`/`before`; navegar só por `Link` não perde nem repete item.
9. Resposta com `replyToId` de outra thread → `422 invalid_reply_target`; resposta válida não muda a visibilidade da mensagem.
10. Reação repetida do mesmo ator é idempotente; não participante recebe `404` sem contagem; emoji fora da whitelist → `422 invalid_emoji`.
11. Saída/remoção do último Owner → `409 last_owner`; exclusão de conta com contêiner → `409 container_transfer_required`.
12. `POST /claim` antes de 90 dias → `403`; override do operador sem justificativa → `422`.
13. `PATCH /api/actors/me` altera perfil e federa `Update`; tentativa de mudar `handle` → `422`.

## 10. Reservado — bots e automação (feature 002)

> **Não implementado no MVP.** Esta seção existe para o contrato não quebrar quando a feature
> [`002-bots-e-automacao`](../../002-bots-e-automacao/spec.md) entrar. Nada aqui é aceito hoje: as
> rotas reservadas respondem `404`.

- **Autenticação de bot**: `Authorization: Bearer squid_pat_<id>_<segredo>` — token por ator, escopos,
  revogável, rotação com sobreposição de 24 h. Token nunca vai em cookie nem em query string.
- **Ator**: bot é ator `Service` com chaves próprias e rótulo público "BOT"; não é conta de humano.
- **Rotas reservadas** (v0.1 em
  [`002-bots-e-automacao/contracts/bot-api.md`](../../002-bots-e-automacao/contracts/bot-api.md)):
  `POST /api/bot/messages`, `GET /api/bot/updates`, `POST /api/bot/webhooks`,
  `DELETE /api/bot/webhooks/{id}`, `GET /api/bot/me`.
- **Erros e limites**: mesmo envelope do §1; `401 not_authenticated`, `403 insufficient_scope`,
  `429 rate_limited` com `Retry-After`. Limite inicial 60 req/min por bot (burst 10).
- **Compatibilidade**: um bot é um cliente normal desta API com escopo — nenhuma rota existente muda.