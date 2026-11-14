<!--
templateVersion: 1.0.0
-->
Project plan; current view only. History lives in `plan/history/` and is never copied back into this file.
Write responsibility: ODEV. Every other role reads it and never edits it.

- status: ATIVO
- owner: odev
- lastVerified: 2026-11-13 13:56:33
- snapshot: plan/history/0006-plan.md (fecho da P-05; 0005 é a P-04, 0004 é a P-03, 0003 é a P-02, 0002 é a P-01, 0001 é o bootstrap)

# Plan — Squid

- schema: sdd-plan/1
- runId da sessão de origem: R-20261104-01

## Problem and MVP

- Problem: redes sociais fechadas decidem por você quem entra, quem vê e quem fica com seus dados.
  A alternativa federada existe, mas é operada por quem tem perfil de administrador de sistemas;
  o Squid entrega uma instância auto-hospedada com identidade recuperável, grupos de amigos e
  comunidades de interesse com política de acesso como dado, e federação aberta por ActivityPub.
- MVP scope: instância única auto-hospedada que sobe, responde health, persiste e é restaurável;
  identidade local com chave e kit de recuperação; grupo e comunidade com papéis e política
  versionada; publicação, feed cronológico e sincronização incremental; mensageria 1-1 e em grupo
  (com resposta e reação, **sem** E2EE declarada); federação com dois silos e interop real
  Mastodon/Friendica; backup cifrado com restore drill no CI; front SSR com preview social.
- Out of MVP (parked in `ideas/BACKLOG.md`): I-01 bots/automação, I-02 eventos/metas/enquetes,
  I-03 E2EE/MLS, I-04 Mutual Sharding + Reed-Solomon, I-05 app nativo Capacitor,
  I-06 AT Protocol.
- Success of the MVP: os critérios mensuráveis SC-001..SC-018 do material adotado
  (`docs/features/05-arquitetura-consolidada.md` §13/§12.2 e `specs/001-squid-mvp/spec.md`) são
  verificáveis em máquina de dev; e, no fechamento do Sprint 5, uma instância Mastodon e uma
  Friendica reais aceitam nossas entregas assinadas em RSA-SHA256 (SC-016).

## Phases

Cada fase termina em entregas pequenas, funcionais por si e verdes em QA. A ordem é a ordem de
dependência, não a ordem de vontade. Mapeamento 1:1 com os sprints do doc 05 §12.2 (material
adotado) — a fase muda de nome, não de conteúdo.

**Checklist de fecho (gate humano 2026-11-12 19:01:23):** além da consolidação do memory bank
e do snapshot do plano, atualizar a cada `PHASE_CLOSED` o `README.md` (§Estado atual) e os
ponteiros de fase de `docs/runbooks/**` (índice + runbooks com "PENDENTE-fase") — ponteiro velho
engana teste e revisão humana.

| Phase | Goal | Small deliveries | Status | Closed at |
| --- | --- | --- | --- | --- |
| P-01 | Fundação verificável: a instância nasce com estrutura, regra de dependência no build e storage que impõe ETag | TASK-0001..TASK-0010 — todas DONE com QA PASSED (detalhe em `plan/history/0002-plan.md`) | **CLOSED** | 2026-11-07 08:30:00 |
| P-02 | Identidade: ator local com perfil, chaves e descoberta | TASK-0012..TASK-0016 (SPEC-0007) — **5/5 DONE com QA PASSED** (QA-0011..QA-0015); gate da casa 167/167; detalhe em `plan/history/0003-plan.md` | **CLOSED** | 2026-11-08 09:45:19 |
| P-03 | Contêineres sociais: grupo/comunidade com papéis, política versada e sucessão de propriedade | TASK-0017..TASK-0022 (SPEC-0008) — **6/6 DONE com QA PASSED** (QA-0016..QA-0021); gate da casa 244/244; detalhe em `plan/history/0004-plan.md` | **CLOSED** | 2026-11-09 04:55:35 |
| P-04 | Publicação e feed: post, timeline incremental, SSE ao vivo | TASK-0023..TASK-0032 (SPEC-0009) — **10/10 DONE com QA PASSED** (QA-0022..QA-0032); gate da casa 432/432; detalhe em `plan/history/0005-plan.md` | **CLOSED** | 2026-11-11 20:00:27 |
| P-05 | Mensageria: conversa 1-1 e em grupo, ordem total, resposta e reação | TASK-0033..TASK-0040 (SPEC-0010) — **8/8 DONE com QA PASSED na 1ª tentativa** (QA-0033..QA-0040); gate da casa 480/480; detalhe em `plan/history/0006-plan.md` | **CLOSED** | 2026-11-12 12:46:09 |
| P-06 | Federação: dois silos, assinatura RSA-SHA256, interop real Mastodon/Friendica | TASK-0041..TASK-0050 — **10 entregas** (SPEC-0011; TASK-0050 cortada em 2026-11-13 do parked SSRF da TASK-0044); aberta 2026-11-13, **5/10 DONE** | **OPEN** | — |
| P-07 | Backup cifrado e restore drill no CI **+ rotação de chave `RK`/`RC`** (dono decidido pelo humano 2026-11-12 — S-39) | a cortar | NOT_STARTED | — |
| P-08 | Front web SSR, preview social e compartilhamento externo | a cortar | NOT_STARTED | — |

### P-01 — entregas

| Task | Entrega | Touches | Parallel-safe |
| --- | --- | --- | --- |
| TASK-0001 | Fundação do repositório: `git init`, `.gitignore`, estrutura de pastas da ADR-21, storage do fluxo `docs/sdd/_tools` | `.gitignore`, `.gitattributes`, `.editorconfig`, `README.md`, `backend/.gitkeep`, `web/.gitkeep`, `infra/.gitkeep`, `docs/sdd/_tools/check-task-0001.ps1` | no (base de todos) |
| TASK-0002 | `backend/Squid.slnx` com os 10 projetos, central package management e regra de dependência verificada pelo build | `backend/**` | no (blockedBy TASK-0001) |
| TASK-0003 | `IGrainStorage` SQLite com ETag imposto (`WHERE Version = @Version` → `InconsistentStateException`) e store name nunca hardcoded | `backend/src/Squid.Persistence/**`, `backend/tests/Squid.Tests/**` | no (blockedBy TASK-0002) |
| TASK-0005 | Silo Orleans + `/health` + `/ready` + Orleans Dashboard em `Squid.Service` | `backend/src/Squid.Service/**`, `backend/Directory.Packages.props`, `backend/tests/Squid.Tests/**` | no (blockedBy 0003+0004; 0006 toca os mesmos) |
| TASK-0006 | OTel exporter OTLP + Serilog com sanitização de PII | mesmos de TASK-0005 | no (blockedBy TASK-0005) |
| TASK-0007 | `ContainerPolicy` + `IPolicyEvaluator` + testes da matriz no Domain (SPEC-0003) | `backend/src/Squid.Domain/**`, `backend/tests/Squid.Tests/**` | yes — disjunto de tudo que está aberto |
| TASK-0008 | Containerfile multi-stage + `infra/compose.yaml` + DoD da fase (SPEC-0004) | `infra/**`, `docs/sdd/_tools/sdd-test.ps1` | yes — disjunto de tudo que está aberto |
| TASK-0009 | Workflow GitHub Actions build+testes ubuntu com prova local (SPEC-0005) | `.github/**`, `docs/sdd/_tools/sdd-test.ps1\|sh` | yes — disjunto de tudo que está aberto |
| TASK-0010 | Cinco runbooks do §8 + índice (SPEC-0006) | `docs/runbooks/**` | yes — diretório novo, disjunto de tudo |

Escopo da P-01: **Sprint 0 inteiro** (decisão humana 2026-11-06, mapeamento 1:1 com o doc 05).
Gate de saída provado: `podman compose up` → `/health` verde → Dashboard acessível → trace de
uma request visível no coletor (DoD do Sprint 0, doc 05 §12.2). Entregas, decisões e pendências
em `plan/history/0002-plan.md`.

### P-02 — entregas

| Task | Entrega | Touches | Parallel-safe |
| --- | --- | --- | --- |
| TASK-0012 | Primitivas de chaves: `RK`/`RC`, HKDF (`fed/ed25519-sign`, `fed/rsa-wrap`), Argon2id, embrulho RSA (BouncyCastle) | `backend/src/Squid.Domain/Crypto/**`, `backend/Directory.Packages.props`, `backend/tests/Squid.Tests/Crypto/**` | yes (disjunto de TASK-0013) |
| TASK-0013 | `ActorGrain` event-sourced: replay == estado direto, snapshot + truncamento observável, `handle` único | `backend/src/Squid.Grains/Actors/**`, `backend/tests/Squid.Tests/Actors/**` | yes (disjunto de TASK-0012) |
| TASK-0014 | Registro + login/sessão: `POST /api/auth/register` (409 duplicado), cookie `squid_session`+CSRF, `logout`/`logout-all` | `backend/src/Squid.Api/Auth/**`, `backend/src/Squid.Service/**`, `backend/tests/Squid.Tests/Auth/**` | no (blockedBy 0012+0013; toca `Squid.Api`) |
| TASK-0015 | Webfinger + perfil JSON-LD com round-trip canônico (chaves públicas RSA+Ed25519) | `backend/src/Squid.Api/Discovery/**`, `backend/src/Squid.Protocols/**`, `backend/src/Squid.Service/**`, `backend/tests/Squid.Tests/Discovery/**` | no (blockedBy 0014; `Squid.Api`/`Service` compartilhados) |
| TASK-0016 | Kit: gerar (mnemônico 24 palavras uma vez), verificar (FR-007), importar em instância limpa restaurando identidade (FR-006) | `backend/src/Squid.Api/Recovery/**`, `backend/src/Squid.Service/**`, `backend/tests/Squid.Tests/Recovery/**` | no (blockedBy 0014; serial com 0015 por `Squid.Api`) |

Escopo da P-02: **Sprint 1 inteiro + sessão** (decisão humana 2026-11-07, 5 entregas).
Gate de saída (DoD Sprint 1, doc 05): `webfinger` resolve; perfil servido em JSON-LD; kit baixado
restaura a `RK` em teste. Spec: `specs/SPEC-0007-identidade-ator-local-chaves-sessao-kit.md`.

### P-03 — entregas

| Task | Entrega | Touches | Parallel-safe |
| --- | --- | --- | --- |
| TASK-0017 | Núcleo: agregado `Container` + presets (data-model §2.2), store SQLite com concorrência otimista, `POST/GET/PATCH /api/containers`, Owner automático, 404 idêntico (FR-018) | `backend/src/Squid.Domain/Containers/**`, `backend/src/Squid.Api/Containers/**`, `backend/src/Squid.Service/**`, `backend/tests/Squid.Tests/Containers/**` | no (base da fase) |
| TASK-0018 | `PUT /policy` com `If-Match` (409/428), trilha `PolicyDecision` append-only (FR-019/SC-011), `GET /api/admin/audit`, operador `Instance:Operators` (FR-008 mínimo) | `backend/src/Squid.Api/Containers/**`, `backend/src/Squid.Api/Admin/**`, `backend/src/Squid.Service/**`, `backend/tests/Squid.Tests/Containers/**` | no (serial com 0017) |
| TASK-0019 | Membership §3.3: `join` (open/request/invite/closed), `invites`, `leave` (`409 last_owner`), `GET /members` paginado filtrado por papel — SC-006 | `backend/src/Squid.Domain/Containers/**`, `backend/src/Squid.Api/Containers/**`, `backend/src/Squid.Service/**`, `backend/tests/Squid.Tests/Containers/**` | no (serial) |
| TASK-0020 | `PATCH /members/{actorId}` auditado (FR-013), estados §3.3 (ban com justificativa, Owner não banível), `IPolicyEvaluator` com contexto do ator (fecha GAP-11; facet `singlePublisher` do DoD) | `backend/src/Squid.Domain/**`, `backend/src/Squid.Api/Containers/**`, `backend/src/Squid.Service/**`, `backend/tests/Squid.Tests/{Containers,Domain}/**` | no (serial) |
| TASK-0021 | Sucessão/claim por inatividade (90d, relógio injetável), `transfer`, `DELETE` com confirmação (soft delete 30d), override do operador com justificativa (`422`) — facet "nunca órfão" | `backend/src/Squid.Domain/Containers/**`, `backend/src/Squid.Api/Containers/**`, `backend/src/Squid.Api/Admin/**`, `backend/src/Squid.Service/**`, `backend/tests/Squid.Tests/Containers/**` | no (serial) |
| TASK-0022 | `GET /api/containers/search?q=` só listados (FR-016/R-10) + facet de fecho do DoD do Sprint 2 | `backend/src/Squid.Api/Containers/**`, `backend/src/Squid.Service/**`, `backend/tests/Squid.Tests/Containers/**` | no (serial; última) |

Escopo da P-03: **Sprint 2 inteiro** (decisão humana 2026-11-08 10:57:14 — entrevista de
abertura, 3/3 defaults aceitos: escopo Sprint 2; agregado = estado + ETag, não event-sourced;
operador = `Instance:Operators` em config). Gate de saída (DoD Sprint 2): um grupo `invite`
só entra por convite; `singlePublisher` bloqueia `Member` e permite o publisher nomeado;
contêiner nunca fica órfão (sucessão e override cobertos por teste).
Spec: `specs/SPEC-0008-containers-sociais-p03.md`.

### P-04 — entregas

| Task | Entrega | Touches | Parallel-safe |
| --- | --- | --- | --- |
| TASK-0023 | Núcleo: `POST /api/containers/{slug}/posts` (texto) + `GET /api/posts/{id}` + replies ≤3 + `Object.Seq` por contêiner + evento `Activity` + suspenso negado | `backend/src/Squid.Domain/Objects/**`, `backend/src/Squid.Api/Posts/**`, `backend/src/Squid.Service/**`, `backend/tests/Squid.Tests/Posts/**` | no (base da fase) |
| TASK-0024 | Migration versionada idempotente (`timeline_item`, `home_timeline`, contador global, `quota_ledger`) + projetor Dapper + pump no Worker | `backend/src/Squid.Persistence/schema/**`, `backend/src/Squid.ReadModels/**`, `backend/src/Squid.Worker/**`, `backend/src/Squid.Service/**`, `backend/tests/Squid.Tests/Projections/**` | no (serial) |
| TASK-0025 | Feed e timeline com `?since=` delta exato + visibilidade corrente (FR-023/025/026) | `backend/src/Squid.ReadModels/**`, `backend/src/Squid.Api/Posts/**`, `backend/src/Squid.Service/**`, `backend/tests/Squid.Tests/Feed/**` | no (serial) |
| TASK-0026 | SSE `GET /api/stream` (Channel, `Last-Event-ID`, ping 15 s, visibilidade do assinante) | `backend/src/Squid.Api/Posts/**`, `backend/src/Squid.Service/**`, `backend/tests/Squid.Tests/Stream/**` | no (serial) |
| TASK-0027 | Upload `POST /api/media` (SkiaSharp, EXIF→blurHash, magic bytes 415/413, blob SHA-256) + `GET /api/media/{hash}` + anexos ≤10 | `backend/src/Squid.Api/Media/**`, `backend/src/Squid.Api/Posts/**`, `backend/src/Squid.Domain/Objects/**`, `backend/Directory.Packages.props`, `backend/src/Squid.Service/**`, `backend/tests/Squid.Tests/Media/**` | no (serial) |
| TASK-0028 | Cota: gate `507`, ledger inteiro, reconciliação diária por varredura | `backend/src/Squid.Api/Media/**`, `backend/src/Squid.ReadModels/**`, `backend/src/Squid.Worker/**`, `backend/src/Squid.Service/**`, `backend/tests/Squid.Tests/Quota/**` | no (serial) |
| TASK-0029 | Link unfurl (OpenGraph, timeout 3 s, fail-open, guarda SSRF) | `backend/src/Squid.Api/Posts/**`, `backend/src/Squid.Service/**`, `backend/tests/Squid.Tests/Unfurl/**` | no (serial) |
| TASK-0030 | Remoção `DELETE /posts` (tombstone + `RemoveContent` + projeção) — FR-027 | `backend/src/Squid.Api/Posts/**`, `backend/src/Squid.ReadModels/**`, `backend/src/Squid.Service/**`, `backend/tests/Squid.Tests/Posts/**` | no (serial) |
| TASK-0031 | Denúncia + fila `/api/admin/reports` + resolve keep/remove/ban + `suspend` — FR-028/FR-008 | `backend/src/Squid.Api/Posts/**`, `backend/src/Squid.Api/Admin/**`, `backend/src/Squid.Service/**`, `backend/tests/Squid.Tests/Moderation/**` | no (serial) |
| TASK-0032 | Thumbnail no Worker + load test P50/P99 (SC-004) + facet de fecho do DoD do Sprint 3 | `backend/src/Squid.Worker/**`, `backend/src/Squid.Api/Media/**`, `backend/src/Squid.Service/**`, `backend/tests/Squid.Tests/Load/**` | no (serial; última) |

Escopo da P-04: **Sprint 3 inteiro + remoção (FR-027) + denúncia (FR-028) + cota (FR-029)**
(decisão humana 2026-11-09, entrevista de abertura em 3 rodadas: migration/banco do SQLite
liberados para teste/dev/CI com script versionado — produção segue gate; `SkiaSharp` como pacote
único de imagem; ledger de cota em SQL+Dapper com emenda ao data-model §5; `Dapper` aprovado).
Gate de saída (DoD Sprint 3): post em comunidade aparece no feed em < 1 s via SSE; `since`
entrega só o delta; unlisted não aparece para não-membro. Gate adicional: SC-004 (10k posts,
P95 < 1 s) no load test. Spec: `specs/SPEC-0009-publicacao-feed-postagem.md`.

### P-05 — entregas

| Task | Entrega | Touches | Parallel-safe |
| --- | --- | --- | --- |
| TASK-0033 | Núcleo: migration `thread`/`thread_participant` + store com ETag (`MessageSeq` total) + `POST/GET /api/threads` (direct=2, group=2..50, criador=admin) | `backend/src/Squid.Persistence/schema/**`, `backend/src/Squid.Api/Threads/**`, `backend/src/Squid.Service/**`, `backend/tests/Squid.Tests/Threads/**` | no (base da fase) |
| TASK-0034 | Envio e leitura: `POST .../messages` (seq sob ETag, ≤4000 sanitizado), `GET .../messages?since=` delta exato, 404 idêntico (FR-031) | `backend/src/Squid.Api/Threads/**`, `backend/src/Squid.Service/**`, `backend/tests/Squid.Tests/Threads/**` | no (serial) |
| TASK-0035 | Participantes e admin: `POST .../participants`, remover com `409 last_participant`, `POST/DELETE .../participants/{actorId}/admin`, sucessão do criador (GAP-30/31/32) | `backend/src/Squid.Api/Threads/**`, `backend/src/Squid.Service/**`, `backend/tests/Squid.Tests/Threads/**` | no (serial) |
| TASK-0036 | Aviso não-E2EE: `POST .../ack-no-e2ee` + `428` na 1ª mensagem (FR-032/§9.6) + `POST .../read` com `lastReadSeq` monotônico (FR-033) | `backend/src/Squid.Api/Threads/**`, `backend/src/Squid.Service/**`, `backend/tests/Squid.Tests/Threads/**` | no (serial) |
| TASK-0037 | Resposta: `replyToId` só na mesma thread (`422 invalid_reply_target`), referência só a participantes (FR-057/§9.9) | `backend/src/Squid.Api/Threads/**`, `backend/src/Squid.Service/**`, `backend/tests/Squid.Tests/Threads/**` | no (serial) |
| TASK-0038 | Reação: whitelist em config (`422 invalid_emoji`), idempotente por (msg, ator, emoji), contagem derivada, só participantes (FR-058/§9.10) | `backend/src/Squid.Api/Threads/**`, `backend/src/Squid.Service/**`, `backend/tests/Squid.Tests/Threads/**` | no (serial) |
| TASK-0039 | Anexo em mensagem: `contentHashes` ≤10 reusando `POST /api/media` intacto + ampliação da regra de visibilidade de `GET /api/media` | `backend/src/Squid.Api/Threads/**`, `backend/src/Squid.Api/Media/**`, `backend/src/Squid.Service/**`, `backend/tests/Squid.Tests/Threads/**` | no (serial) |
| TASK-0040 | Facet de fecho do DoD do Sprint 4: dois conversam, terceiro não lê, ordem sob concorrência, resposta/reação idempotentes | `backend/tests/Squid.Tests/Threads/**` (+ toques mínimos se a facet acusar lacuna) | no (serial; última) |

Escopo da P-05: **Sprint 4 inteiro + anexos em mensagem** (entrevista 2026-11-11, 5 rodadas,
13 questões; decisões 1/4/5/6/7/9/10 nas seções abaixo). Gate de saída (DoD Sprint 4): dois
usuários conversam; terceiro não lê; ordem estável sob envio concorrente; resposta e reação
visíveis só aos participantes e idempotentes. Spec: `specs/SPEC-0010-mensageria-p05.md`.
Emendas ao material adotado: data-model §2.7 (admin de thread) e contrato §6 (adicionar
participante, rotas de admin, anexo) — originais intactos, precedente S-27/P-03.

### P-06 — entregas

| Task | Entrega | Touches | Parallel-safe |
| --- | --- | --- | --- |
| TASK-0041 | Domínio canônico e proxy: `Instance:Domain` opt-in + `X-Forwarded-*` de confiança, origem canônica em helper único nos 4 pontos scheme+Host (I-11) | `backend/src/Squid.Api/{Discovery,Posts,Containers,Threads}/**`, `backend/src/Squid.Service/**`, `backend/tests/Squid.Tests/**` | no (base da fase) |
| TASK-0042 | Chave de assinatura em runtime (fecha **GAP-09**): `Squid:RecoveryKey` no boot, desembrulho RSA só em memória + assinar/verificar HTTP (draft-cavage rsa-sha256/hs2019 + RFC 9421) | `backend/src/Squid.Api/Crypto/**`, `backend/src/Squid.Grains/Actors/**`, `backend/src/Squid.Protocols/**`, `backend/src/Squid.Service/**`, `backend/tests/Squid.Tests/**` | no (serial) |
| TASK-0043 | Superfície ActivityPub: ator completo com `inbox`/`outbox`/`followers`/`following` (**fecha S-26**), coleções paginadas, objeto, contêiner, nodeinfo, Accept negotiation | `backend/src/Squid.Api/Discovery/**`, `backend/src/Squid.Api/Federation/**`, `backend/src/Squid.Protocols/**`, `backend/src/Squid.Service/**`, `backend/tests/Squid.Tests/**` | no (serial) |
| TASK-0044 | Inbox: `InboxGrain` (dedupe por `activity.id`), ordem §3 (assinatura→digest→data→tamanho→rate→dedupe→remetente, FR-040) e aplicação de `Create`/`Follow`/`Undo`/`Delete`/`Like`/`Flag` | `backend/src/Squid.Grains/Inbox/**`, `backend/src/Squid.Api/Federation/**`, `backend/src/Squid.Api/Posts/**`, `backend/src/Squid.Protocols/**`, `backend/src/Squid.Service/**`, `backend/tests/Squid.Tests/**` | no (serial) |
| TASK-0045 | Saída: fan-out por visibilidade (§3.2/§4), reavaliação R-05 → `Cancelled`, migration `0008_federation.sql` (`follow`/`peer`/`peer_delivery`) e rota `POST /api/federation/follow` (GAP-41) | `backend/src/Squid.Persistence/schema/**`, `backend/src/Squid.Api/{Posts,Federation,Containers}/**`, `backend/src/Squid.ReadModels/**`, `backend/src/Squid.Protocols/**`, `backend/src/Squid.Service/**`, `backend/tests/Squid.Tests/**` | no (serial) |
| TASK-0046 | `PeerGrain`: fila, backoff exponencial c/ jitter (N=8, teto 6 h), circuit breaker, DLQ com motivo + rate por par (FR-036) + `PeerClient` com falha 500/timeout/lentidão | `backend/src/Squid.Grains/Peers/**`, `backend/src/Squid.PeerClient/**`, `backend/src/Squid.Service/**`, `backend/src/Squid.ReadModels/**`, `backend/tests/Squid.Tests/**` | no (serial) |
| TASK-0050 | **Pares confiáveis** (GAP-49, cortada 2026-11-13 do parked SSRF da TASK-0044): allowlist `Federation:TrustedPeerHosts` vazia por padrão, só nos clientes de federação — destrava 0047/0048/0049 em loopback/Docker | `backend/src/Squid.Api/Posts/**`, `backend/src/Squid.Service/**`, `backend/tests/Squid.Tests/**` | no (serial, blockedBy 0044+0046, blocks 0047) |
| TASK-0047 | Facet de dois silos A→B (tests-only): contrato §7 itens 1–9, 14, 15 | `backend/tests/Squid.Tests/Federation/**` | no (serial) |
| TASK-0048 | **Runbook pedido pelo humano**: `docs/runbooks/federacao-interop.md` (dois silos + Mastodon/Friendica em Docker + validação) + `peer-entrega-travada`/`dlq-cheia` EXECUTÁVEIS + índice | `docs/runbooks/**` | no (serial, blockedBy 0047) |
| TASK-0049 | Interop real Mastodon/Friendica + facet de fecho do DoD (SC-016, §7 itens 11–13) — **gate humano** (pares reais, execução conjunta) | `backend/tests/Squid.Tests/Federation/**`, correções mínimas provadas por log real (`Squid.Protocols`, `Squid.Api/Federation`, `Squid.Grains`), `docs/runbooks/federacao-interop.md` | no (serial; última) |

Escopo da P-06: **Sprint 5 inteiro** (entrevista 2026-11-13, 2 rodadas / 6 questões — decisões
1..6 abaixo). Gate de saída (DoD Sprint 5): post público de A aparece em B; `membersOnly` não
aparece; dedupe rejeita entrega repetida; peer derrubado não trava o envio; Mastodon e Friendica
reais exibem o post público e nenhuma entrega falha por assinatura. Spec:
`specs/SPEC-0011-federacao-p06.md`.

## Current state

- phase: **P-06 (Federação) OPEN** (aberta 2026-11-13 13:47:25) — 10 entregas cortadas de
  SPEC-0011 (`TASK-0041..TASK-0050`; a 0050 nasceu 22:45:47 do parked SSRF da 0044), **5/10
  DONE** (0041..0045 com QA PASSED na 1ª tentativa; gates 484 → 539), D-01 fechada por gate
  humano (21:23:34, só destino nomeado). **Em aberto**: gate humano sobre a assinatura de
  atores registrados (RK por ator × `Squid:RecoveryKey` — resposta vira TASK-0051 antes da
  0047). P-05 fechou em 2026-11-12 12:46:09 (8/8, gate 480/480, snapshot `0006-plan.md`); a
  partir do fecho de cada fase o **código é a fonte real** daquela fase (divergência =
  `[DIVERGENCIA]`, código vence); P-01 fecha no 0002, P-02 no 0003, P-03 no 0004, P-04 no
  0005, P-05 no 0006.
- authority: **SPEC** para P-06 (nasce SPEC, vira CODE no fecho); **CODE** para P-01..P-05.
- workspace: `C:\dev\squid-squad-net\squid-execution` (desde 2026-11-05). O caminho antigo
  `C:\Users\Ryke\Projetos\squid-squad-net` carrega rótulo de integridade que nega escrita e **não é
  mais o workspace**; fica apenas como backup. Ver `runs/RUNBOOK-retomada.md`.
- nextAction: **dispatch DEV da TASK-0041** (fila serial 0041→0049, uma task por dispatch);
  o **manual** pedido pelo humano é a TASK-0048 (`docs/runbooks/federacao-interop.md`, antes do
  fecho) e a **TASK-0049 executa a interop real com o humano junto** — pares Mastodon/Friendica em
  Docker local, gate humano no dispatch. Nada de deploy (nenhum push é meu de fazer — a CI só roda
  pós-push, gate humano; último run verde 98/98 de 07/11, anterior à P-04 — considere um push
  para revalidar a CI com os testes novos da P-04/P-05: gate local está em 480/480).
- **Git a partir da P-05 (gate PHASE 2026-11-11 20:42:28, modelo C)**: branch por task
  `task/TASK-NNNN-<slug>` desde `develop`; DEV commita ao fim da entrega, QA commita no PASSED,
  ODEV mergeia em `develop` no `DONE` e commita docs; push/force/`main`/apagar branch =
  proibido a agentes, merge+tag de fecho em `main` = humano. Os dispatchs DEV/QA devem incluir
  a linha de autorização de commit deste gate. Commits da P-01..P-04 não mudam.
- blocking gaps: NENHUM — **GAP-09 fechado por decisão humana** na abertura da P-06 (SPEC-0011
  decisão 2: `Squid:RecoveryKey` em segredo de ambiente, desembrulho só em memória); novos
  GAP-41..GAP-47 assumidos na SPEC-0011; `S-26` fecha na TASK-0043 (inbox/outbox/followers/
  following) e `I-11` na TASK-0041 — checagem do backlog pedida ao cortar a fase; abertos e
  carregados: GAP-02, GAP-04/05/06, GAP-13..GAP-18 (SPEC-0008),
  GAP-21..GAP-28 (SPEC-0009), GAP-29..GAP-40 (SPEC-0010 — 29..35 resolvidos por humano,
  36..40 assumidos; 36 = ao vivo não decidido, 37 = UI do aviso é P-08),
  S-11/S-12/S-19/S-22..S-38 (backlog), suite Linux nativa unverified (CI é o executor,
  WSL sem dotnet sondado a vivo em toda QA da P-05).

## Adopted legacy documentation

Material que você já tinha, recebido fora do padrão do fluxo. Os originais nunca são movidos,
renomeados ou reescritos aqui; esta tabela é o ponteiro. Por decisão sua (2026-11-04), este
repositório **não referencia nada dentro de `squid-specs/`**: o que é necessário já está copiado
neste repositório, e os checkouts de Mastodon e Friendica são **apenas inspiração de ideias e
nomes** — nenhum arquivo deles é copiado, nem como trecho.

| Original path | Classification | Action | Now lives as |
| --- | --- | --- | --- |
| `squid-execution/docs/features/05-arquitetura-consolidada.md` | ESTABELECIDA | adotado — é o documento mestre (ADR 1..36, §4.7 padrões obrigatórios, §11 layout, §12.2 sprints, §14 GAPs). Mantido no lugar, sem reescrita | REQ-0001, SPEC-0001, este plano |
| `squid-execution/specs/001-squid-mvp/spec.md` | ESTABELECIDA | adotado — 63 FRs, 18 SCs, 16 entidades, 8 user stories; traduzido em REQ-0001 e SPEC-0001 sem reentrevistar o que já foi decidido | REQ-0001, SPEC-0001 |
| `squid-execution/specs/001-squid-mvp/plan.md` | ESTABELECIDA | adotado como insumo — a ordem de dependência dos sprints vira P-01..P-08; o estado de execução passa a ser este arquivo | este plano |
| `squid-execution/specs/001-squid-mvp/data-model.md` | ESTABELECIDA | adotado como insumo (entrou de verdade na P-02 via §2.12 `RecoveryKit`); **emenda humana 2026-11-08**: §2.12 lista `RecoveryCredentialHash` mas o código persiste `RC` em claro — decisão S-27(a) do fecho; original mantido sem reescrita, a emenda vive aqui + `memory-bank/contracts.md` + `ideas/BACKLOG.md` S-27 | SPEC-0007, REQ-0001 |
| `squid-execution/specs/001-squid-mvp/contracts/http-api.md` | ESTABELECIDA | adotado como insumo de SPEC-0007 (P-02: §2 auth/sessão e §3 recovery em uso) e de SPEC-0008 (P-03: §4 contêineres, §7 audit/override, §9 testes de contrato); contrato publicado vence onde a spec detalhar de outro jeito | SPEC-0007, SPEC-0008 |
| `squid-execution/specs/001-squid-mvp/contracts/activitypub.md` | ESTABELECIDA | adotado como insumo de SPEC-0007 (P-02: descoberta/perfil §1 em uso; `inbox`/`outbox`/`rel=profile` ficam para P-06/P-08 — S-26) | SPEC-0007, ideas/BACKLOG.md |
| `squid-execution/specs/001-squid-mvp/contracts/backup-recovery.md` | ESTABELECIDA | adotado como insumo de SPEC-0007 (a cortar, P-07) | NENHUM (por ora) |
| `squid-execution/specs/001-squid-mvp/research.md` | PARCIAL | adotado como insumo de decisão (R-01..R-21); algumas decisões citam referências externas e serão relidas ao cortar a tarefa que depende delas | NENHUM (por ora) |
| `squid-execution/specs/001-squid-mvp/quickstart.md` | ESTABELECIDA | adotado como matriz de QA ponta a ponta a partir da P-02 | NENHUM (por ora) |
| `squid-execution/docs/features/03-reaproveitamento-orders-orleans.md` | PARCIAL | parcialmente adotado — só a lição dos 10 defeitos, já transformada em regra no §4.7; §3.8/§7 desatualizados, conforme o pacote registra | SPEC-0001 §Rules |
| `squid-execution/docs/features/04-recuperacao-chaves-criptografia.md` | PARCIAL | parcialmente adotado — hierarquia de chaves; o par RSA (§1.2) ainda não está nele, é GAP-03 | NENHUM (até a P-02) |
| `squid-execution/docs/constitution.md` | ESTABELECIDA | adotado como princípios e DoD; o doc 05 prevalece onde divergir | SPEC-0001 §Non-functional |
| `squid-execution/README.md`, `MANIFEST.md`, `DECISIONS.md`, `verify-hashes.ps1` | PARCIAL | mantidos como estão: documentação de handoff do pacote. Não são fonte; o digest de decisões foi absorvido em REQ-0001 | REQ-0001 (digest) |
| `squid-execution/verify-hashes.ps1` + `MANIFEST.md` | ESTABELECIDA | executados no bootstrap: 13/13 artefatos conferidos, 0 problemas | evidence em TASK-0001 |
| `squid-specs/ideias-references/friendica-2026.08-rc`, `mastodon-main` | ESTABELECIDA | **ignorado como código** (decisão sua, 2026-11-04) — nenhum arquivo, trecho ou citação de código; apenas ideias e nomes de vocabulário do protocolo, quando uma spec futura precisar | NENHUM |
| `squid-specs/ideias-references/orders-orleans`, `federation` | PARCIAL | **ignorado como código** (decisão sua) — projetos de estudo que já geraram as regras do §4.7; nada é importado | NENHUM |
| `squid-specs/specs/002`, `003`, `004` | ESTABELECIDA | ignorados por enquanto (ADRs 30..32) — virarão ideias parked I-01..I-03, sem cópia de conteúdo | ideas/BACKLOG.md |
| `squid-specs/features/00-inicio.md`, `01-refino-ideia.md`, `02-arquitetura-poc.md` | PARCIAL | ignorados — registro histórico da ideia original; o doc 05 vence (doc 05 §16) | NENHUM |
| `squid-specs/.specify/`, `.dsh/` | AUSENTE (para este fluxo) | ignorados — Spec Kit ferramental, não é insumo de execução (ADR-27) | NENHUM |

## Decisions taken by a human

| Date/Time | Decision | Scope | Approved by |
| --- | --- | --- | --- |
| 2026-11-04 11:58:28 | Repositório git nasce dentro de `squid-execution/` (repo próprio); `squid-specs/` fica fora | PHASE | Henrique |
| 2026-11-04 11:58:28 | Não referenciar documentação de `squid-specs/`; se necessário, copiar para arquivos `.md` aqui dentro | PHASE | Henrique |
| 2026-11-04 11:58:28 | Nada é copiado de Mastodon ou Friendica (código publicado): só ideias e nomes | PHASE | Henrique |
| 2026-11-04 11:58:28 | Identidade git global: `Henrique <henriquehg@gmail.com>` | PHASE | Henrique |
| 2026-11-04 11:58:28 | Primeira carga P-01 = três entregas: fundação do repo, solution + regra de dependência, storage SQLite com ETag | PHASE | Henrique |
| 2026-11-06 07:35:15 | D-01: instalar `Microsoft.Orleans.*` 10.2 + `Microsoft.Data.Sqlite` 10.0 com versão em `Directory.Packages.props` (contrato local sem Orleans rejeitado) | TASK | Henrique |
| 2026-11-06 09:27:51 | P-01 = Sprint 0 inteiro do doc 05 (mapeamento 1:1), fecha só com o DoD completo `podman compose up → /health → Dashboard → trace` | PHASE | Henrique |
| 2026-11-06 09:27:51 | Autorizado conjunto de pacotes de observabilidade de uma vez: OTel (hosting + exporter OTLP + instrumentação ASP.NET Core), Serilog + Serilog.AspNetCore e pacote de Orleans Dashboard compatível com 10.2 (escolha por sonda de DEV), versão em `Directory.Packages.props` | PHASE | Henrique |
| 2026-11-06 17:12:45 | Instância ODEV única: esta sessão (Web) é a canônica; o fork do Telegram foi renomeado e arquivado pelo humano para eliminar dupla escrita no estado canônico | PHASE | Henrique |
| 2026-11-07 16:06:40 | GAP-01: chave pública Ed25519 derivada da `RK` via HKDF (continuidade após restaurar o kit) | TASK | Henrique |
| 2026-11-07 16:06:40 | GAP-03: par RSA-2048 gerado no registro, embrulhado por `HKDF(RK,"fed/rsa-wrap")`, no estado do ator e no kit | TASK | Henrique |
| 2026-11-07 16:06:40 | GAP-07: autenticação = senha + sessão cookie com Argon2id (passkey/OIDC reservados) | PHASE | Henrique |
| 2026-11-07 16:06:40 | GAP-08: instalar `BouncyCastle.Cryptography` como pacote único de cripto (instalar dependência = gate humano) | PHASE | Henrique |
| 2026-11-07 16:06:40 | Escopo da P-02 = Sprint 1 completo + sessão (5 entregas: chaves, ActorGrain, registro/login, webfinger/JSON-LD, kit) | PHASE | Henrique |
| 2026-11-08 09:45:19 | S-27 fechado pela opção (a): manter `KitRecord.RecoveryCode` em claro no storage (re-export idêntico do kit preservado) e registrar a decisão como emenda ao `data-model.md` §2.12 — trade-off aceito: `RC` vazável com o banco, mas sem `RK` (Argon2id) não abre nada; nenhuma rota vaza; caminho (b) fica no backlog como melhoria futura | PHASE | Henrique |
| 2026-11-08 10:57:14 | Escopo da P-03 = Sprint 2 inteiro: 7 itens do doc 05 + DoD + rotas do contrato §4/§7 relevantes + FR-016 busca + FR-019/SC-011 auditoria — 6 entregas | PHASE | Henrique |
| 2026-11-08 10:57:14 | Agregado `Container` da P-03 = estado simples com ETag (reusa a regra da TASK-0003) + trilha de auditoria append-only separada; **não** event-sourced como o `ActorGrain`; membership em store própria nos dois caminhos | PHASE | Henrique |
| 2026-11-08 10:57:14 | Operador da instância em P-03 = `Instance:Operators` (lista na config), rotas `/api/admin` de P-03 já exigindo operador; gestão de operadores fica para a fase de admin | PHASE | Henrique |
| 2026-11-08 15:57:34 | Origem do `Id` de contêiner alinhada à regra scheme+Host da P-02 (gate do achado MINOR QA-0016, S-28): SPEC-0008 §A emendada e a correção + teste entram na TASK-0019 como AC-07; em produção atrás de TLS continua `https://` | TASK | Henrique |
| 2026-11-08 17:38:56 | Emenda ao `data-model.md` §2.11 aceitando `ChangePolicy` como valor do enum `Action` da trilha de auditoria (flag CONTRATO da TASK-0018; precedente da emenda S-27 — original mantido sem reescrita, emenda viva aqui + `memory-bank/contracts.md` na consolidação + `ideas/BACKLOG.md`); código da TASK-0018 permanece como está | TASK | Henrique |
| 2026-11-09 11:19:12 | Escopo da P-04 = Sprint 3 inteiro (7 itens + DoD) **+** remoção FR-027 **+** denúncia FR-028 **+** cota FR-029 — ~10 entregas | PHASE | Henrique |
| 2026-11-09 11:19:12 | Migration + criação de banco do SQLite **liberados para teste/dev/CI** com script SQL versionado e idempotente no repositório; produção segue sendo gate humano (resposta à pergunta "o que impede o agente de criar banco e migration?") | PHASE | Henrique |
| 2026-11-09 11:19:12 | `SkiaSharp` aprovado como pacote único de imagem (Apache-2.0), versão só em `Directory.Packages.props` (ADR-25); EXIF stripping e blurHash em código próprio | PHASE | Henrique |
| 2026-11-09 11:19:12 | Ledger de cota em SQL puro + Dapper na mesma migration versionada — emenda ao `data-model.md` §5 (`EF Core (migrations)` → `script SQL versionado + Dapper`) | PHASE | Henrique |
| 2026-11-09 11:30:01 | `Dapper` aprovado como pacote de leitura das projeções (data-model §4 já adotado), versão em `Directory.Packages.props` | PHASE | Henrique |
| 2026-11-10 08:00:15 | `SkiaSharp.NativeAssets.Linux` aprovado (natives Linux do SkiaSharp 4.155.0) — sem ele a CI ubuntu e o compose não decodificam imagem (`DllNotFoundException`); entra como AC da TASK-0028 | TASK | Henrique |
| 2026-11-10 23:53:57 | Parar o desenvolvimento da P-04 **após o fecho da TASK-0031**; a TASK-0032 (entrega 10/10 — thumbnail + load test + facet do DoD) **não é dispatchada sem nova ordem**; a fase fica 9/10 com authority SPEC e sem consolidação (fecho exige a última entrega DONE) | PHASE | Henrique |
| 2026-11-11 08:19:47 | **Ordem de retomada**: continuar a P-04 — parada levantada, TASK-0032 liberada para dispatch; ao fechar a última entrega segue o fluxo normal (consolidação + snapshot + authority CODE) | PHASE | Henrique |
| 2026-11-11 18:34:06 | **Gate do BLOCKED da QA-0032**: autorizado o 3º dispatch da QA da TASK-0032 (após 2 mortes de harness, uma delas deixando mutação M3 ativa) com instrução de restauração primeiro — a decisão humana sobrepõe o limite do redispatch único da política de falha; a subagente restaurou, provou 432/432 e fechou PASSED | TASK | Henrique |
| 2026-11-11 20:42:28 | **Modelo C de git a partir da P-05** (decisão sobre o fluxo por task): (1) cada task nasce na branch `task/TASK-NNNN-<slug>` a partir de `develop`; (2) **DEV** faz 1 commit ao fim da entrega e **QA** faz 1 commit no PASSED, ambos autorizados por este gate (mensagem `TASK-NNNN: <resumo>` e `TASK-NNNN QA-NNNN: PASSED <resumo>`); (3) **ODEV** faz o merge em `develop` na transição para `DONE` e commita documentação de fluxo; (4) **PROIBIDO a todos os agentes**: push, rebase/force, mexer em `main`, apagar branches — push e merge/tag de fecho de fase em `main` continuam **gate humano** (disparam CI); (5) P-01..P-04 ficam como estão (commits por fase) — a granularidade começa na P-05; os dispatchs DEV/QA das próximas fases devem incluir a linha de autorização de commit deste gate | PHASE | Henrique |
| 2026-11-11 21:31:24 | Abertura da P-05 (entrevista rodada 1): escopo = **Sprint 4 inteiro + anexos em mensagem** (7 itens + DoD, ~8 entregas); estado de entrega = **`lastReadSeq`** (entregue = persistido, lido = marcador por participante, sem estado por dispositivo); **ao vivo não decidido** — custo respondido (SSE ~1/3 task, webhook ~1-2 entregas duplicando a fila da P-06, Web Push ~1 task + front), sem decisão nesta fase | PHASE | Henrique |
| 2026-11-11 21:43:37 | Abertura da P-05 (entrevista rodada 2): anexo em mensagem = **reusar `POST /api/media` intacto** (mensagem só referencia `contentHashes`; emenda ao §6); participantes **podem ser adicionados depois da criação**; whitelist de emoji **em configuração da instância** (`Reactions:AllowedEmojis`, 👍 padrão) | PHASE | Henrique |
| 2026-11-11 21:58:46 | Abertura da P-05 (entrevista rodada 3): admin de grupo — **qualquer admin adiciona/remove/promove/demove**, admins eleitos promovem outros admins; regra de sucessão: **criador sai → admin mais antigo assume; sem admin → membro mais antigo promovido a admin/criador** (grupo nunca fica sem admin) | PHASE | Henrique |
| 2026-11-11 22:55:32 | Abertura da P-05 (entrevista rodada 4): **corte aprovado com 8 tasks seriais** (TASK-0033..0040); **2 emendas aprovadas** — (a) data-model §2.7 ganha papéis de admin da thread, (b) contrato §6 ganha `POST .../participants` + rotas de admin + `contentHashes` na mensagem — originais intactos (precedente S-27/P-03) | PHASE | Henrique |
| 2026-11-11 23:08:37 | Abertura da P-05 (entrevista rodada 5): estado da thread = **SQLite + ETag** (não `ThreadGrain`/Orleans — ADR-1 vale, thread não federa); rota de admin = **`POST`/`DELETE /api/threads/{id}/participants/{actorId}/admin`** | PHASE | Henrique |
| 2026-11-12 19:01:23 | **Prática de fecho de fase**: a cada `PHASE_CLOSED` o ODEV atualiza o `README.md` (§Estado atual) e os ponteiros de fase de `docs/runbooks/**` (índice + "PENDENTE-fase") junto com a consolidação — ponteiro velho engana teste e revisão humana; rotação de chave registrada como **S-39** (sem dono de fase, candidata P-07); execução imediata: README, runbooks e gate contagem atualizados no fecho da P-05 | PHASE | Henrique |
| 2026-11-12 20:21:42 | **Rotação de chave é da P-07** (dono decidido pelo humano, fecha o S-39): a fase de backup e chaves corta as tasks que criam a rota de `RK`/`RC`, a reemissão do kit e o teste de cadeia derivada — runbooks `rotacionar-chave` e índice atualizados de "sem dono" para "P-07"; linha da P-07 no plano emendada | PHASE | Henrique |
| 2026-11-13 11:48:04 | Abertura da P-06 (entrevista rodada 1): escopo = **Sprint 5 inteiro** (8 itens + DoD, default aceito); **GAP-09 = `Squid:RecoveryKey` em segredo de ambiente no boot**, desembrulho da chave do ator só em memória no `ActorGrain`, sem rota de reidratação, kit = único caminho de recuperação, **produção = gate humano** para injetar o segredo; **interop real Mastodon/Friendica dentro da fase** como fecho + **manual** para subir as instâncias locais e fazer a comunicação real = entregável pedido pelo humano | PHASE | Henrique |
| 2026-11-13 13:31:11 | Abertura da P-06 (entrevista rodada 2, "concordo com as sugestões dadas"): **`I-11` entra** na fase como entrega cedo (`Instance:Domain` + `ForwardedHeaders`); **manual = runbook próprio** `docs/runbooks/federacao-interop.md` + índice, escrito **antes** da validação de interop; **pares reais em Docker local** (Mastodon + Friendica na máquina do humano, sem exposição à internet) com seção opcional "instância pública real" no runbook | PHASE | Henrique |
| 2026-11-13 21:23:34 | **D-01 da TASK-0044 (gate humano)**: destino de um `Create` remoto = **só destino nomeado** — inbox `/c/{slug}`, ou `squid:slug`/caminho `/c/{slug}` em `audience`/`to`/`cc`/`inReplyTo`; sem destino → `202` sem aplicar; fallback de membria (menor slug) **rejeitado**; a saída da TASK-0045 nomeia o destino (`audience` + `squid:slug`) — SPEC-0011 §E e AC-01/GAP-48 da TASK-0045 emendados, contrato não muda | TASK | Henrique |
