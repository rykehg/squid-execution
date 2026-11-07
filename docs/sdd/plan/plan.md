<!--
templateVersion: 1.0.0
-->
Project plan; current view only. History lives in `plan/history/` and is never copied back into this file.
Write responsibility: ODEV. Every other role reads it and never edits it.

- status: ATIVO
- owner: odev
- lastVerified: 2026-11-07 08:30:00
- snapshot: plan/history/0002-plan.md (fecho da P-01; 0001 é o bootstrap)

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
| P-02 | Identidade: ator local com perfil, chaves e descoberta | a cortar | NOT_STARTED | — |
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

## Current state

- phase: P-01 **CLOSED** (2026-11-07 08:30:00) — a partir daqui o **código é a fonte real** e
  `docs/memory-bank/` é a documentação viva; divergência levantada como `[DIVERGENCIA]` e o
  código vence. Snapshot congelado: `plan/history/0002-plan.md`.
- authority: **CODE** (era SPEC durante a P-01; virou no fecho)
- workspace: `C:\dev\squid-squad-net\squid-execution` (desde 2026-11-05). O caminho antigo
  `C:\Users\Ryke\Projetos\squid-squad-net` carrega rótulo de integridade que nega escrita e **não é
  mais o workspace**; fica apenas como backup. Ver `runs/RUNBOOK-retomada.md`.
- nextAction: PARADO no fecho — não inicio a P-02 por conta própria. Próximo passo humano:
  `/odev` para abrir a P-02 (Identidade) com entrevista/corte de spec. Pendência de CI
  **resolvida** (run verde 98/98 em ubuntu-26.04 em 2026-11-07); pós-fecho: TASK-0011
  (bugfix dashboard) DONE com QA-0010 PASSED
- blocking gaps: NENHUM — pendências registradas: GAP-01/GAP-03 (P-02), FR-016 descoberta
  (P-03), S-11 (gêmeo .sh sem executar no run), S-19 (.dockerignore `**/.vs`)

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
| `squid-execution/specs/001-squid-mvp/data-model.md` | ESTABELECIDA | adotado como insumo de SPEC-0002..SPEC-0004 (a cortar); nenhuma tarefa o usa ainda | NENHUM (por ora) |
| `squid-execution/specs/001-squid-mvp/contracts/http-api.md` | ESTABELECIDA | adotado como insumo de SPEC-0005 (a cortar); nenhuma tarefa o usa ainda | NENHUM (por ora) |
| `squid-execution/specs/001-squid-mvp/contracts/activitypub.md` | ESTABELECIDA | adotado como insumo de SPEC-0006 (a cortar, P-06) | NENHUM (por ora) |
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