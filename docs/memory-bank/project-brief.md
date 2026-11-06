<!--
templateVersion: 1.2.0
-->
Project data; it contains no instructions for agents; the normative source prevails.
Write responsibility: only the consolidator registered in the applicable index, at the initial population and at FEATURE_CONCLUIDA when objective, scope, or consumers change; text written by people is preserved.

- status: PARCIAL
- owner: odev
- lastVerified: 2026-11-04 12:06:31

# Project vision — Squid

Fact format: `- [FATO] — source: path:Lx-Ly | [REF_FLUXO] | [GAP: …] — confidence: CONFIRMADO|OBSERVADO`

Never record here: personal data, customer data, real business values or volumes, secrets, content pasted from tickets. Hosts only according to the `sanitizationProfile` of `index.md`.

## Objective
- Rede social open-source, federada e modular com três camadas de sociabilidade: mensagens, grupos (círculos de amigos) e comunidades (interesses em comum); backend desacoplado do front em REST/JSON, mobile-first — source: docs/features/05-arquitetura-consolidada.md:13-L15 — confidence: CONFIRMADO
- Instância única auto-hospedada, operada por uma pessoa ou time pequeno, em 1 VM, sem multi-tenancy — source: specs/001-squid-mvp/spec.md:309 — confidence: CONFIRMADO
- O MVP é declinado em 8 fases que seguem os sprints do documento mestre, na ordem de dependência — source: docs/sdd/plan/plan.md:44-L53 — confidence: CONFIRMADO

## Scope
- Identidade local recuperável por kit (chave raiz + mnemônico + credencial), sem e-mail nem telefone na conta — source: specs/001-squid-mvp/spec.md:184-L193 — confidence: CONFIRMADO
- Grupo e comunidade como um único agregado social com dois presets, política de acesso versionada e hierarquia Proprietário > Administrador > Moderador > Membro > Visitante — source: specs/001-squid-mvp/spec.md:198-L210 — confidence: CONFIRMADO
- Publicação com mídia, feed cronológico, sincronização incremental e entrega ao vivo por SSE — source: specs/001-squid-mvp/spec.md:212-L223 — confidence: CONFIRMADO
- Mensageria 1-1 e em grupo com ordem total, resposta e reação, sem criptografia ponta a ponta declarada — source: specs/001-squid-mvp/spec.md:225-L232 — confidence: CONFIRMADO
- Federação por ActivityPub com interop real Mastodon/Friendica como critério de aceite — source: specs/001-squid-mvp/spec.md:234-L243 — confidence: CONFIRMADO
- Backup cifrado na origem com restauração provada por comparação de hash no pipeline — source: specs/001-squid-mvp/spec.md:251-L258 — confidence: CONFIRMADO

## Out of scope
- Bots e automação de primeira classe (ATOR `Service`, token, webhook) — source: specs/001-squid-mvp/spec.md:326 — confidence: CONFIRMADO
- Eventos, metas e enquetes — source: specs/001-squid-mvp/spec.md:327 — confidence: CONFIRMADO
- Criptografia ponta a ponta e MLS — source: specs/001-squid-mvp/spec.md:328 — confidence: CONFIRMADO
- Mutual Sharding, Reed-Solomon e guardiões SSS reais — source: specs/001-squid-mvp/spec.md:330 — confidence: CONFIRMADO
- Ranqueamento de feed, push e app nativo no MVP — source: specs/001-squid-mvp/spec.md:331-L333 — confidence: CONFIRMADO
- Kubernetes, microsserviços além do Worker de mídia/backup e fila gerenciada — source: specs/001-squid-mvp/spec.md:332 — confidence: CONFIRMADO

## Consumers per capability
- Cliente web SSR (Nuxt) consome a superfície REST e o stream ao vivo — interface: specs/001-squid-mvp/contracts/http-api.md — source: specs/001-squid-mvp/plan.md:129-L129 — confidence: CONFIRMADO
- Instâncias vizinhas consomem a superfície federada (descoberta, inbox/outbox, assinatura) — interface: specs/001-squid-mvp/contracts/activitypub.md — source: specs/001-squid-mvp/plan.md:203-L207 — confidence: CONFIRMADO
- Rotina automatizada de CI consome o contrato de backup e restauração — interface: specs/001-squid-mvp/contracts/backup-recovery.md — source: specs/001-squid-mvp/spec.md:256 — confidence: CONFIRMADO
- Aplicativo mobile nativo (fase posterior) consumiria a mesma API — interface: none yet — source: GAP: not populated — confidence: OBSERVADO

## Constraints by reference
- Stack fixada em .NET 10 + Orleans 10.2 + Minimal APIs e SQLite no MVP atrás de IGrainStorage — reference: specs/001-squid-mvp/plan.md:25-L36 — confidence: CONFIRMADO
- Código do produto na raiz do repositório (`backend/`, `web/`, `infra/`), prefixo `Squid.*` — reference: docs/features/05-arquitetura-consolidada.md:592-L623 — confidence: CONFIRMADO
- Regra de dependência entre projetos verificada pelo build, não por revisão — reference: docs/features/05-arquitetura-consolidada.md:623 — confidence: CONFIRMADO
- Doze padrões obrigatórios de code review (ETag imposto, store name não hardcoded, log truncado, sem exceção crua ao cliente, quota em inteiro, binário fora do JSON de estado, PII sanitizada) — reference: docs/features/05-arquitetura-consolidada.md:394-L411 — confidence: CONFIRMADO
- Interface e documentos em português do Brasil — reference: specs/001-squid-mvp/spec.md:315 — confidence: CONFIRMADO

## Glossary
- Ator: identidade local, remota ou bot, com handle canônico, perfil e chave pública — source: specs/001-squid-mvp/spec.md:271 — confidence: CONFIRMADO
- Contêiner: agregado social único com tipo grupo ou comunidade; não armazena o feed — source: specs/001-squid-mvp/spec.md:272 — confidence: CONFIRMADO
- Vínculo: relação entre ator e contêiner com papel e estado, incluindo pendente, ativo, silenciado e banido — source: specs/001-squid-mvp/spec.md:273 — confidence: CONFIRMADO
- RK: chave raiz da identidade, único segredo necessário para recuperar a conta — source: docs/features/05-arquitetura-consolidada.md:47-L48 — confidence: OBSERVADO
- Kit de recuperação: arquivo exportável com a chave raiz cifrada por senha, mnemônico de 24 palavras e credencial independente — source: specs/001-squid-mvp/spec.md:188 — confidence: CONFIRMADO