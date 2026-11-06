# Data Model: Squid — MVP de rede social federada

**Feature**: `001-squid-mvp` | **Date**: 2026-11-02 | **Fonte**: [spec.md](./spec.md), [plan.md](./plan.md), [research.md](./research.md)

Modelo **canônico**: nenhum tipo daqui conhece JSON-LD, Orleans ou I/O. O adapter de protocolo
traduz na borda (`research.md` R-04, ADR-11). Convenções: URIs canônicas como identidade estável;
`long` para bytes e contadores; timestamps em UTC (`DateTimeOffset`); nenhum binário no estado.

---

## 1. Diagrama de relações

```text
Actor 1 ──── * Membership * ──── 1 Container
  │                                  │  └── 1 ContainerPolicy (versionada)
  │                                  ├── * Object (Post) ── * Attachment
  │                                  └── * Thread ── * Message
  │
  ├── * Activity  (append-only, seq monotônica por agregado)
  ├── * PolicyDecision (auditoria)
  ├── 1 RecoveryKit
  └── * PeerDelivery → Peer

BackupEnvelope * ── 1 KeyEpoch
Quota 1 ── (local, peer)
```

---

## 2. Entidades

### 2.1 `Actor`

Identidade local ou cópia em cache de identidade remota.

| Campo | Tipo | Regra |
|-------|------|-------|
| `Id` | URI canônica | Imutável; é a chave de todo o resto |
| `Handle` | string (`nome@dominio`) | Único no domínio local; `[a-z0-9_]{3,30}` |
| `Kind` | `Local \| Remote` | Define se tem senha e kit |
| `IsBot` | bool | Ator local criado como bot (`Service` no JSON-LD); rótulo público "BOT"; papéis e capacidades seguem a política normal (feature 002) |
| `IsOperator` | bool | Operador global da instância (bootstrap por configuração); múltiplos permitidos; toda ação auditada |
| `DisplayName` | string (≤ 80) | Vazio cai para `Handle` |
| `Summary` | string (≤ 500) | Texto puro sanitizado |
| `AvatarUrl`/`HeaderUrl` | URI? | Aponta para blob local |
| `PublicKeys` | lista `(algorithm, keyId, publicKey)` | RSA-2048 (`#main-key`, assina) e Ed25519 (adicional) — R-02 |
| `WrappedSigningKey` | blob | RSA-2048 privada embrulhada por `HKDF(RK,"fed/rsa-wrap")`; Ed25519 é derivada (R-01); nunca em claro |
| `OutboxSeq` | long | Monotônica por ator |
| `FollowerCount`/`FollowingCount` | long | Derivados, reconciliação diária |
| `CreatedAt` | datetime | UTC |
| `Status` | `Active \| Suspended \| Deleted` | Ver 3.1 |

**Invariantes**: `Handle` não muda depois de publicado (mudança = novo ator, não renomeação);
`Domain` do handle é do ator, não da instância que o hospeda no momento; ator remoto nunca tem senha
nem kit. Contadores de ator remoto são declarados pelo par, não verificados: guardados com
`clamp(0, 100_000_000)` e exibidos como aproximados (padrão `status_stat.rb` do Mastodon), nunca
usados em política, cota ou ordenação. Ator `IsBot` é membro comum perante a `IPolicyEvaluator`;
instalação em contêiner, token, webhook e rate limit próprio são a feature `002-bots-e-automacao`. Não
há e-mail/telefone no modelo: contato não é identidade, e recuperação é o kit (FR-062).

### 2.2 `Container`

Agregado social único com dois presets. **Nunca guarda o feed.**

| Campo | Tipo | Regra |
|-------|------|-------|
| `Id` | URI | `https://dominio/c/{slug}` |
| `Slug` | string | Único entre contêineres do dono; imutável |
| `Kind` | `Group \| Community` | Muda apenas os presets iniciais |
| `Name` | string (≤ 120) | — |
| `Summary` | string (≤ 1000) | — |
| `OwnerActorId` | URI | Exatamente 1 no MVP; canônico é `Membership.Role=Owner`; lista de owners é a evolução reservada |
| `SuccessorActorId` | URI? | Nomeado pelo Owner; só assume após 90 dias de inatividade (configurável) |
| `Policy` | `ContainerPolicy` | Versão vigente (2.3) |
| `Seq` | long | Monotônica; base do delta e do backup |
| `Listed` | bool | Preset: `Group=false`, `Community=true` |
| `CreatedAt`/`DeletedAt` | datetime? | Exclusão é soft, com carência de 30 dias |
| `MemberCount` | long | Contador derivado, nunca fonte da verdade |

**Invariantes**: `MemberCount` e `Seq` não são recalculados no caminho de leitura; nenhuma lista de
membros/feed dentro do estado do agregado além da janela de papéis necessária à política;
`OwnerActorId` sempre tem `Role=Owner` e estado `Active`; nenhum evento deixa o contêiner sem Owner
ativo, e sucessão ou override do operador geram evento auditado.

**Presets** (só valores iniciais; depois a política é livre — §2.3 do doc 05):

| Campo | `Group` | `Community` |
|-------|---------|-------------|
| `Visibility` | `MembersOnly` | `Public` |
| `Join` | `Invite` | `Open` |
| `Posting` | `Members` | `Members` |
| `Listed` | false | true |
| `Interaction` | membros comentam/reagem | membros comentam, visitante reage? Não |

### 2.3 `ContainerPolicy`

Dado versionado. Toda decisão de acesso referencia uma versão.

| Campo | Tipo | Valores |
|-------|------|---------|
| `Posting` | enum | `Everyone \| Members \| Moderators \| Admins \| SinglePublisher` |
| `PublisherIds` | lista de URI | Obrigatória e não vazia se `SinglePublisher` (FR-015) |
| `Visibility` | enum | `Public \| Unlisted \| MembersOnly \| Private` |
| `Join` | enum | `Open \| Request \| Invite \| Closed` |
| `Interaction` | objeto | `CanComment`, `CanReact`, `CanShare`, `CanAttach` — cada um com o mesmo enum de `Posting` |
| `Version` | int | Incrementa a cada alteração; nunca reutiliza número |

**Invariantes**: `SinglePublisher` exige `PublisherIds` válidos e membros ativos; `Private` não
federa (R-11); alteração de política é evento de auditoria com autor, antes/depois e versão nova;
duas edições concorrentes → a segunda falha por versão, não sobrescreve (edge case da spec).

### 2.4 `Membership`

| Campo | Tipo | Regra |
|-------|------|-------|
| `ContainerId` + `ActorId` | chave composta | Um vínculo por par |
| `Role` | `Owner \| Admin \| Moderator \| Member \| Guest` | Herança estrita (3.2) |
| `State` | `Invited \| Requested \| Active \| Silenced \| Banned \| Left` | Ver 3.3 |
| `InvitedBy` | URI? | Auditoria de convite |
| `JoinedAt`/`UpdatedAt` | datetime | UTC |

**Invariantes**: `Owner` é único por contêiner no MVP (Admin e Moderator podem ser múltiplos);
co-ownership futuro apenas relaxa esta invariante, sem mudar o agregado. `Banned` não pode transicionar
para `Active` sem ação de `Admin` com justificativa; `Guest` nunca publica, independentemente da
política (a política não concede o que o papel não tem).

### 2.5 `Object` (Post) e `Message`

`Object` é a publicação em contêiner; `Message` é o item de conversa. Compartilham a forma básica,
mas têm agregados distintos (feed × thread).

| Campo | Tipo | Regra |
|-------|------|-------|
| `Id` | URI | `https://dominio/o/{id}`; público por padrão, inclusive para restrito (não vaza conteúdo) |
| `AuthorActorId` | URI | — |
| `ContainerId`/`ThreadId` | URI | Um dos dois; nunca ambos |
| `Kind` | `Post \| Event \| Question \| Objective` | `Post` no MVP; os demais são a feature 003 (ADR-31) |
| `Content` | string (≤ 5000 no post; ≤ 4000 na mensagem) | Texto sanitizado |
| `Attachments` | lista `Attachment` | ≤ 10 |
| `VisibilityAtPublish` | enum | Congelada na publicação |
| `PolicyVersion` | int | Versão avaliada na publicação |
| `Seq` | long | Ordem total no agregado |
| `ReplyToId` | URI? | Comentário encadeia; profundidade ≤ 3 no MVP |
| `CreatedAt`/`EditedAt`/`DeletedAt` | datetime | Exclusão soft com tombstone federado |
| `TakedownReason` | string? | Obrigatório quando a remoção é de `Moderator`+ (R-07); conteúdo sai, registro fica 6 meses |
| `Counts` | `(likes, replies, shares)` | Derivados; reconciliados |

**Invariantes**: `VisibilityAtPublish` pode ser **mais restritiva** na leitura, nunca mais permissiva;
exclusão nunca apaga a linha da auditoria; edição de post não muda `Seq` (gera evento novo).
Contadores vindos de cópia federada seguem o mesmo teto dos contadores de ator remoto (D4-b).
Respostas a interações (RSVP, voto, claim de meta) são o agregado `Response` da feature 003,
chaveado por `(ObjectId, ActorId)` — nunca campos no `Object` (evita hot spot e garante
idempotência por ator).

### 2.5.1 `Message` (agregado da conversa)

`Message` compartilha a forma básica de `Object`, mas não tem visibilidade própria.

| Campo | Tipo | Regra |
|-------|------|-------|
| `ThreadId` | URI | Obrigatório; dono da ordem |
| `Content` | string (≤ 4000) | Texto sanitizado |
| `Seq` | long | Posição na `MessageSeq` da thread |
| `ReplyToId` | URI? | Outra mensagem **da mesma thread** (FR-057) |
| `DeletedAt` | datetime? | Tombstone local; thread não federa |
| `Counts.reactions` | int | Derivado de `Reaction` (§2.14), nunca fonte |

**Invariantes**: mensagem não tem `VisibilityAtPublish` — o acesso é sempre a lista de participantes
da thread (FR-031); resposta e reação herdam a visibilidade da conversa, nunca a do objeto referenciado.

### 2.6 `Attachment`

| Campo | Tipo | Regra |
|-------|------|-------|
| `Kind` | `Link \| Image \| Gif \| Sticker \| Audio \| Video` | — |
| `ContentHash` | string (SHA-256 hex) | Endereço do blob (R-14) |
| `MimeType` | string | Validado por magic bytes, não por extensão |
| `Url` | URI | Servido pela instância |
| `BlurHash` | string? | Obrigatório para `Image`/`Gif` |
| `Alt` | string | Obrigatório e não vazio para `Image`/`Video`/`Gif` (FR-022) |
| `Width`/`Height`/`DurationSeconds` | int? | Preenchidos no Worker |
| `SizeBytes` | long | — |

**Invariantes**: `Link` não tem blob; mídia sem `Alt` é rejeitada na publicação (não só na UI);
EXIF removido antes de gerar `BlurHash` e thumbnail.

### 2.7 `Thread`

| Campo | Tipo | Regra |
|-------|------|-------|
| `Id` | URI | — |
| `ParticipantActorIds` | lista de URI | 2..50 no MVP |
| `Kind` | `Direct \| Group` | — |
| `MessageSeq` | long | Ordem total (FR-030) |
| `CreatedAt` | datetime | — |

**Invariantes**: remover participante não apaga histórico para os que ficam; participante removido
perde acesso imediatamente; ninguém fora da lista lê nem descobre a lista (FR-031). Mensagens aceitam `ReplyToId` dentro da
thread e reação pelo agregado `Reaction` (§2.14); nenhum dos dois amplia a visibilidade da conversa.

### 2.8 `Activity`

Unidade de mudança de domínio; fonte de delta, outbox e entrega.

| Campo | Tipo | Regra |
|-------|------|-------|
| `Id` | URI | Idempotência de entrada (FR-035) |
| `Type` | enum | `Create \| Update \| Delete \| Follow \| Accept \| Announce \| Join \| Leave \| Block` |
| `ActorId` | URI | Autor |
| `ObjectId` | URI? | Alvo |
| `Seq` | long | Monotônica no agregado |
| `Payload` | `JsonNode` | JSON-LD cru preservado para federação (R-13) |
| `CreatedAt` | datetime | — |

**Invariantes**: `(ActorId, Seq)` único; `Activity` nunca é reescrita; log truncado após snapshot
(§4.7 #3) e o truncamento só remove o que já está no snapshot **e** já foi entregue ou expirou.

### 2.9 `Peer` e `PeerDelivery`

| `Peer` | Tipo | | `PeerDelivery` | Tipo |
|--------|------|---|----------------|------|
| `Domain` | string | | `ActivityId` | URI |
| `Status` | `Healthy \| Degraded \| Down \| Blocked` | | `RecipientActorId` | URI |
| `BackoffUntil` | datetime? | | `PolicyVersionAtEnqueue` | int |
| `FailureCount` | int | | `Attempts` | int |
| `LastSuccessAt` | datetime? | | `NextAttemptAt` | datetime |
| `RateLimitPerMinute` | int | | `State` | `Pending \| Sent \| Failed \| Cancelled` |
| | | | `Reason` | string? |

**Invariantes**: entrega nunca bloqueia a request local (FR-036); `Cancelled` só por política
(R-05); `Failed` vai para DLQ com motivo e alerta; um peer `Blocked` não recebe nada.

### 2.10 `BackupEnvelope` e `KeyEpoch`

| `BackupEnvelope` | Tipo | | `KeyEpoch` | Tipo |
|------------------|------|---|------------|------|
| `Id` | URI | | `Epoch` | int |
| `KeyEpoch` | int | | `CreatedAt` | datetime |
| `SeqFrom`/`SeqTo` | long | | `PublicFingerprint` | string |
| `ContentHash` | string | | `RetiredAt` | datetime? |
| `SizeBytes` | long | | | |
| `TargetId` | string | | | |
| `CreatedAt` | datetime | | | |
| `VerifiedAt` | datetime? | | | |

**Invariantes**: nenhum envelope sem `KeyEpoch` válida; restore aceita qualquer época com chave
disponível (R-20); envelope cujo hash diverge após download é `Corrupt` e nunca é usado.

### 2.11 `PolicyDecision` (auditoria)

| Campo | Tipo |
|-------|------|
| `At` | datetime |
| `ActorId` | URI |
| `Action` | enum (`Post`, `Comment`, `Join`, `Invite`, `RemoveContent`, `ChangeRole`, `View`, `Share`) |
| `ResourceId` | URI |
| `Result` | `Allow \| Deny \| NotFound` |
| `PolicyVersion` | int |
| `Reason` | string |

**Invariantes**: append-only; `Deny`/`NotFound` também registrados (SC-011); retenção mínima 6 meses
para decisões de moderação.

### 2.12 `RecoveryKit`

| Campo | Tipo | Regra |
|-------|------|-------|
| `ActorId` | URI | — |
| `Mnemonic` | 24 palavras | Exibido uma única vez; nunca persistido em claro |
| `EncryptedRootKey` | blob | Argon2id + AEAD |
| `WrappedSigningKey` | blob | RSA privada embrulhada pela `RK`; sem ela o restore não reassina |
| `RecoveryCredentialHash` | string | Independente da `RK` (FR-005) |
| `VerifiedAt` | datetime? | Sem isso, alerta persistente (FR-007) |
| `CreatedAt` | datetime | — |

**Invariantes**: a `RK` em claro nunca é persistida nem logada (FR-004); importação com senha errada
não destrói dados existentes. O kit é a única forma de reidratar a RSA; importá-lo restaura a
capacidade de assinar (FR-006).

### 2.13 `Quota`

| Campo | Tipo | Regra |
|-------|------|-------|
| `Scope` | `(instance)` ou `(local, peer)` | MVP usa instância |
| `UsedBytes` | long | Inteiro (R-19) |
| `LimitBytes` | long | Padrão 10 GiB |
| `UpdatedAt` | datetime | Reconciliado por varredura diária |

### 2.14 `Reaction`

| Campo | Tipo | Regra |
|-------|------|-------|
| `TargetId` | URI | Publicação (`Object`) ou mensagem (`Message`) |
| `ActorId` | URI | Quem reagiu |
| `Emoji` | string (≤ 64) | Emoji Unicode ou shortcode da whitelist da instância |
| `Kind` | `Like \| Custom` | `Like` federa; `Custom` vai por extensão |
| `CreatedAt` | datetime | — |

**Invariantes**: uma linha por `(TargetId, ActorId, Emoji)` — repetir é idempotente e remover apaga a
linha; emoji fora da whitelist é rejeitado (spam); reação nunca aparece fora da visibilidade do alvo
(nem em notificação, preview ou busca); contagem é projeção reconciliada, não fonte; reação de
mensagem é local, porque thread não federa.

---

## 3. Máquinas de estado

### 3.1 `Actor.Status`

```text
Active ──suspend(admin)──► Suspended ──reinstate(admin)──► Active
   └──delete(self|admin)──► Deleted   (terminal; handle liberado só após 30 dias)
```

`Suspended` não publica, não entra em contêiner, não federa saída; conteúdo existente segue a
política da instância (pode ser ocultado, nunca apagado da auditoria).

### 3.2 Papéis (herança estrita)

```text
Owner ⊃ Admin ⊃ Moderator ⊃ Member ⊃ Guest
```

Cada nível pode tudo do nível abaixo **mais** suas capacidades. `Owner` inclui excluir o contêiner e
transferir propriedade; `Admin` gerencia papéis, política, conteúdo e banimento; `Moderator` remove
conteúdo, silencia e aprova entrada; `Member` participa conforme política; `Guest` só lê.

Regra dura: papel nunca é verificado por `if` espalhado — toda checagem passa por
`IPolicyEvaluator` com registro de auditoria.

### 3.3 `Membership.State`

```text
(Invite)   Invited ──accept──► Active ──leave──► Left ──rejoin(se política permite)──► Active
(Request)  Requested ──approve──► Active
                       └──reject──► (removido)
Active ──silence(moderator)──► Silenced ──unsilence──► Active
Active ──ban(admin)──► Banned ──unban(admin, justificativa)──► Active
```

`Silenced` lê mas não publica/comenta; `Banned` não lê. `Owner` não pode ser banido — transferência
primeiro.

### 3.4 `PeerDelivery.State`

```text
Pending ──send ok──► Sent
   ├──falha──► Pending (backoff crescente; attempts++)
   ├──attempts > N ou 4xx permanente──► Failed (DLQ)
   └──política reavaliada nega──► Cancelled
```

*N* = 8 tentativas, backoff exponencial com jitter, teto de 6 h. Circuito aberto por peer manda
novas entregas direto para `Pending` com `NextAttemptAt` futuro — nunca falha na hora.

---

## 4. Projeções de leitura (Dapper)

Tabelas derivadas do log de eventos, atualizadas por Worker (R-18). Nunca no caminho de escrita.

| Tabela | Chave | Conteúdo | Uso |
|--------|-------|----------|-----|
| `timeline_item` | `(container_id, seq)` | post resumido, autor, contadores, visibilidade efetiva | Feed de contêiner e `?since=` |
| `home_timeline` | `(actor_id, seq)` | itens dos contêineres do ator | Timeline pessoal |
| `actor_card` | `actor_id` | nome, handle, avatar | Renderização e busca de handle |
| `container_card` | `container_id` | nome, slug, preset, contadores, `listed` | Descoberta (R-10) |
| `membership_view` | `(container_id, actor_id)` | papel, estado | Checagem de acesso em leitura |
| `notification` | `(actor_id, seq)` | tipo, alvo, lida | Avisos |
| `thread_message` | `(thread_id, seq)` | mensagem | Histórico de conversa |
| `peer_health` | `peer_domain` | estado, falhas, atraso | Painel (FR-055) |
| `policy_audit` | `(at, id)` | decisão | Consulta de auditoria (SC-011) |

**Regras de projeção**: toda query aplica filtro de visibilidade vigente (FR-026); `?since=<seq>`
usa índice em `seq`; nenhuma projeção serve conteúdo de contêiner `Private` a quem não é membro,
mesmo que o autor tenha mudado a visibilidade depois — a leitura usa a política corrente, não a de
publicação (edge case "política alterada").

---

## 5. Mapa de persistência (MVP → fase 2)

| Dado | MVP | Pós-MVP | Contrato que não muda |
|------|-----|---------|----------------------|
| Estado dos atores | SQLite via `IGrainStorage` com ETag (`Squid.Persistence`) | Oracle ATP, mesma interface | `IGrainStorage` |
| Eventos/log | SQLite (tabela append-only, truncada por snapshot) | ATP | `Sequence` monotônica |
| Projeções | SQLite + Dapper | ATP + Dapper | nomes de tabela e consultas |
| Blobs | volume nomeado, content-addressed | Object storage / shard | `ContentHash` SHA-256 |
| Backups | `IBackupTarget` local + S3-compatível | `PeerShardingTarget` | envelope com `keyEpoch` |
| Ledger de cota | EF Core (migrations) | ATP | inteiro em bytes |

---

## 6. Rastreabilidade entidade → requisito

| Entidade | Cobre |
|----------|-------|
| `Actor`, `RecoveryKit` | FR-001..FR-009, FR-062, FR-063 |
| `Container`, `Membership`, `ContainerPolicy` | FR-010..FR-019, FR-059..FR-061 |
| `Object`, `Attachment` | FR-020..FR-029 |
| `Thread`, `Message` | FR-030..FR-033, FR-057 |
| `Reaction` | FR-058 |
| `Activity`, `Peer`, `PeerDelivery` | FR-034..FR-041 |
| `Object` (URL canônica), `Attachment` (preview) | FR-042..FR-044 |
| `BackupEnvelope`, `KeyEpoch`, `Quota` | FR-045..FR-050 |
| `PolicyDecision`, `Peer` (métricas) | FR-051..FR-056 |