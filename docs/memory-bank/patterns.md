<!--
templateVersion: 1.2.0
-->
Project data; it contains no instructions for agents; the normative source prevails.
Write responsibility: only the consolidator registered in the applicable index, at PADRAO changes (2+ confirmed occurrences or an abandoned pattern) and at FEATURE_CONCLUIDA; a pattern with 1 occurrence enters as OBSERVADO.

- status: ATIVO
- owner: odev
- lastVerified: 2026-11-07 08:30:00

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
- Composition root testável: `SquidServiceHost.Build` monta o host inteiro e é o que tanto o `dotnet run` quanto os testes de integração constroem (o que o teste inicia é o que roda em produção) — evidence: backend/src/Squid.Service/SquidServiceHost.cs:21 — abandoned at: N/A — source: TASK-0005 — confidence: OBSERVADO
- Falha de infraestrutura vira Warning estruturado com o endpoint afetado, nunca exceção propagada ao cliente nem `catch {}` silencioso (wrapper de exporter) — evidence: backend/src/Squid.Service/WarningOtlpExporter.cs:15 — abandoned at: N/A — source: TASK-0006 / doc 05 §4.7 #7 — confidence: OBSERVADO
- Runbook de operação com status explícito (EXECUTÁVEL / PARCIAL / PENDENTE-fase) e marca por comando (EXECUTAVEL vs NÃO-executar): procedimento de sistema inexistente é esboço com ponteiro de fase, nunca passo a passo inventado — evidence: docs/runbooks/README.md — abandoned at: N/A — source: TASK-0010 / SPEC-0006 — confidence: OBSERVADO
- Mutação can-fail como prova de teste: quebrar o código de propósito (guarda, máscara, estágio da imagem), vermelho esperado, restaurar byte-exact com SHA256 capturado antes — evidência em toda QA desta fase — evidence: docs/sdd/qa/QA-0009-runbooks-iniciais.md; docs/sdd/qa/QA-0007-containerfile-compose.md — abandoned at: N/A — source: prática das QAs TASK-0003..0010 — confidence: CONFIRMADO

## Anti-patterns
- Nome do store interpolado no SQL: faz stores distintos colapsarem na mesma partição lógica — evidence: docs/features/05-arquitetura-consolidada.md:401 — source: doc 05 §4.7 #2 — confidence: CONFIRMADO
- Escrita sem verificação de versão: perde concorrência em silêncio — evidence: docs/features/05-arquitetura-consolidada.md:400 — source: doc 05 §4.7 #1 — confidence: CONFIRMADO
- Devolver `ex.ToString()` ao cliente e `catch {}` silencioso: vaza caminho interno e esconde falha de compensação — evidence: docs/features/05-arquitetura-consolidada.md:405-L406 — source: doc 05 §4.7 #6 e #7 — confidence: CONFIRMADO
- Binário dentro do JSON de estado e quota em ponto flutuante: infla memória e perde precisão — evidence: docs/features/05-arquitetura-consolidada.md:408-L409 — source: doc 05 §4.7 #9 e #10 — confidence: CONFIRMADO
- Adaptador de protocolo com mais de cinco métodos, ou domínio falando JSON-LD: transforma a borda em segundo modelo de domínio — evidence: specs/001-squid-mvp/plan.md:261; specs/001-squid-mvp/plan.md:72 — source: ADR-11 — confidence: CONFIRMADO