<!--
templateVersion: 1.0.0
-->
Specification: the resolved reading of a requirement, precise enough to cut into tasks and to test against.
Write responsibility: ODEV.

- status: ATIVO
- owner: odev
- lastVerified: 2026-11-08 11:07:11

# SPEC-0008 — Contêineres sociais: agregado, membership, política versionada, sucessão e descoberta

- requirement: REQ-0001
- phase: P-03
- dependsOn: SPEC-0003 (Domain: `ContainerPolicy` + `IPolicyEvaluator`, entregues na P-01), SPEC-0007 (identidade local, sessão e ator — quem entra e quem é dono)

Item do Sprint 2 do doc 05 (`#### Sprint 2 — Contêineres sociais`): criar, entrar e ter
hierarquia funcionando. Não reentrevista o que o doc 05 §2.3/§2.4, o data-model adotado
(§2.2, §2.3, §2.4, §2.11, §3.2, §3.3), os FR-010..FR-019 e o contrato HTTP §4/§7/§9 já decidem.

Decisões humanas desta abertura (entrevista 2026-11-08 10:57:14, Henrique — 3/3 defaults
aceitos): (1) escopo = Sprint 2 inteiro + DoD + rotas do contrato §4/§7 relevantes + FR-016 +
FR-019/SC-011, ~6 entregas; (2) agregado `Container` = **estado simples com ETag** + trilha de
auditoria append-only separada (não event-sourced); (3) operador da instância =
**`Instance:Operators` em config**, sem rota de gestão de operadores.

## Behaviour

### A. Agregado, presets e criação (FR-010, data-model §2.2)

- `POST /api/containers` (sessão + CSRF) cria contêiner com `kind: group|community`,
  aplicando os presets do data-model §2.2: **grupo** = `Visibility: MembersOnly`,
  `Join: Invite`, `Posting: Members`, `Listed: false`; **comunidade** = `Visibility: Public`,
  `Join: Open`, `Posting: Members`, `Listed: true`; `Interaction` nasce todo em `Members`
  nos dois (data-model prevalece sobre a tabela §2.3 do doc 05 — mesma razão do GAP-12).
- `Slug` único **entre contêineres do dono**, imutável; `Id` = `{scheme}://{host}/c/{slug}`
  com **origem = scheme + Host da requisição** — a mesma regra da descoberta na P-02
  (`DiscoveryEndpoints`; em produção atrás de TLS o scheme é `https`). **Emenda humana
  2026-11-08 15:57:34** (gate do achado MINOR QA-0016/S-28): a leitura `https://` fixo do
  AC-01 da TASK-0017 (testada e verdes na QA-0016) é substituída por esta regra; a correção
  e o teste entram na TASK-0019 como AC-07.
- O criador nasce `Owner` (`Membership.Role=Owner`, `State=Active`) — invariante
  data-model §2.2: nenhum contêiner nasce ou fica sem Owner ativo.
- Estado persistido em store própria com concorrência otimista: escrita só com
  `WHERE Version = @Version`; conflito → `409 version_conflict` (mesma regra provada na
  TASK-0003, aplicada à store de contêiner; a store é SQLite, implementada em `Squid.Service`
  — ADR-35: `Squid.Api` não referencia `Squid.Persistence`).
- `GET /api/containers/{slug}` devolve contêiner + política vigente **quando visível**;
  invisível ou inexistente → `404 not_found` com corpo de erro idêntico aos dois casos
  (FR-018, SC-005 — a existência protegida nunca vira metadado).
- `PATCH /api/containers/{slug}` altera apenas `name`, `summary`, `listed` e incrementa
  `Version` (contrato §4).

### B. Política versionada no fio (FR-013, data-model §2.3, contrato §4)

- `PUT /api/containers/{slug}/policy` exige `If-Match: <version>`: versão defasada →
  `409 version_conflict` com a versão atual, **nada alterado**; sem `If-Match` →
  `428 precondition_required` (GAP-16). Sucesso substitui a política, incrementa `Version`
  (nunca reutiliza número) e grava auditoria com autor, antes, depois e versão nova.
- Quem pode: `Admin`+ (avaliado pelo `IPolicyEvaluator`, nunca por `if` de papel —
  regra dura data-model §3.2); sem sessão → `401`.
- Política `SinglePublisher` sem `PublisherIds` válidos e ativos continua rejeitada na
  construção (regra já imposta pelo Domain na P-01).

### C. Trilha de auditoria (FR-019, SC-011, data-model §2.11)

- Toda decisão de política executada nas rotas desta fase é registrada em store
  **append-only** `PolicyDecision` (`At`, `ActorId`, `Action`, `ResourceId`, `Result`
  `Allow|Deny|NotFound`, `PolicyVersion`, `Reason`); `Deny` e `NotFound` também gravam.
- `GET /api/admin/audit?actor=&action=&from=` consulta a trilha com paginação e exige
  operador (escopo desta fase: as demais rotas `/api/admin/*` do §7 entram nas fases donas).
- Mudança de política e mudança de papel geram registro de auditoria com autor, quando e
  versão da política vigente (FR-013).

### D. Operador mínimo (FR-008 parcial, decisão humana 2026-11-08)

- Operador = handles/actor ids em `Instance:Operators` (configuração); sem rota de
  gestão de operadores (fase de admin).
- Toda rota `/api/admin/*` desta fase exige operador autenticado: não operador →
  `403 policy_denied`; sem sessão → `401`.

### E. Membership: entrada, convite e listagem (FR-014, data-model §2.4/§3.3)

- Store própria com chave composta `(ContainerId, ActorId)`; máquina de estados §3.3:
  `Invited|Requested|Active|Silenced|Banned|Left`.
- `POST /api/containers/{slug}/join`: `Open` → `Active` (papel `Member`); `Request` →
  `Requested`; `Invite` → exige convite válido para **este** ator (SC-006); `Closed` → nega
  sempre. Sem convite: `403 policy_denied` quando a existência do contêiner já é pública ao
  ator, `404 not_found` quando não é (FR-018).
- `POST /api/containers/{slug}/invites` (`Admin`+) cria `Invited` com `InvitedBy`
  (convite por ator alvo — GAP-13); o convidado entra via `join`.
- `POST /api/containers/{slug}/leave`: `Active` → `Left`; saída do **último** Owner →
  `409 last_owner` (contrato §9.11) — transferir antes.
- `GET /api/containers/{slug}/members`: página de membros `Active` para quem enxerga o
  contêiner; estados pendentes (`Invited`/`Requested`) só a `Admin`+; `Banned` nunca aparece.
- Reentrada após `Left` segue a política vigente (`Closed` nega, `Invite` exige novo convite).

### F. Papéis, moderação e contexto do avaliador (FR-012/FR-013, GAP-11 fecha aqui)

- `PATCH /api/containers/{slug}/members/{actorId}`: muda papel ou estado
  (`approve`, `reject`, `silence`, `unsilence`, `ban`, `unban`) com auditoria de autor,
  quando e versão da política (FR-013). `Banned` não transiciona para `Active` sem ação de
  `Admin` **com justificativa**; `Owner` não pode ser banido — transferência primeiro.
- `IPolicyEvaluator` passa a aceitar o **contexto do ator** (fecha GAP-11 da SPEC-0003):
  papel + estado de membresia + pertencimento a `PublisherIds` (membro ativo).
  `SinglePublisher`: `Member` não-publisher **negado**, publisher nomeado ativo **permitido**
  (facet DoD do Sprint 2); `Silenced` lê e não escreve; `Banned` não lê; `Guest` continua
  negado em qualquer escrita, independente da política.
- O comando passa a valer como fronteira única da decisão (doc 05 §2.4: a borda HTTP só
  rejeita cedo; a autoridade é a avaliação dentro do comando, agora com o ator completo).

### G. Sucessão, transferência, exclusão e override (FR-017, DoD do Sprint 2)

- `PUT /api/containers/{slug}/successor` (`Owner`) nomeia o sucessor (data-model §2.2).
- `POST /api/containers/{slug}/claim`: o sucessor assume **só** após
  `Containers:SuccessorInactivityDays` (default 90, configurável) de inatividade do Owner;
  antes do prazo → `403` (contrato §9.12); sucesso troca o Owner ativo, move o antigo para
  `Admin` e grava auditoria. Relógio injetável (`TimeProvider`) — o teste não dorme 90 dias.
- `POST /api/containers/{slug}/transfer` (`Owner`) transfere a propriedade: novo Owner
  `Active`, antigo vira `Admin`, auditado; não-Owner → negado.
- `POST /api/admin/containers/{slug}/transfer`: operador força a transferência com
  **justificativa** (auditado); sem justificativa → `422` (contrato §9.12).
- `DELETE /api/containers/{slug}` (`Owner`) exige confirmação (GAP-18) e faz **soft delete**
  com carência `Containers:DeleteGraceDays` (default 30): `DeletedAt` preenchido, `GET` → 404,
  sai da busca; sem confirmação → `400`.
- Invariante provada por teste (DoD): **contêiner nunca fica órfão** — com Owner inativo,
  o claim do sucessor ou o override do operador sempre restaura um Owner ativo.

### H. Descoberta (FR-016, R-10)

- `GET /api/containers/search?q=` devolve **somente** contêineres `Listed=true` e não
  excluídos; `Listed=false` nunca aparece — nem com o nome exato, nem para autenticado.
- Matching: substring case-insensitive em `name` e `slug` (sem FTS/ranking — I-07 fica no
  backlog); `q` ausente ou vazio → `400 validation_failed`.
- A rota é pública (sem sessão) e respeita a regra do 404: só contêineres listados são
  revelados.

## Inputs and outputs

| Direction | Name | Shape | Notes |
| --- | --- | --- | --- |
| in | `kind` | `group \| community` | no `POST /api/containers`; presets aplicados |
| in | `If-Match` | inteiro textual (versão) | obrigatório no `PUT /policy`; 428 ausente, 409 defasado |
| in | `q` | string | no `search`; vazio → 400 |
| in | `confirm` | string | no `DELETE`; deve ser o slug exato (GAP-18) |
| in | `justification` | string (não vazia) | no override do operador; vazia → 422 |
| in | `actorId` + mudança | `role` ou `state` + motivo quando exigido | no `PATCH /members/{actorId}` |
| out | `ContainerView` | `id`, `slug`, `kind`, `name`, `summary`, `listed`, `policy` (com `version`), `memberCount`, `createdAt` | `GET`, `search`, `POST` |
| out | `PolicyDecision` | permitido/negado + motivo + `PolicyVersion` | avaliador; gravado na trilha |
| out | erro | catálogo contrato §8 | nunca stack, tabela ou caminho interno (§9.7) |

## Rules and edge cases

- 404 sem metadado para qualquer recurso restrito (SC-005, §9.1): contêiner invisível,
  `Banned`, membro de `Private` — resposta idêntica ao caso inexistente.
- `PUT /policy` com versão antiga não altera nada (§9.2) — teste com hash antes/depois.
- Duas edições concorrentes do mesmo campo: a segunda falha por versão, não sobrescreve.
- `SinglePublisher` com `PublisherIds` vazio ou com membro inativo: política inválida na
  construção; numeração de versão nunca é reutilizada.
- `Owner` único por contêiner: saída, remoção ou remoção de Owner sem substituto →
  `409 last_owner`; nenhuma sequência deixa o contêiner sem Owner ativo.
- `Banned` → `Active` exige `Admin` + justificativa vazia → `422`; `Owner` banido → negado.
- `Silenced`: leitura ok, escrita negada; `Banned`: leitura vira 404.
- Convite sem convite válido em `Join: Invite` → 0 sucessos em 100 tentativas (SC-006).
- Mutações exigem sessão + CSRF (padrão B-05 da P-02); `401 not_authenticated` sem sessão.
- `search` com `Listed=false`: mesmo `q` com o nome exato não devolve o contêiner.
- Vazamento por metadado: `memberCount` só aparece para quem já pode ver o contêiner.

## Non-functional

- performance: `search` e `GET /members` são queries paginadas sobre índice
  (`Listed`, `Slug`); nenhuma leitura percorre feed ou projeção Dapper (§4 é P-04+).
- security: SC-005 (0 vazamento em 100 tentativas), SC-006 (0 sucesso em 100 entradas por
  convite inválido), SC-011 (100% das decisões auditáveis: quem autorizou, sob qual versão,
  quando), SC-015 (nenhuma PII em telemetria — a auditoria guarda `ActorId`/URI porque é a
  própria trilha, não log de telemetria); denegação é o default em todo caminho não mapeado.
- platform: `Squid.Domain` continua puro (sem Orleans/JSON-LD/I/O, regra SQUID0001);
  rotas minimal API em `Squid.Api/**`; stores SQLite em `Squid.Service/**`; testes em
  `Squid.Tests/**` via `sdd-test.ps1` (gate da casa).

## Out of scope (explicitly)

- Publicação e feed (P-04): a política de `Posting` é avaliada pelo Domain já entregue e
  pelos contextos desta fase, mas **nenhuma rota de post** entra aqui.
- FTS, ranking ou busca em conteúdo (I-07); front (P-08); federação de contêineres (P-06).
- Exclusão de conta (FR-009) e o `409 container_transfer_required` do §9.11 — a rota não
  existe nesta fase; quando FR-009 entrar, esse409 é obrigatório (registrado aqui, não é
  defeito desta fase).
- Gestão de operadores (CRUD/rota) e as demais rotas `/api/admin/*` do §7 (reports, suspend,
  peers, backup) — fases donas.
- Projeções Dapper de membros/contêineres (data-model §4) — Worker/P-04+.
- Convite com código compartilhável ou link público (não existe no contrato); E2EE; bots.
- Qualquer arquivo fora dos `touches` das tasks listadas.

## Gaps carried from the requirement

| Gap | Status | Resolution or assumption in force |
| --- | --- | --- |
| GAP-11 (SPEC-0003) | RESOLVED | `Evaluate` passa a aceitar o contexto do ator: papel + estado de membresia + pertencimento a `PublisherIds`; fecha na TASK-0020 |
| GAP-13 | ASSUMED | convite = estado `Invited` por ator alvo com `InvitedBy`; sem código compartilhável e **sem expiração** no MVP (o contrato não tem rota de redeem) |
| GAP-14 | ASSUMED | operador = `Instance:Operators` na config (decisão humana 2026-11-08 10:57:14); gestão de operadores fora |
| GAP-15 | ASSUMED | busca FR-016 = substring case-insensitive em `name`/`slug` de listados; FTS/ranking é I-07 pós-MVP |
| GAP-16 | ASSUMED | `PUT /policy` sem `If-Match` → `428 precondition_required`; slug duplicado do dono → `400 validation_failed` (catálogo §8 não tem código próprio para os dois) |
| GAP-17 | ASSUMED | relógio injetável (`TimeProvider`) para os 90 dias de inatividade e os 30 dias de carência; defaults `Containers:SuccessorInactivityDays=90`, `Containers:DeleteGraceDays=30` |
| GAP-18 | ASSUMED | confirmação do `DELETE` = query `?confirm=<slug>` com o slug exato (contrato só diz "confirmação obrigatória") |
| GAP-02..10, 12 | — | como estavam (REQ-0001 e specs anteriores) |

## Tasks

| Task | Title | Parallel-safe | Status |
| --- | --- | --- | --- |
| TASK-0017 | Núcleo do contêiner: agregado, presets, criar/ler/editar com ETag | no — abre `Squid.Api/Containers/**` e `Squid.Service/**`, base das demais | TASK_READY |
| TASK-0018 | Política versionada (`If-Match`) + trilha de auditoria + operador | no — serial após TASK-0017 (mesmos arquivos) | TASK_READY |
| TASK-0019 | Membership: join/invite/leave e listagem de membros | no — serial após TASK-0018 | TASK_READY |
| TASK-0020 | Papéis, moderação e contexto do avaliador (GAP-11, facet singlePublisher) | no — serial após TASK-0019 | TASK_READY |
| TASK-0021 | Sucessão, transferência, exclusão e override do operador (DoD) | no — serial após TASK-0020 | TASK_READY |
| TASK-0022 | Descoberta FR-016 (`search`) + facet de fecho do DoD do Sprint 2 | no — serial após TASK-0021 | TASK_READY |
