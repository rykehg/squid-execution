# Documento 03 — Aproveitamento do Orders-Orleans na Arquitetura da POC

> **Natureza deste documento:** é um *delta* em relação ao [`02-arquitetura-poc.md`](02-arquitetura-poc.md). Não substitui o documento 02 — ele revisa seções específicas e reaproveita padrões comprovados do projeto de referência [`orders-orleans`](../../ideias-references/orders-orleans/README.md).
>
> **Base analisada:** `ideias-references/orders-orleans` — Orleans 10.2.0, .NET 10, gRPC, SQLite custom storage, Event Sourcing + Snapshots, Polly, OpenTelemetry, Channels, Saga compensation, 184 testes.

---

## 1. Resumo Executivo (TL;DR)

**Veredito:** o Orders-Orleans não é uma arquitetura concorrente da sua — é uma **arquitetura de execução** diferente (virtual actors + event sourcing) aplicada a um domínio mais simples (pedidos). Cerca de **70% do documento 02 permanece intacto**; o que muda é a *camada de escrita do domínio federado* e a *orquestração do backup*.

O que realmente vale a pena trazer:

| # | Item | Veredito | Impacto |
|---|------|----------|---------|
| 1 | **Grain keyed pelo URI ActivityPub** | ✅ Adotar | Resolve ordenação, dedupe e idempotência de graça |
| 2 | **Event Sourcing como outbox + fonte do backup** | ✅ Adotar | Um mecanismo resolve 3 gaps do doc 02 |
| 3 | **`IGrainStorage` genérico (relacional + coluna JSON)** | 🔧 Adaptar | Substitui EF Core no write-side; ATP fica plugável |
| 4 | **`Channel<T>` + `IAsyncEnumerable`** | 🔧 Adaptar | Vira SSE para a timeline + delta sync do mobile |
| 5 | **Polly `ResiliencePipeline`** | ✅ Adotar | Cópia quase direta para chamadas entre instâncias |
| 6 | **`ActivitySource` + Serilog** | 🔧 Adaptar | Já está no doc 02; ganha granularidade por grain |
| 7 | **Orleans Reminders** | ✅ Adotar | Substitui cron do backup (sem execução dupla em 2 VMs) |
| 8 | **Orleans Dashboard** | 🔧 Adaptar | Observabilidade operacional grátis na POC |
| 9 | **Pirâmide de testes (184 testes em 8 camadas)** | ✅ Adotar | Receita pronta para os gaps de QA |
| 10 | **Cliente console + script de carga** | 🔧 Adaptar | Vira `PeerClient` para simular instâncias remotas |

O que **não** trazer: gRPC como API de federação, Saga compensation no fan-out, e os dez defeitos concretos listados na seção 7.

---

## 2. As Duas Arquiteturas Não São Incompatíveis

O que parece "completamente diferente" é, na prática, a troca do eixo de consistência:

| Eixo | Doc 02 (DDD + CQRS) | Orders-Orleans | Conflito real? |
|------|---------------------|----------------|----------------|
| Unidade de consistência | Agregado DDD + transação de banco | Grain (single-threaded por chave) | **Sim** — é aqui que se ganha |
| Persistência de escrita | EF Core + change tracking | `IPersistentState` (JSON serializado) | **Sim** — simplifica |
| Persistência de leitura | Dapper + DTOs | *Não existe* (o projeto não tem read models) | **Não** — mantenha o seu |
| Comunicação interna | MediatR (in-process) + RabbitMQ | Chamadas de grain diretas | **Sim** — MediatR fica redundante |
| Comunicação externa | REST + ActivityPub | gRPC | **Sim** — gRPC não serve |
| Duplicação de mensagem | Não tratada | Naturalmente evitada (grain = 1 por id) | **Ganho** |
| Orquestração de trabalho pesado | Worker Service + cron | Grain + Reminder | **Ganho** |
| Resiliência de rede | Polly (planejado) | Polly implementado e testado | **Reuso direto** |

**Conclusão:** o doc 02 descreve *o que* o sistema é (domínio, protocolo, cripto, infra). O Orders-Orleans descreve *como* executar comandos com segurança sob concorrência. São camadas diferentes e complementares.

---

## 3. O Que Aproveitar — Análise Detalhada

### 3.1 ✅ ADOTAR — Grain keyed pelo URI ActivityPub

**O que o projeto de referência faz:** [`IOrderGrain`](../../ideias-references/orders-orleans/src/Orders.Orleans/Grains/IOrderGrain.cs) usa `IGrainWithStringKey`; a chave é o `orderId`. Consequência: duas requisições concorrentes para o mesmo pedido são **serializadas pelo runtime**, sem lock, sem `SELECT FOR UPDATE`, sem `rowversion`.

**Por que isso é quase um encaixe perfeito para ActivityPub:** todo objeto do protocolo tem um **identificador global estável** — o próprio URI (`https://instancia/@usuario`, `https://instancia/users/ana/statuses/123`). Isso significa:

```
Grain key = URI canônico do objeto ActivityPub
```

Isso resolve, sem código adicional:

* **Ordem de escrita por objeto** — dois `Like` simultâneos no mesmo post não perdem contagem.
* **Dedupe de entrega** — o `InboxGrain` de um ator vê todas as activities endereçadas a ele em sequência: basta manter os últimos *N* `activity.id` e descartar repetição.
* **Idempotência de fan-out** — reenviar uma activity para o mesmo destino cai no mesmo grain, que reconhece o estado.
* **Hot object isolation** — um post viral não bloqueia o resto do banco; ele satura *apenas* o próprio grain.

**Mapeamento proposto:**

| Grain | Chave | Estado persistido | Responsabilidade |
|-------|-------|-------------------|------------------|
| `ActorGrain` | URI do actor | perfil, chaves públicas, `outboxSeq`, contadores | identidade local e cache de atores remotos |
| `InboxGrain` | URI do actor local | último `activity.id` processado, janela de dedupe, pendências | recepção idempotente |
| `ObjectGrain` | URI do `Note`/`Article` | contadores de like/boost, flags de moderação | agregado de engajamento |
| `CommunityGrain` | URI do `Group` | membros, papéis, metadados | moderação da comunidade (Orkut) |
| `PeerGrain` | domínio remoto | saúde, backoff, fila de entrega pendente | entrega assíncrona por par federado |
| `QuotaGrain` | `(localId, peerDomain)` | crédito/débito em **inteiro** | *fair play* do mutual sharding |
| `ShardHostGrain` | `shardId` | hash SHA-256, tamanho, TTL, owner | hospedagem de fragmento de terceiro |
| `BackupCoordinatorGrain` | `instanceId` | estado do ciclo, último `seq` exportado | orquestra snapshot → cifra → shard → distribui |
| `RestoreCoordinatorGrain` | `instanceId` | progresso da recuperação | remonta e reidrata |

> **⚠️ Cuidado com hot spot:** um `CommunityGrain` com 100 mil membros serializa *todos* os posts da comunidade. Regra: o grain da comunidade guarda **membresia e moderação**, nunca o feed. O post nasce no `ObjectGrain` (chave própria) e o feed é uma projeção de leitura append-only.

### 3.2 ✅ ADOTAR — Event Sourcing como outbox e como fonte do backup

**O que o projeto de referência faz:** [`OrderGrain`](../../ideias-references/orders-orleans/src/Orders.Orleans/Grains/OrderGrain.cs) guarda `IPersistentState<List<OrderEvent>>` + `IPersistentState<OrderSnapshot>`, com *threshold* de snapshot por contagem de eventos ([:189-193](../../ideias-references/orders-orleans/src/Orders.Orleans/Grains/OrderGrain.cs#L189-L193)). O estado é reconstruído por replay + eventos posteriores ao snapshot ([:72-102](../../ideias-references/orders-orleans/src/Orders.Orleans/Grains/OrderGrain.cs#L72-L102)).

**Por que isso é o item mais valioso da lista:** ActivityPub **é** um protocolo de log de atividades (`Outbox` = lista ordenada de activities). Um log de eventos por ator entrega simultaneamente:

1. **O `outbox` do protocolo** — a lista ordenada que outros servidores leem.
2. **A fonte do delta para o backup distribuído** — o gap do doc 02 diz "*query com Dapper (ou usa o log do EF Core) extraindo os dados novos*" (§3, passo 1). Com event sourcing, o delta é **exato e monotônico**: `WHERE seq > @ultimoSeqExportado`. Sem janela de tempo, sem risco de perder escrita entre snapshots.
3. **O delta sync do mobile** — o gap da §5 ("*como sincronizar a Timeline local com a API remota*") vira um endpoint `GET /timeline?since=<seq>`.

**Consequência arquitetural:** a timeline do usuário deixa de ser uma "query de delta" e passa a ser uma **projeção** do mesmo log. Um único invariante (`seq` monotônico por ator) sustenta outbox, backup e sincronização mobile.

```csharp
// Forma proposta — o mesmo padrão do OrderEvent, aplicado ao ActivityPub
[JsonDerivedType(typeof(ActorCreated), "Create")]
[JsonDerivedType(typeof(NotePublished), "Create")]
[JsonDerivedType(typeof(NoteDeleted),  "Delete")]
[JsonDerivedType(typeof(FollowAccepted), "Accept")]
[JsonDerivedType(typeof(AnnounceSent),  "Announce")]
[GenerateSerializer]
public abstract record ActivityEvent
{
    [Id(1)] public string ActorId { get; init; }        // URI do ator dono do log
    [Id(2)] public long   Sequence { get; init; }       // monotônico — chave do delta sync
    [Id(3)] public DateTime Timestamp { get; init; }
    [Id(4)] public string ActivityJson { get; init; }   // payload JSON-LD bruto (ver 3.3)
}
```

> **Ajuste obrigatório em relação à referência:** o `OrderGrain` **nunca trunca** o log após o snapshot — o `List<OrderEvent>` cresce para sempre no estado do grain. Em pedidos isso passa; em rede social, um ator ativo em 2 anos quebra o grain. Ver seção 7, item 3.

### 3.3 🔧 ADAPTAR — `IGrainStorage` genérico (relacional + coluna JSON)

**O que o projeto de referência faz:** [`SqliteGrainStorage`](../../ideias-references/orders-orleans/src/Orders.Orleans/Storage/SqliteGrainStorage.cs) implementa `IGrainStorage` com **uma única tabela**:

```sql
CREATE TABLE GrainStorage (
    StorageName TEXT, GrainId TEXT, StateName TEXT,
    Payload TEXT, Version INTEGER,
    PRIMARY KEY (StorageName, GrainId, StateName)
)
```

O estado inteiro do grain vai serializado em JSON numa coluna de texto.

**Isto é, na prática, exatamente a "modelagem híbrida" do doc 02 (§2), só que genérica:** metadados relacionais indexáveis (`StorageName`, `GrainId`, `StateName`, `Version`) + payload polimórfico em JSON. A diferença é que você **não precisa escrever uma entidade EF Core por tipo de objeto ActivityPub**.

**Adaptação recomendada:**

| Camada | Tecnologia | Por quê |
|--------|-----------|---------|
| Escrita de objetos federados | `IGrainStorage` (JSON) | Não precisa de schema por tipo; o payload AP é polimórfico por natureza |
| Leitura de feeds/timeline | Dapper + tabelas de projeção | Grain não serve para query — usaria o scheduler do silo à toa |
| Ledger de cotas, contas | EF Core (relacional, `decimal`/`long`) | Precisa de transação real e precisão exata |
| Backend do storage | **SQLite na POC → Oracle ATP depois** | A referência prova que o padrão funciona; trocar o backend é trocar uma classe |

**Ponto crítico sobre tipos fortemente tipados:** a referência modela um `record` por evento (5 tipos, com `[Id(n)]` de serialização Orleans). ActivityPub tem **dezenas** de tipos e evolui. Recomendação explícita:

> Guarde o payload JSON-LD como `JsonNode`/`string` no estado do grain e **projete apenas o que você consulta** (id, tipo, autor, data). Tipar 50 tipos AP em records Orleans é a receita para 50 quebras de compatibilidade binária.

### 3.4 🔧 ADAPTAR — `Channel<T>` + `IAsyncEnumerable` → SSE + delta sync

**O que o projeto de referência faz:** `OrderGrain` mantém uma lista de `Channel<OrderUpdate>` por assinante; `SubscribeToOrderUpdates()` devolve `IAsyncEnumerable` com **replay do histórico** antes do stream ao vivo ([`OrderGrain.cs:314-341`](../../ideias-references/orders-orleans/src/Orders.Orleans/Grains/OrderGrain.cs#L314-L341)), e faz `TryComplete` + remoção do canal no `finally` (sem vazamento).

**Adaptação para a POC:** exatamente o mesmo desenho, mas o transporte externo é **SSE** (`text/event-stream`) em vez de gRPC server-streaming, porque o cliente é Nuxt/Vue no browser. O padrão vale para:

* Feed ao vivo de uma comunidade (`GET /communities/{id}/stream`).
* Status de processamento de backup/restore (progresso do `BackupCoordinatorGrain`).
* Notificações do inbox.

O `replay de histórico + live` é um diferencial: o usuário abre a página, recebe os últimos N eventos e continua recebendo os novos — sem polling.

### 3.5 ✅ ADOTAR — Polly `ResiliencePipeline`

Cópia quase direta de [`RetryPolicies.cs`](../../ideias-references/orders-orleans/src/Orders.Orleans/Resilience/RetryPolicies.cs): pipeline `Default` (3x exponencial + jitter + timeout 10s) e `Fast` (2x constante + timeout 5s).

Onde aplicar na POC (mais crítico que na referência, porque a rede é hostil):

| Chamada | Pipeline | Ajuste |
|---------|----------|--------|
| POST no inbox de instância remota | `Default` | + circuit breaker por `PeerGrain` |
| `WebFinger` / fetch de chave pública | `Fast` | Cache negativo no `PeerGrain` |
| Upload/Download de shard | `Default` | Timeout maior (blobs grandes) |
| Conexão Oracle ATP | `Default` | Retry em erro transitório |

**Adição necessária:** Polly 8 tem `ResiliencePipeline`, mas a referência só usa retry e timeout. Para federação faltam: **circuit breaker** (instância morta não deve consumir 3 tentativas por activity, para sempre) e **fallback para fila de dead-letter**.

### 3.6 🔧 ADAPTAR — `ActivitySource` + Serilog

A referência expõe `ActivitySource("Orders.Orleans")` ([`Telemetry.cs`](../../ideias-references/orders-orleans/src/Orders.Orleans/Telemetry.cs)) e inicia um span por método público do grain (`OrderGrain.CreateOrder`, `PayOrder`, etc.).

**O que manter do doc 02:** Serilog com *Destructuring Policies* para PII — isso a referência **não** tem e é mais valioso que o tracing, dado o risco de vazar conteúdo de mensagens.

**O que adaptar:**

1. **Um `ActivitySource` por camada**, não um global: `Federation.Grains`, `Federation.Federation` (HTTP de saída), `Federation.Backup`.
2. **Exporter:** a referência usa `AddConsoleExporter()` ([`Program.cs:9-12`](../../ideias-references/orders-orleans/src/Orders.Service/Program.cs#L9-L12)) — serve para demo, não para diagnóstico. Na POC use **OTLP** (Grafana/Tempo ou Jaeger) desde o início; é o item que mais economiza tempo depurando federação.
3. **Span por delivery federado:** um span `InboxDelivery` com tags `peer.domain`, `activity.type`, `attempt`, `statusCode`. Isso é o equivalente, para a federação, do que o span por grain é para a referência.
4. **Sanitização também em atributos de span**, não só em log — o payload AP vai para tags e vaza PII do mesmo jeito.

### 3.7 ✅ ADOTAR — Orleans Reminders para o ciclo de backup

**O que o projeto de referência *não* faz e você deveria fazer:** o doc 02 (§3, passo 1) prevê "*diariamente, um Worker Service em .NET*" e (§4) `systemd` com Podman. Com 2 VMs, isso tem um problema clássico: **execução dupla** ou *split-brain* se as duas VMs rodarem o timer.

**Orleans Reminders dão exatamente a semântica necessária:**

* Registro **durável** do agendamento (sobrevive a restart do silo).
* Disparo **único** por cluster, independente de quantas VMs/silos existam.
* Callback no `BackupCoordinatorGrain`, que já tem estado transacional — o job sabe **onde parou** (`último seq exportado`), então um disparo atrasado não duplica trabalho.

Desenho proposto (o mesmo vale para expurgo de TTL):

```
Reminder "backup-daily"    → BackupCoordinatorGrain.RunCycleAsync()
                            ├── 1. lê seq alto atual (leitura barata)
                            ├── 2. exporta delta [lastSeq, highSeq]  ← log de eventos
                            ├── 3. delega cifra + Reed-Solomon ao Worker (fora do silo)
                            ├── 4. distribui via PeerGrain (HTTP + Polly)
                            └── 5. grava lastSeq = highSeq (idempotente)

Reminder "ttl-sweep-hourly" → ShardHostGrain.EnforceTtlAsync() para cada shard
```

> **⚠️ Regra de ouro do Orleans que a referência não viola, mas a POC tende a violar:** **nunca** rode Reed-Solomon, compressão ou cifra de blob grande dentro do grain. Isso bloqueia o *scheduler* do silo e derruba a latência de todo o resto. O grain **orquestra**; o Worker (processo separado) **computa**. É a mesma separação que o doc 02 já propõe — só que agora formalizada.

### 3.8 🔧 ADAPTAR — Orleans Dashboard

A referência habilita `Microsoft.Orleans.Dashboard` na porta 8081 (`AddDashboard()` + `MapOrleansDashboard()`). Para uma POC federada, isso dá de graça algo que você não teria:

* Quantos grains estão **ativados** (memória real consumida por ator).
* Quais grains estão com **fila de chamadas** crescendo (hot spots).
* Taxa de falha por tipo de grain (útil para separar falha de negócio de falha de rede).

**Adaptação:** trate como ferramenta de **desenvolvimento e tuning**, não de produção — não tem autenticação. Exponha só na rede interna do Podman.

### 3.9 ✅ ADOTAR — A pirâmide de testes (isto é subestimado)

A referência tem **184 testes em 8 camadas nomeadas** (unit grains, handlers gRPC com Moq, integração com `Orleans TestingHost` + canal real, edge cases, state machine, snapshots, smoke, modelos de domínio). O doc 02 tem QA **apenas no nível E2E** (Robot Framework) — o que é o inverso do que a experiência em mercado financeiro recomenda.

Tradução direta para a POC:

| Camada da referência | Equivalente na POC | Por que importa aqui |
|---------------------|--------------------|----------------------|
| Unit grains (`MockPersistentState`) | `ActorGrain`/`InboxGrain` isolados | Testa dedupe sem subir silo |
| **State machine** (PayOrder em Shipped) | Transições de membresia (`Member → Banned → Member`) | Regras de comunidade são o coração do Orkut-like |
| **Edge cases** (null/empty/whitespace) | Validação de payload AP malformado | Federação recebe lixo de qualquer origem |
| Integração (`TestingHost` + canal real) | Dois silos se falando | Simula federação de verdade, local |
| **Snapshots** (threshold, replay) | Replay do log de atividades | Garante que restore reconstrói estado |
| Serialização round-trip | `JsonNode` do payload AP sobrevive a persist/reload | Maior fonte de bug em Orleans |
| Smoke (assemblies, tipos) | — | Dispensável na POC |

**Insight adicional:** `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` (usado em todos os comandos da referência) **não deve ir para produção** numa rede social multilíngue — ele desliga dados de cultura (ordenação, casing, formatos). É conveniência de CI/container deles, não uma decisão de arquitetura.

### 3.10 🔧 ADAPTAR — `Orders.Client` + `load-test.ps1` → `PeerClient`

A referência tem um console app cliente (`Orders.Client`) que executa o fluxo completo com 5 asserts, e um `load-test.ps1` que mede P50/P99 e throughput.

**Adaptação valiosa:** transforme isso num **`Federation.PeerClient`** — um console que se passa por **outra instância** da federação:

```
Federation.PeerClient --as https://fake-peer.local
  ├── publica WebFinger falso
  ├── recebe inbox delivery e responde 202
  ├── simula falha (500, timeout, resposta lenta)  ← testa Polly/circuit breaker
  └── pede um shard de backup e valida o hash
```

Isso permite testar todo o caminho de federação e de backup **sem depender de uma segunda instância real** — o que, no Oracle Always Free com 2 VMs, economiza semanas. É a adaptação mais subestimada da lista.

E o `load-test.ps1` vira `feeds-load-test.ps1` medindo latência de timeline (P50/P99) — o número que decide se a projeção com Dapper está no caminho certo.

### 3.11 ❌ REJEITAR — Saga compensation aplicada ao fan-out federado

A referência implementa `CompensateShipFailureAsync` ([:465-505](../../ideias-references/orders-orleans/src/Orders.Orleans/Grains/OrderGrain.cs#L465-L505)): se o frete falha, estorna o pagamento e libera o estoque. Em pedidos, isso é correto — as ações são **compensáveis**.

**Em federação, não é.** Quando você publica um post e ele é entregue a 5 instâncias, 3 aceitam e 2 falham:

* Você **não pode** "desfazer" a entrega nas 3 que aceitaram.
* O post **não deve** ser cancelado porque 2 peers estão offline (é exatamente o comportamento esperado de uma rede federada).

**O padrão correto aqui é Outbox + retry durável + dead-letter**, não Saga:

```
Post publicado → evento no log (fonte da verdade)
              → PeerGrain por destino mantém fila de entrega com backoff
              → sucesso: marca entregue (idempotente por activity.id)
              → após N tentativas: dead-letter + alerta, sem apagar o post
```

A lição reaproveitável da referência é a **compensação parcial** (rollback só do que já foi reservado), não a compensação em si. Aplicada à federação: rollback **apenas** de recursos locais (ex.: cota de shard reservada antes do upload falhar).

---

## 4. Arquitetura Revisada (Mapa de Execução)

```
                        ┌──────────────────────────────────────┐
   HTTPS / JSON-LD      │      Federation.Service (Silo)       │
   WebFinger · Inbox ──►│  Minimal APIs (REST) + Health + Dash │
   Outbox · Shards      └──────────────────┬───────────────────┘
                                           │ chamada de grain (in-process)
        ┌──────────────────┬───────────────┼───────────────┬──────────────────┐
        │                  │               │               │                  │
  ┌─────▼─────┐    ┌───────▼────┐   ┌──────▼──────┐  ┌─────▼──────┐   ┌──────▼───────┐
  │ActorGrain │    │ InboxGrain │   │ ObjectGrain │  │ PeerGrain  │   │ QuotaGrain   │
  │outbox ES  │    │ dedupe     │   │ contadores  │  │ retry+back │   │ ledger (int) │
  └─────┬─────┘    └───────┬────┘   └──────┬──────┘  └─────┬──────┘   └──────┬───────┘
        │                  │               │               │                 │
        └──────────────────┴───────┬───────┴───────────────┴─────────────────┘
                                   │ IPersistentState (JSON)
                    ┌──────────────▼─────────────┐   ┌────────────────────────────┐
                    │  IGrainStorage             │   │  Read Models (SQL)         │
                    │  SQLite (POC) → Oracle ATP │   │  Dapper: timeline, feeds   │
                    └────────────────────────────┘   └────────────────────────────┘
                                   │
                    ┌──────────────▼─────────────┐   ┌────────────────────────────┐
                    │ Backup Worker (processo)   │   │ Blob store (shards)        │
                    │ Reed-Solomon + ChaCha20    │   │ filesystem local + TTL     │
                    └────────────────────────────┘   └────────────────────────────┘
```

### Fluxos principais

| Fluxo | Caminho | Padrão herdado |
|-------|---------|----------------|
| Criar conta | `POST /api/actors` → `ActorGrain.CreateAsync` | grain + validação `ArgumentException` |
| Publicar post | `POST /api/notes` → `ObjectGrain` → append no log do `ActorGrain` → enfileira em `PeerGrain` | event sourcing + outbox |
| Receber activity | `POST /inbox` → `InboxGrain.AcceptAsync` (dedupe por `activity.id`) | idempotência por grain |
| Entregar a peer | `PeerGrain.FlushAsync` → HTTP + Polly → dead-letter | resiliência + outbox |
| Timeline | Dapper sobre tabela de projeção (`seq` monotônico) | CQRS read side do doc 02 |
| Backup diário | Reminder → `BackupCoordinatorGrain` → Worker → `ShardHostGrain` remoto | reminder + orquestração |
| Restore | `RestoreCoordinatorGrain` → coleta N shards → recomputa → reidrata | idempotência por `seq` |

---

## 5. Modificações por Seção do Documento 02

### §2 — Arquitetura de Backend

| Item no doc 02 | Proposta | Justificativa |
|----------------|----------|---------------|
| `.NET 10` | **Manter** | A referência já está em .NET 10 com Orleans 10.2.0 — zero atrito |
| `DDD` | **Manter, redefinindo agregado** | Agregado = grain. O grain é a fronteira de consistência transacional, então o desenho de agregados fica *mais* disciplinado |
| `CQRS com MediatR` | **Remover MediatR do domínio** | MediatR + grain = dois mecanismos de dispatch. O command handler vira uma chamada de grain. MediatR só sobrevive se quiser pipeline behaviors na borda HTTP — e aí é opcional |
| `EF Core para escrita` | **Substituir por `IPersistentState`** | O write-side é JSON de payload polimórfico; EF Core só adiciona cerimônia (entidade por tipo + migrations por mudança de payload) |
| `Dapper para leitura` | **Manter — agora é o único caminho de leitura** | Grain não serve query de feed (serializa leitura e satura o scheduler) |
| `Oracle ATP` | **Manter como alvo, começar em SQLite** | Duas implementações de `IGrainStorage`; a POC valida com SQLite e a migração é uma classe |
| `RabbitMQ` para fan-out | **Reduzir escopo** | Fan-out local = grains + outbox. Cross-instance = HTTP durável + dead-letter. RabbitMQ entra quando precisar de desacoplamento durável (fila reprocessável), provavelmente Sprint 4+ |
| `RabbitMQ` para sharding | **Adiar / opcional** | O `PeerGrain` já é uma fila durável por par. RabbitMQ só se o volume justificar |
| `OpenTelemetry + Serilog` | **Manter + OTLP + span por delivery** | Console exporter não diagnostica federação |
| `ECC / X25519 / ChaCha20` | **Manter, mas separar as chaves** | Ver correção abaixo ⚠️ |

> **⚠️ Correção necessária no doc 02 (§2):** há **duas** famílias de chave em jogo e o documento as trata como uma só.
> * **Ed25519** (assinatura) → HTTP Signatures do ActivityPub, identidade do ator/servidor. Não é opcional: sem ela, Mastodon não aceita sua instância.
> * **X25519** (troca de chave) + ChaCha20-Poly1305 → E2EE de conteúdo (DMs).
>
> São pares de chave **distintos**, com ciclos de vida distintos. Além disso, para interoperar com o Mastodon real (que historicamente usa **RSA-SHA256**), a POC provavelmente precisa **suportar RSA na assinatura** mesmo adotando Ed25519 internamente. Vale como novo GAP explícito.

### §3 — Backups Distribuídos

| Item no doc 02 | Proposta |
|----------------|----------|
| "Worker Service diário" | **Reminder do Orleans** → `BackupCoordinatorGrain` (disparo único, durável, retomável) |
| "Query Dapper extraindo dados novos" | **Log de eventos com `seq`** — delta exato, monotônico, idempotente |
| "Criptografa com ChaCha20 via Libsodium" | Manter, **mas no Worker**, nunca no grain |
| "Reed-Solomon" | Manter no Worker. Grain só orquestra (evita bloquear scheduler) |
| "RabbitMQ Exchange de Backup" | `PeerGrain` + HTTP com Polly + dead-letter; RabbitMQ opcional |
| "Recepção valida cota e expurga por TTL" | `QuotaGrain` (ledger em inteiro) + `ShardHostGrain` (hash + TTL) + Reminder de varredura |
| "Base64 do shard dentro do JSON-LD" | ⚠️ **Repensar** — ver seção 7, item 5 |

**Novo requisito que emerge:** *proof of storage*. Se a Instância B aceita hospedar seu shard, como você sabe que ela **ainda** tem o dado? O `ShardHostGrain` deve armazenar o SHA-256 e responder a um `HEAD`/challenge periódico. Sem isso, o "TTL expurgado" pode ser indistinguível de "peer desonesto" — e você só descobre na hora do desastre.

### §4 — Infraestrutura

| Item no doc 02 | Proposta |
|----------------|----------|
| `Podman` + systemd | **Manter**. Use `Containerfile` multi-stage |
| Scripts `.sh` + OCI SDK | **Manter** |
| QA só em E2E (Robot/PyDoll) | **Inverter a ordem**: unit + integração de grain primeiro; E2E depois |
| *(não previsto)* | **Adicionar** Orleans Dashboard na rede interna |
| *(não previsto)* | **Adicionar** volume nomeado para os dados (a referência esquece — ver seção 7) |
| *(não previsto)* | **Definir clustering real**: cada instância federada é um **cluster Orleans próprio** (`ClusterId`/`ServiceId` distintos). `UseLocalhostClustering()` da referência não serve para 2 VMs |

### §5 — Frontend & Mobile

| Gap no doc 02 | Como o Orleans ajuda |
|---------------|----------------------|
| "*Delta sync da timeline SQLite ↔ API*" | Endpoint `?since=<seq>` sobre o log de eventos — deixa de ser um gap de design e vira um `WHERE` |
| "*Nuxt com SSR vs cliente*" | Sem impacto. SSE funciona nos dois |
| "*Estado ao vivo da timeline*" | `Channel<T>` + `IAsyncEnumerable` → SSE (padrão 3.4) |
| "*Segurança de chaves*" | Inalterado: Secure Storage + Ed25519 no device |

### §6 e §7 — Plano de Ação

O Sprint 1 do doc 02 continua válido (infra Oracle + Podman + ATP), mas a **estrutura da solution muda**: em vez de 4 projetos DDD, a referência sugere 5–6 projetos segregados por *camada de execução*, não por camada DDD.

**Estrutura proposta:**

```
src/
├── Federation.Core/          # Modelos AP (JsonNode), eventos, contratos
├── Federation.Grains/        # ActorGrain, InboxGrain, ObjectGrain, PeerGrain, QuotaGrain, ShardHostGrain
│   ├── Storage/              # IGrainStorage: SQLite + Oracle
│   └── Resilience/           # Polly pipelines
├── Federation.Federation/    # HTTP de saída: WebFinger, inbox delivery, shard transfer
├── Federation.Service/       # Silo + Minimal APIs + Health + Dashboard
├── Federation.Backup/        # Worker: Reed-Solomon + ChaCha20 (processo separado)
├── Federation.PeerClient/    # Console: simula instância remota
└── Federation.Tests/         # Unit + integração dois silos + edge cases + state machine
```

**Sprints revisados:**

| Sprint | Foco | Entrega verificável |
|--------|------|---------------------|
| 1 | Infra + esqueleto Orleans | Silo sobe, `/health` + Dashboard, `SqliteGrainStorage` **corrigido** |
| 2 | Identidade + `ActorGrain` | `POST /api/actors`, `/.well-known/webfinger` respondendo, chaves Ed25519 geradas |
| 3 | Inbox + dedupe | `POST /inbox` idempotente; teste com dois silos trocando activities |
| 4 | Outbox + fan-out | `PeerGrain` entregando com retry; `PeerClient` simulando falha |
| 5 | Timeline + SSE | Projeção Dapper + `?since=<seq>` + stream ao vivo |
| 6 | Backup | Reminder → Worker → Reed-Solomon → `ShardHostGrain`; restore validado |
| 7 | Cotas + TTL | `QuotaGrain` com ledger; varredura de TTL; proof-of-storage |
| 8 | Frontend | Nuxt SSR consumindo timeline; POC Capacitor |

---

## 6. Onde o Orleans Resolve Gaps do Doc 02 "de Graça"

Estes são gaps que o documento 02 lista como "a serem definidos" e que **desaparecem** com a mudança:

| Gap no doc 02 | Como é resolvido |
|---------------|------------------|
| "Como garantir ordem de escrita por objeto?" (implícito no fan-out) | Serialização por grain key |
| "Dedupe de entrega ActivityPub" (implícito) | Estado do `InboxGrain` |
| "Delta sync do mobile" (§5) | `seq` monotônico do log |
| "Delta do backup" (§3) | Mesmo `seq` |
| "Fair play de cotas" (§3) | `QuotaGrain` com ledger transacional em inteiro |
| "Disparo único de job em 2 VMs" (§4) | Reminder |
| "Como testar federação sem 2 instâncias" | `PeerClient` |

E os gaps que **permanecem** (o Orleans não ajuda):

* Esquema JSON-LD do `BackupShardOffer` (§3) — puro design de protocolo.
* Biblioteca de Reed-Solomon performática no .NET 10 — puro estudo de biblioteca.
* Interop RSA vs Ed25519 nas HTTP Signatures — puro estudo de protocolo.
* MLS para DMs em grupo — puro estudo de cripto.

---

## 7. O Que NÃO Trazer (Defeitos Encontrados na Referência)

A referência é sólida em design, mas tem problemas concretos que **não devem ser copiados**. Todos verificados no código:

| # | Problema | Local | Consequência se copiado |
|---|----------|-------|------------------------|
| 1 | `StorageName` é **hardcoded** como `"GrainStorage"` em vez de usar o nome do store | [`SqliteGrainStorage.cs:52`](../../ideias-references/orders-orleans/src/Orders.Orleans/Storage/SqliteGrainStorage.cs#L52), `:87`, `:107` | Os 4 stores (`orders`, `inventory`, `payments`, `shipments`) gravam na mesma partição lógica. Só funciona por sorte do `GrainId` incluir o tipo |
| 2 | `WriteStateAsync` **não valida o ETag** — o `UPDATE` não tem `WHERE Version = @Version` | [`SqliteGrainStorage.cs:80-95`](../../ideias-references/orders-orleans/src/Orders.Orleans/Storage/SqliteGrainStorage.cs#L80-L95) | Viola o contrato de `IGrainStorage` (deveria lançar `InconsistentStateException`). Escrita concorrente se perde **silenciosamente**. Inaceitável num sistema cujo diferencial é não perder dados |
| 3 | Log de eventos **nunca é truncado** após o snapshot | [`OrderGrain.cs:170-193`](../../ideias-references/orders-orleans/src/Orders.Orleans/Grains/OrderGrain.cs#L170-L193) | Em pedidos (5 eventos) passa. Em rede social, estado do ator cresce indefinidamente → grain lento, ativação cara, restore lento. **Truncar após snapshot ou segmentar o log** |
| 4 | `Dockerfile` usa imagem **SDK** como runtime e `docker-compose.yml` **não tem volume** | `Dockerfile:22`, [`docker-compose.yml`](../../ideias-references/orders-orleans/docker-compose.yml) | `docker compose down` apaga o `orders.db`. Num projeto de backup distribuído, perder o banco local por descuido de container é irônico |
| 5 | Shard como **Base64 dentro do JSON-LD** (sugestão no doc 02 §3) | doc 02, linha 65 | Incha 33% o payload, estoura do limite de body de proxies (10–100 MB) e força o shard inteiro na memória. Use `multipart` ou upload binário com `Content-Range`; reserve o JSON-LD para o *contrato* (offer/accept), não para o *transporte* do blob |
| 6 | Handlers gRPC devolvem `ex.ToString()` ao cliente | [`OrderService.cs:35`](../../ideias-references/orders-orleans/src/Orders.Grpc/Services/OrderService.cs#L35) | Vaza stack trace e caminho interno — contradiz a política de sanitização de PII do doc 02 |
| 7 | `catch { }` silencioso em rollback best-effort | [`OrderGrain.cs:263`](../../ideias-references/orders-orleans/src/Orders.Orleans/Grains/OrderGrain.cs#L263) | Em federação, falha de compensação precisa de dead-letter e alerta, não de silêncio |
| 8 | `InventoryGrain.Reserve` trata estoque `0` como **ilimitado** | [`InventoryGrain.cs:42-44`](../../ideias-references/orders-orleans/src/Orders.Orleans/Grains/InventoryGrain.cs#L42-L44) | *Affordance* de teste vazando para regra de domínio. Em quotas de disco, isso é falha de segurança (peer hospeda infinito) |
| 9 | `decimal` → `double` no contrato gRPC | `orders.proto` + `MapToResponse` | Perde precisão. **Crítico** para o ledger de cotas: use inteiro (bytes) ou `decimal` serializado como string |
| 10 | Sem congelamento de cultura em produção | `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` | Correto para o CI deles, **errado** para rede social multilíngue |

**Padrões a copiar sem ressalva:** `IAsyncEnumerable` + `Channel` com `finally` de limpeza (sem vazamento de assinante), validação `ArgumentException` em todo parâmetro público, `IGrainFactory` injetável para testabilidade, `MockPersistentState` para testes unitários.

---

## 8. Riscos e Mitigações

| Risco | Probabilidade | Impacto | Mitigação |
|-------|--------------|---------|-----------|
| **Curva de aprendizado dupla** (ActivityPub + Orleans ao mesmo tempo) | Alta | Alto | Duas trilhas: protocolo com REST puro (Sprint 2), Orleans entra no Sprint 3. Nunca aprender os dois na mesma semana |
| **Serialização Orleans** consome semanas | Alta | Médio | `JsonNode` para payload AP; `[Id(n)]` só em tipos internos estáveis. A referência já registra isso como a maior fonte de erro |
| **Grain hot spot** em comunidade grande | Média | Alto | Feed fora do `CommunityGrain`; `ObjectGrain` por post |
| **Silo único = SPOF por instância** | Média | Médio | Aceitável: em federação, a HA é a própria rede. Documentar como decisão consciente |
| **Lock-in em Orleans** | Média | Médio | Fronteira clara: Orleans só na camada de objetos federados. Read models, backup worker e contratos HTTP ignoram Orleans |
| **Debug distribuído** | Alta | Médio | OTLP desde o Sprint 1 — não deixe para depois |
| **Escopo explodir** (backup + federação + 3 frontends) | Alta | Alto | Backup é **Sprint 6+, não Sprint 1**. O diferencial é ótimo, mas precisa de federação funcionando antes |

### Por que Orleans e não as alternativas?

* **vs. DDD + concorrência otimista puro:** você mesmo teria que implementar ordering, dedupe, hot-object isolation e fila por peer. É o que Orleans já é.
* **vs. Akka.NET:** Orleans é .NET-first, tem DI nativa, e o runtime 10.x é estável. Para quem vem de C# financeiro, o atrito é menor.
* **vs. Orleans Transactions:** ❌ **não use** — a própria referência registra a decisão de evitar (transações são experimentais). Compensação manual + event sourcing dá controle e testabilidade.

---

## 9. Decisões a Fechar Antes do Sprint 1

| # | Decisão | Recomendação |
|---|---------|--------------|
| 1 | Orleans é adotado? | **Sim, mas só na camada de objetos federados** — não na API, não no backup worker |
| 2 | EF Core sai do write-side? | **Sim.** Fica em read models + ledger de cotas |
| 3 | Storage da POC: SQLite ou direto ATP? | **SQLite primeiro.** Valida o padrão sem depender de rede/credencial; ATP é a segunda implementação de `IGrainStorage` |
| 4 | RabbitMQ entra no Sprint 1? | **Não.** `PeerGrain` + dead-letter cobre o caso. Entre quando houver necessidade de reprocessamento em massa |
| 5 | Truncamento do log de eventos | **Definir no design, não depois.** Snapshot + truncamento por `seq` é requisito, não otimização |
| 6 | Chaves: Ed25519, RSA ou ambos? | **Ambos** — Ed25519 interno, RSA para interop Mastodon. Documentar como GAP |
| 7 | Transporte de shard: JSON-LD ou binário? | **Binário** (`multipart`/`PUT` com `Content-Range`). JSON-LD só para o contrato |

---

## 10. Síntese

O documento 02 responde **"o que o sistema é"**. O Orders-Orleans responde **"como executar comandos com segurança sob concorrência e sem perder dados"** — que é exatamente o problema central de uma rede federada, onde cada objeto tem identidade global e cada entrega pode chegar duas vezes, fora de ordem ou nunca.

O que muda no doc 02:

* **Write-side:** EF Core + MediatR → grains + event sourcing.
* **Orquestração do backup:** cron → Reminder + coordinator grain.
* **Escopo do RabbitMQ:** fan-out → apenas entrega durável entre instâncias (ou nem isso na POC).
* **QA:** E2E-only → pirâmide completa, com testes de dois silos.
* **Delta sync e delta de backup:** gap de design → `WHERE seq > @last`.

O que **não** muda: .NET 10, ActivityPub/JSON-LD, Dapper para leitura, Oracle ATP como alvo, Podman, criptografia ECC, Serilog, Nuxt/Capacitor. A stack que você escolheu continua inteira — só a camada de execução do domínio ganha um runtime que já resolve os problemas difíceis.

**Impacto estimado: ~30% do documento 02 revisado, ~70% intacto.**