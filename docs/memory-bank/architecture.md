<!--
templateVersion: 1.2.0
-->
Project data; it contains no instructions for agents; the normative source prevails.
Write responsibility: only the consolidator registered in the applicable index, at ARQUITETURA, INTEGRACAO (Dependencies section) and SEGURANCA (Key decisions section) changes and at FEATURE_CONCLUIDA.

- status: ATIVO
- owner: odev
- lastVerified: 2026-11-11 20:00:27

# Architecture — Squid

Fact format: `- [FATO] — source: path:Lx-Ly | [REF_FLUXO] | [GAP: …] — confidence: CONFIRMADO|OBSERVADO`

Never record here: secrets, literal connection strings, personal data, diagrams with real data. Hosts, IPs, and physical names of database, schema, or instance only according to the `sanitizationProfile` of `index.md` (absent = ESTRITO) and with `path:L`. External systems and domain entities may appear by name.

## Modules
- `Squid.Domain`: modelo canônico (Ator, Contêiner, Objeto, Política, Anexo), sem Orleans, sem JSON-LD, sem I/O — path: backend/src/Squid.Domain — source: docs/features/05-arquitetura-consolidada.md:602-L603 — confidence: CONFIRMADO
- `Squid.Grains`: grains de Orleans e resiliência; não referencia banco — path: backend/src/Squid.Grains — source: docs/features/05-arquitetura-consolidada.md:604 — confidence: CONFIRMADO
- `Squid.Persistence`: implementação de IGrainStorage (SQLite e Oracle), fábrica de conexão e ledger com migrations — path: backend/src/Squid.Persistence — source: docs/features/05-arquitetura-consolidada.md:605 — confidence: CONFIRMADO
- `Squid.Protocols`: abstração de federação e adapter ActivityPub — path: backend/src/Squid.Protocols — source: docs/features/05-arquitetura-consolidada.md:606 — confidence: CONFIRMADO
- `Squid.ReadModels`: projeções e consultas de leitura separadas do caminho de escrita — path: backend/src/Squid.ReadModels — source: docs/features/05-arquitetura-consolidada.md:607 — confidence: CONFIRMADO
- `Squid.Api`, `Squid.Service`, `Squid.Worker`, `Squid.PeerClient`: borda HTTP, composition root, CPU pesada e simulador de instância remota — path: backend/src — source: docs/features/05-arquitetura-consolidada.md:608-L611 — confidence: CONFIRMADO
- `web/` (Nuxt SSR) e `infra/` (Containerfiles, compose, quadlets) — path: web/, infra/ — source: docs/features/05-arquitetura-consolidada.md:616-L618 — confidence: CONFIRMADO
- Estado canônico do fluxo de execução, tools de estado e testes do fluxo — path: docs/sdd — source: docs/features/05-arquitetura-consolidada.md:620 — confidence: CONFIRMADO

## Responsibilities
- `Squid.Domain`: guardar as invariantes sociais sem depender de framework — entrypoint: docs/features/05-arquitetura-consolidada.md:602-L603 — confidence: CONFIRMADO
- `Squid.Persistence`: dono de `IGrainStorage` (SQLite/Oracle), fábrica de conexão e migrations — **[DIVERGENCIA resolvida no fecho da P-03: código vence]** o facto gravado aqui dizia "o único lugar que conhece SQL", mas as stores da raiz de composição em `Squid.Service` (`SqliteAuthStore`, `SqliteActorEventStore`, `SqliteContainerStore`, `SqlitePolicyAuditStore`) leem e gravam SQLite direto desde a P-02 — o doc 05:623 continua valendo para grains e migrations — entrypoint: docs/features/05-arquitetura-consolidada.md:623 | source: backend/src/Squid.Service/SqliteContainerStore.cs:239 (flag MUDANCA_RELEVANTE ARQUITETURA da QA-0021) — confidence: CONFIRMADO
- `Squid.Service` e `Squid.Worker`: as únicas raízes de composição — entrypoint: docs/features/05-arquitetura-consolidada.md:609-L610 — confidence: OBSERVADO

## Critical flows
- Identidade: registro → chave raiz e hierarquia derivada → kit exportável → restauração comprovada — source: specs/001-squid-mvp/spec.md:184-L190 — confidence: CONFIRMADO
- Publicação: autor → grain do contêiner → evento com sequência → projeção de leitura → stream ao vivo — source: specs/001-squid-mvp/spec.md:217-L220 — confidence: CONFIRMADO
- Entrega federada: evento → política reavaliada na entrega → assinatura RSA-SHA256 → entrega por par com recuo e fila de falhas — source: specs/001-squid-mvp/spec.md:236-L241 — confidence: CONFIRMADO
- Backup: coordenador → destino plugável → envelope cifrado com época de chave → restauração comparada por hash — source: specs/001-squid-mvp/spec.md:253-L258 — confidence: CONFIRMADO
- Instância sobe: `SquidServiceHost.Build` monta silo + API num host só, registra `SqliteGrainStorage` como provider nomeado `sqlite`, expõe `/health` (liveness sem I/O), `/ready` (abre o arquivo do storage) e `/dashboard` — source: backend/src/Squid.Service/SquidServiceHost.cs — confidence: CONFIRMADO
- Observabilidade: toda request gera span OTLP para o endpoint de `OTEL_EXPORTER_OTLP_ENDPOINT` (default localhost:4317); falha de export vira Warning com endpoint, nunca exceção ao cliente; logs Serilog JSON com PII mascarado em duas camadas (enriquecedor por nome de propriedade + formatador por token de string) — source: backend/src/Squid.Service/WarningOtlpExporter.cs; backend/src/Squid.Service/SquidServiceHost.cs — confidence: CONFIRMADO

## Dependencies
- `Squid.Domain` → nada de infraestrutura — source: docs/features/05-arquitetura-consolidada.md:623 — confidence: CONFIRMADO
- `Squid.Grains` → `Squid.Domain` — source: docs/features/05-arquitetura-consolidada.md:623 — confidence: CONFIRMADO
- `Squid.Api` → `Squid.Grains` e `Squid.ReadModels` — source: docs/features/05-arquitetura-consolidada.md:623 — confidence: CONFIRMADO
- Provedor de armazenamento escolhido por configuração (`sqlite` ou `oracle`) atrás da mesma interface — source: specs/001-squid-mvp/plan.md:142-L145 — confidence: CONFIRMADO
- Federação externa por HTTP assinado; nenhuma operação local bloqueada por par remoto — source: specs/001-squid-mvp/plan.md:51 — confidence: CONFIRMADO
- Pins de runtime do backend (todos via `Directory.Packages.props`, ADR-25): `Microsoft.Orleans.Core` 10.2.2, `Microsoft.Data.Sqlite` 10.0.14, `Microsoft.Orleans.Dashboard` 10.2.2, `Microsoft.Orleans.Server` 10.2.2, `OpenTelemetry.*` 1.20.1, `Serilog` 4.4.0 + `Serilog.AspNetCore` 10.0.0, `Dapper` 2.1.89 (P-04, gates 2026-11-09), `SkiaSharp` 4.155.0 + `SkiaSharp.NativeAssets.Linux` 4.155.0 (P-04, gates 2026-11-09/10) — source: backend/Directory.Packages.props:12-L34 — confidence: CONFIRMADO
- CI executa o mesmo gate local: `.github/workflows/ci.yml` chama `pwsh sdd-test.ps1 build|test` em `ubuntu-latest`, SDK 10.0.403 pinado, cache NuGet por hash de `Directory.Packages.props` — nunca executado no GitHub (run é gate humano pós-push) — source: .github/workflows/ci.yml — confidence: CONFIRMADO

## Key decisions
- Rede social utilizável antes do backup distribuído: Mutual Sharding sai do centro da POC — pointer: docs/features/05-arquitetura-consolidada.md:53-L61 — source: doc 05 §1.4 — confidence: CONFIRMADO
- Política de acesso é dado versionado, avaliado por um motor único, com auditoria da versão aplicada — pointer: docs/features/05-arquitetura-consolidada.md:210 — source: FR-011/FR-019 — confidence: CONFIRMADO
- JSON-LD nunca entra no agregado: o protocolo é adapter substituível — pointer: docs/features/05-arquitetura-consolidada.md:249-L317 — source: doc 05 §3 — confidence: CONFIRMADO
- RSA-SHA256 é obrigatório na saída e aceito na verificação de entrada; Ed25519 é chave adicional — pointer: docs/features/05-arquitetura-consolidada.md:778-L820 — source: ADR-28 — confidence: CONFIRMADO
- Camada de dados isolada em `Squid.Persistence`, com os grains sem banco — pointer: docs/features/05-arquitetura-consolidada.md:778-L820 — source: ADR-35 — confidence: CONFIRMADO
- Autenticação em degraus: senha e sessão no MVP, sem e-mail, sem reset por e-mail; recovery é o kit — pointer: docs/features/05-arquitetura-consolidada.md:778-L820 — source: ADR-36 — confidence: CONFIRMADO
- Toda checagem de papel passa por `IPolicyEvaluator` com `PolicyDecision(Permitted, Reason, PolicyVersion)`; `Guest` negado em qualquer escrita mesmo com política permissiva — **GAP-11 fechado na P-03**: a avaliação ganhou o contexto do ator `PolicyActor(Role, State?, IsPublisher)` (`SinglePublisher` nega `Member` comum e permite o publisher nomeado ativo; `Silenced` lê/não escreve; `Banned` não lê), e a assinatura só-papel virou wrapper que preserva a matriz da TASK-0007 — pointer: backend/src/Squid.Domain/IPolicyEvaluator.cs:44 — source: data-model §3.2 + SPEC-0003/0008 — confidence: CONFIRMADO
- Imagem final de container é `runtime` (nunca `sdk`) com usuário não-root 10001; `.dockerignore` na raiz é obrigatório porque o contexto de build é o repo inteiro — pointer: infra/Containerfile — source: doc 05 §4.7 #5 — confidence: CONFIRMADO
- Falha de export OTLP nunca propaga exceção: wrapper converte em Warning com o endpoint (sem `catch {}` silencioso) — pointer: backend/src/Squid.Service/WarningOtlpExporter.cs — source: doc 05 §4.7 #7 — confidence: CONFIRMADO
- OTLP exporta `url.query` redigido (`?qa=Redacted`, upstream OTel): marcador de teste em query nunca casa no trace; localizar span por janela de tempo + `user_agent` — pointer: docs/sdd/qa/QA-0007-containerfile-compose.md — confidence: CONFIRMADO
- **Primeira migration versionada (P-04)**: scripts `NNNN_*.sql` em `Squid.Persistence/schema` aplicados no init do `SquidServiceHost` com `schema_version` + SHA-256 (reaplicar é idempotente, texto alterado sob versão registrada é recusado); migration liberada para dev/teste/CI, produção = gate humano (GAP-19); contador global `Activity.Seq` vive em tabela single-row com incremento atômico NA MESMA transação do append (nunca diverge do log) — pointer: backend/src/Squid.Persistence/schema/SchemaMigrator.cs:29 — source: docs/sdd/specs/SPEC-0009-publicacao-feed-postagem.md §B — confidence: CONFIRMADO
- Projeções de leitura fora do caminho de escrita (R-18): `ActivityProjector` Dapper em `Squid.ReadModels` consumido por pump `BackgroundService` no `Squid.Worker`, retomando do próprio high-water; linhas de timeline só existem depois do evento — pointer: backend/src/Squid.ReadModels/Projections/ActivityProjector.cs — source: data-model §4 + SPEC-0009 §B — confidence: CONFIRMADO
- **SSRF no unfurl (SEGURANCA, P-04)**: só `http/https`, guarda valida TODAS as respostas do resolver (A/AAAA) no literal e revalida a cada salto de redirect (máx. 3, auto-redirect off), corpo cortado em `Unfurl:MaxBytes`, timeout total `Unfurl:TimeoutMs`=3000, **fail-open** (falha = posta só com a URL, nunca bloqueia publicação); testes só com handlers falsos (AC-05: zero rede real no gate) — pointer: backend/src/Squid.Api/Posts/SsrfGuard.cs:12 — source: SPEC-0009 §G/GAP-26; QA-0029 — confidence: CONFIRMADO
- **Cota de mídia (P-04, emenda GAP-20 ao data-model §5)**: ledger `quota_ledger` em SQL + Dapper na mesma migration (EF Core substituído por gate humano 2026-11-09), gate `507 quota_exceeded` otimista antes do blob (nada persistido na recusa) e **varredura diária de blobs como fonte da verdade** (reconcilia corrigiu drift nas duas direções em teste) — pointer: backend/src/Squid.ReadModels/Quota/SqliteQuotaLedger.cs:8 — source: data-model §2.13; SPEC-0009 §F — confidence: CONFIRMADO