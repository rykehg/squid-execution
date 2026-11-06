# Squid Constitution

> Derivada do documento mestre [`docs/features/05-arquitetura-consolidada.md`](../../features/05-arquitetura-consolidada.md)
> (§1.5 princípios, §4.7 padrões obrigatórios, §10.2 DoD) e do registro de decisões §13.
> Onde este documento e o 05 divergirem, **vence o 05** — esta constituição é a tradução executável dele.

## Core Principles

### I. Local primeiro, federação depois (NON-NEGOTIABLE)

Todo recurso funciona standalone antes de federar. Identidade, contêineres, publicação, feed e
mensageria são entregues e testados em uma instância só; a federação é a Sprint 5, não o Sprint 1.
Nenhuma tarefa pode depender de um par remoto para ser considerada pronta.

**Justificativa:** sem rede social funcionando não existe federação; sem federação não existe rede
de guardiões. O risco dominante do projeto é escopo, não complexidade criptográfica.

### II. Política é dado, não código

Regras de acesso vivem em um objeto (`ContainerPolicy`) avaliado por **um único motor**
(`IPolicyEvaluator`). É proibido `if (role == Role.Admin)` espalhado pelo código. Toda decisão é
registrada com `policyVersion`, porque a auditoria precisa saber qual política valia na hora.

Os quatro pontos de aplicação são conhecidos: borda HTTP (rejeição precoce, **não confiável**),
comando no grain (**autoritativo**), projeção de leitura (complementar) e saída federada (crítico).
Nunca se confia na checagem da borda.

### III. O grain orquestra, o Worker computa

CPU pesada nunca roda no scheduler do silo: Reed-Solomon, compressão, cifra de blobs,
transcodificação, thumbnail, EXIF stripping e reconstrução SSS são do Worker. O grain emite o
comando, aguarda, e recebe o resultado pelo inbox. Um grain que bloqueia thread derruba a latência
de todo o silo.

### IV. O protocolo é adapter; o domínio é canônico

JSON-LD nunca entra no agregado. `ActivityPubAdapter` traduz na borda; o domínio fala o modelo
canônico. A interface de protocolo tem **cinco métodos** — resistir à tentação de aumentá-la. O
teste de que a abstração funciona é adicionar o segundo protocolo sem tocar em `Domain` nem em
`Grains`.

### V. Observável ou não existe

Todo fluxo novo nasce com trace (OTel), log estruturado sanitizado e métrica. PII é removida
**em logs e em span tags** — payload federado vai para tag de trace e vaza igual. Exporter é OTLP,
nunca console. Erro nunca retorna `ex.ToString()` ao cliente.

### VI. Fase 0 sempre recuperável

Backup que não foi restaurado em teste não existe. A camada 0 (kit offline com `RK` cifrada por
Argon2id, mnemônico de 24 palavras e `RC` independente) é obrigatória no MVP. Nenhuma camada
isolada produz a `RK`; e-mail e SMS são alarme, não fechadura. Restore drill roda em CI:
criar → backup → destruir → restaurar → comparar hashes.

### VII. Escopo contido é requisito, não preferência

Os não-objetivos do 05 §1.3 são lei: nenhuma integração de postagem automática em rede fechada,
nenhuma ingestão dessas redes, nenhum algoritmo de ranquamento no MVP (feed cronológico), nenhum
microserviço além do Worker de mídia/backup, nenhum Kubernetes (Podman + systemd em 1–2 VMs).
Infraestrutura nova (Redis, Kafka, Kubernetes, fila gerenciada) exige ADR assinado; a resposta
padrão é não.

## Constraints Técnicas

**Stack aprovada** (05 §4.1) — desvio exige ADR:

| Camada | Escolha |
|--------|---------|
| Runtime | .NET 10 (LTS) |
| Actor framework | Microsoft Orleans 10.2, **somente** na camada de objetos federados |
| API | ASP.NET Core Minimal APIs — REST/JSON-LD + SSE. **Não** gRPC na federação |
| Write side | `IPersistentState` + event sourcing + snapshot, via `IGrainStorage` |
| Storage | SQLite (POC) → Oracle ATP, duas implementações da mesma interface (`Squid.Persistence`) |
| Read side | Dapper sobre tabelas de projeção — grain não serve query |
| Relacional | EF Core, **apenas** ledger de cotas + migrations |
| CPU pesada | Worker Service separado |
| Resiliência | Polly 8 (`ResiliencePipeline`) + circuit breaker + DLQ |
| Observabilidade | OpenTelemetry (OTLP) + Serilog |
| Web | Nuxt 3 + Vue 3 (SSR para OpenGraph) |
| Mobile | Vue 3 + Vite + Capacitor (fase posterior) |
| Infra | Podman + systemd, ARM Ampere A1, volumes nomeados |

**Rejeitados:** MediatR no domínio, RabbitMQ no MVP, gRPC na federação, Orleans Transactions,
Kubernetes, Aspire AppHost antes de 4–5 recursos simultâneos.

**Padrões obrigatórios** (05 §4.7) — cada um é **critério de aceite em code review**:

1. `IGrainStorage` impõe ETag (`WHERE Version = @Version`) e lança `InconsistentStateException`.
2. Nunca hardcodar o nome do store no SQL.
3. Truncar o log de eventos após snapshot.
4. Volume nomeado para todo dado persistente em container.
5. Estágio final da imagem é `runtime`, não `sdk`, com usuário não-root.
6. Nunca devolver `ex.ToString()` ao cliente.
7. Proibido `catch { }` silencioso — dead-letter + alerta.
8. Proibido valor "de teste" em regra de domínio (ex.: estoque 0 = ilimitado).
9. Dinheiro/quota nunca em `double`; usar `long` (bytes) ou `decimal` serializado como string.
10. Binário (mídia, shard) nunca dentro do JSON de estado.
11. Sem `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` em produção.
12. Sanitizar PII em logs **e** em span tags.

**Segurança e privacidade:** Argon2id para senha; sessão HttpOnly; HTTP Signatures com RSA-SHA256
(interop Mastodon) e Ed25519; validação de MIME real por magic bytes; EXIF removido de toda mídia;
segredos fora do repositório. O MVP **não** tem E2EE — e isso é dito ao usuário na UI e no termo de
uso, não escondido em rodapé.

## Workflow de Desenvolvimento

1. **O documento é a fonte da verdade.** Se a implementação exigir desviar, o documento muda
   **antes** do código.
2. **Uma sprint por vez.** Nunca "implemente o projeto"; sempre "implemente a tarefa X do Sprint N".
3. **ADR obrigatório.** Toda decisão não-trivial vira linha na §13 do doc 05. Sem ADR, a decisão
   não existe.
4. **DoD global por épico** (05 §10.2): testes verdes nas camadas aplicáveis; trace e log
   sanitizado no fluxo novo; política coberta por teste positivo **e** negativo; migração de schema
   quando o estado muda; documento atualizado; nenhuma regra nova que viole os 12 padrões.
5. **Diff pequeno.** PR que toca 4 projetos e 3 sprints é reduzido, não revisado.
6. **Regra de dependência verificada pelo build**, não por revisão: `Domain` não referencia
   infraestrutura; o adapter é substituível.
7. **Teste antes de avançar.** Matriz de capacidades (papel × ação) é teste, não planilha.

## Governance

Esta constituição **prevalece** sobre práticas ad hoc e sobre os documentos 00–04 onde houver
divergência; o doc 05 prevalece sobre esta constituição.

* **Emendas:** exigem ADR na §13 do doc 05 + entrada de changelog na §16 + bump de versão
  semântica neste arquivo. Nada de emenda silenciosa.
* **Versionamento:** MAJOR para remoção/redefinição de princípio; MINOR para princípio ou seção
  nova; PATCH para esclarecimento sem mudança de obrigação.
* **Conformidade:** todo PR e code review verifica os princípios I–VII, os 12 padrões e o DoD §10.2.
  Violação sem justificativa registrada em `plan.md → Complexity Tracking` é bloqueio, não opinião.
* **Dúvida de escopo:** a resposta padrão é "não entra no MVP" até o doc 05 dizer o contrário.

**Version**: 1.0.0 | **Ratified**: 2026-11-02 | **Last Amended**: 2026-11-02