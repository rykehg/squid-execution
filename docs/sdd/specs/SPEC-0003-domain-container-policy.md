<!--
templateVersion: 1.0.0
-->
Specification: the resolved reading of a requirement, precise enough to cut into tasks and to test against.
Write responsibility: ODEV.

- status: ATIVO
- owner: odev
- lastVerified: 2026-11-06 17:12:45

# SPEC-0003 — Domain: ContainerPolicy, papéis e motor de política

- requirement: REQ-0001
- phase: P-01
- dependsOn: SPEC-0001

Item do Sprint 0 do doc 05: "`Domain` com `ContainerPolicy` e `IPolicyEvaluator` + testes da
matriz". É o coração do controle de acesso (doc 05 §2.4) e a base da P-03; entra agora porque o
Sprint 0 o lista e porque nada fora de `Squid.Domain` é tocado — o projeto já existe no solution
(TASK-0002) mas não tem código de produto. O que já está decidido no doc 05, no data-model
adotado (§2.3, §2.4, §3.2, §3.3) e nos FR-010..FR-019 não é reentrevistado.

## Behaviour

- `ContainerPolicy` é um registro imutável versionado com as quatro políticas independentes
  `Posting`, `Visibility`, `Join`, `Interaction` (comentário, reação, compartilhamento, anexo),
  mais `PublisherIds` e `Version` (doc 05 §2.4; data-model §2.3).
- `SinglePublisher` é caso de primeira classe: exige `PublisherIds` não vazia com membros ativos
  e permite delegar publicação sem promover a admin (FR-015; doc 05 §2.4).
- Hierarquia de papéis estrita e herdada: `Owner ⊃ Admin ⊃ Moderator ⊃ Member ⊃ Guest` — cada
  nível pode tudo do nível abaixo mais suas capacidades (FR-012; data-model §3.2).
- `Guest` nunca publica, reage, comenta ou compartilha, independentemente da política — a
  política não concede o que o papel não tem (data-model §2.4).
- `IPolicyEvaluator.Evaluate(action, actor, policy)` é o único caminho de checagem: devolve
  `PolicyDecision` (permitido/negado + motivo + versão da política avaliada); nenhuma comparação
  de papel espalhada em `if` no restante do código (data-model §3.2, regra dura).
- Presets: grupo (amigos) e comunidade (interesses) nascem com os defaults do doc 05 §2.3 —
  grupo: `membersOnly`/`invite`/não listado; comunidade: `public`/`open|request`/listada —
  postagem padrão `membersOnly` em ambos (FR-010).
- Matriz de capacidades (papel × ação) é coberta por testes: para cada papel e cada ação
  (publicar, comentar, reagir, compartilhar, anexar, entrar, moderar) o resultado esperado é
  asserts — doc 05 §12 (Unit — política | xUnit | matriz de capacidades).

## Inputs and outputs

| Direction | Name | Shape | Notes |
| --- | --- | --- | --- |
| in | `Action` | enum (post, comment, react, share, attach, join, moderate, admin) | ação avaliada |
| in | `Actor` | papel + estado de membresia (para a matriz: papel basta) | quem pede |
| in | `ContainerPolicy` | registro versionado | política vigente |
| out | `PolicyDecision` | permitido/negado + motivo + `PolicyVersion` | resultado auditável |

## Rules and edge cases

- `SinglePublisher` sem `PublisherIds` (ou lista vazia) é política inválida — construtor/factory
  rejeita, não avalia em falso (FR-015).
- `Private` não federa (R-11) — regra de saída federada fica registrada como comportamento
  esperado, mas o ponto de aplicação federado é fora desta spec (P-06).
- Papel de `Guest` vence qualquer permissão de política — nega sempre que a ação for de escrita.
- Política desconhecida/valor de enum fora do domínio: argumento inválido, não default silencioso.
- `Version` nunca é reutilizada; duas edições concorrentes falham por versão (data-model §2.3) —
  a edição em si é da P-03, aqui só o tipo e a regra de incremento existem.
- Toda decisão carrega a `PolicyVersion` avaliada — prepara a trilha de auditoria FR-019 (P-03).

## Non-functional

- performance: avaliação é pura em memória, sem I/O — O(1) por decisão.
- security: negação é o default — qualquer caminho não mapeado nega; `evaluate` nunca lança para
  decisão esperada (só para entrada inválida).
- platform: `Squid.Domain` mira `net10.0` e **não referencia Orleans, JSON-LD nem I/O** (regra
  de dependência já provada pelos testes de arquitetura da TASK-0004).

## Out of scope (explicitly)

- `ContainerGrain`, `Membership` persistida, presets aplicados a um contêiner real — P-03.
- Trilha de auditoria `PolicyDecision` (FR-019) como persistência — P-03; aqui só o registro
  retornado por `Evaluate`.
- Alteração de política com concorrência e evento de auditoria — P-03.
- Descoberta/listagem de comunidades (FR-016), transferência de propriedade (FR-017) — P-03.
- Qualquer arquivo fora de `backend/src/Squid.Domain/**` e `backend/tests/Squid.Tests/**`.

## Gaps carried from the requirement

| Gap | Status | Resolution or assumption in force |
| --- | --- | --- |
| GAP-11 | ASSUMED | forma de `Actor` na assinatura: para a matriz basta o papel (`ContainerRole`) — o `Actor` completo da spec entra na P-03 com o grain; `Evaluate` aceita o papel, não o agregado |
| GAP-12 | ASSUMED | valores de enum seguem os nomes do data-model §2.3 (`Everyone..SinglePublisher`, `Public..Private`, `Open..Closed`) — o doc 05 usa minúsculas na tabela §2.4; prevalece o data-model por ser o contrato de dados adotado |
| GAP-01..10 | — | resolvidos ou assumidos nas specs/tarefas anteriores |

## Tasks

| Task | Title | Parallel-safe | Status |
| --- | --- | --- | --- |
| TASK-0007 | `ContainerPolicy`, `IPolicyEvaluator` e testes da matriz no Domain | yes — toca só `Squid.Domain/**` e `Squid.Tests/**` (disjunto das demais P-01) | TASK_READY |
