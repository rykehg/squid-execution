<!--
templateVersion: 1.0.0
-->
Requirement as the human stated it, in their words, plus the gaps we still have to close. It is the source of truth for *intent* until the phase closes.
Write responsibility: ODEV.

- status: ATIVO
- owner: odev
- lastVerified: 2026-11-04 12:06:31

# REQ-0001 — Squid: MVP de rede social federada auto-hospedada

- source: material do pacote de execução (`docs/features/05-arquitetura-consolidada.md` v1.8,
  `specs/001-squid-mvp/spec.md`, `specs/001-squid-mvp/plan.md`), mais a conversa de 2026-11-04
  sobre onde o repositório de execução nasce
- receivedAt: 2026-11-04 11:20:31

## What the human asked for (their words)

> "me ajude a dar o inicio no repositório da pasta squid-execution/ com git e iniciar de onde está
> parado a frente. Outros documentos de referencia podem ser achados em squid-specs/ até exemplos
> de implementação em squid-specs/ideias-references/ com dois projetos de codigo aberto frendica e
> mastodon e outros dois order-orleans e federation sendo projetos de teste/estudo meus"

> "porém evite referenciar diretamente documentação do repositório de especificação (se necessário
> copie em arquivos md) e não copie nada diretamente dos repositórios masdodon ou friendica pois são
> códigos já publicados, so referencie ideias e nomes"

E, do material adotado, a intenção do produto (`docs/features/05-arquitetura-consolidada.md` §1.1):

> "Uma rede social open-source, federada e modular, com três camadas de sociabilidade: mensagens,
> grupos (círculos de amigos) e comunidades (interesses em comum). Backend desacoplado do front
> (REST/JSON), mobile-first, e compartilhamento de conteúdo facilitado tanto dentro da rede quanto
> para fora dela."

## What we understood

- O objetivo desta sessão é **abrir o repositório de execução** e retomar o trabalho exatamente do
  ponto em que a especificação parou: Phase 1 do Spec Kit fechada, sem `tasks.md`, sem código.
- O produto é o **Squid**: instância única auto-hospedada, operada por uma pessoa ou time pequeno,
  em que grupos e comunidades compartilham um mesmo agregado social com dois presets, e a política
  de acesso é dado versionado, não código.
- A identidade é recuperável: a conta não tem e-mail nem telefone; a recuperação da mesma chave se
  faz pelo kit baixado, não por e-mail de reset (ADR-36).
- A federação é ActivityPub atrás de um adapter de 5 métodos, com interop real Mastodon/Friendica
  como **critério de aceite** do Sprint 5, não verificação posterior (ADR-28).
- Fora do MVP, por decisão registrada: bots, eventos/metas/enquetes, E2EE/MLS, Mutual Sharding,
  app nativo, AT Protocol, ranquemento de feed, Kubernetes, microserviço além do Worker.

## Constraints already known

- Stack fixada: C# 14 / .NET 10 (LTS), Orleans 10.2, Minimal APIs, SQLite no MVP atrás de
  `IGrainStorage`, Oracle ATP como segunda implementação; Nuxt 3 + Vue 3 no front; Podman + systemd
  em Linux ARM64 (10 projetos .NET, 16 entidades, 8 user stories).
- Monorepo com o produto na raiz do repositório: `backend/`, `web/`, `infra/`, `docs/` (ADR-21);
  prefixo `Squid.*` em solution, projetos e namespaces (ADR-22).
- Central package management e regra de dependência **verificada pelo build**, não por revisão
  (ADR-25). `Directory.Build.targets` transforma violação de dependência em erro de compilação.
- 12 padrões obrigatórios do doc 05 §4.7 são critério de aceite em code review: entre eles, ETag
  imposto no storage, nome do store nunca hardcoded, truncamento de log após snapshot, volume
  nomeado, imagem `runtime`, sem `ex.ToString()` ao cliente, sem `catch {}`, quota como inteiro,
  binário fora do JSON de estado, PII sanitizada em log **e** em span tags.
- Interface e documentos em português do Brasil; o modelo de dados é neutro para qualquer idioma.
- `ideias-references/` (Mastodon, Friendica, orders-orleans, federation) é **entrada de validação,
  não fonte** (ADR-29). Decisão do humano em 2026-11-04: nada é copiado deles — nem trecho, nem
  citação —, só ideias e nomes.
- Este repositório não referencia `squid-specs/`; o que for necessário está copiado aqui.

## Open gaps

Never blocks the flow: a gap with a default assumption is enough to keep going. Mark `blocking: yes`
only when a wrong guess destroys data or misroutes the product.

| Gap | Question | Default assumption if the human does not answer | Blocking | Status |
| --- | --- | --- | --- | --- |
| GAP-01 | A `RK` (chave raiz) de onde nasce a identidade: a chave pública Ed25519 é derivada da `RK` (continuidade após recuperação) ou gerada nova e re-atestada por terceiros? | Derivada da `RK` via HKDF, com a prova de continuidade no kit — é o que sustenta "mesma chave pública após restaurar o kit" (FR-006, premissa de continuidade) | no | OPEN |
| GAP-02 | A fonte de pacotes NuGet é a rede pública ou uma fonte local versionada? O doc 05 §11 registra `nuget.config` com fonte local como bloqueio real já ocorrido | Fonte pública `nuget.org` com `nuget.config` versionado no backend; fonte local entra só se o CI exigir rede fechada | no | OPEN |
| GAP-03 | O par RSA-SHA256 por ator é gerado no registro e guardado no estado + kit (R-02), mas o doc 04 §1.2 ainda não tem a linha do par RSA | Par RSA-2048 gerado no registro, embrulhado por `HKDF(RK, "fed/rsa-wrap")`, no estado do ator e no kit, como ADR-28 e R-02 determinam | no | OPEN |
| GAP-04 | Formato do `etag` em `IGrainStorage`: base64 do `GrainStateVersion` do Orleans (como o storage oficial do Azure) ou inteiro textual da sequência de versão | `GrainStateVersion` serializado em base64, com `Version` monotônica por grain e comparação exata em `WHERE Version = @Version` | no | OPEN |
| GAP-05 | A suíte de storage roda contra Oracle ATP só em job agendado; no MVP o gate é SQLite apenas? | SQLite é o gate do CI rápido; Oracle entra em job agendado quando houver credencial — a suíte de contratos é escrita contra a interface, não contra o SQLite | no | OPEN |
| GAP-06 | O front `web/` é criado na P-01 como pasta reservada ou só na P-08? | Pasta reservada por `web/.gitkeep` (a ADR-21 fixa o layout); nenhum código Nuxt antes da P-08 | no | OPEN |

## Answers received

| Gap | Answer | Answering party | Date/Time |
| --- | --- | --- | --- |
| GAP-01 | pendente — resposta no fechamento da P-02, quando a chave existir | NENHUM | NENHUM |
| GAP-02 | pendente — resposta ao cortar TASK-0002 | NENHUM | NENHUM |
| GAP-03 | pendente — resposta ao cortar a entrega de chaves da P-02 | NENHUM | NENHUM |
| GAP-04 | pendente — resposta ao cortar TASK-0003 | NENHUM | NENHUM |
| GAP-05 | pendente — resposta ao cortar TASK-0003 | NENHUM | NENHUM |
| GAP-06 | resposta por suposição em vigor na P-01 (segue aberta até decisão sua) | ODEV | 2026-11-04 12:06:31 |