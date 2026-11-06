# Documento 05 — Arquitetura Consolidada, Infraestrutura e Primeiros Passos

> **Natureza deste documento:** é o **documento mestre** de execução. Consolida [`02-arquitetura-poc.md`](02-arquitetura-poc.md), o aproveitamento descrito em [`03-reaproveitamento-orders-orleans.md`](03-reaproveitamento-orders-orleans.md) e a resiliência de chaves de [`04-recuperacao-chaves-criptografia.md`](04-recuperacao-chaves-criptografia.md).
>
> **Hierarquia:** o 05 decide. O 03 e o 04 detalham. O 02 permanece como registro da ideia original — quando 02 e 05 divergirem, **vale o 05**.
>
> **Uso pretendido:** base de desenvolvimento incremental com IA. Ver §12.1 antes de começar qualquer tarefa.

---

## 1. Visão e Escopo

### 1.1 O que é o produto

Uma rede social open-source, federada e modular, com três camadas de sociabilidade: **mensagens**, **grupos** (círculos de amigos) e **comunidades** (interesses em comum). Backend desacoplado do front (REST/JSON), mobile-first, e compartilhamento de conteúdo facilitado tanto dentro da rede quanto para fora dela.

A longo prazo, a instância pode hospedar fragmentos criptografados de backup de outras instâncias (*Mutual Sharding*). **Esse não é o MVP** — ver §1.4.

### 1.2 Escopo do MVP (registro formal)

**Entra no MVP:**

| Área | Escopo mínimo |
|------|---------------|
| **Mensageria** | Conversa 1-1 e conversa em grupo, com resposta a mensagem e reação por emoji. Sem E2EE (ver §2.5) |
| **Grupos** | Grupos de amigos: criação, convite, papéis, privacidade por padrão |
| **Comunidades** | Interesses em comum: descoberta, entrada, papéis, feed |
| **Hierarquia** | Administrador, Moderador, Usuário (membro) — em grupos e comunidades |
| **Controle de acesso** | Políticas configuráveis: quem posta, quem vê, quem entra, quem comenta |
| **Publicação** | Texto, link, foto, GIF, emoji/figurinha, áudio, vídeo |
| **Federação** | Um protocolo inicial (ActivityPub), com camada que permita um segundo |
| **Compartilhamento externo** | Link com preview (OpenGraph), share sheet nativo, deep links — ver §3.4 |
| **Identidade** | Perfil, chaves, recuperação de chave em camada 0 |
| **Operação** | Backup local cifrado + restore testado, observabilidade básica |

**Fora do MVP (registrado como não-objetivo):**

* Mutual Sharding / Reed-Solomon (fase 2+)
* E2EE e MLS
* Federação multi-protocolo ativa (só a abstração)
* Cross-posting automático em redes fechadas
* Recomendação/algoritmo de feed
* App iOS publicado em loja
* Multi-tenancy, federação de instâncias com SLA

### 1.3 Não-objetivos explícitos

Os não-objetivos existem para resistir à expansão de escopo — que, com desenvolvimento assistido por IA, é a falha mais provável do projeto.

1. **Nenhuma integração de postagem automática** em X, Instagram, Facebook, WhatsApp ou Tinder. Contraria termos de uso ou não existe API pública (§3.5).
2. **Nenhuma ingestão de conteúdo** dessas redes fechadas.
3. **Nenhum algoritmo** de ranqueamento no MVP: feed cronológico.
4. **Nenhum microserviço** além do Worker de backup/mídia. Orleans resolve o resto.
5. **Nenhum Kubernetes.** Podman + systemd em 1–2 VMs.

### 1.4 Repriorização estratégica (mudança em relação ao doc 02)

O doc 02 colocava o *Mutual Sharding* no centro da POC (§3) e o plano de ação começava por infraestrutura e criptografia de backup. **Isso muda.**

| | Doc 02 | Doc 05 |
|---|--------|--------|
| Centro da POC | Backup distribuído | Rede social utilizável (mensagem, grupo, comunidade) |
| Primeiro entregável | VMs + ATP + RabbitMQ | Instância local com perfil, grupo e post |
| Backup distribuído | Sprint 2–3 | Fase 2 (Sprint 8+) |
| Risco principal | Complexidade criptográfica | Escopo explodir |

**Motivo:** sem uma rede social funcionando, não existe federação; sem federação, não existe rede de guardiões nem de hospedeiros de shard. O diferencial continua sendo o diferencial — só deixa de ser o primeiro passo. Ele será **demonstrado** quando houver pares.

### 1.5 Princípios de arquitetura

1. **Local primeiro, federação depois.** Todo recurso funciona standalone antes de federar.
2. **Um segredo para recuperar.** Nada de "chaves" no plural como se fossem uma coisa (§5.1).
3. **O grain orquestra; o Worker computa.** Nada de CPU pesada no scheduler do silo (§4.6).
4. **Política é dado, não código.** Regras de acesso em objeto avaliado por um motor único (§2.4).
5. **O protocolo é um adapter, o domínio é canônico.** JSON-LD nunca entra no agregado (§2.7).
6. **Observável ou não existe.** Todo fluxo novo nasce com trace, log sanitizado e métrica.
7. **Fase 0 sempre recuperável.** Se o backup não foi restaurado em teste, ele não existe (§6.4).

---

## 2. Modelo de Domínio Canônico

### 2.1 Hierarquia de objetos

```
Actor (usuário local ou remoto)
  └── Container (Group | Community)     ← agregado social
        ├── Membership (Actor + Role)   ← hierarquia
        ├── Post (Object)               ← publicável
        │     └── Attachment[]          ← mídia
        └── Thread (conversa)           ← mensageria

Actor ──► Activity ──► (fan-out local | entrega federada | projeção de leitura)
```

### 2.2 Atores e papéis

**Atores:** `LocalUser` (hospedado aqui) e `RemoteActor` (cache de ator de outra instância).

**Papéis em Container** — hierarquia estrita, cada nível herda o anterior:

| Papel | Pode |
|-------|------|
| `Owner` | Tudo, inclui excluir o container e transferir propriedade |
| `Admin` | Gerenciar papéis, configurar política, remover conteúdo, banir |
| `Moderator` | Remover conteúdo, silenciar membro, aprovar entrada |
| `Member` | Participar conforme a política (postar, comentar, reagir) |
| `Guest` | Somente leitura, quando a política permitir |

> **Regra:** um papel **nunca** é verificado por `if` espalhado no código. Toda checagem passa pelo motor de políticas (§2.4), com registro de auditoria de quem autorizou o quê.

### 2.3 Grupos × Comunidades: um agregado, dois presets

**Decisão:** o mesmo agregado `Container`, com `kind: Group | Community`. Os dois compartilham papéis, políticas e publicação; o que muda são os **presets** e a escala.

| Aspecto | `Group` (amigos) | `Community` (interesses) |
|---------|------------------|--------------------------|
| Privacidade padrão | `membersOnly` | `public` |
| Entrada padrão | `invite` | `open` ou `request` |
| Descoberta | Não listado | Listado / pesquisável |
| Tamanho esperado | Dezenas | Milhares |
| Forma de interação | Conversa (chat) | Feed + threads |
| Postagem padrão | `membersOnly` | `membersOnly` |
| Vida útil | Estável | Pode ser grande e hierárquica |

**Por que unificar:** duplicar o subsistema de papéis, política e auditoria para dois conceitos com 90% de sobreposição é a origem clássica de divergência de comportamento e de bug de permissão. Um agregado, dois presets, um motor de política.

**Consequência de escala:** `Community` não guarda o feed no grain (§4.2). O `ContainerGrain` guarda **membresia, papéis e política**; o conteúdo vive em `ObjectGrain` com chave própria.

### 2.4 Motor de políticas (o coração do controle de acesso)

**Quatro políticas independentes**, avaliadas em sequência:

| Política | Valores | Exemplo do requisito |
|----------|---------|----------------------|
| **Posting** | `everyone` · `members` · `moderators` · `admins` · `singlePublisher` | "só uma pessoa posta", "somente admins" |
| **Visibility** | `public` · `unlisted` · `membersOnly` · `private` | "o que pode ou não ser compartilhado" |
| **Join** | `open` · `request` · `invite` · `closed` | comunidade aberta vs grupo fechado |
| **Interaction** | quem comenta, quem reage, quem compartilha, quem anexa | granularidade fina |

**`singlePublisher` é um caso de primeira classe** — é o clássico canal de avisos (comunidade de notícias, mural da turma). Modelar como "só admin posta" perde a semântica; modelar como publisher explícito permite delegar sem promover a admin.

**Desenho:**

```csharp
public sealed record ContainerPolicy
{
    public PostingPolicy     Posting     { get; init; }
    public VisibilityPolicy  Visibility  { get; init; }
    public JoinPolicy        Join        { get; init; }
    public IReadOnlyList<string> PublisherIds { get; init; } = [];  // singlePublisher
    public InteractionPolicy Interaction { get; init; } = new();
    public int Version { get; init; }   // auditoria: qual política valia na hora
}

public interface IPolicyEvaluator
{
    PolicyDecision Evaluate(Action action, Actor actor, ContainerPolicy policy);
}
```

**Quatro pontos de aplicação — e o único autoritativo é o grain:**

| Ponto | Papel | Autoridade |
|-------|-------|------------|
| Borda HTTP | Rejeitar cedo (economiza I/O) | ❌ Não confiável |
| **Comando no grain** | **Decisão final, serializada** | ✅ **Autoritativa** |
| Projeção de leitura (Dapper) | Filtrar o que não pode ser visto | Complementar |
| Saída federada | Não vazar o que a política proíbe | ⚠️ Crítico (§3.2) |

> **Nunca** confie na checagem da borda: o grain é a única fronteira serializada e, portanto, a única capaz de decidir sem corrida.

### 2.5 Mensageria (MVP sem E2EE — registro honesto)

O doc 02 prometia E2EE com X25519 + ChaCha20-Poly1305. **No MVP, não haverá E2EE.**

| Fase | Mensageria | Proteção |
|------|-----------|----------|
| **MVP** | DM 1-1 e conversa em grupo | TLS em trânsito + cifra em repouso no banco |
| **Fase 2** | Idem | + chaves por usuário no device, sem criptografia de conteúdo |
| **Fase 3** | Idem | E2EE com MLS (RFC 9420) |

**Por que:** E2EE em grupo exige MLS, que é um projeto de meses e não coexiste com busca, moderação e denúncia de conteúdo. Federação de E2EE **não é padronizada** no ActivityPub. Tentar E2EE no MVP entrega uma rede social que não funciona em vez de uma que funciona sem E2EE.

**Consequência que precisa estar na UI:** o usuário deve saber que as mensagens do MVP são legíveis pelo operador da instância. Não é um detalhe — é consentimento informado. Registrar em termo e em tela.

**Interação dentro da conversa (ADR-33):** mensagem aceita resposta (`ReplyToId`, sempre na mesma
thread) e reação (um emoji por ator, removível; `Like` federado, emoji customizado por extensão com
whitelist). Contagem é derivada, nunca fonte. Resposta e reação **não** ampliam a visibilidade da
conversa — nem em notificação, preview ou busca.

**Preparação para a 004 (E2EE/MLS):** o corpo da mensagem é payload opaco e versionado; resposta e
reação são metadados de thread, não conteúdo; quando a cifra entrar, os contadores passam a ser
calculados no cliente e o servidor guarda só `thread`/`seq`. Nada disso está implementado agora.

### 2.6 Anexos e mídia

```csharp
public sealed record Attachment
{
    public AttachmentKind Kind { get; init; }   // Link | Image | Gif | Sticker | Audio | Video
    public string MimeType { get; init; }
    public string Url { get; init; }            // servido pela instância
    public string ContentHash { get; init; }    // SHA-256 — content-addressed
    public string? BlurHash { get; init; }      // preview sem carregar bytes
    public string? Alt { get; init; }           // acessibilidade (obrigatório na UI)
    public int? Width { get; init; }
    public int? Height { get; init; }
    public int? DurationSeconds { get; init; }
    public long SizeBytes { get; init; }
}
```

**Decisões:**

* **Content-addressed (SHA-256)** — dedupe automático, integridade verificável e o **mesmo hash** usado para a integridade de shards no futuro. Uma primitiva, dois usos.
* **Blob fora do estado do grain.** O grain guarda metadados; o binário vive no *blob store* (volume). Nunca serializar bytes de mídia em JSON de estado (§4.7).
* **Processamento no Worker:** thumbnail, transcodificação (FFmpeg), remoção de EXIF. Nunca no scheduler do silo.
* **EXIF stripping** não é opcional: geolocalização em foto de perfil é vazamento.
* **Emojis/figurinhas customizadas** como `Emoji` tags no AP (`:shortcode:`) para compatibilidade com Mastodon.
* **Limites por tipo** e quota por instância — a quota de disco é o mesmo recurso que o Mutual Sharding vai usar depois.

### 2.7 Modelo canônico vs. JSON-LD (anti-corruption layer)

**Regra:** o JSON-LD do ActivityPub **nunca** entra no agregado de domínio.

```
HTTP (JSON-LD) ──► ActivityPubAdapter ──► Canonical Model ──► Grain
                          ▲                     │
                          └─────────────────────┘
```

* **Por quê:** você vai querer um segundo protocolo (§3.3), e o AT Protocol tem um modelo radicalmente diferente (repositórios, lexicons, PDS). Se o domínio falar JSON-LD, o segundo protocolo é impossível sem reescrever tudo.
* **Custo hoje:** uma camada de mapeamento. **Custo depois:** reescrever o domínio.
* **Cuidado com o excesso:** o modelo canônico **é** influenciado pelo AP (não tente ser protocolo-neutro). A abstração fica na borda, com interface mínima (§3.1).

---

## 3. Camada de Protocolos

### 3.1 Interface mínima

```csharp
public interface IFederationProtocol
{
    string Scheme { get; }                        // "activitypub", "atproto"
    Task<CanonicalActor?> ResolveAsync(Uri handle, CancellationToken ct);
    Task<CanonicalObject?> FetchObjectAsync(Uri id, CancellationToken ct);
    Task DeliverAsync(OutboundEnvelope envelope, CancellationToken ct);
    Task<InboundResult>  ReceiveAsync(ProtocolRequest request, CancellationToken ct);
    Task FollowAsync(CanonicalActor local, Uri remoteTarget, CancellationToken ct);
}
```

**Cinco métodos.** Resistir à tentação de aumentar: cada método novo é um contrato que o segundo protocolo precisa cumprir e que provavelmente não vai.

### 3.2 ActivityPub como primeiro adapter

| Recurso | Endpoint |
|---------|----------|
| Descoberta | `/.well-known/webfinger` |
| Perfil | `/users/{name}` (com `publicKey`) |
| Inbox / Outbox | `/users/{name}/inbox`, `/users/{name}/outbox` |
| Objeto | `/objects/{id}` |
| Contêineres | `/c/{slug}` — extensão própria (`Group`) |

**Regras de saída federada (críticas para não vazar):**

1. `visibility == public` → fan-out para seguidores e para o `followers` collection.
2. `visibility == membersOnly` → fan-out **apenas** para atores com `Membership` ativa.
3. `visibility == private` → **não federar em claro.** Sem E2EE (MVP), registrar como GAP: converse privada federada é legível pelo destinatário e pelo servidor dele.
4. Antes de entregar, reavaliar a política **no momento da entrega** — não confie na política de quando o post foi enfileirado.

**Assinatura HTTP:** o ecossistema verifica **RSA-SHA256** no caminho clássico (draft-cavage) e só aceita `Digest: SHA-256=`; Ed25519 entra como chave adicional via RFC 9421. Saída em RSA-SHA256, entrada aceitando os dois (ADR-28).

### 3.3 Expansão futura

| Protocolo | Quando | Nota |
|-----------|--------|------|
| **ActivityPub** | MVP | Escolhido por ecossistema (Mastodon, Friendica, PeerTube) |
| **AT Protocol (Bluesky)** | Fase 2 | Única "rede fechada" da lista que permite integração real |
| **Nostr** | Fase 3 | Simples, mas modelo de relay muito diferente |
| **Matrix** | Não previsto | Sobreposição com mensageria própria |

**Sinal de que a abstração está funcionando:** adicionar o segundo protocolo não exige mudar `Domain` nem `Grains`. Só um novo adapter em `Federation.Protocols`.

### 3.4 Compartilhamento para redes fechadas — o que é possível

Este requisito precisa de um recorte honesto, senão consome o projeto inteiro.

| Rede | O que funciona | O que **não** funciona |
|------|----------------|------------------------|
| **WhatsApp** | Deep link `https://wa.me/?text=<url>` e share sheet. Preview via OpenGraph | Postagem automática (só Business API com número dedicado e template aprovado) |
| **Telegram** | `https://t.me/share/url?url=<url>` e share sheet | Postagem em canal alheio |
| **X** | Intent de postagem (`https://x.com/intent/post?text=<url>`), preview via card | API de escrita ampla (paga, limitada) |
| **Facebook** | Share link (`/sharer/sharer.php?u=`) | Publicação automática |
| **Instagram** | Apenas share sheet / copiar link | Qualquer postagem por terceiros |
| **Tinder** | — | Inexistente como canal de conteúdo |
| **Bluesky** | **API aberta** (AT Protocol) — é o único caso de integração real | — |

**Conclusão do MVP:** o compartilhamento externo é **link + preview + share sheet nativo**, não integração de API.

Três pilares técnicos:

1. **SSR com preview completo** — é a razão do Nuxt (§4.1). `og:title`, `og:description`, `og:image` (thumbnail gerada pelo Worker), `twitter:card`, `oEmbed`. Sem isso, o link não abre preview em nenhum mensageiro.
2. **Web Share API / Capacitor Share** no mobile → share sheet nativo do SO.
3. **Deep links de entrada** — o post precisa de URL canônica e, no app, de universal link/App Link para abrir direto.

### 3.5 Non-goals de integração

* Nenhuma postagem automática em rede fechada.
* Nenhuma importação de conteúdo de rede fechada.
* Nenhum scraping — além de violar ToS, quebra a cada mudança unilateral.

---

## 4. Arquitetura de Execução (.NET + Orleans)

### 4.1 Stack consolidada

| Camada | Escolha | Observação |
|--------|---------|------------|
| Runtime | **.NET 10 (LTS)** | — |
| Actor framework | **Microsoft Orleans 10.2** | Somente na camada de objetos federados |
| API | **ASP.NET Core Minimal APIs** | REST/JSON-LD + SSE. **Não** gRPC na federação |
| Write side | `IPersistentState` + Event Sourcing + Snapshots | via `IGrainStorage` |
| Storage | **SQLite (POC) → Oracle ATP** | Duas implementações da mesma interface |
| Read side | **Dapper** sobre tabelas de projeção | Grain não serve query |
| Relacional transacional | **EF Core** | Apenas ledger de cotas + migrations |
| CPU pesada | **Worker Service** separado | Backup, Reed-Solomon, mídia, cifra |
| Resiliência | **Polly 8** (`ResiliencePipeline`) + circuit breaker | Retry + timeout + DLQ |
| Observabilidade | **OpenTelemetry (OTLP)** + **Serilog** | Console exporter não serve |
| Ops local | **Orleans Dashboard** | Rede interna, sem auth |
| Infra | **Podman + systemd**, ARM Ampere A1 | Volumes nomeados obrigatórios |
| Web | **Nuxt 3 + Vue 3** | SSR para OpenGraph |
| Mobile | **Vue 3 + Vite + Capacitor** | Fase posterior |

**Rejeitados e por quê:** MediatR no domínio (duplica o dispatch do grain), RabbitMQ no MVP (`PeerGrain` + outbox + DLQ cobre), gRPC na federação (ActivityPub exige HTTP/JSON-LD), Orleans Transactions (experimental).

### 4.2 Mapa de grains

| Grain | Chave | Estado | Responsabilidade |
|-------|-------|--------|------------------|
| `ActorGrain` | URI canônico do ator | perfil, chaves públicas, `outboxSeq`, contadores | identidade local e cache de remoto |
| `ContainerGrain` | URI do grupo/comunidade | política, papéis, membros, `seq` | **nunca** guarda o feed |
| `InboxGrain` | URI do ator local | janela de `activity.id`, pendências | recepção idempotente |
| `ObjectGrain` | URI do post/mensagem | conteúdo, contadores, `policyVersion` | agregado de publicação |
| `ThreadGrain` | id da conversa | participantes, ordem | mensageria (ordem total) |
| `PeerGrain` | domínio remoto | saúde, backoff, fila de entrega | entrega assíncrona por par |
| `MediaGrain` | hash do conteúdo | metadados, status de processamento | pipeline de mídia |
| `QuotaGrain` | `(local, peer)` | débito/crédito em **inteiro** | fair play do sharding (fase 2+) |
| `ShardHostGrain` | `shardId` | hash, tamanho, TTL | hospedagem de fragmento (fase 2+) |
| `BackupCoordinatorGrain` | `instanceId` | `IBackupTarget`, último `seq` | orquestra backup |
| `RestoreCoordinatorGrain` | `instanceId` | progresso | **somente dados** |
| `KeyRecoveryCoordinatorGrain` | `instanceId` | shares verificadas, `keyEpoch` | **somente material de chave** (doc 04) |
| `KeyGuardianGrain` | `(peerUri, keyEpoch)` | share cifrada, `retention=permanent` | guarda fragmento de terceiros (doc 04) |

> **Hot spot proibido:** `ContainerGrain` de comunidade com milhares de membros não pode serializar todos os posts. Post nasce em `ObjectGrain` (chave própria); o feed é projeção.

### 4.3 Write side: event sourcing com truncamento

Padrão do `orders-orleans`, **com a correção do log infinito**:

* `IPersistentState<List<ActivityEvent>>` + `IPersistentState<Snapshot>`.
* Snapshot a cada *threshold* de eventos.
* **`Sequence` monotônico por ator** — resolve outbox, delta sync mobile e (futuro) delta de backup.
* **Truncar o log após o snapshot.** Sem isso, o estado do grain cresce indefinidamente e o restore fica lento em anos. É requisito, não otimização.
* Payload JSON-LD guardado como `JsonNode`; apenas os campos consultados são tipados.

### 4.4 Read side: projeções Dapper

* Timeline, feed de comunidade, membros, notificações → tabelas de projeção, consultadas com Dapper.
* Projeção atualizada por um **Worker de projeção** lendo o log de eventos (não pelo grain no caminho da request).
* `?since=<seq>` para delta sync (mobile e cliente web).
* Toda query de leitura aplica filtro de visibilidade da política vigente.

### 4.5 O que sobrou do CQRS/MediatR

| Elemento do doc 02 | Destino |
|---------------------|---------|
| Command handler + MediatR | **Removido.** Command = método do grain |
| Query com EF `.AsNoTracking()` | **Removido** para leitura de feed; manteve-se EF só no ledger |
| Query complexa com Dapper | **Mantido e ampliado** (§4.4) |
| Domain Events | **Mantido** como `ActivityEvent` no log |

O CQRS permanece como **separação de caminhos** (escrita no grain, leitura na projeção), não como camada de mediadores.

### 4.6 Regra: o grain orquestra, o Worker computa

Nunca no scheduler do silo: Reed-Solomon, compressão, cifra de blobs, transcodificação, geração de thumbnail, reconstrução SSS.

Um grain que bloqueia a thread derruba a latência de **todo** o silo. O grain emite comando e aguarda; o Worker executa e devolve pelo inbox.

### 4.7 Padrões obrigatórios (correções herdadas da análise do orders-orleans)

Estas são as regras que evitam copiar os defeitos encontrados. Cada uma é **critério de aceite em code review**.

| # | Regra | Defeito que evita |
|---|-------|-------------------|
| 1 | `IGrainStorage` **deve** impor ETag (`WHERE Version = @Version`) e lançar `InconsistentStateException` | Escrita concorrente perdida em silêncio |
| 2 | Nunca hardcodar o nome do store no SQL | Todos os stores colapsando numa partição lógica |
| 3 | Truncar o log de eventos após snapshot | Crescimento infinito do estado |
| 4 | Volume nomeado para todo dado persistente no container | `compose down` apagando o banco |
| 5 | Imagem `runtime` no estágio final, não `sdk` | Imagem gigante e superfície de ataque |
| 6 | Nunca devolver `ex.ToString()` ao cliente | Vazamento de stack trace e caminho interno |
| 7 | Proibido `catch { }` silencioso; usar dead-letter + alerta | Falha invisível de compensação |
| 8 | Proibido valor "de teste" em regra de domínio (ex.: estoque 0 = ilimitado) | Falha de segurança (quota infinita) |
| 9 | Dinheiro/quota **nunca** em `double`; usar `long` (bytes) ou `decimal` serializado como string | Perda de precisão em ledger |
| 10 | Binário (mídia, shard) **nunca** dentro do JSON de estado | Blob inflado, memória estourada |
| 11 | Não definir `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` em produção | Quebra ordenação e casing em rede multilíngue |
| 12 | Sanitizar PII em **logs e em span tags** | PII vazando por atributo de trace |

---

## 5. Chaves, Criptografia e Recuperação

### 5.1 Hierarquia (detalhe completo no doc 04)

```
Root Key (RK)  ← random 32 bytes
   ├── HKDF("fed/ed25519-sign")     → identidade / HTTP Signatures
   ├── HKDF("fed/x25519-static")    → acordo de chaves
   ├── HKDF("fed/backup-kek")       → embrulha Data Keys
   └── HKDF("fed/guardian-at-rest") → cifra shares de terceiros
```

**`RC` (Credencial de Recuperação) é independente** — não deriva de `RK`, porque precisa funcionar exatamente quando `RK` não existe.

### 5.2 Escopo criptográfico do MVP

| Necessidade | MVP | Observação |
|-------------|-----|------------|
| Assinatura de servidor (AP) | RSA-SHA256 na saída e na verificação de entrada; Ed25519 adicional (RFC 9421) | ADR-28; par RSA por ator embrulhado pela `RK` |
| TLS | Caddy com Let's Encrypt | Obrigatório |
| Cifra em repouso do banco | Sim | SQLCipher/ATP TDE |
| Cifra do backup na origem | Sim | ChaCha20-Poly1305 + `DK` por backup |
| E2EE de mensagem | **Não** | Fase 3 (MLS). Registrado em §2.5 |

### 5.3 Camadas de recuperação (resumo)

| Camada | O que é | Fase |
|--------|---------|------|
| **0** | Kit offline: `RK` cifrada (Argon2id) + mnemônico 24 palavras + `RC` | **MVP — obrigatória** |
| **1** | SSS 3-de-5 entre guardiões independentes | 2 |
| **2** | Escrow com 2FA + janela de 72h (autorização, nunca chave) | 3 |
| **3** | Threshold KMS/OPRF | Futuro |

**Princípio:** nenhuma camada isolada produz a `RK`. E-mail/SMS são **alarme, não fechadura**.

### 5.4 Fase 0 — backup sem rede

```
BackupCoordinatorGrain → IBackupTarget
                          ├── LocalDiskTarget      ← MVP
                          ├── S3CompatibleTarget   ← cópia off-site
                          └── PeerShardingTarget   ← fase 2+
```

**Regra:** o backup é cifrado na origem, portanto **o destino não precisa ser confiável**. Isso permite usar qualquer bucket/HD/disco físico.

**Restore drill obrigatório em CI:** criar → backup → destruir → restaurar → comparar hashes.

---

## 6. Backup Distribuído (fase 2+)

### 6.1 O que fica pronto agora

* `IBackupTarget` como plugin (o coordenador não sabe onde o blob vai).
* Envelope encryption e `keyEpoch` (permite rotação depois sem retrabalho).
* `Sequence` monotônico (o delta é `WHERE seq > @last`).
* Content-addressing SHA-256 (mesma primitiva do shard).
* Ledger de quota em inteiro (só é populado quando houver peer).

### 6.2 O que fica para quando houver rede

* Reed-Solomon (N/K) e o contrato `BackupShardOffer`.
* `QuotaGrain` com fair play e prova de armazenamento.
* Guardiões SSS reais (3-de-5+), heartbeat e re-share anual.
* Transporte de shard **binário** (`multipart`/`Content-Range`), nunca Base64 em JSON-LD.

### 6.3 Registro de honestidade

Com cifra na origem, o **Mutual Sharding não é necessário para segurança** — apenas para *custo* e para a tese de solidariedade de infraestrutura. O diferencial é "não depender de S3 pago nem de provedor", **não** "seria impossível de outra forma". Registrar isso protege o projeto de uma crítica fácil.

---

## 7. Infraestrutura

### 7.1 Oracle Cloud Always Free

| Recurso | Capacidade | Uso |
|---------|-----------|-----|
| Ampere A1 (ARM) | 4 OCPU / 24 GB total | 1–2 VMs |
| ATP / Autonomous JSON | 2 bancos, 20 GB | Fase 2 (MVP usa SQLite) |
| Block storage | 200 GB total | Blobs de mídia + backups locais |
| Egress | 10 TB/mês | Federação e mídia |

**⚠️ Armadilha crítica:** as duas VMs ficam na **mesma conta e normalmente na mesma região**. Suspensão de tenancy ou queda de região apaga ambas. **Nenhuma cópia de sobrevivência pode residir dentro do perímetro da conta Oracle.** A cópia off-site sai para bucket de terceiro, HD físico ou disco de terceiro — cifrada.

### 7.2 Podman + systemd

| Prática | Regra |
|---------|-------|
| Containerfile multi-stage | Estágio final = imagem `runtime`, com usuário não-root |
| Volumes | **Nomeados e explícitos** para banco, blobs e backups |
| Rootless | Containers sem privilégio |
| Restart | Quadlet/unit `systemd` com `Restart=always` |
| Rede | Portas internas fechadas; só Caddy exposto |
| Logs | `journald` + OTel, não arquivo local em container efêmero |

### 7.3 Topologia por fase

| Fase | VMs | O que roda | Recuperação |
|------|-----|-----------|-------------|
| **MVP** | 1 ARM (2 OCPU / 12 GB) | Caddy + API/Silo + Worker + Nuxt | Camada 0 + backup local + off-site |
| **1** | 2 ARM | Acima + Worker isolado | + restore drill em CI |
| **2** | 2 ARM (+1 opcional) | + peer federado | + SSS 3-de-5 |

### 7.4 Ambientes

| Ambiente | Onde | Banco | Federação |
|----------|------|-------|-----------|
| **dev** | Local (Windows/WSL) | SQLite arquivo | Desligada / `PeerClient` |
| **ci** | GitHub Actions | SQLite em memória/arquivo | `PeerClient` |
| **prod** | Oracle ARM | SQLite ✅ → ATP depois | Ligada |

---

## 8. Observabilidade e Operação

| Elemento | Decisão |
|----------|---------|
| Tracing | OTel com `ActivitySource` por camada (`Grains`, `Federation`, `Media`, `Backup`), exporter **OTLP** |
| Span por delivery federado | Tags: `peer.domain`, `activity.type`, `attempt`, `statusCode` |
| Logs | Serilog estruturado + destructuring policies para PII |
| Sanitização | Em **logs e em span tags** (payload AP vai para tag e vaza igual) |
| Dashboard | Orleans Dashboard na rede interna: ativações, filas, falhas por grain |
| Health | `/health` (liveness) e `/ready` (dependências: storage, blobs) |
| Métricas de produto | posts/dia, entregas pendentes, DLQ size, latência P50/P99 de timeline |
| Redundancy score | "Redundância: 1/3 — sem recuperação de desastre" (doc 04 §8.5) |

**Runbooks mínimos (escrever no Sprint 0):** restaurar backup, rotacionar chave, peer com entrega travada, DLQ cheia, disco cheio.

---

## 9. Segurança e Privacidade

| Área | Decisão |
|------|---------|
| Autenticação | Senha (Argon2id) + sessão HttpOnly no MVP, **sem e-mail/telefone**; passkey fase 2; OIDC de instância fase 3 (ADR-36) |
| Autorização | Motor de políticas, decidido **no grain** (§2.4) |
| Federação é hostil | Validação de payload, limites de tamanho, rate limit por peer, assinatura verificada |
| Upload | Validação de MIME real (magic bytes), limite por tipo, sem execução, EXIF removido |
| PII | Sanitização em log/span; retenção definida; direito a exclusão |
| Segredos | Fora do repositório; `RC` e `RK` só no kit e no storage seguro |
| Auditoria | Toda decisão de política registrada com `policyVersion` |

**O que o MVP *não* protege (registrar no termo de uso):** mensagens são legíveis pelo operador; metadados de federação são públicos; não há forward secrecy; não há anonimato contra a instância.

---

## 10. Qualidade e Testes

### 10.1 Pirâmide (herdada e corrigida)

| Camada | Ferramenta | O que cobre |
|--------|-----------|-------------|
| Unit — grains | xUnit + `MockPersistentState` | regras, políticas, transições |
| Unit — política | xUnit | matriz de capacidades (papel × ação) |
| Unit — adapters | xUnit | round-trip canônico ↔ JSON-LD |
| Integração | `Orleans TestingHost` + canal real | fluxo completo de um silo |
| Integração — dois silos | TestingHost × 2 | federação de verdade, local |
| Edge cases | xUnit | payload AP malformado, null/whitespace, unicode, tamanho |
| State machine | xUnit | transições inválidas de papel e de post |
| Storage | SQLite real | ETag, concorrência, truncamento de log |
| Restore drill | script em CI | backup → destruir → restaurar → hash |
| Load | script P50/P99 | latência de timeline |
| E2E | Robot/PyDoll (fase posterior) | jornada do usuário |

### 10.2 DoD global (todo épico)

1. Testes das camadas aplicáveis verdes.
2. Trace e log sanitizado no fluxo novo.
3. Política de acesso coberta por teste (positivo **e** negativo).
4. Migração/versionamento de schema quando muda estado.
5. Documento atualizado (este arquivo ou o detalhamento correspondente).
6. Nenhuma regra nova que viole §4.7.

---

## 11. Estrutura do Repositório

```
backend/                        # solution .NET: silo, API, worker e cliente de teste
├── Squid.slnx
├── Directory.Build.props       # TFM net10.0
├── Directory.Packages.props    # versões centralizadas (central package management)
├── Directory.Build.targets     # regra de dependência violada = erro de build
├── nuget.config                # fonte local versionada (rede fechada já foi bloqueio real)
├── src/
│   ├── Squid.Domain/           # Modelo canônico: Actor, Container, Object, Policy, Attachment
│   │                           #   — sem Orleans, sem JSON-LD, sem I/O
│   ├── Squid.Grains/           # Grains + Resilience/ (Polly); não referencia banco
│   ├── Squid.Persistence/      # IGrainStorage SQLite/Oracle, IDbConnectionFactory, ledger + migrations
│   ├── Squid.Protocols/        # IFederationProtocol + ActivityPubAdapter (+ futuros)
│   ├── Squid.ReadModels/       # Projeções + queries Dapper (via IDbConnectionFactory)
│   ├── Squid.Api/              # Minimal APIs, endpoints AP, SSE, auth
│   ├── Squid.Service/          # Composition root: Silo + Api + Health + Dashboard
│   ├── Squid.Worker/           # Mídia, backup, cifra, Reed-Solomon (fase 2)
│   └── Squid.PeerClient/       # Console: simula instância remota e falhas
└── tests/
    ├── Squid.Tests/            # Unit + integração + dois silos
    └── integration/            # scripts de QA executáveis sem daemon

web/                            # Nuxt 3 (SSR, OpenGraph)
app/                            # Vue 3 + Capacitor (fase posterior — não criar antes dela)
infra/                          # Containerfiles, compose.yaml, quadlets, Caddyfile
docs/features/                  # 00..06
docs/sdd/                       # plano, specs, tarefas, QA e tools do fluxo odev
```

**Regra de dependência:** `Domain` não referencia nada de infraestrutura. `Grains` referencia `Domain`. `Api` referencia `Grains` e `ReadModels`. O adapter de protocolo é **substituível** — e a regra é verificada pelo build (`Directory.Build.targets`), não por revisão (ADR-25).

**Raiz, não `code/`.** O produto mora na raiz do repositório, ao lado de `docs/` (ADR-21). Não versionado: `ideias-references/` — os checkouts de Mastodon, Friendica e `orders-orleans` usados como referência, 543 MB em ~15.9k arquivos —, além de `bin/`, `obj/`, `node_modules/`, bancos SQLite locais e `.env`. O que é publicável e o que é privado se decide na fronteira do git, não na profundidade da pasta (ADR-26).

---

## 12. Roadmap e Primeiros Passos

### 12.1 Como expandir este documento com IA (leia antes de codar)

1. **Uma sprint por vez.** Nunca peça "implemente o projeto". Peça "implemente a tarefa X do Sprint N".
2. **O documento é a fonte da verdade.** Se a implementação exigir desviar, atualize o documento **antes** do código.
3. **Registre ADRs.** Toda decisão não-trivial vira linha na §13. Sem ADR, a decisão não existe.
4. **Fases, não saltos.** O não-objetivo da §1.3 é a proteção contra o entusiasmo da IA.
5. **Teste antes de avançar.** Cada tarefa termina com teste; o DoD da §10.2 é o portão.
6. **Diff pequeno.** Se um PR toca 4 projetos e 3 sprints, reduza.
7. **Desconfie de infraestrutura nova.** Se a IA propuser Redis, Kafka ou K8s, a resposta padrão é não — até haver ADR assinado.

### 12.2 Sprints

---

#### Sprint 0 — Fundação verificável

**Meta:** uma instância que sobe, responde health, persiste e é restaurável.

- [ ] `git init` na raiz + `.gitignore` (`ideias-references/`, `bin/`, `obj/`, `node_modules/`, `*.db`, `.env`)
- [ ] `backend/Squid.slnx` com os 10 projetos (§11) e regras de dependência verificadas no build
- [ ] `IGrainStorage` SQLite com **ETag imposto** (§4.7 #1, #2)
- [ ] `Domain` com `ContainerPolicy` e `IPolicyEvaluator` + testes da matriz
- [ ] Silo + `/health` + `/ready` + Orleans Dashboard
- [ ] OTel com exporter OTLP + Serilog com sanitização
- [ ] Containerfile multi-stage com volumes nomeados
- [ ] `infra/compose.yaml` — `podman compose up` de dev (ADR-23)
- [ ] CI: build + testes
- [ ] Runbooks iniciais (§8)

**DoD:** `podman compose up` → `/health` verde → Dashboard acessível → trace de uma request visível no coletor.

---

#### Sprint 1 — Identidade

**Meta:** existe um ator local com perfil, chaves e descoberta.

- [ ] `ActorGrain` com event sourcing + snapshot + **truncamento**
- [ ] Geração de `RK` e `RC`; hierarquia HKDF (§5.1)
- [ ] `POST /api/actors` (registro) + Argon2id
- [ ] `GET /.well-known/webfinger` e `/users/{name}`
- [ ] Kit de recuperação exportável (camada 0)
- [ ] Teste de round-trip canônico ↔ JSON-LD do perfil

**DoD:** `webfinger` resolve; perfil é servido em JSON-LD; kit baixado restaura a `RK` em teste.

---

#### Sprint 2 — Contêineres sociais (grupos e comunidades)

**Meta:** criar, entrar, e ter hierarquia funcionando.

- [ ] `ContainerGrain` com `kind`, `ContainerPolicy` versionada, `Membership`
- [ ] Presets de Grupo e de Comunidade (§2.3)
- [ ] Fluxo de convite / request / open
- [ ] Gestão de papéis (Owner→Admin→Moderator→Member→Guest)
- [ ] Matriz de capacidades testada (positivo e negativo)
- [ ] Auditoria de decisões de política
- [ ] Sucessão de propriedade (sucessor, claim por inatividade) e override do operador

**DoD:** um grupo `invite` só entra por convite; `singlePublisher` bloqueia `Member` e permite o publisher nomeado; contêiner nunca fica órfão (sucessão e override cobertos por teste).

---

#### Sprint 3 — Publicação e feed

**Meta:** postar, ver, e receber ao vivo.

- [ ] `ObjectGrain` + `ActivityEvent` + `seq` monotônico
- [ ] Projeção de timeline com Dapper + filtro de visibilidade
- [ ] `GET /timeline?since=<seq>`
- [ ] SSE via `Channel<T>` + `IAsyncEnumerable`
- [ ] Texto, link (unfurl) e imagem (upload + thumbnail no Worker)
- [ ] EXIF stripping e `blurhash`
- [ ] Load test P50/P99 de timeline

**DoD:** post em comunidade aparece no feed em < 1s via SSE; `since` entrega só o delta; unlisted não aparece para não-membro.

---

#### Sprint 4 — Mensageria

**Meta:** conversa 1-1 e em grupo, sem E2EE (registrado).

- [ ] `ThreadGrain` com ordem total
- [ ] Envio/recebimento com política de participantes
- [ ] Aviso explícito na UI de que não há E2EE (§2.5)
- [ ] Marcação de leitura e paginação
- [ ] Resposta a mensagem (`ReplyToId` na thread)
- [ ] Reação em mensagem (`Like` padrão + emoji com whitelist)
- [ ] Testes de ordem e de acesso indevido

**DoD:** dois usuários conversam; terceiro não lê; ordem estável sob envio concorrente; resposta e reação visíveis só aos participantes e idempotentes.

---

#### Sprint 5 — Federação

**Meta:** duas instâncias trocam atividades de verdade.

- [ ] `IFederationProtocol` + `ActivityPubAdapter`
- [ ] `InboxGrain` com dedupe por `activity.id`
- [ ] `PeerGrain` com fila, backoff, circuit breaker, DLQ
- [ ] Assinatura HTTP (RSA-SHA256 na saída; `rsa-sha256`/`hs2019` e RFC 9421 na entrada, ADR-28)
- [ ] Regras de saída federada por visibilidade (§3.2)
- [ ] `PeerClient` simulando falha (500, timeout, lentidão)
- [ ] Teste de integração com **dois silos**
- [ ] Interop real: Mastodon e Friendica aceitam a entrega assinada (RSA-SHA256) e suas atividades são verificadas na entrada (ADR-28)

**DoD:** post público de A aparece em B; post `membersOnly` **não** aparece; dedupe rejeita entrega repetida; peer derrubado não trava o envio; Mastodon e Friendica reais exibem o post público e nenhuma entrega falha por assinatura.

---

#### Sprint 6 — Backup e recuperação (fase 0 completa)

- [ ] `BackupCoordinatorGrain` + `IBackupTarget` + `LocalDiskTarget`
- [ ] Envelope encryption + `keyEpoch`
- [ ] Restore drill no CI
- [ ] `S3CompatibleTarget` (off-site fora do perímetro Oracle)
- [ ] Redundancy score no painel

**DoD:** CI destrói e restaura, hashes conferem. Off-site restaura sem o disco local.

---

#### Sprint 7 — Frontend web e compartilhamento externo

- [ ] Nuxt com SSR e OpenGraph/oEmbed completos
- [ ] Share sheet / deep links (§3.4)
- [ ] Login com cookie HttpOnly no SSR
- [ ] Timeline, grupos, comunidades, mensageria
- [ ] App Links / universal links para abrir post direto

**DoD:** link de post abre preview com imagem no WhatsApp e no Telegram; share sheet funciona no Android.

---

#### Sprint 8+ — Pós-MVP

Fase 2: SSS entre guardiões, Mutual Sharding, Reed-Solomon, AT Protocol, Capacitor, E2EE (fase 3).

### 12.3 Backlog estrutural (fora do MVP, não esquecer)

Mutual Sharding · SSS/heartbeat/re-share · escrow 2FA · MLS · AT Protocol · app mobile · E2E · cartões OpenGraph por tipo de mídia · busca · notificações push · federação de contêineres · bots/automação de primeira classe (feature 002: ator `Service`, token com escopo, webhook assinado) · eventos/metas/enquetes (feature 003: `Object.Kind` + agregado `Response`).

---

## 13. Registro de Decisões (ADR consolidado)

| # | Decisão | Origem | Alternativa rejeitada |
|---|---------|--------|----------------------|
| 1 | Orleans só na camada de objetos federados | 03 | DDD + concorrência otimista manual |
| 2 | Event sourcing como outbox e fonte de delta | 03 | Query de delta por janela de tempo |
| 3 | EF Core só em ledger/migrations; escrita via `IPersistentState` | 03 | EF Core para o agregado inteiro |
| 4 | MediatR fora do domínio | 03 | MediatR + grain (dispatch duplo) |
| 5 | Dapper para toda leitura de feed | 02/03 | EF Core com tracking |
| 6 | SQLite no MVP; ATP depois | 03 | ATP desde o início |
| 7 | RabbitMQ adiado; `PeerGrain` + DLQ no MVP | 03 | RabbitMQ desde o Sprint 1 |
| 8 | gRPC **não** na federação; REST/JSON-LD | 03 | gRPC como API principal |
| 9 | Um agregado `Container` com dois presets | 05 | `Group` e `Community` separados |
| 10 | Política de acesso como dado + motor único | 05 | `if (role == Admin)` espalhado |
| 11 | Modelo canônico + adapter, nunca JSON-LD no domínio | 05 | Domínio falando JSON-LD |
| 12 | MVP sem E2EE, declarado na UI | 05 | E2EE parcial/enganosa |
| 13 | Compartilhamento externo = link + preview + share sheet | 05 | Integrações de API em redes fechadas |
| 14 | `RK` + HKDF; SSS só sobre a `RK` | 04 | Tratar "chave" como um único objeto |
| 15 | `RC` independente registrada no setup | 04 | DNS como única autenticação |
| 16 | Guardião = nó self-hosted, nunca celular | 04 | SQLite de app de usuário |
| 17 | Camada 0 obrigatória; SSS é fail-safe | 04 | SSS como única rota |
| 18 | Backup plugável por `IBackupTarget` | 04 | Sharding acoplado ao core |
| 19 | Heartbeat + isenção de TTL + manifesto de hash | 04 | SSS puro sem verificação |
| 20 | Grain orquestra, Worker computa | 03/04 | CPU pesada no scheduler |
| 21 | Monorepo único com o código na **raiz**; `ideias-references/` fora do versionamento | conversa 11-02 | `code/` como unidade de publicação; vários repositórios |
| 22 | Prefixo `Squid.*` em solution, projetos e namespaces; pacote NuGet publicável como `SquidNet.*` | conversa 11-02 | manter `Federation.*` (nome do experimento) |
| 23 | `podman compose` é o caminho de stack completa; Aspire adiado até haver 4–5 recursos simultâneos | conversa 11-02 | Aspire AppHost desde a Sprint 0 |
| 24 | Inner loop **sem daemon** (`dotnet run` + `pnpm dev` + `curl`) é o caminho primário; container é verificação | conversa 11-02 | container como ambiente de desenvolvimento |
| 25 | Central package management; regra de dependência entre projetos verificada pelo build | conversa 11-02 | versão por csproj; regra garantida por revisão |
| 26 | Publicação do repositório adiada; quando decidida, `git subtree` separa `docs/sdd` | conversa 11-02 | dois repositórios desde o início |
| 27 | Spec Kit roda em repositório **separado**, alimentado por cópia unidirecional das especificações; o estado canônico de execução permanece no odev | conversa 11-02 | speckit e odev compartilhando o mesmo estado |
| 28 | Interop real com Mastodon e Friendica é critério do Sprint 5: saída assinada com RSA-SHA256, verificação de entrada aceitando `rsa-sha256`/`hs2019` e RFC 9421, Ed25519 como chave adicional | spec-kit 001 (D1) | Só dois silos Squid; interop adiada |
| 29 | `ideias-references/` é entrada de validação, não fonte de decisão; decisões do repositório de execução não são importadas | spec-kit 001 (D4) | Adotar as decisões do `federation/` como canônicas |
| 30 | Bot é ator `Service` com chaves próprias, autenticado por token com escopo; webhook de saída reusa a mecânica de `PeerDelivery` (fila, backoff, DLQ) — sem agregado nem subsistema novo | spec-kit 002 (parked) | Subsistema de bots separado; webhook como infraestrutura nova |
| 31 | Interações ricas (evento, meta, enquete) são `Object.Kind` com respostas no agregado `Response` por ator; `Event`/`Question` usam AS2, metas usam `squid:Objective`; voto e RSVP federados não são prometidos | spec-kit 003 (parked) | Um agregado por tipo (`EventGrain`/`PollGrain`); voto como campo no `Object` |
| 32 | E2EE não é dividido entre DM e grupo: quando entrar, é MLS (RFC 9420) para os dois, com chave por dispositivo fora da árvore da `RK`; DM é grupo de 2 | spec-kit 004 (parked) | E2EE só em DM + par estático no grupo |
| 33 | Resposta a mensagem e reação entram no MVP: reação é `Reaction` (evento) com contagem derivada, `Like` federado e emoji customizado por extensão com whitelist; corpo de mensagem fica opaco e versionado para a 004 | spec-kit 001 (opção B) | Reação como coluna no `Message`; deixar resposta/reação para a 004 |
| 34 | Propriedade do contêiner: Owner único no MVP, com sucessor nomeado, claim após 90 dias de inatividade e override auditado do operador; a fonte canônica é `Membership.Role=Owner`, então co-ownership depois só relaxa a invariante | spec-kit 001 (opção A + prévia da B) | Múltiplos owners já no MVP |
| 35 | Camada de dados em `Squid.Persistence`: `IGrainStorage` SQLite/Oracle, `IDbConnectionFactory` e ledger EF Core; `Grains` não referencia banco, composição no `Service` e troca por configuração com suíte de contratos | spec-kit 001 (revisão de arquitetura) | Storage dentro de `Squid.Grains`; um projeto por banco |
| 36 | Autenticação em degraus: MVP só senha (Argon2id) + sessão, sem e-mail/telefone e sem reset por e-mail (recuperação é o kit); fase 2 passkey/WebAuthn; fase 3 OIDC como SSO de instância, nunca login social obrigatório | spec-kit 001 (revisão de autenticação) | E-mail + senha com reset; OIDC Google/Microsoft no MVP |

---

## 14. GAPs Consolidados

| # | GAP | Bloqueia | Prioridade |
|---|-----|----------|-----------|
| 1 | **Identidade pós-recuperação:** Ed25519 derivada de `RK` (continuidade) ou nova (re-atestação)? | Sprint 1 | **Bloqueante** |
| 2 | **Interop RSA vs Ed25519** nas HTTP Signatures | Sprint 5 | Alta |
| 3 | **Autenticação:** senha + sessão, ou passkey/OIDC? Impacta Nuxt SSR (CSRF/XSS) | Sprint 1 | Alta |
| 4 | **Contrato `BackupShardOffer`** em JSON-LD | Fase 2 | Média |
| 5 | **Biblioteca Reed-Solomon** performática em .NET | Fase 2 | Média |
| 6 | **Biblioteca SSS** e escolha de campo (GF(256) vs primo) | Fase 2 | Média |
| 7 | **Vocabulário JSON-LD** de contêineres (`Group` estendido) | Sprint 2 | Alta |
| 8 | **Chaves de usuário / E2EE** — fora de escopo, decidir comunicação | Fase 3 | Média |
| 9 | **Ratificação de política em fan-out** — reavaliar na entrega | Sprint 5 | Alta |
| 10 | **Quota de mídia por instância** (limite de disco × sharding) | Sprint 3 | Média |
| 11 | **Moderação de mídia** (conteúdo ilegal) — obrigação legal? | Sprint 3 | **Alta** |
| 12 | **Retenção e direito a exclusão** em conteúdo federado | Sprint 5 | Alta |
| 13 | **Nuxt SSR + token HttpOnly** sem XSS/CSRF | Sprint 7 | Alta |
| 14 | **Busca** em comunidades (SQLite FTS vs ATP) | Pós-MVP | Baixa |
| 15 | **Federação de contêineres privados** — o que trafega? | Sprint 5 | Média |

---

## 15. Riscos

| Risco | Prob. | Impacto | Mitigação |
|-------|-------|---------|-----------|
| **Escopo explodir** (3 camadas sociais + federação + sharding + 2 fronts + E2EE) | **Alta** | **Alto** | §1.3 não-objetivos + 1 sprint por vez + ADR obrigatório |
| Curva dupla Orleans + ActivityPub | Alta | Alto | Sprints separados (1–4 local, 5 federação) |
| Serialização Orleans consumir semanas | Alta | Médio | `JsonNode` para payload; `[Id(n)]` só em tipos estáveis |
| Federação travar por HTTP Signatures | Média | Alto | `PeerClient` + dois silos locais desde o Sprint 5 |
| Restore nunca testado | Média | **Crítico** | Restore drill no CI (Sprint 6) |
| Perda das 2 VMs Oracle juntas | Média | Crítico | Off-site fora do perímetro da conta (§7.1) |
| Moderação de conteúdo ignorada | Média | Alto (legal) | GAP #11 antes do Sprint 3 |
| Abstração de protocolo virar over-engineering | Média | Médio | Interface de 5 métodos, nada além |
| Dívida de documentação | Alta | Médio | §12.1 regra 2 — doc antes do código |

---

## 16. Manutenção deste documento

* **Changelog obrigatório** a cada sprint concluída (data, o que mudou, o que foi descoberto).
* **Nenhuma decisão informal.** Se está no código e não está aqui, é dívida.
* **Numeração estável.** Não renumerar seções — links de tarefas e de PRs apontam para elas.
* **O 02 permanece intacto** como registro histórico da ideia original. Divergência → vence o 05.

### Changelog

| Versão | Data | Mudança |
|--------|------|---------|
| 1.0 | 2026-10-30 | Consolidação inicial de 02 + 03 + 04; escopo MVP de mensageria/grupos/comunidades; camada de protocolos; compartilhamento externo; roadmap 0–8 |
| 1.1 | 2026-11-02 | Produto nomeado **Squid**; §11 reescrita (código na raiz, `backend/`, `app/`, `docs/sdd`); orquestração local, toolchain e fluxo de skills registrados em ADR-21..27; Sprint 0 alinhada |
| 1.2 | 2026-11-03 | D1–D4 do spec-kit 001: interop real Mastodon/Friendica no Sprint 5 (ADR-28); referências como validação (ADR-29); GAP 11 fechado (denúncia + fila humana + registro 6 meses, SLA 24h); §5.2 e §3.2 alinhados ao RSA obrigatório |
| 1.3 | 2026-11-03 | Feature 002 registrada (parked): bots/automação como ator `Service`, token com escopo, webhook assinado sobre `PeerDelivery` (ADR-30); §12.3 atualizado |
| 1.4 | 2026-11-03 | Feature 003 registrada (parked): eventos, metas e enquetes como `Object.Kind` + `Response` por ator (ADR-31); §12.3 atualizado |
| 1.5 | 2026-11-03 | Mensageria do MVP ganha resposta a mensagem e reação (ADR-33); contratos, FR-057/058 e SC-017; corpo de mensagem preparado (opaco) para a 004 |
| 1.6 | 2026-11-03 | Camada de dados extraída para `Squid.Persistence` (ADR-35); propriedade com sucessão, claim e override do operador (ADR-34, FR-059..061); operador global múltiplo |
| 1.7 | 2026-11-03 | Autenticação em degraus registrada (ADR-36): sem e-mail/telefone e sem reset por e-mail no MVP, passkey na fase 2, OIDC SSO na fase 3; FR-062 e premissa explícita |
| 1.8 | 2026-11-04 | Revisão de consistência: §11 alinhada ao ADR-35 (`Squid.Persistence`); spec com FR-063 (edição de perfil) e FR-057..062 nas seções corretas |