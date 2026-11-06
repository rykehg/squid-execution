<!--
templateVersion: 1.0.0
-->
Project plan; current view only. History lives in `plan/history/` and is never copied back into this file.
Write responsibility: ODEV. Every other role reads it and never edits it.

- status: ATIVO
- owner: odev
- lastVerified: 2026-11-05 18:55:00
- snapshot: plan/history/0001-plan.md

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
| P-01 | Fundação verificável: a instância nasce com estrutura, regra de dependência no build e storage que impõe ETag | TASK-0001 (DONE), TASK-0002 (DONE), TASK-0004 (AWAITING_QA), TASK-0003 (TASK_READY) | OPEN | — |
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

Gate de saída da P-01 (DoD do Sprint 0, doc 05 §12.2): estrutura versionada, `dotnet build` verde
com a regra de dependência violável-detectada, e a suíte de storage provando ETag e isolamento de
store. `/health`, Orleans Dashboard e OTel entram como entregas seguintes da P-01 depois destas três.

## Current state

- phase: P-01
- authority: SPEC
- workspace: `C:\dev\squid-squad-net\squid-execution` (desde 2026-11-05). O caminho antigo
  `C:\Users\Ryke\Projetos\squid-squad-net` carrega rótulo de integridade que nega escrita e **não é
  mais o workspace**; fica apenas como backup. Ver `runs/RUNBOOK-retomada.md`.
- nextAction: despachar QA em TASK-0004 (gate verde: 16/16, exit 0, `runs/test-runs/20261105-190004-test-gate.log`); em seguida DEV em TASK-0003 (storage SQLite com ETag)
- blocking gaps: NENHUM

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