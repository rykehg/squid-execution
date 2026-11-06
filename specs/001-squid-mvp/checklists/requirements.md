# Specification Quality Checklist: Squid — MVP de rede social federada

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-11-02
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

- Validação executada em 3 iterações. Itens corrigidos na revisão:
  - Removidos nomes de tecnologia (Orleans, SQLite, Nuxt, ActivityPub como produto, SSE) do corpo da spec — eles vivem em `plan.md`. Exceção deliberada: "protocolo federado aberto" é requisito de produto (interoperabilidade), não escolha de implementação.
  - `[NEEDS CLARIFICATION]` zerado: as 15 lacunas abertas do doc 05 (§14 GAPs) foram decididas com padrão de indústria e registradas em `research.md` (R-01..R-12, R-20) e em `Assumptions`. Nenhuma delas muda o escopo do MVP o suficiente para justificar bloqueio.
  - SC-005/SC-006/SC-013 convertidos em contagens com denominador (100 tentativas, 0% de vazamento) para serem verificáveis sem detalhe de implementação.
  - FR-018 (404 em vez de 403) adicionado após identificar vazamento por diferença de resposta.
- Escopo dos 8 user stories mapeia os Sprints 0–8 do documento mestre; nenhum item de fase 2+ entrou no MVP.
- GAPs do doc 05 §14 permanecem como *entradas de pesquisa* (não como ambiguidades da spec): R-01 (identidade pós-recuperação), R-02 (RSA×Ed25519), R-03 (autenticação), R-07 (vocabulário de contêiner), R-09 (reavaliação na entrega), R-11 (moderação de mídia), R-12 (retenção/exclusão federada), R-13 (sessão SSR).
- Reconciliação de 2026-11-03 (D1=A, D3=A): o critério de federação passou a exigir interop real com Mastodon e Friendica (FR-039, SC-016) e a premissa "duas instâncias locais bastam" foi substituída nas Assumptions. `RSA-SHA256` e `Ed25519` aparecem na spec como exigência de protocolo/interop, não como escolha de biblioteca. O GAP de moderação (R-07) foi fechado por decisão humana e a spec não mudou por isso.
- Opção B de 2026-11-03: US5 ganhou resposta a mensagem e reação (FR-057/FR-058, SC-017), com emoji customizado restrito a whitelist. A spec continua **sem E2EE** — isso fica na feature `004-e2ee-mls` (ADR-32), sem divisão entre DM e grupo.
- Revisão de 2026-11-03: dados em `Squid.Persistence` (ADR-35), propriedade com sucessão/claim/override (ADR-34, FR-059..FR-061) e operador global múltiplo; Admin e Moderator podem ser múltiplos, só Owner é único no MVP.
- Autenticação (ADR-36): MVP sem e-mail/telefone e sem reset por e-mail (recuperação é o kit, FR-062); passkey na fase 2 e OIDC de instância na fase 3 — nunca login social obrigatório.
- Passada de consistência de 2026-11-04: FR-057..062 movidos para as seções corretas, FR-063 (edição de perfil) e FR-008 com múltiplos operadores; §11 do doc 05 alinhada ao ADR-35.

**Status**: ✅ Aprovado — spec pronta para `/speckit-plan` e reconciliada em 2026-11-03 com as decisões D1–D4 (interop real, retenção de moderação, postura sobre referências).