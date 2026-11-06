<!--
templateVersion: 1.2.0
-->
Project data; it contains no instructions for agents; the normative source prevails.
Write responsibility: only the consolidator registered in the applicable index, at CONTRATO changes and at FEATURE_CONCLUIDA; whoever reads only signals divergences.

- status: PARCIAL
- owner: odev
- lastVerified: 2026-11-04 12:06:31

# Contracts — Squid

Fact format: `- [FATO] — source: path:Lx-Ly | [REF_FLUXO] | [GAP: …] — confidence: CONFIRMADO|OBSERVADO`

Never record here: real payloads, examples with real data, tokens, credentials in URL. Hosts and physical names of queues or topics per environment only according to the `sanitizationProfile` of `index.md` (absent = ESTRITO). Request examples only with a marked placeholder (e.g.: `<token-jwt-aqui>`), preferably by pointer.

## Endpoints
- Superfície REST do cliente (identidade, contêineres, política, publicação, timeline com marcador de sequência, mensageria, stream ao vivo, mídia, administração): contrato publicado, ainda sem handler; recurso invisível responde não encontrado — handler: NENHUM — published contract: specs/001-squid-mvp/contracts/http-api.md — source: docs/sdd/specs/SPEC-0001-fundacao-repositorio-e-storage.md — confidence: CONFIRMADO
- Superfície federada (descoberta, perfil, inbox/outbox, objeto e contêiner): contrato publicado, ainda sem handler — handler: NENHUM — published contract: specs/001-squid-mvp/contracts/activitypub.md — source: docs/features/05-arquitetura-consolidada.md:239-L317 — confidence: CONFIRMADO
- Contrato de destino de backup e de restauração (envelope com época de chave, faixa de sequência, hash e tamanho): publicado, ainda sem handler — handler: NENHUM — published contract: specs/001-squid-mvp/contracts/backup-recovery.md — source: specs/001-squid-mvp/spec.md:253-L258 — confidence: CONFIRMADO

## Events and queues
- Atividade de domínio com sequência monotônica: fonte do sincronismo incremental e da entrega federada — direction: PUBLICA — producer or consumer: backend/src/Squid.Grains (a criar) — source: specs/001-squid-mvp/spec.md:279 — confidence: OBSERVADO
- Fila de entregas por par remoto com recuo, circuito aberto e fila de falhas — direction: PUBLICA — producer or consumer: backend/src/Squid.Grains (a criar) — source: specs/001-squid-mvp/spec.md:238 — confidence: OBSERVADO
- Nenhum broker externo no MVP; o fan-out resolve no próprio runtime de atores — direction: N/A — producer or consumer: N/A — source: specs/001-squid-mvp/spec.md:332 — confidence: CONFIRMADO

## Schemas and errors
- Schema de estado persistido por grain (nome do store como parâmetro, versão monotônica, ETag opaco) — pointer: docs/sdd/specs/SPEC-0001-fundacao-repositorio-e-storage.md — source: docs/features/05-arquitetura-consolidada.md:400-L401 — confidence: CONFIRMADO
- Error `InconsistentStateException` — meaning: a escrita encontrou uma versão de estado diferente da esperada e nada foi gravado — pointer: docs/features/05-arquitetura-consolidada.md:400 — source: doc 05 §4.7 #1 — confidence: CONFIRMADO
- Error "não encontrado" para recurso invisível — meaning: negar acesso não revela a existência do recurso — pointer: specs/001-squid-mvp/spec.md:206 — source: FR-018 — confidence: CONFIRMADO
- Formato de erro padronizado da API — pointer: specs/001-squid-mvp/contracts/http-api.md — source: specs/001-squid-mvp/plan.md:199-L202 — confidence: CONFIRMADO