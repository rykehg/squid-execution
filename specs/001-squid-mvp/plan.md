# Implementation Plan: Squid — MVP de rede social federada

**Branch**: `001-squid-mvp` | **Date**: 2026-11-02 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-squid-mvp/spec.md`

**Documento mestre**: [`features/05-arquitetura-consolidada.md`](../../features/05-arquitetura-consolidada.md) — onde este plano divergir do 05, o 05 vence e o plano é corrigido.

## Summary

Construir o MVP do Squid: instância única auto-hospedada com identidade recuperável, contêineres
sociais (grupo e comunidade com o mesmo agregado, dois presets), política de acesso como dado,
publicação com mídia e feed cronológico incremental, mensageria sem E2EE declarada, federação por
um adapter ActivityPub, compartilhamento externo por link/preview e backup cifrado com restauração
provada em CI, com interop real (Mastodon e Friendica) como gate do Sprint 5.

Abordagem técnica: Orleans como camada de objetos federados (grains com chave por URI), event
sourcing com snapshot e truncamento sobre `IGrainStorage`, leitura por projeções Dapper separadas do
caminho de escrita, Worker separado para CPU pesada, e um adapter de protocolo com cinco métodos que
mantém o domínio canônico livre de JSON-LD. Stack fixada em .NET 10 + Orleans 10.2 + Minimal APIs +
SQLite (POC) sobre interface de storage que depois recebe Oracle ATP.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (LTS). Web em TypeScript com Nuxt 3 + Vue 3.

**Primary Dependencies**: Microsoft Orleans 10.2 (+ `Microsoft.Orleans.TestingHost`), ASP.NET Core
Minimal APIs, Polly 8 (`ResiliencePipeline`), Dapper, EF Core (apenas ledger de cotas e migrations),
Microsoft.Data.Sqlite, Serilog (sinks OTLP + console de dev), OpenTelemetry .NET (traces, métricas,
logs via OTLP), Argon2id (`Konscious.Security.Cryptography` ou libsodium), BouncyCastle/`NSec` para
Ed25519, `System.Security.Cryptography` para RSA-SHA256, ImageSharp/SkiaSharp no Worker (thumbnail,
EXIF), FFmpeg via processo (transcodificação), `blurhash` para preview.

**Storage**: SQLite em arquivo no MVP (dev/ci/prod), atrás de `IGrainStorage` e de um contrato de
repositório; Oracle ATP como segunda implementação da mesma interface, atrás de flag de
configuração. Blobs em volume nomeado (content-addressed por SHA-256), nunca no estado do grain.

**Testing**: xUnit + `MockPersistentState` para grains e políticas; `Orleans TestingHost` para
integração e para **dois silos**; SQLite real para ETag/concorrência/truncamento; scripts de
integração executáveis sem daemon (`dotnet run` + `curl`) em `backend/tests/integration`.

**Target Platform**: Linux ARM64 (Oracle Ampere A1, 1 VM, 2 OCPU/12 GB) em Podman + systemd;
desenvolvimento em Windows/WSL com `dotnet run` e `pnpm dev`. Imagem final em estágio `runtime`,
usuário não-root.

**Project Type**: web service (API + silo + worker) com web app SSR no mesmo repositório,
monorepo com código na raiz.

**Performance Goals**: primeira página de feed de comunidade com 10k publicações em < 1 s p95;
atualização de feed entregue a cliente conectado em < 2 s p95; post público visível em instância
vizinha em < 30 s; nenhuma operação local bloqueada por par remoto indisponível.

**Constraints**: sem E2EE no MVP (declarado na UI e no termo); sem Kubernetes (Podman + systemd);
sem microserviço além do Worker de mídia/backup; sem MediatR no domínio; sem gRPC na federação;
CPU pesada nunca no scheduler do silo; PII sanitizada em log **e** em span tags; nenhum binário no
JSON de estado; dinheiro/quota como inteiro (bytes) ou decimal serializado como string.
Assinatura federada de saída em RSA-SHA256, com Ed25519 apenas como chave adicional (R-02, D1).

**Scale/Scope**: 1 instância, 1 operador, dezenas de grupos de amigos e comunidades com até
milhares de membros; 10k–100k publicações por comunidade no horizonte do MVP; 10 projetos .NET +
web; 16 entidades de domínio; 8 user stories (P1×4, P2×3, P3×1).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio | Gate verificável | Status |
|-----------|------------------|--------|
| **I. Local primeiro, federação depois** | Todo entregável de Sprint 0–4 roda e é testado em uma instância; nenhuma tarefa de fundação depende de par remoto. Federação isolada no Sprint 5. | ✅ PASS |
| **II. Política é dado, não código** | `ContainerPolicy` versionada + `IPolicyEvaluator` único; matriz papel × ação testada (positivo e negativo); auditoria com `policyVersion`; decisão autoritativa no grain. | ✅ PASS |
| **III. Grain orquestra, Worker computa** | Nenhuma operação de Reed-Solomon, cifra, transcodificação ou thumbnail no silo; Worker recebe comando e devolve pelo inbox. | ✅ PASS |
| **IV. Protocolo é adapter** | `IFederationProtocol` com exatamente 5 métodos; `Domain` sem referência a JSON-LD; regra de dependência verificada pelo build. | ✅ PASS |
| **V. Observável ou não existe** | Todo fluxo novo entra com `ActivitySource` por camada, metric e log sanitizado; exporter OTLP; teste de sanitização de PII. | ✅ PASS |
| **VI. Fase 0 sempre recuperável** | Kit camada 0 no Sprint 1; `IBackupTarget` + restore drill com comparação de hash no Sprint 6; pipeline falha em divergência. | ✅ PASS |
| **VII. Escopo contido** | Não-objetivos reproduzidos na spec; nenhuma infra nova além de Podman/systemd no MVP; ADR obrigatório para desvio. | ✅ PASS |
| **Padrões §4.7 (×12)** | ETag imposto no storage, store name não hardcoded, log truncado após snapshot, volume nomeado, imagem `runtime`, sem `ex.ToString()`, sem `catch {}`, sem valor de teste em regra, quota em inteiro, binário fora do JSON, sem globalization invariant, PII sanitizada. Cada um vira critério de code review e teste onde couber. | ✅ PASS |
| **Stack aprovada** | Nenhuma tecnologia fora da tabela de §4.1; rejeitados explícitos (MediatR, RabbitMQ, gRPC na federação, Orleans Transactions, K8s, Aspire) não aparecem no plano. | ✅ PASS |

**Pós-desenho (Phase 1)**: os artefatos gerados não introduzem dependência nova, serviço novo nem
violação de padrão. O único ponto de atenção é a quantidade de projetos (ver Complexity Tracking) e
ele já está previsto pelo documento mestre. **Gate reavaliado: PASS.**

## Project Structure

### Documentation (this feature)

```text
specs/001-squid-mvp/
├── plan.md              # Este arquivo (/speckit-plan)
├── research.md          # Phase 0: decisões que fecham os GAPs do doc 05 §14
├── data-model.md        # Phase 1: entidades, invariantes, transições, projeções
├── quickstart.md        # Phase 1: cenários executáveis de validação ponta a ponta
├── contracts/           # Phase 1: contratos externos
│   ├── http-api.md          # REST + SSE vista pelo cliente web/mobile
│   ├── activitypub.md       # superfície federada e regras de entrega
│   └── backup-recovery.md   # contrato de destino de backup e de restauração
├── checklists/
│   └── requirements.md  # qualidade da spec
└── tasks.md             # NÃO criado aqui: gerado pelo fluxo odev (/speckit-tasks opcional)
```

### Source Code (repository root)

Layout fixado pelo doc 05 §11 (ADR-21: código na raiz; ADR-22: prefixo `Squid.*`).

```text
backend/
├── Squid.slnx
├── Directory.Build.props        # TFM net10.0
├── Directory.Packages.props     # central package management (ADR-25)
├── Directory.Build.targets      # regra de dependência violada = erro de build
├── nuget.config                 # fonte local versionada (rede fechada foi bloqueio real)
├── src/
│   ├── Squid.Domain/            # canônico: Actor, Container, Object, Policy, Attachment
│   │                            #   sem Orleans, sem JSON-LD, sem I/O
│   ├── Squid.Grains/            # grains + Resilience/ (Polly); não referencia banco
│   ├── Squid.Persistence/       # IGrainStorage SQLite/Oracle, IDbConnectionFactory,
│   │                            #   ledger EF Core + migrations
│   ├── Squid.Protocols/         # IFederationProtocol + ActivityPubAdapter
│   ├── Squid.ReadModels/        # projeções + queries Dapper (via IDbConnectionFactory)
│   ├── Squid.Api/               # Minimal APIs, endpoints AP, SSE, auth
│   ├── Squid.Service/           # composition root: Silo + Api + Health + Dashboard
│   ├── Squid.Worker/            # mídia, backup, cifra, Reed-Solomon (fase 2)
│   └── Squid.PeerClient/        # console: simula instância remota e falhas
└── tests/
    ├── Squid.Tests/             # unit + integração + dois silos
    └── integration/             # scripts de QA executáveis sem daemon

web/                             # Nuxt 3 (SSR, OpenGraph, oEmbed)
app/                             # Vue 3 + Capacitor — NÃO criar antes da fase posterior
infra/                           # Containerfiles, compose.yaml, quadlets, Caddyfile
docs/features/                   # 00..06 (documento mestre)
docs/sdd/                        # plano, specs, tarefas, QA do fluxo odev
```

**Structure Decision**: monorepo único com o produto na raiz, backend .NET em `backend/`, front SSR
em `web/` e infraestrutura em `infra/`. `app/` existe apenas como diretório planejado e não é criado
no MVP (não-objetivo: aplicativo nativo antes da fase posterior). A regra de dependência
(`Domain` ↛ infraestrutura, `Grains` → `Domain`, `Api` → `Grains` + `ReadModels`) é garantida em
`Directory.Build.targets`, não por revisão. A direção é `Domain` ← `Grains` ← `Api`/`Service`; `Grains`
não referencia banco, `Persistence` implementa `IGrainStorage` e o ledger, `ReadModels` usa
`IDbConnectionFactory`, e `Service`/`Worker` são as únicas composition roots. O provedor é escolhido por
configuração (`Storage:Provider=sqlite|oracle`) e a **mesma suíte de contratos de storage** roda contra
os dois (SQLite no CI rápido, Oracle em job agendado). A separação entre spec-kit (este repositório) e
execução (odev) está registrada em ADR-27: este plano descreve o **quê/como** arquitetural; a
decomposição em tarefas pequenas nasce no repositório de execução.

## Decisões reconciliadas (2026-11-03)

As quatro definições pendentes foram respondidas e estão aplicadas nos artefatos:

| ID | Decisão | Onde aparece |
|----|---------|--------------|
| D1 | Interop real (Mastodon + Friendica); RSA-SHA256 obrigatório na saída e na verificação de entrada; Ed25519 como chave adicional | spec FR-039/SC-016, research R-02, `contracts/activitypub.md` §1/§5, quickstart US6; ADR-28 no doc 05 |
| D2 | Reconciliar só até a Phase 1; `tasks.md` continua fora (ADR-27) | este plano, nota de escopo |
| D3 | Moderação aceita como R-07.6: denúncia + fila humana + remoção com motivo + registro 6 meses + SLA 24h | research R-07; fecha GAP 11 e GAP-06 |
| D4 | Referências são entrada de validação, não decisão; entram `max_id`/`since_id`+`Link` e teto de contadores remotos; FEP-1b12 e sync de seguidores ficam fora | research R-21, data-model, `http-api.md` §1 |

Consequência técnica registrada: RSA não deriva da `RK`; o par RSA-2048 é gerado no registro e
embrulhado por `HKDF(RK, "fed/rsa-wrap")`, guardado no estado do ator e no kit (R-02).

## Phase 0 — Research (`research.md`)

Entradas: GAPs do doc 05 §14 (15 itens) + escolhas de stack que precisam de decisão registrada +
pontos de interop que decidem arquitetura. Cada item termina em `Decision / Rationale /
Alternatives`, com FRs afetados e risco residual. Resumo dos IDs:

| ID | Assunto | Fecha |
|----|---------|-------|
| R-01 | Identidade após recuperação (derivar vs re-atestar) | GAP 1 — **bloqueante Sprint 1** |
| R-02 | RSA-SHA256 × Ed25519 nas HTTP Signatures | GAP 2 — Sprint 5 |
| R-03 | Autenticação: senha + sessão × passkey/OIDC | GAP 3 — Sprint 1 |
| R-04 | Vocabulário JSON-LD de contêiner (`Group` estendido) | GAP 7 — Sprint 2 |
| R-05 | Reavaliação de política no fan-out e cancelamento de entrega | GAP 9 — Sprint 5 |
| R-06 | Quota de mídia e cota por instância | GAP 10 — Sprint 3 |
| R-07 | Moderação de conteúdo e obrigação legal | GAP 11 — **antes do Sprint 3** |
| R-08 | Retenção e exclusão em conteúdo federado | GAP 12 — Sprint 5 |
| R-09 | Sessão HttpOnly em SSR sem XSS/CSRF | GAP 13 — Sprint 7 |
| R-10 | Busca em comunidades (FTS × pós-MVP) | GAP 14 |
| R-11 | Federação de contêineres privados: o que trafega | GAP 15 — Sprint 5 |
| R-12 | Reed-Solomon, SSS e campo: decisão de adiamento | GAPs 5,6 — fase 2 |
| R-13 | Estratégia de serialização de estado Orleans | risco §15 |
| R-14 | Content-addressing e dedupe de mídia | §2.6 |
| R-15 | Incremental backup por sequência | §6.1 |
| R-16 | Padrão de SSE (canal + `IAsyncEnumerable`) | FR-024 |
| R-17 | Estratégia de teste de dois silos | §10.1 |
| R-18 | Projeção de leitura: consistência e atraso aceitável | §4.4 |
| R-19 | Cota e quota como inteiro (bytes) | §4.7 #9 |
| R-20 | Continuidade de chave na rotação (`keyEpoch`) | FR-047 |
| R-21 | Referências abertas: postura e material adotado (D4) | Phase 1 — documentos |

## Phase 1 — Design & Contracts

**`data-model.md`** — 16 entidades do canônico (Ator, Contêiner, Vínculo, Política, Publicação,
Anexo, Conversa, Mensagem, Atividade, Par, Envelope de backup, Decisão de política, Kit de
recuperação, Cota, Época de chave, Reação) com atributos, relações, invariantes, máquina de estados de
vínculo/publicação/entrega e o desenho das projeções de leitura.

**`contracts/http-api.md`** — superfície REST vista pelo cliente (identidade, contêineres, política,
publicação, timeline com `since`, mensageria, streaming ao vivo, mídia, administração), formato de
erro padronizado, paginação, autenticação e códigos de status — com a regra "recurso invisível
responde 404".

**`contracts/activitypub.md`** — endpoints de descoberta, perfil, inbox/outbox, objeto e contêiner;
atividades suportadas e formato mínimo de `Create`; assinatura (draft-cavage RSA-SHA256 e RFC 9421
na entrada); idempotência; limites por par; tabela de decisão de fan-out por visibilidade e por tipo
de destinatário.

**`contracts/backup-recovery.md`** — contrato de destino de backup (local, S3-compatível, shard de
par), formato do envelope (época de chave, faixa de sequência, hash, tamanho), fluxo de restauração,
procedimento de drill e comparação de hashes.

**`quickstart.md`** — cenários executáveis que provam cada user story do MVP no ambiente local
(`podman compose up`, `dotnet run`, `pnpm dev`, `curl`), incluindo o cenário de dois silos e o
restore drill, com resultados esperados verificáveis.

**`checklists/requirements.md`** — qualidade da spec, já validada.

## Implementation Strategy (entregáveis por sprint)

Os sprints do doc 05 §12.2 mapeiam diretamente as user stories priorizadas; a ordem é a ordem de
dependência, não a ordem de vontade.

| Sprint | User story | Entregável de valor | Gate de saída |
|--------|-----------|---------------------|---------------|
| 0 | — (fundação) | Instância sobe, responde health, persiste e é restaurável | compose up → `/health` verde → trace de request no coletor |
| 1 | US1 (P1) | Identidade local com perfil, chaves e kit de recuperação | webfinger resolve; kit restaura a mesma chave em teste |
| 2 | US2 (P1) | Grupo/comunidade com papéis e política versionada | grupo `invite` fecha entrada; `singlePublisher` bloqueia membro |
| 3 | US3 (P1) | Publicação, feed cronológico e delta sync | post ao vivo < 1 s; `since` entrega só o delta; unlisted invisível |
| 4 | US4, US5 (P2) | Comunidade em escala + mensageria sem E2EE | feed de 10k posts p95 < 1 s; terceiro não lê conversa; ordem estável; resposta e reação visíveis só aos participantes |
| 5 | US6 (P2) | Federação com dois silos + interop real | público aparece em Mastodon e Friendica com assinatura aceita; membersOnly não vaza; dedupe; par caído não trava |
| 6 | US8 (P1) | Backup cifrado + restore drill no CI | CI destrói e restaura com hashes iguais; off-site restaura |
| 7 | US7 (P3) | Front web SSR + compartilhamento externo | preview no WhatsApp/Telegram; share sheet Android |
| 8+ | — | Pós-MVP (fora deste plano) | Sharding, SSS, E2EE, AT Protocol |

**DoD por épico** (doc 05 §10.2, obrigatório): testes das camadas aplicáveis verdes; trace e log
sanitizado no fluxo novo; política coberta por teste positivo e negativo; migração de schema quando
o estado muda; documento atualizado; nenhuma regra nova violando §4.7.

## Risks & Mitigations (do doc 05 §15, aplicados a este plano)

| Risco | Impacto | Mitigação neste plano |
|-------|---------|----------------------|
| Escopo explodir | Alto | Não-objetivos na spec + sprint por vez + ADR obrigatório |
| Curva dupla Orleans + ActivityPub | Alto | Sprints 1–4 locais antes de qualquer federação |
| Serialização Orleans consumir semanas | Médio | R-13: payload como `JsonNode`, `[Id(n)]` só em tipos estáveis |
| Federação travar em HTTP Signatures | Alto | R-02 + `PeerClient` + dois silos no CI; interop real (Mastodon/Friendica) verificado no fechamento do Sprint 5, com versões fixadas |
| Restore nunca testado | Crítico | Restore drill obrigatório em CI no Sprint 6 (FR-049) |
| Moderação ignorada | Alto (legal) | R-07 fecha **antes** do Sprint 3 (FR-028) |
| Vazamento por preview/cache/contador | Alto | FR-018/FR-026 + SC-005/SC-013 como teste de saída |
| Abstração de protocolo virar over-engineering | Médio | Interface de 5 métodos; segundo protocolo só depois do MVP |
| Proprietário inativo deixar contêiner órfão | Médio | Sucessor nomeado + claim após 90 dias + override auditado do operador (FR-059..FR-061) |

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| 10 projetos na solution (mais que os 3 típicos) | O documento mestre §11 fixa a separação para que a regra de dependência seja **verificável pelo build**: `Domain` sem infraestrutura, adapter substituível, Worker isolado do silo | Consolidar em 3–4 projetos deixaria a fronteira de dependência dependente de disciplina de revisão, que é exatamente o modo de falha que ADR-25 quer eliminar |
| Orleans como framework de atores (curva alta) | Concorrência por agregado com chave por URI é o coração do modelo federado; reimplementar seria reescrever o produto | DDD com concorrência otimista manual foi avaliado e rejeitado em ADR-1 pelo custo de manter single-writer por ator |
| Dois caminhos de leitura (grain para escrita, Dapper para leitura) | Feed de comunidade com milhares de membros não pode serializar no grain (hot spot proibido, §4.2) | Servir query pelo próprio grain foi rejeitado porque transforma o agregado social em gargalo global |
| Abstração de protocolo mesmo com um só protocolo no MVP | At Protocol tem modelo radicalmente diferente; sem adapter o segundo protocolo exige reescrever o domínio | Domínio falando JSON-LD foi rejeitado em ADR-11; a abstração fica na borda, com 5 métodos e nada além |

## Nota de escopo do artefato

Este plano termina na Phase 1, como pedido. `tasks.md` **não** é gerado aqui: a decomposição em
tarefas pequenas e o estado canônico de execução pertencem ao fluxo odev no repositório de execução
(ADR-27). Se quiser a decomposição no formato do Spec Kit antes disso, rode `/speckit-tasks` — os
insumos já estão completos. Bots/automação são a feature `002-bots-e-automacao` (parked, ADR-30):
o modelo de ator já reserva `Service`/`IsBot` e `contracts/http-api.md` §10 registra a superfície de
token/webhook como reservada — nada disso é implementado no MVP. Interações ricas (eventos, metas,
enquetes) são a feature `003-eventos-metas-enquetes` (parked, ADR-31), com `Object.Kind` reservado.