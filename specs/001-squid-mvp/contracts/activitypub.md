# Contract: Federação (ActivityPub)

**Feature**: `001-squid-mvp` | **Adapter**: `ActivityPubAdapter` (`Squid.Protocols`) | **Versão**: v1

Este contrato descreve **o que a instância expõe e consome** no protocolo federado. Nada aqui entra
no domínio: JSON-LD fica na borda (`IFederationProtocol`, 5 métodos — ADR-11).

## 1. Superfície exposta

| Recurso | Rota | Conteúdo |
|---------|------|----------|
| Descoberta | `GET /.well-known/webfinger?resource=acct:alice@dominio` | `subject`, `links` (`self` → ator, `profile`) |
| Descoberta | `GET /.well-known/nodeinfo` | Metadados de software/versão |
| Ator | `GET /users/{name}` | `Person` (humano) ou `Service` (bot) com `publicKey` (objeto RSA `#main-key`) e `squid:ed25519Key` (R-02), `inbox`, `outbox`, `followers`, `following` |
| Inbox | `POST /users/{name}/inbox` | Recepção assinada |
| Outbox | `GET /users/{name}/outbox` | `OrderedCollection` paginada |
| Seguidores | `GET /users/{name}/followers` | `OrderedCollection` paginada |
| Objeto | `GET /objects/{id}` | Publicação em JSON-LD (com `@context`) |
| Contêiner | `GET /c/{slug}` | `Group` estendido (`squid:kind`, `squid:policy`, `squid:slug` — R-04) |
| Contêiner | `POST /c/{slug}/inbox` | Inbox compartilhada do contêiner |
| Contêiner | `GET /c/{slug}/outbox` | Primeira página + `first`/`next` (**nunca** o feed inteiro) |
| Contêiner | `GET /c/{slug}/members` | `OrderedCollection` paginada, respeitando visibilidade |
| Mídia | `GET /media/{hash}` | Blob content-addressed, com `Cache-Control` longo e ETag |

### 1.1 Documento de ator (formato mínimo)

```json
{
  "@context": [
    "https://www.w3.org/ns/activitystreams",
    { "squid": "https://dominio/ns/squid#" }
  ],
  "id": "https://dominio/users/alice",
  "type": "Person",
  "preferredUsername": "alice",
  "name": "Alice",
  "inbox": "https://dominio/users/alice/inbox",
  "outbox": "https://dominio/users/alice/outbox",
  "followers": "https://dominio/users/alice/followers",
  "publicKey": {
    "id": "https://dominio/users/alice#main-key",
    "owner": "https://dominio/users/alice",
    "publicKeyPem": "-----BEGIN PUBLIC KEY-----..."
  },
  "squid:ed25519Key": {
    "id": "https://dominio/users/alice#ed25519",
    "publicKeyMultibase": "z6Mk..."
  }
}
```

`publicKey` é **objeto único** (RSA) e o `keyId` de toda assinatura aponta para `#main-key`. Um array
é aceito pelo Mastodon de referência (até 10 chaves), mas Friendica e versões anteriores do Mastodon
esperam objeto; o formato único é o denominador comum. O `@context` traz as definições `squid:`
inline, para o par não precisar buscar um contexto remoto. A Ed25519 é chave adicional, usada só
quando o par fala RFC 9421 (R-02).

### 1.2 Contêiner (extensão)

```json
{
  "id": "https://dominio/c/rust",
  "type": "Group",
  "name": "Rust Brasil",
  "squid:kind": "Community",
  "squid:slug": "rust",
  "squid:policy": {
    "posting": "members", "visibility": "public", "join": "open",
    "interaction": { "canComment": "members", "canReact": "members",
                     "canShare": "public", "canAttach": "members" },
    "version": 3
  },
  "squid:capabilities": ["post", "reply", "react", "share"],
  "inbox": "https://dominio/c/rust/inbox",
  "outbox": "https://dominio/c/rust/outbox",
  "members": "https://dominio/c/rust/members"
}
```

> **Nota (D4-e, fora do MVP)**: FEP-1b12 — contêiner como ator `Group` que recebe posts na própria
> inbox e faz `Announce` para os seguidores — é o que faz o feed de comunidade aparecer agregado no
> Mastodon. O MVP expõe contêiner como `Group` com inbox/outbox, mas **não** assina `Announce` como
> ator de grupo; a lacuna fica registrada aqui e não vira requisito (E3).

## 2. Atividades suportadas

| Tipo | Direção | Semântica no domínio |
|------|---------|----------------------|
| `Create` | ambas | Publica objeto (`Object`/`Message`) |
| `Update` | ambas | Edita conteúdo; **nunca** muda `Seq` |
| `Delete` | ambas | Tombstone; remoção local + propagação (R-08) |
| `Follow` / `Accept` | ambas | Relação de seguidor |
| `Undo` | ambas | Desfaz `Follow`, reação |
| `Announce` | ambas | Compartilhamento (boost) respeitando `canShare` |
| `Like` | ambas | Reação |
| `Join` / `Leave` | ambas | Vínculo com contêiner |
| `Block` | saída | Bloqueio de ator/par |
| `Flag` | entrada | Denúncia federada → fila de moderação (R-07) |

Atividade desconhecida **não** é erro: responde `202` e incrementa métrica `federation.unknown_activity`.

**Reação e resposta:** a reação padrão é `Like`, idempotente por `(actor, object)`; emoji customizado
usa extensão (`EmojiReact`/`squid:reaction`), que o Mastodon ignora — não é requisito de interop.
Resposta é `inReplyTo` no objeto. Conversa (`Thread`) **não** tem representação AP e não federa
(R-11); reação de mensagem é sempre local.

### 2.1 Formato mínimo de uma atividade (interop)

```json
{
  "@context": "https://www.w3.org/ns/activitystreams",
  "id": "https://dominio/activities/019abc",
  "type": "Create",
  "actor": "https://dominio/users/alice",
  "published": "2026-11-03T12:00:00Z",
  "to": ["https://www.w3.org/ns/activitystreams#Public"],
  "cc": ["https://dominio/users/alice/followers"],
  "object": {
    "id": "https://dominio/o/019abc",
    "type": "Note",
    "attributedTo": "https://dominio/users/alice",
    "content": "<p>texto</p>",
    "contentMap": { "pt": "<p>texto</p>" },
    "published": "2026-11-03T12:00:00Z",
    "to": ["https://www.w3.org/ns/activitystreams#Public"],
    "cc": ["https://dominio/users/alice/followers"]
  }
}
```

`to`/`cc` refletem a visibilidade (tabela §4): `public` põe `Public` em `to` e `followers` em `cc`;
`unlisted` põe `followers` em `to`; `membersOnly` endereça cada ator membro ativo em `to` e nada em
`cc`. `content` em HTML sanitizado, `published` em UTC e `attributedTo` sempre presente — é o que o
Mastodon exibe.

## 3. Verificação de entrada (ordem obrigatória)

1. **Assinatura** presente e verificável: `Signature` draft-cavage com `rsa-sha256`/`hs2019` (SHA-256), ou RFC 9421 (`Signature-Input` + `Signature`) com RSA ou Ed25519 (R-02); o `keyId` resolve para o ator e a chave é a publicada nele.
2. **`Digest`** do corpo confere com o header.
3. **Data** dentro de janela de ±5 min (rejeita replay).
4. **Tamanho** do corpo ≤ 1 MB e profundidade de aninhamento ≤ 32.
5. **Rate limit** por domínio (padrão 60 req/min, configurável por par).
6. **Idempotência**: `activity.id` já visto → `202` sem reprocessar (InboxGrain, janela de N ids).
7. **Remetente**: domínio não bloqueado; ator resolvível (cache com TTL).

Falha em 1–4 → `401`/`400` sem processar; falha em 5 → `429` + `Retry-After`. Em nenhum caso o
processo cai (FR-040).

## 4. Decisão de fan-out na saída

**Reavaliada no momento da entrega** (R-05, FR-038), não no enfileiramento.

| `visibility` | Destinatários | Observação |
|--------------|---------------|------------|
| `public` | seguidores do autor + coleção `followers` do contêiner + menções | Entrega em paralelo por par |
| `unlisted` | seguidores apenas | Não entra em coleção pública nem em descoberta |
| `membersOnly` | **apenas** atores com `Membership.State = Active` no contêiner, em qualquer instância | Payload marcado com `squid:policy`; nunca para seguidores não membros |
| `private` | **ninguém** | Não federa em claro; limitação registrada (R-11) |

Regras adicionais:

- Antes de cada envio, resolve destinatário e recomputa decisão; negada → `Cancelled` com motivo.
- `Reply` recebe apenas quem participa da conversa de comentários (autor do pai + membros com
  `canComment`).
- Contêiner `Blocked` ou peer `Down` não recebe: fila local com backoff (R-05, FR-036).
- Nenhuma entrega bloqueia a resposta ao usuário local.

## 5. Assinatura de saída

Formato draft-cavage, o que Mastodon e Friendica verificam:

```text
Signature: keyId="https://dominio/users/alice#main-key",algorithm="rsa-sha256",
           headers="(request-target) host date digest",signature="<base64>"
Digest: SHA-256=<base64 do corpo>
Date: Tue, 03 Nov 2026 12:00:00 GMT
```

- `Digest` **só** com SHA-256; Mastodon rejeita `sha-512` (`app/lib/signed_request.rb:65-67`).
- `(request-target)` sempre assinado, com query string; GET assina `(request-target) host date`,
  porque Mastodon exige `host` em GET (`:57`).
- `Date` em HTTP-date (GMT); a janela de aceitação nossa é ±5 min.
- Assina o ator que age, com a chave RSA dele (`#main-key`), nunca uma chave de instância.
- RFC 9421 é aceito na entrada, com `created` e, em POST, `content-digest` assinados; é o caminho do
  Ed25519 e o fallback de quem usa (Friendica tenta draft-cavage e cai para 9421 em `400`/`401`,
  `HTTPSignature.php:299-311`).
- Assinatura rejeitada pelo par vira `Failed` com motivo `signature_rejected` e alerta; nunca retry
  cego com a mesma chave.

## 6. Compatibilidade e limites

- `Accept: application/activity+json` e `application/ld+json; profile="https://www.w3.org/ns/activitystreams"`.
- Payload com campos desconhecidos é preservado como `JsonNode` no `Activity.Payload` e ignorado no
  domínio (R-13) — leitura tolerante, escrita estrita.
- Respostas de erro federadas nunca expõem stack trace ou caminho interno.
- `nodes`/`emojis` de instâncias vizinhas são tratados como dados não confiáveis; HTML remoto é
  sanitizado antes de renderizar.
- Atores remotos `Service` são tratados como atores normais (seguir, mencionar, entrar em contêiner).
  O rótulo de bot (`type: Service` + `squid:isBot`) é informativo, **não** capacidade: a política do
  contêiner continua sendo a única fonte de permissão.

## 7. Testes de contrato obrigatórios (dois silos + interop real, R-17)

1. Post público de A aparece em B com autor, mídia e data corretos (SC-007).
2. Post `membersOnly` **não** chega a seguidor não membro de B (SC-005).
3. `private` não gera nenhuma entrega.
4. Entrega duplicada da mesma `activity.id` não duplica estado (FR-035).
5. Assinatura inválida ou `Digest` divergente → `401`, estado intacto.
6. B indisponível: A publica normalmente e a entrega fica `Pending` com backoff (SC-008).
7. Política alterada entre enfileiramento e envio → `Cancelled`, nada enviado (FR-038).
8. Payload de 5 MB → `413`; aninhamento 100 → `400`; serviço continua respondendo.
9. `webfinger` de handle inexistente → `404` sem revelar atores parecidos.
10. Atributo de trace `peer.domain` presente e sem PII em 100% das entregas (SC-015).
11. Interop Mastodon: post público aparece no perfil do par; log do par sem
    `signature verification failed` (SC-016).
12. Interop Friendica: idem, com `Digest` SHA-256 e `keyId` terminando em `#main-key`.
13. Inbound real: atividade assinada por Mastodon e por Friendica é aceita com `202`; a mesma
    atividade sem `Signature` recebe `401` sem processar.
14. `Like` duplicado do mesmo ator não duplica contagem.
15. Resposta federada preserva `inReplyTo`; conversa privada não gera nenhuma entrega (R-11).