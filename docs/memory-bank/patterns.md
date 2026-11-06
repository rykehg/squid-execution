<!--
templateVersion: 1.2.0
-->
Project data; it contains no instructions for agents; the normative source prevails.
Write responsibility: only the consolidator registered in the applicable index, at PADRAO changes (2+ confirmed occurrences or an abandoned pattern) and at FEATURE_CONCLUIDA; a pattern with 1 occurrence enters as OBSERVADO.

- status: PARCIAL
- owner: odev
- lastVerified: 2026-11-04 12:06:31

# Patterns — Squid

Fact format (descriptive, never imperative): `- [FATO] — source: path:Lx-Ly | [REF_FLUXO] | [GAP: …] — confidence: CONFIRMADO|OBSERVADO`

Never record here: code snippets or blocks, real test data, secrets.

## Patterns
- Nomenclatura `Squid.*` em solution, projetos e namespaces, com pacote publicável sob outro prefixo — evidence: docs/features/05-arquitetura-consolidada.md:781; docs/features/05-arquitetura-consolidada.md:602-L611 — abandoned at: N/A — source: ADR-22 — confidence: CONFIRMADO
- Versões centralizadas em um único arquivo de pacotes por solution — evidence: docs/features/05-arquitetura-consolidada.md:600; specs/001-squid-mvp/plan.md:111 — abandoned at: N/A — source: ADR-25 — confidence: CONFIRMADO
- Caminho de desenvolvimento primário sem daemon (execução direta mais requisições HTTP); container usado como verificação — evidence: docs/features/05-arquitetura-consolidada.md:784; specs/001-squid-mvp/plan.md:42-L44 — abandoned at: N/A — source: ADR-24 — confidence: CONFIRMADO
- Leitura por projeções separadas do caminho de escrita; o agregado não serve consulta de feed — evidence: specs/001-squid-mvp/plan.md:260; specs/001-squid-mvp/plan.md:128 — abandoned at: N/A — source: doc 05 §4.2 — confidence: CONFIRMADO
- Todo fluxo novo nasce com trace, log sanitizado e métrica — evidence: docs/features/05-arquitetura-consolidada.md:73; specs/001-squid-mvp/spec.md:263 — abandoned at: N/A — source: princípio V / FR-052 — confidence: CONFIRMADO
- Documento mestre vence o código: desvio exige atualização do documento antes da implementação — evidence: docs/features/05-arquitetura-consolidada.md:635; specs/001-squid-mvp/plan.md:7 — abandoned at: N/A — source: doc 05 §12.1 — confidence: CONFIRMADO

## Anti-patterns
- Nome do store interpolado no SQL: faz stores distintos colapsarem na mesma partição lógica — evidence: docs/features/05-arquitetura-consolidada.md:401 — source: doc 05 §4.7 #2 — confidence: CONFIRMADO
- Escrita sem verificação de versão: perde concorrência em silêncio — evidence: docs/features/05-arquitetura-consolidada.md:400 — source: doc 05 §4.7 #1 — confidence: CONFIRMADO
- Devolver `ex.ToString()` ao cliente e `catch {}` silencioso: vaza caminho interno e esconde falha de compensação — evidence: docs/features/05-arquitetura-consolidada.md:405-L406 — source: doc 05 §4.7 #6 e #7 — confidence: CONFIRMADO
- Binário dentro do JSON de estado e quota em ponto flutuante: infla memória e perde precisão — evidence: docs/features/05-arquitetura-consolidada.md:408-L409 — source: doc 05 §4.7 #9 e #10 — confidence: CONFIRMADO
- Adaptador de protocolo com mais de cinco métodos, ou domínio falando JSON-LD: transforma a borda em segundo modelo de domínio — evidence: specs/001-squid-mvp/plan.md:261; specs/001-squid-mvp/plan.md:72 — source: ADR-11 — confidence: CONFIRMADO