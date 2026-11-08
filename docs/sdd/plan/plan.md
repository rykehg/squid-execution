<!--
templateVersion: 1.0.0
-->
Project plan; current view only. History lives in `plan/history/` and is never copied back into this file.
Write responsibility: ODEV. Every other role reads it and never edits it.

- status: ATIVO
- owner: odev
- lastVerified: 2026-11-08 09:45:19
- snapshot: plan/history/0003-plan.md (fecho da P-02; 0002 é a P-01, 0001 é o bootstrap)

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

| Phase | Goal | Small deliveries | Status | Closed at |
| --- | --- | --- | --- | --- |
| P-01 | Fundação verificável: a instância nasce com estrutura, regra de dependência no build e storage que impõe ETag | TASK-0001..TASK-0010 — todas DONE com QA PASSED (detalhe em `plan/history/0002-plan.md`) | **CLOSED** | 2026-11-07 08:30:00 |
| P-02 | Identidade: ator local com perfil, chaves e descoberta | TASK-0012..TASK-0016 (SPEC-0007) — **5/5 DONE com QA PASSED** (QA-0011..QA-0015); gate da casa 167/167; detalhe em `plan/history/0003-plan.md` | **CLOSED** | 2026-11-08 09:45:19 |
| P-03 | Contêineres sociais: grupo/comunidade com papéis, política versada e sucessão de propriedade | a cortar | NOT_STARTED | — |
| P-04 | Publicação e feed: post, timeline incremental, SSE ao vivo | a cortar | NOT_STARTED | — |
| P-05 | Mensageria: conversa 1-1 e em grupo, ordem total, resposta e reação | a cortar | NOT_STARTED | — |
| P-06 | Federação: dois silos, assinatura RSA-SHA256, interop real Mastodon/Friendica | a cortar | NOT_STARTED | — |
| P-07 | Backup cifrado e restore drill no CI | a cortar | NOT_STARTED | — |
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



## Current state

- phase: **P-02 (Identidade) CLOSED** (2026-11-08 09:45:19) — snapshot congelado em
  `plan/history/0003-plan.md`. A partir do fecho de cada fase o **código é a fonte real** daquela
  fase (divergência = `[DIVERGENCIA]`, código vence); a P-01 fecha no 0002, a P-02 no 0003.
- authority: **CODE** para P-01 e P-02 (fechadas); a P-03, quando aberta, nasce com authority SPEC
  até o próprio fecho.
- workspace: `C:\dev\squid-squad-net\squid-execution` (desde 2026-11-05). O caminho antigo
  `C:\Users\Ryke\Projetos\squid-squad-net` carrega rótulo de integridade que nega escrita e **não é
  mais o workspace**; fica apenas como backup. Ver `runs/RUNBOOK-retomada.md`.
- nextAction: **PARADO no fecho da P-02** — não inicio a P-03 por conta própria. Próximo passo
  humano: `/odev` para abrir a **P-03 (Contêineres sociais)** com entrevista/corte de spec
  (insumos: doc 05 §Sprint 2, FR-008+, SPEC-0003 já existente, FR-016 descoberta = lacuna da fase).
  Nada de deploy nesta fase (nenhum push é meu de fazer).
- blocking gaps: NENHUM — decidido no fecho: S-27 opção (a) (2026-11-08 09:45:19, Henrique);
  GAP-01/03/07/08 fechados; abertos e carregados: GAP-02, GAP-04/05/06, GAP-09 (P-06),
  FR-016 (P-03), S-11/S-12/S-19/S-22..S-26 (backlog), suite Linux nativa (CI é o executor)

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
| `squid-execution/specs/001-squid-mvp/contracts/http-api.md` | ESTABELECIDA | adotado como insumo de SPEC-0007 (P-02: §2 auth/sessão e §3 recovery em uso); contrato publicado vence onde a spec detalhar de outro jeito | SPEC-0007 |
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